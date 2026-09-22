using UnityEngine;

namespace Judgment
{
    public class StaccatoJudgment
    {
        public static JudgmentResult Judge(NoteController note, float releaseTime, float stacPerfectMs, float stacGreatMs, float stacGoodMs)
        {
            var nd = note.NoteData;
            if (nd == null) return JudgmentResult.Miss;
            // 尾判定以「譜面標註的結束時間 endTime」為基準，而非 startTime。
            // 這樣玩家照著畫出來的長度按住到底再放，屬於正確（rel≈0 → Perfect）。
            // 提早放開（在 endTime 之前，含快放、以及自動放開 start+stacAutoReleaseMs）一律不罰，
            // clamp 為 0 → Perfect；只有「拖過 endTime 太久」才依窗口降級。
            // 修正前用 startTime，會讓長 staccato 因按住到標註長度反而被判 Miss（違反直覺）。
            float rel = releaseTime - nd.endTime;
            if (rel < 0f) rel = 0f;
            if (rel <= stacPerfectMs) return JudgmentResult.Perfect;
            if (rel <= stacGreatMs) return JudgmentResult.Great;
            if (rel <= stacGoodMs) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }
    }
}
