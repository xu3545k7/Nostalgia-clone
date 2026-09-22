using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Judgment
{
[DefaultExecutionOrder(100)]
public class JudgmentManager : MonoBehaviour
{
    public static JudgmentManager Instance;

    private readonly System.Collections.Generic.HashSet<NoteController> activeNotes = new System.Collections.Generic.HashSet<NoteController>();

        // Cached視窗內的音符清單，避免每幀/每鍵反覆全域掃描 activeNotes
        private readonly System.Collections.Generic.List<NoteController> nearbyNotesScratch = new System.Collections.Generic.List<NoteController>();
        private readonly System.Collections.Generic.HashSet<int> spatialStopExcludedLanesScratch =
            new System.Collections.Generic.HashSet<int>();
        private float nearbyNotesCachedSongPos = float.MinValue;
        private int nearbyNotesCachedFrame = -1;
        private const float NearbyCacheEpsMs = 5f; // songPos 變化超過此值才重建清單

        private class ActiveHoldState
        {
            public NoteController note;
            public float pressSongPos;
            public System.Collections.Generic.HashSet<int> pressedLanes = new System.Collections.Generic.HashSet<int>();
            public float headInputSongPosMs;
            public float headTargetTimeMs;
            public float headAbsoluteOffsetMs;
            public bool hasHeadTiming = false;
            public bool headJudgmentCommitted = false;
            public bool headJudgmentPending = false;
            public System.Collections.IEnumerator pendingFinalizeCoroutine = null;
            public bool autoStarted = false;
            // allow a short grace window to swap fingers (release then press another lane)
            // before finalizing tail; stores absolute songPos ms deadline
            public float pendingSwapDeadline = float.MinValue;
            // Extra hold judgment tracking
            public int maxExtra = 0; // maximum extra Perfects allowed for this hold
            public int awardedExtra = 0; // how many extras have been awarded so far (includes Miss/Good/Great awarded)
            public int pressedAwardedExtra = 0; // how many extras were awarded while actually pressed (counts towards percentage)
            public float lastExtraAwardSongPosMs = float.MinValue; // last award time
            public float eighthMs = 0f; // computed from bpm for this hold
        }

        private readonly System.Collections.Generic.Dictionary<NoteController, ActiveHoldState> activeHoldStates = new System.Collections.Generic.Dictionary<NoteController, ActiveHoldState>();

        [Header("Slide / Trill")]
        [Tooltip("Trill judgment interval. Every interval must contain at least one new input.")]
        [SerializeField, Min(0.05f)] private float trillMaximumInputGapSeconds = 0.125f;
        [Tooltip("Small grace after a slide segment reaches its end, in milliseconds.")]
        [SerializeField, Min(0f)] private float slideContactGraceMs = 35f;
        [Tooltip("Minimum interval between gesture contact flashes, in milliseconds.")]
        [SerializeField, Min(0f)] private float gestureEffectCooldownMs = 75f;

        private sealed class ActiveGestureState
        {
            public NoteController note;
            public bool touched;
            public bool failed;
            public float lastInputSongPosMs = float.MinValue;
            public float lastEffectSongPosMs = float.MinValue;
            public int lastLane = -1;
            public int trillTotalTicks;
            public int trillAwardedTicks;
            public bool trillHeadJudgmentQueued;
            public bool trillHasAcceptedInput;
            public float trillLastAcceptedInputSongPosMs = float.MinValue;
            public float trillFailureSongPosMs = float.MinValue;
            public int trillLastFeedbackTick = -1;
        }

        private readonly System.Collections.Generic.Dictionary<NoteController, ActiveGestureState> activeGestureStates =
            new System.Collections.Generic.Dictionary<NoteController, ActiveGestureState>();
        private readonly System.Collections.Generic.List<NoteController> gestureNotesScratch =
            new System.Collections.Generic.List<NoteController>();
        private readonly System.Collections.Generic.Dictionary<int, NoteData> slideNotesByIndex =
            new System.Collections.Generic.Dictionary<int, NoteData>();
        private Chart indexedSlideChart;
        // Track last check time for holds that are not yet being tracked so we can
        // poll them at the same per-beat frequency as hold extras and start them
        // when the player is physically pressing the lane(s).
        private readonly System.Collections.Generic.Dictionary<NoteController, float> untrackedHoldLastCheck = new System.Collections.Generic.Dictionary<NoteController, float>();
        // Nostalgia-style judgment protection — per-frame key consumption & co-judge exception.
        // Logic extracted into InputProtection; JudgmentManager delegates all queries/writes here.
        private readonly InputProtection _inputProtection = new InputProtection();
        // Cached once per Update() to avoid per-loop singleton + property access inside ProcessHoldExtraAwards
        // and FinalizeExpiredHolds (those iterate over all active hold states every frame).
        private bool _cachedDebugMode;
        private bool _cachedRecitalMode;
        private bool autoPlayUsedThisRun;
        public bool AutoPlayUsedThisRun => autoPlayUsedThisRun;
        // HUD refresh dirty flag: set by TryIncrementStats, flushed once at end of Update().
        // Prevents N TextMeshPro mesh rebuilds when N notes are judged in the same frame.
        private bool _hudDirty;
        private bool _inputTimingPathLogged;
        private int _timedInputCount;
        private int _fallbackInputCount;
        private double _inputProjectionSumMs;
        private float _inputProjectionMinMs = float.MaxValue;
        private float _inputProjectionMaxMs = float.MinValue;
        // Last-written HUD values — skip text assignment when unchanged (avoids TMP mesh rebuild).
        private int    _lastHudCombo  = -1;

        // combo 的兩個動作：加一次跳一下，斷掉快速淡出。
        private const float ComboPunchTime = 0.13f;
        private const float ComboHopHeight = 14f;
        private const float ComboFadeTime = 0.16f;
        /// <summary>The score's face and rim; one definition, shared with the page.</summary>
        private static Color ScoreFace => ClassicalBookUITheme.ScoreFace;
        private static Color ScoreShadow => ClassicalBookUITheme.ScoreShadow;
        private float _comboPunch;
        private bool _comboHopped;
        private float _comboAlpha = 1f;
        private int    _lastHudScore  = -1;
        private int    _lastHudFast   = -1;
        private int    _lastHudSlow   = -1;
        private bool   _lastHudWasSettingsPreview;
        private string _lastHudAchStr = null;
        /// <summary>
        /// Two nearly simultaneous notes within this time window (ms) on overlapping lanes share one input;
        /// both are judged and the key is NOT consumed (Inspector-editable).
        /// </summary>
        public int sharedInputThresholdMs
        {
            get => _inputProtection.SharedInputThresholdMs;
            set => _inputProtection.SharedInputThresholdMs = Mathf.Clamp(value, 0, 20);
        }
        /// <summary>Whether a bouncing contact on the pressed key is filtered out.</summary>
        public bool KeyChatterGuard
        {
            get => _inputProtection == null || _inputProtection.EnableSelfStop;
            set { if (_inputProtection != null) _inputProtection.EnableSelfStop = value; }
        }
        private readonly System.Collections.Generic.Dictionary<int, ActiveHoldState> laneToHoldState = new System.Collections.Generic.Dictionary<int, ActiveHoldState>();

        // Scratch lists used when iterating/modifying collections
        /// <summary>
        /// 還沒決定是不是「誤觸」的按鍵。
        /// </summary>
        /// <remarks>
        /// 本家把同一個 16.66ms 操作幀內按下的所有鍵一起處理：某顆音符被判定之後，
        /// 同一幀內落在它 (判定成功區 ∪ Near 區) 而沒有打中別的音符的按鍵就落空
        /// ——不扣分、不出聲、也不干擾判定（README §5）。Near 區是音符左右各 3 條。
        ///
        /// 逐鍵即時判定沒有幀，所以**打不中任何東西的按鍵**先扣住：
        /// <see cref="AbsorbWindowMs"/> 內有鄰近音符被判定就把它吃掉，否則才
        /// 認定是真的誤觸。真正打中的音符判定時機完全不變。
        /// </remarks>
        private struct PendingStray
        {
            public int lane;
            public float songPosMs;
            public float realtime;
        }

        private readonly System.Collections.Generic.List<PendingStray> _pendingStrays =
            new System.Collections.Generic.List<PendingStray>();

        /// <summary>原版的操作幀長度。</summary>
        private const float OperationFrameMs = 16.66f;

        /// <summary>
        /// 事件驅動時，多餘的按鍵在判定前後多久內會被吃掉。
        /// </summary>
        /// <remarks>
        /// 本家把同一幀的按鍵一起處理，幀就是吸收的範圍。逐鍵即時判定沒有幀，
        /// 只好用一段固定時間代替：取本家鄰鍵鎖的長度 3 幀。
        ///
        /// 兩個方向都要等：擦碰可能比正主早到（先扣住，等正主判定來吃），也可能
        /// 晚到（正主剛判定，直接吃）。
        /// </remarks>
        private const float EventAbsorbWindowMs = 50f;

        /// <summary>
        /// 類原型：照本家的規則模擬，但不把輸入降到 60fps —— 按下就判定，不等幀。
        /// </summary>
        /// <remarks>
        /// 設定裡仍叫「輸入幀：完整」。本家的幀在這裡只剩一個意義：擦碰觀察窗與和弦
        /// 共用時間都是一幀長。只讓聲音等幀的模式判定是逐鍵的，走即時反饋那一套。
        /// </remarks>
        private static bool UseClassicSimulation => UseInputFrameBatching && !UseSoundOnlyFrame;

        /// <summary>
        /// 擦碰觀察窗：一下按鍵在多久之內還可能被認定成「別的鍵滑過去的餘波」。
        /// </summary>
        /// <remarks>
        /// 類原型用本家的一幀（本家只有同一幀的按鍵會互相影響）；即時反饋沒有幀，
        /// 用鄰鍵鎖的 3 幀，多等一點換更少漏網。
        /// </remarks>
        private static float AbsorbWindowMs => UseClassicSimulation ? OperationFrameMs : EventAbsorbWindowMs;

        /// <summary>
        /// 鄰鍵鎖對「已經離音符很近」的按鍵放行的範圍。類原型照本家一律擋（0）；
        /// 即時反饋放行進了 Great 範圍的一下。
        /// </summary>
        /// <remarks>
        /// 實測（2026-09-14 兩場）鄰鍵鎖是吃掉最多按鍵的一層：即時反饋一場擋 1898 下，
        /// 其中 449 下原本要打的音符最後 Miss。擦碰是從剛判中的音符滑過去，對後面那顆
        /// 早得多；真的要打它的那一下多半已經落在 Great 範圍裡。
        /// </remarks>
        private float LockAllowWithinMs => UseClassicSimulation ? 0f : greatMs;

        /// <summary>一下沒配對到的按鍵，附近又有音符可能來吃掉它時，要等多久才出錯音。</summary>
        private static float StrayHoldMs => AbsorbWindowMs;

        /// <summary>一顆被按鍵判掉的音符周圍（判定區 ∪ Near 區），在吸收窗內會吃掉多餘的按鍵。</summary>
        private struct ClaimedSpan
        {
            /// <summary>判定區 ∪ Near 區。</summary>
            public int Low;
            public int High;
            /// <summary>判定區本身。</summary>
            public int ExactLow;
            public int ExactHigh;
            /// <summary>被拿走的那顆音符的時間。</summary>
            public float StartTime;
            /// <summary>被拿走的時刻（realtime 秒）。「候選被搶」只看這之後一幀。</summary>
            public float ClaimedAt;
            public float Until;
        }

        private readonly System.Collections.Generic.List<ClaimedSpan> _claimedSpans =
            new System.Collections.Generic.List<ClaimedSpan>(16);

        /// <summary>Near 區：音符判定區左右各幾條 lane。</summary>
        private const int NearReach = 3;

        private readonly System.Collections.Generic.List<NoteController> toFinalizeScratch = new System.Collections.Generic.List<NoteController>();
        private readonly System.Collections.Generic.List<NoteController> toAutoFinalizeScratch = new System.Collections.Generic.List<NoteController>();
        private readonly System.Collections.Generic.Dictionary<NoteController, float> autoHeldNotes = new System.Collections.Generic.Dictionary<NoteController, float>();
        // Throttle for the untracked-hold scan.
        // Interval is derived from BPM (one eighth-note = 30000/BPM ms) so the scan fires
        // at most once per hold-extra combo opportunity, not every frame.
        private float _lastUntrackedHoldCheckMs = float.MinValue;
        // Cached interval (ms); refreshed each time a scan fires so it tracks live BPM.
        private float _untrackedHoldCheckIntervalMs = 250f; // safe fallback (BPM 120 eighth = 250ms)
        // Object pool for JudgmentPayload to eliminate per-judgment heap allocations.
        // With ProtectionEnabled=false all payloads are consumed synchronously, so it is safe
        // to return them at the end of ApplyJudgmentPayload.
        private readonly System.Collections.Generic.Stack<JudgmentPayload> _payloadPool =
            new System.Collections.Generic.Stack<JudgmentPayload>(32);
        // Per-note INoteHandler cache: handler type never changes for a given note once spawned,
        // so we create it once and reuse it rather than allocating a new object every judgment.
        private readonly System.Collections.Generic.Dictionary<NoteController, INoteHandler> _handlerCache =
            new System.Collections.Generic.Dictionary<NoteController, INoteHandler>(64);
        // Compatibility fields expected by legacy callers
        // 對應原版三階：Perfect=PJust(±41)、Great=Just(±82)、Good=Good(±108)。
        // SettingsManager 啟動時會推真正的值進來，這裡是場景還沒把設定接上時的
        // 後備值——兩邊不一致會出現「設定看起來是 108、實際跑 150」這種很難查的落差。
        public int perfectMs = 41;   // PJust
        public int greatMs = 82;     // Just
        public int goodMs = 108;     // Good
        // grace window (ms) allowing quick finger swap without finalizing hold
        public int swapGraceMs = 80;
        // Hold "break tolerance" (ms): a release SHORTER than this is treated as if the hold is still
        // held — no per-beat Miss, no combo break, and the hold does not finalize — so small finger
        // blips don't punish the player. It also serves as the release grace before finalization.
        // Kept intentionally lenient; tune in the Inspector. Effective grace is max(goodMs, this).
        public int holdBreakToleranceMs = 400;
        // Staccato-specific timing windows (ms) — default: start+70/130/200
        public int stacPerfectMs = 70;
        public int stacGreatMs = 130;
        public int stacGoodMs = 200;
        // Auto-release offset for staccato when auto-started (ms)
        public int stacAutoReleaseMs = 60;
        // If true, when a staccato was auto-started, force the release time used for
        // tail judgment to be the auto-release (start + stacAutoReleaseMs) instead
        // of the actual release time. Players who hold should still miss as normal.
        public bool autoSimulateStaccatoRelease = true;

        [Header("HUD (optional)")]
        [SerializeField]
        private UnityEngine.UI.Text comboText = null;
        [SerializeField]
        private UnityEngine.UI.Text scoreText = null;
        [SerializeField]
        private TMPro.TextMeshProUGUI comboTMP = null;
        [SerializeField]
        private TMPro.TextMeshProUGUI scoreTMP = null;
        [SerializeField]
        private TMPro.TextMeshProUGUI achievementTMP = null;
        private Canvas classicalHudCanvas;
        private TextMeshProUGUI screenComboTMP;
        private TextMeshPro trackComboTMP;
        private Canvas trackComboWorldCanvas;
        private TextMeshProUGUI trackComboWorldText;
        private Transform trackComboSurface;
        private ComboDisplayPosition lastComboDisplayPosition = (ComboDisplayPosition)(-1);
        private float lastComboFontSize = -1f;
        private float lastTrackComboScreenHeight = -1f;
        private bool classicalHudVisibleRequested = true;
        private readonly List<float> settingsPreviewComboTimings = new List<float>();
        private int settingsPreviewCombo;
        private int settingsPreviewComboCursor;
        private bool settingsPreviewComboActive;

        // Minimal compatibility stubs — these call into existing managers where appropriate
        public void ResetStats()
        {
            try { StatsManager.Instance?.ResetStats(); } catch { }

            // 力度的基準每一場重來。換一首歌、換一個人、換一台琴都該重新認識，
            // 而且上一場的統計留著只會讓這一場的第一行 log 讀不出意思。
            PlayerTouch.Reset();
            _claimedSpans.Clear();
            SettleAllProvisionalPresses(false);
            _recentTapPresses.Clear();
            EatenInputProbe.ResetSong();
            _pendingMelodyChecks.Clear();
            System.Array.Clear(_melodyPresses, 0, _melodyPresses.Length);
            _mistouchIndexChart = null;
            dynamicUnmarked = 0;
            dynamicError = 0f;
            dynamicNoBands = 0;
            dynamicNoChartVelocity = 0;
            dynamicNoInput = 0;
            dynamicJudged = 0;
            dynamicMissed = 0;
            dynamicReported = 0;
            autoPlayUsedThisRun = SettingsManager.Instance != null &&
                                  SettingsManager.Instance.DebugModeInPlay;
            // Invalidate HUD cache so the reset values (0/0) are written even if they
            // match the previous song's values, then flush immediately.
            _lastHudCombo  = -1;
            _lastHudScore  = -1;
            _lastHudFast   = -1;
            _lastHudSlow   = -1;
            _lastHudWasSettingsPreview = false;
            _lastHudAchStr = null;
            _hudDirty      = true;
            _inputTimingPathLogged = false;
            _timedInputCount = 0;
            _fallbackInputCount = 0;
            try { EarlyClaimCorrector.Reset(); } catch { }
            _inputProjectionSumMs = 0.0;
            _inputProjectionMinMs = float.MaxValue;
            _inputProjectionMaxMs = float.MinValue;
            try { RefreshHudStats(); } catch { }
            // Clear per-frame consumption map so no leftover state carries across songs.
            try { _inputProtection.Reset(); } catch { }
            activeGestureStates.Clear();
        }

        public void EnsureHudVisible()
        {
            // keep compatibility; hud visibility is handled by UI managers
        }

        public void RecordFail(NoteController n)
        {
            if (n == null) return;
            try
            {
                var songPos = GetSongPositionMs();
                ProcessOverlappedJudgmentSuppressions(songPos);
                if (n.IsJudgmentSuppressed) return;
                var nd = n.NoteData;
                // RentPayload() pulls from pool — RecordFail is called from note timeouts.
                var payload = RentPayload();
                payload.note = n;
                payload.result = JudgmentResult.Miss; // treat as Miss so stats increment correctly
                payload.buttonId = nd != null ? nd.startLane : -1;
                payload.inputSongPosMs = songPos;
                payload.pressSongPosMs = 0f;
                payload.releaseSongPosMs = 0f;
                payload.targetTimeMs = nd != null ? nd.startTime : songPos;
                payload.absoluteOffsetMs = Mathf.Abs((nd != null ? nd.startTime : songPos) - songPos);
                payload.triggerPopup = true;
                payload.triggerMesh = false;
                payload.triggerHitSound = false;
                payload.isHoldHead = false;
                payload.isHoldTail = false;
                payload.usePersistentMesh = false;
                payload.hasHeadTiming = nd != null;
                // Route through protection so misses respect protection windows and pending logic
                Protection.ResolveHeadJudgment(n, payload, songPos, true, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
            }
            catch { }
        }

        public void AutoStartHold(NoteController n, float startSongPos)
        {
            if (n == null) return;
            try
            {
                if (AutoFor(n)) IvoryLaneKeyboard.HoldForNote(n);
                try { n.BeginStreamingJudgment(startSongPos); } catch { }
                // Simulate a head press for hold start flows: play sound, effect and notify note
                ApplyJudgmentForHoldStart(n);
                // Ensure an active hold state exists for auto-started holds so the manager
                // can finalize the tail when endTime passes (auto-play / debug flows).
                if (!activeHoldStates.TryGetValue(n, out var st))
                {
                    st = new ActiveHoldState { note = n, pressSongPos = startSongPos, autoStarted = true };
                    try {
                        var nd = n.NoteData;
                        float bpm = 120f;
                        try { if (GameManager.Instance != null && GameManager.Instance.Conductor != null) bpm = GameManager.Instance.Conductor.bpm; } catch { }
                        if (nd != null && nd.startTime < nd.endTime)
                        {
                            var info = HoldJudgment.ComputeExtraInfo(nd, bpm);
                            st.maxExtra = info.maxExtra;
                            st.eighthMs = info.eighthMs;
                        }
                    } catch { }
                    activeHoldStates[n] = st;
                    // For auto-play, simulate the lane(s) being pressed so extras award as JUST
                    try
                    {
                        var nd2 = n.NoteData;
                        if (nd2 != null)
                        {
                            int startLane = nd2.startLane;
                            int endLane = nd2.endLane >= startLane ? nd2.endLane : startLane;
                            for (int lane = startLane; lane <= endLane; lane++)
                            {
                                st.pressedLanes.Add(lane);
                                laneToHoldState[lane] = st;
                            }
                        }
                    }
                    catch { }
                }
                    if (_cachedDebugMode) BuildLogger.Log($"[JMgr] AutoStartHold note={n?.name ?? "<null>"} pressSongPos={startSongPos} maxExtra={st.maxExtra} eighthMs={st.eighthMs}");
            }
            catch { }
        }

        public void AutoJudgeTap(NoteController n)
        {
            if (n == null) return;
            try
            {
                if (AutoFor(n)) IvoryLaneKeyboard.PulseForNote(n);
                var songPos = GetSongPositionMs();
                ProcessOverlappedJudgmentSuppressions(songPos);
                if (n.IsJudgmentSuppressed) return;
                var nd = n.NoteData;
                var payload = RentPayload();
                payload.note = n;
                payload.result = JudgmentResult.Perfect;
                // 自動演奏本來拿的是音符的起始格，所以三格寬的音符永遠不在中心，
                // 演奏會模式下就永遠看不到 PRECISE。自動演奏是「完美地彈完一次」
                // 的樣子，它按的當然是正中央那一格。
                payload.buttonId = nd != null
                    ? IvoryLaneKeyboard.ResolveDebugLane(nd.startLane, nd.endLane)
                    : -1;

                // 力度也一樣：自動演奏用**譜面自己要的那個力度**下鍵。
                //
                // 不記的話判定端拿不到觸鍵力度，走的是「判不了就放行」那條 ——
                // 於是自動演奏永遠是 PRECISE，而那個滿分是「沒有資料」不是「彈得
                // 對」。兩者在結算畫面上長得一模一樣，所以拿自動演奏測評分就永遠
                // 測不出東西來。
                //
                // 這和上面那個中心格是同一個修法：自動演奏是「完美地彈完一次」的
                // 樣子，它該在每一個被評分的維度上都真的做對，而不是在每一個維度
                // 上都剛好被跳過。
                if (nd != null && nd.velocity > 0)
                {
                    try
                    {
                        PianoKeysound.RecordInput(payload.buttonId, -1,
                            Mathf.Clamp01(nd.velocity / 127f), true);
                    }
                    catch { }
                }
                payload.inputSongPosMs = songPos;
                payload.pressSongPosMs = songPos;
                payload.releaseSongPosMs = 0f;
                payload.targetTimeMs = nd != null ? nd.startTime : songPos;
                payload.absoluteOffsetMs = Mathf.Abs((nd != null ? nd.startTime : songPos) - songPos);
                payload.triggerPopup = true;
                payload.triggerMesh = true;
                payload.triggerHitSound = true;
                payload.isHoldHead = HoldJudgment.IsHoldLikeNote(n);
                payload.isHoldTail = false;
                payload.usePersistentMesh = false;
                payload.headInputSongPosMs = songPos;
                payload.headTargetTimeMs = nd != null ? nd.startTime : songPos;
                payload.headAbsoluteOffsetMs = Mathf.Abs((nd != null ? nd.startTime : songPos) - songPos);
                payload.hasHeadTiming = nd != null;
                bool stored = Protection.ResolveHeadJudgment(n, payload, songPos, true, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                if (stored && payload.isHoldHead)
                {
                    if (!activeHoldStates.TryGetValue(n, out var st))
                    {
                        st = new ActiveHoldState { note = n, pressSongPos = songPos };
                        try {
                            var nd2 = n.NoteData;
                            float bpm2 = 120f;
                            try { if (GameManager.Instance != null && GameManager.Instance.Conductor != null) bpm2 = GameManager.Instance.Conductor.bpm; } catch { }
                            if (nd2 != null && nd2.startTime < nd2.endTime)
                            {
                                var info2 = HoldJudgment.ComputeExtraInfo(nd2, bpm2);
                                st.maxExtra = info2.maxExtra;
                                st.eighthMs = info2.eighthMs;
                            }
                        } catch { }
                        activeHoldStates[n] = st;
                    }
                    st.headJudgmentPending = true;
                }
            }
            catch { }
        }

        // Compatibility: process virtual button press from UI (map to FindBestNote + judge)
        /// <summary>
        /// Process a button press with an optional caller-supplied songPos override.
        /// When songPosOverride is finite, it is used instead of reading GetSongPositionMs(),
        /// allowing sub-frame timestamp precision from RealtimeInputBuffer.
        /// </summary>
        // ── 輸入幀：只剩「只讓聲音等幀」在用 ─────────────────────────────
        //
        // 本家的每幀規則（README §4、§5）改在逐鍵判定裡模擬：鄰鍵鎖、判掉的音符吃掉
        // 周圍的多餘按鍵、可疑的判定先暫定（TryHoldProvisionally）。收幀只用來把
        // 琴聲扣到幀尾。
        private struct FramedPress
        {
            public int Lane;
            public float SongPos;
            public long EventId;
        }

        private readonly System.Collections.Generic.List<FramedPress> _inputFrame =
            new System.Collections.Generic.List<FramedPress>(8);
        private float _inputFrameOpenedRealtime = -1f;

        /// <summary>Near 區的半徑，就是文件 §3.1.2 的「左右各 3 個按鍵」。</summary>
        private const int NearRegionReach = 3;

        /// <summary>
        /// 收一次按鍵進當前輸入幀。幀滿了才判定。
        /// </summary>
        /// <remarks>
        /// 幀從**第一顆按鍵**開始算 16.66ms，而不是對齊固定格線：兩者平均延遲
        /// 一樣，但這樣保證任何一組同時按下的鍵一定落在同一幀裡，不會被格線
        /// 邊界切開——而切開正是我們要消除的那個「先後」。
        /// </remarks>
        public void EnqueueInputFramePress(int buttonId, float songPos, long inputEventId)
        {
            // 只有「只讓聲音等幀」需要收幀。類原型不再等幀：本家的幀規則改成在
            // 判定裡用暫定判定模擬（見 TryHoldProvisionally），按下就判。
            if (!UseSoundOnlyFrame)
            {
                ProcessButtonPress(buttonId, songPos, inputEventId);
                return;
            }

            // 沒有硬體時戳的按鍵（電腦鍵盤退回來的 NaN）在幀裡要拿來挑候選、比先後，
            // 先換成現在的歌曲時間。
            if (float.IsNaN(songPos) || float.IsInfinity(songPos)) songPos = GetSongPositionMs();
            if (_inputFrame.Count == 0) _inputFrameOpenedRealtime = Time.unscaledTime;
            _inputFrame.Add(new FramedPress
            {
                Lane = buttonId,
                SongPos = songPos,
                EventId = inputEventId,
            });

            // 只讓聲音等幀：判定、計分、畫面立刻送出，零延遲，防誤觸走逐鍵那一套
            // （被吃掉的按鍵根本不會發聲）。擊弦先扣在 PianoKeysound 裡，幀結算時放行。
            //
            // 扣留只包住這一次呼叫：擊弦可能從 ProcessButtonPress 直接來，也可能
            // 繞過 HitEffectRouter，兩條都在這個呼叫堆疊內。呼叫一結束就停止扣留，
            // 免得自動演奏、Hold 恢復那些路徑的擊弦被順手吞掉。
            PianoKeysound.DeferLane(buttonId);
            try { ProcessButtonPress(buttonId, songPos, inputEventId); }
            finally { PianoKeysound.StopDeferring(buttonId); }
        }

        private static bool UseInputFrameBatching
        {
            get
            {
                SettingsManager settings = SettingsManager.Instance;
                return settings == null || settings.InputFrameBatching;
            }
        }

        private static bool UseSoundOnlyFrame
        {
            get
            {
                SettingsManager settings = SettingsManager.Instance;
                return settings != null && settings.InputFrameSoundOnly;
            }
        }

        /// <summary>幀滿了就結算。每個 Unity 幀開頭跑一次。</summary>
        private void FlushInputFrameIfDue()
        {
            if (_inputFrame.Count == 0) return;
            if ((Time.unscaledTime - _inputFrameOpenedRealtime) * 1000f < OperationFrameMs) return;
            ResolveInputFrame();
        }

        /// <summary>只讓聲音等幀：幀滿了，把這一幀扣住的琴聲放行。</summary>
        /// <remarks>
        /// 判定在按下的當下已經做完，被吃掉的按鍵那時就沒有發聲，這裡不必再判斷誰有效。
        /// </remarks>
        private void ResolveInputFrame()
        {
            for (int i = 0; i < _inputFrame.Count; i++) PianoKeysound.CommitDeferred(_inputFrame[i].Lane);
            _inputFrame.Clear();
        }

        public void ProcessButtonPress(int buttonId, float songPosOverride = float.NaN,
            long inputEventId = 0)
        {
            long costStart = JudgeCostProbe.Begin();
            try
            {
                bool hasOverride = !float.IsNaN(songPosOverride) && !float.IsInfinity(songPosOverride);
                var songPos = hasOverride ? songPosOverride : GetSongPositionMs();
                if (_cachedConductor != null && _cachedConductor.isPlaying && songPos >= 0f)
                {
                    if (hasOverride)
                    {
                        float projection = songPos - GetSongPositionMs();
                        _timedInputCount++;
                        _inputProjectionSumMs += projection;
                        if (projection < _inputProjectionMinMs) _inputProjectionMinMs = projection;
                        if (projection > _inputProjectionMaxMs) _inputProjectionMaxMs = projection;
                    }
                    else
                    {
                        _fallbackInputCount++;
                    }
                }
                if (!_inputTimingPathLogged && _cachedConductor != null &&
                    _cachedConductor.isPlaying && songPos >= 0f)
                {
                    _inputTimingPathLogged = true;
                    float currentSongPos = GetSongPositionMs();
                    string source = inputEventId <= 0 ? "legacy" :
                        ((inputEventId & 1L) == 0L ? "MIDI" : "keyboard");
                    Debug.Log($"[Judgment Input Path] source={source}, " +
                        $"event-current={songPos - currentSongPos:+0.00;-0.00;0.00}ms, " +
                        $"override={(hasOverride ? "yes" : "no")}");
                }
                // ── Nostalgia judgment-protection: per-frame key consumption ──────────
                // Cache Time.frameCount once — it's a Unity native property (P/Invoke),
                // reading it repeatedly in the same call stack adds unnecessary overhead.
                int curFrame = Time.frameCount;
                // 教學示範段：玩家跟著比劃的按鍵不判定，不會搶走示範的音符，也不會
                // 記成誤觸或一掌拍下去。
                if (TutorialSession.BlocksInput(songPos)) return;
                if (_inputProtection.IsConsumed(buttonId, curFrame, inputEventId)) return;
                RememberPressForMelody(buttonId, songPos);
                CheckRecitalMistouch(buttonId, songPos);
                // ─────────────────────────────────────────────────────────────────────
                ProcessOverlappedJudgmentSuppressions(songPos);
                if (TryResumeActiveHold(buttonId, songPos, curFrame, inputEventId)) return;
                if (TryRecoverMissedHold(buttonId, songPos, curFrame, inputEventId)) return;
                var chatterExclusions = GetSpatialStopExcludedLanes(songPos);
                if (_inputProtection.IsChatterBlocked(buttonId, songPos, chatterExclusions))
                {
                    return;
                }
                NoteController gestureHead = RegisterGestureInput(buttonId, songPos);
                if (gestureHead != null)
                {
                    if (!TryCoJudge(gestureHead, buttonId, songPos))
                        ConsumeInputForNote(buttonId, curFrame, songPos, inputEventId);
                    // 本家的顫音每一下都上鎖，錨點是「現在 + Miss 窗早側」，
                    // 所以擋的是更後面的音符，不擋顫音本身。
                    ClaimNote(gestureHead.NoteData, buttonId, songPos, songPos + TrillLockLeadMs);
                    return;
                }
                // First: trigger ALL nearby soft notes matching this lane (allow multiple triggers)
                bool triggeredSoftNote = false;
                try
                {
                    foreach (var n in GetNearbyNotes(songPos))
                    {
                        if (n == null || !n.IsActive || !n.IsJudgeable || n.IsJudged || n.IsJudgmentSuppressed) continue;
                        if (!n.IsSoft) continue;
                        var softNd = n.NoteData; if (softNd == null) continue;
                        if (buttonId < softNd.startLane || buttonId > softNd.endLane) continue;
                        float delta = Mathf.Abs(softNd.startTime - songPos);
                        if (delta > goodMs) continue;

                        var softPayload = RentPayload();
                        softPayload.note = n;
                        softPayload.result = JudgmentResult.Perfect;
                        softPayload.buttonId = buttonId;
                        softPayload.inputSongPosMs = songPos;
                        softPayload.pressSongPosMs = songPos;
                        softPayload.releaseSongPosMs = 0f;
                        softPayload.targetTimeMs = softNd.startTime;
                        softPayload.absoluteOffsetMs = Mathf.Abs(softNd.startTime - songPos);
                        softPayload.triggerPopup = true;
                        softPayload.triggerMesh = true;
                        softPayload.triggerHitSound = true;
                        softPayload.isHoldHead = false;
                        softPayload.isHoldTail = false;
                        softPayload.usePersistentMesh = false;
                        softPayload.headInputSongPosMs = songPos;
                        softPayload.headTargetTimeMs = softNd.startTime;
                        softPayload.headAbsoluteOffsetMs = Mathf.Abs(softNd.startTime - songPos);
                        softPayload.hasHeadTiming = true;
                        ApplyJudgmentPayload(softPayload);
                        triggeredSoftNote = true;
                    }
                }
                catch { }

                // Then continue normal single-note matching for non-soft notes
                var c = FindBestNote(buttonId, songPos, goodMs);
                if (c == null)
                {
                    if (TryRecoverMissedHold(buttonId, songPos, curFrame, inputEventId)) return;
                    // No note is left to claim, but one this key could have hit
                    // may have just been taken by a worse-timed press. Correcting
                    // that is a scoring matter only, so the key still sounds the
                    // way it does today and the flow below is untouched.
                    try
                    {
                        EarlyClaimCorrector.RecordUnmatchedPress(
                            buttonId, songPos, perfectMs, greatMs, goodMs);
                    }
                    catch { }
                    // Nothing on this lane wanted the key. Every earlier branch
                    // that could still claim it has already returned, so this is
                    // the one place a genuinely wrong key can be identified.
                    if (!triggeredSoftNote)
                    {
                        // 正主剛在旁邊判掉：這一下是它的餘波，直接吃掉。
                        if (JustClaimedAround(buttonId))
                        {
                            MistouchProbe.StrayAbsorbed();
                            return;
                        }
                        // 附近根本沒有音符能來吃掉它：明顯按錯，立刻出聲，不必等。
                        if (!HasAbsorberNear(buttonId, songPos))
                        {
                            SettleStray(buttonId);
                            return;
                        }
                        // 先不出聲。觀察窗內如果有鄰近的音符被判定，這一下就是擦到隔壁鍵，
                        // 該被吃掉；等不到才是真的誤觸。
                        _pendingStrays.Add(new PendingStray
                        {
                            lane = buttonId,
                            songPosMs = songPos,
                            realtime = Time.unscaledTime,
                        });
                    }
                    return;
                }

                // ── 鋼琴音在這裡發聲，不等判定結果 ────────────────────────────
                // 鋼琴音原本掛在 payload 套用時的視覺效果上（ShowNoteMeshEffect →
                // HitEffectRouter.Play）。但 Great/Good 的 payload 會被保護窗押後，
                // 最久到 songPos + goodMs（108ms）才 apply——等於把整個保護窗直接
                // 加進聽感延遲，而且只有打 Perfect 的時候才不會延遲，聽起來就像
                // 「打得越差、聲音來得越慢」。
                //
                // 一顆音該用什麼音高、什麼力度，只跟這顆音符本身有關，跟最後判成
                // Perfect 還是 Good 完全無關，所以沒有任何理由等。FindBestNote 只會
                // 回傳 IsJudgeable 的音符，也只會在 goodMs 內配對，這裡拿到的就是
                // 這一下真正要彈響的那顆。
                //
                // PianoKeysound 以 (note, startTime) 去重，稍後 payload 真的套用時
                // 再走一次同一條路不會敲出第二聲。
                // 本家的鄰鍵鎖：剛有別的鍵判中音符，這一鍵在它左右 4 格內，而這顆
                // 音符比那顆晚 4 幀以上 —— 這一下是滑過去的手指，不准它提早吃掉後面
                // 的音符。落空，不出聲、不算誤觸。
                if (c.NoteData != null &&
                    _inputProtection.IsLockedFor(buttonId, songPos, c.NoteData.startTime,
                        UseClassicSimulation, LockAllowWithinMs))
                {
                    MistouchProbe.RejectedByBrush();
                    EatenInputProbe.Eaten(EatenInputProbe.Reason.Lock, c);
                    return;
                }
                // 本家：候選被同一幀的別的鍵拿走，這一下落空，不轉去打下一顆。
                if (c.NoteData != null && !_provisionalPresses.ContainsKey(c) &&
                    WasCandidateTakenJustNow(buttonId, c.NoteData.startTime))
                {
                    MistouchProbe.StrayAbsorbed();
                    EatenInputProbe.Eaten(EatenInputProbe.Reason.Taken, c);
                    return;
                }
                // 可疑的判定（可能是擦碰搶走較晚的音符）或類原型的提早預訂：先暫定，
                // 時間到了才正式套用。
                if (TryHoldProvisionally(c, buttonId, songPos, curFrame, inputEventId)) return;
                CommitNotePress(c, buttonId, songPos, songPos, curFrame, inputEventId, true);
            }
            catch { }
            finally { JudgeCostProbe.EndPress(costStart); }
        }

        // ── 演奏會：多鍵檢查（本家的 over3key）──────────────────────────────
        //
        // 本家在一顆音符被判定的那一幀，數這一幀有幾個新按下的鍵落在它的鍵範圍內；
        // 3 個以上就記一次，扣「旋律」分。意思是「這顆音是被一掌拍下去的，不是用
        // 手指彈的」。和弦裡各自一格的音符不會觸發：每顆音符只數落在自己範圍內的鍵。
        //
        // 逐鍵判定沒有幀，所以記下最近的按鍵，等判定後一幀（加一點餘裕）再回頭數
        // 「判定那一下前後一幀內、落在範圍裡」的按鍵。

        private struct MelodyPress
        {
            /// <summary>鍵道 + 1；0 代表這一格還沒寫過。</summary>
            public int LanePlusOne;
            public float SongPos;
        }

        private struct MelodyCheck
        {
            public int Low;
            public int High;
            public float PressSongPos;
        }

        /// <summary>同一幀內幾個鍵算「一掌拍下去」。本家是 3 個以上。</summary>
        private const int MelodyKeyThreshold = 3;

        private readonly MelodyPress[] _melodyPresses = new MelodyPress[64];
        private int _melodyPressNext;
        private readonly System.Collections.Generic.List<MelodyCheck> _pendingMelodyChecks =
            new System.Collections.Generic.List<MelodyCheck>(16);

        private void RememberPressForMelody(int lane, float songPos)
        {
            if (!_cachedRecitalMode) return;
            _melodyPresses[_melodyPressNext] = new MelodyPress { LanePlusOne = lane + 1, SongPos = songPos };
            _melodyPressNext = (_melodyPressNext + 1) % _melodyPresses.Length;
        }

        private void QueueMelodyCheck(NoteData nd, float pressSongPos)
        {
            if (nd == null) return;
            _pendingMelodyChecks.Add(new MelodyCheck
            {
                Low = Mathf.Min(nd.startLane, nd.endLane),
                High = Mathf.Max(nd.startLane, nd.endLane),
                PressSongPos = pressSongPos,
            });
        }

        private void ProcessMelodyChecks(float songPos)
        {
            for (int i = _pendingMelodyChecks.Count - 1; i >= 0; i--)
            {
                MelodyCheck check = _pendingMelodyChecks[i];
                if (songPos < check.PressSongPos + OperationFrameMs + ProvisionalCheckSlackMs) continue;
                _pendingMelodyChecks.RemoveAt(i);

                int keys = 0;
                for (int p = 0; p < _melodyPresses.Length; p++)
                {
                    MelodyPress press = _melodyPresses[p];
                    if (press.LanePlusOne == 0) continue;
                    int lane = press.LanePlusOne - 1;
                    if (lane < check.Low || lane > check.High) continue;
                    if (Mathf.Abs(press.SongPos - check.PressSongPos) > OperationFrameMs) continue;
                    keys++;
                }
                if (keys >= MelodyKeyThreshold)
                {
                    try { StatsManager.Instance?.RecordOver3Key(); } catch { }
                }
            }
        }

        // ── 演奏會：誤觸計數（本家）──────────────────────────────────────────
        //
        // 本家每一幀把「判定窗內的音符」涵蓋的鍵標起來，標記留 10 幀；這一幀新按
        // 下的鍵落在沒標記的格子上，而且當下至少有一顆音符在窗內，就記一次誤觸。
        // 看的只是「這段時間裡哪幾格有音符」，不管這一下有沒有打中、被不被吃掉、
        // 有沒有出錯音 —— 所以按在音符範圍內永遠不算，按在範圍外永遠算。
        //
        // 以前是跟著錯音走（沒有音符接住才記），外加 70ms 餘波保護；那會讓同一個
        // 動作因為判定器的狀態不同而有時算、有時不算。

        /// <summary>音符離開判定窗之後，它的鍵還算「有音符」多久。本家 10 幀。</summary>
        private const float MistouchLingerMs = 10f * OperationFrameMs;

        /// <summary>滑奏的涵蓋範圍左右各多幾格。本家 2 格。</summary>
        private const int MistouchSlideReach = 2;

        private Chart _mistouchIndexChart;
        private int _mistouchIndexSourceCount = -1;
        private NoteData[] _mistouchIndex;
        private int _mistouchMaxSpanMs;

        private void CheckRecitalMistouch(int lane, float songPos)
        {
            if (!_cachedRecitalMode || lane < 0) return;
            NoteData[] notes = GetMistouchIndex();
            if (notes == null || notes.Length == 0) return;

            float early = goodMs;
            float late = goodMs;
            // 依開始時間排序，從「最長的音符也早就結束」的地方開始找。
            float from = songPos - late - MistouchLingerMs - _mistouchMaxSpanMs;
            int lo = 0, hi = notes.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (notes[mid].startTime < from) lo = mid + 1; else hi = mid;
            }

            bool anyInWindow = false;
            for (int i = lo; i < notes.Length; i++)
            {
                NoteData nd = notes[i];
                if (nd.startTime - early > songPos) break;
                float windowEnd = Mathf.Max(nd.startTime, nd.endTime) + late;
                if (songPos > windowEnd + MistouchLingerMs) continue;

                int low = Mathf.Min(nd.startLane, nd.endLane);
                int high = Mathf.Max(nd.startLane, nd.endLane);
                if (IsSlideNote(nd)) { low -= MistouchSlideReach; high += MistouchSlideReach; }
                if (lane >= low && lane <= high) return;   // 這一格有音符：不算
                if (songPos <= windowEnd) anyInWindow = true;
            }
            if (!anyInWindow) return;   // 窗內沒有任何音符：隨便按都不算

            try { StatsManager.Instance?.RecordMistouch(); } catch { }
            // 演奏會模式：這一鍵不在任何音符的範圍裡，讓它自己亮紅燈。鍵盤在畫面
            // 最下緣，判定線上再給一根 —— 玩家正在讀的是從上面掉下來的東西。
            try { IvoryLaneKeyboard.FlashWrong(lane); } catch { }
            try { Effects.LaneStrikeCue.Wrong(lane); } catch { }
        }

        /// <summary>這首歌要玩家按的音符，依開始時間排序。換譜才重建。</summary>
        private NoteData[] GetMistouchIndex()
        {
            var chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
            var source = chart != null ? chart.notes : null;
            if (source == null) return null;
            if (chart == _mistouchIndexChart && source.Count == _mistouchIndexSourceCount)
                return _mistouchIndex;

            var list = new System.Collections.Generic.List<NoteData>(source.Count);
            int maxSpan = 0;
            for (int i = 0; i < source.Count; i++)
            {
                NoteData nd = source[i];
                if (nd == null || nd.hidden) continue;
                list.Add(nd);
                maxSpan = Mathf.Max(maxSpan, nd.endTime - nd.startTime);
            }
            list.Sort((a, b) => a.startTime.CompareTo(b.startTime));
            _mistouchIndex = list.ToArray();
            _mistouchMaxSpanMs = maxSpan;
            _mistouchIndexChart = chart;
            _mistouchIndexSourceCount = source.Count;
            return _mistouchIndex;
        }

        /// <summary>
        /// 輸入端（MIDI 擦碰、抖動過濾）丟掉了一下按鍵：記下它本來會打到哪顆音符，
        /// 看那顆音符最後有沒有 Miss。只供診斷，不改變任何判定。
        /// </summary>
        public void ReportInputDropped(int lane, EatenInputProbe.Reason reason)
        {
            try
            {
                NoteController candidate = lane >= 0 ? FindBestNote(lane, GetSongPositionMs(), goodMs) : null;
                EatenInputProbe.Eaten(reason, candidate);
            }
            catch { }
        }

        /// <summary>
        /// 一下按鍵正式拿走一顆音符：發聲、上鎖、吃掉周圍的多餘按鍵、判定。
        /// </summary>
        /// <param name="pressSongPos">按下的時間（評分用）。</param>
        /// <param name="nowSongPos">現在的時間。暫定判定晚一點才套用時，兩者不同。</param>
        /// <param name="countMatch">這一下還沒被 <see cref="MistouchProbe"/> 算過配對。</param>
        private void CommitNotePress(NoteController c, int buttonId, float pressSongPos, float nowSongPos,
            int curFrame, long inputEventId, bool countMatch)
        {
            float songPos = pressSongPos;
            if (countMatch) MistouchProbe.Matched();
            try { PianoKeysound.PlayJudgedNote(c, buttonId); } catch { }
            // 擦碰吸收本來也掛在 payload 上，於是同樣被押後。押後的吸收永遠來不及
            // ——擦到隔壁鍵的那一下照樣會響。配對成立的當下就吃掉它才有意義。
            try { ClaimNote(c.NoteData, buttonId, songPos, c.NoteData != null ? c.NoteData.startTime : songPos); } catch { }

            // If this is a hold-like note, prefer the hold flow (BeginHoldTracking)
            bool isHoldHead = HoldJudgment.IsHoldLikeNote(c);
            if (isHoldHead)
            {
                var handler = GetHandler(c);
                float delta = Mathf.Abs((c.NoteData != null ? c.NoteData.startTime : songPos) - songPos);
                JudgmentResult? forcedResult = c.IsSoft ? JudgmentResult.Perfect : (JudgmentResult?)null;
                BeginHoldTracking(c, handler, buttonId, songPos, delta, forcedResult);
                // ── 100ms co-judge check ──────────────────────────────────────────
                if (!TryCoJudge(c, buttonId, songPos)) ConsumeInputForNote(buttonId, curFrame, songPos, inputEventId);
                // ─────────────────────────────────────────────────────────────────
                return;
            }

            // 和弦一致：同時的音符在一幀內都被按下時，整組用最早那一下的時間評分。
            // 本家同一幀的按鍵本來就共用一個時間。
            var nd = c.NoteData;
            songPos = nd != null ? ShareChordPressTime(nd, pressSongPos) : pressSongPos;

            // Non-hold tap: judge and route through protection
            var result = TapJudgment.Judge(c, songPos, perfectMs, greatMs, goodMs);
            var payload = RentPayload();
            payload.note = c;
            payload.result = result;
            payload.buttonId = buttonId;
            payload.inputSongPosMs = songPos;
            payload.pressSongPosMs = songPos;
            payload.releaseSongPosMs = 0f;
            payload.targetTimeMs = nd?.startTime ?? songPos;
            payload.absoluteOffsetMs = Mathf.Abs((nd?.startTime ?? songPos) - songPos);
            payload.triggerPopup = true;
            payload.triggerMesh = true;
            payload.triggerHitSound = true;
            payload.isHoldHead = false;
            payload.isHoldTail = false;
            payload.usePersistentMesh = false;
            payload.headInputSongPosMs = songPos;
            payload.headTargetTimeMs = nd != null ? nd.startTime : songPos;
            payload.headAbsoluteOffsetMs = Mathf.Abs((nd != null ? nd.startTime : songPos) - songPos);
            payload.hasHeadTiming = nd != null;
            Protection.ResolveHeadJudgment(c, payload, nowSongPos, true, GetNearbyNotes(nowSongPos), goodMs, ApplyJudgmentPayload);
            // ── 100ms co-judge check ──────────────────────────────────────────
            if (!TryCoJudge(c, buttonId, songPos)) ConsumeInputForNote(buttonId, curFrame, pressSongPos, inputEventId);
            // ─────────────────────────────────────────────────────────────────
        }

        private static bool HasLaneRangeOverlap(NoteData a, NoteData b)
        {
            if (a == null || b == null) return false;
            int aMin = a.startLane <= a.endLane ? a.startLane : a.endLane;
            int aMax = a.startLane >= a.endLane ? a.startLane : a.endLane;
            int bMin = b.startLane <= b.endLane ? b.startLane : b.endLane;
            int bMax = b.startLane >= b.endLane ? b.startLane : b.endLane;
            return Mathf.Max(aMin, bMin) <= Mathf.Min(aMax, bMax);
        }

        private static int ClampLaneToNoteRange(int lane, NoteData nd)
        {
            if (nd == null) return lane;
            int min = nd.startLane <= nd.endLane ? nd.startLane : nd.endLane;
            int max = nd.startLane >= nd.endLane ? nd.startLane : nd.endLane;
            return Mathf.Clamp(lane, min, max);
        }

        private static void GetTimeRange(NoteData note, out float startMs, out float endMs)
        {
            if (note == null)
            {
                startMs = 0f;
                endMs = 0f;
                return;
            }

            startMs = Mathf.Min(note.startTime, note.endTime);
            endMs = Mathf.Max(note.startTime, note.endTime);
        }

        private static bool HasTimeRangeOverlap(NoteData a, NoteData b)
        {
            if (a == null || b == null) return false;
            GetTimeRange(a, out var aStart, out var aEnd);
            GetTimeRange(b, out var bStart, out var bEnd);
            return Mathf.Max(aStart, bStart) <= Mathf.Min(aEnd, bEnd);
        }

        private static float GetRangeEnd(NoteData note)
        {
            if (note == null) return float.MinValue;
            float startMs;
            float endMs;
            GetTimeRange(note, out startMs, out endMs);
            return endMs;
        }

        private int _excludedLanesFrame = -1;
        private float _excludedLanesSongPos = float.NaN;

        private System.Collections.Generic.HashSet<int> GetSpatialStopExcludedLanes(float songPos)
        {
            // This runs for every physical press (and used to allocate another
            // HashSet again when consuming the same press).  Reuse one scratch
            // set: callers consume it synchronously and never retain it.
            var excluded = spatialStopExcludedLanesScratch;
            // 同一下按鍵會叫兩次（檢查抖動、登記抖動），內容只跟同一格的附近音符有關。
            int frame = Time.frameCount;
            if (frame == _excludedLanesFrame && songPos == _excludedLanesSongPos) return excluded;
            _excludedLanesFrame = frame;
            _excludedLanesSongPos = songPos;
            excluded.Clear();
            foreach (var other in GetNearbyNotes(songPos))
            {
                if (other == null) continue;
                var otherData = other?.NoteData;
                if (otherData == null) continue;

                int minLane = otherData.startLane <= otherData.endLane ? otherData.startLane : otherData.endLane;
                int maxLane = otherData.startLane >= otherData.endLane ? otherData.startLane : otherData.endLane;
                for (int lane = minLane; lane <= maxLane; lane++)
                {
                    excluded.Add(lane);
                }
            }

            return excluded;
        }

        /// <summary>這一次按下用掉了：同一個事件不再判第二次，按下的鍵進入抖動保護。</summary>
        private void ConsumeInputForNote(int buttonId, int frameCount, float songPos,
            long inputEventId = 0)
        {
            _inputProtection.Consume(buttonId, frameCount, inputEventId);
            _inputProtection.GuardChatter(buttonId, songPos, GetSpatialStopExcludedLanes(songPos));
        }

        /// <summary>
        /// 本家顫音上鎖的錨點比現在晚多少：Miss 窗早側 9 幀。
        /// </summary>
        private const float TrillLockLeadMs = 150f;

        /// <summary>
        /// 一顆音符被第 <paramref name="buttonId"/> 鍵拿走了。
        /// </summary>
        /// <remarks>
        /// 兩件事，所有判定模式都一樣：
        ///
        /// * 本家的鄰鍵鎖：左右 4 格鎖 3 幀，只擋比 <paramref name="lockAnchorMs"/>
        ///   晚 4 幀以上的音符。
        /// * 吃掉多餘的按鍵：落在這顆音符 (判定區 ∪ Near 區) 的誤觸候選不出聲，
        ///   <see cref="AbsorbWindowMs"/> 往前（已經扣住的）和往後（還沒到的）各吃一段。
        /// </remarks>
        private void ClaimNote(NoteData nd, int buttonId, float songPos, float lockAnchorMs)
        {
            _inputProtection.LockNeighbours(buttonId, songPos, lockAnchorMs);
            MarkClaimedSpan(nd);
        }

        /// <summary>吃掉這顆音符周圍（判定區 ∪ Near 區）的多餘按鍵：已經在等的，和觀察窗內還沒到的。</summary>
        private void MarkClaimedSpan(NoteData nd)
        {
            if (nd == null) return;
            AbsorbStraysAround(nd);

            int exactLow = Mathf.Min(nd.startLane, nd.endLane);
            int exactHigh = Mathf.Max(nd.startLane, nd.endLane);
            float until = Time.unscaledTime + AbsorbWindowMs * 0.001f;
            for (int i = 0; i < _claimedSpans.Count; i++)
            {
                if (_claimedSpans[i].ExactLow != exactLow || _claimedSpans[i].ExactHigh != exactHigh) continue;
                ClaimedSpan existing = _claimedSpans[i];
                existing.Until = until;
                existing.StartTime = nd.startTime;
                existing.ClaimedAt = Time.unscaledTime;
                _claimedSpans[i] = existing;
                return;
            }
            _claimedSpans.Add(new ClaimedSpan
            {
                Low = exactLow - NearReach,
                High = exactHigh + NearReach,
                ExactLow = exactLow,
                ExactHigh = exactHigh,
                StartTime = nd.startTime,
                ClaimedAt = Time.unscaledTime,
                Until = until,
            });
        }

        /// <summary>
        /// 這一鍵本來的候選（涵蓋它、最早到線的那顆）剛剛才被別的按鍵拿走，而它現在
        /// 配到的是更晚的一顆。
        /// </summary>
        /// <remarks>
        /// 本家同一幀的按鍵在判定之前就挑好候選，候選被搶走的鍵落空，不會轉去打下一顆。
        /// 逐鍵判定時第一下判完，第二下才進來找候選，自然就找到下一顆 —— 鄰鍵鎖只擋
        /// 晚 4 幀以上的，擋不住緊接著的那顆。
        /// </remarks>
        private bool WasCandidateTakenJustNow(int lane, float candidateStartMs)
        {
            // 本家只有「同一幀」的按鍵會共用候選，所以只看一幀（17ms）內被拿走的，
            // 即時反饋也一樣。原本用的是 50ms 的吸收窗，比本家寬，會把兩顆靠得很近、
            // 鍵道重疊的音符的第二下也吃掉。
            float now = Time.unscaledTime;
            float frameSeconds = OperationFrameMs * 0.001f;
            for (int i = _claimedSpans.Count - 1; i >= 0; i--)
            {
                ClaimedSpan span = _claimedSpans[i];
                if (now - span.ClaimedAt > frameSeconds) continue;
                if (lane < span.ExactLow || lane > span.ExactHigh) continue;
                if (candidateStartMs > span.StartTime + 0.5f) return true;
            }
            return false;
        }

        /// <summary>
        /// 這一鍵落在剛被拿走的音符周圍，而且還在觀察窗內。
        /// </summary>
        private bool JustClaimedAround(int lane)
        {
            float now = Time.unscaledTime;
            bool hit = false;
            for (int i = _claimedSpans.Count - 1; i >= 0; i--)
            {
                if (_claimedSpans[i].Until <= now) { _claimedSpans.RemoveAt(i); continue; }
                if (lane >= _claimedSpans[i].Low && lane <= _claimedSpans[i].High) hit = true;
            }
            return hit;
        }

        // ── 暫定判定 ──────────────────────────────────────────────────────────
        //
        // 擦碰搶音符：手指滾向正主時先擦到隔壁鍵，隔壁鍵剛好配到一顆**比正主晚**
        // 的音符，就先判下去了。等正主判中、鄰鍵鎖上來的時候已經來不及 —— 鎖只擋
        // 「之後」的按鍵。
        //
        // 做法不是撤銷，是暫緩：看起來可能是這種情況的判定先不套用，音符原封不動
        // 地留著，觀察窗結束時再看它有沒有被正主的鎖鎖到。鎖到了就作廢（那一下是
        // 擦碰），沒有才正式套用。絕大多數按鍵不符合條件，照常立即判定。
        //
        // 類原型另外照本家做「提早預訂」：提早按下的判定到線才定案，期間更準的一下
        // 可以覆蓋。

        private sealed class ProvisionalPress
        {
            public NoteController Note;
            public int Lane;
            public float PressSongPos;
            public int Frame;
            public long EventId;
            /// <summary>還在擦碰觀察窗裡：窗結束時要檢查有沒有被鎖到。</summary>
            public bool BrushPending;
            public float BrushDeadlineMs;
            /// <summary>琴聲扣著，等觀察窗結束才決定響不響（只有類原型）。</summary>
            public bool SoundDeferred;
            /// <summary>類原型的提早預訂：到這個時間才定案。沒有預訂時是 MinValue。</summary>
            public float ReserveUntilMs = float.MinValue;
        }

        /// <summary>觀察窗結束後多等多久才檢查，讓晚一個 Unity 幀送達的按鍵趕上。</summary>
        private const float ProvisionalCheckSlackMs = 8f;

        private readonly System.Collections.Generic.Dictionary<NoteController, ProvisionalPress> _provisionalPresses =
            new System.Collections.Generic.Dictionary<NoteController, ProvisionalPress>();
        private readonly System.Collections.Generic.List<ProvisionalPress> _provisionalScratch =
            new System.Collections.Generic.List<ProvisionalPress>(8);

        /// <summary>
        /// 這一下要不要先暫定。要的話建立暫定判定並回傳 true，呼叫端不必再做任何事。
        /// </summary>
        /// <remarks>
        /// 只處理一般的點擊音符。長條頭一旦開始就綁著按住的狀態，暫緩它牽動整條
        /// 長條的追蹤，這一版先不處理。
        /// </remarks>
        private bool TryHoldProvisionally(NoteController c, int buttonId, float songPos, int curFrame, long inputEventId)
        {
            NoteData nd = c != null ? c.NoteData : null;
            if (nd == null) return false;

            if (_provisionalPresses.TryGetValue(c, out ProvisionalPress existing))
            {
                PressAgainOnProvisional(existing, buttonId, songPos, curFrame, inputEventId);
                return true;
            }

            if (HoldJudgment.IsHoldLikeNote(c) || c.IsSoft || IsGestureNote(nd)) return false;

            bool brushRisk = HasEarlierRivalWithinLock(c, buttonId, songPos);
            float early = nd.startTime - songPos;
            bool reserve = UseClassicSimulation && early > 0f && early <= greatMs;
            if (!brushRisk && !reserve) return false;

            var claim = new ProvisionalPress
            {
                Note = c,
                Lane = buttonId,
                PressSongPos = songPos,
                Frame = curFrame,
                EventId = inputEventId,
            };
            ArmProvisional(claim, buttonId, songPos, brushRisk);
            _provisionalPresses[c] = claim;
            MistouchProbe.Matched();
            ConsumeInputForNote(buttonId, curFrame, songPos, inputEventId);
            return true;
        }

        /// <summary>設定暫定判定的兩個階段，並發聲（或扣住琴聲）。</summary>
        private void ArmProvisional(ProvisionalPress claim, int buttonId, float songPos, bool brushRisk)
        {
            NoteData nd = claim.Note.NoteData;
            claim.Lane = buttonId;
            claim.PressSongPos = songPos;
            claim.BrushPending = brushRisk;
            claim.BrushDeadlineMs = songPos + AbsorbWindowMs;
            float early = nd.startTime - songPos;
            claim.ReserveUntilMs = UseClassicSimulation && early > 0f && early <= greatMs
                ? nd.startTime
                : float.MinValue;
            bool reserved = claim.ReserveUntilMs > float.MinValue;
            claim.SoundDeferred = brushRisk && UseClassicSimulation && !reserved;

            if (reserved)
            {
                // 本家：提早按下被預訂的那一下，聲音和判定一起延到音符到線。這裡先不
                // 發聲，到期時 CommitNotePress 才放；作廢的話就從頭到尾沒響過。
            }
            else if (claim.SoundDeferred)
            {
                PianoKeysound.DeferLane(buttonId);
                try { PianoKeysound.PlayJudgedNote(claim.Note, buttonId); } catch { }
                finally { PianoKeysound.StopDeferring(buttonId); }
            }
            else
            {
                try { PianoKeysound.PlayJudgedNote(claim.Note, buttonId); } catch { }
            }

            // 吃掉周圍的多餘按鍵照常進行 —— 被吃掉的是這一下旁邊的鍵，就算這一下
            // 最後作廢，那些鍵本來就只是它的餘波。
            try { MarkClaimedSpan(nd); } catch { }

            // 單純的預訂（不是擦碰嫌疑）當下就上鎖：正主已經按下了，本家若是立即判
            // 定也會在此刻上鎖，不能因為我們晚一點才套用，就讓擦碰趁隙搶走後面的音符。
            if (!brushRisk && claim.ReserveUntilMs > float.MinValue)
                _inputProtection.LockNeighbours(buttonId, songPos, nd.startTime);
        }

        /// <summary>
        /// 暫定中的音符又被按了一下：更準就換成這一下（本家的預訂可以被覆蓋），
        /// 否則這一下落空。
        /// </summary>
        private void PressAgainOnProvisional(ProvisionalPress claim, int buttonId, float songPos,
            int curFrame, long inputEventId)
        {
            ConsumeInputForNote(buttonId, curFrame, songPos, inputEventId);
            float target = claim.Note.NoteData.startTime;
            bool closer = Mathf.Abs(songPos - target) + 0.01f < Mathf.Abs(claim.PressSongPos - target);
            if (!closer)
            {
                MistouchProbe.StrayAbsorbed();
                return;
            }

            if (claim.SoundDeferred) PianoKeysound.DiscardDeferred(claim.Lane);
            claim.Frame = curFrame;
            claim.EventId = inputEventId;
            ArmProvisional(claim, buttonId, songPos, HasEarlierRivalWithinLock(claim.Note, buttonId, songPos));
        }

        /// <summary>
        /// 附近有沒有一顆**更早**、還沒被判的音符，只要它被判中，鎖就會擋掉這一下
        /// 去打 <paramref name="note"/>。
        /// </summary>
        /// <remarks>
        /// 鎖是以「打中那顆音符的鍵」為中心左右 4 格，而那個鍵一定落在它的範圍內，
        /// 所以只要這一鍵離它的範圍 4 格以內就有可能。這裡寬鬆地判斷要不要等；真正
        /// 作不作廢，觀察窗結束時拿實際的鎖來看。
        /// </remarks>
        private bool HasEarlierRivalWithinLock(NoteController note, int buttonId, float songPos)
        {
            NoteData nd = note != null ? note.NoteData : null;
            if (nd == null) return false;
            foreach (NoteController other in GetNearbyNotes(songPos))
            {
                if (other == null || other == note) continue;
                if (!other.IsActive || !other.IsJudgeable || other.IsJudged || other.IsJudgmentSuppressed) continue;
                if (_provisionalPresses.ContainsKey(other)) continue;
                // 已經按住的長條頭不會再被判中一次，不是對手。
                if (activeHoldStates.ContainsKey(other)) continue;
                NoteData od = other.NoteData;
                if (od == null || IsGestureNote(od)) continue;
                if (od.startTime + InputProtection.LockLaterThanMs >= nd.startTime) continue;
                if (songPos > od.startTime + goodMs) continue;
                if (od.startTime - songPos > goodMs + AbsorbWindowMs) continue;
                int low = Mathf.Min(od.startLane, od.endLane);
                int high = Mathf.Max(od.startLane, od.endLane);
                int distance = buttonId < low ? low - buttonId : buttonId > high ? buttonId - high : 0;
                if (distance <= InputProtection.LockReach) return true;
            }
            return false;
        }

        /// <summary>每幀檢查暫定判定：觀察窗結束的看鎖，預訂到期的正式套用。</summary>
        private void ProcessProvisionalPresses(float songPos)
        {
            if (_provisionalPresses.Count == 0) return;
            _provisionalScratch.Clear();
            foreach (var kv in _provisionalPresses) _provisionalScratch.Add(kv.Value);

            for (int i = 0; i < _provisionalScratch.Count; i++)
            {
                ProvisionalPress claim = _provisionalScratch[i];
                NoteController note = claim.Note;
                NoteData nd = note != null ? note.NoteData : null;
                if (nd == null || !note.IsActive || note.IsJudged || note.IsJudgmentSuppressed)
                {
                    DropProvisional(claim);
                    continue;
                }

                if (claim.BrushPending)
                {
                    // 多等一點再看：觀察窗內按下的正主，事件可能晚一個 Unity 幀才送進來。
                    // 算不算數仍然只看它按下的時間（WasLockedAfterPress 的窗）。
                    if (songPos < claim.BrushDeadlineMs + ProvisionalCheckSlackMs) continue;
                    if (_inputProtection.WasLockedAfterPress(claim.Lane, claim.PressSongPos,
                            AbsorbWindowMs, nd.startTime, UseClassicSimulation, LockAllowWithinMs))
                    {
                        // 正主在觀察窗內判中，鎖到了這一下：它是擦碰。作廢，音符留給
                        // 真正要打它的那一下。
                        EatenInputProbe.Eaten(EatenInputProbe.Reason.ProvisionalDropped, note);
                        DropProvisional(claim);
                        MistouchProbe.RejectedByBrush();
                        continue;
                    }
                    claim.BrushPending = false;
                    // 它不是擦碰。還要等預訂到期的話，現在就上鎖 —— 本家在這一下判定的
                    // 當下就會上鎖，不能讓後面的擦碰趁著我們還沒套用去搶音符。
                    if (claim.ReserveUntilMs > float.MinValue)
                        _inputProtection.LockNeighbours(claim.Lane, claim.PressSongPos, nd.startTime);
                    if (claim.SoundDeferred)
                    {
                        claim.SoundDeferred = false;
                        PianoKeysound.CommitDeferred(claim.Lane);
                    }
                }

                if (songPos < claim.ReserveUntilMs) continue;

                _provisionalPresses.Remove(note);
                CommitNotePress(note, claim.Lane, claim.PressSongPos, songPos, claim.Frame, claim.EventId, false);
            }
        }

        private void DropProvisional(ProvisionalPress claim)
        {
            if (claim == null) return;
            if (claim.SoundDeferred) PianoKeysound.DiscardDeferred(claim.Lane);
            if (claim.Note != null) _provisionalPresses.Remove(claim.Note);
        }

        /// <summary>歌曲結束或換歌：還在暫定的全部正式套用（或丟掉）。</summary>
        private void SettleAllProvisionalPresses(bool commit)
        {
            if (_provisionalPresses.Count == 0) return;
            _provisionalScratch.Clear();
            foreach (var kv in _provisionalPresses) _provisionalScratch.Add(kv.Value);
            _provisionalPresses.Clear();
            float now = GetSongPositionMs();
            for (int i = 0; i < _provisionalScratch.Count; i++)
            {
                ProvisionalPress claim = _provisionalScratch[i];
                if (claim.SoundDeferred)
                {
                    if (commit) PianoKeysound.CommitDeferred(claim.Lane);
                    else PianoKeysound.DiscardDeferred(claim.Lane);
                }
                if (!commit || claim.Note == null || !claim.Note.IsActive || claim.Note.IsJudged) continue;
                try { CommitNotePress(claim.Note, claim.Lane, claim.PressSongPos, now, claim.Frame, claim.EventId, false); } catch { }
            }
            _provisionalScratch.Clear();
        }

        // ── 和弦共用時間 ────────────────────────────────────────────────────

        private struct RecentTapPress
        {
            public int StartTime;
            public float PressMs;
        }

        private readonly System.Collections.Generic.List<RecentTapPress> _recentTapPresses =
            new System.Collections.Generic.List<RecentTapPress>(16);

        /// <summary>
        /// 同一個時間點的音符（和弦）在一幀內被按下時，回傳這組裡最早那一下的時間。
        /// </summary>
        /// <remarks>
        /// 本家同一幀的按鍵共用一個時間，所以和弦一定拿到一致的判定。逐鍵的時間戳
        /// 讓手指差 10ms 就可能一顆 Perfect、一顆 Great —— 那不是彈得不齊，是精度
        /// 比本家細造成的雜訊。
        /// </remarks>
        private float ShareChordPressTime(NoteData nd, float pressMs)
        {
            float earliest = pressMs;
            for (int i = _recentTapPresses.Count - 1; i >= 0; i--)
            {
                RecentTapPress recent = _recentTapPresses[i];
                if (Mathf.Abs(pressMs - recent.PressMs) > 1000f)
                {
                    _recentTapPresses.RemoveAt(i);
                    continue;
                }
                if (recent.StartTime != nd.startTime) continue;
                if (Mathf.Abs(recent.PressMs - pressMs) > OperationFrameMs) continue;
                if (recent.PressMs < earliest) earliest = recent.PressMs;
            }
            _recentTapPresses.Add(new RecentTapPress { StartTime = nd.startTime, PressMs = pressMs });
            return earliest;
        }

        // ── 誤觸候選 ────────────────────────────────────────────────────────

        /// <summary>
        /// 附近有沒有音符可能在觀察窗內來吃掉這一下（它左右 3 格內、還沒判、快到線了）。
        /// </summary>
        private bool HasAbsorberNear(int lane, float songPos)
        {
            foreach (NoteController note in GetNearbyNotes(songPos))
            {
                if (note == null || !note.IsActive || !note.IsJudgeable || note.IsJudged) continue;
                NoteData nd = note.NoteData;
                if (nd == null) continue;
                if (Mathf.Abs(nd.startTime - songPos) > goodMs + AbsorbWindowMs) continue;
                if (lane < Mathf.Min(nd.startLane, nd.endLane) - NearReach) continue;
                if (lane > Mathf.Max(nd.startLane, nd.endLane) + NearReach) continue;
                return true;
            }
            foreach (var kv in _provisionalPresses)
            {
                NoteData nd = kv.Key != null ? kv.Key.NoteData : null;
                if (nd == null) continue;
                if (lane < Mathf.Min(nd.startLane, nd.endLane) - NearReach) continue;
                if (lane > Mathf.Max(nd.startLane, nd.endLane) + NearReach) continue;
                return true;
            }
            return false;
        }

        private bool IsLaneCurrentlyPressed(int lane)
        {
            if (lane < 0) return false;

            try
            {
                var rtBuffer = RealtimeInputBuffer.Instance;
                if (rtBuffer != null && rtBuffer.IsPressed(lane)) return true;
            }
            catch { }

            try
            {
                var keyAdapter = KeyboardInputAdapter.Instance;
                if (keyAdapter != null && keyAdapter.IsLanePressed(lane)) return true;
            }
            catch { }

            try
            {
                var midi = MIDIInputManager.Instance;
                if (midi != null && midi.IsLanePressed(lane)) return true;
            }
            catch { }

            return false;
        }

        private bool TryGetPressedLaneInRange(NoteData note, out int lane)
        {
            lane = -1;
            if (note == null) return false;

            int minLane = note.startLane <= note.endLane ? note.startLane : note.endLane;
            int maxLane = note.startLane >= note.endLane ? note.startLane : note.endLane;
            for (int current = minLane; current <= maxLane; current++)
            {
                if (!IsLaneCurrentlyPressed(current)) continue;
                lane = current;
                return true;
            }

            return false;
        }

        private static bool IsSlideNote(NoteData note)
        {
            return note != null && (note.note_type == 4 ||
                string.Equals(note.type, "slide", System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsTrillNote(NoteData note)
        {
            return note != null && (note.note_type == 64 ||
                string.Equals(note.type, "trill", System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsGestureNote(NoteData note)
        {
            return IsSlideNote(note) || IsTrillNote(note);
        }

        private void GetSlideContactRange(NoteData data, out int minLane, out int maxLane)
        {
            minLane = data != null ? Mathf.Min(data.startLane, data.endLane) : -1;
            maxLane = data != null ? Mathf.Max(data.startLane, data.endLane) : -1;
            if (!IsSlideNote(data)) return;
            try
            {
                var chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
                if (chart != indexedSlideChart)
                {
                    indexedSlideChart = chart;
                    slideNotesByIndex.Clear();
                    if (chart != null && chart.notes != null)
                    {
                        for (int i = 0; i < chart.notes.Count; i++)
                        {
                            var candidate = chart.notes[i];
                            if (IsSlideNote(candidate)) slideNotesByIndex[candidate.index] = candidate;
                        }
                    }
                }
                NoteData linked;
                if (data.param1 >= 0 && slideNotesByIndex.TryGetValue(data.param1, out linked))
                {
                    minLane = Mathf.Min(minLane, Mathf.Min(linked.startLane, linked.endLane));
                    maxLane = Mathf.Max(maxLane, Mathf.Max(linked.startLane, linked.endLane));
                }
                if (data.param2 >= 0 && slideNotesByIndex.TryGetValue(data.param2, out linked))
                {
                    minLane = Mathf.Min(minLane, Mathf.Min(linked.startLane, linked.endLane));
                    maxLane = Mathf.Max(maxLane, Mathf.Max(linked.startLane, linked.endLane));
                }
            }
            catch { }
        }

        private bool TryGetPressedLaneForGesture(NoteData data, out int lane)
        {
            lane = -1;
            if (data == null) return false;
            if (!IsSlideNote(data)) return TryGetPressedLaneInRange(data, out lane);
            GetSlideContactRange(data, out int minLane, out int maxLane);
            for (int current = minLane; current <= maxLane; current++)
            {
                if (!IsLaneCurrentlyPressed(current)) continue;
                lane = current;
                return true;
            }
            return false;
        }

        private ActiveGestureState GetGestureState(NoteController note)
        {
            if (!activeGestureStates.TryGetValue(note, out var state))
            {
                state = new ActiveGestureState { note = note };
                var data = note != null ? note.NoteData : null;
                if (IsTrillNote(data))
                {
                    float intervalMs = Mathf.Max(50f, trillMaximumInputGapSeconds * 1000f);
                    state.trillTotalTicks = Mathf.Max(1,
                        Mathf.CeilToInt(Mathf.Max(1f, data.endTime - data.startTime) / intervalMs));
                }
                activeGestureStates[note] = state;
            }
            return state;
        }

        private void PulseGestureContact(ActiveGestureState state, float songPos)
        {
            if (state == null || state.note == null) return;
            if (songPos - state.lastEffectSongPosMs < gestureEffectCooldownMs) return;
            state.lastEffectSongPosMs = songPos;
            try { ShowNoteMeshEffect(state.note, JudgmentResult.Perfect, false, true, 0f, false, state.lastLane); } catch { }
            try { state.note.PlayJudgmentLineFanParticles(false, state.lastLane); } catch { }
        }

        // Called for every physical key-down. A Trill deliberately uses new presses rather
        // than a continuously held key, while Slide contact is also polled every frame below.
        private NoteController RegisterGestureInput(int lane, float songPos)
        {
            NoteController matchedTrill = null;
            foreach (var note in activeNotes)
            {
                if (note == null) continue;
                // 先看便宜的資料欄位：絕大多數音符不是滑奏或顫音，不必去碰 IsActive。
                var data = note.NoteData;
                if (!IsGestureNote(data)) continue;
                if (!note.IsActive || note.IsJudged || note.IsJudgmentSuppressed) continue;
                int minLane;
                int maxLane;
                if (IsSlideNote(data)) GetSlideContactRange(data, out minLane, out maxLane);
                else
                {
                    minLane = Mathf.Min(data.startLane, data.endLane);
                    maxLane = Mathf.Max(data.startLane, data.endLane);
                }
                if (lane < minLane || lane > maxLane) continue;
                float earlyWindow = IsSlideNote(data) ? slideContactGraceMs : goodMs;
                if (songPos < data.startTime - earlyWindow || songPos > data.endTime + slideContactGraceMs) continue;

                var state = GetGestureState(note);
                bool firstContact = !state.touched;
                state.touched = true;
                state.lastInputSongPosMs = songPos;
                state.lastLane = lane;
                if (IsTrillNote(data))
                {
                    // Every Trill contact consumes the physical press in the
                    // same way. Previously only the first contact returned a
                    // note, so later contacts leaked into ordinary Tap logic.
                    if (matchedTrill == null) matchedTrill = note;
                    float intervalMs = Mathf.Max(50f, trillMaximumInputGapSeconds * 1000f);
                    float contactSongPos = Mathf.Max(songPos, data.startTime);
                    bool acceptedInput = state.trillFailureSongPosMs == float.MinValue;
                    if (acceptedInput)
                    {
                        float previousContact = state.trillHasAcceptedInput
                            ? state.trillLastAcceptedInputSongPosMs
                            : data.startTime;
                        if (contactSongPos - previousContact > intervalMs + 0.001f)
                        {
                            // A late input cannot revive a broken Trill. Remember the exact
                            // timeout so already-completed score ticks can still be awarded.
                            state.trillFailureSongPosMs = previousContact + intervalMs;
                            acceptedInput = false;
                        }
                        else
                        {
                            state.trillHasAcceptedInput = true;
                            state.trillLastAcceptedInputSongPosMs =
                                Mathf.Max(previousContact, contactSongPos);
                        }
                    }

                    int tick = Mathf.Clamp(
                        Mathf.FloorToInt(Mathf.Max(0f, songPos - data.startTime) / intervalMs),
                        0, Mathf.Max(0, state.trillTotalTicks - 1));
                    bool firstInputForTick = acceptedInput && tick != state.trillLastFeedbackTick;
                    if (firstInputForTick) state.trillLastFeedbackTick = tick;
                    if (acceptedInput)
                    {
                        try { note.SetGestureVisualStrength(1f); } catch { }
                    }
                    if (acceptedInput && firstContact)
                    {
                        try { note.BeginStreamingJudgment(songPos); } catch { }
                        JudgmentResult headResult = TapJudgment.Judge(note, songPos,
                            perfectMs, greatMs, goodMs);
                        var headPayload = RentPayload();
                        headPayload.note = note;
                        headPayload.result = headResult;
                        headPayload.buttonId = lane;
                        headPayload.inputSongPosMs = songPos;
                        headPayload.pressSongPosMs = songPos;
                        headPayload.releaseSongPosMs = 0f;
                        headPayload.targetTimeMs = data.startTime;
                        headPayload.absoluteOffsetMs = Mathf.Abs(songPos - data.startTime);
                        headPayload.triggerPopup = false;
                        headPayload.triggerMesh = false;
                        headPayload.triggerHitSound = true;
                        headPayload.isHoldHead = false;
                        headPayload.isHoldTail = false;
                        // The protected head is the first 0.125-second Trill
                        // judgment, not an extra score item.
                        headPayload.isHoldExtra = true;
                        headPayload.usePersistentMesh = false;
                        headPayload.headInputSongPosMs = songPos;
                        headPayload.headTargetTimeMs = data.startTime;
                        headPayload.headAbsoluteOffsetMs = Mathf.Abs(songPos - data.startTime);
                        headPayload.hasHeadTiming = true;
                        Protection.ResolveHeadJudgment(note, headPayload, songPos, true,
                            GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                        state.trillHeadJudgmentQueued = true;
                    }
                    // Trill judgments are committed at the slot boundary, but
                    // feedback must happen on the physical press. Without this
                    // popup it looked as if Trill was never judged even while
                    // its score ticks were being counted in the background.
                    if (firstInputForTick)
                    {
                        try { ShowJudgementPopups(lane, note.transform.position, JudgmentResult.Perfect); } catch { }
                    }
                }
                PulseGestureContact(state, songPos);
            }
            return matchedTrill;
        }

        private void ResolveGesture(NoteController note, ActiveGestureState state, JudgmentResult result, float songPos)
        {
            if (note == null || note.IsJudged || note.IsJudgmentSuppressed) return;
            var data = note.NoteData;
            var payload = RentPayload();
            payload.note = note;
            payload.result = result;
            payload.buttonId = state != null && state.lastLane >= 0 ? state.lastLane : (data != null ? data.startLane : -1);
            payload.inputSongPosMs = songPos;
            payload.pressSongPosMs = songPos;
            payload.releaseSongPosMs = songPos;
            payload.targetTimeMs = data != null ? data.endTime : songPos;
            payload.absoluteOffsetMs = 0f;
            payload.triggerPopup = true;
            payload.triggerMesh = result != JudgmentResult.Miss;
            payload.triggerHitSound = result != JudgmentResult.Miss;
            payload.isHoldHead = false;
            payload.isHoldTail = false;
            payload.isHoldExtra = false;
            payload.usePersistentMesh = false;
            payload.hasHeadTiming = false;
            ApplyJudgmentPayload(payload);
            activeGestureStates.Remove(note);
        }

        private void AwardTrillTick(NoteController note, ActiveGestureState state,
            JudgmentResult result, float songPos, bool sustainTick = true)
        {
            if (note == null || note.IsJudged || note.IsJudgmentSuppressed) return;
            var data = note.NoteData;
            var payload = RentPayload();
            payload.note = note;
            payload.result = result;
            payload.buttonId = state != null && state.lastLane >= 0
                ? state.lastLane
                : (data != null ? data.startLane : -1);
            payload.inputSongPosMs = songPos;
            payload.pressSongPosMs = songPos;
            payload.releaseSongPosMs = 0f;
            payload.targetTimeMs = songPos;
            payload.absoluteOffsetMs = 0f;
            payload.triggerPopup = false;
            payload.triggerMesh = false;
            payload.triggerHitSound = false;
            payload.isHoldHead = false;
            payload.isHoldTail = false;
            payload.isHoldExtra = true;
            // Trill slots after the head are length-driven continuation, not
            // separate aimed hits, so they score at the sustain weight. Slot 0 is
            // the real head and keeps full weight: normally the input path awards
            // it as the protected head payload, but Auto Play never runs that path
            // (it only marks contact), so slot 0 arrives here instead. Weighting it
            // as a sustain tick cost 0.75 per Trill against an expected total that
            // StatsManager builds with the head at 1.0, which is why a flawless
            // Auto run of Melodiniq EXPERT stopped at 998,060 -- exactly its 8
            // Trills x 0.75 / 3092.25 total weight.
            payload.isSustainTick = sustainTick;
            payload.usePersistentMesh = false;
            payload.hasHeadTiming = false;
            ApplyJudgmentPayload(payload);
        }

        private void FinishSuccessfulTrill(NoteController note, ActiveGestureState state)
        {
            if (note == null || note.IsJudged) return;
            // A very short Trill can end before its protected head payload is
            // released. Keep the visual alive until that payload commits;
            // otherwise pooling the note would cancel the protected judgment.
            if (Protection.HasPendingHead(note)) return;
            // Tick timing uses the player-adjusted judgment clock, while the
            // ribbon travels on the raw audio/visual clock. Do not pool a
            // successful Trill before its rendered tail has actually reached
            // the end marker, especially when Judgment Offset is positive.
            try
            {
                var data = note.NoteData;
                var visualConductor = _cachedConductor != null
                    ? _cachedConductor
                    : (GameManager.Instance != null ? GameManager.Instance.Conductor : null);
                if (data != null && visualConductor != null &&
                    visualConductor.effectiveSongPosition + 0.5f < data.endTime)
                    return;
            }
            catch { }
            try { note.SetGestureVisualStrength(1f); } catch { }
            try { note.OnJudged(JudgmentResult.Perfect); } catch { }
            activeGestureStates.Remove(note);
        }

        private void FailTrillAndRemainingTicks(NoteController note, ActiveGestureState state, float songPos)
        {
            if (note == null || state == null || note.IsJudged) return;
            int remaining = Mathf.Max(1, state.trillTotalTicks - state.trillAwardedTicks);
            // Count every unplayed 0.125-second slot. The last Miss uses the normal
            // finalization path so the note disappears and one popup is shown.
            int silentMisses = remaining - 1;
            if (silentMisses > 0 && !note.IsTutorialDemo)
            {
                try { StatsManager.Instance?.IncrementMany(JudgmentResult.Miss, silentMisses); } catch { }
                _hudDirty = true;
            }
            try { note.SetGestureVisualStrength(0f); } catch { }
            ResolveGesture(note, state, JudgmentResult.Miss, songPos);
        }

        private void ProcessGestureNotes(float songPos)
        {
            gestureNotesScratch.Clear();
            foreach (var note in activeNotes)
            {
                if (note == null || !note.IsActive || note.IsJudged || note.IsJudgmentSuppressed) continue;
                if (IsGestureNote(note.NoteData)) gestureNotesScratch.Add(note);
            }

            float trillGapMs = Mathf.Max(50f, trillMaximumInputGapSeconds * 1000f);
            foreach (var note in gestureNotesScratch)
            {
                if (note == null || !note.IsActive || note.IsJudged) continue;
                var data = note.NoteData;
                if (data == null || songPos < data.startTime - goodMs) continue;
                var state = GetGestureState(note);

                // Auto Play must be frame-rate independent. Slide nodes can be
                // only 1-3 ms long, so a frame can cross the complete contact
                // window. Once Auto has crossed the start, register contact
                // before the expiration branch below resolves the node.
                if (AutoFor(note) && songPos >= data.startTime)
                {
                    bool firstDebugContact = !state.touched;
                    state.touched = true;
                    state.lastInputSongPosMs = songPos;
                    state.lastLane = Mathf.RoundToInt((data.startLane + data.endLane) * 0.5f);
                    if (firstDebugContact && IsTrillNote(data))
                    {
                        try { note.BeginStreamingJudgment(data.startTime); } catch { }
                    }
                    PulseGestureContact(state, songPos);
                }

                if (IsSlideNote(data))
                {
                    if (songPos >= data.startTime - slideContactGraceMs &&
                        songPos <= data.endTime + slideContactGraceMs &&
                        TryGetPressedLaneForGesture(data, out var heldLane))
                    {
                        state.touched = true;
                        state.lastInputSongPosMs = songPos;
                        state.lastLane = heldLane;
                        PulseGestureContact(state, songPos);
                    }
                    if (songPos >= data.endTime + slideContactGraceMs)
                        ResolveGesture(note, state, state.touched ? JudgmentResult.Perfect : JudgmentResult.Miss, songPos);
                    continue;
                }

                // Trill score still contains one result per 0.125 seconds, but continuity
                // is rolling: the real time between any two accepted inputs may never
                // exceed the configured maximum gap.
                if (AutoFor(note) && songPos >= data.startTime && songPos <= data.endTime)
                {
                    state.trillHasAcceptedInput = true;
                    state.trillLastAcceptedInputSongPosMs = songPos;
                    state.trillFailureSongPosMs = float.MinValue;
                }

                float fadeOrigin = state.touched ? state.lastInputSongPosMs : data.startTime;
                float visualStrength = songPos < data.startTime
                    ? 1f
                    : 1f - Mathf.Clamp01((songPos - fadeOrigin) / trillGapMs);
                try { note.SetGestureVisualStrength(visualStrength); } catch { }

                float continuityDeadline = state.trillHasAcceptedInput
                    ? state.trillLastAcceptedInputSongPosMs + trillGapMs
                    : data.startTime + trillGapMs;
                float failureTime = state.trillFailureSongPosMs != float.MinValue
                    ? state.trillFailureSongPosMs
                    : continuityDeadline;

                while (state.trillHasAcceptedInput &&
                       state.trillAwardedTicks < state.trillTotalTicks)
                {
                    int tickIndex = state.trillAwardedTicks;
                    float boundary = Mathf.Min(data.endTime, data.startTime + ((tickIndex + 1) * trillGapMs));
                    if (songPos < boundary) break;
                    if (boundary > failureTime + 0.001f) break;

                    if (tickIndex == 0 && state.trillHeadJudgmentQueued)
                    {
                        state.trillAwardedTicks++;
                        continue;
                    }

                    AwardTrillTick(note, state, JudgmentResult.Perfect, boundary,
                        sustainTick: tickIndex > 0);
                    state.trillAwardedTicks++;
                }

                if (state.trillAwardedTicks >= state.trillTotalTicks)
                {
                    FinishSuccessfulTrill(note, state);
                    continue;
                }

                // Do not fail exactly on the mathematical boundary: an event timestamped
                // at that same instant is valid. Once the clock is past it, the chain is
                // permanently broken and every unplayed result becomes Miss.
                if (songPos > failureTime + 0.001f)
                {
                    state.failed = true;
                    FailTrillAndRemainingTicks(note, state, failureTime);
                }
            }
        }

        private bool TryStartHeldHoldContact(NoteController note, int buttonId, float songPos)
        {
            if (note == null) return false;
            var nd = note.NoteData;
            if (nd == null) return false;

            var handler = GetHandler(note);
            float pressSongPos = Mathf.Max(songPos, nd.startTime);
            float delta = Mathf.Abs(nd.startTime - songPos);
            JudgmentResult? forcedResult = note.IsSoft ? JudgmentResult.Perfect : (JudgmentResult?)null;
            BeginHoldTracking(note, handler, buttonId, pressSongPos, delta, forcedResult);
            return true;
        }

        private bool TryResumeActiveHold(int buttonId, float songPos, int curFrame,
            long inputEventId = 0)
        {
            ActiveHoldState bestHold = null;
            float bestEndDelta = float.MaxValue;

            foreach (var kv in activeHoldStates)
            {
                var st = kv.Value; if (st == null || st.note == null) continue;
                var ndH = st.note.NoteData; if (ndH == null) continue;
                if (st.note.IsJudgmentSuppressed) continue;
                if (st.note.IsStaccato) continue;
                int minLane = Mathf.Min(ndH.startLane, ndH.endLane);
                int maxLane = Mathf.Max(ndH.startLane, ndH.endLane);
                if (buttonId < minLane || buttonId > maxLane) continue;
                if (songPos < ndH.startTime || songPos > ndH.endTime + goodMs) continue;

                float endDelta = ndH.endTime - songPos;
                if (endDelta >= 0f && endDelta < bestEndDelta)
                {
                    bestEndDelta = endDelta;
                    bestHold = st;
                }
            }

            if (bestHold == null) return false;

            try { bestHold.note?.ClearHeadMissed(); } catch { }
            try { bestHold.pendingSwapDeadline = float.MinValue; } catch { }
            BeginHoldTracking(bestHold.note, buttonId, songPos);
            ConsumeInputForNote(buttonId, curFrame, songPos, inputEventId);
            return true;
        }

        private NoteController FindRecoverableMissedHold(int buttonId, float songPos)
        {
            foreach (var n in activeNotes)
            {
                if (n == null) continue;
                if (n.IsJudgmentSuppressed) continue;
                // 鍵道和時間先篩：每一下沒配到音符的按鍵都會掃一次全部音符。
                var ndMiss = n.NoteData; if (ndMiss == null) continue;
                if (buttonId < ndMiss.startLane || buttonId > ndMiss.endLane) continue;
                if (songPos < ndMiss.startTime || songPos > ndMiss.endTime + goodMs) continue;
                if (!HoldJudgment.IsHoldLikeNote(n)) continue;
                if (!n.IsActive) continue;
                if (n.IsJudgeable) continue;
                return n;
            }

            return null;
        }

        private bool TryRecoverMissedHold(int buttonId, float songPos, int curFrame,
            long inputEventId = 0)
        {
            var missedHold = FindRecoverableMissedHold(buttonId, songPos);
            if (missedHold == null) return false;

            return TryRecoverMissedHold(missedHold, buttonId, songPos, curFrame, true, inputEventId);
        }

        private bool TryRecoverMissedHold(NoteController missedHold, int buttonId, float songPos,
            int curFrame, bool consumeSpatial, long inputEventId = 0)
        {
            if (missedHold == null) return false;

            try { missedHold.ClearHeadMissed(); } catch { }
            var nd = missedHold.NoteData;
            float target = nd != null ? nd.startTime : songPos;
            var headResult = TapJudgment.Judge(missedHold, songPos, perfectMs, greatMs, goodMs);
            var headPayload = RentPayload();
            headPayload.note = missedHold;
            headPayload.result = headResult;
            headPayload.buttonId = buttonId;
            headPayload.inputSongPosMs = songPos;
            headPayload.pressSongPosMs = songPos;
            headPayload.releaseSongPosMs = 0f;
            headPayload.targetTimeMs = target;
            headPayload.absoluteOffsetMs = Mathf.Abs(target - songPos);
            headPayload.triggerPopup = true;
            headPayload.triggerMesh = true;
            headPayload.triggerHitSound = true;
            headPayload.isHoldHead = true;
            headPayload.isHoldTail = false;
            headPayload.usePersistentMesh = true;
            headPayload.headInputSongPosMs = songPos;
            headPayload.headTargetTimeMs = target;
            headPayload.headAbsoluteOffsetMs = Mathf.Abs(target - songPos);
            headPayload.hasHeadTiming = true;
            bool stored = Protection.ResolveHeadJudgment(missedHold, headPayload, songPos, true, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
            try { missedHold.OnHoldStart(headResult); } catch { }
            BeginHoldTracking(missedHold, buttonId, songPos);
            try { if (stored && activeHoldStates.TryGetValue(missedHold, out var s2)) s2.headJudgmentPending = true; } catch { }
            if (consumeSpatial)
            {
                ConsumeInputForNote(buttonId, curFrame, songPos, inputEventId);
                ClaimNote(nd, buttonId, songPos, target);
            }
            return true;
        }

        private void ProcessHeldHoldContacts(float songPos)
        {
            foreach (var note in activeNotes)
            {
                if (note == null || !note.IsActive || note.IsJudgmentSuppressed) continue;
                if (!HoldJudgment.IsHoldLikeNote(note)) continue;

                var nd = note.NoteData;
                if (nd == null) continue;
                if (!TryGetPressedLaneInRange(nd, out var lane)) continue;

                bool hasState = activeHoldStates.TryGetValue(note, out var state) && state != null;
                if (hasState)
                {
                    if (state.pressedLanes != null && state.pressedLanes.Count > 0) continue;
                    if (songPos < nd.startTime || songPos > nd.endTime + goodMs) continue;

                    try { note.ClearHeadMissed(); } catch { }
                    try { state.pendingSwapDeadline = float.MinValue; } catch { }
                    BeginHoldTracking(note, lane, songPos);
                    continue;
                }

                if (note.IsJudgeable)
                {
                    if (songPos < nd.startTime || songPos > nd.startTime + goodMs) continue;
                    TryStartHeldHoldContact(note, lane, songPos);
                    continue;
                }

                if (songPos < nd.startTime || songPos > nd.endTime + goodMs) continue;
                TryRecoverMissedHold(note, lane, songPos, -1, false);
            }
        }

        private bool ShouldSuppressLaterOverlappedNote(NoteController earlier, NoteController later, float songPos)
        {
            if (earlier == null || later == null || earlier == later) return false;
            if (later.IsJudged || later.IsJudgmentSuppressed) return false;
            if (earlier.IsJudgmentSuppressed) return false;
            if (earlier.IsSoft || later.IsSoft) return false;

            var earlierData = earlier.NoteData;
            var laterData = later.NoteData;
            if (earlierData == null || laterData == null) return false;
            // Slide chains intentionally overlap in both time and lanes. Trill also spans a
            // range for its whole duration. Neither may participate in TAP overlap suppression.
            if (IsGestureNote(earlierData) || IsGestureNote(laterData)) return false;
            if (laterData.startTime <= earlierData.startTime) return false;
            if (!HasLaneRangeOverlap(earlierData, laterData)) return false;
            if (!HasTimeRangeOverlap(earlierData, laterData)) return false;

            // Never suppress a later note while its own judgment window is still open. The player
            // must always get their full window to hit an overlapping note. Otherwise releasing a
            // HOLD early (which finalizes/judges the hold) would instantly suppress every not-yet-
            // reached note sitting inside the hold's time span, even though those notes remain
            // perfectly hittable — the exact "early release loses later overlapping notes" bug.
            if (songPos <= laterData.startTime + goodMs) return false;

            return earlier.IsJudged || songPos > GetRangeEnd(earlierData) + goodMs;
        }

        private void SuppressJudgmentForNote(NoteController note)
        {
            if (note == null || note.IsJudged || note.IsJudgmentSuppressed) return;
            try { CleanupHoldState(note); } catch { }
            try { Protection.RemoveProtection(note); } catch { }
            try { note.SuppressJudgment(); } catch { }
        }

        private void SuppressLaterOverlappedNotes(NoteController earlier, float songPos)
        {
            if (earlier == null) return;
            foreach (var other in activeNotes)
            {
                if (!ShouldSuppressLaterOverlappedNote(earlier, other, songPos)) continue;
                SuppressJudgmentForNote(other);
            }
        }

        private int _overlapSuppressionFrame = -1;

        private void ProcessOverlappedJudgmentSuppressions(float songPos)
        {
            // 每一下按鍵、每一次找音符都會叫到這裡，而它對每顆已判定的音符都要掃一遍
            // 全部音符。抑制條件以 goodMs 為界，同一格內 songPos 差幾毫秒不會改變結果，
            // 所以一格只做一次。
            int frame = Time.frameCount;
            if (frame == _overlapSuppressionFrame) return;
            _overlapSuppressionFrame = frame;

            foreach (var earlier in activeNotes)
            {
                if (earlier == null) continue;
                if (!earlier.IsActive && !earlier.IsJudged) continue;
                if (earlier.IsJudgmentSuppressed) continue;
                var earlierData = earlier.NoteData;
                if (earlierData == null) continue;
                // Suppression is impossible until the source note is judged or
                // its complete range has expired. Skipping the inner scan here
                // turns the normal gameplay path from O(activeNotes²) into O(n);
                // only the few completed overlap sources pay the pair scan.
                if (!earlier.IsJudged && songPos <= GetRangeEnd(earlierData) + goodMs)
                    continue;
                SuppressLaterOverlappedNotes(earlier, songPos);
            }
        }

        // Returns true if there are co-judge notes, and judges them all.
        // Does NOT consume the button when returning true.
        // Co-judge condition:
        //   1) |other.startTime - primary.startTime| <= SharedInputThresholdMs (default 20ms)
        //   2) max(minLaneA, minLaneB) <= min(maxLaneA, maxLaneB)  (lane-range overlap)
        private bool TryCoJudge(NoteController primary, int buttonId, float songPos)
        {
            bool anyCoJudge = false;
            try
            {
                float primaryStart = primary?.NoteData != null ? primary.NoteData.startTime : songPos;
                var primaryNd = primary?.NoteData;
                // Shared-input (co-judge) window: two overlapping notes whose starts fall within
                // SharedInputThresholdMs (default 100ms) are hit by a single press. This is the
                // documented shared-input rule and must stay INDEPENDENT of the 20ms spatial-stop
                // window (which only blocks *neighbouring* lanes). Wiring it to SpatialStopWindowMs
                // silently shrank the co-judge window to 20ms, so 20–100ms stacked notes dropped a
                // judgment on a single press.
                // This is chord tolerance, not a rapid-note judgment window. A 100ms value
                // consumed genuine repetitions before the player could hit them and graded
                // them against the previous press, creating a systematic FAST bias.
                int coJudgeWindowMs = _inputProtection != null
                    ? Mathf.Clamp(_inputProtection.SharedInputThresholdMs, 0, 20)
                    : 20;

                foreach (var other in GetNearbyNotes(songPos))
                {
                    if (other == null || other == primary) continue;
                    if (!other.IsActive || !other.IsJudgeable || other.IsJudged || other.IsJudgmentSuppressed) continue;
                    if (other.IsSoft) continue;
                    var ond = other.NoteData; if (ond == null) continue;
                    // Gesture heads were already registered by
                    // RegisterGestureInput. Treating another Trill as a Tap
                    // here would destroy its entire remaining body.
                    if (IsGestureNote(ond)) continue;
                    if (Mathf.Abs(ond.startTime - primaryStart) > coJudgeWindowMs) continue;
                    if (!HasLaneRangeOverlap(primaryNd, ond)) continue;
                    // Co-judge: this note shares the input
                    anyCoJudge = true;
                    if (HoldJudgment.IsHoldLikeNote(other))
                    {
                        var coH = GetHandler(other);
                        int coButton = ClampLaneToNoteRange(buttonId, ond);
                        float coDelta = Mathf.Abs(ond.startTime - songPos);
                        JudgmentResult? coForced = other.IsSoft ? JudgmentResult.Perfect : (JudgmentResult?)null;
                        try { BeginHoldTracking(other, coH, coButton, songPos, coDelta, coForced); } catch { }
                    }
                    else
                    {
                        int coButton = ClampLaneToNoteRange(buttonId, ond);
                        var coResult = TapJudgment.Judge(other, songPos, perfectMs, greatMs, goodMs);
                        var coNd = other.NoteData;
                        var coPayload = RentPayload();
                        coPayload.note = other;
                        coPayload.result = coResult;
                        coPayload.buttonId = coNd != null ? ClampLaneToNoteRange(coButton, coNd) : coButton;
                        coPayload.inputSongPosMs = songPos;
                        coPayload.pressSongPosMs = songPos;
                        coPayload.releaseSongPosMs = 0f;
                        coPayload.targetTimeMs = coNd?.startTime ?? songPos;
                        coPayload.absoluteOffsetMs = Mathf.Abs((coNd?.startTime ?? songPos) - songPos);
                        coPayload.triggerPopup = true;
                        coPayload.triggerMesh = true;
                        coPayload.triggerHitSound = true;
                        coPayload.isHoldHead = false;
                        coPayload.isHoldTail = false;
                        coPayload.usePersistentMesh = false;
                        coPayload.headInputSongPosMs = songPos;
                        coPayload.headTargetTimeMs = coNd != null ? coNd.startTime : songPos;
                        coPayload.headAbsoluteOffsetMs = Mathf.Abs((coNd != null ? coNd.startTime : songPos) - songPos);
                        coPayload.hasHeadTiming = coNd != null;
                        try { Protection.ResolveHeadJudgment(other, coPayload, songPos, true, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload); } catch { }
                    }
                }
            }
            catch { }
            return anyCoJudge;
        }

        private static int GetResultRank(JudgmentResult res)
        {
            switch (res)
            {
                case JudgmentResult.Perfect: return 0;
                case JudgmentResult.Great: return 1;
                case JudgmentResult.Good: return 2;
                case JudgmentResult.Miss: return 3;
                default: return 4;
            }
        }

        /// <summary>
        /// When a key is held, allow soft notes to auto-judge as soon as they are within the good window.
        /// 只遍歷時間窗內的音符以避免大譜面時全掃 activeNotes。
        /// </summary>
        public void TryAutoSoftOnHold(int buttonId, float songPos)
        {
            ProcessOverlappedJudgmentSuppressions(songPos);
            NoteController best = null;
            float bestDelta = float.MaxValue;
            foreach (var n in GetNearbyNotes(songPos))
            {
                if (n == null || !n.IsActive || !n.IsJudgeable || n.IsJudged || n.IsJudgmentSuppressed) continue;
                if (!n.IsSoft) continue;
                var nd = n.NoteData; if (nd == null) continue;
                if (buttonId < nd.startLane || buttonId > nd.endLane) continue;
                float delta = Mathf.Abs(nd.startTime - songPos);
                if (delta > goodMs) continue;
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = n;
                }
            }

            if (best == null) return;

            var ndSoft = best.NoteData;
            var payload = RentPayload();
            payload.note = best;
            payload.result = JudgmentResult.Perfect;
            payload.buttonId = buttonId;
            payload.inputSongPosMs = songPos;
            payload.pressSongPosMs = songPos;
            payload.releaseSongPosMs = 0f;
            payload.targetTimeMs = ndSoft?.startTime ?? songPos;
            payload.absoluteOffsetMs = Mathf.Abs((ndSoft?.startTime ?? songPos) - songPos);
            payload.triggerPopup = true;
            payload.triggerMesh = true;
            payload.triggerHitSound = true;
            payload.isHoldHead = false;
            payload.isHoldTail = false;
            payload.usePersistentMesh = false;
            payload.headInputSongPosMs = songPos;
            payload.headTargetTimeMs = ndSoft != null ? ndSoft.startTime : songPos;
            payload.headAbsoluteOffsetMs = Mathf.Abs((ndSoft != null ? ndSoft.startTime : songPos) - songPos);
            payload.hasHeadTiming = ndSoft != null;
            ApplyJudgmentPayload(payload);
        }

                    // Legacy-like BeginHoldTracking that accepts an INoteHandler and mirrors old manager behavior
                    public void BeginHoldTracking(NoteController note, INoteHandler handler, int buttonId, float pressSongPos, float deltaMs, JudgmentResult? forcedResult)
                    {
                        if (note == null || handler == null) return;

                        float inputSongPos = pressSongPos;
                        float startTime = (note.NoteData != null) ? note.NoteData.startTime : inputSongPos;
                        pressSongPos = Mathf.Max(pressSongPos, startTime);

                        HoldNoteHandler.HoldBeginInfo beginInfo = default;
                        if (!activeHoldStates.TryGetValue(note, out var state) || state == null)
                        {
                            if (handler is HoldNoteHandler holdHandler)
                            {
                                // KeyHitEffectManager disabled: per-key visual removed (Judge mesh only).
                                var request = new HoldNoteHandler.HoldBeginRequest
                                {
                                    Note = note,
                                    ButtonId = buttonId,
                                    PressSongPos = inputSongPos,
                                    DeltaMs = Mathf.Abs(deltaMs),
                                    ForcedResult = forcedResult
                                };
                                beginInfo = holdHandler.BeginHold(request);
                            }
                            else
                            {
                                JudgmentResult headResult;
                                if (forcedResult.HasValue)
                                {
                                    headResult = forcedResult.Value;
                                }
                                else
                                {
                                    try { headResult = handler.EvaluateHead(note, inputSongPos, Mathf.Abs(deltaMs), buttonId); }
                                    catch { headResult = HoldJudgment.EvaluateHoldHeadResult(Mathf.Abs(deltaMs), perfectMs, greatMs, goodMs); }
                                }
                                try { handler.PlayHeadSound(note, inputSongPos, headResult); } catch { }
                                beginInfo = new HoldNoteHandler.HoldBeginInfo(headResult, headResult != JudgmentResult.Miss);
                            }

                            if (beginInfo.Result != JudgmentResult.Miss)
                            {
                                try { note.BeginStreamingJudgment(inputSongPos); } catch { }
                            }
                            try { note.OnHoldStart(beginInfo.Result); } catch { }

                            state = new ActiveHoldState
                            {
                                note = note,
                                pressSongPos = pressSongPos,
                                pendingFinalizeCoroutine = null,
                                headInputSongPosMs = 0f,
                                headTargetTimeMs = 0f,
                                headAbsoluteOffsetMs = 0f,
                                hasHeadTiming = false
                            };
                            // compute extra-judgment cap for holds based on BPM and formula (use helper)
                            try
                            {
                                var ndLocal = note.NoteData;
                                float bpm = 120f;
                                try { if (GameManager.Instance != null && GameManager.Instance.Conductor != null) bpm = GameManager.Instance.Conductor.bpm; } catch { }
                                if (ndLocal != null && ndLocal.startTime < ndLocal.endTime)
                                {
                                    var info = HoldJudgment.ComputeExtraInfo(ndLocal, bpm);
                                    state.maxExtra = info.maxExtra;
                                    state.eighthMs = info.eighthMs;
                                }
                            }
                            catch { }
                            activeHoldStates[note] = state;
                            // No longer consider this note untracked
                            try { untrackedHoldLastCheck.Remove(note); } catch { }

                            if (_cachedDebugMode) BuildLogger.Log($"[JMgr] BeginHoldTracking(handler) created state for note={note?.name ?? "<null>"} pressSongPos={pressSongPos} maxExtra={state.maxExtra}");

                            if (beginInfo.UsePersistentMesh)
                            {
                                float headTimingOffset = inputSongPos - startTime;
                                try { ShowNoteMeshEffect(note, beginInfo.Result, false,
                                    true, headTimingOffset, true, buttonId); } catch { }
                                try { ShowNoteMeshEffect(note, beginInfo.Result, true,
                                    false, 0f, false, buttonId); } catch { }
                            }
                            try { handler.OnBeginHold(note, buttonId, inputSongPos, deltaMs, beginInfo.Result); } catch { }
                            var nd = note.NoteData;
                            float targetTime = nd != null ? nd.startTime : inputSongPos;
                            float absoluteOffset = Mathf.Abs(inputSongPos - targetTime);
                            var payload = RentPayload();
                            payload.note = note;
                            payload.result = beginInfo.Result;
                            payload.buttonId = buttonId;
                            payload.inputSongPosMs = inputSongPos;
                            payload.pressSongPosMs = pressSongPos;
                            payload.targetTimeMs = targetTime;
                            payload.absoluteOffsetMs = absoluteOffset;
                            payload.usePersistentMesh = beginInfo.UsePersistentMesh;
                            payload.isHoldHead = true;
                            payload.isHoldTail = false;
                            payload.triggerMesh = false;
                            payload.triggerPopup = true;
                            payload.triggerHitSound = false;

                            bool stored = Protection.ResolveHeadJudgment(note, payload, inputSongPos, true,
                                GetNearbyNotes(inputSongPos), goodMs, ApplyJudgmentPayload);
                            if (stored && payload.isHoldHead)
                            {
                                state.headJudgmentPending = true;
                            }
                        }
                        else
                        {
                            if (state.pendingFinalizeCoroutine != null)
                            {
                                try { StopCoroutine(state.pendingFinalizeCoroutine); } catch { }
                                state.pendingFinalizeCoroutine = null;
                            }
                            try { handler.OnBeginHold(note, buttonId, pressSongPos, deltaMs, forcedResult); } catch { }
                        }

                        state.pressedLanes.Add(buttonId);
                        laneToHoldState[buttonId] = state;
                        try { NoteJudgementMeshManager.EnsureCreated().SetPersistentLit(note, true); } catch { }
                        if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] BeginHoldTracking(handler) created state for note={note?.name ?? "<null>"} button={buttonId} pressSongPos={pressSongPos} maxExtra={state.maxExtra}"); } catch { } }
                        try
                        {
                            ParticleEffectPlayer.NextSealIsPrecise =
                                _cachedRecitalMode && !note.IsTrill && !note.IsSlide
                                && note.NoteData != null
                                && DynamicMatches(note.NoteData, buttonId);
                            HitEffectRouter.BeginHold(note, buttonId);
                        }
                        catch { }
                        finally { ParticleEffectPlayer.NextSealIsPrecise = false; }
                        // KeyHitEffectManager disabled: per-key visual removed (Judge mesh only).
                    }

        // --- Hold input tracking (public API for input systems) ---
        // Call this when a hold key/lane is pressed down and the game should begin tracking a hold for `note`.
        public void BeginHoldTracking(NoteController note, int lane, float songPos)
        {
            if (note == null) return;
            try
            {
                if (!activeHoldStates.TryGetValue(note, out var st))
                {
                    try { note.BeginStreamingJudgment(songPos); } catch { }
                    st = new ActiveHoldState { note = note, pressSongPos = songPos };
                    try
                    {
                        var nd = note.NoteData;
                        float bpm = 120f;
                        try { if (GameManager.Instance != null && GameManager.Instance.Conductor != null) bpm = GameManager.Instance.Conductor.bpm; } catch { }
                        if (nd != null)
                        {
                            var info = HoldJudgment.ComputeExtraInfo(nd, bpm);
                            st.maxExtra = info.maxExtra;
                            st.eighthMs = info.eighthMs;
                            if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] BeginHoldTracking(input) computed extra for note={note?.name ?? "<null>"} start={nd.startTime} end={nd.endTime} maxExtra={st.maxExtra}"); } catch { } }
                        }
                    }
                    catch { }
                    activeHoldStates[note] = st;
                    // No longer consider this note untracked
                    try { untrackedHoldLastCheck.Remove(note); } catch { }
                    if (_cachedDebugMode) BuildLogger.Log($"[JMgr] BeginHoldTracking(input) created state for note={note?.name ?? "<null>"} pressSongPos={songPos} maxExtra={st.maxExtra}");
                }
                st.pressedLanes.Add(lane);
                laneToHoldState[lane] = st;
                try { NoteJudgementMeshManager.EnsureCreated().SetPersistentLit(note, true); } catch { }
                try { HitEffectRouter.BeginHold(note, lane); } catch { }
                if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] BeginHoldTracking(input) state for note={note?.name ?? "<null>"} lane={lane} pressSongPos={songPos} maxExtra={st.maxExtra}"); } catch { } }
                    // KeyHitEffectManager disabled: per-key visual removed (Judge mesh only).
            }
            catch { }
        }

        // Call this when a held key generates another 'press' event while held (optional — can be used for effects)
        public void HandleHeldKeyPress(int lane, float songPos)
        {
            try
            {
                if (!laneToHoldState.TryGetValue(lane, out var st)) return;
                // For now we don't re-judge the head here — head judgments are routed through Protection and
                // committed later via ApplyJudgmentPayload. We can trigger local effects and extra per-eighth judgments.
                try { HitEffectRouter.Play(st.note, lane, JudgmentResult.Perfect); } catch { }
                // extra award now handled by per-frame ProcessHoldExtraAwards
            }
            catch { }
        }

        // Call this when the held key/lane is released. This will finalize the hold if no other lanes remain pressed.
        public void HandleHeldKeyRelease(int lane, float songPos)
        {
            try
            {
                // Tap/STAC columns follow the physical key release even though
                // those notes do not own an ActiveHoldState.
                try { NoteJudgementMeshManager.Instance?.ReleaseEffectsForLane(lane); } catch { }
                // The key is up, so whatever comes next is a real strike rather
                // than a bouncing contact. Must run before the hold-state early
                // out below, which most released lanes take.
                try { _inputProtection.ReleaseLane(lane); } catch { }
                // 示範段的長押是自動按住的；玩家放開自己的鍵不能把它放掉。
                if (TutorialSession.BlocksInput(songPos)) return;
                if (!laneToHoldState.TryGetValue(lane, out var st)) return;
                var note = st.note;
                // Input adapters run before JudgmentManager.Update. If a frame stalls, several
                // scheduled hold ticks can elapse before this release event is delivered. Catch
                // them up while this lane is still known to have been held; otherwise clearing
                // pressedLanes first would turn valid historical ticks into Miss judgments.
                try { AwardHoldExtraTicksThrough(st, songPos, true); } catch { }
                // remove lane mapping and persistent mesh
                    // KeyHitEffectManager disabled: per-key visual removed (Judge mesh only).
                st.pressedLanes.Remove(lane);
                laneToHoldState.Remove(lane);

                // if other lanes for this hold are still pressed, don't finalize yet
                if (st.pressedLanes.Count > 0) return;
                // Sliding hardware/touch input can report the old lane's release
                // without emitting a new down edge for the neighbouring lane.
                // Query the complete authored range before declaring a break.
                try
                {
                    var rangeData = note != null ? note.NoteData : null;
                    if (TryGetPressedLaneInRange(rangeData, out int replacementLane))
                    {
                        st.pressedLanes.Add(replacementLane);
                        laneToHoldState[replacementLane] = st;
                        st.pendingSwapDeadline = float.MinValue;
                        try { NoteJudgementMeshManager.EnsureCreated().SetPersistentLit(note, true); } catch { }
                        return;
                    }
                }
                catch { }

                // if no lanes remain pressed, allow a short swap grace window before finalizing
                bool debugModeActive = AutoFor(note);
                bool isStaccato = false;
                try { isStaccato = note != null && note.IsStaccato; } catch { isStaccato = false; }
                if (!debugModeActive && !isStaccato)
                {
                    // Defer finalize by the break tolerance: the player must be released for longer than
                    // holdBreakToleranceMs before the hold stops rewarding / finalizes. Within this window
                    // ProcessHoldExtraAwards treats the note as still held (no per-beat Miss, no combo break),
                    // and a re-press clears this deadline. Clamp to at least goodMs so we never regress the
                    // original finger-swap grace.
                    st.pendingSwapDeadline = songPos + (float)Mathf.Max(goodMs, holdBreakToleranceMs);
                    if (debugModeActive) { try { BuildLogger.Log($"[JMgr] HandleHeldKeyRelease: deferred finalize (swap grace) note={note?.name ?? "<null>"} until={st.pendingSwapDeadline}"); } catch { } }
                    if (debugModeActive) BuildLogger.Log($"[JMgr] HandleHeldKeyRelease: note={note?.name ?? "<null>"} pendingSwapDeadline={st.pendingSwapDeadline}");
                    return;
                }

                // Debug mode or staccato: finalize tail judgment immediately (legacy behavior)
                try { NoteJudgementMeshManager.Instance?.HidePersistentEffect(note); } catch { }
                var nd = note.NoteData;
                float releaseTime = songPos;
                // If this is an auto-started staccato and we are configured to simulate
                // an auto-release, override the release time so auto behaves like a
                // simulated quick release instead of reflecting a long player hold.
                try
                {
                    if (note != null && note.IsStaccato && st != null && st.autoStarted && autoSimulateStaccatoRelease)
                    {
                        var ndLocal = note.NoteData;
                        if (ndLocal != null)
                        {
                            releaseTime = ndLocal.startTime + (float)stacAutoReleaseMs;
                        }
                    }
                }
                catch { }
                JudgmentResult res;
                if (note.IsStaccato)
                {
                    res = StaccatoJudgment.Judge(note, releaseTime, stacPerfectMs, stacGreatMs, stacGoodMs);
                }
                else
                {
                    var handler = GetHandler(note);
                    res = handler != null ? handler.EvaluateHoldEnd(note, releaseTime, st.pressSongPos) : JudgmentResult.Miss;
                }

                float headInput = st.hasHeadTiming ? st.headInputSongPosMs : st.pressSongPos;
                float headTarget = st.hasHeadTiming ? st.headTargetTimeMs : (nd != null ? nd.startTime : st.pressSongPos);
                float headOffset = st.hasHeadTiming ? st.headAbsoluteOffsetMs : UnityEngine.Mathf.Abs(headInput - headTarget);

                var payload = RentPayload();
                payload.note = note;
                payload.result = res;
                payload.buttonId = lane;
                payload.inputSongPosMs = headInput;
                payload.pressSongPosMs = st.pressSongPos;
                payload.releaseSongPosMs = releaseTime;
                payload.targetTimeMs = headTarget;
                payload.absoluteOffsetMs = headOffset;
                payload.triggerPopup = true;
                payload.triggerMesh = false;
                payload.triggerHitSound = false;
                payload.isHoldHead = false;
                payload.isHoldTail = true;
                payload.usePersistentMesh = false;
                payload.headInputSongPosMs = headInput;
                payload.headTargetTimeMs = headTarget;
                payload.headAbsoluteOffsetMs = headOffset;
                payload.hasHeadTiming = st.hasHeadTiming;

                // For tail finalization, do not allow protection for staccato notes (match legacy behavior)
                if (debugModeActive) { try { BuildLogger.Log($"[JMgr] HandleHeldKeyRelease note={note?.name ?? "<null>"} lane={lane} releaseSongPos={songPos} autoStarted={st?.autoStarted} autoSimulate={autoSimulateStaccatoRelease} computedReleaseTime={releaseTime}"); } catch { } }
                // Tail judgments should not use protection — apply immediately or let protection module
                // store only if it already has an entry. Do not create new protection windows for tails.
                bool storedTail = Protection.ResolveTailJudgment(note, payload, songPos, false, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                if (debugModeActive) { try { BuildLogger.Log($"[JProt] ResolveTailJudgment returned storedTail={storedTail} note={note?.name ?? "<null>"}"); } catch { } }
                if (!storedTail)
                {
                    // payload applied immediately by callback; cleanup now
                    try { CleanupHoldState(note); } catch { }
                }
                // if storedTail==true the protection module will call ApplyJudgmentPayload later which will call CleanupHoldState
            }
            catch { }
        }

        private void TryIncrementStats(JudgmentResult res, bool isSustainTick = false)
        {
            try { StatsManager.Instance?.Increment(res, isSustainTick); } catch { }
            // Mark HUD dirty; actual refresh happens once at end of Update() to avoid
            // N TextMeshPro mesh rebuilds when N notes are judged in the same frame.
            _hudDirty = true;
        }

        /// <summary>
        /// Record one judgment whose score share is scaled -- the recital-mode
        /// path, where where the key landed matters as well as when.
        /// </summary>
        private void TryIncrementStats(JudgmentResult res, bool isSustainTick, decimal itemWeight)
        {
            if (itemWeight == 1m) { TryIncrementStats(res, isSustainTick); return; }
            try { StatsManager.Instance?.Increment(res, isSustainTick, itemWeight); } catch { }
            // Mark HUD dirty; actual refresh happens once at end of Update() to avoid
            // N TextMeshPro mesh rebuilds when N notes are judged in the same frame.
            _hudDirty = true;
        }

        /// <summary>
        /// The colour a centre hit's light turns: violet carrying gold.
        /// </summary>
        /// <remarks>
        /// Not plain violet. The rest of this game's light is gold, and a hit
        /// that swapped to a cold purple would read as a different *kind* of
        /// event rather than as a better one. Keeping the warmth in it says
        /// "the same thing, but richer", which is exactly what it is.
        /// </remarks>
        private static readonly Color PreciseGlow = new Color(1.06f, 0.72f, 1.24f, 1f);

        /// <summary>
        /// 這一顆判定該拿到的配分比例。非演奏會模式永遠是 1。
        /// </summary>
        /// <remarks>
        /// 只算真正由一次按鍵claim 的那一下——tap 和 hold 的頭。長條的尾巴和
        /// 每拍的加分沿用原本的配分，否則同一個「按得準不準」會在一條長音上
        /// 被重複扣好幾次。
        /// </remarks>
        private decimal RecitalWeight(JudgmentPayload payload) => RecitalWeight(payload, false);

        /// <param name="count">
        /// True at the two places a note is actually settled, so the tally is one
        /// per note -- the same function is also asked for the weight when a
        /// judgment is handed to the corrector, and counting there would record
        /// every note twice.
        /// </param>
        private decimal RecitalWeight(JudgmentPayload payload, bool count)
        {
            if (!_cachedRecitalMode || payload == null) return 1m;
            // 教學示範段是自動彈的，不是玩家的演奏。旋律檢查是排隊到 Update 才數，
            // 統計靜音蓋不到那裡，所以在源頭就不收。
            if (payload.note != null && payload.note.IsTutorialDemo) return 1m;
            // 這裡以前有一個「自動演奏就直接回 1」的早退，理由是自動演奏拿的是
            // 音符的起始格、寬音符一律算偏格。但那個問題已經在源頭修掉了（自動
            // 演奏現在按的就是中心格），而這個早退還順手把**計數**也跳過了 ——
            // 於是用自動演奏測結算畫面時，中心格一顆都沒記到，評審面板判定
            // 「這一局沒有資料」，整頁維持一般模式的樣子。
            if (payload.isHoldTail || payload.isHoldExtra || payload.isSustainTick) return 1m;
            if (!IsPositiveJudgment(payload.result)) return 1m;
            var nd = payload.note != null ? payload.note.NoteData : null;
            if (nd == null) return 1m;
            if (count)
            {
                try
                {
                    StatsManager.Instance?.RecordRecitalNote();
                    // 本家的「旋律」：這顆音符是不是被一掌拍下去的（同一幀 3 鍵以上）。
                    // 要等這一幀的其他按鍵都進來才數得準，所以先排著，Update 裡再數。
                    // 滑奏本來就要在鍵道間移動，本家也不數它。
                    if (payload.note != null && !payload.note.IsSlide)
                        QueueMelodyCheck(nd, payload.pressSongPosMs);
                    // 踏板：這顆音在響的時候，踏板在不在譜面要的位置。譜面沒有
                    // 踏板、或踏板是譜面自己踩的時候，這裡不會回 true，那一項就
                    // 不列入評分。
                    if (PedalNoteRenderer.TrySamplePedal(payload.inputSongPosMs,
                            out bool wanted, out bool actual))
                        StatsManager.Instance?.RecordPedal(wanted == actual);

                    // 強弱。判得出來的才進分母 —— 和踏板同一條規則。
                    //
                    // 連音和滑音排除：它們本來就要在鍵道之間移動，一顆一顆評強弱
                    // 只會變成評手指剛好掃過去的力道。IsPrecise 也是這樣排的。
                    var note = payload.note;
                    if (note != null && !note.IsTrill && !note.IsSlide
                        && TryJudgeDynamic(nd, payload.buttonId, out bool onDynamic))
                    {
                        StatsManager.Instance?.RecordDynamic(onDynamic);
                        // PRECISE **就是**強弱這件事，所以它和上面那一筆共用同一
                        // 個判斷。分開算的話兩個數字遲早會不一致，而它們講的明明
                        // 是同一件事。
                        StatsManager.Instance?.RecordPreciseAttempt(onDynamic);
                    }
                }
                catch { }
            }
            // 不再依落在哪一格打折：本家的演奏會不評「中心格」，評的是有沒有一掌拍下去。
            return 1m;
        }

        private bool IsPrecise(JudgmentPayload payload)
        {
            if (!_cachedRecitalMode || payload == null) return false;
            // **時間不管。** PRECISE 問的是「力度對不對」，而時間準不準旁邊的
            // JUST / GREAT / GOOD 已經在講了。一個判定名稱只回答一個問題 ——
            // 把兩件事綁在一起的話，玩家看到 PRECISE 少了也不知道該改哪一邊。
            //
            // 只有「按下去的那一瞬間」算。每拍的加分和長音的續判都不是一次新的
            // 落鍵 —— 力度問的是那一下手用了多重，一顆音只問一次。
            if (payload.isHoldExtra || payload.isSustainTick) return false;
            var note = payload.note;
            if (note == null) return false;
            // STAC 的分數是在放開的時候結算的，那一筆就是它唯一的判定 —— 對它
            // 來說「尾巴」才是那一下。其他音符的尾巴則不是新的落鍵。
            if (payload.isHoldTail && !note.IsStaccato) return false;
            // 連音和滑音本來就要在鍵道之間移動，一顆一顆評它們的強弱只會變成
            // 評手指剛好掃過去的力道。
            if (note.IsTrill || note.IsSlide) return false;
            var nd = note.NoteData;
            return nd != null && DynamicMatches(nd, payload.buttonId);
        }

        /// <summary>
        /// 這一下的強弱是不是譜面要的。
        /// </summary>
        /// <remarks>
        /// **為什麼 PRECISE 是強弱不是位置。** 打在哪一格是這個遊戲的規則，強弱
        /// 是這台樂器的規則 —— 一首曲子彈得對不對，從來不是指你的手指落在鍵盤的
        /// 哪一公分，而是指該響的地方響了、該收的地方收了。位置那件事仍然計分、
        /// 仍然有回饋（偏格的鍵會亮黃燈），只是它不再叫做「精準」。
        ///
        /// 界線用的是 <see cref="VelocityBands"/>，也就是**這首曲子自己的**強弱，
        /// 和音符外殼畫的是同一組。彈的人看到的是殼，被評的就該是殼說的那件事。
        ///
        /// **判不了就不算在玩家頭上。** 三種情況會直接放行：這首譜面沒有可用的
        /// 力度資料、這一顆音沒有被還原出力度、輸入裝置根本沒有力度（電腦鍵盤）。
        /// 踏板那一項評分用的是同一條規則 —— 沒有資料的時候不列入，而不是給零分。
        /// </remarks>
        /// <summary>
        /// 玩家自己的「中間力度」。
        /// </summary>
        /// <remarks>
        /// 用很慢的指數平滑追蹤（每顆 2%），所以它跟的是這個人的**手勁**，不是
        /// 剛剛那個樂句的強弱 —— 跟太快的話，一段強奏會把基準整個抬上去，接下來
        /// 正常力度的音符就全部被判成弱。
        ///
        /// 每首歌重來：換一首歌、換一個人、換一台琴都該重新認識。
        /// </remarks>
        /// <summary>譜面沒有對它要求強弱的音符。不算對也不算錯。</summary>
        private static int dynamicUnmarked;

        /// <summary>誤差總和，用來看「差多少」而不只是「對幾顆」。</summary>
        private static float dynamicError;

        private static int dynamicNoBands;
        private static int dynamicNoChartVelocity;
        private static int dynamicNoInput;
        private static int dynamicJudged;
        private static int dynamicMissed;
        private static int dynamicReported;

        /// <summary>
        /// 強弱判得出來嗎，判得出來的話對不對。
        /// </summary>
        /// <remarks>
        /// 兩個答案必須分開回傳。<c>DynamicMatches</c> 把「判不出來」和「判出來而
        /// 且對」都回成 true —— 那對 PRECISE 是對的（判不了就不算在玩家頭上），
        /// 但評分需要知道分母：把判不出來的音符算進去，等於因為資料缺席而扣分。
        /// </remarks>
        /// <summary>
        /// 誤差超過譜面幅度的幾倍就算沒彈到。
        /// </summary>
        /// <remarks>
        /// 用**距離**而不是分段比對。
        ///
        /// 分段的問題在於懸崖長在錯的地方：邊界上差一個力度值就從對翻成錯，格子
        /// 中間差三十卻還是對。而力度本來就是連續的東西，玩家的「差一點」和「差
        /// 很多」在分段底下完全看不出來。
        ///
        /// 0.75 個幅度：譜面的 HalfSpan 是「多遠算完全的強／弱」，所以超過它的
        /// 四分之三，等於把一個該強調的彈成了普通的。
        /// </remarks>
        private const float DynamicTolerance = 0.75f;

        /// <summary>
        /// 這一下的強弱判得出來嗎，判得出來的話彈對了嗎。
        /// </summary>
        /// <remarks>
        /// **只評譜面真的有要求的音符。**
        ///
        /// <see cref="VelocityBands"/> 只把最響的約 18% 和最輕的約 18% 標成強／
        /// 弱，中間六成**沒有任何要求** —— 那六成怎麼彈都算對，等於白送。把它們
        /// 算進來，PRECISE 的數字有一大半來自沒有人要求過的東西。
        ///
        /// 收成只評被標記的那四成之後，PRECISE 的意思變成「這首曲子要你強調的地
        /// 方，你強調了嗎」。分母變小，但每一顆都在回答那個問題。
        /// </remarks>
        private static bool TryJudgeDynamic(NoteData nd, int lane, out bool matched)
        {
            matched = true;
            if (nd == null) return false;
            if (!VelocityBands.Measured) { dynamicNoBands++; ReportDynamics(); return false; }
            if (nd.velocity <= 0) { dynamicNoChartVelocity++; ReportDynamics(); return false; }

            // 譜面沒有對這一顆要求任何事 —— 不評。
            if (VelocityBands.Classify(nd.velocity) == VelocityBands.Band.Normal)
            {
                dynamicUnmarked++;
                ReportDynamics();
                return false;
            }

            PianoKeysound.DescribeLaneInput(lane, out _, out float played, out bool autoPressed);
            if (played < 0f) { dynamicNoInput++; ReportDynamics(); return false; }

            int raw = Mathf.Clamp(Mathf.RoundToInt(played * 127f), 1, 127);
            // 自動演奏按的就是譜面的力度，不經過玩家手勁的換算（見 PianoKeysound.RecordInput）。
            int mapped = autoPressed ? raw : PlayerTouch.ToChartScale(raw);

            // 距離，以譜面自己的幅度為單位。
            float span = Mathf.Max(1f, VelocityBands.HalfSpan);
            float error = Mathf.Abs(mapped - nd.velocity) / span;

            // 往「更誇張」的方向偏不算錯。
            //
            // 譜面要你彈重，你彈得**更重**；要你彈輕，你彈得**更輕** —— 那是把
            // 這個強弱做得更明顯，不是做錯。會扣分的只有往中間縮，也就是該有的
            // 對比沒有做出來。
            bool loud = VelocityBands.Classify(nd.velocity) == VelocityBands.Band.Loud;
            bool overshot = loud ? mapped > nd.velocity : mapped < nd.velocity;
            if (overshot) error = 0f;

            dynamicJudged++;
            dynamicError += error;
            matched = error <= DynamicTolerance;
            if (!matched) dynamicMissed++;
            ReportDynamics();
            return true;
        }

        private static bool DynamicMatches(NoteData nd, int lane)
        {
            // 判不出來就放行：PRECISE 不該因為資料缺席而拿不到。
            return !TryJudgeDynamic(nd, lane, out bool matched) || matched;
        }

        /// <summary>
        /// 每 40 顆講一次力度判定實際在做什麼。
        /// </summary>
        /// <remarks>
        /// 「力度好像永遠不會錯」有三種完全不同的原因：這首譜面沒有可用的力度分界
        /// （沒有分界就沒有對錯）、這些音符本身沒有被還原出力度、或者判定真的跑了
        /// 但兩邊都落在「中」那一段。三者在畫面上長得一模一樣，所以必須讓它自己
        /// 講出是哪一種。
        /// </remarks>
        private static void ReportDynamics()
        {
            int total = dynamicNoBands + dynamicNoChartVelocity + dynamicNoInput
                + dynamicUnmarked + dynamicJudged;
            if (total - dynamicReported < 40) return;
            dynamicReported = total;
            float meanError = dynamicJudged > 0 ? dynamicError / dynamicJudged : 0f;
            Debug.Log($"[Dynamics] judged={dynamicJudged} missed={dynamicMissed} "
                + $"meanError={meanError:F2} "
                + $"| skipped: unmarked={dynamicUnmarked} noBands={dynamicNoBands} "
                + $"noChartVelocity={dynamicNoChartVelocity} noInput={dynamicNoInput} "
                + $"| chartMid={VelocityBands.Middle:F0}±{VelocityBands.HalfSpan:F0} "
                + $"playerMid={PlayerTouch.Level:F0}±{PlayerTouch.Spread:F0}");
        }

        private static bool IsPositiveJudgment(JudgmentResult res)
        {
            return res == JudgmentResult.Perfect || res == JudgmentResult.Great || res == JudgmentResult.Good;
        }

        private void RecordTimingOffsetIfNeeded(JudgmentPayload payload)
        {
            if (payload == null) return;
            if (!IsPositiveJudgment(payload.result)) return;
            // Fast/Late reflects PRESS timing. A hold's head already records its own offset below when the
            // head payload is applied; the tail (release) is not an early/late press event. The old tail
            // branch re-recorded headInputSongPosMs - headTargetTimeMs — i.e. the SAME head offset a second
            // time (double-counting fast/late for every hold), and never measured the release. So tails and
            // per-beat extras contribute no fast/late sample.
            if (payload.isHoldTail) return;
            try
            {
                // Keep an unbiased signed sample for diagnosis/calibration.
                // Rhythmic hold extras use synthetic zero offsets and must not
                // dilute the player's real TAP/HOLD-head timing distribution.
                if (!payload.isHoldExtra)
                {
                    StatsManager.Instance?.RecordRawTimingOffset(
                        payload.inputSongPosMs - payload.targetTimeMs);
                    var claimedNd = payload.note != null ? payload.note.NoteData : null;
                    if (claimedNd != null)
                    {
                        // Hold heads are tracked so a later press is still counted
                        // as a steal, but not offered for correction: the head's
                        // press time is reused when the tail is finalised.
                        // 事後改判會照這個份量加減分。演奏會模式下這一顆的
                        // 份量不見得是 1，傳錯的話改判就會多退或少退。
                        EarlyClaimCorrector.RecordJudgment(
                            claimedNd.startLane, claimedNd.endLane,
                            payload.targetTimeMs, payload.inputSongPosMs,
                            payload.result, RecitalWeight(payload), !payload.isHoldHead,
                            payload.note.transform.position);
                    }
                }
                StatsManager.Instance?.RecordTimingOffset(
                    payload.inputSongPosMs - payload.targetTimeMs, payload.result);
            }
            catch { }
        }

        // Update inspector-assigned HUD texts (optional)
        /// <summary>
        /// Builds the score readout as a legacy <see cref="UnityEngine.UI.Text"/>,
        /// the way the results page draws it, and retires the TMP one.
        /// </summary>
        /// <remarks>
        /// **Why the component type had to change and not just the font.** Both
        /// screens already resolve the same typeface. What made them look like
        /// two different fonts is the renderer: the results total is a legacy
        /// Text -- rasterised glyphs, synthetic bold, an outline made of offset
        /// copies of the mesh -- while the HUD was TMP, which renders from a
        /// signed-distance field with its own faux bold and a shader outline that
        /// hugs the letterform. Same outlines, same weight setting, visibly
        /// different letters. No amount of colour or width tuning closes that,
        /// because the difference is in how the glyph is drawn.
        ///
        /// The results page is the one that cannot move: its total is a legacy
        /// Text on purpose, because some standalone graphics backends produced an
        /// empty TMP mesh for that one large line. So the HUD comes to it.
        ///
        /// **Why the TMP object stays.** Only its component is switched off. It
        /// still owns the RectTransform this copies its placement from, it is
        /// what <c>ConfigureClassicalGameplayHud</c> found the HUD canvas
        /// through, and the existing <c>scoreText</c> write in RefreshHudStats
        /// feeds the new one with no extra plumbing -- that field was declared
        /// and wired for exactly this and had simply never been assigned.
        /// </remarks>
        private void EnsureLegacyScoreText()
        {
            if (scoreText != null || scoreTMP == null) return;

            Font face = ClassicalBookUITheme.GetScoreSourceFont();
            if (face == null) return;   // 拿不到字就維持 TMP，總比沒有分數好

            RectTransform from = scoreTMP.rectTransform;
            var host = new GameObject("ScoreLegacy", typeof(RectTransform));
            host.layer = scoreTMP.gameObject.layer;
            RectTransform rect = host.GetComponent<RectTransform>();
            rect.SetParent(from.parent, false);
            rect.anchorMin = from.anchorMin;
            rect.anchorMax = from.anchorMax;
            rect.pivot = from.pivot;
            rect.anchoredPosition = from.anchoredPosition;
            rect.sizeDelta = from.sizeDelta;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            var legacy = host.AddComponent<UnityEngine.UI.Text>();
            legacy.font = face;
            legacy.fontSize = 48;
            legacy.fontStyle = FontStyle.Bold;
            legacy.alignment = TextAnchor.UpperRight;
            legacy.supportRichText = true;
            legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacy.verticalOverflow = VerticalWrapMode.Overflow;
            legacy.raycastTarget = false;
            legacy.color = ScoreFace;

            // 舊版 Text 沒有字距，得自己把字排開。要加在描邊之前，理由見
            // LetterSpacing 的說明。
            host.AddComponent<LetterSpacing>().Spacing = 5f;

            // 和結算那顆同一套投影。分數的字是白的、"SCORE" 是金的，兩個都在
            // 這一份投影後面 —— Shadow 動的是整份 mesh，不分字色。
            var drop = host.AddComponent<UnityEngine.UI.Shadow>();
            drop.effectColor = ScoreShadow;
            drop.effectDistance = new Vector2(3f, -3f);

            host.transform.SetAsLastSibling();
            scoreTMP.enabled = false;   // 同一個位置只留一個
            scoreText = legacy;
        }

        /// <summary>
        /// The score: pale green inside, gold all round it -- the same two colours
        /// as the results page, so it is the same number in both places.
        /// </summary>
        /// <remarks>
        /// **Why it touches fontMaterial and not fontSharedMaterial.** TMP's
        /// outline lives in the material, and every text styled by
        /// <see cref="StyleClassicalHudText"/> is on the *same shared* one --
        /// setting the outline through the shared material would gild the combo,
        /// the judgment words and anything else drawn with that font. Reading
        /// `fontMaterial` makes TMP hand back an instance for this text alone.
        ///
        /// **Why the word SCORE stays gold.** It is coloured by a rich-text tag
        /// inside the string, which overrides the base colour -- so the label
        /// keeps the page's gold and only the digits go green, which is the same
        /// split the results page uses.
        /// </remarks>
        private static void StyleScoreNumber(TextMeshProUGUI text)
        {
            if (text == null) return;
            // 字面直接指定，不經 ApplyLocalizedFont。那條路是照**字串內容**挑字
            // 的（有中文就換中文字型），對一個內容會變的 HUD 欄位來說，挑到哪一支
            // 取決於它被套用的當下顯示什麼 —— 那不是我們要的。
            //
            // 拿不到就**維持原本的字**，絕不指定 null：寧可字型不一致，也不能讓
            // 分數變成一排缺字方塊。
            //
            // 要在 fontMaterial 之前設：換字型會換掉材質，順序反了描邊會被丟掉。
            TMP_FontAsset face = ClassicalBookUITheme.GetScoreFontAsset();
            if (face != null) text.font = face;

            // 淡綠：飽和的綠會把金框吃掉。兩個顏色的明度要拉開，細細一圈的框才
            // 讀得出來是框 —— 字心亮、外圈暗一階，這樣金色才浮得出來。
            text.color = ScoreFace;
            Material own = text.fontMaterial;   // 取得就會複製一份，不會動到共用的
            if (own == null) return;
            text.outlineColor = ScoreShadow;
            // 描邊寬度受字型圖集的 padding 限制，超過就會被裁掉、反而看不見。
            // 這支字型原本用 0.13，往上加一點點就夠 —— 太厚會把淡綠的字心擠窄，
            // 讀起來變成一團金色而不是一個綠色的數字。
            text.outlineWidth = 0.14f;

        }

        /// <summary>
        /// Drives the combo's two gestures: a kick on every increment, and a
        /// quick fade when it breaks.
        /// </summary>
        /// <remarks>
        /// **Why the state is written every frame instead of on change.** The
        /// layout path resets `localScale` and rewrites the colour whenever the
        /// combo's position or font size changes, and it runs from Update. Rather
        /// than chase which call stamps on the animation and when, this simply
        /// reasserts scale and alpha after all of them, every frame. It costs two
        /// comparisons and cannot be got wrong later by someone adding a third
        /// caller.
        ///
        /// **Why coming back is instant but leaving is not.** A break wants to be
        /// *seen* leaving -- that is the feedback. The first note of the next
        /// combo wants its kick immediately; fading back in would put the
        /// response half a beat behind the key that earned it.
        /// </remarks>
        private void UpdateComboFlourish()
        {
            float dt = Time.unscaledDeltaTime;
            if (_comboPunch > 0f) _comboPunch = Mathf.Max(0f, _comboPunch - dt);

            bool live = _lastHudCombo > 0;
            _comboAlpha = live ? 1f : Mathf.Max(0f, _comboAlpha - dt / ComboFadeTime);

            float t = _comboPunch / ComboPunchTime;
            float rise = ComboHopHeight * t * t;

            // 只在跳的時候動頂點，跳完再多做一次把它放回去，之後就不碰了 ——
            // 每一幀重建一次網格只為了加 0 是白花的。
            bool hopping = rise > 0.05f;
            if (hopping || _comboHopped)
            {
                ApplyComboHop(screenComboTMP, rise);
                ApplyComboHop(trackComboWorldText, rise);
                _comboHopped = hopping;
            }

            ApplyComboAlpha(screenComboTMP, _comboAlpha);
            ApplyComboAlpha(trackComboWorldText, _comboAlpha);
        }

        /// <summary>
        /// Lifts the combo's digits, leaving the word COMBO where it is.
        /// </summary>
        /// <remarks>
        /// **Why the vertices and not the transform.** The label and the count are
        /// two lines of one text object, so scaling or moving the RectTransform
        /// takes the word along with the number. TMP hands out the glyph
        /// positions per character, so the last line -- the digits -- can be
        /// lifted on its own.
        ///
        /// **Why the last line rather than a character range.** The count's
        /// length changes as it climbs and the label's does not, so anything
        /// counted from the start of the string would need re-deriving on every
        /// increment. The digits are always the final line.
        ///
        /// **Why ForceMeshUpdate every time.** It rebuilds from the source text,
        /// which throws away the previous frame's offset -- so each frame applies
        /// its own displacement to a clean mesh instead of accumulating one. That
        /// also makes this correct when the text has just changed underneath it,
        /// which for a combo counter is most of the frames it runs on.
        /// </remarks>
        private static void ApplyComboHop(TextMeshProUGUI text, float rise)
        {
            if (text == null) return;
            text.ForceMeshUpdate();

            TMP_TextInfo info = text.textInfo;
            if (info == null || info.characterCount == 0 || info.lineCount == 0) return;

            TMP_LineInfo line = info.lineInfo[info.lineCount - 1];
            for (int i = line.firstCharacterIndex; i <= line.lastCharacterIndex; i++)
            {
                if (i < 0 || i >= info.characterInfo.Length) break;
                TMP_CharacterInfo glyph = info.characterInfo[i];
                if (!glyph.isVisible) continue;

                Vector3[] vertices = info.meshInfo[glyph.materialReferenceIndex].vertices;
                int corner = glyph.vertexIndex;
                if (corner + 3 >= vertices.Length) continue;
                for (int k = 0; k < 4; k++) vertices[corner + k].y += rise;
            }

            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }

        private static void ApplyComboAlpha(TextMeshProUGUI text, float alpha)
        {
            if (text == null) return;

            // 用 CanvasRenderer 的 alpha，不是 TMP 的 alpha 屬性。後者動的是基礎
            // 顏色，而 "COMBO" 那幾個字是 rich-text 的 <color> 標籤畫的，會蓋掉
            // 自己那一段的 alpha —— 淡出時數字消失、標籤留在原地。
            CanvasRenderer canvas = text.canvasRenderer;
            if (canvas != null && !Mathf.Approximately(canvas.GetAlpha(), alpha))
                canvas.SetAlpha(alpha);
        }

        /// <summary>
        /// Puts a black drop shadow behind a HUD text -- label and number alike.
        /// </summary>
        /// <remarks>
        /// **Why the material's underlay and not a Shadow component.** A `Shadow`
        /// is a mesh effect, and mesh effects only run when the canvas rebuilds
        /// the graphic. <see cref="ApplyComboHop"/> pushes its lifted vertices
        /// straight to the mesh with `UpdateVertexData`, which does not go through
        /// them -- so every frame the digits hopped, their shadow would vanish.
        /// The underlay is part of the shader, so it survives.
        ///
        /// **Why fontMaterial.** Every text styled here shares one material, and
        /// the outline settings above were already being written to it -- so the
        /// combo, the score and anything else on that font were dressing each
        /// other. Reading `fontMaterial` gives this text its own copy.
        ///
        /// The property names are spelled out rather than taken from
        /// `ShaderUtilities`, so this keeps compiling across TMP versions that
        /// move those constants.
        /// </remarks>
        private static void ApplyHudShadow(TextMeshProUGUI text)
        {
            if (text == null) return;
            Material own = text.fontMaterial;
            if (own == null) return;

            own.EnableKeyword("UNDERLAY_ON");
            own.SetColor("_UnderlayColor", ClassicalBookUITheme.ScoreShadow);
            own.SetFloat("_UnderlayOffsetX", 0.6f);
            own.SetFloat("_UnderlayOffsetY", -0.6f);
            own.SetFloat("_UnderlayDilate", 0.15f);
            own.SetFloat("_UnderlaySoftness", 0.25f);

            // 投影會超出字形原本的範圍，不放寬邊距就會被裁掉。
            text.UpdateMeshPadding();
        }

        private void RefreshHudStats()
        {
            try
            {
                var mgr = StatsManager.Instance;
                if (mgr == null) return;
                var t = mgr.GetStatsWithCombo();
                int score = t.Item8;
                var timing = mgr.GetTimingStats();
                int fast = timing.fast;
                int slow = timing.late;
                bool settingsPreview = SongSelectionManager.Instance != null &&
                    SongSelectionManager.Instance.IsSettingsGameplayPreviewRunning;
                int combo = settingsPreview && settingsPreviewComboActive
                    ? settingsPreviewCombo
                    : t.Item6;

                // Only rebuild TextMeshPro mesh when values actually changed.
                if (combo != _lastHudCombo || score != _lastHudScore ||
                    fast != _lastHudFast || slow != _lastHudSlow ||
                    settingsPreview != _lastHudWasSettingsPreview)
                {
                    // 連打的時候每一下都要重新彈，不是「還在彈就跳過」—— 那個
                    // 節奏本身就是回饋。第一次畫（-1）不算增加。
                    if (_lastHudCombo >= 0 && combo > _lastHudCombo && combo > 0)
                    {
                        _comboPunch = ComboPunchTime;
                        _comboAlpha = 1f;
                    }
                    _lastHudCombo = combo;
                    _lastHudScore = score;
                    _lastHudFast = fast;
                    _lastHudSlow = slow;
                    _lastHudWasSettingsPreview = settingsPreview;
                    string scoreStr = settingsPreview
                        ? $"<size=22><color=#72D6FF>FAST</color> <color=#8E1C2B>/ SLOW</color></size>\n{fast:N0} / {slow:N0}"
                        : $"<size=22><color=#A3761F>SCORE</color></size>\n{score:0000000}";
                    WriteComboText(combo);
                    if (scoreText != null) scoreText.text = scoreStr;
                    if (scoreTMP  != null) scoreTMP.text  = scoreStr;
                }
                ApplyComboDisplayLayout(false);
            }
            catch { }
        }

        private void WriteComboText(int combo)
        {
            combo = Mathf.Max(0, combo);
            // 小標的金色和結算畫面的 Gold 同一階（0.64, 0.46, 0.17）。
            string screenText = $"<size=46%><color=#A3761F>COMBO</color></size>\n{combo:N0}";
            if (comboText != null) comboText.text = screenText;
            if (comboTMP != null) comboTMP.text = screenText;
            if (screenComboTMP != null) screenComboTMP.text = screenText;
            if (trackComboTMP != null)
                trackComboTMP.text = $"<size=62%><color=#A3761F>COMBO</color></size>\n{combo:N0}";
            if (trackComboWorldText != null)
                trackComboWorldText.text = screenText;
        }

        public void BeginSettingsPreviewCombo(float segmentStartMs)
        {
            settingsPreviewComboTimings.Clear();
            settingsPreviewCombo = 0;
            settingsPreviewComboCursor = 0;
            settingsPreviewComboActive = true;

            Chart chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
            if (chart != null && chart.notes != null)
            {
                for (int i = 0; i < chart.notes.Count; i++)
                {
                    NoteData note = chart.notes[i];
                    if (note != null && note.startTime >= segmentStartMs - 0.5f)
                        settingsPreviewComboTimings.Add(note.startTime);
                }
                settingsPreviewComboTimings.Sort();
            }

            _lastHudCombo = -1;
            _hudDirty = true;
            WriteComboText(0);
        }

        public void ResetSettingsPreviewComboDisplay()
        {
            settingsPreviewComboTimings.Clear();
            settingsPreviewCombo = 0;
            settingsPreviewComboCursor = 0;
            settingsPreviewComboActive = false;
            _lastHudCombo = -1;
            _hudDirty = true;
            WriteComboText(0);
        }

        private void UpdateSettingsPreviewCombo(float songPositionMs)
        {
            bool previewRunning = SongSelectionManager.Instance != null &&
                SongSelectionManager.Instance.IsSettingsGameplayPreviewRunning;
            if (!previewRunning)
            {
                settingsPreviewComboActive = false;
                return;
            }
            if (!settingsPreviewComboActive) return;

            int previous = settingsPreviewCombo;
            while (settingsPreviewComboCursor < settingsPreviewComboTimings.Count &&
                   settingsPreviewComboTimings[settingsPreviewComboCursor] <= songPositionMs)
            {
                settingsPreviewCombo++;
                settingsPreviewComboCursor++;
            }
            if (settingsPreviewCombo != previous) _hudDirty = true;
        }

        private void ConfigureClassicalGameplayHud()
        {
            if (scoreTMP == null) return;

            classicalHudCanvas = scoreTMP.GetComponentInParent<Canvas>();
            if (classicalHudCanvas == null) return;
            classicalHudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            classicalHudCanvas.overrideSorting = true;
            classicalHudCanvas.sortingOrder = 24000;

            CanvasScaler scaler = classicalHudCanvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = classicalHudCanvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (comboTMP != null) comboTMP.gameObject.SetActive(false);
            if (achievementTMP != null)
            {
                achievementTMP.gameObject.SetActive(false);
            }

            StyleClassicalHudText(scoreTMP, new Vector2(1f, 1f), new Vector2(-54f, -42f),
                new Vector2(390f, 108f), new Vector2(1f, 1f), TextAlignmentOptions.TopRight, 48f);
            EnsureLegacyScoreText();

            TextMeshProUGUI timeText = null;
            TextMeshProUGUI[] hudTexts = classicalHudCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < hudTexts.Length; i++)
            {
                if (hudTexts[i] != null && hudTexts[i].name == "DspTimeDisplay")
                {
                    timeText = hudTexts[i];
                    break;
                }
            }
            if (timeText == null)
            {
                GameObject timeObject = GameObject.Find("DspTimeDisplay");
                if (timeObject != null) timeText = timeObject.GetComponent<TextMeshProUGUI>();
            }
            if (timeText == null) timeText = achievementTMP;
            if (timeText != null)
            {
                screenComboTMP = timeText;
                timeText.transform.SetParent(classicalHudCanvas.transform, false);
            }

            WriteComboText(0);
            ApplyComboDisplayLayout(true);
            classicalHudCanvas.gameObject.SetActive(classicalHudVisibleRequested);
            if (trackComboTMP != null)
                trackComboTMP.gameObject.SetActive(classicalHudVisibleRequested &&
                    lastComboDisplayPosition == ComboDisplayPosition.Track);
            if (trackComboWorldCanvas != null)
                trackComboWorldCanvas.gameObject.SetActive(classicalHudVisibleRequested &&
                    lastComboDisplayPosition == ComboDisplayPosition.Track);
        }

        public void RefreshComboDisplayLayout()
        {
            lastComboDisplayPosition = (ComboDisplayPosition)(-1);
            ApplyComboDisplayLayout(true);
            _lastHudCombo = -1;
            _hudDirty = true;
        }

        private void ApplyComboDisplayLayout(bool force)
        {
            ComboDisplayPosition position = SettingsManager.Instance != null
                ? SettingsManager.Instance.CurrentComboDisplayPosition
                : ComboDisplayPosition.TopCenter;
            float fontSize = SettingsManager.Instance != null
                ? SettingsManager.Instance.ComboFontSize
                : 48f;
            float trackScreenHeight = SettingsManager.Instance != null
                ? SettingsManager.Instance.TrackComboScreenHeight
                : 0.35f;
            bool layoutUnchanged = position == lastComboDisplayPosition &&
                Mathf.Approximately(fontSize, lastComboFontSize) &&
                Mathf.Approximately(trackScreenHeight, lastTrackComboScreenHeight);
            if (!force && layoutUnchanged)
            {
                if (position == ComboDisplayPosition.Track)
                    UpdateTrackComboTransform();
                return;
            }

            lastComboDisplayPosition = position;
            lastComboFontSize = fontSize;
            lastTrackComboScreenHeight = trackScreenHeight;
            bool onTrack = position == ComboDisplayPosition.Track;
            if (screenComboTMP != null) screenComboTMP.gameObject.SetActive(!onTrack);

            if (onTrack)
            {
                EnsureTrackComboWorldCanvas();
                if (trackComboTMP != null) trackComboTMP.gameObject.SetActive(false);
                if (trackComboWorldText != null)
                {
                    trackComboWorldText.fontSize = fontSize;
                    UpdateTrackComboTransform();
                    trackComboWorldCanvas.gameObject.SetActive(classicalHudVisibleRequested);
                }
                return;
            }

            if (trackComboTMP != null) trackComboTMP.gameObject.SetActive(false);
            if (trackComboWorldCanvas != null) trackComboWorldCanvas.gameObject.SetActive(false);
            if (screenComboTMP == null) return;

            switch (position)
            {
                case ComboDisplayPosition.TopLeft:
                    StyleClassicalHudText(screenComboTMP, new Vector2(0f, 1f),
                        new Vector2(54f, -294f), new Vector2(520f, 210f),
                        new Vector2(0f, 1f), TextAlignmentOptions.TopLeft, fontSize);
                    break;
                case ComboDisplayPosition.Center:
                    // Center and Track share the same 0..100% height setting,
                    // but Center maps it to screen space: 0% is mid-screen and
                    // 100% stops 5% below the camera's top edge.
                    float centerViewportY = Mathf.Lerp(0.50f, 0.95f,
                        Mathf.Clamp01(trackScreenHeight));
                    StyleClassicalHudText(screenComboTMP, new Vector2(0.5f, centerViewportY),
                        Vector2.zero, new Vector2(620f, 220f),
                        new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, fontSize);
                    break;
                default:
                    StyleClassicalHudText(screenComboTMP, new Vector2(0.5f, 1f),
                        new Vector2(0f, -48f), new Vector2(520f, 210f),
                        new Vector2(0.5f, 1f), TextAlignmentOptions.Top, fontSize);
                    break;
            }
        }

        private void EnsureTrackComboText()
        {
            if (trackComboTMP != null) return;
            GameObject track = null;
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null && gameManager.gameplayRoot != null)
            {
                Transform found = gameManager.gameplayRoot.transform.Find("Track");
                if (found != null) track = found.gameObject;
            }
            if (track == null) track = GameObject.Find("Track");
            if (track == null) return;
            trackComboSurface = track.transform;

            GameObject display = new GameObject("TrackComboDisplay", typeof(RectTransform),
                typeof(MeshRenderer), typeof(TextMeshPro));
            display.layer = track.layer;
            Transform parent = gameManager != null && gameManager.gameplayRoot != null
                ? gameManager.gameplayRoot.transform
                : track.transform.parent;
            if (parent != null) display.transform.SetParent(parent, true);

            display.transform.localScale = Vector3.one * 0.35f;

            trackComboTMP = display.GetComponent<TextMeshPro>();
            trackComboTMP.rectTransform.sizeDelta = new Vector2(16f, 4f);
            trackComboTMP.alignment = TextAlignmentOptions.Center;
            trackComboTMP.textWrappingMode = TextWrappingModes.NoWrap;
            trackComboTMP.overflowMode = TextOverflowModes.Overflow;
            trackComboTMP.richText = true;
            trackComboTMP.raycastTarget = false;
            trackComboTMP.fontSize = (SettingsManager.Instance != null
                ? SettingsManager.Instance.ComboFontSize
                : 48f) / 6f;
            trackComboTMP.fontStyle = FontStyles.Bold;
            trackComboTMP.color = new Color(0.96f, 0.91f, 0.79f, 1f);
            trackComboTMP.outlineWidth = 0.16f;
            trackComboTMP.outlineColor = new Color(0.04f, 0.02f, 0.01f, 0.96f);
            Material fontMaterial = trackComboTMP.fontMaterial;
            if (fontMaterial != null)
            {
                if (fontMaterial.HasProperty("_CullMode")) fontMaterial.SetFloat("_CullMode", 0f);
                if (fontMaterial.HasProperty("_Cull")) fontMaterial.SetFloat("_Cull", 0f);
            }
            int combo = Mathf.Max(0, _lastHudCombo);
            trackComboTMP.text = $"<size=62%><color=#D9BC76>COMBO</color></size>\n{combo:N0}";
            MeshRenderer renderer = display.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sortingOrder = 60;
            UpdateTrackComboTransform();
        }

        private void EnsureTrackComboWorldCanvas()
        {
            if (trackComboWorldCanvas != null && trackComboWorldText != null) return;
            GameManager manager = GameManager.Instance;
            Transform track = manager != null && manager.gameplayRoot != null
                ? manager.gameplayRoot.transform.Find("Track")
                : null;
            if (track == null)
            {
                GameObject found = GameObject.Find("Track");
                if (found != null) track = found.transform;
            }
            if (track == null) return;
            trackComboSurface = track;

            GameObject canvasObject = new GameObject("TrackComboWorldCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = track.gameObject.layer;
            Transform parent = manager != null && manager.gameplayRoot != null
                ? manager.gameplayRoot.transform
                : track.parent;
            if (parent != null) canvasObject.transform.SetParent(parent, true);

            trackComboWorldCanvas = canvasObject.GetComponent<Canvas>();
            trackComboWorldCanvas.renderMode = RenderMode.WorldSpace;
            trackComboWorldCanvas.worldCamera = Camera.main;
            trackComboWorldCanvas.overrideSorting = true;
            trackComboWorldCanvas.sortingOrder = -100;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(900f, 260f);
            // The initial world-canvas scale was technically visible but far
            // too small at the gameplay camera's distance. Use a 20x baseline;
            // the player's Combo Font Size setting still scales the glyphs.
            canvasRect.localScale = Vector3.one * 0.24f;

            GameObject textObject = new GameObject("Combo", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.layer = canvasObject.layer;
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(canvasRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            trackComboWorldText = textObject.GetComponent<TextMeshProUGUI>();
            StyleClassicalHudText(trackComboWorldText, new Vector2(0.5f, 0.5f),
                Vector2.zero, canvasRect.sizeDelta, new Vector2(0.5f, 0.5f),
                TextAlignmentOptions.Center, SettingsManager.Instance != null
                    ? SettingsManager.Instance.ComboFontSize
                    : 48f);
            trackComboWorldText.maskable = false;
            trackComboWorldText.overflowMode = TextOverflowModes.Overflow;
            Material worldFontMaterial = trackComboWorldText.fontMaterial;
            if (worldFontMaterial != null)
            {
                // Keep the Combo between the Track and Note layers. Notes must
                // remain readable when they pass over the world-space text.
                if (worldFontMaterial.HasProperty("_ZTestMode"))
                    worldFontMaterial.SetFloat("_ZTestMode", 4f);
                if (worldFontMaterial.HasProperty("_ZTest"))
                    worldFontMaterial.SetFloat("_ZTest", 4f);
                if (worldFontMaterial.HasProperty("_CullMode"))
                    worldFontMaterial.SetFloat("_CullMode", 0f);
                worldFontMaterial.renderQueue = 3000;
            }
            WriteComboText(Mathf.Max(0, _lastHudCombo));
            UpdateTrackComboTransform();
        }

        private void UpdateTrackOverlayLayout(float fontSize, float trackPosition)
        {
            if (screenComboTMP == null) return;
            if (trackComboSurface == null)
            {
                GameManager manager = GameManager.Instance;
                if (manager != null && manager.gameplayRoot != null)
                {
                    Transform found = manager.gameplayRoot.transform.Find("Track");
                    if (found != null) trackComboSurface = found;
                }
                if (trackComboSurface == null)
                {
                    GameObject found = GameObject.Find("Track");
                    if (found != null) trackComboSurface = found.transform;
                }
            }

            Camera camera = Camera.main;
            Vector2 viewport = new Vector2(0.5f, Mathf.Lerp(0.22f, 0.60f,
                Mathf.Clamp01(trackPosition)));
            if (camera != null && trackComboSurface != null)
            {
                Plane plane = new Plane(trackComboSurface.up, trackComboSurface.position);
                Ray nearRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.22f, 0f));
                Ray farRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.60f, 0f));
                if (plane.Raycast(nearRay, out float nearDistance) &&
                    plane.Raycast(farRay, out float farDistance))
                {
                    Vector3 nearPoint = nearRay.GetPoint(nearDistance);
                    Vector3 farPoint = farRay.GetPoint(farDistance);
                    Vector3 trackPoint = Vector3.Lerp(nearPoint, farPoint,
                        Mathf.Clamp01(trackPosition));
                    Renderer renderer = trackComboSurface.GetComponent<Renderer>();
                    if (renderer != null) trackPoint = renderer.bounds.ClosestPoint(trackPoint);
                    Vector3 projected = camera.WorldToViewportPoint(
                        trackPoint + trackComboSurface.up * 0.03f);
                    if (projected.z > camera.nearClipPlane)
                    {
                        viewport.x = Mathf.Clamp(projected.x, 0.12f, 0.88f);
                        viewport.y = Mathf.Clamp(projected.y, 0.15f, 0.75f);
                    }
                }
            }

            RectTransform rect = screenComboTMP.rectTransform;
            rect.anchorMin = viewport;
            rect.anchorMax = viewport;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(520f, 210f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = new Vector3(1f, 0.58f, 1f);
            screenComboTMP.fontSize = fontSize;
            screenComboTMP.alignment = TextAlignmentOptions.Center;
        }

        private void UpdateTrackComboTransform()
        {
            Transform displayTransform = trackComboWorldCanvas != null
                ? trackComboWorldCanvas.transform
                : (trackComboTMP != null ? trackComboTMP.transform : null);
            if (displayTransform == null || trackComboSurface == null) return;
            Camera camera = Camera.main;
            float trackPosition = SettingsManager.Instance != null
                ? SettingsManager.Instance.TrackComboScreenHeight
                : 0.35f;
            Vector3 position = trackComboSurface.TransformPoint(new Vector3(0f, 0.03f, -0.37f));
            if (camera != null)
            {
                Plane trackPlane = new Plane(trackComboSurface.up, trackComboSurface.position);
                Ray nearRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.22f, 0f));
                Ray oldFarRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.60f, 0f));
                if (trackPlane.Raycast(nearRay, out float nearDistance) &&
                    trackPlane.Raycast(oldFarRay, out float oldFarDistance) &&
                    nearDistance > 0f && oldFarDistance > 0f)
                {
                    Vector3 nearPoint = nearRay.GetPoint(nearDistance);
                    Vector3 oldFarPoint = oldFarRay.GetPoint(oldFarDistance);
                    // New 100% equals 250% of the previous adjustable world
                    // distance. LerpUnclamped is intentional for the extension.
                    position = Vector3.LerpUnclamped(nearPoint, oldFarPoint,
                        Mathf.Clamp01(trackPosition) * 2.5f);
                    Renderer trackRenderer = trackComboSurface.GetComponent<Renderer>();
                    if (trackRenderer != null) position = trackRenderer.bounds.ClosestPoint(position);
                    position += trackComboSurface.up * 0.04f;
                }
            }

            displayTransform.position = position;
            displayTransform.rotation =
                trackComboSurface.rotation * Quaternion.Euler(90f, 0f, 0f);
            if (camera != null)
            {
                Vector3 textFront = -displayTransform.forward;
                Vector3 towardCamera = (camera.transform.position - position).normalized;
                if (Vector3.Dot(textFront, towardCamera) < 0f)
                    displayTransform.Rotate(0f, 180f, 0f, Space.Self);
            }
        }

        private static void StyleClassicalHudText(TextMeshProUGUI text, Vector2 anchor,
            Vector2 position, Vector2 size, Vector2 pivot, TextAlignmentOptions alignment, float fontSize)
        {
            if (text == null) return;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            ClassicalBookUITheme.StyleText(text, ClassicalBookUITheme.ScoreFace, fontSize, FontStyles.Bold);
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Masking;
            text.maskable = true;
            text.raycastTarget = false;
            text.richText = true;
            text.characterSpacing = 4f;
            text.outlineWidth = 0.10f;
            text.outlineColor = ClassicalBookUITheme.ScoreShadow;
            ApplyHudShadow(text);
            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.transform.SetAsLastSibling();
        }

        public void SetClassicalHudVisible(bool visible)
        {
            classicalHudVisibleRequested = visible;
            if (classicalHudCanvas != null) classicalHudCanvas.gameObject.SetActive(visible);
            if (trackComboTMP != null)
                trackComboTMP.gameObject.SetActive(visible &&
                    lastComboDisplayPosition == ComboDisplayPosition.Track);
            if (trackComboWorldCanvas != null)
                trackComboWorldCanvas.gameObject.SetActive(visible &&
                    lastComboDisplayPosition == ComboDisplayPosition.Track);
        }

        private bool IsBetterPayload(JudgmentPayload incoming, JudgmentPayload current)
        {
            if (incoming == null || current == null) return true;
            if (incoming.absoluteOffsetMs + 0.01f < current.absoluteOffsetMs) return true;
            if (System.Math.Abs(incoming.absoluteOffsetMs - current.absoluteOffsetMs) <= 0.01f)
            {
                return GetResultRank(incoming.result) < GetResultRank(current.result);
            }
            return false;
        }

        private void OnJudgmentRevised(int buttonId, UnityEngine.Vector3 notePosition, JudgmentResult res)
        {
            ShowJudgementPopups(buttonId, notePosition, res);
            _hudDirty = true;
        }

        private void ShowJudgementPopups(int buttonId, UnityEngine.Vector3 notePosition, JudgmentResult res,
            bool precise = false)
        {
            try
            {
                var popup = JudgePopupManager.Instance;
                if (popup != null && popup.enablePopup)
                {
                    var keyRect = VirtualKeyButton.GetRectForId(buttonId);
                    if (keyRect != null)
                        popup.ShowPopupAtRectTransform(keyRect, res, precise);
                    else
                        popup.ShowPopupAtWorldPosition(notePosition, res, 0f, null, precise);
                }
            }
            catch { }
            try
            {
                using (HitchProbe.Measure("judgePopup"))
                    SimpleJudgePopupManager.Instance?.ShowAtPosition(notePosition, res, precise);
            }
            catch { }
        }

        private void ShowNoteMeshEffect(NoteController note, JudgmentResult res, bool persistent = false,
            bool playHitParticles = true, float signedTimingOffsetMs = 0f, bool useTimingColor = false,
            int inputLane = -1, bool precise = false)
        {
            if (note == null) return;
            if (res != JudgmentResult.Perfect && res != JudgmentResult.Great && res != JudgmentResult.Good) return;
            try
            {
                using (HitchProbe.Measure("judgeMesh"))
                    NoteJudgementMeshManager.EnsureCreated().ShowEffect(
                        note, res, persistent, signedTimingOffsetMs, useTimingColor, inputLane,
                        precise ? PreciseGlow : (Color?)null);
            }
            catch { }
            if (playHitParticles)
            {
                try
                {
                    // 魔法陣的外框跟著判定走：精準的那一下連封印的邊都是紫的。
                    ParticleEffectPlayer.NextSealIsPrecise = precise;
                    HitEffectRouter.Play(note, inputLane >= 0 ? inputLane : (note.NoteData?.startLane ?? -1), res);
                }
                catch { }
                finally { ParticleEffectPlayer.NextSealIsPrecise = false; }
            }
        }

        private void HideNoteMeshEffect(NoteController note)
        {
            if (note == null) return;
            try { NoteJudgementMeshManager.EnsureCreated().HidePersistentEffect(note); } catch { }
        }

        /// <summary>
        /// 自動演奏的是這一顆嗎：全域的自動演奏，或教學示範段的音符。
        /// </summary>
        private bool AutoFor(NoteController note)
        {
            return _cachedDebugMode || (note != null && note.IsTutorialDemo);
        }

        /// <summary>
        /// 示範段的音符照常判定（聲音、特效、音符消失都要），但統計要靜音。
        /// 回傳有沒有真的推進靜音，交給 <see cref="EndStatsMute"/>。
        /// </summary>
        private static bool BeginStatsMute(NoteController note)
        {
            if (note == null || !note.IsTutorialDemo) return false;
            var stats = StatsManager.Instance;
            if (stats == null) return false;
            stats.PushMute();
            return true;
        }

        private static void EndStatsMute(bool muted)
        {
            if (muted) StatsManager.Instance?.PopMute();
        }

        private void ApplyJudgmentPayload(JudgmentPayload payload)
        {
            bool muted = BeginStatsMute(payload != null ? payload.note : null);
            try { ApplyJudgmentPayloadCore(payload); }
            finally { EndStatsMute(muted); }
        }

        private void ApplyJudgmentPayloadCore(JudgmentPayload payload)
        {
            if (payload == null) return;
            var note = payload.note;
            if (note == null) return;
            if (note.IsJudgmentSuppressed)
            {
                CleanupHoldState(note);
                ReturnPayload(payload);
                return;
            }
            // 這顆音符成立了 —— 同一個操作幀內落在它 (判定區 ∪ Near 區) 的多餘按鍵
            // 就是擦碰，在這裡被吃掉，不會變成誤觸的聲音。
            if (payload.result != JudgmentResult.Miss) AbsorbStraysAround(note.NoteData);
            if (!payload.isHoldTail && !payload.isHoldExtra && !payload.isSustainTick)
                EatenInputProbe.Resolved(note, payload.result == JudgmentResult.Miss);
            if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] ApplyJudgmentPayload note={note.name ?? note.GetInstanceID().ToString()} res={payload.result} head={payload.isHoldHead} tail={payload.isHoldTail} popup={payload.triggerPopup} mesh={payload.triggerMesh} sound={payload.triggerHitSound}"); } catch { } }

            INoteHandler handler = null;
            try { handler = GetHandler(note); } catch { handler = null; }

            if (payload.isHoldTail)
            {
                float tailTimingOffset = payload.hasHeadTiming
                    ? payload.headInputSongPosMs - payload.headTargetTimeMs
                    : payload.inputSongPosMs - payload.targetTimeMs;
                if (payload.triggerMesh)
                    ShowNoteMeshEffect(note, payload.result, false, true, tailTimingOffset, true,
                        payload.buttonId, IsPrecise(payload));
                bool preciseTail = IsPrecise(payload);
                if (payload.triggerPopup) { ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result, preciseTail); }
                TryIncrementStats(payload.result, false,
                    note.IsStaccato ? RecitalWeight(payload, true) : 1m);
                RecordTimingOffsetIfNeeded(payload);
                try { note.OnHoldEnd(payload.result); } catch { }
                try { handler?.OnFinalizeHold(note, payload.releaseSongPosMs, payload.pressSongPosMs); } catch { }
                CleanupHoldState(note);
                SuppressLaterOverlappedNotes(note, payload.releaseSongPosMs > 0f ? payload.releaseSongPosMs : payload.inputSongPosMs);
                ReturnPayload(payload);
                return;
            }

            if (payload.isHoldHead)
            {
                float headTimingOffset = payload.inputSongPosMs - payload.targetTimeMs;
                bool preciseHead = IsPrecise(payload);
                if (payload.triggerMesh)
                {
                    if (payload.usePersistentMesh)
                    {
                        // First show the timing-position burst, then keep the
                        // Hold contact core anchored on the judgment line.
                        //
                        // 紫光只屬於**落鍵的那一下**。持續的接觸光柱回到金色：
                        // 精準講的是手放在哪裡，那是一個瞬間的事實，不是這條
                        // 長音接下來三秒鐘的狀態 —— 一路紫下去，紫色就不再是
                        // 「你剛剛按得很準」，而只是「這是一條長音」。
                        ShowNoteMeshEffect(note, payload.result, false, true, headTimingOffset, true,
                            payload.buttonId, preciseHead);
                        ShowNoteMeshEffect(note, payload.result, true, false, 0f, false,
                            payload.buttonId);
                    }
                    else
                    {
                        ShowNoteMeshEffect(note, payload.result, false, true, headTimingOffset, true,
                            payload.buttonId, preciseHead);
                    }
                }
                if (payload.triggerHitSound) { try { handler?.PlayHeadSound(note, payload.inputSongPosMs, payload.result); } catch { } }
                if (payload.triggerPopup) { ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result, preciseHead); }
                TryIncrementStats(payload.result, false, RecitalWeight(payload, true));
                RecordTimingOffsetIfNeeded(payload);
                if (activeHoldStates.TryGetValue(note, out var state))
                {
                    state.headJudgmentCommitted = true;
                    state.headJudgmentPending = false;
                    state.headInputSongPosMs = payload.inputSongPosMs;
                    state.headTargetTimeMs = payload.targetTimeMs;
                    state.headAbsoluteOffsetMs = payload.absoluteOffsetMs;
                    state.hasHeadTiming = payload.hasHeadTiming;
                }
                SuppressLaterOverlappedNotes(note, payload.inputSongPosMs);
                ReturnPayload(payload);
                return;
            }

            // Hold extras update score/popups, but the contact core is already
            // continuously lit; do not stack rhythmic brightness pulses over it.
            bool precise = IsPrecise(payload);
            if (payload.triggerMesh && !payload.isHoldExtra)
            {
                float timingOffset = payload.inputSongPosMs - payload.targetTimeMs;
                ShowNoteMeshEffect(note, payload.result, false, true, timingOffset, true,
                    payload.buttonId, precise);
            }
            if (payload.triggerHitSound) { try { handler?.PlayHeadSound(note, payload.inputSongPosMs, payload.result); } catch { } }
            if (payload.triggerPopup) { ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result, precise); }
            TryIncrementStats(payload.result, payload.isSustainTick, RecitalWeight(payload, true));
            RecordTimingOffsetIfNeeded(payload);
            if (_cachedDebugMode) { try { if (payload.isHoldExtra) { BuildLogger.Log($"[JMgr] ApplyJudgmentPayload: isHoldExtra for note={note?.name ?? "<null>"} res={payload.result} songPos={payload.inputSongPosMs}"); } } catch { } }
            // If this payload is an extra per-hold judgment, do not mark the note as judged/released.
            if (!payload.isHoldExtra)
            {
                try { note.OnJudged(payload.result); } catch { }
                SuppressLaterOverlappedNotes(note, payload.inputSongPosMs);
                // note is now IsJudged=true (IsActive=false). All iteration consumers already
                // check IsActive/IsJudged, so the note is naturally filtered out.
                // Do NOT call nearbyNotesScratch.Remove(note) here — we may be inside a
                // foreach over that list, and List.Remove during enumeration throws
                // InvalidOperationException, which is extremely expensive in Unity/Mono
                // (full stack-trace capture). With 16-note chords this caused ~32 exceptions/frame.
            }
            ReturnPayload(payload);
        }

        // Cached judgment offset, refreshed once per frame in Update() to avoid per-call singleton access.
        private float _cachedJudgmentOffsetMs;
        // Cached reference to GameManager's Conductor, refreshed per frame.
        private Conductor _cachedConductor;

        // --- Core judgement application ---
        private float GetSongPositionMs()
        {
            var c = _cachedConductor;
            float pos = (c != null) ? c.effectiveSongPosition : 0f;
            return pos + _cachedJudgmentOffsetMs;
        }

        public float CurrentSongPositionMs
        {
            get
            {
                try
                {
                    var gm = GameManager.Instance;
                    var conductor = gm != null ? gm.Conductor : null;
                    float pos = conductor != null ? conductor.effectiveSongPosition : 0f;
                    var sm = SettingsManager.Instance;
                    float offset = sm != null ? sm.JudgmentOffsetMs : 0f;
                    return pos + offset;
                }
                catch
                {
                    return GetSongPositionMs();
                }
            }
        }

        private float GetProtectionMs()
        {
            // Use 'goodMs' as base protection duration (ms) to avoid double-judging nearby press events
            return (float)goodMs + 10f;
        }

        private void ApplyJudgmentForHoldStart(NoteController note)
        {
            if (note == null) return;
            bool muted = BeginStatsMute(note);
            try
            {
                // play sound once for head
                try { HitSoundManager.Instance?.PlayHitSound(); } catch { }
                // effect and popup
                // Show a persistent note judgement mesh to simulate the head being held
                var head = note.NoteData;
                int centreLane = head != null
                    ? IvoryLaneKeyboard.ResolveDebugLane(head.startLane, head.endLane)
                    : -1;
                // 力度也要照譜面的來。長條頭走的是另一條路（不經過 payload），
                // 所以上面那個 tap 的補償碰不到它 —— 少了這一行，自動演奏的長音
                // 會用上一次留在那條鍵道上的力度發聲，而那個值和這一顆無關。
                if (head != null && head.velocity > 0 && centreLane >= 0)
                {
                    try
                    {
                        PianoKeysound.RecordInput(centreLane, -1,
                            Mathf.Clamp01(head.velocity / 127f), true);
                    }
                    catch { }
                }

                // PRECISE 現在只問強弱，而自動演奏彈的就是譜面自己的力度 —— 所以
                // 它一定是 PRECISE。這裡以前看的是中心格，那是 CENTRE 那一列的事。
                // 和一般音符同一條規則（DynamicMatches）：判不出強弱就放行。以前這裡要求
                // 「有力度資料」，於是沒量到強弱分界的譜面上，自動演奏的點擊是 PRECISE、
                // 長條頭卻不是。
                bool precise = _cachedRecitalMode && head != null && !note.IsTrill && !note.IsSlide;
                // 自動演奏的長條頭不經過 payload，所以也不會經過 RecitalWeight。
                // 沒有補這一筆的話，自動演奏測出來的統計會少掉所有的長音。
                if (_cachedRecitalMode && head != null && !note.IsTrill && !note.IsSlide)
                {
                    try
                    {
                        StatsManager.Instance?.RecordRecitalNote();
                        // 自動演奏彈的就是譜面自己的力度，所以強弱一定對 ——
                        // 而 PRECISE 問的正是強弱。這裡不看中心格：中心格是
                        // CENTRE 那一列在講的事，兩列各回答一個問題。
                        if (head.velocity > 0 && VelocityBands.Measured)
                        {
                            StatsManager.Instance?.RecordDynamic(true);
                            StatsManager.Instance?.RecordPreciseAttempt(true);
                        }
                        if (PedalNoteRenderer.TrySamplePedal(GetSongPositionMs(),
                                out bool wanted, out bool actual))
                            StatsManager.Instance?.RecordPedal(wanted == actual);
                    }
                    catch { }
                }
                try
                {
                    // 先閃一下（精準的話是紫的），再放上一直亮著的金色光柱。
                    ShowNoteMeshEffect(note, JudgmentResult.Perfect, false, true, 0f, false,
                        centreLane, precise);
                    ShowNoteMeshEffect(note, JudgmentResult.Perfect, true, false, 0f, false,
                        centreLane);
                }
                catch { }
                try
                {
                    ParticleEffectPlayer.NextSealIsPrecise = precise;
                    HitEffectRouter.BeginHold(note, centreLane >= 0 ? centreLane : (note.NoteData?.startLane ?? -1));
                }
                catch { }
                finally { ParticleEffectPlayer.NextSealIsPrecise = false; }
                try { SimpleJudgePopupManager.Instance?.ShowAtPosition(note.transform.position,
                    JudgmentResult.Perfect, precise); } catch { }
                // Also show persistent key-level mesh(s) for the lanes belonging to this note so
                // the virtual key visuals remain lit while the hold is active.
                try
                {
                    var nd = note.NoteData;
                    if (nd != null)
                    {
                        int startLane = nd.startLane;
                        int endLane = nd.endLane >= startLane ? nd.endLane : startLane;
                        for (int lane = startLane; lane <= endLane; lane++)
                        {
                            // intentionally no per-key visual; judge mesh is shown instead
                        }
                    }
                }
                catch { }
                // notify note to mark head pressed
                try { note.OnHoldStart(JudgmentResult.Perfect); } catch { }
                Protection.SetProtection(note, GetSongPositionMs() + GetProtectionMs());
                // stats: count as perfect hit for head
                try { StatsManager.Instance?.Increment(JudgmentResult.Perfect); } catch { }
                try { RefreshHudStats(); } catch { }
            }
            catch { }
            finally { EndStatsMute(muted); }
        }

        public void ApplyJudgment(NoteController note, JudgmentResult result, float accuracyMs, float protectionMs)
        {
            if (note == null) return;
            try
            {
                // record timing offset if available
                try { StatsManager.Instance?.RecordTimingOffset(accuracyMs, result); } catch { }
                // increment counts / combo / score
                try { StatsManager.Instance?.Increment(result); } catch { }
                try { RefreshHudStats(); } catch { }

                // play central hit sound for instantaneous judgments (suppress for end/tail judgments if needed)
                try { HitSoundManager.Instance?.PlayHitSound(); } catch { }

                // visual effects & popup
                try { EffectManager.PlayEffect(note, result); } catch { }
                try { SimpleJudgePopupManager.Instance?.ShowAtPosition(note.transform.position, result); } catch { }

                // notify the note controller so it can hide and release
                try { note.OnJudged(result); } catch { }

                // set simple protection to avoid immediate re-judgement
                try { Protection.SetProtection(note, GetSongPositionMs() + protectionMs); } catch { }
            }
            catch { }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            try { BuildLogger.Log($"[JMgr] Awake - Instance created on GameObject={gameObject.name}"); } catch { }
            DontDestroyOnLoad(gameObject);
            // Pre-warm the payload pool so the first wave of judgments (e.g. simultaneous
            // chord notes) does not trigger heap allocations.
            for (int i = 0; i < 32; i++) _payloadPool.Push(new JudgmentPayload());

            // A corrected judgment has to be visible, or the popup keeps showing
            // the result the score no longer holds.
            EarlyClaimCorrector.JudgmentRevised -= OnJudgmentRevised;
            EarlyClaimCorrector.JudgmentRevised += OnJudgmentRevised;

            // Sync runtime-configurable timing & protection values from SettingsManager (best-effort)
            try
            {
                var sm = SettingsManager.Instance;
                if (sm != null)
                {
                    perfectMs = sm.PerfectMs;
                    greatMs = sm.GreatMs;
                    goodMs = sm.GoodMs;

                    stacPerfectMs = sm.StacPerfectMs;
                    stacGreatMs = sm.StacGreatMs;
                    stacGoodMs = sm.StacGoodMs;

                    stacAutoReleaseMs = sm.StacAutoReleaseMs;
                    autoSimulateStaccatoRelease = sm.AutoSimulateStaccatoRelease;
                    swapGraceMs = sm.SwapGraceMs;

                    try { sharedInputThresholdMs = sm.SharedInputThresholdMs; } catch { }

                    try { Protection.SetProtectionEnabled(sm.ProtectionEnabled); } catch { }
                    // 鄰鍵保護是本家固定的鄰鍵鎖，不讀設定（見 InputProtection）。
                }
            }
            catch { }
            ConfigureClassicalGameplayHud();
        }

        // --- JudgmentPayload pool helpers ---
        // Rent an already-allocated payload from the pool (or allocate a new one on the
        // very rare occasions the pool is empty). All fields are reset to their defaults.
        private JudgmentPayload RentPayload()
        {
            return _payloadPool.Count > 0 ? _payloadPool.Pop() : new JudgmentPayload();
        }

        // Return a payload back to the pool after it has been fully consumed.
        private void ReturnPayload(JudgmentPayload p)
        {
            if (p != null) { p.Reset(); _payloadPool.Push(p); }
        }
        // ------------------------------------

        // Return the cached INoteHandler for a note, creating and caching it on first call.
        // Cost per judgment: one Dictionary lookup (O(1)) instead of new allocation every time.
        private INoteHandler GetHandler(NoteController note)
        {
            if (note == null) return null;
            if (_handlerCache.TryGetValue(note, out var cached)) return cached;
            var h = NoteHandlerFactory.Create(note);
            _handlerCache[note] = h;
            return h;
        }

        void Update()
        {
            long costStart = JudgeCostProbe.Begin();
            try { using (HitchProbe.Measure("judgeUpdate")) UpdateCore(); }
            finally { JudgeCostProbe.EndUpdate(costStart); }
        }

        private void UpdateCore()
        {
            // Refresh per-frame caches
            var gm = GameManager.Instance;
            _cachedConductor = (gm != null) ? gm.Conductor : null;
            var sm = SettingsManager.Instance;
            _cachedJudgmentOffsetMs = (sm != null) ? sm.JudgmentOffsetMs : 0f;
            _cachedDebugMode = sm != null && sm.DebugModeInPlay;
            _cachedRecitalMode = sm != null && sm.RecitalModeInPlay;
            if (_cachedDebugMode) autoPlayUsedThisRun = true;
            ApplyComboDisplayLayout(false);

            // 輸入幀要在其他判定處理之前結算：這一格收到的按鍵先變成判定，
            // 後面的 miss/hold 檢查才看得到最新的狀態。
            FlushInputFrameIfDue();

            float songPos = GetSongPositionMs();
            UpdateSettingsPreviewCombo(songPos);
            // 暫定判定要在誤觸結算之前：作廢或套用的結果會決定周圍的按鍵被不被吃掉。
            ProcessProvisionalPresses(songPos);
            ProcessMelodyChecks(songPos);
            FlushPendingStrays();
            ProcessGestureNotes(songPos);
            ProcessOverlappedJudgmentSuppressions(songPos);
            ProcessHeldHoldContacts(songPos);
            ProcessHoldExtraAwards(songPos);
            FinalizeExpiredHolds(songPos);
            // Flush HUD update once per frame (dirty flag set by TryIncrementStats).
            if (_hudDirty) { _hudDirty = false; RefreshHudStats(); }
            // 版面之後才動，不然這一幀的縮放會被 StyleClassicalHudText 拍回 1。
            UpdateComboFlourish();
        }

        /// <summary>
        /// 把等過一個操作幀還沒被吃掉的按鍵認定為誤觸，這時候才出聲。
        /// </summary>
        private void FlushPendingStrays()
        {
            if (_pendingStrays.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _pendingStrays.Count - 1; i >= 0; i--)
            {
                if ((now - _pendingStrays[i].realtime) * 1000f < StrayHoldMs) continue;
                int lane = _pendingStrays[i].lane;
                _pendingStrays.RemoveAt(i);
                SettleStray(lane);
            }
        }

        /// <summary>
        /// 認定這一下是真的誤觸：記進診斷，**不出聲**。
        /// </summary>
        /// <remarks>
        /// 本家的空按和 Miss 都沒有聲音（見 PAN-001 的音效邏輯.md）。以前除了 Hardcore
        /// 以外會放一個短錯音，理由是「按了沒聲音像鍵盤壞了」；照使用者的選擇改成和本家
        /// 一樣，所有模式都安靜。
        /// </remarks>
        private void SettleStray(int lane)
        {
            // 記下它長什麼樣再放行。這是唯一真的「破壞手感」的一類——
            // 上游擋掉的和被吸收的都沒有造成傷害。
            try
            {
                PianoKeysound.DescribeLaneInput(lane, out int strayPitch, out float strayVelocity);
                MistouchProbe.StraySounded(strayPitch, strayVelocity, Time.unscaledTimeAsDouble);
            }
            catch { }
            // 演奏會的誤觸計數和紅燈不在這裡：本家看的是按下那一刻的鍵位，
            // 不是有沒有出錯音。見 CheckRecitalMistouch。
        }

        /// <summary>
        /// 某顆音符被判定了：吃掉吸收窗內、已經在等的誤觸候選中落在它 (判定區 ∪ Near 區) 的按鍵。
        /// </summary>
        private void AbsorbStraysAround(NoteData nd)
        {
            if (nd == null || _pendingStrays.Count == 0) return;
            int low = Mathf.Min(nd.startLane, nd.endLane) - NearReach;
            int high = Mathf.Max(nd.startLane, nd.endLane) + NearReach;
            float now = Time.unscaledTime;
            for (int i = _pendingStrays.Count - 1; i >= 0; i--)
            {
                var stray = _pendingStrays[i];
                if (stray.lane < low || stray.lane > high) continue;
                if ((now - stray.realtime) * 1000f > StrayHoldMs) continue;
                MistouchProbe.StrayAbsorbed();
                _pendingStrays.RemoveAt(i);          // 吸收：不出聲、不扣分
            }
        }

        // Award extra Perfect judgments for active holds while the player keeps the lane pressed.
        // This runs each frame and awards when an eighth-note interval has elapsed since the
        // last award (or since press start for the first award), capped by state.maxExtra.
        private void ProcessHoldExtraAwards(float songPos)
        {
            try
            {
                foreach (var kv in activeHoldStates)
                {
                    var st = kv.Value;
                    if (st == null) continue;

                    if (st.maxExtra <= 0)
                    {
                        continue;
                    }

                    if (st.awardedExtra >= st.maxExtra)
                    {
                        continue;
                    }

                    // In normal (non-debug) mode, we always award per-beat: pressed => JUST, not pressed => MISS
                    // In DebugMode, retain legacy behavior (only award when pressed)
                    bool debugModeActive = AutoFor(st.note);
                    bool hasPressed = st.pressedLanes != null && st.pressedLanes.Count > 0;
                    // If no pressed lanes tracked on the state, consult the shared input query
                    // so players who press after a missed head still count for per-beat awards.
                    // Must use IsLaneCurrentlyPressed (RealtimeBuffer + keyboard + MIDI), not the
                    // keyboard adapter alone — otherwise MIDI/touch players holding a recovered hold
                    // read as "not pressing" and get a spurious per-beat Miss / combo break.
                    try
                    {
                        if (!hasPressed)
                        {
                            var ndCheck2 = st.note?.NoteData;
                            if (ndCheck2 != null)
                            {
                                int startL = ndCheck2.startLane;
                                int endL = ndCheck2.endLane >= startL ? ndCheck2.endLane : startL;
                                for (int l = startL; l <= endL; l++)
                                {
                                    if (IsLaneCurrentlyPressed(l)) { hasPressed = true; break; }
                                }
                            }
                        }
                    }
                    catch { }
                    // Hold "break tolerance": a release shorter than holdBreakToleranceMs counts as still
                    // held, so a brief finger blip awards no Miss and does not break combo. HandleHeldKeyRelease
                    // sets pendingSwapDeadline to (releaseTime + tolerance) and a re-press clears it, so
                    // "released but still inside the tolerance window" is exactly songPos < pendingSwapDeadline.
                    if (!hasPressed && st.pendingSwapDeadline > float.MinValue && songPos < st.pendingSwapDeadline)
                    {
                        hasPressed = true;
                    }
                    if (debugModeActive && !hasPressed)
                    {
                        // DebugMode: skip awarding when not pressed
                        continue;
                    }

                    AwardHoldExtraTicksThrough(st, songPos, hasPressed || debugModeActive);
                }
            }
            catch { }
        }

        // Awards every scheduled hold tick up to an event/song timestamp. The cursor advances
        // by the fixed interval rather than being reset to the current frame, so frame hitches
        // cannot permanently discard ticks or alter the rhythm of later ticks.
        private void AwardHoldExtraTicksThrough(ActiveHoldState st, float throughSongPos, bool consideredPressed)
        {
            if (st == null || st.note == null || st.maxExtra <= 0 || st.awardedExtra >= st.maxExtra)
                return;

            float unitMs = st.eighthMs;
            if (unitMs <= 0f)
            {
                try
                {
                    var conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
                    unitMs = conductor != null
                        ? HoldJudgment.ComputeEighthMs(conductor.bpm)
                        : HoldJudgment.ComputeEighthMs(120f);
                }
                catch { unitMs = HoldJudgment.ComputeEighthMs(120f); }
            }

            float intervalMs = Mathf.Max(1f, unitMs);
            float lastTick = st.lastExtraAwardSongPosMs == float.MinValue
                ? st.pressSongPos
                : st.lastExtraAwardSongPosMs;
            float limit = throughSongPos;
            var data = st.note.NoteData;
            if (data != null) limit = Mathf.Min(limit, data.endTime);

            int lane = -1;
            if (consideredPressed && st.pressedLanes != null)
            {
                foreach (var pressedLane in st.pressedLanes)
                {
                    lane = pressedLane;
                    break;
                }
            }
            if (lane < 0 && data != null) lane = data.startLane;

            float nextTick = lastTick + intervalMs;
            while (st.awardedExtra < st.maxExtra && nextTick <= limit + 0.001f)
            {
                var tickResult = consideredPressed ? JudgmentResult.Perfect : JudgmentResult.Miss;
                var payload = RentPayload();
                payload.note = st.note;
                payload.result = tickResult;
                payload.buttonId = lane;
                payload.inputSongPosMs = nextTick;
                payload.pressSongPosMs = nextTick;
                payload.releaseSongPosMs = 0f;
                payload.targetTimeMs = nextTick;
                payload.absoluteOffsetMs = 0f;
                // When several ticks are recovered in one frame, show only the newest popup.
                // All recovered ticks still contribute independently to score and combo.
                payload.triggerPopup = nextTick + intervalMs > limit + 0.001f ||
                                       st.awardedExtra + 1 >= st.maxExtra;
                payload.triggerMesh = consideredPressed;
                payload.triggerHitSound = false;
                payload.isHoldExtra = true;
                payload.isSustainTick = true;
                payload.isHoldHead = false;
                payload.isHoldTail = false;
                payload.usePersistentMesh = false;
                payload.hasHeadTiming = false;
                try { ApplyJudgmentPayload(payload); } catch { }

                st.awardedExtra++;
                if (consideredPressed) st.pressedAwardedExtra++;
                st.lastExtraAwardSongPosMs = nextTick;
                if (_cachedDebugMode)
                    BuildLogger.Log($"[JMgr] AwardTick note={st.note?.name ?? "<null>"} tickTime={nextTick:F2} result={tickResult} awarded={st.awardedExtra}/{st.maxExtra} pressedCount={st.pressedAwardedExtra}");
                nextTick += intervalMs;
            }
        }

        // Grade the un-held remainder of a hold when it is finalized without every per-beat extra
        // having been awarded. The grade is lenient and reflects how much of the hold the player
        // actually held: >= 80% held -> Great, >= 50% held -> Good, otherwise Miss.
        // pressedCount = extras awarded while actually pressed; total = maxExtra for the hold.
        private static JudgmentResult ComputeHoldFillResult(int pressedCount, int total)
        {
            if (total <= 0) return JudgmentResult.Miss;
            float pct = (100f * (float)pressedCount) / (float)total;
            if (pct >= 80f) return JudgmentResult.Great;
            if (pct >= 50f) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }

        // Finalize only those active holds whose endTime has passed. This mirrors the
        // tail-finalization portion of FlushPendingJudgments but only targets expired holds
        // to avoid unnecessary work and to ensure holds are reclaimed when their endTime passes.
        private void FinalizeExpiredHolds(float songPos)
        {
            toFinalizeScratch.Clear();
            foreach (var kv in activeHoldStates) toFinalizeScratch.Add(kv.Key);
            for (int i = 0; i < toFinalizeScratch.Count; i++)
            {
                var note = toFinalizeScratch[i];
                if (note == null) continue;
                try
                {
                    var nd = note.NoteData; if (nd == null) continue;
                    // compute release deadline: normally endTime, but for auto-started staccato
                    // use startTime + stacAutoReleaseMs so auto-flow releases earlier to match desired "all street" effect
                    var handler = GetHandler(note);
                    var state = activeHoldStates.TryGetValue(note, out var s) ? s : null;
                    float releaseDeadline = nd.endTime;
                    if (note.IsStaccato && state != null && state.autoStarted)
                    {
                        releaseDeadline = nd.startTime + (float)stacAutoReleaseMs;
                    }
                    // In non-debug mode, allow recovery grace period (goodMs) after endTime before finalizing
                    float finalizeDeadline = releaseDeadline;
                    bool debugModeActive = AutoFor(note);
                    if (!debugModeActive && !note.IsStaccato)
                    {
                        finalizeDeadline = releaseDeadline + (float)goodMs;
                    }
                    // only finalize if we've passed the computed finalize deadline
                        if (songPos < finalizeDeadline)
                        {
                        // However, handle pending swap deadlines: if a hold has a pendingSwapDeadline
                        // and it has expired, finalize now using that deadline as release time.
                        var stateChk = activeHoldStates.TryGetValue(note, out var stchk) ? stchk : null;
                        if (stateChk != null && stateChk.pendingSwapDeadline > float.MinValue && songPos >= stateChk.pendingSwapDeadline)
                        {
                            // force finalize using pendingSwapDeadline
                        }
                        else continue;
                    }
                    float releaseTime = releaseDeadline;
                    if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] FinalizeExpiredHolds: note={note?.name ?? "<null>"} songPos={songPos} releaseDeadline={releaseDeadline} awarded={activeHoldStates[note]?.awardedExtra}/{activeHoldStates[note]?.maxExtra} autoStarted={activeHoldStates[note]?.autoStarted}"); } catch { } }
                    JudgmentResult res;
                    if (note.IsStaccato)
                    {
                        res = StaccatoJudgment.Judge(note, releaseTime, stacPerfectMs, stacGreatMs, stacGoodMs);
                    }
                    else
                    {
                        res = handler != null ? handler.EvaluateHoldEnd(note, releaseTime, activeHoldStates[note].pressSongPos) : JudgmentResult.Miss;
                    }
                    float headInput = activeHoldStates[note].hasHeadTiming ? activeHoldStates[note].headInputSongPosMs : activeHoldStates[note].pressSongPos;
                    float headTarget = activeHoldStates[note].hasHeadTiming ? activeHoldStates[note].headTargetTimeMs : (nd != null ? nd.startTime : activeHoldStates[note].pressSongPos);
                    float headOffset = activeHoldStates[note].hasHeadTiming ? activeHoldStates[note].headAbsoluteOffsetMs : UnityEngine.Mathf.Abs(headInput - headTarget);
                    // Emit judgments for any remaining un-awarded hold extras before final tail judgement
                    try
                    {
                        var stRem = activeHoldStates.TryGetValue(note, out var strem) ? strem : null;
                        if (stRem != null && stRem.maxExtra > stRem.awardedExtra)
                        {
                            int missing = stRem.maxExtra - stRem.awardedExtra;
                            // determine summary result based on how many extras WERE pressed
                            int pressedCount = stRem.pressedAwardedExtra;
                            int total = stRem.maxExtra > 0 ? stRem.maxExtra : 1;
                            float pct = (100f * (float)pressedCount) / (float)total;
                            JudgmentResult fillResult = ComputeHoldFillResult(pressedCount, total);
                            if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Finalize fill extras note={note?.name ?? "<null>"} pressed={stRem.pressedAwardedExtra} max={stRem.maxExtra} pct={pct:F1} fill={fillResult}"); } catch { } }

                            int silentFillCount = Mathf.Max(0, missing - 1);
                            if (silentFillCount > 0)
                            {
                                if (!note.IsTutorialDemo)
                                    try { StatsManager.Instance?.IncrementMany(fillResult, silentFillCount); } catch { }
                                _hudDirty = true;
                            }
                            // One visible payload is enough; the preceding results were
                            // aggregated above to avoid a long effect/popup burst in one frame.
                            for (int mi = 0; mi < Mathf.Min(1, missing); mi++)
                            {
                                var laneExtra = nd.startLane;
                                var extraPayload = RentPayload();
                                extraPayload.note = note;
                                extraPayload.result = fillResult;
                                extraPayload.buttonId = laneExtra;
                                extraPayload.inputSongPosMs = releaseTime;
                                extraPayload.pressSongPosMs = releaseTime;
                                extraPayload.releaseSongPosMs = releaseTime;
                                extraPayload.targetTimeMs = releaseTime;
                                extraPayload.absoluteOffsetMs = 0f;
                                extraPayload.triggerPopup = true;
                                extraPayload.triggerMesh = (fillResult == JudgmentResult.Perfect || fillResult == JudgmentResult.Great || fillResult == JudgmentResult.Good);
                                extraPayload.triggerHitSound = false;
                                extraPayload.isHoldExtra = true;
                                extraPayload.isSustainTick = true;
                                extraPayload.isHoldHead = false;
                                extraPayload.isHoldTail = false;
                                extraPayload.usePersistentMesh = false;
                                extraPayload.hasHeadTiming = false;
                                try { ApplyJudgmentPayload(extraPayload); } catch { }
                            }
                            stRem.awardedExtra = stRem.maxExtra;
                        }
                    }
                    catch { }

                    var payload = RentPayload();
                    payload.note = note;
                    payload.result = res;
                    payload.buttonId = nd.startLane;
                    payload.inputSongPosMs = headInput;
                    payload.pressSongPosMs = activeHoldStates[note].pressSongPos;
                    payload.releaseSongPosMs = releaseTime;
                    payload.targetTimeMs = headTarget;
                    payload.absoluteOffsetMs = headOffset;
                    payload.triggerPopup = true;
                    payload.triggerMesh = false;
                    payload.triggerHitSound = false;
                    payload.isHoldHead = false;
                    payload.isHoldTail = true;
                    payload.usePersistentMesh = false;
                    payload.headInputSongPosMs = headInput;
                    payload.headTargetTimeMs = headTarget;
                    payload.headAbsoluteOffsetMs = headOffset;
                    payload.hasHeadTiming = activeHoldStates[note].hasHeadTiming;
                    if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] FinalizeExpiredHolds: finalizing tail note={note?.name ?? "<null>"} endTime={nd.endTime} songPos={songPos} finalizeDeadline={finalizeDeadline} res={res} autoStarted={activeHoldStates[note]?.autoStarted} autoSimulate={autoSimulateStaccatoRelease} releaseTime={releaseTime}"); } catch { } }
                    // For expired tails, do not create new protection windows — immediate apply unless a pending tail exists
                    bool storedTail = Protection.ResolveTailJudgment(note, payload, songPos, false, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                    if (!storedTail)
                    {
                        try { CleanupHoldState(note); } catch { }
                    }
                }
                catch { }
            }
            // Additionally, finalize holds whose pendingSwapDeadline expired but didn't hit endTime finalize path
            foreach (var kv in activeHoldStates)
            {
                var st = kv.Value; if (st == null) continue;
                if (st.pendingSwapDeadline > float.MinValue && songPos >= st.pendingSwapDeadline)
                {
                    var note = st.note; if (note == null) continue;
                    try
                    {
                        var nd = note.NoteData; if (nd == null) continue;
                        float releaseTime = st.pendingSwapDeadline;
                        var handler = GetHandler(note);
                        JudgmentResult res;
                        if (note.IsStaccato)
                        {
                            res = StaccatoJudgment.Judge(note, releaseTime, stacPerfectMs, stacGreatMs, stacGoodMs);
                        }
                        else
                        {
                            res = handler != null ? handler.EvaluateHoldEnd(note, releaseTime, st.pressSongPos) : JudgmentResult.Miss;
                        }
                        float headInput = st.hasHeadTiming ? st.headInputSongPosMs : st.pressSongPos;
                        float headTarget = st.hasHeadTiming ? st.headTargetTimeMs : (nd != null ? nd.startTime : st.pressSongPos);
                        float headOffset = st.hasHeadTiming ? st.headAbsoluteOffsetMs : UnityEngine.Mathf.Abs(headInput - headTarget);
                        // Emit judgments for any remaining un-awarded hold extras before final tail judgement
                        try
                        {
                            if (st.maxExtra > st.awardedExtra)
                            {
                                int missing2 = st.maxExtra - st.awardedExtra;
                                int pressedCount2 = st.pressedAwardedExtra;
                                int total2 = st.maxExtra > 0 ? st.maxExtra : 1;
                                float pct2 = (100f * (float)pressedCount2) / (float)total2;
                                JudgmentResult fillResult2 = ComputeHoldFillResult(pressedCount2, total2);
                                if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] PendingSwap finalize fill extras note={note?.name ?? "<null>"} pressed={st.pressedAwardedExtra} max={st.maxExtra} pct={pct2:F1} fill={fillResult2}"); } catch { } }

                                int silentFillCount2 = Mathf.Max(0, missing2 - 1);
                                if (silentFillCount2 > 0)
                                {
                                    if (!note.IsTutorialDemo)
                                        try { StatsManager.Instance?.IncrementMany(fillResult2, silentFillCount2); } catch { }
                                    _hudDirty = true;
                                }
                                for (int mi2 = 0; mi2 < Mathf.Min(1, missing2); mi2++)
                                {
                                    var laneExtra2 = nd.startLane;
                                    var extraPayload2 = RentPayload();
                                    extraPayload2.note = note;
                                    extraPayload2.result = fillResult2;
                                    extraPayload2.buttonId = laneExtra2;
                                    extraPayload2.inputSongPosMs = releaseTime;
                                    extraPayload2.pressSongPosMs = releaseTime;
                                    extraPayload2.releaseSongPosMs = releaseTime;
                                    extraPayload2.targetTimeMs = releaseTime;
                                    extraPayload2.absoluteOffsetMs = 0f;
                                    extraPayload2.triggerPopup = true;
                                    extraPayload2.triggerMesh = (fillResult2 == JudgmentResult.Perfect || fillResult2 == JudgmentResult.Great || fillResult2 == JudgmentResult.Good);
                                    extraPayload2.triggerHitSound = false;
                                    extraPayload2.isHoldExtra = true;
                                    extraPayload2.isSustainTick = true;
                                    extraPayload2.isHoldHead = false;
                                    extraPayload2.isHoldTail = false;
                                    extraPayload2.usePersistentMesh = false;
                                    extraPayload2.hasHeadTiming = false;
                                    try { ApplyJudgmentPayload(extraPayload2); } catch { }
                                }
                                st.awardedExtra = st.maxExtra;
                            }
                        }
                        catch { }


                        var payload = RentPayload();
                        payload.note = note;
                        payload.result = res;
                        payload.buttonId = nd.startLane;
                        payload.inputSongPosMs = headInput;
                        payload.pressSongPosMs = st.pressSongPos;
                        payload.releaseSongPosMs = releaseTime;
                        payload.targetTimeMs = headTarget;
                        payload.absoluteOffsetMs = headOffset;
                        payload.triggerPopup = true;
                        payload.triggerMesh = false;
                        payload.triggerHitSound = false;
                        payload.isHoldHead = false;
                        payload.isHoldTail = true;
                        payload.usePersistentMesh = false;
                        payload.headInputSongPosMs = headInput;
                        payload.headTargetTimeMs = headTarget;
                        payload.headAbsoluteOffsetMs = headOffset;
                        payload.hasHeadTiming = st.hasHeadTiming;
                        bool storedTail = Protection.ResolveTailJudgment(note, payload, songPos, false, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                        if (!storedTail) { try { CleanupHoldState(note); } catch { } }
                    }
                    catch { }
                }
            }
        }

        public void RegisterNote(NoteController note)
        {
            if (note == null) return;
            activeNotes.Add(note);
            try { untrackedHoldLastCheck[note] = float.MinValue; } catch { }
        }

        public void UnregisterNote(NoteController note)
        {
            if (note == null) return;
            activeNotes.Remove(note);
            activeGestureStates.Remove(note);
            // 音符物件會被物件池拿去當下一顆音符用。暫定中的判定若留著，會套到別顆上。
            if (_provisionalPresses.TryGetValue(note, out ProvisionalPress provisional))
                DropProvisional(provisional);
            EatenInputProbe.Forget(note);
            try { untrackedHoldLastCheck.Remove(note); } catch { }
            _handlerCache.Remove(note);
            Protection.RemoveProtection(note);
            // Drop any lingering hold-tracking state. NoteController instances are pooled and reused,
            // so if a tracked hold is returned to the pool through a path that bypasses CleanupHoldState
            // (e.g. the endTime+500 failsafe in NoteController), the leftover state — pressedLanes, extras,
            // pending finalize — would bind to the NEXT note this instance is reused for and produce phantom
            // finalize/extra judgments. Guard laneToHoldState removal by identity so we never unmap a lane
            // that now belongs to a different note's hold.
            if (activeHoldStates.TryGetValue(note, out var st))
            {
                activeHoldStates.Remove(note);
                if (st != null)
                {
                    if (st.pendingFinalizeCoroutine != null) { try { StopCoroutine(st.pendingFinalizeCoroutine); } catch { } }
                    if (st.pressedLanes != null)
                    {
                        foreach (var lane in st.pressedLanes)
                        {
                            if (laneToHoldState.TryGetValue(lane, out var mapped) && mapped == st)
                                laneToHoldState.Remove(lane);
                        }
                    }
                }
            }
        }

        // Stats / reporting wrappers so callers (or adapter) can query via the manager
        public (int perfect, int great, int good, int miss, int fail, int combo, int maxCombo, int score) GetStatsWithCombo()
        {
            try { return StatsManager.Instance != null ? StatsManager.Instance.GetStatsWithCombo() : (0,0,0,0,0,0,0,0); } catch { return (0,0,0,0,0,0,0,0); }
        }

        public (int fast, int late) GetTimingStats()
        {
            try { return StatsManager.Instance != null ? StatsManager.Instance.GetTimingStats() : (0,0); } catch { return (0,0); }
        }

        public (int timed, int fallback, float averageMs, float minMs, float maxMs) GetInputTimingStats()
        {
            if (_timedInputCount <= 0)
                return (0, _fallbackInputCount, 0f, 0f, 0f);
            return (_timedInputCount, _fallbackInputCount,
                (float)(_inputProjectionSumMs / _timedInputCount),
                _inputProjectionMinMs, _inputProjectionMaxMs);
        }

        public (int perfect, int great, int good, int miss, int fail) GetStats()
        {
            try { return StatsManager.Instance != null ? StatsManager.Instance.GetStats() : (0,0,0,0,0); } catch { return (0,0,0,0,0); }
        }

        public NoteController FindBestNote(int buttonId, float songPos, int goodMs)
        {
            // Priority algorithm extracted into NoteSelector; pass the nearby-notes window.
            ProcessOverlappedJudgmentSuppressions(songPos);
            return NoteSelector.FindBestNote(buttonId, songPos, goodMs, GetNearbyNotes(songPos));
        }

        // Protection & pending handling migrated into JudgmentProtection

        // 依歌曲位置取得「時間窗內」的可判定音符，避免頻繁全掃 activeNotes。
        private System.Collections.Generic.IEnumerable<NoteController> GetNearbyNotes(float songPos)
        {
            // 若與上次 songPos 差異極小，直接重用 cache。
            //
            // 同一格內也重用：每一下按鍵帶的是自己的硬體時戳，彼此差幾毫秒，原本
            // 5ms 的容差讓一個和弦的每一顆鍵都重建一次清單（每次都要掃全部音符、
            // 呼叫原生屬性）。視窗前後各留了 250ms，一格內的差距完全在裡面。重建
            // 也只會發生在跨格的時候，不會在某個 foreach 走到一半時換掉清單。
            int frame = Time.frameCount;
            if (nearbyNotesCachedSongPos > float.MinValue &&
                (frame == nearbyNotesCachedFrame ||
                 Mathf.Abs(songPos - nearbyNotesCachedSongPos) < NearbyCacheEpsMs))
            {
                return nearbyNotesScratch;
            }

            nearbyNotesCachedFrame = frame;
            nearbyNotesCachedSongPos = songPos;
            nearbyNotesScratch.Clear();

            // 視窗：以最大判定窗為基底，額外預留一些提前量，避免快進時漏抓。
            float window = Mathf.Max(goodMs, stacGoodMs) + 250f;
            float minTime = songPos - window;
            float maxTime = songPos + window;

            foreach (var n in activeNotes)
            {
                if (n == null || !n.IsActive || !n.IsJudgeable) continue;
                var nd = n.NoteData; if (nd == null) continue;

                // 只關心頭部時間是否落在視窗內。對長條尾巴，進入尾部判定時計算會再次呼叫。
                float t = nd.startTime;
                if (t < minTime || t > maxTime)
                {
                    // Long Hold/Trill bodies remain relevant to head
                    // protection even when their start lies outside the small
                    // Tap lookup window.
                    bool sustainedOverlap = (HoldJudgment.IsHoldLikeNote(n) || IsGestureNote(nd)) &&
                        nd.endTime >= minTime && nd.startTime <= maxTime;
                    if (!sustainedOverlap) continue;
                }
                nearbyNotesScratch.Add(n);
            }

            return nearbyNotesScratch;
        }

        private void ProcessPendingJudgments(float songPos)
        {
            Protection.ProcessPendingJudgments(songPos, ApplyJudgmentPayload);
        }

        public void FlushPendingJudgments()
        {
            float songPos = GetSongPositionMs();
            // 還在暫定的判定先正式套用，它們的 payload 才會進到下面的保護佇列。
            SettleAllProvisionalPresses(true);
            // apply pending judgments stored in the protection module
            Protection.FlushPendingJudgments(ApplyJudgmentPayload);

            // finalize active holds
            toFinalizeScratch.Clear();
            foreach (var kv in activeHoldStates) toFinalizeScratch.Add(kv.Key);
            // Track notes whose tail payloads were stored by protection — those must NOT be cleaned up now
            var tailsStoredByProtection = new System.Collections.Generic.HashSet<NoteController>();
            for (int i = 0; i < toFinalizeScratch.Count; i++)
            {
                var note = toFinalizeScratch[i];
                if (note == null) continue;
                try
                {
                    var handler = GetHandler(note);
                    var nd = note.NoteData; if (nd == null) continue;
                    // decide releaseTime similarly to FinalizeExpiredHolds: allow auto staccato early release
                    var state = activeHoldStates.TryGetValue(note, out var s2) ? s2 : null;
                    float releaseTime = nd.endTime;
                    if (note.IsStaccato && state != null && state.autoStarted)
                    {
                        releaseTime = nd.startTime + (float)stacAutoReleaseMs;
                    }
                    JudgmentResult res;
                    if (note.IsStaccato)
                    {
                        // Use dedicated StaccatoJudgment helper (staccato uses its own timing windows)
                        res = StaccatoJudgment.Judge(note, releaseTime, stacPerfectMs, stacGreatMs, stacGoodMs);
                    }
                    else
                    {
                        res = handler != null ? handler.EvaluateHoldEnd(note, releaseTime, activeHoldStates[note].pressSongPos) : JudgmentResult.Miss;
                    }
                    float headInput = activeHoldStates[note].hasHeadTiming ? activeHoldStates[note].headInputSongPosMs : activeHoldStates[note].pressSongPos;
                    float headTarget = activeHoldStates[note].hasHeadTiming ? activeHoldStates[note].headTargetTimeMs : (nd != null ? nd.startTime : activeHoldStates[note].pressSongPos);
                    float headOffset = activeHoldStates[note].hasHeadTiming ? activeHoldStates[note].headAbsoluteOffsetMs : UnityEngine.Mathf.Abs(headInput - headTarget);
                    var payload = RentPayload();
                    payload.note = note;
                    payload.result = res;
                    payload.buttonId = nd.startLane;
                    payload.inputSongPosMs = headInput;
                    payload.pressSongPosMs = activeHoldStates[note].pressSongPos;
                    payload.releaseSongPosMs = releaseTime;
                    payload.targetTimeMs = headTarget;
                    payload.absoluteOffsetMs = headOffset;
                    payload.triggerPopup = true;
                    payload.triggerMesh = false;
                    payload.triggerHitSound = false;
                    payload.isHoldHead = false;
                    payload.isHoldTail = true;
                    payload.usePersistentMesh = false;
                    payload.headInputSongPosMs = headInput;
                    payload.headTargetTimeMs = headTarget;
                    payload.headAbsoluteOffsetMs = headOffset;
                    payload.hasHeadTiming = activeHoldStates[note].hasHeadTiming;
                    // For tail finalization, do not allow protection for staccato notes (match legacy behavior)
                    // When flushing, do NOT allow protection for tail finalization — enforce immediate apply.
                    bool storedTail = Protection.ResolveTailJudgment(note, payload, songPos, false, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                    if (storedTail) tailsStoredByProtection.Add(note);
                    // if storedTail is true, the protection module will apply later; otherwise payload applied immediately via callback
                }
                catch { }
            }
            for (int i = 0; i < toFinalizeScratch.Count; i++)
            {
                var note = toFinalizeScratch[i];
                if (note == null) continue;
                if (tailsStoredByProtection.Contains(note)) continue; // leave state for later cleanup when protection applies
                CleanupHoldState(note);
            }

            toAutoFinalizeScratch.Clear();
            foreach (var kv in autoHeldNotes)
            {
                var note = kv.Key;
                if (note == null) continue;
                toAutoFinalizeScratch.Add(note);
                try
                {
                    var handler = GetHandler(note);
                    float releaseTime = (note.NoteData != null) ? note.NoteData.endTime : songPos;
                    var res = handler != null ? handler.EvaluateHoldEnd(note, releaseTime, kv.Value) : JudgmentResult.Miss;
                    HideNoteMeshEffect(note);
                    try { note.OnHoldEnd(res); } catch { }
                }
                catch { }
            }
            for (int i = 0; i < toAutoFinalizeScratch.Count; i++)
            {
                var note = toAutoFinalizeScratch[i];
                if (note != null)
                {
                    try { autoHeldNotes.Remove(note); } catch { }
                }
            }
        }

        private void CleanupHoldState(NoteController note)
        {
            if (note == null) return;
            // clear only protection end marker here; do NOT remove pending head/tail entries
            // because those may have been stored by the protection module and must be
            // applied later by ProcessPendingJudgments/FlushPendingJudgments.
            _handlerCache.Remove(note);
            try {
                if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] CleanupHoldState note={note?.name ?? "<null>"} HasPendingHead={Protection.HasPendingHead(note)} HasPendingTail={Protection.HasPendingTail(note)}"); } catch { } }
                Protection.ClearProtectionEnd(note);
            } catch { }
            HideNoteMeshEffect(note);
            try { HitEffectRouter.StopHold(note); } catch { }
                if (activeHoldStates.TryGetValue(note, out var state))
            {
                if (state.pendingFinalizeCoroutine != null) { try { StopCoroutine(state.pendingFinalizeCoroutine); } catch { } }
                var lanesToRemove = new System.Collections.Generic.List<int>(state.pressedLanes);
                foreach (var lane in lanesToRemove) { /* per-key visuals disabled */ laneToHoldState.Remove(lane); }
                activeHoldStates.Remove(note);
                    try { untrackedHoldLastCheck.Remove(note); } catch { }
            }
        }
    }
}
