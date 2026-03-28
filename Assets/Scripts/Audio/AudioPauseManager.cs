// Deprecated helper — audio-only pause logic has been inlined into GameManager.ApplyAudioManageWindows.
// Kept as an inert placeholder to avoid breaking references.
using UnityEngine;

[System.Obsolete("AudioPauseManager is deprecated; audio pause is handled inline in GameManager.")]
public class AudioPauseManager : MonoBehaviour
{
    void Awake()
    {
        // No-op placeholder
    }

    public void PauseAudio(AudioSource s, object audioSyncObj = null)
    {
        try { Debug.LogWarning("AudioPauseManager.PauseAudio called on deprecated class — no-op."); } catch { }
    }

    public void ResumeAudio(AudioSource s, object audioSyncObj = null)
    {
        try { Debug.LogWarning("AudioPauseManager.ResumeAudio called on deprecated class — no-op."); } catch { }
    }
}
