using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What counts as loud and soft <em>in this piece</em>.
/// </summary>
/// <remarks>
/// **Why the bands are per-chart and not fixed numbers.** A nocturne's fortissimo
/// and an étude's pianissimo can be the same MIDI velocity. A fixed pair of
/// thresholds therefore says nothing about how a piece is meant to be played --
/// it sorts pieces, not notes. Measured across the whole library the median
/// velocity is 96, but the median of any one chart can sit anywhere, and a
/// threshold set from the library average marks a quiet piece entirely soft and
/// a loud one entirely loud, which is the same as marking nothing.
///
/// Each chart is cut at its own twentieth and eightieth percentiles, so roughly
/// the loudest fifth and the softest fifth of *that performance* are marked and
/// the middle three fifths are left bare.
///
/// **Why a chart can decline to have bands at all.** Two cases have no dynamics
/// to show and must not be dressed up as though they had: a chart whose velocity
/// was never restored (every note falls back to one value) and a chart that is
/// genuinely flat. Both come out as a spread too small to divide, and then
/// nothing is marked -- which is the honest picture.
/// </remarks>
public static class VelocityBands
{
    /// <summary>Fewer real velocities than this and the sample is not worth cutting.</summary>
    private const int MinSample = 24;
    /// <summary>The two bands have to be at least this far apart to mean anything.</summary>
    private const int MinSpread = 8;
    /// <summary>How much of the chart a band aims to cover.</summary>
    /// <remarks>
    /// Velocities land on a handful of discrete values, so a percentile falls
    /// *inside* a run of identical notes: cutting at the eightieth marked a
    /// median of 35% of each chart and up to 79%, which distinguishes nothing.
    /// The band is grown one distinct value at a time instead, and stops as soon
    /// as it has enough.
    /// </remarks>
    private const int TargetShare = 18;

    /// <summary>A band may overshoot the target up to here, but no further.</summary>
    /// <remarks>
    /// Without this a quantised chart gets a starved band. 鬼火 has three levels
    /// -- 60% of its notes at 112, 24% at 96 -- so its soft band is either the
    /// 8.8% below 96 or the 33% below 112, with nothing in between; stopping at a
    /// flat cap left it marking a tenth of the piece and reading as though the
    /// feature were off. Allowing one overshoot past the target reaches the real
    /// division, and the hard limit still refuses a band that would swallow the
    /// chart's own normal level.
    /// </remarks>
    private const int HardShare = 40;

    /// <summary>一個力度值要佔這麼多，才算得上是這首曲子的一「階」。</summary>
    /// <remarks>
    /// 上面那條 40% 的煞車假設力度是連續的：最高的那一階太大，就代表它沒有在區
    /// 分什麼。但有一種譜面它判錯 —— **力度被量化成兩階，而兩階都超過 40%**。
    ///
    /// 瑠璃の鳥就是：1584 顆音符裡 802 顆是 80、658 顆是 96，兩隻手都同時用這兩
    /// 個值（所以不是「右手旋律左手伴奏」那種固定差）。玩家聽到的強弱就是這兩階
    /// 的落差，但 Edge 從上往下收到 101 就撞到 96 那 658 顆（一加超過 40%），從下
    /// 往上收到 78 就撞到 80 那 802 顆 —— 兩側都停在大片之外，最後只標到中間零星
    /// 的裝飾音：強 3%、弱 2%，整首聽起來明明有強弱，畫面上卻幾乎什麼都沒有。
    ///
    /// 這種譜面的分界不在某一階**裡面**，而在兩階**之間**。
    /// </remarks>
    private const int PlateauShare = 25;

    private static int strongFrom = int.MaxValue;
    private static int weakTo = int.MinValue;
    private static float middle;
    private static float halfSpan = 1f;

    /// <summary>This piece's ordinary level -- its own median, not the library's.</summary>
    public static float Middle => middle;

    /// <summary>
    /// How far from <see cref="Middle"/> counts as fully loud or fully soft.
    /// </summary>
    /// <remarks>
    /// Taken from whichever band exists rather than from a fixed number, so the
    /// wash reaches full colour exactly where a note would have been marked. A
    /// piece with only a soft band still scales against its own soft edge.
    /// </remarks>
    public static float HalfSpan => halfSpan;

    /// <summary>False when this chart carries no usable dynamics.</summary>
    public static bool Measured { get; private set; }

    public static bool IsStrong(int velocity) => Measured && velocity >= strongFrom;
    public static bool IsWeak(int velocity) => Measured && velocity > 0 && velocity <= weakTo;

    /// <summary>這首曲子自己的三段力度。</summary>
    public enum Band
    {
        Soft,
        Normal,
        Loud,
    }

    /// <summary>
    /// 一個力度落在這首曲子的哪一段。
    /// </summary>
    /// <remarks>
    /// 用的是和音符外殼**完全同一組**界線。彈的人看到的是殼，被評的也該是殼說的
    /// 那件事 —— 兩邊各判一次的話，畫面上沒有殼的音符還是可能扣到分。
    /// </remarks>
    public static Band Classify(int velocity)
    {
        if (IsStrong(velocity)) return Band.Loud;
        if (IsWeak(velocity)) return Band.Soft;
        return Band.Normal;
    }

    /// <summary>True once this song has reported its first shell to the log.</summary>
    public static bool ShellReported { get; set; }

    /// <summary>Reads one chart's own loud and soft. Safe to call with null.</summary>
    public static void Prepare(Chart chart)
    {
        Prepare(chart != null ? chart.notes : null, null);
    }

    /// <summary>
    /// Measures only <paramref name="notes"/> — for a chart where just part of it
    /// is about dynamics. The tutorial's full course is the case: every other
    /// lesson's velocities were squeezed into a narrow range, so over the whole
    /// course the extremes were too common to count as loud or soft and nothing
    /// was measured, which switched the dynamics lesson's effects off entirely.
    /// </summary>
    public static void Prepare(List<NoteData> notes, string scope)
    {
        ShellReported = false;
        Measured = false;
        strongFrom = int.MaxValue;
        weakTo = int.MinValue;

        int total = notes != null ? notes.Count : 0;
        int sampled = Measure(notes);

        // 一首曲子一行。這個功能查了兩輪都卡在「不知道是沒資料還是沒畫出來」，
        // 這一行把資料那一半講死，剩下的就只剩繪製。
        Debug.Log($"[VelocityBands]{(scope != null ? " scope=" + scope : "")} notes={total} withVelocity={sampled} " +
            $"weak<={(weakTo == int.MinValue ? "--" : weakTo.ToString())} " +
            $"strong>={(strongFrom == int.MaxValue ? "--" : strongFrom.ToString())} " +
            $"measured={Measured}");
    }

    /// <summary>Does the measuring; returns how many notes carried a velocity.</summary>
    private static int Measure(List<NoteData> notes)
    {
        if (notes == null || notes.Count == 0) return 0;

        var sample = new List<int>(notes.Count);
        for (int i = 0; i < notes.Count; i++)
        {
            NoteData note = notes[i];
            if (note == null || note.velocity <= 0) continue;
            sample.Add(Mathf.Clamp(note.velocity, 1, 127));
        }

        // 只有零星幾顆帶力度的譜面，那幾顆多半是雜訊而不是表情。
        if (sample.Count < MinSample || sample.Count * 2 < notes.Count) return sample.Count;

        sample.Sort();
        int target = Mathf.Max(1, sample.Count * TargetShare / 100);
        int hard = Mathf.Max(1, sample.Count * HardShare / 100);

        // 從最高的值往下收，收到「再多收一階就超過上限」為止。收不到任何一階
        // 就代表這首曲子的最高力度本身就佔了一大片 —— 那個「強」沒有在區分
        // 什麼，這一側就不給界線。
        int loud = Edge(sample, target, hard, true);
        int soft = Edge(sample, target, hard, false);

        // 兩側加起來還不到一個 band 的量，就是兩邊都撞到大片的那種譜面。這時候
        // 改切在兩階之間 —— 而不是把「幾乎沒有強弱」當成結論。
        if (Covered(sample, loud, soft) < target) SplitBetweenPlateaus(sample, ref loud, ref soft);

        bool hasLoud = loud > 0;
        bool hasSoft = soft > 0;
        if (hasLoud && hasSoft && loud - soft < MinSpread) return sample.Count;
        if (!hasLoud && !hasSoft) return sample.Count;

        strongFrom = hasLoud ? loud : int.MaxValue;
        weakTo = hasSoft ? soft : int.MinValue;

        middle = sample[sample.Count / 2];
        float reach = 0f;
        if (hasLoud) reach = Mathf.Max(reach, loud - middle);
        if (hasSoft) reach = Mathf.Max(reach, middle - soft);
        halfSpan = Mathf.Max(1f, reach);

        Measured = true;
        return sample.Count;
    }

    /// <summary>
    /// Grows a band inward one distinct velocity at a time: it stops once it has
    /// <paramref name="target"/> notes, and while short of that it will take one
    /// more value even if that overshoots -- but never past <paramref name="hard"/>.
    /// -1 when even the first value is too common to be exceptional.
    /// </summary>
    /// <summary>目前這組界線一共標到幾顆。</summary>
    private static int Covered(List<int> sorted, int loud, int soft)
    {
        int count = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (loud > 0 && sorted[i] >= loud) count++;
            else if (soft > 0 && sorted[i] <= soft) count++;
        }
        return count;
    }

    /// <summary>
    /// 把分界移到最大的兩「階」之間：下面那階是弱，上面那階是強。
    /// </summary>
    /// <remarks>
    /// 只在兩階都夠大（各佔 <see cref="PlateauShare"/> 以上）、而且彼此相距夠遠
    /// （<see cref="MinSpread"/>）的時候才動手。少了任一個條件，這就只是一首恰好
    /// 有個常見力度的普通譜面，原本的 Edge 已經給了正確的答案。
    ///
    /// 兩階之間那些零星的值仍然是「普通」—— 它們既不是這首曲子的強，也不是弱。
    /// </remarks>
    private static void SplitBetweenPlateaus(List<int> sorted, ref int loud, ref int soft)
    {
        int need = Mathf.Max(1, sorted.Count * PlateauShare / 100);
        int firstValue = 0, firstCount = 0, secondValue = 0, secondCount = 0;

        int i = 0;
        while (i < sorted.Count)
        {
            int value = sorted[i];
            int run = 0;
            while (i < sorted.Count && sorted[i] == value) { run++; i++; }
            if (run > firstCount)
            {
                secondValue = firstValue; secondCount = firstCount;
                firstValue = value; firstCount = run;
            }
            else if (run > secondCount)
            {
                secondValue = value; secondCount = run;
            }
        }

        if (firstCount < need || secondCount < need) return;
        int high = Mathf.Max(firstValue, secondValue);
        int low = Mathf.Min(firstValue, secondValue);
        if (high - low < MinSpread) return;

        loud = high;
        soft = low;
    }

    private static int Edge(List<int> sorted, int target, int hard, bool fromTop)
    {
        int edge = -1;
        int taken = 0;
        int i = fromTop ? sorted.Count - 1 : 0;

        while (fromTop ? i >= 0 : i < sorted.Count)
        {
            int value = sorted[i];
            int run = 0;
            while (fromTop ? (i >= 0 && sorted[i] == value) : (i < sorted.Count && sorted[i] == value))
            {
                run++;
                i += fromTop ? -1 : 1;
            }
            if (taken >= target) break;
            if (taken + run > hard) break;
            taken += run;
            edge = value;
        }
        return edge;
    }
}
