using System.Collections.Generic;
using UnityEngine;

namespace Judgment
{
    /// <summary>
    /// Nostalgia 音符選擇器：從候選清單中找出最適合給定按鍵的音符。
    ///
    /// 優先順序（與街機一致）：
    ///   1. 時間最近：|startTime − songPos| 最小
    ///   2. 距離最近：|noteCenter − buttonId| 最小（noteCenter = (startLane+endLane)/2）
    ///   3. 位置最左：startLane 最小
    ///
    /// 只選取其 lane 範圍涵蓋 buttonId 且 delta ≤ goodMs 的音符。
    /// </summary>
    public static class NoteSelector
    {
        private const float Eps = 0.01f;

        /// <summary>
        /// 從 <paramref name="candidates"/> 中選出最適合 <paramref name="buttonId"/> 的音符。
        /// </summary>
        /// <param name="buttonId">按下的按鍵（lane index）</param>
        /// <param name="songPos">目前歌曲時間 (ms)</param>
        /// <param name="goodMs">Good 判定窗口 (ms)，超出則排除</param>
        /// <param name="candidates">候選音符集合（通常為 GetNearbyNotes 回傳值）</param>
        public static NoteController FindBestNote(
            int buttonId,
            float songPos,
            int goodMs,
            IEnumerable<NoteController> candidates)
        {
            NoteController best     = null;
            float          bestDelta = float.MaxValue;
            float          bestDist  = float.MaxValue;
            int            bestLane  = int.MaxValue;

            foreach (var n in candidates)
            {
                if (n == null || !n.IsActive || !n.IsJudgeable) continue;
                var nd = n.NoteData;
                if (nd == null) continue;

                // 按鍵必須在 note 的 lane 範圍內
                if (buttonId < nd.startLane || buttonId > nd.endLane) continue;

                float delta = Mathf.Abs(nd.startTime - songPos);
                if (delta > goodMs) continue;

                // 按鍵與音符中心的 lane 距離
                float noteCenter = (nd.startLane + nd.endLane) * 0.5f;
                float dist       = Mathf.Abs(noteCenter - buttonId);

                bool better = false;
                if (delta + Eps < bestDelta)
                {
                    // 時間明顯更近
                    better = true;
                }
                else if (delta - bestDelta <= Eps)
                {
                    // 時間相近：比較距離
                    if (dist + Eps < bestDist)
                    {
                        better = true;
                    }
                    else if (dist - bestDist <= Eps && nd.startLane < bestLane)
                    {
                        // 距離也相近：選最左
                        better = true;
                    }
                }

                if (better)
                {
                    best      = n;
                    bestDelta = delta;
                    bestDist  = dist;
                    bestLane  = nd.startLane;
                }
            }

            return best;
        }
    }
}
