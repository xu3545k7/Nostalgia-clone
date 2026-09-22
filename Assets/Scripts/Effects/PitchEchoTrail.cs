using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Every note you actually hit drifts back up the track as a faint bar, placed
/// on an invisible 88-key keyboard laid across the same width as the lanes.
/// </summary>
/// <remarks>
/// The falling notes say which key to press; they never say where the music
/// sits, because the track is laid out in lanes and not in pitch.  These echoes
/// say it: the piece slowly writes itself up the screen in its real register, so
/// leaps, crossed hands and scale runs leave a visible shape behind you.
///
/// It travels the opposite way to the notes and much slower, which is what keeps
/// the two readable at once -- anything drifting up at a fifth of the fall speed
/// cannot be mistaken for something to hit.  Kept faint for the same reason.
///
/// Drawn on its own screen-space overlay canvas rather than as world geometry.
/// The gameplay screen has an overlay canvas of its own, and an overlay canvas
/// paints after every camera -- a world mesh sitting in the middle of the frame,
/// unculled and unclipped, simply never survives it.  The perspective is still
/// the real one: each echo's corners are worked out in the track's local space
/// and then projected through the camera, so they lean and converge exactly as
/// the track does.
/// </remarks>
[DisallowMultipleComponent]
public sealed class PitchEchoTrail : MonoBehaviour
{
    private const string ObjectName = "~PitchEchoTrail";
    private const string TrackShader = "Nostalgia/Player View Marble Track";
    /// <summary>MIDI 21 is A0, the lowest key on an 88-key piano.</summary>
    private const int LowestKey = 21;
    private const int KeyCount = 88;
    private const int MaxEchoes = 200;
    /// <summary>Slices across the width and along the length. Enough for a smooth falloff.</summary>
    private const int Columns = 6;
    private const int Segments = 3;

    [Header("Keyboard")]
    [Tooltip("88 鍵佔軌道寬度的比例。1 = 和鍵道一樣寬。")]
    [SerializeField, Range(0.2f, 1.2f)] private float keyboardWidth = 1f;
    [Tooltip("每個音符的寬度佔一個琴鍵的比例。1 = 剛好一個鍵；超過 1 相鄰的和弦音會黏成一塊。")]
    [SerializeField, Range(0.2f, 3f)] private float keyFill = 0.7f;
    [Tooltip("抬離軌道表面多高，**世界單位**。局部單位在這個軌道上沒有意義：它的 Y 縮放是 0。")]
    [SerializeField, Range(0.01f, 8f)] private float surfaceLift = 0.6f;

    [Tooltip("殘響往遠端飄的速度，佔軌道可見縱深的比例／秒。長押的長度也是用它"
        + "累積出來的 —— 按住的時候近端停在判定線上，已經畫下的部分照樣以這個速度"
        + "離開，所以按 T 秒就留下 riseSpeed × T 那麼長的一條。")]
    [SerializeField, Range(0.01f, 0.5f)] private float riseSpeed = 0.06f;

    [Tooltip("起點沿軌道往前推多少，佔可見縱深的比例。判定線那條棒子浮在軌道表面"
        + "上方，同深度但較高，所以貼在表面的殘響投影到螢幕會落在它下面 —— 這個"
        + "偏移就是拿來補那段視差的。")]
    [SerializeField, Range(0f, 0.1f)] private float startOffset = 0.005f;
    [Tooltip("可見遠端離判定線多遠，佔軌道半長的比例。太大殘響會飄到看不見的地方。")]
    [SerializeField, Range(0.05f, 1f)] private float visibleDepth = 0.35f;
    [Tooltip("短於這個秒數的一律當點按，不管譜面標成什麼。")]
    [SerializeField, Range(0.05f, 1f)] private float tapThresholdSeconds = 0.25f;
    [Tooltip("點按的長度，也是所有音的下限。")]
    [SerializeField, Range(0f, 0.2f)] private float minimumLength = 0.02f;
    [Tooltip("一條長押最多畫多久（秒）。收不到放開事件時的保險。")]
    [SerializeField, Min(1f)] private float maxHoldSeconds = 12f;
    [Tooltip("飄多久之後完全消失（秒）。")]
    [SerializeField, Min(0.5f)] private float lifetime = 9f;

    [Header("Look")]
    [Tooltip("疊在 gameplay 之上的排序。Overlay 畫布一律在世界物件之上，所以只要"
        + "低於 HUD 的畫布就會落在「軌道之上、HUD 之下」。")]
    [SerializeField] private int sortingOrder = 1;
    [Tooltip("右手（hand 0）的顏色，和音符本身同一條規則。")]
    // 飽和的紅藍讀起來廉價，而且在大理石上很跳。改成帶著暖／冷傾向的低飽和色，
    // 遠看是光而不是塑膠片。
    [SerializeField] private Color rightHandColour = new Color(0.90f, 0.68f, 0.60f, 1f);
    [SerializeField] private Color leftHandColour = new Color(0.63f, 0.74f, 0.88f, 1f);
    [Tooltip("最亮時的不透明度。刻意低，才不會影響下落讀譜。")]
    [SerializeField, Range(0f, 0.6f)] private float opacity = 0.32f;
    [Tooltip("剛射出時的淡入時間（秒），免得在判定線上突然冒出來。")]
    [SerializeField, Range(0f, 1f)] private float fadeInSeconds = 0.25f;

    private struct Echo
    {
        public int key;
        public float length;      // 佔軌道長度的比例
        public float distance;    // 已經飄了多遠，同樣是比例
        public float age;
        public bool rightHand;
        /// <summary>While the key is still down: the tail stays at the judgment
        /// line and the far end keeps drawing away from it.</summary>
        public bool growing;
        public NoteData source;
    }

    private readonly List<Echo> echoes = new List<Echo>(MaxEchoes);
    private readonly Queue<NoteData> pending = new Queue<NoteData>();

    private Renderer trackRenderer;
    private Transform judgmentLine;
    private EchoGraphic graphic;
    private Bounds localBounds;
    private int acrossAxis, alongAxis, normalAxis;
    private bool nearIsMax;
    private bool axesResolved;
    private Chart trackedChart;
    private float nextSearchTime;
    private int searchAttempts;

    private Vector3[] vertices;
    private Color[] colours;
    private int[] triangles;

    private Transform cachedJudgmentOwner;
    private Renderer[] cachedJudgmentRenderers;

    // 一條殘響的格點：(Columns+1) × (Segments+1) 個。相鄰的格子共用邊上的點，
    // 所以每個點只投影一次，而不是每格四個角各投影一次。
    private readonly Vector3[] gridPoints = new Vector3[(Columns + 1) * (Segments + 1)];
    private readonly float[] columnWeight = new float[Columns + 1];
    private readonly float[] columnAcross = new float[Columns + 1];
    private readonly float[] segmentAlpha = new float[Segments + 1];
    private readonly float[] segmentT = new float[Segments + 1];

    // 每一種「什麼都沒出現」的原因各報一次。這個效果失敗的方式全都是靜悄悄的，
    // 從截圖上分辨不出是哪一種。失敗路徑留成警告，成功路徑只是一般 log —— 會動的
    // 功能不該在 Console 留一排黃色。
    private bool reportedNoTrack, reportedNoFilter, reportedNoCamera, reportedFirstEcho, reportedAxes, reportedFirstMesh, reportedJudgmentSpan, reportedFirstHold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject(ObjectName + "Installer");
        DontDestroyOnLoad(host);
        host.AddComponent<PitchEchoTrail>();
    }

    private void OnEnable()
    {
        NoteController.Judged += OnNoteJudged;
        NoteController.HoldStarted += OnHoldStarted;
        NoteController.HoldFinished += OnHoldFinished;
    }

    private void OnDisable()
    {
        NoteController.Judged -= OnNoteJudged;
        NoteController.HoldStarted -= OnHoldStarted;
        NoteController.HoldFinished -= OnHoldFinished;
    }

    /// <summary>
    /// Only records the note here.  The judgment can land in the middle of the
    /// judging pass, and building geometry from inside someone else's loop is a
    /// good way to be the cause of their bug; the queue is drained in Update.
    /// </summary>
    private void OnNoteJudged(NoteData note, JudgmentResult result)
    {
        if (note == null) return;
        if (result == JudgmentResult.Miss) return;
        // A long note that also reports through HoldFinished would otherwise be
        // echoed twice; the release is the one that knows how long it was held.
        if (Mathf.Max(0, note.endTime - note.startTime) / 1000f > tapThresholdSeconds) return;
        if (pending.Count < MaxEchoes) pending.Enqueue(note);
    }

    /// <summary>
    /// The key went down.  The echo starts here and keeps being drawn for as
    /// long as it is held, which is what makes its length the player's own
    /// press rather than anything the chart declared.
    /// </summary>
    private void OnHoldStarted(NoteData note, JudgmentResult result)
    {
        if (note == null || result == JudgmentResult.Miss) return;
        int key = KeyIndex(note);
        if (key < 0 || echoes.Count >= MaxEchoes) return;

        // 同一顆音符若再次按下（放開又補按、判定流程重入），舊的那條要先封口。
        // 不封的話它會一直長到 maxHoldSeconds，而放開事件只會關掉其中一條。
        CloseGrowing(note);

        echoes.Add(new Echo
        {
            key = key,
            length = 0f,
            distance = 0f,
            age = 0f,
            rightHand = note.hand == 0,
            growing = true,
            source = note,
        });
    }

    /// <summary>The key came up: stop drawing and let it drift off like the rest.</summary>
    private void OnHoldFinished(NoteData note, JudgmentResult result)
    {
        if (note == null) return;

        if (CloseGrowing(note)) return;

        // No live echo to close: the press was never seen, so fall back to the
        // chart's own duration rather than dropping the note entirely.
        if (result != JudgmentResult.Miss && pending.Count < MaxEchoes) pending.Enqueue(note);
    }

    /// <summary>
    /// Stops every echo still being drawn for a note, and says whether there was
    /// one.
    /// </summary>
    /// <remarks>
    /// Every one, not the first found: a note that reports a second press before
    /// its release leaves an orphan behind, and an orphan keeps lengthening
    /// until the twelve second guard cuts it off.
    /// </remarks>
    private bool CloseGrowing(NoteData note)
    {
        bool closed = false;
        for (int i = echoes.Count - 1; i >= 0; i--)
        {
            if (!echoes[i].growing || echoes[i].source != note) continue;
            Echo echo = echoes[i];
            echo.growing = false;
            echo.length = Mathf.Max(echo.length, minimumLength);
            echoes[i] = echo;
            closed = true;
        }

        return closed;
    }

    private void Update()
    {
        using (HitchProbe.Measure("echoTrail")) UpdateCore();
    }

    private void UpdateCore()
    {
        // 還沒建好時**保留**佇列，不要清掉。軌道和相機要等歌真的開始才到位，清掉
        // 等於把開頭幾秒的音符全部丟進水裡 —— 佇列本來就有 MaxEchoes 的上限，
        // 留著頂多是開場補畫一批，比整段消失好。
        if (!EnsureBuilt()) return;

        // 換歌、回選曲、或譜面被清掉時把畫面收乾淨。這個元件是 DontDestroyOnLoad，
        // 沒有人會替它清；成長中的長押又不參與生命週期回收，留著就會一直掛在畫面上。
        Chart chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
        if (!ReferenceEquals(chart, trackedChart))
        {
            trackedChart = chart;
            echoes.Clear();
            pending.Clear();
        }

        DrainPending();
        Advance(Time.deltaTime);
        Rebuild();
    }

    /// <summary>
    /// The track does not exist until a song loads, so this keeps looking rather
    /// than giving up after one miss -- the trap the pedal renderer and the track
    /// glass both fell into.
    /// </summary>
    private bool EnsureBuilt()
    {
        if (graphic != null) return true;
        if (Time.unscaledTime < nextSearchTime) return false;
        // 開場密集地找，找不到再慢慢退。半秒一次的話，歌曲前幾秒的音符就沒了。
        nextSearchTime = Time.unscaledTime + (searchAttempts++ < 40 ? 0.05f : 0.5f);

        trackRenderer = FindTrack();
        if (trackRenderer == null)
        {
            if (!reportedNoTrack)
            {
                reportedNoTrack = true;
                Debug.LogWarning("[PitchEchoTrail] 還找不到用 " + TrackShader + " 的 Renderer（歌還沒開始就正常）。");
            }
            return false;
        }

        var filter = trackRenderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            if (!reportedNoFilter)
            {
                reportedNoFilter = true;
                Debug.LogWarning($"[PitchEchoTrail] 軌道 '{trackRenderer.name}' 上沒有 MeshFilter/sharedMesh" +
                                 $"（型別 {trackRenderer.GetType().Name}），量不到它的局部尺寸，殘響無法定位。");
            }
            trackRenderer = null;
            return false;
        }
        localBounds = filter.sharedMesh.bounds;

        // 刻意**不掛在軌道底下**。軌道的縮放是 (10.5, ~0, 100)：Y 被壓成 0，所以在它的
        // 局部座標裡不管抬多高，換算到世界都還是貼在平面上，永遠 z-fighting。
        // 改成自己待在原點、頂點直接用世界座標算 —— 傾斜和透視仍然來自軌道本身，
        // 因為每個點都是拿軌道的 TransformPoint 換出來的。
        // CanvasRenderer 要明講。Graphic 有 [RequireComponent]，但用 GameObject 的
        // 建構子一次帶多個型別時它不會補上，於是 SetMesh 每幀丟
        // MissingComponentException —— 畫面上看起來就只是「什麼都沒有」。
        var host = new GameObject(ObjectName,
            typeof(Canvas), typeof(CanvasRenderer), typeof(EchoGraphic));
        host.layer = trackRenderer.gameObject.layer;
        host.transform.SetParent(transform, false);

        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var rect = host.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        graphic = host.GetComponent<EchoGraphic>();
        graphic.raycastTarget = false;

        ResolveAxes(trackRenderer.transform);
        return axesResolved;
    }

    private static Renderer FindTrack()
    {
        foreach (var candidate in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var materials = candidate.sharedMaterials;
            if (materials == null) continue;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || materials[i].shader == null) continue;
                if (materials[i].shader.name == TrackShader) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Works out how the track lies instead of assuming it.  The thinnest axis of
    /// the slab is its normal; of the other two, the one that travels furthest
    /// horizontally on screen is the one the lanes run across.  A wrong guess
    /// would send the echoes sideways, which is indistinguishable from "it did
    /// not render" in a screenshot.
    /// </summary>
    private void ResolveAxes(Transform host)
    {
        // Camera.main 只找 tag 是 MainCamera 的；遊戲相機沒打 tag 的話這裡會一直是
        // null，整個效果就永遠停在這一行。退回場上任何一台啟用中的相機。
        Camera camera = Camera.main;
        if (camera == null)
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length && camera == null; i++)
                if (cameras[i] != null && cameras[i].isActiveAndEnabled) camera = cameras[i];
        }
        if (camera == null)
        {
            if (!reportedNoCamera)
            {
                reportedNoCamera = true;
                Debug.LogWarning("[PitchEchoTrail] 找不到任何啟用中的相機，無法判斷軌道朝向。");
            }
            return;
        }

        Vector3 size = localBounds.size;
        normalAxis = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);

        int first = -1, second = -1;
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == normalAxis) continue;
            if (first < 0) first = axis; else second = axis;
        }
        if (first < 0 || second < 0) return;

        acrossAxis = ScreenSpread(camera, host, first) >= ScreenSpread(camera, host, second)
            ? first : second;
        alongAxis = acrossAxis == first ? second : first;

        Vector3 low = localBounds.center, high = localBounds.center;
        low[alongAxis] = localBounds.min[alongAxis];
        high[alongAxis] = localBounds.max[alongAxis];
        nearIsMax = Vector3.Distance(camera.transform.position, host.TransformPoint(high)) <
                    Vector3.Distance(camera.transform.position, host.TransformPoint(low));
        axesResolved = true;

        if (!reportedAxes)
        {
            reportedAxes = true;
            Debug.Log($"[PitchEchoTrail] 軌道 '{trackRenderer.name}' 尺寸 {localBounds.size} " +
                             $"→ 橫向軸={acrossAxis} 縱深軸={alongAxis} 法線軸={normalAxis} " +
                             $"近端在{(nearIsMax ? "max" : "min")}，相機 '{camera.name}'。");
        }
    }

    private float ScreenSpread(Camera camera, Transform host, int axis)
    {
        Vector3 low = localBounds.center, high = localBounds.center;
        low[axis] = localBounds.min[axis];
        high[axis] = localBounds.max[axis];
        Vector3 a = camera.WorldToScreenPoint(host.TransformPoint(low));
        Vector3 b = camera.WorldToScreenPoint(host.TransformPoint(high));
        return Mathf.Abs(a.x - b.x);
    }

    private void DrainPending()
    {
        int drained = 0;
        while (pending.Count > 0)
        {
            drained++;
            NoteData note = pending.Dequeue();

            // 顫音是一顆音符包著一串 sub_notes（兩個相鄰鍵的快速交替），母音符自己
            // 沒有可用的音高。展開成每個 sub_note 一條，並照它們原本的時間差錯開，
            // 畫出來就是實際彈奏的交替形狀，而不是一坨。
            if (note.subNotes != null && note.subNotes.Count > 0)
            {
                DrainSubNotes(note, ref drained);
                continue;
            }

            int key = KeyIndex(note);
            if (key < 0) continue;

            // 長度只看時值，不看譜面怎麼標。原本是先判斷 note_type / type 是不是
            // hold，但診斷顯示到達事件的音符沒有一顆帶著 hold 標記 —— 而畫面上明明
            // 有長押。時值本身就帶著「按了多久」這個資訊，不需要再去信一個轉檔過程
            // 中可能遺失的旗標。
            float seconds = Mathf.Max(0, note.endTime - note.startTime) / 1000f;
            bool isHold = seconds > tapThresholdSeconds;
            if (!isHold) seconds = 0f;
            echoes.Add(new Echo
            {
                key = key,
                length = Mathf.Max(minimumLength, seconds * riseSpeed),
                // 補畫的那一批稍微錯開，免得開場全部疊在判定線上變成一條橫槓。
                distance = drained * 0.004f,
                age = 0f,
                rightHand = note.hand == 0,
                growing = false,
                source = note,
            });
        }

        // Oldest first, so dropping from the front sheds the faintest ones.
        while (echoes.Count > MaxEchoes) echoes.RemoveAt(0);
    }

    /// <summary>
    /// The judgment line spans exactly the width the player can hit, so it is the
    /// right ruler for laying out eighty-eight keys.  Measured from its renderers
    /// rather than assumed, and reported once so a wrong answer is visible in the
    /// log instead of only on screen.
    /// </summary>
    private bool TryGetJudgmentSpan(out float centre, out float span)
    {
        centre = 0f;
        span = 0f;
        if (judgmentLine == null) return false;

        // 快取：GetComponentsInChildren 每次都配置一個新陣列，這裡每幀都會問，
        // 等於每幀餵一次垃圾回收。判定線換了才重抓。
        if (!ReferenceEquals(cachedJudgmentOwner, judgmentLine) || cachedJudgmentRenderers == null)
        {
            cachedJudgmentOwner = judgmentLine;
            cachedJudgmentRenderers = judgmentLine.GetComponentsInChildren<Renderer>();
        }
        var renderers = cachedJudgmentRenderers;
        if (renderers == null || renderers.Length == 0) return false;

        Bounds world = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

        Transform track = trackRenderer.transform;
        Vector3 low = track.InverseTransformPoint(new Vector3(world.min.x, world.center.y, world.center.z));
        Vector3 high = track.InverseTransformPoint(new Vector3(world.max.x, world.center.y, world.center.z));
        float a = low[acrossAxis];
        float b = high[acrossAxis];
        span = Mathf.Abs(b - a);
        centre = (a + b) * 0.5f;
        if (span < 0.001f) return false;

        if (!reportedJudgmentSpan)
        {
            reportedJudgmentSpan = true;
            Debug.Log($"[PitchEchoTrail] 判定線寬度 world {world.size.x:F1} → 軌道局部 {span:F2}" +
                      $"（板子 {localBounds.size[acrossAxis]:F2}），中心 local {centre:F2}。" +
                      $" 88 鍵改鋪在這段上。");
        }
        return true;
    }

    /// <summary>
    /// One echo per sub-note, offset along the track by how much later it was
    /// struck -- the same treatment a run of separate notes would get, so a trill
    /// reads as the alternation it actually is.
    /// </summary>
    private void DrainSubNotes(NoteData note, ref int drained)
    {
        for (int i = 0; i < note.subNotes.Count && echoes.Count < MaxEchoes; i++)
        {
            SubNoteData sub = note.subNotes[i];
            if (sub == null) continue;

            int key = SubKeyIndex(sub);
            if (key < 0) continue;

            float seconds = Mathf.Max(0, sub.end_timing_msec - sub.start_timing_msec) / 1000f;
            if (seconds <= tapThresholdSeconds) seconds = 0f;

            // 已經彈過的那些先飄了一段，距離就是那段時間乘上飄行速度。
            float elapsed = Mathf.Max(0, sub.start_timing_msec - note.startTime) / 1000f;

            drained++;
            echoes.Add(new Echo
            {
                key = key,
                length = Mathf.Max(minimumLength, seconds * riseSpeed),
                distance = elapsed * riseSpeed,
                age = 0f,
                rightHand = note.hand == 0,
                growing = false,
                source = note,
            });
        }
    }

    /// <summary>Sub-notes carry the 1..88 keyboard index; src_pitch is MIDI when present.</summary>
    private static int SubKeyIndex(SubNoteData sub)
    {
        int pitch = sub.src_pitch > 0 ? sub.src_pitch
            : (sub.scale_piano > 0 ? sub.scale_piano + LowestKey - 1 : 0);
        if (pitch <= 0) return -1;
        int key = pitch - LowestKey;
        return key >= 0 && key < KeyCount ? key : -1;
    }

    /// <summary>Legacy charts index the keyboard 1..88 instead of by MIDI number.</summary>
    private static int KeyIndex(NoteData note)
    {
        int pitch = note.pitch;
        if (pitch <= 0 && note.scale_piano > 0) pitch = note.scale_piano + LowestKey - 1;
        if (pitch <= 0) return -1;
        int key = pitch - LowestKey;
        return key >= 0 && key < KeyCount ? key : -1;
    }

    private void Advance(float deltaTime)
    {
        for (int i = echoes.Count - 1; i >= 0; i--)
        {
            Echo echo = echoes[i];
            if (echo.growing && echo.age > maxHoldSeconds)
            {
                // 放開事件沒來（離開遊玩、暫停、判定流程中斷）。自己封口，
                // 否則這條會無限延長並永遠留在畫面上。
                echo.growing = false;
                echoes[i] = echo;
            }

            if (echo.growing)
            {
                // Held: the near end stays on the judgment line while the ink
                // already laid down keeps travelling, so the ribbon lengthens at
                // exactly the speed everything else drifts.
                echo.length += riseSpeed * deltaTime;
                echo.age += deltaTime;
                echoes[i] = echo;
                continue;
            }
            echo.distance += riseSpeed * deltaTime;
            echo.age += deltaTime;
            if (echo.age >= lifetime || echo.distance > 1.2f) echoes.RemoveAt(i);
            else echoes[i] = echo;
        }
    }

    private void Rebuild()
    {
        if (graphic == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        if (echoes.Count == 0)
        {
            graphic.SetQuads(vertices, colours, triangles, 0);
            return;
        }

        EnsureBuffers(echoes.Count * Columns * Segments);

        // 88 鍵要鋪在**玩得到的寬度**上，不是整塊板子上。板子有 105 個世界單位寬，
        // 但畫面上的軌道窄得多 —— 照板子鋪的話大部分琴鍵會落在鏡頭外，正是先前
        // 兩次 log 的頂點都在 x=-26 卻什麼都看不到的原因。判定線的長度就是玩得到
        // 的寬度，拿它當基準。
        float acrossCentre = localBounds.center[acrossAxis];
        float keyboardSpan = localBounds.size[acrossAxis];
        if (TryGetJudgmentSpan(out float judgmentCentre, out float judgmentSpan))
        {
            acrossCentre = judgmentCentre;
            keyboardSpan = judgmentSpan;
        }
        keyboardSpan *= keyboardWidth;
        float keyWidth = keyboardSpan / KeyCount;
        float half = keyWidth * keyFill * 0.5f;
        float acrossStart = acrossCentre - keyboardSpan * 0.5f;

        // 起點是**判定線**，不是板子的邊緣。軌道是 1000 單位深的板子，但大理石只畫
        // 到判定線為止（shader 會裁），所以它的近端遠在畫面外 —— 殘響從那裡射出就
        // 等於射在相機後面。
        float slabNear = nearIsMax ? localBounds.max[alongAxis] : localBounds.min[alongAxis];
        float slabFar = nearIsMax ? localBounds.min[alongAxis] : localBounds.max[alongAxis];

        if (judgmentLine == null)
        {
            GameObject line = GameObject.Find("JudgmentLine");
            judgmentLine = line != null ? line.transform : null;
        }
        float nearValue = judgmentLine != null
            ? trackRenderer.transform.InverseTransformPoint(judgmentLine.position)[alongAxis]
            : slabNear;
        float farValue = Mathf.Lerp(nearValue, slabFar, visibleDepth);
        // 起點往前推一點，讓它看起來從判定線長出來而不是從大理石的最近緣。
        nearValue = Mathf.Lerp(nearValue, farValue, startOffset);
        float surface = localBounds.max[normalAxis];

        // 法線方向從軌道的 transform 取，抬升在世界座標裡加，才不會被 Y 縮放吃掉。
        Transform track = trackRenderer.transform;
        Vector3 normalUnit = Vector3.zero;
        normalUnit[normalAxis] = 1f;
        Vector3 lift = track.TransformDirection(normalUnit);
        lift = lift.sqrMagnitude > 1e-6f ? lift.normalized * surfaceLift : Vector3.up * surfaceLift;

        int quad = 0;

        // ── 每幀只問引擎一次 ───────────────────────────────────────────────
        //
        // 以前每個頂點都走 transform.TransformPoint → Camera.WorldToScreenPoint，
        // 兩百條殘響 × 18 格 × 4 角 ≈ 一萬四千個頂點，每一個都是好幾次原生呼叫；
        // 實測這一段每幀 4ms，是 144fps 預算的六成，密集段落判定再多一點就掉格。
        //
        // 軌道的局部→世界是仿射的，相機的世界→裁切空間是一個矩陣，兩個都是這一幀
        // 的常數。取出來之後投影就只是乘法，結果和原本的呼叫完全相同。
        Matrix4x4 localToWorld = track.localToWorldMatrix;
        Matrix4x4 viewProjection = cam.projectionMatrix * cam.worldToCameraMatrix;
        Rect pixelRect = cam.pixelRect;
        Vector3 acrossStep = localToWorld.GetColumn(acrossAxis);
        Vector3 alongStep = localToWorld.GetColumn(alongAxis);
        Vector3 surfacePoint = Vector3.zero;
        surfacePoint[normalAxis] = surface;
        Vector3 origin = localToWorld.MultiplyPoint3x4(surfacePoint) + lift;

        for (int col = 0; col <= Columns; col++)
        {
            float u = -1f + 2f * col / Columns;
            columnAcross[col] = u;
            columnWeight[col] = Mathf.Cos(u * Mathf.PI * 0.5f);
        }
        for (int seg = 0; seg <= Segments; seg++)
        {
            float tt = seg / (float)Segments;
            segmentT[seg] = tt;
            segmentAlpha[seg] = LengthProfile(tt);
        }

        int rowWidth = Columns + 1;
        for (int i = 0; i < echoes.Count; i++)
        {
            Echo echo = echoes[i];
            float centre = acrossStart + keyWidth * (echo.key + 0.5f);

            // Distance and length are fractions of the track, so a longer track
            // does not make the echoes crawl.
            float tail = Mathf.Lerp(nearValue, farValue, echo.distance);
            float head = Mathf.Lerp(nearValue, farValue, echo.distance + echo.length);

            float fadeIn = fadeInSeconds > 0.001f ? Mathf.Clamp01(echo.age / fadeInSeconds) : 1f;
            // A held note must not fade while it is still being played.
            float fadeOut = echo.growing ? 1f : 1f - Mathf.Clamp01(echo.age / lifetime);
            // Distance as well as age: something that has travelled most of the
            // way up should already be nearly gone, however recently it was hit.
            fadeOut *= 1f - Mathf.Clamp01(echo.distance / 0.9f);
            Color colour = echo.rightHand ? rightHandColour : leftHandColour;
            colour.a = opacity * fadeIn * fadeOut * fadeOut;

            // 一條殘響的格點全部投影好：寬度方向餘弦、長度方向平滑斜坡，讀起來是
            // 一道光而不是塑膠片。角落先在世界座標裡算好再過相機，傾斜和匯聚才是真的。
            for (int seg = 0; seg <= Segments; seg++)
            {
                float along = Mathf.Lerp(tail, head, segmentT[seg]);
                Vector3 rowBase = origin + alongStep * along;
                for (int col = 0; col <= Columns; col++)
                {
                    Vector3 world = rowBase + acrossStep * (centre + half * columnAcross[col]);
                    gridPoints[seg * rowWidth + col] = ProjectFast(viewProjection, pixelRect, world);
                }
            }

            for (int col = 0; col < Columns; col++)
            {
                float w0 = columnWeight[col];
                float w1 = columnWeight[col + 1];
                for (int seg = 0; seg < Segments; seg++)
                {
                    float a0 = segmentAlpha[seg];
                    float a1 = segmentAlpha[seg + 1];
                    int v = quad * 4;
                    vertices[v + 0] = gridPoints[seg * rowWidth + col];
                    vertices[v + 1] = gridPoints[seg * rowWidth + col + 1];
                    vertices[v + 2] = gridPoints[(seg + 1) * rowWidth + col + 1];
                    vertices[v + 3] = gridPoints[(seg + 1) * rowWidth + col];
                    colours[v + 0] = Tint(colour, w0 * a0);
                    colours[v + 1] = Tint(colour, w1 * a0);
                    colours[v + 2] = Tint(colour, w1 * a1);
                    colours[v + 3] = Tint(colour, w0 * a1);
                    int tri = quad * 6;
                    triangles[tri + 0] = v;
                    triangles[tri + 1] = v + 1;
                    triangles[tri + 2] = v + 2;
                    triangles[tri + 3] = v + 2;
                    triangles[tri + 4] = v + 3;
                    triangles[tri + 5] = v;
                    quad++;
                }
            }
        }

        graphic.SetQuads(vertices, colours, triangles, quad);

        if (!reportedFirstMesh)
        {
            reportedFirstMesh = true;

            // 投影之後 vertices 裡裝的是螢幕像素，不是世界座標 —— 標籤跟著改，
            // 不然下次讀 log 的人（包括我）會拿它去對世界空間的東西。
            Debug.Log($"[PitchEchoTrail] 第一次建出 {echoes.Count} 條殘響，" +
                             $"第一個頂點 螢幕 {vertices[0]}（螢幕 {Screen.width}x{Screen.height}）" +
                             $"，抬升 {lift.magnitude:F2}。" +
                             $" 判定線 {(judgmentLine != null ? judgmentLine.position.ToString() : "找不到")}" +
                             $"，起點 local {nearValue:F2} → 終點 local {farValue:F2}" +
                             $"（板子 {slabNear:F2}…{slabFar:F2}）。" +
                             $" 單條尺寸 {(vertices[1] - vertices[0]).magnitude:F0} × " +
                             $"{(vertices[3] - vertices[0]).magnitude:F0} 像素。");
        }
    }

    /// <summary>
    /// Allocates the vertex buffers once, at the most they can ever need.
    /// </summary>
    /// <remarks>
    /// This used to reallocate whenever the length was not *exactly* the size
    /// asked for, and the size asked for is the live echo count -- which changes
    /// on every judgment and again whenever one expires. Each strike therefore
    /// threw away and rebuilt about half a megabyte of arrays, which is what the
    /// stutter on hitting a note was. The consumer is given a count, so a buffer
    /// larger than the frame needs costs nothing to draw.
    /// </remarks>
    private void EnsureBuffers(int quads)
    {
        int needed = Mathf.Max(quads, MaxEchoes * Columns * Segments) * 4;
        if (vertices != null && vertices.Length >= needed) return;
        vertices = new Vector3[needed];
        colours = new Color[needed];
        triangles = new int[needed / 4 * 6];
    }

    /// <summary>How bright the echo is along its length: eased in at the strike,
    /// full through the body, gone by the tip.</summary>
    private static float LengthProfile(float t)
    {
        // 起漲拉長、峰值壓低、尾巴拖得更遠：一道透光的暈，而不是一根有頭有尾的棒子。
        const float rise = 0.28f;
        if (t < rise) return Mathf.SmoothStep(0f, 1f, t / rise) * 0.85f;
        float fall = 1f - (t - rise) / (1f - rise);
        return Mathf.SmoothStep(0f, 1f, fall) * fall * 0.85f;
    }

    private static Color Tint(Color colour, float weight)
    {
        colour.a *= Mathf.Clamp01(weight);
        return colour;
    }

    /// <summary>
    /// Screen pixels, with anything behind the camera pushed far off-frame so a
    /// quad that straddles the camera plane cannot fold back across the screen.
    /// </summary>
    /// <remarks>
    /// 和 <see cref="Camera.WorldToScreenPoint(Vector3)"/> 同一條式子：裁切空間 →
    /// 除以 w → 對應到相機的像素矩形。w 就是到相機的深度，≤ 0 代表在相機後面。
    /// </remarks>
    private static Vector3 ProjectFast(Matrix4x4 m, Rect pixelRect, Vector3 world)
    {
        float x = m.m00 * world.x + m.m01 * world.y + m.m02 * world.z + m.m03;
        float y = m.m10 * world.x + m.m11 * world.y + m.m12 * world.z + m.m13;
        float w = m.m30 * world.x + m.m31 * world.y + m.m32 * world.z + m.m33;
        if (w <= 0f) return new Vector3(-10000f, -10000f, w);
        float inv = 1f / w;
        return new Vector3(
            pixelRect.x + (x * inv * 0.5f + 0.5f) * pixelRect.width,
            pixelRect.y + (y * inv * 0.5f + 0.5f) * pixelRect.height,
            w);
    }
}

/// <summary>
/// Draws the echoes as plain screen-space quads.  Vertex colours only, so there
/// is no shader to resolve and nothing for the render pipeline to strip.
/// </summary>
internal sealed class EchoGraphic : MaskableGraphic
{
    private Vector3[] points;
    private Color[] tints;
    private int[] indices;
    private int quadCount;

    // 送進 CanvasRenderer 的那份。位置要扣掉畫布中心，所以不能直接用 points。
    private Vector3[] local;
    private Vector2[] uvs;
    private Mesh mesh;

    /// <summary>頂點數。0 代表還沒建過，或建了但什麼都沒有。</summary>
    public int CurrentVertexCount { get; private set; }

    public void SetQuads(Vector3[] screenPoints, Color[] colours, int[] triangles, int count)
    {
        points = screenPoints;
        tints = colours;
        indices = triangles;
        quadCount = count;
        // 當場建，不等 canvas 的重建佇列：這個效果每幀都在變，晚一幀就漏一幀。
        if (isActiveAndEnabled) UpdateGeometry();
    }

    /// <summary>
    /// 直接把陣列寫進 Mesh 交給 CanvasRenderer。
    /// </summary>
    /// <remarks>
    /// 預設的路徑是 OnPopulateMesh → VertexHelper：每個頂點建一個 UIVertex 再
    /// AddVert，一萬多個頂點每幀這樣做一遍，本身就要一兩毫秒。這裡的頂點已經在
    /// 陣列裡了，整段交出去就好。
    /// </remarks>
    protected override void UpdateGeometry()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "PitchEchoTrail", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
        }
        mesh.Clear();

        int vertexCount = points != null && tints != null && indices != null && quadCount > 0
            ? Mathf.Min(quadCount * 4, Mathf.Min(points.Length, tints.Length))
            : 0;
        int indexCount = vertexCount / 4 * 6;
        if (vertexCount > 0)
        {
            if (local == null || local.Length < points.Length)
            {
                local = new Vector3[points.Length];
                uvs = new Vector2[points.Length];
            }
            Rect r = rectTransform.rect;
            // The canvas is a full-screen overlay with no scaler, so its rect is the
            // screen and the only conversion needed is the centred origin.
            float cx = r.width * 0.5f;
            float cy = r.height * 0.5f;
            for (int i = 0; i < vertexCount; i++)
                local[i] = new Vector3(points[i].x - cx, points[i].y - cy, 0f);

            mesh.SetVertices(local, 0, vertexCount);
            mesh.SetColors(tints, 0, vertexCount);
            mesh.SetUVs(0, uvs, 0, vertexCount);
            mesh.SetTriangles(indices, 0, indexCount, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(r.width * 4f, r.height * 4f, 1f));
        }
        CurrentVertexCount = vertexCount;
        canvasRenderer.SetMesh(mesh);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        // 幾何由 UpdateGeometry 直接寫，這裡不產生任何東西。
        vh.Clear();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (mesh != null) Destroy(mesh);
    }
}
