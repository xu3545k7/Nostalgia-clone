using UnityEngine;

/// <summary>
/// Simple audio sync helper that uses AudioSettings.dspTime as the authoritative timebase.
/// Attach this to a GameObject and assign an AudioSource. Call Play() to start and use
/// GetAudioTime() to drive visual timing (in seconds) based on DSP time for accurate sync.
/// </summary>
public class AudioSync : MonoBehaviour
{
    [Tooltip("The AudioSource that plays the music. Should be configured with the correct clip.")]
    public AudioSource audioSource;

    // dsp time when playback was started (AudioSettings.dspTime)
    private double startDspTime = 0.0;

    // If playback is scheduled instead of immediate, we store the scheduled dsp time
    private double scheduledDspTime = 0.0;

    // Return true if we believe audio is playing
    public bool IsPlaying => audioSource != null && audioSource.isPlaying;

    /// <summary>
    /// Start immediate playback and capture the DSP time baseline.
    /// </summary>
    public void Play()
    {
        if (audioSource == null) return;
        // Record baseline at the moment we call Play. There is a tiny latency between calling Play() and actual sound,
        // but using dspTime as baseline keeps everything consistent. For tighter scheduling, use PlayScheduled.
        startDspTime = AudioSettings.dspTime;
        scheduledDspTime = startDspTime;
        audioSource.Play();
    }

    /// <summary>
    /// Schedule playback at an exact DSP time. Call this when you want precise alignment.
    /// Use GetAudioTime() afterwards; it will be relative to the scheduled DSP time.
    /// </summary>
    public void PlayScheduled(double dspTime)
    {
        if (audioSource == null) return;
        scheduledDspTime = dspTime;
        startDspTime = dspTime;
        audioSource.PlayScheduled(dspTime);
    }

    /// <summary>
    /// Stop playback and reset internal timers.
    /// </summary>
    public void Stop()
    {
        if (audioSource == null) return;
        audioSource.Stop();
        startDspTime = 0.0;
        scheduledDspTime = 0.0;
    }

    /// <summary>
    /// Pause playback but keep DSP baseline so GetAudioTime continues to report correct time if resumed via Resume().
    /// Note: AudioSource.Pause does not preserve a dsp-synchronized time; use Resume to continue.
    /// </summary>
    public void Pause()
    {
        if (audioSource == null) return;
        audioSource.Pause();
    }

    /// <summary>
    /// Resume playback and adjust DSP baseline so that GetAudioTime remains continuous.
    /// </summary>
    public void Resume()
    {
        if (audioSource == null) return;
        // When resuming, set startDspTime such that GetAudioTime() equals audioSource.time (in seconds)
        double now = AudioSettings.dspTime;
        double clipTime = audioSource.time; // seconds into clip
        startDspTime = now - clipTime;
        audioSource.UnPause();
    }

    /// <summary>
    /// Returns the elapsed audio time in seconds using the DSP time baseline.
    /// If playback hasn't started, returns 0.
    /// </summary>
    public double GetAudioTime()
    {
        if (audioSource == null) return 0.0;
        if (startDspTime <= 0.0) return 0.0;
        return AudioSettings.dspTime - startDspTime;
    }

    /// <summary>
    /// Returns the scheduled DSP time where playback was set to start (or 0 if none).
    /// </summary>
    public double GetScheduledDspTime()
    {
        return scheduledDspTime;
    }

    /// <summary>
    /// For debugging: debug-draw the current dsp and audio times in OnGUI when enabled.
    /// </summary>
    void OnGUI()
    {
        // Keep this lightweight and optional; only when running in Editor and a debug define is set
        #if UNITY_EDITOR
        if (audioSource != null && Application.isPlaying)
        {
            string s = string.Format("dspNow={0:F3} scheduled={1:F3} audioTime={2:F3}", AudioSettings.dspTime, scheduledDspTime, GetAudioTime());
            GUI.Label(new Rect(10, 10, 600, 20), s);
        }
        #endif
    }
}
