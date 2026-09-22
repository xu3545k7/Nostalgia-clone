using NUnit.Framework;

/// <summary>
/// Tests for TimingMath — sub-frame timing and monotonic position clamping.
/// </summary>
[TestFixture]
public class TimingMathTests
{
    [Test]
    public void ProjectSongPos_EventBeforeSample_Backdates()
    {
        float result = TimingMath.ProjectSongPosToEvent(1000f, 10.0, 9.995);
        Assert.That(result, Is.EqualTo(995f).Within(0.001f));
    }

    [Test]
    public void ProjectSongPos_EventAfterSample_Advances()
    {
        float result = TimingMath.ProjectSongPosToEvent(1000f, 10.0, 10.005);
        Assert.That(result, Is.EqualTo(1005f).Within(0.001f));
    }

    [Test]
    public void ProjectSongPos_OutsideSanityWindow_ReturnsFallback()
    {
        float result = TimingMath.ProjectSongPosToEvent(
            1000f, 10.0, 10.6, maxDeltaMs: 500.0, fallback: -999f);
        Assert.That(result, Is.EqualTo(-999f));
    }

    // ── SubFrameSongPos ─────────────────────────────────────────────────

    [Test]
    public void SubFrameSongPos_ValidAge_BackdatesSongPos()
    {
        // Event happened 5ms ago; songPos should be shifted back 5ms
        float result = TimingMath.SubFrameSongPos(1000f, 10.010, 10.005);
        Assert.AreEqual(995f, result, 0.5f);
    }

    [Test]
    public void SubFrameSongPos_ZeroAge_ReturnsSongPos()
    {
        float result = TimingMath.SubFrameSongPos(1000f, 10.0, 10.0);
        // Age = 0 which is not > 0, so fallback
        Assert.AreEqual(-1f, result);
    }

    [Test]
    public void SubFrameSongPos_NegativeEventTime_ReturnsFallback()
    {
        float result = TimingMath.SubFrameSongPos(1000f, 10.0, -1.0);
        Assert.AreEqual(-1f, result);
    }

    [Test]
    public void SubFrameSongPos_ZeroEventTime_ReturnsFallback()
    {
        float result = TimingMath.SubFrameSongPos(1000f, 10.0, 0.0);
        Assert.AreEqual(-1f, result);
    }

    [Test]
    public void SubFrameSongPos_FutureEvent_ReturnsFallback()
    {
        // Event timestamp in the "future" = negative age
        float result = TimingMath.SubFrameSongPos(1000f, 10.0, 10.1);
        Assert.AreEqual(-1f, result);
    }

    [Test]
    public void SubFrameSongPos_AgeBeyondMax_ReturnsFallback()
    {
        // 100ms ago, exceeds default 50ms max
        float result = TimingMath.SubFrameSongPos(1000f, 10.1, 10.0);
        Assert.AreEqual(-1f, result);
    }

    [Test]
    public void SubFrameSongPos_AgeAtBoundary_Works()
    {
        // 49ms ago, just within 50ms max
        float result = TimingMath.SubFrameSongPos(1000f, 10.049, 10.0);
        Assert.AreEqual(951f, result, 0.5f);
    }

    [Test]
    public void SubFrameSongPos_CustomMaxAge_Respected()
    {
        // 80ms ago, custom max 100ms
        float result = TimingMath.SubFrameSongPos(1000f, 10.080, 10.0, maxAgeMs: 100.0);
        Assert.AreEqual(920f, result, 0.5f);
    }

    [Test]
    public void SubFrameSongPos_CustomFallback_Returned()
    {
        float result = TimingMath.SubFrameSongPos(1000f, 10.0, 0.0, fallback: -999f);
        Assert.AreEqual(-999f, result);
    }

    // ── MonotonicClamp ──────────────────────────────────────────────────

    [Test]
    public void MonotonicClamp_ForwardMotion_AcceptsCurrent()
    {
        float result = TimingMath.MonotonicClamp(100f, 90f);
        Assert.AreEqual(100f, result);
    }

    [Test]
    public void MonotonicClamp_SameValue_AcceptsCurrent()
    {
        float result = TimingMath.MonotonicClamp(100f, 100f);
        Assert.AreEqual(100f, result);
    }

    [Test]
    public void MonotonicClamp_SmallBackwardJitter_AcceptsCurrent()
    {
        // 0.3ms backward within 0.5 epsilon
        float result = TimingMath.MonotonicClamp(99.7f, 100f);
        Assert.AreEqual(99.7f, result);
    }

    [Test]
    public void MonotonicClamp_LargeBackwardJump_ClampsToCached()
    {
        // 2ms backward exceeds 0.5 epsilon
        float result = TimingMath.MonotonicClamp(98f, 100f);
        Assert.AreEqual(100f, result);
    }

    [Test]
    public void MonotonicClamp_ExactEpsilonBoundary_AcceptsCurrent()
    {
        // current = cached - epsilon exactly
        float result = TimingMath.MonotonicClamp(99.5f, 100f, epsilon: 0.5f);
        Assert.AreEqual(99.5f, result);
    }

    [Test]
    public void MonotonicClamp_CustomEpsilon_Works()
    {
        // 1.5ms backward, epsilon = 2ms, should accept
        float result = TimingMath.MonotonicClamp(98.5f, 100f, epsilon: 2f);
        Assert.AreEqual(98.5f, result);
    }

    [Test]
    public void MonotonicClamp_NegativeValues_Works()
    {
        // Pre-roll positions can be negative
        float result = TimingMath.MonotonicClamp(-500f, -600f);
        Assert.AreEqual(-500f, result);
    }
}
