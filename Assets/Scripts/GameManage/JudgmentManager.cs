using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Duplicate enum — disable legacy copy to avoid CS0101 (keep for reference)
#if false
public enum JudgmentResult { Perfect, Great, Good, Miss, Fail }
#endif

// 此檔案已重構，所有判定相關功能請改用 Judgment 資料夾下的類別。
// 將舊版檔案包在條件編譯中以避免與重構後檔案衝突並解決編譯錯誤。
#if false
    private void FinalizeHold(NoteController note, float releaseSongPos, float pressSongPos)
    {
        if (note == null) return;
        var nd = note.NoteData;
        if (nd == null) return;

        ActiveHoldState state = null;
        if (activeHoldStates.TryGetValue(note, out var retrievedState) && retrievedState != null)
        {
            state = retrievedState;
            state.pendingFinalizeCoroutine = null;
        }

        HideNoteMeshEffect(note);

        // Find one of the original button IDs for popup purposes. This is imperfect but sufficient.
        int anyButtonId = -1;
        foreach (var kvp in laneToHoldState)
        {
            if (kvp.Value.note == note)
            {
                anyButtonId = kvp.Key;
                break;
            }
        }
        if (anyButtonId == -1) anyButtonId = nd.startLane; // fallback

        try
        {
            var handler = EnsureHandler(note);
            JudgmentResult finalRes;
            if (note.IsStaccato)
            {
                float rel = releaseSongPos - (nd != null ? nd.startTime : releaseSongPos);
                if (rel < 0f) rel = 0f;
                if (rel <= stacPerfectMs) finalRes = JudgmentResult.Perfect;
                else if (rel <= stacGreatMs) finalRes = JudgmentResult.Great;
                else if (rel <= stacGoodMs) finalRes = JudgmentResult.Good;
                else finalRes = JudgmentResult.Miss;
            }
            else
            {
                finalRes = handler.EvaluateHoldEnd(note, releaseSongPos, pressSongPos);
            }
            float headInput = state != null && state.hasHeadTiming ? state.headInputSongPosMs : pressSongPos;
            float headTarget = state != null && state.hasHeadTiming ? state.headTargetTimeMs : (nd != null ? nd.startTime : pressSongPos);
            float headOffset = state != null && state.hasHeadTiming ? state.headAbsoluteOffsetMs : Mathf.Abs(headInput - headTarget);
            bool hasHeadTiming = state != null && state.hasHeadTiming;

            var payload = new JudgmentPayload
            {
                note = note,
                result = finalRes,
                buttonId = anyButtonId,
                inputSongPosMs = headInput,
                pressSongPosMs = pressSongPos,
                releaseSongPosMs = releaseSongPos,
                targetTimeMs = headTarget,
                absoluteOffsetMs = headOffset,
                triggerMesh = false,
                triggerPopup = true,
                triggerHitSound = false,
                isHoldHead = false,
                isHoldTail = true,
                usePersistentMesh = false,
                headInputSongPosMs = headInput,
                headTargetTimeMs = headTarget,
                headAbsoluteOffsetMs = headOffset,
                hasHeadTiming = hasHeadTiming
            };

            ResolveTailJudgment(note, payload, releaseSongPos, allowProtection: !note.IsStaccato);
        }
        catch { }
    }

    private void HandleStaccatoTimeout(NoteController note, ActiveHoldState state)
    {
        if (note == null) return;

        HideNoteMeshEffect(note);
        var particleManager = HitParticleManager.Instance;
        if (particleManager != null)
        {
            particleManager.StopHoldEmission(note);
        }

        const JudgmentResult timeoutResult = JudgmentResult.Miss;

        int buttonId = -1;
        try
        {
            if (state != null)
            {
                foreach (var lane in state.pressedLanes)
                {
                    buttonId = lane;
                    break;
                }
            }
            if (buttonId < 0 && note.NoteData != null)
            {
                buttonId = note.NoteData.startLane;
            }
        }
        catch { buttonId = -1; }

        var nd = note.NoteData;
        float songPos = GetPreciseSongPositionMs();
        float headInput = state != null && state.hasHeadTiming ? state.headInputSongPosMs : (state != null ? state.pressSongPos : (nd != null ? nd.startTime : songPos));
        float headTarget = state != null && state.hasHeadTiming ? state.headTargetTimeMs : (nd != null ? nd.startTime : songPos);
        float headOffset = state != null && state.hasHeadTiming ? state.headAbsoluteOffsetMs : Mathf.Abs(headInput - headTarget);
        bool hasHeadTiming = state != null && state.hasHeadTiming;

        var payload = new JudgmentPayload
        {
            note = note,
            result = timeoutResult,
            buttonId = buttonId >= 0 ? buttonId : (nd != null ? nd.startLane : 0),
            inputSongPosMs = headInput,
            pressSongPosMs = state != null ? state.pressSongPos : (nd != null ? nd.startTime : songPos),
            releaseSongPosMs = songPos,
            targetTimeMs = headTarget,
            absoluteOffsetMs = headOffset,
            triggerPopup = true,
            triggerMesh = false,
            triggerHitSound = false,
            isHoldHead = false,
            isHoldTail = true,
            usePersistentMesh = false,
            headInputSongPosMs = headInput,
            headTargetTimeMs = headTarget,
            headAbsoluteOffsetMs = headOffset,
            hasHeadTiming = hasHeadTiming
        };

        ResolveTailJudgment(note, payload, songPos, allowProtection: false);
    }

    private void CleanupHoldState(NoteController note)
    {
        if (note == null) return;
        pendingHeadJudgments.Remove(note);
        pendingTailJudgments.Remove(note);
        HideNoteMeshEffect(note);
        var particleManager = HitParticleManager.Instance;
        if (particleManager != null)
        {
            particleManager.StopHoldEmission(note);
        }
        if (activeHoldStates.TryGetValue(note, out var state))
        {
            if (state.pendingFinalizeCoroutine != null)
            {
                StopCoroutine(state.pendingFinalizeCoroutine);
            }

            var lanesToRemove = new List<int>();
            foreach (var lane in state.pressedLanes)
            {
                lanesToRemove.Add(lane);
            }
            foreach (var lane in lanesToRemove)
            {
                try { KeyHitEffectManager.Instance.HidePersistentMeshForKeyId(lane); } catch { }
                laneToHoldState.Remove(lane);
            }
            activeHoldStates.Remove(note);
        }
    }

    private void ShowJudgementPopups(int bottonId, Vector3 notePosition, JudgmentResult res)
    {
        try
        {
            var popup = JudgePopupManager.Instance;
            // If the new particle-based popup manager exists and has its enable flag OFF,
            // treat that as a global disable for judge popups (legacy SimpleJudgePopupManager
            // also won't be shown). This lets the Inspector toggle control all popup types.
            if (popup != null && !popup.enablePopup)
            {
                return;
            }
            if (popup != null)
            {
                var keyRect = VirtualKeyButton.GetRectForId(bottonId);
                if (keyRect != null) popup.ShowPopupAtRectTransform(keyRect, res);
            }
        }
        catch { }
        try
        {
            var simple = SimpleJudgePopupManager.Instance;
            if (simple != null)
            {
                // If a particle-based manager exists and disabled popups globally, we already returned above.
                simple.ShowAtPosition(notePosition, res);
            }
        }
        catch { }
    }

    private void ShowNoteMeshEffect(NoteController note, JudgmentResult res, bool persistent = false)
    {
        if (note == null) return;
        if (res != JudgmentResult.Perfect && res != JudgmentResult.Great && res != JudgmentResult.Good) return;

        // The column carries the pedal itself now: struck under the pedal it stretches
        // out instead of returning to the line. See NoteJudgementMeshManager.
        try
        {
            NoteJudgementMeshManager.EnsureCreated().ShowEffect(note, res, persistent);
        }
        catch { }

        try
        {
            var nd = note.NoteData;
            int laneId = nd != null ? nd.startLane : -1;
            HitEffectRouter.Play(note, laneId, res);
        }
        catch { }
    }

    private void HideNoteMeshEffect(NoteController note)
    {
        if (note == null) return;
        try
        {
            NoteJudgementMeshManager.EnsureCreated().HidePersistentEffect(note);
        }
        catch { }
    }

    public void RegisterNote(NoteController n)
    {
        if (n == null) return;
        activeNotes.Add(n);
    // no runtime logging
        try
        {
            var h = NoteHandlerFactory.Create(n);
            if (h != null) noteHandlers[n] = h;
        }
        catch { }
    }

    public void UnregisterNote(NoteController n)
    {
        if (n == null) return;
        activeNotes.Remove(n);
    // no runtime logging
        try { noteHandlers.Remove(n); } catch { }
    }

    // Called when a button is pressed; find the closest overlapping note on the judgment line for this id
    public void ProcessButtonPress(int bottonId)
    {
        float songPos = GetPreciseSongPositionMs();

        // If judgments are currently disabled (e.g., during visual pre-roll), do not perform
        // any judgement. Still show key-hit feedback so the player sees their input.
        if (!CanJudgeNow())
        {
            TryShowTransientMesh(bottonId);
            try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(bottonId); } catch { }
            return;
        }

        TryShowTransientMesh(bottonId);
        var best = Judgment.JudgmentManager.Instance.FindBestNote(bottonId, songPos, goodMs);

        if (best != null)
        {
            bool isHoldNoteHead = IsHoldLikeNote(best);
            if (isHoldNoteHead)
            {
                float pressSongPos = Mathf.Max(songPos, (best.NoteData != null ? best.NoteData.startTime : songPos));
                JudgmentResult res = Judgment.JudgmentResult.Miss;
                if (best.IsStaccato)
                {
                    res = Judgment.StaccatoJudgment.Judge(best, pressSongPos, stacPerfectMs, stacGreatMs, stacGoodMs);
                }
                else
                {
                    res = Judgment.HoldJudgment.JudgeHead(best, pressSongPos, perfectMs, greatMs, goodMs);
                }
                // 分數計算
                int score = Judgment.ScoreManager.GetScore(res, 1, 1f); // 這裡 combo/acc 可依需求傳入
                // 特效
                Judgment.EffectManager.PlayEffect(best, res);
                // 其他原本的 BeginHoldTracking 可根據需求保留或簡化
                // ...existing code...
            }
            else
            {
                float preciseSongPos = songPos;
                JudgmentResult res = Judgment.TapJudgment.Judge(best, preciseSongPos, perfectMs, greatMs, goodMs);
                int score = Judgment.ScoreManager.GetScore(res, 1, 1f);
                Judgment.EffectManager.PlayEffect(best, res);
                // ...existing code...
            }
        }
        else
        {
            // no note matched -> still show persistent key-hit feedback so player sees their press
            TryShowTransientMesh(bottonId);
            try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(bottonId); } catch { }
        }
    }

    // Convenience API: accept a key name (e.g. "Q" or "7") and resolve to a bottonId using the mapping,
    // then call ProcessButtonPress. This is intended for virtual keyboards or UI buttons that only
    // know the character they represent.
    public void ProcessButtonPressByName(string bottonName)
    {
        if (string.IsNullOrEmpty(bottonName)) return;
        if (mapping == null) mapping = new ButtonMapping();
        if (!mapping.LoadFromResources("botton_setting"))
        {
            #if UNITY_EDITOR
            Debug.LogWarningFormat("[JudgmentManager] ProcessButtonPressByName: failed to load mapping to resolve name={0}", bottonName);
            #endif
            return;
        }
        else
        {
            BuildCachedBindings();
        }
        if (mapping.TryGetIdForName(bottonName, out int id))
        {
            ProcessButtonPress(id);
        }
        else
        {
            #if UNITY_EDITOR
            Debug.LogWarningFormat("[JudgmentManager] ProcessButtonPressByName: no mapping id for name={0}", bottonName);
            #endif
        }
    }

    public void RecordFail(NoteController n)
    {
        if (n == null) return;
        if (n == null) return;
        // Delegate fail handling to the per-note handler so stats are owned by handlers.
        try
        {
            var handler = EnsureHandler(n);
            var res = handler.EvaluateUnhandled(n, GetPreciseSongPositionMs(), false);
            HideNoteMeshEffect(n);
            try { n.OnJudged(res); } catch { }
            try { var simple = SimpleJudgePopupManager.Instance; if (simple != null) simple.ShowAtPosition(n.transform.position, res); } catch { }
            try { var popup = JudgePopupManager.Instance; if (popup != null && popup.enablePopup) popup.ShowPopupAtWorldPosition(n.transform.position, res); } catch { }
            return;
        }
        catch
        {
            // Fallback: mark miss without touching manager counters if handler fails
            HideNoteMeshEffect(n);
            try { n.OnJudged(JudgmentResult.Miss); } catch { }
            try { var simple = SimpleJudgePopupManager.Instance; if (simple != null) simple.ShowAtPosition(n.transform.position, JudgmentResult.Miss); } catch { }
            try { var popup = JudgePopupManager.Instance; if (popup != null && popup.enablePopup) popup.ShowPopupAtWorldPosition(n.transform.position, JudgmentResult.Miss); } catch { }
            return;
        }
    }

    private float GetPreciseSongPositionMs()
    {
        var gm = GameManager.Instance;
        if (gm != null && gm.Conductor != null && gm.Conductor != cachedConductor)
        {
            cachedConductor = gm.Conductor;
        }

        var conductor = GetCachedConductor();
        if (conductor != null)
        {
            float dspMs = conductor.GetDspSongPositionMs();
            // Apply user-configurable judgment offset (ms). Positive = treat hits as earlier, Negative = treat as later.
            float offset = 0f;
            try { offset = SettingsManager.Instance != null ? SettingsManager.Instance.JudgmentOffsetMs : 0f; } catch { offset = 0f; }
            return dspMs + offset;
        }
        cachedConductor = null;
        return 0f;
    }
    

    public void ResetStats()
    {
        // delegate to StatsManager to own/reset stats
        try { StatsManager.Instance.ResetStats(); } catch { }
        perfectCount = 0; greatCount = 0; goodCount = 0; missCount = 0; failCount = 0;
        RefreshComboLabel(true);
    }

    public (int perfect, int great, int good, int miss, int fail) GetStats()
    {
        try { return StatsManager.Instance.GetStats(); } catch { return (perfectCount, greatCount, goodCount, missCount, failCount); }
    }

    public (int perfect, int great, int good, int miss, int fail, int combo, int maxCombo, int score) GetStatsWithCombo()
    {
        try { return StatsManager.Instance.GetStatsWithCombo(); }
        catch { return (perfectCount, greatCount, goodCount, missCount, failCount, 0, 0, 0); }
    }

    public (int fast, int late) GetTimingStats()
    {
        try { return StatsManager.Instance.GetTimingStats(); }
        catch { return (0, 0); }
    }

    // Ensure outstanding holds and unjudged notes resolve so final stats include every judgment.
    public void FlushPendingJudgments()
    {
        float songPos = GetPreciseSongPositionMs();

        pendingHeadFinalizeScratch.Clear();
        foreach (var kv in pendingHeadJudgments)
        {
            pendingHeadFinalizeScratch.Add(kv.Key);
        }
        for (int i = 0; i < pendingHeadFinalizeScratch.Count; i++)
        {
            var note = pendingHeadFinalizeScratch[i];
            if (note == null) continue;
            if (!pendingHeadJudgments.TryGetValue(note, out var entry)) continue;
            ApplyJudgmentPayload(entry.payload);
            pendingHeadJudgments.Remove(note);
        }
        pendingHeadFinalizeScratch.Clear();

        pendingTailFinalizeScratch.Clear();
        foreach (var kv in pendingTailJudgments)
        {
            pendingTailFinalizeScratch.Add(kv.Key);
        }
        for (int i = 0; i < pendingTailFinalizeScratch.Count; i++)
        {
            var note = pendingTailFinalizeScratch[i];
            if (note == null) continue;
            if (!pendingTailJudgments.TryGetValue(note, out var entry)) continue;
            ApplyJudgmentPayload(entry.payload);
            pendingTailJudgments.Remove(note);
        }
        pendingTailFinalizeScratch.Clear();

        toFinalizeScratch.Clear();
        foreach (var kv in activeHoldStates)
        {
            var note = kv.Key;
            var state = kv.Value;
            if (note == null) continue;
            toFinalizeScratch.Add(note);
            try
            {
                var handler = EnsureHandler(note);
                var nd = note.NoteData;
                if (nd == null) continue;
                float releaseTime = nd.endTime;
                JudgmentResult res;
                if (note.IsStaccato)
                {
                    float rel = releaseTime - nd.startTime;
                    if (rel < 0f) rel = 0f;
                    if (rel <= stacPerfectMs) res = JudgmentResult.Perfect;
                    else if (rel <= stacGreatMs) res = JudgmentResult.Great;
                    else if (rel <= stacGoodMs) res = JudgmentResult.Good;
                    else res = JudgmentResult.Miss;
                }
                else
                {
                    res = handler.EvaluateHoldEnd(note, releaseTime, state.pressSongPos);
                }
                float headInput = state.hasHeadTiming ? state.headInputSongPosMs : state.pressSongPos;
                float headTarget = state.hasHeadTiming ? state.headTargetTimeMs : (nd != null ? nd.startTime : state.pressSongPos);
                float headOffset = state.hasHeadTiming ? state.headAbsoluteOffsetMs : Mathf.Abs(headInput - headTarget);
                var payload = new JudgmentPayload
                {
                    note = note,
                    result = res,
                    buttonId = nd.startLane,
                    inputSongPosMs = headInput,
                    pressSongPosMs = state.pressSongPos,
                    releaseSongPosMs = releaseTime,
                    targetTimeMs = headTarget,
                    absoluteOffsetMs = headOffset,
                    triggerPopup = false,
                    triggerMesh = false,
                    triggerHitSound = false,
                    isHoldHead = false,
                    isHoldTail = true,
                    usePersistentMesh = false,
                    headInputSongPosMs = headInput,
                    headTargetTimeMs = headTarget,
                    headAbsoluteOffsetMs = headOffset,
                    hasHeadTiming = state.hasHeadTiming
                };
                ApplyJudgmentPayload(payload);
            }
            catch { }
        }
        for (int i = 0; i < toFinalizeScratch.Count; i++)
        {
            CleanupHoldState(toFinalizeScratch[i]);
        }

        toAutoFinalizeScratch.Clear();
        foreach (var kv in autoHeldNotes)
        {
            var note = kv.Key;
            if (note == null) continue;
            toAutoFinalizeScratch.Add(note);
            try
            {
                var handler = EnsureHandler(note);
                float releaseTime = (note.NoteData != null) ? note.NoteData.endTime : songPos;
                var res = handler.EvaluateHoldEnd(note, releaseTime, kv.Value);
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
                autoHeldNotes.Remove(note);
            }
        }
    }

    private void HandleHeldKey(int bottonId, bool pressedThisFrame)
    {
        if (pressedThisFrame)
        {
            return;
        }

        if (laneToHoldState.ContainsKey(bottonId))
        {
            return;
        }

        // Check if this lane belongs to an already active hold and needs to be registered
        foreach (var kv in activeHoldStates)
        {
            var note = kv.Key;
            var state = kv.Value;
            var nd = note?.NoteData;
            if (nd == null) continue;
            if (bottonId < nd.startLane || bottonId > nd.endLane) continue;

            if (!state.pressedLanes.Contains(bottonId))
            {
                state.pressedLanes.Add(bottonId);
                laneToHoldState[bottonId] = state;
                try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(bottonId); } catch { }
            }
            return;
        }

        float songPos = GetPreciseSongPositionMs();
        NoteController best = null;
        float bestDelta = float.MaxValue;

        foreach (var n in activeNotes)
        {
            if (n == null || !n.IsJudgeable) continue;
            var nd = n.NoteData;
            if (nd == null) continue;
            if (!IsHoldLikeNote(n)) continue;
            if (activeHoldStates.ContainsKey(n)) continue;
            if (bottonId < nd.startLane || bottonId > nd.endLane) continue;
            if (songPos < nd.startTime) continue;

            float delta = Mathf.Abs(nd.startTime - songPos);
            if (delta <= goodMs && delta < bestDelta)
            {
                bestDelta = delta;
                best = n;
            }
        }

        if (best != null)
        {
            var handler = EnsureHandler(best);
            float pressSongPos = Mathf.Max(songPos, (best.NoteData != null ? best.NoteData.startTime : songPos));
            JudgmentResult? forcedResult = best.IsSoft ? JudgmentResult.Perfect : (JudgmentResult?)null;
            BeginHoldTracking(best, handler, bottonId, pressSongPos, bestDelta, forcedResult);
        }
    }

    private void BeginHoldTracking(NoteController note, INoteHandler handler, int buttonId, float pressSongPos, float deltaMs, JudgmentResult? forcedResult)
    {
        if (note == null || handler == null) return;

        float startTime = (note.NoteData != null) ? note.NoteData.startTime : pressSongPos;
        pressSongPos = Mathf.Max(pressSongPos, startTime);

        HoldNoteHandler.HoldBeginInfo beginInfo = default;
        if (!activeHoldStates.TryGetValue(note, out var state) || state == null)
        {
            if (handler is HoldNoteHandler holdHandler)
            {
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
                    catch { headResult = EvaluateHoldHeadResult(Mathf.Abs(deltaMs)); }
                }
                try { handler.PlayHeadSound(note, pressSongPos, headResult); } catch { }
                beginInfo = new HoldNoteHandler.HoldBeginInfo(headResult, headResult != JudgmentResult.Miss);
            }

            note.OnHoldStart(beginInfo.Result);

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
            activeHoldStates[note] = state;

            ShowNoteMeshEffect(note, beginInfo.Result, beginInfo.UsePersistentMesh);
            try { handler.OnBeginHold(note, buttonId, pressSongPos, deltaMs, beginInfo.Result); } catch { }
            var nd = note.NoteData;
            float targetTime = nd != null ? nd.startTime : pressSongPos;
            float absoluteOffset = Mathf.Abs(pressSongPos - targetTime);
            var payload = new JudgmentPayload
            {
                note = note,
                result = beginInfo.Result,
                buttonId = buttonId,
                inputSongPosMs = pressSongPos,
                pressSongPosMs = pressSongPos,
                targetTimeMs = targetTime,
                absoluteOffsetMs = absoluteOffset,
                usePersistentMesh = beginInfo.UsePersistentMesh,
                isHoldHead = true,
                isHoldTail = false,
                triggerMesh = false,
                triggerPopup = true,
                triggerHitSound = false
            };

            ResolveHeadJudgment(note, payload, pressSongPos, allowProtection: true);
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
        var particleManager = HitParticleManager.Instance;
        if (particleManager != null)
        {
            particleManager.BeginHoldEmission(note, buttonId);
        }
        try { KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(buttonId); } catch { }
    }

    private JudgmentResult EvaluateHoldHeadResult(float delta)
    {
        // Pure mapping from delta -> judgment result without modifying any statistics.
        if (SettingsManager.Instance != null && SettingsManager.Instance.DebugMode)
        {
            return JudgmentResult.Perfect;
        }

        if (delta <= perfectMs)
        {
            return JudgmentResult.Perfect;
        }

        if (delta <= greatMs)
        {
            return JudgmentResult.Great;
        }

        return JudgmentResult.Good;
    }

    private static int GetAnyPressedLane(ActiveHoldState state)
    {
        if (state == null || state.pressedLanes == null) return -1;
        foreach (var lane in state.pressedLanes)
        {
            return lane;
        }
        return -1;
    }

    private static bool IsHoldLikeNote(NoteController note)
    {
        if (note == null) return false;

        if (note.IsStaccato)
        {
            return true;
        }

        try
        {
            var nd = note.NoteData;
            if (nd == null) return false;

            if (!string.IsNullOrEmpty(nd.type))
            {
                if (string.Equals(nd.type, "hold", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(nd.type, "staccato", StringComparison.OrdinalIgnoreCase) || string.Equals(nd.type, "stac", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (int.TryParse(nd.type, out int parsedType) && (parsedType == 2 || parsedType == 3))
                {
                    return true;
                }
            }

            if (nd.note_type == 2 || nd.note_type == 3)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    private bool IsNoteAwaitingDeferredJudgment(NoteController note)
    {
        if (note == null)
        {
            return false;
        }

        if (pendingHeadJudgments.ContainsKey(note))
        {
            return true;
        }

        if (pendingTailJudgments.ContainsKey(note))
        {
            return true;
        }

        if (activeHoldStates.ContainsKey(note))
        {
            return true;
        }

        return false;
    }

    private static bool LanesOverlap(NoteController a, NoteController b)
    {
        if (a == null || b == null) return false;
        var ad = a.NoteData;
        var bd = b.NoteData;
        if (ad == null || bd == null) return false;
        return ad.endLane >= bd.startLane && ad.startLane <= bd.endLane;
    }

    private bool TryComputeHeadProtection(NoteController note, float currentSongPos, out float protectionEndMs)
    {
        protectionEndMs = 0f;
        var nd = note?.NoteData;
        if (nd == null) return false;

        float noteStart = nd.startTime;
        float windowStart = noteStart - goodMs;
        float bestGreen = 0f;

        foreach (var other in activeNotes)
        {
            if (other == null || other == note) continue;
            var od = other.NoteData;
            if (od == null) continue;
            if (od.startTime > noteStart) continue;
            if (!LanesOverlap(other, note)) continue;

            float previousEnd = IsHoldLikeNote(other) ? od.endTime : (od.startTime + goodMs);
            if (previousEnd < windowStart) continue;

            float candidateGreen = 0.5f * (previousEnd + noteStart);
            if (candidateGreen > bestGreen)
            {
                bestGreen = candidateGreen;
            }
        }

        if (bestGreen <= 0f || currentSongPos >= bestGreen)
        {
            return false;
        }

        protectionEndMs = bestGreen;
        return true;
    }

    private bool TryComputeTailProtection(NoteController note, float currentSongPos, out float protectionEndMs)
    {
        protectionEndMs = 0f;
        var nd = note?.NoteData;
        if (nd == null) return false;

        if (note.IsStaccato)
        {
            return false;
        }

        float tailTime = nd.endTime;
        float bestGreen = 0f;

        foreach (var other in activeNotes)
        {
            if (other == null || other == note) continue;
            var od = other.NoteData;
            if (od == null) continue;
            if (od.startTime < tailTime) continue;
            if (!LanesOverlap(other, note)) continue;

            float nextWindowStart = od.startTime - goodMs;
            if (tailTime + goodMs < nextWindowStart) continue;

            float candidateGreen = 0.5f * (tailTime + od.startTime);
            if (candidateGreen > bestGreen)
            {
                bestGreen = candidateGreen;
            }
        }

        if (bestGreen <= 0f || currentSongPos >= bestGreen)
        {
            return false;
        }

        protectionEndMs = bestGreen;
        return true;
    }

    private float GetHeadFinalizationDeadline(NoteController note)
    {
        var nd = note?.NoteData;
        if (nd == null) return float.MinValue;
        return nd.startTime + goodMs;
    }

    private float GetTailFinalizationDeadline(NoteController note)
    {
        var nd = note?.NoteData;
        if (nd == null) return float.MinValue;
        return nd.endTime + goodMs;
    }

    private static bool IsPositiveJudgment(JudgmentResult res)
    {
        return res == JudgmentResult.Perfect || res == JudgmentResult.Great || res == JudgmentResult.Good;
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

    private void TryIncrementStats(JudgmentResult res)
    {
        try
        {
            StatsManager.Instance.Increment(res);
        }
        catch { }
    }

    private void RecordTimingOffsetIfNeeded(JudgmentPayload payload)
    {
        if (!IsPositiveJudgment(payload.result)) return;
        try
        {
            if (payload.isHoldTail)
            {
                if (!payload.hasHeadTiming) return;
                StatsManager.Instance.RecordTimingOffset(
                    payload.headInputSongPosMs - payload.headTargetTimeMs, payload.result);
            }
            else
            {
                StatsManager.Instance.RecordTimingOffset(
                    payload.inputSongPosMs - payload.targetTimeMs, payload.result);
            }
        }
        catch { }
    }

    private void ApplyJudgmentPayload(JudgmentPayload payload)
    {
        var note = payload.note;
        if (note == null) return;

        var handler = EnsureHandler(note);

        if (payload.isHoldTail)
        {
            if (payload.triggerMesh) ShowNoteMeshEffect(note, payload.result);
            if (payload.triggerPopup) 
            {
                ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result);
                try
                {
                    Color c = Color.white;
                    switch (payload.result)
                    {
                        case JudgmentResult.Perfect: c = Color.cyan; break;
                        case JudgmentResult.Great: c = Color.yellow; break;
                        case JudgmentResult.Good: c = new Color(1f, 0.6f, 0.2f); break;
                        default: c = Color.gray; break;
                    }
                    Vector3 pos = note.transform.position;
                    pos.y += 1.2f;
                    JudgementGlowController.Instance?.PlayAt(pos, followX: true, color: c);
                }
                catch { }
            }
            TryIncrementStats(payload.result);
            RecordTimingOffsetIfNeeded(payload);
            try { note.OnHoldEnd(payload.result); } catch { }
            try { handler?.OnFinalizeHold(note, payload.releaseSongPosMs, payload.pressSongPosMs); } catch { }
            CleanupHoldState(note);ㄙㄧㄣ
            return;
        }

        if (payload.isHoldHead)
        {
            if (payload.triggerMesh) ShowNoteMeshEffect(note, payload.result, payload.usePersistentMesh);
            if (payload.triggerHitSound)
            {
                try { handler?.PlayHeadSound(note, payload.inputSongPosMs, payload.result); } catch { }
            }
            if (payload.triggerPopup)
            {
                ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result);
                try
                {
                    Color c = Color.white;
                    switch (payload.result)
                    {
                        case JudgmentResult.Perfect: c = Color.cyan; break;
                        case JudgmentResult.Great: c = Color.yellow; break;
                        case JudgmentResult.Good: c = new Color(1f, 0.6f, 0.2f); break;
                        default: c = Color.gray; break;
                    }
                    Vector3 pos = note.transform.position;
                    pos.y += 1.2f;
                    JudgementGlowController.Instance?.PlayAt(pos, followX: true, color: c);
                }
                catch { }
            }
            TryIncrementStats(payload.result);
            RecordTimingOffsetIfNeeded(payload);
            if (activeHoldStates.TryGetValue(note, out var state))
            {
                state.headJudgmentCommitted = true;
                state.headJudgmentPending = false;
                state.headInputSongPosMs = payload.inputSongPosMs;
                state.headTargetTimeMs = payload.targetTimeMs;
                state.headAbsoluteOffsetMs = payload.absoluteOffsetMs;
                state.hasHeadTiming = true;
            }
            return;
        }

        if (payload.triggerMesh) ShowNoteMeshEffect(note, payload.result);
        if (payload.triggerHitSound)
        {
            try { handler?.PlayHeadSound(note, payload.inputSongPosMs, payload.result); } catch { }
        }
        if (payload.triggerPopup)
        {
            ShowJudgementPopups(payload.buttonId, note.transform.position, payload.result);
            try
            {
                Color c = Color.white;
                switch (payload.result)
                {
                    case JudgmentResult.Perfect: c = Color.cyan; break;
                    case JudgmentResult.Great: c = Color.yellow; break;
                    case JudgmentResult.Good: c = new Color(1f, 0.6f, 0.2f); break;
                    default: c = Color.gray; break;
                }
                Vector3 pos = note.transform.position;
                pos.y += 1.2f;
                JudgementGlowController.Instance?.PlayAt(pos, followX: true, color: c);
            }
            catch { }
        }
        TryIncrementStats(payload.result);
        RecordTimingOffsetIfNeeded(payload);
        note.OnJudged(payload.result);
    }

    private bool IsBetterPayload(JudgmentPayload incoming, JudgmentPayload current)
    {
        if (incoming.absoluteOffsetMs + 0.01f < current.absoluteOffsetMs)
        {
            return true;
        }

        if (Mathf.Abs(incoming.absoluteOffsetMs - current.absoluteOffsetMs) <= 0.01f)
        {
            return GetResultRank(incoming.result) < GetResultRank(current.result);
        }

        return false;
    }

    private void StorePendingHead(NoteController note, JudgmentPayload payload, float protectionEnd, float deadline)
    {
        if (!pendingHeadJudgments.TryGetValue(note, out var entry))
        {
            entry = new PendingJudgmentEntry
            {
                payload = payload,
                protectionEndMs = protectionEnd,
                finalizationDeadlineMs = deadline
            };
            pendingHeadJudgments[note] = entry;
        }
        else
        {
            entry.protectionEndMs = Mathf.Max(entry.protectionEndMs, protectionEnd);
            entry.finalizationDeadlineMs = Mathf.Max(entry.finalizationDeadlineMs, deadline);
            if (IsBetterPayload(payload, entry.payload))
            {
                entry.payload = payload;
            }
        }

        if (payload.isHoldHead && activeHoldStates.TryGetValue(note, out var state))
        {
            state.headJudgmentPending = true;
        }
    }

    private void StorePendingTail(NoteController note, JudgmentPayload payload, float protectionEnd, float deadline)
    {
        if (!pendingTailJudgments.TryGetValue(note, out var entry))
        {
            entry = new PendingJudgmentEntry
            {
                payload = payload,
                protectionEndMs = protectionEnd,
                finalizationDeadlineMs = deadline
            };
            pendingTailJudgments[note] = entry;
        }
        else
        {
            entry.protectionEndMs = Mathf.Max(entry.protectionEndMs, protectionEnd);
            entry.finalizationDeadlineMs = Mathf.Max(entry.finalizationDeadlineMs, deadline);
            if (IsBetterPayload(payload, entry.payload))
            {
                entry.payload = payload;
            }
        }
    }

    private void ProcessPendingJudgments(float songPos)
    {
        pendingHeadFinalizeScratch.Clear();
        foreach (var kvp in pendingHeadJudgments)
        {
            var note = kvp.Key;
            var entry = kvp.Value;
            if (songPos >= entry.finalizationDeadlineMs)
            {
                pendingHeadFinalizeScratch.Add(note);
            }
        }

        for (int i = 0; i < pendingHeadFinalizeScratch.Count; i++)
        {
            var note = pendingHeadFinalizeScratch[i];
            if (!pendingHeadJudgments.TryGetValue(note, out var entry)) continue;
            ApplyJudgmentPayload(entry.payload);
            pendingHeadJudgments.Remove(note);
        }

        pendingTailFinalizeScratch.Clear();
        foreach (var kvp in pendingTailJudgments)
        {
            var note = kvp.Key;
            var entry = kvp.Value;
            if (songPos >= entry.finalizationDeadlineMs)
            {
                pendingTailFinalizeScratch.Add(note);
            }
        }

        for (int i = 0; i < pendingTailFinalizeScratch.Count; i++)
        {
            var note = pendingTailFinalizeScratch[i];
            if (!pendingTailJudgments.TryGetValue(note, out var entry)) continue;
            ApplyJudgmentPayload(entry.payload);
            pendingTailJudgments.Remove(note);
        }
    }

    private void ResolveHeadJudgment(NoteController note, JudgmentPayload payload, float currentSongPos, bool allowProtection)
    {
        if (pendingHeadJudgments.TryGetValue(note, out var entry))
        {
            if (currentSongPos < entry.protectionEndMs)
            {
                if (IsBetterPayload(payload, entry.payload))
                {
                    entry.payload = payload;
                }
                entry.finalizationDeadlineMs = Mathf.Max(entry.finalizationDeadlineMs, GetHeadFinalizationDeadline(note));
                return;
            }

            pendingHeadJudgments.Remove(note);
        }

        if (!allowProtection || payload.result == JudgmentResult.Perfect)
        {
            ApplyJudgmentPayload(payload);
            return;
        }

        if (TryComputeHeadProtection(note, currentSongPos, out var protectionEnd))
        {
            if (currentSongPos < protectionEnd)
            {
                StorePendingHead(note, payload, protectionEnd, GetHeadFinalizationDeadline(note));
                return;
            }
        }

        ApplyJudgmentPayload(payload);
    }

    private void ResolveTailJudgment(NoteController note, JudgmentPayload payload, float currentSongPos, bool allowProtection)
    {
        if (pendingTailJudgments.TryGetValue(note, out var entry))
        {
            if (currentSongPos < entry.protectionEndMs)
            {
                if (IsBetterPayload(payload, entry.payload))
                {
                    entry.payload = payload;
                }
                entry.finalizationDeadlineMs = Mathf.Max(entry.finalizationDeadlineMs, GetTailFinalizationDeadline(note));
                return;
            }

            pendingTailJudgments.Remove(note);
        }

        if (!allowProtection || payload.result == JudgmentResult.Perfect)
        {
            ApplyJudgmentPayload(payload);
            return;
        }

        if (TryComputeTailProtection(note, currentSongPos, out var protectionEnd))
        {
            if (currentSongPos < protectionEnd)
            {
                StorePendingTail(note, payload, protectionEnd, GetTailFinalizationDeadline(note));
                return;
            }
        }

        ApplyJudgmentPayload(payload);
    }

    private void ApplyImmediateHeadJudgment(NoteController note, JudgmentResult result, int buttonId, float songPos, bool triggerMesh, bool triggerHitSound)
    {
        if (note == null) return;
        var nd = note.NoteData;
        float targetTime = nd != null ? nd.startTime : songPos;
        int resolvedButton = buttonId;
        if (resolvedButton < 0)
        {
            resolvedButton = nd != null ? nd.startLane : 0;
        }

        var payload = new JudgmentPayload
        {
            note = note,
            result = result,
            buttonId = resolvedButton,
            inputSongPosMs = songPos,
            pressSongPosMs = songPos,
            targetTimeMs = targetTime,
            absoluteOffsetMs = Mathf.Abs(songPos - targetTime),
            triggerMesh = triggerMesh,
            triggerPopup = true,
            triggerHitSound = triggerHitSound,
            isHoldHead = false,
            isHoldTail = false,
            usePersistentMesh = false
        };

        ApplyJudgmentPayload(payload);
    }

    private void TryShowTransientMesh(int buttonId)
    {
        try
        {
            KeyHitEffectManager.Instance.ShowMeshForKeyId(buttonId);
        }
        catch { }
    }

    private void PurgeAutoHeldNotesWithNullKeys()
    {
        if (autoHeldNotes.Count == 0)
        {
            return;
        }

        toFinalizeScratch.Clear();
        foreach (var kv in autoHeldNotes)
        {
            if (kv.Key == null)
            {
                toFinalizeScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < toFinalizeScratch.Count; i++)
        {
            var key = toFinalizeScratch[i];
            if (!ReferenceEquals(key, null))
            {
                autoHeldNotes.Remove(key);
            }
        }
        toFinalizeScratch.Clear();
    }

#endif

