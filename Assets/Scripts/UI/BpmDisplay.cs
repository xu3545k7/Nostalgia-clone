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
    private int lastSpeedDirection;
    private bool lastSpeedWarning;
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
    private int currentTimeSigDenominator = 0;
    // effective subdivision actually used for BPM computation (set after detection)
    private int effectiveBeatSubdivision = 1;
    // beat_timings has no single unit across all charts. Anchor its opening
    // median interval to first_bpm, then measure later tempo changes relatively.
    private float referenceBeatIntervalMs;

    /// <summary>
    /// 實際拿來當錪的 BPM。宣告的 <c>first_bpm</c> 和拍點表對得上就是它，
    /// 對不上就從拍點表自己推。
    /// </summary>
    /// <remarks>
    /// **為什麼不能無條件相信 first_bpm。** 它只是一個標示欄位，而產生它的管線
    /// 上有好幾個默默填 120 的後備（偵測器找不到峰值、MIDI 沒有 tempo 事件、
    /// tempo 事件壞掉）。真正決定音符位置的是 <c>beat_timings</c> —— 譜面就是照它
    /// 畫的，所以宣告值和格線對不上的時候，該被懷疑的是宣告值。
    ///
    /// 推法：中位間隔當一拍算出 BPM，落在 55~320 就用；否則當一小節
    /// （×拍號分子），再不行試 ×2、×4。這是八度歧義，沒有完美解 —— 但比
    /// 相信一個已知會退化成 120 的欄位好。
    /// </remarks>
    private float anchorBpm;
    private sealed class DetectedTempoChange
    {
        public int timeMs;
        public float beforeBpm;
        public float afterBpm;
    }
    private readonly List<DetectedTempoChange> detectedTempoChanges =
        new List<DetectedTempoChange>();
    private int detectedTimeSignatureChangeCount = -1;
    private int detectedBeatTimingCount = -1;
    private const float UiRefreshInterval = 0.05f;
    private float nextUiRefreshTime;

    /// <summary>「現在的速度」最多往回看幾個拍點間距。</summary>
    private const int InstantWindowMaxIntervals = 16;

    /// <summary>往回看到累積這麼久就停（毫秒）。慢的曲子不必湊滿上面那個筆數。</summary>
    private const float InstantWindowTargetMs = 1200f;

    /// <summary>窗內的間距，排序取中位數用。每次更新都會跑，不要每次配置新的。</summary>
    private readonly List<int> instantIntervals = new List<int>(InstantWindowMaxIntervals);

    /// <summary>
    /// 別人已經親手排過這一塊了，<see cref="ConfigureClassicalPresentation"/> 不要再動。
    /// </summary>
    /// <remarks>
    /// **為什麼需要這個開關。** <c>Start</c> 裡那一套會把框撐成至少 400×82、對齊設成
    /// MidlineLeft、再掛一個自己的 Canvas。GameBackgroundManager 另外把這一塊貼到分數
    /// 底下，兩邊寫的是同一個 RectTransform —— 誰贏完全看 <c>Start</c> 跟那段程式哪個
    /// 先跑，而那是沒有保證的。速度和分數疊在一起、或者整塊跑到左邊，都是這個競態。
    ///
    /// 用旗標而不是調整執行順序：順序要靠 Script Execution Order 設定，那是場景資料
    /// 不是程式，改了在別的場景不會跟著走；而且下一個來排版的人也不會知道有這條規則。
    /// </remarks>
    private bool placementLocked;

    /// <summary>宣告這一塊的位置由呼叫端負責，擋掉內建的那一套。</summary>
    public void LockPlacement()
    {
        placementLocked = true;
    }

    void Start()
    {
        // We'll initialize lazily in Update so this script is safe regardless of load order
        ConfigureClassicalPresentation();
    }

    private void ConfigureClassicalPresentation()
    {
        if (bpmText == null || placementLocked) return;
        bool insideUnifiedPanel = bpmText.transform.parent != null &&
                                  bpmText.transform.parent.name == "ClassicalSongInfoPanel";
        Canvas frontCanvas = bpmText.GetComponent<Canvas>();
        if (insideUnifiedPanel)
        {
            if (frontCanvas != null) Destroy(frontCanvas);
        }
        else
        {
            if (frontCanvas == null) frontCanvas = bpmText.gameObject.AddComponent<Canvas>();
            frontCanvas.overrideSorting = true;
            frontCanvas.sortingOrder = 23988;
        }

        float targetSize = insideUnifiedPanel ? 18f : 27f;
        ClassicalBookUITheme.StyleText(bpmText, new Color(0.96f, 0.91f, 0.79f, 1f), targetSize, FontStyles.Bold);
        bpmText.richText = true;
        bpmText.overrideColorTags = false;
        bpmText.textWrappingMode = TextWrappingModes.NoWrap;
        bpmText.overflowMode = TextOverflowModes.Masking;
        bpmText.maskable = true;
        bpmText.raycastTarget = false;
        bpmText.alignment = TextAlignmentOptions.MidlineLeft;
        bpmText.outlineWidth = 0.11f;
        bpmText.outlineColor = new Color(0.10f, 0.045f, 0.025f, 0.88f);
        bpmText.enableAutoSizing = true;
        bpmText.fontSizeMin = insideUnifiedPanel ? 12f : 16f;
        bpmText.fontSizeMax = targetSize;
        bpmText.characterSpacing = 0f;
        bpmText.wordSpacing = 0f;
        if (!insideUnifiedPanel)
        {
            bpmText.rectTransform.sizeDelta = new Vector2(
                Mathf.Max(400f, bpmText.rectTransform.sizeDelta.x),
                Mathf.Max(82f, bpmText.rectTransform.sizeDelta.y));
        }
    }

    public void ConfigureFloatingPresentation()
    {
        if (bpmText == null) return;
        ClassicalBookUITheme.StyleText(bpmText, new Color(0.96f, 0.91f, 0.79f, 1f),
            64f, FontStyles.Bold);
        bpmText.richText = true;
        bpmText.overrideColorTags = false;
        bpmText.textWrappingMode = TextWrappingModes.NoWrap;
        bpmText.overflowMode = TextOverflowModes.Overflow;
        bpmText.maskable = false;
        bpmText.raycastTarget = false;
        bpmText.alignment = TextAlignmentOptions.MidlineLeft;
        bpmText.outlineWidth = 0.14f;
        bpmText.outlineColor = new Color(0.04f, 0.02f, 0.01f, 0.96f);
        bpmText.enableAutoSizing = true;
        bpmText.fontSizeMin = 28f;
        bpmText.fontSizeMax = 64f;
    }

    /// <summary>
    /// HUD 上那兩行字。**數字不隨快慢變色**。
    /// </summary>
    /// <remarks>
    /// 以前加速會把數字染成藍色、減速染成紅色（2026-11-10 使用者要求取消）。
    /// 取消的只有「上色」這一層，速度變化的偵測（<see cref="ResolveSpeedVisual"/>、
    /// <c>detectedTempoChanges</c>）原樣留著：它還在決定要不要重畫，而且以後要換
    /// 別的提示方式（箭頭、框線）時不必重做。
    ///
    /// 顏色寫在數字自己的 <c>&lt;color&gt;</c> 標籤裡，不用 <c>overrideColorTags</c>：
    /// 後者會把整塊（含 TIME SIGNATURE／BPM 兩個標籤）一起上色。
    /// </remarks>
    private static string FormatBpmText(string timeSignature, int bpm)
    {
        string ts = string.IsNullOrWhiteSpace(timeSignature) ? "-" : timeSignature;
        string bpmValue = bpm > 0 ? bpm.ToString() : "-";
        // 字級是絕對點數，不隨 fontSize 縮放 —— 要改大小就得改這裡。
        //
        // 兩個標籤（TIME SIGNATURE／BPM）是說明不是讀數，壓到最小還讀得出就好。
        // 數字從 58 降到 44：這一塊現在貼在分數下面，而分數是 48 —— 比分數大的
        // 速度值會把視線先拉到這裡，但這一局真正要看的是分數。
        return $"<size=15><color=#D9BC76>TIME SIGNATURE</color>  {ts}</size>\n" +
               $"<size=18><color=#D9BC76>BPM</color></size>  " +
               $"<size=44><color=#F5E8C9>{bpmValue}</color></size>";
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
        bool chartChanged = false;
        if (gm == null) gm = GameManager.Instance;
        if (gm != null && gm.CurrentChart != lastChartRef)
        {
            // Chart changed (or first time) -> force reinitialization
            lastChartRef = gm.CurrentChart;
            initialized = false;
            chart = null;
            conductor = null;
            chartHeader = gm.CurrentChartHeader;
            lastSpeedDirection = 0;
            lastSpeedWarning = false;
            referenceBeatIntervalMs = 0f;
            anchorBpm = 0f;
            detectedTimeSignatureChangeCount = -1;
            detectedBeatTimingCount = -1;
            detectedTempoChanges.Clear();
            chartChanged = true;
        }

        // GameManager can assign/recreate the Conductor after the chart has
        // already initialized. Keep the playback clock reference live or the
        // warning window can never advance.
        if (gm != null && conductor != gm.Conductor)
        {
            conductor = gm.Conductor;
            chartChanged = true;
        }

        // A chart instance may be published before its beat list has finished
        // loading. Its reference then stays the same, so retry initialization
        // when the timing data becomes available.
        int availableBeatCount = gm != null && gm.CurrentChart != null &&
                                 gm.CurrentChart.beat_timings != null
            ? gm.CurrentChart.beat_timings.Count
            : 0;
        if (initialized && availableBeatCount > 1 &&
            (referenceBeatIntervalMs <= 0f ||
             availableBeatCount != detectedBeatTimingCount))
        {
            initialized = false;
            chartChanged = true;
        }

        // BPM/time-signature text is informational and cannot change meaningfully
        // 120 times per second. Throttling its string work avoids steady GC/TMP cost.
        float now = Time.unscaledTime;
        if (!chartChanged && now < nextUiRefreshTime) return;
        nextUiRefreshTime = now + UiRefreshInterval;

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

            newText = FormatBpmText(headerTs, headerBpm > 0f ? Mathf.RoundToInt(headerBpm) : 0);
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
        float speedFactor = gm != null ? gm.CurrentAudioSpeedFactor : 1f;
        int displayBpm = Mathf.RoundToInt(primaryBpm * speedFactor);
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
                currentTimeSigDenominator = curDen;
            }
            else currentTimeSigDenominator = timeSigDenominator;
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
                    displayBpm = Mathf.RoundToInt(instBpm * speedFactor);
                    secondLine = $"BPM：{displayBpm}";
                    wroteBpm = true;
                }
                else if (measureBpms != null && measureBpms.Count > 0)
                {
                    int measureIndex = Mathf.Max(0, beatIndex / Mathf.Max(1, beatsPerMeasure));
                    if (measureIndex < measureBpms.Count)
                    {
                        float currentMeasureBpm = measureBpms[measureIndex];
                        displayBpm = Mathf.RoundToInt(currentMeasureBpm * speedFactor);
                        secondLine = $"BPM：{displayBpm}";
                        wroteBpm = true;
                    }
                }
            }
        }

        // If we didn't write a BPM from measurements, optionally use the chart's first_bpm as a fallback.
        if (!wroteBpm && preferChartFirstBpm)
        {
            displayBpm = Mathf.RoundToInt(primaryBpm * speedFactor);
            secondLine = $"BPM：{displayBpm}";
            wroteBpm = true;
        }
        // Build the display text once and only update TMP when it changed to avoid allocations
        // Only update visible text when either time signature or rounded BPM changed
        int speedDirection = 0;
        bool speedWarning = false;
        if (conductor != null)
            ResolveSpeedVisual(conductor.effectiveSongPosition, out speedDirection,
                out speedWarning);
        if (displayBpm != lastDisplayedBpm || tsText != lastDisplayedTs ||
            speedDirection != lastSpeedDirection || speedWarning != lastSpeedWarning)
        {
            if (enableDebug &&
                (speedDirection != lastSpeedDirection ||
                 speedWarning != lastSpeedWarning))
            {
                Debug.Log($"[BpmDisplay] warning={speedWarning} direction={speedDirection} " +
                          $"songMs={(conductor != null ? conductor.effectiveSongPosition : -1f):F0}");
            }
            string newText = "Time Signature：" + tsText + "\n" + secondLine;
                try
                {
                    // 顏色寫在數字自己的 <color> 標籤裡，所以這裡不開
                    // overrideColorTags（那會把整塊一起上色）。
                    bpmText.overrideColorTags = false;
                    bpmText.color = new Color(0.96f, 0.91f, 0.79f, 1f);
                    newText = FormatBpmText(tsText, displayBpm);
                    // 速度方向還在驅動「要不要重畫」，但數字已經不隨快慢變色了，
                    // 所以變速時算出來的字串會一模一樣 —— 那就不要再丟給 TMP 一次，
                    // SetText + ForceMeshUpdate 是這一塊最貴的動作。
                    if (newText != lastDisplayedText)
                    {
                        bpmText.SetText(newText);
                        bpmText.ForceMeshUpdate();
                    }
                    if (enableDebug) try { BuildLogger.Log($"[BpmDisplay] UI set to: {newText}"); } catch { }
                    lastDisplayedText = newText;
                    lastDisplayedBpm = displayBpm;
                    lastDisplayedTs = tsText;
                    lastSpeedDirection = speedDirection;
                    lastSpeedWarning = speedWarning;
                }
            catch (System.Exception ex)
            {
                try { BuildLogger.LogWarning($"[BpmDisplay] Failed to set bpmText: {ex.Message}"); } catch { }
            }
        }
        return;
    }

    private void ResolveSpeedVisual(float songPositionMs, out int direction, out bool warning)
    {
        direction = 0;
        warning = false;
        int currentChangeCount = chart != null && chart.time_signature_changes != null
            ? chart.time_signature_changes.Count
            : 0;
        int currentBeatCount = chart != null && chart.beat_timings != null
            ? chart.beat_timings.Count
            : 0;
        if (currentChangeCount != detectedTimeSignatureChangeCount ||
            currentBeatCount != detectedBeatTimingCount)
            BuildDetectedTempoChanges();

        SongSelectionManager.SongOption selected = SongSelectionManager.Instance?.GetSelectedSong();
        float previousFactor = selected != null && selected.audioSpeedFactor > 0f
            ? selected.audioSpeedFactor
            : 1f;
        int nextTime = int.MaxValue;
        int nextDirection = 0;
        int latestPastTime = int.MinValue;
        int latestPastDirection = 0;

        if (gm != null && gm.CurrentAudioSpeedEvents != null)
        {
            float factorBeforeEvent = previousFactor;
            for (int i = 0; i < gm.CurrentAudioSpeedEvents.Count; i++)
            {
                SongSelectionManager.AudioSpeedEvent speedEvent = gm.CurrentAudioSpeedEvents[i];
                if (speedEvent == null) continue;
                float factor = speedEvent.factor > 0f ? speedEvent.factor : 1f;
                int eventDirection = CompareFactor(factor, factorBeforeEvent);
                if (speedEvent.audiochangeTimeMs <= songPositionMs)
                {
                    previousFactor = factor;
                    if (eventDirection != 0 &&
                        speedEvent.audiochangeTimeMs >= latestPastTime)
                    {
                        latestPastTime = speedEvent.audiochangeTimeMs;
                        latestPastDirection = eventDirection;
                    }
                }
                else if (eventDirection != 0 &&
                         speedEvent.audiochangeTimeMs < nextTime)
                {
                    nextTime = speedEvent.audiochangeTimeMs;
                    nextDirection = eventDirection;
                }
                factorBeforeEvent = factor;
            }
        }

        for (int i = 0; i < detectedTempoChanges.Count; i++)
        {
            DetectedTempoChange tempo = detectedTempoChanges[i];
            if (tempo.timeMs <= songPositionMs)
            {
                int tempoDirection = CompareFactor(tempo.afterBpm, tempo.beforeBpm);
                if (tempoDirection != 0 && tempo.timeMs >= latestPastTime)
                {
                    latestPastTime = tempo.timeMs;
                    latestPastDirection = tempoDirection;
                }
                continue;
            }
            if (tempo.timeMs < nextTime)
            {
                nextTime = tempo.timeMs;
                nextDirection = CompareFactor(tempo.afterBpm, tempo.beforeBpm);
            }
            break;
        }

        // An upcoming change owns the colour throughout its four-measure
        // warning window. Consecutive events therefore join without a white
        // frame between them.
        if (nextTime != int.MaxValue)
        {
            float warningStart = GetMeasureWarningStart(nextTime, previousFactor);
            if (songPositionMs >= warningStart && songPositionMs < nextTime)
            {
                direction = nextDirection;
                warning = direction != 0;
                return;
            }
        }

        // Do not turn white on the exact frame a change is crossed. Keep its
        // colour until one complete stable measure has elapsed with no newer
        // tempo event.
        if (latestPastTime != int.MinValue && latestPastDirection != 0)
        {
            float stableDurationMs =
                GetMeasureDurationMs(latestPastTime, previousFactor, true);
            if (songPositionMs < latestPastTime + stableDurationMs)
            {
                direction = latestPastDirection;
                warning = true;
            }
        }
    }

    private void BuildDetectedTempoChanges()
    {
        detectedTempoChanges.Clear();
        detectedTimeSignatureChangeCount =
            chart != null && chart.time_signature_changes != null
                ? chart.time_signature_changes.Count
                : 0;
        detectedBeatTimingCount =
            chart != null && chart.beat_timings != null
                ? chart.beat_timings.Count
                : 0;
        if (chart == null || chart.beat_timings == null ||
            chart.beat_timings.Count < 3)
            return;
        if (referenceBeatIntervalMs <= 0f)
            referenceBeatIntervalMs = MedianDelta(chart.beat_timings);

        if (chart.time_signature_changes != null)
        {
            for (int i = 0; i < chart.time_signature_changes.Count; i++)
            {
                TimeSigChange change = chart.time_signature_changes[i];
                if (change == null || change.time_ms <= 0) continue;
                float beforeSpan = MedianIntervalAround(change.time_ms, false);
                float afterSpan = MedianIntervalAround(change.time_ms, true);
                float beforeBpm = CalculateAnchoredBpm(
                    beforeSpan, GetDenominatorAt(change.time_ms - 1));
                float afterBpm = CalculateAnchoredBpm(
                    afterSpan, GetDenominatorAt(change.time_ms));
                AddDetectedTempoChange(change.time_ms, beforeBpm, afterBpm, 0);
            }
        }

        // A tempo change does not require a time-signature change. Scan every
        // chart's timing grid for two stable neighbouring interval regions.
        // This also supports imported charts that only contain beat_timings.
        const int window = 4;
        List<int> beats = chart.beat_timings;
        for (int boundary = window; boundary + window < beats.Count; boundary++)
        {
            float beforeSpan = MedianIntervalRange(boundary - window, window);
            float afterSpan = MedianIntervalRange(boundary, window);
            if (beforeSpan <= 0f || afterSpan <= 0f) continue;
            if (!IsStableIntervalRange(boundary - window, window, beforeSpan) ||
                !IsStableIntervalRange(boundary, window, afterSpan)) continue;

            int transitionInterval = boundary;
            int searchEnd = Mathf.Min(boundary + window, beats.Count - 1);
            for (int interval = boundary; interval < searchEnd; interval++)
            {
                int span = beats[interval + 1] - beats[interval];
                if (span > 0 &&
                    Mathf.Abs(span - afterSpan) / afterSpan <= 0.006f)
                {
                    transitionInterval = interval;
                    break;
                }
            }
            int eventTime = beats[transitionInterval];
            float beforeBpm = CalculateAnchoredBpm(
                beforeSpan, GetDenominatorAt(eventTime - 1));
            float afterBpm = CalculateAnchoredBpm(
                afterSpan, GetDenominatorAt(eventTime));
            int mergeWindowMs = Mathf.RoundToInt(
                Mathf.Max(beforeSpan, afterSpan) * 2.25f);
            AddDetectedTempoChange(
                eventTime, beforeBpm, afterBpm, mergeWindowMs);
        }

        detectedTempoChanges.Sort((a, b) => a.timeMs.CompareTo(b.timeMs));
        if (enableDebug)
        {
            debugBuilder.Clear();
            for (int i = 0; i < detectedTempoChanges.Count; i++)
            {
                DetectedTempoChange tempo = detectedTempoChanges[i];
                if (i > 0) debugBuilder.Append(", ");
                debugBuilder.Append(tempo.timeMs)
                    .Append("ms ")
                    .Append(Mathf.RoundToInt(tempo.beforeBpm))
                    .Append("->")
                    .Append(Mathf.RoundToInt(tempo.afterBpm));
            }
            Debug.Log($"[BpmDisplay] detected {detectedTempoChanges.Count} tempo changes: " +
                      debugBuilder);
        }
    }

    private void AddDetectedTempoChange(
        int timeMs, float beforeBpm, float afterBpm, int mergeWindowMs)
    {
        if (beforeBpm <= 0f || afterBpm <= 0f) return;
        float relativeChange = Mathf.Abs(afterBpm - beforeBpm) /
                               Mathf.Max(beforeBpm, afterBpm);
        // Imported tempo maps commonly use small staged changes (for example
        // Melodiniq starts 193 -> 196 BPM, only about 1.6%). A 5% threshold
        // missed those entirely. One percent still rejects normal 1 ms JSON
        // rounding noise while retaining intentional gradual acceleration.
        if (relativeChange < 0.01f) return;

        int direction = CompareFactor(afterBpm, beforeBpm);
        for (int i = 0; i < detectedTempoChanges.Count; i++)
        {
            DetectedTempoChange existing = detectedTempoChanges[i];
            if (CompareFactor(existing.afterBpm, existing.beforeBpm) != direction)
                continue;
            int timeDistance = Mathf.Abs(existing.timeMs - timeMs);
            bool sameTempoLevels =
                Mathf.Abs(existing.beforeBpm - beforeBpm) /
                    Mathf.Max(existing.beforeBpm, beforeBpm) < 0.005f &&
                Mathf.Abs(existing.afterBpm - afterBpm) /
                    Mathf.Max(existing.afterBpm, afterBpm) < 0.005f;
            if (timeDistance <= mergeWindowMs ||
                (sameTempoLevels && timeDistance <= mergeWindowMs * 2))
                return;
        }

        detectedTempoChanges.Add(new DetectedTempoChange
        {
            timeMs = timeMs,
            beforeBpm = beforeBpm,
            afterBpm = afterBpm
        });
    }

    // startInterval is the index of beat[startInterval] -> beat[startInterval + 1].
    private float MedianIntervalRange(int startInterval, int count)
    {
        reusableIntList.Clear();
        List<int> beats = chart.beat_timings;
        int end = Mathf.Min(startInterval + count, beats.Count - 1);
        for (int i = Mathf.Max(0, startInterval); i < end; i++)
        {
            int span = beats[i + 1] - beats[i];
            if (span > 0) reusableIntList.Add(span);
        }
        if (reusableIntList.Count == 0) return 0f;
        reusableIntList.Sort();
        return reusableIntList[reusableIntList.Count / 2];
    }

    private bool IsStableIntervalRange(int startInterval, int count, float median)
    {
        if (median <= 0f) return false;
        List<int> beats = chart.beat_timings;
        int end = Mathf.Min(startInterval + count, beats.Count - 1);
        int valid = 0;
        int nearMedian = 0;
        for (int i = Mathf.Max(0, startInterval); i < end; i++)
        {
            int span = beats[i + 1] - beats[i];
            if (span <= 0) continue;
            valid++;
            if (Mathf.Abs(span - median) / median <= 0.10f)
                nearMedian++;
        }
        return valid >= count && nearMedian >= count - 1;
    }

    private float MedianIntervalAround(int timeMs, bool after)
    {
        reusableIntList.Clear();
        List<int> beats = chart.beat_timings;
        if (after)
        {
            for (int i = 1; i < beats.Count && reusableIntList.Count < 8; i++)
            {
                if (beats[i - 1] < timeMs) continue;
                int span = beats[i] - beats[i - 1];
                if (span > 0) reusableIntList.Add(span);
            }
        }
        else
        {
            for (int i = beats.Count - 1; i >= 1 && reusableIntList.Count < 8; i--)
            {
                if (beats[i] > timeMs) continue;
                int span = beats[i] - beats[i - 1];
                if (span > 0) reusableIntList.Add(span);
            }
        }
        if (reusableIntList.Count == 0) return 0f;
        reusableIntList.Sort();
        return reusableIntList[reusableIntList.Count / 2];
    }

    private int GetDenominatorAt(int timeMs)
    {
        int denominator = timeSigDenominator > 0 ? timeSigDenominator : 4;
        if (chart != null && chart.time_signature_changes != null)
        {
            for (int i = 0; i < chart.time_signature_changes.Count; i++)
            {
                TimeSigChange change = chart.time_signature_changes[i];
                if (change == null || change.time_ms > timeMs) break;
                if (change.denominator > 0) denominator = change.denominator;
            }
        }
        return denominator;
    }

    private float CalculateAnchoredBpm(float intervalMs, int denominator)
    {
        if (intervalMs <= 0f || chart == null || anchorBpm <= 0f ||
            referenceBeatIntervalMs <= 0f) return 0f;
        float denominatorScale = timeSigDenominator > 0 && denominator > 0
            ? (float)timeSigDenominator / denominator
            : 1f;
        return anchorBpm * referenceBeatIntervalMs / intervalMs *
            denominatorScale;
    }

    private float GetMeasureWarningStart(int changeTimeMs, float currentFactor)
    {
        float measureDurationMs =
            GetMeasureDurationMs(changeTimeMs, currentFactor, false);
        return Mathf.Max(0f, changeTimeMs - measureDurationMs * 4f);
    }

    private float GetMeasureDurationMs(
        int changeTimeMs, float currentFactor, bool afterChange)
    {
        int signatureTime = afterChange ? changeTimeMs : changeTimeMs - 1;
        GetTimeSignatureAt(signatureTime, out int numerator, out int denominator);
        float localInterval = MedianIntervalAround(changeTimeMs, afterChange);
        float bpm = CalculateAnchoredBpm(localInterval, denominator);
        if (bpm <= 0f)
            bpm = chart != null && chart.first_bpm > 0f ? chart.first_bpm : 120f;
        bpm *= Mathf.Max(0.01f, currentFactor);

        // beat_timings is not uniform across imported charts: some charts store
        // subdivisions, while others store one timestamp per whole measure.
        // Calculate four musical measures from BPM and time signature instead
        // of subtracting a fixed number of timing entries.
        float beatsInMeasure = Mathf.Max(1, numerator) * 4f /
                               Mathf.Max(1, denominator);
        return 60000f * beatsInMeasure / bpm;
    }

    private void GetTimeSignatureAt(int timeMs, out int numerator, out int denominator)
    {
        numerator = timeSigNumerator > 0 ? timeSigNumerator : 4;
        denominator = timeSigDenominator > 0 ? timeSigDenominator : 4;
        if (chart == null || chart.time_signature_changes == null) return;
        for (int i = 0; i < chart.time_signature_changes.Count; i++)
        {
            TimeSigChange change = chart.time_signature_changes[i];
            if (change == null || change.time_ms > timeMs) break;
            if (change.numerator > 0) numerator = change.numerator;
            if (change.denominator > 0) denominator = change.denominator;
        }
    }

    private static int CompareFactor(float next, float previous)
    {
        if (next > previous + 0.0001f) return 1;
        if (next < previous - 0.0001f) return -1;
        return 0;
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
        referenceBeatIntervalMs = MedianDelta(chart.beat_timings);
        // 這是 HUD 上顯示的 BPM 數字，和音符流速無關（那邊看密度，見 ChartScrollSpeed）。
        anchorBpm = ChartTempo.ResolveBpm(chart, beatsPerMeasure);
        try
        {
            if (anchorBpm > 0f && chart.beat_timings.Count >= 2)
            {
                int sampleCount = Mathf.Min(16, chart.beat_timings.Count - 1);
                var deltas = new System.Collections.Generic.List<int>(sampleCount);
                for (int i = 1; i <= sampleCount; i++) deltas.Add(chart.beat_timings[i] - chart.beat_timings[i - 1]);
                deltas.Sort();
                int median = deltas[deltas.Count / 2];
                float expected = 60000f / Mathf.Max(1f, anchorBpm);
                // Number of timing-grid entries per BPM beat. This is metadata
                // only; BPM calculation below uses the relative interval.
                float approx = expected / Mathf.Max(1f, median);
                int r = Mathf.Clamp(Mathf.RoundToInt(approx), 1, 16);
                detectedSubdivision = r;
            }
        }
        catch { detectedSubdivision = 1; }

        // If we auto-detected a subdivision (e.g. beat_timings are in smaller units), use it
        int effectiveSubdivision = beatUnitSubdivision > 1 ? beatUnitSubdivision : detectedSubdivision;
        if (effectiveSubdivision <= 0) effectiveSubdivision = 1;
        effectiveBeatSubdivision = effectiveSubdivision;
        BuildDetectedTempoChanges();
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
            float bpm = anchorBpm > 0f && referenceBeatIntervalMs > 0f
                ? anchorBpm * (referenceBeatIntervalMs * beatsPerMeasure) / span
                : 60000f * conventionalBeatsPerMeasure / (float)span;
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
                float bpm = anchorBpm > 0f && referenceBeatIntervalMs > 0f
                    ? anchorBpm * referenceBeatIntervalMs / avgDelta
                    : 60000f / (avgDelta * beatUnitSubdivision);
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

    /// <summary>
    /// 這個時間點的速度。往回看一小段拍點，取**間距的中位數**當一拍。
    /// </summary>
    /// <remarks>
    /// **為什麼不能只看相鄰那一個間距**（舊版的做法）。拍點只存整數毫秒，每個
    /// 間距都帶 ±1ms 的進位誤差，而那個誤差換算成 BPM 要除以間距本身 —— 速度
    /// 越快，同樣的 1ms 就越兇：
    ///
    /// | 速度 | 一拍 | ±1ms 換算成 BPM |
    /// |---|---|---|
    /// | 120 | 500ms | ±0.24 |
    /// | 180 | 333ms | ±0.54 |
    /// | 666 | 90ms  | **±7.4** |
    ///
    /// 使用者報的 `666` 那首實測（宣告 666 BPM、一拍 90ms、整首定速）：1308 個
    /// 間距是 90×838、91×235、89×235，顯示出來就是 **666 / 659 / 673 三個數字
    /// 輪流跳，1308 拍裡換了 823 次** —— 一秒鐘跳七次，而譜面一點變速都沒有。
    ///
    /// **為什麼是中位數不是平均。** 間距只會是相鄰的兩三個整數，中位數直接選到
    /// 主值（同一首改成 16 筆窗的中位數之後，1308 拍裡 1307 拍顯示 666，整首只
    /// 換 2 次）。平均會算出 90.07 這種值，換算回去又要四捨五入，等於把問題搬個
    /// 位置；而且譜尾那一筆**收尾標記**（每份譜的最後一筆拍點本來就不在格線上）
    /// 會把平均拉走，中位數對它免疫。
    ///
    /// **窗長兩個條件誰先到算誰。** 固定筆數在慢曲上會把窗拉成好幾秒（60 BPM
    /// 的 16 拍 = 16 秒），真的變速就跟不上；固定時間在快曲上又只蓋到兩三筆，
    /// 雜訊壓不下來。代價是真的變速時數字會晚半個窗才換（666 BPM 約 0.6 秒），
    /// 那是刻意的取捨：這個欄位是給人讀的，不是判定用的。
    /// </remarks>
    private float ComputeInstantBpmAtSongPos(float songPosMs)
    {
        if (chart == null || chart.beat_timings == null || chart.beat_timings.Count < 2) return 0f;
        var beats = chart.beat_timings;
        // FindBeatIndexForSongPos 給的是「下一個拍點」，所以 idx-1..idx 就是現在
        // 正在走的那一個間距；窗從它開始往回鋪。
        int idx = Mathf.Clamp(FindBeatIndexForSongPos(songPosMs), 1, beats.Count - 1);

        instantIntervals.Clear();
        float gathered = 0f;
        for (int i = idx; i > 0; i--)
        {
            int delta = beats[i] - beats[i - 1];
            if (delta <= 0) continue;                   // 同一刻的重複拍點
            instantIntervals.Add(delta);
            gathered += delta;
            if (instantIntervals.Count >= InstantWindowMaxIntervals) break;
            if (gathered >= InstantWindowTargetMs && instantIntervals.Count >= 3) break;
        }
        if (instantIntervals.Count == 0) return 0f;
        instantIntervals.Sort();
        int span = instantIntervals[instantIntervals.Count / 2];
        if (span <= 0) return 0f;

        int sub = Mathf.Max(1, effectiveBeatSubdivision);
        float denominatorScale = timeSigDenominator > 0 && currentTimeSigDenominator > 0
            ? (float)timeSigDenominator / currentTimeSigDenominator
            : 1f;
        float bpm = chart.first_bpm > 0f && referenceBeatIntervalMs > 0f
            ? chart.first_bpm * referenceBeatIntervalMs / span * denominatorScale
            : 60000f / (span * sub);
        if (enableDebug)
        {
            try
            {
                BuildLogger.Log($"[BpmDisplay] instant: idx={idx} window={instantIntervals.Count}" +
                                $" medianMs={span} sub={sub} -> bpm={bpm:F2}");
            }
            catch { }
        }
        return bpm;
    }
}
