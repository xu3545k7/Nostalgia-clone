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
    [SerializeField, Tooltip("Optional override material that supports vertex colors (e.g. Unlit/Transparent or Sprites/Default) for gradient alpha.")]
    private Material vertexColorMaterial;
    [SerializeField, Range(0f, 1f), Tooltip("Top alpha multiplier for the hit effect gradient (0 = fully transparent at top)."),]
    private float gradientTopAlpha = 0.15f;
    [SerializeField, Tooltip("If disabled, the effect will not fade out over lifetime (only the vertical gradient applies).")]
    private bool enableLifetimeFade = true;

    private readonly List<EffectState> effectPool = new List<EffectState>(DefaultPool);
    private Camera cachedCamera;
    private Transform container;
    private Transform cachedJudgmentLine;
    private float nextJudgmentLineSearch;
    private const float JudgmentLineSearchInterval = 0.5f;
    [SerializeField] private bool applySettingsOnAwake = true;
    [SerializeField] private bool useResourceFallback = true;
    [SerializeField] private NoteJudgementMeshSettings settingsOverride;

    private NoteSpawner cachedNoteSpawner;
    private float cachedTrackWidth = -1f;
    private float nextTrackWidthSampleTime;

    private class EffectState
    {
        public GameObject GameObject;
        public Transform Transform;
        public MeshRenderer Renderer;
        public MaterialPropertyBlock PropertyBlock;
        public Color BaseColor = Color.white;
        public float StartTime;
        public Vector3 StartPosition;
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

        var existing = FindFirstObjectByType<NoteJudgementMeshManager>();
        if (existing != null)
        {
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
        if (mf != null && mf.sharedMesh != null)
        {
            mf.mesh = Instantiate(mf.sharedMesh);
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
            for (int vi = 0; vi < verts.Length; vi++)
                state.VertexIsTop[vi] = verts[vi].y >= 0f;
        }

        return state;
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

    private void ReleaseState(EffectState state)
    {
        if (state == null) return;
        if (state.Persistent && state.Note != null)
        {
            activePersistentEffects.Remove(state.Note);
        }
        state.Persistent = false;
        state.Note = null;
        state.NoteRenderer = null;
        state.PropertyBlockDirty = true;
        state.SortingApplied = false;
        state.Busy = false;
        if (state.GameObject != null)
        {
            state.GameObject.SetActive(false);
        }
    }

    public void ShowEffect(NoteController note, JudgmentResult result, bool persistent = false)
    {
        if (!TryGetMaterial(result, out var material, out var baseColor))
        {
            if (debugSnap)
            {
                Debug.LogWarning($"[NoteJudge] ShowEffect: no material for result={result} (note={(note!=null?note.name:"null")}), effect skipped");
            }
            return;
        }

        if (persistent && note != null)
        {
            HidePersistentEffect(note);
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
        state.Note = persistent ? note : null;
        // Cache child renderer once — ApplyTransformForNote will reuse it every frame.
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
            state.Renderer.SetPropertyBlock(state.PropertyBlock);
        }
        state.PropertyBlockDirty = false;
        ApplyTransformForNote(state, note);
        state.GameObject.SetActive(true);
        if (persistent && note != null)
        {
            activePersistentEffects[note] = state;
        }
    }

    public void HidePersistentEffect(NoteController note)
    {
        if (note == null) return;
        if (activePersistentEffects.TryGetValue(note, out var state))
        {
            ReleaseState(state);
        }
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

    private void ApplyTransformForNote(EffectState state, NoteController note)
    {
        Vector3 position = Vector3.zero;
        Quaternion baseRotation = Quaternion.identity;
        float width = fallbackWidth;

        if (note != null && note.transform != null)
        {
            var noteTransform = note.transform;
            position = noteTransform.position;
            width = ResolveWidth(note);

            var jlTransform = snapToJudgmentPlane ? GetJudgmentLineTransform() : null;
            if (jlTransform != null)
            {
                // Compute world-space position so the mesh is reliably aligned to the judgment line
                // X: prefer renderer.bounds.center.x when available so the quad sits exactly over the visible sprite/mesh
                float x = noteTransform.position.x;
                if (useRendererBoundsForX && note != null)
                {
                    // Use cached renderer; fall back to GetComponentInChildren only on cache miss.
                    var rend = state.NoteRenderer;
                    if (rend == null)
                    {
                        try { rend = note.GetComponentInChildren<Renderer>(); state.NoteRenderer = rend; } catch { }
                    }
                    if (rend != null) x = rend.bounds.center.x;
                }

                float jlY = jlTransform.position.y + judgmentYOffset + surfaceOffset + (meshHeight * 0.5f);
                position = new Vector3(x + localPositionOffset.x, jlY + localPositionOffset.y, jlTransform.position.z + localPositionOffset.z);
                baseRotation = jlTransform.rotation;

                if (debugSnap)
                {
                    Debug.Log($"[NoteJudge] Aligning note={note?.name} x={x} jlY={jlTransform.position.y} meshCenterY={position.y} meshHeight={meshHeight} pos={position}");
                }
                // Apply sorting order relative to judgment line renderer once per effect activation.
                if (!state.SortingApplied)
                {
                    if (_cachedJlRenderer == null) _cachedJlRenderer = jlTransform.GetComponent<Renderer>();
                    if (_cachedJlRenderer != null && state.Renderer != null)
                    {
                        state.Renderer.sortingLayerID = _cachedJlRenderer.sortingLayerID;
                        state.Renderer.sortingOrder = Mathf.Max(state.Renderer.sortingOrder, _cachedJlRenderer.sortingOrder + sortingOrderOffset);
                    }
                    state.SortingApplied = true;
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

        state.StartPosition = position;
        Quaternion rotation = baseRotation * Quaternion.Euler(rotationOffsetEuler);
        float finalWidth = Mathf.Max(0.01f, (width * widthScale) + widthPadding);
        Vector3 scale = new Vector3(finalWidth, Mathf.Max(0.01f, meshHeight), 1f);

        state.Transform.position = state.StartPosition;
        state.Transform.rotation = rotation;
        state.Transform.localScale = scale;
    }

    private float ResolveWidth(NoteController note)
    {
        if (note == null)
        {
            return Mathf.Max(0.01f, fallbackWidth);
        }

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

        int startLane = data.startLane;
        int endLane = data.endLane;
        int span = Mathf.Max(1, Mathf.Abs(endLane - startLane) + 1);

        float trackWidth = GetTrackWidth(note);
        float lanes = Mathf.Max(1, totalLaneCount);
        float laneWidth = trackWidth / lanes;
        float width = laneWidth * span * Mathf.Max(0f, laneWidthMultiplier);
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
        Material pick = vertexColorMaterial;
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

            if (state.Persistent)
            {
                // Release if note is destroyed (fake-null) OR its GameObject is no
                // longer active in hierarchy (pooled/recycled) OR note was judged.
                bool noteGone = state.Note == null
                    || !state.Note.gameObject.activeInHierarchy
                    || state.Note.IsJudged;
                if (noteGone)
                {
                    ReleaseState(state);
                    continue;
                }

                ApplyTransformForNote(state, state.Note);
                // 持續效果：只在顏色變更（PropertyBlockDirty）時才重算 gradient 與 SetPropertyBlock。
                // 每幀都更新位置（ApplyTransformForNote）但不重算色彩，減少 mesh.vertices 分配與 SetPropertyBlock 開銷。
                if (state.PropertyBlockDirty)
                {
                    ApplyVerticalGradient(state, 1f);
                    if (state.Renderer != null && state.PropertyBlock != null)
                    {
                        state.PropertyBlock.SetColor("_Color", state.BaseColor);
                        state.Renderer.SetPropertyBlock(state.PropertyBlock);
                    }
                    state.PropertyBlockDirty = false;
                }
                continue;
            }

            float elapsed = now - state.StartTime;
            if (elapsed >= duration)
            {
                ReleaseState(state);
                continue;
            }
            state.Transform.position = state.StartPosition;

            // 縱向漸層 + 可選的存活期淡出
            float alpha = 1f;
            if (enableLifetimeFade)
            {
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
                alpha = Mathf.Clamp01(1f - (t * t)); // ease-out
            }

            ApplyVerticalGradient(state, alpha);
            if (state.Renderer != null && state.PropertyBlock != null)
            {
                var c = state.BaseColor;
                c.a *= alpha;
                state.PropertyBlock.SetColor("_Color", c);
                state.Renderer.SetPropertyBlock(state.PropertyBlock);
            }
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
