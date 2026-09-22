using System.Collections.Generic;
using System.Collections;
using UnityEngine;

namespace Judgment
{
    public class JudgmentProtection
    {
        // Toggle for protection logic ("green line"). Can be switched at runtime if needed.
        // Disable protection to make judgement matching as permissive as possible.
        // Setting to false means pending/head/tail protection windows are bypassed
        // and payloads are applied immediately.
        public static bool ProtectionEnabled = true;

        private Dictionary<NoteController, float> protectionEndDict = new Dictionary<NoteController, float>();

        // pending structures
        private class PendingJudgmentEntry
        {
            public JudgmentPayload payload;
            public float protectionEndMs;
            public float finalizationDeadlineMs;
            public System.Action<JudgmentPayload> applyCallback;
            public Coroutine finalizeCoroutine;
            public int scheduleVersion;
        }

        private sealed class ProtectionScheduler : MonoBehaviour
        {
        }

        private readonly Dictionary<NoteController, PendingJudgmentEntry> pendingHeadJudgments = new Dictionary<NoteController, PendingJudgmentEntry>();
        private readonly Dictionary<NoteController, PendingJudgmentEntry> pendingTailJudgments = new Dictionary<NoteController, PendingJudgmentEntry>();
        private ProtectionScheduler scheduler;

        private ProtectionScheduler EnsureScheduler()
        {
            if (scheduler != null) return scheduler;

            var existing = Object.FindFirstObjectByType<ProtectionScheduler>();
            if (existing != null)
            {
                scheduler = existing;
                return scheduler;
            }

            var go = new GameObject("JudgmentProtectionScheduler");
            Object.DontDestroyOnLoad(go);
            scheduler = go.AddComponent<ProtectionScheduler>();
            return scheduler;
        }

        private void StopScheduledFinalize(PendingJudgmentEntry entry)
        {
            if (entry == null || entry.finalizeCoroutine == null) return;
            try
            {
                var host = EnsureScheduler();
                if (host != null) host.StopCoroutine(entry.finalizeCoroutine);
            }
            catch { }
            entry.finalizeCoroutine = null;
        }

        private void CancelPendingEntry(Dictionary<NoteController, PendingJudgmentEntry> dict, NoteController note)
        {
            if (note == null) return;
            if (!dict.TryGetValue(note, out var entry)) return;
            StopScheduledFinalize(entry);
            dict.Remove(note);
        }

        private void SchedulePendingFinalize(NoteController note, bool isHead, PendingJudgmentEntry entry)
        {
            if (!ProtectionEnabled || note == null || entry == null) return;

            var host = EnsureScheduler();
            if (host == null) return;

            StopScheduledFinalize(entry);
            entry.scheduleVersion++;
            entry.finalizeCoroutine = host.StartCoroutine(WaitAndFinalizePending(note, isHead, entry.scheduleVersion));
        }

        private IEnumerator WaitAndFinalizePending(NoteController note, bool isHead, int version)
        {
            while (ProtectionEnabled)
            {
                var dict = isHead ? pendingHeadJudgments : pendingTailJudgments;
                if (!dict.TryGetValue(note, out var entry)) yield break;
                if (entry.scheduleVersion != version) yield break;

                var jm = JudgmentManager.Instance;
                if (jm == null)
                {
                    yield return null;
                    continue;
                }

                if (jm.CurrentSongPositionMs >= entry.finalizationDeadlineMs)
                {
                    entry.finalizeCoroutine = null;
                    dict.Remove(note);
                    entry.applyCallback?.Invoke(entry.payload);
                    yield break;
                }

                yield return null;
            }
        }

        public void SetProtection(NoteController note, float protectionEndMs)
        {
            // When protection is disabled, do not record protection windows.
            if (!ProtectionEnabled) return;
            if (note == null) return;
            protectionEndDict[note] = protectionEndMs;
        }

        public bool IsInProtection(NoteController note, float currentMs)
        {
            // If protection is disabled, treat everything as not in protection so
            // FindBestNote and other matchers are unaffected.
            if (!ProtectionEnabled) return false;
            if (note == null) return false;
            if (protectionEndDict.TryGetValue(note, out var endMs))
            {
                return currentMs < endMs;
            }
            return false;
        }

        public void RemoveProtection(NoteController note)
        {
            if (note == null) return;
            protectionEndDict.Remove(note);
            CancelPendingEntry(pendingHeadJudgments, note);
            CancelPendingEntry(pendingTailJudgments, note);
        }

        /// <summary>
        /// Clear only the protection end marker for a note, but keep any pending head/tail entries.
        /// Use this when the manager wants to drop the temporary protection window without discarding
        /// pending judgments that the protection module is holding.
        /// </summary>
        public void ClearProtectionEnd(NoteController note)
        {
            if (note == null) return;
            protectionEndDict.Remove(note);
        }

        public bool HasPendingHead(NoteController note)
        {
            if (!ProtectionEnabled) return false;
            if (note == null) return false;
            return pendingHeadJudgments.ContainsKey(note);
        }

        public bool HasPendingTail(NoteController note)
        {
            if (!ProtectionEnabled) return false;
            if (note == null) return false;
            return pendingTailJudgments.ContainsKey(note);
        }

        /// <summary>
        /// Clear all protection windows and pending judgments. Call this when
        /// protection is being disabled to avoid stale pending entries causing
        /// unexpected later application of judgments.
        /// </summary>
        public void ClearAllPendingAndProtection()
        {
            foreach (var entry in pendingHeadJudgments.Values) StopScheduledFinalize(entry);
            foreach (var entry in pendingTailJudgments.Values) StopScheduledFinalize(entry);
            protectionEndDict.Clear();
            pendingHeadJudgments.Clear();
            pendingTailJudgments.Clear();
        }

        // --- Compute protection windows (head/tail) based on active notes and goodMs window ---
        public bool TryComputeHeadProtection(NoteController note, float currentSongPos, IEnumerable<NoteController> candidates, int goodMs, out float protectionEndMs)
        {
            protectionEndMs = 0f;
            var nd = note?.NoteData;
            if (nd == null) return false;
            if (candidates == null) return false;

            float noteStart = nd.startTime;
            float windowStart = noteStart - goodMs;
            float bestGreen = 0f;

            foreach (var other in candidates)
            {
                if (other == null || other == note) continue;
                var od = other.NoteData;
                if (od == null) continue;
                if (od.startTime > noteStart) continue;
                if (!(od.endLane >= nd.startLane && od.startLane <= nd.endLane)) continue;

                float previousEnd = (IsHoldLikeNote(other) ? od.endTime : (od.startTime + goodMs));
                if (previousEnd < windowStart) continue;

                float candidateGreen = 0.5f * (previousEnd + noteStart);
                if (candidateGreen > bestGreen) bestGreen = candidateGreen;
            }

            if (bestGreen <= 0f || currentSongPos >= bestGreen) return false;
            // Clamp protection window so we never defer beyond current + goodMs; this keeps pending short-lived
            protectionEndMs = UnityEngine.Mathf.Min(bestGreen, currentSongPos + goodMs);
            if (protectionEndMs <= currentSongPos) return false;
            return true;
        }

        public bool TryComputeTailProtection(NoteController note, float currentSongPos, IEnumerable<NoteController> candidates, int goodMs, out float protectionEndMs)
        {
            protectionEndMs = 0f;
            var nd = note?.NoteData;
            if (nd == null) return false;
            if (note.IsStaccato) return false;
            if (candidates == null) return false;

            float tailTime = nd.endTime;
            float windowEnd = tailTime + goodMs;
            float bestGreen = 0f;

            foreach (var other in candidates)
            {
                if (other == null || other == note) continue;
                var od = other.NoteData;
                if (od == null) continue;
                if (od.startTime < tailTime) continue; // only consider notes after this tail
                if (!(od.endLane >= nd.startLane && od.startLane <= nd.endLane)) continue;
                if (od.startTime > windowEnd) continue; // too far ahead; no need to defer

                float candidateGreen = 0.5f * (tailTime + od.startTime);
                if (candidateGreen > bestGreen) bestGreen = candidateGreen;
            }

            if (bestGreen <= 0f || currentSongPos >= bestGreen) return false;
            // Clamp protection window for tails as well
            protectionEndMs = UnityEngine.Mathf.Min(bestGreen, currentSongPos + goodMs);
            if (protectionEndMs <= currentSongPos) return false;
            return true;
        }

        private static bool IsHoldLikeNote(NoteController note)
        {
            if (note == null) return false;
            try
            {
                var nd = note.NoteData; if (nd == null) return false;
                if (!string.IsNullOrEmpty(nd.type))
                {
                    if (string.Equals(nd.type, "hold", System.StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(nd.type, "trill", System.StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(nd.type, "staccato", System.StringComparison.OrdinalIgnoreCase) || string.Equals(nd.type, "stac", System.StringComparison.OrdinalIgnoreCase)) return true;
                    if (int.TryParse(nd.type, out int parsedType) && (parsedType == 2 || parsedType == 3)) return true;
                }
                if (nd.note_type == 2 || nd.note_type == 3 || nd.note_type == 64) return true;
            }
            catch { }
            return false;
        }

        private static bool IsBetterPayload(JudgmentPayload incoming, JudgmentPayload current, float eps = 0.01f)
        {
            if (incoming == null) return true;
            if (current == null) return true;
            if (incoming.absoluteOffsetMs + eps < current.absoluteOffsetMs) return true;
            if (System.Math.Abs(incoming.absoluteOffsetMs - current.absoluteOffsetMs) <= eps)
            {
                int r1 = GetResultRank(incoming.result);
                int r2 = GetResultRank(current.result);
                return r1 < r2;
            }
            return false;
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

        private float GetHeadFinalizationDeadline(NoteController note, int goodMs)
        {
            var nd = note?.NoteData;
            if (nd == null) return float.MinValue;
            return nd.startTime + goodMs;
        }

        private float GetTailFinalizationDeadline(NoteController note, int goodMs)
        {
            var nd = note?.NoteData;
            if (nd == null) return float.MinValue;
            return nd.endTime + goodMs;
        }

        // Reuse scratch lists to avoid per-frame allocations in ProcessPendingJudgments
        private readonly System.Collections.Generic.List<NoteController> headToFinalize = new System.Collections.Generic.List<NoteController>();
        private readonly System.Collections.Generic.List<NoteController> tailToFinalize = new System.Collections.Generic.List<NoteController>();

        public void StorePendingHead(NoteController note, JudgmentPayload payload, float protectionEnd, float deadline, System.Action<JudgmentPayload> applyCallback)
        {
            if (!ProtectionEnabled) return;
            if (!pendingHeadJudgments.TryGetValue(note, out var entry))
            {
                entry = new PendingJudgmentEntry { payload = payload, protectionEndMs = protectionEnd, finalizationDeadlineMs = deadline, applyCallback = applyCallback };
                pendingHeadJudgments[note] = entry;
            }
            else
            {
                entry.protectionEndMs = UnityEngine.Mathf.Max(entry.protectionEndMs, protectionEnd);
                entry.finalizationDeadlineMs = UnityEngine.Mathf.Max(entry.finalizationDeadlineMs, deadline);
                if (IsBetterPayload(payload, entry.payload)) entry.payload = payload;
                if (applyCallback != null) entry.applyCallback = applyCallback;
            }
            SchedulePendingFinalize(note, true, entry);
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { BuildLogger.Log($"[JProt] StorePendingHead note={note?.name ?? "<null>"} res={payload?.result} protectionEnd={protectionEnd} deadline={deadline}"); } catch { }
            #endif
        }

        public void StorePendingTail(NoteController note, JudgmentPayload payload, float protectionEnd, float deadline, System.Action<JudgmentPayload> applyCallback)
        {
            if (!ProtectionEnabled) return;
            if (!pendingTailJudgments.TryGetValue(note, out var entry))
            {
                entry = new PendingJudgmentEntry { payload = payload, protectionEndMs = protectionEnd, finalizationDeadlineMs = deadline, applyCallback = applyCallback };
                pendingTailJudgments[note] = entry;
            }
            else
            {
                entry.protectionEndMs = UnityEngine.Mathf.Max(entry.protectionEndMs, protectionEnd);
                entry.finalizationDeadlineMs = UnityEngine.Mathf.Max(entry.finalizationDeadlineMs, deadline);
                if (IsBetterPayload(payload, entry.payload)) entry.payload = payload;
                if (applyCallback != null) entry.applyCallback = applyCallback;
            }
            SchedulePendingFinalize(note, false, entry);
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { BuildLogger.Log($"[JProt] StorePendingTail note={note?.name ?? "<null>"} res={payload?.result} protectionEnd={protectionEnd} deadline={deadline}"); } catch { }
            #endif
        }

        // Resolve head: returns true if stored as pending, false if applied immediately (applyCallback invoked)
        public bool ResolveHeadJudgment(NoteController note, JudgmentPayload payload, float currentSongPos, bool allowProtection, IEnumerable<NoteController> candidates, int goodMs, System.Action<JudgmentPayload> applyCallback)
        {
            if (!ProtectionEnabled || !allowProtection)
            {
                applyCallback?.Invoke(payload);
                return false;
            }
            if (pendingHeadJudgments.TryGetValue(note, out var entry))
            {
                if (currentSongPos < entry.protectionEndMs)
                {
                    if (IsBetterPayload(payload, entry.payload)) entry.payload = payload;
                    entry.finalizationDeadlineMs = UnityEngine.Mathf.Max(entry.finalizationDeadlineMs, GetHeadFinalizationDeadline(note, goodMs));
                    if (applyCallback != null) entry.applyCallback = applyCallback;
                    SchedulePendingFinalize(note, true, entry);
                    return true;
                }
                CancelPendingEntry(pendingHeadJudgments, note);
            }

            if (!allowProtection || payload.result == JudgmentResult.Perfect)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                try { BuildLogger.Log($"[JProt] ResolveHeadJudgment APPLY IMMEDIATE note={note?.name ?? "<null>"} res={payload.result} allowProtection={allowProtection}"); } catch { }
                #endif
                applyCallback?.Invoke(payload);
                return false;
            }

            if (TryComputeHeadProtection(note, currentSongPos, candidates, goodMs, out var protectionEnd))
            {
                if (currentSongPos < protectionEnd)
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    try { BuildLogger.Log($"[JProt] ResolveHeadJudgment STORE note={note?.name ?? "<null>"} current={currentSongPos} protectionEnd={protectionEnd}"); } catch { }
                    #endif
                    StorePendingHead(note, payload, protectionEnd, GetHeadFinalizationDeadline(note, goodMs), applyCallback);
                    return true;
                }
            }
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { BuildLogger.Log($"[JProt] ResolveHeadJudgment APPLY note={note?.name ?? "<null>"} res={payload.result}"); } catch { }
            #endif
            applyCallback?.Invoke(payload);
            return false;
        }

        // Resolve tail: returns true if stored as pending, false if applied immediately (applyCallback invoked)
        public bool ResolveTailJudgment(NoteController note, JudgmentPayload payload, float currentSongPos, bool allowProtection, IEnumerable<NoteController> candidates, int goodMs, System.Action<JudgmentPayload> applyCallback)
        {
            if (!ProtectionEnabled || !allowProtection)
            {
                applyCallback?.Invoke(payload);
                return false;
            }
            if (pendingTailJudgments.TryGetValue(note, out var entry))
            {
                if (currentSongPos < entry.protectionEndMs)
                {
                    if (IsBetterPayload(payload, entry.payload)) entry.payload = payload;
                    entry.finalizationDeadlineMs = UnityEngine.Mathf.Max(entry.finalizationDeadlineMs, GetTailFinalizationDeadline(note, goodMs));
                    if (applyCallback != null) entry.applyCallback = applyCallback;
                    SchedulePendingFinalize(note, false, entry);
                    return true;
                }
                CancelPendingEntry(pendingTailJudgments, note);
            }

            if (!allowProtection || payload.result == JudgmentResult.Perfect)
            {
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                try { BuildLogger.Log($"[JProt] ResolveTailJudgment APPLY IMMEDIATE note={note?.name ?? "<null>"} res={payload.result} allowProtection={allowProtection}"); } catch { }
                #endif
                applyCallback?.Invoke(payload);
                return false;
            }

            if (TryComputeTailProtection(note, currentSongPos, candidates, goodMs, out var protectionEnd))
            {
                if (currentSongPos < protectionEnd)
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    try { BuildLogger.Log($"[JProt] ResolveTailJudgment STORE note={note?.name ?? "<null>"} current={currentSongPos} protectionEnd={protectionEnd}"); } catch { }
                    #endif
                    StorePendingTail(note, payload, protectionEnd, GetTailFinalizationDeadline(note, goodMs), applyCallback);
                    return true;
                }
            }
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { BuildLogger.Log($"[JProt] ResolveTailJudgment APPLY note={note?.name ?? "<null>"} res={payload.result}"); } catch { }
            #endif
            applyCallback?.Invoke(payload);
            return false;
        }

        // Process pending judgments whose deadline has passed; invokes applyCallback for each
        public void ProcessPendingJudgments(float songPos, System.Action<JudgmentPayload> applyCallback)
        {
            // Event-driven mode: pending head/tail finalization is scheduled when entries are stored.
        }

        // Flush all pending judgments and apply them via callback without extra allocations
        public void FlushPendingJudgments(System.Action<JudgmentPayload> applyCallback)
        {
            if (!ProtectionEnabled) return;
            headToFinalize.Clear();
            foreach (var kv in pendingHeadJudgments) headToFinalize.Add(kv.Key);
            for (int i = 0; i < headToFinalize.Count; i++)
            {
                var note = headToFinalize[i];
                if (note == null) continue;
                if (!pendingHeadJudgments.TryGetValue(note, out var entry)) continue;
                StopScheduledFinalize(entry);
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                try { BuildLogger.Log($"[JProt] FlushPending APPLY HEAD note={note?.name ?? "<null>"} res={entry.payload?.result}"); } catch { }
                #endif
                applyCallback?.Invoke(entry.payload);
            }
            pendingHeadJudgments.Clear();

            tailToFinalize.Clear();
            foreach (var kv in pendingTailJudgments) tailToFinalize.Add(kv.Key);
            for (int i = 0; i < tailToFinalize.Count; i++)
            {
                var note = tailToFinalize[i];
                if (note == null) continue;
                if (!pendingTailJudgments.TryGetValue(note, out var entry)) continue;
                StopScheduledFinalize(entry);
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                try { BuildLogger.Log($"[JProt] FlushPending APPLY TAIL note={note?.name ?? "<null>"} res={entry.payload?.result}"); } catch { }
                #endif
                applyCallback?.Invoke(entry.payload);
            }
            pendingTailJudgments.Clear();
        }
    }
}
