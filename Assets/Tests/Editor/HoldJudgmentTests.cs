using NUnit.Framework;
using Judgment;

/// <summary>
/// Tests for HoldJudgment pure static helpers — EighthMs computation,
/// head/tail evaluation, and extra-award logic.
/// </summary>
[TestFixture]
public class HoldJudgmentTests
{
    // ── ComputeEighthMs ─────────────────────────────────────────────────

    [Test]
    public void EighthMs_Bpm120_Returns250()
    {
        // 8th note at 120 BPM: 30000/120 = 250ms
        Assert.AreEqual(250f, HoldJudgment.ComputeEighthMs(120f), 0.01f);
    }

    [Test]
    public void EighthMs_Bpm180_Returns167()
    {
        // 30000/180 = 166.67ms
        Assert.AreEqual(166.67f, HoldJudgment.ComputeEighthMs(180f), 0.01f);
    }

    [Test]
    public void EighthMs_Bpm60_Returns250_16thNote()
    {
        // BPM < 120 uses 16th notes: 15000/60 = 250ms
        Assert.AreEqual(250f, HoldJudgment.ComputeEighthMs(60f), 0.01f);
    }

    [Test]
    public void EighthMs_Bpm100_Returns150_16thNote()
    {
        // 15000/100 = 150ms
        Assert.AreEqual(150f, HoldJudgment.ComputeEighthMs(100f), 0.01f);
    }

    [Test]
    public void EighthMs_Bpm240_Returns250_QuarterNote()
    {
        // BPM > 220 uses quarter: 60000/240 = 250ms
        Assert.AreEqual(250f, HoldJudgment.ComputeEighthMs(240f), 0.01f);
    }

    [Test]
    public void EighthMs_Bpm300_Returns200_QuarterNote()
    {
        // 60000/300 = 200ms
        Assert.AreEqual(200f, HoldJudgment.ComputeEighthMs(300f), 0.01f);
    }

    [Test]
    public void EighthMs_ZeroBpm_ReturnsFallback500()
    {
        Assert.AreEqual(500f, HoldJudgment.ComputeEighthMs(0f));
    }

    [Test]
    public void EighthMs_NegativeBpm_ReturnsFallback500()
    {
        Assert.AreEqual(500f, HoldJudgment.ComputeEighthMs(-10f));
    }

    // ── EvaluateHoldHeadResult ──────────────────────────────────────────

    [Test]
    public void HeadResult_DeltaWithinPerfect_ReturnsPerfect()
    {
        Assert.AreEqual(JudgmentResult.Perfect, HoldJudgment.EvaluateHoldHeadResult(30f, 50, 100, 150));
    }

    [Test]
    public void HeadResult_DeltaWithinGreat_ReturnsGreat()
    {
        Assert.AreEqual(JudgmentResult.Great, HoldJudgment.EvaluateHoldHeadResult(80f, 50, 100, 150));
    }

    [Test]
    public void HeadResult_DeltaBeyondGood_ReturnsMiss()
    {
        Assert.AreEqual(JudgmentResult.Miss, HoldJudgment.EvaluateHoldHeadResult(200f, 50, 100, 150));
    }

    // ── ComputeStaccatoTailResult ───────────────────────────────────────

    [Test]
    public void StaccatoTail_QuickRelease_ReturnsPerfect()
    {
        // release at 1050, start at 1000 → delta 50 < 70
        Assert.AreEqual(JudgmentResult.Perfect, HoldJudgment.ComputeStaccatoTailResult(1050f, 1000f, 70, 130, 200));
    }

    [Test]
    public void StaccatoTail_MediumRelease_ReturnsGreat()
    {
        // delta 100
        Assert.AreEqual(JudgmentResult.Great, HoldJudgment.ComputeStaccatoTailResult(1100f, 1000f, 70, 130, 200));
    }

    [Test]
    public void StaccatoTail_LateRelease_ReturnsMiss()
    {
        // delta 300
        Assert.AreEqual(JudgmentResult.Miss, HoldJudgment.ComputeStaccatoTailResult(1300f, 1000f, 70, 130, 200));
    }

    [Test]
    public void StaccatoTail_ReleaseBeforePress_ClampedToZero()
    {
        // release at 990, start at 1000 → negative delta → clamp to 0 → Perfect
        Assert.AreEqual(JudgmentResult.Perfect, HoldJudgment.ComputeStaccatoTailResult(990f, 1000f, 70, 130, 200));
    }

    // ── ShouldAwardExtra ────────────────────────────────────────────────

    [Test]
    public void ShouldAwardExtra_MaxZero_ReturnsFalse()
    {
        Assert.IsFalse(HoldJudgment.ShouldAwardExtra(0f, float.MinValue, 0, 0, 250f, 500f));
    }

    [Test]
    public void ShouldAwardExtra_AllAwardsUsed_ReturnsFalse()
    {
        Assert.IsFalse(HoldJudgment.ShouldAwardExtra(0f, 200f, 5, 5, 250f, 500f));
    }

    [Test]
    public void ShouldAwardExtra_FirstAward_AfterOneEighth_ReturnsTrue()
    {
        // Press at 0, lastAward=MinValue (none yet), eighthMs=250, songPos=230 → 0.9*250=225 → 230 >= 225
        Assert.IsTrue(HoldJudgment.ShouldAwardExtra(0f, float.MinValue, 0, 5, 250f, 230f));
    }

    [Test]
    public void ShouldAwardExtra_FirstAward_TooEarly_ReturnsFalse()
    {
        // songPos 100 < 225 (0.9 * 250)
        Assert.IsFalse(HoldJudgment.ShouldAwardExtra(0f, float.MinValue, 0, 5, 250f, 100f));
    }

    [Test]
    public void ShouldAwardExtra_SubsequentAward_AfterEighth_ReturnsTrue()
    {
        // lastAward=200, eighthMs=250, songPos=430 → 430-200+0.001=230.001 >= 225 (0.9*250)
        Assert.IsTrue(HoldJudgment.ShouldAwardExtra(0f, 200f, 1, 5, 250f, 430f));
    }

    [Test]
    public void ShouldAwardExtra_SubsequentAward_TooSoon_ReturnsFalse()
    {
        // lastAward=200, songPos=300 → 300-200=100 < 225
        Assert.IsFalse(HoldJudgment.ShouldAwardExtra(0f, 200f, 1, 5, 250f, 300f));
    }

    // ── Staccato never awards per-beat extras ───────────────────────────
    // 系ぎて 的 28 顆 staccato 讓自動模式每輪固定掉 26 次 COMBO：長度 167~252ms
    // 算得出 maxExtra>=1，但自動流程在 startTime+60ms 就放開，第一個 extra 永遠
    // 等不到，FinalizeExpiredHolds 於是用 ComputeHoldFillResult(0, n)=Miss 補完。
    // StatsManager.ConfigureExpectedJudgments 早就規定 staccato 只有頭+尾，這裡
    // 是同一條規則的執行期版本。

    // 220 BPM：eighth = 30000/220 = 136.4ms，是 ComputeEighthMs 能給的最短間隔。
    // 用它才問得出真問題——120 BPM 下 252ms 的 staccato 本來就算不出 extra，
    // 那樣測了等於沒測（修好前也會過）。
    private const float DenseBpm = 220f;

    [Test]
    public void ExtraInfo_StaccatoByNoteType_HasNoExtras()
    {
        // validMs = (252-1)*0.8 = 200.8 → 200.8/136.4 = 1 個 extra（修好前）
        var nd = new NoteData { startTime = 1000, endTime = 1252, note_type = 3 };
        Assert.AreEqual(0, HoldJudgment.ComputeExtraInfo(nd, DenseBpm).maxExtra);
    }

    [Test]
    public void ExtraInfo_StaccatoByTypeString_HasNoExtras()
    {
        var nd = new NoteData { startTime = 1000, endTime = 1252, type = "staccato" };
        Assert.AreEqual(0, HoldJudgment.ComputeExtraInfo(nd, DenseBpm).maxExtra);
        var abbreviated = new NoteData { startTime = 1000, endTime = 1252, type = "stac" };
        Assert.AreEqual(0, HoldJudgment.ComputeExtraInfo(abbreviated, DenseBpm).maxExtra);
    }

    [Test]
    public void ExtraInfo_GenuineHold_StillHasExtras()
    {
        // 同樣是長音符，真 hold 完全不受影響。
        var nd = new NoteData { startTime = 1000, endTime = 1252, note_type = 2, type = "hold" };
        Assert.AreEqual(1, HoldJudgment.ComputeExtraInfo(nd, DenseBpm).maxExtra);
    }

    [Test]
    public void IsStaccato_MatchesBothSpellings()
    {
        Assert.IsTrue(HoldJudgment.IsStaccato(new NoteData { note_type = 3 }));
        Assert.IsTrue(HoldJudgment.IsStaccato(new NoteData { type = "Staccato" }));
        Assert.IsTrue(HoldJudgment.IsStaccato(new NoteData { type = "stac" }));
        Assert.IsFalse(HoldJudgment.IsStaccato(new NoteData { note_type = 2, type = "hold" }));
        Assert.IsFalse(HoldJudgment.IsStaccato(null));
    }
}
