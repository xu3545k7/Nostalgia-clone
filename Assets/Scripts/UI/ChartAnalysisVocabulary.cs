using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The words the game uses to describe a chart, and the levels at which each one
/// applies.
/// </summary>
/// <remarks>
/// One place on purpose.  The bookmark panel and the score-book overlay both
/// describe the same analysis, and when each kept its own copy of the list they
/// drifted apart: renaming the techniques in one left the other showing the old
/// words and missing the ones added since.
///
/// Every threshold is measured rather than guessed: the 80th percentile of the
/// library, or the 90th for the measures most charts score zero on. A word then
/// means "unusually much of this" and not merely "has some", and a tag that
/// fires for the median chart tells the player nothing.
///
/// The numbers below were re-measured over the library's 123 charts, 105 of
/// them carrying technique data. The first pass was taken when there were fewer,
/// and several had drifted a long way from the percentile they were supposed to
/// be: the hold threshold had become the 95th and one-sidedness the 90th, so
/// both words had quietly stopped being sayable.
/// </remarks>
public static class ChartAnalysisVocabulary
{
    // ---- 門檻 ----------------------------------------------------------------
    // 全部量自現行曲庫（Nostalgia-clone/UserSongs，123 張，其中 105 張帶技法資料）。
    // 常見項取第 80 百分位；多數譜為 0 的稀有項取第 90，否則「有一點」就會被當成
    // 「特別多」。這些是 const 而不是散落的字面值：同一個數字在描述、技法選字和
    // 雷達圖三個地方都要用，各寫一份就會各自漂走。

    public const float BurstThreshold = 3.21f;
    public const float StaminaThreshold = 155f;
    public const float HandSpeedThreshold = 32.2f;
    public const float ChordThreshold = 2.265f;
    /// <summary>Peak notes per second in one hand: chords taken at speed.</summary>
    public const float ChordRateThreshold = 36f;
    public const float HoldThreshold = 0.298f;
    public const float HandBiasThreshold = 0.101f;
    /// <summary>89% 的譜完全沒有手勢音符，所以用第 90 百分位。</summary>
    public const float SlideThreshold = 0.004f;
    public const float IndependenceThreshold = 0.121f;

    public const float LeapThreshold = 0.387f;
    /// <summary>
    /// Fast leaps as a share of every move, the same denominator as leaps.
    /// </summary>
    /// <remarks>
    /// This used to be measured against "moves taken that fast", which made it a
    /// conditional probability rather than an amount, and let a sparse chart
    /// with a handful of quick jumps outrank one that leaps at speed throughout.
    /// The measure changed with cache version 5, and this threshold with it:
    /// 0.151 under the old denominator, 0.080 under this one.
    /// </remarks>
    public const float FastLeapThreshold = 0.080f;
    public const float OctaveThreshold = 0.230f;
    public const float OctaveRunThreshold = 0.046f;
    public const float ArpeggioThreshold = 0.156f;
    public const float FingerRunThreshold = 0.124f;
    public const float FastRunThreshold = 0.043f;
    /// <summary>Doubled onsets inside fast passagework, as a share of all notes.</summary>
    public const float ThickRunThreshold = 0.0196f;
    public const float TrillThreshold = 0.029f;
    /// <summary>77% 為 0，取第 90 百分位。</summary>
    public const float RepeatThreshold = 0.013f;
    /// <summary>70% 為 0，取第 90 百分位。</summary>
    public const float HandCrossThreshold = 0.008f;

    /// <summary>One measure, with the level at which it stops being ordinary.</summary>
    public readonly struct Trait
    {
        public readonly string name;
        public readonly float value;
        public readonly float threshold;

        public Trait(string name, float value, float threshold)
        {
            this.name = name;
            this.value = value;
            this.threshold = threshold;
        }

        public bool Notable => value >= threshold;
        /// <summary>How far past ordinary, so the most distinctive traits rank first.</summary>
        public float Score => threshold > 0.0001f ? value / threshold : 0f;
    }

    /// <summary>
    /// The techniques the hands are asked for, as ratios that can be shown as
    /// percentages.
    /// </summary>
    public static void CollectTechniques(ChartAnalysis analysis, List<Trait> into)
    {
        if (analysis == null || into == null) return;

        // Finger independence comes from note lengths, so it is available even on
        // the charts that carry no pitch at all.
        into.Add(new Trait(Localize.T("手指獨立", "手指独立", "Independence"),
            analysis.independenceRatio, IndependenceThreshold));

        if (!analysis.hasTechniqueData) return;

        // Speed changes what a technique is.  A chart that takes its octaves and
        // its jumps at tempo is described by the fast name instead of the plain
        // one, so the two never spend two slots saying one thing.
        bool fastOctaves = analysis.octaveRunRatio >= OctaveRunThreshold;
        bool fastLeaps = analysis.fastLeapRatio >= FastLeapThreshold;
        bool fastRuns = analysis.fastRunRatio >= FastRunThreshold;

        into.Add(fastLeaps
            ? new Trait(Localize.T("高速跳躍", "高速跳跃", "Fast leaps"), analysis.fastLeapRatio, FastLeapThreshold)
            : new Trait(Localize.T("大跨度跳躍", "大跨度跳跃", "Leaps"), analysis.leapRatio, LeapThreshold));
        into.Add(fastOctaves
            ? new Trait(Localize.T("高速八度", "高速八度", "Fast octaves"), analysis.octaveRunRatio, OctaveRunThreshold)
            : new Trait(Localize.T("八度", "八度", "Octaves"), analysis.octaveRatio, OctaveThreshold));
        into.Add(new Trait(Localize.T("琶音", "琶音", "Arpeggios"), analysis.arpeggioRatio, ArpeggioThreshold));
        into.Add(fastRuns
            ? new Trait(Localize.T("高速運指", "高速运指", "Fast runs"), analysis.fastRunRatio, FastRunThreshold)
            : new Trait(Localize.T("音階跑動", "音阶跑动", "Scale runs"), analysis.fingerRunRatio, FingerRunThreshold));
        into.Add(new Trait(Localize.T("雙音跑動", "双音跑动", "Double-stop runs"),
            analysis.thickRunRatio, ThickRunThreshold));
        into.Add(new Trait(Localize.T("顫音", "颤音", "Trills"), analysis.trillRatio, TrillThreshold));
        into.Add(new Trait(Localize.T("同音反覆", "同音反复", "Repeated notes"), analysis.repeatRatio, RepeatThreshold));
        into.Add(new Trait(Localize.T("交叉手", "交叉手", "Hand crossing"), analysis.handCrossRatio, HandCrossThreshold));
    }

    /// <summary>
    /// One radar spoke: the value, and the three levels of the library it is
    /// read against.
    /// </summary>
    /// <remarks>
    /// A single threshold is not a common standard. The six measures have very
    /// different distributions -- measured over the 210 charts that carry
    /// technique data, a hold ratio of 0.40 is the 95th percentile while a peak
    /// hand rate of 33 is the 80th, and the composite reach and agility axes
    /// take a maximum of two and three ratios, so their "1.0" lands nearer the
    /// 65th. Scaled by one number each, the same distance from the centre meant
    /// something different on every spoke.
    ///
    /// Each axis instead carries the library's median, 80th and 95th
    /// percentile, and is drawn through them onto the same fixed radii. A ring
    /// then means one thing everywhere: the bright ring is "busier than four
    /// charts in five", on every spoke and on every song.
    /// </remarks>
    public readonly struct RadarAxis
    {
        /// <summary>Where the library's median sits, as a fraction of the spoke.</summary>
        public const float MedianRadius = 0.40f;
        /// <summary>Where the 80th percentile sits -- the ring worth naming.</summary>
        public const float NotableRadius = 0.70f;

        public readonly string name;
        public readonly float value;
        public readonly float median;
        public readonly float notable;
        public readonly float rare;

        public RadarAxis(string name, float value, float median, float notable, float rare)
        {
            this.name = name;
            this.value = value;
            this.median = median;
            this.notable = notable;
            this.rare = rare;
        }

        public bool Notable => value >= notable;

        /// <summary>How far out to plot, 0 at the centre and 1 at the outer ring.</summary>
        public float Fraction
        {
            get
            {
                if (value <= 0f) return 0f;
                if (value < median)
                    return MedianRadius * value / Mathf.Max(median, 0.0001f);
                if (value < notable)
                    return Mathf.Lerp(MedianRadius, NotableRadius,
                        (value - median) / Mathf.Max(notable - median, 0.0001f));
                if (value < rare)
                    return Mathf.Lerp(NotableRadius, 1f,
                        (value - notable) / Mathf.Max(rare - notable, 0.0001f));
                return 1f;
            }
        }
    }

    /// <summary>
    /// The six axes of the summary radar, each against the library's own spread.
    /// </summary>
    /// <remarks>
    /// Percentiles measured over the library, not estimated. Reach and agility
    /// are composites, so their anchors were re-measured after the thresholds
    /// they normalise by moved -- an aggregate shifts whenever any member does.
    ///
    /// Reach gathers leaps, octaves and hand crossing; agility gathers scale
    /// runs, arpeggios, trills and repeated notes. A spoke apiece would ask the
    /// same question several times over -- leaps and octaves are both "how far
    /// must the hand travel" -- and dilute each other into a polygon that always
    /// sits near the centre.
    ///
    /// Chord weight is measured from 1.0, not from 0: every chart has at least
    /// one note per onset, so counting from zero would give a chart with no
    /// chords at all a spoke nearly half way out.
    /// </remarks>
    public static void CollectRadar(ChartAnalysis analysis, List<RadarAxis> into)
    {
        if (analysis == null || into == null) return;

        // 前四軸的分位點量自全部 123 張譜，後兩軸只有帶技法資料的 105 張有值，
        // 所以分位點也量自那 105 張 —— 拿沒有資料的譜一起算會把中位數往下拉。
        into.Add(new RadarAxis(Localize.T("速度", "速度", "Speed"),
            analysis.peakHandRate, 19.0f, HandSpeedThreshold, 38.9f));
        into.Add(new RadarAxis(Localize.T("耐力", "耐力", "Stamina"),
            analysis.sustainedSeconds, 105.3f, StaminaThreshold, 230.2f));
        // 這一軸畫的是 ChordLoad 而不是每個發聲點幾顆音：厚度本身和難度無關，
        // 快慢才有關，而純粹的快已經是速度軸了。見 ChartAnalysis.ChordLoad。
        into.Add(new RadarAxis(Localize.T("和弦", "和弦", "Chords"),
            analysis.ChordLoad, 4f, 10f, 17f));
        into.Add(new RadarAxis(Localize.T("長押", "长按", "Holds"),
            analysis.HoldRatio, 0.196f, HoldThreshold, 0.450f));
        // 這一軸只收**快的**位移。一個慢慢移過去的大跳沒有難度可言，而
        // leapRatio / octaveRatio 是不分快慢的，所以那兩個留給標籤列去描述，
        // 雷達圖用它們的高速版本。
        into.Add(new RadarAxis(Localize.T("跨度", "跨度", "Reach"),
            Aggregate(Ratio(analysis.fastLeapRatio, FastLeapThreshold),
                      Ratio(analysis.octaveRunRatio, OctaveRunThreshold),
                      Ratio(analysis.handCrossRatio, HandCrossThreshold)),
            0.945f, 1.836f, 3.380f));
        into.Add(new RadarAxis(Localize.T("靈巧", "灵巧", "Agility"),
            Aggregate(Ratio(analysis.fingerRunRatio, FingerRunThreshold),
                      Ratio(analysis.arpeggioRatio, ArpeggioThreshold),
                      Ratio(analysis.trillRatio, TrillThreshold),
                      Ratio(analysis.repeatRatio, RepeatThreshold),
                      Ratio(analysis.thickRunRatio, ThickRunThreshold)),
            1.262f, 2.293f, 4.479f));
    }

    private static float Ratio(float value, float threshold)
    {
        return threshold > 0.0001f ? value / threshold : 0f;
    }

    /// <summary>
    /// Combines the members of one composite axis: close to the strongest, but
    /// a chart that does several of them gets credit for all of them.
    /// </summary>
    /// <remarks>
    /// A plain maximum threw away everything but the winner, so hand crossing --
    /// which 70% of charts never do at all -- could only ever show up on the
    /// radar by out-scoring leaps and octaves outright, and otherwise vanished.
    /// A sum is the opposite mistake: four ordinary techniques would then read
    /// as one extraordinary one.
    ///
    /// The cube norm sits between the two. One member at 1.5 gives 1.5; three
    /// members at 1.0 give 1.44, so breadth counts for something without
    /// drowning out depth.
    /// </remarks>
    private static float Aggregate(params float[] members)
    {
        float total = 0f;
        for (int i = 0; i < members.Length; i++)
        {
            float value = Mathf.Max(0f, members[i]);
            total += value * value * value;
        }

        return total > 0f ? Mathf.Pow(total, 1f / 3f) : 0f;
    }

    /// <summary>
    /// The strongest techniques with their numbers, whether or not they clear the
    /// threshold: "no leaps at all" is information too.
    /// </summary>
    public static string DescribeTechniques(ChartAnalysis analysis, int maximum = 3)
    {
        if (analysis == null) return string.Empty;

        var traits = new List<Trait>(9);
        CollectTechniques(analysis, traits);
        traits.Sort((a, b) => b.Score.CompareTo(a.Score));

        var text = new StringBuilder(96);
        int shown = 0;
        for (int i = 0; i < traits.Count && shown < maximum; i++)
        {
            if (traits[i].value < 0.005f) continue;
            if (shown > 0) text.Append("   ");
            text.Append(traits[i].name).Append(' ')
                .Append((traits[i].value * 100f).ToString("0")).Append('%');
            shown++;
        }
        return shown > 0 ? text.ToString() : Localize.T("無明顯技法", "无明显技法", "None");
    }

    /// <summary>
    /// A short read on what makes the chart hard, from the shape of its own
    /// numbers rather than from its authored level.  At most three, ranked by how
    /// far past ordinary each one is, so what comes out is what stands out.
    /// </summary>
    public static string DescribeCharacter(ChartAnalysis analysis)
    {
        if (analysis == null) return string.Empty;

        var traits = new List<Trait>(16)
        {
            new Trait(Localize.T("爆發", "爆发", "Burst"), analysis.Burstiness, BurstThreshold),
            // Endurance is time spent busy, not average business: a short sprint
            // and a six minute grind can share an average and feel nothing alike.
            new Trait(Localize.T("耐力", "耐力", "Stamina"), analysis.sustainedSeconds, StaminaThreshold),
            // Strikes per second in one hand, chords counted once, so this stays
            // a speed measure instead of restating chord weight.
            new Trait(Localize.T("單手極速", "单手极速", "Hand speed"), analysis.peakHandRate, HandSpeedThreshold),
            // 厚不等於難：慢速的六聲部聖詠在「每個發聲點幾顆音」上是滿分，彈起來
            // 卻不難。和弦一旦是快的，說的就是另一件事，所以換一個詞和一個量。
            analysis.peakChordRate >= ChordRateThreshold
                ? new Trait(Localize.T("高速和弦", "高速和弦", "Fast chords"),
                    analysis.peakChordRate, ChordRateThreshold)
                : new Trait(Localize.T("和弦厚重", "和弦厚重", "Chord-heavy"),
                    analysis.NotesPerOnset, ChordThreshold),
            new Trait(Localize.T("長押多", "长按多", "Hold-heavy"), analysis.HoldRatio, HoldThreshold),
            new Trait(Localize.T("偏一手", "偏一手", "One-sided"), analysis.HandBias, HandBiasThreshold),
            // Gesture notes are authored, not derived, and 89% of the library
            // has none at all, so the bar is the 90th percentile rather than
            // the 80th -- otherwise a single gesture would win the tag line.
            new Trait(Localize.T("滑音", "滑音", "Slides"), analysis.TechnicalRatio, SlideThreshold),
        };
        CollectTechniques(analysis, traits);
        traits.Sort((a, b) => b.Score.CompareTo(a.Score));

        var tags = new List<string>(3);
        for (int i = 0; i < traits.Count && tags.Count < 3; i++)
            if (traits[i].Notable) tags.Add(traits[i].name);

        if (tags.Count == 0) return Localize.T("平穩", "平稳", "Even");
        return string.Join("  ·  ", tags);
    }
}
