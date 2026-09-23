using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws the sustain pedal as notes falling behind the playfield: the head is pressed
/// as it crosses the judgment line, and the terminator at the far end is released as
/// it crosses.
/// </summary>
/// <remarks>
/// A pedal note is background rather than a lane, so it runs the full width of the
/// track and sits under the guide lines. It is dark at both ends and nearly invisible
/// through the middle, because the pedal is held for most of a piece and the middle
/// carries no information — the two ends are the only moments the player has to act
/// on. Darkening on the way into the terminator doubles as the answer to "when may I
/// let go", so the release tolerance is something the player looks at rather than
/// something that has to be explained.
///
/// Nothing here reads player input yet. The notes follow the chart's own pedal so the
/// shape can be judged before there is a pedal to play it with. Auto-pedal ("Pedal If
/// Missing", per beat or per bar) is the easiest way to see it: it puts a change on
/// every beat or bar, which makes the shape obvious.
///
/// The scroll maths are the same line NoteController uses, so pedal notes and real
/// notes cannot drift apart:
///     z = judgmentZ + ((timeMs - songPos) / 1000) * speed
/// </remarks>
[DisallowMultipleComponent]
public sealed class PedalNoteRenderer : MonoBehaviour
{
    private const string RootName = "Pedal Notes";

    /// <summary>Long pedalling makes these few; the cap only exists so a pathological
    /// chart cannot allocate without bound.</summary>
    private const int MaxVisible = 24;

    public static PedalNoteRenderer Instance { get; private set; }

    [SerializeField, Tooltip("Colour of the pedal note before it is pressed.")]
    private Color noteColor = new Color(0.72f, 0.26f, 0.05f, 0.82f);

    [SerializeField, Tooltip("Colour the body turns once the pedal is down, and keeps for the rest of the note.")]
    private Color heldColor = new Color(0.84f, 0.33f, 0.06f, 0.86f);

    [SerializeField, Range(0f, 0.35f), Tooltip("Maximum opacity of an approaching pedal cue. The cue must not wash out the black-marble TRACK." )]
    private float approachMeshAlpha = 0.62f;

    [SerializeField, Range(0f, 0.25f), Tooltip("Final opacity of the scrolling TRACK cue while the pedal is held. Kept near zero so pressing the pedal clears the playfield." )]
    private float heldMeshAlpha = 0.22f;

    [SerializeField, Min(0f), Tooltip("How long the body takes to turn, in ms. Short, but not a snap.")]
    private float colorShiftMs = 120f;

    [SerializeField, Tooltip("Colour of the terminator line at the far end.")]
    private Color terminatorColor = new Color(1.30f, 1.24f, 1.12f, 0.96f);

    [SerializeField, ColorUsage(true, true), Tooltip("Full-track light bar marking the exact pedal-down moment.")]
    /// <summary>
    /// The bar's face: white, with the warmth left to its edge.
    /// </summary>
    /// <remarks>
    /// A white core inside a coloured rim is how a lit filament reads -- the
    /// middle is past the point where a colour can be brighter and only the
    /// falling edge still carries hue. Colouring the face instead gives a flat
    /// amber strip, which is the difference between "a light" and "a painted
    /// line".
    /// </remarks>
    // 沒有光暈撐場，這條線就得自己夠亮。
    //
    // 1.75 是「會發亮」，2.7 剛過泛光門檻，3.8 是**這條線自己就是一個光源**。
    // 它只有幾個像素厚，而薄的東西在泛光裡會被稀釋得特別厲害 —— 同樣的數值放在
    // 一塊有面積的東西上會刺眼，放在一條線上只是剛好看得清楚。
    private Color pressBarColor = new Color(3.80f, 3.62f, 3.20f, 1f);

    /// <summary>The rim round both bars: orange-gold, and narrow.</summary>
    [SerializeField, ColorUsage(true, true)]
    private Color pedalBarRimColor = new Color(1.35f, 0.58f, 0.14f, 1f);

    [SerializeField, Min(1f), Tooltip("How long the pedal-down light bar remains at the judgment line before fading.")]
    private float pressBarFadeMs = 160f;

    [Header("Track Cue")]
    [SerializeField, Range(0.25f, 1f), Tooltip("Width of the scrolling pedal effect relative to TRACK. Pedal input is global, so the effect covers the complete track width.")]
    private float cueWidthFraction = 1f;

    /// <summary>
    /// World units trimmed from each side of every pedal cue.
    /// </summary>
    /// <remarks>
    /// The cues sit at y = 0.01, under a track surface at y = 2, so the whole bar
    /// should be hidden by the stone. But it is built at exactly the track's own
    /// width, and two surfaces that share an edge do not resolve cleanly: a
    /// sliver of the cue shows past the track at each end. The bar's colour is
    /// HDR, so those two slivers bloom into a pair of soft balls sitting at the
    /// far left and right — with no object at that position bright enough for a
    /// brightness sweep to blame, because the object producing them is the same
    /// full-width bar whose middle is correctly hidden.
    ///
    /// Tucking the cue under the stone removes the seam rather than dimming it.
    /// </remarks>
    /// <summary>
    /// How far the cue reaches **past** each edge of the track.
    /// </summary>
    /// <remarks>
    /// This used to be an inset of 0.9, holding the cue inside the track. The
    /// cue is read by the two glowing edges its shader draws just inside its own
    /// rim, so sitting inside the track put those edges in the middle of
    /// everything else drawn on the floor -- and once the dynamics wash covered
    /// the same span, they were being rubbed out entirely.
    ///
    /// Letting them overhang instead puts the one part of the pedal a player
    /// actually looks at outside the crowd, against the dark beyond the track,
    /// where nothing competes with it. Narrowing the wash was tried first and was
    /// the wrong way round: it cost the wash real width to protect a line that
    /// was in the wrong place to begin with.
    /// </remarks>
    private const float CueEdgeOutsetWorld = 0.55f;

    [Header("Pedal bracket")]
    [Tooltip("Gap between the track's edge and the bracket's rail, in world units.")]
    [SerializeField, Range(0.1f, 8f)] private float bracketGapWorld = 2.2f;
    [Tooltip("Length of the ticks turned in towards the track, as a share of the track's width.")]
    [SerializeField, Range(0.01f, 0.25f)] private float bracketTickShare = 0.055f;
    [Tooltip("Stroke weight of the bracket, as a share of the track's width.")]
    [SerializeField, Range(0.002f, 0.05f)] private float bracketStrokeShare = 0.018f;
    [Tooltip("How far from the judgment line the press and release flare, as a share of the track's width.")]
    [SerializeField, Range(0.05f, 1.5f)] private float bracketFlareShare = 0.30f;
    [Tooltip("Nudge the parked pedal bar along the track, in world units. Negative pulls it toward the player.")]
    [SerializeField, Range(-3f, 3f)] private float pedalBarZOffset = -0.35f;
    [Tooltip("How far the additive glow around each bar reaches, as a share of the track's width.")]
    [SerializeField, Range(0.002f, 0.3f)] private float pedalBarGlowShare = 0.022f;
    [Tooltip("Where the side speed lines sit and how wide their band is, as shares of the track's width.")]
    [SerializeField, Range(0.005f, 0.3f)] private float sideLineGapShare = 0.02f;
    [SerializeField, Range(0.01f, 0.4f)] private float sideLineBandShare = 0.085f;
    [Tooltip("Temporary: log what the pedal's beam mesh actually built, once every 30 frames.")]
    [SerializeField] private bool logPedalBeam = true;
    [Tooltip("How tall the release beam stands off the track, as a share of the track's width.")]
    [SerializeField, Range(0.005f, 1.5f)] private float pedalBeamShare = 0.042f;

    /// <summary>Cue width with both edges tucked safely under the track.</summary>
    private float CueWidth(float trackWidth)
    {
        return Mathf.Max(0.1f, trackWidth * cueWidthFraction + CueEdgeOutsetWorld * 2f);
    }

    [SerializeField, ColorUsage(true, true), Tooltip("Warm brass light on the two outside edges of the cue.")]
    private Color cueEdgeColor = new Color(1.02f, 0.50f, 0.12f, 1f);

    [SerializeField, Tooltip("Colour at the pedal-up boundary. The held orange darkens into this before the empty release gap, then returns from it after the next press.")]
    private Color cueReleaseColor = new Color(0.008f, 0.005f, 0.003f, 1f);

    [SerializeField, Range(0f, 4f)] private float cueEdgeGlow = 1.15f;

    [SerializeField, Range(0.2f, 3f)]
    [Tooltip("Extra edge glow on the pedal-down bar, over cueEdgeGlow. Its colour is already HDR, so this multiplies an above-1 value: the two together decide whether the bar reads as a line or as a band of light.")]
    // Was a hard-coded 2.25, which put the bar at 1.397 x 1.15 x 2.25 = 3.62 —
    // nearly three times the bloom threshold, and by a wide margin the brightest
    // thing in the scene. A full-width line that far over blooms into a thick
    // glowing band lying parallel to the judgment line, which is what it was
    // mistaken for. 1.15 keeps it the brightest pedal cue and still glowing
    // (about 1.8, comfortably past the threshold) without flooding the screen.
    private float pressBarGlowMultiplier = 1.15f;
    [SerializeField, Range(0.005f, 0.2f)] private float cueEdgeWidth = 0.045f;

    [SerializeField, Range(0.1f, 1.5f), Tooltip("Fixed world-space thickness of the pedal-down and pedal-up marker lines. It must not grow with note speed.")]
    private float markerLineDepthWorld = 0.45f;

    // 踩下、放開兩個標記都是空心的細框，不是實心的亮條：實心的亮條橫過整條軌道，
    // 剛好壓在判定線上，每一顆經過的音符都得從它底下穿過去。框只有四條細邊，中
    // 間是空的。
    [Header("Marker frames")]
    [SerializeField, Range(0.3f, 4f), Tooltip("踩下／放開細框沿軌道方向的深度（世界單位）。")]
    private float markerFrameDepthWorld = 1.4f;
    [SerializeField, Range(0.03f, 0.5f), Tooltip("細框前後兩條橫邊的粗細（世界單位）。")]
    private float markerFrameLineWorld = 0.12f;
    [SerializeField, Range(0.05f, 1.5f), Tooltip("細框左右兩端短邊的寬度（世界單位）。")]
    private float markerFrameCapWorld = 0.35f;
    [SerializeField, ColorUsage(true, true), Tooltip("放開那個細框的顏色。踩下的沿用 pressBarColor。")]
    private Color releaseFrameColor = new Color(2.6f, 1.75f, 0.70f, 0.95f);

    [Header("Note avoidance")]
    [SerializeField, Range(0f, 1f), Tooltip("音符所在位置的踏板特效剩多少亮度。0 = 完全讓開。")]
    private float noteGapFloor = 0f;

    /// <summary>踩著時站在判定線上那道光的高度，相對於原本。</summary>
    private const float PressWallHeightShare = 1f / 3f;

    [SerializeField, Min(0.1f), Tooltip("How far ahead pedal notes are drawn, in seconds of chart time.")]
    private float leadSeconds = 4f;

    [SerializeField, Tooltip("Depth profile and how wide a pedal change may be played.")]
    private PedalNoteTuning tuning = PedalNoteTuning.Standard;

    [SerializeField, Range(0f, 1f), Tooltip("How much the held body brightens when the pedal goes down. This is the part no note has: the press lights the whole sustained object, not just the point it was struck at.")]
    private float bodyFlashAlpha = 0.38f;

    [SerializeField, Min(0f), Tooltip("How long the body's brightening takes to settle, in ms.")]
    private float bodyFlashMs = 260f;

    [Header("Mechanical Pedal")]
    [SerializeField, Range(0.04f, 0.14f), Tooltip("Width of the rounded sustain-pedal toe relative to TRACK.")]
    private float pedalWidthFraction = 0.075f;
    [SerializeField, Range(0.8f, 2.8f), Tooltip("A real pedal is a long lever: narrow at the hinge and broader at the player's toe.")]
    private float pedalLengthToWidth = 2.2f;
    [SerializeField, Range(0.008f, 0.08f), Tooltip("Places the treadle behind the keyboard on the player side, in viewport height.")]
    private float pedalViewportOffset = 0.032f;
    [SerializeField, Range(0.04f, 0.30f), Tooltip("Keeps the treadle below the ivory key tops so their geometry remains foreground.")]
    private float pedalSurfaceLift = 0.11f;
    [SerializeField, Range(-10f, 0f), Tooltip("Released angle of the treadle. It stays below the key fascia instead of swinging upward through the keys.")]
    private float pedalRestAngle = -4f;
    [SerializeField, Range(-16f, 0f), Tooltip("Pressed lever angle. Kept shallow enough that the toe remains visible below the keyboard.")]
    private float pedalPressedAngle = -9f;
    [SerializeField, Min(0.02f)] private float pedalPressSeconds = 0.075f;
    [SerializeField, Min(0.02f)] private float pedalReleaseSeconds = 0.14f;
    [SerializeField, Min(0f), Tooltip("How early the brass edge warns that the pedal will be released.")]
    private float releaseWarningMs = 190f;
    [SerializeField, ColorUsage(true, true)] private Color pedalHeldGlow = new Color(0.90f, 0.44f, 0.10f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color pedalPressGlow = new Color(1.75f, 1.30f, 0.62f, 1f);

    private readonly List<Visual> pool = new List<Visual>(8);
    private Material sharedMaterial;
    private Mesh bodyMesh;
    private Mesh lineMesh;

    private GameObject pedalAssembly;
    private Transform pedalPivot;
    private float pedalPivotRestY;
    private float pedalMechanicalDrop;
    private Renderer pedalGlassRenderer;
    private readonly List<Renderer> pedalEdgeRenderers = new List<Renderer>(4);
    private Material pedalBaseMaterial;
    private Material pedalBrassMaterial;
    private Material pedalGlassMaterial;
    private Material pedalEdgeMaterial;
    private Mesh pedalTreadleMesh;
    private MaterialPropertyBlock pedalGlassBlock;
    private MaterialPropertyBlock pedalEdgeBlock;
    private float pedalPressAmount;
    private float pedalPressPulse;
    private bool pedalWasDown;
    private bool pedalStateInitialized;
    private float physicalPedalWidth;
    private float physicalPedalLength;
    private Transform cachedJudgmentLine;
    private Camera cachedMainCamera;

    private PedalTimeline lastTimeline;
    private List<PedalSpan> playable = new List<PedalSpan>();
    private float trackCenterX;
    private float trackWidth;
    private float surfaceY;
    private bool haveGeometry;
    private bool warnedOverflow;
    private string lastReport;

    private sealed class Visual
    {
        public GameObject Object;
        public Transform Body;
        public Transform Head;
        public Transform Line;
        public MeshRenderer BodyRenderer;
        public MeshRenderer HeadRenderer;
        public MeshRenderer LineRenderer;
        public MaterialPropertyBlock BodyBlock;
        public MaterialPropertyBlock HeadBlock;
        public MaterialPropertyBlock LineBlock;
        public MeshRenderer Bracket;
        public Mesh BracketMesh;
        public MeshRenderer Embers;
        public Mesh EmberMesh;
        public MeshRenderer Beam;
        public Mesh BeamMesh;
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int HeadRampId = Shader.PropertyToID("_HeadRamp");
    private static readonly int TailRampId = Shader.PropertyToID("_TailRamp");
    private static readonly int HeadAlphaId = Shader.PropertyToID("_HeadAlpha");
    private static readonly int IdleAlphaId = Shader.PropertyToID("_IdleAlpha");
    private static readonly int TailAlphaId = Shader.PropertyToID("_TailAlpha");
    // 拋物線：由這裡直接寫進每個踏板音符自己的 property block。
    // 曾經改用全域值（Shader.SetGlobalFloat），但那要另一個元件去場景裡找判定線
    // 與 spawner，找不到或找到舊的就會整根用同一個高度——看起來就是「一下卡在
    // 上面、一下卡在下面，而且還是平的」。這裡本來就知道 judgmentZ 和速度。
    private static readonly int TMinId = Shader.PropertyToID("_TMin");
    private static readonly int TMaxId = Shader.PropertyToID("_TMax");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int DarkColorId = Shader.PropertyToID("_DarkColor");
    private static readonly int EdgeGlowId = Shader.PropertyToID("_EdgeGlow");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>
    /// Brings the renderer up with the game, before any chart is chosen.
    /// </summary>
    /// <remarks>
    /// Creation used to hang off one line at the tail of GameManager.LoadChartData,
    /// inside a try/catch that swallows everything. Anything throwing earlier in that
    /// block — or the method returning before reaching it — meant the renderer was
    /// never built and said nothing about it, which is indistinguishable from the
    /// renderer being built and drawing wrongly. Standing itself up removes the
    /// dependency: it polls for the track and the pedal anyway, and it is now always
    /// in the hierarchy, so "is it alive" stops being a question.
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GetOrCreate();
    }

    /// <summary>
    /// Hides every pedal cue. A switch rather than a guess: the cues are the only
    /// full-width bars lying parallel to the judgment line, so turning them off
    /// settles in one run whether a glowing line across the track is one of them.
    /// </summary>
    public static bool CuesHidden
    {
        get => cuesHidden;
        set
        {
            if (cuesHidden == value) return;
            cuesHidden = value;
            if (Instance != null) Instance.ApplyCueVisibility();
        }
    }

    private static bool cuesHidden;

    private void ApplyCueVisibility()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            var visual = pool[i];
            if (visual != null && visual.Object != null && cuesHidden)
                visual.Object.SetActive(false);
        }
    }

    /// <summary>Creates the renderer, which then waits for a playfield to appear.</summary>
    /// <remarks>
    /// This deliberately does not check for the track first. It is called once, from
    /// chart load, and requiring the track to already exist at that moment meant that
    /// if it did not — a scene still coming up, an inactive object, GameObject.Find
    /// skipping it — the renderer was never created and never tried again. Failing
    /// that way is completely silent. Finding the track is retried per frame in
    /// EnsureGeometry instead, where not finding it says so.
    /// </remarks>
    public static PedalNoteRenderer GetOrCreate()
    {
        if (Instance != null) return Instance;
        return new GameObject(RootName).AddComponent<PedalNoteRenderer>();
    }

    /// <summary>
    /// Whether a pedal note is crossing the judgment line right now.
    /// </summary>
    /// <remarks>
    /// Asked of the renderer rather than of the chart, so that whatever reacts to the
    /// pedal reacts to the pedal the player can actually see. The drawn notes have
    /// already been merged and had their presses pushed around to be playable, and a
    /// second reader going back to the raw spans would disagree with the screen at
    /// exactly the moments that matter.
    /// </remarks>
    /// <summary>
    /// What the chart wanted from the pedal at this moment, and what the player
    /// was actually doing.
    /// </summary>
    /// <remarks>
    /// Returns false when there is nothing to mark: no pedal in the chart, no
    /// MIDI pedal attached, or the pedal is being played by the chart rather
    /// than by the player. The caller treats that as "not asked for" rather than
    /// as a failure -- marking somebody down for a pedal they were never given
    /// is the worst kind of wrong.
    ///
    /// The reference is <see cref="playable"/>, the spans that were actually
    /// drawn, for the same reason <see cref="IsPedalDownNow"/> uses them: the
    /// raw chart spans have been merged and nudged to be playable, and grading
    /// against something other than what the player saw is grading a ghost.
    /// </remarks>
    public static bool TrySamplePedal(float songMs, out bool wanted, out bool actual)
    {
        wanted = false;
        actual = false;

        PedalNoteRenderer renderer = Instance;
        if (renderer == null || renderer.playable.Count == 0) return false;

        SettingsManager settings = SettingsManager.Instance;
        if (settings == null || settings.CurrentPianoPedalSource != PianoPedalSource.Player)
            return false;

        MIDIInputManager midi = MIDIInputManager.Instance;
        if (midi == null) return false;

        wanted = renderer.IsDownAt(songMs);
        actual = midi.SustainPedalDown;
        return true;
    }

    public static bool IsPedalDownNow()
    {
        PedalNoteRenderer renderer = Instance;
        if (renderer == null || renderer.playable.Count == 0) return false;

        Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
        if (conductor == null) return false;

        // The render clock, because this answers "is the head past the line" and the
        // head is drawn on that clock.
        return renderer.IsDownAt(conductor.renderSongPosition);
    }

    /// <summary>
    /// The play surface and the judgment line, once something has measured them.
    /// </summary>
    /// <remarks>
    /// Shared so the playfield is measured in one place. Measuring it twice is how the
    /// notes ended up two units in the air: the judgment line looks like the obvious
    /// anchor and is not on the surface at all — its height is a player setting and it
    /// hovers above the track.
    /// </remarks>
    public static bool TryGetSurface(out float surfaceY, out float judgmentZ)
    {
        surfaceY = 0f;
        judgmentZ = 0f;
        PedalNoteRenderer renderer = Instance;
        if (renderer == null || !renderer.haveGeometry) return false;
        surfaceY = renderer.surfaceY;
        judgmentZ = renderer.ResolveJudgmentZ();
        return true;
    }

    private bool IsDownAt(float songMs)
    {
        int index = FirstUnconsumed(songMs);
        return index < playable.Count && playable[index].start_ms <= songMs;
    }

    /// <summary>
    /// Says what the renderer is doing, once per change of state.
    /// </summary>
    /// <remarks>
    /// Not BuildLogger.Log: that is [Conditional("NOSTALGIA_VERBOSE_LOGGING")] and the
    /// project defines no symbols, so every one of those calls is removed at compile
    /// time and an empty console proves nothing at all. Reporting only on change means
    /// this can sit on the per-frame path without becoming noise.
    /// </remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Report(string state)
    {
        if (state == lastReport) return;
        lastReport = state;
        Debug.Log("[PedalNoteRenderer] " + state);
    }

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildAssets();
        Report("created, waiting for a chart");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DisposeRuntimeObject(pedalAssembly);
        DisposeRuntimeObject(bodyMesh);
        DisposeRuntimeObject(lineMesh);
        DisposeRuntimeObject(sharedMaterial);
        DisposeRuntimeObject(pedalBaseMaterial);
        DisposeRuntimeObject(pedalBrassMaterial);
        DisposeRuntimeObject(pedalGlassMaterial);
        DisposeRuntimeObject(pedalEdgeMaterial);
        DisposeRuntimeObject(pedalTreadleMesh);
    }

    /// <summary>
    /// Forgets the current pedalling and re-reads the playfield, so the next frame
    /// picks up whichever pedal the new chart resolved to. Safe to call at any time.
    /// </summary>
    public void Rebuild()
    {
        lastTimeline = null;
        playable.Clear();
        haveGeometry = false;
        warnedOverflow = false;
        lastReport = null; // so the new chart reports its state even if it matches the old one
        pedalStateInitialized = false;
        pedalPressAmount = 0f;
        pedalPressPulse = 0f;
        cachedJudgmentLine = null;
        cachedMainCamera = null;
        if (pedalAssembly != null) pedalAssembly.SetActive(false);
        HideFrom(0);
    }

    private void LateUpdate()
    {
        using (HitchProbe.Measure("pedalFx")) LateUpdateCore();
    }

    private void LateUpdateCore()
    {
        // After the notes have moved, so a pedal note and the notes above it are
        // placed from the same frame's clock rather than one frame apart.
        if (!EnsureGeometry())
        {
            HideFrom(0);
            SetPhysicalPedalVisible(false);
            return;
        }

        GameManager game = GameManager.Instance;
        Conductor conductor = game != null ? game.Conductor : null;
        NoteSpawner spawner = game != null ? game.NoteSpawner : null;
        if (conductor == null || spawner == null)
        {
            Report(conductor == null ? "waiting: no Conductor" : "waiting: no NoteSpawner");
            HideFrom(0);
            SetPhysicalPedalVisible(false);
            return;
        }

        EnsurePhysicalPedal();
        SetPhysicalPedalVisible(true);

        bool hasChartPedal = RefreshPedal();

        // The render clock, not the judgment clock: this is something the eye reads,
        // and it has to agree with where the notes are drawn.
        float songMs = conductor.renderSongPosition;
        float speed = spawner.speed;
        float judgmentZ = ResolveJudgmentZ();
        UpdatePhysicalPedal(songMs, judgmentZ);

        if (!hasChartPedal)
        {
            HideFrom(0);
            return;
        }

        // 拋物線模式下，踏板和音符用同一個生成距離——不然音符還沒出現，
        // 踏板已經在跑道深處了。
        SettingsManager arcSettings = SettingsManager.Instance;
        float farZ = (arcSettings != null && arcSettings.EffectiveNoteArcHeight > 0f)
            ? judgmentZ + arcSettings.ArcSpawnWorldUnits()
            : judgmentZ + leadSeconds * speed;
        currentSpeed = speed;

        int used = 0;

        for (int i = FirstUnconsumed(songMs); i < playable.Count && used < MaxVisible; i++)
        {
            PedalSpan span = playable[i];
            // 畫的起點可能被往後挪過（見 BuildDrawnStarts）。span 本身不動：它要
            // 繼續代表真正的踏板時間，判斷「這一段過去了沒」靠的是它。
            int drawnStart = i < playableDrawnStart.Count ? playableDrawnStart[i] : span.start_ms;
            float headZ = judgmentZ + ((drawnStart - songMs) / 1000f) * speed;
            if (headZ > farZ) break; // sorted, so everything after this is further away

            float tailZ = judgmentZ + ((span.end_ms - songMs) / 1000f) * speed;
            if (tailZ - Mathf.Max(headZ, judgmentZ) <= 0f) continue;

            bool snappedPress = i < playableSnapped.Count && playableSnapped[i];
            Place(Rent(used), span, headZ, tailZ, judgmentZ, songMs, speed, snappedPress,
                  spawner.travelTimeSeconds);
            used++;
        }

        if (used >= MaxVisible && !warnedOverflow)
        {
            warnedOverflow = true;
            BuildLogger.LogWarning($"[PedalNoteRenderer] More than {MaxVisible} pedal notes on screen; the rest are not drawn.");
        }

        // Separates "the emitter never fired" from "it fired and you cannot see it",
        // which look identical from outside and need completely different fixes.
        // Deliberately without the counts: those change every frame and would turn a
        // state report into a flood. Everything here is stable, so it prints once and
        // says where the notes were put.
        Report(used == 0
            ? $"drawing nothing: {playable.Count} notes, none within {leadSeconds:F1}s of the line"
            : $"drawing at judgmentZ={judgmentZ:F2} speed={speed:F1} surfaceY={surfaceY:F2} " +
              $"trackX={trackCenterX:F2} width={trackWidth:F2}");

        HideFrom(used);
    }

    /// <summary>
    /// Index of the first note not yet fully past the judgment line. Binary search
    /// rather than a running cursor, because seeking and restarting move the clock
    /// backwards and a cursor would have to be invalidated on every one.
    /// </summary>
    private int FirstUnconsumed(float songMs)
    {
        int low = 0;
        int high = playable.Count - 1;
        int found = playable.Count;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (playable[mid].end_ms > songMs)
            {
                found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return found;
    }



    private void Place(Visual visual, PedalSpan span, float headZ, float tailZ, float judgmentZ,
        float songMs, float speed, bool snappedPress, float travelSeconds)
    {
        float lengthZ = tailZ - headZ;
        float noteLengthMs = span.end_ms - span.start_ms;

        // Stop the body at the judgment line so a note is consumed as it is played,
        // the way a hold tail is, instead of sweeping on over the keyboard.
        float drawnHeadZ = Mathf.Max(headZ, judgmentZ);
        float tMin = lengthZ > 0.0001f ? Mathf.Clamp01((drawnHeadZ - headZ) / lengthZ) : 0f;

        float headRamp = PedalNoteShape.RampFraction(tuning.headRampMs, noteLengthMs, tuning.rampSpanFraction);
        float tailRamp = PedalNoteShape.RampFraction(tuning.tailRampMs, noteLengthMs, tuning.rampSpanFraction);

        if (cuesHidden)
        {
            visual.Object.SetActive(false);
            return;
        }
        visual.Object.SetActive(true);
        visual.Body.position = new Vector3(trackCenterX, surfaceY, drawnHeadZ);
        visual.Body.localScale = new Vector3(CueWidth(trackWidth), 1f, tailZ - drawnHeadZ);

        visual.BodyRenderer.GetPropertyBlock(visual.BodyBlock);
        visual.BodyBlock.SetColor(ColorId, BodyColor(songMs - span.start_ms));
        visual.BodyBlock.SetFloat(HeadRampId, headRamp);
        visual.BodyBlock.SetFloat(TailRampId, tailRamp);
        visual.BodyBlock.SetFloat(HeadAlphaId, tuning.headAlpha);
        // Head goes black -> orange, the held middle stays orange, and the tail
        // returns orange -> black before the real release gap.
        visual.BodyBlock.SetFloat(IdleAlphaId, tuning.idleAlpha);
        visual.BodyBlock.SetFloat(TailAlphaId, tuning.tailAlpha);
        visual.BodyBlock.SetFloat(TMinId, tMin);
        visual.BodyBlock.SetFloat(TMaxId, 1f);
        ApplyArcBlock(visual.BodyBlock, judgmentZ, speed, travelSeconds);
        visual.BodyBlock.SetColor(EdgeColorId, cueEdgeColor);
        visual.BodyBlock.SetColor(DarkColorId, cueReleaseColor);
        visual.BodyBlock.SetFloat(EdgeGlowId, cueEdgeGlow);
        visual.BodyBlock.SetFloat(EdgeWidthId, cueEdgeWidth);
        visual.BodyRenderer.SetPropertyBlock(visual.BodyBlock);

        // Unlike the body gradient, this is a precise, full-width pedal-down cue.
        // It travels with the head, stops at the judgment line, then rapidly fades.
        // Clock-derived fading also survives seeking and preview chart reloads.
        // Restore the exact pedal-down bar. Before the press it travels with the
        // chart; after crossing the line it stays there only long enough to read as
        // an impact, then fades rather than covering the held TRACK.
        // 踩著的時候就停在判定線上，不淡出。原本是一按下去就開始消失，理由是
        // 「不要蓋住按著的軌道」—— 但現在中間那條長帶已經沒有了，這一條橫桿是
        // 唯一在說「現在踩著」的東西，它得留著。放開之後才淡，而它淡掉的同時
        // 放開的那一條正好抵達，剛好接上。
        float sincePressMs = songMs - span.start_ms;
        float sinceReleaseMs = songMs - span.end_ms;
        float pressBarAlpha = sincePressMs < 0f || sinceReleaseMs < 0f
            ? 1f
            : (pressBarFadeMs <= 0f ? 0f : 1f - Mathf.Clamp01(sinceReleaseMs / pressBarFadeMs));

        // 踩下、放開兩條原本是 shader 畫的實心亮條。現在兩者都改成空心細框，畫在
        // PlaceBracket 的網格裡 —— 那裡才能一段一段讓開正好經過的音符。
        visual.HeadRenderer.enabled = false;
        visual.LineRenderer.enabled = false;

        PlaceBracket(visual, headZ, tailZ, judgmentZ, songMs, pressBarAlpha, snappedPress,
                     speed, travelSeconds);
    }

    // ── 讓開音符 ──────────────────────────────────────────────────────────────
    //
    // 踏板的框和音符畫在同一層（音符的高度），看到的疊不疊就是世界座標疊不疊。
    //
    // 照真實鋼琴的踩法分兩種處理：
    //
    // - **只讓開長條走廊。** 長押、滑奏、顫音的身體是一條長長的走廊，框從上面
    //   橫過去會把走廊切斷，所以框在走廊裡斷開。一般音符不讓：音符畫在框上面，框
    //   從它底下穿過去就好。
    // - **疊到音符頭就讓位、改用包圈。** 框在音符的位置斷開，改由一圈和框同色的
    //   光包住整個和弦（在力度光暈外面），線看起來沒有斷，只是被和弦佔了位置、繞
    //   過去。
    //
    //   **踩下和放開兩條都這樣做。** 以前只有踩下有，而且只在它被吸到和弦上的時候
    //   （PlayablePedal.SnapRhythmicPresses）。放開那條照樣會從音符頭上壓過去，而
    //   它壓到的往往正是強調音符 —— 那顆的力度光暈最亮、最需要被看清楚，卻被一條
    //   橫線切成兩半。放開不吸附，所以它讓位的程度改看**實際疊到多少**：剛碰到就
    //   淡淡地讓，正中就完全讓開，離開就收回來。
    //
    // 停在判定線上的時候不包：和弦一到線就被打掉，接下來是打擊特效的事。

    private struct NoteGap
    {
        public float Lo;
        public float Hi;
        /// <summary>缺口邊緣往內化開多寬；窄的缺口會縮小，免得兩側的化開在中間碰頭。</summary>
        public float Feather;
        /// <summary>0 = 不讓，1 = 完全讓開。</summary>
        public float Strength;
    }

    /// <summary>一個被包住的和弦：相鄰的音符合成一圈，不是一顆一圈。</summary>
    private struct WrapGroup
    {
        public float Lo;
        public float Hi;
        public float ZNear;
        public float ZFar;
        /// <summary>
        /// 這一組音符**查弧線用的**世界 Z：音符頭的前緣。
        /// </summary>
        /// <remarks>
        /// 音符是一片平躺的貼圖，整片用「前緣」那一點的位移剛性地抬起來
        /// （NoteController.LeadingEdgeZ）。包圈若照自己每個頂點的 z 去彎，
        /// 中心就落在 z 中點的高度、遠邊還會再翹起來 —— 半個音符高在弧線陡的
        /// 地方就是看得見的落差，看起來就是圈浮在音符上面。所以包圈也要剛性地
        /// 用同一個參考點。
        /// </remarks>
        public float RefZ;
        /// <summary>這一組和那條線實際疊到多少：0 剛好擦邊，1 完全疊住。</summary>
        public float Strength;
        /// <summary>包圈要在範圍外再讓多少。範圍已經含力度光暈時是 0。</summary>
        public float HaloBand;
    }

    private float currentSpeed = 1f;
    private readonly List<NoteGap> pressGaps = new List<NoteGap>(16);
    private readonly List<NoteGap> releaseGaps = new List<NoteGap>(16);
    private readonly List<float> gapBreaks = new List<float>(48);
    private readonly List<WrapGroup> wrapGroups = new List<WrapGroup>(8);
    private readonly List<WrapGroup> releaseWrapGroups = new List<WrapGroup>(8);

    /// <summary>缺口邊緣往內化開的寬度，佔一格鍵道寬的比例。只往內側化開，不外擴。</summary>
    private const float NoteGapFeatherLanes = 0.2f;
    /// <summary>包圈和力度光暈之間留的縫、包圈本身的厚度，都佔音符高的比例。</summary>
    private const float WrapGapShare = 0.08f;
    private const float WrapBandShare = 0.30f;
    /// <summary>兩顆音符之間的空隙小於幾格鍵道寬，就合成同一圈。</summary>
    private const float WrapMergeLanes = 0.6f;
    /// <summary>
    /// 踏板那一層比判定線高多少。音符是 +0.1（NoteController.judgmentYOffset），框貼在它上面一點點。
    /// </summary>
    /// <remarks>
    /// 不能放在音符下面：小節線是不透明的 Lit 平面、會寫深度，而且就躺在音符底下。
    /// 框放在 +0.08 的時候剛好被它擋掉，踩下的框和小節線疊在一起的地方只剩小節線自己
    /// 的暗色 —— 看起來就是「線變黑了」。框在音符上面也不會蓋住音符：兩者都是透明、
    /// 不寫深度，誰先畫由 sortingOrder 決定，框是 0、音符比它大。
    /// </remarks>
    private const float NoteLayerYOffset = 0.11f;

    private float beatLineTopY = float.NegativeInfinity;
    private int beatLineProbeFrame = -100000;

    /// <summary>踏板那一層的高度：音符平面上方，而且一定高過小節線。找不到判定線就退回軌道面。</summary>
    private float ResolveNoteLayerY()
    {
        if (cachedJudgmentLine == null) ResolveJudgmentZ();
        if (cachedJudgmentLine == null) return surfaceY;
        float y = cachedJudgmentLine.position.y + NoteLayerYOffset;

        // 拋物線模式下**不量**：這個量法抓的是「隨便找到的一條小節線」的世界高度，
        // 而弧線會把小節線抬高或壓低好幾百個單位，抓到哪一條就差多少。兩秒重量一次
        // 的結果就是踏板每兩秒跳一次——那就是「踏板位置不穩定」。弧線模式下小節線
        // 和踏板本來就查同一張表，不會互相穿透，這個保護也不需要。
        SettingsManager layerSettings = SettingsManager.Instance;
        if (layerSettings != null && layerSettings.EffectiveNoteArcHeight > 0f)
            return y;

        // 小節線的高度是它自己的容器決定的（掛在 Track 底下、local y 0.1），和判定線
        // 無關，所以實際量一次。兩秒量一次就夠：它不會在歌曲中途換高度。
        if (Time.frameCount - beatLineProbeFrame > 120)
        {
            beatLineProbeFrame = Time.frameCount;
            BeatLineController beatLine = FindAnyObjectByType<BeatLineController>();
            Renderer beatRenderer = beatLine != null ? beatLine.GetComponentInChildren<Renderer>() : null;
            if (beatRenderer != null) beatLineTopY = beatRenderer.bounds.max.y;
        }
        if (!float.IsNegativeInfinity(beatLineTopY)) y = Mathf.Max(y, beatLineTopY + 0.01f);
        return y;
    }

    private float NoteHeightWorld()
    {
        SettingsManager settings = SettingsManager.Instance;
        return settings != null ? settings.NoteVisualHeight : 2f;
    }

    /// <summary>
    /// 世界 z 在 [<paramref name="centreZ"/> ± <paramref name="halfDepthWorld"/>] 這一段裡，
    /// 長條走廊**實際畫著**的那幾段 x（相對於軌道中心）。
    /// </summary>
    /// <remarks>
    /// 問的是場上每顆音符畫出來的身體（NoteController.TryGetVisibleCorridor），不是
    /// 從譜面時間推算。推算的版本對不上畫面：長押判中後頭凍在線上、身體用裁切窗吃
    /// 掉、可見跑道有上限、還有平滑偏移 —— 玩家看到線上什麼都沒有，框卻斷了。
    /// </remarks>
    // ── 場上音符的範圍，一幀只問一次 ──────────────────────────────────────
    //
    // 每條看得到的踏板都要問兩次走廊（踩下框、放開框）、再問一次音符頭，而每次都把
    // 場上所有音符掃過一遍、對每一顆呼叫原生的 Renderer.bounds。密集段落場上有
    // 兩三百顆音符、畫面上三四條踏板，一幀就是幾千次原生呼叫 —— 而答案在同一幀
    // 裡根本不會變。所以第一次問的時候整批量好，這一幀剩下的查詢都查表。

    private struct CorridorBox
    {
        public float MinX, MaxX, MinZ, MaxZ;
    }

    private struct HeadBox
    {
        public Bounds Head;
        public Bounds Shell;
    }

    private readonly List<CorridorBox> frameCorridors = new List<CorridorBox>(64);
    private readonly List<HeadBox> frameHeads = new List<HeadBox>(256);
    private int corridorsFrame = -1;
    private int headsFrame = -1;

    private List<CorridorBox> VisibleCorridors()
    {
        if (corridorsFrame == Time.frameCount) return frameCorridors;
        corridorsFrame = Time.frameCount;
        frameCorridors.Clear();
        IReadOnlyList<NoteController> notes = NoteController.Live;
        for (int i = 0; i < notes.Count; i++)
        {
            NoteController note = notes[i];
            if (note == null) continue;
            if (!note.TryGetVisibleCorridor(out float minX, out float maxX, out float minZ, out float maxZ))
                continue;
            frameCorridors.Add(new CorridorBox { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ });
        }
        return frameCorridors;
    }

    private List<HeadBox> VisibleHeads()
    {
        if (headsFrame == Time.frameCount) return frameHeads;
        headsFrame = Time.frameCount;
        frameHeads.Clear();
        IReadOnlyList<NoteController> notes = NoteController.Live;
        for (int i = 0; i < notes.Count; i++)
        {
            NoteController note = notes[i];
            if (note == null) continue;
            if (!note.TryGetVisibleHead(false, out Bounds head)) continue;
            note.TryGetVisibleHead(true, out Bounds shell);
            frameHeads.Add(new HeadBox { Head = head, Shell = shell });
        }
        return frameHeads;
    }

    private void CollectCorridorGaps(float centreZ, float halfDepthWorld, List<NoteGap> into)
    {
        into.Clear();
        if (trackWidth <= 0f) return;
        float zLo = centreZ - halfDepthWorld;
        float zHi = centreZ + halfDepthWorld;
        float laneFeather = trackWidth / PianoVisualLayout.LegacyLaneCount * NoteGapFeatherLanes;

        List<CorridorBox> corridors = VisibleCorridors();
        for (int i = 0; i < corridors.Count; i++)
        {
            CorridorBox box = corridors[i];
            float minX = box.MinX, maxX = box.MaxX, minZ = box.MinZ, maxZ = box.MaxZ;
            float overlap = Mathf.Min(zHi, maxZ) - Mathf.Max(zLo, minZ);
            if (overlap <= 0f) continue;

            float full = Mathf.Max(0.0001f, Mathf.Min(zHi - zLo, maxZ - minZ));
            float halfW = (maxX - minX) * 0.5f;
            into.Add(new NoteGap
            {
                Lo = minX - trackCenterX,
                Hi = maxX - trackCenterX,
                Feather = Mathf.Min(laneFeather, halfW * 0.5f),
                Strength = Mathf.Clamp01(overlap / full),
            });
        }
    }

    /// <summary>
    /// 實際畫著、疊在 [<paramref name="centreZ"/> ± <paramref name="halfDepthWorld"/>] 上的
    /// 音符頭（連同力度光暈），相鄰的合成一組。
    /// </summary>
    private void CollectWrapGroups(float centreZ, float halfDepthWorld, List<WrapGroup> into)
    {
        into.Clear();
        if (trackWidth <= 0f) return;
        float zLo = centreZ - halfDepthWorld;
        float zHi = centreZ + halfDepthWorld;

        List<HeadBox> heads = VisibleHeads();
        for (int i = 0; i < heads.Count; i++)
        {
            Bounds head = heads[i].Head;
            float overlap = Mathf.Min(zHi, head.max.z) - Mathf.Max(zLo, head.min.z);
            if (overlap <= 0f) continue;

            // 疊多少就讓多少。硬性的「有碰到就整個讓開」會在音符滑過線的那一格
            // 突然開關一次，而這條線每一幀都在動 —— 那個跳動比被壓過去更顯眼。
            float full = Mathf.Max(0.0001f, Mathf.Min(zHi - zLo, head.max.z - head.min.z));

            // 範圍含力度光暈：包圈從光暈外面開始。
            Bounds shell = heads[i].Shell;
            into.Add(new WrapGroup
            {
                Lo = shell.min.x - trackCenterX,
                Hi = shell.max.x - trackCenterX,
                ZNear = shell.min.z,
                ZFar = shell.max.z,
                RefZ = head.min.z,
                HaloBand = 0f,
                Strength = Mathf.Clamp01(overlap / full),
            });
        }

        if (into.Count < 2) return;
        into.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        float mergeGap = trackWidth / PianoVisualLayout.LegacyLaneCount * WrapMergeLanes;
        int write = 0;
        for (int i = 1; i < into.Count; i++)
        {
            WrapGroup current = into[write];
            WrapGroup next = into[i];
            if (next.Lo - current.Hi <= mergeGap)
            {
                current.Hi = Mathf.Max(current.Hi, next.Hi);
                current.ZNear = Mathf.Min(current.ZNear, next.ZNear);
                current.ZFar = Mathf.Max(current.ZFar, next.ZFar);
                // 一個和弦的音符在同一個 z 上，取最近的那一顆當參考點就夠。
                current.RefZ = Mathf.Min(current.RefZ, next.RefZ);
                // 合成一圈之後取最強的那一顆：一個和弦是一個東西，不該因為裡面
                // 某一顆剛好擦邊就整圈變淡。
                current.Strength = Mathf.Max(current.Strength, next.Strength);
                into[write] = current;
            }
            else
            {
                into[++write] = next;
            }
        }
        into.RemoveRange(write + 1, into.Count - write - 1);
    }

    /// <summary>
    /// 一條線疊到音符頭時：在框上開一個缺口，並記下要畫哪幾圈包圈。
    /// </summary>
    /// <param name="fade">
    /// 這條線整體讓位的程度，通常是「離判定線還有多遠」。到線上就是 0：和弦在那
    /// 一刻被打掉，接下來是打擊特效的事，不需要再有一圈光繞著它。
    /// </param>
    private void AddWrapGaps(float centreZ, float halfDepth, float fade,
        List<WrapGroup> groups, List<NoteGap> gaps)
    {
        groups.Clear();
        if (fade <= 0.01f || trackWidth <= 0f) return;

        CollectWrapGroups(centreZ, halfDepth, groups);
        float laneWidth = trackWidth / PianoVisualLayout.LegacyLaneCount;
        float noteHeight = NoteHeightWorld();
        for (int i = 0; i < groups.Count; i++)
        {
            WrapGroup g = groups[i];
            g.Strength *= fade;
            groups[i] = g;
            float inner = g.HaloBand + noteHeight * WrapGapShare;
            gaps.Add(new NoteGap
            {
                Lo = g.Lo - inner,
                Hi = g.Hi + inner,
                Feather = Mathf.Min(laneWidth * 0.1f, (g.Hi - g.Lo) * 0.25f + inner),
                Strength = g.Strength,
            });
        }
    }

    /// <summary>越靠近判定線，讓位讓得越少；到線上就不讓了。</summary>
    private static float WrapFade(float barZ, float judgmentZ, float noteHeight)
    {
        return Mathf.Clamp01((barZ - judgmentZ) / Mathf.Max(0.01f, noteHeight * 1.5f));
    }

    private readonly Vector2[] wrapHex = new Vector2[6];

    /// <summary>順時針走 a → b 這條邊時朝外的法線。</summary>
    private static Vector2 Outward(Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        if (edge.sqrMagnitude < 1e-10f) return Vector2.up;
        edge.Normalize();
        return new Vector2(-edge.y, edge.x);
    }

    /// <summary>把世界 z 換成那個位置代表的歌曲時間。</summary>
    private float TimeAtZ(float z, float judgmentZ, float songMs)
    {
        return songMs + (z - judgmentZ) / Mathf.Max(1f, currentSpeed) * 1000f;
    }

    /// <summary>x 這個位置的特效亮度倍率：1 = 沒有音符，noteGapFloor = 完全疊在音符上。</summary>
    /// <remarks>缺口不超出音符的寬度：化開是往音符內側收，不往外擴。</remarks>
    private float Clearance(float x, List<NoteGap> gaps)
    {
        if (gaps == null || gaps.Count == 0) return 1f;
        float clear = 1f;
        for (int i = 0; i < gaps.Count; i++)
        {
            NoteGap g = gaps[i];
            if (x <= g.Lo || x >= g.Hi) continue;
            float inside = Mathf.Min(
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(g.Lo, g.Lo + g.Feather, x)),
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(g.Hi, g.Hi - g.Feather, x)));
            clear = Mathf.Min(clear, 1f - g.Strength * inside * (1f - noteGapFloor));
        }
        return clear;
    }

    /// <summary>
    /// The body's colour: the approaching violet, turning to the lighter held violet
    /// as the pedal goes down and staying there.
    /// </summary>
    /// <remarks>
    /// It does not turn back. The colour is what the note *is* now — pressed — rather
    /// than a flash, which is the one thing on screen that says a held pedal is being
    /// held rather than merely drawn. The brightness still drops away, so a pressed
    /// note stays as quiet as an unpressed one.
    /// </remarks>
    private Color BodyColor(float sincePressMs)
    {
        Color transparentApproach = noteColor;
        transparentApproach.a = Mathf.Clamp01(approachMeshAlpha);
        if (sincePressMs < 0f) return transparentApproach;

        Color transparentHeld = heldColor;
        transparentHeld.a = Mathf.Clamp01(heldMeshAlpha);
        if (colorShiftMs <= 0f) return transparentHeld;
        return Color.Lerp(transparentApproach, transparentHeld,
            Mathf.Clamp01(sincePressMs / colorShiftMs));
    }

    /// <summary>
    /// The held depth, briefly lifted just after the pedal goes down.
    /// </summary>
    /// <remarks>
    /// This is the part of the effect no note has. A note's hit effect happens at the
    /// point it was struck; a pedal press opens the whole instrument, so it lights the
    /// entire sustained body rather than a place on the track. It reads as a change of
    /// state instead of an impact, which is what a pedal actually is — and because
    /// notes never do this, it cannot be mistaken for one even when a press lands on
    /// the same frame as a chord.
    ///
    /// Derived from the clock rather than fired on an event, so seeking to the middle
    /// of a press shows the right thing instead of nothing.
    /// </remarks>
    private float HeldDepth(float sincePressMs)
    {
        if (bodyFlashMs <= 0f || sincePressMs < 0f || sincePressMs >= bodyFlashMs)
        {
            return tuning.idleAlpha;
        }
        float phase = sincePressMs / bodyFlashMs;
        float settle = 1f - (1f - phase) * (1f - phase); // fast off the press, then easing
        return Mathf.Clamp01(tuning.idleAlpha + bodyFlashAlpha * (1f - settle));
    }

    /// <summary>
    /// 沒有吸附的踩下，畫面上至少要離音符多遠（毫秒）。
    /// </summary>
    /// <remarks>
    /// **往後推，不往前推。** 古典的標準踏法是切分踏板：手先下去、音響了，腳才跟
    /// 著踩（約慢 30~80ms），放開則落在下一個音響起的那一刻。踩在音符**之前**是
    /// 錯的踏法 —— 那會把上一個和聲一起拖進來。所以這條線只會被推到音符之後；
    /// 往前推等於在畫面上教一個錯的東西。
    ///
    /// 45ms 是切分踏板那個區間的下緣：夠讓線離開音符頭，又還在「幾乎同時」的範
    /// 圍內，不會被讀成另一拍。
    ///
    /// **只動畫面。** 聲音那邊讀的是 PedalTimeline 本身（PianoVoiceManager
    /// .ResolvePedalDown），這個渲染器自己建一份 playable 來畫 —— 兩邊從來不共用，
    /// 所以這裡推多少都不會改變延音什麼時候起作用。
    /// </remarks>
    private const int PressVisualGapMs = 45;

    /// <summary>踩下離音符多近，才算「腳和手一起下去」。</summary>
    private const int RhythmicSnapWindowMs = 80;
    /// <summary>那個音符之前腳至少要已經放開多久，才不是連音踏板在同一個和弦上換踏板。</summary>
    private const int RhythmicMinLiftMs = 150;

    private List<bool> playableSnapped = new List<bool>();

    /// <summary>每一段踏板**畫**在哪個時間開始。和 playable 一樣長。</summary>
    /// <remarks>
    /// 建的時候算一次，不是每幀算。找最近的音符要走整份譜面，而畫面上同時有好
    /// 幾段踏板、每秒又有上百幀 —— 那是一個算完之後就不會變的東西，沒有理由一直
    /// 重算。
    /// </remarks>
    private List<int> playableDrawnStart = new List<int>();
    private Chart snapChart;
    private int snapNoteCount = -1;

    /// <summary>
    /// 算出每一段踏板畫在哪裡：貼著音符的踩下往**後**挪開一點。
    /// </summary>
    /// <remarks>
    /// 吸附過的不挪：那是「腳和手一起下去」，它就該站在和弦上，繞過去是包圈的
    /// 工作。剩下的是切分踏板 —— 真實的腳本來就晚那麼一點，只是譜面上的時間戳常
    /// 常和音符差幾毫秒，畫出來就疊在音符頭上。
    ///
    /// 挪完會超過這一段自己的結束時間就不挪：寧可疊著，也不要畫出一條比放開還
    /// 晚的踩下。
    /// </remarks>
    private void BuildDrawnStarts(List<int> onsets)
    {
        playableDrawnStart.Clear();
        for (int i = 0; i < playable.Count; i++)
        {
            PedalSpan span = playable[i];
            int drawn = span.start_ms;
            bool snapped = i < playableSnapped.Count && playableSnapped[i];
            if (!snapped && onsets != null && onsets.Count > 0)
            {
                int nearest = NearestOnset(onsets, span.start_ms);
                if (Mathf.Abs(nearest - span.start_ms) < PressVisualGapMs)
                {
                    int pushed = nearest + PressVisualGapMs;
                    if (pushed < span.end_ms) drawn = pushed;
                }
            }
            playableDrawnStart.Add(drawn);
        }
    }

    /// <summary>排序過的發音點裡，離 time 最近的那一個。</summary>
    private static int NearestOnset(List<int> sorted, int time)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] < time) lo = mid + 1; else hi = mid;
        }
        if (lo >= sorted.Count) return sorted[sorted.Count - 1];
        if (lo == 0) return sorted[0];
        return time - sorted[lo - 1] <= sorted[lo] - time ? sorted[lo - 1] : sorted[lo];
    }

    /// <summary>譜面上所有要按的音符的開始時間，排序、去重。</summary>
    private static List<int> CollectOnsets(Chart chart)
    {
        var onsets = new List<int>();
        if (chart == null || chart.notes == null) return onsets;
        for (int i = 0; i < chart.notes.Count; i++)
        {
            NoteData nd = chart.notes[i];
            if (nd != null && !nd.hidden) onsets.Add(nd.startTime);
        }
        onsets.Sort();
        int write = 0;
        for (int i = 0; i < onsets.Count; i++)
            if (write == 0 || onsets[i] != onsets[write - 1]) onsets[write++] = onsets[i];
        onsets.RemoveRange(write, onsets.Count - write);
        return onsets;
    }

    private bool RefreshPedal()
    {
        PianoVoiceManager voices = PianoVoiceManager.Instance;
        if (voices == null)
        {
            Report("waiting: no PianoVoiceManager, so there is no pedal to read");
            return false;
        }

        // Identity is the change signal: the voice manager hands back the same
        // timeline object until the chart or the auto-pedal mode actually changes, so
        // switching "Pedal If Missing" mid-song re-reads on the next frame.
        PedalTimeline active = voices.ActivePedalTimeline;
        Chart chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
        int noteCount = chart != null && chart.notes != null ? chart.notes.Count : -1;
        if (!ReferenceEquals(active, lastTimeline) || !ReferenceEquals(chart, snapChart)
            || noteCount != snapNoteCount)
        {
            lastTimeline = active;
            snapChart = chart;
            snapNoteCount = noteCount;
            playable = PlayablePedal.Build(active.Spans, tuning.minChangeIntervalMs,
                tuning.maxPressShiftMs, tuning.minVisibleGapMs, tuning.minSpanMs);
            // 節奏踏板（腳和手一起下去）吸到它的和弦上；連音踏板維持原樣。
            List<int> onsets = CollectOnsets(chart);
            playableSnapped = PlayablePedal.SnapRhythmicPresses(playable, onsets,
                RhythmicSnapWindowMs, RhythmicMinLiftMs, tuning.minSpanMs);
            BuildDrawnStarts(onsets);
            warnedOverflow = false;
            int snappedCount = 0;
            for (int i = 0; i < playableSnapped.Count; i++) if (playableSnapped[i]) snappedCount++;
            Report($"pedal resolved: raw={active.SpanCount} playable={playable.Count} rhythmic={snappedCount}");
        }

        if (playable.Count == 0)
        {
            // Almost always the answer: the chart has no CC64 and auto-pedal is off,
            // so there is genuinely nothing to draw.
            Report("nothing to draw: this chart resolved to no pedal at all " +
                   "(set Audio > Pedal If Missing to per beat or per bar)");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Creates one small mechanical object below the player edge of the track.
    /// It is deliberately independent of the scrolling cue: the cue says when,
    /// while this object says whether the pedal is physically up or down.
    /// </summary>
    private void EnsurePhysicalPedal()
    {
        float desiredWidth = Mathf.Max(1.2f, trackWidth * pedalWidthFraction);
        float desiredLength = Mathf.Max(1f, desiredWidth * pedalLengthToWidth);
        bool sizeChanged = pedalAssembly != null &&
            (Mathf.Abs(physicalPedalWidth - desiredWidth) > 0.01f ||
             Mathf.Abs(physicalPedalLength - desiredLength) > 0.01f);
        if (sizeChanged)
        {
            pedalAssembly.SetActive(false);
            DisposeRuntimeObject(pedalAssembly);
            pedalAssembly = null;
        }
        if (pedalAssembly != null) return;

        // A script/domain reload can preserve the generated hierarchy while its
        // non-serialized C# references are cleared. Do not leave an old flat pedal
        // underneath the rebuilt mechanical model.
        GameObject staleAssembly = GameObject.Find("Mechanical Sustain Pedal");
        if (staleAssembly != null)
        {
            staleAssembly.SetActive(false);
            DisposeRuntimeObject(staleAssembly);
        }

        physicalPedalWidth = desiredWidth;
        physicalPedalLength = desiredLength;
        // The camera is shallow to the keyboard. A physically thin slab reads as
        // a flat sprite from that view, so retain a deliberately visible metal
        // sidewall while keeping the top proportions piano-like.
        float thickness = Mathf.Clamp(desiredLength * 0.145f, 0.30f, 0.72f);

        EnsurePedalMaterials();
        if (pedalTreadleMesh == null) pedalTreadleMesh = BuildRoundedPedalMesh();
        pedalGlassBlock = new MaterialPropertyBlock();
        pedalEdgeBlock = new MaterialPropertyBlock();
        pedalEdgeRenderers.Clear();

        pedalAssembly = new GameObject("Mechanical Sustain Pedal");
        pedalAssembly.transform.SetParent(transform, false);

        // A short floor plinth and a deep lacquer housing establish a fixed piece
        // of piano hardware. The lever is the only part parented to pedalPivot.
        CreatePrimitivePart(pedalAssembly.transform, "Pedal Floor Plinth", PrimitiveType.Cube,
            new Vector3(0f, -thickness * 1.05f, -desiredLength * 0.08f), Quaternion.identity,
            new Vector3(desiredWidth * 1.38f, thickness * 0.42f, desiredLength * 0.48f),
            pedalBaseMaterial, out _);

        CreatePrimitivePart(pedalAssembly.transform, "Black Lacquer Pedal Housing", PrimitiveType.Cube,
            new Vector3(0f, 0f, desiredLength * 0.035f), Quaternion.identity,
            new Vector3(desiredWidth * 0.82f, thickness * 2.34f, desiredLength * 0.27f),
            pedalBaseMaterial, out _);

        // Two separate bearing blocks make the axle read as a physical hinge
        // rather than a glowing line passing through the treadle.
        float bearingX = desiredWidth * 0.285f;
        Vector3 bearingScale = new Vector3(
            desiredWidth * 0.16f, thickness * 1.38f, desiredLength * 0.16f);
        CreatePrimitivePart(pedalAssembly.transform, "Left Hinge Bearing", PrimitiveType.Cube,
            new Vector3(-bearingX, thickness * 0.72f, -desiredLength * 0.005f), Quaternion.identity,
            bearingScale, pedalBaseMaterial, out _);
        CreatePrimitivePart(pedalAssembly.transform, "Right Hinge Bearing", PrimitiveType.Cube,
            new Vector3(bearingX, thickness * 0.72f, -desiredLength * 0.005f), Quaternion.identity,
            bearingScale, pedalBaseMaterial, out _);

        CreatePrimitivePart(pedalAssembly.transform, "Gold Hinge Axle", PrimitiveType.Cylinder,
            new Vector3(0f, thickness * 1.14f, -desiredLength * 0.018f), Quaternion.Euler(0f, 0f, 90f),
            new Vector3(thickness * 0.72f, desiredWidth * 0.39f, thickness * 0.72f),
            pedalBrassMaterial, out _);

        pedalPivot = new GameObject("Pedal Hinge Pivot").transform;
        pedalPivot.SetParent(pedalAssembly.transform, false);
        // Pivot and visible axle share exactly the same centre, so the treadle
        // rotates as a lever instead of translating around an imaginary point.
        pedalPivotRestY = thickness * 1.14f;
        // Rotation already lowers the long toe substantially.  The former 11%
        // whole-pivot drop moved the complete pedal out of the camera when it was
        // pressed.  Keep only a small mechanical compression at the hinge.
        pedalMechanicalDrop = Mathf.Clamp(desiredLength * 0.012f, 0.08f, 0.18f);
        pedalPivot.localPosition = new Vector3(0f, pedalPivotRestY, 0f);

        // A custom rounded trapezoid is used instead of a stretched capsule.
        // It stays narrow at the hinge, opens into a long lever and terminates
        // in the broad semicircular toe found on a real sustain pedal.
        CreateMeshPart(pedalPivot, "Dark Gold Pedal Underside", pedalTreadleMesh,
            new Vector3(0f, -thickness * 0.34f, -desiredLength * 0.012f), Quaternion.identity,
            new Vector3(desiredWidth * 1.05f, thickness * 0.92f, desiredLength * 1.025f),
            pedalEdgeMaterial, out Renderer darkGoldUnderside);

        CreateMeshPart(pedalPivot, "Brushed Gold Treadle", pedalTreadleMesh,
            new Vector3(0f, thickness * 0.04f, 0f), Quaternion.identity,
            new Vector3(desiredWidth, thickness, desiredLength),
            pedalBrassMaterial, out Renderer goldBody);

        // The inset polished face produces a real metal rim and highlight rather
        // than faking the state with an emissive flat colour.
        CreateMeshPart(pedalPivot, "Polished Gold Treadle Face", pedalTreadleMesh,
            new Vector3(0f, thickness * 0.52f, -desiredLength * 0.045f), Quaternion.identity,
            new Vector3(desiredWidth * 0.86f, thickness * 0.12f, desiredLength * 0.91f),
            pedalGlassMaterial, out pedalGlassRenderer);

        // A thin raised rim remains visible along the rounded toe even when the
        // centre face reflects a dark part of the environment.
        CreateMeshPart(pedalPivot, "Raised Gold Toe Rim", pedalTreadleMesh,
            new Vector3(0f, thickness * 0.455f, -desiredLength * 0.020f), Quaternion.identity,
            new Vector3(desiredWidth * 0.955f, thickness * 0.10f, desiredLength * 0.975f),
            pedalEdgeMaterial, out Renderer goldRim);

        pedalEdgeRenderers.Add(darkGoldUnderside);
        pedalEdgeRenderers.Add(goldBody);
        pedalEdgeRenderers.Add(goldRim);

        pedalPressAmount = 0f;
        pedalPressPulse = 0f;
        pedalStateInitialized = false;
        pedalAssembly.SetActive(false);
    }

    private void EnsurePedalMaterials()
    {
        if (pedalBaseMaterial == null)
            pedalBaseMaterial = RuntimeLitMaterial("Pedal Black Lacquer",
                new Color(0.025f, 0.022f, 0.020f, 1f), 0.35f, 0.82f, Color.black);
        if (pedalBrassMaterial == null)
            pedalBrassMaterial = RuntimeLitMaterial("Pedal Brushed Gold Edge",
                new Color(0.58f, 0.34f, 0.075f, 1f), 0.96f, 0.82f,
                new Color(0.018f, 0.009f, 0.001f, 1f));
        if (pedalGlassMaterial == null)
            pedalGlassMaterial = RuntimeLitMaterial("Pedal Polished Gold Face",
                new Color(0.88f, 0.62f, 0.18f, 1f), 0.98f, 0.94f,
                new Color(0.030f, 0.014f, 0.0015f, 1f));
        if (pedalEdgeMaterial == null)
            pedalEdgeMaterial = RuntimeLitMaterial("Pedal Brass Light",
                new Color(0.52f, 0.27f, 0.055f, 1f), 0.70f, 0.86f,
                new Color(0.10f, 0.035f, 0.002f, 1f));
    }

    private static Material RuntimeLitMaterial(string materialName, Color baseColor,
        float metallic, float smoothness, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave
        };
        if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 1f);
        if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
        if (material.HasProperty(EmissionColorId)) material.SetColor(EmissionColorId, emission);
        material.EnableKeyword("_EMISSION");
        // Opaque TRACK is queue 2000 and note sprites render later. Drawing the
        // pedal first lets real depth testing keep it behind both systems.
        material.renderQueue = 1900;
        return material;
    }

    private static Transform CreatePrimitivePart(Transform parent, string partName,
        PrimitiveType type, Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
        Material material, out Renderer renderer)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        return part.transform;
    }

    private static Transform CreateMeshPart(Transform parent, string partName, Mesh mesh,
        Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
        Material material, out Renderer renderer)
    {
        var part = new GameObject(partName);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        return part.transform;
    }

    private static Mesh BuildRoundedPedalMesh()
    {
        const int noseSegments = 12;
        var outline = new List<Vector2>(noseSegments + 7)
        {
            new Vector2(-0.20f, 0f),
            new Vector2(0.20f, 0f),
            new Vector2(0.25f, -0.30f),
            new Vector2(0.39f, -0.51f)
        };
        for (int i = 0; i <= noseSegments; i++)
        {
            float angle = Mathf.PI * i / noseSegments;
            outline.Add(new Vector2(
                Mathf.Cos(angle) * 0.50f,
                -0.69f - Mathf.Sin(angle) * 0.31f));
        }
        outline.Add(new Vector2(-0.39f, -0.51f));
        outline.Add(new Vector2(-0.25f, -0.30f));

        int count = outline.Count;
        var vertices = new List<Vector3>(count * 2 + 2);
        var uvs = new List<Vector2>(count * 2 + 2);
        vertices.Add(new Vector3(0f, 0.5f, -0.52f));
        uvs.Add(new Vector2(0.5f, 0.52f));
        for (int i = 0; i < count; i++)
        {
            Vector2 point = outline[i];
            vertices.Add(new Vector3(point.x, 0.5f, point.y));
            uvs.Add(new Vector2(point.x + 0.5f, -point.y));
        }
        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0f, -0.5f, -0.52f));
        uvs.Add(new Vector2(0.5f, 0.52f));
        int bottomStart = vertices.Count;
        for (int i = 0; i < count; i++)
        {
            Vector2 point = outline[i];
            vertices.Add(new Vector3(point.x, -0.5f, point.y));
            uvs.Add(new Vector2(point.x + 0.5f, -point.y));
        }

        var triangles = new List<int>(count * 12);
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int topA = 1 + i;
            int topB = 1 + next;
            int bottomA = bottomStart + i;
            int bottomB = bottomStart + next;
            triangles.Add(0); triangles.Add(topA); triangles.Add(topB);
            triangles.Add(bottomCenter); triangles.Add(bottomB); triangles.Add(bottomA);
            triangles.Add(topA); triangles.Add(bottomA); triangles.Add(topB);
            triangles.Add(topB); triangles.Add(bottomA); triangles.Add(bottomB);
        }

        var mesh = new Mesh
        {
            name = "Rounded Sustain Pedal Treadle",
            hideFlags = HideFlags.DontSave
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void SetPhysicalPedalVisible(bool visible)
    {
        if (pedalAssembly != null && pedalAssembly.activeSelf != visible)
            pedalAssembly.SetActive(visible);
    }

    private void UpdatePhysicalPedal(float songMs, float judgmentZ)
    {
        if (pedalAssembly == null || pedalPivot == null) return;

        // MaterialPropertyBlock is not a UnityEngine.Object and is lost during a
        // domain/script reload even when the runtime pedal hierarchy survives.
        // Recreate it here so entering Play Mode, recompiling, or switching charts
        // cannot leave a valid renderer paired with a null property block.
        if (pedalGlassBlock == null) pedalGlassBlock = new MaterialPropertyBlock();
        if (pedalEdgeBlock == null) pedalEdgeBlock = new MaterialPropertyBlock();

        if (IvoryLaneKeyboard.TryGetPedalAnchor(out Transform pianoAnchor))
        {
            if (pedalAssembly.transform.parent != pianoAnchor)
                pedalAssembly.transform.SetParent(pianoAnchor, false);
            pedalAssembly.transform.localPosition = Vector3.zero;
            pedalAssembly.transform.localRotation = Quaternion.identity;
            pedalAssembly.transform.localScale = Vector3.one;
        }
        else
        {
            // Fallback while the runtime keyboard is still constructing.
            if (pedalAssembly.transform.parent != transform)
                pedalAssembly.transform.SetParent(transform, true);
            Vector3 pedalCenter = ResolveVisiblePedalCenter(judgmentZ);
            Vector3 hingePosition = pedalCenter + Vector3.forward * (physicalPedalLength * 0.5f);
            pedalAssembly.transform.position = hingePosition;
            float projectedWidthScale = ResolvePedalScreenWidthScale(hingePosition, judgmentZ);
            pedalAssembly.transform.localScale = new Vector3(projectedWidthScale, 1f, 1f);
        }

        bool down = ResolvePhysicalPedalDown(songMs);
        if (!pedalStateInitialized)
        {
            pedalStateInitialized = true;
            pedalWasDown = down;
            pedalPressAmount = down ? 1f : 0f;
        }
        else if (down != pedalWasDown)
        {
            pedalWasDown = down;
            pedalPressPulse = 0f;
        }

        float duration = down ? pedalPressSeconds : pedalReleaseSeconds;
        float rate = duration > 0.0001f ? 1f / duration : 1000f;
        pedalPressAmount = Mathf.MoveTowards(pedalPressAmount, down ? 1f : 0f,
            Time.unscaledDeltaTime * rate);
        pedalPressPulse = Mathf.MoveTowards(pedalPressPulse, 0f,
            Time.unscaledDeltaTime / 0.18f);

        float eased = pedalPressAmount * pedalPressAmount * (3f - 2f * pedalPressAmount);
        // The treadle extends a long way towards the player.  A positive rest
        // angle lifts that long toe by several world units and makes the released
        // pedal intersect the ivory front fascia.  Keep even values left behind
        // by an older serialized component on the safe, downward side of the
        // hinge; pressing still has the full lever motion to pedalPressedAngle.
        float safeRestAngle = Mathf.Min(pedalRestAngle, -2.5f);
        // Likewise cap legacy -18 degree values. With this long treadle that old
        // angle puts the toe below the viewport before the release animation can
        // be seen.
        float safePressedAngle = Mathf.Clamp(pedalPressedAngle, -10f, safeRestAngle - 1f);
        float angle = Mathf.Lerp(safeRestAngle, safePressedAngle, eased);
        pedalPivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
        pedalPivot.localPosition = new Vector3(0f,
            pedalPivotRestY - pedalMechanicalDrop * eased, 0f);

        if (pedalGlassRenderer != null)
        {
            pedalGlassBlock.Clear();
            pedalGlassBlock.SetColor(BaseColorId, new Color(0.88f, 0.62f, 0.18f, 1f));
            pedalGlassBlock.SetColor(EmissionColorId, new Color(0.030f, 0.014f, 0.0015f, 1f));
            pedalGlassRenderer.SetPropertyBlock(pedalGlassBlock);
        }

        pedalEdgeBlock.Clear();
        pedalEdgeBlock.SetColor(BaseColorId, new Color(0.58f, 0.34f, 0.075f, 1f));
        pedalEdgeBlock.SetColor(EmissionColorId, new Color(0.018f, 0.009f, 0.001f, 1f));
        for (int i = 0; i < pedalEdgeRenderers.Count; i++)
        {
            if (pedalEdgeRenderers[i] != null)
                pedalEdgeRenderers[i].SetPropertyBlock(pedalEdgeBlock);
        }
    }

    private bool ResolvePhysicalPedalDown(float songMs)
    {
        SettingsManager settings = SettingsManager.Instance;
        if (settings != null && settings.CurrentPianoPedalSource == PianoPedalSource.Player)
        {
            MIDIInputManager midi = MIDIInputManager.Instance;
            if (midi != null) return midi.SustainPedalDown;
        }
        return IsDownAt(songMs);
    }

    private float ResolveReleaseWarning(float songMs)
    {
        if (releaseWarningMs <= 0f || playable.Count == 0) return 0f;
        int index = FirstUnconsumed(songMs);
        if (index >= playable.Count) return 0f;
        PedalSpan span = playable[index];
        if (span.start_ms > songMs) return 0f;
        float remaining = span.end_ms - songMs;
        return remaining >= 0f && remaining <= releaseWarningMs
            ? 1f - Mathf.Clamp01(remaining / releaseWarningMs)
            : 0f;
    }

    private float ResolveJudgmentZ()
    {
        if (cachedJudgmentLine == null)
        {
            GameObject line = GameObject.Find("JudgmentLine");
            cachedJudgmentLine = line != null ? line.transform : null;
        }
        return cachedJudgmentLine != null ? cachedJudgmentLine.position.z : 0f;
    }

    private float ResolvePedalScreenWidthScale(Vector3 hingePosition, float judgmentZ)
    {
        if (cachedMainCamera == null) cachedMainCamera = Camera.main;
        if (cachedMainCamera == null || physicalPedalWidth <= 0.001f) return 1f;

        Vector3 trackCenterAtLine = new Vector3(trackCenterX, surfaceY, judgmentZ);
        Vector3 trackLeft = trackCenterAtLine + Vector3.left * (trackWidth * 0.5f);
        Vector3 trackRight = trackCenterAtLine + Vector3.right * (trackWidth * 0.5f);
        float projectedTrackWidth = Mathf.Abs(
            cachedMainCamera.WorldToScreenPoint(trackRight).x -
            cachedMainCamera.WorldToScreenPoint(trackLeft).x);

        Vector3 pedalCenter = hingePosition + Vector3.back * (physicalPedalLength * 0.5f);
        Vector3 pedalLeft = pedalCenter + Vector3.left * (physicalPedalWidth * 0.5f);
        Vector3 pedalRight = pedalCenter + Vector3.right * (physicalPedalWidth * 0.5f);
        float projectedPedalWidth = Mathf.Abs(
            cachedMainCamera.WorldToScreenPoint(pedalRight).x -
            cachedMainCamera.WorldToScreenPoint(pedalLeft).x);

        if (float.IsNaN(projectedTrackWidth) || float.IsInfinity(projectedTrackWidth) ||
            float.IsNaN(projectedPedalWidth) || float.IsInfinity(projectedPedalWidth) ||
            projectedPedalWidth <= 0.01f)
        {
            return 1f;
        }

        float targetScreenWidth = projectedTrackWidth * pedalWidthFraction;
        return Mathf.Clamp(targetScreenWidth / projectedPedalWidth, 0.25f, 2f);
    }

    private Vector3 ResolveVisiblePedalCenter(float judgmentZ)
    {
        if (cachedMainCamera == null) cachedMainCamera = Camera.main;
        float visibleY = surfaceY + pedalSurfaceLift;
        if (cachedMainCamera == null)
        {
            return new Vector3(trackCenterX, visibleY,
                judgmentZ - physicalPedalLength * 0.55f);
        }

        // Preserve the track's projected horizontal centre, even if a future
        // camera preset is not centred on world X=0. Its vertical position is on
        // the player side, where opaque keys render over it as foreground hardware.
        Vector3 trackScreen = cachedMainCamera.WorldToViewportPoint(
            new Vector3(trackCenterX, surfaceY, judgmentZ));
        Ray ray = cachedMainCamera.ViewportPointToRay(new Vector3(
            trackScreen.x, Mathf.Clamp01(trackScreen.y - pedalViewportOffset), 0f));
        Plane topOfTrack = new Plane(Vector3.up, new Vector3(0f, visibleY, 0f));
        if (topOfTrack.Raycast(ray, out float distance) && distance > 0f)
        {
            Vector3 point = ray.GetPoint(distance);
            point.x = trackCenterX;
            return point;
        }

        return new Vector3(trackCenterX, visibleY,
            judgmentZ - physicalPedalLength * 0.55f);
    }

    /// <summary>
    /// Reads the track's width and surface height once. World space throughout,
    /// because that is where NoteController places notes and these have to line up
    /// with them.
    /// </summary>
    private bool EnsureGeometry()
    {
        if (haveGeometry) return true;

        GameObject track = GameObject.Find("Track");
        Renderer trackRenderer = track != null ? track.GetComponentInChildren<Renderer>() : null;
        if (trackRenderer == null)
        {
            Report(track == null
                ? "waiting: no GameObject named 'Track' (it may not be active yet)"
                : "waiting: 'Track' has no Renderer to measure");
            return false;
        }

        Bounds bounds = trackRenderer.bounds;
        trackCenterX = bounds.center.x;
        trackWidth = bounds.size.x;

        // The top of the track's bounds, which measurement says is y≈0: the track is a
        // flat plane. Not the judgment line — that sits two units above the surface,
        // because its height is a player setting and it is a bar hovering over the
        // playfield rather than part of it. Anchoring to it put the notes in mid-air.
        // Pedal timing is a transparent overlay on the opaque TRACK. Keep a very
        // small lift to avoid z-fighting without moving it into the note layer.
        surfaceY = bounds.max.y + 0.012f;

        haveGeometry = true;
        Report($"track measured: x={trackCenterX:F2} w={trackWidth:F2} surfaceY={surfaceY:F2}");
        return true;
    }

    /// <summary>
    /// The two tails that give the bars a direction: up from the press, down
    /// from the release.
    /// </summary>
    /// <remarks>
    /// **Why a direction and not another shape.** Two warm horizontal bars are
    /// told apart by brightness only while both are on screen -- which is exactly
    /// when it does not matter. A tail is read from one bar alone: the press
    /// glows *up the track*, because the rest of the pedalling is still coming;
    /// the release glows *down*, because what it ends lies behind it. Both point
    /// into the span, so the pair brackets it without filling it.
    ///
    /// **Why they do not meet in the middle.** Each tail reaches a little way and
    /// stops, and on a long span there is clear track between them. That gap is
    /// the point: it is where the dynamics field shows through. A gradient that
    /// ran the whole length would be the solid band again, drawn more politely.
    ///
    /// While the pedal is held the press bar is parked on the judgment line, so
    /// its tail stands there glowing up the track -- which reads as "still going"
    /// without anything having to move.
    /// </remarks>
    // 這四個是重複使用的緩衝區。原本每一幀、每一段踏板都 new 一次 List ——
    // 一首曲子跑下來就是持續不斷的 GC 垃圾，而且它們的內容每幀都會被整個重寫，
    // 根本沒有理由重新配置。
    private readonly System.Collections.Generic.List<Vector3> frameVertices =
        new System.Collections.Generic.List<Vector3>(512);
    private readonly System.Collections.Generic.List<Color> frameColours =
        new System.Collections.Generic.List<Color>(512);
    private readonly System.Collections.Generic.List<int> frameTriangles =
        new System.Collections.Generic.List<int>(768);
    private readonly System.Collections.Generic.List<Vector3> emberVertices =
        new System.Collections.Generic.List<Vector3>(1024);
    private readonly System.Collections.Generic.List<Color> emberColours =
        new System.Collections.Generic.List<Color>(1024);
    private readonly System.Collections.Generic.List<int> emberTriangles =
        new System.Collections.Generic.List<int>(1024);
    private readonly System.Collections.Generic.List<Vector3> beamVertices =
        new System.Collections.Generic.List<Vector3>(8);
    private readonly System.Collections.Generic.List<Color> beamColours =
        new System.Collections.Generic.List<Color>(8);
    private readonly System.Collections.Generic.List<int> beamTriangles =
        new System.Collections.Generic.List<int>(12);
    // uv.x = 離中心線多遠、uv.y = 離源頭多遠。衰減在 shader 裡算，這裡只負責說
    // 每個頂點站在哪。
    private readonly System.Collections.Generic.List<Vector2> emberUV =
        new System.Collections.Generic.List<Vector2>(1024);
    private readonly System.Collections.Generic.List<Vector2> beamUV =
        new System.Collections.Generic.List<Vector2>(256);

    /// <summary>
    /// 網格裡「不跟著自己的 z 彎、整段用同一個參考點抬起來」的那幾段頂點。
    /// </summary>
    /// <remarks>
    /// 框、光暈、火星都是沿著跑道躺的東西，逐頂點彎才對。包圈不是：它圍著的
    /// 音符是一片剛性的貼圖（見 WrapGroup.RefZ）。範圍一定是照 Start 遞增加進來的
    /// （頂點只會往後長），所以套用的時候一路往前走就好，不必查表。
    /// </remarks>
    private struct ArcRigidRange
    {
        public int Start;
        public int End;
        public float Distance;   // 離判定線多遠（網格座標）
    }

    private readonly System.Collections.Generic.List<ArcRigidRange> frameRigid =
        new System.Collections.Generic.List<ArcRigidRange>(16);

    /// <param name="pressBarAlpha">
    /// 踩下那條橫桿的殘量。放開之後它會淡掉，而淡掉的同時放開的那一條正好抵達
    /// —— 兩條接得上，中間不會有一格什麼都沒有。
    /// </param>
    /// <param name="snappedPress">這一段的踩下是節奏踏板、已經吸到和弦上：畫包圈。</param>
    private void PlaceBracket(Visual visual, float headZ, float tailZ, float judgmentZ,
        float songMs, float pressBarAlpha, bool snappedPress,
        float speedForArc, float travelSecondsForArc)
    {
        if (visual.BodyRenderer != null) visual.BodyRenderer.enabled = false;
        if (visual.Bracket == null || visual.BracketMesh == null) return;
        SettingsManager arcSettings = SettingsManager.Instance;
        bool arcActive = arcSettings != null && arcSettings.EffectiveNoteArcHeight > 0f
                         && travelSecondsForArc > 0f && speedForArc > 0.0001f;

        // 停住的時候要**看起來**落在判定線上。橫桿有自己的厚度，而判定線的
        // 圖形和 ResolveJudgmentZ() 也未必在同一個 z 上，所以留一個可調的位移，
        // 而不是假設兩者剛好對齊。
        float headDraw = Mathf.Max(headZ, judgmentZ) + pedalBarZOffset;
        float span = tailZ - headDraw;
        if (span <= 0.01f)
        {
            visual.Bracket.enabled = false;
            return;
        }

        // 尾巴的長度：固定一段，但短的踏板不能讓兩道漸層碰在一起 —— 碰到就
        // 又變回一條實心帶了。
        float reach = Mathf.Min(trackWidth * 0.26f, span * 0.30f);
        float halfWidth = CueWidth(trackWidth) * 0.5f;
        float rail = Mathf.Max(0.02f, trackWidth * 0.008f);

        var vertices = frameVertices;
        var colours = frameColours;
        var triangles = frameTriangles;
        vertices.Clear();
        colours.Clear();
        triangles.Clear();
        frameRigid.Clear();

        var emberV = emberVertices;
        var emberC = emberColours;
        var emberT = emberTriangles;
        emberV.Clear();
        emberC.Clear();
        emberT.Clear();
        emberUV.Clear();

        // 兩個細框各自有哪幾格被長條走廊佔著。一般音符不讓，框從它底下過。
        float frameHalfDepth = markerFrameDepthWorld * 0.5f;
        CollectCorridorGaps(headDraw, frameHalfDepth, pressGaps);
        CollectCorridorGaps(tailZ, frameHalfDepth, releaseGaps);

        // 疊到音符頭的地方讓出來，改由包圈繞過去。**踩下和放開兩條都做。**
        //
        // 踩下那條被吸到和弦上的時候（節奏踏板），和弦頭正好在 headZ、往後佔一個
        // 音符高，所以量的是那一段；沒吸附的和放開那條沒有這個保證，量的就是線自
        // 己前後半個音符高之內有什麼。
        float noteHeight = NoteHeightWorld();
        AddWrapGaps(
            snappedPress ? headZ + noteHeight * 0.5f : headDraw,
            noteHeight * 0.5f,
            WrapFade(headZ, judgmentZ, noteHeight),
            wrapGroups, pressGaps);
        AddWrapGaps(tailZ, noteHeight * 0.5f,
            WrapFade(tailZ, judgmentZ, noteHeight),
            releaseWrapGroups, releaseGaps);

        // 框的兩條長邊。**拋物線模式要沿路切段**：一整片四頂點的四邊形只有兩端
        // 會被抬到弧線上，中間是一條直線（弦），整個框就被拉成一片斜平面——那正是
        // 「左右的裝飾線還是直線」的樣子。切段的成本只在有弧線時才付。
        float railSegmentWorld = 2f;
        int RailSteps(float a, float b)
        {
            if (!arcActive) return 1;
            float length = Mathf.Abs(b - a);
            return Mathf.Clamp(Mathf.CeilToInt(length / railSegmentWorld), 1, 64);
        }

        void Rail(float x0, float x1, float a, float b, Color near, Color far)
        {
            int steps = RailSteps(a, b);
            for (int s = 0; s < steps; s++)
            {
                float t0 = (float)s / steps;
                float t1 = (float)(s + 1) / steps;
                float za = Mathf.Lerp(a, b, t0);
                float zb = Mathf.Lerp(a, b, t1);
                Color ca = Color.Lerp(near, far, t0);
                Color cb = Color.Lerp(near, far, t1);
                int at = vertices.Count;
                vertices.Add(new Vector3(x0, za, 0f));
                vertices.Add(new Vector3(x1, za, 0f));
                vertices.Add(new Vector3(x1, zb, 0f));
                vertices.Add(new Vector3(x0, zb, 0f));
                colours.Add(ca); colours.Add(ca); colours.Add(cb); colours.Add(cb);
                triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
                triangles.Add(at); triangles.Add(at + 2); triangles.Add(at + 3);
            }
        }

        // 一條橫過軌道的細邊，在有音符的地方斷開。
        //
        // 斷點放在每個缺口的化開起點、邊緣、另一側邊緣、化開終點，中間各畫一段、兩
        // 端各帶自己位置的亮度 —— 缺口的邊就是準的，不必把整條切成幾十段去逼近。
        void MaskedRail(float x0, float x1, float a, float b, Color colour, List<NoteGap> gaps)
        {
            gapBreaks.Clear();
            gapBreaks.Add(x0);
            gapBreaks.Add(x1);
            void Break(float e)
            {
                if (e > x0 && e < x1) gapBreaks.Add(e);
            }
            for (int i = 0; i < gaps.Count; i++)
            {
                // 化開那一段取中點多切一刀：SmoothStep 是曲線，只拿兩端線性內插會
                // 讓缺口的邊看起來是一段斜坡。化開在音符寬度之內，不往外擴。
                float feather = gaps[i].Feather;
                Break(gaps[i].Lo);
                Break(gaps[i].Lo + feather * 0.5f);
                Break(gaps[i].Lo + feather);
                Break(gaps[i].Hi - feather);
                Break(gaps[i].Hi - feather * 0.5f);
                Break(gaps[i].Hi);
            }
            gapBreaks.Sort();

            for (int i = 0; i + 1 < gapBreaks.Count; i++)
            {
                float left = gapBreaks[i];
                float right = gapBreaks[i + 1];
                if (right - left < 0.0001f) continue;
                float leftClear = Clearance(left, gaps);
                float rightClear = Clearance(right, gaps);
                if (leftClear < 0.01f && rightClear < 0.01f) continue;
                Color lc = colour; lc.a *= leftClear;
                Color rc = colour; rc.a *= rightClear;
                int at = vertices.Count;
                vertices.Add(new Vector3(left, a, 0f));
                vertices.Add(new Vector3(right, a, 0f));
                vertices.Add(new Vector3(right, b, 0f));
                vertices.Add(new Vector3(left, b, 0f));
                colours.Add(lc); colours.Add(rc); colours.Add(rc); colours.Add(lc);
                triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
                triangles.Add(at); triangles.Add(at + 2); triangles.Add(at + 3);
            }
        }

        // 包住一個和弦的光：和框同色，在力度光暈外面，形狀跟音符一樣是六角形。
        //
        // 最亮的地方離內緣一小段、往外化開。內緣不是最亮，是因為內緣貼著的是力度
        // 光暈 —— 兩道光在同一條邊上一樣亮，就分不出哪一圈是哪一圈。
        void WrapRing(WrapGroup g, Color colour)
        {
            if (colour.a <= 0.001f) return;
            float offset = g.HaloBand + noteHeight * WrapGapShare;
            float band = noteHeight * WrapBandShare;
            float cx = (g.Lo + g.Hi) * 0.5f;
            float cy = (g.ZNear + g.ZFar) * 0.5f - judgmentZ;
            float halfW = (g.Hi - g.Lo) * 0.5f + offset;
            float halfH = (g.ZFar - g.ZNear) * 0.5f + offset;
            float tip = Mathf.Min(halfH * 0.52f, halfW * 0.45f);
            float flat = halfW - tip;

            Vector2[] ring = wrapHex;
            ring[0] = new Vector2(-halfW, 0f);
            ring[1] = new Vector2(-flat, halfH);
            ring[2] = new Vector2(flat, halfH);
            ring[3] = new Vector2(halfW, 0f);
            ring[4] = new Vector2(flat, -halfH);
            ring[5] = new Vector2(-flat, -halfH);

            Color innerColour = colour; innerColour.a *= 0.55f;
            Color fade = colour; fade.a = 0f;
            int at = vertices.Count;
            for (int layer = 0; layer < 3; layer++)
            {
                float push = layer == 0 ? 0f : layer == 1 ? band * 0.35f : band;
                Color c = layer == 0 ? innerColour : layer == 1 ? colour : fade;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 prev = ring[(i + 5) % 6];
                    Vector2 here = ring[i];
                    Vector2 next = ring[(i + 1) % 6];
                    Vector2 n1 = Outward(prev, here);
                    Vector2 n2 = Outward(here, next);
                    // 斜接：兩條邊各自外推之後的交點，尖端的帶子才不會被夾細。
                    float miter = 1f + Vector2.Dot(n1, n2);
                    Vector2 offsetDir = miter > 0.0001f ? (n1 + n2) / miter : n1;
                    Vector2 v = here + offsetDir * push;
                    vertices.Add(new Vector3(cx + v.x, cy + v.y, 0f));
                    colours.Add(c);
                }
            }
            // 整圈用音符自己的參考點抬高、橫向也用同一個倍率 —— 它圍著的是一片
            // 剛性的貼圖，不是沿跑道躺著的長條。
            frameRigid.Add(new ArcRigidRange
            {
                Start = at,
                End = vertices.Count,
                Distance = g.RefZ - judgmentZ,
            });
            for (int layer = 0; layer < 2; layer++)
            {
                int a = at + layer * 6;
                int b = a + 6;
                for (int i = 0; i < 6; i++)
                {
                    int next = (i + 1) % 6;
                    triangles.Add(a + i); triangles.Add(b + i); triangles.Add(b + next);
                    triangles.Add(a + i); triangles.Add(b + next); triangles.Add(a + next);
                }
            }
        }

        // 踩下／放開的標記：一個空心的細框，中心在 z。前後兩條橫邊、左右兩條短邊，
        // 中間什麼都沒有 —— 音符從它中間穿過去的時候看得到音符。
        void MarkerFrame(float z, Color colour, List<NoteGap> gaps)
        {
            if (colour.a <= 0.001f) return;
            float centre = z - judgmentZ;
            float a = centre - markerFrameDepthWorld * 0.5f;
            float b = centre + markerFrameDepthWorld * 0.5f;
            float line = Mathf.Min(markerFrameLineWorld, markerFrameDepthWorld * 0.45f);
            MaskedRail(-halfWidth, halfWidth, a, a + line, colour, gaps);
            MaskedRail(-halfWidth, halfWidth, b - line, b, colour, gaps);
            float cap = Mathf.Min(markerFrameCapWorld, halfWidth);
            MaskedRail(-halfWidth, -halfWidth + cap, a + line, b - line, colour, gaps);
            MaskedRail(halfWidth - cap, halfWidth, a + line, b - line, colour, gaps);
        }

        // 兩端的菱形。結算畫面的金色分隔線就是「一條線，兩端各一顆菱形」——
        // 同一個母題用在軌道上，兩邊的視覺語言就是同一套。上亮下暗做出倒角，
        // 平的菱形只是一個色塊。
        void Finial(float x, float z, float size, Color face)
        {
            float half = size * 0.5f;
            float waist = half * 0.62f;
            float y = z - judgmentZ;
            Color lit = Color.Lerp(face, Color.white, 0.55f);
            Color deep = new Color(face.r * 0.34f, face.g * 0.30f, face.b * 0.26f, face.a);
            int at = vertices.Count;
            vertices.Add(new Vector3(x, y + half, 0f));
            vertices.Add(new Vector3(x + waist, y, 0f));
            vertices.Add(new Vector3(x, y - half, 0f));
            vertices.Add(new Vector3(x - waist, y, 0f));
            colours.Add(lit); colours.Add(face); colours.Add(deep); colours.Add(face);
            triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
            triangles.Add(at); triangles.Add(at + 2); triangles.Add(at + 3);
        }

        // 一片三角碎片，寫進**加法混合**的那一份網格。碎的、有方向的質感靠
        // 三角形，三個頂點就夠。
        void Shard(float x, float z, float size, bool up, Color face)
        {
            float half = size * 0.5f;
            float y = z - judgmentZ;
            float tip = up ? half : -half;
            Color lit = Color.Lerp(face, Color.white, 0.45f);
            int at = emberV.Count;
            emberV.Add(new Vector3(x, y + tip, 0f));
            emberV.Add(new Vector3(x + half * 0.86f, y - tip * 0.6f, 0f));
            emberV.Add(new Vector3(x - half * 0.86f, y - tip * 0.6f, 0f));
            emberC.Add(lit); emberC.Add(face); emberC.Add(face);
            // 尖端是源頭，兩個底角是末端：一顆火星於是有方向，而不是一個亮三角。
            emberUV.Add(new Vector2(0f, 0f));
            emberUV.Add(new Vector2(1f, 1f));
            emberUV.Add(new Vector2(1f, 1f));
            emberT.Add(at); emberT.Add(at + 1); emberT.Add(at + 2);
        }

        // 一顆火星兩層：一圈暖光，一顆近白的芯。
        //
        // 亮度真正的來源是**加法混合**：alpha 混合的粒子疊在一起只是互相蓋掉，
        // 一百顆半透明的火星最亮還是等於一顆；加法會累加，密集處自然燒成一片
        // 白熱的核心、往外自然衰減。這才是參考圖那個樣子，而且不必把每個頂點的
        // 顏色寫成荒謬的數字去硬闖 bloom 的門檻。
        void Glowing(float x, float z, float size, bool up, float alpha)
        {
            Color rim = pedalBarRimColor;
            Shard(x, z, size * 2.6f, up,
                new Color(rim.r * 1.9f, rim.g * 1.45f, rim.b * 0.95f, alpha * 0.70f));
            Shard(x, z, size, up, new Color(4.2f, 3.6f, 2.6f, alpha));
        }

        // 橫桿自己的光暈，也走加法。原本只有火星在發光，線只是一塊亮色 ——
        // 而魔法陣之所以亮，是因為它整片都在累加。一條不發光的線旁邊擺著發光的
        // 火星，看起來只會像線壞了。
        //
        // 三排頂點：上下兩排透明、中間那排最亮，所以是從線往兩側散開的光，不是
        // 一塊有邊界的亮方塊。
        // 一個會自己化開的發光矩形：三排 x 三列，正中間那一格是 uv = 0 的芯，
        // 四周都是 uv = 1 的邊。衰減在 shader 裡算，所以它沒有輪廓。
        //
        // 只用四個角是行不通的：四個角的 uv 全是 1，橫向衰減 1 - uv.x² 會把整片
        // 算成零。細長的形狀必須有中心的那一排，這一點在這個檔案裡已經踩過兩次。
        // 上下兩邊各自的長度。兩邊一樣長就是一圈對稱的暈，那是「這裡有東西」；
        // 一邊長一邊短才有方向，而方向本身就能說是踩下還是抬起。
        // 光暈的中段相對於兩端有多短。1 = 等寬。
        //
        // 括號的形狀拿掉了：方向已經由「光往哪一側拖」講得很清楚，再讓輪廓也去
        // 講同一件事，只是讓那片光多了一個會被讀成物件的形狀。光該是沒有形狀的。
        const float GlowWaistShare = 1f;

        // 從線的**邊緣**往外散的一側光暈，形狀是一個中括號：兩端伸得遠、中間
        // 收窄。
        //
        // **形狀做在光暈上，不是做在線上。** 那條線只有幾個像素厚，把它折成中括
        // 號等於什麼都沒做 —— 沒有人看得到幾個像素的彎折。光暈有面積，形狀才讀
        // 得出來。
        //
        // 中括號本身是有意義的：兩端伸長、中間收窄，整個形狀有一個明確的朝向，
        // 而那個朝向就是腳的方向。踩下的往譜面深處張開，放開的往玩家這側張開。
        //
        // 參數 reach 帶正負號：正的往譜面深處，負的往玩家這一側。
        void GlowSide(float cy, float halfW, float reach, Color core, List<NoteGap> gaps)
        {
            if (Mathf.Abs(reach) < 0.0001f) return;

            // 28 欄：一欄大約一格鍵道，讓開音符的時候缺口才對得上位置。
            const int Cols = 28;
            int at = emberV.Count;
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col <= Cols; col++)
                {
                    float t = col / (float)Cols * 2f - 1f;
                    float away = Mathf.Abs(t);
                    // 中段留平、外側才張開。1.9 次方把彎折集中在最外側，中間那
                    // 一大段維持等寬 —— 一路遞增的話讀成的是三角形不是括號。
                    float span = reach * Mathf.Lerp(GlowWaistShare, 1f, Mathf.Pow(away, 1.9f));
                    emberV.Add(new Vector3(t * halfW, cy + (row == 0 ? 0f : span), 0f));
                    Color masked = core;
                    masked.a *= Clearance(t * halfW, gaps);
                    emberC.Add(masked);
                    // 橫向的衰減收斂一點（×0.7）：照原本的 0..1 的話兩端剛好被
                    // 1 - uv.x² 歸零，而兩端正是這個形狀要被看見的部分。
                    emberUV.Add(new Vector2(away * 0.7f, row));
                }
            }
            for (int col = 0; col < Cols; col++)
            {
                int a = at + col;
                int b = at + (Cols + 1) + col;
                emberT.Add(a); emberT.Add(b); emberT.Add(b + 1);
                emberT.Add(a); emberT.Add(b + 1); emberT.Add(a + 1);
            }
        }

        // 網格的 +y 對到世界的 +z，也就是音符來的方向。所以 back 是往譜面深處
        // 拖的那一截，front 是往玩家這一側探出來的那一截。
        //
        // **光暈讓開線本身。** 原本這一片是以線為中心、最亮的那一排正好壓在線上
        // ——於是白色的芯和橘金的細邊全被加法混合吃掉，整條變成一團會發光的東
        // 西，沒有芯也沒有框。真實的光是從發光體**邊緣往外**散的，不是蓋在它自
        // 己身上。
        //
        // 所以改成兩片：一片從線的後緣往深處、一片從前緣往玩家。中間讓出線自己
        // 的厚度，那一段完全沒有光暈，邊緣就永遠讀得到。
        void BarGlow(float z, float halfSpan, float back, float front, Color core, List<NoteGap> gaps)
        {
            float cy = z - judgmentZ;
            // 光暈從細框的外緣往外散，框裡面留空。
            float gap = markerFrameDepthWorld * 0.5f;
            GlowSide(cy + gap, halfSpan, back, core, gaps);
            GlowSide(cy - gap, halfSpan, -front, core, gaps);
        }

        // 踩著的時候，停在判定線上的那條會飄出暖色的火星。停住的東西很容易被
        // 讀成「畫面卡住了」—— 有東西在上面動，才會被讀成「正在持續」。
        //
        // 每一顆的一生都是歌曲時鐘和自己的索引算出來的：沒有狀態、不需要生成、
        // 不需要池，跳轉或預覽重載也沒有東西要重設。十六顆菱形不值得一整套粒子
        // 系統。會脈動的光也能表示「還在」，但脈動是**警告**的語彙；飄散的火星
        // 是「正在燒」的語彙 —— 踏板對聲音做的正是後者。
        void Motes(float barZ, float halfSpan, float burst)
        {
            // 覆蓋整條橫桿，不是點綴幾顆。線有多長就鋪多少 —— 十幾顆散在一條
            // 滿版的線上，讀起來是「有幾個小東西」而不是「這條線在燒」。
            const int Count = 110;
            const float LifeMs = 1600f;
            float size = trackWidth * 0.0032f;
            // 慢。火星是從一個還在燒的東西上飄出來的，不是被噴出來的。
            float drift = trackWidth * 0.055f;

            for (int i = 0; i < Count; i++)
            {
                // 每一顆用自己的相位錯開，看起來才不像一起發射的一排。
                float phase = Mathf.Repeat(songMs / LifeMs + i * 0.6180339f, 1f);

                // 越飄越淡，最後收在完全透明，所以循環回去的時候不會跳。
                float alpha = (1f - phase) * (1f - phase) * 0.85f * burst;
                if (alpha < 0.02f) continue;

                // 往四面八方散開，不是往上飄。往上是「被吹走」，四散才是
                // 「從這裡發出來的」—— 而聲音本來就是從這條線往外擴的。
                // 沿著線均分，再加一點各自的偏移：純亂數會結塊，純均分會排成
                // 一列梳子。
                float along = (i + 0.5f) / Count * 2f - 1f;
                float spread = along + (Mathf.Repeat(i * 0.7548777f, 1f) - 0.5f) * (1.6f / Count);
                float angle = Mathf.Repeat(i * 2.3999632f, 6.2831853f);
                float reach = drift * phase;
                float wobble = Mathf.Sin(songMs * 0.0016f + i * 2.4f) * trackWidth * 0.004f;

                // 一半朝上一半朝下，散出來才不像同一個模子印的。
                float moteX = spread * halfSpan * 0.92f + Mathf.Cos(angle) * reach + wobble;
                alpha *= Clearance(moteX, pressGaps);
                if (alpha < 0.02f) continue;
                Glowing(moteX, barZ + Mathf.Sin(angle) * reach,
                    size * (0.55f + 0.45f * (1f - phase)), (i & 1) == 0, alpha);
            }
        }

        // 兩條沿著邊緣走的細軌，不是一塊實心色塊。一大片柔邊在深色軌道上會糊成
        // 一團；細線淡出才讀得出是「一個框開了頭」，質感也在那兩條線上。
        // 把整段踏板**框起來**，而不是從兩條線各拖一小截漸層出去。
        //
        // 漸層那版說的是「這裡開始」和「這裡結束」，中間那一大段沒有任何東西宣告
        // 它屬於踏板 —— 譜面上同時還有音符、力度色場、導引線，那段空白就被它們
        // 佔去了。一個閉合的框把「這一段是踏板的」講死，而且只用了四條細線。
        //
        // 細線可以比色塊亮得多：它佔的面積小，同樣的濃度不會壓過底下的力度色場。
        // 這也是為什麼框比漸層划算 —— 更清楚，而且更不吵。
        void Frame(float fromZ, float toZ, Color edge)
        {
            float a = fromZ - judgmentZ;
            float b = toZ - judgmentZ;
            if (b - a < 0.01f) return;
            // 兩側的長邊。兩端的短邊由踩下、放開兩個細框負責，這裡不重畫一次 ——
            // 疊在一起的話細框就又變成一條粗的實心線了。
            Rail(-halfWidth, -halfWidth + rail, a, b, edge, edge);
            Rail(halfWidth - rail, halfWidth, a, b, edge, edge);
        }

        // 菊金：比橘金再黃一點、再亮一點的金。框是這段譜面上最細的東西，不亮到
        // 這個程度就會被旁邊任何一片有面積的東西蓋過去。
        Color frameGold = new Color(3.05f, 2.35f, 0.86f, 0.95f);
        Frame(Mathf.Min(headDraw, tailZ), Mathf.Max(headDraw, tailZ), frameGold);

        // 踩下的細框：到判定線之前跟著譜面下來，踩著的時候停在線上，放開後淡掉。
        Color pressFrame = pressBarColor;
        pressFrame.a *= pressBarAlpha;
        MarkerFrame(headDraw, pressFrame, pressGaps);
        for (int i = 0; i < wrapGroups.Count; i++)
        {
            Color ringColour = pressFrame;
            ringColour.a *= wrapGroups[i].Strength;
            WrapRing(wrapGroups[i], ringColour);
        }

        // 放開（抬起）的細框：**只讓位，不包圈。**
        //
        // 量過這個曲庫：放開有 97% 落在某個音符的 45ms 之內，而且一段都不在音符
        // 之前 —— 那就是切分踏板的樣子（手按下去的那一刻腳抬起來）。所以「放開正
        // 好在音符上」不是特例，是常態。
        //
        // 常態就不能用特例的畫法。每一次換踏板都在和弦外面畫一圈光，密的段落會
        // 變成一串圈。包圈留給吸附過的踩下：那裡和弦和踏板是同一個動作，圈是在說
        // 「這個位置被那個和弦佔了」；放開只是經過。
        MarkerFrame(tailZ, releaseFrameColor, releaseGaps);

        bool held = headZ <= judgmentZ && tailZ > judgmentZ;
        // 踩下的那一瞬間有多近。火星跟著它，不跟著「踩著」——「踩著」是狀態，
        // 由停住的那條橫桿負責。
        float pressMoment = 1f - Mathf.Clamp01(
            Mathf.Abs(headZ - judgmentZ) / Mathf.Max(0.01f, trackWidth * 0.35f));
        float glowReach = trackWidth * pedalBarGlowShare;
        // 踩著的那條是畫面上唯一停住的東西，它得撐得住被一直看。
        // 貼著線，只多出一點點邊。光暈散得比線本身還寬的時候，讀到的是一片霧，
        // 線反而不見了 —— 發光的是那條線，不是它周圍的空氣。
        //
        // **兩條線的光往相反的方向拖。** 踩下那條往譜面深處拖，因為腳是往下往後
        // 走的；放開那條往玩家這側探出來，因為腳是抬起來、往前離開的。
        //
        // 這樣即使兩條線長得一模一樣，也一眼分得出哪一條是哪一條 —— 靠的是光的
        // 方向，不是顏色或粗細。顏色和粗細在同一條軌道上已經被別的東西用掉了，
        // 而方向還沒有人用。
        // 踩下那條的光改成**站起來**（見 PlaceReleaseBeam 的 PressWall）。躺在
        // 軌道面上的光暈和軌道共平面，會和力度色場、導引線疊在同一層互相稀釋；
        // 站起來的光有自己的空間，而且和放開那道光幕、兩側的光牆是同一種語言。
        //
        // 放開那條維持躺著的一小圈：它只存在一瞬間，站起來的東西需要時間被讀，
        // 而它沒有那個時間。
        BarGlow(tailZ, halfWidth, glowReach * 0.55f, glowReach * 3.4f,
            new Color(3.0f, 2.1f, 1.1f, 0.6f), releaseGaps);

        // 踩下的細框也給一圈小光暈，前後一樣長、比放開那圈小。
        //
        // 以前刻意不給，理由是要靠光往哪一側拖來分辨踩下和放開。現在兩側有光牆
        // 從踩下鋪到放開，起點和終點已經由它講清楚了，框本身不必再背這件事 ——
        // 沒有光暈的細框在一片發光的東西旁邊，反而讀起來像沒亮。
        // 顏色跟著框走（偏白的金），放開後和框一起淡掉。
        float pressGlowAlpha = 0.42f * pressBarAlpha;
        if (pressGlowAlpha > 0.01f)
            BarGlow(headDraw, halfWidth, glowReach * 0.9f, glowReach * 0.9f,
                new Color(3.2f, 2.75f, 2.1f, pressGlowAlpha), pressGaps);

        if (pressMoment > 0.02f) Motes(headDraw, halfWidth, pressMoment);

        // 踩著的時候，橫桿的**下緣**（玩家這一側）包著一層慢慢動的亮粒子。
        //
        // 火星（Motes）講的是「剛踩下去那一下」，會自己淡掉；這一層講的是「還踩
        // 著」，所以它不會淡，只會一直在那裡慢慢挪。狀態要用持續的東西講，事件才
        // 用會消失的東西講 —— 這兩件事用同一種粒子的話，玩家分不出哪個是哪個。
        //
        // 貼在下緣不是上緣：上面是譜面來的方向，任何東西擺在那裡都會擋到還沒打的
        // 音符；下面已經打完了，是畫面上唯一空著的地方。
        void HeldCoat(float barZ, float halfSpan, float strength)
        {
            if (strength < 0.02f) return;

            const int Count = 64;
            float size = trackWidth * 0.0034f;
            float band = Mathf.Max(0.03f, markerLineDepthWorld * 0.45f) * 1.9f;

            for (int i = 0; i < Count; i++)
            {
                // 沿線均分再各自偏一點：純均分是一把梳子，純亂數會結塊。
                float along = (i + 0.5f) / Count * 2f - 1f;
                float jitter = (Mathf.Repeat(i * 0.7548777f, 1f) - 0.5f) * (1.7f / Count);
                // 慢。每一顆沿著線來回挪，週期各自不同，所以整排永遠不會同步。
                float sway = Mathf.Sin(songMs * 0.00042f + i * 2.3999632f) * (2.2f / Count);
                float x = (along + jitter + sway) * halfSpan * 0.97f;

                // 離線多遠。緊貼著，而且都在下緣那一側。
                float depth = 0.22f + 0.78f * Mathf.Repeat(i * 0.4142135f, 1f);
                float breathe = 0.78f + 0.22f * Mathf.Sin(songMs * 0.0011f + i * 1.117f);
                float z = barZ - band * depth * breathe;

                // 越靠近線越亮：光是從線上來的。
                float near = 1f - depth;
                float alpha = strength * (0.30f + 0.70f * near * near)
                    * (0.72f + 0.28f * Mathf.Sin(songMs * 0.0019f + i * 0.917f));
                alpha *= Clearance(x, pressGaps);
                if (alpha < 0.02f) continue;

                Glowing(x, z, size * (0.6f + 0.5f * near), (i & 1) == 0, alpha);
            }
        }

        HeldCoat(headDraw, halfWidth, held ? 1f : 0f);

        // 踩下的那顆大一點：它是停在判定線上被看最久的那一個。
        float finial = trackWidth * 0.024f;
        // 飾件用邊的顏色，不用面的白 —— 白色的菱形在白心的橫桿兩端等於看不見。
        Color pressFace = new Color(pedalBarRimColor.r, pedalBarRimColor.g,
            pedalBarRimColor.b, 1f);
        Color releaseFace = new Color(pedalBarRimColor.r * 0.82f, pedalBarRimColor.g * 0.78f,
            pedalBarRimColor.b * 0.74f, 1f);
        Color Faded(Color face, float x, List<NoteGap> gaps)
        {
            face.a *= Clearance(x, gaps);
            return face;
        }
        Finial(-halfWidth, headDraw, finial, Faded(pressFace, -halfWidth, pressGaps));
        Finial(halfWidth, headDraw, finial, Faded(pressFace, halfWidth, pressGaps));
        Finial(-halfWidth, tailZ, finial * 0.82f, Faded(releaseFace, -halfWidth, releaseGaps));
        Finial(halfWidth, tailZ, finial * 0.82f, Faded(releaseFace, halfWidth, releaseGaps));

        // 斷口也要收頭。
        //
        // 這條線的**外側**兩端本來就各有一顆菱形 —— 那是它說「我到這裡為止」的方
        // 式。被音符切開的那兩個端點沒有收頭，於是同一條線上出現了兩種結尾：一種
        // 是收好的，一種是沒有的。沒收頭的那種讀起來不是「線斷了」，是「線不見
        // 了」——而「不見了」和「被音符擋住」在畫面上長得一模一樣，這就是斷開看不
        // 出來的原因。
        //
        // 收頭比把缺口加寬有效得多：加寬只是讓空白變大，空白再大還是空白；一個
        // 收好的端點是**主動**的訊號。
        BreakCaps(headDraw, pressFace, finial * 0.52f, pressGaps);
        BreakCaps(tailZ, releaseFace, finial * 0.44f, releaseGaps);

        // 斷口的收頭：每個缺口的兩側各一顆，大小跟著那個缺口讓開的程度走。
        void BreakCaps(float z, Color face, float size, List<NoteGap> gaps)
        {
            if (gaps == null) return;
            for (int i = 0; i < gaps.Count; i++)
            {
                NoteGap gap = gaps[i];
                // 幾乎沒讓開的缺口不必收頭：那裡的線本來就還在。
                if (gap.Strength < 0.25f) continue;
                // 缺口比軌道還寬的時候（整條線都被吃掉）沒有端點可言。
                if (gap.Lo <= -halfWidth && gap.Hi >= halfWidth) continue;

                float scale = size * Mathf.Clamp01(gap.Strength);
                if (gap.Lo > -halfWidth)
                    Finial(gap.Lo, z, scale, Faded(face, gap.Lo - 0.001f, gaps));
                if (gap.Hi < halfWidth)
                    Finial(gap.Hi, z, scale, Faded(face, gap.Hi + 0.001f, gaps));
            }
        }

        // 框也是每幀在 C# 生的（body 那片四邊形在這個模式下是關掉的），所以
        // 弧線要在這裡加，不是在 shader 裡。
        ApplyArcToVertices(vertices, judgmentZ, speedForArc, travelSecondsForArc, true,
                           colours, frameRigid);
        visual.BracketMesh.Clear();
        visual.BracketMesh.SetVertices(vertices);
        visual.BracketMesh.SetColors(colours);
        visual.BracketMesh.SetTriangles(triangles, 0);
        visual.BracketMesh.RecalculateBounds();

        // **踩下之前不畫牆。** pressBarAlpha 在踩下之前就是 1（它管的是放開後的
        // 淡出），拿它當牆的強度的話，光牆會跟著還沒被踩的那條線一路飄下來 ——
        // 那就變成一個預告，而這道光要講的是「現在正踩著」。
        float wallStrength = headZ <= judgmentZ ? pressBarAlpha : 0f;
        PlaceReleaseBeam(visual, headZ, tailZ, judgmentZ, songMs, headDraw, pressBarAlpha,
            wallStrength, speedForArc, travelSecondsForArc);

        if (visual.Embers != null && visual.EmberMesh != null)
        {
            visual.EmberMesh.Clear();
            if (emberT.Count > 0)
            {
                ApplyArcToVertices(emberV, judgmentZ, speedForArc, travelSecondsForArc, true,
                                   emberC);
                visual.EmberMesh.SetVertices(emberV);
                visual.EmberMesh.SetColors(emberC);
                visual.EmberMesh.SetUVs(0, emberUV);
                visual.EmberMesh.SetTriangles(emberT, 0);
                visual.EmberMesh.RecalculateBounds();
            }
            visual.Embers.enabled = emberT.Count > 0;
            Transform glow = visual.Embers.transform;
            glow.position = new Vector3(trackCenterX, ResolveNoteLayerY() + 0.004f, judgmentZ);
            glow.rotation = Quaternion.Euler(90f, 0f, 0f);
            glow.localScale = Vector3.one;
        }

        Transform mark = visual.Bracket.transform;
        // 網格的 +y 對到世界的 +z，原點放在判定線上。
        // 和音符同一層：框畫在地板上、音符浮在判定線的高度的話，鏡頭斜著看，
        // 兩者明明在同一個 z 也會錯開。
        mark.position = new Vector3(trackCenterX, ResolveNoteLayerY(), judgmentZ);
        mark.rotation = Quaternion.Euler(90f, 0f, 0f);
        mark.localScale = Vector3.one;
        visual.Bracket.enabled = true;
    }

    /// <summary>
    /// The release: a curtain of light standing straight up off the track.
    /// </summary>
    /// <remarks>
    /// **Why it stands up.** Everything else the pedal draws lies flat on the
    /// floor, and flat things are foreshortened to almost nothing by the time
    /// they reach the far end of the runway. A curtain standing perpendicular to
    /// the track is seen face-on by a camera looking along it, so the release
    /// reads at any distance -- and standing up is also what "lift" looks like.
    ///
    /// **Why it is its own object.** The floor pieces are all rotated ninety
    /// degrees so their mesh y becomes world z. This one must keep world y as up,
    /// so it cannot share that transform.
    ///
    /// Brightest where it meets the track and fading upward: light coming off a
    /// surface, not a panel hanging in the air.
    /// </remarks>
    private void PlaceReleaseBeam(Visual visual, float headZ, float tailZ, float judgmentZ,
        float songMs, float pressBarZ, float pressBarAlpha, float wallStrength,
        float speedForArc, float travelSecondsForArc)
    {
        if (visual.Beam == null || visual.BeamMesh == null) return;

        // 放開線過了判定線之後，這段踏板的**放開側**就沒有東西要畫了。但踩下那
        // 道站起來的光還在收 —— 它和放開是同一個瞬間開始的，如果這裡直接關掉整
        // 個網格，那道光會是「消失」而不是「收回去」，而收回去正是它要講的話。
        bool releasePending = tailZ >= judgmentZ;
        if (!releasePending && wallStrength <= 0.001f)
        {
            visual.Beam.enabled = false;
            return;
        }

        float halfWidth = CueWidth(trackWidth) * 0.5f;
        float height = trackWidth * pedalBeamShare;
        Color foot = new Color(2.6f, 1.8f, 0.95f, 0.60f);
        Color top = new Color(foot.r, foot.g, foot.b, 0f);

        beamVertices.Clear();
        beamColours.Clear();
        beamTriangles.Clear();
        beamUV.Clear();

        // 放開的那條線在網格裡的深度。整個網格錨在判定線上，所以這是相對值。
        float beamZ = tailZ - judgmentZ;

        // 放開那一刻的橫向光牆。不是一塊平整的板：沿寬度分段，每一段的高度各自
        // 不同，等高就是一條齊頭的線 —— 整齊的矩形讀起來是「一個物件」，參差才
        // 讀成光。
        //
        // 用連續的三角帶而不是一段一段分開的矩形：加法混合下，分開的段落邊界會
        // 疊出一道亮縫。
        if (releasePending)
        {
            const int Steps = 26;
            int baseAt = beamVertices.Count;
            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float x = Mathf.Lerp(-halfWidth, halfWidth, t);
                // 兩個頻率疊起來，單一個正弦會看出是一個規律的波形。
                float vary = 0.55f
                    + 0.30f * Mathf.Repeat(i * 0.7548777f, 1f)
                    + 0.15f * Mathf.Sin(i * 1.7f);
                // 放開那一刻正好有音符的地方，光幕讓開：矮下去、也淡下去。
                float clear = Clearance(x, releaseGaps);
                float tall = height * Mathf.Clamp(vary, 0.25f, 1.3f) * clear;
                float across = Mathf.Abs(t * 2f - 1f);
                Color footHere = foot;
                footHere.a *= clear;
                beamVertices.Add(new Vector3(x, 0f, beamZ));
                beamColours.Add(footHere);
                beamUV.Add(new Vector2(across, 0f));
                beamVertices.Add(new Vector3(x, tall, beamZ));
                beamColours.Add(top);
                beamUV.Add(new Vector2(across, 1f));
            }
            for (int i = 0; i < Steps; i++)
            {
                int a = baseAt + i * 2;
                int b = a + 2;
                beamTriangles.Add(a); beamTriangles.Add(a + 1); beamTriangles.Add(b + 1);
                beamTriangles.Add(a); beamTriangles.Add(b + 1); beamTriangles.Add(b);
            }
        }

        // 一顆會自己化開的光點。三排 x 三列，正中間是 uv = 0 的芯。
        void Spark(float x, float y, float z, float radiusX, float radiusY, Color core)
        {
            int at = beamVertices.Count;
            for (int row = 0; row < 3; row++)
            {
                float along = row == 1 ? 0f : 1f;
                for (int col = 0; col < 3; col++)
                {
                    float across = col == 1 ? 0f : 1f;
                    beamVertices.Add(new Vector3(
                        x + (col - 1) * radiusX, y + (row - 1) * radiusY, z));
                    beamColours.Add(core);
                    beamUV.Add(new Vector2(across, along));
                }
            }
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int a = at + row * 3 + col;
                    int b = a + 3;
                    beamTriangles.Add(a); beamTriangles.Add(b); beamTriangles.Add(b + 1);
                    beamTriangles.Add(a); beamTriangles.Add(b + 1); beamTriangles.Add(a + 1);
                }
            }
        }

        // 軌道兩側的光點。踩下的時候往上升，放開的時候往下落。
        //
        // **方向是有意思的，不是隨便挑的。** 踩下去是制音器離開琴弦，整台琴被
        // 放開、開始共鳴 —— 所以東西是往上走的。放開是制音器落下把聲音壓住 ——
        // 所以東西往下掉，落到軌道面就沒了。兩個動作互為反面，看一眼就知道是哪
        // 一個，不必比對顏色或形狀。
        //
        // 小而亮的點是唯一一種光靠 bloom 就會好看的東西：沒有輪廓可以做壞，也
        // 不會像簾幕或線條那樣在讀的時候變成「一塊有形狀的物件」。
        // 踩下的瞬間把光點拋上去，它們一路減速到頂點、再一路加速落下，**剛好在
        // 該放開的那一刻回到軌道邊緣**。
        //
        // 這不只是好看：拋物線的高度就是這段踏板的長度，玩家看一眼就知道還要踩
        // 多久，而落地的瞬間就是該抬腳的瞬間。資訊藏在物理裡，不需要再多一個
        // 讀數或倒數。
        //
        // 所以它不是一段循環播放的特效 —— 一顆光點的一生就是一次踏板，沒有回收
        // 也沒有重來。
        float spanLength = Mathf.Max(0.01f, tailZ - headZ);
        float sideInner = trackWidth * (0.5f + sideLineGapShare);
        float sideBand = trackWidth * sideLineBandShare;
        // 側邊那條窄帶鋪開的長度。光點和光柱共用它：它們是同一件事的兩種讀法，
        // 佔的是同一段譜面，兩個數字分開寫遲早會走散。
        float sideDepth = trackWidth * 1.6f;

        // 踩得越久拋得越高。真實的重力下飛行時間和高度不是線性的，但這裡要的
        // 是**讀得出來**：長一倍的踏板就該明顯地高一截，所以直接讓高度跟著
        // 長度走，再夾住兩端免得長踏板把光點丟出畫面。
        float climb = Mathf.Clamp(spanLength * 0.22f,
            trackWidth * 0.10f, trackWidth * 0.55f);

        // 光有多高 = 還要踩多久。踩下時最高，一路收短，收乾淨的那一刻就是該抬
        // 腳的那一刻；最後那一小段收得比線性快，因為消失要像一個事件。
        //
        // 光和光點共用這一個算式：光點是活在光裡面的，不是旁邊另一組東西。
        float LightHeight(float progress)
        {
            float left = 1f - progress;
            return climb * 1.15f * left * (0.30f + 0.70f * left);
        }

        // 兩端一樣的收尾。光和光點都用它，所以光點不會凸出在光的外面。
        float AlongFade(float t)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.18f))
                * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.18f));
        }

        // 光點在**光的裡面**，不是在光的旁邊。
        //
        // 這樣兩者就不是兩個並排的效果，而是一個東西的裡和外：光是體積，光點是
        // 那個體積裡面在動的東西。並排的兩個效果眼睛要分別讀；包在一起的只讀一
        // 次，而且看起來比兩個都大。
        //
        // 落地的訊息沒有丟。光點的高度是**光的高度的比例**，光收乾淨的時候它們
        // 一起貼回軌道面 —— 該放開的那一刻仍然有一個明確的事件，只是現在由整個
        // 量體一起說，不是靠一條拋物線。
        void Sparks(float progress)
        {
            if (progress <= 0f || progress >= 1f) return;
            float ceiling = LightHeight(progress);
            if (ceiling <= 0.0001f) return;

            const int Points = 40;
            float edge = trackWidth * 0.5f;
            float radius = trackWidth * 0.0024f;

            // 快到該放開的時候整批亮一下：那是提示「準備抬腳」的那一下。
            float landing = Mathf.Clamp01((progress - 0.82f) / 0.18f);
            Color tint = Color.Lerp(new Color(3.6f, 2.6f, 1.45f, 1f),
                new Color(5.0f, 3.7f, 2.2f, 1f), landing);

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                for (int i = 0; i < Points; i++)
                {
                    int k = i + side * Points;

                    float t = Mathf.Repeat(k * 0.2360679f, 1f);
                    float ends = AlongFade(t);
                    if (ends < 0.01f) continue;

                    // 慢慢往上飄，飄到頂就從底下再進來。光點在光裡面是在**流
                    // 動**的 —— 定住的一組亮點是灑在上面的糖粉。
                    float rise = Mathf.Repeat(k * 0.4142135f + songMs * 0.00013f, 1f);
                    // 越靠近頂端越淡。光本身就是往上化開的，裡面的東西不能比
                    // 它還實，否則光點會從光裡浮出來變成獨立的一層。
                    float fade = 1f - rise * rise;

                    // 在光的厚度裡前後錯開一點點。全部貼在同一個平面上的話，
                    // 側面一看就是一張貼紙。
                    float across = (Mathf.Repeat(k * 0.7548777f, 1f) - 0.5f)
                        * trackWidth * 0.014f;

                    // 每顆的大小和亮度各自不同。整齊一致的一群讀起來是圖案，
                    // 有大有小才讀成一把撒在光裡的東西。
                    float grade = 0.45f + 0.9f * Mathf.Repeat(k * 0.6180339f + 0.37f, 1f);
                    float alpha = ends * fade * (0.45f + 0.55f * landing)
                        * (0.55f + 0.45f * grade);

                    Spark(sign * edge + across,
                        ceiling * rise,
                        (t - 0.5f) * sideDepth,
                        radius * grade,
                        radius * grade * 1.35f,
                        new Color(tint.r, tint.g, tint.b, alpha));
                }
            }
        }

        // 連續那一片佔整體高度的幾成，以及它的濃度。光束從這個高度往上長。
        const float SheetShare = 0.42f;
        const float SheetAlpha = 0.55f;

        // 光束的間距（佔軌道寬的比例）和支數上限。間距是**固定**的，所以一束光
        // 從出生到消失都待在同一個世界位置上。
        const float SpacingShare = 0.085f;
        const int MaxShafts = 34;

        // 譜面左右、垂直於軌道面的一片楔形光。
        //
        // **形狀就是資訊。** 它從判定線往譜面深處鋪，判定線那端最高、一路遞減，
        // 歸零的那個位置**就是放開線現在所在的位置**。所以玩家不必去讀任何數字：
        // 光鋪到哪裡，腳就還要踩到哪裡；放開線往下走，整片跟著矮下去，它抵達判
        // 定線的那一刻剛好清空。
        //
        // 這比「一根等高的柱子在變短」好讀，因為楔形同時給了兩件事：**還剩多久**
        // （鋪多遠）和**進行到哪**（最遠那一段已經塌掉了）。
        //
        // 和光點的分工沒變：光柱是低頻的一片暈，光點是高頻的細點，疊在同一條窄
        // 帶上不會互相吃掉。
        void SideGlow(float progress)
        {
            if (progress <= 0f || progress >= 1f) return;

            // 放開線離判定線多遠。網格錨在判定線上，所以這就是要鋪的長度。
            float span = tailZ - judgmentZ;
            if (span <= 0.0001f) return;

            // 判定線那端的高度。整體隨著 span 收短而一起降，而且**越後面掉得
            // 越快** —— 平方就是這個意思：剩一半的時候還有七成高，剩兩成的時候
            // 只剩三成。收尾要像一個事件，不是等速滑下去。
            float left = Mathf.Clamp01(span / Mathf.Max(0.01f, spanLength));
            float peak = climb * 1.15f * Mathf.Lerp(0.12f, 1f, left * left);
            if (peak <= 0.0001f) return;

            // 越短越燙。亮度自己也在說「快到了」，不必再加一個閃爍的倒數。
            //
            // 暖色和金色的差別在綠通道：金是 g 跟得很緊的黃（g/r 大約 0.7），
            // 讀起來是金屬；暖是 g 掉下來、b 幾乎不參與的琥珀橘（g/r 大約 0.48）。
            // 這條軌道上的金屬感已經由踏板橫桿的金邊負責了，光不該再跟它搶。
            float urge = 1f - left;
            Color core = Color.Lerp(new Color(3.10f, 1.50f, 0.56f, 0.46f),
                new Color(4.30f, 2.25f, 0.92f, 0.62f), urge * urge);
            // 一點點呼吸，讓它是活的而不是一片貼上去的板子。
            float breathe = 0.93f + 0.07f * Mathf.Sin(songMs * 0.011f);

            // 楔形的側面：t 是離判定線多遠（0 = 判定線，1 = 放開線）。
            //
            // **丘拿掉了。** 整片維持大致等高，只在最末端（放開線那一格）收掉，
            // 起伏改由沿路的不均勻來給。
            //
            // 丘是一個**形狀**，而形狀會被讀成一個物件：那片光變成了一座停在軌道
            // 旁邊的小山，而不是軌道自己在發光。等高加上不均勻則沒有輪廓可以被
            // 認出來 —— 它只是亮度有疏有密的一片光。
            //
            // 「還要踩多久」這件事沒有丟：它由這片光**鋪到哪裡**講，而那是形狀之
            // 外的另一個頻道。
            float Profile(float t)
            {
                // 只有最後 12% 收掉，免得末端切成一條直邊。
                float ends = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.12f));
                // 沿路的高低。兩個無理數頻率疊起來，週期長到不會被看出是規律。
                float rough = 0.72f
                    + 0.18f * Mathf.Sin(t * 9.7f + 1.3f)
                    + 0.10f * Mathf.Sin(t * 23.1f - 0.7f);
                return ends * Mathf.Clamp(rough, 0.35f, 1f);
            }

            // 一束光，立在 x 這個平面上、沿著 z 只佔一小段。
            //
            // **形狀是紡錘不是矩形。** 矩形的上緣和兩個上角是三條硬邊，再怎麼調
            // alpha 都還是會被眼睛判定成「一塊有邊的東西」；把寬度沿著高度收成一
            // 條曲線之後，輪廓上一個直角都不剩，收尾自然收在一個點上。
            //
            // 每一層沿 z 有三 column：兩端 uv.x = 1、中間 uv.x = 0，邊緣由 shader
            // 化開。細的東西一定要有中間那一排，不然 1 - uv.x² 會把兩邊都算成零、
            // 整束消失 —— 這個檔案裡已經踩過兩次。
            void Shaft(float x, float z, float halfLen, float height, Color foot)
            {
                const int Rows = 6;
                int at = beamVertices.Count;
                for (int row = 0; row <= Rows; row++)
                {
                    float v = row / (float)Rows;
                    // 底部最寬、往上收成一個點。0.7 次方讓它在中段就開始收，不是
                    // 到頂才突然縮 —— 那樣會變成一根有肩膀的釘子。
                    float wide = halfLen * Mathf.Pow(1f - v, 0.7f);
                    float y = height * v;
                    Color lit = new Color(foot.r, foot.g, foot.b, foot.a * (1f - v * v));
                    for (int col = 0; col < 3; col++)
                    {
                        float across = col == 1 ? 0f : 1f;
                        beamVertices.Add(new Vector3(x, y, z + (col - 1) * wide));
                        beamColours.Add(lit);
                        beamUV.Add(new Vector2(across, v));
                    }
                }
                for (int row = 0; row < Rows; row++)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        int a = at + row * 3 + col;
                        int b = a + 3;
                        beamTriangles.Add(a); beamTriangles.Add(b); beamTriangles.Add(b + 1);
                        beamTriangles.Add(a); beamTriangles.Add(b + 1); beamTriangles.Add(a + 1);
                    }
                }
            }

            // 一側一片。緊貼著軌道的邊，多層疊出來的厚度反而會讓它變成一個站在
            // 軌道旁邊的獨立量體。
            void Wall(float x)
            {
                // --- 連續的那一片 ---------------------------------------
                //
                // 這一層是**不可以有缺口的**。之前只畫參差的光束，於是兩束之間
                // 就整段沒有東西 —— 讀起來不是「一片有疏密的光」，是「幾根插在
                // 那裡的棍子」。連續的底片先把整條鋪滿，光束再加在它上面。
                const int Steps = 30;
                int at = beamVertices.Count;
                for (int i = 0; i <= Steps; i++)
                {
                    float t = i / (float)Steps;
                    float z = t * span;
                    // 最遠那一端不會硬生生切掉：Profile 已經讓它收到零，這裡再
                    // 給一點點餘量，免得最後一格變成一條亮線。
                    float tall = peak * Profile(t) * SheetShare;
                    Color lit = new Color(core.r, core.g, core.b,
                        core.a * breathe * SheetAlpha);
                    beamVertices.Add(new Vector3(x, 0f, z));
                    beamColours.Add(lit);
                    beamUV.Add(new Vector2(0f, 0f));
                    beamVertices.Add(new Vector3(x, tall, z));
                    beamColours.Add(lit);
                    beamUV.Add(new Vector2(0f, 1f));
                }
                for (int i = 0; i < Steps; i++)
                {
                    int a = at + i * 2;
                    int b = a + 2;
                    beamTriangles.Add(a); beamTriangles.Add(a + 1); beamTriangles.Add(b + 1);
                    beamTriangles.Add(a); beamTriangles.Add(b + 1); beamTriangles.Add(b);
                }

                // --- 疏密的光束 ------------------------------------------
                //
                // 它們只負責質感，不負責覆蓋。所以位置可以不規則、高矮可以差很
                // 多 —— 底下那一片保證了任何一處都不會空掉。
                //
                // 光束**釘在固定的世界位置上**，不是沿著 span 均分。
                //
                // 這就是抖動的來源。原本支數是 round(span / trackWidth * 14)，而
                // span 每一幀都在縮 —— 支數每掉一根，t = (i + 0.5) / shafts 就讓
                // **整排光束同時換位置**。一秒跳個好幾次，看起來就是在抖。
                //
                // 改成固定間距之後，每一束從出生到消失都待在同一個地方，只是隨著
                // 楔形掃過而變矮。「大家慢慢往下退」本來就該是這個樣子：退的是高
                // 度，不是位置。
                float spacing = trackWidth * SpacingShare;
                int shafts = Mathf.Min(MaxShafts, Mathf.CeilToInt(span / spacing));
                for (int i = 0; i < shafts; i++)
                {
                    float jitter = (Mathf.Repeat(i * 0.6180339f, 1f) - 0.5f) * 0.55f;
                    float z = (i + 0.5f + jitter) * spacing;
                    if (z >= span) continue;
                    float t = z / span;
                    float floorHeight = peak * Profile(t);
                    if (floorHeight <= 0.0001f) continue;

                    float thin = Mathf.Repeat(i * 0.7548777f, 1f);
                    float rise = Mathf.Repeat(i * 0.4142135f + 0.23f, 1f);
                    float lit = Mathf.Repeat(i * 0.2360679f + 0.61f, 1f);

                    // 粗細也跟著固定間距走，不跟 span —— 跟著 span 的話每一束會
                    // 隨著時間慢慢變瘦，那是另一種形式的抖。
                    float halfLen = spacing * (0.10f + 0.26f * thin);
                    // 一律**高過**那一片，所以光束是從片上長出來的，不是插在旁
                    // 邊。參差的是它們高出多少，不是有沒有。
                    float tallEach = floorHeight * (SheetShare + 0.62f * (0.35f + 0.65f * rise * rise));
                    // 亮度各自不同，而且慢慢在變 —— 一組定住的亮度是圖案。
                    float pulse = 0.72f + 0.28f * Mathf.Sin(songMs * 0.0035f + i * 2.399f);
                    float alpha = core.a * breathe * (0.40f + 0.85f * lit) * pulse;

                    Shaft(x, z, halfLen, tallEach,
                        new Color(core.r, core.g, core.b, alpha));
                }
            }

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                Wall(sign * trackWidth * 0.5f);
            }
        }

        // 踩著的時候，停住的那條橫桿上站起來的一道光。
        //
        // **高度不是算出來的，是走出來的。** 之前用兩個正弦疊加，那永遠是同一個
        // 波在跑 —— 看久了會認出那個週期，於是它讀成一個動畫而不是一團火。這裡
        // 改成每一根有自己的位置和速度：每幀給一個隨機的加速度，撞到上下限之前
        // 就開始被推回來，於是它自己會減速、停住、反向。
        //
        // 鄰居之間互相牽引（Link）是關鍵：沒有它的話每一根各走各的，讀起來是一
        // 排雜訊；有了它整排才會像一片連在一起的東西在起伏。
        void PressWall(float z, float halfSpan, float strength)
        {
            if (strength < 0.01f) return;

            // 高度上限 = 兩側光牆的一半。
            //
            // 兩側那兩片講的是「還要踩多久」，它們有資格佔畫面的高度；這一道只
            // 講「現在踩著」，一個狀態不需要和一個進度一樣大。壓到一半之後兩者
            // 的主從關係就固定了，而不是每首歌看 climb 算出多少各憑運氣。
            //
            // ×0.82 是預留給起伏的空間：模擬的高度會在 1 附近上下盪，偶爾衝到
            // 1.2 左右，乘下來剛好落在上限上。**上限要靠縮放來守，不能靠夾。**
            // 再壓到三分之一：它站在判定線上，高了就擋到正在到線的音符。
            float peak = Mathf.Min(trackWidth * 0.19f, climb * 1.15f * 0.5f) * 0.82f
                * PressWallHeightShare;
            Color core = new Color(4.6f, 3.2f, 1.7f, 0.85f * strength);

            int at = beamVertices.Count;
            for (int i = 0; i <= WallColumns; i++)
            {
                float t = i / (float)WallColumns;
                float x = (t * 2f - 1f) * halfSpan;
                // 兩端收掉：切齊的直邊會讓這道光讀成一塊立起來的板子。
                float ends = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.10f))
                    * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.10f));
                // **不夾。** 上一版在這裡夾了一刀，結果每一根一碰到上限就被切
                // 平，整排貼在天花板上變成一條直線 —— 起伏全部被吃掉了。上限是
                // 靠上面那個 0.82 的縮放守的，不是靠這裡切。
                // 有音符正在過線的那幾格讓開。
                float clear = Clearance(x, pressGaps);
                float tall = peak * wallHeight[i] * ends * strength * clear;

                Color lit = new Color(core.r, core.g, core.b, core.a * ends * clear);
                beamVertices.Add(new Vector3(x, 0f, z));
                beamColours.Add(lit);
                beamUV.Add(new Vector2(0f, 0f));
                beamVertices.Add(new Vector3(x, tall, z));
                beamColours.Add(lit);
                beamUV.Add(new Vector2(0f, 1f));
            }
            for (int i = 0; i < WallColumns; i++)
            {
                int a = at + i * 2;
                int b = a + 2;
                beamTriangles.Add(a); beamTriangles.Add(a + 1); beamTriangles.Add(b + 1);
                beamTriangles.Add(a); beamTriangles.Add(b + 1); beamTriangles.Add(b);
            }
        }

        StepWall(wallStrength > 0.01f);

        // 收得比淡出快得多：三次方在 alpha 掉到一半的時候只剩八分之一的高度。
        PressWall(pressBarZ - judgmentZ, CueWidth(trackWidth) * 0.5f,
            wallStrength * wallStrength * wallStrength);

        // 踩下的那一刻是 0、該放開的那一刻是 1。兩個端點都是同一條判定線，所以
        // 用 z 算就好，不必另外拿時鐘 —— 少一個會和畫面分岔的來源。
        float held = Mathf.Clamp01((judgmentZ - headZ) / Mathf.Max(0.01f, tailZ - headZ));
        int beforeSpark = beamVertices.Count;
        Sparks(held);
        int beforeGlow = beamVertices.Count;
        SideGlow(held);
        // 只在 Editor／開發版印：發行版裡每條光束每 30 格一行，遊玩時每秒十幾行 Debug.Log。
        if (logPedalBeam && Debug.isDebugBuild && Time.frameCount % 30 == 0)
        {
            Debug.Log(string.Format(
                "[pedal beam] held={0:F3} climb={1:F3} trackWidth={2:F3} " +
                "wall={3} sparks={4} glow={5} x=±{6:F3}",
                held, climb, trackWidth, beforeSpark,
                beforeGlow - beforeSpark, beamVertices.Count - beforeGlow,
                sideInner + sideBand * 0.5f));
        }

        // 這片光是每幀在 C# 生出來的網格，不走 PedalNote 那個 shader，所以弧線
        // 要在這裡加。頂點是以判定線為原點的區域座標（beam.position.z = judgmentZ、
        // 沒有旋轉縮放），所以世界 Z 就是 judgmentZ + v.z。
        ApplyArcToVertices(beamVertices, judgmentZ, speedForArc, travelSecondsForArc, false,
                           beamColours);

        visual.BeamMesh.Clear();
        visual.BeamMesh.SetVertices(beamVertices);
        visual.BeamMesh.SetColors(beamColours);
        visual.BeamMesh.SetUVs(0, beamUV);
        visual.BeamMesh.SetTriangles(beamTriangles, 0);
        visual.BeamMesh.RecalculateBounds();

        Transform beam = visual.Beam.transform;
        // 錨在判定線上，不是錨在放開的位置：側邊的速度線分布在整條跑道上，
        // 而放開那道光幕自己帶著 beamZ 的位移。
        beam.position = new Vector3(trackCenterX, ResolveNoteLayerY(), judgmentZ);
        beam.rotation = Quaternion.identity;
        beam.localScale = Vector3.one;
        visual.Beam.enabled = true;
    }

    /// <summary>踩著時那道光牆沿線分幾根。</summary>
    private const int WallColumns = 26;

    private readonly float[] wallHeight = new float[WallColumns + 1];
    private readonly float[] wallSpeed = new float[WallColumns + 1];
    private readonly float[] wallScratch = new float[WallColumns + 1];
    private int wallSteppedFrame = -1;
    private bool wallWasActive;

    /// <summary>
    /// 推進光牆的高度。
    /// </summary>
    /// <remarks>
    /// **為什麼用模擬不用算式。** 正弦疊加永遠是同一個波在跑，看久了會認出那個
    /// 週期，於是它讀成一段循環播放的動畫而不是一團火。給每一根自己的位置和速
    /// 度之後，它下一秒長什麼樣子連寫的人都不知道 —— 那才是「自然」的定義。
    ///
    /// **上下限是推回來，不是撞上去。** 超過上限的部分變成一個把它往回拉的力，
    /// 所以它會自己減速、停住、反向；直接夾住的話每一根都會黏在天花板上，整排
    /// 變成一條平的。
    ///
    /// **鄰居互相牽引是關鍵。** 沒有它每一根各走各的，讀起來是一排雜訊；有了它
    /// 整排才像一片連在一起的東西在起伏。
    ///
    /// 一幀只走一次：這個方法會被每一顆看得見的踏板音符呼叫，不擋住的話模擬會
    /// 跟著畫面上有幾顆踏板而跑得不一樣快。
    /// </remarks>
    private void StepWall(bool active)
    {
        if (wallSteppedFrame == Time.frameCount) return;
        wallSteppedFrame = Time.frameCount;

        if (!active)
        {
            wallWasActive = false;
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        if (!wallWasActive)
        {
            // 踩下的那一刻**噴出來**：整排給一個往上的初速，高低各異。起始高度
            // 壓在很低的地方，所以第一下是衝上來的，不是淡進來的。
            wallWasActive = true;
            for (int i = 0; i <= WallColumns; i++)
            {
                float grade = Mathf.Repeat(i * 0.6180339f, 1f);
                wallHeight[i] = 0.05f + 0.10f * grade;
                wallSpeed[i] = 2.6f + 1.9f * Mathf.Repeat(i * 0.4142135f, 1f);
            }
            return;
        }

        const float Accel = 6.4f;     // 亂流有多強
        const float Stiff = 18f;      // 超出上下限之後被推回來的力道
        const float Damp = 1.7f;      // 阻尼。沒有它會越盪越大
        const float Link = 11f;       // 鄰居之間的牽引
        const float Centre = 3.4f;    // 往中線回復的力
        const float Low = 0.16f;
        const float High = 1f;
        const float Mid = 0.60f;

        for (int i = 0; i <= WallColumns; i++) wallScratch[i] = wallHeight[i];

        for (int i = 0; i <= WallColumns; i++)
        {
            float h = wallScratch[i];

            // 每一根自己的亂流。用 PerlinNoise 而不是 Random：它沿著 i 和時間都
            // 是連續的，所以相鄰的兩根不會拿到完全無關的力，時間上也不會每幀跳。
            float noise = Mathf.PerlinNoise(i * 0.31f, Time.time * 0.55f) * 2f - 1f;
            float a = noise * Accel;

            // 往中線回復。**這一條是起伏的來源。**
            //
            // 只有亂流加上下限的話，整排會被亂流推到某一側就停在那裡 —— 上限把
            // 它擋住，於是它貼著上限不動，看起來就是「到頂就停了」。一條指向中
            // 線的力讓它衝過頭之後自己會盪回來，而盪回來又會衝過中線，於是它永
            // 遠在動。
            a += (Mid - h) * Centre;

            // 上下限：推回來。力道比中線那條大得多，所以它是牆，中線只是趨勢。
            if (h > High) a -= (h - High) * Stiff;
            else if (h < Low) a += (Low - h) * Stiff;

            // 鄰居。邊界拿自己當鄰居，等於那一側沒有拉力。
            float left = wallScratch[i > 0 ? i - 1 : i];
            float right = wallScratch[i < WallColumns ? i + 1 : i];
            a += ((left + right) * 0.5f - h) * Link;

            float v = wallSpeed[i] + a * dt;
            v -= v * Mathf.Min(1f, Damp * dt);
            wallSpeed[i] = v;
            wallHeight[i] = Mathf.Clamp(h + v * dt, 0f, High * 1.35f);
        }
    }

    /// <summary>
    /// 1 right on the judgment line, easing to 0 at <paramref name="window"/> away.
    /// </summary>
    /// <remarks>
    /// Symmetric on purpose: the flare starts before the event so the eye is
    /// already there when it happens, and lingers after so a press that lands
    /// during a busy bar is not missed. A flare that only ran afterwards would
    /// be telling the player about something they had already had to do.
    /// </remarks>
    private static float Flare(float distance, float window)
    {
        float t = 1f - Mathf.Clamp01(Mathf.Abs(distance) / window);
        return t * t;
    }

    private Visual Rent(int index)
    {
        while (pool.Count <= index) pool.Add(CreateVisual(pool.Count));
        return pool[index];
    }

    private void HideFrom(int index)
    {
        for (int i = index; i < pool.Count; i++)
        {
            if (pool[i].Object.activeSelf) pool[i].Object.SetActive(false);
        }
    }




    private Visual CreateVisual(int index)
    {
        var go = new GameObject("Pedal Note " + index);
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        var visual = new Visual
        {
            Object = go,
            BodyBlock = new MaterialPropertyBlock(),
            HeadBlock = new MaterialPropertyBlock(),
            LineBlock = new MaterialPropertyBlock(),
        };

        Material flat = VelocityHalo.Material;
        if (flat != null)
        {
            var bracket = new GameObject("Bracket", typeof(MeshFilter), typeof(MeshRenderer));
            bracket.transform.SetParent(go.transform, false);
            visual.BracketMesh = new Mesh
            {
                name = "PedalBracket " + index,
                hideFlags = HideFlags.HideAndDontSave,
            };
            visual.BracketMesh.MarkDynamic();
            bracket.GetComponent<MeshFilter>().sharedMesh = visual.BracketMesh;
            visual.Bracket = bracket.GetComponent<MeshRenderer>();
            visual.Bracket.sharedMaterial = flat;
            visual.Bracket.shadowCastingMode = ShadowCastingMode.Off;
            visual.Bracket.receiveShadows = false;
        }

        Material glow = VelocityHalo.AdditiveMaterial;
        if (glow != null)
        {
            var embers = new GameObject("Embers", typeof(MeshFilter), typeof(MeshRenderer));
            embers.transform.SetParent(go.transform, false);
            visual.EmberMesh = new Mesh
            {
                name = "PedalEmbers " + index,
                hideFlags = HideFlags.HideAndDontSave,
            };
            visual.EmberMesh.MarkDynamic();
            embers.GetComponent<MeshFilter>().sharedMesh = visual.EmberMesh;
            visual.Embers = embers.GetComponent<MeshRenderer>();
            visual.Embers.sharedMaterial = glow;
            visual.Embers.shadowCastingMode = ShadowCastingMode.Off;
            visual.Embers.receiveShadows = false;

            // 放開的那一道要站起來，所以它不能和前面那些一樣被壓平在地板上 ——
            // 得是自己的物件，保持沒有旋轉。
            var beam = new GameObject("ReleaseBeam", typeof(MeshFilter), typeof(MeshRenderer));
            beam.transform.SetParent(go.transform, false);
            visual.BeamMesh = new Mesh
            {
                name = "PedalBeam " + index,
                hideFlags = HideFlags.HideAndDontSave,
            };
            visual.BeamMesh.MarkDynamic();
            beam.GetComponent<MeshFilter>().sharedMesh = visual.BeamMesh;
            visual.Beam = beam.GetComponent<MeshRenderer>();
            visual.Beam.sharedMaterial = glow;
            visual.Beam.shadowCastingMode = ShadowCastingMode.Off;
            visual.Beam.receiveShadows = false;
        }
        visual.Body = CreateQuad(go.transform, "Body", bodyMesh, out visual.BodyRenderer);
        visual.Head = CreateQuad(go.transform, "Press", lineMesh, out visual.HeadRenderer);
        visual.Line = CreateQuad(go.transform, "Terminator", lineMesh, out visual.LineRenderer);
        visual.HeadRenderer.sortingOrder = visual.BodyRenderer.sortingOrder + 2;
        visual.LineRenderer.sortingOrder = visual.BodyRenderer.sortingOrder + 1;
        return visual;
    }

    private Transform CreateQuad(Transform parent, string name, Mesh mesh, out MeshRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go.transform;
    }

    private void BuildAssets()
    {
        // The body runs 0..1 along Z from the head, so scaling Z stretches it between
        // the head and the terminator without touching its width. The line is centred
        // on Z instead, so it straddles the moment it marks.
        bodyMesh = Quad("Pedal Note Body", 0f, 1f);
        lineMesh = Quad("Pedal Note Terminator", -0.5f, 0.5f);

        sharedMaterial = RuntimeMaterial("Nostalgia/Pedal Note", "Pedal Note (Runtime)");
    }

    /// <summary>
    /// Resolves a shader, saying so out loud when it had to settle for a fallback.
    /// </summary>
    /// <remarks>
    /// A missing shader here fails silently — the fallbacks shade by vertex colour and
    /// draw nothing useful — and "I cannot see it" is an expensive thing to debug from
    /// the outside. Shader.Find only reaches shaders that a scene already references or
    /// that are listed under Always Included Shaders, so a build can lose these even
    /// though the editor keeps them.
    /// </remarks>
    private static Material RuntimeMaterial(string shaderName, string materialName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            shader = Shader.Find("Nostalgia/Subtle Track Guide Lines");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            // Debug.LogWarning, not BuildLogger.LogWarning: that one is
            // [Conditional("UNITY_EDITOR")] and compiles out of a build — which is
            // exactly backwards, because this only ever goes wrong in a build. The
            // editor finds every shader in Assets; a build only keeps the ones a scene
            // references or that are listed under Always Included Shaders.
            Debug.LogWarning($"[PedalNoteRenderer] Shader '{shaderName}' not found; " +
                             $"falling back to '{(shader != null ? shader.name : "nothing")}'. " +
                             "Add it to Project Settings > Graphics > Always Included Shaders.");
        }
        var material = new Material(shader) { name = materialName, hideFlags = HideFlags.DontSave };
        if (shaderName == "Nostalgia/Pedal Note") material.renderQueue = 2009;
        return material;
    }

    /// <summary>
    /// 沿 Z 切成幾段的長條。段數要夠，拋物線模式才彎得出弧線。
    /// </summary>
    /// <remarks>
    /// 拋物線是在 vertex shader 裡做的（NoteArcCurve.hlsl），只有四個角的話，
    /// 兩端被抬起來、中間會是一條直線——那是弦不是弧，長的踏板音符看起來會
    /// 穿過弧線。32 段在最長的踏板上也看不出折角，而這是每個踏板共用的一份
    /// 網格，多出來的頂點只有一次成本。
    /// </remarks>
    private const int QuadSegments = 32;

    /// <summary>
    /// 把一串「以判定線為原點」的頂點抬到弧線上。給每幀自己生網格的東西用
    /// （光幕、火星），那些不走 PedalNote 的 vertex shader。
    /// </summary>
    /// <param name="flatOnTrack">
    /// 這片網格是不是「躺在軌道上」的那種——它的 transform 帶著 Euler(90,0,0)，
    /// 所以**區域 +y 對到世界 +z**（距離存在 y），而世界的「上」是區域 −z。
    /// 框、火星是這種；那道光幕沒有旋轉，距離在 z、上就是 y。
    /// 弄錯軸的話，判定線那一點的位移不會是 0，看起來就是整片浮在高空。
    /// </param>
    /// <param name="colours">
    /// 一起淡入的頂點色（可以是 null）。逐頂點做，長踏板才會是一條漸層而不是
    /// 整塊一起變淡。
    /// </param>
    /// <param name="rigid">
    /// 不照自己的 z 彎、整段共用一個參考距離的頂點範圍（包圈）。範圍必須照
    /// Start 遞增。
    /// </param>
    private void ApplyArcToVertices(List<Vector3> vertices, float judgmentZ,
                                    float speed, float travelSeconds, bool flatOnTrack,
                                    List<Color> colours = null,
                                    List<ArcRigidRange> rigid = null)
    {
        if (vertices == null || vertices.Count == 0) return;
        PrepareArc(judgmentZ, speed, travelSeconds);
        if (!NoteArcScreen.Active) return;
        int rigidAt = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            // 距離是「離判定線多遠」（網格原點就在判定線上）。
            float distance = flatOnTrack ? v.y : v.z;
            if (rigid != null)
            {
                while (rigidAt < rigid.Count && i >= rigid[rigidAt].End) rigidAt++;
                if (rigidAt < rigid.Count && i >= rigid[rigidAt].Start)
                    distance = rigid[rigidAt].Distance;
            }
            float offset = NoteArcScreen.OffsetAtZ(judgmentZ + distance);
            v.x *= NoteArcScreen.LateralAtZ(judgmentZ + distance);
            if (colours != null && i < colours.Count)
            {
                Color c = colours[i];
                c.a *= NoteArcScreen.FadeAtZ(judgmentZ + distance);
                colours[i] = c;
            }
            if (flatOnTrack) v.z -= offset;      // 區域 −z ＝ 世界 +y
            else v.y += offset;
            vertices[i] = v;
        }
    }

    /// <summary>
    /// 每幀備妥那張「世界 Z → 該抬多高」的表。和音符查的是同一張。
    /// </summary>
    /// <remarks>
    /// 條件只看「有沒有開拋物線」。曾經還看自己的 travelSeconds 和 speed——那是
    /// 弧線長度還來自各自的 spawner 時留下的。那兩個值在載譜的空檔會是 0，於是
    /// 踏板那一幀自己放棄、音符卻照算，踏板就平掉一幀再彈回來。
    /// </remarks>
    private void PrepareArc(float judgmentZ, float speed, float travelSeconds)
    {
        SettingsManager settings = SettingsManager.Instance;
        float share = settings != null ? settings.EffectiveNoteArcHeight : 0f;
        if (share <= 0f)
        {
            NoteArcScreen.Disable();
            return;
        }
        NoteArcScreen.Prepare(Camera.main, judgmentZ,
                              settings.ArcTravelWorldUnits(),
                              ResolveNoteLayerY(),
                              settings.JudgmentLineScreenHeight,
                              share,
                              settings.ArcSpawnWorldUnits());
    }

    /// <summary>
    /// 把拋物線那張表寫進 property block。傾斜模式寫 0 筆，shader 就不位移。
    /// </summary>
    private void ApplyArcBlock(MaterialPropertyBlock block, float judgmentZ, float speed,
                               float travelSeconds)
    {
        if (block == null) return;
        PrepareArc(judgmentZ, speed, travelSeconds);
        NoteArcScreen.ApplyTo(block);
    }

    private static Mesh Quad(string name, float minZ, float maxZ)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        for (int i = 0; i <= QuadSegments; i++)
        {
            float t = (float)i / QuadSegments;
            float z = Mathf.Lerp(minZ, maxZ, t);
            vertices.Add(new Vector3(-0.5f, 0f, z));
            vertices.Add(new Vector3(0.5f, 0f, z));
            // uv.y is the position along the note, which is what the shader shades by.
            uvs.Add(new Vector2(0f, t));
            uvs.Add(new Vector2(1f, t));
            // Unused by the real shaders, but the fallbacks shade by vertex colour and a
            // mesh without one draws nothing at all — which looks exactly like a bug
            // somewhere else.
            colors.Add(Color.white);
            colors.Add(Color.white);
            if (i > 0)
            {
                int b0 = (i - 1) * 2;
                triangles.Add(b0);
                triangles.Add(b0 + 2);
                triangles.Add(b0 + 1);
                triangles.Add(b0 + 1);
                triangles.Add(b0 + 2);
                triangles.Add(b0 + 3);
            }
        }
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void DisposeRuntimeObject(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
