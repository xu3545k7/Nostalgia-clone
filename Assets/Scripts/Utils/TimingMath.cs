/// <summary>
/// Pure timing math utilities. No Unity dependencies so they are unit-testable.
/// </summary>
public static class TimingMath
{
    /// <summary>
    /// Compute a sub-frame-corrected song position by back-dating <paramref name="songPos"/>
    /// based on the age of the hardware input event.
    /// Returns <paramref name="fallback"/> when the event timestamp is invalid.
    /// </summary>
    /// <param name="songPos">Current song position in ms at the time of the frame.</param>
    /// <param name="nowRealtime">realtimeSinceStartupAsDouble at frame start.</param>
    /// <param name="eventTime">Hardware timestamp (realtimeSinceStartup) of the input event.</param>
    /// <param name="maxAgeMs">Sanity cap: ignore events older than this (ms). Default 50.</param>
    /// <param name="fallback">Returned when the event time is unusable. Default -1.</param>
    public static float SubFrameSongPos(float songPos, double nowRealtime, double eventTime,
                                        double maxAgeMs = 50.0, float fallback = -1f)
    {
        if (eventTime <= 0.0) return fallback;
        double ageMs = (nowRealtime - eventTime) * 1000.0;
        if (ageMs > 0.0 && ageMs < maxAgeMs)
        {
            return songPos - (float)ageMs;
        }
        return fallback;
    }

    /// <summary>
    /// Projects a song-position sample onto the realtime timestamp of an input
    /// event. Unlike SubFrameSongPos, this deliberately supports an event that
    /// arrived later in the same frame as the Conductor sample.
    /// </summary>
    public static float ProjectSongPosToEvent(float sampledSongPos, double sampleRealtime,
                                               double eventTime, double maxDeltaMs = 50.0,
                                               float fallback = -1f)
    {
        if (sampleRealtime <= 0.0 || eventTime <= 0.0) return fallback;
        double deltaMs = (eventTime - sampleRealtime) * 1000.0;
        if (System.Math.Abs(deltaMs) <= maxDeltaMs)
        {
            return sampledSongPos + (float)deltaMs;
        }
        return fallback;
    }

    /// <summary>
    /// Enforce monotonic (non-decreasing) position with a small epsilon tolerance for jitter.
    /// Returns the new cached position.
    /// </summary>
    /// <param name="current">Newly computed position.</param>
    /// <param name="cached">Previously cached monotonic position.</param>
    /// <param name="epsilon">Allowed backward drift before clamping (ms). Default 0.5.</param>
    public static float MonotonicClamp(float current, float cached, float epsilon = 0.5f)
    {
        if (current >= cached - epsilon)
        {
            return current;
        }
        return cached;
    }
}
