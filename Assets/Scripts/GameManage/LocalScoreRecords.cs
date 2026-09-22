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
        SongSelectionManager.SongOption option)
    {
        return GetTopScores(GetKey(option));
    }

    public static IReadOnlyList<LocalScoreEntry> GetTopScores(string key)
    {
        EnsureLoaded();
        LocalChartScoreList chart = FindChart(key);
        return chart != null ? chart.scores : EmptyScores;
    }

    public static int GetBestScore(SongSelectionManager.SongOption option)
    {
        IReadOnlyList<LocalScoreEntry> scores = GetTopScores(option);
        return scores.Count > 0 ? scores[0].score : 0;
    }

    /// <returns>1-based local placement, or 0 when the score missed the top three.</returns>
    public static int RecordManualScore(
        SongSelectionManager.SongOption option, int score)
    {
        string key = GetKey(option);
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
            achievedAtUtcTicks = DateTime.UtcNow.Ticks
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

    private static void EnsureLoaded()
    {
        if (data != null) return;
        string path = Path.Combine(Application.persistentDataPath, FileName);
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
        string path = Path.Combine(Application.persistentDataPath, FileName);
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
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
