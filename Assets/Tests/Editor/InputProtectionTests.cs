using NUnit.Framework;
using Judgment;

/// <summary>
/// Tests for InputProtection — key consumption, the arcade neighbour lock, and chatter guard.
/// </summary>
[TestFixture]
public class InputProtectionTests
{
    private InputProtection _prot;

    [SetUp]
    public void SetUp()
    {
        _prot = new InputProtection();
    }

    // ── IsConsumed ──────────────────────────────────────────────────────

    [Test]
    public void IsConsumed_NotConsumed_ReturnsFalse()
    {
        Assert.IsFalse(_prot.IsConsumed(0, 1));
    }

    [Test]
    public void IsConsumed_ConsumedSameFrame_ReturnsTrue()
    {
        _prot.Consume(5, 100);
        Assert.IsTrue(_prot.IsConsumed(5, 100));
    }

    [Test]
    public void IsConsumed_ConsumedDifferentFrame_ReturnsFalse()
    {
        _prot.Consume(5, 100);
        Assert.IsFalse(_prot.IsConsumed(5, 101));
    }

    [Test]
    public void IsConsumed_DifferentButton_ReturnsFalse()
    {
        _prot.Consume(5, 100);
        Assert.IsFalse(_prot.IsConsumed(6, 100));
    }

    [Test]
    public void IsConsumed_DistinctEventsInSameFrame_ReturnsFalse()
    {
        _prot.Consume(5, 100, 41);
        Assert.IsTrue(_prot.IsConsumed(5, 100, 41));
        Assert.IsFalse(_prot.IsConsumed(5, 100, 42));
    }

    // ── LockNeighbours / IsLockedFor（本家鄰鍵鎖）─────────────────────────

    [Test]
    public void Lock_NoLock_ReturnsFalse()
    {
        Assert.IsFalse(_prot.IsLockedFor(10, 1000f, 5000f));
    }

    [Test]
    public void Lock_BlocksLaterNoteWithinReach()
    {
        _prot.LockNeighbours(10, 1000f, 1000f);
        // 14 is 4 lanes away; the note is 200ms later than the one just hit.
        Assert.IsTrue(_prot.IsLockedFor(14, 1010f, 1200f));
        Assert.IsTrue(_prot.IsLockedFor(6, 1010f, 1200f));
    }

    [Test]
    public void Lock_NeverBlocksNearlySimultaneousNote()
    {
        // A chord's late finger or a tight arpeggio must still land.
        _prot.LockNeighbours(10, 1000f, 1000f);
        Assert.IsFalse(_prot.IsLockedFor(12, 1010f, 1000f));
        Assert.IsFalse(_prot.IsLockedFor(12, 1010f, 1000f + InputProtection.LockLaterThanMs));
    }

    [Test]
    public void Lock_OutOfReach_ReturnsFalse()
    {
        _prot.LockNeighbours(10, 1000f, 1000f);
        Assert.IsFalse(_prot.IsLockedFor(15, 1010f, 1200f));
        Assert.IsFalse(_prot.IsLockedFor(5, 1010f, 1200f));
    }

    [Test]
    public void Lock_ExpiresAfterThreeFrames()
    {
        _prot.LockNeighbours(10, 1000f, 1000f);
        Assert.IsTrue(_prot.IsLockedFor(11, 1000f + InputProtection.LockDurationMs - 1f, 1200f));
        Assert.IsFalse(_prot.IsLockedFor(11, 1000f + InputProtection.LockDurationMs, 1200f));
    }

    [Test]
    public void Lock_Frames_HoldsForTheHitFramePlusTwo()
    {
        // 本家：計數 3，每幀開頭先減 1 再查 → 判中那一幀加後面 2 幀。
        // 1000ms 在第 60 幀（1000..1016.67），鎖到第 62 幀結束（1050ms）。
        _prot.LockNeighbours(10, 1005f, 1000f);
        Assert.IsTrue(_prot.IsLockedFor(11, 1049f, 1200f, frames: true));
        Assert.IsFalse(_prot.IsLockedFor(11, 1051f, 1200f, frames: true));
    }

    [Test]
    public void Lock_Frames_BlocksOnlyFiveFramesLater()
    {
        _prot.LockNeighbours(10, 1005f, 1005f);   // 錨點第 60 幀
        Assert.IsFalse(_prot.IsLockedFor(11, 1010f, 1055f, frames: true), "第 63 幀，晚 3 幀");
        Assert.IsFalse(_prot.IsLockedFor(11, 1010f, 1072f, frames: true), "第 64 幀，晚 4 幀");
        Assert.IsTrue(_prot.IsLockedFor(11, 1010f, 1090f, frames: true), "第 65 幀，晚 5 幀");
    }

    [Test]
    public void Lock_AllowWithin_LetsAPressCloseToItsNoteThrough()
    {
        _prot.LockNeighbours(10, 1000f, 1000f);
        // 1010ms 按、音符在 1090ms：早 80ms，放行範圍 82ms 以內 → 不擋。
        Assert.IsFalse(_prot.IsLockedFor(11, 1010f, 1090f, allowWithinMs: 82f));
        // 音符在 1200ms：早 190ms，還是擦碰 → 擋。
        Assert.IsTrue(_prot.IsLockedFor(11, 1010f, 1200f, allowWithinMs: 82f));
    }

    [Test]
    public void Lock_LatestHitOverwritesAnchor()
    {
        _prot.LockNeighbours(10, 1000f, 1000f);
        _prot.LockNeighbours(11, 1020f, 1150f);
        // Anchored to the 1150ms note now, so 1200ms is no longer "later by 4 frames".
        Assert.IsFalse(_prot.IsLockedFor(12, 1030f, 1200f));
    }

    [Test]
    public void Lock_LaneBoundaries_DoNotThrow()
    {
        _prot.LockNeighbours(1, 1000f, 1000f);
        Assert.IsTrue(_prot.IsLockedFor(0, 1010f, 1200f));
        _prot.LockNeighbours(30, 1000f, 1000f);
        Assert.IsTrue(_prot.IsLockedFor(31, 1010f, 1200f));
        Assert.IsFalse(_prot.IsLockedFor(-1, 1010f, 1200f));
    }

    [Test]
    public void LockAfterPress_SeesLockSetInsideWindow()
    {
        // A brush at 1000ms; the real note is hit 10ms later and locks around lane 10.
        _prot.LockNeighbours(10, 1010f, 1000f);
        Assert.IsTrue(_prot.WasLockedAfterPress(12, 1000f, 16.66f, 1200f));
    }

    [Test]
    public void LockAfterPress_IgnoresLockSetAfterWindow()
    {
        _prot.LockNeighbours(10, 1030f, 1000f);
        Assert.IsFalse(_prot.WasLockedAfterPress(12, 1000f, 16.66f, 1200f));
    }

    [Test]
    public void LockAfterPress_StillOnlyBlocksLaterNotes()
    {
        _prot.LockNeighbours(10, 1010f, 1000f);
        Assert.IsFalse(_prot.WasLockedAfterPress(12, 1000f, 16.66f, 1050f));
    }

    // ── GuardChatter / IsChatterBlocked ─────────────────────────────────

    [Test]
    public void Chatter_BlocksSameLaneUntilReleased()
    {
        _prot.GuardChatter(10, 1000f);
        Assert.IsTrue(_prot.IsChatterBlocked(10, 1010f));
        _prot.ReleaseLane(10);
        Assert.IsFalse(_prot.IsChatterBlocked(10, 1011f));
    }

    [Test]
    public void Chatter_DoesNotTouchNeighbours()
    {
        _prot.GuardChatter(10, 1000f);
        Assert.IsFalse(_prot.IsChatterBlocked(11, 1010f));
    }

    [Test]
    public void Chatter_Disabled_ReturnsFalse()
    {
        _prot.EnableSelfStop = false;
        _prot.GuardChatter(10, 1000f);
        Assert.IsFalse(_prot.IsChatterBlocked(10, 1010f));
    }

    // ── Reset ───────────────────────────────────────────────────────────

    [Test]
    public void Reset_ClearsConsumed()
    {
        _prot.Consume(5, 100);
        _prot.Reset();
        Assert.IsFalse(_prot.IsConsumed(5, 100));
    }

    [Test]
    public void Reset_ClearsConsumedEvent()
    {
        _prot.Consume(5, 100, 41);
        _prot.Reset();
        Assert.IsFalse(_prot.IsConsumed(5, 100, 41));
    }

    [Test]
    public void Reset_ClearsLocksAndChatter()
    {
        _prot.LockNeighbours(10, 1000f, 1000f);
        _prot.GuardChatter(10, 1000f);
        _prot.Reset();
        Assert.IsFalse(_prot.IsLockedFor(11, 1010f, 1200f));
        Assert.IsFalse(_prot.IsChatterBlocked(10, 1010f));
    }

    // ── SharedInputThresholdMs ──────────────────────────────────────────

    [Test]
    public void SharedInputThreshold_DefaultIs20()
    {
        Assert.AreEqual(20, _prot.SharedInputThresholdMs);
    }

    [Test]
    public void SharedInputThreshold_SetAndGet()
    {
        _prot.SharedInputThresholdMs = 50;
        Assert.AreEqual(50, _prot.SharedInputThresholdMs);
    }
}
