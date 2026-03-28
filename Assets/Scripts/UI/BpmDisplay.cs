using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Text;

/// <summary>
/// Display BPM on-screen. Shows initial JSON `first_bpm` when entering gameplay,
/// then switches to a computed BPM derived from `Chart.beat_timings` per-measure.
/// Attach this to a GameObject in the scene and assign a TextMeshProUGUI in the Inspector.
/// </summary>
public class BpmDisplay : MonoBehaviour
{
    [Tooltip("Assign a TextMeshProUGUI to display the BPM")]
    public TextMeshProUGUI bpmText;

    [Tooltip("Beats per measure (e.g. 4 for common time). Used when computing BPM from beat_timings.")]
    public int beatsPerMeasure = 4;
    [Tooltip("Enable debug logging for BPM computation (prints spans and computed BPMs)")]
    public bool enableDebug = false;
    [Tooltip("If true, prefer the chart's `first_bpm` for the UI instead of showing instantaneous/computed BPM during playback")]
    public bool preferChartFirstBpm = true;
    [Tooltip("Minimum beat index (number of beats encountered) before allowing computed/instant BPM to be used in the UI.")]
    public int minBeatsBeforeUseMeasuredBpm = 2;
    [Tooltip("If true, wait until the first full measure (beatsPerMeasure) has been observed before switching to measured BPM.")]
    public bool waitForFirstMeasure = true;
    // no visual flash; we update the text every detection (like combo/score)

    // Runtime state
    private Chart chart = null;
    private Conductor conductor = null;
    private GameManager gm = null;
    private GameManager.ChartHeader chartHeader = null;
    // Track last chart reference so we can detect when the selected song changes
    private Chart lastChartRef = null;
    private List<float> measureBpms = new List<float>();
    private bool initialized = false;
    // cache to avoid allocating/updating TMP every frame when value unchanged
    private string lastDisplayedText = null;
    private int lastBeatIndex = -1;
    private int lastDisplayedBpm = int.MinValue;
    private string lastDisplayedTs = null;
    // reusable temporary list to avoid per-call allocations when computing deltas
    private List<int> reusableIntList = new List<int>(16);
    // reusable StringBuilder for debug output to avoid allocations when debug enabled
    private StringBuilder debugBuilder = new StringBuilder(1024);
    // number of smallest beat subdivisions per conventional beat unit (e.g. 3 for dotted-quarter in 6/8)
    private int beatUnitSubdivision = 1;
    // detected subdivision based on beat_timings spacing vs chart.first_bpm
    private int detectedSubdivision = 1;
    private int timeSigNumerator = 0;
    private int timeSigDenominator = 0;
    // effective subdivision actually used for BPM computation (set after detection)
    private int effectiveBeatSubdivision = 1;
    // remove median-based BPM; compute instantaneous BPM per detection

    void Start()
    {
        // We'll initialize lazily in Update so this script is safe regardless of load order
    }

    // compute median delta (ms) of first few beat timings; returns 0 if unavailable
    private float MedianDelta(System.Collections.Generic.List<int> beats)
    {
        if (beats == null || beats.Count < 2) return 0f;
        int sampleCount = Mathf.Min(16, beats.Count - 1);
        reusableIntList.Clear();
        for (int i = 1; i <= sampleCount; i++) reusableIntList.Add(beats[i] - beats[i - 1]);
        reusableIntList.Sort();
        return reusableIntList[reusableIntList.Count / 2];
    }

    private string GetChartTimeSignature(Chart c)
    {
        if (c == null) return "";
        if (!string.IsNullOrEmpty(c.time_signature)) return c.time_signature;
        if (timeSigNumerator > 0 && timeSigDenominator > 0) return $"{timeSigNumerator}/{timeSigDenominator}";
        return "";
    }

    void Update()
    {
        // Ensure we have a reference to GameManager and detect chart changes so
        // the BPM display reinitializes when a new song is selected.
        if (gm == null) gm = GameManager.Instance;
        if (gm != null && gm.CurrentChart != lastChartRef)
        {
            // Chart changed (or first time) -> force reinitialization
            lastChartRef = gm.CurrentChart;
            initialized = false;
            chart = null;
            conductor = null;
            chartHeader = gm.CurrentChartHeader;
        }

        if (!initialized)
        {
            TryInitialize();
        }

        if (bpmText == null) return;
        // no visual flash; update every detection

        if (chart == null)
        {
            // Fallback: use header BPM if full chart not yet parsed
            float headerBpm = chartHeader != null ? chartHeader.first_bpm : 0f;
            string headerTs = (chartHeader != null && !string.IsNullOrEmpty(chartHeader.time_signature)) ? chartHeader.time_signature : "-";
            string newText;
            if (headerBpm > 0f)
            {
                int headerDisplayBpm = Mathf.RoundToInt(headerBpm);
                newText = "Time Signature：" + headerTs + "\nBPM：" + headerDisplayBpm;
                lastDisplayedBpm = headerDisplayBpm;
                lastDisplayedTs = headerTs;
            }
            else
            {
                newText = "BPM：\n(—)：-";
            }

            if (lastDisplayedText != newText)
            {
                bpmText.SetText(newText);
                bpmText.ForceMeshUpdate();
                lastDisplayedText = newText;
            }
            return;
        }

        // Primary BPM value from JSON (unscaled; audio speed is independent)
        float primaryBpm = chart.first_bpm;

        // determine unit name for display (use effective subdivision)
        string unitName = "beat";
        if (effectiveBeatSubdivision == 3) unitName = "dotted-quarter";
        else if (timeSigDenominator == 4) unitName = "quarter";
        else if (effectiveBeatSubdivision > 1) unitName = $"subdiv x{effectiveBeatSubdivision}";

        // Compose two-line display as requested:
        // Time Signature：N/D\nBPM：value
        string tsText = timeSigNumerator > 0 && timeSigDenominator > 0 ? $"{timeSigNumerator}/{timeSigDenominator}" : (chart != null && !string.IsNullOrEmpty(GetChartTimeSignature(chart)) ? GetChartTimeSignature(chart) : "-");
        // display BPM as integer (rounded) to reduce updates and allocations
        int displayBpm = Mathf.RoundToInt(primaryBpm);
        string secondLine = $"BPM：{displayBpm}";

        // If playing, try to compute an instantaneous BPM from beat timings first.
        // Only fall back to chart.first_bpm when no computed/measure BPM is available (preferChartFirstBpm=true).
        bool wroteBpm = false;
        if (conductor != null && conductor.isActive && chart != null && chart.beat_timings != null && chart.beat_timings.Count > 1)
        {
            float songPos = conductor.effectiveSongPosition; // ms

            // Dynamic time-signature lookup from time_signature_changes
            if (chart.time_signature_changes != null && chart.time_signature_changes.Count > 0)
            {
                int curNum = timeSigNumerator;
                int curDen = timeSigDenominator;
                foreach (var tc in chart.time_signature_changes)
                {
                    if (tc.time_ms <= (int)songPos) { curNum = tc.numerator; curDen = tc.denominator; }
                    else break;
                }
                if (curNum > 0 && curDen > 0) tsText = $"{curNum}/{curDen}";
            }
            int beatIndex = FindBeatIndexForSongPos(songPos);
            // Determine required minimum beats before allowing measured BPM
            int requiredBeats = minBeatsBeforeUseMeasuredBpm;
            if (waitForFirstMeasure)
            {
                requiredBeats = Mathf.Max(requiredBeats, Mathf.Max(1, beatsPerMeasure));
            }
            // Only use measured/instant BPM after we've seen the required number of beats
            if (beatIndex >= Mathf.Max(0, requiredBeats))
            {
                float instBpm = ComputeInstantBpmAtSongPos(songPos);
                if (instBpm > 0f)
                {
                    displayBpm = Mathf.RoundToInt(instBpm);
                    secondLine = $"BPM：{displayBpm}";
                    wroteBpm = true;
                }
                else if (measureBpms != null && measureBpms.Count > 0)
                {
                    int measureIndex = Mathf.Max(0, beatIndex / Mathf.Max(1, beatsPerMeasure));
                    if (measureIndex < measureBpms.Count)
                    {
                        float currentMeasureBpm = measureBpms[measureIndex];
                        displayBpm = Mathf.RoundToInt(currentMeasureBpm);
                        secondLine = $"BPM：{displayBpm}";
                        wroteBpm = true;
                    }
                }
            }
        }

        // If we didn't write a BPM from measurements, optionally use the chart's first_bpm as a fallback.
        if (!wroteBpm && preferChartFirstBpm)
        {
            displayBpm = Mathf.RoundToInt(primaryBpm);
            secondLine = $"BPM：{displayBpm}";
            wroteBpm = true;
        }
        // Build the display text once and only update TMP when it changed to avoid allocations
        // Only update visible text when either time signature or rounded BPM changed
        if (displayBpm != lastDisplayedBpm || tsText != lastDisplayedTs)
        {
            string newText = "Time Signature：" + tsText + "\n" + secondLine;
                try
                {
                    bpmText.SetText(newText);
                    bpmText.ForceMeshUpdate();
                    if (enableDebug) try { BuildLogger.Log($"[BpmDisplay] UI set to: {newText}"); } catch { }
                    lastDisplayedText = newText;
                    lastDisplayedBpm = displayBpm;
                    lastDisplayedTs = tsText;
                }
            catch (System.Exception ex)
            {
                try { BuildLogger.LogWarning($"[BpmDisplay] Failed to set bpmText: {ex.Message}"); } catch { }
            }
        }
        return;
    }

    

    private void TryInitialize()
    {
        if (gm == null) gm = GameManager.Instance;
        if (gm == null) return;
        chart = gm.CurrentChart;
        conductor = gm.Conductor;
        chartHeader = gm.CurrentChartHeader;
        if (chart == null || chart.beat_timings == null || chart.beat_timings.Count == 0)
        {
            initialized = true; // nothing to compute
            return;
        }

        // If the chart provides a time_signature (e.g. "4/4" or "6/8"), prefer it
        // to determine beatsPerMeasure and map compound meters to conventional beat units.
        try
        {
            if (!string.IsNullOrEmpty(chart.time_signature))
            {
                var parts = chart.time_signature.Split('/');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0].Trim(), out int parsedNumerator) && parsedNumerator > 0)
                    {
                        beatsPerMeasure = parsedNumerator;
                        timeSigNumerator = parsedNumerator;
                    }
                    if (int.TryParse(parts[1].Trim(), out int parsedDenominator) && parsedDenominator > 0)
                    {
                        timeSigDenominator = parsedDenominator;
                    }
                }
            }
            // If there are numeric fallback fields on Chart, use them (keeps compatibility)
            try
            {
                var fnum = chart.GetType().GetField("time_signature_numerator");
                var fden = chart.GetType().GetField("time_signature_denominator");
                if (fnum != null && fden != null)
                {
                    var numObj = fnum.GetValue(chart);
                    var denObj = fden.GetValue(chart);
                    if (numObj != null && denObj != null)
                    {
                        int parsedNumerator = System.Convert.ToInt32(numObj);
                        int parsedDenominator = System.Convert.ToInt32(denObj);
                        if (parsedNumerator > 0) { beatsPerMeasure = parsedNumerator; timeSigNumerator = parsedNumerator; }
                        if (parsedDenominator > 0) { timeSigDenominator = parsedDenominator; }
                    }
                }
            }
            catch { }

            // Map time signature to an effective subdivision representing
            // how many smallest note units (beat_timings entries) form a conventional beat.
            // Rules:
            // - 4/4 -> conventional beat is quarter (subdivision = 1)
            // - compound meters like 6/8, 9/8, 12/8 -> conventional beat is dotted-quarter (3 eighths)
            // - for denominators divisible by 4 (e.g., /16), treat subdivision = denominator/4 (sixteenth -> 4)
            // - otherwise fallback to 1
            if (timeSigDenominator == 4)
            {
                beatUnitSubdivision = 1; // quarter note is conventional beat
            }
            else if (timeSigDenominator == 8)
            {
                // treat 6/8,9/8,12/8 etc. as compound (dotted-quarter)
                if (timeSigNumerator % 3 == 0 && timeSigNumerator >= 6)
                {
                    beatUnitSubdivision = 3; // dotted-quarter = 3 eighths
                }
                else
                {
                    beatUnitSubdivision = 1; // treat as eighth-based simple meter
                }
            }
            else if (timeSigDenominator % 4 == 0)
            {
                beatUnitSubdivision = Mathf.Max(1, timeSigDenominator / 4);
            }
            else
            {
                beatUnitSubdivision = 1;
            }
        }
        catch { }

        // Auto-detect subdivision: compare median beat delta to expected duration from chart.first_bpm
        detectedSubdivision = 1;
        try
        {
            if (chart.first_bpm > 0f && chart.beat_timings.Count >= 2)
            {
                int sampleCount = Mathf.Min(16, chart.beat_timings.Count - 1);
                var deltas = new System.Collections.Generic.List<int>(sampleCount);
                for (int i = 1; i <= sampleCount; i++) deltas.Add(chart.beat_timings[i] - chart.beat_timings[i - 1]);
                deltas.Sort();
                int median = deltas[deltas.Count / 2];
                float expected = 60000f / Mathf.Max(1f, chart.first_bpm);
                // how many expected-beats fit into median delta
                float approx = median / expected;
                int r = Mathf.Clamp(Mathf.RoundToInt(approx), 1, 16);
                detectedSubdivision = r;
            }
        }
        catch { detectedSubdivision = 1; }

        // If we auto-detected a subdivision (e.g. beat_timings are in smaller units), use it
        int effectiveSubdivision = beatUnitSubdivision > 1 ? beatUnitSubdivision : detectedSubdivision;
        if (effectiveSubdivision <= 0) effectiveSubdivision = 1;
        effectiveBeatSubdivision = effectiveSubdivision;
        ComputeMeasureBpms(chart.beat_timings, beatsPerMeasure, effectiveSubdivision);
        initialized = true;
    }

    private void ComputeMeasureBpms(List<int> beatTimings, int beatsPerMeasure, int beatUnitSubdivision)
    {
        measureBpms.Clear();
        if (beatTimings == null || beatTimings.Count == 0) return;
        if (beatsPerMeasure <= 0) beatsPerMeasure = 4;
        if (beatUnitSubdivision <= 0) beatUnitSubdivision = 1;

        // Compute BPM per conventional beat unit by taking the duration of a measure
        // and converting: BPM = 60000 * (conventionalBeatsPerMeasure) / measureDurationMs
        // where conventionalBeatsPerMeasure = beatsPerMeasure / beatUnitSubdivision
        int step = beatsPerMeasure; // advance by whole measures
        if (enableDebug) debugBuilder.Clear();
        for (int i = 0; i + beatsPerMeasure < beatTimings.Count; i += step)
        {
            int t0 = beatTimings[i];
            int t1 = beatTimings[i + beatsPerMeasure];
            int span = t1 - t0;
            if (span <= 0) continue;
            float conventionalBeatsPerMeasure = (float)beatsPerMeasure / (float)beatUnitSubdivision;
            float bpm = 60000f * conventionalBeatsPerMeasure / (float)span;
            measureBpms.Add(bpm);
            if (enableDebug && debugBuilder.Length < 20000)
            {
                debugBuilder.AppendLine($"measure {i / step}: t0={t0}, t1={t1}, spanMs={span}, convBeats={conventionalBeatsPerMeasure}, bpm={bpm:F2}");
            }
        }
        // If we computed none but there are beats, fall back to single-beat spacing average
        if (measureBpms.Count == 0 && beatTimings.Count >= 2)
        {
            float avgDelta = 0f;
            int cnt = 0;
            for (int i = 1; i < beatTimings.Count; i++)
            {
                avgDelta += (beatTimings[i] - beatTimings[i - 1]);
                cnt++;
            }
            if (cnt > 0)
            {
                avgDelta /= cnt;
                // avgDelta represents subdivision interval; convert to conventional beat using beatUnitSubdivision
                float bpm = 60000f * (float)beatUnitSubdivision / avgDelta;
                measureBpms.Add(bpm);
                if (enableDebug)
                {
                    debugBuilder.AppendLine($"fallback avgDelta={avgDelta:F2}, beatUnitSubdivision={beatUnitSubdivision}, bpm={bpm:F2}");
                }
            }
        }

        if (enableDebug)
        {
            // Log a concise summary to the Console to help trace unexpected low BPM values
            var dbg = debugBuilder.ToString();
            if (string.IsNullOrEmpty(dbg)) dbg = "(no measures computed)";
            try { BuildLogger.Log($"[BpmDisplay] Computed {measureBpms.Count} measureBpms:\n" + dbg); } catch { }
        }
    }

    // Find the index of the first beat strictly after songPos (ms). Returns 0..N
    private int FindBeatIndexForSongPos(float songPosMs)
    {
        if (chart == null || chart.beat_timings == null || chart.beat_timings.Count == 0) return 0;
        var beats = chart.beat_timings;
        int lo = 0, hi = beats.Count - 1;
        if (songPosMs < beats[0]) return 0;
        if (songPosMs >= beats[hi]) return hi;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (beats[mid] <= songPosMs) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    // Compute a local BPM centered around songPos using a window of neighbouring beats.
    // windowRadius = number of beats before/after to include (e.g. 3 -> up to 7 intervals)
    // Compute instantaneous BPM using the nearest adjacent beat interval (no median)
    private float ComputeInstantBpmAtSongPos(float songPosMs)
    {
        if (chart == null || chart.beat_timings == null || chart.beat_timings.Count < 2) return 0f;
        var beats = chart.beat_timings;
        int idx = FindBeatIndexForSongPos(songPosMs);
        // If idx is 0, use interval between 0 and 1; otherwise use interval between idx-1 and idx (closest past interval)
        int i0 = Mathf.Clamp(idx - 1, 0, beats.Count - 2);
        int i1 = i0 + 1;
        int span = beats[i1] - beats[i0];
        if (span <= 0) return 0f;
        int sub = Mathf.Max(1, effectiveBeatSubdivision);
        float bpm = 60000f * (float)sub / (float)span;
        if (enableDebug)
        {
            try { BuildLogger.Log($"[BpmDisplay] instant: idx={idx} use={i0}-{i1} spanMs={span} sub={sub} -> bpm={bpm:F2}"); } catch { }
        }
        return bpm;
    }
}
