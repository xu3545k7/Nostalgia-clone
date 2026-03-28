using System;
using UnityEngine;

public interface INoteHandler
{
    // Evaluate the head timing and return the JudgmentResult for a head press.
    // Implementations should be pure (no StatsManager mutations); JudgmentManager will finalize stats.
    // delta is absolute time difference in ms between note start and songPos.
    JudgmentResult EvaluateHead(NoteController note, float songPos, float delta, int buttonId);

    // Play the head hit sound (if any) for this judgment. Implementations should call HitSoundManager.
    void PlayHeadSound(NoteController note, float songPos, JudgmentResult res);

    // Evaluate an auto-judged tap (DebugMode). Should return the result without mutating StatsManager.
    JudgmentResult EvaluateAutoJudgeTap(NoteController note, float songPos);

    // Evaluate an auto-started hold (DebugMode). Should return the head result without mutating StatsManager.
    JudgmentResult EvaluateAutoStartHold(NoteController note, float pressSongPos);

    // Evaluate a finalized hold (called when hold ends). Should compute final result and return it; stats are handled externally.
    JudgmentResult EvaluateHoldEnd(NoteController note, float releaseSongPos, float pressSongPos);

    // Evaluate an unhandled note (timed out without judgement). Should compute final result (e.g., Miss or Perfect for soft)
    // without touching StatsManager; caller will update stats.
    JudgmentResult EvaluateUnhandled(NoteController note, float songPos, bool anyKeyDown);

    // Optional lifecycle hooks (no-ops by default)
    void OnBeginHold(NoteController note, int buttonId, float pressSongPos, float delta, JudgmentResult? forcedRes = null);

    void OnFinalizeHold(NoteController note, float releaseSongPos, float pressSongPos);
    
    // Hooks for DebugMode auto flows
    void OnAutoJudge(NoteController note);
    void OnAutoStartHold(NoteController note, float pressSongPos);
}
