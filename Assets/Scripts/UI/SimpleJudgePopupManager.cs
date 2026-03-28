using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleJudgePopupManager : MonoBehaviour
{
    private static SimpleJudgePopupManager _instance;
    public static SimpleJudgePopupManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SimpleJudgePopupManager>();
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

    [Header("Pool")]
    public int poolSize = 8;
    public float displayTime = 0.1F;
    [Tooltip("How long the popup fades out after displayTime (seconds)")]
    public float fadeDuration = 0.5f;

    [Header("Placement")]
    public float worldVerticalOffset = 0.6f;
    public float worldForwardOffset = 0.02f;
    [Tooltip("Extra forward offset (world units) to apply when we fallback from an offscreen anchor. Helps bring the popup in front of geometry/camera.")]
    public float fallbackForwardOffset = 1.0f;
    // larger default so sprites are more likely visible; tune in Inspector
    public float worldPopupScale = 1.0f;
    public int worldSortingOrder = 2000;
    [Tooltip("Additional Z offset (world units) applied to the popup's Z coordinate. Use this to move popups forward/back relative to the judgment line or spawn anchor.")]
    public float judgePopupZOffset = 0f;

    // sprites can be assigned in inspector or loaded from Resources/graphic/judge by name
    public Sprite justSprite;
    public Sprite greatSprite;
    public Sprite goodSprite;
    public Sprite missSprite;

    private List<GameObject> pool = new List<GameObject>();
    private Transform container;
    private static Material overlayMaterial;
    private Transform judgmentLineTransform;
    // Track running hide coroutines per pooled item so we can cancel stale fades when reusing
    private readonly System.Collections.Generic.Dictionary<GameObject, Coroutine> activeCoroutines = new System.Collections.Generic.Dictionary<GameObject, Coroutine>();
    // Cached Camera.main — refreshed only when null, avoids FindMainCamera() every judgment
    private Camera _cachedCamera;
    // Cached SpriteRenderer per pooled item — avoids GetComponent() every judgment
    private readonly System.Collections.Generic.Dictionary<GameObject, SpriteRenderer> _srCache = new System.Collections.Generic.Dictionary<GameObject, SpriteRenderer>();
    // Cached WaitForSeconds for displayTime — avoids per-coroutine allocation
    private WaitForSeconds _cachedWaitForDisplay;

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
        if (justSprite == null || greatSprite == null || goodSprite == null || missSprite == null)
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
                }
            }
            catch { }

            // fallback: try common lowercase paths
            if (justSprite == null) justSprite = Resources.Load<Sprite>("graphic/judge/Just") ?? Resources.Load<Sprite>("graphic/judge/just");
            if (greatSprite == null) greatSprite = Resources.Load<Sprite>("graphic/judge/Great") ?? Resources.Load<Sprite>("graphic/judge/great");
            if (goodSprite == null) goodSprite = Resources.Load<Sprite>("graphic/judge/Good") ?? Resources.Load<Sprite>("graphic/judge/good");
            if (missSprite == null) missSprite = Resources.Load<Sprite>("graphic/judge/Miss") ?? Resources.Load<Sprite>("graphic/judge/miss");
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
            sr.sortingOrder = worldSortingOrder;
            // create overlay material once and assign so sprites render on top of geometry
            if (overlayMaterial == null)
            {
                var baseShader = Shader.Find("Sprites/Default");
                if (baseShader != null)
                {
                    overlayMaterial = new Material(baseShader) { name = "SimpleJudgePopup_OverlayMat" };
                    // push renderQueue high so it draws after opaque geometry
                    overlayMaterial.renderQueue = 4000;
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
        _cachedWaitForDisplay = new WaitForSeconds(displayTime);

        // If a saved setting exists, pick up judge popup Z offset from SettingsManager so
        // the manager uses the user's preference even if SettingsManager applied earlier
        try
        {
            var sm = SettingsManager.Instance;
            if (sm != null)
            {
                judgePopupZOffset = sm.JudgePopupZOffset;
                // if (sm.DebugMode) Debug.Log($"SimpleJudgePopupManager: picked up JudgePopupZOffset from SettingsManager = {judgePopupZOffset}");
            }
        }
        catch { }
    }

    // Map JudgmentResult to sprite
    private Sprite SpriteForResult(JudgmentResult r)
    {
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
    public void ShowAtPosition(Vector3 worldPos, JudgmentResult result)
    {
        var sprite = SpriteForResult(result);
        if (sprite == null)
        {
            Debug.LogWarning($"SimpleJudgePopupManager: ShowAtPosition - no sprite for result={result}. Current sprites -> just={(justSprite!=null?justSprite.name:"(null)")}, great={(greatSprite!=null?greatSprite.name:"(null)")}, good={(goodSprite!=null?goodSprite.name:"(null)")}, miss={(missSprite!=null?missSprite.name:"(null)")}" );
            return;
        }

        GameObject item = null;
        foreach (var go in pool) if (!go.activeSelf) { item = go; break; }
        if (item == null)
        {
            item = new GameObject("SimpleJudgePopupExtra");
            item.transform.SetParent(container, false);
            var srx = item.AddComponent<SpriteRenderer>();
            srx.sortingOrder = worldSortingOrder;
            if (overlayMaterial != null) srx.sharedMaterial = overlayMaterial;
            pool.Add(item);
            _srCache[item] = srx;
        }

        if (!_srCache.TryGetValue(item, out var sr)) { sr = item.GetComponent<SpriteRenderer>(); if (sr != null) _srCache[item] = sr; }
        sr.sprite = sprite;
    // Ensure alpha reset (previous fade may have set alpha to 0)
    var col = sr.color; col.a = 1f; sr.color = col;
    sr.enabled = true;

        // place slightly above the judgment line but keep the note's X (user requested)
        // If a JudgmentLine exists in scene, align Y/Z to that line; keep X from the note's worldPos.
        Vector3 pos;
        var effectiveZOffset = judgePopupZOffset;

        if (judgmentLineTransform != null)
        {
            // Use judgment line Y (plus offset) and allow adjustable Z offset
            pos = new Vector3(worldPos.x, judgmentLineTransform.position.y + worldVerticalOffset, judgmentLineTransform.position.z + effectiveZOffset);
            // if (dbg) Debug.Log($"SimpleJudgePopupManager: Using judgmentLine Z={judgmentLineTransform.position.z} judgePopupZOffset={effectiveZOffset} -> computed Z={pos.z}");
        }
        else
        {
            // fallback: use note position Y and Z, but apply Z offset as well so users can nudge forward/back
            pos = new Vector3(worldPos.x, worldPos.y + worldVerticalOffset, worldPos.z + effectiveZOffset);
            // if (dbg) Debug.Log($"SimpleJudgePopupManager: No JudgmentLine; using note Z={worldPos.z} judgePopupZOffset={effectiveZOffset} -> computed Z={pos.z}");
        }
        if (_cachedCamera == null) _cachedCamera = Camera.main;
        Camera cam = _cachedCamera;
        // Safety: if computed anchor position is outside camera view (or behind camera),
        // fall back to using the note's world position as Y/Z base to avoid popups placed far away (e.g. y=400).
        if (cam != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(pos);
            if (vp.z <= 0f || vp.x < -0.2f || vp.x > 1.2f || vp.y < -0.2f || vp.y > 1.2f)
            {
                // fallback to note position Y/Z
                // if (dbg) Debug.Log($"SimpleJudgePopupManager: computed anchor pos {pos} is offscreen (vp={vp}); falling back to note-based Y/Z.");
                pos = new Vector3(worldPos.x, worldPos.y + worldVerticalOffset, worldPos.z);
            }

            Vector3 dirToCam = (cam.transform.position - pos).normalized;
            // If we just fell back because anchor was offscreen, push the popup further toward the camera
            // to ensure it's rendered in front of track geometry. Use fallbackForwardOffset for that.
            float useForward = worldForwardOffset;
            // Simple heuristic: if the computed viewport was outside, use the larger fallback offset
            if (vp.z <= 0f || vp.x < -0.2f || vp.x > 1.2f || vp.y < -0.2f || vp.y > 1.2f) useForward = Mathf.Max(useForward, fallbackForwardOffset);
            pos += dirToCam * useForward;
            // lay flat on track and align yaw to camera so text lies readable on XZ plane
            float camYaw = cam.transform.eulerAngles.y;
            item.transform.rotation = Quaternion.Euler(90f, camYaw, 0f);
        }
        else
        {
            // No camera available: keep flattened orientation
            item.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // If no camera, fallback to note-based Y as well to be safe
            pos = new Vector3(worldPos.x, worldPos.y + worldVerticalOffset, worldPos.z);
        }

        item.transform.position = pos;
        item.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldPopupScale);
    // if (dbg) Debug.Log($"SimpleJudgePopupManager: placed item={item.name} at pos={pos} scale={item.transform.localScale} sortingOrder={sr.sortingOrder}");
        item.SetActive(true);
        // If there's an existing hide coroutine for this item, stop it so the new display won't be overridden
        try
        {
            if (activeCoroutines.TryGetValue(item, out var old))
            {
                if (old != null) StopCoroutine(old);
                activeCoroutines.Remove(item);
            }
        }
        catch { }
        var c = StartCoroutine(HideAndFade(item, displayTime, fadeDuration));
        try { activeCoroutines[item] = c; } catch { }
    }

    private IEnumerator HideAndFade(GameObject go, float delay, float fadeTime)
    {
        yield return _cachedWaitForDisplay ?? new WaitForSeconds(delay);
        if (go == null) yield break;
        if (!_srCache.TryGetValue(go, out var sr)) { sr = go.GetComponent<SpriteRenderer>(); if (sr != null) _srCache[go] = sr; }
        if (sr == null)
        {
            go.SetActive(false);
            yield break;
        }

        // Ensure starting alpha is current sprite alpha (usually 1)
        Color start = sr.color;
        float startA = start.a;
        float elapsed = 0f;
        // If fadeTime <= 0, just disable immediately
        if (fadeTime <= 0f)
        {
            sr.enabled = false;
            go.SetActive(false);
            yield break;
        }

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            // simple linear fade; could use easing if desired
            float a = Mathf.Lerp(startA, 0f, t);
            var c = sr.color; c.a = a; sr.color = c;
            yield return null;
        }

        // fully faded
        var final = sr.color; final.a = 0f; sr.color = final;
        sr.enabled = false;
        go.SetActive(false);
        // clear active coroutine record
        try { if (activeCoroutines.ContainsKey(go)) activeCoroutines.Remove(go); } catch { }
    }
}
