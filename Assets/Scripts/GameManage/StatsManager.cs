using System;
using UnityEngine;
using Judgment;

public class StatsManager : MonoBehaviour
{
    private static StatsManager _instance;
    public static StatsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var existing = FindFirstObjectByType<StatsManager>();
                if (existing != null) _instance = existing;
                else
                {
                    var go = new GameObject("StatsManager");
                    _instance = go.AddComponent<StatsManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    // exposed counts for compatibility / inspection
    public int perfectCount = 0;
    public int greatCount = 0;
    public int goodCount = 0;
    public int missCount = 0;
    public int failCount = 0;
    public int currentCombo = 0;
    public int maxCombo = 0;
    public int currentScore = 0;
    public int fastCount = 0;
    public int lateCount = 0;
    /// <summary>演奏會模式：打中（非 Miss）的音符數。誤觸率、多鍵率都以它為分母。</summary>
    public int recitalNoteCount = 0;
    /// <summary>
    /// 演奏會模式：同一幀內用 3 個以上的鍵按同一顆音符的次數（本家的 over3key，
    /// 評「旋律」那一項）。
    /// </summary>
    public int recitalOver3KeyCount = 0;
    /// <summary>真的被認定成誤觸、而且出了聲的按鍵數。</summary>
    public int recitalMistouchCount = 0;
    /// <summary>又準又在中心格的音符數。分數上它就是 Perfect，只是值得有名字。</summary>
    public int recitalPreciseCount = 0;
    /// <summary>有機會拿 PRECISE 的音符。分母沒有的話，分子講不出任何事。</summary>
    public int recitalPreciseTotal = 0;
    /// <summary>踏板狀態正確的音符數，以及有被量到踏板的音符數。</summary>
    public int recitalPedalCorrect = 0;
    public int recitalPedalTotal = 0;
    /// <summary>強弱判得出來的音符，以及其中彈對的。</summary>
    public int recitalDynamicCorrect = 0;
    public int recitalDynamicTotal = 0;
    private double rawTimingOffsetSumMs = 0.0;
    private int rawTimingOffsetCount = 0;
    private float rawTimingOffsetMinMs = float.PositiveInfinity;
    private float rawTimingOffsetMaxMs = float.NegativeInfinity;
    private bool rawTimingOffsetExtremesStale = false;
    // Widest judgment window is goodMs (150). 200 leaves headroom without
    // silently folding real outliers into the edge bins.
    private const int RawOffsetHistogramRange = 200;
    private const int RawOffsetHistogramBins = RawOffsetHistogramRange * 2 + 1;
    private readonly int[] rawTimingOffsetHistogram = new int[RawOffsetHistogramBins];

    [Header("Long-note scoring")]
    [SerializeField, Range(0f, 1f), Tooltip("Score value of one sustain tick (a hold's per-beat tick, or a Trill slot after its head) relative to an ordinary note. Combo and judgment tallies are NOT affected — only the share of the 1,000,000 that long notes claim. 1 = the old length-proportional behaviour.")]
    private float sustainTickScoreWeight = 0.25f;

    private const int MaxScoreValue = 1_000_000;
    private static readonly decimal MaxScoreValueDecimal = 1_000_000m;
    private static readonly decimal PrecisionStep = 0.001m;
    private int totalJudgments = 0;
    private int expectedJudgments = 0;
    // Score is shared out per unit of WEIGHT, not per judgment. Sustain ticks carry
    // a fraction of a unit, so a 4-second hold no longer scores like 40 taps while
    // still producing the same 40 combo ticks.
    private decimal expectedScoreWeight = 0m;
    private decimal accumulatedScoreWeight = 0m;
    private decimal perJudgmentUnit = 0m;
    private decimal totalScoreRaw = 0m;
    private decimal totalScoreDecimal = 0m;

    private decimal SustainTickWeight => (decimal)Mathf.Clamp01(sustainTickScoreWeight);

    // 新手教學的示範段：判定照常跑（聲音、特效、音符消失），但不留下任何統計。
    // 用計數不用布林：示範音符的判定可能巢狀進另一顆的處理裡。
    private int muteDepth;

    /// <summary>接下來的記錄全部略過，直到對應的 <see cref="PopMute"/>。</summary>
    public void PushMute() { muteDepth++; }

    public void PopMute() { if (muteDepth > 0) muteDepth--; }

    public bool Muted => muteDepth > 0;

    public void Increment(JudgmentResult res)
    {
        IncrementMany(res, 1, 1m);
    }

    /// <summary>
    /// Record one judgment, optionally as a long-note sustain tick that claims only
    /// <see cref="sustainTickScoreWeight"/> of a normal note's score share.
    /// </summary>
    public void Increment(JudgmentResult res, bool isSustainTick)
    {
        IncrementMany(res, 1, isSustainTick ? SustainTickWeight : 1m);
    }

    /// <summary>
    /// Record one judgment whose share of the 1,000,000 is scaled by
    /// <paramref name="itemWeightScale"/>.
    /// </summary>
    /// <remarks>
    /// 演奏會模式用的。那個模式下一顆音符值多少，除了判定以外還看按鍵落在
    /// 音符的哪一格——中間那格拿滿，偏掉的仍然成立但拿不到全部。縮放的是
    /// 「這一項的份量」，不是判定本身，所以連段、判定統計、fast/late 全部
    /// 不受影響，滿分依然是 1,000,000。
    /// </remarks>
    public void Increment(JudgmentResult res, bool isSustainTick, decimal itemWeightScale)
    {
        if (itemWeightScale < 0m) itemWeightScale = 0m;
        IncrementMany(res, 1, (isSustainTick ? SustainTickWeight : 1m) * itemWeightScale);
    }

    public void IncrementMany(JudgmentResult res, int count)
    {
        IncrementMany(res, count, 1m);
    }

    public void IncrementMany(JudgmentResult res, int count, decimal itemWeight)
    {
        if (count <= 0 || Muted) return;
        if (itemWeight < 0m) itemWeight = 0m;
        try
        {
            switch (res)
            {
                case JudgmentResult.Perfect: perfectCount += count; break;
                case JudgmentResult.Great: greatCount += count; break;
                case JudgmentResult.Good: goodCount += count; break;
                case JudgmentResult.Miss: missCount += count; break;
                case JudgmentResult.Fail: failCount += count; break;
            }

            bool addToCombo = res == JudgmentResult.Perfect || res == JudgmentResult.Great || res == JudgmentResult.Good;
            if (addToCombo)
            {
                currentCombo += count;
                if (currentCombo > maxCombo)
                {
                    maxCombo = currentCombo;
                }
            }
            else
            {
                currentCombo = 0;
            }

            totalJudgments += count;

            decimal weight = GetScoreWeight(res) * itemWeight;
            if (weight < 0m) weight = 0m;
            accumulatedScoreWeight += weight * count;

            if (perJudgmentUnit > 0m && expectedScoreWeight > 0m)
            {
                totalScoreRaw += perJudgmentUnit * weight * count;
                totalScoreDecimal = CeilToPrecision(totalScoreRaw, PrecisionStep);

                if (totalJudgments >= expectedJudgments && totalScoreRaw >= MaxScoreValueDecimal)
                {
                    totalScoreRaw = MaxScoreValueDecimal;
                    totalScoreDecimal = MaxScoreValueDecimal;
                }
            }
            else
            {
                decimal denominator = expectedScoreWeight > 0m
                    ? expectedScoreWeight
                    : Math.Max(1, totalJudgments);
                decimal normalized = SafeClamp01(denominator > 0m ? accumulatedScoreWeight / denominator : 0m);
                totalScoreRaw = normalized * MaxScoreValueDecimal;
                totalScoreDecimal = CeilToPrecision(totalScoreRaw, PrecisionStep);
            }

            if (totalScoreDecimal > MaxScoreValueDecimal)
            {
                totalScoreDecimal = MaxScoreValueDecimal;
            }

            currentScore = (int)Math.Min(MaxScoreValue, DecimalCeilToInt(totalScoreDecimal));
        }
        catch { }
    }

    /// <summary>
    /// Move one already-counted judgment from <paramref name="from"/> to
    /// <paramref name="to"/>, and its timing sample from
    /// <paramref name="fromOffsetMs"/> to <paramref name="toOffsetMs"/>.
    /// </summary>
    /// <remarks>
    /// Used when a note turns out to have been claimed by the wrong press: the
    /// first press inside a note's window takes it, but a closer one can arrive
    /// while the window is still open. Nothing about the note count changes, so
    /// <see cref="totalJudgments"/> and the combo are deliberately untouched —
    /// only which bucket the one judgment sits in, and what it was worth.
    ///
    /// Score is a plain weighted accumulation with no combo multiplier, so
    /// swapping the weight is exact rather than an approximation.
    /// </remarks>
    public void ReviseJudgment(JudgmentResult from, JudgmentResult to,
        float fromOffsetMs, float toOffsetMs, decimal itemWeight)
    {
        if (Muted) return;
        try
        {
            if (from != to)
            {
                switch (from)
                {
                    case JudgmentResult.Perfect: perfectCount = Math.Max(0, perfectCount - 1); break;
                    case JudgmentResult.Great: greatCount = Math.Max(0, greatCount - 1); break;
                    case JudgmentResult.Good: goodCount = Math.Max(0, goodCount - 1); break;
                    default: return; // only positive judgments are revisable
                }
                switch (to)
                {
                    case JudgmentResult.Perfect: perfectCount++; break;
                    case JudgmentResult.Great: greatCount++; break;
                    case JudgmentResult.Good: goodCount++; break;
                    default: return;
                }

                if (itemWeight < 0m) itemWeight = 0m;
                decimal delta = (GetScoreWeight(to) - GetScoreWeight(from)) * itemWeight;
                accumulatedScoreWeight += delta;
                if (accumulatedScoreWeight < 0m) accumulatedScoreWeight = 0m;

                if (perJudgmentUnit > 0m && expectedScoreWeight > 0m)
                {
                    totalScoreRaw += perJudgmentUnit * delta;
                    if (totalScoreRaw < 0m) totalScoreRaw = 0m;
                }
                else
                {
                    decimal denominator = expectedScoreWeight > 0m
                        ? expectedScoreWeight
                        : Math.Max(1, totalJudgments);
                    decimal normalized = SafeClamp01(
                        denominator > 0m ? accumulatedScoreWeight / denominator : 0m);
                    totalScoreRaw = normalized * MaxScoreValueDecimal;
                }

                totalScoreDecimal = CeilToPrecision(totalScoreRaw, PrecisionStep);
                if (totalScoreDecimal > MaxScoreValueDecimal) totalScoreDecimal = MaxScoreValueDecimal;
                currentScore = (int)Math.Min(MaxScoreValue, DecimalCeilToInt(totalScoreDecimal));
            }

            ReviseRawTimingOffset(fromOffsetMs, toOffsetMs);
            ReviseTimingDirection(fromOffsetMs, from, toOffsetMs, to);
        }
        catch { }
    }

    private void ReviseRawTimingOffset(float fromOffsetMs, float toOffsetMs)
    {
        if (rawTimingOffsetCount <= 0) return;
        if (float.IsNaN(fromOffsetMs) || float.IsInfinity(fromOffsetMs)) return;
        if (float.IsNaN(toOffsetMs) || float.IsInfinity(toOffsetMs)) return;

        rawTimingOffsetSumMs += toOffsetMs - fromOffsetMs;

        int fromBin = Mathf.Clamp(
            Mathf.RoundToInt(fromOffsetMs) + RawOffsetHistogramRange, 0, RawOffsetHistogramBins - 1);
        int toBin = Mathf.Clamp(
            Mathf.RoundToInt(toOffsetMs) + RawOffsetHistogramRange, 0, RawOffsetHistogramBins - 1);
        if (rawTimingOffsetHistogram[fromBin] > 0) rawTimingOffsetHistogram[fromBin]--;
        rawTimingOffsetHistogram[toBin]++;
        // A retracted sample can be the one that set an extreme, so min/max are
        // no longer monotone and get rebuilt from the histogram. That costs the
        // sub-millisecond precision of the tracked values; the shape and median
        // read off the same bins either way.
        rawTimingOffsetExtremesStale = true;
    }

    private void ReviseTimingDirection(float fromOffsetMs, JudgmentResult fromResult,
        float toOffsetMs, JudgmentResult toResult)
    {
        bool countedBefore = CountsForFastLate(fromOffsetMs, fromResult);
        bool countsAfter = CountsForFastLate(toOffsetMs, toResult);
        if (countedBefore)
        {
            if (fromOffsetMs < 0f) fastCount = Math.Max(0, fastCount - 1);
            else lateCount = Math.Max(0, lateCount - 1);
        }
        if (countsAfter)
        {
            if (toOffsetMs < 0f) fastCount++;
            else lateCount++;
        }
    }

    private static bool CountsForFastLate(float offsetMs, JudgmentResult result)
    {
        if (result != JudgmentResult.Great && result != JudgmentResult.Good) return false;
        return Mathf.Abs(offsetMs) > 0.0001f;
    }

    private void RebuildRawTimingExtremes()
    {
        rawTimingOffsetExtremesStale = false;
        rawTimingOffsetMinMs = float.PositiveInfinity;
        rawTimingOffsetMaxMs = float.NegativeInfinity;
        for (int i = 0; i < RawOffsetHistogramBins; i++)
        {
            if (rawTimingOffsetHistogram[i] <= 0) continue;
            float ms = i - RawOffsetHistogramRange;
            if (ms < rawTimingOffsetMinMs) rawTimingOffsetMinMs = ms;
            if (ms > rawTimingOffsetMaxMs) rawTimingOffsetMaxMs = ms;
        }
    }

    public void ResetStats()
    {
        muteDepth = 0;
        recitalNoteCount = 0;
        recitalOver3KeyCount = 0;
        recitalMistouchCount = 0;
        recitalPreciseCount = 0;
        recitalPreciseTotal = 0;
        recitalPedalCorrect = 0;
        recitalPedalTotal = 0;
        recitalDynamicCorrect = 0;
        recitalDynamicTotal = 0;
        perfectCount = 0;
        greatCount = 0;
        goodCount = 0;
        missCount = 0;
        failCount = 0;
        currentCombo = 0;
        maxCombo = 0;
        currentScore = 0;
        fastCount = 0;
        lateCount = 0;
        rawTimingOffsetSumMs = 0.0;
        rawTimingOffsetCount = 0;
        rawTimingOffsetMinMs = float.PositiveInfinity;
        rawTimingOffsetMaxMs = float.NegativeInfinity;
        rawTimingOffsetExtremesStale = false;
        System.Array.Clear(rawTimingOffsetHistogram, 0, rawTimingOffsetHistogram.Length);
        totalJudgments = 0;
        accumulatedScoreWeight = 0m;
        totalScoreRaw = 0m;
        totalScoreDecimal = 0m;
        expectedJudgments = 0;
        expectedScoreWeight = 0m;
        perJudgmentUnit = 0m;
    }

    public (int perfect, int great, int good, int miss, int fail) GetStats()
    {
        return (perfectCount, greatCount, goodCount, missCount, failCount);
    }

    public (int perfect, int great, int good, int miss, int fail, int combo, int maxCombo, int score) GetStatsWithCombo()
    {
        return (perfectCount, greatCount, goodCount, missCount, failCount, currentCombo, maxCombo, currentScore);
    }

    public (int fast, int late) GetTimingStats()
    {
        return (fastCount, lateCount);
    }

    public void RecordRawTimingOffset(float offsetMs)
    {
        if (Muted || float.IsNaN(offsetMs) || float.IsInfinity(offsetMs)) return;
        rawTimingOffsetSumMs += offsetMs;
        rawTimingOffsetCount++;
        if (offsetMs < rawTimingOffsetMinMs) rawTimingOffsetMinMs = offsetMs;
        if (offsetMs > rawTimingOffsetMaxMs) rawTimingOffsetMaxMs = offsetMs;
        // 1 ms bins over [-RawOffsetHistogramRange, +RawOffsetHistogramRange].
        // Fixed array, no allocation, and it is enough to recover the median and
        // the shape — which the mean alone cannot distinguish (a distribution
        // centred at -30 and one centred at 0 with a heavy early tail from
        // mistouches produce the same average).
        int bin = Mathf.Clamp(
            Mathf.RoundToInt(offsetMs) + RawOffsetHistogramRange,
            0, RawOffsetHistogramBins - 1);
        rawTimingOffsetHistogram[bin]++;
    }

    public (int count, float averageMs, float minMs, float maxMs) GetRawTimingOffsetStats()
    {
        if (rawTimingOffsetCount <= 0) return (0, 0f, 0f, 0f);
        if (rawTimingOffsetExtremesStale) RebuildRawTimingExtremes();
        return (rawTimingOffsetCount,
            (float)(rawTimingOffsetSumMs / rawTimingOffsetCount),
            rawTimingOffsetMinMs, rawTimingOffsetMaxMs);
    }

    /// <summary>
    /// Median raw timing offset (ms), recovered from the 1 ms histogram.
    /// Unlike the mean this is barely moved by a handful of very early
    /// mistouch samples, so comparing the two separates "the whole
    /// distribution is shifted" from "it is centred but has an early tail".
    /// </summary>
    public float GetRawTimingOffsetMedianMs()
    {
        if (rawTimingOffsetCount <= 0) return 0f;
        int target = rawTimingOffsetCount / 2;
        int running = 0;
        for (int i = 0; i < RawOffsetHistogramBins; i++)
        {
            running += rawTimingOffsetHistogram[i];
            if (running > target) return i - RawOffsetHistogramRange;
        }
        return 0f;
    }

    /// <summary>
    /// Compact distribution summary: sample counts in 25 ms buckets from
    /// -150 ms to +150 ms, so the log line shows the shape, not just moments.
    /// </summary>
    public string GetRawTimingOffsetShape()
    {
        if (rawTimingOffsetCount <= 0) return string.Empty;
        var sb = new System.Text.StringBuilder(96);
        for (int bucket = -150; bucket < 150; bucket += 25)
        {
            int sum = 0;
            for (int ms = bucket; ms < bucket + 25; ms++)
            {
                int i = ms + RawOffsetHistogramRange;
                if ((uint)i < RawOffsetHistogramBins) sum += rawTimingOffsetHistogram[i];
            }
            if (sb.Length > 0) sb.Append('/');
            sb.Append(sum);
        }
        return sb.ToString();
    }

    /// <summary>一顆音符在演奏會模式下被打中了。</summary>
    public void RecordRecitalNote()
    {
        if (Muted) return;
        recitalNoteCount++;
    }

    /// <summary>一顆音符被同一幀內 3 個以上的鍵按下（本家的 over3key）。</summary>
    public void RecordOver3Key()
    {
        if (Muted) return;
        recitalOver3KeyCount++;
    }

    /// <summary>
    /// One key that belonged to no note. Counted where the game finally decides
    /// so -- after the input frame has had its chance to absorb it as a brush.
    /// </summary>
    public void RecordMistouch()
    {
        if (Muted) return;
        recitalMistouchCount++;
    }

    /// <summary>
    /// 一顆**有機會**拿 PRECISE 的音符，以及有沒有拿到。
    /// </summary>
    /// <remarks>
    /// 分母是**拿到 JUST 的那些**：PRECISE 是 JUST 之上再加條件（中心格 + 強弱
    /// 對），所以只拿到 GREAT 的音符不在分母裡 —— 它們沒有輸在位置或力度上，是
    /// 輸在時間上，而時間有它自己那一列。
    ///
    /// 打歪了、強弱錯了但時間準的那些**在**分母裡，因為那正是沒拿到的原因。
    ///
    /// 把分母也記下來才有意義：單獨一個「PRECISE 37」在 40 顆的譜上是接近完美，
    /// 在 1400 顆的譜上是幾乎沒拿到。
    /// </remarks>
    public void RecordPreciseAttempt(bool earned)
    {
        if (Muted) return;
        recitalPreciseTotal++;
        if (earned) recitalPreciseCount++;
    }

    public (int earned, int total) GetRecitalPreciseStats()
    {
        return (recitalPreciseCount, recitalPreciseTotal);
    }

    public void RecordPrecise()
    {
        if (Muted) return;
        recitalPreciseCount++;
    }

    /// <summary>
    /// Records whether the pedal was where the chart wanted it when this note
    /// was played.
    /// </summary>
    /// <remarks>
    /// Per note, not per pedal span: what a listener hears is notes that rang on
    /// and notes that were cut off, and a span that was half a beat late damages
    /// exactly the notes it covered. Counting notes rather than spans also makes
    /// the mark comparable with the other three, which are all per note.
    /// </remarks>
    public void RecordPedal(bool correct)
    {
        if (Muted) return;
        recitalPedalTotal++;
        if (correct) recitalPedalCorrect++;
    }

    /// <summary>
    /// 一顆**判得出強弱**的音符，以及玩家有沒有彈對。
    /// </summary>
    /// <remarks>
    /// 判不出來的不要送進來。沒有力度資料的譜面、沒有還原出力度的音符、沒有力度
    /// 的輸入裝置 —— 那三種情況下玩家沒有做錯任何事，把它們算進分母等於因為資料
    /// 缺席而扣人分數。這和踏板那一項是同一條規則。
    /// </remarks>
    public void RecordDynamic(bool correct)
    {
        if (Muted) return;
        recitalDynamicTotal++;
        if (correct) recitalDynamicCorrect++;
    }

    public (int notes, int over3Key, int mistouch, int precise) GetRecitalStats()
    {
        return (recitalNoteCount, recitalOver3KeyCount, recitalMistouchCount,
            recitalPreciseCount);
    }

    public (int correct, int total) GetRecitalDynamicStats()
    {
        return (recitalDynamicCorrect, recitalDynamicTotal);
    }

    public (int correct, int total) GetRecitalPedalStats()
    {
        return (recitalPedalCorrect, recitalPedalTotal);
    }

    public int CurrentCombo => currentCombo;
    public int MaxCombo => maxCombo;
    public int CurrentScore => currentScore;

    public int TotalJudgments => totalJudgments;
    public int ExpectedJudgments => expectedJudgments;

    public void ConfigureExpectedJudgments(int expectedCount)
    {
        ConfigureExpectedJudgments(expectedCount, expectedCount);
    }

    /// <summary>
    /// <paramref name="expectedWeightTotal"/> must be the sum of every judgment's
    /// score weight for a full-Perfect run, using the SAME sustain weighting the
    /// runtime applies. If the two disagree, a perfect play cannot land on exactly
    /// 1,000,000.
    /// </summary>
    private void ConfigureExpectedJudgments(int expectedCount, decimal expectedWeightTotal)
    {
        expectedJudgments = Mathf.Max(0, expectedCount);
        expectedScoreWeight = expectedWeightTotal > 0m ? expectedWeightTotal : 0m;
        perJudgmentUnit = expectedScoreWeight > 0m
            ? MaxScoreValueDecimal / expectedScoreWeight
            : 0m;
        totalScoreRaw = 0m;
        totalScoreDecimal = 0m;
    }

    public void ConfigureExpectedJudgments(Chart chart)
    {
        if (chart == null)
        {
            ConfigureExpectedJudgments(0);
            return;
        }

        int expected = 0;
        decimal expectedWeight = 0m;
        decimal sustain = SustainTickWeight;
        // Determine BPM to compute eighth-note duration (ms). Prefer chart header, fallback to Conductor if available.
        float bpm = 120f;
        try
        {
            if (chart.first_bpm > 0f) bpm = chart.first_bpm;
            else if (GameManager.Instance != null && GameManager.Instance.Conductor != null) bpm = GameManager.Instance.Conductor.bpm;
        }
        catch { }

        float eighthMs = Mathf.Max(1f, 30000f / Mathf.Max(0.0001f, bpm));

        if (chart.notes != null)
        {
            for (int i = 0; i < chart.notes.Count; i++)
            {
                var note = chart.notes[i];
                if (note == null) continue;
                // 教學示範段自己彈、不計分，滿分只算玩家要打的那些。
                if (note.tutorialDemo) continue;
                bool isTrill = note.note_type == 64 ||
                    string.Equals(note.type, "trill", StringComparison.OrdinalIgnoreCase);
                if (isTrill)
                {
                    // Trill is not one aggregate judgment: every 0.125-second
                    // input window contributes one result to score/achievement.
                    int trillTicks = Mathf.Max(1,
                        Mathf.CeilToInt(Mathf.Max(1f, note.endTime - note.startTime) / 125f));
                    expected += trillTicks;
                    // Slot 0 is the real head judgment (JudgmentManager awards it via
                    // the protected head payload, not AwardTrillTick), so it keeps full
                    // weight; every later slot is a sustain tick.
                    expectedWeight += 1m + (trillTicks - 1) * sustain;
                    continue;
                }
                expected++; // head (or single judgment)
                expectedWeight += 1m;
                if (CountsAsDualJudgment(note))
                {
                    expected++; // tail / dual count
                    expectedWeight += 1m;
                }

                // Only a genuine HOLD (note_type 2 / type "hold") generates per-beat extra judgments.
                // A STACCATO (note_type 3 / type "staccato"/"stac") is head+tail only: a correctly
                // played staccato is released immediately and never awards extras at runtime, so
                // counting length-based extras here left a full-Perfect run unable to reach max score.
                try
                {
                    bool holdWithExtras = false;
                    if (!string.IsNullOrEmpty(note.type))
                    {
                        if (string.Equals(note.type, "hold", StringComparison.OrdinalIgnoreCase)) holdWithExtras = true;
                        else if (int.TryParse(note.type, out int parsed) && parsed == 2) holdWithExtras = true;
                    }
                    if (!holdWithExtras && note.note_type == 2) holdWithExtras = true;

                    if (holdWithExtras)
                    {
                        try
                        {
                            var info = HoldJudgment.ComputeExtraInfo(note, null);
                            expected += info.maxExtra;
                            // Head and tail above already claimed full weight; the
                            // length-driven ticks are what gets discounted.
                            expectedWeight += info.maxExtra * sustain;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        ConfigureExpectedJudgments(expected, expectedWeight);
    }

    /// <summary>
    /// Returns the achievement percentage (0-100) defined as currentScore / maxPossibleScore (all-perfect), rounded to nearest integer.
    /// This is intentionally derived from the displayed score ratio so score and achievement stay unified.
    /// </summary>
    public int GetAchievementPercent()
    {
        try
        {
            decimal percent = GetAchievementPercentDecimal();
            int rounded = (int)System.Math.Round(percent, MidpointRounding.AwayFromZero);
            if (rounded < 0) rounded = 0;
            if (rounded > 100) rounded = 100;
            return rounded;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Returns the achievement percentage as a decimal (0.0 - 100.0) with full precision.
    /// This uses the same algorithm as GetAchievementPercent but preserves fractional precision.
    /// </summary>
    public decimal GetAchievementPercentDecimal()
    {
        try
        {
            if (currentScore <= 0)
            {
                return 0m;
            }

            decimal percent = ((decimal)currentScore / MaxScoreValueDecimal) * 100m;
            if (percent < 0m) percent = 0m;
            if (percent > 100m) percent = 100m;
            return percent;
        }
        catch { return 0m; }
    }

    private static decimal GetScoreWeight(JudgmentResult result)
    {
        switch (result)
        {
            case JudgmentResult.Perfect:
                return 1m;
            case JudgmentResult.Great:
                return 0.9m;
            case JudgmentResult.Good:
                return 0.5m;
            case JudgmentResult.Miss:
            case JudgmentResult.Fail:
            default:
                return 0m;
        }
    }

    private static bool CountsAsDualJudgment(NoteData note)
    {
        if (note == null)
        {
            return false;
        }

        try
        {
            if (!string.IsNullOrEmpty(note.type))
            {
                if (string.Equals(note.type, "hold", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(note.type, "staccato", StringComparison.OrdinalIgnoreCase) || string.Equals(note.type, "stac", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (int.TryParse(note.type, out int parsedType) && (parsedType == 2 || parsedType == 3))
                {
                    return true;
                }
            }
        }
        catch { }

        if (note.note_type == 2 || note.note_type == 3)
        {
            return true;
        }

        return false;
    }

    public void RecordTimingOffset(float offsetMs)
    {
        if (Muted || float.IsNaN(offsetMs) || float.IsInfinity(offsetMs))
        {
            return;
        }

        if (Mathf.Abs(offsetMs) <= 0.0001f)
        {
            return;
        }

        if (offsetMs < 0f)
        {
            fastCount++;
        }
        else if (offsetMs > 0f)
        {
            lateCount++;
        }
    }

    /// <summary>
    /// Records FAST/SLOW only after the judgment has left the JUST/Perfect range.
    /// </summary>
    public void RecordTimingOffset(float offsetMs, JudgmentResult result)
    {
        if (result == JudgmentResult.Perfect) return;
        if (result != JudgmentResult.Great && result != JudgmentResult.Good) return;
        RecordTimingOffset(offsetMs);
    }

    private static decimal CeilToPrecision(decimal value, decimal precision)
    {
        if (precision <= 0m)
        {
            return value;
        }

        return Math.Ceiling(value / precision) * precision;
    }

    private static int DecimalCeilToInt(decimal value)
    {
        return (int)Math.Ceiling(value);
    }

    private static decimal SafeClamp01(decimal value)
    {
        if (value < 0m) return 0m;
        if (value > 1m) return 1m;
        return value;
    }
}
