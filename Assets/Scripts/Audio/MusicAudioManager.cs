using UnityEngine;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class MusicAudioManager : MonoBehaviour
{
    [Header("Audio Source")]
    public AudioSource musicSource;

    [Header("Pitch Compensation")]
    [Tooltip("Keep pitch natural when speeding up/slowing down. When enabled, sets mixerPitchParam to 1/speed.")]
    public bool forceNaturalPitch = true;
    [Tooltip("Name of the exposed mixer parameter controlling pitch. Default for Pitch Shifter is 'Pitch'.")]
    public string mixerPitchParam = "Pitch";
    [Tooltip("If true, mixerPitchParam expects semitone offset (Pitch Shifter). If false, expects linear multiplier (Group Pitch).")]
    public bool mixerPitchParamIsSemitone = true;
    [Tooltip("If false, skip mixer pitch compensation (route to clean group if provided).")]
    public bool useMixer = false;
    [Tooltip("Fallback mixer group to route music through if the source has none assigned.")]
    public AudioMixerGroup defaultMusicMixerGroup;
    [Tooltip("Optional clean mixer group without pitch effects for charts that disable mixer.")]
    public AudioMixerGroup cleanMusicMixerGroup;
    [Tooltip("If true, auto-assign a mixer group (default or by scanning mixers for mixerPitchParam) when missing.")]
    public bool autoAssignMixerGroup = true;

    [Header("Defaults")]
    public float defaultVolume = 1f;

    void Awake()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        if (musicSource != null)
        {
            ApplyRoutingForUseMixer(useMixer);
            musicSource.playOnAwake = false;
            musicSource.loop = false;
            musicSource.spatialBlend = 0f;
            musicSource.volume = defaultVolume;
        }
    }

    /// <summary>
    /// Assign a music source at runtime and ensure mixer routing/pitch compensation are applied immediately.
    /// </summary>
    public void SetMusicSource(AudioSource src)
    {
        if (src == null) return;
        musicSource = src;
        ApplyRoutingForUseMixer(useMixer);
        ApplyPitchCompensation(1f); // initialize mixer param
    }

    /// <summary>
    /// Apply playback speed to the music source and (optionally) compensate pitch via mixer.
    /// </summary>
    public void ApplySpeed(float speed)
    {
        if (musicSource == null) return;
        if (speed <= 0f) speed = 1f;
        musicSource.pitch = speed;
        ApplyPitchCompensation(speed);
    }

    /// <summary>
    /// Set exposed mixer pitch parameter to counter AudioSource pitch when desired.
    /// </summary>
    public void ApplyPitchCompensation(float speed)
    {
        if (musicSource == null) return;
        if (!useMixer) return;
        var mixer = musicSource.outputAudioMixerGroup != null ? musicSource.outputAudioMixerGroup.audioMixer : null;
        if (mixer == null || string.IsNullOrWhiteSpace(mixerPitchParam)) return;

        float targetLinear = (forceNaturalPitch && speed > 0f) ? (1f / speed) : 1f;
        TrySetMixerPitch(mixer, mixerPitchParam, mixerPitchParamIsSemitone, targetLinear);
    }

    private bool TrySetMixerPitch(AudioMixer mixer, string primaryParam, bool isSemitone, float targetLinear)
    {
        if (mixer == null) return false;
        string[] candidates = new[] { primaryParam, "Pitch", "MusicPitch" };
        foreach (var name in candidates)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            try
            {
                if (isSemitone)
                {
                    float semitones = 12f * Mathf.Log(targetLinear, 2f);
                    if (mixer.SetFloat(name, semitones)) return true;
                }
                else
                {
                    if (mixer.SetFloat(name, targetLinear)) return true;
                }
            }
            catch { }
        }
        return false;
    }

    public void SetVolume(float volume)
    {
        if (musicSource == null) return;
        musicSource.volume = Mathf.Clamp01(volume);
    }

    public void PlayImmediate(AudioClip clip)
    {
        if (musicSource == null || clip == null) return;
        musicSource.clip = clip;
        musicSource.time = 0f;
        musicSource.Play();
    }

    public void PlayScheduled(AudioClip clip, double dspStart)
    {
        if (musicSource == null || clip == null) return;
        musicSource.clip = clip;
        musicSource.time = 0f;
        musicSource.PlayScheduled(dspStart);
    }

    public void Stop()
    {
        if (musicSource == null) return;
        musicSource.Stop();
    }

    /// <summary>
    /// Switch the routing between pitch-compensated and clean groups based on useMixer flag.
    /// </summary>
    public void ApplyRoutingForUseMixer(bool enableMixer)
    {
        useMixer = enableMixer;
        if (musicSource == null) return;

        AudioMixerGroup target = enableMixer ? defaultMusicMixerGroup : cleanMusicMixerGroup;

        if (target != null)
        {
            if (musicSource.outputAudioMixerGroup != target)
            {
                musicSource.outputAudioMixerGroup = target;
            }
        }
        else
        {
            // No explicit target: clear when disabling to avoid pitch FX, or auto-assign when enabling.
            if (!enableMixer && musicSource.outputAudioMixerGroup != null)
            {
                musicSource.outputAudioMixerGroup = null;
            }

            if (enableMixer && musicSource.outputAudioMixerGroup == null)
            {
                TryAutoAssignMixerGroup(musicSource);
            }
        }

        // Refresh mixer parameter after routing change so compensation stays correct.
        ApplyPitchCompensation(musicSource.pitch <= 0f ? 1f : musicSource.pitch);
    }

    private void TryAutoAssignMixerGroup(AudioSource source)
    {
        if (!autoAssignMixerGroup || source == null) return;
        if (source.outputAudioMixerGroup != null) return;

        // 1) Use configured default if available
        if (useMixer && defaultMusicMixerGroup != null)
        {
            source.outputAudioMixerGroup = defaultMusicMixerGroup;
            return;
        }

        // Use clean group when mixer is disabled
        if (!useMixer && cleanMusicMixerGroup != null)
        {
            source.outputAudioMixerGroup = cleanMusicMixerGroup;
            return;
        }

        // 2) Find any AudioMixer where the pitch param is exposed (GetFloat succeeds) and use its first group
        try
        {
            var mixers = Resources.FindObjectsOfTypeAll<AudioMixer>();
            foreach (var m in mixers)
            {
                if (m == null) continue;
                float dummy;
                if (m.GetFloat(mixerPitchParam, out dummy))
                {
                    var groups = m.FindMatchingGroups(string.Empty);
                    if (groups != null && groups.Length > 0)
                    {
                        source.outputAudioMixerGroup = groups[0];
                        return;
                    }
                }
            }
        }
        catch { }
    }
}
