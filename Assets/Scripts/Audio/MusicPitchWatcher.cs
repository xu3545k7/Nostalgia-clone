using UnityEngine;

/// Attach this anywhere in the scene (e.g., same GameObject as MusicAudioManager).
/// It bridges GameManager's speed factor to MusicAudioManager without modifying GameManager.
public class MusicPitchWatcher : MonoBehaviour
{
    [Tooltip("MusicAudioManager that will enforce pitch compensation.")]
    public MusicAudioManager musicManager;
    private bool lastUseMixer;

    void Awake()
    {
        if (musicManager == null)
        {
            musicManager = GetComponent<MusicAudioManager>();
        }
    }

    void Update()
    {
        if (musicManager == null) return;

        var gm = GameManager.Instance;
        float speed = (gm != null) ? gm.CurrentAudioSpeedFactor : 1f;
        bool desiredUseMixer = (gm != null) ? gm.CurrentUseMixer : false;

        // Rebind to the actual music source if missing or if a better candidate is found
        var src = FindMusicSource(gm);
        if (src != null && src != musicManager.musicSource)
        {
            musicManager.SetMusicSource(src);
        }

        // Route to pitch or clean mixer based on JSON-driven flag
        if (desiredUseMixer != lastUseMixer || musicManager.useMixer != desiredUseMixer)
        {
            musicManager.ApplyRoutingForUseMixer(desiredUseMixer);
            lastUseMixer = desiredUseMixer;
        }

        if (musicManager.musicSource != null)
        {
            // Keep applying current speed (ensures pitch compensation stays in sync)
            musicManager.ApplySpeed(speed);
        }
    }

    private AudioSource FindMusicSource(GameManager gm)
    {
        // 1) Prefer GameManager.Conductor (with AudioSync if present)
        if (gm != null && gm.Conductor != null)
        {
            var syncSrc = gm.Conductor.audioSync != null ? gm.Conductor.audioSync.audioSource : null;
            if (syncSrc != null) return syncSrc;

            var condSrc = gm.Conductor.GetComponent<AudioSource>();
            if (condSrc != null) return condSrc;
        }

        // 2) Fallback: find any Conductor in scene
        try
        {
            var conductor = FindAnyObjectByType<Conductor>();
            if (conductor != null)
            {
                var syncSrc = conductor.audioSync != null ? conductor.audioSync.audioSource : null;
                if (syncSrc != null) return syncSrc;

                var condSrc = conductor.GetComponent<AudioSource>();
                if (condSrc != null) return condSrc;
            }
        }
        catch { }

        // 3) Fallback: heuristic search for an AudioSource likely used for music
        try
        {
            var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var src in sources)
            {
                if (src == null) continue;
                string n = src.gameObject.name.ToLowerInvariant();
                string g = src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name.ToLowerInvariant() : "";
                if (n.Contains("music") || g.Contains("music")) return src;
            }
        }
        catch { }

        return null;
    }
}
