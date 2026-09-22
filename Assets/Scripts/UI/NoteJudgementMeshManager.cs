using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-120)]
public class NoteJudgementMeshManager : MonoBehaviour
{
    private const int DefaultPool = 16;

    public static NoteJudgementMeshManager Instance { get; private set; }

    [Header("Effect Settings")]
    [SerializeField] private int poolSize = DefaultPool;
    [SerializeField] private float duration = 0.35f;
    [SerializeField] private float meshHeight = 1.0f;
    [SerializeField, HideInInspector] private float playerHeightScale = 1f;
    [SerializeField, Min(0.01f), Tooltip("Time for the light column to rush upward from the judgment line.")]
    private float riseDuration = 0.055f;
    [SerializeField, Min(0.01f), Tooltip("Time for the released light column to shrink and fade.")]
    private float releaseDuration = 0.34f;

    [SerializeField, Tooltip("Lay the light on the track instead of firing it up off the surface. Both frames are computed either way; this only picks which one the mesh is drawn in.")]
    private bool layOnTrack = true;

    [SerializeField, Tooltip("A struck note throws a rune burst alongside its light column.")]
    private bool enableRuneBurst = true;

    [SerializeField, Tooltip("A slide shows only its rune burst and wake — no light column. The column is a vertical flash at one lane, and it sits on top of the wake that is the whole point of a slide's feedback.")]
    private bool slideSkipsLightColumn = true;

    [SerializeField, Min(0.01f), Tooltip("Time for a column struck under the sustain pedal to hold its height and fade out. Longer than the normal return, because nothing is travelling: the lingering is the pedal.")]
    private float sustainedReleaseDuration = 0.30f;
    [SerializeField, Min(0.05f), Tooltip("Automatic return delay for Tap/STAC and a failsafe for paths without key-up events.")]
    private float transientMaximumHoldTime = 0.11f;
    [SerializeField, Min(1f), Tooltip("STAC reaches this multiple of the normal light-column height.")]
    private float staccatoHeightMultiplier = 1.5f;
    [SerializeField, Min(1f), Tooltip("Persistent Hold contact glow height relative to the note head.")]
    private float holdContactHeightMultiplier = 1.35f;
    [SerializeField] private float surfaceOffset = 0.01f;
    [SerializeField] private float fallbackWidth = 1.0f;
    [SerializeField]
    [Tooltip("When aligning mesh effects to the judgment line, add this vertical offset (world units).")]
    private float judgmentYOffset = 0.1f;
    [SerializeField]
    [Tooltip("How many sorting order steps above the JudgmentLine the effect should be placed.")]
    private int sortingOrderOffset = 2;
    [SerializeField] private Vector3 rotationOffsetEuler = new Vector3(90f, 0f, 0f);
    [SerializeField, Range(0.05f, 3.0f)] private float widthScale = 0.8f;
    [SerializeField] private float widthPadding = 0.0f;
    [SerializeField] private Vector3 localPositionOffset = Vector3.zero;
    [SerializeField] private bool snapToJudgmentPlane = true;
    [SerializeField] private bool useRendererBoundsForX = true;
    [SerializeField] private bool debugSnap = false;

    [Header("Lane Width Derivation")]
    [SerializeField] private int totalLaneCount = 28;
    [SerializeField] private float trackWidthFallback = 105f;
    [SerializeField] private float laneGapCompensation = 0f;
    [SerializeField] private float laneWidthMultiplier = 1.0f;
    [SerializeField] private float trackWidthSampleInterval = 0.5f;

    [Header("Materials")] 
    [SerializeField] private Material perfectMaterial;
    [SerializeField] private Material greatMaterial;
    [SerializeField] private Material goodMaterial;
    [SerializeField, ColorUsage(true, true), Tooltip("Square mesh colour when the input is earlier than the note (FAST).")]
    private Color fastTimingColor = new Color(0.447f, 0.839f, 1f, 1f); // #72D6FF
    [SerializeField, ColorUsage(true, true), Tooltip("Square mesh colour when the input is later than the note (SLOW).")]
    private Color slowTimingColor = new Color(0.557f, 0.110f, 0.169f, 1f); // #8E1C2B
    [SerializeField, Min(0f), Tooltip("Offsets inside this range keep the normal judgment colour.")]
    private float timingColorDeadZoneMs = 0.5f;
    [SerializeField, Tooltip("Optional override material that supports vertex colors (e.g. Unlit/Transparent or Sprites/Default) for gradient alpha.")]
    private Material vertexColorMaterial;
    private Material premiumGlowMaterial;
    [SerializeField, Range(0f, 1f), Tooltip("Top alpha multiplier for the hit effect gradient (0 = fully transparent at top)."),]
    private float gradientTopAlpha = 0.15f;
    [SerializeField, Tooltip("If disabled, the effect will not fade out over lifetime (only the vertical gradient applies).")]
    private bool enableLifetimeFade = true;

    private readonly List<EffectState> effectPool = new List<EffectState>(DefaultPool);
    private Camera cachedCamera;
    private Transform container;
    private Transform cachedJudgmentLine;
    private bool warnedMissingJudgmentLine;
    private float nextJudgmentLineSearch;
    private const float JudgmentLineSearchInterval = 0.5f;
    [SerializeField] private bool applySettingsOnAwake = true;
    [SerializeField] private bool useResourceFallback = true;
    [SerializeField] private NoteJudgementMeshSettings settingsOverride;

    private NoteSpawner cachedNoteSpawner;
    private float cachedTrackWidth = -1f;
    private float nextTrackWidthSampleTime;

    private enum EffectPhase
    {
        Rising,
        Holding,
        Releasing
    }

    private class EffectState
    {
        public GameObject GameObject;
        public Transform Transform;
        public MeshRenderer Renderer;
        public MaterialPropertyBlock PropertyBlock;
        public Color BaseColor = Color.white;
        public float StartTime;
        public Vector3 StartPosition;
        public float ReleaseStartTime;
        public float ReleaseStartHeight;
        public float CurrentHeight;
        public float TargetHeight;
        public float Width;
        public Vector3 HitOrigin;
        // Growth frame of the light column: its local +Y is the track normal.
        public Quaternion Rotation;
        // Judgment-plane frame handed to particles / decorations, which stay
        // lying on the track exactly where they were authored.
        public Quaternion AnchorRotation;
        public int StartLane;
        public int EndLane;
        public float SignedTimingOffsetMs;
        public bool UseTimingPosition;
        public bool IsStaccato;
        public EffectPhase Phase;
        // Struck while the sustain pedal was down, so its return runs outward
        // instead of back to the line.
        public bool Sustained;
        public bool Busy;
        public bool Persistent;
        public NoteController Note;
        // Cached child renderer of the tracked note — avoids GetComponentInChildren every frame.
        public Renderer NoteRenderer;
        // Pre-computed per-vertex "is top" flags — avoids mesh.vertices copy (Vector3[]) every frame.
        public bool[] VertexIsTop;
        // Reusable Color array for mesh.colors assignment — avoids Color[] allocation every frame.
        public Color[] VertexColors;
        // Dirty flag: persistent effects skip gradient+SetPropertyBlock when color hasn't changed.
        public bool PropertyBlockDirty = true;
        // Whether sorting order (relative to JudgmentLine renderer) has been applied for this effect slot.
        public bool SortingApplied;
    }
    private readonly Dictionary<NoteController, EffectState> activePersistentEffects = new Dictionary<NoteController, EffectState>();
    // Cached JudgmentLine renderer — used once per effect activation for sorting, then not queried again.
    private Renderer _cachedJlRenderer;

    public static NoteJudgementMeshManager EnsureCreated()
    {
        if (Instance != null)
        {
            return Instance;
        }

        // 一定要連**未啟用**的場景實例一起找。
        //
        // main_scene 裡就有一個設定好的 NoteJudgementMeshManager（vertexColorMaterial、
        // settingsOverride、poolSize 都是在編輯器裡調好的），而它掛在 gameplay root
        // 底下——那個東西在遊戲真正開始之前是關著的。
        //
        // FindFirstObjectByType 看不到未啟用的物件，於是這裡會以為場景裡沒有，
        // 就建一個欄位全是預設值的替身出來。等場景那個真的被啟用，它的 Awake
        // 看到 Instance 已經被替身佔住，就會把**自己**銷毀——設定好的那個消失，
        // 留下沒有材質的替身，判定光效因此看起來整個不見。
        //
        // 這個洞本來一直沒發作，因為以前只有打到音符才會呼叫這個函式，那時候
        // 場景實例早就啟用了。把預熱提前到場景載入之後才踩到。
        NoteJudgementMeshManager existing = SceneSingleton.Find<NoteJudgementMeshManager>();
        if (existing != null)
        {
            // 啟用它，Awake 才會跑、池子才會建起來——預熱要的就是這個。
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
            if (Instance != null) return Instance;
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("NoteJudgementMeshManager");
        DontDestroyOnLoad(go);
        return go.AddComponent<NoteJudgementMeshManager>();
    }


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        cachedCamera = Camera.main;
        container = new GameObject("NoteJudgementMeshContainer").transform;
        DontDestroyOnLoad(container.gameObject);

        if (applySettingsOnAwake)
        {
            NoteJudgementMeshSettings settings = null;
            if (settingsOverride != null)
            {
                settings = settingsOverride;
            }
            else if (useResourceFallback)
            {
                settings = NoteJudgementMeshSettings.LoadFromResources();
            }

            if (settings != null)
            {
                ApplySettings(settings);
            }
        }
        if (SettingsManager.Instance != null)
            playerHeightScale = SettingsManager.Instance.JudgmentMeshHeight;

        // Keep a concrete Resources reference so standalone builds cannot strip
        // the premium shader and silently restore the legacy square material.
        var premiumSource = Resources.Load<Material>("Materials/PremiumJudgmentGlow");
        var premiumShader = premiumSource != null
            ? premiumSource.shader
            : Shader.Find("Custom/PremiumJudgmentGlow");
        if (premiumShader != null)
        {
            premiumGlowMaterial = premiumSource != null
                ? new Material(premiumSource)
                : new Material(premiumShader);
            premiumGlowMaterial.name = "Premium Judgment Glow (Runtime)";
            premiumGlowMaterial.renderQueue = 4000;
            premiumGlowMaterial.SetFloat("_GlowIntensity", 5.2f);
            premiumGlowMaterial.SetFloat("_CoreIntensity", 8f);
            premiumGlowMaterial.SetFloat("_EdgeSoftness", 0.2f);
        }
        else
        {
            Debug.LogError("[NoteJudge] PremiumJudgmentGlow is missing; judgment effects would fall back to the legacy square material.");
        }

        for (int i = 0; i < Mathf.Max(1, poolSize); i++)
        {
            effectPool.Add(CreateEffectState("NoteJudgeMesh" + i));
        }
    }

    private void ApplySettings(NoteJudgementMeshSettings settings)
    {
        duration = settings.duration;
        meshHeight = settings.meshHeight;
        surfaceOffset = settings.surfaceOffset;
        fallbackWidth = Mathf.Max(0.01f, settings.fallbackWidth);
        rotationOffsetEuler = settings.rotationOffsetEuler;
        widthScale = Mathf.Clamp(settings.widthScale, 0.05f, 3.0f);
        widthPadding = settings.widthPadding;
        localPositionOffset = settings.localPositionOffset;
        snapToJudgmentPlane = settings.snapToJudgmentPlane;
        totalLaneCount = Mathf.Max(1, settings.totalLaneCount);
        trackWidthFallback = Mathf.Max(0.01f, settings.trackWidthFallback);
        laneGapCompensation = settings.laneGapCompensation;
        laneWidthMultiplier = Mathf.Max(0.0f, settings.laneWidthMultiplier);
        trackWidthSampleInterval = Mathf.Max(0.05f, settings.trackWidthSampleInterval);
        cachedTrackWidth = -1f;
#if UNITY_EDITOR
        if (debugSnap)
        {
            Debug.Log($"[NoteJudge] Applied settings from asset '{settings.name}'");
        }
#endif
    }

    private Transform GetJudgmentLineTransform()
    {
        if (cachedJudgmentLine != null)
        {
            return cachedJudgmentLine;
        }

        if (Time.unscaledTime < nextJudgmentLineSearch)
        {
            return cachedJudgmentLine;
        }

        cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;
        nextJudgmentLineSearch = Time.unscaledTime + JudgmentLineSearchInterval;
        return cachedJudgmentLine;
    }

    private EffectState CreateEffectState(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        var collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        go.transform.SetParent(container, false);
        go.SetActive(false);

        // 確保每個效果都有自己的可變 Mesh，頂點色不會互相覆蓋
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null)
        {
            mf.mesh = CreatePremiumBeamMesh();
            mf.mesh.MarkDynamic();
        }

        var state = new EffectState
        {
            GameObject = go,
            Transform = go.transform,
            Renderer = go.GetComponent<MeshRenderer>(),
            PropertyBlock = new MaterialPropertyBlock(),
            Busy = false
        };

        // 若指定了支援頂點色的材質，套用以啟用漸層透明
        if (vertexColorMaterial != null && state.Renderer != null)
        {
            state.Renderer.sharedMaterial = vertexColorMaterial;
        }

        if (state.Renderer != null)
        {
            state.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            state.Renderer.receiveShadows = false;
        }

        // Pre-compute top/bottom vertex flags once so ApplyVerticalGradient never calls mesh.vertices again.
        if (mf != null && mf.mesh != null)
        {
            var verts = mf.mesh.vertices; // intentional: one-time allocation at pool creation
            state.VertexIsTop = new bool[verts.Length];
            state.VertexColors = new Color[verts.Length];
            float maxY = float.MinValue;
            for (int vi = 0; vi < verts.Length; vi++)
                maxY = Mathf.Max(maxY, verts[vi].y);
            for (int vi = 0; vi < verts.Length; vi++)
                state.VertexIsTop[vi] = verts[vi].y >= maxY - 0.001f;
        }

        return state;
    }

    /// <summary>Applies the player-facing MESH height without rebuilding the pool.</summary>
    public void SetMeshHeight(float height)
    {
        playerHeightScale = Mathf.Clamp(height, 0.25f, 4f);
        for (int i = 0; i < effectPool.Count; i++)
        {
            EffectState state = effectPool[i];
            if (state == null || !state.Busy) continue;
            float target = ResolveEffectHeight(state.Note, state.Persistent);
            if (state.IsStaccato) target *= Mathf.Max(1f, staccatoHeightMultiplier);
            state.TargetHeight = Mathf.Max(0.01f, target);
            if (state.Phase == EffectPhase.Holding)
                state.CurrentHeight = state.TargetHeight;
        }
    }

    private static Mesh CreatePremiumBeamMesh()
    {
        // Preserve the original straight rectangular silhouette. The upgraded
        // appearance comes from the white/gold core shader, not tapered geometry.
        var mesh = new Mesh { name = "Premium Judgment Light Column" };
        mesh.vertices = new[]
        {
            new Vector3(-0.50f, -0.50f, 0f), new Vector3(0.50f, -0.50f, 0f),
            new Vector3(-0.50f, -0.30f, 0f), new Vector3(0.50f, -0.30f, 0f),
            new Vector3(-0.50f,  0.10f, 0f), new Vector3(0.50f,  0.10f, 0f),
            new Vector3(-0.50f,  0.50f, 0f), new Vector3(0.50f,  0.50f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),    new Vector2(1f, 0f),
            new Vector2(0f, 0.20f), new Vector2(1f, 0.20f),
            new Vector2(0f, 0.60f), new Vector2(1f, 0.60f),
            new Vector2(0f, 1f),    new Vector2(1f, 1f)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 1, 2, 3,
            2, 4, 3, 3, 4, 5,
            4, 6, 5, 5, 6, 7
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private EffectState AcquireState()
    {
        for (int i = 0; i < effectPool.Count; i++)
        {
            if (!effectPool[i].Busy)
            {
                return effectPool[i];
            }
        }

        var created = CreateEffectState("NoteJudgeMeshExtra" + effectPool.Count);
        effectPool.Add(created);
        return created;
    }

    private void ReleaseState(EffectState state, bool stopNoteParticles = true)
    {
        if (state == null) return;
        if (state.Persistent && state.Note != null)
        {
            if (stopNoteParticles)
            {
                try { state.Note.StopJudgmentLineFanParticles(); } catch { }
            }
            activePersistentEffects.Remove(state.Note);
        }
        state.Persistent = false;
        state.Note = null;
        state.NoteRenderer = null;
        state.PropertyBlockDirty = true;
        state.SortingApplied = false;
        state.CurrentHeight = 0f;
        state.TargetHeight = 0f;
        state.Busy = false;
        if (state.GameObject != null)
        {
            state.GameObject.SetActive(false);
        }
    }

    /// <param name="overrideColor">
    /// Replaces the judgment's own colour. Recital mode's centre hits come
    /// through here: the light is the loudest thing on a hit, so a different
    /// colour on it is how "you took that one on the middle key" gets said
    /// without a word or an extra effect.
    /// </param>
    public void ShowEffect(NoteController note, JudgmentResult result, bool persistent = false,
        float signedTimingOffsetMs = 0f, bool useTimingColor = false, int inputLane = -1,
        Color? overrideColor = null)
    {
        if (!TryGetMaterial(result, out var material, out var baseColor))
        {
            if (debugSnap)
            {
                Debug.LogWarning($"[NoteJudge] ShowEffect: no material for result={result} (note={(note!=null?note.name:"null")}), effect skipped");
            }
            return;
        }

        // FAST/SLOW are only meaningful after leaving the JUST/Perfect window.
        // Small offsets that still earned Perfect retain the normal judgment colour.
        if (useTimingColor && result != JudgmentResult.Perfect)
        {
            if (signedTimingOffsetMs < -timingColorDeadZoneMs)
            {
                baseColor = fastTimingColor;
            }
            else if (signedTimingOffsetMs > timingColorDeadZoneMs)
            {
                baseColor = slowTimingColor;
            }
        }

        if (overrideColor.HasValue) baseColor = overrideColor.Value;

        // STAC is a taller tap flash, never a sustained Hold contact light.
        bool isStaccato = note != null && note.IsStaccato;
        persistent = persistent && !isStaccato;

        // Everything that is not a Hold throws runes instead of a light column.
        // The column's height depended on how long the key stayed down, whether
        // the pedal was in, and whether the note was a STAC, so the same
        // judgment never drew the same shape twice. Runes rise a fixed height.
        // A Hold keeps the column: there the sustained vertical is showing that
        // contact is still being held, which is worth drawing.
        // Only a note that was actually struck. TryGetMaterial above does not
        // filter on the result -- it hands back the shared vertex-colour
        // material whatever happened -- so Miss and Fail reach here too, and a
        // missed note has already scrolled past the line. Their bursts were the
        // runes appearing away from the judgment line.
        bool struck = result == JudgmentResult.Perfect ||
                      result == JudgmentResult.Great ||
                      result == JudgmentResult.Good;

        // The burst sits on top of the light column rather than replacing it,
        // so this falls through to the mesh either way. A Hold gets one only
        // when its column is first placed: the refresh path below re-enters
        // here to keep a held note lit, and firing on each of those would leave
        // a Hold spraying runes for as long as it is held.
        if (enableRuneBurst && struck && note != null &&
            (!persistent || !activePersistentEffects.ContainsKey(note)))
        {
            PlayRuneBurst(note, inputLane);
        }

        // A slide's feedback is the wake it leaves along the lane it crossed.
        // The light column is a vertical flash at a single lane, drawn on top
        // of exactly the runes that carry the direction, so for a slide it is
        // the thing in the way rather than the thing being read.
        if (slideSkipsLightColumn && !persistent && IsSlideNote(note)) return;

        if (persistent && note != null)
        {
            if (activePersistentEffects.TryGetValue(note, out var existing))
            {
                // This is a visual refresh of the same held note. Keep its
                // judgment-line particle stream alive without a second burst.
                ReleaseState(existing, false);
            }
        }

        var state = AcquireState();
        if (state.Renderer == null || state.Transform == null)
        {
            return;
        }

        state.Renderer.sharedMaterial = material;
        state.BaseColor = baseColor;
        state.PropertyBlockDirty = true;
        state.StartTime = Time.unscaledTime;
        state.Busy = true;
        state.Persistent = persistent;
        state.IsStaccato = isStaccato;
        state.Phase = EffectPhase.Rising;
        state.Sustained = PedalNoteRenderer.IsPedalDownNow();
        state.CurrentHeight = 0.01f;
        // Keep the source for the short-lived burst as well. Particle systems
        // query this state in the judgment frame to inherit the exact FAST/SLOW
        // depth of the MESH.
        state.Note = note;
        state.SignedTimingOffsetMs = signedTimingOffsetMs;
        state.UseTimingPosition = useTimingColor;
        int noteStartLane = note != null && note.NoteData != null ? note.NoteData.startLane : -1;
        int noteEndLane = note != null && note.NoteData != null && note.NoteData.endLane >= noteStartLane
            ? note.NoteData.endLane
            : noteStartLane;
        state.StartLane = noteStartLane;
        state.EndLane = noteEndLane;
        // Cache the child renderer once; live Hold pose updates reuse it every frame.
        state.NoteRenderer = (persistent && note != null)
            ? note.GetComponentInChildren<Renderer>()
            : null;

        if (debugSnap)
        {
            var jl = GetJudgmentLineTransform();
            Debug.Log($"[NoteJudge] ShowEffect note={note?.name} persistent={persistent} jl={(jl!=null?jl.position.ToString():"null")} meshHeight={meshHeight} useRendererBoundsForX={useRendererBoundsForX}");
        }

        ApplyVerticalGradient(state, 1f); // 初始化頂到底的透明漸層（ShowEffect 時執行一次）
        if (state.Renderer != null && state.PropertyBlock != null)
        {
            state.PropertyBlock.SetColor("_Color", state.BaseColor);
            state.PropertyBlock.SetFloat("_ShimmerStrength", 0.16f);
            state.Renderer.SetPropertyBlock(state.PropertyBlock);
        }
        state.PropertyBlockDirty = false;
        RefreshTransformForNote(state, note);
        ApplyAnimatedTransform(state);
        state.GameObject.SetActive(true);
        if (persistent && note != null)
        {
            activePersistentEffects[note] = state;
            // Some judgment paths create the persistent mesh without routing a
            // separate decorative hit event. Starting here makes the Hold magic
            // circle part of the persistent effect contract instead of an optional
            // side effect of one input implementation.
            int lane = inputLane >= 0
                ? inputLane
                : (note.NoteData != null ? note.NoteData.startLane : -1);
            try { HitEffectRouter.BeginHold(note, lane); } catch { }
        }
    }

    /// <summary>
    /// The point on the judgment line, in this note's lane, that a hit effect
    /// should be drawn at.
    /// </summary>
    /// <remarks>
    /// The line's own transform supplies the height and the depth; only the
    /// lane position comes from the note. Nothing is unwound to get there.
    ///
    /// Every other position this class can produce is shaped for the light
    /// column and carries offsets that a round seal centred on the line must
    /// not inherit: <see cref="TryComputeEffectTransform"/> returns the
    /// column's centre, which is <c>meshHeight * 0.5</c> plus
    /// <c>judgmentYOffset</c>, <c>surfaceOffset</c> and
    /// <c>localPositionOffset</c> above the line, and it displaces the whole
    /// thing up the lane by how early the input was when
    /// TimingSensitiveJudgmentVisuals is on. <see cref="TryGetHitOrigin"/> only
    /// looks like it backs the first of those out -- it subtracts along the
    /// anchor's local -Y, and the anchor carries rotationOffsetEuler (90,0,0),
    /// so the correction lands along the track in Z while the offset it means
    /// to cancel was added in world Y.
    ///
    /// Subtracting them one by one was the previous attempt and it kept leaving
    /// a residue, because the list is not closed: anything added to that
    /// composition later would silently reappear here. Reading the line is.
    /// </remarks>
    public bool TryGetJudgmentLineOrigin(NoteController note, out Vector3 position,
        out float width, int inputLane = -1)
    {
        if (!TryComputeEffectTransform(note, null, false, 0f,
                out position, out _, out _, out width))
        {
            return false;
        }

        Transform judgmentLine = snapToJudgmentPlane ? GetJudgmentLineTransform() : null;
        if (judgmentLine == null)
        {
            // The lookup is GameObject.Find("JudgmentLine"). Rename that object
            // and every effect quietly falls back to the note's own position,
            // which looks exactly like the effect failing to stay on the line.
            // Said once so it is diagnosable instead of invisible.
            if (snapToJudgmentPlane && !warnedMissingJudgmentLine)
            {
                warnedMissingJudgmentLine = true;
                try
                {
                    BuildLogger.LogWarning("[NoteJudge] No GameObject named \"JudgmentLine\" in the " +
                        "scene. Hit effects are falling back to each note's own position, so they " +
                        "will sit wherever the note was struck instead of on the line.");
                }
                catch { }
            }
            return true;
        }

        Vector3 linePosition = judgmentLine.position;
        position.y = linePosition.y;
        position.z = linePosition.z;
        return true;
    }

    private void PlayRuneBurst(NoteController note, int inputLane)
    {
        if (note == null) return;
        if (!TryGetJudgmentLineOrigin(note, out Vector3 position, out float width, inputLane)) return;

        bool rightHand = ResolveIsRightHand(note, inputLane);
        RuneBurstEmitter emitter = RuneBurstEmitter.EnsureCreated();
        emitter.Play(position, width, rightHand);

        // A slide additionally throws a wake back along the lane it crossed.
        // The direction comes from the note rather than from the input, because
        // the lane the player happened to press is anywhere inside the slide's
        // contact range and says nothing about which way the note runs.
        if (TryGetSlideDirection(note, out float slideDirection))
        {
            emitter.PlaySlideWake(position, width, rightHand, slideDirection);
        }
    }

    private static bool IsSlideNote(NoteController note)
    {
        try
        {
            return note != null && note.IsSlide;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The direction a slide travels: positive toward higher lanes.
    /// </summary>
    private static bool TryGetSlideDirection(NoteController note, out float direction)
    {
        direction = 0f;
        try
        {
            if (note == null || !note.IsSlide) return false;
            var data = note.NoteData;
            if (data == null) return false;
            int span = data.endLane - data.startLane;
            if (span == 0) return false;
            direction = Mathf.Sign(span);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the hand from the note's own data, the way the note picks its
    /// art. The lane index is only a fallback, because it disagrees with the
    /// hand the moment a passage crosses them.
    /// </summary>
    private bool ResolveIsRightHand(NoteController note, int inputLane)
    {
        try
        {
            if (note != null && note.NoteData != null) return note.NoteData.hand == 0;
        }
        catch { }

        if (inputLane < 0) return false;
        return inputLane >= Mathf.Max(1, totalLaneCount / 2);
    }

    public void HidePersistentEffect(NoteController note)
    {
        if (note == null) return;
        try { note.StopJudgmentLineFanParticles(); } catch { }
        try { HitEffectRouter.StopHold(note); } catch { }
        if (activePersistentEffects.TryGetValue(note, out var state))
        {
            BeginRelease(state);
        }
    }

    /// <summary>
    /// Releases transient columns whose judged note covers this input lane. Hold
    /// contact columns are released separately when the final held lane is lifted.
    /// </summary>
    public void ReleaseEffectsForLane(int lane, bool includePersistent = false)
    {
        for (int i = 0; i < effectPool.Count; i++)
        {
            var state = effectPool[i];
            if (state == null || !state.Busy || state.Phase == EffectPhase.Releasing) continue;
            if (!includePersistent && state.Persistent) continue;
            if (lane < state.StartLane || lane > state.EndLane) continue;
            BeginRelease(state);
        }
    }

    private void BeginRelease(EffectState state)
    {
        if (state == null || !state.Busy || state.Phase == EffectPhase.Releasing) return;

        if (state.Persistent && state.Note != null)
        {
            try { state.Note.StopJudgmentLineFanParticles(); } catch { }
            activePersistentEffects.Remove(state.Note);
        }

        state.Persistent = false;
        state.Note = null;
        state.NoteRenderer = null;
        state.SignedTimingOffsetMs = 0f;
        state.UseTimingPosition = false;
        state.ReleaseStartHeight = Mathf.Max(0.01f, state.CurrentHeight);
        state.ReleaseStartTime = Time.unscaledTime;
        state.Phase = EffectPhase.Releasing;
    }

    // Set persistent head 'lit' state for a note. When lit==true, ensure the persistent
    // effect is shown with the normal judgment color (Perfect). When lit==false,
    // dim the existing persistent effect (keep visible) so the player can see the head
    // while within recovery/grace windows.
    public void SetPersistentLit(NoteController note, bool lit)
    {
        if (note == null) return;
        try
        {
            if (lit)
            {
                // Show or refresh persistent effect using Perfect appearance
                ShowEffect(note, JudgmentResult.Perfect, true);
                return;
            }

            // Dim persistent effect rather than fully hiding it so players can recover.
            if (activePersistentEffects.TryGetValue(note, out var state))
            {
                Color baseColor = state.BaseColor;
                // reduce brightness and alpha to indicate unlit state
                baseColor.r *= 0.45f;
                baseColor.g *= 0.45f;
                baseColor.b *= 0.45f;
                baseColor.a *= 0.6f;
                state.BaseColor = baseColor;
                state.PropertyBlockDirty = true; // Update() will re-apply gradient & PropertyBlock
            }
            else
            {
                // If no persistent exists yet, create a dimmed persistent effect
                ShowEffect(note, JudgmentResult.Good, true);
                if (activePersistentEffects.TryGetValue(note, out var st2))
                {
                    var bc = st2.BaseColor;
                    bc.r *= 0.45f; bc.g *= 0.45f; bc.b *= 0.45f; bc.a *= 0.6f;
                    st2.BaseColor = bc;
                    st2.PropertyBlockDirty = true; // Update() will re-apply gradient & PropertyBlock
                }
            }
        }
        catch { }
    }

    public bool TryGetWorldAnchor(NoteController note, out Vector3 position, out Quaternion rotation, out float width,
        int inputLane = -1)
    {
        return TryComputeEffectTransform(note, null, false, 0f, out position, out rotation, out _, out width);
    }

    /// <summary>
    /// Returns the judgment-line end of the tall gradient mesh. Hit bursts must
    /// originate here rather than at the gradient mesh centre.
    /// </summary>
    public bool TryGetHitOrigin(NoteController note, out Vector3 position, out Quaternion rotation, out float width,
        int inputLane = -1)
    {
        // The particle router calls this immediately after ShowEffect. Reuse
        // that exact timing-space origin so the burst and MESH never separate.
        EffectState latest = null;
        for (int i = 0; i < effectPool.Count; i++)
        {
            EffectState candidate = effectPool[i];
            if (candidate == null || !candidate.Busy || candidate.Note != note) continue;
            if (latest == null || candidate.StartTime > latest.StartTime) latest = candidate;
        }
        if (latest != null && Time.unscaledTime - latest.StartTime <= 0.05f)
        {
            position = latest.HitOrigin;
            rotation = latest.AnchorRotation;
            width = latest.Width;
            ApplyCameraPull(ref position);
            return true;
        }

        bool resolved = TryComputeEffectTransform(note, null, false, 0f,
            out position, out rotation, out _, out width);
        if (!resolved) return false;

        // The pooled judgment quad is meshHeight units tall and centred on its
        // transform. Its negative local-Y edge is the end touching the line.
        position += rotation * new Vector3(0f, -Mathf.Max(0.01f, meshHeight) * 0.5f, 0f);

        // Pull the flash a tiny amount toward the camera to prevent z-fighting
        // without visibly detaching it from the judgment plane.
        ApplyCameraPull(ref position);

        return true;
    }

    private void ApplyCameraPull(ref Vector3 position)
    {
        var cam = cachedCamera != null ? cachedCamera : Camera.main;
        if (cam != null)
        {
            cachedCamera = cam;
            Vector3 towardCamera = cam.transform.position - position;
            if (towardCamera.sqrMagnitude > 0.0001f)
            {
                position += towardCamera.normalized * 0.015f;
            }
        }
    }

    private void RefreshTransformForNote(EffectState state, NoteController note)
    {
        Vector3 position;
        Quaternion rotation;
        Quaternion beamRotation;
        float width;
        TryComputeEffectTransform(note, state != null ? state.NoteRenderer : null,
            state != null && state.UseTimingPosition,
            state != null ? state.SignedTimingOffsetMs : 0f,
            out position, out rotation, out beamRotation, out width);

        float effectHeight = ResolveEffectHeight(note, state != null && state.Persistent);
        if (state.IsStaccato)
        {
            effectHeight *= Mathf.Max(1f, staccatoHeightMultiplier);
        }
        // Preserve the judgment-line endpoint while changing height. The old
        // authored meshHeight only defines where that endpoint is located.
        Vector3 hitOrigin = position + rotation * new Vector3(0f, -Mathf.Max(0.01f, meshHeight) * 0.5f, 0f);
        state.HitOrigin = hitOrigin;
        state.AnchorRotation = rotation;
        // The column rises out of the judgment line along the track normal.
        state.Rotation = beamRotation;
        state.Width = width;
        state.TargetHeight = Mathf.Max(0.01f, effectHeight);

        if (note != null && note.transform != null)
        {
            var jlTransform = snapToJudgmentPlane ? GetJudgmentLineTransform() : null;
            if (jlTransform != null && !state.SortingApplied)
            {
                if (_cachedJlRenderer == null) _cachedJlRenderer = jlTransform.GetComponent<Renderer>();
                if (_cachedJlRenderer != null && state.Renderer != null)
                {
                    state.Renderer.sortingLayerID = _cachedJlRenderer.sortingLayerID;
                    state.Renderer.sortingOrder = Mathf.Max(state.Renderer.sortingOrder, _cachedJlRenderer.sortingOrder + sortingOrderOffset);
                }

                // The held contact core is the one intentional foreground layer:
                // it must cover the note head exactly where it touches the line.
                if (state.Persistent && state.NoteRenderer != null && state.Renderer != null)
                {
                    state.Renderer.sortingLayerID = state.NoteRenderer.sortingLayerID;
                    state.Renderer.sortingOrder = Mathf.Max(state.Renderer.sortingOrder,
                        state.NoteRenderer.sortingOrder + 3);
                }
                state.SortingApplied = true;
            }
        }
    }

    private void ApplyAnimatedTransform(EffectState state)
    {
        // Both frames were already being computed: AnchorRotation lies in the judgment
        // plane, which is where the flat note sprites and every anchored decoration
        // live, and Rotation fires off that surface along the track normal. Laying the
        // light down is a choice between the two rather than new geometry — and it is
        // what makes the sustained stretch run away *along* the track, which is the
        // direction the pedal is actually lengthening the note in.
        Quaternion frame = layOnTrack ? state.AnchorRotation : state.Rotation;
        float height = Mathf.Max(0.001f, state.CurrentHeight);
        state.StartPosition = state.HitOrigin + frame * new Vector3(0f, height * 0.5f, 0f);
        state.Transform.position = state.StartPosition;
        state.Transform.rotation = frame;
        state.Transform.localScale = new Vector3(state.Width, height, 1f);
    }

    private float ResolveEffectHeight(NoteController note, bool persistent)
    {
        if (note == null) return Mathf.Max(0.01f, meshHeight * playerHeightScale);

        float noteHeight = 1f;
        try
        {
            noteHeight = Mathf.Max(0.05f, note.GetCurrentWorldHeight());
        }
        catch { }

        // Persistent Hold light is a compact contact core on the judgment line.
        // It covers the note head without stretching along the remaining tail.
        if (persistent)
        {
            return Mathf.Max(Mathf.Max(0.05f, meshHeight),
                noteHeight * Mathf.Max(1f, holdContactHeightMultiplier)) * playerHeightScale;
        }

        // Every note type gets the same authored beam height. A Hold used to add
        // its remaining tail length here, which made sense while the beam lay
        // flat along the track and enclosed the tail — now that it fires out of
        // the track surface, that only turned a held note into a tower that sank
        // as the hold played out.
        float headClearance = noteHeight * 1.3f;
        return Mathf.Max(Mathf.Max(0.05f, meshHeight), headClearance) * playerHeightScale;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (premiumGlowMaterial != null)
        {
            Destroy(premiumGlowMaterial);
            premiumGlowMaterial = null;
        }
    }

    private bool TryComputeEffectTransform(NoteController note, Renderer noteRendererOverride,
        bool useTimingPosition, float signedTimingOffsetMs,
        out Vector3 position, out Quaternion rotation, out Quaternion beamRotation, out float finalWidth)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        beamRotation = Quaternion.identity;
        float width = fallbackWidth;
        Quaternion baseRotation = Quaternion.identity;

        if (note != null && note.transform != null)
        {
            var noteTransform = note.transform;
            position = noteTransform.position;
            width = ResolveWidth(note);

            var jlTransform = snapToJudgmentPlane ? GetJudgmentLineTransform() : null;
            if (jlTransform != null)
            {
                float x = noteTransform.position.x;
                if (useRendererBoundsForX)
                {
                    var rend = noteRendererOverride;
                    if (rend == null)
                    {
                        try { rend = note.GetComponentInChildren<Renderer>(); } catch { }
                    }
                    if (rend != null) x = rend.bounds.center.x;
                }

                float jlY = jlTransform.position.y + judgmentYOffset + surfaceOffset + (meshHeight * 0.5f);
                float effectZ = jlTransform.position.z;
                bool timingPositionEnabled = useTimingPosition;
                try
                {
                    var settings = SettingsManager.Instance;
                    timingPositionEnabled = useTimingPosition &&
                        (settings == null || settings.TimingSensitiveJudgmentVisuals);
                }
                catch { }
                if (timingPositionEnabled)
                {
                    float scrollSpeed = 30f;
                    try
                    {
                        var spawner = GetCachedNoteSpawner();
                        if (spawner != null) scrollSpeed = Mathf.Max(0.01f, spawner.speed);
                    }
                    catch { }
                    // FAST (negative offset) remains above the judgment line;
                    // SLOW (positive offset) appears below it.
                    effectZ -= (signedTimingOffsetMs / 1000f) * scrollSpeed;
                }
                position = new Vector3(x + localPositionOffset.x, jlY + localPositionOffset.y,
                    effectZ + localPositionOffset.z);
                baseRotation = jlTransform.rotation;

                if (debugSnap)
                {
                    Debug.Log($"[NoteJudge] Aligning note={note?.name} x={x} jlY={jlTransform.position.y} meshCenterY={position.y} meshHeight={meshHeight} pos={position}");
                }
            }
            else
            {
                baseRotation = noteTransform.rotation;
                position += SafeTransformVector(noteTransform, Vector3.up * surfaceOffset);
                position += SafeTransformVector(noteTransform, localPositionOffset);
            }
        }
        else
        {
            var cam = cachedCamera != null ? cachedCamera : Camera.main;
            if (cam == null)
            {
                cachedCamera = Camera.main;
                cam = cachedCamera;
            }
            if (cam != null)
            {
                baseRotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                position = cam.transform.position + cam.transform.forward * 2f;
            }
        }

        // The judgment-plane frame keeps every anchored decoration lying on the
        // track, matching the flat note sprites.
        rotation = baseRotation * Quaternion.Euler(rotationOffsetEuler);
        // The light column leaves that surface instead: its growth axis
        // (local +Y) is the track normal, so it fires perpendicular to the lane
        // while local X stays on the lane axis and keeps the beam in its lane.
        beamRotation = baseRotation;
        finalWidth = Mathf.Max(0.01f, (width * widthScale) + widthPadding);
        return true;
    }

    private float ResolveWidth(NoteController note)
    {
        if (note == null)
        {
            return Mathf.Max(0.01f, fallbackWidth);
        }

        // Match the width the player actually sees.  The former lane-derived
        // value ignored sprite/world scaling, so a configured 0.8 could still
        // appear exactly as wide as the rendered note.
        try
        {
            float renderedWidth = note.GetCurrentWorldWidth();
            if (renderedWidth > 0.001f)
            {
                return renderedWidth;
            }
        }
        catch { }

        float laneWidthEstimate = ResolveLaneSpanWidth(note);
        if (laneWidthEstimate > 0.001f)
        {
            return laneWidthEstimate;
        }

        try
        {
            var renderer = note.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                float width = renderer.bounds.size.x;
                if (width > 0.001f)
                {
                    return width;
                }
            }
        }
        catch { }

        try
        {
            var spriteRenderer = note.GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                float width = spriteRenderer.bounds.size.x;
                if (width > 0.001f)
                {
                    return width;
                }
            }
        }
        catch { }

        return Mathf.Max(0.01f, fallbackWidth);
    }

    private float ResolveLaneSpanWidth(NoteController note)
    {
        if (note == null) return -1f;
        var data = note.NoteData;
        if (data == null) return -1f;

        float trackWidth = GetTrackWidth(note);
        float width = PianoVisualLayout.ResolveVisualWidth(data, trackWidth) * Mathf.Max(0f, laneWidthMultiplier);
        if (Mathf.Abs(laneGapCompensation) > 0.0001f)
        {
            width -= laneGapCompensation;
        }
        return Mathf.Max(0.01f, width);
    }

    private float GetTrackWidth(NoteController note)
    {
        float now = Time.unscaledTime;
        if (cachedTrackWidth > 0f && now < nextTrackWidthSampleTime)
        {
            return cachedTrackWidth;
        }

        float width = -1f;

        try
        {
            var track = note != null ? note.TrackTransform : null;
            if (track != null)
            {
                var renderer = track.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    width = renderer.bounds.size.x;
                }
                else
                {
                    float inferred = Mathf.Abs(track.lossyScale.x);
                    if (inferred > 0.001f)
                    {
                        width = inferred * 100f;
                    }
                }
            }
        }
        catch { width = -1f; }

        if (width <= 0f)
        {
            var spawner = GetCachedNoteSpawner();
            if (spawner != null && spawner.trackTransform != null)
            {
                try
                {
                    var renderer = spawner.trackTransform.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        width = renderer.bounds.size.x;
                    }
                    else
                    {
                        float inferred = Mathf.Abs(spawner.trackTransform.lossyScale.x);
                        if (inferred > 0.001f)
                        {
                            width = inferred * 100f;
                        }
                    }
                }
                catch { width = -1f; }
            }
        }

        if (width <= 0f)
        {
            width = Mathf.Max(0.01f, trackWidthFallback);
        }

        cachedTrackWidth = width;
        nextTrackWidthSampleTime = now + Mathf.Max(0.05f, trackWidthSampleInterval);
        return cachedTrackWidth;
    }

    private NoteSpawner GetCachedNoteSpawner()
    {
        if (cachedNoteSpawner != null && cachedNoteSpawner.isActiveAndEnabled)
        {
            return cachedNoteSpawner;
        }

        cachedNoteSpawner = FindFirstObjectByType<NoteSpawner>();
        return cachedNoteSpawner;
    }

    private Vector3 SafeTransformVector(Transform reference, Vector3 vector)
    {
        if (reference == null) return vector;
        try
        {
            return reference.TransformVector(vector);
        }
        catch
        {
            return vector;
        }
    }

    private bool TryGetMaterial(JudgmentResult result, out Material material, out Color baseColor)
    {
        // 首選統一的頂點色材質
        Material pick = premiumGlowMaterial != null ? premiumGlowMaterial : vertexColorMaterial;
        Color color = Color.white;

        // 從對應判定材質取得顏色，若沒設再用白色
        Material source = null;
        switch (result)
        {
            case JudgmentResult.Perfect:
                source = perfectMaterial;
                break;
            case JudgmentResult.Great:
                source = greatMaterial;
                break;
            case JudgmentResult.Good:
                source = goodMaterial;
                break;
            default:
                source = null;
                break;
        }

        if (source != null && source.HasProperty("_Color"))
        {
            color = source.color;
        }

        if (source != null && source.HasProperty("_EmissionColor"))
        {
            Color emission = source.GetColor("_EmissionColor");
            float peak = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
            if (peak > 0.001f)
            {
                Color emissionHue = new Color(emission.r / peak, emission.g / peak, emission.b / peak, color.a);
                color = Color.Lerp(color, emissionHue, 0.82f);
            }
        }

        // 若未指定統一材質，退回原本材質（保持舊行為）
        if (pick == null)
        {
            pick = source;
        }

        material = pick;
        baseColor = color;
        return material != null;
    }

    private void ApplyVerticalGradient(EffectState state, float alphaMultiplier)
    {
        if (state == null || state.Renderer == null) return;
        var mf = state.GameObject != null ? state.GameObject.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.mesh == null) return;
        var mesh = mf.mesh;

        // Reuse pre-allocated Color array (avoids new Color[] every frame).
        var cols = state.VertexColors;
        if (cols == null || cols.Length != mesh.vertexCount)
        {
            cols = new Color[mesh.vertexCount];
            state.VertexColors = cols;
        }

        float bottomA = Mathf.Clamp01(state.BaseColor.a * alphaMultiplier);
        float topA    = Mathf.Clamp01(state.BaseColor.a * gradientTopAlpha * alphaMultiplier);
        Color bottom  = new Color(state.BaseColor.r, state.BaseColor.g, state.BaseColor.b, bottomA);
        Color top     = new Color(state.BaseColor.r, state.BaseColor.g, state.BaseColor.b, topA);

        // Use pre-computed top/bottom flags — avoids mesh.vertices managed array copy every call.
        var isTop = state.VertexIsTop;
        if (isTop != null && isTop.Length == mesh.vertexCount)
        {
            for (int i = 0; i < mesh.vertexCount; i++)
                cols[i] = isTop[i] ? top : bottom;
        }
        else
        {
            // Fallback (first call or vertex count changed): read vertices once and cache flags.
            var verts = mesh.vertices;
            state.VertexIsTop = new bool[verts.Length];
            if (state.VertexColors == null || state.VertexColors.Length != verts.Length)
            {
                cols = new Color[verts.Length];
                state.VertexColors = cols;
            }
            for (int i = 0; i < verts.Length; i++)
            {
                state.VertexIsTop[i] = verts[i].y >= 0f;
                cols[i] = state.VertexIsTop[i] ? top : bottom;
            }
        }
        mesh.colors = cols;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < effectPool.Count; i++)
        {
            var state = effectPool[i];
            if (!state.Busy || state.GameObject == null) continue;

            if (state.Persistent && state.Phase != EffectPhase.Releasing)
            {
                // Release if note is destroyed (fake-null) OR its GameObject is no
                // longer active in hierarchy (pooled/recycled) OR note was judged.
                bool noteGone = state.Note == null
                    || !state.Note.gameObject.activeInHierarchy
                    || state.Note.IsJudged;
                if (noteGone)
                {
                    BeginRelease(state);
                }
                else
                {
                    RefreshTransformForNote(state, state.Note);
                }
                // 持續效果：只在顏色變更（PropertyBlockDirty）時才重算 gradient 與 SetPropertyBlock。
                // 每幀都更新位置（ApplyTransformForNote）但不重算色彩，減少 mesh.vertices 分配與 SetPropertyBlock 開銷。
            }

            float alpha = 1f;
            float shimmerStrength = 0.16f;
            if (state.Phase == EffectPhase.Rising)
            {
                float t = Mathf.Clamp01((now - state.StartTime) / Mathf.Max(0.01f, riseDuration));
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                state.CurrentHeight = Mathf.Lerp(0.01f, state.TargetHeight, eased);
                if (t >= 1f)
                {
                    state.CurrentHeight = state.TargetHeight;
                    if (state.Persistent)
                    {
                        state.Phase = EffectPhase.Holding;
                    }
                    else
                    {
                        // Tap/STAC immediately flow back after touching their peak.
                        // Only a sustained Hold is allowed to remain at max height.
                        BeginRelease(state);
                    }
                }
            }
            else if (state.Phase == EffectPhase.Holding)
            {
                state.CurrentHeight = state.TargetHeight;
                if (!state.Persistent && now - state.StartTime >= transientMaximumHoldTime)
                {
                    BeginRelease(state);
                }
            }

            // 縱向漸層 + 可選的存活期淡出
            if (state.Phase == EffectPhase.Releasing)
            {
                float duration = state.Sustained ? sustainedReleaseDuration : releaseDuration;
                float t = Mathf.Clamp01((now - state.ReleaseStartTime) / Mathf.Max(0.01f, duration));
                // Accelerating return: it begins gently at the peak and keeps
                // gaining speed. Under the sustain pedal it does not travel at
                // all -- the pedal now reads as the column lingering at its
                // height, not as a length that changes.
                float eased = t * t;
                // Under the pedal the column holds the height it reached and
                // simply goes out. It used to grow to a multiple of that height
                // instead, so the same note drew a different length depending on
                // the pedal -- the elongation this replaces.
                state.CurrentHeight = state.Sustained
                    ? state.ReleaseStartHeight
                    : Mathf.Lerp(state.ReleaseStartHeight, 0.01f, eased);
                // Keep the body luminous while its upper edge flows back toward
                // the line; the brighter moving caustic gives a liquid return.
                alpha = 1f - Mathf.Pow(t, 1.35f);
                shimmerStrength = Mathf.Lerp(0.34f, 0.12f, eased);
                if (t >= 1f)
                {
                    ReleaseState(state);
                    continue;
                }
            }

            ApplyAnimatedTransform(state);
            ApplyVerticalGradient(state, alpha);
            if (state.Renderer != null && state.PropertyBlock != null)
            {
                var c = state.BaseColor;
                c.a *= alpha;
                state.PropertyBlock.SetColor("_Color", c);
                state.PropertyBlock.SetFloat("_ShimmerStrength", shimmerStrength);
                state.Renderer.SetPropertyBlock(state.PropertyBlock);
            }
            state.PropertyBlockDirty = false;
        }
    }

    public void HideAllEffects()
    {
        var persistentStates = new List<EffectState>(activePersistentEffects.Values);
        for (int i = 0; i < persistentStates.Count; i++)
        {
            ReleaseState(persistentStates[i]);
        }
        activePersistentEffects.Clear();

        for (int i = 0; i < effectPool.Count; i++)
        {
            var state = effectPool[i];
            if (state == null) continue;
            if (state.Busy)
            {
                ReleaseState(state);
            }
            else if (state.GameObject != null)
            {
                state.GameObject.SetActive(false);
            }
        }
    }
}
