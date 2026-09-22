using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TrackGuideLineOverlay : MonoBehaviour
{
    [SerializeField] private Material guideMaterial;
    [SerializeField, Min(1)] private int laneCount = 28;
    [SerializeField, Min(1)] private int majorInterval = 7;
    [SerializeField, Min(0.01f)] private float guideWidthWorld = 0.075f;
    [SerializeField, Min(0.01f)] private float borderWidthWorld = 0.22f;
    [SerializeField, Min(0f)] private float innerBorderInsetWorld = 0.42f;
    [SerializeField, Range(0f, 1f)] private float masterOpacity = 1f;
    [SerializeField] private Color guideColor = new Color(0.72f, 0.53f, 0.23f, 0.24f);
    [SerializeField] private Color centerColor = new Color(0.86f, 0.67f, 0.31f, 0.36f);
    [SerializeField] private Color borderColor = new Color(0.82f, 0.61f, 0.26f, 0.58f);

    private const string OverlayName = "Track Guide Line Overlay";
    private GameObject overlayObject;
    private Mesh overlayMesh;

    private void OnEnable()
    {
        if (Application.isPlaying && SettingsManager.Instance != null)
        {
            masterOpacity = SettingsManager.Instance.TrackGuideLineOpacity;
        }
        Rebuild();
    }

    public void SetOpacity(float opacity)
    {
        masterOpacity = Mathf.Clamp01(opacity);
        Rebuild();
    }

    private void OnValidate()
    {
        laneCount = Mathf.Max(1, laneCount);
        majorInterval = Mathf.Max(1, majorInterval);
        Rebuild();
    }

    private void OnDisable()
    {
        DestroyOverlay();
    }

    private void Rebuild()
    {
        if (!isActiveAndEnabled) return;

        MeshFilter sourceFilter = GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null || guideMaterial == null)
        {
            DestroyOverlay();
            return;
        }

        EnsureOverlayObject();

        Bounds bounds = sourceFilter.sharedMesh.bounds;
        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minZ = bounds.min.z;
        float maxZ = bounds.max.z;
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        float guideWidth = guideWidthWorld / scaleX;
        float borderWidth = borderWidthWorld / scaleX;
        float borderInset = innerBorderInsetWorld / scaleX;

        var vertices = new List<Vector3>(80);
        var colors = new List<Color>(80);
        var triangles = new List<int>(120);

        // Strong outer rails plus a much quieter inset rail create a framed,
        // classical brass-inlay look without enclosing every individual lane.
        Color resolvedBorderColor = MultiplyAlpha(borderColor, masterOpacity);
        AddSoftStrip(vertices, colors, triangles, minX + borderWidth * 0.5f, minZ, maxZ, borderWidth, resolvedBorderColor);
        AddSoftStrip(vertices, colors, triangles, maxX - borderWidth * 0.5f, minZ, maxZ, borderWidth, resolvedBorderColor);

        Color insetColor = resolvedBorderColor;
        insetColor.a *= 0.34f;
        float insetWidth = Mathf.Max(guideWidth * 0.65f, borderWidth * 0.28f);
        AddSoftStrip(vertices, colors, triangles, minX + borderInset, minZ, maxZ, insetWidth, insetColor);
        AddSoftStrip(vertices, colors, triangles, maxX - borderInset, minZ, maxZ, insetWidth, insetColor);

        for (int lane = majorInterval; lane < laneCount; lane += majorInterval)
        {
            float x = Mathf.Lerp(minX, maxX, lane / (float)laneCount);
            bool isCenter = lane * 2 == laneCount;
            Color color = MultiplyAlpha(isCenter ? centerColor : guideColor, masterOpacity);
            float width = guideWidth * (isCenter ? 1.35f : 1f);
            AddSoftStrip(vertices, colors, triangles, x, minZ, maxZ, width, color);
        }

        overlayMesh.Clear();
        overlayMesh.name = "Track 7-Lane Guide Mesh";
        overlayMesh.SetVertices(vertices);
        overlayMesh.SetColors(colors);
        overlayMesh.SetTriangles(triangles, 0, true);
        overlayMesh.RecalculateBounds();
    }

    private void EnsureOverlayObject()
    {
        if (overlayObject == null)
        {
            Transform existing = transform.Find(OverlayName);
            overlayObject = existing != null ? existing.gameObject : new GameObject(OverlayName);
            overlayObject.transform.SetParent(transform, false);
            overlayObject.transform.localPosition = Vector3.zero;
            overlayObject.transform.localRotation = Quaternion.identity;
            overlayObject.transform.localScale = Vector3.one;
            overlayObject.layer = gameObject.layer;
            overlayObject.hideFlags = HideFlags.DontSave;
        }

        MeshFilter filter = overlayObject.GetComponent<MeshFilter>();
        if (filter == null) filter = overlayObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = overlayObject.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = overlayObject.AddComponent<MeshRenderer>();

        if (overlayMesh == null)
        {
            overlayMesh = new Mesh { hideFlags = HideFlags.DontSave };
        }
        filter.sharedMesh = overlayMesh;
        renderer.sharedMaterial = guideMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.sortingOrder = 1;
    }

    private static void AddSoftStrip(List<Vector3> vertices, List<Color> colors, List<int> triangles,
        float centerX, float minZ, float maxZ, float width, Color color)
    {
        float half = Mathf.Max(0.0001f, width * 0.5f);
        float core = half * 0.32f;
        int start = vertices.Count;
        float[] x = { centerX - half, centerX - core, centerX + core, centerX + half };

        for (int i = 0; i < 4; i++)
        {
            float alphaScale = (i == 0 || i == 3) ? 0f : 1f;
            Color vertexColor = color;
            vertexColor.a *= alphaScale;
            vertices.Add(new Vector3(x[i], 0f, minZ));
            vertices.Add(new Vector3(x[i], 0f, maxZ));
            colors.Add(vertexColor);
            colors.Add(vertexColor);
        }

        for (int column = 0; column < 3; column++)
        {
            int a = start + column * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(c); triangles.Add(b); triangles.Add(d);
        }
    }

    private static Color MultiplyAlpha(Color color, float multiplier)
    {
        color.a *= Mathf.Clamp01(multiplier);
        return color;
    }

    private void DestroyOverlay()
    {
        if (overlayObject != null)
        {
            if (Application.isPlaying) Destroy(overlayObject);
            else DestroyImmediate(overlayObject);
        }
        overlayObject = null;

        if (overlayMesh != null)
        {
            if (Application.isPlaying) Destroy(overlayMesh);
            else DestroyImmediate(overlayMesh);
        }
        overlayMesh = null;
    }
}
