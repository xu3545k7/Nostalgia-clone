using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class LocalScoreEntry
{
    public int score;
    public string rank;
    public long achievedAtUtcTicks;

    /// <summary>這一局有沒有全連（沒有 Miss、沒有 Fail）。</summary>
    /// <remarks>
    /// 後加的欄位。`JsonUtility` 讀舊檔時找不到它就是 `false`，也就是「以前打的都
    /// 不算全連」—— 那是唯一誠實的預設值，因為當時根本沒有記。檔案版本不必跟著
    /// 升：多一個欄位對舊版讀取器也是無害的（它會忽略）。
    /// </remarks>
    public bool fullCombo;
}

[Serializable]
internal sealed class LocalChartScoreList
{
    public string key;
    public List<LocalScoreEntry> scores = new List<LocalScoreEntry>();
}

[Serializable]
internal sealed class LocalScoreFile
{
    public int version = 1;
    public List<LocalChartScoreList> charts = new List<LocalChartScoreList>();
}

/// <summary>
/// Local-only manual-play leaderboard. It never modifies chart/register data
/// and retains at most three scores for each chart difficulty.
/// </summary>
public static class LocalScoreRecords
{
    private const int MaxScoresPerChart = 3;
    private const string FileName = "nostalgia_local_scores.json";
    private static LocalScoreFile data;
    private static readonly List<LocalScoreEntry> EmptyScores = new List<LocalScoreEntry>();

    /// <summary>
    /// 練習模式的紀錄排在另一個桶裡。
    /// </summary>
    /// <remarks>
    /// **兩種模式的分數不是同一件事。** 練習可以改速度、背景音樂關著、段落重播，
    /// 拿它和演奏會的分數排在一起是比錯了對象。
    ///
    /// 舊的做法是兩邊共用同一個桶，而且是**單向**地漏：結算頁擋掉了練習的寫入
    /// （對），可是練習室的紀錄牌讀的也是那個桶 —— 所以演奏會打的分數會回填到練習
    /// 室，而練習打的分數哪裡都不記。前綴把兩邊彻底分開；舊檔的鍵沒有前綴，所以
    /// 以前的紀錄自動算演奏會的（那本來就是它們唯一被記下來的來源）。
    /// </remarks>
    private const string PracticePrefix = "practice|";

    public static string GetKey(SongSelectionManager.SongOption option, bool practice)
    {
        string key = GetKey(option);
        return practice && !string.IsNullOrEmpty(key) ? PracticePrefix + key : key;
    }

    public static string GetKey(SongSelectionManager.SongOption option)
    {
        if (option == null) return string.Empty;
        string identity = !string.IsNullOrWhiteSpace(option.chartFileName)
            ? "chart:" + option.chartFileName
            : $"song:{option.displayName}|{option.difficultyName}|{option.difficultyLevel}";
        if (!string.IsNullOrWhiteSpace(option.externalId))
            identity = "external:" + option.externalId + "|" + identity;
        return identity.Replace('\\', '/').Trim().ToLowerInvariant();
    }

    public static IReadOnlyList<LocalScoreEntry> GetTopScores(
        SongSelectionManager.SongOption option, bool practice = false)
    {
        return GetTopScores(GetKey(option, practice));
    }

    public static IReadOnlyList<LocalScoreEntry> GetTopScores(string key)
    {
        EnsureLoaded();
        LocalChartScoreList chart = FindChart(key);
        return chart != null ? chart.scores : EmptyScores;
    }

    /// <summary>這張譜最好的那一筆，沒打過就是 null。</summary>
    /// <remarks>
    /// 清單要同時顯示名次勳章和全連記號，而這兩件事在同一筆紀錄裡 —— 分開取會在
    /// 「最高分那局沒全連、但另一局全連了」的時候給出互相矛盾的兩個圖示。
    ///
    /// **全連是看所有紀錄，不是只看最高分那筆。** 全連是一件做到過就算的事，而分數
    /// 是比大小的；把兩者綁在同一筆會讓玩家在刷分之後失去已經拿到的記號。
    /// </remarks>
    public static LocalScoreEntry GetBest(SongSelectionManager.SongOption option,
        bool practice = false)
    {
        IReadOnlyList<LocalScoreEntry> scores = GetTopScores(option, practice);
        if (scores == null || scores.Count == 0) return null;
        LocalScoreEntry best = scores[0];
        for (int i = 0; i < scores.Count; i++)
        {
            if (scores[i] == null) continue;
            if (scores[i].score > best.score) best = scores[i];
        }
        return best;
    }

    /// <summary>這張譜**曾經**全連過嗎。</summary>
    public static bool EverFullCombo(SongSelectionManager.SongOption option,
        bool practice = false)
    {
        IReadOnlyList<LocalScoreEntry> scores = GetTopScores(option, practice);
        if (scores == null) return false;
        for (int i = 0; i < scores.Count; i++)
            if (scores[i] != null && scores[i].fullCombo) return true;
        return false;
    }

    public static int GetBestScore(SongSelectionManager.SongOption option,
        bool practice = false)
    {
        IReadOnlyList<LocalScoreEntry> scores = GetTopScores(option, practice);
        return scores.Count > 0 ? scores[0].score : 0;
    }

    /// <returns>1-based local placement, or 0 when the score missed the top three.</returns>
    public static int RecordManualScore(
        SongSelectionManager.SongOption option, int score, bool fullCombo = false,
        bool practice = false)
    {
        string key = GetKey(option, practice);
        if (string.IsNullOrEmpty(key)) return 0;
        EnsureLoaded();
        LocalChartScoreList chart = FindChart(key);
        if (chart == null)
        {
            chart = new LocalChartScoreList { key = key };
            data.charts.Add(chart);
        }

        LocalScoreEntry added = new LocalScoreEntry
        {
            score = Mathf.Clamp(score, 0, 1000000),
            rank = GetRank(score),
            achievedAtUtcTicks = DateTime.UtcNow.Ticks,
            fullCombo = fullCombo
        };
        chart.scores.Add(added);
        chart.scores.Sort((a, b) =>
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0
                ? byScore
                : b.achievedAtUtcTicks.CompareTo(a.achievedAtUtcTicks);
        });
        int placement = chart.scores.IndexOf(added);
        if (chart.scores.Count > MaxScoresPerChart)
            chart.scores.RemoveRange(
                MaxScoresPerChart, chart.scores.Count - MaxScoresPerChart);
        if (placement >= MaxScoresPerChart)
        {
            if (chart.scores.Count == 0) data.charts.Remove(chart);
            return 0;
        }

        Save();
        return placement + 1;
    }

    public static string GetRank(int score)
    {
        return score >= 1000000 ? "P"
            : score >= 950000 ? "S"
            : score >= 900000 ? "A+"
            : score >= 850000 ? "A"
            : score >= 750000 ? "B+"
            : score >= 600000 ? "B"
            : "C";
    }

    public static Texture2D LoadRankTexture(string rank)
    {
        string assetName = rank == "A+" ? "APlus" : rank == "B+" ? "BPlus" : rank;
        return Resources.Load<Texture2D>($"UI/Results/Ranks/Rank_{assetName}");
    }

    private static LocalChartScoreList FindChart(string key)
    {
        if (string.IsNullOrEmpty(key) || data == null || data.charts == null) return null;
        for (int i = 0; i < data.charts.Count; i++)
        {
            LocalChartScoreList chart = data.charts[i];
            if (chart != null && string.Equals(chart.key, key, StringComparison.Ordinal))
                return chart;
        }
        return null;
    }

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
                ? JsonUtility.FromJson<LocalScoreFile>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalScoreRecords] Could not load local scores: {ex.Message}");
            data = null;
        }
        if (data == null) data = new LocalScoreFile();
        if (data.charts == null) data.charts = new List<LocalChartScoreList>();
        for (int i = data.charts.Count - 1; i >= 0; i--)
        {
            LocalChartScoreList chart = data.charts[i];
            if (chart == null || string.IsNullOrEmpty(chart.key))
            {
                data.charts.RemoveAt(i);
                continue;
            }
            if (chart.scores == null) chart.scores = new List<LocalScoreEntry>();
            chart.scores.RemoveAll(entry => entry == null);
            chart.scores.Sort((a, b) => b.score.CompareTo(a.score));
            if (chart.scores.Count > MaxScoresPerChart)
                chart.scores.RemoveRange(
                    MaxScoresPerChart, chart.scores.Count - MaxScoresPerChart);
        }
    }

    private static void Save()
    {
        string path = PlayerSlots.PathFor(FileName);
        string temporaryPath = path + ".tmp";
        try
        {
            // 檔案現在在 players/<id>/ 底下，建根目錄已經不夠。
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporaryPath, path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LocalScoreRecords] Could not save local scores: {ex.Message}");
        }
    }
}
