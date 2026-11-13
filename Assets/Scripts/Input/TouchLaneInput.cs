using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// 觸控 → 鍵道。手指放在哪一道就按住哪一道，滑過去就換道（滑奏自然成立）。
/// </summary>
/// <remarks>
/// 只做「位置換算」這一件事，其餘全部交給 <see cref="RealtimeInputBuffer"/>：
/// 按下／放開用 <see cref="RealtimeInputBuffer.InjectLaneState"/> 送進和鍵盤同一個
/// 佇列，判定、keysound、按鍵提示、防誤觸都不知道這一下是手指。
///
/// **鍵道邊界取的是判定線那一排在螢幕上的位置**，不管手指按在畫面多高。玩家看
/// 到音符落在判定線上的哪裡就按哪裡；軌道是透視的，按在上方用軌道當下那一排的
/// 寬度去算反而會和眼睛看到的落點對不上。
///
/// 畫面最上面一條（<see cref="PauseZoneFraction"/>）不是鍵道，點了是暫停 ——
/// 平板沒有右鍵。
/// </remarks>
[DefaultExecutionOrder(-490)]
public class TouchLaneInput : MonoBehaviour
{
    private const int LaneCount = 28;

    /// <summary>畫面上方這個比例的範圍點下去＝暫停，不算鍵道。</summary>
    private const float PauseZoneFraction = 0.10f;

    /// <summary>
    /// 用事件本身的時間往回推按下的時刻（次幀精度）。往回推的量夾在這個上限內，
    /// 避免時鐘對不上時把整段判定推早 —— 鍵盤那邊就吃過 20–30 ms 的虧。
    /// </summary>
    private const double MaxBackdateSeconds = 0.034;

    public static TouchLaneInput Instance { get; private set; }

    /// <summary>手機／平板自動開。桌機要測的話把這個設成 true（Editor 可用滑鼠模擬觸控）。</summary>
    public static bool ForceEnable;

    // touchId → 現在壓著哪一道（-1 ＝ 不在鍵道上）
    private readonly Dictionary<int, int> fingerLane = new Dictionary<int, int>(10);
    // 每一道有幾根手指：兩根手指壓同一道，放開一根不能算放開
    private readonly int[] laneFingers = new int[LaneCount];
    // 判定線那一排，第 i 道左緣在螢幕上的 x（共 29 個邊界）
    private readonly float[] laneEdgeScreenX = new float[LaneCount + 1];
    private bool edgesValid;
    private GameObject judgmentLine;
    private bool edgesAscending = true;

    /// <summary>從暫停區按下去的手指：整根都不算，滑進鍵道也不算。</summary>
    private const int IgnoredFinger = -2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Application.isMobilePlatform && !ForceEnable) return;
        if (Instance != null) return;
        RealtimeInputBuffer.EnsureCreated();
        var go = new GameObject("TouchLaneInput");
        DontDestroyOnLoad(go);
        go.AddComponent<TouchLaneInput>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // 不碰 Input.multiTouchEnabled：專案只開新版 Input System，舊 API 會丟例外；
        // iOS 的多指預設本來就是開的。
    }

    private void OnEnable() => InputSystem.onEvent += OnInputEvent;

    private void OnDisable()
    {
        InputSystem.onEvent -= OnInputEvent;
        ReleaseAll(Time.realtimeSinceStartupAsDouble);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnApplicationPause(bool paused)
    {
        // 切到背景時手指的 Ended 事件不一定會送到，留著就是一道永遠按著
        if (paused) ReleaseAll(Time.realtimeSinceStartupAsDouble);
    }

    private void LateUpdate() => edgesValid = RebuildLaneEdges();

    private bool RebuildLaneEdges()
    {
        var cam = Camera.main;
        // GameObject.Find 只找得到啟用中的物件，而且場景很大：記住找到的那一個，
        // 不見了（換場景被銷毀）才重找。
        if (judgmentLine == null) judgmentLine = GameObject.Find("JudgmentLine");
        var line = judgmentLine;
        if (cam == null || line == null || !line.activeInHierarchy) return false;

        // 和 LaneStrikeCue／音符同一套：軌道以 x = 0 為中心，寬度切 28 等份
        float trackWidth = PianoVisualLayout.ResolveTrackWidth(null);
        float laneWidth = trackWidth / LaneCount;
        Vector3 at = line.transform.position;
        for (int i = 0; i <= LaneCount; i++)
        {
            float x = -trackWidth * 0.5f + i * laneWidth;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(x, at.y, at.z));
            if (sp.z <= 0f) return false;
            laneEdgeScreenX[i] = sp.x;
        }
        edgesAscending = laneEdgeScreenX[LaneCount] >= laneEdgeScreenX[0];
        return true;
    }

    private int LaneAt(Vector2 screen)
    {
        if (!edgesValid) return -1;
        if (screen.y > Screen.height * (1f - PauseZoneFraction)) return -1;
        float x = screen.x;
        for (int i = 0; i < LaneCount; i++)
        {
            float a = laneEdgeScreenX[i], b = laneEdgeScreenX[i + 1];
            if (edgesAscending ? (x >= a && x < b) : (x <= a && x > b)) return i;
        }
        return -1;
    }

    private static bool GameplayAcceptsTouch()
    {
        var pause = GamePauseManager.Instance;
        if (pause != null && pause.IsOverlayOpen) return false;
        return Judgment.JudgmentManager.Instance != null;
    }

    private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (!(device is Touchscreen)) return;
        if (!eventPtr.IsA<StateEvent>()) return;

        TouchState touch;
        try { touch = StateEvent.GetState<TouchState>(eventPtr); }
        catch { return; } // 不是單指 TouchState（例如整個 Touchscreen 的狀態）

        double now = Time.realtimeSinceStartupAsDouble;
        double age = InputState.currentTime - eventPtr.time;
        if (double.IsNaN(age) || age < 0.0) age = 0.0;
        if (age > MaxBackdateSeconds) age = MaxBackdateSeconds;
        double timestamp = now - age;

        int id = touch.touchId;
        TouchPhase phase = touch.phase;

        if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
        {
            if (fingerLane.TryGetValue(id, out int was))
            {
                fingerLane.Remove(id);
                SetFinger(was, -1, timestamp);
            }
            return;
        }

        if (phase == TouchPhase.Began)
        {
            if (touch.position.y > Screen.height * (1f - PauseZoneFraction))
            {
                if (GameplayAcceptsTouch()) GamePauseManager.Instance?.ShowPauseOverlay();
                if (fingerLane.TryGetValue(id, out int old)) SetFinger(old, -1, timestamp);
                fingerLane[id] = IgnoredFinger;
                return;
            }
        }

        if (phase != TouchPhase.Began && phase != TouchPhase.Moved && phase != TouchPhase.Stationary)
            return;

        int lane = GameplayAcceptsTouch() ? LaneAt(touch.position) : -1;
        if (phase != TouchPhase.Began && fingerLane.TryGetValue(id, out int prev))
        {
            if (prev == IgnoredFinger || prev == lane) return;
            fingerLane[id] = lane;
            SetFinger(prev, lane, timestamp);
            return;
        }

        if (phase == TouchPhase.Began)
        {
            // 同一個 id 沒收到 Ended 就又 Began（系統吞事件時會這樣）：先放掉舊的
            if (fingerLane.TryGetValue(id, out int stale)) SetFinger(stale, -1, timestamp);
            fingerLane[id] = lane;
            SetFinger(-1, lane, timestamp);
        }
    }

    /// <summary>一根手指從 <paramref name="from"/> 道移到 <paramref name="to"/> 道（-1 ＝ 不在鍵道上）。</summary>
    private void SetFinger(int from, int to, double timestamp)
    {
        var buffer = RealtimeInputBuffer.Instance;
        if (buffer == null) return;

        // 先放後按：滑奏時舊的一道先放開，同一刻再按下新的一道
        if ((uint)from < LaneCount && laneFingers[from] > 0)
        {
            laneFingers[from]--;
            if (laneFingers[from] == 0) buffer.InjectLaneState(from, false, timestamp);
        }
        if ((uint)to < LaneCount)
        {
            laneFingers[to]++;
            if (laneFingers[to] == 1) buffer.InjectLaneState(to, true, timestamp);
        }
    }

    private void ReleaseAll(double timestamp)
    {
        var buffer = RealtimeInputBuffer.Instance;
        for (int i = 0; i < LaneCount; i++)
        {
            if (laneFingers[i] > 0 && buffer != null) buffer.InjectLaneState(i, false, timestamp);
            laneFingers[i] = 0;
        }
        fingerLane.Clear();
    }
}
