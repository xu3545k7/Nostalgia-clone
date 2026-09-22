using System.Collections.Generic;
using UnityEngine;

/// <summary>How to invent a sustain pedal for a chart that carries none.</summary>
/// <remarks>
/// Seventeen of the restored charts have no pedal at all, because their source
/// MIDIs never had CC64 — they are electronic or ensemble arrangements rather
/// than piano writing. Played on the sampled piano they damp the moment a key is
/// released, which is correct but very dry.
///
/// What is invented here is a guess and cannot be anything else: real pedalling
/// follows the harmony, and the chart does not say where the harmony changes.
/// Changing pedal on a metrical boundary is the standard approximation and holds
/// up while the harmonic rhythm is slower than that boundary; on faster changes
/// it smears. That is why this defaults to off and is chosen per taste.
/// </remarks>
public enum PianoAutoPedal
{
    Off = 0,
    Beat = 1,
    Measure = 2,
}

/// <summary>Builds the substitute pedal described by <see cref="PianoAutoPedal"/>.</summary>
public static class AutoPedal
{
    /// <summary>
    /// Silence between two spans. Any gap at all is enough for the timeline to
    /// report a pedal change, but a change the player can hear needs the dampers
    /// to actually touch the strings.
    /// </summary>
    private const int LiftMs = 20;

    /// <summary>Shortest span worth producing; below this it is just a stutter.</summary>
    private const int MinSpanMs = 60;

    /// <summary>
    /// Longest span worth producing. Anything longer is subdivided.
    /// </summary>
    /// <remarks>
    /// Per-bar pedalling holds far longer than any real pedalling in the corpus does
    /// against that much density, and the piano pays for it: simulated across 107
    /// charts, per-bar peaks at 240 simultaneous voices and puts 21 of them over the
    /// 160-voice pool, where the chart's own CC64 peaks at 150 and never exceeds it.
    /// Past that the voice manager starts stealing, which is heard as the piano cutting
    /// out under dense playing.
    ///
    /// Two seconds is the loosest cap that clears every chart (worst peak 131, none
    /// over the pool). It is also a defensible musical limit on a guess: holding one
    /// pedal across more than about two seconds of unknown harmony is where a metrical
    /// approximation stops being plausible anyway.
    /// </remarks>
    private const int MaxSpanMs = 2000;

    /// <summary>
    /// 一條 beat_timings 記的是幾個四分音符——回答「幾條算一小節」。
    /// </summary>
    /// <remarks>
    /// beat_timings 在這個曲庫裡有兩種意思：59 份是一小節一條，31 份是一個四分
    /// 音符一條。把它一律當成「拍」，Measure 模式在前者會變成四小節一換，Beat
    /// 模式在後者會變成一個四分音符一換。離線的產生器踩過同一個坑：那批四分音符
    /// 格線的譜面精確度只有 40%、段數是真人的 1.84 倍，換算成每秒換踏是同密度
    /// 真人的 2.4～3.1 倍。
    /// </remarks>
    public static int EntriesPerBar(IReadOnlyList<int> beatTimings, float bpm, int beatsPerBar)
    {
        // 拍號寫 1/8、1/4 的譜面（felzione、だれかの心臓）照著用會變成「一拍一小節」，
        // 那不是它們真正的小節。夾住不夠，得退回 4。
        int perBar = (beatsPerBar >= 2 && beatsPerBar <= 7) ? beatsPerBar : 4;
        if (beatTimings == null || beatTimings.Count < 8 || bpm <= 0f) return perBar;

        var steps = new List<int>(beatTimings.Count - 1);
        for (int i = 0; i + 1 < beatTimings.Count; i++) steps.Add(beatTimings[i + 1] - beatTimings[i]);
        steps.Sort();
        float quarter = 60000f / bpm;
        // 間距接近一個四分音符 → 一條就是一拍，要 perBar 條才是一小節；
        // 明顯更長 → 一條本來就是一小節。
        return steps[steps.Count / 2] < quarter * 1.6f ? perBar : 1;
    }

    /// <summary>
    /// Turns beat positions into pedal spans, changing pedal on every beat or
    /// every bar. Returns an empty list when there is nothing usable to build on.
    /// </summary>
    public static List<PedalSpan> Build(IReadOnlyList<int> beatTimings, int beatsPerBar,
        PianoAutoPedal mode, IReadOnlyList<NoteData> notes = null)
    {
        var spans = new List<PedalSpan>();
        if (mode == PianoAutoPedal.Off || beatTimings == null || beatTimings.Count < 2) return spans;

        List<(int start, int end)> sustained = CollectSustainedNotes(notes);
        int step = mode == PianoAutoPedal.Measure ? Mathf.Max(1, beatsPerBar) : 1;

        int press = beatTimings[0];
        for (int i = 0; i + step < beatTimings.Count; i += step)
        {
            int lift = beatTimings[i + step] - LiftMs;

            // 一顆還在響的長音橫跨這個放開點，就把放開往後推到下一個候選拍。
            // 手還按著的音就是當下的和聲；這時放踏板，那顆音自己不會斷（鍵還按著），
            // 斷的是它底下所有已經放開的音。實測：不推的話每拍模式有 19% 的放開落在
            // 長音中間，比隨便一條拍線(14.6%)還糟，而真人只有 5.9%；推了之後是 2%。
            //
            // 「壓太久」那條例外照樣過 —— 那是聲音預算不是音樂（見 MaxSpanMs）。
            if (lift - press < MaxSpanMs && CrossesSustainedNote(sustained, lift)) continue;

            AddSubdivided(spans, press, lift);
            press = beatTimings[i + step];
        }
        return spans;
    }

    /// <summary>
    /// 長到足以決定和聲的音符。
    /// </summary>
    /// <remarks>
    /// **不看 note_type，只看時長。** 譜面裡有 16878 顆「型別是 tap 但長度 ≥300ms」
    /// 的音符（占全部的 7.8%），它們和 hold 一樣代表手還按著。實測在譜面生成器那邊，
    /// 這種長 tap 貢獻了 34% 的否決點——只認 hold 會漏掉三分之一。
    /// </remarks>
    private const int SustainedMs = 300;
    private const int EdgeToleranceMs = 25;

    private static List<(int start, int end)> CollectSustainedNotes(IReadOnlyList<NoteData> notes)
    {
        // 元素型別要帶名字，否則 Sort 的 lambda 收到的是無名的 (int, int)，
        // 讀 .start 就會編不過。
        var result = new List<(int start, int end)>();
        if (notes == null) return result;
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n == null) continue;
            if (n.endTime - n.startTime >= SustainedMs) result.Add((n.startTime, n.endTime));
        }
        result.Sort((a, b) => a.start.CompareTo(b.start));
        return result;
    }

    private static bool CrossesSustainedNote(List<(int start, int end)> sustained, int when)
    {
        if (sustained == null || sustained.Count == 0) return false;
        // 起音排序過，所以從「起音 <= when」的最後一顆往回找就好，再往前的都結束很久了。
        int lo = 0, hi = sustained.Count - 1, last = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (sustained[mid].start <= when) { last = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        for (int i = last; i >= 0; i--)
        {
            var span = sustained[i];
            if (span.end > when + EdgeToleranceMs && span.start < when - EdgeToleranceMs) return true;
            if (when - span.start > 12000) break;
        }
        return false;
    }

    /// <summary>
    /// Adds one span, split into equal pieces if it is longer than <see cref="MaxSpanMs"/>.
    /// </summary>
    /// <remarks>
    /// Equal pieces rather than a cap plus a remainder, so a subdivided bar still
    /// changes pedal on a regular pulse instead of once long and once short.
    /// </remarks>
    private static void AddSubdivided(List<PedalSpan> spans, int start, int end)
    {
        int length = end - start;
        if (length < MinSpanMs) return;

        int pieces = Mathf.Max(1, Mathf.CeilToInt(length / (float)MaxSpanMs));
        float piece = length / (float)pieces;
        for (int k = 0; k < pieces; k++)
        {
            int from = start + Mathf.RoundToInt(k * piece);
            int to = start + Mathf.RoundToInt((k + 1) * piece) - (k < pieces - 1 ? LiftMs : 0);
            if (to - from < MinSpanMs) continue;
            spans.Add(new PedalSpan { start_ms = from, end_ms = to });
        }
    }
}
