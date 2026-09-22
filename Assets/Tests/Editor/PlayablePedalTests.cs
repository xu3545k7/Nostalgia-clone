using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The two pure halves of the playable pedal: reducing recorded CC64 to changes a
/// foot could place, and the glow curve that draws those changes.
/// </summary>
[TestFixture]
public class PlayablePedalTests
{
    private static readonly PedalNoteTuning Tuning = PedalNoteTuning.Standard;

    private static List<PedalSpan> Spans(params int[] pairs)
    {
        var list = new List<PedalSpan>(pairs.Length / 2);
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            list.Add(new PedalSpan { start_ms = pairs[i], end_ms = pairs[i + 1] });
        }
        return list;
    }

    private static List<PedalSpan> Play(List<PedalSpan> raw) =>
        PlayablePedal.Build(raw, Tuning.minChangeIntervalMs, Tuning.maxPressShiftMs,
            Tuning.minVisibleGapMs, Tuning.minSpanMs);

    // ---- reducing to playable changes --------------------------------------

    [Test]
    public void Merge_KeepsLegatoPedallingSeparatedByATwoMillisecondLift()
    {
        // The shape syuten is full of: long spans, near-instant lift-and-stamp
        // between them. That is one change every 2.8 s, which is comfortable —
        // merging on gap width instead of press-to-press would have flattened the
        // whole chart into a single press.
        List<PedalSpan> playable = Play(Spans(3, 2792, 2794, 5582));

        Assert.AreEqual(2, playable.Count);
        Assert.AreEqual(3, playable[0].start_ms);
        Assert.AreEqual(2794, playable[1].start_ms);
    }

    // ---- 節奏踏板吸附 --------------------------------------------------------

    private static List<bool> Snap(List<PedalSpan> spans, params int[] onsets) =>
        PlayablePedal.SnapRhythmicPresses(spans, onsets, 80, 150, Tuning.minSpanMs);

    [Test]
    public void Snap_LeavesLegatoPedallingAlone()
    {
        // 和弦在 1000：腳在 1000 抬起、1090 才踩。踩下離和弦 90ms 以內也不能吸 ——
        // 它就是剛在同一個和弦上換的踏板。
        List<PedalSpan> spans = Spans(0, 1000, 1060, 2000);
        List<bool> snapped = Snap(spans, 0, 1000, 2000);

        Assert.IsFalse(snapped[1]);
        Assert.AreEqual(1060, spans[1].start_ms);
    }

    [Test]
    public void Snap_MovesARhythmicPressOntoItsChord()
    {
        // 腳在 600 就放開了，1030 才和 1000 的和弦一起踩下去。
        List<PedalSpan> spans = Spans(0, 600, 1030, 2000);
        List<bool> snapped = Snap(spans, 0, 1000, 2000);

        Assert.IsTrue(snapped[1]);
        Assert.AreEqual(1000, spans[1].start_ms);
        Assert.AreEqual(2000, spans[1].end_ms, "只動踩下，放開不動。");
    }

    [Test]
    public void Snap_IgnoresAPressWithNoNoteNearby()
    {
        List<PedalSpan> spans = Spans(0, 600, 1300, 2000);
        List<bool> snapped = Snap(spans, 0, 1000, 2000);

        Assert.IsFalse(snapped[1]);
        Assert.AreEqual(1300, spans[1].start_ms);
    }

    [Test]
    public void Snap_FirstPressOfThePieceCountsAsRhythmic()
    {
        List<PedalSpan> spans = Spans(40, 900);
        List<bool> snapped = Snap(spans, 0, 500);

        Assert.IsTrue(snapped[0]);
        Assert.AreEqual(0, spans[0].start_ms);
    }

    [Test]
    public void Merge_PushesAClosePressLaterInsteadOfDeletingIt()
    {
        // Deleting the change would tell the player the harmony never turned over.
        // Pushing is also the direction a pianist plays it: the pedal arriving after
        // the chord is syncopated pedalling, not an error.
        List<PedalSpan> playable = Play(Spans(0, 90, 150, 900));

        Assert.AreEqual(2, playable.Count, "The change survives, moved rather than lost.");
        Assert.AreEqual(Tuning.minChangeIntervalMs, playable[1].start_ms);
        Assert.AreEqual(900, playable[1].end_ms, "Only the press moves; the lift stays put.");
    }

    [Test]
    public void Merge_HoldsThroughAPressThatCannotBePushedFarEnough()
    {
        // 150 ms of shift is more than a nudge, so the change is given up rather
        // than redrawn somewhere the music never asked for.
        List<PedalSpan> playable = Play(Spans(0, 90, 100, 900));

        Assert.AreEqual(1, playable.Count);
        Assert.AreEqual(0, playable[0].start_ms);
        Assert.AreEqual(900, playable[0].end_ms);
    }

    [Test]
    public void Merge_RefusesAPushThatWouldEatTheSpanItMoves()
    {
        // The shift itself is small enough, but 50 ms of press would be left behind.
        List<PedalSpan> playable = Play(Spans(0, 1000, 150, 300, 2000, 3000));

        Assert.AreEqual(2, playable.Count);
        Assert.AreEqual(0, playable[0].start_ms);
        Assert.AreEqual(2000, playable[1].start_ms);
    }

    [Test]
    public void Merge_PushedPressNeverLandsBeforeTheLiftBeforeIt()
    {
        // Overlapping input is out of contract, but both AutoPedal and the restore
        // tool feed this, so it degrades to holding through rather than emitting
        // spans that overlap each other.
        List<PedalSpan> playable = Play(Spans(0, 400, 150, 500, 2000, 3000));

        for (int i = 1; i < playable.Count; i++)
        {
            Assert.GreaterOrEqual(playable[i].start_ms, playable[i - 1].end_ms,
                "A press cannot begin before the pedal has come up.");
        }
    }

    [Test]
    public void Merge_MeasuresPressToPressFromTheHeldSpanNotTheLastFragment()
    {
        // Four presses 100 ms apart, none of them pushable into place — too far to
        // shift, or nothing left of the span afterwards. The interval is measured
        // from the press being held, so they collapse into it until enough time has
        // passed to place a new one, 300 ms in. Measuring against the previous
        // fragment instead would chain forward and swallow that reachable press too.
        List<PedalSpan> playable = Play(Spans(0, 90, 100, 190, 200, 290, 300, 1000));

        Assert.AreEqual(2, playable.Count);
        Assert.AreEqual(0, playable[0].start_ms);
        Assert.AreEqual(300, playable[1].start_ms, "A press 300 ms in is reachable and survives.");
        Assert.AreEqual(1000, playable[1].end_ms);
    }

    [Test]
    public void Merge_LiftsTooBriefToRenderArePulledEarlier()
    {
        // A 2 ms gap falls between two frames, so without this the drop to darkness
        // that marks the lift is never drawn and the change reads as one press.
        List<PedalSpan> playable = Play(Spans(0, 2000, 2002, 4000));

        Assert.AreEqual(2, playable.Count);
        Assert.AreEqual(2002 - Tuning.minVisibleGapMs, playable[0].end_ms);
        Assert.AreEqual(2002, playable[1].start_ms, "Only the lift moves; the press stays put.");
    }

    [Test]
    public void Merge_WideningALiftNeverEatsTheSpanItShortens()
    {
        // The span is only just long enough to be a press, so the gap stays short
        // rather than the press being widened out of existence.
        List<PedalSpan> playable = PlayablePedal.Build(Spans(0, 100, 102, 900), 90, 120, 400, 80);

        Assert.AreEqual(2, playable.Count);
        Assert.AreEqual(80, playable[0].end_ms);
        Assert.GreaterOrEqual(playable[0].end_ms - playable[0].start_ms, 80);
    }

    [Test]
    public void Merge_DropsAnIsolatedDabTooShortToHold()
    {
        List<PedalSpan> playable = Play(Spans(0, 1000, 3000, 3030, 5000, 6000));

        Assert.AreEqual(2, playable.Count, "A 30 ms blip between one-second gaps is not a press.");
        Assert.AreEqual(0, playable[0].start_ms);
        Assert.AreEqual(5000, playable[1].start_ms);
    }

    [Test]
    public void Merge_LeavesTheSourceSpansAlone()
    {
        List<PedalSpan> raw = Spans(0, 2000, 2002, 4000);
        Play(raw);

        Assert.AreEqual(2000, raw[0].end_ms, "The chart's own spans still feed the keysound.");
        Assert.AreEqual(2, raw.Count);
    }

    [Test]
    public void Merge_SkipsDegenerateAndAbsentInput()
    {
        Assert.AreEqual(0, Play(null).Count);
        Assert.AreEqual(0, Play(new List<PedalSpan>()).Count);
        Assert.AreEqual(0, Play(Spans(500, 500, 900, 800)).Count,
            "Zero-length and backwards spans carry no pedalling.");
    }

    [Test]
    public void Merge_HardcoreLeavesAChangeWhereStandardHasToMoveIt()
    {
        // Both modes keep the change. Standard cannot place presses 180 ms apart so
        // it pushes the second one out; hardcore asks more of the foot and can leave
        // it exactly where the recording put it.
        List<PedalSpan> raw = Spans(0, 170, 180, 1000);
        PedalNoteTuning standard = PedalNoteTuning.Standard;
        PedalNoteTuning hardcore = PedalNoteTuning.Hardcore;

        List<PedalSpan> viaStandard = PlayablePedal.Build(raw, standard.minChangeIntervalMs,
            standard.maxPressShiftMs, standard.minVisibleGapMs, standard.minSpanMs);
        List<PedalSpan> viaHardcore = PlayablePedal.Build(raw, hardcore.minChangeIntervalMs,
            hardcore.maxPressShiftMs, hardcore.minVisibleGapMs, hardcore.minSpanMs);

        Assert.AreEqual(2, viaStandard.Count);
        Assert.AreEqual(250, viaStandard[1].start_ms, "Standard has to push the press out.");
        Assert.AreEqual(2, viaHardcore.Count);
        Assert.AreEqual(180, viaHardcore[1].start_ms, "Hardcore plays it as recorded.");
    }

    [Test]
    public void AutoPedal_DoesNotLiftInsideASustainedNote()
    {
        // 拍線每 500ms，所以放開點落在 480 / 980 / 1480…
        var beats = new List<int>();
        for (int i = 0; i < 12; i++) beats.Add(i * 500);

        // 300~900ms 有一顆還按著的音，正好橫跨 480 那個放開點。
        var notes = new List<NoteData>
        {
            new NoteData { startTime = 300, endTime = 900, pitch = 60 },
        };

        List<PedalSpan> withoutNotes = AutoPedal.Build(beats, 4, PianoAutoPedal.Beat);
        List<PedalSpan> withNotes = AutoPedal.Build(beats, 4, PianoAutoPedal.Beat, notes);

        Assert.AreEqual(480, withoutNotes[0].end_ms, "沒有音符資訊時照舊每拍放開");
        // 推到下一個候選拍，而不是刪掉這次變化——和 generate_pedal 同一條規則。
        Assert.AreEqual(980, withNotes[0].end_ms, "放開應該被推過那顆長音");
    }

    [Test]
    public void AutoPedal_TreatsLongTapsLikeHolds()
    {
        // 譜面裡有 16878 顆「型別是 tap 但長度 >= 300ms」的音符。判斷只看時長，
        // 不看 note_type —— 只認 hold 會漏掉三分之一的否決點。
        var beats = new List<int>();
        for (int i = 0; i < 12; i++) beats.Add(i * 500);

        var tap = new List<NoteData>
        {
            new NoteData { startTime = 300, endTime = 900, pitch = 60, note_type = 0, type = "tap" },
        };
        Assert.AreEqual(980, AutoPedal.Build(beats, 4, PianoAutoPedal.Beat, tap)[0].end_ms);
    }

    [Test]
    public void AutoPedal_StillLiftsWhenTheSpanGetsTooLong()
    {
        // 一顆蓋住整首的長音不能讓踏板永遠不放——那條上限是聲音預算，不是音樂。
        var beats = new List<int>();
        for (int i = 0; i < 20; i++) beats.Add(i * 500);
        var notes = new List<NoteData>
        {
            new NoteData { startTime = 100, endTime = 9000, pitch = 60 },
        };

        List<PedalSpan> spans = AutoPedal.Build(beats, 4, PianoAutoPedal.Beat, notes);

        Assert.IsNotEmpty(spans, "壓太久仍然必須放開");
        foreach (var span in spans)
            Assert.LessOrEqual(span.end_ms - span.start_ms, 2000, "沒有守住 MaxSpanMs");
    }

    [Test]
    public void Merge_LeavesPerBeatAutoPedalIntact()
    {
        // This is the shape the effect gets looked at with before a real pedal
        // exists. Auto-pedal lifts for a fixed 20 ms, so a gap-width rule would have
        // erased every change in the song and left one press three minutes long.
        var beats = new List<int>();
        for (int i = 0; i < 200; i++) beats.Add(i * 500);

        List<PedalSpan> playable = Play(AutoPedal.Build(beats, 4, PianoAutoPedal.Beat));

        Assert.AreEqual(199, playable.Count, "One pedal change per beat must survive.");
        Assert.AreEqual(500 - Tuning.minVisibleGapMs, playable[0].end_ms,
            "The 20 ms auto-pedal lift is widened to something the eye can catch.");
    }

    [Test]
    public void Merge_HoldsTheInvariantsOnMessyCharts()
    {
        // Pushing presses around is the part most likely to produce something the
        // renderer and the judgment cannot both read, so the three properties they
        // rely on are checked over generated charts rather than argued about. Every
        // third chart is deliberately out of contract with overlapping spans.
        var random = new System.Random(7);
        for (int trial = 0; trial < 300; trial++)
        {
            bool overlapping = trial % 3 == 0;
            var raw = new List<PedalSpan>();
            int cursor = 0;
            int count = random.Next(1, 40);
            for (int i = 0; i < count; i++)
            {
                int start = cursor + random.Next(overlapping ? -200 : 0, 400);
                int end = start + random.Next(1, 900);
                raw.Add(new PedalSpan { start_ms = start, end_ms = end });
                cursor = end;
            }
            raw.Sort((a, b) => a.start_ms != b.start_ms
                ? a.start_ms.CompareTo(b.start_ms)
                : a.end_ms.CompareTo(b.end_ms));

            List<PedalSpan> playable = Play(raw);
            for (int i = 0; i < playable.Count; i++)
            {
                Assert.GreaterOrEqual(playable[i].end_ms - playable[i].start_ms, Tuning.minSpanMs,
                    $"trial {trial}: a span too short to read as a press survived.");
                if (i == 0) continue;
                Assert.GreaterOrEqual(playable[i].start_ms, playable[i - 1].end_ms,
                    $"trial {trial}: spans overlap.");
                Assert.GreaterOrEqual(playable[i].start_ms - playable[i - 1].start_ms,
                    Tuning.minChangeIntervalMs,
                    $"trial {trial}: two presses closer together than a foot can place.");
            }
        }
    }


    // ---- the shape of the note ---------------------------------------------

    private static float Depth(float t, float noteLengthMs) => PedalNoteShape.AlphaAt(
        t,
        PedalNoteShape.RampFraction(Tuning.headRampMs, noteLengthMs, Tuning.rampSpanFraction),
        PedalNoteShape.RampFraction(Tuning.tailRampMs, noteLengthMs, Tuning.rampSpanFraction),
        Tuning);

    [Test]
    public void Shape_IsDarkestAtTheHead()
    {
        Assert.AreEqual(Tuning.headAlpha, Depth(0f, 3000f), 0.001f);
    }

    [Test]
    public void Shape_IsNearlyInvisibleThroughTheMiddle()
    {
        // The middle carries no information: the pedal is simply down. It must not
        // compete for attention with the notes being read over it.
        float middle = Depth(0.5f, 3000f);
        Assert.AreEqual(Tuning.idleAlpha, middle, 0.001f);
        Assert.Less(middle, 0.2f, "A note held for most of a song cannot sit bright on screen.");
    }

    [Test]
    public void Shape_DarkensIntoTheTerminator()
    {
        // 3000 ms note, so the tail ramp is the full 200 ms: t = 1 - 200/3000.
        const float length = 3000f;
        float rampStart = 1f - Tuning.tailRampMs / length;

        float beforeRamp = Depth(rampStart - 0.01f, length);
        float midRamp = Depth(rampStart + (1f - rampStart) * 0.5f, length);
        float atTerminator = Depth(1f, length);

        Assert.AreEqual(Tuning.idleAlpha, beforeRamp, 0.001f, "Nothing happens before the ramp.");
        Assert.Greater(midRamp, beforeRamp);
        Assert.Greater(atTerminator, midRamp);
        Assert.AreEqual(Tuning.tailAlpha, atTerminator, 0.001f);
    }

    [Test]
    public void Shape_DarkeningIsAlsoTheEarlyReleaseWindow()
    {
        // The visual and the tolerance are deliberately the same statement, so where
        // it starts darkening is where letting go is accepted.
        const float length = 3000f;
        float rampStart = 1f - Tuning.tailRampMs / length;

        Assert.Greater(Depth(rampStart + 0.01f, length), Tuning.idleAlpha);
        Assert.AreEqual(Tuning.idleAlpha, Depth(rampStart - 0.01f, length), 0.001f);
    }

    [Test]
    public void Shape_RampsStayAConstantLengthRegardlessOfNoteLength()
    {
        // A seven-second pedal must not start warning about its release six seconds
        // early, so the ramp is a constant time and only its fraction changes.
        Assert.AreEqual(Tuning.tailRampMs / 1000f,
            PedalNoteShape.RampFraction(Tuning.tailRampMs, 1000f, Tuning.rampSpanFraction), 0.0001f);
        Assert.AreEqual(Tuning.tailRampMs / 7600f,
            PedalNoteShape.RampFraction(Tuning.tailRampMs, 7600f, Tuning.rampSpanFraction), 0.0001f);
    }

    [Test]
    public void Shape_ShortNoteKeepsAMiddleInsteadOfDarkeningEndToEnd()
    {
        // 300 ms is shorter than the two ramps put together, so without the cap they
        // would cover the whole note and it would read as one solid bar.
        const float length = 300f;
        float head = Depth(0f, length);
        float middle = Depth(0.5f, length);
        float terminator = Depth(1f, length);

        Assert.AreEqual(Tuning.headAlpha, head, 0.001f);
        Assert.AreEqual(Tuning.tailAlpha, terminator, 0.001f);
        Assert.Less(middle, head);
        Assert.Less(middle, terminator, "The middle stays lighter than both ends.");
        Assert.LessOrEqual(PedalNoteShape.RampFraction(Tuning.headRampMs, length, Tuning.rampSpanFraction)
            + PedalNoteShape.RampFraction(Tuning.tailRampMs, length, Tuning.rampSpanFraction), 1f,
            "The two ramps must never cover more than the note.");
    }

    [Test]
    public void Shape_NeverLeavesTheZeroToOneRange()
    {
        foreach (float length in new[] { 80f, 300f, 1000f, 3000f, 7600f })
        {
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float depth = Depth(t, length);
                Assert.That(depth, Is.InRange(0f, 1f), $"length {length}, t {t}");
            }
        }
    }

    [Test]
    public void Shape_DegenerateNoteHasNoRamps()
    {
        Assert.AreEqual(0f, PedalNoteShape.RampFraction(200f, 0f, 0.4f), 0.0001f);
        Assert.AreEqual(Tuning.idleAlpha, PedalNoteShape.AlphaAt(0.5f, 0f, 0f, Tuning), 0.001f);
    }

    // ---- beat_timings 的格線 ----------------------------------------------
    // 這個曲庫的 beat_timings 有兩種意思：59 份一小節一條、31 份一個四分音符一條。
    // 分不出來的話 Measure 模式在前者變成四小節一換，Beat 模式在後者變成一個四分
    // 音符一換——離線產生器就是這樣讓 Hemisphere 每 311ms 換一次踏板的。

    private static List<int> Grid(int step, int count)
    {
        var v = new List<int>();
        for (int i = 0; i < count; i++) v.Add(i * step);
        return v;
    }

    [Test]
    public void EntriesPerBar_QuarterGridNeedsTheWholeBar()
    {
        // Hemisphere：BPM 193（四分音符 311ms）、4/4，一條就是一個四分音符。
        Assert.AreEqual(4, AutoPedal.EntriesPerBar(Grid(311, 32), 193f, 4));
    }

    [Test]
    public void EntriesPerBar_BarGridIsAlreadyOneBar()
    {
        // Melodiniq：同樣 BPM 193，但一條 1244ms＝一小節，再乘 4 就變成四小節。
        Assert.AreEqual(1, AutoPedal.EntriesPerBar(Grid(1244, 32), 193f, 4));
    }

    [Test]
    public void EntriesPerBar_ThreeFourGridUsesThree()
    {
        // 3/4，BPM 150：四分音符 400ms，一小節 3 條。
        Assert.AreEqual(3, AutoPedal.EntriesPerBar(Grid(400, 32), 150f, 3));
        Assert.AreEqual(1, AutoPedal.EntriesPerBar(Grid(1200, 32), 150f, 3));
    }

    [Test]
    public void EntriesPerBar_SillyTimeSignatureFallsBackToFour()
    {
        // felzione 的拍號寫 1/8、だれかの心臓 寫 1/4——照著用會變成一拍一小節。
        Assert.AreEqual(4, AutoPedal.EntriesPerBar(Grid(465, 32), 129f, 1));
    }

    [Test]
    public void EntriesPerBar_NoBpmOrTooFewEntriesKeepsTheTimeSignature()
    {
        Assert.AreEqual(4, AutoPedal.EntriesPerBar(Grid(311, 32), 0f, 4));
        Assert.AreEqual(4, AutoPedal.EntriesPerBar(Grid(311, 4), 193f, 4));
        Assert.AreEqual(4, AutoPedal.EntriesPerBar(null, 193f, 4));
    }

    [Test]
    public void Build_MeasureModeOnAQuarterGridChangesOncePerBar()
    {
        var beats = Grid(311, 40);
        var spans = AutoPedal.Build(beats, AutoPedal.EntriesPerBar(beats, 193f, 4),
            PianoAutoPedal.Measure);
        Assert.Greater(spans.Count, 1);
        // 一小節 1244ms，沒有超過 MaxSpanMs(2000)，所以不會被再切開。
        Assert.AreEqual(311 * 4, spans[1].start_ms - spans[0].start_ms);
    }
}
