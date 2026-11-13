using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 演奏者的實力評分：四個指標各自累積，只往上不往下。
/// </summary>
/// <remarks>
/// **為什麼不是把分數平均起來。** 一首曲子的七位數分數只說「這一局打得怎樣」，
/// 而且不同難度的分數是同一個上限 —— Lv3 全 P 和 Lv15 全 P 都是一百萬。評分要回答
/// 的是另一個問題：**你現在的實力在哪裡**，所以它必須把難度乘進去。
///
/// **刻度是照著使用者的定義訂的：**
///   3000 普通、5000 精熟、6000 = Lv15 也能 S＋無 Miss。
/// 所以一次演出的滿分是 `等級 × 400`（Lv15 → 6000），再乘上這一局的品質。
///
/// **四個指標分開累積。** 品質不是一個數字，是結算畫面那四條：完整度、旋律、失誤
/// 錯音、情感與表情（`Judgment.RecitalAssessment`）。每一項各自記住自己的最佳值，
/// 總評分是四項的等權平均 —— 和結算畫面 `Report.average` 同一條規則。這樣「我卡在
/// 哪一項」直接看得出來，而不是只知道總分沒動。
///
/// **只加不減。** 打得比目前實力差不會把評分拉下來 —— 那會讓人不敢挑戰難的曲子，
/// 而這個評分的用途正好相反。但也不能當作沒發生：低於目前實力的那一局會記下**差
/// 多少**，畫面上照實說。
///
/// **演奏會多一千。** 練習模式下旋律和表情這兩項有大量「沒有被要求就給滿分」的情
/// 況（沒有踏板資料、沒有力度資料），所以同樣的品質在練習模式比較容易拿。演奏會
/// 是正式的那一場，整段刻度往上一千：`等級 × 400 + 1000`。
///
/// **為什麼自己一份檔案。** `LocalScoreRecords` 是排行榜，而它**刻意不收**練習模式
/// 和自動演奏的分數（速度可以改、背景音樂關著，那和別人的分數不是同一件事）。實力
/// 評分要的正好是「你打過的每一局」，包含練習模式 —— 兩者收的東西不同，所以兩份檔。
/// </remarks>
public static class PlayerRating
{
    /// <summary>一個難度等級值多少分。Lv15 × 400 = 6000。</summary>
    public const float PerLevel = 400f;
    /// <summary>演奏會整段刻度往上多少。</summary>
    public const float RecitalBand = 1000f;

    public const float Ordinary = 3000f;     // 普通
    public const float Proficient = 5000f;   // 精熟

    private const string FileName = "player_rating.json";
    private const int MaxHistory = 8;

    [Serializable]
    internal sealed class Band
    {
        /// <summary>四項各自的最佳值，順序同結算畫面：完整度、旋律、失誤、表情。</summary>
        public float[] best = new float[4];
        public string bestSong;
        public string bestTier;
        public int bestLevel;
        public long updatedAtUtcTicks;
    }

    [Serializable]
    public sealed class Attempt
    {
        public string song;
        public string tier;
        public int level;
        public int score;
        public float[] marks = new float[4];
        public float value;          // 這一局換算出來的評分
        public float gap;            // 低於目前實力多少（0 = 有進步）
        public bool recital;
        public long atUtcTicks;
    }

    [Serializable]
    internal sealed class RatingFile
    {
        public int version = 1;
        public Band practice = new Band();
        public Band recital = new Band();
        public List<Attempt> history = new List<Attempt>();
    }

    private static RatingFile data;

    /// <summary>四項的名字（繁體原文）。同時也是譯文檔的鍵，所以**不要**在這裡翻。</summary>
    /// <remarks>
    /// <c>static readonly</c> 的內容只算一次。在這裡包 <c>Localize.S</c> 的話，
    /// 整個陣列會停在程式啟動時那個語言，換語言之後不會變。要翻的字從
    /// <see cref="CriterionName"/> 拿。
    /// </remarks>
    public static readonly string[] CriterionNames =
    {
        "完整度", "旋律", "失誤錯音", "情感表情"
    };

    /// <summary>第 <paramref name="index"/> 項的名字，照目前語言。</summary>
    public static string CriterionName(int index) =>
        index >= 0 && index < CriterionNames.Length
            ? Localize.S(CriterionNames[index]) : string.Empty;

    // ------------------------------------------------------------------
    // 讀
    // ------------------------------------------------------------------

    /// <summary>目前的評分。recital=true 取演奏會那一段刻度。</summary>
    public static float Current(bool recital)
    {
        EnsureLoaded();
        Band band = recital ? data.recital : data.practice;
        float sum = 0f;
        for (int i = 0; i < 4; i++) sum += band.best[i];
        return sum * 0.25f;
    }

    /// <summary>四項各自的最佳值。</summary>
    public static float[] Criteria(bool recital)
    {
        EnsureLoaded();
        Band band = recital ? data.recital : data.practice;
        var copy = new float[4];
        Array.Copy(band.best, copy, 4);
        return copy;
    }

    /// <summary>這一段刻度的滿分：Lv15 的上限。畫進度條要用。</summary>
    public static float Ceiling(bool recital) => 15f * PerLevel + (recital ? RecitalBand : 0f);

    /// <summary>評語。門檻在演奏會那一段整體往上 1000。</summary>
    public static string Title(float rating, bool recital)
    {
        float shift = recital ? RecitalBand : 0f;
        // 四個畫面（簾桿、練習室、側牌、結算）都從這裡拿評語，所以翻這一個函式
        // 就四個畫面一起好。距離下一級的那幾句在 SelectionRail.NextStep，用字要和
        // 這裡對齊：精熟→熟練，所以那邊是「熟練まで」不是「精熟まで」。
        if (rating >= 6000f + shift) return Localize.T("名家", "名家", "Master");
        if (rating >= Proficient + shift) return Localize.T("精熟", "精熟", "Proficient");
        if (rating >= Ordinary + shift) return Localize.T("普通", "普通", "Ordinary");
        if (rating > 0f) return Localize.T("初學", "初学", "Beginner");
        return Localize.T("尚無紀錄", "尚无纪录", "No Record");
    }

    /// <summary>最近幾次挑戰，新的在前。</summary>
    public static IReadOnlyList<Attempt> History
    {
        get
        {
            EnsureLoaded();
            return data.history;
        }
    }

    public static Attempt LastAttempt
    {
        get
        {
            EnsureLoaded();
            return data.history.Count > 0 ? data.history[0] : null;
        }
    }

    /// <summary>撐起目前評分的那一首（四項裡最高的那一項來自哪）。</summary>
    public static string BestSong(bool recital)
    {
        EnsureLoaded();
        Band band = recital ? data.recital : data.practice;
        return band.bestSong;
    }

    // ------------------------------------------------------------------
    // 寫
    // ------------------------------------------------------------------

    /// <summary>
    /// 記一次演出。回傳這一局換算出來的評分。
    /// </summary>
    /// <remarks>
    /// 自動演奏不要送進來 —— 那不是演奏者打的。
    /// </remarks>
    public static float Record(SongSelectionManager.SongOption chart, int score,
        Judgment.RecitalAssessment.Report report, bool recital)
    {
        if (chart == null || !report.measured) return 0f;
        EnsureLoaded();

        int level = Mathf.Max(1, chart.difficultyLevel);
        float ceiling = level * PerLevel + (recital ? RecitalBand : 0f);
        float[] marks = { report.completeness, report.melody, report.mistakes, report.expression };

        Band band = recital ? data.recital : data.practice;
        float before = Current(recital);

        float value = 0f;
        bool raised = false;
        for (int i = 0; i < 4; i++)
        {
            float earned = ceiling * Mathf.Clamp01(marks[i] * 0.01f);
            value += earned;
            // 只加不減：每一項各自往上棘輪。
            if (earned > band.best[i])
            {
                band.best[i] = earned;
                raised = true;
            }
        }
        value *= 0.25f;

        if (raised)
        {
            band.bestSong = chart.displayName;
            band.bestTier = chart.difficultyName;
            band.bestLevel = level;
            band.updatedAtUtcTicks = DateTime.UtcNow.Ticks;
        }

        var attempt = new Attempt
        {
            song = chart.displayName,
            tier = chart.difficultyName,
            level = level,
            score = score,
            marks = marks,
            value = value,
            // 低於目前實力就不算進去，但要記下差多少 —— 照實說比假裝沒發生有用。
            gap = raised ? 0f : Mathf.Max(0f, before - value),
            recital = recital,
            atUtcTicks = DateTime.UtcNow.Ticks
        };
        data.history.Insert(0, attempt);
        if (data.history.Count > MaxHistory) data.history.RemoveRange(MaxHistory, data.history.Count - MaxHistory);

        Save();
        return value;
    }

    // ------------------------------------------------------------------
    // 存檔
    // ------------------------------------------------------------------

    /// <summary>換人了：丟掉快取，下一次讀就會讀到新那位的檔案。</summary>
    /// <remarks>
    /// 靜態快取跨換人是這一整套最容易出的錯 —— 沒有丟的話，換過去的人會看到前一位的
    /// 成績，而且**下一次存檔會把它寫進新的人的檔案裡**，兩邊都毀掉。所以是 slot 一
    /// 變就丟，不是等誰記得呼叫。
    /// </remarks>
    private static void Forget() => data = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSlotChanges() => PlayerSlots.Changed += Forget;

    private static void EnsureLoaded()
    {
        if (data != null) return;
        string path = PlayerSlots.PathFor(FileName);
        try
        {
            data = File.Exists(path)
                ? JsonUtility.FromJson<RatingFile>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerRating] Could not load rating: {ex.Message}");
            data = null;
        }
        if (data == null) data = new RatingFile();
        Repair(data.practice);
        Repair(data.recital);
        if (data.history == null) data.history = new List<Attempt>();
        data.history.RemoveAll(entry => entry == null);
        for (int i = 0; i < data.history.Count; i++)
        {
            if (data.history[i].marks == null || data.history[i].marks.Length != 4)
                data.history[i].marks = new float[4];
        }
    }

    private static void Repair(Band band)
    {
        if (band == null) return;
        // JsonUtility 會把缺少的陣列讀成 null 或長度不符 —— 舊檔案、手改過的檔案
        // 都會走到這裡，不能讓它在第一次讀取就丟例外。
        if (band.best == null || band.best.Length != 4) band.best = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (float.IsNaN(band.best[i]) || band.best[i] < 0f) band.best[i] = 0f;
        }
    }

    private static void Save()
    {
        string path = PlayerSlots.PathFor(FileName);
        string temporary = path + ".tmp";
        try
        {
            // 檔案現在在 players/<id>/ 底下，建根目錄已經不夠。
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerRating] Could not save rating: {ex.Message}");
        }
    }
}
