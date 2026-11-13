using UnityEngine;

/// <summary>
/// 這份譜該用多快的流速：由**畫面上同時有幾個音符**決定。
/// </summary>
/// <remarks>
/// **這個功能預設是關的**（<c>SettingsManager.densityScaledScroll</c>）。理由：開著的
/// 話玩家設的 110 在不同曲子上是不同的實際流速，數字不再可預期；而實測 365 份譜，
/// 它也只把「畫面擁擠度」的差距從 3.9 倍收到 **1.7 倍**，沒真的做到等值。要等值
/// 得把 <see cref="Strength"/> 拉到 1.0，那會讓實際流速在 0.45×～2.20× 之間跑，最疏的
/// 譜音符慢到爬。兩邊都不划算，所以改成「數字就是單位/秒」。實作和校準全部留著，
/// 下面的理由也還站得住 —— 要重新打開只要把設定打開。
///
/// **為什麼固定的單位/秒是錯的。** 原本 <c>NoteSpawner.speed</c> 直接吃設定值（單位／
/// 秒），完全不看譜面。那代表音符在畫面上的間距只由<b>時間</b>決定——可是同樣 135 ms
/// 的間隔，在一首譜裡是密集的連打、在另一首裡是普通的八分音符。同一個設定值在兩首曲
/// 子上是兩種可讀性，而玩家每換一首就得重調。
///
/// **量什麼。** 音符在畫面上停留 <c>NoteSpawner.travelTimeSeconds</c> 秒（預設 2 秒），
/// 所以「畫面上有幾個音符」就是「任何 2 秒裡有幾次擊鍵」。軌道長度固定，流速 v 決定畫
/// 面裝得下幾秒（T = D/v），畫面上的音符數 = 密度 × D/v —— 要讓它不隨譜面暴增，v 就得
/// 跟著密度走。所以取<b>最忙的 2 秒有幾次擊鍵</b>，關係是正比。
///
/// **為什麼不是「音符間隔的百分位」。** 這是第一版的做法（第 20 百分位間隔），實測 361
/// 份譜後推翻：百分位是<b>整份譜</b>的統計，一段密集樂句只有在它含了超過兩成的擊鍵時
/// 才推得動它。結果是密度最高的譜完全沒被加速 —— PRAGMATISM -RESURRECTION- Master
/// 每 2 秒 42 次擊鍵（全庫前幾名），p20 只算出 ×0.98；Entrance Master 每 2 秒 28 次，
/// p20 反而把它<b>降到 ×0.81</b>。兩成四的譜方向是相反的。
///
/// 和「最擁擠的 2 秒」的秩相關：視窗密度 0.93，p20 間隔只有 0.72。同一首曲子難度越高
/// 越該快的單調性：視窗 98%，p20 95%。兩個量都輸，所以換掉。
///
/// **校準。** 曲庫 361 份譜（只算看得見的擊鍵），最忙 2 秒的擊鍵數中位 15、四分位 11
/// 與 24、最疏 4（瑠璃の鳥 Normal）、最密 46（系ぎて Master）。取中位當
/// <see cref="ReferenceOnsets"/>，所以多數曲子的流速和改版前差不多。
///
/// 樣本數是**登錄的譜**：照每首 <c>register.json</c> 的 <c>chartFileName</c> 解析，也就
/// 是遊戲真的會載入的那些。兩個曲庫（NosMania 分開版與 clone）幾乎是彼此的複本，用
/// 資料夾 glob 掃會把共有的譜算兩次 —— 第一版寫的「720 份」就是這樣來的，實際是 111
/// 首、361 份。去重之後上面每一個統計值都沒變（中位 15、四分位 11 與 24、秩相關 0.93
/// 對 0.72），只有樣本數和夾住的份數要改。
/// </remarks>
public static class ChartScrollSpeed
{
    /// <summary>
    /// 視窗長度（毫秒）。
    /// </summary>
    /// <remarks>
    /// 2000 不是隨手挑的：它就是 <c>NoteSpawner.travelTimeSeconds</c> 的預設值，也就是
    /// 一顆音符從生出來到打擊線待在畫面上的時間。換窗長只會等比縮放
    /// <see cref="ReferenceOnsets"/>，排序幾乎不動（1000／1500／3000 對「最擁擠的 2 秒」
    /// 的秩相關分別是 0.93／0.93／0.92，和 2000 的 0.93 一樣），所以取和實際停留時間
    /// 一致的那一個。
    /// </remarks>
    private const int WindowMs = 2000;

    /// <summary>基準：最忙的 2 秒有幾次擊鍵。這個密度的譜拿到的比例是 1。全庫中位數。</summary>
    public const float ReferenceOnsets = 15f;

    /// <summary>
    /// 取哪一個百分位當「最忙」。
    /// </summary>
    /// <remarks>
    /// 不取最大值：一個小節的裝飾音串不該決定整首的流速。p95 和最大值的秩相關 0.96，
    /// 但不會被單一突起綁架。
    /// </remarks>
    private const float Percentile = 0.95f;

    /// <summary>同刻的和弦算一次擊鍵。低於這個毫秒數視為同一次。</summary>
    private const int ChordWindowMs = 8;

    /// <summary>太短的譜量不出密度（教學的前幾課）。</summary>
    private const int MinOnsets = 48;

    /// <summary>
    /// 修正的強度。1 = 完全拉平（0 = 不修）。
    /// </summary>
    /// <remarks>
    /// **為什麼不是 1。** 全庫的密度跨了 11.5 倍（4 到 46），完全拉平要 11.5 倍的流速
    /// 範圍，做不到；而且完全拉平的意思是每一首曲子看起來都一樣密 —— 快曲不再看起來
    /// 快，慢曲也不再看起來慢，譜面的性格被洗掉了。慢曲被拉得太慢也有自己的問題：音符
    /// 掃過得太慢，反而看不出何時該下去。
    ///
    /// 0.65 是部分修正：密度差一倍的兩首，流速差 1.57 倍而不是 2 倍。實測 361 份譜，
    /// 這個強度下四分位是 0.82 與 1.36、中位數 1.00。
    /// </remarks>
    private const float Strength = 0.65f;

    /// <summary>
    /// 比例的上下限。
    /// </summary>
    /// <remarks>
    /// 兩端都真的會碰到（下限 26 份、上限 37 份，合起來約一成七），而且碰到是對的：
    /// 最疏的譜每 2 秒只有 4 次擊鍵，不夾的話會算出 0.42×，音符幾乎不動。
    /// </remarks>
    public const float MinRatio = 0.62f;
    public const float MaxRatio = 1.55f;

    private static int[] countBuffer;

    // 比例是整份譜的常數，而算一次要掃過所有音符；玩家每按一下加減速都會重新套一次，
    // 所以記住上一份。
    //
    // **不能只比物件參考。** 練習模式的速度是拿同一個 Chart **就地拉伸**的
    // （<c>GameManager.FinaliseLoadedChart</c> → <c>ApplyTimeStretch</c>），參考不變而內容變了，
    // 只比參考會拿到拉伸前的比例。再比一個「最後一顆音符的時間」就夠了：拉伸一定
    // 會改變它，而讀一個元素是 O(1)。
    private static Chart cachedChart;
    private static int cachedTail;
    private static float cachedRatio = 1f;

    /// <summary>這份譜的流速比例。1 = 和設定值一樣。</summary>
    public static float Ratio(Chart chart)
    {
        if (chart == null) return 1f;
        int tail = chart.notes != null && chart.notes.Count > 0
            ? chart.notes[chart.notes.Count - 1].startTime : 0;
        if (ReferenceEquals(chart, cachedChart) && tail == cachedTail) return cachedRatio;

        float busy = BusyOnsets(chart);
        float ratio = busy <= 0f
            ? 1f
            : Mathf.Clamp(Mathf.Pow(busy / ReferenceOnsets, Strength), MinRatio, MaxRatio);
        cachedChart = chart;
        cachedTail = tail;
        cachedRatio = ratio;
        return ratio;
    }

    /// <summary>設定值套上這份譜的比例。</summary>
    public static float Resolve(Chart chart, float requested)
    {
        if (requested <= 0f) return requested;
        return requested * Ratio(chart);
    }

    /// <summary>
    /// 這份譜最忙的 2 秒有幾次擊鍵。
    /// </summary>
    /// <remarks>
    /// 算的是**擊鍵次數**不是音符數：同刻的和弦在畫面上是一排，玩家讀的是一個時間點，
    /// 不是五個。所以 <see cref="ChordWindowMs"/> 以內的併成一次。
    ///
    /// 隱藏音符不在這裡處理 —— <c>Chart.NormaliseHiddenNotes</c> 在載入時就把它們從
    /// <c>notes</c> 移走了，執行期看到的已經只有畫得出來的。（第一版的校準是在原始
    /// JSON 上做的，把隱藏音符算進去了，基準因此偏小：全庫七成的譜含隱藏音符（251/361），教學
    /// 初階 04 的間隔被壓成四分之一。）
    /// </remarks>
    public static float BusyOnsets(Chart chart)
    {
        if (chart == null || chart.notes == null || chart.notes.Count < MinOnsets) return 0f;

        int n = chart.notes.Count;
        if (countBuffer == null || countBuffer.Length < n)
            countBuffer = new int[Mathf.NextPowerOfTwo(n)];

        // 先收斂成擊鍵時刻。譜面是照時間排好的，但沒排好也不能算出負的窗長。
        int onsets = 0;
        int previous = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            var note = chart.notes[i];
            if (note == null) continue;
            int at = note.startTime;
            if (previous != int.MinValue)
            {
                if (at < previous) continue;                  // 沒排序：跳過逆行的
                if (at - previous < ChordWindowMs) continue;  // 同一次擊鍵
            }
            countBuffer[onsets++] = at;
            previous = at;
        }
        if (onsets < MinOnsets) return 0f;

        // 每個以擊鍵為起點的 2 秒窗裡有幾次擊鍵。右端只往前走，所以整體是 O(n)。
        // 就地改寫：countBuffer[i] 從「時刻」換成「以它為起點的窗裡的次數」。寫到 i 的
        // 時候右端 j >= i，而 i 之後的還是時刻，所以讀得到的都還沒被覆蓋。
        int right = 0;
        for (int i = 0; i < onsets; i++)
        {
            if (right < i) right = i;
            int start = countBuffer[i];
            while (right + 1 < onsets && countBuffer[right + 1] - start <= WindowMs) right++;
            countBuffer[i] = right - i + 1;
        }

        System.Array.Sort(countBuffer, 0, onsets);
        int index = Mathf.Clamp(Mathf.FloorToInt(onsets * Percentile), 0, onsets - 1);
        return countBuffer[index];
    }
}
