using UnityEngine;

/// <summary>
/// 考程的兩座階梯：練習室的<b>技階</b>十階，演奏廳的<b>檢定</b>八級。
/// </summary>
/// <remarks>
/// **為什麼是兩座而不是一座。** 兩者量的是不同的東西，而且用不同的方式量：技階問
/// 「撐不撐得住」（三首連奏、共用一份容錯、歸零就結束，完全沒有分數門檻），檢定問
/// 「彈得好不好」（兩首對比曲一定聽完，只看 <see cref="Judgment.RecitalAssessment"/>
/// 的四項，完全沒有存活條件）。一座階梯同時回答兩題的話，其中一題一定會變成另一題
/// 的函數。
///
/// **為什麼它們仍然是同一座梯子。** 檢定 N 級要求技階 ≥ N−2（見
/// <see cref="Grade.RequiredStage"/>）。一份你物理上撐不住的譜，談不上「彈得好」。
/// 這是兩者唯一的耦合，而且是單向的 —— 技階從不要求檢定。
///
/// **為什麼單位是「階」和「級」，不是「段」。** 段位是圍棋和武道的詞。這間房是音樂
/// 院，而音樂院用的是「檢定幾級」。兩個單位刻意不同字，因為玩家會同時持有兩個數
/// 字，而「三段」和「三級」念起來一樣。
///
/// **為什麼排序鍵永遠只排序、永不當門檻。** 量過這個曲庫：Lv1–3 那一帶的 26 份譜
/// 裡，達到 <see cref="ChartAnalysisVocabulary"/> 顯著門檻的顫音、八度、同音反覆、
/// 雙音跑動、耐力各是 <b>0 首</b> —— 技法在低階沒有供給。所以一到四階用的是「任何
/// 譜都一定有」的比例（密度／和音／長押／左右手差），技法名稱從供得起的地方（五階）
/// 才開始。拿技法當門檻的那一版，前四階永遠開不了課。
/// </remarks>
public static class SyllabusLadder
{
    /// <summary>一門課考幾首。</summary>
    public const int CourseSize = 3;

    /// <summary>一場檢定考幾首。技巧曲一首、歌唱曲一首。</summary>
    public const int PaperSize = 2;

    /// <summary>容錯點的下限。短譜的三首加起來乘上比率會低到荒謬。</summary>
    public const int MinimumAllowance = 24;

    /// <summary>等級帶湊不滿時最多往外放寬幾級。</summary>
    public const int MaxWidening = 3;

    /// <summary>
    /// 考場開了沒 —— 也就是「一局裡連開三首譜」那套流程做好了沒。
    /// </summary>
    /// <remarks>
    /// 現在是 false，而且畫面上會照實說。原因是這件事是<b>引擎沒做過的行為</b>：
    /// <c>GameManager</c> 的曲末分支只有一個出口（攤開結算），<c>StartSong</c> 每次
    /// 都跑 <c>ResetStats()</c> 和 <c>CleanupAllResources()</c>，而且專案裡根本不存
    /// 在一個代表「一局已完成」的值物件 —— 分數、stats、評審報告全是在結算的那一刻
    /// 從單例現撈的，下一首開場就被抹掉。連奏要先補上那個快照、曲中中止、以及暫停選
    /// 單在考試中換鈕（不換的話「重新開始」會把一次性的考試變成無限重試，整套過關條
    /// 件當場失效）。
    ///
    /// 檢定不需要這些：它是<b>送審制</b>（一局一首，打完在結算頁按「送審」），所以
    /// 檢定現在就能用，技階要等這一格翻成 true。兩條階梯因此可以分兩批出貨。
    ///
    /// 順帶一提，三組容錯率（3% / 2% / 1.2%）目前沒有任何資料支撐 —— 這個專案在此
    /// 之前沒有任何東西能讓玩家失敗，所以不存在失敗遙測。考場開張時第一件事應該是
    /// 把「每一首結束時剩多少容錯」寫進 oplog，跑過幾十次之後再回頭調。
    /// </remarks>
    // 不是 const：這一格是開關，const 會讓編譯器把另一邊折成無法抵達的程式碼
    // （CS0162）。換成 static readonly 之後兩邊都是活的，翻這一格仍然就是全部的改法。
    public static readonly bool ExamHallOpen = true;

    /// <summary>
    /// 一階怎麼從曲庫裡挑譜。全部讀 <see cref="ChartAnalysis"/> 現成的欄位。
    /// </summary>
    public enum Metric
    {
        /// <summary>最忙的那一秒有幾顆音。一階取<b>最低</b>：入門考該挑全帶裡最溫和的。</summary>
        PeakDensity,
        /// <summary>和別人同時按下的音符佔比。</summary>
        ChordShare,
        /// <summary>長押佔比。</summary>
        HoldShare,
        /// <summary>兩手的音符數差多少。取<b>最小</b>：左右分得越平均，越考得出分手。</summary>
        HandBalance,
        /// <summary>一隻手按著不放、其他指頭繼續動。不需要音高，沒有音高資料的譜也算得出來。</summary>
        Independence,
        /// <summary>同一隻手跳八度以上的快速移動。</summary>
        Leap,
        /// <summary>一隻手剛好抓一個八度的時刻。</summary>
        Octave,
        /// <summary>和弦音順著同一個方向跑。</summary>
        Arpeggio,
        /// <summary>兩個音高互換的快速交替。</summary>
        Trill,
        /// <summary>每秒十二顆以上持續了幾秒。耐力真正的代價。</summary>
        Sustained,
        /// <summary>整首平均每秒幾顆。檢定的「技巧曲」用這個。</summary>
        AverageDensity,
    }

    public sealed class Stage
    {
        public readonly int Index;

        /// <summary>這一階的名字，繁體原文。同時也是譯文檔的鍵。</summary>
        public readonly string RawName;

        /// <summary>這一階的名字，照目前語言。</summary>
        /// <remarks>
        /// 做成屬性不是欄位：<see cref="Stages"/> 是 <c>static readonly</c>，欄位只算
        /// 一次，在那裡翻的話整張表會停在程式啟動時那個語言。六個顯示的地方都讀這個，
        /// 所以改這裡六個一起好。
        /// </remarks>
        public string Name => Localize.S(RawName);

        public readonly int Low;
        public readonly int High;
        public readonly Metric Key;
        /// <summary>true = 取這個指標<b>最低</b>的三首。</summary>
        public readonly bool Ascending;
        /// <summary>
        /// 容錯點 = ceil(三首音符數合計 × 這個比率)。**目前沒有人讀。**
        /// </summary>
        /// <remarks>
        /// 這是為「一局連開三首、共用一池容錯」那種一次性考試寫的。考場最後做成**逐曲
        /// 應試**：每一首各自打、可以重測、取最好的一次，那種模式裡「共用一池容錯」
        /// 沒有意義 —— 沒有失敗狀態可以扣。欄位和編輯器裡的那個旋鈕留著，哪天真的
        /// 做了一次性考試再接回來；現在別把它印在考場上，那會是個騙人的數字。
        /// </remarks>
        public readonly float AllowanceRate;

        /// <summary>
        /// 抽到的第一首低於這個值，就代表曲庫裡根本沒有這個技法。
        /// </summary>
        /// <remarks>
        /// 這不是門檻，是<b>誠實度檢查</b>：低於它時考卷加印一行「本階曲庫未達顯著，
        /// 已取最接近的三首」。拿它當門檻的話前四階永遠開不了課（低階沒有技法供給），
        /// 而假裝三首長押譜真的在考長押則是騙人。實測這個曲庫只有三階會觸發 ——
        /// Lv5–6 那一帶最長押的譜也只有 4.0%。遞增鍵（一階、四階）沒有這個概念。
        /// </remarks>
        public readonly float Significance;

        internal Stage(int index, string name, int low, int high, Metric key, bool ascending,
            float rate, float significance)
        {
            Index = index; RawName = name; Low = low; High = high;
            Key = key; Ascending = ascending; AllowanceRate = rate; Significance = significance;
        }
    }

    /// <summary>
    /// 一級要求四項各拿幾分。0 = 這一級不看這一項。
    /// </summary>
    /// <remarks>
    /// 每一首都要達到，兩首平均也要達到。順序和
    /// <see cref="Judgment.RecitalAssessment.Report"/> 一致：完整度、旋律、失誤錯音、
    /// 情感表情。
    /// </remarks>
    public readonly struct Demand
    {
        public readonly float Completeness;
        public readonly float Melody;
        public readonly float Mistakes;
        public readonly float Expression;
        /// <summary>兩首平均的額外門檻。0 = 沒有額外要求。</summary>
        public readonly float Average;

        internal Demand(float completeness, float melody, float mistakes, float expression, float average)
        {
            Completeness = completeness; Melody = melody; Mistakes = mistakes;
            Expression = expression; Average = average;
        }

        public float For(int criterion)
        {
            switch (criterion)
            {
                case 0: return Completeness;
                case 1: return Melody;
                case 2: return Mistakes;
                case 3: return Expression;
                default: return 0f;
            }
        }
    }

    public sealed class Grade
    {
        public readonly int Index;
        public readonly int Low;
        public readonly int High;
        public readonly Demand Demand;
        /// <summary>
        /// 兩首都必須真的有力度與踏板資料可評。
        /// </summary>
        /// <remarks>
        /// 防漏用的。<c>RecitalAssessment.Evaluate</c> 在分母為 0 時把情感表情<b>退回
        /// 滿分</b>（「沒有被要求的東西不該被扣分」，那條規則在單曲結算是對的），於是最
        /// 難那幾級的情感門檻可以靠「這份譜沒有資料」白拿。七八級把它擋掉。
        ///
        /// 檢查的是<b>這一局實際評到了什麼</b>（`StatsManager` 的 dynamic/pedal total），
        /// 不是譜面屬性。譜面屬性要在 <see cref="ChartAnalysis"/> 加兩個欄位、把
        /// CacheVersion 從 10 推到 11，而引擎端的串流解析器是刻意 Skip 掉 pedal_data
        /// 的（那個 skip 就是解析速度的來源）—— 為了兩級的門檻讓 690 份分析檔全部失效
        /// 不划算，而且送審的那一刻本來就拿得到真正需要的那個數字。
        /// </remarks>
        public readonly bool NeedsExpressionData;

        /// <summary>
        /// 這一級的考題該有多難（0–100，全庫相對）。
        /// </summary>
        /// <remarks>
        /// **為什麼需要這個：等級帶不夠。** 舊的抽法是「帶內平均密度最高的那一份」，
        /// 而帶內最高值是帶內中位的 3～5 倍 —— 只要帶裡有一份「標的等級低、實際很難」
        /// 的譜，那一級就會爆衝。實測：舊法的三級抽到 <c>系ぎて Normal</c>（標 Lv5，
        /// 速度負荷 41.2），比四、五、六級都難；而 <c>Conflict (VILA Remix) Normal</c>
        /// 標 Lv4 卻有 54.7，比 Lv13 的中位還高，二到六級放寬後都抃得到它。
        ///
        /// 改成瞑準目標之後，八級的平均難度是 13 / 25 / 31 / 46 / 55 / 65 / 78 / 89，
        /// 單調而且級距平均 11 分（範圍 6～15）。舊法是不單調的。
        ///
        /// 數值是等距鑄開的百分位，不是調出來的；等級帶還在，當內容的軟性限制。
        /// </remarks>
        public readonly int Target;

        internal Grade(int index, int low, int high, int target, Demand demand,
            bool needsExpressionData)
        {
            Index = index; Low = low; High = high; Target = target;
            Demand = demand; NeedsExpressionData = needsExpressionData;
        }

        /// <summary>報考這一級之前要先有幾階。一二級免修。</summary>
        public int RequiredStage => Mathf.Max(0, Index - 2);
    }

    /// <summary>
    /// 技階十階。
    /// </summary>
    /// <remarks>
    /// 等級帶重疊是刻意的：這個曲庫在 Lv2（3 份）和 Lv5（3 份）非常薄，單級的帶湊不
    /// 出三首不同的歌。實測每一階都抽得出來，最窄的三階（Lv5–6）有 25 首。
    /// </remarks>
    public static readonly Stage[] Stages =
    {
        //                                 lv帶   指標               遞增  容錯   顯著
        new Stage(1,  "視奏",       1,  3,  Metric.PeakDensity,    true,  0.030f, 0f),
        new Stage(2,  "和音",       3,  5,  Metric.ChordShare,     false, 0.030f, 0.30f),
        new Stage(3,  "長押",       5,  6,  Metric.HoldShare,      false, 0.030f, 0.05f),
        new Stage(4,  "分手",       6,  7,  Metric.HandBalance,    true,  0.020f, 0f),
        new Stage(5,  "手指獨立",   7,  8,  Metric.Independence,   false, 0.020f, 0.05f),
        new Stage(6,  "大跨度跳躍", 8,  9,  Metric.Leap,           false, 0.020f, 0.10f),
        new Stage(7,  "八度",       9,  10, Metric.Octave,         false, 0.020f, 0.05f),
        new Stage(8,  "琶音",       10, 11, Metric.Arpeggio,       false, 0.012f, 0.05f),
        new Stage(9,  "顫音",       11, 13, Metric.Trill,          false, 0.012f, 0.03f),
        new Stage(10, "無窮動",     13, 99, Metric.Sustained,      false, 0.012f, 10f),
    };

    /// <summary>
    /// 檢定八級。
    /// </summary>
    /// <remarks>
    /// **級與級的差別不是等級變高，是多一項從選修變必修。** 這才是八級各自成立的理
    /// 由，不是八個亂編的數字。三個常數（75 / 85 / 95）一個都沒改，全部沿用
    /// <see cref="Judgment.RecitalAssessment"/>。
    ///
    /// **七級的門檻被改成單調的。** 原案是「四項全部 ≥75」，可是六級已經要求完整度
    /// ≥85 且失誤 ≥85 —— 照原案寫，七級在這兩項上反而<b>比六級鬆</b>。七級真正新增
    /// 的是「情感表情從選修變必修」，所以前面那兩項維持六級的高度。
    ///
    /// **一級是 Lv1–3 不是 Lv1–2。** 量過：這個曲庫非教學的 Lv1 譜是 <b>0 份</b>
    /// （全庫僅有的兩份 Lv1 是新手教學課程），Lv2 只有 3 份而且其中一份沒有分析檔。
    /// Lv1–2 的候選恰好只剩兩首，而那兩首正好也是一階會抽走的 —— 第一個小時兩條階
    /// 梯會發給玩家同樣的歌。放寬到 Lv1–3 之後候選有 26 首。
    /// </remarks>
    public static readonly Grade[] Grades =
    {
        //                  lv帶  目標     完整  旋律  失誤  情感  平均   要表情資料
        new Grade(1, 1,  3,  12, new Demand(75f,  0f,   0f,   0f,   0f),  false),
        new Grade(2, 3,  4,  23, new Demand(75f,  0f,   0f,   0f,   0f),  false),
        new Grade(3, 4,  5,  34, new Demand(75f,  0f,   75f,  0f,   0f),  false),
        new Grade(4, 5,  7,  45, new Demand(85f,  0f,   75f,  0f,   0f),  false),
        new Grade(5, 7,  8,  56, new Demand(85f,  75f,  75f,  0f,   0f),  false),
        new Grade(6, 8,  10, 67, new Demand(85f,  75f,  85f,  0f,   0f),  false),
        new Grade(7, 10, 12, 78, new Demand(85f,  75f,  85f,  75f,  0f),  true),
        new Grade(8, 12, 99, 90, new Demand(85f,  75f,  85f,  85f,  85f), true),
    };

    public static Stage StageAt(int index) =>
        index >= 1 && index <= Stages.Length ? Stages[index - 1] : null;

    public static Grade GradeAt(int index) =>
        index >= 1 && index <= Grades.Length ? Grades[index - 1] : null;

    /// <summary>手改過的門檻組成一份新的 <see cref="Demand"/>。見 <see cref="SyllabusOverrides"/>。</summary>
    public static Demand MakeDemand(float completeness, float melody, float mistakes,
        float expression, float average) =>
        new Demand(completeness, melody, mistakes, expression, average);

    /// <summary>
    /// 等級帶印成字。最高那一階／級的上限是哨兵值 99，不能直接印出來。
    /// </summary>
    public static string BandText(int low, int high) =>
        high >= 99 ? Localize.SF("Lv{0} 以上", low) : $"Lv{low}–{high}";

    /// <summary>四項的名字，順序和 <c>RecitalAssessment.Report</c> 一致。</summary>
    /// <remarks>繁體原文，同時是譯文檔的鍵。要顯示的字用 <see cref="CriterionName"/>。</remarks>
    public static readonly string[] CriterionNames = { "完整度", "旋律", "失誤錯音", "情感表情" };

    /// <summary>同樣四項的短名。考程書那一列只有 649 寬，八級那一行放不下全名。</summary>
    public static readonly string[] CriterionShortNames = { "完整", "旋律", "失誤", "情感" };

    /// <summary>第 <paramref name="index"/> 項的名字，照目前語言。</summary>
    public static string CriterionName(int index) =>
        index >= 0 && index < CriterionNames.Length
            ? Localize.S(CriterionNames[index]) : string.Empty;

    /// <summary>同上，短名。</summary>
    public static string CriterionShortName(int index) =>
        index >= 0 && index < CriterionShortNames.Length
            ? Localize.S(CriterionShortNames[index]) : string.Empty;

    /// <summary>印蓋出來要給人看的字。</summary>
    /// <remarks>
    /// <see cref="SealFor"/> 回的是**存進存檔**的字串，<see cref="SealRank"/> 還拿它
    /// 比名次 —— 那個不能翻，翻掉舊存檔的印就排不出來了。要印在畫面上時才過這一層。
    /// </remarks>
    public static string SealText(string seal) =>
        string.IsNullOrEmpty(seal) ? string.Empty : Localize.S(seal);

    /// <summary>證書上蓋哪一枚印。兩首平均對 75 / 85 / 95。</summary>
    public static string SealFor(float average)
    {
        if (average >= Judgment.RecitalAssessment.DistinctionMark) return "特優";
        if (average >= Judgment.RecitalAssessment.MeritMark) return "優等";
        if (average >= Judgment.RecitalAssessment.PassMark) return "及第";
        return string.Empty;
    }

    /// <summary>印只往上蓋：拿了三級優等之後四級考砸，三級優等不會掉。</summary>
    public static int SealRank(string seal)
    {
        switch (seal)
        {
            case "特優": return 3;
            case "優等": return 2;
            case "及第": return 1;
            default: return 0;
        }
    }

    /// <summary>
    /// 一份譜在某個指標上的值。<c>analysis</c> 為 null 或空譜回傳 NaN，代表「不可比」。
    /// </summary>
    public static float Measure(ChartAnalysis analysis, Metric metric)
    {
        if (analysis == null || analysis.noteCount <= 0) return float.NaN;
        float notes = analysis.noteCount;
        switch (metric)
        {
            case Metric.PeakDensity: return analysis.peakDensity;
            case Metric.ChordShare: return analysis.chordNoteCount / notes;
            case Metric.HoldShare: return analysis.holdCount / notes;
            case Metric.HandBalance:
                return Mathf.Abs(analysis.rightHandCount - analysis.leftHandCount) / notes;
            case Metric.Independence: return analysis.independenceRatio;
            case Metric.Leap: return analysis.leapRatio;
            case Metric.Octave: return analysis.octaveRatio;
            case Metric.Arpeggio: return analysis.arpeggioRatio;
            case Metric.Trill: return analysis.trillRatio;
            case Metric.Sustained: return analysis.sustainedSeconds;
            case Metric.AverageDensity: return analysis.averageDensity;
            default: return float.NaN;
        }
    }

    /// <summary>指標的顯示名，給考卷印「本階照什麼排的」。</summary>
    public static string MetricName(Metric metric) => Localize.S(MetricNameSource(metric));

    private static string MetricNameSource(Metric metric)
    {
        switch (metric)
        {
            case Metric.PeakDensity: return "尖峰密度";
            case Metric.ChordShare: return "和音比例";
            case Metric.HoldShare: return "長押比例";
            case Metric.HandBalance: return "兩手均衡";
            case Metric.Independence: return "手指獨立";
            case Metric.Leap: return "跨度跳躍";
            case Metric.Octave: return "八度";
            case Metric.Arpeggio: return "琶音";
            case Metric.Trill: return "顫音";
            case Metric.Sustained: return "耐力秒數";
            case Metric.AverageDensity: return "平均密度";
            default: return string.Empty;
        }
    }
}
