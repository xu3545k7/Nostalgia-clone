using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Restyles the JudgmentLine into a HOLLOW glowing bar:
///   • spans the track, with a Z-depth (thickness) that matches a note (defaultNoteScale.z ≈ 0.5),
///   • hollow centre (it is a rectangular frame / outline, not a filled quad),
///   • ALWAYS drawn on top of the keyboard (ZTest Always via the JudgmentLineOverlay shader), sitting
///     a touch above the keyboard,
///   • flashes white when a beat / measure line reaches it — that pulse is already wired
///     (BeatLineController -> JudgmentLineGlow.TriggerBeatPulse), and this bar exposes the emissive
///     renderer that the pulse writes to, so it works with no extra hookup.
///
/// Usage: attach to the scene's "JudgmentLine" GameObject. Every look/size value is tunable live in the
/// Inspector — OnValidate rebuilds the mesh so you can dial it in with immediate feedback in Play mode.
/// </summary>
[ExecuteAlways]
public class JudgmentLineBar : MonoBehaviour
{
    [Header("Mode")]
    [Tooltip("OFF (default): leave the existing judgment-line visual untouched — this component ONLY " +
             "anchors its screen height. Turn ON to also replace the line with the hollow glowing bar " +
             "(overlay shader + frame mesh). Keep it OFF until the anchor works, so attaching this never " +
             "makes the line disappear.")]
    public bool buildBarMesh = false;

    [Header("Span")]
    [Tooltip("Take the width from the 'Track' object; otherwise use manualWidth.")]
    public bool autoResolveTrackWidth = true;
    [Tooltip("Used when autoResolveTrackWidth is off, or when no Track is found.")]
    public float manualWidth = 10f;
    [Tooltip("Extra width added to EACH side beyond the track.")]
    public float widthPadding = 0.15f;

    [Header("Bar shape")]
    [Tooltip("Depth along Z. Match your note depth (NoteSpawner.defaultNoteScale.z, ~0.5) so the bar is 'as thick as a note'.")]
    public float thickness = 1.0f;
    [Tooltip("Border width of the hollow frame. The centre between the borders stays empty (中空).")]
    public float outlineWidth = 0.08f;
    [Tooltip("Local Y lift so the bar sits just above the keyboard. Baked into the mesh (no per-frame drift).")]
    public float heightOffset = 0.05f;

    [Header("Look")]
    public Color baseColor = new Color(0.82f, 0.93f, 1f, 0.26f);
    [Tooltip("Resting glow strength (before any beat pulse).")]
    [Range(0f, 8f)] public float intensity = 1.4f;
    [Tooltip("Higher = drawn later. Overlay range so it sits over other transparent FX.")]
    public int renderQueue = 2990;

    [Header("Camera height anchor")]
    [Tooltip("Lock the judgment line to a fixed HEIGHT on screen, so changing the camera angle no longer " +
             "slides it up/down. Each frame the line's depth (Z) is recomputed from the camera so it " +
             "projects to targetViewportY, while staying on the note plane (its own Y). Turn OFF to place " +
             "the line manually (the 'unless individually configured' case).")]
    public bool anchorToCameraHeight = false;
    [Tooltip("Capture the height the line ALREADY has on screen at your reference camera angle, and hold " +
             "THAT height when the angle changes (so it 'returns to' the original observed height). Use the " +
             "context-menu 'Capture reference height' to re-grab it at the current angle. Turn OFF to pin to " +
             "the manual targetViewportY instead.")]
    public bool autoCaptureReferenceHeight = true;
    [Tooltip("Manual screen height to pin to when autoCapture is OFF. Viewport space: 0 = bottom, 1 = top.")]
    [Range(0f, 1f)] public float targetViewportY = 0.28f;
    [Tooltip("Horizontal viewport point used for the anchor ray (0.5 = centre).")]
    [Range(0f, 1f)] public float anchorViewportX = 0.5f;
    [Tooltip("Print anchor diagnostics to the Console (throttled). Turn off once it works.")]
    public bool debugLog = false;

    private bool _capturedHeight;
    private float _referenceViewportY;
    private bool _capturedWorldAnchor;
    private Vector3 _worldAnchor;

    private MeshFilter _mf;
    private MeshRenderer _mr;
    private Material _mat;
    private Mesh _mesh;

    void OnEnable()
    {
        _capturedHeight = false;
        if (!_capturedWorldAnchor)
        {
            _worldAnchor = transform.position;
            _capturedWorldAnchor = true;
        }
        Rebuild();
    }
    void OnValidate(){ if (isActiveAndEnabled) Rebuild(); }

    // Pin the line to a fixed screen height. Runs in LateUpdate (after the camera settings have been
    // applied for the frame): cast a ray from the camera through (anchorViewportX, targetY), intersect it
    // with the note plane (the line's own Y), and slide the line in Z to that point — so whatever the
    // camera angle, the line projects to the same on-screen height.
    // Notes read judgmentZ from this line when they spawn, so they converge onto the anchored line too.
    void LateUpdate()
    {
        // Runs in EDIT mode too (ExecuteAlways) so it works whether you change the angle live in the
        // editor or in Play via the settings UI.
        if (!anchorToCameraHeight) { MaybeLog("SKIP: anchorToCameraHeight is OFF"); return; }
        var cam = ResolveCamera();
        if (cam == null) { MaybeLog("SKIP: no Camera found (Camera.main null and none in scene)"); return; }

        // Capture the line's CURRENT on-screen height once, then hold it.
        if (autoCaptureReferenceHeight && !_capturedHeight)
        {
            _referenceViewportY = cam.WorldToViewportPoint(transform.position).y;
            _capturedHeight = true;
            MaybeLog($"captured reference viewport Y = {_referenceViewportY:F3} at rotX={cam.transform.eulerAngles.x:F1}");
        }
        float targetY = autoCaptureReferenceHeight ? _referenceViewportY : targetViewportY;

        float planeY = transform.position.y;                       // stay on the note plane
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        var ray = cam.ViewportPointToRay(new Vector3(anchorViewportX, targetY, 0f));
        if (plane.Raycast(ray, out float dist) && dist > 0f)
        {
            Vector3 hit = ray.GetPoint(dist);
            MaybeLog($"cam={cam.name} rotX={cam.transform.eulerAngles.x:F1} targetY={targetY:F3} planeY={planeY:F2} curZ={transform.position.z:F2} -> newZ={hit.z:F2}");
            if (Mathf.Abs(transform.position.z - hit.z) > 0.0001f)   // avoid dirtying the scene when static
            {
                var p = transform.position;
                p.z = hit.z;                                        // only depth changes; X/Y stay
                transform.position = p;
            }
        }
        else
        {
            MaybeLog($"raycast MISS dist={dist:F2} targetY={targetY:F3} rotX={cam.transform.eulerAngles.x:F1} — target height is above the plane horizon at this angle");
        }
    }

    private static Camera ResolveCamera()
    {
        if (Camera.main != null) return Camera.main;
        return Object.FindFirstObjectByType<Camera>();
    }

    public void ApplyCameraZResponsiveAnchor(Camera camera)
    {
        if (camera == null) return;
        if (!_capturedWorldAnchor)
        {
            _worldAnchor = transform.position;
            _capturedWorldAnchor = true;
        }

        // Follow the natural projection of the authored judgment-line position,
        // but clamp it to a playable part of the screen. Camera Z therefore has
        // a visible effect without allowing the line to disappear off-screen.
        float naturalY = camera.WorldToViewportPoint(_worldAnchor).y;
        targetViewportY = Mathf.Clamp(naturalY, 0.12f, 0.48f);
        anchorToCameraHeight = true;
        autoCaptureReferenceHeight = false;
        _capturedHeight = false;
    }

    public void SetScreenHeight(float viewportY)
    {
        targetViewportY = Mathf.Clamp(viewportY, 0.12f, 0.55f);
        anchorToCameraHeight = true;
        autoCaptureReferenceHeight = false;
        _capturedHeight = false;
    }

    private void MaybeLog(string msg)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!debugLog) return;
        if (Time.frameCount % 30 != 0) return;   // throttle
        Debug.Log($"[JudgmentLineBar] {msg}");
#endif
    }

    // Grab the line's current on-screen height as the reference to hold. Run this (right-click the
    // component header -> Capture reference height) while the camera is at the angle whose height you want.
    [ContextMenu("Capture reference height at current camera")]
    private void CaptureReferenceHeight()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[JudgmentLineBar] No Camera.main to capture from."); return; }
        _referenceViewportY = cam.WorldToViewportPoint(transform.position).y;
        _capturedHeight = true;
        Debug.Log($"[JudgmentLineBar] Captured reference viewport Y = {_referenceViewportY:F3}");
    }
    void OnDestroy()
    {
        if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
        if (_mat  != null) { if (Application.isPlaying) Destroy(_mat);  else DestroyImmediate(_mat);  }
    }

    private void Rebuild()
    {
        // Anchor-only mode: do NOT touch the existing line's mesh/material (that is what made the line
        // vanish when this got attached). The screen-height anchor in LateUpdate runs regardless.
        if (!buildBarMesh) return;

        _mf = GetComponent<MeshFilter>();  if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>(); if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();

        // --- material (ZTest Always overlay shader) ---
        if (_mat == null)
        {
            var sh = Shader.Find("Nostalgia/JudgmentLineOverlay");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit"); // safe fallback
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null && _mr.sharedMaterial != null) sh = _mr.sharedMaterial.shader;
            if (sh == null)
            {
                // A visual-only component must never abort GameObject activation:
                // HideSelection enables the gameplay root immediately before
                // StartSong, so throwing here also prevents chart/audio startup.
                Debug.LogError("[JudgmentLineBar] No usable shader was included in this Player build; keeping the authored judgment-line renderer unchanged.");
                _mr.enabled = _mr.sharedMaterial != null;
                return;
            }
            _mat = new Material(sh) { name = "JudgmentLineBar (instance)", hideFlags = HideFlags.DontSave };
        }
        _mat.renderQueue = renderQueue;
        if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", baseColor);
        if (_mat.HasProperty("_Intensity")) _mat.SetFloat("_Intensity", intensity);
        _mr.sharedMaterial = _mat;
        _mr.enabled = true;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mr.receiveShadows = false;
        _mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        // --- hollow frame mesh (compensated for the object's own scale so world size is correct) ---
        float width = ResolveWidth();
        _mf.sharedMesh = BuildFrameMesh(width, thickness, outlineWidth, heightOffset, transform.lossyScale);

        // --- make sure the beat-pulse driver exists on this object ---
        if (GetComponent<JudgmentLineGlow>() == null) gameObject.AddComponent<JudgmentLineGlow>();

        if (debugLog)
        {
            var b = _mf.sharedMesh != null ? _mf.sharedMesh.bounds.size : Vector3.zero;
            Debug.Log($"[JudgmentLineBar] BUILD shader='{_mat.shader.name}' width={width:F2} thickness={thickness} " +
                      $"lossyScale={transform.lossyScale} meshVerts={_mf.sharedMesh?.vertexCount} meshWorldSize≈{Vector3.Scale(b, transform.lossyScale)} " +
                      $"worldPos={transform.position} rot={transform.eulerAngles} rendererEnabled={_mr.enabled}");
        }
    }

    private float ResolveWidth()
    {
        float w = manualWidth;
        if (autoResolveTrackWidth)
        {
            try
            {
                var track = GameObject.Find("Track");
                if (track != null) w = PianoVisualLayout.ResolveTrackWidth(track.transform);
            }
            catch { }
        }
        return Mathf.Max(0.01f, w) + widthPadding * 2f;
    }

    private Mesh BuildFrameMesh(float width, float depth, float border, float y, Vector3 lossy)
    {
        if (_mesh == null) { _mesh = new Mesh { name = "JudgmentLineBarMesh" }; _mesh.hideFlags = HideFlags.DontSave; }
        _mesh.Clear();

        float hw = Mathf.Max(0.001f, width) * 0.5f;
        float hd = Mathf.Max(0.001f, depth) * 0.5f;
        float b = Mathf.Clamp(border, 0.008f, Mathf.Min(hw, hd) * 0.48f);
        float sx = Mathf.Abs(lossy.x) > 1e-4f ? lossy.x : 1f;
        float sy = Mathf.Abs(lossy.y) > 1e-4f ? lossy.y : 1f;
        float sz = Mathf.Abs(lossy.z) > 1e-4f ? lossy.z : 1f;
        var vertices = new List<Vector3>(32);
        var triangles = new List<int>(48);

        void AddQuad(float x0, float x1, float z0, float z1, float lift = 0f)
        {
            int start = vertices.Count;
            float yy = (y + lift) / sy;
            vertices.Add(new Vector3(x0 / sx, yy, z1 / sz));
            vertices.Add(new Vector3(x1 / sx, yy, z1 / sz));
            vertices.Add(new Vector3(x1 / sx, yy, z0 / sz));
            vertices.Add(new Vector3(x0 / sx, yy, z0 / sz));
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        // One full-note-thick platinum glass band. Overlapping additive quads
        // brighten the outer frame and the two inset rails without extra renderers.
        AddQuad(-hw, hw, -hd, hd);
        AddQuad(-hw, hw, hd - b, hd, 0.001f);
        AddQuad(-hw, hw, -hd, -hd + b, 0.001f);
        AddQuad(-hw, -hw + b, -hd, hd, 0.001f);
        AddQuad(hw - b, hw, -hd, hd, 0.001f);
        float rail = Mathf.Max(0.006f, b * 0.28f);
        AddQuad(-hw + b, hw - b, hd - b - rail, hd - b, 0.002f);
        AddQuad(-hw + b, hw - b, -hd + b, -hd + b + rail, 0.002f);

        _mesh.SetVertices(vertices);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        return _mesh;
    }
}
