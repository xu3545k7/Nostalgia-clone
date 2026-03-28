using System.Collections.Generic;
using UnityEngine;

namespace Judgment
{
public class JudgmentManager : MonoBehaviour
{
    public static JudgmentManager Instance;
    private readonly System.Collections.Generic.HashSet<NoteController> activeNotes = new System.Collections.Generic.HashSet<NoteController>();

        // Cached視窗內的音符清單，避免每幀/每鍵反覆全域掃描 activeNotes
        private readonly System.Collections.Generic.List<NoteController> nearbyNotesScratch = new System.Collections.Generic.List<NoteController>();
        private float nearbyNotesCachedSongPos = float.MinValue;
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
        // HUD refresh dirty flag: set by TryIncrementStats, flushed once at end of Update().
        // Prevents N TextMeshPro mesh rebuilds when N notes are judged in the same frame.
        private bool _hudDirty;
        // Last-written HUD values — skip text assignment when unchanged (avoids TMP mesh rebuild).
        private int    _lastHudCombo  = -1;
        private int    _lastHudScore  = -1;
        private string _lastHudAchStr = null;
        /// <summary>
        /// Two notes within this time window (ms) on overlapping lanes share one input;
        /// both are judged and the key is NOT consumed (Inspector-editable).
        /// </summary>
        public int sharedInputThresholdMs
        {
            get => _inputProtection.SharedInputThresholdMs;
            set => _inputProtection.SharedInputThresholdMs = value;
        }
        private readonly System.Collections.Generic.Dictionary<int, ActiveHoldState> laneToHoldState = new System.Collections.Generic.Dictionary<int, ActiveHoldState>();

        // Scratch lists used when iterating/modifying collections
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
        public int perfectMs = 50;    
        public int greatMs = 100;
        public int goodMs = 150;
        // grace window (ms) allowing quick finger swap without finalizing hold
        public int swapGraceMs = 80;
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

        // Minimal compatibility stubs — these call into existing managers where appropriate
        public void ResetStats()
        {
            try { StatsManager.Instance?.ResetStats(); } catch { }
            // Invalidate HUD cache so the reset values (0/0) are written even if they
            // match the previous song's values, then flush immediately.
            _lastHudCombo  = -1;
            _lastHudScore  = -1;
            _lastHudAchStr = null;
            _hudDirty      = true;
            try { RefreshHudStats(); } catch { }
            // Clear per-frame consumption map so no leftover state carries across songs.
            try { _inputProtection.Reset(); } catch { }
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
                var songPos = GetSongPositionMs();
                var nd = n.NoteData;
                var payload = RentPayload();
                payload.note = n;
                payload.result = JudgmentResult.Perfect;
                payload.buttonId = nd != null ? nd.startLane : -1;
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
        public void ProcessButtonPress(int buttonId)
        {
            try
            {
                // ── Nostalgia judgment-protection: per-frame key consumption ──────────
                // Cache Time.frameCount once — it's a Unity native property (P/Invoke),
                // reading it repeatedly in the same call stack adds unnecessary overhead.
                int curFrame = Time.frameCount;
                if (_inputProtection.IsConsumed(buttonId, curFrame)) return;
                // ─────────────────────────────────────────────────────────────────────
                var songPos = GetSongPositionMs();
                // First: trigger ALL nearby soft notes matching this lane (allow multiple triggers)
                    // If this press falls within an already active hold's time range for this lane,
                    // attach the lane back to that hold so player can recover (non-drop behavior).
                    try
                    {
                        ActiveHoldState bestHold = null;
                        float bestEndDelta = float.MaxValue;
                        foreach (var kv in activeHoldStates)
                        {
                            var st = kv.Value; if (st == null || st.note == null) continue;
                            var ndH = st.note.NoteData; if (ndH == null) continue;
                            if (st.note.IsStaccato) continue; // staccato follows its own quick logic
                            if (buttonId < ndH.startLane || buttonId > ndH.endLane) continue;
                            // Allow recovery during hold time OR within goodMs tolerance after release
                            if (songPos < ndH.startTime || songPos > ndH.endTime + goodMs) continue;
                            // Prefer the hold whose end is soonest after current time (to avoid picking future overlaps)
                            float endDelta = ndH.endTime - songPos;
                            if (endDelta >= 0f && endDelta < bestEndDelta)
                            {
                                bestEndDelta = endDelta;
                                bestHold = st;
                            }
                        }
                        if (bestHold != null)
                        {
                            // Re-attach this lane to the active hold state to resume JUST ticks.
                            // Clear headMissed flag so the note becomes judgeable again
                            try { bestHold.note?.ClearHeadMissed(); } catch { }
                            // Clear any pending swap deadline so we don't finalize while swapping
                            try { bestHold.pendingSwapDeadline = float.MinValue; } catch { }
                            BeginHoldTracking(bestHold.note, buttonId, songPos);
                            _inputProtection.ConsumeWithSpatial(buttonId, curFrame, songPos);
                            return; // consume this press
                        }
                    }
                    catch { }
                try
                {
                    foreach (var n in GetNearbyNotes(songPos))
                    {
                        if (n == null || !n.IsActive || !n.IsJudgeable || n.IsJudged) continue;
                        if (!n.IsSoft) continue;
                        var softNd = n.NoteData; if (softNd == null) continue;
                        if (buttonId < softNd.startLane || buttonId > softNd.endLane) continue;
                        float delta = Mathf.Abs(softNd.startTime - songPos);
                        if (delta > goodMs) continue;

                        var softPayload = RentPayload();
                        softPayload.note = n;
                        softPayload.result = JudgmentResult.Perfect;
                        softPayload.buttonId = softNd.startLane;
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
                    }
                }
                catch { }

                // Then continue normal single-note matching for non-soft notes
                var c = FindBestNote(buttonId, songPos, goodMs);
                if (c == null)
                {
                    // If no judgeable note found, allow recovery for holds whose head was missed
                    // by searching active notes for hold-like notes that are within the hold window
                    // (including post-end goodMs grace) and whose head was missed (IsJudgeable == false)
                    try
                    {
                        NoteController missedHold = null;
                        foreach (var n in activeNotes)
                        {
                            if (n == null) continue;
                            if (!HoldJudgment.IsHoldLikeNote(n)) continue;
                            if (!n.IsActive) continue;
                            var ndMiss = n.NoteData; if (ndMiss == null) continue;
                            if (buttonId < ndMiss.startLane || buttonId > ndMiss.endLane) continue;
                            // within hold time or within grace after end
                            if (songPos < ndMiss.startTime || songPos > ndMiss.endTime + goodMs) continue;
                            // only consider holds that previously became unjudgeable (likely headMissed)
                            if (n.IsJudgeable) continue;
                            missedHold = n;
                            break;
                        }
                        if (missedHold != null)
                        {
                            // Treat this press as a recovered head press: clear headMissed and begin hold tracking.
                            try { missedHold.ClearHeadMissed(); } catch { }
                            // Compute head judgment using tap algorithm
                            var nd2 = missedHold.NoteData;
                            float target = nd2 != null ? nd2.startTime : songPos;
                            var headResult = TapJudgment.Judge(missedHold, songPos, perfectMs, greatMs, goodMs);
                            var headPayload = RentPayload();
                            headPayload.note = missedHold;
                            headPayload.result = headResult;
                            headPayload.buttonId = nd2 != null ? nd2.startLane : buttonId;
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
                            // create hold state and attach this lane
                            BeginHoldTracking(missedHold, buttonId, songPos);
                            // If protection stored the head, mark pending in the state if present
                            try { if (stored && activeHoldStates.TryGetValue(missedHold, out var s2)) s2.headJudgmentPending = true; } catch { }
                            _inputProtection.ConsumeWithSpatial(buttonId, curFrame, songPos);
                            return;
                        }
                    }
                    catch { }
                    return;
                }

                // If this is a hold-like note, prefer the hold flow (BeginHoldTracking)
                bool isHoldHead = HoldJudgment.IsHoldLikeNote(c);
                if (isHoldHead)
                {
                    var handler = GetHandler(c);
                    float pressSongPos = Mathf.Max(songPos, (c.NoteData != null ? c.NoteData.startTime : songPos));
                    float delta = Mathf.Abs((c.NoteData != null ? c.NoteData.startTime : songPos) - songPos);
                    JudgmentResult? forcedResult = c.IsSoft ? JudgmentResult.Perfect : (JudgmentResult?)null;
                    BeginHoldTracking(c, handler, buttonId, pressSongPos, delta, forcedResult);
                    // ── 100ms co-judge check ──────────────────────────────────────────
                    if (!TryCoJudge(c, buttonId, songPos)) _inputProtection.ConsumeWithSpatial(buttonId, curFrame, songPos);
                    // ─────────────────────────────────────────────────────────────────
                    return;
                }

                // Non-hold tap: judge and route through protection
                var result = TapJudgment.Judge(c, songPos, perfectMs, greatMs, goodMs);
                var nd = c.NoteData;
                var payload = RentPayload();
                payload.note = c;
                payload.result = result;
                payload.buttonId = nd?.startLane ?? buttonId;
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
                Protection.ResolveHeadJudgment(c, payload, songPos, true, GetNearbyNotes(songPos), goodMs, ApplyJudgmentPayload);
                // ── 100ms co-judge check ──────────────────────────────────────────
                if (!TryCoJudge(c, buttonId, songPos)) _inputProtection.ConsumeWithSpatial(buttonId, curFrame, songPos);
                // ─────────────────────────────────────────────────────────────────
            }
            catch { }
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

        // Returns true if there are co-judge notes, and judges them all.
        // Does NOT consume the button when returning true.
        // Co-judge condition:
        //   1) |other.startTime - primary.startTime| <= SpatialStopWindowMs (fallback SharedInputThresholdMs)
        //   2) max(minLaneA, minLaneB) <= min(maxLaneA, maxLaneB)  (lane-range overlap)
        private bool TryCoJudge(NoteController primary, int buttonId, float songPos)
        {
            bool anyCoJudge = false;
            try
            {
                float primaryStart = primary?.NoteData != null ? primary.NoteData.startTime : songPos;
                var primaryNd = primary?.NoteData;
                int coJudgeWindowMs = _inputProtection != null && _inputProtection.SpatialStopWindowMs > 0
                    ? _inputProtection.SpatialStopWindowMs
                    : (_inputProtection != null ? _inputProtection.SharedInputThresholdMs : 100);

                foreach (var other in GetNearbyNotes(songPos))
                {
                    if (other == null || other == primary) continue;
                    if (!other.IsActive || !other.IsJudgeable || other.IsJudged) continue;
                    if (other.IsSoft) continue;
                    var ond = other.NoteData; if (ond == null) continue;
                    if (Mathf.Abs(ond.startTime - primaryStart) > coJudgeWindowMs) continue;
                    if (!HasLaneRangeOverlap(primaryNd, ond)) continue;
                    // Co-judge: this note shares the input
                    anyCoJudge = true;
                    if (HoldJudgment.IsHoldLikeNote(other))
                    {
                        var coH = GetHandler(other);
                        int coButton = ClampLaneToNoteRange(buttonId, ond);
                        float coPress = Mathf.Max(songPos, ond.startTime);
                        float coDelta = Mathf.Abs(ond.startTime - songPos);
                        JudgmentResult? coForced = other.IsSoft ? JudgmentResult.Perfect : (JudgmentResult?)null;
                        try { BeginHoldTracking(other, coH, coButton, coPress, coDelta, coForced); } catch { }
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
            NoteController best = null;
            float bestDelta = float.MaxValue;
            foreach (var n in GetNearbyNotes(songPos))
            {
                if (n == null || !n.IsActive || !n.IsJudgeable || n.IsJudged) continue;
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
            payload.buttonId = ndSoft?.startLane ?? buttonId;
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

                        float startTime = (note.NoteData != null) ? note.NoteData.startTime : pressSongPos;
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
                                    PressSongPos = pressSongPos,
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
                                    try { headResult = handler.EvaluateHead(note, pressSongPos, Mathf.Abs(deltaMs), buttonId); }
                                    catch { headResult = HoldJudgment.EvaluateHoldHeadResult(Mathf.Abs(deltaMs), perfectMs, greatMs, goodMs); }
                                }
                                try { handler.PlayHeadSound(note, pressSongPos, headResult); } catch { }
                                beginInfo = new HoldNoteHandler.HoldBeginInfo(headResult, headResult != JudgmentResult.Miss);
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

                            if (beginInfo.UsePersistentMesh) try { ShowNoteMeshEffect(note, beginInfo.Result, beginInfo.UsePersistentMesh); } catch { }
                            try { handler.OnBeginHold(note, buttonId, pressSongPos, deltaMs, beginInfo.Result); } catch { }
                            var nd = note.NoteData;
                            float targetTime = nd != null ? nd.startTime : pressSongPos;
                            float absoluteOffset = Mathf.Abs(pressSongPos - targetTime);
                            var payload = RentPayload();
                            payload.note = note;
                            payload.result = beginInfo.Result;
                            payload.buttonId = buttonId;
                            payload.inputSongPosMs = pressSongPos;
                            payload.pressSongPosMs = pressSongPos;
                            payload.targetTimeMs = targetTime;
                            payload.absoluteOffsetMs = absoluteOffset;
                            payload.usePersistentMesh = beginInfo.UsePersistentMesh;
                            payload.isHoldHead = true;
                            payload.isHoldTail = false;
                            payload.triggerMesh = false;
                            payload.triggerPopup = true;
                            payload.triggerHitSound = false;

                            bool stored = Protection.ResolveHeadJudgment(note, payload, pressSongPos, true, GetNearbyNotes(pressSongPos), goodMs, ApplyJudgmentPayload);
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
                        var particleManager = HitParticleManager.Instance;
                        if (particleManager != null)
                        {
                            try { particleManager.BeginHoldEmission(note, buttonId); } catch { }
                        }
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
                try { HitParticleManager.Instance?.PlayHitEffect(st.note, lane, JudgmentResult.Perfect); } catch { }
                // extra award now handled by per-frame ProcessHoldExtraAwards
            }
            catch { }
        }

        // Call this when the held key/lane is released. This will finalize the hold if no other lanes remain pressed.
        public void HandleHeldKeyRelease(int lane, float songPos)
        {
            try
            {
                if (!laneToHoldState.TryGetValue(lane, out var st)) return;
                var note = st.note;
                // remove lane mapping and persistent mesh
                    // KeyHitEffectManager disabled: per-key visual removed (Judge mesh only).
                st.pressedLanes.Remove(lane);
                laneToHoldState.Remove(lane);

                // if other lanes for this hold are still pressed, don't finalize yet
                if (st.pressedLanes.Count > 0) return;
                // if no lanes remain pressed, allow a short swap grace window before finalizing
                bool debugModeActive = _cachedDebugMode;
                bool isStaccato = false;
                try { isStaccato = note != null && note.IsStaccato; } catch { isStaccato = false; }
                if (!debugModeActive && !isStaccato)
                {
                    // set pending swap deadline so quick lane swaps don't finalize the hold
                    // Use goodMs as the release grace so releasing player must be away for goodMs
                    // before reword stops (default goodMs is 150ms).
                    st.pendingSwapDeadline = songPos + (float)goodMs;
                    // dim persistent head visual while within swap-grace (keep visible for recovery)
                    try { NoteJudgementMeshManager.EnsureCreated().SetPersistentLit(note, false); } catch { }
                    if (debugModeActive) { try { BuildLogger.Log($"[JMgr] HandleHeldKeyRelease: deferred finalize (swap grace) note={note?.name ?? "<null>"} until={st.pendingSwapDeadline}"); } catch { } }
                    if (debugModeActive) BuildLogger.Log($"[JMgr] HandleHeldKeyRelease: note={note?.name ?? "<null>"} pendingSwapDeadline={st.pendingSwapDeadline}");
                    return;
                }

                // Debug mode or staccato: finalize tail judgment immediately (legacy behavior)
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
                payload.buttonId = nd != null ? nd.startLane : -1;
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

        private void TryIncrementStats(JudgmentResult res)
        {
            try { StatsManager.Instance?.Increment(res); } catch { }
            // Mark HUD dirty; actual refresh happens once at end of Update() to avoid
            // N TextMeshPro mesh rebuilds when N notes are judged in the same frame.
            _hudDirty = true;
        }

        private static bool IsPositiveJudgment(JudgmentResult res)
        {
            return res == JudgmentResult.Perfect || res == JudgmentResult.Great || res == JudgmentResult.Good;
        }

        private void RecordTimingOffsetIfNeeded(JudgmentPayload payload)
        {
            if (payload == null) return;
            if (!IsPositiveJudgment(payload.result)) return;
            try
            {
                if (payload.isHoldTail)
                {
                    if (!payload.hasHeadTiming) return;
                    StatsManager.Instance?.RecordTimingOffset(payload.headInputSongPosMs - payload.headTargetTimeMs);
                }
                else
                {
                    StatsManager.Instance?.RecordTimingOffset(payload.inputSongPosMs - payload.targetTimeMs);
                }
            }
            catch { }
        }

        // Update inspector-assigned HUD texts (optional)
        private void RefreshHudStats()
        {
            try
            {
                var mgr = StatsManager.Instance;
                if (mgr == null) return;
                var t = mgr.GetStatsWithCombo();
                int combo = t.Item6;
                int score = t.Item8;

                // Only rebuild TextMeshPro mesh when values actually changed.
                if (combo != _lastHudCombo || score != _lastHudScore)
                {
                    _lastHudCombo = combo;
                    _lastHudScore = score;
                    string comboStr = "Combo\n" + combo.ToString();
                    string scoreStr = "Score:\n" + score.ToString();
                    if (comboText != null) comboText.text = comboStr;
                    if (comboTMP  != null) comboTMP.text  = comboStr;
                    if (scoreText != null) scoreText.text = scoreStr;
                    if (scoreTMP  != null) scoreTMP.text  = scoreStr;
                }

                try
                {
                    if (achievementTMP != null)
                    {
                        // decimal.ToString("F3") is expensive; skip when combo/score unchanged
                        // (achievement only changes when a judgment is applied).
                        decimal achDec = mgr.GetAchievementPercentDecimal();
                        string achStr = achDec.ToString("F3");
                        if (achStr != _lastHudAchStr)
                        {
                            _lastHudAchStr = achStr;
                            achievementTMP.text = "Achv rate:\n" + achStr + "%";
                        }
                    }
                }
                catch { }
            }
            catch { }
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

        private void ShowJudgementPopups(int buttonId, UnityEngine.Vector3 notePosition, JudgmentResult res)
        {
            try
            {
                var popup = JudgePopupManager.Instance;
                if (popup != null && !popup.enablePopup) { return; }
                if (popup != null)
                {
                    var keyRect = VirtualKeyButton.GetRectForId(buttonId);
                    if (keyRect != null) popup.ShowPopupAtRectTransform(keyRect, res);
                }
            }
            catch { }
            try { SimpleJudgePopupManager.Instance?.ShowAtPosition(notePosition, res); } catch { }
        }

        private void ShowNoteMeshEffect(NoteController note, JudgmentResult res, bool persistent = false)
        {
            if (note == null) return;
            if (res != JudgmentResult.Perfect && res != JudgmentResult.Great && res != JudgmentResult.Good) return;
            try { NoteJudgementMeshManager.EnsureCreated().ShowEffect(note, res, persistent); } catch { }
            try { HitParticleManager.Instance?.PlayHitEffect(note, note.NoteData?.startLane ?? -1, res); } catch { }
        }

        private void HideNoteMeshEffect(NoteController note)
        {
            if (note == null) return;
            try { NoteJudgementMeshManager.EnsureCreated().HidePersistentEffect(note); } catch { }
        }

        private void ApplyJudgmentPayload(JudgmentPayload payload)
        {
            if (payload == null) return;
            var note = payload.note;
            if (note == null) return;
            if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] ApplyJudgmentPayload note={note.name ?? note.GetInstanceID().ToString()} res={payload.result} head={payload.isHoldHead} tail={payload.isHoldTail} popup={payload.triggerPopup} mesh={payload.triggerMesh} sound={payload.triggerHitSound}"); } catch { } }

            INoteHandler handler = null;
            try { handler = GetHandler(note); } catch { handler = null; }

            if (payload.isHoldTail)
            {
                if (payload.triggerMesh) ShowNoteMeshEffect(note, payload.result);
                if (payload.triggerPopup) { ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result); }
                TryIncrementStats(payload.result);
                RecordTimingOffsetIfNeeded(payload);
                try { note.OnHoldEnd(payload.result); } catch { }
                try { handler?.OnFinalizeHold(note, payload.releaseSongPosMs, payload.pressSongPosMs); } catch { }
                CleanupHoldState(note);
                ReturnPayload(payload);
                return;
            }

            if (payload.isHoldHead)
            {
                if (payload.triggerMesh) ShowNoteMeshEffect(note, payload.result, payload.usePersistentMesh);
                if (payload.triggerHitSound) { try { handler?.PlayHeadSound(note, payload.inputSongPosMs, payload.result); } catch { } }
                if (payload.triggerPopup) { ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result); }
                TryIncrementStats(payload.result);
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
                ReturnPayload(payload);
                return;
            }

            if (payload.triggerMesh) ShowNoteMeshEffect(note, payload.result);
            if (payload.triggerHitSound) { try { handler?.PlayHeadSound(note, payload.inputSongPosMs, payload.result); } catch { } }
            if (payload.triggerPopup) { ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result); }
            TryIncrementStats(payload.result);
            RecordTimingOffsetIfNeeded(payload);
            if (_cachedDebugMode) { try { if (payload.isHoldExtra) { BuildLogger.Log($"[JMgr] ApplyJudgmentPayload: isHoldExtra for note={note?.name ?? "<null>"} res={payload.result} songPos={payload.inputSongPosMs}"); } } catch { } }
            // If this payload is an extra per-hold judgment, do not mark the note as judged/released.
            if (!payload.isHoldExtra)
            {
                try { note.OnJudged(payload.result); } catch { }
                // note is now IsJudged=true (IsActive=false). All iteration consumers already
                // check IsActive/IsJudged, so the note is naturally filtered out.
                // Do NOT call nearbyNotesScratch.Remove(note) here — we may be inside a
                // foreach over that list, and List.Remove during enumeration throws
                // InvalidOperationException, which is extremely expensive in Unity/Mono
                // (full stack-trace capture). With 16-note chords this caused ~32 exceptions/frame.
            }
            ReturnPayload(payload);
        }

        // --- Core judgement application ---
        private float GetSongPositionMs()
        {
            try { return GameManager.Instance?.Conductor?.effectiveSongPosition ?? 0f; } catch { return 0f; }
        }

        private float GetProtectionMs()
        {
            // Use 'goodMs' as base protection duration (ms) to avoid double-judging nearby press events
            return (float)goodMs + 10f;
        }

        private void ApplyJudgmentForHoldStart(NoteController note)
        {
            if (note == null) return;
            try
            {
                // play sound once for head
                try { HitSoundManager.Instance?.PlayHitSound(); } catch { }
                // effect and popup
                // Show a persistent note judgement mesh to simulate the head being held
                try { ShowNoteMeshEffect(note, JudgmentResult.Perfect, true); } catch { }
                try { SimpleJudgePopupManager.Instance?.ShowAtPosition(note.transform.position, JudgmentResult.Perfect); } catch { }
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
        }

        public void ApplyJudgment(NoteController note, JudgmentResult result, float accuracyMs, float protectionMs)
        {
            if (note == null) return;
            try
            {
                // record timing offset if available
                try { StatsManager.Instance?.RecordTimingOffset(accuracyMs); } catch { }
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
            float songPos = GetSongPositionMs();
            // Cache SettingsManager.DebugMode once — avoids per-hold-state singleton lookup each frame.
            try { _cachedDebugMode = SettingsManager.Instance != null && SettingsManager.Instance.DebugMode; } catch { _cachedDebugMode = false; }
            try { ProcessPendingJudgments(songPos); } catch { }
            try { ProcessHoldExtraAwards(songPos); } catch { }
            try { FinalizeExpiredHolds(songPos); } catch { }
            // Flush HUD update once per frame (dirty flag set by TryIncrementStats).
            if (_hudDirty) { _hudDirty = false; try { RefreshHudStats(); } catch { } }
        }

        // Award extra Perfect judgments for active holds while the player keeps the lane pressed.
        // This runs each frame and awards when an eighth-note interval has elapsed since the
        // last award (or since press start for the first award), capped by state.maxExtra.
        private void ProcessHoldExtraAwards(float songPos)
        {
            try
            {
                if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] ProcessHoldExtraAwards activeHoldStates={activeHoldStates.Count} songPos={songPos}"); } catch { } }

                // Periodically poll hold-like notes that are NOT yet being tracked and
                // start tracking them when the player is physically holding the lane(s).
                // Throttled: scan at most once per eighth-note interval (= one hold-extra combo
                // opportunity) rather than every frame, so cost scales with BPM not framerate.
                try
                {
                    if (songPos - _lastUntrackedHoldCheckMs >= _untrackedHoldCheckIntervalMs)
                    {
                    // Refresh interval from live BPM so it adapts to tempo changes.
                    try
                    {
                        float bpmNow = 120f;
                        if (GameManager.Instance != null && GameManager.Instance.Conductor != null)
                            bpmNow = GameManager.Instance.Conductor.bpm;
                        // eighth-note in ms = (60000 / BPM) / 2 = 30000 / BPM
                        float eighth = bpmNow > 0f ? 30000f / bpmNow : 250f;
                        // Clamp to a reasonable range so extremely fast/slow BPM can't starve or flood the scan.
                        _untrackedHoldCheckIntervalMs = Mathf.Clamp(eighth, 50f, 500f);
                    }
                    catch { _untrackedHoldCheckIntervalMs = 250f; }
                    _lastUntrackedHoldCheckMs = songPos;
                    foreach (var n in activeNotes)
                    {
                        if (n == null) continue;
                        if (!HoldJudgment.IsHoldLikeNote(n)) continue;
                        if (activeHoldStates.ContainsKey(n)) continue; // already tracked
                        var nd = n.NoteData; if (nd == null) continue;
                        // Only consider notes within their hold window (allow grace after end)
                        if (songPos < nd.startTime || songPos > nd.endTime + goodMs) continue;
                        // Interval already enforced by outer BPM throttle (_untrackedHoldCheckIntervalMs);
                        // no per-note timing re-check needed here.

                        // Check physical input for any lane of this hold
                        bool hasPressed = false;
                        int chosenLane = nd.startLane;
                        try
                        {
                            var keyAdapter = KeyboardInputAdapter.Instance;
                            if (keyAdapter != null)
                            {
                                int startL = nd.startLane;
                                int endL = nd.endLane >= startL ? nd.endLane : startL;
                                for (int l = startL; l <= endL; l++)
                                {
                                    try { if (keyAdapter.IsLanePressed(l)) { hasPressed = true; chosenLane = l; break; } } catch { }
                                }
                            }
                        }
                        catch { }

                        if (hasPressed)
                        {
                            if (_cachedDebugMode) BuildLogger.Log($"[JMgr] UntrackedHoldCheck starting hold note={n?.name ?? "<null>"} lane={chosenLane} songPos={songPos}");
                            try { BeginHoldTracking(n, chosenLane, songPos); } catch { }
                        }
                    }
                    } // end throttle
                }
                catch { }
                foreach (var kv in activeHoldStates)
                {
                    var st = kv.Value;
                    if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] ActiveHoldState note={st?.note?.name ?? "<null>"} press={st?.pressSongPos} pressedLanes={st?.pressedLanes?.Count ?? 0} awarded={st?.awardedExtra}/{st?.maxExtra}"); } catch { } }
                    if (st == null) continue;

                    if (st.maxExtra <= 0)
                    {
                        if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Skip extra: maxExtra=0 for note={st.note?.name ?? "<null>"}"); } catch { } }
                        continue;
                    }

                    if (st.awardedExtra >= st.maxExtra)
                    {
                        if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Skip extra: already awarded max for note={st.note?.name ?? "<null>"}"); } catch { } }
                        continue;
                    }

                    // In normal (non-debug) mode, we always award per-beat: pressed => JUST, not pressed => MISS
                    // In DebugMode, retain legacy behavior (only award when pressed)
                    bool debugModeActive = _cachedDebugMode;
                    bool hasPressed = st.pressedLanes != null && st.pressedLanes.Count > 0;
                    // If no pressed lanes tracked on the state, consult the global input adapter
                    // so players who press after a missed head still count for per-beat awards.
                    try
                    {
                        if (!hasPressed)
                        {
                            var keyAdapter = KeyboardInputAdapter.Instance;
                            if (keyAdapter != null)
                            {
                                var ndCheck2 = st.note?.NoteData;
                                if (ndCheck2 != null)
                                {
                                    int startL = ndCheck2.startLane;
                                    int endL = ndCheck2.endLane >= startL ? ndCheck2.endLane : startL;
                                    for (int l = startL; l <= endL; l++)
                                    {
                                        try { if (keyAdapter.IsLanePressed(l)) { hasPressed = true; break; } } catch { }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    if (debugModeActive && !hasPressed)
                    {
                        // DebugMode: skip awarding when not pressed
                        if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Skip extra (debug): no pressed lanes for note={st.note?.name ?? "<null>"}"); } catch { } }
                        continue;
                    }

                    // Do not award beyond the note's end time
                    try
                    {
                        var ndCheck = st.note?.NoteData;
                        if (ndCheck != null && songPos >= ndCheck.endTime)
                        {
                            // let FinalizeExpiredHolds handle tail
                            continue;
                        }
                    }
                    catch { }

                    float eighth = st.eighthMs > 0f ? st.eighthMs : 0f;
                    if (eighth <= 0f)
                    {
                        try { if (GameManager.Instance != null && GameManager.Instance.Conductor != null) eighth = Mathf.Max(1f, 30000f / Mathf.Max(0.0001f, GameManager.Instance.Conductor.bpm)); } catch { eighth = 500f; }
                    }

                    float last = st.lastExtraAwardSongPosMs;
                    if (last == float.MinValue) last = st.pressSongPos;

                    float needed = eighth * 0.9f - (songPos - last);
                    if (needed > 0f)
                    {
                        if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Skip extra: wait {needed:F1}ms more for note={st.note?.name ?? "<null>"}"); } catch { } }
                        continue;
                    }

                    // award one judgment tick: choose lane from pressedLanes if available, otherwise fallback to note's startLane
                    int lane = -1;
                    if (hasPressed)
                    {
                        foreach (var l in st.pressedLanes) { lane = l; break; }
                    }
                    if (lane < 0)
                    {
                        try { var nd = st.note?.NoteData; if (nd != null) lane = nd.startLane; } catch { }
                    }
                    // Determine result based on press state in non-debug; always Perfect in debug
                    var tickResult = (hasPressed || debugModeActive) ? JudgmentResult.Perfect : JudgmentResult.Miss;
                    var payload = RentPayload();
                    payload.note = st.note;
                    payload.result = tickResult;
                    payload.buttonId = lane;
                    payload.inputSongPosMs = songPos;
                    payload.pressSongPosMs = songPos;
                    payload.releaseSongPosMs = 0f;
                    payload.targetTimeMs = songPos;
                    payload.absoluteOffsetMs = 0f;
                    payload.triggerPopup = true;
                    payload.triggerMesh = (tickResult == JudgmentResult.Perfect || tickResult == JudgmentResult.Great || tickResult == JudgmentResult.Good);
                    payload.triggerHitSound = false;
                    payload.isHoldExtra = true;
                    payload.isHoldHead = false;
                    payload.isHoldTail = false;
                    payload.usePersistentMesh = false;
                    payload.hasHeadTiming = false;
                    try { ApplyJudgmentPayload(payload); } catch { }
                    st.awardedExtra++;
                    // Track how many of the awarded extras were actually during a press
                    try { if (tickResult != JudgmentResult.Miss) st.pressedAwardedExtra++; } catch { }
                    st.lastExtraAwardSongPosMs = songPos;
                    if (_cachedDebugMode) BuildLogger.Log($"[JMgr] AwardTick note={st.note?.name ?? "<null>"} tickResult={tickResult} awarded={st.awardedExtra}/{st.maxExtra} pressedCount={st.pressedAwardedExtra}");
                    // If we've consumed the majority of extra awards, hide the persistent head
                    // visual (head disappears) because no significant awards remain. Allow
                    // re-press to re-show the head (ApplyJudgmentForHoldStart will restore it).
                    try
                    {
                        if (st.maxExtra > 0)
                        {
                            int threshold = Mathf.CeilToInt(st.maxExtra * 0.8f);
                            if (st.awardedExtra >= threshold)
                            {
                                try { HideNoteMeshEffect(st.note); } catch { }
                            }
                        }
                    }
                    catch { }
                    if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Awarded hold tick for note={st.note?.name ?? "<null>"} res={tickResult} awarded={st.awardedExtra}/{st.maxExtra} songPos={songPos}"); } catch { } }
                }
            }
            catch { }
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
                    bool debugModeActive = _cachedDebugMode;
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
                            JudgmentResult fillResult = JudgmentResult.Miss;
                            if (pct >= 60f) fillResult = JudgmentResult.Great;
                            else if (pct >= 30f) fillResult = JudgmentResult.Good;
                            if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] Finalize fill extras note={note?.name ?? "<null>"} pressed={stRem.pressedAwardedExtra} max={stRem.maxExtra} pct={pct:F1} fill={fillResult}"); } catch { } }

                            for (int mi = 0; mi < missing; mi++)
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
                                JudgmentResult fillResult2 = JudgmentResult.Miss;
                                if (pct2 >= 60f) fillResult2 = JudgmentResult.Great;
                                else if (pct2 >= 30f) fillResult2 = JudgmentResult.Good;
                                if (_cachedDebugMode) { try { BuildLogger.Log($"[JMgr] PendingSwap finalize fill extras note={note?.name ?? "<null>"} pressed={st.pressedAwardedExtra} max={st.maxExtra} pct={pct2:F1} fill={fillResult2}"); } catch { } }

                                for (int mi2 = 0; mi2 < missing2; mi2++)
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
            try { untrackedHoldLastCheck.Remove(note); } catch { }
            _handlerCache.Remove(note);
            Protection.RemoveProtection(note);
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

        public (int perfect, int great, int good, int miss, int fail) GetStats()
        {
            try { return StatsManager.Instance != null ? StatsManager.Instance.GetStats() : (0,0,0,0,0); } catch { return (0,0,0,0,0); }
        }

        public NoteController FindBestNote(int buttonId, float songPos, int goodMs)
        {
            // Priority algorithm extracted into NoteSelector; pass the nearby-notes window.
            return NoteSelector.FindBestNote(buttonId, songPos, goodMs, GetNearbyNotes(songPos));
        }

        // Protection & pending handling migrated into JudgmentProtection

        // 依歌曲位置取得「時間窗內」的可判定音符，避免頻繁全掃 activeNotes。
        private System.Collections.Generic.IEnumerable<NoteController> GetNearbyNotes(float songPos)
        {
            // 若與上次 songPos 差異極小，直接重用 cache
            if (nearbyNotesCachedSongPos > float.MinValue && Mathf.Abs(songPos - nearbyNotesCachedSongPos) < NearbyCacheEpsMs)
            {
                return nearbyNotesScratch;
            }

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
                if (t < minTime || t > maxTime) continue;
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
            try { HitParticleManager.Instance?.StopHoldEmission(note); } catch { }
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
