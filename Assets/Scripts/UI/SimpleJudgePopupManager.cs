using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleJudgePopupManager : MonoBehaviour
{
    private const int ForegroundRenderQueue = 4500;
    private const int ForegroundSortingOrder = 32767;
    private static SimpleJudgePopupManager _instance;
    public static SimpleJudgePopupManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // 連未啟用的場景實例一起找，否則會在 gameplay root 還關著的時候
                // 建出替身，把設定好的那個擠掉（見 SceneSingleton）。
                _instance = SceneSingleton.Find<SimpleJudgePopupManager>();
                if (_instance == null)
                {
                    var go = new GameObject("SimpleJudgePopupManager");
                    _instance = go.AddComponent<SimpleJudgePopupManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    [Header("Enable")]
    [Tooltip("When false, this popup manager is completely disabled and will not show any popups.")]
    public bool enablePopup = true;

    [Header("Pool")]
    public int poolSize = 8;

    [Header("Timing (adjustable at runtime)")]
    [Tooltip("How long the popup flies upward (seconds).")]
    public float flyOutDuration = 0.1f;
    [Tooltip("How long the popup fades out after fly-out finishes (seconds).")]
    public float fadeDuration = 0.3f;

    [Header("Fly-Out")]
    [Tooltip("Offset toward camera so popup renders in front of geometry.")]
    public float forwardOffset = 0.5f;
    // Fixed fly distance (world units). Popup always travels exactly this far.
    private const float FLY_DISTANCE = 2f;

    [Header("Motion")]
    [Tooltip("每秒衰減多少速度。越大停得越快。")]
    public float damping = 6.5f;
    [Tooltip("彈出時撐開的幅度。")]
    [Range(0f, 0.6f)] public float popScale = 0.30f;

    [Header("Scale")]
    public float worldPopupScale = 1.0f;
    public int worldSortingOrder = 2000;

    // sprites can be assigned in inspector or loaded from Resources/graphic/judge by name
    public Sprite justSprite;
    public Sprite greatSprite;
    public Sprite goodSprite;
    public Sprite missSprite;
    public Sprite preciseSprite;

    private List<GameObject> pool = new List<GameObject>();
    private Transform container;
    private static Material overlayMaterial;
    private Transform judgmentLineTransform;
    // Track running hide coroutines per pooled item so we can cancel stale fades when reusing
    private readonly System.Collections.Generic.Dictionary<GameObject, Coroutine> activeCoroutines = new System.Collections.Generic.Dictionary<GameObject, Coroutine>();
    // Cached Camera.main — refreshed only when null, avoids FindMainCamera() every judgment
    private Camera _cachedCamera;
    private readonly System.Collections.Generic.Dictionary<GameObject, SpriteRenderer> _srCache = new System.Collections.Generic.Dictionary<GameObject, SpriteRenderer>();

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // create container
        var existing = GameObject.Find("SimpleJudgePopupContainer");
        if (existing != null) container = existing.transform;
        if (container == null)
        {
            var c = new GameObject("SimpleJudgePopupContainer");
            container = c.transform; container.SetParent(null);
        }

    // try load sprites from Resources/graphic/judge (case-insensitive name mapping)
        if (justSprite == null || greatSprite == null || goodSprite == null || missSprite == null
            || preciseSprite == null)
        {
            try
            {
                var loaded = Resources.LoadAll<Sprite>("graphic/judge");
                if (loaded != null && loaded.Length > 0)
                {
                    var map = new Dictionary<string, Sprite>();
                    foreach (var s in loaded)
                    {
                        if (s == null) continue;
                        var key = s.name.Trim().ToLowerInvariant();
                        if (!map.ContainsKey(key)) map[key] = s;
                    }
                    if (justSprite == null && map.TryGetValue("just", out var js)) justSprite = js;
                    if (greatSprite == null && map.TryGetValue("great", out var gs)) greatSprite = gs;
                    if (goodSprite == null && map.TryGetValue("good", out var gos)) goodSprite = gos;
                    if (missSprite == null && map.TryGetValue("miss", out var ms)) missSprite = ms;
                    if (preciseSprite == null && map.TryGetValue("precise", out var prs)) preciseSprite = prs;
                }
            }
            catch { }

            // fallback: try common lowercase paths
            if (justSprite == null) justSprite = Resources.Load<Sprite>("graphic/judge/Just") ?? Resources.Load<Sprite>("graphic/judge/just");
            if (greatSprite == null) greatSprite = Resources.Load<Sprite>("graphic/judge/Great") ?? Resources.Load<Sprite>("graphic/judge/great");
            if (goodSprite == null) goodSprite = Resources.Load<Sprite>("graphic/judge/Good") ?? Resources.Load<Sprite>("graphic/judge/good");
            if (missSprite == null) missSprite = Resources.Load<Sprite>("graphic/judge/Miss") ?? Resources.Load<Sprite>("graphic/judge/miss");
            if (preciseSprite == null) preciseSprite = Resources.Load<Sprite>("graphic/judge/Precise") ?? Resources.Load<Sprite>("graphic/judge/precise");
        }

        // Debug: report loaded sprites and pool config
        try
        {
            var dbg = false;
            try { dbg = SettingsManager.Instance != null && SettingsManager.Instance.DebugMode; } catch { dbg = false; }
            // report loaded sprite names so we can diagnose Resources loading issues
            try
            {
                Debug.Log($"SimpleJudgePopupManager: loaded sprites -> just={(justSprite!=null?justSprite.name:"(null)")}, great={(greatSprite!=null?greatSprite.name:"(null)")}, good={(goodSprite!=null?goodSprite.name:"(null)")}, miss={(missSprite!=null?missSprite.name:"(null)")}");
            }
            catch { }
        }
        catch { }

        // create pool
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("SimpleJudgePopup");
            go.transform.SetParent(container, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.enabled = false;
            sr.sortingOrder = Mathf.Max(worldSortingOrder, ForegroundSortingOrder);
            // create overlay material once and assign so sprites render on top of geometry
            if (overlayMaterial == null)
            {
                // Judgment artwork needs depth-independent rendering, but must
                // not share the Hold-magic palette shader. That shader applies
                // radial HDR colouring and was turning Just/Great text green or
                // blue depending on stale property-block values.
                var baseShader = Shader.Find("Nostalgia/JudgePopupOverlay");
                if (baseShader == null) baseShader = Shader.Find("Sprites/Default");
                if (baseShader != null)
                {
                    overlayMaterial = new Material(baseShader) { name = "SimpleJudgePopup_OverlayMat" };
                    overlayMaterial.renderQueue = ForegroundRenderQueue;
                }
            }
            if (overlayMaterial != null) sr.sharedMaterial = overlayMaterial;
            go.SetActive(false);
            pool.Add(go);
            _srCache[go] = sr;
        }
    // try { if (SettingsManager.Instance != null && SettingsManager.Instance.DebugMode) Debug.Log($"SimpleJudgePopupManager: pool created size={pool.Count} container={container.name} overlayMat={(overlayMaterial!=null?overlayMaterial.name:"(null)")}"); } catch { }

        // cache JudgmentLine transform if present in scene
        var jlGo = GameObject.Find("JudgmentLine");
        if (jlGo != null) judgmentLineTransform = jlGo.transform;

        _cachedCamera = Camera.main;

    }

    // Map JudgmentResult to sprite
    private Sprite SpriteForResult(JudgmentResult r, bool precise = false)
    {
        if (precise && r == JudgmentResult.Perfect && preciseSprite != null) return preciseSprite;
        switch (r)
        {
            case JudgmentResult.Perfect: return justSprite;
            case JudgmentResult.Great: return greatSprite;
            case JudgmentResult.Good: return goodSprite;
            case JudgmentResult.Miss:
            case JudgmentResult.Fail: return missSprite;
            default: return null;
        }
    }

    // Show popup at given world position
    public void ShowAtPosition(Vector3 worldPos, JudgmentResult result, bool precise = false)
    {
        if (!enablePopup) return;

        var sprite = SpriteForResult(result, precise);
        if (sprite == null) return;

        GameObject item = null;
        foreach (var go in pool) if (!go.activeSelf) { item = go; break; }
        if (item == null)
        {
            item = new GameObject("SimpleJudgePopupExtra");
            item.transform.SetParent(container, false);
            var srx = item.AddComponent<SpriteRenderer>();
            srx.sortingOrder = Mathf.Max(worldSortingOrder, ForegroundSortingOrder);
            if (overlayMaterial != null) srx.sharedMaterial = overlayMaterial;
            pool.Add(item);
            _srCache[item] = srx;
        }

        if (!_srCache.TryGetValue(item, out var sr)) { sr = item.GetComponent<SpriteRenderer>(); if (sr != null) _srCache[item] = sr; }
        sr.sprite = sprite;
        // A pooled renderer must never inherit an RGB tint from an older use.
        // The sprite itself owns the classical judgment colour.
        sr.color = Color.white;
        sr.enabled = true;

        // Determine judgment line world position for this note's X lane
        if (_cachedCamera == null) _cachedCamera = Camera.main;
        Camera cam = _cachedCamera;

        // Base position = judgment line at the note's X; fallback to note position
        Vector3 jlPos;
        if (judgmentLineTransform != null)
            jlPos = new Vector3(worldPos.x, judgmentLineTransform.position.y, judgmentLineTransform.position.z);
        else
            jlPos = worldPos;

        // Target = judgment line + setting height (in screen space)
        // Read "判定顯示高度" from SettingsManager live (minimum 30)
        // Setting value is a percentage of screen height (30 = 30%, 50 = 50%)
        float targetHeight = 30f;
        var sm = SettingsManager.Instance;
        if (sm != null) targetHeight = Mathf.Max(30f, sm.JudgePopupHeight);

        // Convert screen percentages to world positions via camera
        Vector3 targetPos, spawnPos;
        if (cam != null)
        {
            // Target viewport Y = targetHeight / 100
            float targetVpY = targetHeight / 100f;
            // Spawn viewport Y = (targetHeight - FLY_DISTANCE) / 100
            float spawnVpY = (targetHeight - FLY_DISTANCE) / 100f;

            // Get viewport X from the note's world position
            Vector3 noteVp = cam.WorldToViewportPoint(jlPos);
            float vpX = noteVp.x;
            // Use a fixed depth (distance from camera) based on judgment line
            float depth = noteVp.z;

            targetPos = cam.ViewportToWorldPoint(new Vector3(vpX, targetVpY, depth));
            spawnPos = cam.ViewportToWorldPoint(new Vector3(vpX, spawnVpY, depth));
        }
        else
        {
            // Fallback without camera
            Vector3 camUp = Vector3.up;
            float scale = 0.1f;
            targetPos = jlPos + camUp * targetHeight * scale;
            spawnPos = jlPos + camUp * (targetHeight - FLY_DISTANCE) * scale;
        }

        // Push slightly toward camera so it renders in front
        if (cam != null)
        {
            Vector3 dirToCam = (cam.transform.position - spawnPos).normalized;
            spawnPos += dirToCam * forwardOffset;
            targetPos += dirToCam * forwardOffset;
            // Billboard: face the camera
            item.transform.rotation = cam.transform.rotation;
        }
        else
        {
            item.transform.rotation = Quaternion.identity;
        }

        item.transform.position = spawnPos;
        item.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldPopupScale);
        item.SetActive(true);

        // Cancel any existing coroutine on this item
        if (activeCoroutines.TryGetValue(item, out var old))
        {
            if (old != null) StopCoroutine(old);
            activeCoroutines.Remove(item);
        }
        var c = StartCoroutine(AnimatePopup(item, spawnPos, targetPos));
        activeCoroutines[item] = c;
    }

    /// <summary>
    /// Throws the word like a particle: launched hard, slowed by drag, and faded
    /// out where it comes to rest.
    /// </summary>
    /// <remarks>
    /// **Why not two phases.** It used to fly for 0.1s on a SmoothStep and then
    /// sit still for 0.3s while it faded. SmoothStep eases *in and out*, so it
    /// started slowly, and stopping dead before the fade made the whole thing
    /// read as two separate events -- a slide, then a disappearance.
    ///
    /// **What a particle does instead.** It leaves with all its speed at once
    /// and loses it to drag: position integrates v0 * exp(-damping * t), which
    /// covers most of the distance in the first fifty milliseconds and then
    /// glides. That is the "thrown" feel, and it is what makes a burst of them
    /// during a dense passage look like the notes threw them rather than like a
    /// list animating.
    ///
    /// **Why v0 is solved rather than tuned.** The travel is fixed (the word has
    /// to end up at the judgment line), so the launch speed is whatever makes
    /// the integral of the decay come out to exactly that distance over the
    /// life. Changing the damping then changes the *character* of the motion
    /// without moving where it lands.
    /// </remarks>
    private IEnumerator AnimatePopup(GameObject go, Vector3 spawnPos, Vector3 targetPos)
    {
        if (go == null) yield break;
        if (!_srCache.TryGetValue(go, out var sr)) { sr = go.GetComponent<SpriteRenderer>(); if (sr != null) _srCache[go] = sr; }
        if (sr == null) { go.SetActive(false); yield break; }

        Vector3 travel = targetPos - spawnPos;
        float distance = travel.magnitude;
        Vector3 direction = distance > 1e-4f ? travel / distance : Vector3.up;

        float life = Mathf.Max(0.06f, flyOutDuration + fadeDuration);
        float drag = Mathf.Max(0.5f, damping);
        // ∫v0·e^(-k·t) dt over the life = distance  →  v0 = d·k / (1 - e^(-k·life))
        float launch = distance * drag / Mathf.Max(0.001f, 1f - Mathf.Exp(-drag * life));
        Vector3 baseScale = go.transform.localScale;

        float elapsed = 0f;
        float travelled = 0f;
        while (elapsed < life)
        {
            if (go == null) yield break;
            float dt = Time.deltaTime;
            elapsed += dt;

            travelled += launch * Mathf.Exp(-drag * elapsed) * dt;
            go.transform.position = spawnPos + direction * Mathf.Min(travelled, distance * 1.02f);

            // 撐開再回彈。粒子被丟出來的那一刻是最大的，然後收回原尺寸。
            float pop = 1f + popScale * Mathf.Exp(-elapsed * 13f) * Mathf.Cos(elapsed * 24f);
            go.transform.localScale = baseScale * pop;

            // 最後三分之一才開始淡出：字要先讀得到，才輪得到它消失。
            float fadeFrom = life * 0.62f;
            float a = elapsed <= fadeFrom
                ? 1f
                : 1f - Mathf.Clamp01((elapsed - fadeFrom) / Mathf.Max(0.001f, life - fadeFrom));
            var c = sr.color; c.a = a * a; sr.color = c;
            yield return null;
        }

        if (go != null) go.transform.localScale = baseScale;

        // Done
        if (go != null)
        {
            var c2 = sr.color; c2.a = 0f; sr.color = c2;
            sr.enabled = false;
            go.SetActive(false);
        }
        if (activeCoroutines.ContainsKey(go)) activeCoroutines.Remove(go);
    }
}
