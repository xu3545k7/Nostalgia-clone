using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal particle-based judge popup manager. Spawns short-lived particles at the
/// judgment position using optional sprites and colors per result.
/// </summary>
public class JudgePopupManager : MonoBehaviour
{
    private static JudgePopupManager _instance;

    public static JudgePopupManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<JudgePopupManager>();
                if (_instance == null)
                {
                    var go = new GameObject(nameof(JudgePopupManager));
                    _instance = go.AddComponent<JudgePopupManager>();
                }
            }
            return _instance;
        }
    }

    [Header("Toggle")]
    [Tooltip("Enable or disable the particle-based judge popup.")]
    public bool enablePopup = true;

    [Header("Placement")]
    [Tooltip("Optional reference to the judgment line. When assigned, popups snap to this Y height.")]
    public Transform judgmentLine;
    [Tooltip("Vertical offset above the judgment line or note position (world units).")]
    public float worldYOffset = 0.6f;
    [Tooltip("Move the popup slightly toward the camera to avoid z-fighting (world units).")]
    public float forwardOffset = 0.02f;
    [Tooltip("When true, the popup faces the main camera before applying custom rotation.")]
    public bool faceCamera = true;

    [Header("Appearance")]
    [Tooltip("Lifetime of the spawned particle (seconds).")]
    public float lifetime = 0.1f;
    [Tooltip("Default Euler rotation applied after camera alignment.")]
    public Vector3 defaultRotationEuler = new Vector3(90f, 0f, 0f);
    [Tooltip("Uniform scale multiplier applied after width matching.")]
    public float defaultScale = 1f;
    [Tooltip("How much of the judged note width the popup should occupy (0-1).")]
    [Range(0.1f, 1.0f)] public float widthFillRatio = 0.85f;
    [Tooltip("Minimum width applied when note width is unknown (world units).")]
    public float minWidth = 0.3f;
    [Header("Motion")]
    [Tooltip("When enabled, each popup flies outward in the configured direction and eases to a stop.")]
    public bool enableFlyOutMotion = true;
    [Tooltip("Fly-out direction relative to the final rotation (automatically normalized).")]
    public Vector3 flyOutDirection = new Vector3(0f, 1f, 0f);
    [Tooltip("How far the popup travels (world units).")]
    public float flyOutDistance = 0.35f;
    [Tooltip("Duration of the motion (seconds). Clamped to the particle lifetime.")]
    public float flyOutDuration = 0.1f;
    [Tooltip("When true, fly-out direction is interpreted in world space instead of rotated with the popup.")]
    public bool flyOutDirectionUsesWorldSpace = true;
    [Tooltip("When true, popups rise along Camera.main.up (if available). Overrides other direction settings.")]
    public bool flyOutAlignsWithCameraUp = true;
    [Header("Line Alignment")]
    [Tooltip("When assigned, popups snap their Y (and optionally Z) to this transform.")]
    public bool snapZToJudgmentLine = true;
    [Tooltip("Use this fallback position when no judgmentLine is assigned.")]
    public bool useFallbackLinePosition = true;
    [Tooltip("Fallback world position for the judgment line (used when judgmentLine is null).")]
    public Vector3 fallbackLinePosition = new Vector3(0f, 0.5f, -2f);

    [Header("Particle Source")]
    [Tooltip("Optional particle prefab. If assigned, this prefab will be instantiated per popup.")]
    public ParticleSystem particlePrefab;
    [Tooltip("Fallback material used when no sprite is set.")]
    public Material fallbackMaterial;

    [Header("Result Sprites")]
    public Sprite perfectSprite;
    public Sprite greatSprite;
    public Sprite goodSprite;
    public Sprite missSprite;
    [Tooltip("Automatically load result sprites from a Resources folder when inspector slots are empty.")]
    public bool autoLoadSpritesFromResources = true;
    [Tooltip("Resources folder that contains the result sprites (relative to Assets/Resources).")]
    public string resourcesSpriteFolder = "graphic/judge";
    [Tooltip("Resource name used for the Perfect result (within the sprite folder).")]
    public string perfectSpriteResourceName = "Just";
    [Tooltip("Resource name used for the Great result.")]
    public string greatSpriteResourceName = "Great";
    [Tooltip("Resource name used for the Good result.")]
    public string goodSpriteResourceName = "good";
    [Tooltip("Resource name used for the Miss result.")]
    public string missSpriteResourceName = "Miss";

    [Header("Result Colors")]
    public Color perfectColor = Color.cyan;
    public Color greatColor = Color.yellow;
    public Color goodColor = new Color(1f, 0.6f, 0.2f);
    public Color missColor = Color.gray;
    [Tooltip("Apply per-result colors to particles. Disable if sprites already contain baked colors.")]
    public bool applyResultColor = false;
    [Tooltip("When enabled (and applyResultColor is false), only Miss/Fail results are tinted using missColor.")]
    public bool tintOnlyMissWhenUntinted = true;
    [Header("Diagnostics")]
    [Tooltip("When enabled, logs detailed JudgePopup traces to the console.")]
    public bool enableDebugLogging = false;

    private readonly Dictionary<Sprite, Material> materialCache = new Dictionary<Sprite, Material>();
    private Material nullSpriteMaterial;
    private static bool debugLoggingEnabled;
    private const string DebugPrefix = "[JudgePopupDBG]";
    // Cached Camera.main — avoids FindMainCamera() per judgment
    private Camera _cachedCamera;
    // Particle pool — avoids Instantiate/Destroy every judgment
    private readonly Queue<ParticleSystem> _psPool = new Queue<ParticleSystem>(8);
    // Shared Particle buffer for AnimateFlyOut — safe because coroutines don't overlap within a frame
    private readonly ParticleSystem.Particle[] _sharedParticleBuffer = new ParticleSystem.Particle[1];

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        _cachedCamera = Camera.main;
        EnsureJudgmentLineReference();
        EnsureResultSprites();
        SyncDebugFlag();
        // Pre-warm pool so first burst of simultaneous judgments doesn't Instantiate
        if (particlePrefab != null)
        {
            for (int i = 0; i < 8; i++)
            {
                var warm = Instantiate(particlePrefab);
                warm.gameObject.SetActive(false);
                _psPool.Enqueue(warm);
            }
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void OnValidate()
    {
        if (_instance == null || _instance == this)
        {
            EnsureJudgmentLineReference();
            EnsureResultSprites();
            SyncDebugFlag();
        }
    }

    private void SyncDebugFlag()
    {
        debugLoggingEnabled = enableDebugLogging;
        DebugLog("SyncDebugFlag: debug logging " + (debugLoggingEnabled ? "ENABLED" : "disabled"));
    }

    private static void DebugLog(string message)
    {
        if (!debugLoggingEnabled) return;
        Debug.Log(DebugPrefix + " " + message);
    }

    private static void DebugLogWarning(string message)
    {
        if (!debugLoggingEnabled) return;
        Debug.LogWarning(DebugPrefix + " " + message);
    }

    internal static void TraceExternal(string message)
    {
        if (_instance != null)
        {
            if (_instance.enableDebugLogging)
            {
                Debug.Log(DebugPrefix + " [External] " + message);
            }
        }
        else
        {
            // If we have no instance yet, log anyway so callers know the manager is missing.
            Debug.Log(DebugPrefix + " [External-NoInstance] " + message);
        }
    }

    private void EnsureJudgmentLineReference()
    {
        if (judgmentLine != null) return;
        var lineGO = GameObject.Find("JudgmentLine");
        if (lineGO != null)
        {
            judgmentLine = lineGO.transform;
            DebugLog("Auto-wired judgmentLine from scene object 'JudgmentLine'");
        }
    }

    public void ShowPopupAtRectTransform(RectTransform target, JudgmentResult result)
    {
        DebugLog("ShowPopupAtRectTransform target=" + (target != null ? target.name : "(null)") + " result=" + result + " enablePopup=" + enablePopup);
        if (!enablePopup || target == null) return;

        Vector3 worldCenter;
        try { worldCenter = target.TransformPoint(target.rect.center); }
        catch { worldCenter = target.position; }

        ShowPopupAtWorldPosition(worldCenter, result);
    }

    public void ShowPopupAtWorldPosition(Vector3 worldPos, JudgmentResult result, float noteWorldWidth = 0f, Vector3? rotationEuler = null)
    {
        if (!enablePopup) return;

        DebugLog("ShowPopupAtWorldPosition pos=" + worldPos + " result=" + result + " noteWidth=" + noteWorldWidth + " rotationOverride=" + (rotationEuler.HasValue ? rotationEuler.Value.ToString() : "(auto)"));

        Vector3 spawnPos = worldPos;
        if (judgmentLine != null)
        {
            spawnPos.y = judgmentLine.position.y + worldYOffset;
            if (snapZToJudgmentLine)
            {
                spawnPos.z = judgmentLine.position.z;
            }
            DebugLog("Using judgmentLine anchor at position=" + judgmentLine.position + " -> spawnPos=" + spawnPos);
        }
        else
        {
            if (useFallbackLinePosition)
            {
                spawnPos.y = fallbackLinePosition.y + worldYOffset;
                if (snapZToJudgmentLine) spawnPos.z = fallbackLinePosition.z;
                DebugLog("Using fallback line position=" + fallbackLinePosition + " -> spawnPos=" + spawnPos);
            }
            else
            {
                spawnPos.y += worldYOffset;
            }
        }

        Quaternion cameraFacing = Quaternion.identity;
        if (faceCamera)
        {
            DebugLog("faceCamera enabled; attempting to align with Camera.main");
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            var cam = _cachedCamera;
            if (cam != null)
            {
                Vector3 fromCamera = spawnPos - cam.transform.position;
                if (fromCamera.sqrMagnitude > 0.0001f)
                {
                    Vector3 dirToCamera = -fromCamera.normalized;
                    Vector3 dirFromCamera = fromCamera.normalized;
                    cameraFacing = Quaternion.LookRotation(dirFromCamera, Vector3.up);
                    spawnPos += dirToCamera * Mathf.Max(0f, forwardOffset);
                    DebugLog("Aligned with camera " + cam.name + " forwardOffset applied -> " + spawnPos);
                }
                else DebugLog("Camera alignment skipped due to zero magnitude vector");
            }
            else DebugLogWarning("Camera.main not found while faceCamera=true");
        }

        Vector3 euler = rotationEuler ?? defaultRotationEuler;
        Quaternion finalRotation = cameraFacing * Quaternion.Euler(euler);
        DebugLog("Final rotation euler=" + finalRotation.eulerAngles);

        float targetWidth = noteWorldWidth > 0f ? noteWorldWidth * Mathf.Clamp01(widthFillRatio) : minWidth;
        targetWidth = Mathf.Max(minWidth, targetWidth);
        DebugLog("Computed targetWidth=" + targetWidth + " from noteWidth=" + noteWorldWidth);

        SpawnParticle(spawnPos, finalRotation, targetWidth, result);
    }

    private void SpawnParticle(Vector3 position, Quaternion rotation, float width, JudgmentResult result)
    {
        ParticleSystem ps = CreateParticleInstance();
        GameObject go = ps.gameObject;
        go.transform.position = position;
        go.transform.rotation = rotation;

        DebugLog("SpawnParticle instance=" + go.name + " position=" + position + " rotationEuler=" + rotation.eulerAngles + " width=" + width + " result=" + result);

        Sprite sprite = SpriteForResult(result);
        if (sprite == null)
        {
            DebugLogWarning("No sprite assigned for result " + result + ", using fallback material.");
        }
        float aspect = (sprite != null && sprite.bounds.size.x > 0.0001f)
            ? Mathf.Max(0.01f, sprite.bounds.size.y / sprite.bounds.size.x)
            : 1f;

        DebugLog("Sprite=" + (sprite != null ? sprite.name : "(null)") + " aspect=" + aspect);

        float widthScaled = width * Mathf.Max(0.0001f, defaultScale);
        float heightScaled = widthScaled * aspect;
        go.transform.localScale = new Vector3(widthScaled, heightScaled, widthScaled);
        DebugLog("Applied particle localScale width=" + widthScaled + " height=" + heightScaled + " defaultScale=" + defaultScale);

        var main = ps.main;
        main.duration = Mathf.Max(0.01f, lifetime);
        main.startLifetime = Mathf.Max(0.01f, lifetime);
        main.startSpeed = 0f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = ResolveStartColor(result);
        main.maxParticles = Mathf.Max(main.maxParticles, 1);

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        ConfigureRenderer(ps, sprite);

            Vector3 flyDirection = ResolveFlyOutDirection(rotation);

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var emit = new ParticleSystem.EmitParams
        {
            startLifetime = Mathf.Max(0.01f, lifetime),
            startSize = 1f,
            startColor = ResolveStartColor(result),
            position = position,
            velocity = Vector3.zero
        };
        ps.Emit(emit, 1);

        DebugLog("Emit called on " + go.name + " lifetime=" + lifetime + " color=" + ColorForResult(result));

        if (enableFlyOutMotion)
        {
                StartCoroutine(AnimateFlyOut(ps, position, flyDirection));
        }

        StartCoroutine(ReturnToPool(ps, Mathf.Max(lifetime, flyOutDuration) + 0.1f));
    }

    private IEnumerator ReturnToPool(ParticleSystem ps, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (ps == null) yield break;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.SetActive(false);
        if (particlePrefab != null) _psPool.Enqueue(ps);
        else Destroy(ps.gameObject);
    }

    private ParticleSystem CreateParticleInstance()
    {
        if (particlePrefab != null)
        {
            // Try pool first
            while (_psPool.Count > 0)
            {
                var pooled = _psPool.Dequeue();
                if (pooled != null) { pooled.gameObject.SetActive(true); return pooled; }
            }
            var inst = Instantiate(particlePrefab);
            DebugLog("CreateParticleInstance: pool miss; instantiated prefab " + particlePrefab.name + " -> " + inst.name);
            return inst;
        }

        var go = new GameObject("JudgePopupParticle");
        var ps = go.AddComponent<ParticleSystem>();
        DebugLog("CreateParticleInstance: built runtime particle " + go.name);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startSpeed = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = 1f;
        main.maxParticles = 1;
        main.stopAction = ParticleSystemStopAction.None;
        main.startColor = applyResultColor ? Color.white : Color.white;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = SharedQuadMesh;
        renderer.alignment = ParticleSystemRenderSpace.World;
        if (fallbackMaterial != null)
        {
            renderer.material = fallbackMaterial;
            renderer.sharedMaterial = fallbackMaterial;
        }

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    private Sprite SpriteForResult(JudgmentResult result)
    {
        EnsureResultSprites();
        return result switch
        {
            JudgmentResult.Perfect => perfectSprite,
            JudgmentResult.Great => greatSprite,
            JudgmentResult.Good => goodSprite,
            JudgmentResult.Miss => missSprite,
            JudgmentResult.Fail => missSprite,
            _ => null
        };
    }

    private Color ColorForResult(JudgmentResult result)
    {
        return result switch
        {
            JudgmentResult.Perfect => perfectColor,
            JudgmentResult.Great => greatColor,
            JudgmentResult.Good => goodColor,
            JudgmentResult.Miss => missColor,
            JudgmentResult.Fail => missColor,
            _ => Color.white
        };
    }

    private Color ResolveStartColor(JudgmentResult result)
    {
        if (applyResultColor) return ColorForResult(result);
        if (tintOnlyMissWhenUntinted)
        {
            if (result == JudgmentResult.Miss || result == JudgmentResult.Fail)
            {
                return missColor;
            }
        }
        return Color.white;
    }

    private void EnsureResultSprites()
    {
        if (!autoLoadSpritesFromResources) return;

        // First try: load all sprites in the folder and map by lowercase name (robust against case)
        try
        {
            var loaded = Resources.LoadAll<Sprite>(string.IsNullOrWhiteSpace(resourcesSpriteFolder) ? "" : resourcesSpriteFolder.TrimEnd('/'));
            if (loaded != null && loaded.Length > 0)
            {
                var map = new Dictionary<string, Sprite>();
                foreach (var s in loaded)
                {
                    if (s == null) continue;
                    var key = s.name.Trim().ToLowerInvariant();
                    if (!map.ContainsKey(key)) map[key] = s;
                }
                if (perfectSprite == null && map.TryGetValue(perfectSpriteResourceName.Trim().ToLowerInvariant(), out var ps)) perfectSprite = ps;
                if (greatSprite == null && map.TryGetValue(greatSpriteResourceName.Trim().ToLowerInvariant(), out var gs)) greatSprite = gs;
                if (goodSprite == null && map.TryGetValue(goodSpriteResourceName.Trim().ToLowerInvariant(), out var gos)) goodSprite = gos;
                if (missSprite == null && map.TryGetValue(missSpriteResourceName.Trim().ToLowerInvariant(), out var ms)) missSprite = ms;
            }
        }
        catch { }

        // Fallbacks: try explicit case permutations like SimpleJudgePopupManager does
        perfectSprite = TryLoadSpriteIfMissing(perfectSprite, perfectSpriteResourceName) ?? perfectSprite;
        greatSprite = TryLoadSpriteIfMissing(greatSprite, greatSpriteResourceName) ?? greatSprite;
        goodSprite = TryLoadSpriteIfMissing(goodSprite, goodSpriteResourceName) ?? goodSprite;
        missSprite = TryLoadSpriteIfMissing(missSprite, missSpriteResourceName) ?? missSprite;
    }

    private Sprite TryLoadSpriteIfMissing(Sprite current, string resourceName)
    {
        if (current != null) return current;
        var sprite = LoadSpriteFromResources(resourceName);
        if (sprite == null)
        {
            DebugLogWarning("TryLoadSpriteIfMissing: failed to load sprite '" + resourceName + "' from Resources folder '" + resourcesSpriteFolder + "'.");
        }
        else
        {
            DebugLog("TryLoadSpriteIfMissing: loaded sprite '" + sprite.name + "' from Resources.");
        }
        return sprite ?? current;
    }

    private Sprite LoadSpriteFromResources(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName)) return null;
        string basePath = string.IsNullOrWhiteSpace(resourcesSpriteFolder)
            ? resourceName
            : resourcesSpriteFolder.TrimEnd('/') + "/" + resourceName;
        var sprite = Resources.Load<Sprite>(basePath);
        return sprite;
    }

    private void ConfigureRenderer(ParticleSystem ps, Sprite sprite)
    {
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = SharedQuadMesh;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortingFudge = Mathf.Max(renderer.sortingFudge, 2f);
        renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 20);

        Material mat = GetOrCreateMaterialForSprite(sprite);

        if (mat != null)
        {
            renderer.material = mat;
            renderer.sharedMaterial = mat;
            if (sprite != null)
            {
                ApplySpriteToMaterial(mat, sprite);
            }
        }

        var textureSheet = ps.textureSheetAnimation;
        textureSheet.enabled = true;
        textureSheet.mode = ParticleSystemAnimationMode.Sprites;
        textureSheet.numTilesX = 1;
        textureSheet.numTilesY = 1;
        if (sprite != null)
        {
            if (textureSheet.spriteCount > 0)
            {
                textureSheet.SetSprite(0, sprite);
                for (int i = textureSheet.spriteCount - 1; i > 0; i--)
                {
                    textureSheet.RemoveSprite(i);
                }
            }
            else
            {
                textureSheet.AddSprite(sprite);
            }
            if (textureSheet.spriteCount == 0)
            {
                DebugLogWarning("ConfigureRenderer: textureSheet.spriteCount==0 after attempting to add sprite " + sprite.name);
            }
        }
        else
        {
            while (textureSheet.spriteCount > 0)
            {
                textureSheet.RemoveSprite(textureSheet.spriteCount - 1);
            }
        }
    }

    private Material GetOrCreateMaterialForSprite(Sprite sprite)
    {
        if (sprite == null)
        {
            if (nullSpriteMaterial == null)
            {
                nullSpriteMaterial = CreateBaseMaterial(null);
            }
            return nullSpriteMaterial;
        }

        if (materialCache.TryGetValue(sprite, out var cached) && cached != null)
        {
            return cached;
        }

        var mat = CreateBaseMaterial(sprite.texture);
        if (mat != null)
        {
            mat.name = "JudgePopupMat_" + sprite.name;
            ApplySpriteToMaterial(mat, sprite);
            materialCache[sprite] = mat;
        }
        return mat;
    }

    private Material CreateBaseMaterial(Texture texture)
    {
        Material mat;
        if (fallbackMaterial != null)
        {
            mat = new Material(fallbackMaterial) { name = "JudgePopupAutoMat" };
        }
        else
        {
            string[] shaderCandidates =
            {
                "Sprites/Default",
                "UI/Default",
                "Universal Render Pipeline/Particles/Unlit",
                "Particles/Standard Unlit",
                "Unlit/Transparent"
            };

            Shader shader = null;
            foreach (var name in shaderCandidates)
            {
                shader = Shader.Find(name);
                if (shader != null)
                {
                    DebugLog("CreateBaseMaterial found shader '" + name + "'");
                    break;
                }
            }

            if (shader == null)
            {
                DebugLogWarning("CreateBaseMaterial could not find a suitable shader. Popups may be invisible.");
                return null;
            }

            mat = new Material(shader) { name = "JudgePopupAutoMat" };
        }

        if (texture != null)
        {
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            mat.mainTexture = texture;
        }
        mat.renderQueue = 3000;
        return mat;
    }

    private void ApplySpriteToMaterial(Material mat, Sprite sprite)
    {
        if (mat == null || sprite == null) return;

        var tex = sprite.texture;
        if (tex == null) return;

        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;

        // 將縮放/偏移重置為 1/0，交給 TextureSheetAnimation 依 Sprite 定義處理裁切
        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTextureScale("_MainTex", Vector2.one);
            mat.SetTextureOffset("_MainTex", Vector2.zero);
        }
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
        }
    }

    private Vector3 ResolveFlyOutDirection(Quaternion rotation)
    {
        if (!enableFlyOutMotion) return Vector3.zero;

        if (flyOutAlignsWithCameraUp)
        {
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            var cam = _cachedCamera;
            if (cam != null)
            {
                var camUp = cam.transform.up;
                if (camUp.sqrMagnitude > 0.0001f) return camUp.normalized;
            }
        }

        Vector3 baseDir = flyOutDirection.sqrMagnitude > 0.0001f ? flyOutDirection : Vector3.up;
        if (flyOutDirectionUsesWorldSpace)
        {
            return baseDir.normalized;
        }

        return (rotation * baseDir).normalized;
    }

    private IEnumerator AnimateFlyOut(ParticleSystem ps, Vector3 origin, Vector3 worldDir)
    {
        if (ps == null || worldDir.sqrMagnitude < 0.0001f) yield break;
        worldDir = worldDir.normalized;

        float duration = Mathf.Clamp(flyOutDuration, 0.01f, lifetime);
        if (duration <= 0f) yield break;

        Vector3 destination = origin + worldDir * Mathf.Max(0f, flyOutDistance);
        var particleBuffer = _sharedParticleBuffer;

        float elapsed = 0f;
        while (elapsed < duration && ps != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            int count = ps.GetParticles(particleBuffer);
            if (count > 0)
            {
                particleBuffer[0].position = Vector3.LerpUnclamped(origin, destination, eased);
                ps.SetParticles(particleBuffer, count);
            }

            yield return null;
        }

        if (ps != null)
        {
            int count = ps.GetParticles(particleBuffer);
            if (count > 0)
            {
                particleBuffer[0].position = destination;
                ps.SetParticles(particleBuffer, count);
            }
        }
    }

    private static Mesh _sharedQuadMesh;

    private static Mesh SharedQuadMesh
    {
        get
        {
            if (_sharedQuadMesh != null) return _sharedQuadMesh;

            var mesh = new Mesh { name = "JudgePopupQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _sharedQuadMesh = mesh;
            return _sharedQuadMesh;
        }
    }
}

