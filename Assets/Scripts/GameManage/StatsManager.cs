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

    private const int MaxScoreValue = 1_000_000;
    private static readonly decimal MaxScoreValueDecimal = 1_000_000m;
    private static readonly decimal PrecisionStep = 0.001m;
    private int totalJudgments = 0;
    private int expectedJudgments = 0;
    private decimal accumulatedScoreWeight = 0m;
    private decimal perJudgmentUnit = 0m;
    private decimal totalScoreRaw = 0m;
    private decimal totalScoreDecimal = 0m;

    public void Increment(JudgmentResult res)
    {
        try
        {
            switch (res)
            {
                case JudgmentResult.Perfect: perfectCount++; break;
                case JudgmentResult.Great: greatCount++; break;
                case JudgmentResult.Good: goodCount++; break;
                case JudgmentResult.Miss: missCount++; break;
                case JudgmentResult.Fail: failCount++; break;
            }

            bool addToCombo = res == JudgmentResult.Perfect || res == JudgmentResult.Great || res == JudgmentResult.Good;
            if (addToCombo)
            {
                currentCombo++;
                if (currentCombo > maxCombo)
                {
                    maxCombo = currentCombo;
                }
            }
            else
            {
                currentCombo = 0;
            }

            totalJudgments++;

            decimal weight = GetScoreWeight(res);
            if (weight < 0m) weight = 0m;
            accumulatedScoreWeight += weight;

            if (perJudgmentUnit > 0m && expectedJudgments > 0)
            {
                totalScoreRaw += perJudgmentUnit * weight;
                totalScoreDecimal = CeilToPrecision(totalScoreRaw, PrecisionStep);

                if (totalJudgments >= expectedJudgments && totalScoreRaw >= MaxScoreValueDecimal)
                {
                    totalScoreRaw = MaxScoreValueDecimal;
                    totalScoreDecimal = MaxScoreValueDecimal;
                }
            }
            else
            {
                decimal denomInt = expectedJudgments > 0 ? expectedJudgments : Math.Max(1, totalJudgments);
                decimal denominator = denomInt <= 0 ? 1m : (decimal)denomInt;
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

    public void ResetStats()
    {
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
        totalJudgments = 0;
        accumulatedScoreWeight = 0m;
        totalScoreRaw = 0m;
        totalScoreDecimal = 0m;
        expectedJudgments = 0;
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

    public int CurrentCombo => currentCombo;
    public int MaxCombo => maxCombo;
    public int CurrentScore => currentScore;

    public int TotalJudgments => totalJudgments;
    public int ExpectedJudgments => expectedJudgments;

    public void ConfigureExpectedJudgments(int expectedCount)
    {
        expectedJudgments = Mathf.Max(0, expectedCount);
        if (expectedJudgments > 0)
        {
            perJudgmentUnit = MaxScoreValueDecimal / expectedJudgments;
        }
        else
        {
            perJudgmentUnit = 0m;
        }
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
                expected++; // head (or single judgment)
                if (CountsAsDualJudgment(note))
                {
                    expected++; // tail / dual count
                }

                // If this is a hold-like note, compute extra per-hold judgments using the same helper
                // used at runtime so expected counts match awarded extras.
                try
                {
                    bool isHold = false;
                    if (!string.IsNullOrEmpty(note.type))
                    {
                        if (string.Equals(note.type, "hold", StringComparison.OrdinalIgnoreCase)) isHold = true;
                        else if (string.Equals(note.type, "staccato", StringComparison.OrdinalIgnoreCase) || string.Equals(note.type, "stac", StringComparison.OrdinalIgnoreCase)) isHold = true;
                        else if (int.TryParse(note.type, out int parsed) && (parsed == 2 || parsed == 3)) isHold = true;
                    }
                    if (!isHold && (note.note_type == 2 || note.note_type == 3)) isHold = true;

                    if (isHold)
                    {
                        try
                        {
                            var info = HoldJudgment.ComputeExtraInfo(note, null);
                            expected += info.maxExtra;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        ConfigureExpectedJudgments(expected);
    }

    /// <summary>
    /// Returns the achievement percentage (0-100) defined as currentScore / maxPossibleScore (all-perfect), rounded to nearest integer.
    /// If no expected judgments configured, return 100.
    /// </summary>
    public int GetAchievementPercent()
    {
        try
        {
            // New achievement calculation: accuracy-like.
            // We compute: (sum of weights) / (total judgments) * 100
            // Weights: perfect=1, great=0.9, good=0.5, miss/fail=0
            if (totalJudgments <= 0)
            {
                return 0;
            }

            decimal sumWeights = 0m;
            sumWeights += (decimal)perfectCount * 1.0m;
            sumWeights += (decimal)greatCount * 0.9m;
            sumWeights += (decimal)goodCount * 0.5m;
            // missCount and failCount contribute 0

            decimal percent = 0m;
            try
            {
                percent = (sumWeights / (decimal)totalJudgments) * 100m;
            }
            catch { percent = 0m; }

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
            // New achievement calculation (decimal precision): accuracy-like.
            // We compute: (sum of weights) / (total judgments) * 100
            // Weights: perfect=1, great=0.9, good=0.5, miss/fail=0
            if (totalJudgments <= 0)
            {
                return 0m;
            }

            decimal sumWeights = 0m;
            sumWeights += (decimal)perfectCount * 1.0m;
            sumWeights += (decimal)greatCount * 0.9m;
            sumWeights += (decimal)goodCount * 0.5m;

            decimal percent = 0m;
            try
            {
                percent = (sumWeights / (decimal)totalJudgments) * 100m;
            }
            catch { percent = 0m; }

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
        if (float.IsNaN(offsetMs) || float.IsInfinity(offsetMs))
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
