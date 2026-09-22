using UnityEngine;

namespace Judgment
{
    /// <summary>
    /// Tracks which press consumed each note, and — when enabled — hands a note
    /// back to a better-timed press that arrives while its window is still open.
    /// </summary>
    /// <remarks>
    /// Matching is first-come-first-served, so a note goes to whichever press
    /// enters its window first, and "first" is early by definition. When the
    /// player produces more presses than the chart has notes — a rolled chord
    /// under one wide note, or notes the difficulty transform removed — the
    /// earliest finger wins and the better-timed one behind it finds nothing.
    /// Measured on a real session: a fifth of all hits were taken ~84 ms early
    /// while a press ~49 ms closer followed. No timing offset can undo that,
    /// because shifting the window moves both edges and the earliest press still
    /// wins inside it.
    ///
    /// <see cref="NoteSelector.EarlyClaimLimitMs"/> fixes this by refusing the
    /// far-early claim, which costs a miss whenever no better press follows.
    /// This class fixes it without that cost: the early press still takes the
    /// note and fires its feedback immediately, so nothing is delayed, and the
    /// judgment is corrected in place if a closer press turns up afterwards.
    ///
    /// Only plain taps are corrected. A hold head's press time is reused when
    /// its tail is finalised, so revising it after the fact would leave the hold
    /// scored against one time and judged against another.
    /// </remarks>
    public static class EarlyClaimCorrector
    {
        private struct Entry
        {
            public int              StartLane;
            public int              EndLane;
            public float            TargetMs;
            public float            PressMs;
            public JudgmentResult   Result;
            public decimal          Weight;
            public bool             Revisable;
            public bool             Stolen;
            public Vector3          Position;
        }

        private const int   Capacity      = 64;
        private const float MinGainMs     = 1f;   // ignore ties and float noise
        private const float EntryLifetime = 500f; // ms of song time worth keeping

        /// <summary>
        /// Earliness at which a claim is considered suspect. Matches the low end
        /// of the useful <see cref="NoteSelector.EarlyClaimLimitMs"/> range so
        /// the lone counter reports what turning that setting on would cost.
        /// </summary>
        private const float LoneEarlyThresholdMs = 50f;

        /// <summary>
        /// When true a better-timed press rewrites the judgment it arrives too
        /// late to win outright. Pair with <see cref="NoteSelector.EarlyClaimLimitMs"/>
        /// at 0 — a claim that never happened cannot be corrected.
        /// </summary>
        public static bool Enabled = false;

        /// <summary>
        /// Raised when a judgment has been rewritten, with the lane, the world
        /// position of the note, and the corrected result. JudgmentManager uses
        /// it to refresh the popup so the number on screen matches the number in
        /// the score.
        /// </summary>
        public static System.Action<int, Vector3, JudgmentResult> JudgmentRevised;

        private static readonly Entry[] Ring = new Entry[Capacity];
        private static int    _writeIndex;
        private static int    _unmatchedPresses;
        private static int    _steals;
        private static double _gainSumMs;
        private static double _stealingOffsetSumMs;
        private static int    _earlyClaims;
        private static int    _loneEarlyClaims;
        private static int    _revisions;
        private static int    _upgrades;

        public static void Reset()
        {
            for (int i = 0; i < Capacity; i++) Ring[i] = default;
            _writeIndex = 0;
            _unmatchedPresses = 0;
            _steals = 0;
            _gainSumMs = 0d;
            _stealingOffsetSumMs = 0d;
            _earlyClaims = 0;
            _loneEarlyClaims = 0;
            _revisions = 0;
            _upgrades = 0;
        }

        /// <summary>Remember a note that has just been consumed by a press.</summary>
        public static void RecordJudgment(int startLane, int endLane, float targetMs, float pressMs,
            JudgmentResult result, decimal weight, bool revisable, Vector3 position)
        {
            Retire(_writeIndex);
            Ring[_writeIndex] = new Entry
            {
                StartLane = startLane,
                EndLane   = endLane,
                TargetMs  = targetMs,
                PressMs   = pressMs,
                Result    = result,
                Weight    = weight,
                Revisable = revisable,
                Stolen    = false,
                Position  = position,
            };
            _writeIndex = (_writeIndex + 1) % Capacity;
        }

        /// <summary>
        /// Report a press that found no note. If a note it could have hit was
        /// already taken by a press with worse timing, that is a steal — and,
        /// when <see cref="Enabled"/>, the judgment is rewritten to this press.
        /// </summary>
        /// <remarks>
        /// Scoring only. The press keeps whatever sound it makes today — the
        /// note itself was already voiced by the press that claimed it, and
        /// changing what a second key press sounds like is a separate call from
        /// deciding which press the judgment belongs to.
        /// </remarks>
        public static void RecordUnmatchedPress(int buttonId, float songPos,
            int perfectMs, int greatMs, int goodMs)
        {
            _unmatchedPresses++;

            float bestGain = 0f;
            float bestStealingOffset = 0f;
            int bestIndex = -1;
            for (int i = 0; i < Capacity; i++)
            {
                ref readonly Entry e = ref Ring[i];
                if (e.TargetMs <= 0f) continue;
                if (songPos - e.TargetMs > EntryLifetime) continue;
                if (buttonId < e.StartLane || buttonId > e.EndLane) continue;

                float mine = Mathf.Abs(songPos - e.TargetMs);
                if (mine > goodMs) continue;
                float theirs = Mathf.Abs(e.PressMs - e.TargetMs);
                float gain = theirs - mine;
                if (gain <= MinGainMs || gain <= bestGain) continue;

                bestGain = gain;
                bestStealingOffset = e.PressMs - e.TargetMs;
                bestIndex = i;
            }

            if (bestIndex < 0) return;
            Ring[bestIndex].Stolen = true;
            _steals++;
            _gainSumMs += bestGain;
            _stealingOffsetSumMs += bestStealingOffset;

            if (!Enabled) return;
            ref Entry target = ref Ring[bestIndex];
            if (!target.Revisable) return;

            float oldOffset = target.PressMs - target.TargetMs;
            float newOffset = songPos - target.TargetMs;
            JudgmentResult newResult = Classify(Mathf.Abs(newOffset), perfectMs, greatMs, goodMs);
            if (newResult == JudgmentResult.Miss) return;

            StatsManager.Instance?.ReviseJudgment(
                target.Result, newResult, oldOffset, newOffset, target.Weight);

            _revisions++;
            if (newResult != target.Result) _upgrades++;

            bool resultChanged = newResult != target.Result;
            Vector3 position = target.Position;
            // Fold the correction back in, so a third press is measured against
            // the timing that now stands rather than the one that was replaced.
            target.PressMs = songPos;
            target.Result  = newResult;

            if (resultChanged)
            {
                try { JudgmentRevised?.Invoke(buttonId, position, newResult); } catch { }
            }
        }

        private static JudgmentResult Classify(float delta, int perfectMs, int greatMs, int goodMs)
        {
            if (delta <= perfectMs) return JudgmentResult.Perfect;
            if (delta <= greatMs) return JudgmentResult.Great;
            if (delta <= goodMs) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }

        /// <summary>
        /// Tally an entry on its way out of the ring. A claim that was well early
        /// and that no better press ever followed is one an early-claim limit
        /// would convert from a scoring hit into a miss — that is its real cost.
        /// </summary>
        private static void Retire(int index)
        {
            ref Entry e = ref Ring[index];
            if (e.TargetMs <= 0f) return;
            if (e.TargetMs - e.PressMs > LoneEarlyThresholdMs)
            {
                _earlyClaims++;
                if (!e.Stolen) _loneEarlyClaims++;
            }
            e = default;
        }

        /// <summary>Compact one-line summary for the end-of-song timing log.</summary>
        public static string Format()
        {
            for (int i = 0; i < Capacity; i++) Retire(i);
            if (_unmatchedPresses <= 0) return "steal=0/0";
            string tail = $" early={_earlyClaims} lone={_loneEarlyClaims}";
            if (Enabled) tail += $" revised={_revisions} upgraded={_upgrades}";
            if (_steals <= 0) return $"steal=0/{_unmatchedPresses}{tail}";
            float gain = (float)(_gainSumMs / _steals);
            float taken = (float)(_stealingOffsetSumMs / _steals);
            return $"steal={_steals}/{_unmatchedPresses} " +
                   $"gain={gain:0.0}ms takenAt={taken:+0.0;-0.0;0.0}ms{tail}";
        }
    }
}
