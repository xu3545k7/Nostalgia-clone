using System;
using UnityEngine;

public class HoldNoteHandler : DefaultNoteHandler
{
    public struct HoldBeginRequest
    {
        public NoteController Note;
        public int ButtonId;
        public float PressSongPos;
        public float DeltaMs;
        public JudgmentResult? ForcedResult;
    }

    public struct HoldBeginInfo
    {
        public JudgmentResult Result;
        public bool UsePersistentMesh;

        public HoldBeginInfo(JudgmentResult result, bool usePersistentMesh)
        {
            Result = result;
            UsePersistentMesh = usePersistentMesh;
        }
    }

    public HoldNoteHandler() : base() { }
    public HoldNoteHandler(NoteController note) : base(note) { }
    public override JudgmentResult EvaluateHead(NoteController note, float songPos, float delta, int buttonId)
    {
        // reuse default head evaluation
        return base.EvaluateHead(note, songPos, delta, buttonId);
    }

    public virtual HoldBeginInfo BeginHold(HoldBeginRequest request)
    {
        if (request.Note == null)
        {
            return new HoldBeginInfo(JudgmentResult.Miss, false);
        }

        JudgmentResult result = request.ForcedResult ?? EvaluateHead(request.Note, request.PressSongPos, Mathf.Abs(request.DeltaMs), request.ButtonId);

        PlayHeadSound(request.Note, request.PressSongPos, result);

        bool persistentMesh = result != JudgmentResult.Miss;
        return new HoldBeginInfo(result, persistentMesh);
    }

    public override void OnBeginHold(NoteController note, int buttonId, float pressSongPos, float delta, JudgmentResult? forcedRes = null)
    {
        // default no-op; JudgmentManager still manages hold states
    }

    public override void OnFinalizeHold(NoteController note, float releaseSongPos, float pressSongPos)
    {
        // default no-op
    }
}
