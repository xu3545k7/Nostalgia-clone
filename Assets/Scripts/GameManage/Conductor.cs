using UnityEngine;

public class Conductor : MonoBehaviour
{
    public float bpm = 120.0f;
    public float songPosition { get; private set; } // The current position of the song in milliseconds
    // Visual pre-roll clock (active before audio starts)
    public float visualSongPosition { get; private set; } // Negative to 0 ms during pre-roll
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Debugging")]
    public bool showDebugOverlay = false;
    #endif

    private AudioSource audioSource;
    // Optional runtime reference to the project's AudioSync. If assigned, it is the authoritative
    // timing/audio source (uses DSP-based timing). If null, Conductor falls back to using its
    // own AudioSource and AudioSettings.dspTime where appropriate.
    public AudioSync audioSync;
    public bool isPlaying { get; private set; } = false;
    public bool isVisualPlaying { get; private set; } = false;
    // Unified read-only flags/values for gameplay timing
    public bool isActive => isPlaying || isVisualPlaying || hasScheduledStart;
    // Monotonic cached effective position (ms)
    private float _effectiveSongPositionCached = 0f;
    private bool _effectiveInitialized = false;
    public float effectiveSongPosition => _effectiveSongPositionCached;
    private float _lastPreRollDurationMs = 0f;
    public float LastPreRollDurationMs => _lastPreRollDurationMs;
    // Scheduled start support to align audio precisely without stutter
    private bool hasScheduledStart = false;
    private double scheduledDspTime = -1.0;
    // Captured DSP time when audio playback begins (or is scheduled to begin). Used to keep
    // gameplay timing aligned to DSP instead of AudioSource.time, which is affected by pitch.
    private double audioStartDspTime = -1.0;
    // Warn once if audioSource has no valid AudioClip when trying to read time
    private bool _warnedAudioSourceNoClip = false;
    // Playback speed multiplier (kept for compatibility; gameplay timing uses unscaled DSP time)
    public float playbackSpeed = 1f;

    public float RemainingPreRollMs
    {
        get
        {
            // Prefer AudioSync's scheduled time if available
            double scheduled = scheduledDspTime;
            if (audioSync != null)
            {
                double s = audioSync.GetScheduledDspTime();
                if (s > 0.0) scheduled = s;
            }
            if (!hasScheduledStart || scheduled < 0.0) return 0f;
            double now = AudioSettings.dspTime;
            double remain = scheduled - now;
            if (remain <= 0.0) return 0f;
            return (float)(remain * 1000.0);
        }
    }

    /// <summary>
    /// Returns the song position in milliseconds using the most precise DSP-aligned clock available.
    /// Negative values indicate time before the scheduled start.
    /// </summary>
    public float GetDspSongPositionMs()
    {
        try
        {
            // Prefer AudioSync when present – it exposes an audio DSP timeline.
            if (audioSync != null)
            {
                double audioTime = audioSync.GetAudioTime();
                if (audioTime > 0.0 || isPlaying)
                {
                    return (float)(audioTime * 1000.0);
                }

                double scheduled = audioSync.GetScheduledDspTime();
                if (scheduled > 0.0)
                {
                    double now = AudioSettings.dspTime;
                    return (float)((now - scheduled) * 1000.0);
                }
            }

            // Fall back to our AudioSource when AudioSync is not available.
            if (audioSource != null && audioSource.clip != null)
            {
                // Use DSP delta from the captured start time so pitch changes never affect timing.
                double baseline = audioStartDspTime;
                if (baseline > 0.0)
                {
                    double now = AudioSettings.dspTime;
                    return (float)((now - baseline) * 1000.0);
                }

                if (hasScheduledStart && scheduledDspTime > 0.0)
                {
                    double now = AudioSettings.dspTime;
                    return (float)((now - scheduledDspTime) * 1000.0);
                }
            }

            // During visual pre-roll use the visual clock so callers still see negative time.
            if (isVisualPlaying)
            {
                return visualSongPosition;
            }

            // As a final fallback, return the last known songPosition which is kept in milliseconds.
            return songPosition;
        }
        catch
        {
            return songPosition;
        }
    }

    void Awake()
    {
        // Try to bind an AudioSource and an AudioSync if available. Prefer AudioSync.audioSource
        // when present so Conductor and visuals share the same DSP-referenced source.
        audioSource = GetComponent<AudioSource>();
        if (audioSync == null)
        {
            audioSync = GetComponent<AudioSync>();
            if (audioSync == null)
            {
                // Find first AudioSync in the scene as a fallback (keeps backward compatibility)
                audioSync = Object.FindFirstObjectByType<AudioSync>();
            }
        }
        if (audioSync != null && audioSync.audioSource != null)
        {
            audioSource = audioSync.audioSource;
        }
        if (audioSource == null)
        {
            // Add an AudioSource component if it doesn't exist
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        // Initialize music volume from SettingsManager if available
        if (SettingsManager.Instance != null)
        {
            SetMusicVolume(SettingsManager.Instance.MusicVolume);
        }
    }

    void Update()
    {
        if (isPlaying)
        {
            // Update song position: prefer AudioSync's DSP-based time when available
            if (audioSync != null)
            {
                songPosition = (float)(audioSync.GetAudioTime() * 1000.0);
            }
            else if (audioSource != null && audioSource.clip != null)
            {
                // Derive time from DSP start to ignore AudioSource pitch scaling
                if (audioStartDspTime > 0.0)
                {
                    double now = AudioSettings.dspTime;
                    songPosition = (float)((now - audioStartDspTime) * 1000.0);
                }
                else
                {
                    // Only read audioSource.time when a valid AudioClip is assigned.
                    songPosition = audioSource.time * 1000.0f;
                }
            }
            else
            {
                // If there's no clip assigned on the AudioSource, attempting to read time
                // can return 0 or be meaningless. Emit a single editor-only warning to
                // help track misconfigured inspector assignments.
                if (!_warnedAudioSourceNoClip)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    try { Debug.LogWarning("Conductor: audioSource.clip is null or not an AudioClip; audio time unavailable. Check assigned AudioSource or provided AudioClip."); } catch { }
#endif
                    _warnedAudioSourceNoClip = true;
                }
            }
        }
        if (isVisualPlaying)
        {
            // Advance visual clock towards 0 during pre-roll
            visualSongPosition += Time.deltaTime * 1000.0f;
            if (visualSongPosition > 0f)
            {
                visualSongPosition = 0f; // Clamp at 0 until audio takes over
            }
        }

        // When scheduled start time arrives, switch from visual pre-roll to audio time
        if (hasScheduledStart)
        {
            double now = AudioSettings.dspTime;
            if (now >= scheduledDspTime)
            {
                double scheduled = scheduledDspTime;
                double offsetMs = (now - scheduled) * 1000.0;
                try { BuildLogger.Log($"Conductor: Scheduled audio started at dsp={now:F3}, scheduled={scheduled:F3}, offset={offsetMs:+0.0;-0.0;0.0}ms"); }
                catch { }
                isPlaying = true;
                audioStartDspTime = scheduled;
                StopVisualPreRoll();
                hasScheduledStart = false;
                scheduledDspTime = -1.0;
                songPosition = 0f;
                //Debug.Log("Conductor: Scheduled audio started. Switched to audio clock.");
            }
        }

        // Compute and cache a monotonic effective position every frame
        float effNow;
        if (isPlaying)
        {
            effNow = songPosition;
        }
        else if (isVisualPlaying)
        {
            effNow = visualSongPosition;
        }
        else if (hasScheduledStart)
        {
            float remainMs = RemainingPreRollMs;
            effNow = (remainMs > 0f) ? -remainMs : 0f;
        }
        else
        {
            effNow = songPosition;
        }
        // Initialize cache when first entering an active timing state (pre-roll or playing)
        if (!_effectiveInitialized)
        {
            _effectiveSongPositionCached = effNow;
            _effectiveInitialized = true;
        }
        else
        {
            // Avoid backward jumps due to jitter; allow tiny negative epsilon
            if (effNow >= _effectiveSongPositionCached - 0.5f)
            {
                _effectiveSongPositionCached = effNow;
            }
        }
    }

    /// <summary>
    /// Starts playing the song from the beginning.
    /// </summary>
    /// <param name="clip">The audio clip to play.</param>
    public void Play(AudioClip clip)
    {
        if (clip == null)
        {
            //Debug.LogError("AudioClip is null. Cannot play.");
            return;
        }
        // If an AudioSync is available, prefer using it (it manages its own AudioSource and DSP baseline).
        if (audioSync != null && audioSync.audioSource != null)
        {
            var a = audioSync.audioSource;
            a.Stop();
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 0f;
            a.clip = clip;
            a.time = 0f;
            StopVisualPreRoll();
            audioSync.Play();
            audioStartDspTime = AudioSettings.dspTime;
            isPlaying = true;
            songPosition = 0f;
            return;
        }

        // Fallback to using the local AudioSource
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        // Configure a safe 2D audio setup (preserve volume if previously set)
        audioSource.Stop();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f; // 2D
        audioSource.clip = clip;
        audioSource.time = 0f;

        // When actual audio starts, stop visual pre-roll (audio becomes the timing source)
        StopVisualPreRoll();
        audioSource.Play();
        audioStartDspTime = AudioSettings.dspTime;
        isPlaying = true;
        songPosition = 0f;
        //Debug.Log($"Conductor started playback. audioSource.isPlaying={audioSource.isPlaying}");
    }

    /// <summary>
    /// Schedule playback at a future DSP time. Keeps visual pre-roll running until the scheduled time.
    /// </summary>
    public void PlayScheduled(AudioClip clip, double dspStart)
    {
        if (clip == null)
        {
            //Debug.LogError("AudioClip is null. Cannot schedule play.");
            return;
        }

        // Prefer AudioSync for scheduled playback
        if (audioSync != null && audioSync.audioSource != null)
        {
            var a = audioSync.audioSource;
            // Defensive: if the underlying AudioSource is disabled or on an inactive GameObject,
            // attempting PlayScheduled will throw. In that case, fall back to running without audio.
            if (a == null || !a.enabled || !a.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("Conductor: audioSync.audioSource is not available or disabled; scheduling without audio.");
                StartPlayingWithoutAudio();
                return;
            }
            a.Stop();
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 0f;
            a.clip = clip;
            a.time = 0f;
            audioSync.PlayScheduled(dspStart);
            hasScheduledStart = true;
            scheduledDspTime = dspStart;
            audioStartDspTime = dspStart;
            isPlaying = false; // will flip true at dspStart
            songPosition = 0f;
            _effectiveSongPositionCached = -RemainingPreRollMs; // initialize cache for pre-roll
            _effectiveInitialized = true;
            return;
        }

        // Fallback to local AudioSource scheduled playback
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // Defensive: ensure the AudioSource component and its GameObject are active/enabled
        if (audioSource == null || !audioSource.enabled || !audioSource.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"Conductor: local AudioSource is not available or disabled; cannot schedule clip '{clip.name}'. Running without audio.");
            StartPlayingWithoutAudio();
            return;
        }

        audioSource.Stop();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.clip = clip;
        audioSource.time = 0f;

        audioSource.PlayScheduled(dspStart);
        hasScheduledStart = true;
        scheduledDspTime = dspStart;
        audioStartDspTime = dspStart;
        isPlaying = false; // will flip true at dspStart
        songPosition = 0f;
        _effectiveSongPositionCached = -RemainingPreRollMs; // initialize cache for pre-roll
        _effectiveInitialized = true;
        //Debug.Log($"Conductor: Scheduled clip '{clip.name}' at dsp={dspStart:F3}");
    }

    /// <summary>
    /// Stops the playback.
    /// </summary>
    public void Stop()
    {
        if (audioSync != null)
        {
            audioSync.Stop();
        }
        else if (audioSource != null)
        {
            audioSource.Stop();
        }
        isPlaying = false;
        StopVisualPreRoll();
        hasScheduledStart = false;
        scheduledDspTime = -1.0;
        audioStartDspTime = -1.0;
        songPosition = 0f;
        _effectiveSongPositionCached = 0f;
        _effectiveInitialized = false;
        //Debug.Log("Conductor stopped playback.");
    }

    /// <summary>
    /// Seek playback and gameplay timing to the specified millisecond position.
    /// This adjusts the audio playback position (AudioSync or AudioSource) and updates
    /// Conductor's internal cached timing so gameplay subsystems observe the new position.
    /// </summary>
    public void SeekToMs(float targetMs)
    {
        if (targetMs < 0f) targetMs = 0f;
        double targetSec = targetMs / 1000.0;

        // If using AudioSync, set its audioSource.time then call Pause/Resume pattern
        if (audioSync != null && audioSync.audioSource != null)
        {
            try
            {
                var src = audioSync.audioSource;
                bool wasPlaying = src.isPlaying;
                try { audioSync.Pause(); } catch { }
                try { src.time = (float)targetSec; } catch { }
                try { audioSync.Resume(); } catch { }
                // Ensure Conductor songPosition baseline matches AudioSync after resume
                songPosition = (float)(audioSync.GetAudioTime() * 1000.0);
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
                isPlaying = wasPlaying || isPlaying;
            }
            catch { }
            return;
        }

        // Fallback: operate on local AudioSource
        if (audioSource != null)
        {
            try
            {
                bool wasPlaying = audioSource.isPlaying;
                // Pause, set time, then unpause to avoid audible glitch
                if (wasPlaying)
                {
                    audioSource.Pause();
                }
                audioSource.time = (float)targetSec;
                // Update DSP baseline to keep Conductor timing consistent
                audioStartDspTime = AudioSettings.dspTime - targetSec;
                songPosition = targetMs;
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
                isPlaying = wasPlaying || isPlaying;
                if (wasPlaying)
                {
                    audioSource.UnPause();
                }
            }
            catch { }
            return;
        }

        // No audio source available – just update cached positions so visuals advance
        songPosition = targetMs;
        _effectiveSongPositionCached = songPosition;
        _effectiveInitialized = true;
    }

    /// <summary>
    /// Pause playback/time. Stops audio and freezes Conductor timing until Resume is called.
    /// </summary>
    public void Pause()
    {
        try
        {
            if (audioSync != null && audioSync.audioSource != null)
            {
                audioSync.Pause();
            }
            else if (audioSource != null)
            {
                audioSource.Pause();
            }
        }
        catch { }
        isPlaying = false;
    }

    /// <summary>
    /// Resume playback after Pause(). Restores DSP baseline so timing continues from paused audio time.
    /// </summary>
    public void Resume()
    {
        try
        {
            if (audioSync != null && audioSync.audioSource != null)
            {
                audioSync.Resume();
                // Align songPosition to audioSync's time
                songPosition = (float)(audioSync.GetAudioTime() * 1000.0);
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
            }
            else if (audioSource != null)
            {
                double now = AudioSettings.dspTime;
                double clipTime = audioSource.time; // seconds into clip
                audioStartDspTime = now - clipTime;
                try { audioSource.UnPause(); } catch { }
                songPosition = (float)(clipTime * 1000.0);
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
            }
        }
        catch { }
        isPlaying = true;
    }

    /// <summary>
    /// Set the music playback volume (0..1) on the Conductor's AudioSource. This affects music only.
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        if (audioSync != null && audioSync.audioSource != null)
        {
            audioSync.audioSource.volume = Mathf.Clamp01(volume);
            return;
        }
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        audioSource.volume = Mathf.Clamp01(volume);
        //Debug.Log($"Conductor: Music volume set to {audioSource.volume:F2}");
    }

    /// <summary>
    /// Starts a visual pre-roll where effectiveSongPosition advances from -delay to 0 before audio starts.
    /// </summary>
    public void StartVisualPreRoll(float delaySeconds)
    {
        float delayMs = Mathf.Max(0f, delaySeconds) * 1000f;
        if (delayMs <= 0f)
        {
            return;
        }
        isVisualPlaying = true;
        visualSongPosition = -delayMs;
        _effectiveSongPositionCached = visualSongPosition;
        _effectiveInitialized = true;
    _lastPreRollDurationMs = delayMs;
    //Debug.Log($"Conductor: Visual pre-roll started for {delaySeconds:F2}s (from {visualSongPosition} ms to 0 ms)");
    }

    /// <summary>
    /// Stops the visual pre-roll and resets the visual clock.
    /// </summary>
    public void StopVisualPreRoll()
    {
        if (isVisualPlaying)
        {
            isVisualPlaying = false;
            visualSongPosition = 0f;
            //Debug.Log("Conductor: Visual pre-roll stopped.");
        }
    }

    /// <summary>
    /// Start playback without an audio clip. This advances the timing system into a 'playing' state
    /// so spawners and other gameplay systems will treat timing as active even when no audio is present.
    /// Useful for testing or when audio is intentionally omitted.
    /// </summary>
    public void StartPlayingWithoutAudio()
    {
        // Stop any visual pre-roll and mark as playing so effectiveSongPosition will advance from 0
        StopVisualPreRoll();
        isPlaying = true;
        songPosition = 0f;
        _effectiveSongPositionCached = 0f;
        _effectiveInitialized = true;
        hasScheduledStart = false;
        scheduledDspTime = -1.0;
        //Debug.Log("Conductor: Entered playing state without audio.");
    }



    // Reusable buffer for OnGUI to minimize allocations
    private static readonly System.Text.StringBuilder ConductorOnGuiBuffer = new System.Text.StringBuilder(256);

    /// <summary>
    /// Preload spawners (notes / beat lines) for the provided chart. This should be called after spawners are initialized
    /// and before starting playback to reduce runtime instantiation.
    /// It will search the scene for NoteSpawner and BeatLineSpawner components and call their preload methods if available.
    /// </summary>
    public void PreloadSpawners(Chart chart)
    {
        if (chart == null) return;
        // Find all NoteSpawner instances in the scene
    var noteSpawners = Object.FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var ns in noteSpawners)
        {
            try
            {
                ns.PreloadAll(chart);
            }
            catch (System.Exception)
            {
                //Debug.LogWarning("Conductor.PreloadSpawners: NoteSpawner.PreloadAll failed");
            }
        }

    var beatLineSpawners = Object.FindObjectsByType<BeatLineSpawner>(FindObjectsSortMode.None);
        foreach (var bs in beatLineSpawners)
        {
            try
            {
                bs.PreloadAllBeatLines(chart);
            }
            catch (System.Exception)
            {
                //Debug.LogWarning("Conductor.PreloadSpawners: BeatLineSpawner.PreloadAllBeatLines failed");
            }
        }
        //Debug.Log("Conductor: PreloadSpawners completed.");
    }
}
