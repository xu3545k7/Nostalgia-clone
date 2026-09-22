using System.Collections.Generic;
using UnityEngine;

namespace Judgment
{
    /// <summary>
    /// Nostalgia 音符選擇器：從候選清單中找出最適合給定按鍵的音符。
    ///
    /// 優先順序（與街機一致）：
    ///   1. 時間最近：|startTime − songPos| 最小，未到的音符再乘上
    ///      <see cref="NoteSelector.FutureNotePenalty"/>
    ///   2. 距離最近：|noteCenter − buttonId| 最小（noteCenter = (startLane+endLane)/2）
    ///   3. 位置最左：startLane 最小
    ///
    /// 只選取其 lane 範圍涵蓋 buttonId 且 delta ≤ goodMs 的音符。
    /// </summary>
    public static class NoteSelector
    {
        private const float Eps = 0.01f;

        /// <summary>
        /// How much further away a not-yet-due note counts compared with one
        /// already past. 1 keeps the original symmetric "nearest in time" rule.
        /// </summary>
        /// <remarks>
        /// Symmetric matching quietly rewrites late hits as early ones: press
        /// 80 ms after a note and the *next* note is often nearer than the one
        /// aimed at, so the press is credited forward and scored FAST. Measured
        /// on real sessions this drains the late side of the timing histogram
        /// (a cliff just past +25 ms) and piles up a long flat early tail,
        /// holding FAST to SLOW at six to nine to one — and no timing offset can
        /// correct it, because shifting the offset moves both sides together.
        ///
        /// Above 1 a future note has to be proportionally closer before it can
        /// take a press, so a late hit stays with the note it was meant for.
        /// Left at 1 by default: this changes how existing play scores, and that
        /// is the player's call rather than a silent correction.
        /// </remarks>
        /// <summary>
        /// **已無作用。** 排序改成照判定文件的「t0 最早優先」之後，這個加權失去意義
        /// （較早的音符本來就排在前面）。留著只是不動設定檔的 schema；要重新啟用的話
        /// 必須先想清楚它和時間優先級誰說了算。
        /// </summary>
        public static float FutureNotePenalty = 1f;

        /// <summary>
        /// How early a press may still claim a note, in ms. 0 disables the limit
        /// and keeps the window fully symmetric. Values below
        /// <see cref="MinEarlyClaimLimitMs"/> are raised to it.
        /// </summary>
        /// <remarks>
        /// Matching is first-come-first-served, so a note goes to whichever press
        /// enters its window first — and "first" is early by definition. Measured
        /// with EarlyClaimCorrector: a fifth of all hits were taken by a press ~84 ms
        /// early while a press ~49 ms closer arrived afterwards and found nothing
        /// left. No timing offset can undo that, since shifting the window moves
        /// both edges and the earliest press still wins inside it.
        ///
        /// Refusing the far-early claim lets the better press through. The cost
        /// is the claims with no better press behind them, which become misses
        /// instead of late-scoring hits — that is what the probe's lone counter
        /// measures, and why this ships off by default.
        /// </remarks>
        public static float EarlyClaimLimitMs = 0f;

        /// <summary>
        /// Floor for <see cref="EarlyClaimLimitMs"/>. Anything tighter would cut
        /// into the Perfect window and make notes unhittable rather than merely
        /// harder to claim early.
        /// </summary>
        public const float MinEarlyClaimLimitMs = 25f;

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
            // 優先級照判定文件 §3.2.3 的三層：**時間 > 距離 > 位置**。
            //
            // 第一層是「基準時間 t0 **更早**的音符優先」，不是「離現在最近的優先」。
            // 兩者只要有兩顆音符同時在窗內就會分歧：A 在 1000ms、B 在 1050ms，玩家在
            // 1040ms 按下——照文件是 A（先到判定線的先解決），照舊的 |delta| 是 B。
            // 全曲庫有 103716 對「同 lane 重疊且間隔 ≤150ms」的音符會走到這條規則。
            // 舊行為會讓玩家「跳過」較早那顆去吃比較近的，然後前一顆變 Miss。
            NoteController best     = null;
            float          bestStart = float.MaxValue;
            float          bestDist  = float.MaxValue;
            float          bestCenter = float.MaxValue;

            foreach (var n in candidates)
            {
                if (n == null || !n.IsActive || !n.IsJudgeable) continue;
                var nd = n.NoteData;
                if (nd == null) continue;
                // Slide and Trill are continuous gesture notes. Their lane
                // contacts are accumulated by JudgmentManager and must not be
                // consumed as ordinary one-shot taps.
                if (string.Equals(nd.type, "slide", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(nd.type, "trill", System.StringComparison.OrdinalIgnoreCase) ||
                    nd.note_type == 4 || nd.note_type == 64)
                    continue;

                // 按鍵必須在 note 的 lane 範圍內
                if (buttonId < nd.startLane || buttonId > nd.endLane) continue;

                float ahead = nd.startTime - songPos;   // >0 while the note is still coming
                float delta = Mathf.Abs(ahead);
                if (delta > goodMs) continue;
                // Too early to claim: the note stays alive for a later press
                // rather than being spent on this one.
                if (EarlyClaimLimitMs > 0f &&
                    ahead > Mathf.Max(EarlyClaimLimitMs, MinEarlyClaimLimitMs)) continue;
                // FutureNotePenalty 原本是用來偏向「還沒到的音符不要太早被吃掉」，
                // 那是在用 |delta| 排序時的近似。改成「t0 最早優先」之後，較早的音符
                // 本來就一定排在前面，這個加權失去意義，留著只會讓兩套規則互相干擾。

                // 按鍵與音符中心的 lane 距離
                float noteCenter = (nd.startLane + nd.endLane) * 0.5f;
                float dist       = Mathf.Abs(noteCenter - buttonId);

                bool better = false;
                if (nd.startTime + Eps < bestStart)
                {
                    // 第一層：基準時間更早的先解決（文件 §3.2.3 時間優先級）
                    better = true;
                }
                else if (nd.startTime - bestStart <= Eps)
                {
                    // 第二層：基準時間相同 → 離判定區中心更近的優先
                    if (dist + Eps < bestDist)
                    {
                        better = true;
                    }
                    else if (dist - bestDist <= Eps && noteCenter + Eps < bestCenter)
                    {
                        // 第三層：距離也相同 → 判定區中心更靠左的優先
                        better = true;
                    }
                }

                if (better)
                {
                    best       = n;
                    bestStart  = nd.startTime;
                    bestDist   = dist;
                    bestCenter = noteCenter;
                }
            }

            return best;
        }
    }
}
