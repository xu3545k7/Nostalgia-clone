using System.Collections.Generic;

/// <summary>
/// Reduces recorded pedalling to the changes a player could actually execute.
/// </summary>
/// <remarks>
/// What makes pedalling hard to play is how often it changes, not how briefly the
/// foot is off the pedal. A pianist changing pedal legato lifts and stamps inside a
/// couple of milliseconds and thinks nothing of it; syuten's 94 spans are separated
/// by gaps as short as 2 ms but average a change every 1.7 seconds, which is
/// comfortable. Merging on gap width would have flattened that whole chart into one
/// press, so the threshold here is press-to-press instead.
///
/// A change too close to the one before it is pushed later rather than deleted.
/// Deleting is the bigger lie: it tells the player the harmony never turned over.
/// Pushing is also the musically correct direction — pressing the pedal slightly
/// after the chord is syncopated pedalling, which is how the change is played in the
/// first place, whereas pulling it earlier would catch the harmony being cleared.
/// Only when a press cannot be pushed far enough to become reachable is it given up
/// and held through.
///
/// The keysound keeps using the raw spans regardless: it needs those 2 ms gaps to
/// clear the previous harmony, and it has no feet to run out of.
/// </remarks>
public static class PlayablePedal
{
    /// <summary>
    /// The pedal changes worth asking a player for, as a new list that shares no
    /// objects with <paramref name="raw"/>.
    /// </summary>
    /// <param name="minChangeIntervalMs">
    /// Shortest press-to-press interval a foot can place, measured from the press
    /// being held rather than the previous fragment.
    /// </param>
    /// <param name="maxPressShiftMs">
    /// How much later a press may be moved to make room for it. Past this the
    /// pedalling would no longer be describing the music it came from, so the press
    /// is held through instead.
    /// </param>
    /// <param name="minVisibleGapMs">
    /// Shortest lift worth drawing. A 2 ms gap falls between two frames, so the drop
    /// to darkness that marks the lift would never be rendered and the change would
    /// read as one continuous press. Widening it pulls the drawn lift up to this
    /// much earlier than the recording — the direction the release tolerance already
    /// allows, and never far enough to matter against a ~200 ms window.
    /// </param>
    /// <param name="minSpanMs">
    /// A span shorter than this is an isolated dab rather than a press. It is
    /// dropped, which widens the surrounding gap — the honest reading, because there
    /// is nothing there to hold.
    /// </param>
    public static List<PedalSpan> Build(IReadOnlyList<PedalSpan> raw, int minChangeIntervalMs,
        int maxPressShiftMs, int minVisibleGapMs, int minSpanMs)
    {
        var playable = new List<PedalSpan>(raw != null ? raw.Count : 0);
        if (raw == null || raw.Count == 0) return playable;

        if (minChangeIntervalMs < 0) minChangeIntervalMs = 0;
        if (maxPressShiftMs < 0) maxPressShiftMs = 0;
        if (minVisibleGapMs < 0) minVisibleGapMs = 0;
        if (minSpanMs < 0) minSpanMs = 0;

        bool open = false;
        int start = 0;
        int end = 0;

        for (int i = 0; i < raw.Count; i++)
        {
            PedalSpan span = raw[i];
            if (span == null || span.end_ms <= span.start_ms) continue;

            if (!open)
            {
                start = span.start_ms;
                end = span.end_ms;
                open = true;
                continue;
            }

            if (span.start_ms - start >= minChangeIntervalMs)
            {
                Flush(playable, start, end, minSpanMs);
                start = span.start_ms;
                end = span.end_ms;
                continue;
            }

            // Too close to the press being held. Push it to the earliest moment a
            // foot could reach, as long as that is a nudge rather than a rewrite and
            // leaves enough of the span behind to still read as a press.
            //
            // The pushed press must also land after the lift before it. That holds
            // on its own for sorted, non-overlapping input, but this is fed by both
            // the restore tool and AutoPedal, and an overlapping pair should degrade
            // to holding through rather than emit spans that overlap each other.
            int pushed = start + minChangeIntervalMs;
            if (pushed >= end && pushed - span.start_ms <= maxPressShiftMs
                && span.end_ms - pushed >= minSpanMs)
            {
                Flush(playable, start, end, minSpanMs);
                start = pushed;
                end = span.end_ms;
                continue;
            }

            // Nowhere left to put it: hold through the change.
            if (span.end_ms > end) end = span.end_ms;
        }

        if (open) Flush(playable, start, end, minSpanMs);

        WidenGapsToVisible(playable, minVisibleGapMs, minSpanMs);
        return playable;
    }

    /// <summary>
    /// 把「節奏踏板」的踩下吸到它伴隨的那個和弦上，回傳每一段有沒有被吸附。
    /// </summary>
    /// <remarks>
    /// 真實鋼琴有兩種踩法：
    ///
    /// **連音（切分）踏板**是最常見的：新和弦按下的同一瞬間抬腳，聲音出來之後才
    /// 踩。踩下刻意比音符晚，放開才和音符同時。這種不吸附 —— 把踩下拉到和弦上，
    /// 它會和剛才的放開疊在一起，「換踏板」在畫面上就消失了，也教錯了踩法。
    ///
    /// **節奏（直接）踏板**是腳和手同時下去，多半在樂段開頭、重音。它的特徵是踩下
    /// 之前腳已經放開一段時間了（不是剛在同一個和弦上抬起來），而且踩下離某個音符
    /// 很近。只有這種才吸附。
    ///
    /// 吸附是改資料不是改畫面：評分看的也是這份資料，看到的時間和評的時間要一致。
    /// </remarks>
    /// <param name="sortedOnsets">音符開始時間，由小到大。</param>
    /// <param name="snapWindowMs">踩下離音符多近才算「和手同時」。</param>
    /// <param name="minLiftBeforeMs">
    /// 那個音符之前，腳至少要已經放開多久。連音踏板的放開就落在同一個和弦上，
    /// 差距只有幾十毫秒，會被這一條擋掉。
    /// </param>
    public static List<bool> SnapRhythmicPresses(List<PedalSpan> spans, IReadOnlyList<int> sortedOnsets,
        int snapWindowMs, int minLiftBeforeMs, int minSpanMs)
    {
        var snapped = new List<bool>(spans != null ? spans.Count : 0);
        if (spans == null) return snapped;
        for (int i = 0; i < spans.Count; i++) snapped.Add(false);
        if (sortedOnsets == null || sortedOnsets.Count == 0) return snapped;

        for (int i = 0; i < spans.Count; i++)
        {
            PedalSpan span = spans[i];
            int onset = NearestOnset(sortedOnsets, span.start_ms);
            if (System.Math.Abs(onset - span.start_ms) > snapWindowMs) continue;

            // 第一段之前沒有放開：樂曲一開頭就踩，本來就是和手一起下去的。
            if (i > 0 && onset - spans[i - 1].end_ms < minLiftBeforeMs) continue;
            if (span.end_ms - onset < minSpanMs) continue;

            span.start_ms = onset;
            snapped[i] = true;
        }
        return snapped;
    }

    private static int NearestOnset(IReadOnlyList<int> sorted, int time)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] < time) lo = mid + 1; else hi = mid;
        }
        if (lo >= sorted.Count) return sorted[sorted.Count - 1];
        if (lo == 0) return sorted[0];
        return time - sorted[lo - 1] <= sorted[lo] - time ? sorted[lo - 1] : sorted[lo];
    }

    private static void Flush(List<PedalSpan> into, int start, int end, int minSpanMs)
    {
        if (end - start < minSpanMs) return;
        into.Add(new PedalSpan { start_ms = start, end_ms = end });
    }

    /// <summary>
    /// Pulls a lift earlier when the recorded one is too brief to survive being
    /// sampled once a frame, without ever shortening a span below what it takes to
    /// still read as a press.
    /// </summary>
    private static void WidenGapsToVisible(List<PedalSpan> spans, int minVisibleGapMs, int minSpanMs)
    {
        if (minVisibleGapMs <= 0) return;
        for (int i = 0; i + 1 < spans.Count; i++)
        {
            PedalSpan current = spans[i];
            int wanted = spans[i + 1].start_ms - minVisibleGapMs;
            if (wanted >= current.end_ms) continue;
            current.end_ms = System.Math.Max(wanted, current.start_ms + minSpanMs);
        }
    }
}
