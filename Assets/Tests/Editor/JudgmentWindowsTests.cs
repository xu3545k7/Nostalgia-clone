using NUnit.Framework;

/// <summary>
/// Tests for JudgmentWindows — pure timing-delta to judgment-result mapping.
/// </summary>
[TestFixture]
public class JudgmentWindowsTests
{
    // Default windows: perfect=50, great=100, good=150

    // ── Evaluate (tap / hold head) ──────────────────────────────────────

    [Test]
    public void Evaluate_WithinPerfect_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.Evaluate(30f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_ExactPerfectBoundary_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.Evaluate(50f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_BetweenPerfectAndGreat_ReturnsGreat()
    {
        Assert.AreEqual(JudgmentResult.Great, JudgmentWindows.Evaluate(75f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_ExactGreatBoundary_ReturnsGreat()
    {
        Assert.AreEqual(JudgmentResult.Great, JudgmentWindows.Evaluate(100f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_BetweenGreatAndGood_ReturnsGood()
    {
        Assert.AreEqual(JudgmentResult.Good, JudgmentWindows.Evaluate(120f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_ExactGoodBoundary_ReturnsGood()
    {
        Assert.AreEqual(JudgmentResult.Good, JudgmentWindows.Evaluate(150f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_BeyondGood_ReturnsMiss()
    {
        Assert.AreEqual(JudgmentResult.Miss, JudgmentWindows.Evaluate(200f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_ZeroDelta_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.Evaluate(0f, 50, 100, 150));
    }

    [Test]
    public void Evaluate_JustOverPerfect_ReturnsGreat()
    {
        Assert.AreEqual(JudgmentResult.Great, JudgmentWindows.Evaluate(50.1f, 50, 100, 150));
    }

    // ── EvaluateStaccato (tail release) ─────────────────────────────────

    [Test]
    public void Staccato_WithinPerfect_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.EvaluateStaccato(50f, 70, 130, 200));
    }

    [Test]
    public void Staccato_ExactPerfectBoundary_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.EvaluateStaccato(70f, 70, 130, 200));
    }

    [Test]
    public void Staccato_BetweenPerfectAndGreat_ReturnsGreat()
    {
        Assert.AreEqual(JudgmentResult.Great, JudgmentWindows.EvaluateStaccato(100f, 70, 130, 200));
    }

    [Test]
    public void Staccato_BeyondGood_ReturnsMiss()
    {
        Assert.AreEqual(JudgmentResult.Miss, JudgmentWindows.EvaluateStaccato(250f, 70, 130, 200));
    }

    [Test]
    public void Staccato_NegativeDelta_ClampedToZero_ReturnsPerfect()
    {
        // Negative release delta (released before press) → treated as 0ms → Perfect
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.EvaluateStaccato(-10f, 70, 130, 200));
    }

    [Test]
    public void Staccato_ZeroDelta_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, JudgmentWindows.EvaluateStaccato(0f, 70, 130, 200));
    }
}
