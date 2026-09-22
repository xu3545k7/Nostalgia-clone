using System;
using UnityEngine;

/// <summary>
/// Establishes a high-refresh gameplay cap before the first scene loads.
/// </summary>
public static class DisplaySync
{
    private const int GameplayTargetFrameRate = 120;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        // Present at the monitor cadence. A forced unsynchronised 120 FPS cap
        // produces an uneven 5:6 / 8:11 cadence on 144/165 Hz displays; long
        // horizontal beat lines make that mismatch look like severe stutter.
        double refreshValue = Screen.currentResolution.refreshRateRatio.value;
        int nativeRefresh = refreshValue > 1.0
            ? Mathf.RoundToInt((float)refreshValue)
            : GameplayTargetFrameRate;
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = Mathf.Max(60, nativeRefresh);
        Debug.Log($"[Display Sync] vSync=1 native={nativeRefresh}Hz target={Application.targetFrameRate}");

        MatchNativeResolution();

        // Keep a very small release-build frame-pacing sample. It performs no
        // hierarchy searches and emits only one line every 15 seconds, allowing
        // standalone Player.log to distinguish CPU/GC stalls from visual timing.
        var diagnostics = new GameObject("FramePacingProbe");
        UnityEngine.Object.DontDestroyOnLoad(diagnostics);
        diagnostics.hideFlags = HideFlags.HideInHierarchy;
        diagnostics.AddComponent<FramePacingProbe>();
    }

    /// <summary>
    /// 用螢幕自己的解析度開，而不是上一次存下來的那個。
    /// </summary>
    /// <remarks>
    /// **為什麼需要這一段。** Unity 會把解析度記在登錄檔（Screenmanager Resolution
    /// Width/Height），而那一筆一旦被寫過就永遠優先於專案設定裡的「使用原生解析
    /// 度」。它可能來自很久以前的一次執行、來自 Editor 的 Game 視窗、或別台機器
    /// 的設定被帶過來 —— 玩家從來沒有選過 1280x720，但它會一直用 1280x720 開，
    /// 然後在 2K 螢幕上被放大成一片糊。
    ///
    /// **不能用 Screen.currentResolution 當來源。** 在無邊框全螢幕底下它回報的是
    /// 目前的**算圖**解析度，不是螢幕的 —— 拿它去比對永遠相等，這一段就成了空
    /// 轉。這正是第一版沒有生效的原因。
    ///
    /// 真正問螢幕的是 <c>Display.main.systemWidth/systemHeight</c>。再退一步是
    /// <c>Screen.resolutions</c> 的最後一筆（那份清單是遞增排序的全螢幕模式清
    /// 單），最後才輪到 currentResolution。
    ///
    /// **為什麼不做成設定項。** 這個遊戲的判定和視覺都綁在螢幕的更新率與像素上，
    /// 而玩家要的永遠是「和我的螢幕一樣」。與其給一個選項讓他去發現自己被設成
    /// 720p，不如直接用對的那一個。
    ///
    /// 全螢幕模式不碰：那是玩家可能真的會有偏好的東西（有人要視窗化開兩個螢幕）。
    /// </remarks>
    private static void MatchNativeResolution()
    {
        try
        {
            int width = 0;
            int height = 0;
            string source = "none";

            if (Display.main != null
                && Display.main.systemWidth > 0 && Display.main.systemHeight > 0)
            {
                width = Display.main.systemWidth;
                height = Display.main.systemHeight;
                source = "Display.main";
            }

            Resolution[] modes = Screen.resolutions;
            if (modes != null && modes.Length > 0)
            {
                Resolution largest = modes[modes.Length - 1];
                // 清單是遞增的，但不保證，所以還是掃一遍。
                for (int i = 0; i < modes.Length; i++)
                {
                    if ((long)modes[i].width * modes[i].height
                        > (long)largest.width * largest.height) largest = modes[i];
                }
                if ((long)largest.width * largest.height > (long)width * height)
                {
                    width = largest.width;
                    height = largest.height;
                    source = "Screen.resolutions";
                }
            }

            if (width <= 0 || height <= 0)
            {
                width = Screen.currentResolution.width;
                height = Screen.currentResolution.height;
                source = "currentResolution";
            }

            Debug.Log($"[Display Sync] window={Screen.width}x{Screen.height} "
                + $"current={Screen.currentResolution.width}x{Screen.currentResolution.height} "
                + $"display={(Display.main != null ? Display.main.systemWidth : 0)}"
                + $"x{(Display.main != null ? Display.main.systemHeight : 0)} "
                + $"modes={(modes != null ? modes.Length : 0)} "
                + $"mode={Screen.fullScreenMode} -> {width}x{height} via {source}");

            if (width <= 0 || height <= 0) return;
            if (Screen.width == width && Screen.height == height) return;

            Screen.SetResolution(width, height, Screen.fullScreenMode,
                Screen.currentResolution.refreshRateRatio);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Display Sync] could not match the native resolution: {ex.Message}");
        }
    }
}

internal sealed class FramePacingProbe : MonoBehaviour
{
    private const float ReportInterval = 15f;
    private bool tracking;
    private int frames;
    private int over120Budget;
    private int over60Budget;
    private int over30Budget;
    private float elapsed;
    private float maxFrameMs;
    private int gc0AtStart;
    private GameManager cachedGameManager;
    private float nextManagerLookup;
    private float maxVisualClockDeltaMs;

    private void Update()
    {
        if (cachedGameManager == null && Time.unscaledTime >= nextManagerLookup)
        {
            cachedGameManager = GameManager.Instance;
            nextManagerLookup = Time.unscaledTime + 1f;
        }
        Conductor conductor = cachedGameManager != null ? cachedGameManager.Conductor : null;
        bool gameplayActive = conductor != null && conductor.isActive && Time.timeScale > 0f;
        if (!gameplayActive)
        {
            if (tracking) Report("end");
            tracking = false;
            return;
        }

        if (!tracking)
        {
            tracking = true;
            ResetSample();
        }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f || dt > 0.5f) return;
        frames++;
        elapsed += dt;
        float ms = dt * 1000f;
        if (ms > maxFrameMs) maxFrameMs = ms;
        if (ms > 8.75f) over120Budget++;
        if (ms > 17.25f) over60Budget++;
        if (ms > 34f) over30Budget++;
        float visualDelta = Mathf.Abs(conductor.RenderJudgmentDeltaMs);
        if (visualDelta > maxVisualClockDeltaMs) maxVisualClockDeltaMs = visualDelta;

        if (elapsed >= ReportInterval)
            Report("interval");
    }

    private void ResetSample()
    {
        frames = 0;
        elapsed = 0f;
        maxFrameMs = 0f;
        over120Budget = 0;
        over60Budget = 0;
        over30Budget = 0;
        gc0AtStart = GC.CollectionCount(0);
        maxVisualClockDeltaMs = 0f;
    }

    private void Report(string reason)
    {
        if (frames > 0 && elapsed > 0f)
        {
            float averageFps = frames / elapsed;
            int gc0 = GC.CollectionCount(0) - gc0AtStart;
            Debug.Log($"[Frame Pacing] {reason} avg={averageFps:F1}fps max={maxFrameMs:F1}ms " +
                $">8.75ms={over120Budget}/{frames} >17.25ms={over60Budget} >34ms={over30Budget} " +
                $"gc0={gc0} visualDeltaMax={maxVisualClockDeltaMs:F2}ms");
        }
        ResetSample();
    }
}
