using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages pooled hit effects for key inputs. Replaces coroutine-based animation with
/// per-frame state updates to avoid performance spikes when rapidly spawning effects.
/// </summary>
public class KeyHitEffectManager : MonoBehaviour
{
    private static KeyHitEffectManager _instance;

    public static KeyHitEffectManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // 連未啟用的場景實例一起找，否則會在 gameplay root 還關著的時候
                // 建出替身，把設定好的那個擠掉（見 SceneSingleton）。
                _instance = SceneSingleton.Find<KeyHitEffectManager>();
                if (_instance == null)
                {
                    var go = new GameObject("KeyHitEffectManager");
                    _instance = go.AddComponent<KeyHitEffectManager>();
                    DontDestroyOnLoad(go);
                }
            }

            return _instance;
        }
    }

    public static KeyHitEffectManager TryGetExistingInstance()
    {
        if (_instance != null) return _instance;

        var found = FindFirstObjectByType<KeyHitEffectManager>();
        if (found != null)
        {
            _instance = found;
        }
        return found;
    }

    [Header("Sprite Effects")]
    public int poolSize = 16;
    public Sprite effectSprite;
    public Color effectColor = Color.white;
    public float duration = 0.45f;
    public float upwardDistance = 0.6f;
    public float startScale = 0.9f;
    public float endScale = 1.3f;
    public int sortingOrder = 3000;
    [Tooltip("Optional XYZ offset applied to sprite hit effects (world units).")]
    public Vector3 spriteOffset = Vector3.zero;

    [Header("Mesh Effects")]
    public Material meshMaterial;
    public int meshPoolSize = 16;
    public float meshHeight = 0.6f;
    public float meshZOffset = 0.02f;
    [Tooltip("Optional XYZ offset applied to mesh hit effects (world units).")]
    public Vector3 meshOffset = Vector3.zero;

    private readonly List<GameObject> pool = new List<GameObject>();
    private readonly List<GameObject> meshPool = new List<GameObject>();
    private readonly Dictionary<int, GameObject> activePersistentMeshes = new Dictionary<int, GameObject>();
    private readonly List<SpriteEffectState> activeSpriteEffects = new List<SpriteEffectState>(32);
    private readonly List<MeshEffectState> activeMeshEffects = new List<MeshEffectState>(32);
    private readonly Stack<SpriteEffectState> spriteStatePool = new Stack<SpriteEffectState>(32);
    private readonly Stack<MeshEffectState> meshStatePool = new Stack<MeshEffectState>(32);

    private Transform container;
    private static Material overlayMaterial;
    private Transform cachedJudgmentLine;
    private Camera cachedMainCamera;
    private readonly Vector3[] reusableCorners = new Vector3[4];
    private MaterialPropertyBlock sharedMeshPropertyBlock;
    private int cachedSortingOrder = int.MinValue;
    private float nextSortingOrderSampleTime;
    private NoteController cachedNoteController;
    private NoteSpawner cachedNoteSpawner;
    private float nextSpawnerRefreshTime;
    private float cachedTrackWidth = -1f;
    private float nextTrackWidthSampleTime;

    private class SpriteEffectState
    {
        public GameObject GameObject;
        public SpriteRenderer Renderer;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Color StartColor;
        public float StartTime;
        public float Duration;
    }

    private class MeshEffectState
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Color BaseColor;
        public float StartTime;
        public float Duration;
        public bool HasColorProperty;
        // new fields for height-based animation anchored at judgment line
        public float StartHeight;
        public float EndHeight;
        public float AnchorY; // top Y (judgment line)
        public float Width;
        public Vector3 BaseCenter; // X,Z of center, Y is computed from height
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        container = GameObject.Find("KeyHitEffectContainer")?.transform;
        if (container == null)
        {
            container = new GameObject("KeyHitEffectContainer").transform;
            container.SetParent(null);
        }

        if (overlayMaterial == null)
        {
            var baseShader = Shader.Find("Sprites/Default");
            if (baseShader != null)
            {
                overlayMaterial = new Material(baseShader)
                {
                    name = "KeyHitEffect_OverlayMat",
                    renderQueue = 4000
                };
            }
        }

        cachedMainCamera = Camera.main;
        cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;

        sharedMeshPropertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < poolSize; i++) pool.Add(CreateSpriteEffectObject("KeyHitEffect"));
        for (int i = 0; i < meshPoolSize; i++) meshPool.Add(CreateMeshEffectObject("KeyHitMesh"));
    }

    private GameObject CreateSpriteEffectObject(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(container, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.enabled = false;
        sr.sortingOrder = sortingOrder;
        if (overlayMaterial != null) sr.sharedMaterial = overlayMaterial;
        go.SetActive(false);
        return go;
    }

    private GameObject CreateMeshEffectObject(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        var collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        go.transform.SetParent(container, false);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (meshMaterial != null) mr.sharedMaterial = meshMaterial;
        go.SetActive(false);
        return go;
    }

    private GameObject AcquireSpriteEffectObject()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (!pool[i].activeSelf) return pool[i];
        }

        GameObject first = null;
        int additional = Mathf.Clamp(pool.Count >> 1, 1, 8);
        for (int i = 0; i < additional; i++)
        {
            var created = CreateSpriteEffectObject("KeyHitEffectExtra");
            pool.Add(created);
            if (first == null) first = created;
        }

        return first;
    }

    private GameObject AcquireMeshEffectObject()
    {
        for (int i = 0; i < meshPool.Count; i++)
        {
            if (!meshPool[i].activeSelf) return meshPool[i];
        }

        GameObject first = null;
        int additional = Mathf.Clamp(meshPool.Count >> 1, 1, 4);
        for (int i = 0; i < additional; i++)
        {
            var created = CreateMeshEffectObject("KeyHitMeshExtra");
            meshPool.Add(created);
            if (first == null) first = created;
        }

        return first;
    }

    public void ShowEffectAtWorld(Vector3 worldPos)
    {
        if (effectSprite == null) return;

        var item = AcquireSpriteEffectObject();
        var sr = item.GetComponent<SpriteRenderer>();
        ActivateSpriteEffect(item, sr, worldPos + spriteOffset);
    }

    public void ShowEffectAtRect(RectTransform rt)
    {
        if (rt == null) return;

        var cam = cachedMainCamera != null ? cachedMainCamera : Camera.main;
        if (cam == null) return;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, rt.position);

        float planeY = 0f;
        if (cachedJudgmentLine == null)
        {
            cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;
        }

        if (cachedJudgmentLine != null) planeY = cachedJudgmentLine.position.y;

        Ray ray = cam.ScreenPointToRay(screenPoint);
        Vector3 worldPos = IntersectRayWithPlaneY(ray, planeY);
        ShowEffectAtWorld(worldPos);
    }

    public void ShowMeshAtRect(RectTransform rt)
    {
        if (rt == null) return;

        var cam = cachedMainCamera != null ? cachedMainCamera : Camera.main;
        if (cam == null) return;

        rt.GetWorldCorners(reusableCorners);
        Vector3 worldLeft = (reusableCorners[0] + reusableCorners[1]) * 0.5f;
        Vector3 worldRight = (reusableCorners[2] + reusableCorners[3]) * 0.5f;

        float planeY = 0f;
        float planeZ = 0f;

        if (cachedJudgmentLine == null)
        {
            cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;
        }

        if (cachedJudgmentLine != null)
        {
            planeY = cachedJudgmentLine.position.y;
            planeZ = cachedJudgmentLine.position.z;
        }

        Ray rLeft = cam.ScreenPointToRay(RectTransformUtility.WorldToScreenPoint(null, worldLeft));
        Ray rRight = cam.ScreenPointToRay(RectTransformUtility.WorldToScreenPoint(null, worldRight));

        Vector3 pLeft = IntersectRayWithPlaneY(rLeft, planeY);
        Vector3 pRight = IntersectRayWithPlaneY(rRight, planeY);

        float width = Vector3.Distance(pLeft, pRight);
        Vector3 center = (pLeft + pRight) * 0.5f;
        if (cachedJudgmentLine != null) center.z = planeZ + meshZOffset;
        // apply optional mesh offset (world space)
        center += meshOffset;

        var item = AcquireMeshEffectObject();
    var mr = item.GetComponent<MeshRenderer>();
    if (meshMaterial != null && mr != null) mr.sharedMaterial = meshMaterial;

        var camRotation = cam.transform.eulerAngles.y;
        // Top-anchored: place the quad so its top edge is at the judgment line (center.y)
        item.transform.position = new Vector3(center.x, center.y - (meshHeight * 0.5f), center.z);
        item.transform.rotation = Quaternion.Euler(90f, camRotation, 0f);
        item.transform.localScale = new Vector3(width, meshHeight, 1f);

    try { if (mr != null) mr.sortingOrder = GetEffectSortingOrder(); }
        catch { }

        ActivateMeshEffect(item, mr, center);
    }

    public void ShowMeshForKeyId(int id)
    {
#if UNITY_EDITOR
        // Debug.Log($"KeyHitEffectManager: ShowMeshForKeyId called id={id}");
#endif

        var rt = VirtualKeyButton.GetRectForId(id);
        if (rt != null)
        {
            ShowMeshAtRect(rt);
            return;
        }

        try
        {
            if (cachedJudgmentLine == null) cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;
            if (cachedJudgmentLine == null) return;

            float trackWidth = GetTrackWidth();

            const int totalLanes = 28;
            float laneWidth = trackWidth / totalLanes;
            float centerLane = id + 0.5f;
            float posX = (centerLane * laneWidth) - (trackWidth / 2f);

            Vector3 pLeft = new Vector3(posX - laneWidth * 0.5f, cachedJudgmentLine.position.y, cachedJudgmentLine.position.z);
            Vector3 pRight = new Vector3(posX + laneWidth * 0.5f, cachedJudgmentLine.position.y, cachedJudgmentLine.position.z);

            float width = Vector3.Distance(pLeft, pRight);
            Vector3 center = (pLeft + pRight) * 0.5f;
            center.z = cachedJudgmentLine.position.z + meshZOffset;
            center += meshOffset;

            var item = AcquireMeshEffectObject();
            var mr = item.GetComponent<MeshRenderer>();
            if (meshMaterial != null && mr != null) mr.sharedMaterial = meshMaterial;

            var cam = cachedMainCamera != null ? cachedMainCamera : Camera.main;
            // Top-anchored initial placement
            item.transform.position = new Vector3(center.x, center.y - (meshHeight * 0.5f), center.z);
            item.transform.rotation = Quaternion.Euler(90f, cam != null ? cam.transform.eulerAngles.y : 0f, 0f);
            item.transform.localScale = new Vector3(width, meshHeight, 1f);

            try { if (mr != null) mr.sortingOrder = GetEffectSortingOrder(); }
            catch { }

            if (SettingsManager.Instance != null && SettingsManager.Instance.DebugMode)
            {
                try
                {
                    if (mr != null && mr.sharedMaterial != null) mr.sharedMaterial.renderQueue = 4000;
                }
                catch { }
            }

            ActivateMeshEffect(item, mr, center);
        }
        catch { }
    }

    public void ShowPersistentMeshForKeyId(int id)
    {
        if (activePersistentMeshes.ContainsKey(id)) return;

        var rt = VirtualKeyButton.GetRectForId(id);
        var item = AcquireMeshEffectObject();
    var mr = item.GetComponent<MeshRenderer>();
    if (meshMaterial != null && mr != null) mr.sharedMaterial = meshMaterial;

        Vector3 center;
        float width;
        var cam = cachedMainCamera != null ? cachedMainCamera : Camera.main;

        if (cachedJudgmentLine == null) cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;

        if (rt != null && cam != null)
        {
            rt.GetWorldCorners(reusableCorners);
            Vector3 worldLeft = (reusableCorners[0] + reusableCorners[1]) * 0.5f;
            Vector3 worldRight = (reusableCorners[2] + reusableCorners[3]) * 0.5f;
            float planeY = cachedJudgmentLine != null ? cachedJudgmentLine.position.y : 0f;
            Ray rLeft = cam.ScreenPointToRay(RectTransformUtility.WorldToScreenPoint(null, worldLeft));
            Ray rRight = cam.ScreenPointToRay(RectTransformUtility.WorldToScreenPoint(null, worldRight));
            Vector3 pLeft = IntersectRayWithPlaneY(rLeft, planeY);
            Vector3 pRight = IntersectRayWithPlaneY(rRight, planeY);
            width = Vector3.Distance(pLeft, pRight);
            center = (pLeft + pRight) * 0.5f;
        }
        else
        {
            if (cachedJudgmentLine == null) return;

            float trackWidth = GetTrackWidth();

            const int totalLanes = 28;
            float laneWidth = trackWidth / totalLanes;
            float centerLane = id + 0.5f;
            float posX = (centerLane * laneWidth) - (trackWidth / 2f);

            Vector3 pLeft = new Vector3(posX - laneWidth * 0.5f, cachedJudgmentLine.position.y, cachedJudgmentLine.position.z);
            Vector3 pRight = new Vector3(posX + laneWidth * 0.5f, cachedJudgmentLine.position.y, cachedJudgmentLine.position.z);
            width = Vector3.Distance(pLeft, pRight);
            center = (pLeft + pRight) * 0.5f;
        }

        if (cachedJudgmentLine != null) center.z = cachedJudgmentLine.position.z + meshZOffset;
        center += meshOffset;

        if (cam != null)
        {
            item.transform.rotation = Quaternion.Euler(90f, cam.transform.eulerAngles.y, 0f);
        }
        else
        {
            item.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // Place persistent mesh top-anchored so top aligns with judgment line
        item.transform.position = new Vector3(center.x, center.y - (meshHeight * 0.5f), center.z);
        item.transform.localScale = new Vector3(width, meshHeight, 1f);

    try { if (mr != null) mr.sortingOrder = GetEffectSortingOrder(); }
        catch { }

        item.SetActive(true);
        activePersistentMeshes[id] = item;
    }

    public void HidePersistentMeshForKeyId(int id)
    {
        if (!activePersistentMeshes.TryGetValue(id, out var item)) return;

        item.SetActive(false);
        activePersistentMeshes.Remove(id);
    }

    private Vector3 IntersectRayWithPlaneY(Ray ray, float planeY)
    {
        if (Mathf.Abs(ray.direction.y) < 0.0001f)
        {
            var fallback = ray.origin + ray.direction * 5f;
            fallback.y = planeY;
            return fallback;
        }

        float t = (planeY - ray.origin.y) / ray.direction.y;
        if (t < 0f) t = 0.01f;
        return ray.origin + ray.direction * t;
    }

    private SpriteEffectState GetSpriteState()
    {
        return spriteStatePool.Count > 0 ? spriteStatePool.Pop() : new SpriteEffectState();
    }

    private MeshEffectState GetMeshState()
    {
        return meshStatePool.Count > 0 ? meshStatePool.Pop() : new MeshEffectState();
    }

    private void ReleaseSpriteState(int index)
    {
        var state = activeSpriteEffects[index];
        int lastIndex = activeSpriteEffects.Count - 1;
        if (index != lastIndex)
        {
            activeSpriteEffects[index] = activeSpriteEffects[lastIndex];
        }
        activeSpriteEffects.RemoveAt(lastIndex);
        state.GameObject = null;
        state.Renderer = null;
        state.Duration = 0f;
        state.StartTime = 0f;
        state.StartPosition = Vector3.zero;
        state.EndPosition = Vector3.zero;
        state.StartColor = Color.clear;
        spriteStatePool.Push(state);
    }

    private void ReleaseMeshState(int index)
    {
        var state = activeMeshEffects[index];
        int lastIndex = activeMeshEffects.Count - 1;
        if (index != lastIndex)
        {
            activeMeshEffects[index] = activeMeshEffects[lastIndex];
        }
        activeMeshEffects.RemoveAt(lastIndex);
        state.GameObject = null;
        state.Renderer = null;
        state.Duration = 0f;
        state.StartTime = 0f;
        state.HasColorProperty = false;
        state.StartPosition = Vector3.zero;
        state.EndPosition = Vector3.zero;
        state.BaseColor = Color.clear;
        state.StartHeight = 0f;
        state.EndHeight = 0f;
        state.AnchorY = 0f;
        state.Width = 0f;
        state.BaseCenter = Vector3.zero;
        meshStatePool.Push(state);
    }

    private void ActivateSpriteEffect(GameObject go, SpriteRenderer sr, Vector3 worldPos)
    {
        sr.sprite = effectSprite;
        sr.color = effectColor;
        sr.sortingOrder = GetEffectSortingOrder();
        sr.enabled = true;

        Vector3 startPos = worldPos;
        Vector3 endPos = worldPos + Vector3.up * upwardDistance;

        go.transform.position = startPos;
        var cam = cachedMainCamera != null ? cachedMainCamera : Camera.main;
        go.transform.rotation = Quaternion.Euler(90f, cam != null ? cam.transform.eulerAngles.y : 0f, 0f);
        go.transform.localScale = Vector3.one * startScale;
        go.SetActive(true);

        var state = GetSpriteState();
        state.GameObject = go;
        state.Renderer = sr;
        state.StartPosition = startPos;
        state.EndPosition = endPos;
        state.StartColor = effectColor;
        state.StartTime = Time.time;
        state.Duration = Mathf.Max(0.0001f, duration);

        activeSpriteEffects.Add(state);
    }

    private void ActivateMeshEffect(GameObject go, MeshRenderer mr, Vector3 center)
    {
        Vector3 startPos = center;
        Vector3 endPos = center + Vector3.up * upwardDistance;

        bool hasColor = false;
        Color baseColor = Color.white;

        if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Color"))
        {
            try
            {
                baseColor = mr.sharedMaterial.color;
                hasColor = true;
            }
            catch
            {
                baseColor = Color.white;
                hasColor = false;
            }
        }

        if (hasColor && sharedMeshPropertyBlock != null)
        {
            sharedMeshPropertyBlock.Clear();
            sharedMeshPropertyBlock.SetColor("_Color", baseColor);
            mr.SetPropertyBlock(sharedMeshPropertyBlock);
        }

        // For mesh effects we will animate height while keeping the top anchored at the judgment line.
        go.transform.position = startPos;
        go.SetActive(true);

        var state = GetMeshState();
        state.GameObject = go;
        state.Renderer = mr;
        state.BaseColor = baseColor;
        state.StartTime = Time.time;
        state.Duration = Mathf.Max(0.0001f, duration);
        state.HasColorProperty = hasColor;

        // store width and anchor for height-driven animation
        state.Width = go.transform.localScale.x;
        state.StartHeight = meshHeight;
        state.EndHeight = meshHeight + upwardDistance;
        state.AnchorY = center.y; // top Y (judgment line)
        state.BaseCenter = new Vector3(center.x, 0f, center.z);

        activeMeshEffects.Add(state);
    }

    public void ClearAllEffects()
    {
        for (int i = activeSpriteEffects.Count - 1; i >= 0; i--)
        {
            var state = activeSpriteEffects[i];
            if (state != null)
            {
                if (state.Renderer != null) state.Renderer.enabled = false;
                if (state.GameObject != null) state.GameObject.SetActive(false);
            }
            ReleaseSpriteState(i);
        }

        for (int i = activeMeshEffects.Count - 1; i >= 0; i--)
        {
            var state = activeMeshEffects[i];
            if (state != null)
            {
                if (state.HasColorProperty && state.Renderer != null && sharedMeshPropertyBlock != null)
                {
                    try
                    {
                        sharedMeshPropertyBlock.Clear();
                        sharedMeshPropertyBlock.SetColor("_Color", state.BaseColor);
                        state.Renderer.SetPropertyBlock(sharedMeshPropertyBlock);
                    }
                    catch { }
                }
                if (state.GameObject != null) state.GameObject.SetActive(false);
            }
            ReleaseMeshState(i);
        }

        foreach (var kvp in activePersistentMeshes)
        {
            if (kvp.Value != null) kvp.Value.SetActive(false);
        }
        activePersistentMeshes.Clear();

        for (int i = 0; i < pool.Count; i++)
        {
            var go = pool[i];
            if (go == null) continue;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
            go.SetActive(false);
        }

        for (int i = 0; i < meshPool.Count; i++)
        {
            var go = meshPool[i];
            if (go == null) continue;
            go.SetActive(false);
        }

        cachedSortingOrder = int.MinValue;
    }

    private int GetEffectSortingOrder()
    {
        if (cachedSortingOrder != int.MinValue && Time.unscaledTime < nextSortingOrderSampleTime)
        {
            return cachedSortingOrder;
        }

        int resolved = sortingOrder;

        try
        {
            if (cachedNoteController == null || !cachedNoteController.isActiveAndEnabled)
            {
                cachedNoteController = FindFirstObjectByType<NoteController>();
            }

            if (cachedNoteController != null)
            {
                var sr = cachedNoteController.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    resolved = Mathf.Max(0, sr.sortingOrder - 1);
                }
                else
                {
                    var r = cachedNoteController.GetComponent<Renderer>();
                    if (r != null) resolved = Mathf.Max(0, r.sortingOrder - 1);
                }
            }
        }
        catch { }

        cachedSortingOrder = resolved;
        nextSortingOrderSampleTime = Time.unscaledTime + 0.5f;
        return resolved;
    }

    private NoteSpawner GetCachedNoteSpawner()
    {
        if (cachedNoteSpawner != null && cachedNoteSpawner.isActiveAndEnabled)
        {
            return cachedNoteSpawner;
        }

        if (Time.unscaledTime < nextSpawnerRefreshTime)
        {
            return cachedNoteSpawner;
        }

        cachedNoteSpawner = FindFirstObjectByType<NoteSpawner>();
        nextSpawnerRefreshTime = Time.unscaledTime + 0.5f;
        if (cachedNoteSpawner == null)
        {
            cachedTrackWidth = -1f;
        }
        else
        {
            nextTrackWidthSampleTime = 0f;
        }
        return cachedNoteSpawner;
    }

    private float GetTrackWidth()
    {
        if (cachedTrackWidth > 0f && Time.unscaledTime < nextTrackWidthSampleTime)
        {
            return cachedTrackWidth;
        }

        float width = -1f;
        var spawner = GetCachedNoteSpawner();
        if (spawner != null && spawner.trackTransform != null)
        {
            var renderer = spawner.trackTransform.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                width = renderer.bounds.size.x;
            }
            else
            {
                width = Mathf.Abs(spawner.trackTransform.lossyScale.x) * 100f;
            }
        }

        if (width <= 0f) width = 105f;

        cachedTrackWidth = width;
        nextTrackWidthSampleTime = Time.unscaledTime + 0.5f;
        return cachedTrackWidth;
    }

    private void Update()
    {
        using (HitchProbe.Measure("keyHitFx")) UpdateCore();
    }

    private void UpdateCore()
    {
        if (cachedMainCamera == null) cachedMainCamera = Camera.main;
        if (cachedJudgmentLine == null) cachedJudgmentLine = GameObject.Find("JudgmentLine")?.transform;

        float now = Time.time;

        if (activeSpriteEffects.Count == 0 && activeMeshEffects.Count == 0)
        {
            return;
        }

        int spriteIndex = activeSpriteEffects.Count - 1;
        while (spriteIndex >= 0)
        {
            var state = activeSpriteEffects[spriteIndex];
            if (state.GameObject == null || state.Renderer == null)
            {
                ReleaseSpriteState(spriteIndex);
                spriteIndex = Mathf.Min(spriteIndex, activeSpriteEffects.Count - 1);
                continue;
            }

            float progress = (now - state.StartTime) / state.Duration;
            if (progress >= 1f)
            {
                state.Renderer.enabled = false;
                state.GameObject.SetActive(false);
                ReleaseSpriteState(spriteIndex);
                spriteIndex = Mathf.Min(spriteIndex, activeSpriteEffects.Count - 1);
                continue;
            }

            float t = Mathf.Clamp01(progress);
            state.GameObject.transform.position = Vector3.Lerp(state.StartPosition, state.EndPosition, t);
            float scale = Mathf.Lerp(startScale, endScale, t);
            state.GameObject.transform.localScale = new Vector3(scale, scale, scale);

            Color c = state.StartColor;
            c.a = Mathf.Lerp(1f, 0f, t);
            state.Renderer.color = c;

            spriteIndex--;
        }

        int meshIndex = activeMeshEffects.Count - 1;
        while (meshIndex >= 0)
        {
            var state = activeMeshEffects[meshIndex];
            if (state.GameObject == null || state.Renderer == null)
            {
                ReleaseMeshState(meshIndex);
                meshIndex = Mathf.Min(meshIndex, activeMeshEffects.Count - 1);
                continue;
            }

            float progress = (now - state.StartTime) / state.Duration;
            if (progress >= 1f)
            {
                if (state.HasColorProperty && sharedMeshPropertyBlock != null)
                {
                    sharedMeshPropertyBlock.Clear();
                    sharedMeshPropertyBlock.SetColor("_Color", state.BaseColor);
                    state.Renderer.SetPropertyBlock(sharedMeshPropertyBlock);
                }

                state.GameObject.SetActive(false);
                ReleaseMeshState(meshIndex);
                meshIndex = Mathf.Min(meshIndex, activeMeshEffects.Count - 1);
                continue;
            }

            float t = Mathf.Clamp01(progress);

            // Animate height while keeping the top anchored at AnchorY
            float currentHeight = Mathf.Lerp(state.StartHeight, state.EndHeight, t);
            float centerY = state.AnchorY - (currentHeight * 0.5f);
            state.GameObject.transform.position = new Vector3(state.BaseCenter.x, centerY, state.BaseCenter.z);
            state.GameObject.transform.localScale = new Vector3(state.Width, currentHeight, 1f);

            if (state.HasColorProperty && sharedMeshPropertyBlock != null)
            {
                Color c = state.BaseColor;
                c.a = Mathf.Lerp(1f, 0f, t);
                sharedMeshPropertyBlock.Clear();
                sharedMeshPropertyBlock.SetColor("_Color", c);
                state.Renderer.SetPropertyBlock(sharedMeshPropertyBlock);
            }

            meshIndex--;
        }
    }
}

