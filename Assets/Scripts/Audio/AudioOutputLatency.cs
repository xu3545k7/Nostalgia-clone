/// <summary>
/// Pure utility for computing audio output latency from DSP buffer configuration.
/// Extracted from Conductor for testability — no MonoBehaviour dependency.
/// </summary>
public static class AudioOutputLatency
{
    /// <summary>
    /// Computes the audio output pipeline latency in milliseconds.
    /// AudioSettings.dspTime reports when samples enter the buffer;
    /// actual audible output lags by bufferLength * numBuffers / sampleRate seconds.
    /// </summary>
    /// <param name="bufferLength">DSP buffer size in samples (e.g. 256, 512, 1024).</param>
    /// <param name="numBuffers">Number of DSP buffers in the pipeline (typically 2-4).</param>
    /// <param name="sampleRate">Audio output sample rate in Hz (e.g. 44100, 48000).</param>
    /// <returns>Output latency in milliseconds, always >= 0.</returns>
    public static float ComputeMs(int bufferLength, int numBuffers, int sampleRate)
    {
        if (sampleRate <= 0) sampleRate = 48000;
        if (bufferLength <= 0 || numBuffers <= 0) return 0f;
        return (float)((double)(bufferLength * numBuffers) / sampleRate * 1000.0);
    }
}
