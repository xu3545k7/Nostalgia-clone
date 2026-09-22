using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TrackMarbleThemeController : MonoBehaviour
{
    [SerializeField] private Renderer trackRenderer;
    [SerializeField] private TrackMarbleTheme previewTheme = TrackMarbleTheme.Black;
    [SerializeField, Tooltip("Move the marble pattern toward the player at exactly the note travel speed.")]
    private bool syncScrollWithNotes = true;

    [Header("Physical Track Thickness")]
    [SerializeField] private bool enableTrackThickness = true;
    [SerializeField, Range(0.12f, 1.5f)] private float trackThicknessWorld = 0.62f;
    [SerializeField, Range(0.04f, 0.8f)] private float judgmentEdgeDepthWorld = 0.22f;

    // The edge trim is a full-width bar lying on the judgment line, built at
    // metallic 0.82 / smoothness 0.78 — mirror-like. A mirror's brightness has
    // nothing to do with its own colour: this one is dark brass (luminance 0.20)
    // yet rendered as a bright line straight across the track whenever what it
    // reflected was bright, a video background above all. Dark and brightly-lit
    // at once is what made it so hard to find, because every search for
    // "something bright" reads a colour and skips it.
    [SerializeField]
    [Tooltip("Draw the brass trim along the judgment line. Off by default: it is a mirror-finish bar spanning the full track width, and it reflects a bright background as a bright line lying across the track.")]
    private bool showJudgmentEdge = false;

    private static readonly int ThemeId = Shader.PropertyToID("_Theme");
    private static readonly int ScrollOffsetId = Shader.PropertyToID("_ScrollOffset");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private MaterialPropertyBlock propertyBlock;
    private MeshFilter meshFilter;
    private Conductor conductor;
    private float accumulatedWorldDistance;
    private float lastSongPositionMs;
    private bool timingWasActive;
    private GameObject thicknessObject;
    private Renderer thicknessRenderer;
    private Mesh thicknessMesh;
    private GameObject judgmentEdgeObject;
    private Material judgmentEdgeMaterial;
    private float currentScrollOffset;
    private TrackMarbleTheme? lastReportedTheme;

    private void OnEnable()
    {
        if (Application.isPlaying) EnsureTrackThickness();
        TrackMarbleTheme theme = previewTheme;
        if (Application.isPlaying && SettingsManager.Instance != null)
        {
            theme = SettingsManager.Instance.CurrentTrackMarbleTheme;
        }
        SetTheme(theme);
    }

    private void LateUpdate()
    {
        if (Application.isPlaying && enableTrackThickness && thicknessRenderer == null)
            EnsureTrackThickness();
        if (!Application.isPlaying || !syncScrollWithNotes || trackRenderer == null) return;

        if (conductor == null)
        {
            conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
            if (conductor == null) conductor = FindFirstObjectByType<Conductor>();
        }

        bool timingActive = conductor != null && conductor.isActive;
        if (!timingActive)
        {
            timingWasActive = false;
            accumulatedWorldDistance = 0f;
            ApplyScrollOffset(0f);
            return;
        }

        float songPositionMs = conductor.renderSongPosition;
        if (!timingWasActive)
        {
            timingWasActive = true;
            lastSongPositionMs = songPositionMs;
            return;
        }

        float deltaMs = songPositionMs - lastSongPositionMs;
        lastSongPositionMs = songPositionMs;

        // A negative/very large jump means restart or seek; avoid flashing across the Track.
        if (deltaMs < -100f || deltaMs > 1000f)
        {
            accumulatedWorldDistance = 0f;
            ApplyScrollOffset(0f);
            return;
        }

        float noteSpeed = ResolveNoteSpeed();
        accumulatedWorldDistance += Mathf.Max(0f, deltaMs) * 0.001f * noteSpeed;

        float trackLength = ResolveTrackWorldLength();
        float textureTiling = ResolveTextureTiling();
        // The authored Plane UV points toward the player in increasing V. Shift
        // the sample backwards so the visible marble pattern itself travels
        // from the far end toward the judgment line with descending notes.
        float offset = -Mathf.Repeat(accumulatedWorldDistance * textureTiling / trackLength, 1f);
        ApplyScrollOffset(offset);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            SetTheme(previewTheme);
        }
    }

    public void SetTheme(TrackMarbleTheme theme)
    {
        previewTheme = theme;
        if (trackRenderer == null)
        {
            trackRenderer = GetComponent<Renderer>();
        }
        if (trackRenderer == null) return;

        propertyBlock ??= new MaterialPropertyBlock();
        trackRenderer.GetPropertyBlock(propertyBlock, 0);
        propertyBlock.SetFloat(ThemeId, theme == TrackMarbleTheme.White ? 1f : 0f);
        trackRenderer.SetPropertyBlock(propertyBlock, 0);
        ApplyThemeToRenderer(thicknessRenderer, theme);

        if (Application.isPlaying && lastReportedTheme != theme)
        {
            Material activeMaterial = trackRenderer.sharedMaterial;
            string shaderName = activeMaterial != null && activeMaterial.shader != null
                ? activeMaterial.shader.name : "<missing>";
            Texture stoneTexture = activeMaterial != null && activeMaterial.HasProperty(BaseMapId)
                ? activeMaterial.GetTexture(BaseMapId) : null;
            Debug.Log($"[TrackMarbleTheme] applied={theme}, shader={shaderName}, " +
                      $"texture={(stoneTexture != null ? stoneTexture.name : "<missing>")}");
            lastReportedTheme = theme;
        }
    }

    private float ResolveNoteSpeed()
    {
        if (GameManager.Instance != null && GameManager.Instance.NoteSpawner != null)
        {
            return Mathf.Max(0f, GameManager.Instance.NoteSpawner.speed);
        }
        if (UIManager.Instance != null)
        {
            return Mathf.Max(0f, UIManager.Instance.GetCurrentSpeed());
        }
        return SettingsManager.Instance != null ? Mathf.Max(0f, SettingsManager.Instance.DefaultSpeed) : 70f;
    }

    private float ResolveTrackWorldLength()
    {
        meshFilter ??= GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return Mathf.Max(0.001f,
                meshFilter.sharedMesh.bounds.size.z * Mathf.Abs(transform.lossyScale.z));
        }
        return 100f;
    }

    private float ResolveTextureTiling()
    {
        Material material = trackRenderer != null ? trackRenderer.sharedMaterial : null;
        if (material != null && material.HasProperty(BaseMapId))
        {
            return Mathf.Max(0.001f, Mathf.Abs(material.GetTextureScale(BaseMapId).y));
        }
        return 1f;
    }

    private void ApplyScrollOffset(float offset)
    {
        currentScrollOffset = offset;
        if (trackRenderer == null) return;
        propertyBlock ??= new MaterialPropertyBlock();
        trackRenderer.GetPropertyBlock(propertyBlock, 0);
        propertyBlock.SetFloat(ScrollOffsetId, offset);
        trackRenderer.SetPropertyBlock(propertyBlock, 0);
        ApplyScrollToRenderer(thicknessRenderer, offset);
    }

    private void EnsureTrackThickness()
    {
        if (!enableTrackThickness || thicknessRenderer != null) return;
        trackRenderer ??= GetComponent<Renderer>();
        meshFilter ??= GetComponent<MeshFilter>();
        if (trackRenderer == null || meshFilter == null || meshFilter.sharedMesh == null) return;

        GameObject lineObject = GameObject.Find("JudgmentLine");
        Camera camera = Camera.main;
        if (lineObject == null || camera == null) return;

        Bounds bounds = meshFilter.sharedMesh.bounds;
        float lineZ = Mathf.Clamp(
            transform.InverseTransformPoint(lineObject.transform.position).z,
            bounds.min.z, bounds.max.z);
        Vector3 minEndWorld = transform.TransformPoint(
            new Vector3(bounds.center.x, bounds.center.y, bounds.min.z));
        Vector3 maxEndWorld = transform.TransformPoint(
            new Vector3(bounds.center.x, bounds.center.y, bounds.max.z));
        float farZ = Vector3.SqrMagnitude(maxEndWorld - camera.transform.position) >=
                     Vector3.SqrMagnitude(minEndWorld - camera.transform.position)
            ? bounds.max.z
            : bounds.min.z;
        float localRunLength = Mathf.Abs(farZ - lineZ);
        if (localRunLength < 0.01f) return;

        float parentYScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        float parentZScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        float localThickness = trackThicknessWorld / parentYScale;
        float localEdgeDepth = judgmentEdgeDepthWorld / parentZScale;
        float direction = Mathf.Sign(farZ - lineZ);

        thicknessMesh = BuildTrackWallMesh(bounds, lineZ, farZ, localThickness);
        thicknessObject = new GameObject("Track Thickness Runtime");
        thicknessObject.name = "Track Thickness Runtime";
        thicknessObject.hideFlags = HideFlags.DontSave;
        thicknessObject.transform.SetParent(transform, false);
        thicknessObject.AddComponent<MeshFilter>().sharedMesh = thicknessMesh;
        thicknessRenderer = thicknessObject.AddComponent<MeshRenderer>();
        thicknessRenderer.sharedMaterial = trackRenderer.sharedMaterial;
        thicknessRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        thicknessRenderer.receiveShadows = false;

        if (!showJudgmentEdge)
        {
            // Nothing is created at all, rather than created and hidden: the
            // object is built with HideFlags.DontSave, so a disabled one cannot
            // be turned off in the scene and would come back every play.
            ApplyThemeToRenderer(thicknessRenderer, previewTheme);
            ApplyScrollToRenderer(thicknessRenderer, currentScrollOffset);
            return;
        }

        judgmentEdgeObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        judgmentEdgeObject.name = "Track Judgment Edge Runtime";
        judgmentEdgeObject.hideFlags = HideFlags.DontSave;
        judgmentEdgeObject.transform.SetParent(transform, false);
        judgmentEdgeObject.transform.localPosition = new Vector3(
            bounds.center.x,
            bounds.center.y + localThickness * 0.88f,
            lineZ + direction * localEdgeDepth * 0.5f);
        judgmentEdgeObject.transform.localScale = new Vector3(
            bounds.size.x * 1.006f, localThickness * 0.24f, localEdgeDepth);
        Collider edgeCollider = judgmentEdgeObject.GetComponent<Collider>();
        if (edgeCollider != null) Destroy(edgeCollider);
        Renderer edgeRenderer = judgmentEdgeObject.GetComponent<Renderer>();
        edgeRenderer.sharedMaterial = GetOrCreateJudgmentEdgeMaterial();
        edgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        edgeRenderer.receiveShadows = false;

        ApplyThemeToRenderer(thicknessRenderer, previewTheme);
        ApplyScrollToRenderer(thicknessRenderer, currentScrollOffset);
    }

    private static Mesh BuildTrackWallMesh(Bounds bounds, float lineZ, float farZ,
        float localThickness)
    {
        float y0 = bounds.center.y;
        float y1 = y0 + localThickness;
        float x0 = bounds.min.x;
        float x1 = bounds.max.x;
        var vertices = new List<Vector3>(16);
        var uvs = new List<Vector2>(16);
        var triangles = new List<int>(24);

        AddWallQuad(vertices, uvs, triangles,
            new Vector3(x0, y0, lineZ), new Vector3(x1, y0, lineZ),
            new Vector3(x0, y1, lineZ), new Vector3(x1, y1, lineZ));
        AddWallQuad(vertices, uvs, triangles,
            new Vector3(x1, y0, farZ), new Vector3(x0, y0, farZ),
            new Vector3(x1, y1, farZ), new Vector3(x0, y1, farZ));
        AddWallQuad(vertices, uvs, triangles,
            new Vector3(x0, y0, farZ), new Vector3(x0, y0, lineZ),
            new Vector3(x0, y1, farZ), new Vector3(x0, y1, lineZ));
        AddWallQuad(vertices, uvs, triangles,
            new Vector3(x1, y0, lineZ), new Vector3(x1, y0, farZ),
            new Vector3(x1, y1, lineZ), new Vector3(x1, y1, farZ));

        var mesh = new Mesh
        {
            name = "Track Upward Thickness Walls",
            hideFlags = HideFlags.DontSave
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddWallQuad(List<Vector3> vertices, List<Vector2> uvs,
        List<int> triangles, Vector3 bottomLeft, Vector3 bottomRight,
        Vector3 topLeft, Vector3 topRight)
    {
        int start = vertices.Count;
        vertices.Add(bottomLeft);
        vertices.Add(bottomRight);
        vertices.Add(topLeft);
        vertices.Add(topRight);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private Material GetOrCreateJudgmentEdgeMaterial()
    {
        if (judgmentEdgeMaterial != null) return judgmentEdgeMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        judgmentEdgeMaterial = new Material(shader)
        {
            name = "Track Dark Brass Edge (Runtime)",
            hideFlags = HideFlags.DontSave,
            renderQueue = 1995
        };
        Color brass = new Color(0.31f, 0.17f, 0.055f, 1f);
        if (judgmentEdgeMaterial.HasProperty("_BaseColor"))
            judgmentEdgeMaterial.SetColor("_BaseColor", brass);
        if (judgmentEdgeMaterial.HasProperty("_Color"))
            judgmentEdgeMaterial.SetColor("_Color", brass);
        // Not the original 0.82/0.78. Turning the trim back on should not bring
        // the mirror back with it.
        if (judgmentEdgeMaterial.HasProperty("_Metallic"))
            judgmentEdgeMaterial.SetFloat("_Metallic", 0.35f);
        if (judgmentEdgeMaterial.HasProperty("_Smoothness"))
            judgmentEdgeMaterial.SetFloat("_Smoothness", 0.35f);
        if (judgmentEdgeMaterial.HasProperty("_EmissionColor"))
            judgmentEdgeMaterial.SetColor("_EmissionColor",
                new Color(0.025f, 0.010f, 0.001f, 1f));
        judgmentEdgeMaterial.EnableKeyword("_EMISSION");
        return judgmentEdgeMaterial;
    }

    private void ApplyThemeToRenderer(Renderer target, TrackMarbleTheme theme)
    {
        if (target == null) return;
        propertyBlock ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(propertyBlock, 0);
        propertyBlock.SetFloat(ThemeId, theme == TrackMarbleTheme.White ? 1f : 0f);
        target.SetPropertyBlock(propertyBlock, 0);
    }

    private void ApplyScrollToRenderer(Renderer target, float offset)
    {
        if (target == null) return;
        propertyBlock ??= new MaterialPropertyBlock();
        target.GetPropertyBlock(propertyBlock, 0);
        propertyBlock.SetFloat(ScrollOffsetId, offset);
        target.SetPropertyBlock(propertyBlock, 0);
    }

    private void OnDestroy()
    {
        if (judgmentEdgeMaterial != null)
        {
            if (Application.isPlaying) Destroy(judgmentEdgeMaterial);
            else DestroyImmediate(judgmentEdgeMaterial);
        }
        if (thicknessMesh != null)
        {
            if (Application.isPlaying) Destroy(thicknessMesh);
            else DestroyImmediate(thicknessMesh);
        }
        judgmentEdgeMaterial = null;
        thicknessMesh = null;
    }
}
