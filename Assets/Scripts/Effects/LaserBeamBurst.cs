using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders the LK laser as a quad aligned with the track plane.
/// </summary>
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class LaserBeamBurst : MonoBehaviour
{
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField, Min(0.01f)] private float defaultLength = 1f;
    [SerializeField, Min(0.01f)] private float defaultDuration = 0.18f;
    [SerializeField, Min(0.001f)] private float defaultWidth = 1f;
    [SerializeField] private float heightOffset = 0.02f;
    [SerializeField, Tooltip("限制雷射高度，避免超出畫面。")] private bool clampLength = true;
    [SerializeField, Min(0.01f)] private float maxLength = 25f;
    [SerializeField] private AnimationCurve widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private string emissionProperty = "_EmissionColor";
    [SerializeField] private string baseColorProperty = "_BaseColor";

    [Header("Editor Preview")]
    [SerializeField] private bool previewInEditor;
    [SerializeField] private Color previewColor = Color.white;
    [SerializeField, Range(0f, 3f)] private float previewIntensity = 1f;

    private Coroutine playRoutine;
    private MaterialPropertyBlock propertyBlock;
    private Action<LaserBeamBurst> releaseCallback;
    private float currentBaseWidth;
    private float currentBaseLength;

    private int cachedBaseColorProperty = -1;
    private int cachedEmissionProperty = -1;
    private static Mesh sharedQuadMesh;

    private void Awake()
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        EnsureQuadMesh();
        ConfigureRenderer();
        ResolveMaterialPropertyIds();
        propertyBlock = new MaterialPropertyBlock();
        currentBaseWidth = defaultWidth;
        currentBaseLength = defaultLength;
        ApplyScale(0f);

        if (Application.isPlaying)
        {
            ApplyColor(Color.black, 0f);
        }
        else
        {
            ApplyPreviewState();
        }
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            ApplyPreviewState();
        }
    }

    private void OnDisable()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (Application.isPlaying)
        {
            ApplyScale(0f);
            ApplyColor(Color.black, 0f);
        }
        else
        {
            ApplyPreviewState();
        }
    }

    public void ConfigureReleaseCallback(Action<LaserBeamBurst> onRelease)
    {
        releaseCallback = onRelease;
    }

    public void Play(Vector3 worldPosition, Vector3 forwardDirection, Color color,
        float lengthOverride = -1f, float durationOverride = -1f, float widthOverride = -1f,
        Vector3 upDirection = default)
    {
        if (meshRenderer == null)
        {
            Debug.LogWarning("LaserBeamBurst requires a MeshRenderer.");
            releaseCallback?.Invoke(this);
            return;
        }

        float length = lengthOverride > 0f ? lengthOverride : defaultLength;
        float duration = durationOverride > 0f ? durationOverride : defaultDuration;
        Vector3 forward = forwardDirection.sqrMagnitude > 1e-4f ? forwardDirection.normalized : Vector3.forward;
        Vector3 up = upDirection.sqrMagnitude > 1e-4f ? upDirection.normalized : Vector3.up;
        float offset = Mathf.Max(heightOffset, 0.001f);

        transform.position = worldPosition + up * offset;
        transform.rotation = Quaternion.LookRotation(forward, up);
        currentBaseLength = Mathf.Max(0.01f, length);
        if (clampLength)
        {
            currentBaseLength = Mathf.Min(currentBaseLength, maxLength);
        }
        ApplyWidth(widthOverride);

        meshRenderer.enabled = true;
        meshRenderer.forceRenderingOff = false;

        Debug.Log($"[LaserBeamBurst] Resolved width = {currentBaseWidth:F4} on {name}.");

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
        }
        playRoutine = StartCoroutine(PulseRoutine(duration, color));
    }

    private IEnumerator PulseRoutine(float duration, Color color)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float normalized = duration > Mathf.Epsilon ? elapsed / duration : 1f;
            float widthFactor = widthCurve != null ? Mathf.Max(0f, widthCurve.Evaluate(normalized)) : 1f;
            ApplyScale(widthFactor);
            float intensity = intensityCurve != null ? Mathf.Max(0f, intensityCurve.Evaluate(normalized)) : 1f;
            ApplyColor(color, intensity);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyScale(0f);
        ApplyColor(color, 0f);
        meshRenderer.enabled = false;
        playRoutine = null;
        releaseCallback?.Invoke(this);
    }

    private void ApplyWidth(float widthOverride)
    {
        currentBaseWidth = widthOverride > 0f ? widthOverride : defaultWidth;
        ApplyScale(1f);
    }

    private void ApplyScale(float widthFactor)
    {
        float width = Mathf.Max(0f, currentBaseWidth * widthFactor);
        float length = Mathf.Max(0.01f, currentBaseLength);
        transform.localScale = new Vector3(width, length, 1f);
    }

    private void ApplyColor(Color baseColor, float intensity)
    {
        if (meshRenderer == null)
        {
            return;
        }

        propertyBlock ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(propertyBlock);

        if (cachedBaseColorProperty >= 0)
        {
            propertyBlock.SetColor(cachedBaseColorProperty, baseColor);
        }

        Color emissionColor = baseColor * intensity;
        if (cachedEmissionProperty >= 0)
        {
            propertyBlock.SetColor(cachedEmissionProperty, emissionColor);
        }

        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ApplyPreviewState()
    {
        EnsureQuadMesh();
        currentBaseLength = defaultLength;
        ApplyWidth(-1f);
        if (previewInEditor)
        {
            ApplyColor(previewColor, previewIntensity);
            meshRenderer.enabled = true;
        }
        else
        {
            ApplyColor(Color.black, 0f);
            meshRenderer.enabled = false;
        }
    }

    private void EnsureQuadMesh()
    {
        if (meshFilter == null)
        {
            return;
        }

        if (sharedQuadMesh == null)
        {
            sharedQuadMesh = BuildQuadMesh();
        }

        if (meshFilter.sharedMesh == null)
        {
            meshFilter.sharedMesh = sharedQuadMesh;
        }
    }

    private void ConfigureRenderer()
    {
        if (meshRenderer == null)
        {
            return;
        }

        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.allowOcclusionWhenDynamic = false;
        meshRenderer.enabled = false;
        ResolveMaterialPropertyIds();
    }

    private void ResolveMaterialPropertyIds()
    {
        cachedBaseColorProperty = ResolveColorPropertyId(baseColorProperty, "_BaseColor", "_Color", "_TintColor");
        cachedEmissionProperty = ResolveColorPropertyId(emissionProperty, "_EmissionColor", "_Emission", "_EmissionColorHDR");
    }

    private int ResolveColorPropertyId(params string[] candidates)
    {
        if (meshRenderer == null)
        {
            return -1;
        }

        var material = meshRenderer.sharedMaterial;
        if (material == null)
        {
            return -1;
        }

        foreach (string name in candidates)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (material.HasProperty(name))
            {
                return Shader.PropertyToID(name);
            }
        }

        return -1;
    }

    private static Mesh BuildQuadMesh()
    {
        var mesh = new Mesh { name = "LaserBeamQuad" };

        // XY quad so Y becomes beam height (stands upright) while Z stays thin.
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, 1f, 0f)
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };

        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            EnsureQuadMesh();
            ConfigureRenderer();
            ResolveMaterialPropertyIds();
            ApplyPreviewState();
        }
    }
#endif
}
