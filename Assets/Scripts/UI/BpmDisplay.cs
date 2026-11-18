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
    private int lastDisplayedBpm = int.MinValue;
    private string lastDisplayedTs = null;
    private int lastSpeedDirection;
    private bool lastSpeedWarning;
    /// <summary>目前印在畫面上的「等一下的 BPM」。0 ＝ 沒有預告。</summary>
    private int lastPendingBpm;
    private int lastPendingTimeMs;
    private int lastPendingDirection;

    // ── 預告牌（BPM 左邊那一塊）和交接動畫 ──────────────────────────────
    private TextMeshProUGUI pendingText;
    private BpmChangeFrame pendingFrame;
    private BpmChangeArrow pendingArrow;
    private int pendingTextValue;
    private float pendingDigitsMinX, pendingDigitsMaxX;
    private float pendingDigitsCentreY, pendingDigitsHeight;

    private enum HandoverPhase { None, Moving }
    private HandoverPhase handover;
    private float handoverStart;
    private int handoverFromBpm;
    private int handoverToBpm;
    /// <summary>交接完之後，讀數先鎖在宣告值，等量測追上來。</summary>
    private int overrideBpm;
    private float overrideUntil;

    /// <summary>鎖最晚活到什麼時候（不受 <see cref="FinishHandover"/> 延長）。</summary>
    private float overrideHardLimit;

    /// <summary>白框和數字之間留多少，以數字高為單位。</summary>
    private const float PlatePadX = 0.34f;
    private const float PlatePadY = 0.26f;
    /// <summary>預告牌、三角形、BPM 標籤三者之間的間距，以數字高為單位。</summary>
    private const float PlateGap = 0.40f;
    /// <summary>三角形多高，以數字高為單位。</summary>
    private const float PlateArrowHeight = 0.50f;
    /// <summary>交接動畫多久。</summary>
    /// <remarks>
    /// 短於 0.25 秒會讀成「閃一下換掉」，長於 0.6 秒則會在下一顆音符落下時還在
    /// 路上 —— 變速點附近通常正好是譜面最忙的地方。
    /// </remarks>
    private const float HandoverSeconds = 0.42f;
    /// <summary>交接之後讀數鎖多久。</summary>
    /// <remarks>
    /// 即時 BPM 是往回看一整個窗（最多 1.2 秒）的中位數，所以越過變速點之後它
    /// 還會回報舊的速度好一陣子。不鎖的話，新值才剛移到位就被量測拉回舊值。
    /// </remarks>
    private const float HandoverHoldSeconds = 1.8f;

    /// <summary>變快：淺藍。</summary>
    private static readonly Color FasterTint = new Color(0.624f, 0.831f, 0.925f, 1f);
    /// <summary>變慢：紅。</summary>
    private static readonly Color SlowerTint = new Color(0.855f, 0.396f, 0.424f, 1f);
    /// <summary>落地之後就是一般讀數的顏色。</summary>
    private static readonly Color SettledTint = new Color(0.961f, 0.910f, 0.788f, 1f);
    /// <summary>白框。偏暖的白，純白在這一套金／羊皮紙的配色裡會跳出來。</summary>
    private static readonly Color PlateFrameTint = new Color(0.953f, 0.937f, 0.898f, 1f);
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

        /// <summary>整段變完的時刻。單一次變速就等於 <see cref="timeMs"/>。</summary>
        /// <remarks>
        /// 漸快／漸慢在拍點表上是一串小變速，偵測會一筆一筆抓到。那一串要當成
        /// **一次**事件：預告在開頭就出現、顯示的是整串走完的目標值，而交接動畫
        /// 等到這個時刻才演。不這樣分的話，每一小步都想自己放一次動畫，而步與步
        /// 之間比一次動畫還短 —— 畫面就一直在演上一步的交接。
        /// </remarks>
        public int settleTimeMs;

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
    /// HUD 上那兩行字。**數字不隨快慢變色**，變速改用「舊值 ▶ 新值」預告。
    /// </summary>
    /// <remarks>
    /// 以前加速會把數字染成藍色、減速染成紅色（2026-11-10 使用者要求取消）。
    /// 現在這一行**完全不動**：「BPM」三個字和現在生效的讀數留在原位，
    /// 「等一下的速度」另外掛一個 TMP 在它左邊（<see cref="pendingText"/>），
    /// 白框框住、變快是淺藍、變慢是紅，旁邊一個三角形指向它等一下要去的位置。
    /// 到了變速點，這一行的讀數淡出，左邊那個直接移過來補上
    /// （<see cref="BeginHandover"/>）。
    ///
    /// <paramref name="valueAlpha"/> 就是那個淡出。
    ///
    /// 速度變化的偵測（<see cref="ResolveSpeedVisual"/>、<c>detectedTempoChanges</c>）
    /// 一行都沒動 —— 當初取消上色時特地留著的就是這個，換提示方式不必重做。
    ///
    /// **現在的值要淡一階。** 兩個同樣亮的數字並排，眼睛不知道該讀哪一個；
    /// 預告的重點是右邊那個，左邊只是「從哪裡來」。
    ///
    /// 三角形不是字元，是 <see cref="BpmChangeArrow"/> 畫的網格 —— 主字型沒有
    /// U+25B6。<c>&lt;space&gt;</c> 空出來的縫就是給它的。
    ///
    /// 顏色寫在數字自己的 <c>&lt;color&gt;</c> 標籤裡，不用 <c>overrideColorTags</c>：
    /// 後者會把整塊（含 TIME SIGNATURE／BPM 兩個標籤）一起上色。
    /// </remarks>
    private static string FormatBpmText(string timeSignature, int bpm, float valueAlpha)
    {
        string ts = string.IsNullOrWhiteSpace(timeSignature) ? "-" : timeSignature;
        string bpmValue = bpm > 0 ? bpm.ToString() : "-";
        // 淡出寫成 alpha 標籤，不動字的寬度 —— 這一行是靠右對齊的，寬度一變
        // 「BPM」三個字就會跟著跑，而交接的前提正是它不准動。
        string fade = valueAlpha >= 0.999f
            ? string.Empty
            : $"<alpha=#{Mathf.RoundToInt(Mathf.Clamp01(valueAlpha) * 255f):X2}>";
        // 字級是絕對點數，不隨 fontSize 縮放 —— 要改大小就得改這裡。
        //
        // 兩個標籤（TIME SIGNATURE／BPM）是說明不是讀數，壓到最小還讀得出就好。
        // 數字從 58 降到 44：這一塊現在貼在分數下面，而分數是 48 —— 比分數大的
        // 速度值會把視線先拉到這裡，但這一局真正要看的是分數。
        return $"<size=15><color=#D9BC76>TIME SIGNATURE</color>  {ts}</size>\n" +
               $"<size=18><color=#D9BC76>BPM</color></size>  " +
               $"<size=44><color=#F5E8C9>{fade}{bpmValue}</color></size>";
    }

    /// <summary>
    /// 預告牌和交接動畫。回傳主字串上那個讀數現在該有多淡（1 ＝ 不淡）。
    /// </summary>
    /// <remarks>
    /// **為什麼是獨立的一個 TMP，不是寫進同一行字裡。** 兩個理由。一是那一行
    /// 靠右對齊：只要把預告塞進同一個字串，整行就往左推，連「BPM」三個字一起
    /// 跑。獨立的物件疊在旁邊，主字串一個字元都不用改。二是交接要動 ——
    /// 字串裡的字沒辦法自己走路。
    ///
    /// **位置是從 TMP 自己的字框讀出來的，不是算出來的。** 這一塊的字級是寫死的
    /// 絕對點數、對齊是 TopRight、數字還會在兩位和三位之間變 —— 任何「從右邊緣
    /// 往左推幾個像素」的算法都會在某一首歌上錯開。<c>textInfo</c> 裡的每一個字
    /// 都帶著自己畫出來的框，那是唯一不會錯的來源，而且兩邊（主讀數和預告值）
    /// 都量，所以預告的字高永遠跟著讀數走，不必各自對字級。
    ///
    /// 位移只有水平一個方向：兩個數字本來就在同一條基線上，讓它「橫著滑過去」
    /// 比走一條斜線更像同一個東西換了位置。
    /// </remarks>
    private float UpdatePendingVisual(int pendingBpm, int direction, float now)
    {
        bool moving = handover == HandoverPhase.Moving;
        if ((pendingBpm <= 0 && !moving) || bpmText == null)
        {
            ShowPending(false);
            return 1f;
        }

        // 動畫跑到一半讀不到錨點（讀數變成「-」、文字還沒生成）就直接收掉。
        // 不收的話 handover 會卡在 Moving：節流被它繞過、讀數也被它鎖住。
        if (!ResolveLineAnchors(out float labelLeft, out float valueRight,
                                out float centreY, out float digitHeight,
                                out float ceilingY))
        {
            if (moving) FinishHandover(now);
            ShowPending(false);
            return 1f;
        }

        EnsurePendingPlate();
        if (pendingText == null)
        {
            if (moving) FinishHandover(now);
            return 1f;
        }

        int shown = moving ? handoverToBpm : pendingBpm;
        if (shown <= 0)
        {
            if (moving) FinishHandover(now);
            ShowPending(false);
            return 1f;
        }
        if (pendingTextValue != shown || pendingDigitsHeight <= 0.01f)
        {
            pendingTextValue = shown;
            pendingText.SetText(shown.ToString());
            pendingText.ForceMeshUpdate();
            MeasurePendingDigits();
        }
        if (pendingDigitsHeight <= 0.01f)
        {
            if (moving) FinishHandover(now);
            ShowPending(false);
            return 1f;
        }

        // 預告的字高跟著讀數走：兩邊都是量出來的，所以字級哪天改了也不用重調。
        float scale = digitHeight / pendingDigitsHeight;
        float padX = pendingDigitsHeight * PlatePadX * scale;
        float gap = digitHeight * PlateGap;
        float arrowHeight = digitHeight * PlateArrowHeight;
        float arrowWidth = arrowHeight * 0.92f;
        float arrowRight = labelLeft - gap;
        float homeRight = arrowRight - arrowWidth - gap - padX;

        float move = 0f, frameFade = 1f, valueAlpha = 1f;
        if (moving)
        {
            float t = Mathf.Clamp01((now - handoverStart) / HandoverSeconds);
            // 位置用 ease-out：一開始就有速度，到位的時候收住。
            move = 1f - Mathf.Pow(1f - t, 3f);
            // 框和三角形先走 —— 它們是「還沒發生」的標記，數字還在路上就該退場。
            frameFade = 1f - Mathf.Clamp01(t / 0.55f);
            // 舊讀數要在新值蓋上來之前淡乾淨，不然兩個數字會有一段互相疊著。
            valueAlpha = 1f - Mathf.Clamp01(t / 0.70f);
            if (t >= 1f) FinishHandover(now);
        }

        float targetRight = Mathf.Lerp(homeRight, valueRight, move);
        RectTransform rect = pendingText.rectTransform;
        rect.localScale = Vector3.one * scale;
        rect.localPosition = new Vector3(
            targetRight - pendingDigitsMaxX * scale,
            centreY - pendingDigitsCentreY * scale,
            0f);

        Color tint = direction < 0 ? SlowerTint : FasterTint;
        if (moving) tint = Color.Lerp(lastPendingDirection < 0 ? SlowerTint : FasterTint,
                                      SettledTint, move);
        pendingText.color = tint;

        if (pendingFrame != null)
        {
            // 框掛在數字底下當小孩，所以它自己會跟著移動和縮放。
            RectTransform frame = pendingFrame.rectTransform;
            float padLocalX = pendingDigitsHeight * PlatePadX;
            // **框的上緣不能頂到上面那一行。** 兩行的行高是 TMP 照各自最大的字
            // 算出來的，這裡不去猜 —— 直接問第一行的底在哪，框就停在它下面。
            float padWorldY = pendingDigitsHeight * PlatePadY * scale;
            if (ceilingY < float.MaxValue)
            {
                float headroom = ceilingY - (centreY + digitHeight * 0.5f) - 2f;
                padWorldY = Mathf.Clamp(Mathf.Min(padWorldY, headroom),
                    digitHeight * 0.10f, padWorldY);
            }
            float padLocalY = padWorldY / Mathf.Max(0.0001f, scale);
            frame.sizeDelta = new Vector2(
                pendingDigitsMaxX - pendingDigitsMinX + padLocalX * 2f,
                pendingDigitsHeight + padLocalY * 2f);
            frame.localPosition = new Vector3(
                (pendingDigitsMinX + pendingDigitsMaxX) * 0.5f, pendingDigitsCentreY, 0f);
            Color frameColour = PlateFrameTint;
            frameColour.a = frameFade;
            pendingFrame.color = frameColour;
            if (pendingFrame.gameObject.activeSelf != frameFade > 0.01f)
                pendingFrame.gameObject.SetActive(frameFade > 0.01f);
        }

        if (pendingArrow != null)
        {
            RectTransform arrow = pendingArrow.rectTransform;
            arrow.sizeDelta = new Vector2(arrowWidth, arrowHeight);
            arrow.localPosition = new Vector3(arrowRight - arrowWidth * 0.5f, centreY, 0f);
            Color arrowColour = new Color(0.851f, 0.737f, 0.463f, frameFade);
            pendingArrow.color = arrowColour;
            if (pendingArrow.gameObject.activeSelf != frameFade > 0.01f)
                pendingArrow.gameObject.SetActive(frameFade > 0.01f);
        }

        if (!pendingText.gameObject.activeSelf) pendingText.gameObject.SetActive(true);
        return valueAlpha;
    }

    /// <summary>
    /// 越過變速點了：舊讀數淡出，預告值從左邊移過來補上。
    /// </summary>
    /// <remarks>
    /// **為什麼要鎖住讀數。** 即時 BPM 是往回看一整個窗的中位數，越過變速點之後
    /// 它還會回報舊的速度好一陣子；不鎖的話新值才剛移到位就被量測拉回去，看起來
    /// 像動畫演完又彈回來。鎖的是我們自己從拍點表上宣告過的那個值，不是猜的。
    /// </remarks>
    private void BeginHandover(int fromBpm, int toBpm, float now, float songNowMs)
    {
        handover = HandoverPhase.Moving;
        handoverStart = now;
        handoverFromBpm = fromBpm > 0 ? fromBpm : toBpm;
        handoverToBpm = toBpm;
        overrideBpm = toBpm;
        overrideUntil = now + HandoverSeconds + HandoverHoldSeconds;

        // **鎖不能活過下一次變速。** 鎖的用意是撐過「量測還在回報舊速度」那
        // 一兩秒，可是兩次變速之間不一定有那麼久。不夾的話，下一次變速的開頭
        // 讀數還黏在上一個目標上 —— 那就是「一直變就完全不對」裡最刺眼的一段。
        overrideHardLimit = overrideUntil;
        int nextChange = NextTempoChangeAfter(songNowMs);
        if (songNowMs >= 0f && nextChange != int.MaxValue)
        {
            float factor = gm != null
                ? Mathf.Max(0.01f, gm.CurrentAudioSpeedFactor) : 1f;
            float realSeconds = (nextChange - songNowMs) / 1000f / factor;
            overrideHardLimit = Mathf.Min(overrideHardLimit,
                now + Mathf.Max(HandoverSeconds, realSeconds));
            overrideUntil = Mathf.Min(overrideUntil, overrideHardLimit);
        }
    }

    /// <summary>這個時刻之後的下一次變速；沒有就回 <c>int.MaxValue</c>。</summary>
    private int NextTempoChangeAfter(float songPositionMs)
    {
        for (int i = 0; i < detectedTempoChanges.Count; i++)
        {
            DetectedTempoChange tempo = detectedTempoChanges[i];
            if (tempo != null && tempo.timeMs > songPositionMs) return tempo.timeMs;
        }
        return int.MaxValue;
    }

    private void FinishHandover(float now)
    {
        handover = HandoverPhase.None;
        ShowPending(false);
        overrideUntil = Mathf.Max(overrideUntil, now + HandoverHoldSeconds);
        if (overrideHardLimit > 0f)
            overrideUntil = Mathf.Min(overrideUntil, overrideHardLimit);
    }

    private void ShowPending(bool show)
    {
        if (pendingText != null && pendingText.gameObject.activeSelf != show)
            pendingText.gameObject.SetActive(show);
        if (pendingFrame != null && pendingFrame.gameObject.activeSelf != show)
            pendingFrame.gameObject.SetActive(show);
        if (pendingArrow != null && pendingArrow.gameObject.activeSelf != show)
            pendingArrow.gameObject.SetActive(show);
    }

    /// <summary>主字串最後一行上的三個錨點：BPM 標籤的左緣、讀數的右緣和中心。</summary>
    private bool ResolveLineAnchors(out float labelLeft, out float valueRight,
        out float centreY, out float digitHeight, out float ceilingY)
    {
        labelLeft = valueRight = centreY = digitHeight = 0f;
        ceilingY = float.MaxValue;
        TMP_TextInfo info = bpmText.textInfo;
        if (info == null || info.characterCount <= 0 || info.lineCount <= 0) return false;
        TMP_LineInfo line = info.lineInfo[info.lineCount - 1];
        if (info.lineCount > 1) ceilingY = info.lineInfo[info.lineCount - 2].descender;
        int first = Mathf.Max(0, line.firstCharacterIndex);
        int last = Mathf.Min(line.lastCharacterIndex, info.characterCount - 1);
        if (last < first) return false;

        labelLeft = info.characterInfo[first].bottomLeft.x;
        int firstDigit = -1, lastDigit = -1;
        for (int i = first; i <= last; i++)
        {
            char ch = info.characterInfo[i].character;
            if (ch < '0' || ch > '9') continue;
            if (firstDigit < 0) firstDigit = i;
            lastDigit = i;
        }
        if (firstDigit < 0) return false;

        TMP_CharacterInfo digit = info.characterInfo[firstDigit];
        valueRight = info.characterInfo[lastDigit].topRight.x;
        centreY = (digit.topRight.y + digit.bottomLeft.y) * 0.5f;
        digitHeight = digit.topRight.y - digit.bottomLeft.y;
        return digitHeight > 0.01f;
    }

    /// <summary>預告值自己那幾個數字實際畫出來的框（它自己的座標系）。</summary>
    private void MeasurePendingDigits()
    {
        pendingDigitsMinX = pendingDigitsMaxX = 0f;
        pendingDigitsCentreY = pendingDigitsHeight = 0f;
        TMP_TextInfo info = pendingText != null ? pendingText.textInfo : null;
        if (info == null || info.characterCount <= 0) return;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo c = info.characterInfo[i];
            if (c.character < '0' || c.character > '9') continue;
            minX = Mathf.Min(minX, c.bottomLeft.x);
            maxX = Mathf.Max(maxX, c.topRight.x);
            minY = Mathf.Min(minY, c.bottomLeft.y);
            maxY = Mathf.Max(maxY, c.topRight.y);
        }
        if (minX > maxX) return;
        pendingDigitsMinX = minX;
        pendingDigitsMaxX = maxX;
        pendingDigitsCentreY = (minY + maxY) * 0.5f;
        pendingDigitsHeight = maxY - minY;
    }

    private void EnsurePendingPlate()
    {
        if (pendingText != null || bpmText == null) return;
        try
        {
            var host = new GameObject("BpmPendingPlate", typeof(RectTransform));
            host.layer = bpmText.gameObject.layer;
            RectTransform rect = host.GetComponent<RectTransform>();
            rect.SetParent(bpmText.rectTransform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(220f, 80f);

            pendingText = host.AddComponent<TextMeshProUGUI>();
            // 字型和描邊照抄主讀數，不然它讀起來是別的系統印上去的東西。
            if (bpmText.font != null) pendingText.font = bpmText.font;
            pendingText.fontSize = 44f;
            pendingText.fontStyle = bpmText.fontStyle;
            pendingText.alignment = TextAlignmentOptions.Midline;
            pendingText.textWrappingMode = TextWrappingModes.NoWrap;
            pendingText.overflowMode = TextOverflowModes.Overflow;
            pendingText.enableAutoSizing = false;
            pendingText.raycastTarget = false;
            pendingText.maskable = false;
            pendingText.richText = false;
            // outlineWidth 的 setter 會直接去動 fontMaterial，字型還沒解析好時
            // 那是 null —— 先確認材質在，再描邊。
            if (pendingText.fontSharedMaterial != null)
            {
                pendingText.outlineWidth = bpmText.outlineWidth;
                pendingText.outlineColor = bpmText.outlineColor;
            }
            pendingText.SetText("0");
            pendingText.ForceMeshUpdate();

            var frameHost = new GameObject("Frame", typeof(RectTransform));
            frameHost.layer = host.layer;
            RectTransform frameRect = frameHost.GetComponent<RectTransform>();
            frameRect.SetParent(rect, false);
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            pendingFrame = frameHost.AddComponent<BpmChangeFrame>();
            pendingFrame.color = PlateFrameTint;
            pendingFrame.raycastTarget = false;
            pendingFrame.maskable = false;

            var arrowHost = new GameObject("BpmPendingArrow", typeof(RectTransform));
            arrowHost.layer = host.layer;
            RectTransform arrowRect = arrowHost.GetComponent<RectTransform>();
            arrowRect.SetParent(bpmText.rectTransform, false);
            arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
            arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            pendingArrow = arrowHost.AddComponent<BpmChangeArrow>();
            // **尖端朝右。** 朝左（指著新值）試過，讀起來是反的：這個三角形
            // 旁邊就是要移過去的那個數字，而它等一下真的會往右走到讀數的位置
            // ——箭頭指的是那件事。指回自己那一格，等於在說「看這裡」，
            // 可是那個數字本來就已經被白框框起來了。
            pendingArrow.PointsLeft = false;
            pendingArrow.color = new Color(0.851f, 0.737f, 0.463f, 1f);
            pendingArrow.raycastTarget = false;
            pendingArrow.maskable = false;

            ShowPending(false);
            pendingTextValue = 0;
            pendingDigitsHeight = 0f;
        }
        catch (System.Exception ex)
        {
            try { BuildLogger.LogWarning($"[BpmDisplay] plate: {ex.Message}"); } catch { }
        }
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
            lastPendingBpm = 0;
            lastPendingTimeMs = 0;
            lastPendingDirection = 0;
            handover = HandoverPhase.None;
            overrideBpm = 0;
            overrideUntil = 0f;
            overrideHardLimit = 0f;
            ShowPending(false);
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
        // 交接動畫要逐幀跑 —— 照 20Hz 的節流走，那段位移會變成一格一格跳。
        if (!chartChanged && handover == HandoverPhase.None && now < nextUiRefreshTime) return;
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

            // 還沒讀到譜面，當然也還不知道有沒有變速。
            ShowPending(false);
            newText = FormatBpmText(headerTs, headerBpm > 0f ? Mathf.RoundToInt(headerBpm) : 0, 1f);
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
        // 乘上播放倍率**之前**的那個值。預告要用它去推「等一下是多少」——
        // 拿已經乘過的顯示值再乘一次倍率，倍率就被算了兩遍。
        float rawBpm = primaryBpm;
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
                    rawBpm = instBpm;
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
                        rawBpm = currentMeasureBpm;
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
            rawBpm = primaryBpm;
            displayBpm = Mathf.RoundToInt(primaryBpm * speedFactor);
            secondLine = $"BPM：{displayBpm}";
            wroteBpm = true;
        }
        // Build the display text once and only update TMP when it changed to avoid allocations
        // Only update visible text when either time signature or rounded BPM changed
        int speedDirection = 0;
        bool speedWarning = false;
        float pendingRawBpm = 0f;
        float pendingFactor = speedFactor;
        int pendingTimeMs = 0;
        float songNowMs = conductor != null ? conductor.effectiveSongPosition : -1f;
        if (conductor != null)
            ResolveSpeedVisual(songNowMs, out speedDirection,
                out speedWarning, out pendingRawBpm, out pendingFactor, out pendingTimeMs);
        // 速度事件（整首放慢放快）本身不改拍速，只改倍率 —— 那種情況下
        // pendingRawBpm 是 0，等一下的讀數就是現在這個拍速乘上新的倍率。
        int pendingBpm = 0;
        if (speedWarning && speedDirection != 0)
        {
            float basis = pendingRawBpm > 0f ? pendingRawBpm : rawBpm;
            if (basis > 0f) pendingBpm = Mathf.RoundToInt(basis * pendingFactor);
            // 兩邊四捨五入到同一個整數就不是「變化」，別預告一個一樣的數字。
            if (pendingBpm == displayBpm) pendingBpm = 0;
        }

        // 預告值消失的那一幀就是越過變速點的那一幀 —— 接著開始交接。
        // 條件寫得嚴一點：暫停、倒帶、換歌、重算拍點表都會讓預告值消失，
        // 但那些都不是「剛剛越過」，不該放一段動畫。
        if (handover == HandoverPhase.None && lastPendingBpm > 0 && pendingBpm == 0 &&
            conductor != null && conductor.isActive && lastPendingTimeMs > 0 &&
            songNowMs >= lastPendingTimeMs && songNowMs - lastPendingTimeMs <= 600f)
        {
            BeginHandover(lastDisplayedBpm > 0 ? lastDisplayedBpm : displayBpm,
                lastPendingBpm, now, songNowMs);
        }

        if (handover == HandoverPhase.Moving)
        {
            // 動畫期間讀數是「正在淡出的那個舊值」，新的那個在左邊自己走過來。
            displayBpm = handoverFromBpm;
            pendingBpm = 0;
        }
        else if (overrideBpm > 0)
        {
            // 鎖住宣告過的新值，等即時量測的窗推進到新的速度為止。
            if (now >= overrideUntil || displayBpm == overrideBpm) overrideBpm = 0;
            else displayBpm = overrideBpm;
        }

        float valueAlpha = 1f;
        try { valueAlpha = UpdatePendingVisual(pendingBpm, speedDirection, now); }
        catch (System.Exception ex)
        {
            try { BuildLogger.LogWarning($"[BpmDisplay] plate: {ex.Message}"); } catch { }
        }
        if (pendingBpm > 0)
        {
            lastPendingTimeMs = pendingTimeMs;
            lastPendingDirection = speedDirection;
        }
        lastPendingBpm = pendingBpm;

        if (displayBpm != lastDisplayedBpm || tsText != lastDisplayedTs ||
            handover == HandoverPhase.Moving ||
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
                    newText = FormatBpmText(tsText, displayBpm, valueAlpha);
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

    /// <summary>
    /// 現在在不在某一次變速的窗裡，以及那一次變速之後是多少。
    /// </summary>
    /// <remarks>
    /// <paramref name="pendingBpm"/> 只在**還沒越過**的那一段（變速點前四小節）
    /// 給值；越過之後是 0，因為那時候 <c>displayBpm</c> 自己已經是新的值了，
    /// 再印一次「舊 ▶ 新」等於把已經發生的事說成還要發生。
    ///
    /// 整首放慢放快的速度事件不改拍速、只改倍率，所以那種情況 pendingBpm 是 0、
    /// 由 <paramref name="pendingFactor"/> 帶新的倍率出去，呼叫端拿現在的拍速去乘。
    /// </remarks>
    private void ResolveSpeedVisual(float songPositionMs, out int direction, out bool warning,
        out float pendingBpm, out float pendingFactor, out int pendingTimeMs)
    {
        direction = 0;
        warning = false;
        pendingBpm = 0f;
        pendingFactor = 1f;
        pendingTimeMs = 0;
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
        int nextSettleTime = int.MaxValue;
        int nextDirection = 0;
        float nextBpm = 0f;
        float nextFactor = previousFactor;
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
                    nextBpm = 0f;          // 拍速不變，變的是倍率
                    nextFactor = factor;
                }
                factorBeforeEvent = factor;
            }
        }

        for (int i = 0; i < detectedTempoChanges.Count; i++)
        {
            DetectedTempoChange tempo = detectedTempoChanges[i];
            // 一串漸快要整串走完才算「過去」，否則爬坡中途就會被當成已經變完。
            if (tempo.settleTimeMs <= songPositionMs)
            {
                int tempoDirection = CompareFactor(tempo.afterBpm, tempo.beforeBpm);
                if (tempoDirection != 0 && tempo.settleTimeMs >= latestPastTime)
                {
                    latestPastTime = tempo.settleTimeMs;
                    latestPastDirection = tempoDirection;
                }
                continue;
            }
            if (tempo.timeMs < nextTime)
            {
                nextTime = tempo.timeMs;
                nextSettleTime = tempo.settleTimeMs;
                nextDirection = CompareFactor(tempo.afterBpm, tempo.beforeBpm);
                nextBpm = tempo.afterBpm;
                nextFactor = previousFactor;
            }
            break;
        }

        // An upcoming change owns the colour throughout its four-measure
        // warning window. Consecutive events therefore join without a white
        // frame between them.
        if (nextTime != int.MaxValue)
        {
            // 速度事件沒有「整段」的概念，它的穩定時刻就是它自己。
            int settleTime = nextSettleTime != int.MaxValue ? nextSettleTime : nextTime;
            float warningStart = GetMeasureWarningStart(nextTime, previousFactor);
            // 預告窗不能回溯到上一次變速之前：四小節在慢曲上是好幾秒，而兩次
            // 變速之間可能只隔一兩小節 —— 不夾的話，上一次還在交接，下一次的
            // 預告牌就已經蓋上來了。
            if (latestPastTime != int.MinValue)
                warningStart = Mathf.Max(warningStart, latestPastTime);
            if (songPositionMs >= warningStart && songPositionMs < settleTime)
            {
                direction = nextDirection;
                warning = direction != 0;
                pendingBpm = nextBpm;
                pendingFactor = nextFactor > 0f ? nextFactor : previousFactor;
                pendingTimeMs = settleTime;
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
                // 已經越過了：方向還留著（它還在決定要不要重畫），但不給預告值
                // —— 使用者要的是「變完只留新值」。
                direction = latestPastDirection;
                warning = true;
                pendingFactor = previousFactor;
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
                AddDetectedTempoChange(change.time_ms, beforeBpm, afterBpm, 0,
                    Mathf.Min(beforeSpan, afterSpan));
            }
        }

        // A tempo change does not require a time-signature change. Scan every
        // chart's timing grid for two stable neighbouring interval regions.
        // This also supports imported charts that only contain beat_timings.
        //
        // **窗是「一小節的筆數」，不是固定的四筆。** beat_timings 在這個曲庫裡有
        // 兩種意思：一小節一條，或一個四分音符一條（見拍點表的兩種意義）。寫死
        // 四筆的話，前者等於要求前後各四小節都穩定 —— 只維持一兩小節的變速
        // 完全看不到。改成照時間換算，兩種譜都剛好是一小節。
        int window = IntervalsPerMeasure();
        List<int> beats = chart.beat_timings;
        for (int boundary = window; boundary + window < beats.Count; boundary++)
        {
            float beforeSpan = MedianIntervalRange(boundary - window, window);
            float afterSpan = MedianIntervalRange(boundary, window);
            if (beforeSpan <= 0f || afterSpan <= 0f) continue;
            // 被分割線切開的那兩段不歸這一遍管：它們是**一個單位的兩半**，
            // 直接當成兩個單位去算，會得出一個兩倍快的假變速。交給下面
            // 照比例算的那一遍。
            // 三個起點都要看：這個邊界的前後兩段，各自都可能是某一對碎片的
            // **前半或後半**。只看一個的話，「後半 → 下一個完整單位」那個邊界
            // 會漏掉，而它正好會報出一個反方向的假變速（實測：純分割、速度沒變
            // 的譜面會冒出一次 240→120）。
            float unitHere = MedianIntervalRange(boundary - UnitWindow, UnitWindow);
            if (unitHere > 0f &&
                (IsSplitPair(boundary, unitHere) ||
                 IsSplitPair(boundary - 1, unitHere) ||
                 IsSplitPair(boundary - 2, unitHere)))
                continue;
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
                eventTime, beforeBpm, afterBpm, mergeWindowMs,
                Mathf.Min(beforeSpan, afterSpan));
        }

        // 小節內的分割。
        //
        // **一段碎片不是「一拍」。** 分割線把一個拍單位切成兩段，所以那兩段各自
        // 都比一個單位短 —— 拿它們直接去算 BPM（當成一個完整的單位）會得出一個
        // 一兩倍快的假速度，這正是這一遍原本寫錯的地方。
        //
        // 對的算法是先問「這一段佔了一個單位的幾分之幾」：
        //   f        = 前段 / 單位          分割點落在單位裡的位置
        //   單位(後) = 後段 / (1 - f)       後段用它自己的速度走完剩下的 (1-f)
        // 速度沒變的話，單位(後) 會算回原本的單位，差距進不了 1% 的門檻 ——
        // 所以「純粹切開、沒改速度」不會冒出一次變速，這是同一條式子的副產品。
        if (chart.measure_splits != null && chart.measure_splits.Count > 0)
        {
            // 製譜器自己記了分割點，不用猜。
            for (int i = 0; i < chart.measure_splits.Count; i++)
            {
                int index = FindBeatIndexAt(chart.measure_splits[i]);
                if (index > 0) AddSplitTempoChange(index);
            }
        }
        else
        {
            for (int interval = UnitWindow; interval + 2 < beats.Count; interval++)
            {
                // 合併檢查是對整張清單跑的，所以清單一大就變 O(N^2)。拍點表本身
                // 壞掉的譜（每一條都和鄰居不一樣）會一路加到底，給它一個天花板。
                if (detectedTempoChanges.Count >= MaxDetectedTempoChanges) break;
                float unit = MedianIntervalRange(interval - UnitWindow, UnitWindow);
                if (!IsSplitPair(interval, unit)) continue;
                AddSplitTempoChange(interval + 1);
            }
        }

        detectedTempoChanges.Sort((a, b) => a.timeMs.CompareTo(b.timeMs));
        CollapseDenseTempoChanges();
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

    /// <summary>兩次事件之間至少要留這麼久（歌曲毫秒）。</summary>
    /// <remarks>
    /// 下限是一次交接動畫走完的時間（<see cref="HandoverSeconds"/> 0.42 秒）再
    /// 加一點餘裕。比這個還密的話，下一次的預告會蓋在上一次的動畫上。
    /// </remarks>
    private const float MinEventSpacingMs = 700f;

    private readonly List<DetectedTempoChange> collapsedTempoChanges =
        new List<DetectedTempoChange>();

    /// <summary>
    /// 把「密到來不及一個一個演」的變速併成一次事件。
    /// </summary>
    /// <remarks>
    /// **為什麼要併。** 預告牌的規則是「變速前四小節出現，變完留下新值」。可是
    /// 匯入的速度圖常常把一次漸快寫成一串小變速：`felzione` 在 99～109 秒之間
    /// 從 129 爬到 160，拍點表上是八段，每段隔 1.2～2.3 秒。一段一塊預告牌的
    /// 話，每一塊都還在四小節預告窗裡就被下一塊接手 —— 預告值跳個不停、交接
    /// 動畫永遠演不完、讀數被上一個目標鎖著。使用者看到的就是「四小節內一直變
    /// 就完全不對」。
    ///
    /// 併起來之後那一串是**一次**事件：129 → 160，預告在 99 秒前四小節出現、
    /// 整段爬坡期間讀數照實跟著爬，到 109 秒才演一次交接。
    ///
    /// **不分方向。** 來回搖擺（慢-快-慢-快）也要併 —— 那是演奏的呼吸不是變速，
    /// 併完淨變化接近零，下面那道門檻會把它整個丟掉。
    ///
    /// 間隔門檻取「一小節」和一次動畫的時間裡大的那個：慢曲照小節走（音樂上的
    /// 一小節才是人感覺得到的單位），快曲照動畫走（一小節比動畫還短的時候，
    /// 照小節走等於沒併）。
    /// </remarks>
    private void CollapseDenseTempoChanges()
    {
        if (detectedTempoChanges.Count < 2) return;
        collapsedTempoChanges.Clear();

        int index = 0;
        while (index < detectedTempoChanges.Count)
        {
            DetectedTempoChange head = detectedTempoChanges[index];
            int tail = index;
            while (tail + 1 < detectedTempoChanges.Count)
            {
                DetectedTempoChange current = detectedTempoChanges[tail];
                DetectedTempoChange next = detectedTempoChanges[tail + 1];
                float spacing = Mathf.Max(
                    GetMeasureDurationMs(current.timeMs, 1f, true), MinEventSpacingMs);
                if (next.timeMs - current.timeMs > spacing) break;
                tail++;
            }

            if (tail == index)
            {
                collapsedTempoChanges.Add(head);
                index++;
                continue;
            }

            DetectedTempoChange last = detectedTempoChanges[tail];
            float shortestSpanMs = Mathf.Min(
                MedianIntervalAround(head.timeMs, false),
                MedianIntervalAround(last.timeMs, true));
            float netFloor = MinRelativeChange;
            if (shortestSpanMs > 0.001f)
                netFloor = Mathf.Max(netFloor, QuantisationSteps / shortestSpanMs);
            float net = Mathf.Abs(last.afterBpm - head.beforeBpm) /
                        Mathf.Max(head.beforeBpm, last.afterBpm);
            if (net >= netFloor)
            {
                collapsedTempoChanges.Add(new DetectedTempoChange
                {
                    timeMs = head.timeMs,
                    settleTimeMs = last.timeMs,
                    beforeBpm = head.beforeBpm,
                    afterBpm = last.afterBpm
                });
            }
            index = tail + 1;
        }

        detectedTempoChanges.Clear();
        detectedTempoChanges.AddRange(collapsedTempoChanges);
    }

    /// <summary>量「一個單位有多長」用幾筆。</summary>
    /// <remarks>
    /// 固定四筆，不跟著 <see cref="IntervalsPerMeasure"/> 走：這個數字要的是
    /// **不受分割影響的中位數**。分割在譜面上是稀疏的，四筆裡最多混進一兩段
    /// 碎片，中位數還是完整的那個單位；只取一筆的話，取到碎片就全錯。
    /// </remarks>
    private const int UnitWindow = 4;

    /// <summary>短到這個比例以下才算「被切開的碎片」。</summary>
    private const float SplitFragmentShare = 0.80f;

    /// <summary>兩段加起來超過一個單位這麼多就不是「切開一個單位」。</summary>
    private const float SplitPairCeiling = 1.45f;

    /// <summary>碎片之後要回到正常的單位；還是碎片的話那是變速不是分割。</summary>
    private const float SplitResumeShare = 0.70f;

    /// <summary>太短的間距不看：那多半是資料壞掉，不是一段速度。</summary>
    private const int MinSplitSpanMs = 40;

    /// <summary>最多認多少次變速。</summary>
    private const int MaxDetectedTempoChanges = 512;

    /// <summary>
    /// 這兩段看起來是不是「一個單位被切成兩半」，而不是速度變了。
    /// </summary>
    /// <remarks>
    /// 關鍵在**後面那一筆**。拍點表一小節一條的譜上，「半小節的分割」和「速度
    /// 變兩倍」前兩筆長得一模一樣（U, U, U/2, U/2…）—— 分得開它們的是再下一筆：
    /// 分割會回到一個完整的單位，變速則繼續維持新的間距。
    /// </remarks>
    private bool IsSplitPair(int interval, float unitMs)
    {
        if (unitMs <= 0f || interval < 0) return false;
        List<int> beats = chart != null ? chart.beat_timings : null;
        if (beats == null || interval + 2 >= beats.Count) return false;

        float first = beats[interval + 1] - beats[interval];
        float second = beats[interval + 2] - beats[interval + 1];
        if (first < MinSplitSpanMs || second < MinSplitSpanMs) return false;
        if (first >= unitMs * SplitFragmentShare) return false;

        float pair = first + second;
        if (pair > unitMs * SplitPairCeiling) return false;

        float next = interval + 3 < beats.Count
            ? beats[interval + 3] - beats[interval + 2]
            : pair;
        return next >= pair * SplitResumeShare;
    }

    /// <summary>
    /// 分割點那一筆：前段吃掉一個單位的 f，後段用新的速度走完剩下的 (1-f)。
    /// </summary>
    private void AddSplitTempoChange(int beatIndex)
    {
        List<int> beats = chart != null ? chart.beat_timings : null;
        if (beats == null || beatIndex <= 0 || beatIndex + 1 >= beats.Count) return;
        if (detectedTempoChanges.Count >= MaxDetectedTempoChanges) return;

        float unit = MedianIntervalRange(beatIndex - 1 - UnitWindow, UnitWindow);
        if (unit <= 0f) return;
        float first = beats[beatIndex] - beats[beatIndex - 1];
        float second = beats[beatIndex + 1] - beats[beatIndex];
        if (first < MinSplitSpanMs || second < MinSplitSpanMs) return;

        float fraction = first / unit;
        // 整整一個單位就不是分割（那是普通的拍線，速度變化歸區段掃描管）；
        // 太靠近邊緣的話 (1 - f) 會小到讓後段的速度爆掉。
        if (fraction < 0.05f || fraction > 0.95f) return;

        float unitAfter = second / (1f - fraction);
        int eventTime = beats[beatIndex];
        AddDetectedTempoChange(eventTime,
            CalculateAnchoredBpm(unit, GetDenominatorAt(eventTime - 1)),
            CalculateAnchoredBpm(unitAfter, GetDenominatorAt(eventTime)),
            Mathf.RoundToInt(unit * 0.5f),
            Mathf.Min(unit, unitAfter));
    }

    /// <summary>拍點表上最接近這個時刻的那一筆；差太遠就回 -1。</summary>
    /// <remarks>
    /// 分割點不一定有拍點：製譜器只有在那一段的速度真的被改掉時才會把邊界那一筆
    /// 釘上去。沒有那一筆就代表速度沒變，本來就不該報一次變速。
    /// </remarks>
    private int FindBeatIndexAt(int timeMs)
    {
        List<int> beats = chart != null ? chart.beat_timings : null;
        if (beats == null || beats.Count == 0) return -1;
        int lo = 0, hi = beats.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (beats[mid] < timeMs) lo = mid + 1; else hi = mid;
        }
        int best = lo;
        if (lo > 0 && Mathf.Abs(beats[lo - 1] - timeMs) < Mathf.Abs(beats[lo] - timeMs))
            best = lo - 1;
        return Mathf.Abs(beats[best] - timeMs) <= SplitAnchorToleranceMs ? best : -1;
    }

    /// <summary>分割點和拍點差這麼多以內算同一個位置。</summary>
    private const int SplitAnchorToleranceMs = 20;

    /// <summary>一小節佔拍點表上幾筆。</summary>
    /// <remarks>
    /// 照時間換算而不是照「每小節幾拍」數筆數 —— 拍點表一小節一條的譜，
    /// 一筆就是一小節；一個四分音符一條的譜，4/4 就是四筆。
    ///
    /// **拍號要問「整首主要是什麼」，不是問第 0 毫秒。** 曲庫裡 29 份譜（7%）
    /// 的第一條拍號是**弱起小節**：`1/8 → 6/8`、`1/4 → 4/4`，那條 1/8 只蓋到
    /// 一筆拍點。問第 0 毫秒會得到「一小節 = 一個八分音符」，於是這裡回 1 ——
    /// 窗只有一筆，中位數完全不濾雜訊，整首的細微搖擺都被當成變速。
    ///
    /// 上限放到 16 筆：6/8 的譜一小節就是六筆，硬夾到 4 等於只看三分之二小節。
    /// </remarks>
    private int IntervalsPerMeasure()
    {
        if (referenceBeatIntervalMs <= 0.001f) return 4;
        GetDominantTimeSignature(out int numerator, out int denominator);
        float bpm = anchorBpm > 0f ? anchorBpm
            : (chart != null && chart.first_bpm > 0f ? chart.first_bpm : 120f);
        float beatsInMeasure = Mathf.Max(1, numerator) * 4f / Mathf.Max(1, denominator);
        float measureMs = 60000f * beatsInMeasure / Mathf.Max(1f, bpm);
        return Mathf.Clamp(Mathf.RoundToInt(measureMs / referenceBeatIntervalMs), 1, 16);
    }

    /// <summary>蓋住最多時間的那一條拍號。</summary>
    /// <remarks>
    /// 用時間長度挑，不用出現次數：弱起小節那一條只活一筆拍點，而中段臨時改一
    /// 小節的 3/4 也不該蓋過整首的 4/4。
    /// </remarks>
    private void GetDominantTimeSignature(out int numerator, out int denominator)
    {
        numerator = timeSigNumerator > 0 ? timeSigNumerator : 4;
        denominator = timeSigDenominator > 0 ? timeSigDenominator : 4;
        if (chart == null || chart.time_signature_changes == null ||
            chart.time_signature_changes.Count == 0) return;

        int endMs = chart.music_finish_time_msec > 0 ? chart.music_finish_time_msec : 0;
        if (endMs <= 0 && chart.beat_timings != null && chart.beat_timings.Count > 0)
            endMs = chart.beat_timings[chart.beat_timings.Count - 1];
        if (endMs <= 0) return;

        int bestSpan = -1;
        for (int i = 0; i < chart.time_signature_changes.Count; i++)
        {
            TimeSigChange change = chart.time_signature_changes[i];
            if (change == null || change.numerator <= 0 || change.denominator <= 0)
                continue;
            int next = endMs;
            for (int j = i + 1; j < chart.time_signature_changes.Count; j++)
            {
                TimeSigChange later = chart.time_signature_changes[j];
                if (later == null) continue;
                next = later.time_ms;
                break;
            }
            int span = next - change.time_ms;
            if (span <= bestSpan) continue;
            bestSpan = span;
            numerator = change.numerator;
            denominator = change.denominator;
        }
    }

    /// <summary>一次變速至少要跨過幾格整數毫秒。</summary>
    /// <remarks>
    /// 兩個相鄰窗的中位數都是整數，所以同一個速度下它們可以差到兩格（89 / 91）。
    /// 三格是「差距一定不是進位造成的」的最小安全值。
    /// </remarks>
    private const float QuantisationSteps = 3f;

    /// <summary>相對變化的下限，給格子很粗的慢曲用。</summary>
    private const float MinRelativeChange = 0.01f;

    /// <summary>
    /// 記下一次變速。<paramref name="shortestSpanMs"/> 是比較的兩段裡短的那一段。
    /// </summary>
    /// <remarks>
    /// **門檻不能是固定的百分比。** 拍點只存整數毫秒，所以一格毫秒換算成相對
    /// 變化是 1/間距 —— 速度越快越兇：
    ///
    /// | 速度 | 一拍 | 一格毫秒佔 |
    /// |---|---|---|
    /// | 120 | 500ms | 0.2% |
    /// | 180 | 333ms | 0.3% |
    /// | 666 | 90ms  | **1.1%** |
    ///
    /// 原本寫死 1%，那個數字是照 180 BPM 估的。使用者報的 `666` 那首（宣告
    /// 666 BPM、一拍 90ms、整首定速）因此被判出 **182 次變速**：1308 個間距是
    /// 90×838、91×235、89×235，相鄰兩個窗的中位數差一格就 1.1%，差兩格 2.2%，
    /// 全部越過門檻。那 182 次假變速又讓預告牌幾乎一直掛著、交接動畫一直在演
    /// （動畫期間刻意繞過 20Hz 節流），於是整首都在逐幀重建兩個 TMP 和兩個網格
    /// —— 使用者說的「很卡」。
    ///
    /// 換成跟著格子走之後：同一首 0 次，而 180 BPM 以下的譜門檻還是 1%，
    /// Melodiniq 那種 193→196（1.6%）的小變速照樣抓得到。
    /// </remarks>
    private void AddDetectedTempoChange(
        int timeMs, float beforeBpm, float afterBpm, int mergeWindowMs,
        float shortestSpanMs)
    {
        if (beforeBpm <= 0f || afterBpm <= 0f) return;
        float relativeChange = Mathf.Abs(afterBpm - beforeBpm) /
                               Mathf.Max(beforeBpm, afterBpm);
        float floor = MinRelativeChange;
        if (shortestSpanMs > 0.001f)
            floor = Mathf.Max(floor, QuantisationSteps / shortestSpanMs);
        if (relativeChange < floor) return;

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
            settleTimeMs = timeMs,
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
