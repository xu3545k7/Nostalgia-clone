using NUnit.Framework;

/// <summary>
/// Tests for AudioOutputLatency.ComputeMs — audio pipeline latency calculation.
/// </summary>
[TestFixture]
public class AudioOutputLatencyTests
{
    [Test]
    public void ComputeMs_256Buffers2x48000_Returns10_67ms()
    {
        // 256 * 2 / 48000 * 1000 = 10.667ms
        float ms = AudioOutputLatency.ComputeMs(256, 2, 48000);
        Assert.AreEqual(10.667f, ms, 0.01f);
    }

    [Test]
    public void ComputeMs_1024Buffers2x48000_Returns42_67ms()
    {
        float ms = AudioOutputLatency.ComputeMs(1024, 2, 48000);
        Assert.AreEqual(42.667f, ms, 0.01f);
    }

    [Test]
    public void ComputeMs_512Buffers4x44100_Returns46_44ms()
    {
        // 512 * 4 / 44100 * 1000 = 46.44ms
        float ms = AudioOutputLatency.ComputeMs(512, 4, 44100);
        Assert.AreEqual(46.44f, ms, 0.01f);
    }

    [Test]
    public void ComputeMs_ZeroSampleRate_DefaultsTo48000()
    {
        float ms = AudioOutputLatency.ComputeMs(256, 2, 0);
        // Should use 48000 fallback: 256 * 2 / 48000 * 1000
        Assert.AreEqual(10.667f, ms, 0.01f);
    }

    [Test]
    public void ComputeMs_NegativeSampleRate_DefaultsTo48000()
    {
        float ms = AudioOutputLatency.ComputeMs(256, 2, -1);
        Assert.AreEqual(10.667f, ms, 0.01f);
    }

    [Test]
    public void ComputeMs_ZeroBufferLength_ReturnsZero()
    {
        float ms = AudioOutputLatency.ComputeMs(0, 2, 48000);
        Assert.AreEqual(0f, ms, 0.001f);
    }

    [Test]
    public void ComputeMs_LargeBufferConfig_ReturnsCorrectValue()
    {
        // Extreme case: 4096 * 8 / 96000 * 1000 = 341.33ms
        float ms = AudioOutputLatency.ComputeMs(4096, 8, 96000);
        Assert.AreEqual(341.33f, ms, 0.01f);
    }
}
