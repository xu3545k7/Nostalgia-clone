using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 指認「某一格突然卡住」是誰造成的。
/// </summary>
/// <remarks>
/// 寫這個是因為前面兩次都在從原始碼推理：把第一次判定會建立的東西一個個搬到
/// 載入時做，結果還是卡。再猜下去只是重複同一個錯誤——這個專案裡
/// <see cref="PianoAudioDiagnostics"/> 的註解已經記過同一課。
///
/// 關鍵的一點：**shader 第一次被繪製時才編譯，而那發生在腳本跑完之後的算繪階段**。
/// 任何只量腳本時間的做法都看不到它。所以這裡分兩層量：
///
/// * `frame` —— 這一格從頭到尾多久（用上一格的 unscaledDeltaTime，那才是完整的
///   一格，含算繪與 GPU 等待）。
/// * `cs` —— 從第一個 Update 到最後一個 LateUpdate 的實際牆鐘時間，也就是**所有**
///   C# 花掉的時間，協程也算在內。
/// * `stages` —— 其中被明確標記的區段各花多久。
///
/// 三者一比才能斷定，少了 `cs` 會嚴重誤導：
///
/// * `cs` 逼近 `frame`，而且 stages 也逼近 `cs` → 是列出來的那段程式碼。
/// * `cs` 逼近 `frame`，但 stages 很小 → 是**還沒被標記的 C#**。這是最容易看錯的
///   一種：舊版沒有量 `cs`，於是把每一段沒標記的 C#（譜面解析、資產載入、
///   協程裡的東西）都印成「算繪／GPU」，害我往 shader 追了好幾輪。
/// * `cs` 遠小於 `frame` → 才真的是引擎那一側：算繪、GPU 等待、shader 編譯、
///   貼圖上傳。這個專案的 Preloaded Shaders 是空的，所以每個材質都是第一次
///   畫到才編譯。
/// * `heapDelta` 很大 → 有人在配置受管理記憶體，那一定是 C#，不會是 GPU。
/// * `gc` 有增加 → 是垃圾回收，那要找的是配置而不是某個功能。
///
/// 只有超過門檻的那幾格會印東西，平常的成本是每個區段兩次
/// <see cref="Stopwatch.GetTimestamp"/>。
/// </remarks>
public static class HitchProbe
{
    /// <summary>超過這麼久的一格才值得報告。60fps 是 16.7ms。</summary>
    private const double ReportThresholdMs = 40d;

    /// <summary>一格裡最多記幾種區段。超過就丟掉，不要為了診斷去配置記憶體。</summary>
    private const int MaxStages = 24;

    private static readonly string[] stageNames = new string[MaxStages];
    private static readonly double[] stageMs = new double[MaxStages];
    private static readonly int[] stageHits = new int[MaxStages];
    private static int stageCount;

    // 上一格的快照。unscaledDeltaTime 描述的是「上一格有多長」，所以要報告的
    // 一定是上一格記到的東西，這一格的還沒跑完。
    private static readonly string[] prevNames = new string[MaxStages];
    private static readonly double[] prevMs = new double[MaxStages];
    private static readonly int[] prevHits = new int[MaxStages];
    private static int prevCount;
    private static double prevTotalMs;
    private static int prevGcCount;
    private static long prevHeapBytes;

    // 這一格的 C# 牆鐘時間：第一個 Update 到最後一個 LateUpdate。協程夾在中間，
    // 所以也被涵蓋。這是「是不是我的程式碼」唯一可靠的判準。
    private static long scriptStartTicks;
    private static double scriptWallMs;
    private static double prevScriptWallMs;

    private static readonly double TicksToMs = 1000d / Stopwatch.Frequency;

    /// <summary>診斷本身的開關。關掉之後 <see cref="Measure"/> 只剩兩次取時間。</summary>
    public static bool Enabled = true;

    // 這一格發生過、但成本不在腳本裡的事件。引擎那一側的工作（非同步資產整合、
    // 貼圖上傳）沒辦法用碼錶量——它不在 Update 和 LateUpdate 之間。能做的是留下
    // 「這一格有這件事」的記號，再跟 frame 對照。
    private const int MaxMarks = 8;
    private static readonly string[] markNames = new string[MaxMarks];
    private static int markCount;
    private static readonly string[] prevMarkNames = new string[MaxMarks];
    private static int prevMarkCount;

    /// <summary>
    /// 記下「這一格發生過某件事」，不量時間。
    /// </summary>
    /// <remarks>
    /// 給那些成本落在引擎那一側的事情用。<see cref="Measure"/> 對它們沒有意義：
    /// 非同步資產的整合發生在腳本跑之前，碼錶包不到，量出來會是 0——看起來就像
    /// 「純 GPU」，其實是引擎在解壓資產。
    /// </remarks>
    public static void Mark(string what)
    {
        if (!Enabled || what == null || markCount >= MaxMarks) return;
        for (int i = 0; i < markCount; i++) if (ReferenceEquals(markNames[i], what)) return;
        markNames[markCount++] = what;
    }

    /// <summary>
    /// 量一段程式碼。用 <c>using (HitchProbe.Measure("名字")) { ... }</c>。
    /// </summary>
    /// <remarks>
    /// 名字必須是常數字串：它只被拿來比參考位址，不做字串比較，這樣每次判定都
    /// 呼叫也不會有成本。
    /// </remarks>
    public static Scope Measure(string stage) => new Scope(stage, Stopwatch.GetTimestamp());

    public readonly struct Scope : System.IDisposable
    {
        private readonly string stage;
        private readonly long started;

        internal Scope(string stage, long started)
        {
            this.stage = stage;
            this.started = started;
        }

        public void Dispose()
        {
            if (!Enabled || stage == null) return;
            Accumulate(stage, (Stopwatch.GetTimestamp() - started) * TicksToMs);
        }
    }

    private static void Accumulate(string stage, double ms)
    {
        for (int i = 0; i < stageCount; i++)
        {
            // 參考位址比較就夠了，呼叫端傳的都是字面常數。
            if (!ReferenceEquals(stageNames[i], stage)) continue;
            stageMs[i] += ms;
            stageHits[i]++;
            return;
        }
        if (stageCount >= MaxStages) return;
        stageNames[stageCount] = stage;
        stageMs[stageCount] = ms;
        stageHits[stageCount] = 1;
        stageCount++;
    }

    /// <summary>
    /// 一格的開始：把上一格的量測收起來，需要的話報告出去。
    /// </summary>
    internal static void BeginFrame()
    {
        double frameMs = Time.unscaledDeltaTime * 1000d;
        if (frameMs > ReportThresholdMs && prevCount >= 0) Report(frameMs);
        if (prevCount >= 0) AccumulateSlowFrames(frameMs, System.GC.CollectionCount(0) - prevGcCount);

        prevCount = stageCount;
        prevTotalMs = 0d;
        for (int i = 0; i < stageCount; i++)
        {
            prevNames[i] = stageNames[i];
            prevMs[i] = stageMs[i];
            prevHits[i] = stageHits[i];
            prevTotalMs += stageMs[i];
        }
        prevGcCount = System.GC.CollectionCount(0);
        prevHeapBytes = System.GC.GetTotalMemory(false);
        prevScriptWallMs = scriptWallMs;
        prevMarkCount = markCount;
        for (int i = 0; i < markCount; i++) prevMarkNames[i] = markNames[i];
        stageCount = 0;
        markCount = 0;

        scriptStartTicks = Stopwatch.GetTimestamp();
        scriptWallMs = 0d;
    }

    /// <summary>一格的最後：把 C# 這一側的牆鐘時間收起來。</summary>
    internal static void EndFrame()
    {
        if (scriptStartTicks == 0L) return;
        scriptWallMs = (Stopwatch.GetTimestamp() - scriptStartTicks) * TicksToMs;
    }

    private static void Report(double frameMs)
    {
        // 由大到小排，只印真的佔到時間的那幾段。選擇排序：最多 24 筆，而且一格
        // 卡住的時候多花幾微秒排序無所謂。
        var builder = new System.Text.StringBuilder(256);
        builder.Append("[Hitch] frame=").Append(frameMs.ToString("0.0"))
               .Append("ms cs=").Append(prevScriptWallMs.ToString("0.0"))
               .Append("ms stages=").Append(prevTotalMs.ToString("0.0")).Append("ms");

        int gcDelta = System.GC.CollectionCount(0) - prevGcCount;
        double heapDeltaMb = (System.GC.GetTotalMemory(false) - prevHeapBytes) / 1048576d;
        builder.Append(" gc=").Append(gcDelta)
               .Append(" heapDelta=").Append(heapDeltaMb.ToString("+0.0;-0.0;0.0")).Append("MB");

        bool printedAny = false;
        for (int printed = 0; printed < 6; printed++)
        {
            int best = -1;
            double bestMs = 0.5d;   // 半毫秒以下不值得印
            for (int i = 0; i < prevCount; i++)
            {
                if (prevMs[i] <= bestMs) continue;
                bestMs = prevMs[i];
                best = i;
            }
            if (best < 0) break;
            builder.Append(printedAny ? ", " : " | ")
                   .Append(prevNames[best]).Append('=').Append(prevMs[best].ToString("0.0"))
                   .Append("ms");
            if (prevHits[best] > 1) builder.Append('×').Append(prevHits[best]);
            prevMs[best] = 0d;
            printedAny = true;
        }

        for (int i = 0; i < prevMarkCount; i++)
        {
            builder.Append(i == 0 ? " | during: " : ", ").Append(prevMarkNames[i]);
        }

        // 結論那一行。先分 C# 和引擎，再分「標記過的」和「還沒標記的」——
        // 少了這一層，沒標記的 C# 會被誤判成 GPU。
        double outsideCs = frameMs - prevScriptWallMs;
        double unmeasuredCs = prevScriptWallMs - prevTotalMs;
        if (outsideCs > unmeasuredCs && outsideCs > ReportThresholdMs * 0.5d)
        {
            builder.Append(" | ").Append(outsideCs.ToString("0.0"))
                   .Append("ms ENGINE side (render/GPU, shader compile, texture upload)");
        }
        else if (unmeasuredCs > ReportThresholdMs * 0.5d)
        {
            builder.Append(" | ").Append(unmeasuredCs.ToString("0.0"))
                   .Append("ms UNINSTRUMENTED C# — 包一層 HitchProbe.Measure 就會現形");
        }

        Debug.Log(builder.ToString());
        try { BuildLogger.Log(builder.ToString()); } catch { }
    }

    // ── 慢幀統計 ─────────────────────────────────────────────────────────
    //
    // 上面那套只報 40ms 以上的格子，那是「畫面停住」的尺度。可是在 144Hz 下，玩家
    // 說的「有點卡」是 9～14ms 的格子 —— 比預算多一半，眼睛看得出來，卻永遠碰不到
    // 40ms 的門檻，於是一筆都不會被記下來。
    //
    // 這裡不逐格印，而是把「超過一格預算 1.25 倍」的格子彙總起來，每 5 秒一行：
    // 慢幀裡各區段平均花多少、全部格子平均花多少。兩者一比，多出來的那一段就是
    // 讓格子變慢的東西。

    private const double SlowSummarySeconds = 5d;
    private const int MaxSummaryStages = 40;

    private static readonly string[] summaryNames = new string[MaxSummaryStages];
    private static readonly double[] summarySlowMs = new double[MaxSummaryStages];
    private static readonly double[] summaryAllMs = new double[MaxSummaryStages];
    private static int summaryCount;
    private static int summaryFrames;
    private static int summarySlowFrames;
    private static double summarySlowFrameMs;
    private static double summarySlowCsMs;
    private static double summarySlowStagesMs;
    private static int summaryGc;
    private static int summarySlowGc;
    private static double summaryStartedAt = -1d;
    private static double slowThresholdMs;

    private static double SlowThresholdMs()
    {
        if (slowThresholdMs > 0d) return slowThresholdMs;
        double hz = 0d;
        try { hz = Screen.currentResolution.refreshRateRatio.value; } catch { }
        if (hz < 30d || hz > 500d) hz = 60d;
        slowThresholdMs = 1.25d * 1000d / hz;
        return slowThresholdMs;
    }

    private static int SummaryIndex(string name)
    {
        for (int i = 0; i < summaryCount; i++)
            if (ReferenceEquals(summaryNames[i], name)) return i;
        if (summaryCount >= MaxSummaryStages) return -1;
        summaryNames[summaryCount] = name;
        summarySlowMs[summaryCount] = 0d;
        summaryAllMs[summaryCount] = 0d;
        return summaryCount++;
    }

    private static void AccumulateSlowFrames(double frameMs, int gcDelta)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (summaryStartedAt < 0d) summaryStartedAt = now;

        bool slow = frameMs > SlowThresholdMs();
        summaryFrames++;
        if (gcDelta > 0) summaryGc += gcDelta;
        for (int i = 0; i < prevCount; i++)
        {
            int index = SummaryIndex(prevNames[i]);
            if (index < 0) continue;
            summaryAllMs[index] += prevMs[i];
            if (slow) summarySlowMs[index] += prevMs[i];
        }
        if (slow)
        {
            summarySlowFrames++;
            summarySlowFrameMs += frameMs;
            summarySlowCsMs += prevScriptWallMs;
            summarySlowStagesMs += prevTotalMs;
            if (gcDelta > 0) summarySlowGc += gcDelta;
        }

        if (now - summaryStartedAt < SlowSummarySeconds) return;
        if (summarySlowFrames >= 3) ReportSlowFrames();
        summaryStartedAt = now;
        summaryCount = 0;
        summaryFrames = summarySlowFrames = summaryGc = summarySlowGc = 0;
        summarySlowFrameMs = summarySlowCsMs = summarySlowStagesMs = 0d;
    }

    private static void ReportSlowFrames()
    {
        double n = summarySlowFrames;
        double frame = summarySlowFrameMs / n;
        double cs = summarySlowCsMs / n;
        double stages = summarySlowStagesMs / n;
        var builder = new System.Text.StringBuilder(384);
        builder.Append("[SlowFrames] ").Append(summarySlowFrames).Append('/').Append(summaryFrames)
               .Append(" (>").Append(SlowThresholdMs().ToString("0.0")).Append("ms)")
               .Append(" avg=").Append(frame.ToString("0.0"))
               .Append("ms cs=").Append(cs.ToString("0.0"))
               .Append(" marked=").Append(stages.ToString("0.0"))
               .Append(" unmarkedCs=").Append((cs - stages).ToString("0.0"))
               .Append(" engine=").Append((frame - cs).ToString("0.0"))
               .Append(" gc=").Append(summarySlowGc).Append('/').Append(summaryGc);

        // 慢幀裡平均最花時間的幾段，旁邊附上全部格子的平均做對照。
        builder.Append(" | slow vs all:");
        bool[] used = new bool[summaryCount];
        for (int printed = 0; printed < 8; printed++)
        {
            int best = -1;
            double bestMs = 0.05d;
            for (int i = 0; i < summaryCount; i++)
            {
                if (used[i]) continue;
                double avg = summarySlowMs[i] / n;
                if (avg <= bestMs) continue;
                bestMs = avg;
                best = i;
            }
            if (best < 0) break;
            used[best] = true;
            builder.Append(' ').Append(summaryNames[best]).Append('=')
                   .Append((summarySlowMs[best] / n).ToString("0.00")).Append('/')
                   .Append((summaryAllMs[best] / summaryFrames).ToString("0.00"));
        }
        Debug.Log(builder.ToString());
    }

    /// <summary>每格最先跑，把上一格收起來，並起算這一格的 C# 時間。</summary>
    [DefaultExecutionOrder(-10000)]
    private sealed class Ticker : MonoBehaviour
    {
        private void Update() => BeginFrame();
    }

    /// <summary>
    /// 每格最後跑，收掉這一格的 C# 時間。
    /// </summary>
    /// <remarks>
    /// 必須是**另一個**元件：執行順序對 Update 和 LateUpdate 是同一個值，
    /// <see cref="Ticker"/> 的 -10000 會讓它的 LateUpdate 排在所有人**前面**，
    /// 量到的就不是終點了。這裡用 +10000 才是真的最後。
    ///
    /// 協程（yield return null 之後）夾在 Update 和 LateUpdate 之間，所以一併
    /// 被涵蓋——譜面解析、資產載入這些都在協程裡。
    /// </remarks>
    [DefaultExecutionOrder(10000)]
    private sealed class LateTicker : MonoBehaviour
    {
        private void LateUpdate() => EndFrame();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Object.FindFirstObjectByType<Ticker>() != null) return;
        var go = new GameObject("HitchProbe");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Ticker>();
        go.AddComponent<LateTicker>();
    }
}
