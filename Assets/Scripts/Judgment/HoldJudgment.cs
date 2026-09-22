using System;
using UnityEngine;

namespace Judgment
{
    // Consolidated helper utilities for hold-related judgment logic.
    // Provides convenience methods used by JudgmentManager for head/tail and staccato evaluation.
    public static class HoldJudgment
    {
        private static Chart cachedDensityChart;
        private static float cachedDensityIntervalMs;

        // Determine whether a note should be treated as a hold-like note
        public static bool IsHoldLikeNote(NoteController note)
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

        // Map timing delta -> head judgment result (used when no handler provided)
        public static JudgmentResult EvaluateHoldHeadResult(float delta, int perfectMs, int greatMs, int goodMs)
        {
            return JudgmentWindows.Evaluate(delta, perfectMs, greatMs, goodMs);
        }

        // Compute staccato tail final result based on releaseTime - startTime and configured windows
        public static JudgmentResult ComputeStaccatoTailResult(float releaseSongPos, float startTime, int stacPerfectMs, int stacGreatMs, int stacGoodMs)
        {
            return JudgmentWindows.EvaluateStaccato(
                releaseSongPos - startTime, stacPerfectMs, stacGreatMs, stacGoodMs);
        }

        // Convenience head judge that operates on a NoteController and song position
        public static JudgmentResult JudgeHead(NoteController note, float songPos, int perfectMs, int greatMs, int goodMs)
        {
            if (note == null) return JudgmentResult.Miss;
            var nd = note.NoteData;
            if (nd == null) return JudgmentResult.Miss;
            float delta = Mathf.Abs(nd.startTime - songPos);
            if (delta <= perfectMs) return JudgmentResult.Perfect;
            if (delta <= greatMs) return JudgmentResult.Great;
            if (delta <= goodMs) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }

        // Convenience tail judge that operates on a NoteController and song position (uses staccato windows)
        public static JudgmentResult JudgeTail(NoteController note, float songPos, int stacPerfectMs, int stacGreatMs, int stacGoodMs)
        {
            if (note == null) return JudgmentResult.Miss;
            var nd = note.NoteData;
            if (nd == null) return JudgmentResult.Miss;
            float rel = songPos - nd.startTime;
            if (rel < 0f) rel = 0f;
            if (rel <= stacPerfectMs) return JudgmentResult.Perfect;
            if (rel <= stacGreatMs) return JudgmentResult.Great;
            if (rel <= stacGoodMs) return JudgmentResult.Good;
            return JudgmentResult.Miss;
        }

        // --- New helpers for continuous hold extra-judgment logic ---
        // Compute the duration (ms) of the award unit based on bpm according to policy:
        // - bpm < 120  => one judgment per 16th note
        // - 120..220   => one judgment per 8th note
        // - bpm > 220   => one judgment per quarter note
        // This returns the milliseconds-per-award-unit (formerly 'eighthMs').
        public static float ComputeEighthMs(float bpm)
        {
            if (bpm <= 0f) return 500f;
            if (bpm < 120f)
            {
                // 16th note: quarter / 4 = (60000/bpm) / 4 = 15000/bpm
                return Mathf.Max(1f, 15000f / bpm);
            }
            if (bpm <= 220f)
            {
                // 8th note
                return Mathf.Max(1f, 30000f / bpm);
            }
            // quarter note
            return Mathf.Max(1f, 60000f / bpm);
        }

        /// <summary>
        /// Staccato, by either the numeric note_type (3) or the string type the chart
        /// converters emit ("staccato" / "stac"). Both spellings occur in the wild, so
        /// this mirrors StatsManager.CountsAsDualJudgment rather than picking one.
        /// </summary>
        public static bool IsStaccato(NoteData nd)
        {
            if (nd == null) return false;
            try
            {
                if (!string.IsNullOrEmpty(nd.type))
                {
                    if (string.Equals(nd.type, "staccato", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(nd.type, "stac", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (int.TryParse(nd.type, out int parsed) && parsed == 3) return true;
                }
            }
            catch { }
            return nd.note_type == 3;
        }

        // Compute max extra judgments and eighthMs for a hold note given its NoteData and bpm.
        public static (int maxExtra, float eighthMs) ComputeExtraInfo(NoteData nd, float? overrideBpm = null)
        {
            // 1. BPM 來源：優先用即時 BPM（與 UI 同步）
            float bpm = 120f;
            Chart chart = null;
            try {
                if (GameManager.Instance != null && GameManager.Instance.CurrentChart != null)
                    chart = GameManager.Instance.CurrentChart;
            } catch { }
            try {
                string chartInfo = chart == null ? "null" : $"beats={chart.beat_timings?.Count ?? -1} first_bpm={chart.first_bpm}";
                try { BuildLogger.Log($"[HoldJudg] DIAG chart={chartInfo} nd.startTime={nd?.startTime}"); } catch { }
                if (chart != null && chart.beat_timings != null && chart.beat_timings.Count > 1)
                {
                    try { BuildLogger.Log($"[HoldJudg] DIAG beats[0]={chart.beat_timings[0]} beats[last]={chart.beat_timings[chart.beat_timings.Count-1]}"); } catch { }
                }
            } catch { }
            if (nd != null && chart != null)
            {
                float instantBpm = ComputeInstantBpmAtMs(chart, nd.startTime);
                if (instantBpm > 0f) bpm = instantBpm;
                else if (chart.first_bpm > 0f) bpm = chart.first_bpm;
            }
            else if (overrideBpm.HasValue) bpm = overrideBpm.Value;
            else if (GameManager.Instance != null && GameManager.Instance.Conductor != null)
                bpm = GameManager.Instance.Conductor.bpm;
            float eighth = chart != null
                ? ComputeChartDensityIntervalMs(chart)
                : ComputeEighthMs(bpm);
            if (nd == null) return (0, eighth);
            // A STACCATO never awards per-beat extras. It is judged by its *release*
            // (StaccatoJudgment clamps an early release to Perfect), and the auto flow
            // releases it at startTime + stacAutoReleaseMs — 60 ms — so on a dense chart
            // the first extra interval never arrives. FinalizeExpiredHolds then fills the
            // un-awarded extras through ComputeHoldFillResult(0, n) = Miss, which breaks
            // the combo on a note that was played exactly right. Measured on 系ぎて: its
            // judgment density is 73 ms, so 26 of the 28 staccatos forced a Miss every run.
            // StatsManager.ConfigureExpectedJudgments already scores staccato as head+tail
            // only; this is the runtime half of that same rule.
            if (IsStaccato(nd)) return (0, eighth);
            try
            {
                try { BuildLogger.Log($"[HoldJudg] ComputeExtraInfo called nd.start={nd.startTime} end={nd.endTime} bpm={bpm}"); } catch { }
                float holdMs = (float)(nd.endTime - nd.startTime);
                // Head and tail already award one judgment each. Continuous
                // Combo ticks use 80% of the bar duration, preserving the
                // intended long-note scoring ratio while their spacing comes
                // from the whole chart's average judgment density.
                float validMs = Mathf.Max(0f, (holdMs - 1f) * 0.8f);
                int extra = validMs > 0f ? Mathf.FloorToInt(validMs / eighth) : 0;
                if (extra < 0) extra = 0;
                try { BuildLogger.Log($"[HoldJudg] ComputeExtraInfo start={nd.startTime} end={nd.endTime} holdMs={holdMs} validMs={validMs} eighth={eighth} extra={extra}"); } catch { }
                return (extra, eighth);
            }
            catch { return (0, eighth); }
        }

        public static float ComputeChartDensityIntervalMs(Chart chart)
        {
            if (chart == null) return ComputeEighthMs(120f);
            if (chart == cachedDensityChart && cachedDensityIntervalMs > 0f)
                return cachedDensityIntervalMs;

            int baseJudgments = 0;
            float earliestMs = float.MaxValue;
            float latestMs = 0f;
            if (chart.notes != null)
            {
                for (int i = 0; i < chart.notes.Count; i++)
                {
                    NoteData note = chart.notes[i];
                    if (note == null) continue;
                    earliestMs = Mathf.Min(earliestMs, note.startTime);
                    latestMs = Mathf.Max(latestMs, Mathf.Max(note.startTime, note.endTime));

                    bool trill = note.note_type == 64 ||
                        string.Equals(note.type, "trill", StringComparison.OrdinalIgnoreCase);
                    if (trill)
                    {
                        baseJudgments += Mathf.Max(1,
                            Mathf.CeilToInt(Mathf.Max(1f, note.endTime - note.startTime) / 125f));
                        continue;
                    }

                    baseJudgments++;
                    bool dual = note.note_type == 2 || note.note_type == 3 ||
                        string.Equals(note.type, "hold", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(note.type, "staccato", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(note.type, "stac", StringComparison.OrdinalIgnoreCase);
                    if (dual) baseJudgments++;
                }
            }

            float durationMs = chart.music_finish_time_msec > 0
                ? chart.music_finish_time_msec
                : Mathf.Max(0f, latestMs - (earliestMs < float.MaxValue ? earliestMs : 0f));
            float intervalMs = baseJudgments > 0 && durationMs > 0f
                ? durationMs / baseJudgments
                : ComputeEighthMs(chart.first_bpm > 0f ? chart.first_bpm : 120f);

            cachedDensityChart = chart;
            cachedDensityIntervalMs = Mathf.Clamp(intervalMs, 60f, 1200f);
            return cachedDensityIntervalMs;
        }

        // Decide whether an extra judgment should be awarded at this song position.
        // - pressSongPos: when the hold was first pressed
        // - lastAwardSongPosMs: last extra award time (float.MinValue if none)
        // - awardedExtra: how many extras already awarded
        // - maxExtra: cap
        // - eighthMs: duration of an eighth-note
        // - songPos: current song position
        public static bool ShouldAwardExtra(float pressSongPos, float lastAwardSongPosMs, int awardedExtra, int maxExtra, float eighthMs, float songPos)
        {
            if (maxExtra <= 0) return false;
            if (awardedExtra >= maxExtra) return false;
            float eighth = eighthMs > 0f ? eighthMs : ComputeEighthMs(120f);
            float last = lastAwardSongPosMs == float.MinValue ? pressSongPos : lastAwardSongPosMs;
            return songPos - last + 0.001f >= eighth;
        }
        // 取得指定 ms 時間點的即時 BPM（複製自 BpmDisplay）
        public static float ComputeInstantBpmAtMs(Chart chart, float songPosMs)
        {
            if (chart == null || chart.beat_timings == null || chart.beat_timings.Count < 2) return 0f;
            var beats = chart.beat_timings;
            // beatsPerMeasure: 先抓 chart.time_signature 或預設 4
            int beatsPerMeasure = 4;
            try {
                if (!string.IsNullOrEmpty(chart.time_signature)) {
                    var parts = chart.time_signature.Split('/');
                    if (parts.Length >= 1 && int.TryParse(parts[0], out int n) && n > 0) beatsPerMeasure = n;
                }
            } catch { }
            // 找到第一個大於 songPosMs 的 beat index
            int idx = 0, hi = beats.Count - 1;
            if (songPosMs < beats[0]) idx = 0;
            else if (songPosMs >= beats[hi]) idx = hi;
            else {
                int lo = 0;
                while (lo < hi) {
                    int mid = (lo + hi) / 2;
                    if (beats[mid] <= songPosMs) lo = mid + 1; else hi = mid;
                }
                idx = lo;
            }
            int i0 = Mathf.Clamp(idx - 1, 0, beats.Count - 2);
            int i1 = i0 + 1;
            int span = beats[i1] - beats[i0];
            if (span <= 0) return 0f;
            // 正確公式：bpm = 60000 * beatsPerMeasure / span（span為一小節長度）
            float bpm = 60000f * beatsPerMeasure / (float)span;
            return bpm;
        }
    }
}
