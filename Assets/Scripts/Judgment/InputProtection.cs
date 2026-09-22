using System.Collections.Generic;

namespace Judgment
{
    /// <summary>
    /// 判定保護：按鍵消耗、接點抖動、以及本家的「判中後鄰鍵鎖」。
    /// </summary>
    /// <remarks>
    /// 鄰鍵鎖照本家 nostalgia.dll 的做法（PAN-001-2024102200_extracted/README.md §5）：
    /// 用第 k 號鍵判中一顆時間為 T 的音符之後，k−4 ～ k+4 這幾格鎖 3 幀，鎖住期間
    /// 它們**只**不能去打「比 T 晚 4 幀以上」的音符。
    ///
    /// 舊版是「左右 3 格停 20ms、擋所有音符」。兩者差在擋誰：舊版連和弦裡晚到的
    /// 手指、錯開一點的琶音都一起擋，所以才需要把附近音符的鍵道排除掉、再開一個
    /// 「和弦落鍵容許」讓玩家調。本家只擋**較晚的**音符，幾乎同時的鄰音永遠打得到，
    /// 也就不需要任何排除或調整。
    /// </remarks>
    public class InputProtection
    {
        // buttonId → Unity frameCount (the frame when this key was last consumed)
        private readonly Dictionary<int, int> _consumedFrame = new Dictionary<int, int>();
        // Positive ids identify physical press edges. This remains precise when
        // several queued inputs are drained during one Unity frame.
        private readonly Dictionary<int, long> _consumedEvent = new Dictionary<int, long>();

        private const int LaneSlots = 32;

        // 鄰鍵鎖：每一格在什麼時候被鎖（歌曲 ms），以及鎖它的那顆音符的時間。
        private readonly float[] _lockSetMs = new float[LaneSlots];
        private readonly float[] _lockAnchorMs = new float[LaneSlots];

        // 按下的那顆鍵本身的抖動保護，放開就解除。
        private readonly float[] _selfBlockedUntilMs = new float[LaneSlots];

        /// <summary>本家：判中那一鍵左右各幾格上鎖。</summary>
        public const int LockReach = 4;

        /// <summary>
        /// 不分幀的時候，鎖住多久（ms）。
        /// </summary>
        /// <remarks>
        /// 本家把計數設成 3，**每幀開頭先減 1 再檢查**，所以實際擋的是判中那一幀剩下的
        /// 時間加上後面 2 幀：從按下算起 33～50ms，看按在那一幀的哪個位置。以前這裡用
        /// 50ms，是整個範圍的最大值 —— 每一把鎖都比本家長。取平均 2.5 幀。
        /// </remarks>
        public const float LockDurationMs = 41.67f;

        /// <summary>本家：判中那一幀之後還鎖幾幀（計數 3，先減再查）。</summary>
        public const int LockExtraFrames = 2;

        /// <summary>
        /// 不分幀的時候，只擋比鎖定音符晚多少以上的音符（ms）。
        /// </summary>
        /// <remarks>
        /// 本家比的是整數幀：晚 5 幀以上才擋。兩個時間各自取整之後差 5 幀，實際間隔在
        /// 67～83ms 之間都有可能，機率隨間隔線性上升，取中間的 4.5 幀。分幀模式直接比幀。
        /// </remarks>
        public const float LockLaterThanMs = 75f;

        /// <summary>本家：音符晚 5 幀以上才擋。</summary>
        public const int LockLaterThanFrames = 5;

        /// <summary>接點抖動保護的時間，和鄰鍵鎖無關。</summary>
        public const float ChatterGuardMs = 50f;

        private const double MsPerFrame = 1000.0 / 60.0;

        // 加一點點再取整：剛好落在幀邊界上的時間（例如 1000ms）除完是 59.999…，
        // 會被捨到前一幀。
        private static int Frame(float ms) => (int)System.Math.Floor(ms / MsPerFrame + 1e-6);

        /// <summary>
        /// 按下的那顆鍵本身也擋一段時間，用來擋接點抖動。放開就解除。
        /// </summary>
        /// <remarks>
        /// Off restores the older behaviour, where the pressed lane was guarded
        /// only by press id and a bouncing contact reached judgment twice.
        /// </remarks>
        public bool EnableSelfStop = true;

        /// <summary>
        /// 兩音符 startTime 差距不超過此值 (ms) 且 lane 重疊時，
        /// 共享同一輸入並同時判定，不消耗按鍵。
        /// </summary>
        // Only absorb tiny timestamp/rounding differences inside an intended chord.
        // 100ms also swallowed genuine rapid notes (notably 80ms repetitions),
        // judging the later note with the earlier press and flooding FAST results.
        public int SharedInputThresholdMs = 20;

        public InputProtection()
        {
            Reset();
        }

        // ── 查詢 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 回傳 true 代表此按鍵「本幀已被消耗」，應丟棄此次輸入。
        /// </summary>
        public bool IsConsumed(int buttonId, int frameCount, long inputEventId = 0)
        {
            if (inputEventId > 0)
            {
                return _consumedEvent.TryGetValue(buttonId, out long consumedId) &&
                       consumedId == inputEventId;
            }
            return _consumedFrame.TryGetValue(buttonId, out int f) && f == frameCount;
        }

        /// <summary>這一鍵還在自己的抖動保護裡嗎。</summary>
        public bool IsChatterBlocked(int buttonId, float songPosMs, ISet<int> excludedLanes = null)
        {
            if (!EnableSelfStop) return false;
            if (buttonId < 0 || buttonId >= LaneSlots) return false;
            if (excludedLanes != null && excludedLanes.Contains(buttonId)) return false;
            return songPosMs < _selfBlockedUntilMs[buttonId];
        }

        /// <summary>
        /// 這一鍵現在能不能去打一顆時間為 <paramref name="noteStartMs"/> 的音符。
        /// </summary>
        /// <param name="frames">照本家的整數幀比（類原型）。false 用毫秒的平均值。</param>
        /// <param name="allowWithinMs">
        /// 這一下離音符已經這麼近（ms 以內）就不擋。0 = 照本家，一律擋。
        /// </param>
        /// <remarks>
        /// <paramref name="allowWithinMs"/> 是即時反饋自己的放寬，本家沒有。擦碰是從剛
        /// 打完的音符滑過去，對後面那顆通常早很多；已經進了 Great 範圍的一下，幾乎一定
        /// 是玩家真的要打它。
        /// </remarks>
        /// <returns>true 代表被鄰鍵鎖擋住。</returns>
        public bool IsLockedFor(int buttonId, float songPosMs, float noteStartMs,
            bool frames = false, float allowWithinMs = 0f)
        {
            if (buttonId < 0 || buttonId >= LaneSlots) return false;
            if (!LockHolds(buttonId, songPosMs, frames)) return false;
            if (allowWithinMs > 0f && noteStartMs - songPosMs <= allowWithinMs) return false;
            return IsLaterThanAnchor(buttonId, noteStartMs, frames);
        }

        /// <summary>在 <paramref name="atMs"/> 這個時刻，這一格的鎖還在不在。</summary>
        private bool LockHolds(int lane, float atMs, bool frames)
        {
            float setAt = _lockSetMs[lane];
            if (float.IsNegativeInfinity(setAt)) return false;
            return frames
                ? Frame(atMs) <= Frame(setAt) + LockExtraFrames
                : atMs < setAt + LockDurationMs;
        }

        private bool IsLaterThanAnchor(int lane, float noteStartMs, bool frames)
        {
            float anchor = _lockAnchorMs[lane];
            return frames
                ? Frame(noteStartMs) - Frame(anchor) >= LockLaterThanFrames
                : noteStartMs > anchor + LockLaterThanMs;
        }

        /// <summary>
        /// 一下在 <paramref name="pressMs"/> 按下的鍵，在它之後 <paramref name="windowMs"/>
        /// 之內，有沒有被別的判中鎖到而不該去打 <paramref name="noteStartMs"/> 那顆音符。
        /// </summary>
        /// <remarks>
        /// 給暫定判定回頭檢查用：擦碰先到、正主晚一點才判中上鎖時，那把鎖的時間
        /// 晚於擦碰，<see cref="IsLockedFor"/> 在按下的當下看不到它。觀察窗之後才
        /// 上的鎖不算，免得一幀卡頓把窗外的判中也算進來。
        /// </remarks>
        public bool WasLockedAfterPress(int buttonId, float pressMs, float windowMs, float noteStartMs,
            bool frames = false, float allowWithinMs = 0f)
        {
            if (buttonId < 0 || buttonId >= LaneSlots) return false;
            float setAt = _lockSetMs[buttonId];
            if (float.IsNegativeInfinity(setAt)) return false;
            if (setAt > pressMs + windowMs) return false;
            // 鎖比這一下早上的話，要看按下的那一刻鎖還在不在；比它晚上的，按下時當然
            // 還沒過期。
            if (setAt <= pressMs && !LockHolds(buttonId, pressMs, frames)) return false;
            if (allowWithinMs > 0f && noteStartMs - pressMs <= allowWithinMs) return false;
            return IsLaterThanAnchor(buttonId, noteStartMs, frames);
        }

        // ── 寫入 ────────────────────────────────────────────────────────────

        /// <summary>將這一次按下標記為已消耗。</summary>
        public void Consume(int buttonId, int frameCount, long inputEventId = 0)
        {
            if (inputEventId > 0)
                _consumedEvent[buttonId] = inputEventId;
            else
                _consumedFrame[buttonId] = frameCount;
        }

        /// <summary>按下的那一鍵進入抖動保護，直到放開或保護時間結束。</summary>
        public void GuardChatter(int buttonId, float songPosMs, ISet<int> excludedLanes = null)
        {
            if (!EnableSelfStop) return;
            if (buttonId < 0 || buttonId >= LaneSlots) return;
            if (excludedLanes != null && excludedLanes.Contains(buttonId)) return;
            float until = songPosMs + ChatterGuardMs;
            if (until > _selfBlockedUntilMs[buttonId]) _selfBlockedUntilMs[buttonId] = until;
        }

        /// <summary>
        /// 第 <paramref name="buttonId"/> 鍵判中了一顆時間為 <paramref name="anchorNoteMs"/>
        /// 的音符：左右 <see cref="LockReach"/> 格上鎖。
        /// </summary>
        /// <remarks>
        /// 本家是直接覆寫，不取較早或較晚的那一筆：鎖永遠跟著最近一次判中走。
        /// </remarks>
        public void LockNeighbours(int buttonId, float songPosMs, float anchorNoteMs)
        {
            if (buttonId < 0) return;
            int low = System.Math.Max(0, buttonId - LockReach);
            int high = System.Math.Min(LaneSlots - 1, buttonId + LockReach);
            for (int lane = low; lane <= high; lane++)
            {
                _lockSetMs[lane] = songPosMs;
                _lockAnchorMs[lane] = anchorNoteMs;
            }
        }

        /// <summary>
        /// Lifts the pressed lane's own protection because the key came up.
        /// </summary>
        /// <remarks>
        /// This is what separates contact bounce from a fast repeated note. A
        /// bounce re-fires without the key ever rising, so it stays blocked; a
        /// real re-strike must release first, and that clears the block however
        /// quickly the next press follows.
        /// </remarks>
        public void ReleaseLane(int lane)
        {
            if (lane < 0 || lane >= LaneSlots) return;
            _selfBlockedUntilMs[lane] = float.NegativeInfinity;
        }

        // ── 重置 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 清除所有消耗紀錄（切換曲目時呼叫，避免跨曲攜帶殘留狀態）。
        /// </summary>
        public void Reset()
        {
            _consumedFrame.Clear();
            _consumedEvent.Clear();
            for (int i = 0; i < LaneSlots; i++)
            {
                _lockSetMs[i] = float.NegativeInfinity;
                _lockAnchorMs[i] = 0f;
                _selfBlockedUntilMs[i] = float.NegativeInfinity;
            }
        }
    }
}
