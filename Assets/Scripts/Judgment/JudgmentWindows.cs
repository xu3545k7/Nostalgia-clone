/// <summary>
/// Pure judgment-window evaluation. No Unity / NoteController dependencies.
/// </summary>
public static class JudgmentWindows
{
    /// <summary>
    /// Map an absolute timing delta (ms) to a judgment result using the given windows.
    /// </summary>
    public static JudgmentResult Evaluate(float absDeltaMs, int perfectMs, int greatMs, int goodMs)
    {
        if (absDeltaMs <= perfectMs) return JudgmentResult.Perfect;
        if (absDeltaMs <= greatMs)   return JudgmentResult.Great;
        if (absDeltaMs <= goodMs)    return JudgmentResult.Good;
        return JudgmentResult.Miss;
    }

    /// <summary>
    /// Map an absolute timing delta (ms) to a judgment result for staccato tail release.
    /// The delta is clamped to >= 0 (release before press is treated as 0).
    /// </summary>
    public static JudgmentResult EvaluateStaccato(float releaseDeltaMs,
        int stacPerfectMs, int stacGreatMs, int stacGoodMs)
    {
        if (releaseDeltaMs < 0f) releaseDeltaMs = 0f;
        if (releaseDeltaMs <= stacPerfectMs) return JudgmentResult.Perfect;
        if (releaseDeltaMs <= stacGreatMs)   return JudgmentResult.Great;
        if (releaseDeltaMs <= stacGoodMs)    return JudgmentResult.Good;
        return JudgmentResult.Miss;
    }
}
