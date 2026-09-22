using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings-page viewport for the real running game.  It does not reproduce
/// notes or effects: SongSelectionManager starts the normal GameManager flow,
/// and this camera simply captures that exact scene into the settings card.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
public sealed class GameplaySettingsPreview : MonoBehaviour
{
    // The settings screen is full-screen, so displaying the already-rendered
    // gameplay camera is both exact and cheaper than rendering it a second time.
    private const bool UseDirectGameplayView = true;
    private const int UiLayer = 5;
    private const int MinWidth = 640;
    private const int MaxWidth = 1920;

    private RawImage output;
    private Camera previewCamera;
    private RenderTexture target;
    private Vector2Int targetSize;
    private SongSelectionManager.SongOption previewSong;
    private bool previewSessionActive;

    public SongSelectionManager.SongOption PreviewSong => previewSong;

    private void Awake()
    {
        output = GetComponent<RawImage>();
        output.raycastTarget = false;
        output.color = UseDirectGameplayView ? Color.clear : Color.white;
    }

    private void OnEnable()
    {
        if (!UseDirectGameplayView) EnsureCamera();
        RefreshNow();
    }

    private void LateUpdate() => RefreshNow();

    public void SetPreviewSong(SongSelectionManager.SongOption song)
    {
        bool changed = !ReferenceEquals(previewSong, song);
        previewSong = song;
        if (changed && previewSessionActive)
            SongSelectionManager.Instance?.StartSettingsGameplayPreview(previewSong);
    }

    public void BeginPreviewSession()
    {
        if (previewSessionActive) return;
        previewSessionActive = true;
        SettingsManager.Instance?.ApplySettings();
        SongSelectionManager.Instance?.StartSettingsGameplayPreview(previewSong);
    }

    public void EndPreviewSession()
    {
        if (!previewSessionActive) return;
        previewSessionActive = false;
        SongSelectionManager.Instance?.StopSettingsGameplayPreview();
    }

    public void RefreshNow()
    {
        if (!isActiveAndEnabled) return;
        if (UseDirectGameplayView)
        {
            // Do not reinterpret HDR colours or omit renderer features in a
            // second camera. The transparent settings canvas reveals the same
            // Track, notes, particles and post-processing as normal gameplay.
            DisableCaptureView();
            return;
        }
        Camera source = ResolveGameplayCamera();
        if (source == null)
        {
            if (previewCamera != null) previewCamera.enabled = false;
            return;
        }

        EnsureCamera();
        EnsureTargetTexture();
        if (previewCamera == null || target == null) return;

        // Copy the actual gameplay camera every frame.  Camera Z/X, track,
        // judgment-line compensation, real note materials and particles are
        // therefore exactly the same objects used by normal gameplay.
        previewCamera.CopyFrom(source);
        CopyRenderPipelineCameraState(source, previewCamera);
        previewCamera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        previewCamera.transform.localScale = source.transform.lossyScale;
        previewCamera.targetTexture = target;
        previewCamera.rect = new Rect(0f, 0f, 1f, 1f);
        previewCamera.depth = source.depth - 1f;
        previewCamera.cullingMask = source.cullingMask & ~(1 << UiLayer);
        previewCamera.enabled = true;
        output.texture = target;
    }

    private void DisableCaptureView()
    {
        if (previewCamera != null)
        {
            previewCamera.enabled = false;
            previewCamera.targetTexture = null;
        }
        if (output != null)
        {
            output.texture = null;
            output.color = Color.clear;
            output.raycastTarget = false;
        }
        if (target != null) ReleaseTarget();
    }

    /// <summary>
    /// Camera.CopyFrom only copies the built-in Camera. URP keeps post-processing,
    /// volume masks, anti-aliasing and its renderer selection on an additional
    /// component; without these the HDR effects are written into the preview RT
    /// before gameplay tonemapping and consequently clip to plain white.
    /// </summary>
    private static void CopyRenderPipelineCameraState(Camera source, Camera destination)
    {
        if (source == null || destination == null) return;

        Component sourceData = null;
        Component[] sourceComponents = source.GetComponents<Component>();
        for (int i = 0; i < sourceComponents.Length; i++)
        {
            Component candidate = sourceComponents[i];
            if (candidate != null && candidate.GetType().Name == "UniversalAdditionalCameraData")
            {
                sourceData = candidate;
                break;
            }
        }
        if (sourceData == null) return;

        System.Type dataType = sourceData.GetType();
        Component destinationData = destination.GetComponent(dataType);
        if (destinationData == null) destinationData = destination.gameObject.AddComponent(dataType);
        if (destinationData == null) return;

        string[] propertyNames =
        {
            "renderPostProcessing", "volumeLayerMask", "volumeTrigger",
            "antialiasing", "antialiasingQuality", "stopNaN", "dithering",
            "renderShadows", "requiresDepthTexture", "requiresColorTexture",
            "requiresDepthOption", "requiresColorOption", "allowXRRendering"
        };
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        for (int i = 0; i < propertyNames.Length; i++)
        {
            try
            {
                var property = dataType.GetProperty(propertyNames[i], flags);
                if (property == null || !property.CanRead || !property.CanWrite) continue;
                property.SetValue(destinationData, property.GetValue(sourceData));
            }
            catch { }
        }

        // Renderer selection is exposed through SetRenderer rather than a
        // writable property in current URP versions.
        try
        {
            var rendererIndex = dataType.GetProperty("scriptableRendererIndex", flags);
            var setRenderer = dataType.GetMethod("SetRenderer", flags, null,
                new[] { typeof(int) }, null);
            if (rendererIndex != null && setRenderer != null)
                setRenderer.Invoke(destinationData, new[] { rendererIndex.GetValue(sourceData) });
        }
        catch { }
    }

    private Camera ResolveGameplayCamera()
    {
        Camera main = Camera.main;
        if (main != null && main != previewCamera) return main;
        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate != null && candidate != previewCamera && candidate.targetTexture == null)
                return candidate;
        }
        return null;
    }

    private void EnsureCamera()
    {
        if (previewCamera != null) return;
        GameObject cameraObject = new GameObject("SettingsGameplayPreviewCamera", typeof(Camera));
        cameraObject.hideFlags = HideFlags.DontSave;
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.enabled = false;
    }

    private void EnsureTargetTexture()
    {
        RectTransform rect = transform as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        float aspect = rect != null && rect.rect.height > 1f ? rect.rect.width / rect.rect.height : 16f / 9f;
        int width = Mathf.Clamp(Mathf.RoundToInt((rect != null ? rect.rect.width : 1280f) * scale), MinWidth, MaxWidth);
        int height = Mathf.Max(360, Mathf.RoundToInt(width / Mathf.Max(0.1f, aspect)));
        Vector2Int wanted = new(width, height);
        if (target != null && wanted == targetSize) return;

        ReleaseTarget();
        targetSize = wanted;
        target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "Settings_GameplayPreview_RT",
            antiAliasing = 2,
            useMipMap = false,
            autoGenerateMips = false,
            hideFlags = HideFlags.DontSave
        };
        target.Create();
        output.texture = target;
    }

    private void OnDisable()
    {
        if (previewCamera != null) previewCamera.enabled = false;
        EndPreviewSession();
    }

    private void OnDestroy()
    {
        EndPreviewSession();
        ReleaseTarget();
        if (previewCamera != null) Destroy(previewCamera.gameObject);
    }

    private void ReleaseTarget()
    {
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (output != null) output.texture = null;
        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
        target = null;
        targetSize = Vector2Int.zero;
    }
}
