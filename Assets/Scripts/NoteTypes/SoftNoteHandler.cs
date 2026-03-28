using System;
using UnityEngine;

// SoftNoteHandler: soft notes always count as Perfect for head and final.
// Inherit DefaultNoteHandler so we reuse the default stat-updating behavior.
public class SoftNoteHandler : DefaultNoteHandler
{
    public SoftNoteHandler() : base() { }
    public SoftNoteHandler(NoteController note) : base(note) { }
    public override JudgmentResult EvaluateHead(NoteController note, float songPos, float delta, int buttonId)
    {
        // Soft notes always count as Perfect for head
        float timingOffsetMs = 0f;
        try
        {
            var nd = note != null ? note.NoteData : null;
            if (nd != null)
            {
                timingOffsetMs = songPos - nd.startTime;
            }
        }
        catch { timingOffsetMs = 0f; }

        return JudgmentResult.Perfect;
    }

    public override JudgmentResult EvaluateHoldEnd(NoteController note, float releaseSongPos, float pressSongPos)
    {
        return JudgmentResult.Perfect;
    }

    public override void PlayHeadSound(NoteController note, float songPos, JudgmentResult res)
    {
        if (res == JudgmentResult.Miss) return;
        var mgr = HitSoundManager.Instance ?? HitSoundManager.EnsureInstance();
        mgr?.PlayHitSound();
    }
}
