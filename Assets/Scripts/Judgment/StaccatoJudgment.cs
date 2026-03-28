using UnityEngine;

namespace Judgment
{
    public class StaccatoJudgment
    {
        public static JudgmentResult Judge(NoteController note, float releaseTime, float stacPerfectMs, float stacGreatMs, float stacGoodMs)
        {
            var nd = note.NoteData;
            if (nd == null) return JudgmentResult.Miss;
            float rel = releaseTime - nd.startTime;
            if (rel < 0f) rel = 0f;
            if (rel <= stacPerfectMs) return JudgmentResult.Perfect;
            if (rel <= stacGreatMs) return JudgmentResult.Great;
            if (rel <= stacGoodMs) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }
    }
}
