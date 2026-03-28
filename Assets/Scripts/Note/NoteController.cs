using System;
using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.Rendering;
using Judgment;

public class NoteController : MonoBehaviour
{
    public Material rightHandMaterial; // 在 Inspector 中指定右手紅色材質
    public Material leftHandMaterial;  // 在 Inspector 中指定左手藍色材質
    public Sprite rightHandSprite; // 可選：在 Inspector 中指定右手的圖片 (優先於 Material)
    public Sprite leftHandSprite;  // 可選：在 Inspector 中指定左手的圖片 (優先於 Material)
    // Staccato-specific sprites (optional). If assigned, these override the normal hand sprites for staccato notes.
    public Sprite staccatoRightSprite;
    public Sprite staccatoLeftSprite;
    // Soft note sprite (type==1). Assign in Inspector to override visual for "soft" notes.
    public Sprite softSprite;
    public Texture2D softTexture;
    public Texture2D rightHandTexture; // 可選：直接指定貼圖檔 (拖 PNG 也行)
    public Texture2D leftHandTexture;  // 可選：直接指定貼圖檔 (拖 PNG 也行)
    [Header("Staccato Indicator")]
    [SerializeField] private Sprite rightStaccatoIndicatorSprite;
    [SerializeField] private Sprite leftStaccatoIndicatorSprite;
    [SerializeField] private Vector3 staccatoIndicatorOffset = new Vector3(0f, 0.6f, 0f);
    [SerializeField] private float staccatoIndicatorScale = 1f;
    [SerializeField] private float staccatoIndicatorHeight = 1f;
    [SerializeField] private bool staccatoIndicatorUpright = true;
    [SerializeField] private bool staccatoIndicatorOffsetRelativeToWidth = true;

    // ... (其餘屬性不變) ...
    public float startTime;
    public float endTime;
    
    private float spawnZ;
    private float judgmentZ = 0.0f; // 判定線的 Z 軸位置
    private Transform judgmentLineTransformForNote = null;
    // Cached judgment line renderer sorting info so notes can ensure they render above it
    private int cachedJudgmentSortingOrder = int.MinValue;
    private int cachedJudgmentSortingLayerID = -1;

    private Conductor conductor;
    private bool didTryFindConductor = false;
    private NoteData noteData;
    private NoteSpawner noteSpawner;
    private Vector3 parentLossyScale = Vector3.one;
    private Renderer noteRenderer;
    private SpriteRenderer runtimeSpriteRenderer;
    private bool isInitialized = false;
    [Header("Hold Tail Visual")]
    [SerializeField] private Texture2D rightHoldTailTexture;
    [SerializeField] private Texture2D leftHoldTailTexture;
    [SerializeField] private Shader holdTailShaderOverride;
    [SerializeField] private Color rightTailTint = Color.white;
    [SerializeField] private Color leftTailTint = Color.white;

    private GameObject holdTailObject;
    private MeshRenderer holdTailRenderer;
    private MeshFilter holdTailMeshFilter;
    private float currentTailWidthWorld = 0f;
    private static Mesh sharedHoldTailQuad;
    private MaterialPropertyBlock holdTailPropertyBlock;
    private static readonly int HoldTailColorId = Shader.PropertyToID("_Color");
    private static readonly int HoldTailMainTexId = Shader.PropertyToID("_MainTex");
    // Cached soft-note flag to avoid repeated reflection checks
    private bool isSoftCached = false;
    // Cached staccato-note flag to avoid repeated reflection checks
    private bool isStaccatoCached = false;
    private GameObject staccatoIndicatorInstance;
    private StaccatoIndicatorBillboard staccatoIndicatorController;
    private SpriteRenderer staccatoIndicatorRenderer;
    // Public accessor for cached soft flag so other managers can read without reflection
    public bool IsSoft => isSoftCached;
    // Public accessor for cached staccato flag
    public bool IsStaccato => isStaccatoCached;
    private float cachedWorldWidth = 1f;
    [SerializeField]
    private float releaseZThreshold = 0.00f; // world units: how close to judgmentZ before actually releasing
    [SerializeField]
    private float holdTailYOffset = 0.0f; // small Y offset to avoid Z-fighting with the track
    [SerializeField]
    private float holdTailShrinkSpeed = 1f; // units per second to smooth the tail shortening (world units/sec)
    [SerializeField]
    private float holdTailWidthFactor = 0.8f; // fraction of note width used for tail thickness (0.8 = 80% of note width)
    [SerializeField]
    private float tailEndOffsetMs = 40f; // tail will end at (endTime - tailEndOffsetMs) milliseconds
    [SerializeField]
    private float minimumHoldTailVisualLength = 0.02f; // ensure very short holds still show a visible tail pre-judgment
    private float currentTailLength = 0f;
    private float initialTailLength = 0f;
    // Cache last applied tail length to avoid redundant hold-tail mesh updates
    private float lastAppliedTailLength = -1f;
    [SerializeField]
    private float tailLengthUpdateThreshold = 0.001f; // world units: minimum change to update endpoint
    private float holdDurationMs = 0f;
    private float holdTailAdjustedEndMs = 0f;
    private bool isJudged = false; // flag set when judged by JudgmentManager
    // Soft notes: judged immediately, but disappear only when reaching judgment line.
    private bool softJudgedPendingRelease = false;
    private bool headPressed = false; // set when player successfully pressed the head of a hold
    private bool headMissed = false;  // set when head wasn't pressed within allowed window -> make the hold unjudgeable
    
    // Hit sound tracking
    private bool hasTriggeredHitSound = false;
    private static AudioClip cachedHitSound;
    private static Material cachedRightHoldTailMaterial;
    private static Material cachedLeftHoldTailMaterial;
    private static readonly System.Collections.Generic.Dictionary<Texture2D, Sprite> spriteCache = new System.Collections.Generic.Dictionary<Texture2D, Sprite>();
    private NotePool owningPool;
    private static Camera cachedMainCamera = null;

    public Transform TrackTransform => noteSpawner != null ? noteSpawner.trackTransform : null;

    private void Awake()
    {
        if (holdTailPropertyBlock == null)
        {
            holdTailPropertyBlock = new MaterialPropertyBlock();
        }
    }

    [Header("Positioning")]
    [Tooltip("If true, notes will initialize their Y position to match the JudgmentLine's Y. Disable to keep notes at a fixed track Y instead.")]
    [SerializeField]
    private bool alignToJudgmentLineY = true;
    [Tooltip("Vertical offset applied to notes when aligning to the JudgmentLine (world units). Note Y = JudgmentLine.Y + this offset.")]
    [SerializeField]
    private float judgmentYOffset = 0.1f;

    // Helper to detect if NoteData represents a "soft" note (note_type == 1).
    // Uses reflection to be tolerant of different NoteData definitions (string or int fields).
    private static bool IsSoftNote(object nd)
    {
        if (nd == null) return false;
        try
        {
            var t = nd.GetType();
            // common case: a string-typed 'type' property like "tap"/"hold"/"soft" or numeric string
            var propType = t.GetProperty("type");
            if (propType != null)
            {
                var v = propType.GetValue(nd);
                if (v != null)
                {
                    var s = v.ToString();
                    if (string.Equals(s, "soft", StringComparison.OrdinalIgnoreCase)) return true;
                    if (s == "1") return true;
                    if (int.TryParse(s, out int vi) && vi == 1) return true;
                }
            }

            // try common numeric property/field names
            string[] names = new string[] { "note_type", "noteType", "typeId", "typeIndex", "noteTypeId" };
            foreach (var nm in names)
            {
                var p = t.GetProperty(nm);
                if (p != null)
                {
                    var vv = p.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi2) && vi2 == 1) return true;
                }
                var f = t.GetField(nm);
                if (f != null)
                {
                    var vv = f.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi3) && vi3 == 1) return true;
                }
            }
        }
        catch { }
        return false;
    }

    // Helper to detect if NoteData represents a "staccato" note (note_type == 3) or string type "staccato".
    private static bool IsStaccatoNote(object nd)
    {
        if (nd == null) return false;
        try
        {
            var t = nd.GetType();
            var propType = t.GetProperty("type");
            if (propType != null)
            {
                var v = propType.GetValue(nd);
                if (v != null)
                {
                    var s = v.ToString();
                    if (string.Equals(s, "staccato", StringComparison.OrdinalIgnoreCase)) return true;
                    if (s == "3") return true;
                    if (int.TryParse(s, out int vi) && vi == 3) return true;
                }
            }

            string[] names = new string[] { "note_type", "noteType", "typeId", "typeIndex", "noteTypeId" };
            foreach (var nm in names)
            {
                var p = t.GetProperty(nm);
                if (p != null)
                {
                    var vv = p.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi2) && vi2 == 3) return true;
                }
                var f = t.GetField(nm);
                if (f != null)
                {
                    var vv = f.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi3) && vi3 == 3) return true;
                }
            }
        }
        catch { }
        return false;
    }

    public void SetOwningPool(NotePool pool)
    {
        this.owningPool = pool;
    }

    // Expose note data and active flag for JudgmentManager
    public NoteData NoteData => noteData;
    public bool IsActive => isInitialized && gameObject.activeInHierarchy && !isJudged;
    // Whether this note can be judged by player input. For holds, becomes false if head was missed.
    public bool IsJudgeable => IsActive && !headMissed;
    public bool IsJudged
    {
        get => isJudged;
        set => isJudged = value;
    }

    // Clear headMissed flag when player recovers a hold after release (for non-drop behavior)
    public void ClearHeadMissed()
    {
        headMissed = false;
    }

    public float GetCurrentWorldWidth()
    {
        try
        {
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.enabled)
            {
                float width = runtimeSpriteRenderer.bounds.size.x;
                if (width > 0f)
                {
                    cachedWorldWidth = width;
                    return width;
                }
            }
        }
        catch { }

        try
        {
            if (noteRenderer != null && noteRenderer.enabled)
            {
                float width = noteRenderer.bounds.size.x;
                if (width > 0f)
                {
                    cachedWorldWidth = width;
                    return width;
                }
            }
        }
        catch { }

        return Mathf.Max(0.0001f, cachedWorldWidth);
    }

    // Precreate a disabled holdTail to avoid runtime allocations at spawn time
    public void PrecreateHoldTail()
    {
        if (holdTailObject != null) return;
        try
        {
            EnsureHoldTailVisualExists();
            if (holdTailObject != null)
            {
                if (owningPool != null && owningPool.Container != null)
                {
                    holdTailObject.transform.SetParent(owningPool.Container, false);
                }
                else
                {
                    holdTailObject.transform.SetParent(null, false);
                }
                holdTailObject.SetActive(false);
            }
        }
        catch
        {
            DisposeHoldTailImmediate();
        }
    }

    private void EnsureHoldTailVisualExists()
    {
        if (holdTailObject != null && holdTailRenderer != null && holdTailMeshFilter != null)
        {
            return;
        }

        if (sharedHoldTailQuad == null)
        {
            sharedHoldTailQuad = new Mesh
            {
                name = "HoldTailQuad"
            };
            sharedHoldTailQuad.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 0f, 1f),
                new Vector3(0.5f, 0f, 1f)
            };
            sharedHoldTailQuad.uv = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            sharedHoldTailQuad.colors = new Color[]
            {
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 0f),
                new Color(1f, 1f, 1f, 0f)
            };
            sharedHoldTailQuad.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            sharedHoldTailQuad.RecalculateBounds();
        }

        if (holdTailObject == null)
        {
            holdTailObject = new GameObject("HoldTail");
            holdTailObject.hideFlags = HideFlags.DontSave;
        }

        holdTailMeshFilter = holdTailObject.GetComponent<MeshFilter>();
        if (holdTailMeshFilter == null)
        {
            holdTailMeshFilter = holdTailObject.AddComponent<MeshFilter>();
        }
        holdTailMeshFilter.sharedMesh = sharedHoldTailQuad;

        holdTailRenderer = holdTailObject.GetComponent<MeshRenderer>();
        if (holdTailRenderer == null)
        {
            holdTailRenderer = holdTailObject.AddComponent<MeshRenderer>();
        }

        holdTailRenderer.shadowCastingMode = ShadowCastingMode.Off;
        holdTailRenderer.receiveShadows = false;
        holdTailRenderer.lightProbeUsage = LightProbeUsage.Off;
        holdTailRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        holdTailRenderer.allowOcclusionWhenDynamic = false;

        holdTailObject.transform.localScale = Vector3.one;
        holdTailObject.transform.localRotation = Quaternion.identity;
        holdTailObject.transform.localPosition = Vector3.zero;
        holdTailObject.SetActive(false);
    }

    private void DisposeHoldTailImmediate()
    {
        try
        {
            if (holdTailRenderer != null)
            {
                holdTailRenderer.sharedMaterial = null;
            }
        }
        catch { }

        if (holdTailObject != null)
        {
            try { Destroy(holdTailObject); }
            catch { }
        }

        holdTailObject = null;
        holdTailRenderer = null;
        holdTailMeshFilter = null;
    }

    private Texture2D ResolveHoldTailTexture(bool isRightHand)
    {
        Texture2D texture = isRightHand ? rightHoldTailTexture : leftHoldTailTexture;
        if (texture != null) return texture;

        // Fallback to hand-specific runtime textures if dedicated tail texture isn't assigned
        texture = isRightHand ? rightHandTexture : leftHandTexture;
        if (texture != null) return texture;

        Sprite fallbackSprite = null;
        if (isRightHand)
        {
            if (rightHandSprite != null) fallbackSprite = rightHandSprite;
            else if (staccatoRightSprite != null) fallbackSprite = staccatoRightSprite;
        }
        else
        {
            if (leftHandSprite != null) fallbackSprite = leftHandSprite;
            else if (staccatoLeftSprite != null) fallbackSprite = staccatoLeftSprite;
        }

        if (fallbackSprite != null)
        {
            try { return fallbackSprite.texture; }
            catch { }
        }

        // Attempt to load common resource paths so users don't have to remember to assign manually.
        string baseName = isRightHand ? "Righthold" : "Lefthold";
        string[] resourceHints = new string[]
        {
            baseName,
            $"HoldTail/{baseName}",
            $"HoldTails/{baseName}",
            $"Textures/{baseName}",
            $"Materials/{baseName}"
        };
        foreach (var hint in resourceHints)
        {
            try
            {
                var tex = Resources.Load<Texture2D>(hint);
                if (tex != null) return tex;
            }
            catch { }
        }

        return null;
    }

    private Material ResolveHoldTailMaterial(bool isRightHand, out Texture2D resolvedTexture)
    {
        resolvedTexture = ResolveHoldTailTexture(isRightHand);
        if (resolvedTexture == null)
        {
            try { BuildLogger.LogWarning($"[NoteController] Hold tail texture missing for {(isRightHand ? "right" : "left")} hand. Assign tail textures in the inspector (Lefthold.png / Righthold.png)." ); } catch { }
            return null;
        }

        Material cached = isRightHand ? cachedRightHoldTailMaterial : cachedLeftHoldTailMaterial;
        if (cached != null)
        {
            if (cached.mainTexture == null)
            {
                cached.mainTexture = resolvedTexture;
            }
            resolvedTexture = cached.mainTexture as Texture2D ?? resolvedTexture;
            cached.color = isRightHand ? rightTailTint : leftTailTint;
            return cached;
        }

        Shader shader = holdTailShaderOverride != null ? holdTailShaderOverride : Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        var material = new Material(shader)
        {
            name = isRightHand ? "RightHoldTailMaterial" : "LeftHoldTailMaterial",
            hideFlags = HideFlags.DontSave
        };
        material.mainTexture = resolvedTexture;
        material.color = isRightHand ? rightTailTint : leftTailTint;
        material.renderQueue = (int)RenderQueue.Transparent;

        if (holdTailShaderOverride == null)
        {
            if (material.HasFloat("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasFloat("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasFloat("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasFloat("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        if (isRightHand)
        {
            cachedRightHoldTailMaterial = material;
        }
        else
        {
            cachedLeftHoldTailMaterial = material;
        }

        return material;
    }

    private void ApplyHoldTailMaterialProperties(bool isRightHand, Texture2D texture)
    {
        if (holdTailRenderer == null) return;
        if (holdTailPropertyBlock == null) holdTailPropertyBlock = new MaterialPropertyBlock();

        holdTailPropertyBlock.Clear();
        holdTailPropertyBlock.SetColor(HoldTailColorId, isRightHand ? rightTailTint : leftTailTint);
        if (texture != null)
        {
            holdTailPropertyBlock.SetTexture(HoldTailMainTexId, texture);
        }
        holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
    }

    private void SetHoldTailActive(bool active)
    {
        if (holdTailObject == null || holdTailRenderer == null) return;
        if (active)
        {
            holdTailRenderer.enabled = true;
            holdTailObject.SetActive(true);
            try
            {
                bool isRightHand = noteData != null && noteData.hand == 0;
                Texture2D currentTexture = null;
                if (holdTailRenderer.sharedMaterial != null)
                {
                    currentTexture = holdTailRenderer.sharedMaterial.mainTexture as Texture2D;
                }
                ApplyHoldTailMaterialProperties(isRightHand, currentTexture);
            }
            catch { }
        }
        else
        {
            holdTailRenderer.enabled = false;
            holdTailObject.SetActive(false);
        }
    }

    private void UpdateHoldTailDimensions(float widthWorld, float lengthWorld, bool forceUpdate = false)
    {
        if (holdTailObject == null) return;

        widthWorld = Mathf.Max(0.0001f, widthWorld);
        lengthWorld = Mathf.Max(0f, lengthWorld);

        Transform parent = holdTailObject.transform.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;

        float localWidth = parentScale.x != 0f ? widthWorld / parentScale.x : widthWorld;
        float localLength = parentScale.z != 0f ? lengthWorld / parentScale.z : lengthWorld;

        Vector3 currentScale = holdTailObject.transform.localScale;
        bool widthChanged = forceUpdate || Mathf.Abs(currentScale.x - localWidth) > tailLengthUpdateThreshold;
        bool lengthChanged = forceUpdate || Mathf.Abs(currentScale.z - localLength) > tailLengthUpdateThreshold;

        if (widthChanged || lengthChanged)
        {
            holdTailObject.transform.localScale = new Vector3(localWidth, 1f, Mathf.Max(0.0001f, localLength));
            holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
        }

        currentTailWidthWorld = widthWorld;
        lastAppliedTailLength = lengthWorld;
    }
    
    public void Initialize(NoteData noteData, NoteSpawner spawner, float timeToStartMs)
    {
        this.noteData = noteData;
        isSoftCached = false;
        isStaccatoCached = false;
        try { isStaccatoCached = IsStaccatoNote(noteData); }
        catch { isStaccatoCached = false; }
        if (!isStaccatoCached)
        {
            try { isSoftCached = IsSoftNote(noteData); }
            catch { isSoftCached = false; }
        }
            if (this.noteData == null)
            {
                // Debug.LogError("NoteController.Initialize called with null noteData. Releasing note to avoid errors.");
                ReleaseOrDestroy();
                return;
            }

            // Defensive reset: clear judgement-related runtime flags to avoid carrying
            // over state from pooled instances (prevents stale isJudged/headMissed etc.).
            try
            {
                isJudged = false;
                softJudgedPendingRelease = false;
                headMissed = false;
                headPressed = false;
                hasTriggeredHitSound = false;
            }
            catch { }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Defensive: if Initialize is called while already initialized, attempt cleanup first
        if (isInitialized)
        {
            //try
            //{
            //    Debug.LogWarning($"[NoteController] Initialize called while already initialized. Cleaning previous state. goId={gameObject.GetInstanceID()} startTime={(this.noteData!=null?this.noteData.startTime:-1)}\nCallStack:\n{System.Environment.StackTrace}");
            //}
            //catch { }
            try { CleanupPooled(); } catch { }
            try { JudgmentManager.Instance?.UnregisterNote(this); } catch { }
            isJudged = false;
            isInitialized = false;
        }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        //try {
        //    var nd = this.noteData;
        //    if (nd != null)
        //    {
        //        Debug.Log($"[NoteController] Initialize: note startTime={nd.startTime} type={nd.type} lanes={nd.startLane}-{nd.endLane} goId={gameObject.GetInstanceID()}");
        //    }
        //} catch {}
#endif

        try
        {
            // Keep original chart timings; global delay is applied by starting audio later
                startTime = noteData.startTime; 
            endTime = noteData.endTime;
            noteSpawner = spawner;

            // Guard against initialization order: prefer spawner-provided conductor to avoid expensive scene-wide searches
            if (noteSpawner != null)
            {
                try
                {
                    if (noteSpawner.Conductor != null)
                    {
                        conductor = noteSpawner.Conductor;
                    }
                }
                catch
                {
                    // ignore and fallback
                }
            }
            if (conductor == null)
            {
                if (GameManager.Instance != null)
                {
                    conductor = GameManager.Instance.Conductor;
                }
                else
                {
                    // Last resort: expensive lookup - should be rare after the above changes
                    conductor = FindAnyObjectByType<Conductor>();
                }
            }

            float initialSpeed = (noteSpawner != null) ? noteSpawner.speed : 30f;
            float travelSeconds = Mathf.Max(0f, timeToStartMs) / 1000f;
            this.spawnZ = judgmentZ + travelSeconds * initialSpeed;

            // --- Display selection for left/right hand ---
            noteRenderer = GetComponent<Renderer>();
            // Prefer any SpriteRenderer in children so we can ensure a child renderer lies flat on the track
            runtimeSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

            bool hasSprite = (noteData.hand == 0 && rightHandSprite != null) || (noteData.hand != 0 && leftHandSprite != null);
            bool hasTexture = (noteData.hand == 0 && rightHandTexture != null) || (noteData.hand != 0 && leftHandTexture != null);

            if (hasSprite || hasTexture)
            {
                if (noteRenderer != null && !(noteRenderer is SpriteRenderer))
                {
                    noteRenderer.enabled = false;
                }

                if (runtimeSpriteRenderer == null)
                {
                    // Create a child GameObject called RuntimeSprite which will be rotated to lie flat on track
                    try
                    {
                        GameObject child = new GameObject("RuntimeSprite");
                        child.transform.SetParent(transform, false);
                        // Rotate so the sprite's local Y maps to world Z (lay flat)
                        child.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                        runtimeSpriteRenderer = child.AddComponent<SpriteRenderer>();
                        // Debug.Log($"NoteController.Initialize: Created child SpriteRenderer '{child.name}' for note on GameObject '{gameObject.name}'.");
                    }
                    catch
                    {
                        // Debug.LogWarning($"NoteController.Initialize: Creating child SpriteRenderer failed: {exChild}");
                    }
                }
                else
                {
                    // If the found SpriteRenderer is on the parent object (not a child), move its sprite to a new child rotated to lie flat.
                    if (runtimeSpriteRenderer.gameObject == gameObject)
                    {
                        try
                        {
                            Sprite existingSprite = runtimeSpriteRenderer.sprite;
                            int sortingLayer = runtimeSpriteRenderer.sortingLayerID;
                            int sortingOrder = runtimeSpriteRenderer.sortingOrder;
                            // disable existing renderer to avoid double-draw
                            runtimeSpriteRenderer.enabled = false;
                            GameObject child = new GameObject("RuntimeSprite");
                            child.transform.SetParent(transform, false);
                            child.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                            var childRenderer = child.AddComponent<SpriteRenderer>();
                            childRenderer.sprite = existingSprite;
                            childRenderer.sortingLayerID = sortingLayer;
                            childRenderer.sortingOrder = sortingOrder;
                            runtimeSpriteRenderer = childRenderer;
                            // Debug.Log("NoteController.Initialize: Moved SpriteRenderer to child and rotated to lie flat.");
                        }
                        catch
                        {
                            // Debug.LogWarning($"NoteController.Initialize: Failed to move parent SpriteRenderer to child: {exMove}");
                        }
                    }
                    else
                    {
                        // Already a child SpriteRenderer; ensure the child is rotated to lie flat
                        runtimeSpriteRenderer.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                        // Debug.Log($"NoteController.Initialize: Using existing child SpriteRenderer '{runtimeSpriteRenderer.gameObject.name}' and set rotation to lie flat.");
                    }
                }

                if (runtimeSpriteRenderer == null)
                {
                    // Diagnostic dump to help track down why SpriteRenderer cannot be created
                    string compList = "";
                    var comps = GetComponents<Component>();
                    foreach (var c in comps)
                    {
                        if (c == null) continue;
                        compList += c.GetType().Name + ",";
                    }
                    string spawnerName = noteSpawner != null ? noteSpawner.gameObject.name : "(no spawner)";
                    // Debug.LogError($"NoteController.Initialize: failed to create or find SpriteRenderer for note (noteData.hand={(noteData!=null?noteData.hand:-1)}).\nGameObject='{gameObject.name}', Application.isPlaying={Application.isPlaying}, spawner={spawnerName}, components=[{compList}]. Sprite will not be assigned.");
                }
                else
                {
                        try
                        {
                            // Special-case: staccato notes may have dedicated sprites
                            if (isStaccatoCached)
                            {
                                if (noteData.hand == 0) // right
                                {
                                    if (staccatoRightSprite != null) runtimeSpriteRenderer.sprite = staccatoRightSprite;
                                    else if (rightHandSprite != null) runtimeSpriteRenderer.sprite = rightHandSprite;
                                    else if (rightHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(rightHandTexture);
                                }
                                else // left
                                {
                                    if (staccatoLeftSprite != null) runtimeSpriteRenderer.sprite = staccatoLeftSprite;
                                    else if (leftHandSprite != null) runtimeSpriteRenderer.sprite = leftHandSprite;
                                    else if (leftHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(leftHandTexture);
                                }
                            }
                            else
                            {
                                if (isSoftCached)
                                {
                                    if (softSprite != null)
                                        runtimeSpriteRenderer.sprite = softSprite;
                                    else if (softTexture != null)
                                        runtimeSpriteRenderer.sprite = GetOrCreateSprite(softTexture);
                                    else
                                    {
                                        // fallback to handed sprites if soft sprite not assigned
                                        if (noteData.hand == 0 && rightHandSprite != null) runtimeSpriteRenderer.sprite = rightHandSprite;
                                        else if (noteData.hand != 0 && leftHandSprite != null) runtimeSpriteRenderer.sprite = leftHandSprite;
                                        else if (noteData.hand == 0 && rightHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(rightHandTexture);
                                        else if (noteData.hand != 0 && leftHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(leftHandTexture);
                                    }
                                }
                                else
                                {
                                    if (noteData.hand == 0) // right
                                    {
                                        if (rightHandSprite != null)
                                            runtimeSpriteRenderer.sprite = rightHandSprite;
                                        else if (rightHandTexture != null)
                                            runtimeSpriteRenderer.sprite = GetOrCreateSprite(rightHandTexture);
                                    }
                                    else // left
                                    {
                                        if (leftHandSprite != null)
                                            runtimeSpriteRenderer.sprite = leftHandSprite;
                                        else if (leftHandTexture != null)
                                            runtimeSpriteRenderer.sprite = GetOrCreateSprite(leftHandTexture);
                                    }
                                }
                            }
                        }
                    catch
                    {
                        // Debug.LogError($"NoteController.Initialize: Exception while assigning sprite: {ex} -- noteData.hand={(noteData!=null?noteData.hand:-1)}");
                    }

                    // Copy sorting from existing renderer when possible
                    if (noteRenderer != null)
                    {
                        runtimeSpriteRenderer.sortingLayerID = noteRenderer.sortingLayerID;
                        runtimeSpriteRenderer.sortingOrder = noteRenderer.sortingOrder;
                    }

                    // Ensure the note sprite renders above the JudgmentLine when present.
                    try
                    {
                        if (runtimeSpriteRenderer != null && cachedJudgmentSortingOrder != int.MinValue)
                        {
                            // Align sorting layer to judgment line to make ordering deterministic,
                            // then ensure order is higher than the judgment line's order.
                            runtimeSpriteRenderer.sortingLayerID = cachedJudgmentSortingLayerID;
                            runtimeSpriteRenderer.sortingOrder = Mathf.Max(runtimeSpriteRenderer.sortingOrder, cachedJudgmentSortingOrder + 2);
                        }
                    }
                    catch { }

                    // Also ensure mesh-based renderers (if any) use the same sorting so they render above the judgment line.
                    try
                    {
                        if (noteRenderer != null && runtimeSpriteRenderer != null)
                        {
                            noteRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            noteRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder;
                        }
                        if (holdTailRenderer != null && runtimeSpriteRenderer != null)
                        {
                            holdTailRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            holdTailRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder;
                        }
                        if (staccatoIndicatorRenderer != null && runtimeSpriteRenderer != null)
                        {
                            staccatoIndicatorRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            staccatoIndicatorRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder;
                        }
                    }
                    catch { }

                    // --- Protective visibility fix for staccato notes ---
                    // Some staccato sprites in authoring may have unexpected alpha/sorting that makes them invisible
                    // (even though they spawn). Apply a conservative, non-destructive fix only for staccato instances
                    // so we can determine if invisibility is due to alpha/sorting issues.
                    try
                    {
            if (isStaccatoCached && runtimeSpriteRenderer != null)
                        {
                            // Ensure the renderer is enabled
                            runtimeSpriteRenderer.enabled = true;

                            // Force opaque alpha if somehow set to transparent
                            try
                            {
                                var col = runtimeSpriteRenderer.color;
                                if (col.a < 0.01f) runtimeSpriteRenderer.color = new Color(col.r, col.g, col.b, 1f);
                            }
                            catch { }

                            // Ensure sprite sorting is above mesh renderer to avoid being occluded
                            try
                            {
                                int meshOrder = 0;
                                if (noteRenderer != null) meshOrder = noteRenderer.sortingOrder;
                                runtimeSpriteRenderer.sortingOrder = Mathf.Max(runtimeSpriteRenderer.sortingOrder, meshOrder + 2);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    // Extra diagnostic for staccato visibility checks
                    try
                    {
                        if (isStaccatoCached)
                        {
                            string sname = (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null) ? runtimeSpriteRenderer.sprite.name : "<none>";
                            var rc = runtimeSpriteRenderer != null ? runtimeSpriteRenderer.color : Color.clear;
                            var rb = runtimeSpriteRenderer != null ? runtimeSpriteRenderer.bounds : new Bounds();
                            BuildLogger.Log($"[NoteController] StaccatoVisibilityCheck: sprite={sname} color={rc} spriteBounds={rb} localScale={transform.localScale}");
                        }
                    }
                    catch { }
                    #endif
                }
            }
            else
            {
                // No sprite/texture provided, fall back to material-based behavior on the existing renderer
                if (noteRenderer != null)
                {
                    if (noteData.hand == 0) // right
                    {
                        if (rightHandMaterial != null)
                            noteRenderer.material = rightHandMaterial;
                    }
                    else
                    {
                        if (leftHandMaterial != null)
                            noteRenderer.material = leftHandMaterial;
                    }
                }
                else
                {
                    // Debug.LogError("Note Prefab is missing a Renderer component!");
                }
            }

            // --- Dynamically calculate position and scale ---
            float totalLanes = 28f; // Total number of lanes
            // Determine track width from assigned spawner/track when possible to avoid hardcoded lane spacing errors
            float trackWidth = 105f; // default fallback width (world units)
            try
            {
                if (noteSpawner != null && noteSpawner.trackTransform != null)
                {
                    var rend = noteSpawner.trackTransform.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        trackWidth = rend.bounds.size.x;
                    }
                    else
                    {
                        // If no renderer is present, attempt to infer width from localScale.x if it's been configured
                        // (this is a best-effort fallback; keep the existing constant if inference fails)
                        float inferred = Mathf.Abs(noteSpawner.trackTransform.lossyScale.x);
                        if (inferred > 0.001f)
                        {
                            // assume a base unity width of 1 maps to scale.x units; multiply by a nominal per-track unit
                            trackWidth = inferred * 100f; // nominal conversion factor when no renderer is available
                        }
                    }
                }
            }
            catch { /* keep fallback trackWidth */ }
            float laneWidth = trackWidth / totalLanes;
            float gap = 1.0f;

            float width = ((noteData.endLane - noteData.startLane + 1) * laneWidth) - gap;
            float centerLane = (noteData.startLane + noteData.endLane) / 2.0f;
            float positionX = (centerLane * laneWidth) - (trackWidth / 2.0f) + (laneWidth / 2.0f);

            // If a JudgmentLine exists in scene, align note Y/Z to that line so visuals match popups
            try
            {
                var jlGo = GameObject.Find("JudgmentLine");
                if (jlGo != null)
                        {
                            judgmentLineTransformForNote = jlGo.transform;
                            judgmentZ = judgmentLineTransformForNote.position.z;
                            float jy = judgmentLineTransformForNote.position.y;

                            // Only align note Y to the judgment line if the option is enabled.
                            if (alignToJudgmentLineY)
                            {
                                transform.position = new Vector3(positionX, jy + judgmentYOffset, spawnZ);
                            }
                            else
                            {
                                // Keep existing fallback Y (track or default)
                                float fallbackY = 0.5f;
                                try { if (noteSpawner != null && noteSpawner.trackTransform != null) fallbackY = noteSpawner.trackTransform.position.y; } catch { }
                                transform.position = new Vector3(positionX, fallbackY, spawnZ);
                            }

                            // Cache judgment line renderer sorting information (if available)
                            try
                            {
                                var jlRenderer = jlGo.GetComponent<Renderer>();
                                if (jlRenderer != null)
                                {
                                    cachedJudgmentSortingOrder = jlRenderer.sortingOrder;
                                    cachedJudgmentSortingLayerID = jlRenderer.sortingLayerID;
                                }
                            }
                            catch { }
                        }
                else
                {
                    transform.position = new Vector3(positionX, 0.5f, spawnZ);
                }
            }
            catch
            {
                transform.position = new Vector3(positionX, 0.5f, spawnZ);
            }

            parentLossyScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;

            // Determine current world width in a robust way:
            // Prefer the assigned Sprite's intrinsic bounds (sprite.bounds.size.x) multiplied by the current lossyScale,
            // otherwise fall back to renderer.bounds.
            float currentWorldWidth = 1f;
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null)
            {
                // sprite.bounds is in local units at scale=1; multiply by lossyScale.x to get world width
                float spriteLocalWidth = runtimeSpriteRenderer.sprite.bounds.size.x;
                currentWorldWidth = spriteLocalWidth * transform.lossyScale.x;
                if (currentWorldWidth <= 0f)
                {
                    currentWorldWidth = runtimeSpriteRenderer.bounds.size.x > 0f ? runtimeSpriteRenderer.bounds.size.x : 1f;
                }
            }
            else if (noteRenderer != null)
            {
                currentWorldWidth = noteRenderer.bounds.size.x > 0f ? noteRenderer.bounds.size.x : 1f;
            }
            else if (runtimeSpriteRenderer != null)
            {
                currentWorldWidth = runtimeSpriteRenderer.bounds.size.x > 0f ? runtimeSpriteRenderer.bounds.size.x : 1f;
            }

            float desiredWorldWidth = width;
            cachedWorldWidth = Mathf.Max(0.0001f, desiredWorldWidth);
            float scaleFactorX = desiredWorldWidth / Mathf.Max(0.0001f, currentWorldWidth);
            Vector3 baseLocal = transform.localScale;
            // Apply same X scale multiplier to Y to preserve sprite aspect ratio and ensure height matches proportionally
            float newLocalX = baseLocal.x * scaleFactorX;
            float newLocalY = baseLocal.y * scaleFactorX;
            transform.localScale = new Vector3(newLocalX, newLocalY, baseLocal.z);

            // If this is a hold note, create a quad-based tail mesh to visually represent its length
            if (noteData.type == "hold")
            {
                try
                {
                    float speedWorldUnits = (noteSpawner != null) ? noteSpawner.speed : 30f;
                    holdTailAdjustedEndMs = Mathf.Max(startTime, endTime - tailEndOffsetMs);
                    holdDurationMs = Mathf.Max(1f, holdTailAdjustedEndMs - startTime);
                    float initialLengthWorld = (holdDurationMs / 1000f) * speedWorldUnits;
                    if (initialLengthWorld < minimumHoldTailVisualLength)
                    {
                        initialLengthWorld = minimumHoldTailVisualLength;
                    }

                    EnsureHoldTailVisualExists();
                    if (holdTailObject != null && holdTailRenderer != null)
                    {
                        holdTailObject.transform.SetParent(this.transform, false);
                        holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
                        holdTailObject.transform.localRotation = Quaternion.identity;

                        Texture2D resolvedTexture;
                        Material material = ResolveHoldTailMaterial(noteData.hand == 0, out resolvedTexture);
                        if (material != null)
                        {
                            holdTailRenderer.sharedMaterial = material;
                            ApplyHoldTailMaterialProperties(noteData.hand == 0, resolvedTexture);
                        }

                        if (runtimeSpriteRenderer != null)
                        {
                            holdTailRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            // Match the note's sorting so the tail is not buried under track/background
                            holdTailRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder;
                        }
                        else if (noteRenderer != null)
                        {
                            holdTailRenderer.sortingLayerID = noteRenderer.sortingLayerID;
                            holdTailRenderer.sortingOrder = noteRenderer.sortingOrder;
                        }

                        SetHoldTailActive(true);
                        float tailWidthWorld = Mathf.Max(0.01f, width * holdTailWidthFactor);
                        UpdateHoldTailDimensions(tailWidthWorld, initialLengthWorld, true);
                    }

                    initialTailLength = initialLengthWorld;
                    currentTailLength = initialTailLength;
                }
                catch
                {
                    SetHoldTailActive(false);
                }
            }

            // Load hit sound once for all notes
            if (cachedHitSound == null)
            {
                cachedHitSound = Resources.Load<AudioClip>("Sound/Tap");
                if (cachedHitSound != null)
                {
                    // Debug.Log("NoteController: Hit sound loaded successfully");
                }
                else
                {
                    // Debug.LogWarning("NoteController: Hit sound not found at Resources/Sound/Tap");
                }
            }

            // Successful initialization
            // Ensure any previous judged/hidden state is cleared when the object is reused from the pool
            isJudged = false;
            softJudgedPendingRelease = false;
            // Re-enable the runtime sprite if present. For the original renderer, only enable it
            // when we're not using a runtime sprite (to avoid double-draw / visible quad behind sprite).
            if (runtimeSpriteRenderer != null)
            {
                runtimeSpriteRenderer.enabled = true;
            }
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null)
            {
                // We're using a sprite for rendering; ensure the original renderer (mesh) stays disabled
                if (noteRenderer != null && !(noteRenderer is SpriteRenderer)) noteRenderer.enabled = false;
            }
            else
            {
                // No runtime sprite assigned: ensure the original renderer is enabled so the note is visible
                if (noteRenderer != null) noteRenderer.enabled = true;
            }
            RefreshStaccatoIndicator();
            isInitialized = true;
            // Register with JudgmentManager so it can be found when player presses buttons
            try { JudgmentManager.Instance.RegisterNote(this); } catch { }
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                string spriteName = (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null) ? runtimeSpriteRenderer.sprite.name : "<none>";
                string rendererType = (noteRenderer != null) ? noteRenderer.GetType().Name : "<noRenderer>";
                // extra diagnostics: camera / spawn / timing info to help track invisible notes
                Camera cam = Camera.main;
                string camName = cam != null ? cam.name : "<noMainCam>";
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                float camFar = cam != null ? cam.farClipPlane : -1f;
                float effectiveSongPos = conductor != null ? conductor.effectiveSongPosition : -99999f;
                BuildLogger.Log($"[NoteController] Initialized: goId={gameObject.GetInstanceID()} start={startTime} end={endTime} type={noteData?.type} note_type={(noteData!=null?noteData.note_type:-1)} hand={(noteData!=null?noteData.hand:-1)} sprite={spriteName} renderer={rendererType} pos={transform.position} judgmentZ={judgmentZ} spawnZ={spawnZ} travelSeconds={(timeToStartMs/1000f):F3} initialSpeed={((noteSpawner!=null)?noteSpawner.speed:30f):F1} effectiveSongPos={effectiveSongPos} mainCam={camName} camPos={camPos} camFar={camFar}");
                if (runtimeSpriteRenderer != null)
                {
                    try { BuildLogger.Log($"[NoteController] SpriteRenderer: enabled={runtimeSpriteRenderer.enabled} sortingLayer={runtimeSpriteRenderer.sortingLayerID} order={runtimeSpriteRenderer.sortingOrder} bounds={runtimeSpriteRenderer.bounds}"); } catch { }
                }
                if (noteRenderer != null)
                {
                    try { BuildLogger.Log($"[NoteController] MeshRenderer: enabled={noteRenderer.enabled} bounds={noteRenderer.bounds}"); } catch { }
                }
            }
            catch { }
            #endif
        }
        catch
        {
            // Debug.LogError($"NoteController.Initialize: Exception caught during Initialize: {ex}\nNote data: {noteData}");
            isInitialized = false;
            return;
        }
    }

    // Called by pool when object is returned; cleans up transient children (holdTail) and resets state
    public void CleanupPooled()
    {
        try
        {
                    if (holdTailObject != null)
                    {
                        SetHoldTailActive(false);
                        holdTailObject.transform.localScale = Vector3.one;
                        holdTailObject.transform.localPosition = Vector3.zero;
                        if (owningPool != null && owningPool.Container != null)
                        {
                            holdTailObject.transform.SetParent(owningPool.Container, false);
                        }
                    }
        }
        catch
        {
            // Debug.LogWarning($"CleanupPooled: exception while cleaning tail: {ex}");
        }

    // Reset runtime state
    isInitialized = false;
    hasTriggeredHitSound = false;
    noteData = null;
    // reset cached soft flag
    isSoftCached = false;
    softJudgedPendingRelease = false;
    noteSpawner = null;
    cachedWorldWidth = 1f;
        // Reset cached tail length so next Initialize will reapply positions
        lastAppliedTailLength = -1f;
        currentTailLength = 0f;
        initialTailLength = 0f;
        currentTailWidthWorld = 0f;
        holdDurationMs = 0f;
        holdTailAdjustedEndMs = 0f;
    // Clear judged flag so pooled notes become active/visible when respawned
    isJudged = false;
        // unregister from JudgmentManager
        try { JudgmentManager.Instance.UnregisterNote(this); } catch { }
        HideStaccatoIndicator();
    }

    // Helper to release via PooledObject if available, otherwise destroy
    public void ReleaseOrDestroy()
    {
        // mark as not active for judgment
        isJudged = true;
        HideStaccatoIndicator();
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            // Try to get current songPos for diagnostic
            float songPosNow = float.NaN;
            try
            {
                if (noteData != null) startTime = noteData.startTime;
                var c = conductor != null ? conductor : (GameManager.Instance != null ? GameManager.Instance.Conductor : null);
                if (c != null) songPosNow = c.effectiveSongPosition;
            }
            catch { }

            string stack = System.Environment.StackTrace;
            // Debug.Log($"[NoteController] ReleaseOrDestroy: goId={gameObject.GetInstanceID()} isJudged={isJudged} owningPool={(owningPool!=null?"yes":"no")} songPosNow={songPosNow} startTime={startTime} posZ={transform.position.z} judgmentZ={judgmentZ}\nCallStack:\n{stack}");
        }
        catch { }
        #endif
        if (owningPool != null)
        {
            owningPool.Despawn(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Called by JudgmentManager when this note is judged
    public void OnJudged(JudgmentResult result)
    {
        if (isJudged) return;
        isJudged = true;
        if (isSoftCached)
        {
            // For soft notes, keep them visible and release when touching judgment line.
            softJudgedPendingRelease = true;
            return;
        }
        // hide visuals (sound is managed centrally by JudgmentManager)
        if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false;
        if (noteRenderer != null) noteRenderer.enabled = false;
        SetHoldTailActive(false);
        HideStaccatoIndicator();
        // Immediately release
        ReleaseOrDestroy();
    }

    // Called when a hold note is pressed at its head. Do not release the note here; keep it active until hold end.
    public void OnHoldStart(JudgmentResult startResult)
    {
        headPressed = true;
        // Do not mark as fully judged; play hit sound and ensure tail is visible
        try
        {
            // sound is played centrally by JudgmentManager; mark triggered to avoid any local replays
            hasTriggeredHitSound = true;
        }
        catch { }

        try
        {
            if (isStaccatoCached)
            {
                SetHoldTailActive(false);
                HideStaccatoIndicator();
            }
            else
            {
                SetHoldTailActive(true);
            }
        }
        catch { }

        if (isStaccatoCached)
        {
            try { if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false; } catch { }
            try { if (noteRenderer != null) noteRenderer.enabled = false; } catch { }
            HideStaccatoIndicator();
        }
    }

    // Called when the player releases a hold or the hold naturally ends. This finalizes visuals and releases the note.
    public void OnHoldEnd(JudgmentResult endResult)
    {
        if (isJudged) return;
        headPressed = false;
        // Tail/end judgement should NOT play a hit sound (head already played on start).
        // Removed PlayHitSound() here to avoid duplicate/undesired audio on release.

        if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false;
        if (noteRenderer != null) noteRenderer.enabled = false;
        SetHoldTailActive(false);
        HideStaccatoIndicator();
        ReleaseOrDestroy();
    }

    void Update()
    {
    if (!isInitialized) return;
    // Defensive: if noteData becomes null for any reason while marked initialized,
    // avoid NullReferenceExceptions and try to recover by releasing the object.
    if (noteData == null)
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try { BuildLogger.LogWarning($"[NoteController] Update: noteData is null for goId={gameObject.GetInstanceID()} - releasing to avoid NRE"); } catch { }
        #endif
        // Mark judged so it won't be considered active, then release to pool/destroy.
        isJudged = true;
        try { ReleaseOrDestroy(); } catch { }
        return;
    }
    // Try to ensure we have a conductor reference; do the expensive Find only once per instance.
    if (conductor == null && !didTryFindConductor)
    {
        didTryFindConductor = true;
        if (GameManager.Instance != null)
            conductor = GameManager.Instance.Conductor;
        else
            conductor = FindAnyObjectByType<Conductor>();
    }

        float songPos = (conductor != null) ? conductor.effectiveSongPosition : 0f;
        float currentSpeed = (noteSpawner != null) ? noteSpawner.speed : 30f;

        // Soft-only delayed disappearance:
        // judged soft notes stay visible until they hit the judgment line.
        if (isSoftCached && isJudged && softJudgedPendingRelease)
        {
            float timeToStartSoft = startTime - songPos;
            float targetZSoft = judgmentZ + (timeToStartSoft / 1000f) * currentSpeed;
            Vector3 posSoft = transform.position;
            float maxStepSoft = currentSpeed * Time.deltaTime;
            posSoft.z = Mathf.MoveTowards(posSoft.z, targetZSoft, maxStepSoft);
            if (Mathf.Abs(targetZSoft - posSoft.z) <= 0.0001f) posSoft.z = targetZSoft;
            transform.position = posSoft;

            bool reachedByTime = songPos >= startTime;
            bool reachedByPos = Mathf.Abs(posSoft.z - judgmentZ) <= 0.0005f;
            if (reachedByTime || reachedByPos)
            {
                if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false;
                if (noteRenderer != null) noteRenderer.enabled = false;
                SetHoldTailActive(false);
                HideStaccatoIndicator();
                softJudgedPendingRelease = false;
                TryReleaseOrDestroy("SoftJudged reached judgment line", songPos, currentSpeed);
            }
            return;
        }

        // Cache settings once per Update — avoids N×SettingsManager.Instance reads per active note per frame
        var _sm = SettingsManager.Instance;
        bool _debugMode = _sm != null && _sm.DebugMode;
        bool _judgmentMode = _sm != null && _sm.JudgmentMode;

        // --- Debug Mode Auto-Play Logic ---

    if (noteData.type == "tap" || isSoftCached || isStaccatoCached)
        {
            // If judgment mode is enabled, do not auto-trigger on reaching startTime.
            // Instead wait for player input; if the note passes beyond the 'good' window, count as fail.
            // If we are in normal JudgmentMode (player must press), enforce miss window.
            // If DebugMode is enabled, auto-judge as Perfect at startTime.
            if (_judgmentMode && !_debugMode)
            {
                if (songPos - startTime > (JudgmentManager.Instance != null ? JudgmentManager.Instance.goodMs : 150))
                {
                    // note missed (failed)
                    try { JudgmentManager.Instance.RecordFail(this); } catch { }
                        TryReleaseOrDestroy("MissTimeout: tap missed window", songPos, currentSpeed);
                    return;
                }
            }
            else
            {
                // Auto path: either not in JudgmentMode, or DebugMode enabled -> auto-trigger behavior
                if (songPos >= startTime)
                {
                    // In DebugMode we want an actual Perfect judgement (so counts and popups are emitted)
                        if (_debugMode)
                        {
                            // Snap head exactly to judgment line so disappearance corresponds to on-line visual
                            transform.position = new Vector3(transform.position.x, transform.position.y, judgmentZ);
                                    // Ensure visuals reflect being on the line; sound is handled by JudgmentManager
                                    if (!hasTriggeredHitSound) { hasTriggeredHitSound = true; }
                            try {
                                // For staccato notes in DebugMode, simulate a hold start so the manager
                                // will auto-finalize the tail at endTime. For normal taps, keep AutoJudgeTap.
                                if (isStaccatoCached)
                                {
                                    if (!headPressed)
                                    {
                                        JudgmentManager.Instance.AutoStartHold(this, startTime);
                                        headPressed = true; // mark as pressed so hold-logic applies
                                    }
                                }
                                else
                                {
                                    JudgmentManager.Instance.AutoJudgeTap(this);
                                }
                            } catch { }
                            return;
                        }

                    // Non-judgment-mode: previous behavior (snap & release)
                    if (!hasTriggeredHitSound)
                    {
                        // sound is handled centrally by JudgmentManager in judgment flows
                        hasTriggeredHitSound = true;
                    }
                    transform.position = new Vector3(transform.position.x, transform.position.y, judgmentZ);
                    TryReleaseOrDestroy("AutoSnap: tap reached startTime (non-judgment)", songPos, currentSpeed);
                    return;
                }
            }

            // Move deterministically toward the DSP-derived target using deltaTime to keep playback smooth.
            float timeToStart = startTime - songPos;
            float targetZ = judgmentZ + (timeToStart / 1000f) * currentSpeed;
            Vector3 pos = transform.position;
            float maxStep = currentSpeed * Time.deltaTime;
            pos.z = Mathf.MoveTowards(pos.z, targetZ, maxStep);
            // Snap if we are within an imperceptible range to guarantee precise alignment.
            if (Mathf.Abs(targetZ - pos.z) <= 0.0001f) pos.z = targetZ;
            transform.position = pos;
        }
    else if (noteData.type == "hold")
        {
            // Do NOT immediately destroy when songPos passes endTime. Instead, allow the tail to
            // smoothly shrink to zero, then destroy the GameObject once the tail is effectively gone.
                if (songPos < startTime)
            {
                // --- Before hold starts: Move the entire note ---
                float timeToStart = startTime - songPos;
                float headZ = judgmentZ + (timeToStart / 1000f) * currentSpeed;

                // Advance toward the DSP-derived head position using deltaTime while preserving exact alignment.
                Vector3 pos = transform.position;
                float maxStep = currentSpeed * Time.deltaTime;
                pos.z = Mathf.MoveTowards(pos.z, headZ, maxStep);
                if (Mathf.Abs(headZ - pos.z) <= 0.0001f) pos.z = headZ;
                transform.position = pos;
                if (holdTailObject != null)
                {
                    float widthWorld = currentTailWidthWorld > 0f
                        ? currentTailWidthWorld
                        : Mathf.Max(0.01f, cachedWorldWidth * holdTailWidthFactor);
                    currentTailLength = initialTailLength;
                    bool force = Mathf.Abs(lastAppliedTailLength - currentTailLength) > tailLengthUpdateThreshold;
                    UpdateHoldTailDimensions(widthWorld, currentTailLength, force);
                    SetHoldTailActive(!isStaccatoCached && currentTailLength > 0.0001f);
                }

            }
            else
            {
                bool debugModeActive = _debugMode;
                bool judgmentModeActive = _judgmentMode;

                // If DebugMode is enabled and we just reached the startTime, ensure the hold is auto-started
                if (debugModeActive && !headPressed)
                {
                    // start as if pressed at startTime so auto flow always runs before miss logic
                    try { JudgmentManager.Instance.AutoStartHold(this, startTime); } catch { }
                    headPressed = true; // mark so we don't re-trigger
                }

                // If the player hasn't pressed the head within the allowed judgment window (after start), mark as missed
                try
                {
                    if (!headPressed && !headMissed && (judgmentModeActive || debugModeActive))
                    {
                        float allowedMs = (JudgmentManager.Instance != null) ? JudgmentManager.Instance.goodMs : 150f;
                        if (songPos - startTime > allowedMs)
                        {
                            headMissed = true;
                            // immediately avoid snapping to judgment line; we'll let visual continue flowing
                            // show a Miss popup for the head immediately so start-judgement has an icon
                            try
                            {
                                var simple = SimpleJudgePopupManager.Instance;
                                if (simple != null) simple.ShowAtPosition(this.transform.position, JudgmentResult.Miss);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                // Trigger hit sound when hold note starts
                // If the head was missed, do not snap to judgment line nor play hit sound;
                // instead let the note continue flowing past the judgment line as if unjudged.
                if (!headMissed)
                {
                    if (!hasTriggeredHitSound)
                    {
                        // Head sound is managed centrally by JudgmentManager; mark as triggered locally
                        hasTriggeredHitSound = true;
                    }
                }

                // --- After hold starts: Shrink the note towards remaining time until endTime ---
                float adjustedEndMs2 = holdTailAdjustedEndMs;
                float remainingMsAdjusted = adjustedEndMs2 - songPos;
                float remainingRatio = holdDurationMs > 0f ? Mathf.Clamp01(remainingMsAdjusted / holdDurationMs) : 0f;
                float targetLengthWorld = Mathf.Max(0f, initialTailLength * remainingRatio);
                // If head was missed, in non-debug JudgmentMode we snap to judgment line so
                // the hold visually stays on the line and continues to receive per-beat ticks.
                // In DebugMode or non-judgment flows, preserve original "flowing past" behavior.
                if (headMissed)
                {
                    // Reuse outer scope's debugModeActive/judgmentModeActive variables
                    if (judgmentModeActive && !debugModeActive)
                    {
                        // Keep head locked on judgment line to allow continued per-beat judgement
                        Vector3 pos = transform.position;
                        pos.z = judgmentZ;
                        transform.position = pos;
                    }
                    else
                    {
                        // Legacy behavior: let head continue flowing past the line
                        float timeToStartAfter = startTime - songPos; // negative after start
                        float headZ = judgmentZ + (timeToStartAfter / 1000f) * currentSpeed;
                        Vector3 pos = transform.position;
                        float maxStepMiss = currentSpeed * Time.deltaTime;
                        pos.z = Mathf.MoveTowards(pos.z, headZ, maxStepMiss);
                        if (Mathf.Abs(headZ - pos.z) <= 0.0001f) pos.z = headZ;
                        transform.position = pos;
                    }
                }
                else
                {
                    // Head stays at judgment line while the player is holding
                    Vector3 pos = transform.position;
                    pos.z = judgmentZ;
                    transform.position = pos;
                }
                if (holdTailObject != null)
                {
                    float widthWorld = currentTailWidthWorld > 0f
                        ? currentTailWidthWorld
                        : Mathf.Max(0.01f, cachedWorldWidth * holdTailWidthFactor);

                    float previousLength = currentTailLength;
                    float shrinkRate = Mathf.Max(holdTailShrinkSpeed, currentSpeed);
                    currentTailLength = Mathf.MoveTowards(previousLength, targetLengthWorld, shrinkRate * Time.deltaTime);

                    bool forceUpdate = Mathf.Abs(lastAppliedTailLength - currentTailLength) > tailLengthUpdateThreshold;
                    UpdateHoldTailDimensions(widthWorld, currentTailLength, forceUpdate);
                    SetHoldTailActive(!isStaccatoCached && currentTailLength > 0.0001f);

                    if (targetLengthWorld <= 0.001f)
                    {
                        SetHoldTailActive(false);
                        // In non-judgment (non-debug) mode, do NOT release immediately even if head was missed.
                        // Allow the hold to continue until endTime so player can still re-press and recover.
                        bool isNonDebugMode = !_debugMode && _judgmentMode;

                        if (headMissed && !isNonDebugMode)
                        {
                            // head was missed AND (debug mode or non-judgment) -> release when tail ends
                            TryReleaseOrDestroy("Hold tail ended: headMissed", songPos, currentSpeed);
                            return;
                        }
                        if (headMissed && isNonDebugMode)
                        {
                            // In non-debug judgment mode: keep the note alive until endTime to allow recovery
                            // Tail is now invisible but the hold state persists in JudgmentManager
                        }
                        else if (_judgmentMode)
                        {
                            // In judgment mode (no headMissed), defer immediate release; allow endTime handling to decide fail timeout
                        }
                        else
                        {
                            TryReleaseOrDestroy("Hold tail ended: non-judgment", songPos, currentSpeed);
                            return;
                        }
                    }
                }
                // If we've passed or reached the endTime
                if (songPos >= endTime)
                {
                    if (headMissed)
                    {
                        // For non-debug JudgmentMode: do NOT locally release here. Defer finalization
                        // to JudgmentManager (which uses endTime + goodMs grace) so the player can
                        // re-press and recover; do not destroy the note locally.
                        // Reuse outer scope variables debugModeActive/judgmentModeActive
                        if (judgmentModeActive && !debugModeActive)
                        {
                            // Defer release until manager's grace window expires; but if we've
                            // already passed the grace window locally, release here to avoid
                            // stuck notes when no manager state exists.
                            float grace = (JudgmentManager.Instance != null) ? JudgmentManager.Instance.goodMs : 150f;
                            if (songPos - endTime > grace)
                            {
                                try
                                {
                                    var simple = SimpleJudgePopupManager.Instance;
                                    if (simple != null) simple.ShowAtPosition(this.transform.position, JudgmentResult.Miss);
                                }
                                catch { }
                                TryReleaseOrDestroy("Hold end: headMissed (grace expired)", songPos, currentSpeed);
                                return;
                            }
                            // otherwise defer to manager
                            return;
                        }
                        else
                        {
                            // Legacy behavior: show end Miss popup then release immediately
                            try
                            {
                                var simple = SimpleJudgePopupManager.Instance;
                                if (simple != null) simple.ShowAtPosition(this.transform.position, JudgmentResult.Miss);
                            }
                            catch { }
                            TryReleaseOrDestroy("Hold end: headMissed", songPos, currentSpeed);
                            return;
                        }
                    }
                    if (debugModeActive)
                    {
                        // In DebugMode let JudgmentManager's auto-finalize logic handle the tail; no local fail needed
                        return;
                    }
                    if (judgmentModeActive)
                    {
                        if (songPos - endTime > (JudgmentManager.Instance != null ? JudgmentManager.Instance.goodMs : 150))
                        {
                            try { JudgmentManager.Instance.RecordFail(this); } catch { }
                            TryReleaseOrDestroy("Hold end: fail timeout", songPos, currentSpeed);
                            return;
                        }
                        // otherwise keep head at judgment line until timeout
                    }
                    else
                    {
                        TryReleaseOrDestroy("Hold end: non-judgment immediate release", songPos, currentSpeed);
                        return;
                    }
                }
            }
        }

        // Failsafe to destroy notes that are long past
        if (songPos > endTime + 500)
        {
            TryReleaseOrDestroy("Failsafe: past endTime+500", songPos, currentSpeed);
        }
    }

    private void RefreshStaccatoIndicator()
    {
        if (!isStaccatoCached || noteData == null)
        {
            HideStaccatoIndicator();
            return;
        }

        Sprite indicatorSprite = null;
        try
        {
            int hand = noteData.hand;
            indicatorSprite = (hand == 0) ? rightStaccatoIndicatorSprite : leftStaccatoIndicatorSprite;
        }
        catch
        {
            indicatorSprite = rightStaccatoIndicatorSprite != null ? rightStaccatoIndicatorSprite : leftStaccatoIndicatorSprite;
        }

        if (indicatorSprite == null)
        {
            HideStaccatoIndicator();
            return;
        }

        if (staccatoIndicatorInstance == null)
        {
            staccatoIndicatorInstance = new GameObject("StaccatoIndicator");
            staccatoIndicatorInstance.transform.SetParent(this.transform, false);
            staccatoIndicatorRenderer = staccatoIndicatorInstance.AddComponent<SpriteRenderer>();
            staccatoIndicatorController = staccatoIndicatorInstance.AddComponent<StaccatoIndicatorBillboard>();
        }
        else
        {
            if (staccatoIndicatorRenderer == null)
            {
                staccatoIndicatorRenderer = staccatoIndicatorInstance.GetComponent<SpriteRenderer>();
                if (staccatoIndicatorRenderer == null)
                {
                    staccatoIndicatorRenderer = staccatoIndicatorInstance.AddComponent<SpriteRenderer>();
                }
            }

            if (staccatoIndicatorController == null)
            {
                staccatoIndicatorController = staccatoIndicatorInstance.GetComponent<StaccatoIndicatorBillboard>();
                if (staccatoIndicatorController == null)
                {
                    staccatoIndicatorController = staccatoIndicatorInstance.AddComponent<StaccatoIndicatorBillboard>();
                }
            }
        }

        if (staccatoIndicatorRenderer == null)
        {
            HideStaccatoIndicator();
            return;
        }

        staccatoIndicatorRenderer.sprite = indicatorSprite;
        staccatoIndicatorRenderer.enabled = true;
        staccatoIndicatorRenderer.color = Color.white;

        int sortingLayerId = 0;
        int sortingOrder = 0;
        if (runtimeSpriteRenderer != null)
        {
            sortingLayerId = runtimeSpriteRenderer.sortingLayerID;
            sortingOrder = runtimeSpriteRenderer.sortingOrder;
        }
        else if (noteRenderer != null)
        {
            sortingLayerId = noteRenderer.sortingLayerID;
            sortingOrder = noteRenderer.sortingOrder;
        }

        staccatoIndicatorRenderer.sortingLayerID = sortingLayerId;
        staccatoIndicatorRenderer.sortingOrder = sortingOrder + 5;

        Vector3 parentScale = transform.lossyScale;
        float noteWorldWidth = Mathf.Max(0.0001f, cachedWorldWidth);
        float widthScaleFactor = Mathf.Max(0.01f, staccatoIndicatorScale);
        float targetWorldWidth = Mathf.Max(0.0001f, noteWorldWidth * widthScaleFactor);
        float targetWorldHeight = Mathf.Max(0.01f, staccatoIndicatorHeight);

        float spriteWidthUnits = 1f;
        float spriteHeightUnits = 1f;
        if (staccatoIndicatorRenderer.sprite != null)
        {
            var spriteBounds = staccatoIndicatorRenderer.sprite.bounds;
            spriteWidthUnits = Mathf.Max(0.0001f, spriteBounds.size.x);
            spriteHeightUnits = Mathf.Max(0.0001f, spriteBounds.size.y);
        }

        float parentScaleX = Mathf.Max(0.0001f, Mathf.Abs(parentScale.x));
        float parentScaleY = Mathf.Max(0.0001f, Mathf.Abs(parentScale.y));
        float parentScaleZ = Mathf.Max(0.0001f, Mathf.Abs(parentScale.z));

        float localX = targetWorldWidth / (spriteWidthUnits * parentScaleX);
        float localY = targetWorldHeight / (spriteHeightUnits * parentScaleY);
        float localZ = targetWorldWidth / (spriteWidthUnits * parentScaleZ);
        staccatoIndicatorInstance.transform.localScale = new Vector3(localX, localY, localZ);
        staccatoIndicatorInstance.transform.localRotation = Quaternion.identity;

        if (staccatoIndicatorController != null)
        {
            Transform trackSpace = (noteSpawner != null) ? noteSpawner.trackTransform : null;
            staccatoIndicatorController.Configure(
                this.transform,
                staccatoIndicatorOffset,
                staccatoIndicatorUpright,
                staccatoIndicatorOffsetRelativeToWidth ? this : null,
                staccatoIndicatorOffsetRelativeToWidth,
                trackSpace
            );
        }
        else
        {
            staccatoIndicatorInstance.transform.position = transform.position + staccatoIndicatorOffset;
        }

        staccatoIndicatorInstance.SetActive(true);
    }

    private void HideStaccatoIndicator()
    {
        if (staccatoIndicatorRenderer != null)
        {
            staccatoIndicatorRenderer.enabled = false;
        }
        if (staccatoIndicatorInstance != null)
        {
            staccatoIndicatorInstance.SetActive(false);
        }
    }

    // Defensive release wrapper: log reasons and avoid releasing a note until it's actually
    // reached the judgment line (to avoid premature disappearance). This is conservative
    // and only defers release for a short time while the note approaches the judgment Z.
    private bool deferredReleaseLogged = false;
    private void TryReleaseOrDestroy(string reason, float songPos, float currentSpeed)
    {
        // If already judged, allow release
        if (isJudged)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[NoteController] TryReleaseOrDestroy(allow) reason={reason} goId={gameObject.GetInstanceID()} isJudged={isJudged} posZ={transform.position.z} judgmentZ={judgmentZ} songPos={songPos} startTime={startTime}"); } catch { }
            // #endif
            ReleaseOrDestroy();
            return;
        }

        // Compute expected Z for the note head based on songPos and currentSpeed.
        // targetZ = judgmentZ + ((startTime - songPos)/1000f) * currentSpeed
        float targetZ = judgmentZ + ((startTime - songPos) / 1000f) * currentSpeed;
        float deltaToTarget = Mathf.Abs(transform.position.z - targetZ);
        // thresholds: z threshold (world units) to allow release when near expected position
        const float zThreshold = 0.05f; // world units
        // allow immediate release if the note is very close to where it should be
        if (deltaToTarget <= zThreshold)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[NoteController] TryReleaseOrDestroy(permitted) reason={reason} goId={gameObject.GetInstanceID()} deltaToTarget={deltaToTarget} targetZ={targetZ} posZ={transform.position.z} songPos={songPos} startTime={startTime}"); } catch { }
            // #endif
            ReleaseOrDestroy();
            deferredReleaseLogged = false;
            return;
        }

        // If we've already passed far beyond endTime, allow release regardless (failsafe)
        if (songPos > endTime + 500)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[NoteController] TryReleaseOrDestroy(failsafeRelease) reason={reason} goId={gameObject.GetInstanceID()} songPos={songPos} endTime={endTime}"); } catch { }
            // #endif
            ReleaseOrDestroy();
            deferredReleaseLogged = false;
            return;
        }

        // Otherwise defer release and log once to avoid spamming.
        if (!deferredReleaseLogged)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.LogWarning($"[NoteController] Deferred release for goId={gameObject.GetInstanceID()} reason={reason} posZ={transform.position.z} judgmentZ={judgmentZ} deltaToTarget={deltaToTarget} songPos={songPos} startTime={startTime}"); } catch { }
            // #endif
            deferredReleaseLogged = true;
        }
        // Do not call ReleaseOrDestroy now; Update will be called next frame and this guard
        // will be re-evaluated. If the situation persists far beyond expected time, the
        // existing failsafe (songPos > endTime + 500) will eventually force a release.
    }
    
    /// <summary>
    /// Plays hit sound effect using GameManager's HitSoundManager or fallback to AudioSource.PlayClipAtPoint
    /// </summary>
    private void PlayHitSound()
    {
        if (cachedHitSound == null)
        {
            // Debug.LogWarning("PlayHitSound: cachedHitSound is null!");
            return;
        }
        
        // Development-only diagnostics: log caller context to help find unexpected release sounds
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            string t = noteData != null ? (noteData.type ?? "<null>") : "<noNoteData>";
            int nt = (noteData != null) ? noteData.note_type : -1;
            BuildLogger.Log($"[NoteController] PlayHitSound invoked: type={t} note_type={nt} start={startTime} end={endTime} headPressed={headPressed} isStaccato={isStaccatoCached} hasTriggeredHitSound={hasTriggeredHitSound}\nCallStack:\n{System.Environment.StackTrace}");
        }
        catch { }
        #endif
        // NOTE: Local per-note playback is intentionally disabled.
        // All hit sound playback is centralized in JudgmentManager.PlayHitSoundCentral()
        // to avoid duplicate or unexpected audio (tails/hold-ends). Keep the
        // diagnostic log above so developers can still trace who invoked this
        // method, but do not actually play any clip here.
        return;
    }

    // Create or retrieve a cached Sprite for the given Texture2D to avoid repeated allocations
    private Sprite GetOrCreateSprite(Texture2D tex)
    {
        if (tex == null) return null;
        Sprite s;
        if (spriteCache.TryGetValue(tex, out s)) return s;
        try
        {
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            spriteCache[tex] = s;
            return s;
        }
        catch
        {
            // Debug.LogWarning($"GetOrCreateSprite failed for texture {tex.name}: {ex}");
            return null;
        }
    }

    // Preload shared resources used by NoteController instances to avoid hitches during pre-roll.
    // Call this before visual pre-roll so static cached assets and sprite cache are warmed.
    public static void PreloadSharedResources(GameObject notePrefab)
    {
        try
        {
            if (cachedHitSound == null)
            {
                cachedHitSound = Resources.Load<AudioClip>("Sound/Tap");
            }

            if (notePrefab == null) return;

            // Inspect the prefab for NoteController and any configured textures/sprites
            var nc = notePrefab.GetComponentInChildren<NoteController>();
            if (nc == null) return;

            // Pre-cache textures -> sprites using the same logic as instance method to avoid duplication
            try
            {
                if (nc.softTexture != null && !spriteCache.ContainsKey(nc.softTexture))
                {
                    var s = Sprite.Create(nc.softTexture, new Rect(0, 0, nc.softTexture.width, nc.softTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    spriteCache[nc.softTexture] = s;
                }
                if (nc.rightHandTexture != null && !spriteCache.ContainsKey(nc.rightHandTexture))
                {
                    var s = Sprite.Create(nc.rightHandTexture, new Rect(0, 0, nc.rightHandTexture.width, nc.rightHandTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    spriteCache[nc.rightHandTexture] = s;
                }
                if (nc.leftHandTexture != null && !spriteCache.ContainsKey(nc.leftHandTexture))
                {
                    var s = Sprite.Create(nc.leftHandTexture, new Rect(0, 0, nc.leftHandTexture.width, nc.leftHandTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    spriteCache[nc.leftHandTexture] = s;
                }
                // If softSprite (Sprite) exists on prefab, ensure it's usable by leaving as-is (no creation needed)
            }
            catch { }
        }
        catch { }
    }
}

/// <summary>
/// Keeps a staccato indicator hovering above its parent note and facing the camera.
/// </summary>
public class StaccatoIndicatorBillboard : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.6f, 0f);
    [Tooltip("When enabled, ignores vertical tilt and only rotates around the Y axis.")]
    [SerializeField] private bool freezeYawOnly = false;
    [SerializeField] private bool scaleOffsetByWidth = false;

    private NoteController widthSource;
    private Transform coordinateSpace;

    private Camera cachedCamera;

    public void Configure(Transform newTarget, Vector3 newOffset, bool freezeYaw, NoteController newWidthSource, bool scaleByWidth, Transform newCoordinateSpace)
    {
        target = newTarget;
        offset = newOffset;
        freezeYawOnly = freezeYaw;
        widthSource = (scaleByWidth && newWidthSource != null) ? newWidthSource : null;
        scaleOffsetByWidth = scaleByWidth && newWidthSource != null;
        coordinateSpace = newCoordinateSpace;
        UpdateTransform();
    }

    public void SetTarget(Transform newTarget, Vector3 newOffset)
    {
        Configure(newTarget, newOffset, freezeYawOnly, widthSource, scaleOffsetByWidth, coordinateSpace);
    }

    private void OnEnable()
    {
        RefreshCamera();
        UpdateTransform();
    }

    private void LateUpdate()
    {
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        Transform basis = coordinateSpace != null ? coordinateSpace : target;
        Vector3 upVector = Vector3.up;
        Vector3 forwardOnPlane = Vector3.forward;
        Vector3 rightOnPlane = Vector3.right;

        if (basis != null)
        {
            upVector = basis.up.sqrMagnitude > 1e-6f ? basis.up.normalized : Vector3.up;

            Vector3 candidateForward = Vector3.ProjectOnPlane(basis.forward, upVector);
            if (candidateForward.sqrMagnitude > 1e-6f)
            {
                forwardOnPlane = candidateForward.normalized;
            }

            Vector3 candidateRight = Vector3.ProjectOnPlane(basis.right, upVector);
            if (candidateRight.sqrMagnitude > 1e-6f)
            {
                rightOnPlane = candidateRight.normalized;
            }
            else
            {
                rightOnPlane = Vector3.Cross(forwardOnPlane, upVector).normalized;
            }

            float widthScale = 1f;
            if (scaleOffsetByWidth && widthSource != null)
            {
                try
                {
                    widthScale = Mathf.Max(0.0001f, widthSource.GetCurrentWorldWidth());
                }
                catch
                {
                    widthScale = 1f;
                }
            }

            float xOffset = offset.x * (widthScale * 0.5f);
            Vector3 worldOffset = (rightOnPlane * xOffset) + (upVector * offset.y) + (forwardOnPlane * offset.z);
            if (target != null)
            {
                transform.position = target.position + worldOffset;
            }
            else
            {
                transform.position = basis.position + worldOffset;
            }
        }

        Vector3 yawDir = forwardOnPlane;
        if (yawDir.sqrMagnitude < 1e-6f && basis != null)
        {
            yawDir = Vector3.ProjectOnPlane(basis.forward, upVector);
        }
        if (yawDir.sqrMagnitude < 1e-6f)
        {
            yawDir = Vector3.forward;
        }

        Quaternion yawRotation = Quaternion.LookRotation(yawDir.normalized, upVector);

        float slopeDegrees;
        var settingsManager = SettingsManager.Instance;
        if (settingsManager != null)
        {
            slopeDegrees = settingsManager.CameraRotX;
        }
        else
        {
            var cam = GetActiveCamera();
            if (cam != null)
            {
                slopeDegrees = Mathf.Abs(cam.transform.eulerAngles.x);
                if (slopeDegrees > 180f)
                {
                    slopeDegrees = 360f - slopeDegrees;
                }
            }
            else
            {
                slopeDegrees = 90f;
            }
        }
        slopeDegrees = Mathf.Clamp(slopeDegrees, 0f, 90f);

        Quaternion pitchRotation = Quaternion.AngleAxis(slopeDegrees, rightOnPlane);

        transform.rotation = pitchRotation * yawRotation;
    }

    private Camera GetActiveCamera()
    {
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
        {
            return cachedCamera;
        }

        RefreshCamera();
        return cachedCamera;
    }

    private void RefreshCamera()
    {
        cachedCamera = Camera.main;
        if (cachedCamera == null)
        {
            var cameras = Camera.allCameras;
            if (cameras != null && cameras.Length > 0)
            {
                cachedCamera = cameras[0];
            }
        }
    }
}
