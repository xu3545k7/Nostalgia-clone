using UnityEngine;

/// <summary>
/// Centralized hit sound playback with a lightweight singleton. Falls back to an
/// AudioSource.PlayClipAtPoint when SFXManager is not available.
/// </summary>
public class HitSoundManager : MonoBehaviour
{
    public static HitSoundManager Instance { get; private set; }

    // When true, callers should skip playing tail/end sounds. SFXManager also checks this.
    public static bool IsSuppressingEnd { get; private set; }

    [Header("Hit Sound")]
    [Tooltip("Clip used for tap/hold-head hit sounds. If unset, will load Resources/Sound/Tap.")]
    public AudioClip hitClip;
    [Range(0f, 2f)] public float volume = 1f;
    [Range(0.5f, 2f)] public float pitch = 1f;
    [Tooltip("Mark this object DontDestroyOnLoad so hit sounds persist across scenes.")]
    public bool persistAcrossScenes = true;

    [Header("Pooling (SFXManager)")]
    [Tooltip("Auto-create an SFXManager with a pooled set of AudioSources if none exists.")]
    public bool autoCreateSFXManager = true;
    [Tooltip("Number of pooled AudioSources in SFXManager.")]
    public int sfxPoolSize = 24;
    [Tooltip("Maximum simultaneous voices; beyond this the oldest is stolen.")]
    public int sfxMaxSimultaneousVoices = 16;

    private float GetSettingsVolume()
    {
        try
        {
            return Mathf.Clamp01(SettingsManager.Instance != null ? SettingsManager.Instance.HitSoundVolume : 1f);
        }
        catch { return 1f; }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);

        EnsureSFXManager();

        // Load default clip if none assigned.
        if (hitClip == null)
        {
            try { hitClip = Resources.Load<AudioClip>("Sound/Tap"); } catch { hitClip = null; }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void EnsureSFXManager()
    {
        if (!autoCreateSFXManager) return;
        if (SFXManager.Instance != null) return;
        try
        {
            var existing = FindFirstObjectByType<SFXManager>();
            if (existing != null)
            {
                // Attach settings if available
                existing.poolSize = sfxPoolSize;
                existing.maxSimultaneousVoices = sfxMaxSimultaneousVoices;
                return;
            }

            var go = new GameObject("SFXManager");
            var mgr = go.AddComponent<SFXManager>();
            mgr.poolSize = sfxPoolSize;
            mgr.maxSimultaneousVoices = sfxMaxSimultaneousVoices;
        }
        catch { }
    }

    /// <summary>
    /// Prewarm pooled AudioSources by a short silent play/stop to allocate internal buffers.
    /// </summary>
    public void PrewarmPool()
    {
        var mgr = SFXManager.Instance ?? FindFirstObjectByType<SFXManager>();
        if (mgr == null) return;
        try
        {
            // Play a very short silent clip if available, otherwise do nothing.
            var clip = hitClip ?? Resources.Load<AudioClip>("Sound/Tap");
            if (clip == null) return;
            // Use near-zero volume to avoid audible output.
            SFXManager.Instance.PlayClip(clip, 0.0001f, 1f);
        }
        catch { }
    }

    /// <summary>
    /// Ensure there is an instance available; creates one if none exist.
    /// </summary>
    public static HitSoundManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Try to find an existing one in the scene first.
        var existing = FindFirstObjectByType<HitSoundManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("HitSoundManager");
        return go.AddComponent<HitSoundManager>();
    }

    /// <summary>
    /// Play the hit sound using SFXManager if available, otherwise a one-shot AudioSource.
    /// Respects IsSuppressingEnd flag to avoid unwanted tail sounds.
    /// </summary>
    public void PlayHitSound(Vector3? worldPos = null)
    {
        if (IsSuppressingEnd) return;

        var clipToPlay = hitClip;
        if (clipToPlay == null)
        {
            try { clipToPlay = Resources.Load<AudioClip>("Sound/Tap"); } catch { clipToPlay = null; }
        }
        if (clipToPlay == null) return;

        float effectiveVolume = Mathf.Clamp(volume * GetSettingsVolume(), 0f, 2f);

        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayClip(clipToPlay, effectiveVolume, pitch, worldPos);
        }
        else
        {
            var pos = worldPos ?? Vector3.zero;
            AudioSource.PlayClipAtPoint(clipToPlay, pos, effectiveVolume);
        }
    }

    /// <summary>
    /// Play a specific clip with optional volume/pitch overrides. Falls back to PlayHitSound when clip is null.
    /// </summary>
    public void PlayClip(AudioClip clip, float volumeOverride = 1f, float pitchOverride = 1f, Vector3? worldPos = null)
    {
        if (IsSuppressingEnd) return;
        if (clip == null)
        {
            PlayHitSound(worldPos);
            return;
        }

        float effectiveVolume = Mathf.Clamp(volumeOverride * GetSettingsVolume(), 0f, 2f);

        if (SFXManager.Instance != null)
        {
            SFXManager.Instance.PlayClip(clip, effectiveVolume, pitchOverride, worldPos);
        }
        else
        {
            var pos = worldPos ?? Vector3.zero;
            AudioSource.PlayClipAtPoint(clip, pos, effectiveVolume);
        }
    }

    /// <summary>
    /// Toggle suppression for tail/end sounds (used by callers when needed).
    /// </summary>
    public static void SetSuppressEnd(bool suppress)
    {
        IsSuppressingEnd = suppress;
    }
}
