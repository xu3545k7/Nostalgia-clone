using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime 28-lane ivory keyboard placed on the player side of the judgment line.
/// Each key is rear-hinged: the press is quick and the mechanical return is slower.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class IvoryLaneKeyboard : MonoBehaviour
{
    public const int LaneCount = 28;

    // Long white-key proportions: the rear edge sits beside the judgment line,
    // while the player-side front edge extends toward the camera.
    private const float OriginalKeyDepth = 11.0f;
    private const float KeyDepth = OriginalKeyDepth * 2.5f;
    private const float KeyHeight = 1.15f;
    // A visible rear-hinged depression. With the long white-key depth this
    // lowers the player-side edge clearly without translating the whole key.
    // Rear-hinged press with enough pitch difference to read from the gameplay
    // camera. A small lift below prevents the long front edge entering the bed.
    // The longer lever needs a smaller angular value to keep the front-edge travel
    // physical; it still moves farther on screen than the former short key.
    private const float PressAngle = -4.8f;
    private const float PressClearanceLift = 0f;
    private const float PressTowardPlayerShift = 0.62f;
    private const float PressSeconds = 0.045f;
    private const float ReleaseSeconds = 0.22f;
    // The rear hinge is flush with the player edge of the judgment line.  The
    // old 0.28u spacer was visible as a detached strip when entering gameplay.
    private const float JudgmentGap = 0f;
    private const float DebugPulseSeconds = 0.11f;
    /// <summary>
    /// 誤觸紅燈的長度。
    /// </summary>
    /// <remarks>
    /// 從 0.34 拉到 0.6。Hardcore 模式下沒打中的按鍵**不出聲**（那一顆音不屬於這
    /// 首曲子，混進來只會弄髒玩家正在演奏的東西），所以這盞燈是誤觸唯一的回饋
    /// 管道 —— 一個管道要扛兩個管道的工作，它就得更久、更亮。
    ///
    /// 0.6 秒仍然短於一個樂句，所以連續誤觸還是分得出是幾次，不會糊成一片。
    /// </remarks>
    private const float WrongFlashSeconds = 0.6f;

    /// <summary>
    /// 偏格黃燈的長度。比紅燈短。
    /// </summary>
    /// <remarks>
    /// 兩種燈說的是兩種嚴重程度：紅燈是**這一下不該按**，黃燈是**按對了音符、
    /// 但不是它的中心格**。後者仍然得分、仍然接 combo，所以它不該在畫面上停得
    /// 和真正的錯誤一樣久。
    /// </remarks>
    private const float OffCentreFlashSeconds = 0.24f;
    // Keep the console independent from the judgment-line height, but retain the
    // original useful key depth.  KeyDepth contains the 2.5x modelling depth;
    // scaling the whole console to 40% here yields an 11u visible key instead of
    // exposing the full 27.5u construction length to the player.
    private const float FixedConsoleDepthScale = 0.4f;
    private const float ScreenBottomViewportY = 0.008f;
    // The mechanical pedal lives below the key bed.  Reserve this complete
    // console depth when screen-bottom alignment is enabled so the pedal is not
    // pushed off-screen merely to put the ivory fronts at viewport zero.
    private const float PedalAnchorLocalY = -6.15f;
    // Local +Z is the track/rear side. Keep the pedal mechanically behind the
    // keys while the lower Y clearance prevents a depressed key intersecting it.
    private const float PedalAnchorLocalZ = KeyDepth * 0.34f;
    private const float PedalVisualReserveLocalDepth = 6.60f;
    private const float ConsoleBottomLocalY = -6.90f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static IvoryLaneKeyboard instance;

    private readonly Transform[] keys = new Transform[LaneCount];
    private readonly Renderer[][] keyRenderers = new Renderer[LaneCount][];
    private Mesh keyMesh;
    private float keyMeshWidth = float.NaN;
    private readonly bool[] manualPressed = new bool[LaneCount];
    private readonly bool[] visualPressed = new bool[LaneCount];
    /// <summary>
    /// Lanes held on a MIDI device, mirrored from the device itself rather than
    /// accumulated from events.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="manualPressed"/> because the two have opposite
    /// failure modes. A computer key always reports its release, so counting
    /// events is safe; a MIDI note-off can be lost, arrive with a pitch that
    /// maps to no lane, or map to a different lane than its note-on did — and
    /// any of those leaves a key lit forever, which the bloom turns into a ball
    /// of light parked on the judgment line. Mirroring the device state each
    /// frame cannot leak: whatever is not held now is not lit now.
    /// </remarks>
    private readonly bool[] midiPressed = new bool[LaneCount];
    private readonly float[] visualPressAmounts = new float[LaneCount];
    private readonly float[] pulseUntil = new float[LaneCount];
    /// <summary>誤觸的紅燈熄滅的時刻，每個鍵道一個。</summary>
    private readonly float[] wrongUntil = new float[LaneCount];
    private readonly bool[] visualWrong = new bool[LaneCount];
    /// <summary>偏格的黃燈熄滅的時刻，每個鍵道一個。</summary>
    private readonly float[] offCentreUntil = new float[LaneCount];
    private MaterialPropertyBlock colorBlock;
    private readonly int[] autoHoldCounts = new int[LaneCount];
    private readonly Dictionary<NoteController, int> autoHeldNotes = new Dictionary<NoteController, int>();
    private readonly List<NoteController> autoHoldReleaseScratch = new List<NoteController>();

    private Material sharedMaterial;
    private Vector3 trackNormal = Vector3.up;
    private bool built;
    private bool pendingBottomAlignment = true;
    private bool screenBottomAlignmentEnabled = true;
    private Transform pedalAnchor;
    private Transform cachedJudgmentLine;
    private Vector3 lastLayoutCameraPosition = new Vector3(float.PositiveInfinity, 0f, 0f);
    private Quaternion lastLayoutCameraRotation = Quaternion.identity;
    private Vector3 lastLayoutLinePosition = new Vector3(float.PositiveInfinity, 0f, 0f);
    private bool capturedConsoleLayout;
    private Quaternion consoleRotationInCameraSpace = Quaternion.identity;

    /// <summary>
    /// 鍵盤面在世界座標的傾角（度），拋物線模式用。null = 照原本的做法
    /// （姿態鎖在鏡頭座標系裡，鏡頭轉它就跟著轉）。
    /// </summary>
    /// <remarks>
    /// 拋物線模式要的是「鍵盤貼著弧線落地的切線」——那是一個**世界空間**的角度，
    /// 和鏡頭無關；鏡頭再垂直看過去。鎖在鏡頭座標系的原做法給不出這件事，所以
    /// 這裡給一個明確的覆寫，切回傾斜模式時設回 null 就恢復原狀。
    /// </remarks>
    private static float? worldTiltOverrideDeg;

    public static void SetWorldTiltOverride(float? degrees)
    {
        worldTiltOverrideDeg = degrees;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureController();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureController();
    }

    private static void EnsureController()
    {
        if (instance != null) return;
        var existing = FindFirstObjectByType<IvoryLaneKeyboard>();
        if (existing != null)
        {
            instance = existing;
            return;
        }

        var root = new GameObject("IvoryLaneKeyboard");
        instance = root.AddComponent<IvoryLaneKeyboard>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (sharedMaterial != null) Destroy(sharedMaterial);
    }

    private void Update()
    {
        using (HitchProbe.Measure("keyboard")) UpdateCore();
    }

    private void UpdateCore()
    {
        if (!built)
        {
            TryBuild();
            return;
        }

        float now = Time.unscaledTime;
        UpdateAutoHeldNotes();
        for (int lane = 0; lane < LaneCount; lane++)
        {
            bool down = manualPressed[lane] || midiPressed[lane]
                || autoHoldCounts[lane] > 0 || pulseUntil[lane] > now;
            bool wrong = wrongUntil[lane] > now;
            bool offCentre = !wrong && offCentreUntil[lane] > now;
            // 紅燈和黃燈都是會退掉的，所以亮著的那幾格每幀都要重上色；其餘照舊
            // 只在狀態改變時上色一次。
            //
            // 紅蓋過黃：同一下同時是誤觸又是偏格是不可能的（誤觸根本沒有音符可
            // 偏），但萬一兩個計時器疊在一起，該讓比較嚴重的那個說話。
            if (wrong || offCentre)
            {
                visualPressed[lane] = down;
                visualWrong[lane] = true;
                ApplyRendererColors(lane, down,
                    wrong ? Mathf.Clamp01((wrongUntil[lane] - now) / WrongFlashSeconds) : 0f,
                    offCentre
                        ? Mathf.Clamp01((offCentreUntil[lane] - now) / OffCentreFlashSeconds)
                        : 0f);
            }
            else if (down != visualPressed[lane] || visualWrong[lane])
            {
                visualWrong[lane] = false;
                ApplyState(lane, down);
            }
            AnimateKey(lane, Time.unscaledDeltaTime);
        }
    }

    /// <summary>Switches one independent lane to its physical pressed/released state.</summary>
    public static void SetLanePressed(int lane, bool pressed)
    {
        if (lane < 0 || lane >= LaneCount) return;
        EnsureController();
        instance.manualPressed[lane] = pressed;
        if (instance.built)
            instance.ApplyState(lane, pressed || instance.midiPressed[lane]
                || instance.autoHoldCounts[lane] > 0 || instance.pulseUntil[lane] > Time.unscaledTime);
    }

    /// <summary>
    /// Mirrors one lane's MIDI hold state. Safe to call every frame with the
    /// device's own answer; Update picks the change up.
    /// </summary>
    public static void SetMidiLanePressed(int lane, bool pressed)
    {
        if (lane < 0 || lane >= LaneCount) return;
        if (instance == null) return;
        instance.midiPressed[lane] = pressed;
    }

    /// <summary>
    /// Names which input claims each lit lane, so a stuck key can be attributed.
    /// </summary>
    /// <remarks>
    /// Four independent sources can hold a key down and they are indistinguishable
    /// once the key is drawn: a computer key, a MIDI key, an auto-play hold, and a
    /// debug pulse. Knowing the lane is stuck says nothing about which of them is
    /// still claiming it, and that is the whole question.
    /// </remarks>
    public static string DescribeHeldLanes()
    {
        if (instance == null) return "keyboard=<none>";
        var sb = new System.Text.StringBuilder("keys:");
        bool any = false;
        float now = Time.unscaledTime;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if (!instance.visualPressed[lane]) continue;
            any = true;
            sb.Append($" [{lane}");
            if (instance.manualPressed[lane]) sb.Append(" manual");
            if (instance.midiPressed[lane]) sb.Append(" midi");
            if (instance.autoHoldCounts[lane] > 0) sb.Append($" autoHold={instance.autoHoldCounts[lane]}");
            if (instance.pulseUntil[lane] > now) sb.Append(" pulse");
            sb.Append(']');
        }
        if (!any) sb.Append(" none lit");
        return sb.ToString();
    }

    /// <summary>Releases manual, DEBUG pulse and hold state left by an unloaded chart.</summary>
    public static void ResetAllPressedStates()
    {
        if (instance == null) return;
        instance.autoHeldNotes.Clear();
        for (int lane = 0; lane < LaneCount; lane++)
        {
            instance.manualPressed[lane] = false;
            instance.midiPressed[lane] = false;
            instance.autoHoldCounts[lane] = 0;
            instance.pulseUntil[lane] = 0f;
            instance.wrongUntil[lane] = 0f;
            instance.offCentreUntil[lane] = 0f;
            instance.visualWrong[lane] = false;
            if (instance.built) instance.ApplyState(lane, false);
        }
    }

    public static bool TryGetPedalAnchor(out Transform anchor)
    {
        anchor = instance != null && instance.built ? instance.pedalAnchor : null;
        return anchor != null;
    }

    private void LateUpdate()
    {
        using (HitchProbe.Measure("keyboardLate")) LateUpdateCore();
    }

    private void LateUpdateCore()
    {
        if (!built) return;
        Camera camera = Camera.main;
        if (cachedJudgmentLine == null)
        {
            GameObject lineObject = GameObject.Find("JudgmentLine");
            cachedJudgmentLine = lineObject != null ? lineObject.transform : null;
        }
        if (camera == null || cachedJudgmentLine == null) return;

        Transform line = cachedJudgmentLine;
        bool cameraChanged = (camera.transform.position - lastLayoutCameraPosition).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(camera.transform.rotation, lastLayoutCameraRotation) > 0.0001f;
        bool lineChanged = (line.position - lastLayoutLinePosition).sqrMagnitude > 0.000001f;
        if (!cameraChanged && !lineChanged) return;

        if (RestoreJudgmentLinePlacement(camera) && screenBottomAlignmentEnabled)
            AlignBuiltKeyboardToScreenBottom(camera);
        lastLayoutCameraPosition = camera.transform.position;
        lastLayoutCameraRotation = camera.transform.rotation;
        lastLayoutLinePosition = line.position;
    }

    /// <summary>Brief state-only press used by DEBUG auto judgment.</summary>
    public static void PulseForNote(NoteController note)
    {
        if (note == null || note.NoteData == null) return;
        PulseLane(ResolveDebugLane(note.NoteData.startLane, note.NoteData.endLane));
    }

    /// <summary>
    /// Keeps the selected DEBUG key down for the complete hold judgment interval.
    /// Overlapping holds are reference-counted so one ending note cannot release another.
    /// </summary>
    public static void HoldForNote(NoteController note)
    {
        if (note == null || note.NoteData == null) return;
        EnsureController();
        if (instance.autoHeldNotes.ContainsKey(note)) return;

        int lane = ResolveDebugLane(note.NoteData.startLane, note.NoteData.endLane);
        instance.autoHeldNotes.Add(note, lane);
        instance.autoHoldCounts[lane]++;
        if (instance.built) instance.ApplyState(lane, true);
    }

    /// <summary>
    /// Lights one key red: this press belonged to no note on screen.
    /// </summary>
    /// <remarks>
    /// 演奏會模式的紅燈。呼叫點在誤觸真的成立的那一刻（<c>FlushPendingStrays</c>），
    /// 也就是那一下已經等過一個操作幀、沒有被鄰近的音符吸收——和誤觸音出聲
    /// 的是同一個判斷。擦到隔壁鍵那種被吃掉的按鍵不會亮紅燈，否則和弦邊緣
    /// 的輕擦會讓整排鍵盤閃個不停。
    /// </remarks>
    public static void FlashWrong(int lane)
    {
        if (lane < 0 || lane >= LaneCount) return;
        EnsureController();
        if (instance == null) return;
        instance.wrongUntil[lane] = Time.unscaledTime + WrongFlashSeconds;
    }

    /// <summary>
    /// Lights one key amber: the right note, taken off its middle key.
    /// </summary>
    /// <remarks>
    /// 演奏會模式的黃燈。和紅燈是一組對照：**紅是不該按的鍵，黃是該按的音符按
    /// 偏了格**。兩者都是「這一鍵有問題」，而顏色本身就講出了嚴重程度 —— 玩家
    /// 不需要記規則，紅比黃嚴重是所有人都已經知道的事。
    ///
    /// 它只在演奏會模式出現：一般模式裡音符哪一格都一樣，沒有「偏格」這回事，
    /// 亮起來只會變成一個沒有意義的閃爍。
    /// </remarks>
    public static void FlashOffCentre(int lane)
    {
        if (lane < 0 || lane >= LaneCount) return;
        EnsureController();
        if (instance == null) return;
        instance.offCentreUntil[lane] = Time.unscaledTime + OffCentreFlashSeconds;
    }

    public static void PulseLane(int lane)
    {
        if (lane < 0 || lane >= LaneCount) return;
        EnsureController();
        instance.pulseUntil[lane] = Mathf.Max(instance.pulseUntil[lane], Time.unscaledTime + DebugPulseSeconds);
        if (instance.built) instance.ApplyState(lane, true);
    }

    /// <summary>
    /// Three-wide notes use the middle lane; two-wide notes use the right lane.
    /// Wider even spans also use the right-hand centre to keep the rule deterministic.
    /// </summary>
    public static int ResolveDebugLane(int startLane, int endLane)
    {
        int min = Mathf.Clamp(Mathf.Min(startLane, endLane), 0, LaneCount - 1);
        int max = Mathf.Clamp(Mathf.Max(startLane, endLane), 0, LaneCount - 1);
        return min + ((max - min + 1) / 2);
    }

    /// <summary>
    /// Compensates a Camera-Z change by translating only the keyboard visual
    /// along the track plane. JudgmentLine and all gameplay anchors stay fixed.
    /// </summary>
    public static void CompensateForCameraZ(Camera camera)
    {
        SetScreenBottomAlignment(true, camera);
    }

    /// <summary>
    /// Applies or removes screen-bottom alignment. Removing it restores the
    /// authored placement beside the judgment line instead of retaining the
    /// last camera compensation offset.
    /// </summary>
    public static void SetScreenBottomAlignment(bool enabled, Camera camera)
    {
        if (camera == null) return;

        EnsureController();
        if (instance == null) return;
        instance.screenBottomAlignmentEnabled = enabled;
        instance.pendingBottomAlignment = enabled;
        if (!instance.built)
        {
            return;
        }

        if (!instance.RestoreJudgmentLinePlacement(camera)) return;
        if (!enabled) return;

        instance.AlignBuiltKeyboardToScreenBottom(camera);
    }

    private bool RestoreJudgmentLinePlacement(Camera camera)
    {
        if (camera == null) return false;

        GameObject lineObject = GameObject.Find("JudgmentLine");
        GameObject trackObject = GameObject.Find("Track");
        if (lineObject == null || trackObject == null) return false;

        Transform line = lineObject.transform;
        Transform track = trackObject.transform;
        Vector3 normal = track.up.sqrMagnitude > 0.0001f ? track.up.normalized : Vector3.up;
        Vector3 towardPlayer = Vector3.ProjectOnPlane(
            camera.transform.position - line.position, normal).normalized;
        if (towardPlayer.sqrMagnitude < 0.5f) towardPlayer = -track.forward.normalized;
        Vector3 right = Vector3.Cross(normal, towardPlayer).normalized;
        if (Vector3.Dot(right, track.right) < 0f) right = -right;
        Vector3 rowForward = Vector3.Cross(right, normal).normalized;

        float surfaceHeight = track.GetComponentInChildren<Renderer>() != null
            ? Vector3.Dot(track.GetComponentInChildren<Renderer>().bounds.center, normal)
            : Vector3.Dot(track.position, normal);
        Vector3 surfacePoint = line.position;
        surfacePoint += normal * (surfaceHeight - Vector3.Dot(surfacePoint, normal));
        float visibleDepth = KeyDepth * FixedConsoleDepthScale;
        Vector3 rowCenter = surfacePoint + towardPlayer * (JudgmentGap + visibleDepth * 0.5f)
                            + normal * (KeyHeight * 0.5f + 0.035f);

        trackNormal = normal;
        transform.SetPositionAndRotation(rowCenter, Quaternion.LookRotation(rowForward, normal));
        transform.localScale = new Vector3(1f, 1f, FixedConsoleDepthScale);
        return true;
    }

    private void AlignBuiltKeyboardToScreenBottom(Camera camera)
    {
        GameObject lineObject = GameObject.Find("JudgmentLine");
        GameObject trackObject = GameObject.Find("Track");
        if (lineObject == null || trackObject == null) return;

        Transform line = lineObject.transform;
        Transform track = trackObject.transform;
        Vector3 normal = track.up.sqrMagnitude > 0.0001f ? track.up.normalized : Vector3.up;
        Vector3 towardPlayer = Vector3.ProjectOnPlane(camera.transform.position - line.position, normal).normalized;
        if (towardPlayer.sqrMagnitude < 0.5f) towardPlayer = -track.forward.normalized;

        Vector3 right = Vector3.Cross(normal, towardPlayer).normalized;
        if (Vector3.Dot(right, track.right) < 0f) right = -right;
        Vector3 rowForward = Vector3.Cross(right, normal).normalized;

        float surfaceHeight = track.GetComponentInChildren<Renderer>() != null
            ? Vector3.Dot(track.GetComponentInChildren<Renderer>().bounds.center, normal)
            : Vector3.Dot(track.position, normal);
        Vector3 surfacePoint = line.position;
        surfacePoint += normal * (surfaceHeight - Vector3.Dot(surfacePoint, normal));

        // Capture the complete console pose relative to the camera. Later camera
        // angle changes reuse that exact relative rotation, so only the playfield
        // above the judgment line changes perspective.
        Quaternion authoredRotation = Quaternion.LookRotation(rowForward, normal);
        if (!capturedConsoleLayout)
        {
            consoleRotationInCameraSpace =
                Quaternion.Inverse(camera.transform.rotation) * authoredRotation;
            capturedConsoleLayout = true;
        }
        Quaternion consoleRotation =
            camera.transform.rotation * consoleRotationInCameraSpace;
        if (worldTiltOverrideDeg.HasValue)
        {
            // 鍵盤面繞 X 轉到切線角度：左右方向（rowForward 的水平分量）保持不變，
            // 只有前後的仰角被指定。
            Vector3 flatForward = Vector3.ProjectOnPlane(rowForward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
            consoleRotation = Quaternion.AngleAxis(-worldTiltOverrideDeg.Value, Vector3.right)
                              * Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        }
        Vector3 consoleNormal = consoleRotation * Vector3.up;
        Vector3 consoleTowardPlayer = consoleRotation * Vector3.back;

        // Rear hinge follows the judgment line, but the authored keyboard depth is
        // constant. No viewport measurement is allowed to stretch the keys.
        Vector3 rearHinge = surfacePoint + consoleTowardPlayer * JudgmentGap
                            + consoleNormal * (KeyHeight * 0.5f + 0.035f);
        transform.SetPositionAndRotation(
            rearHinge + consoleTowardPlayer *
                (KeyDepth * FixedConsoleDepthScale * 0.5f),
            consoleRotation);
        transform.localScale = new Vector3(1f, 1f, FixedConsoleDepthScale);

        // Screen-bottom mode is a presentation constraint, not another track
        // offset.  Lock the lowest player-facing edge to viewport Y so Camera Z,
        // aspect ratio and resolution cannot leave a strip below the keyboard.
        Vector3 consoleBottomWorld = transform.TransformPoint(new Vector3(
            0f, ConsoleBottomLocalY,
            PedalAnchorLocalZ - PedalVisualReserveLocalDepth));
        Vector3 viewport = camera.WorldToViewportPoint(consoleBottomWorld);
        if (viewport.z > 0.001f)
        {
            Vector3 targetWorld = camera.ViewportToWorldPoint(new Vector3(
                viewport.x, ScreenBottomViewportY, viewport.z));
            transform.position += targetWorld - consoleBottomWorld;
        }
    }

    private void TryBuild()
    {
        var lineObject = GameObject.Find("JudgmentLine");
        var trackObject = GameObject.Find("Track");
        if (lineObject == null || trackObject == null || !trackObject.activeInHierarchy) return;

        Transform line = lineObject.transform;
        Transform track = trackObject.transform;
        Camera camera = Camera.main;
        if (camera == null) return;

        float trackWidth = PianoVisualLayout.ResolveTrackWidth(track);
        float laneWidth = trackWidth / LaneCount;
        // Restore the original visible separation between the 28 independent keys.
        float keyWidth = laneWidth * 0.94f;

        trackNormal = track.up.normalized;
        Vector3 towardPlayer = Vector3.ProjectOnPlane(camera.transform.position - line.position, trackNormal).normalized;
        if (towardPlayer.sqrMagnitude < 0.5f) towardPlayer = -track.forward;
        Vector3 right = Vector3.Cross(trackNormal, towardPlayer).normalized;
        if (Vector3.Dot(right, track.right) < 0f) right = -right;
        Vector3 rowForward = Vector3.Cross(right, trackNormal).normalized;

        float surfaceHeight = track.GetComponentInChildren<Renderer>() != null
            ? Vector3.Dot(track.GetComponentInChildren<Renderer>().bounds.center, trackNormal)
            : Vector3.Dot(track.position, trackNormal);
        Vector3 surfacePoint = line.position;
        surfacePoint += trackNormal * (surfaceHeight - Vector3.Dot(surfacePoint, trackNormal));
        float visibleDepth = KeyDepth * FixedConsoleDepthScale;
        Vector3 rowCenter = surfacePoint + towardPlayer * (JudgmentGap + visibleDepth * 0.5f)
                            + trackNormal * (KeyHeight * 0.5f + 0.035f);

        transform.SetPositionAndRotation(rowCenter, Quaternion.LookRotation(rowForward, trackNormal));
        transform.localScale = new Vector3(1f, 1f, FixedConsoleDepthScale);
        sharedMaterial = CreateIvoryMaterial();
        CreateKeyboardFrame(trackWidth);

        for (int lane = 0; lane < LaneCount; lane++)
        {
            float offset = ((lane + 0.5f) - LaneCount * 0.5f) * laneWidth;
            CreateKey(lane, offset, keyWidth);
        }
        built = true;
        if (pendingBottomAlignment)
            CompensateForCameraZ(camera);
    }

    private void CreateKeyboardFrame(float trackWidth)
    {
        Renderer rearRail = CreatePart(transform, "RearBrassRail",
            new Vector3(trackWidth + 0.30f, 0.075f, 0.16f),
            new Vector3(0f, KeyHeight * 0.5f + 0.015f, KeyDepth * 0.5f - 0.10f));
        SetRendererColor(rearRail, new Color(0.62f, 0.43f, 0.17f, 1f), new Color(0.08f, 0.045f, 0.012f, 1f));

        pedalAnchor = new GameObject("PianoPedalAnchor").transform;
        pedalAnchor.SetParent(transform, false);
        pedalAnchor.localPosition = new Vector3(
            0f, PedalAnchorLocalY, PedalAnchorLocalZ);
        // The keyboard console is compressed to 40% along its depth axis, but
        // the pedal is separate hardware and must retain its authored length.
        pedalAnchor.localScale = new Vector3(1f, 1f, 1f / FixedConsoleDepthScale);
    }

    private void CreateKey(int lane, float xOffset, float width)
    {
        var keyRoot = new GameObject($"IvoryKey_{lane:00}").transform;
        keyRoot.SetParent(transform, false);
        // The root is the piano-key hinge. Local +Z points back toward the track,
        // so all geometry extends along -Z toward the player.
        keyRoot.localPosition = new Vector3(xOffset, 0f, KeyDepth * 0.5f);

        // 鍵身是一塊生出來的網格，不是疊起來的方盒。琴鍵的樣子來自**倒角的頂緣**
        // 和**圓弧的前唇** —— 方盒只有三個平面，每面在任何光下都只回傳一個平坦的
        // 值，高光沒有地方跑，所以顏色怎麼調都還是塑膠磚。
        Renderer body = CreateMeshPart(keyRoot, "KeyBody", EnsureKeyMesh(width), Vector3.zero);
        // The vertical fascia overlaps both the key body and the continuous apron.
        // This makes the keyboard read as one thick cabinet instead of 28 floating
        // tiles, while retaining a hairline joint between individual keys.
        Renderer frontFace = CreatePart(keyRoot, "IvoryFrontFace",
            new Vector3(width * 0.999f, KeyHeight * 3.35f, 0.34f),
            new Vector3(0f, -KeyHeight * 1.13f, -KeyDepth + 0.10f));
        Renderer goldLip = CreatePart(keyRoot, "GoldLip",
            new Vector3(width * 0.74f, 0.050f, 0.085f),
            new Vector3(0f, KeyHeight * 0.5f + 0.027f, -KeyDepth * 0.94f));
        keys[lane] = keyRoot;
        keyRenderers[lane] = new[] { body, frontFace, goldLip };
        ApplyRendererColors(lane, false);
    }

    /// <summary>
    /// The key body: a top face with a chamfered edge all round and a rounded
    /// front lip, generated once and shared by every key of the same width.
    /// </summary>
    /// <remarks>
    /// Built as a small grid over (across, along). The chamfer is a quarter arc
    /// sampled three times on each side and the front lip another quarter arc,
    /// and the two drops simply add -- so the front corners round off on their
    /// own without a special case, which is the whole reason for laying it out
    /// as a grid rather than as separate strips.
    ///
    /// Closed on every side. The first version left the back and the underside
    /// out on the grounds that the track and the key bed cover them -- they do
    /// not, from the gameplay camera, and an open face is invisible from outside,
    /// so the key read as see-through. Every wall's winding is decided by testing
    /// its normal against the direction it should face, rather than by getting
    /// the vertex order right by hand six times.
    /// </remarks>
    private Mesh EnsureKeyMesh(float width)
    {
        if (keyMesh != null && Mathf.Approximately(keyMeshWidth, width)) return keyMesh;

        const int Columns = 6;
        const int Rows = 4;
        float half = width * 0.5f;
        float top = KeyHeight * 0.5f;
        float bottom = -top;
        float chamfer = Mathf.Min(width * 0.16f, 0.09f);
        float lip = Mathf.Min(KeyDepth * 0.05f, KeyHeight * 0.8f);

        var columnX = new float[Columns];
        var columnDrop = new float[Columns];
        for (int i = 0; i < 3; i++)
        {
            float a = i / 2f * Mathf.PI * 0.5f;
            float out_ = chamfer * Mathf.Sin(a);
            float down = chamfer - chamfer * Mathf.Cos(a);
            columnX[2 - i] = -(half - chamfer) - out_;
            columnDrop[2 - i] = down;
            columnX[3 + i] = (half - chamfer) + out_;
            columnDrop[3 + i] = down;
        }

        var rowZ = new float[Rows];
        var rowDrop = new float[Rows];
        rowZ[0] = 0f;
        rowZ[1] = -(KeyDepth - lip);
        for (int i = 1; i < 3; i++)
        {
            float a = i / 2f * Mathf.PI * 0.5f;
            rowZ[1 + i] = -(KeyDepth - lip) - lip * Mathf.Sin(a);
            rowDrop[1 + i] = lip - lip * Mathf.Cos(a);
        }

        var vertices = new System.Collections.Generic.List<Vector3>(Columns * Rows + 32);
        var triangles = new System.Collections.Generic.List<int>(256);

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Columns; col++)
                vertices.Add(new Vector3(columnX[col], top - columnDrop[col] - rowDrop[row], rowZ[row]));

        for (int row = 0; row < Rows - 1; row++)
        {
            for (int col = 0; col < Columns - 1; col++)
            {
                int a = row * Columns + col;
                int b = a + 1;
                int c = a + Columns;
                int d = c + 1;
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
                triangles.Add(a); triangles.Add(b); triangles.Add(d);
            }
        }

        // 兩側的裙板：從倒角的外緣垂下去。
        for (int row = 0; row < Rows - 1; row++)
        {
            AddSkirt(vertices, triangles, vertices[row * Columns], vertices[(row + 1) * Columns],
                bottom, Vector3.left);
            AddSkirt(vertices, triangles, vertices[row * Columns + Columns - 1],
                vertices[(row + 1) * Columns + Columns - 1], bottom, Vector3.right);
        }

        // 前緣：圓唇的下緣接到鍵底。後緣：貼著軌道那一面。
        int lastRow = (Rows - 1) * Columns;
        for (int col = 0; col < Columns - 1; col++)
        {
            AddSkirt(vertices, triangles, vertices[lastRow + col], vertices[lastRow + col + 1],
                bottom, Vector3.back);
            AddSkirt(vertices, triangles, vertices[col], vertices[col + 1],
                bottom, Vector3.forward);
        }

        // 鍵底。玩家看不到，但少了它從側面掃過去就會看穿。
        int floor = vertices.Count;
        vertices.Add(new Vector3(columnX[0], bottom, 0f));
        vertices.Add(new Vector3(columnX[Columns - 1], bottom, 0f));
        vertices.Add(new Vector3(columnX[Columns - 1], bottom, -KeyDepth));
        vertices.Add(new Vector3(columnX[0], bottom, -KeyDepth));
        triangles.Add(floor); triangles.Add(floor + 2); triangles.Add(floor + 1);
        triangles.Add(floor); triangles.Add(floor + 3); triangles.Add(floor + 2);

        keyMesh = new Mesh { name = "IvoryLaneKey", hideFlags = HideFlags.DontSave };
        keyMesh.SetVertices(vertices);
        keyMesh.SetTriangles(triangles, 0);
        keyMesh.RecalculateNormals();
        keyMesh.RecalculateBounds();
        keyMeshWidth = width;
        return keyMesh;
    }

    /// <summary>
    /// Drops a wall from an edge of the top surface down to the key bed, wound
    /// so that it faces <paramref name="outward"/>.
    /// </summary>
    private static void AddSkirt(System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<int> triangles, Vector3 from, Vector3 to,
        float bottom, Vector3 outward)
    {
        Vector3 fromFloor = new Vector3(from.x, bottom, from.z);
        Vector3 toFloor = new Vector3(to.x, bottom, to.z);

        int first = vertices.Count;
        vertices.Add(from);
        vertices.Add(to);
        vertices.Add(toFloor);
        vertices.Add(fromFloor);

        // 繞序由「它該朝哪邊」決定，不是手算六次頂點順序。算錯一面就是一個洞，
        // 而背面剔除讓那個洞從外面完全看不出來 —— 只會覺得鍵是半透明的。
        bool flip = Vector3.Dot(Vector3.Cross(to - from, toFloor - to), outward) < 0f;
        if (flip)
        {
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
            triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
        }
        else
        {
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first + 2); triangles.Add(first + 3); triangles.Add(first);
        }
    }

    private Renderer CreateMeshPart(Transform parent, string partName, Mesh mesh, Vector3 localPosition)
    {
        var part = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
        part.layer = parent.gameObject.layer;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;

        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = sharedMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        return renderer;
    }

    private Renderer CreatePart(Transform parent, string partName, Vector3 scale, Vector3 localPosition)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.layer = parent.gameObject.layer;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = sharedMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        return renderer;
    }

    private static Material CreateIvoryMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = "IvoryLaneKey_Runtime" };
        material.hideFlags = HideFlags.DontSave;
        material.SetFloat("_Metallic", 0.06f);
        // 拋光的象牙。反光要看得出來，粗糙面在聚光燈下只會變成一塊平的灰。
        material.SetFloat("_Smoothness", 0.86f);
        material.EnableKeyword("_EMISSION");
        return material;
    }

    private void UpdateAutoHeldNotes()
    {
        if (autoHeldNotes.Count == 0)
        {
            // A count can survive its dictionary entry if the two ever disagree,
            // and nothing else would ever bring it back down.
            for (int lane = 0; lane < LaneCount; lane++) autoHoldCounts[lane] = 0;
            return;
        }

        float songPosition = float.MinValue;
        bool songRunning = false;
        try
        {
            var conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
            if (conductor != null)
            {
                songPosition = conductor.effectiveSongPosition;
                songRunning = conductor.isActive;
            }
        }
        catch { }

        autoHoldReleaseScratch.Clear();
        foreach (var pair in autoHeldNotes)
        {
            NoteController note = pair.Key;
            // Notes are pooled, so a finished one is deactivated rather than
            // destroyed: it stays non-null with its old NoteData, and the endTime
            // test below can then never come true. That stranded the key down for
            // the rest of the song — and a held key's emission sits at 2.8x, well
            // past the bloom threshold, so it read as a ball of light parked on
            // the judgment line. Recycling is the real "this note is over" signal.
            bool release = note == null || note.NoteData == null
                || !note.gameObject.activeInHierarchy
                || !songRunning;
            if (!release && songPosition > float.MinValue)
                release = songPosition >= note.NoteData.endTime;
            if (release) autoHoldReleaseScratch.Add(note);
        }

        for (int i = 0; i < autoHoldReleaseScratch.Count; i++)
        {
            NoteController note = autoHoldReleaseScratch[i];
            if (!autoHeldNotes.TryGetValue(note, out int lane)) continue;
            autoHeldNotes.Remove(note);
            autoHoldCounts[lane] = Mathf.Max(0, autoHoldCounts[lane] - 1);
        }
    }

    private void ApplyState(int lane, bool pressed)
    {
        if (!built || keys[lane] == null) return;
        visualPressed[lane] = pressed;
        ApplyRendererColors(lane, pressed);
    }

    private void AnimateKey(int lane, float deltaTime)
    {
        Transform key = keys[lane];
        if (key == null) return;

        float target = visualPressed[lane] ? 1f : 0f;
        float duration = visualPressed[lane] ? PressSeconds : ReleaseSeconds;
        float rate = duration > 0.0001f ? 1f / duration : 1000f;
        visualPressAmounts[lane] = Mathf.MoveTowards(
            visualPressAmounts[lane], target, Mathf.Max(0f, deltaTime) * rate);

        float amount = visualPressAmounts[lane];
        float eased = amount * amount * (3f - 2f * amount);
        Vector3 keyPosition = key.localPosition;
        keyPosition.y = PressClearanceLift * eased;
        keyPosition.z = KeyDepth * 0.5f - PressTowardPlayerShift * eased;
        key.localPosition = keyPosition;
        key.localRotation = Quaternion.Euler(PressAngle * eased, 0f, 0f);
    }

    private void ApplyRendererColors(int lane, bool pressed)
    {
        ApplyRendererColors(lane, pressed, 0f, 0f);
    }

    /// <param name="wrong">
    /// 誤觸紅燈的殘量，1 是剛按錯、0 是已經退乾淨。
    /// </param>
    /// <param name="offCentre">
    /// 偏格黃燈的殘量。和紅燈是同一套機制、不同的顏色和長度。
    /// </param>
    private void ApplyRendererColors(int lane, bool pressed, float wrong, float offCentre)
    {
        var renderers = keyRenderers[lane];
        if (renderers == null) return;

        // Resting keys use a deeper antique ivory. A pressed key changes to a
        // cool pale-green porcelain so the state is readable even without Bloom.
        //
        // The resting key surfaces sit darker than the brass lip that edges them.
        // Ivory this pale flattens against a dark track — the keys read as one
        // slab and the lip disappears into them. Dropping the surfaces and
        // lighting the lip separates the two: the eye gets an edge to follow
        // along the keyboard instead of a single field of off-white.
        // 舞台燈打在鍵盤中央，兩端收到七成。均勻打亮讀起來像日光燈，不像聚光燈；
        // 這條曲線就是照片裡鍵盤中段亮、兩翼沉下去的那個樣子。
        float centred = Mathf.Abs((lane + 0.5f) / LaneCount * 2f - 1f);
        float lit = Mathf.Lerp(1f, 0.70f, centred * centred);

        Color body = pressed ? new Color(0.90f, 1.00f, 0.92f, 1f) : Light(0.94f, 0.93f, 0.88f, lit);
        Color front = pressed ? new Color(0.76f, 0.96f, 0.79f, 1f) : Light(0.79f, 0.76f, 0.70f, lit);
        // Deeper brass than the surfaces, and the only resting part that emits.
        Color gold = pressed ? new Color(0.69f, 0.96f, 0.66f, 1f) : new Color(0.46f, 0.31f, 0.11f, 1f);
        Color[] colors = { body, front, gold };

        Color paleGreenGlow = new Color(0.42f, 1.00f, 0.54f, 1f);

        // 誤觸：整顆鍵往紅的推過去，發光也換成紅的。用混色而不是直接蓋掉，
        // 紅燈退到一半的時候鍵才有從紅色回到象牙色的過程，而不是啪一下切掉。
        //
        // 顏色壓得更深、發光推得更亮：深的鍵身讓它在一排象牙白裡「破洞」一樣明
        // 顯，亮的邊緣讓餘光也接得到。這兩件事要一起做 —— 只調亮度的話，泛光會
        // 把整顆鍵糊成一團白，反而看不出是紅的。
        Color wrongBody = new Color(0.52f, 0.045f, 0.045f, 1f);
        Color wrongGlow = new Color(1.35f, 0.10f, 0.08f, 1f);

        // 偏格：琥珀黃。和紅燈刻意不同色相而不是只有深淺之差 —— 同一個顏色的兩
        // 種亮度在餘光裡分不出來，而這兩件事的處理方式完全不同（一個要改按法，
        // 一個要改位置）。
        Color strayBody = new Color(0.58f, 0.40f, 0.05f, 1f);
        Color strayGlow = new Color(1.00f, 0.70f, 0.12f, 1f);

        bool amber = offCentre > 0f && wrong <= 0f;
        Color faultBody = amber ? strayBody : wrongBody;
        Color faultGlow = amber ? strayGlow : wrongGlow;
        float fault = amber ? offCentre : wrong;
        if (fault > 0f)
        {
            // 紅燈用**開頭很陡**的曲線，黃燈維持平滑。
            //
            // 誤觸要在發生的那一下就被看見，所以它前段幾乎不衰減、後段才退；
            // 偏格只是提醒，平滑地來平滑地走就好。
            float eased = amber
                ? fault * fault * (3f - 2f * fault)
                : Mathf.Sqrt(fault);
            body = Color.Lerp(body, faultBody, eased);
            front = Color.Lerp(front, faultBody * 0.85f, eased);
            gold = Color.Lerp(gold, faultBody * 0.7f, eased);
            colors = new[] { body, front, gold };
        }

        MaterialPropertyBlock block = colorBlock ?? (colorBlock = new MaterialPropertyBlock());
        for (int i = 0; i < renderers.Length; i++)
        {
            // Prevent the track/key-bed shadow from blacking out the long
            // player-side half of a glowing pressed key.
            renderers[i].receiveShadows = false;
            block.Clear();
            block.SetColor(BaseColorId, colors[i]);
            block.SetColor(ColorId, colors[i]);
            // Resting: the surfaces keep only the faintest lift, while the lip
            // carries real emission. 1.4 on a colour of luminance 0.33 adds about
            // 0.47 — clearly the brightest thing on the key, and still far below
            // the 1.30 bloom threshold, so it stays a crisp edge rather than a
            // glow that smears along the whole keyboard.
            float emissionStrength = pressed
                ? (i == 0 ? 2.7f : i == 1 ? 2.1f : 1.8f)
                : (i == 0 ? 0.10f * lit : i == 1 ? 0.04f * lit : 1.4f);
            Color emissionColor = emissionStrength > 0f
                ? (pressed ? paleGreenGlow : colors[i]) * emissionStrength
                : Color.black;
            if (fault > 0f)
            {
                // 3.0 在泛光門檻之上——誤觸要在餘光裡就看得到，不用盯著鍵盤。
                // 黃燈收到 2.1：它是提醒不是警報，亮到和誤觸一樣會讓兩件事在
                // 餘光裡變成同一件事。
                float eased = fault * fault * (3f - 2f * fault);
                float strength = amber ? (i == 2 ? 1.6f : 2.1f) : (i == 2 ? 3.0f : 4.2f);
                emissionColor = Color.Lerp(emissionColor, faultGlow * strength, eased);
            }
            block.SetColor(EmissionColorId, emissionColor);
            renderers[i].SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// A resting key surface, dimmed by how far down the keyboard it sits.
    /// </summary>
    /// <remarks>
    /// The surfaces used to rest around 0.5-0.6 so the brass lip could be the
    /// brightest thing on the key. Under a stage light the keys are the brightest
    /// thing in the room, so they are pale now and the lip keeps its separation
    /// from its emission instead of from being lighter than its neighbours.
    /// Everything stays under the 1.30 bloom threshold: the cap tops out near
    /// 1.05 with its emission, so it reads as lit rather than blooming.
    /// </remarks>
    private static Color Light(float r, float g, float b, float lit)
    {
        return new Color(r * lit, g * lit, b * lit, 1f);
    }

    private static void SetRendererColor(Renderer renderer, Color baseColor, Color emission)
    {
        if (renderer == null) return;
        var block = new MaterialPropertyBlock();
        block.SetColor(BaseColorId, baseColor);
        block.SetColor(EmissionColorId, emission);
        renderer.SetPropertyBlock(block);
    }
}
