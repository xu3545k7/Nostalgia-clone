using System.Collections.Generic;

namespace Judgment
{
    /// <summary>
    /// Nostalgia 判定保護：每幀按鍵消耗機制。
    ///
    /// 規則：
    ///   1. 每個物理按鍵在同一 Unity Frame 內只能觸發一次音符判定。
    ///      按鍵被「消耗」後，同幀再次觸發的事件會被丟棄。
    ///   2. 若兩個音符的 startTime 差距在 SharedInputThresholdMs 以內，
    ///      且它們的 lane 範圍都涵蓋該按鍵，則視為「共享輸入」——
    ///      兩者都判定，但按鍵「不被消耗」(100ms 例外)。
    /// </summary>
    public class InputProtection
    {
        // buttonId → Unity frameCount (the frame when this key was last consumed)
        private readonly Dictionary<int, int> _consumedFrame = new Dictionary<int, int>();
        // Per-lane protection deadline (ms). While songPos < blockedUntilMs[lane],
        // that lane is temporarily blocked from new judgments.
        private readonly float[] _blockedUntilMs = new float[32];

        /// <summary>
        /// 啟用「左右鄰近 lane」短時間停止判定。
        /// </summary>
        public bool EnableSpatialStop = true;

        /// <summary>
        /// 中心鍵左右各幾格一起套用停止判定（例：3 代表共 7 格）。
        /// </summary>
        public int SpatialStopRange = 3;

        /// <summary>
        /// 鄰近 lane 停止判定持續時間（ms）。
        /// </summary>
        public int SpatialStopWindowMs = 20;

        /// <summary>
        /// 兩音符 startTime 差距不超過此值 (ms) 且 lane 重疊時，
        /// 共享同一輸入並同時判定，不消耗按鍵。
        /// </summary>
        public int SharedInputThresholdMs = 100;

        // ── 查詢 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 回傳 true 代表此按鍵「本幀已被消耗」，應丟棄此次輸入。
        /// <para>建議由呼叫端快取 <c>Time.frameCount</c> 後傳入，避免重複 P/Invoke。</para>
        /// </summary>
        public bool IsConsumed(int buttonId, int frameCount)
        {
            return _consumedFrame.TryGetValue(buttonId, out int f) && f == frameCount;
        }

        /// <summary>
        /// 回傳 true 代表此 lane 仍在鄰近停止判定時間窗內，應丟棄此次輸入。
        /// </summary>
        public bool IsSpatiallyBlocked(int buttonId, float songPosMs)
        {
            if (!EnableSpatialStop) return false;
            if (buttonId < 0) return false;

            int center = buttonId;
            int r = SpatialStopRange;
            if (r < 0) r = 0;
            if (r > 31) r = 31;

            int minLane = center - r;
            if (minLane < 0) minLane = 0;
            int maxLane = center + r;
            if (maxLane > 31) maxLane = 31;

            for (int lane = minLane; lane <= maxLane; lane++)
            {
                if (songPosMs < _blockedUntilMs[lane]) return true;
            }
            return false;
        }

        // ── 寫入 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 將按鍵標記為「本幀已消耗」。
        /// <para>建議由呼叫端快取 <c>Time.frameCount</c> 後傳入，避免重複 P/Invoke。</para>
        /// </summary>
        public void Consume(int buttonId, int frameCount)
        {
            _consumedFrame[buttonId] = frameCount;
        }

        /// <summary>
        /// 將按鍵標記為本幀已消耗，並對中心鍵左右 SpatialStopRange 套用短時間保護窗。
        /// </summary>
        public void ConsumeWithSpatial(int buttonId, int frameCount, float songPosMs)
        {
            Consume(buttonId, frameCount);
            if (!EnableSpatialStop) return;
            if (buttonId < 0) return;

            int center = buttonId;
            int r = SpatialStopRange;
            if (r < 0) r = 0;
            if (r > 31) r = 31;

            int minLane = center - r;
            if (minLane < 0) minLane = 0;
            int maxLane = center + r;
            if (maxLane > 31) maxLane = 31;

            float until = songPosMs + SpatialStopWindowMs;
            for (int lane = minLane; lane <= maxLane; lane++)
            {
                if (until > _blockedUntilMs[lane]) _blockedUntilMs[lane] = until;
            }
        }

        // ── 重置 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 清除所有消耗紀錄（切換曲目時呼叫，避免跨曲攜帶殘留狀態）。
        /// </summary>
        public void Reset()
        {
            _consumedFrame.Clear();
            for (int i = 0; i < _blockedUntilMs.Length; i++) _blockedUntilMs[i] = 0f;
        }
    }
}
