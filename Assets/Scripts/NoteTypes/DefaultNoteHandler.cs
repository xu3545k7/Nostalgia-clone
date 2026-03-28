using System;
using UnityEngine;

public class DefaultNoteHandler : INoteHandler
{
    public DefaultNoteHandler() { }
    public DefaultNoteHandler(NoteController note) { }
    public virtual JudgmentResult EvaluateHead(NoteController note, float songPos, float delta, int buttonId)
    {
        // default: follow JudgmentManager thresholds via a simple delta-based mapping.
        if (SettingsManager.Instance != null && SettingsManager.Instance.DebugMode)
        {
            return JudgmentResult.Perfect;
        }

        float timingOffsetMs = 0f;
        try
        {
            var nd = note != null ? note.NoteData : null;
            if (nd != null)
            {
                timingOffsetMs = songPos - nd.startTime;
            }
        }
        catch { }

        var jm = JudgmentResult.Good;
        if (delta <= JudgmentManagerStatic.GetPerfectMs()) jm = JudgmentResult.Perfect;
        else if (delta <= JudgmentManagerStatic.GetGreatMs()) jm = JudgmentResult.Great;
        else jm = JudgmentResult.Good;
        return jm;
    }

    public virtual void PlayHeadSound(NoteController note, float songPos, JudgmentResult res)
    {
        if (res == JudgmentResult.Miss) return;
        var mgr = HitSoundManager.Instance ?? HitSoundManager.EnsureInstance();
        mgr?.PlayHitSound();
    }

    public virtual void OnAutoJudge(NoteController note)
    {
        // default show: do nothing extra
    }

    public virtual void OnAutoStartHold(NoteController note, float pressSongPos)
    {
    }

    public virtual void OnBeginHold(NoteController note, int buttonId, float pressSongPos, float delta, JudgmentResult? forcedRes = null)
    {
    }

    public virtual void OnFinalizeHold(NoteController note, float releaseSongPos, float pressSongPos)
    {
    }

    public virtual JudgmentResult EvaluateAutoJudgeTap(NoteController note, float songPos)
    {
        return JudgmentResult.Perfect;
    }

    public virtual JudgmentResult EvaluateAutoStartHold(NoteController note, float pressSongPos)
    {
        return JudgmentResult.Perfect;
    }

    public virtual JudgmentResult EvaluateHoldEnd(NoteController note, float releaseSongPos, float pressSongPos)
    {
        if (note == null) return JudgmentResult.Miss;
        var nd = note.NoteData;
        if (nd == null) return JudgmentResult.Miss;

        float heldMs = (releaseSongPos - pressSongPos);
        float totalMs = Mathf.Max(1f, nd.endTime - nd.startTime);
        JudgmentResult finalRes;

        if (note != null && note.IsSoft)
        {
            finalRes = JudgmentResult.Perfect;
        }
        else if (totalMs < 50f)
        {
            finalRes = JudgmentResult.Perfect;
        }
        else if (heldMs >= 0.8f * totalMs - 50f)
        {
            finalRes = JudgmentResult.Perfect;
        }
        else if (heldMs >= 0.5f * totalMs - 50f)
        {
            finalRes = JudgmentResult.Great;
        }
        else
        {
            finalRes = JudgmentResult.Good;
        }
        return finalRes;
    }

    public virtual JudgmentResult EvaluateUnhandled(NoteController note, float songPos, bool anyKeyDown)
    {
        if (note != null && note.IsSoft)
        {
            return JudgmentResult.Perfect;
        }
        float delta = Mathf.Abs((note != null && note.NoteData != null ? note.NoteData.startTime : 0f) - songPos);
        if (delta <= JudgmentManagerStatic.GetGoodMs() && anyKeyDown)
        {
            return JudgmentResult.Perfect;
        }
        return JudgmentResult.Miss;
    }
}
