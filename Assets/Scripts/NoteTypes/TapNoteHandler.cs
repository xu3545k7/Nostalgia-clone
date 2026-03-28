using System;
using UnityEngine;

public class TapNoteHandler : DefaultNoteHandler
{
    public TapNoteHandler() : base() { }
    public TapNoteHandler(NoteController note) : base(note) { }
    public override JudgmentResult EvaluateHead(NoteController note, float songPos, float delta, int buttonId)
    {
        // tap notes follow default behavior
        return base.EvaluateHead(note, songPos, delta, buttonId);
    }

    public override void OnAutoJudge(NoteController note)
    {
        // no-op; default behavior handled in EvaluateAutoJudgeTap
    }
}
