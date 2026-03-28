using System;
using System.Collections.Generic;

namespace Judgment
{
    // Thin static wrapper to preserve legacy 'Protection' static API while delegating
    // to the instance-based JudgmentProtection implementation.
    public static class Protection
    {
        private static readonly JudgmentProtection impl = new JudgmentProtection();

        public static bool ResolveHeadJudgment(NoteController note, JudgmentPayload payload, float currentSongPos, bool allowProtection, IEnumerable<NoteController> candidates, int goodMs, Action<JudgmentPayload> applyCallback)
        {
            return impl.ResolveHeadJudgment(note, payload, currentSongPos, allowProtection, candidates, goodMs, applyCallback);
        }

        public static bool ResolveTailJudgment(NoteController note, JudgmentPayload payload, float currentSongPos, bool allowProtection, IEnumerable<NoteController> candidates, int goodMs, Action<JudgmentPayload> applyCallback)
        {
            return impl.ResolveTailJudgment(note, payload, currentSongPos, allowProtection, candidates, goodMs, applyCallback);
        }

        public static bool IsInProtection(NoteController note, float currentMs)
        {
            return impl.IsInProtection(note, currentMs);
        }

        public static void ProcessPendingJudgments(float songPos, Action<JudgmentPayload> applyCallback)
        {
            impl.ProcessPendingJudgments(songPos, applyCallback);
        }

        public static void FlushPendingJudgments(Action<JudgmentPayload> applyCallback)
        {
            impl.FlushPendingJudgments(applyCallback);
        }

        public static void SetProtection(NoteController note, float protectionEndMs)
        {
            impl.SetProtection(note, protectionEndMs);
        }

        public static void RemoveProtection(NoteController note)
        {
            impl.RemoveProtection(note);
        }

        public static void ClearProtectionEnd(NoteController note)
        {
            impl.ClearProtectionEnd(note);
        }

        // Toggle global protection enabled state. When disabling, clear any
        // stored protection/pending state to avoid stale pending judgments.
        public static void SetProtectionEnabled(bool enabled)
        {
            JudgmentProtection.ProtectionEnabled = enabled;
            if (!enabled)
            {
                try { impl.ClearAllPendingAndProtection(); } catch { }
            }
        }

        public static bool HasPendingHead(NoteController note)
        {
            return impl.HasPendingHead(note);
        }

        public static bool HasPendingTail(NoteController note)
        {
            return impl.HasPendingTail(note);
        }
    }
}
