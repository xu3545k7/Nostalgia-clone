using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;

public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    [Header("Pool")]
    public int poolSize = 24;
    public AudioMixerGroup sfxMixerGroup;

    [Header("Voice limit")]
    public int maxSimultaneousVoices = 16;

    [Header("Debug")]
    [Tooltip("When true (Editor only), force PlayClip to play regardless of suppression heuristics. Use only for tracing.")]
    public bool debugForceAllowPlay = false;

    private List<AudioSource> pool;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        pool = new List<AudioSource>(poolSize);
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject($"SFX_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // default to 2D
            if (sfxMixerGroup != null) src.outputAudioMixerGroup = sfxMixerGroup;
            pool.Add(src);
        }
        DontDestroyOnLoad(gameObject);
    }

    AudioSource GetFreeSource()
    {
        foreach (var s in pool)
        {
            if (!s.isPlaying) return s;
        }
        // All busy: steal the one that has the largest playback time (oldest)
        AudioSource oldest = pool[0];
        foreach (var s in pool)
        {
            if (s.time > oldest.time) oldest = s;
        }
        return oldest;
    }

    public void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f, Vector3? worldPos = null)
    {
        // If JudgmentManager (or anyone) requested global suppression for 'end' judgments,
        // obey it so end/tail sounds never play regardless of who called PlayClip.
        try
        {
            if (HitSoundManager.IsSuppressingEnd)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                try { Debug.Log($"[SFXManager] Suppressed PlayClip due to HitSoundManager.IsSuppressingEnd clip={(clip!=null?clip.name:"(null)")}"); } catch { }
                #endif
                return;
            }
        }
        catch { }

        // Development-only diagnostic: log when the clip is requested so we can trace unexpected tails
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            if (clip != null)
            {
                var name = string.IsNullOrEmpty(clip.name) ? "(noname)" : clip.name;
                Debug.Log($"[SFXManager] PlayClip requested: clip={name} volume={volume} pitch={pitch} worldPos={(worldPos.HasValue?worldPos.Value.ToString():"(null)")}");
            }
            else
            {
                Debug.Log("[SFXManager] PlayClip requested: clip=(null)");
            }
        }
        catch { }
#endif

        // Development-only defensive suppression: if the callstack indicates a finalize/end path
        // (e.g. FinalizeHold, AutoFinalizeHold, OnHoldEnd, DelayedFinalizeCoroutine), suppress playback
        // to avoid stray tail sounds while we trace and fix the root caller.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            var st = System.Environment.StackTrace ?? "";
            var stLower = st.ToLowerInvariant();

            // If developer opted to force-allow playback for tracing, skip suppression checks.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugForceAllowPlay)
            {
                Debug.Log("[SFXManager] debugForceAllowPlay=true -> bypassing suppression checks (Editor only)");
            }
            else
            {
                // If developer has enabled explicit signatures to test, only allow playback when stacktrace
                // contains one of those signatures. This allows "turning on" specific callers one-by-one.
                try
                {
                    // Use reflection to detect a development-only DevSoundTracer type if it exists in any loaded assembly.
                    // This avoids hard compile dependency on the tracer (which might live in a different assembly/asmdef)
                    var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        var tracerType = asm.GetType("DevSoundTracer") ?? asm.GetType("DevTools.DevSoundTracer");
                        if (tracerType == null) continue;

                        var hasMethod = tracerType.GetMethod("HasEnabledSignatures", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var containsMethod = tracerType.GetMethod("StackContainsEnabledSignature", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (hasMethod == null || containsMethod == null) continue;

                        var hasEnabled = (bool)hasMethod.Invoke(null, null);
                        if (!hasEnabled) break; // tracer present but no enabled signatures -> use normal logic

                        var allowed = (bool)containsMethod.Invoke(null, new object[] { stLower });
                        if (!allowed)
                        {
                            Debug.Log($"[SFXManager] Dev override: suppressing PlayClip because stacktrace does not match any enabled signature clip={(clip!=null?clip.name:"(null)")}");
                            return;
                        }
                        // allowed by dev override
                        break;
                    }
                }
                catch { }

                // Existing heuristic suppression: if stacktrace contains finalize/end helpers, suppress.
                if (stLower.Contains("finalizehold(") || stLower.Contains("autofinalizehold(") || stLower.Contains("onholdend(") || stLower.Contains("delayedfinalizecoroutine("))
                {
                    Debug.Log($"[SFXManager] Suppressing hit sound (dev-guard) clip={(clip!=null?clip.name:"(null)")} - caller appears to be finalize/end path\nCallStack:\n{st}");
                    return;
                }
            }
#endif

        }
        catch { }
#endif

        if (clip == null) return;

        // enforce voice limit: prefer to steal the oldest active source when pool is saturated
        int active = 0;
        foreach (var s in pool) if (s.isPlaying) active++;
        AudioSource src;
        if (active >= maxSimultaneousVoices)
        {
            // Instead of dropping, steal the oldest playing source so short hit sounds still play.
            src = GetFreeSource();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { Debug.Log($"[SFXManager] Voice limit reached -> stealing oldest source for clip={(clip!=null?clip.name:"(null)")} active={active} max={maxSimultaneousVoices} src={src.gameObject.name}"); } catch { }
#endif
        }
        else
        {
            src = GetFreeSource();
        }
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume);
        src.pitch = Mathf.Clamp(pitch, -3f, 3f);
        if (worldPos.HasValue)
        {
            src.spatialBlend = 1f;
            src.transform.position = worldPos.Value;
        }
        else
        {
            src.spatialBlend = 0f;
        }
    src.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    try
    {
        Debug.Log($"[SFXManager] Playing clip: clip={(clip!=null?clip.name:"(null)")} src={src.gameObject.name} activeAfter={(active+1)}");
    }
    catch { }
#endif
    }
}
