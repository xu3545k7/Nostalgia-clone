using UnityEngine;

namespace Judgment
{
    public class TapJudgment
    {
        public static JudgmentResult Judge(NoteController note, float songPos, int perfectMs, int greatMs, int goodMs)
        {
            var nd = note.NoteData;
            if (nd == null) return JudgmentResult.Miss;
            float delta = Mathf.Abs(nd.startTime - songPos);
            if (delta <= perfectMs) return JudgmentResult.Perfect;
            if (delta <= greatMs) return JudgmentResult.Great;
            if (delta <= goodMs) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }
    }
}
