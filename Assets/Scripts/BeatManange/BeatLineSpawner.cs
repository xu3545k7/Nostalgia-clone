using UnityEngine;
using UnityEngine.SceneManagement;

public class BeatLineSpawner : MonoBehaviour
{
    [Tooltip("2D prefab for beat lines. This will be used for spawning beat lines.")]
    public GameObject beatLinePrefab2D; // 2D prefab for beat lines
    public Transform beatLineContainer;
    public Transform trackTransform; // TRACK transform to attach beat lines
    [Tooltip("Legacy lookahead in ms (kept for backward compatibility). If travelTimeSeconds > 0, that value will be used instead.")]
    public float spawnLookahead = 30000f; // Legacy ms
    [Header("Visual Travel Time")]
    [Tooltip("How many seconds a beat line should travel from spawn to judgment. Overrides spawnLookahead if > 0.")]
    public float travelTimeSeconds = 3f;
    [Tooltip("Multiply lookahead distance for a longer visible runway during pre-roll.")]
    public float runwayFactor = 1.0f;
    public float Speed { get; set; } = 30f; // Public property for speed control
    [Header("Visual Scale")]
    [Tooltip("Default local scale applied to spawned 2D beat line instances (use to correct oversized prefabs).")]
    public Vector3 defaultBeatLineScale = new Vector3(0.5f, 0.1f, 0.5f);

    private Chart chart;
    private Conductor conductor;
    private int nextBeatIndex = 0;
    private bool isInitialized = false;
    private Vector3 cachedCorrectedLocalScale = Vector3.one;
    private bool cachedScaleValid = false;
    // For visual pre-roll: we synthesize negative-time beats back to -lookahead
    private bool precomputedNegativeBeats = false;
    private System.Collections.Generic.List<float> negativeBeats = new System.Collections.Generic.List<float>();
    private BeatLinePool beatLinePool;
    // Dev-only: report once why spawning is blocked
    private bool _reportedSpawnBlock = false;

    // Derive an initial beat interval from the chart to drive pre-roll beats; fall back to Conductor.bpm
    private float GetBeatIntervalMs()
    {
        if (chart != null && chart.beat_timings != null && chart.beat_timings.Count >= 2)
        {
            // Use the first observed interval (chart timings are already in ms)
            float interval = chart.beat_timings[1] - chart.beat_timings[0];
            if (interval > 0.5f) return interval;
        }
        // Fallback: compute from current conductor bpm
        return 60000f / Mathf.Max(1f, conductor != null ? conductor.bpm : 120f);
    }

    public void Initialize(Chart chart, Conductor conductor)
    {
        this.chart = chart;
        this.conductor = conductor;
        this.nextBeatIndex = 0;
        this.isInitialized = true;
        if (negativeBeats == null)
        {
            negativeBeats = new System.Collections.Generic.List<float>(64);
        }
        else
        {
            negativeBeats.Clear();
        }
        precomputedNegativeBeats = false;
        // Force recreate container every time Initialize is called to avoid stale
        // or externally-reparented containers leaking UI/world transforms into the
        // spawner. This ensures a fresh, uniquely-named container owned by this
        // spawner instance on every restart/reselect.
        if (beatLineContainer != null)
        {
            // Only destroy an existing container if it was previously created/owned
            // by this spawner (we name owned containers with the prefix
            // "BeatLineContainer_"). If the container was assigned in the
            // scene/inspector (e.g. a shared 'Beat Line Container' that also
            // contains the judgment line), do NOT destroy it — instead reuse it.
            bool ownedBySpawner = false;
            try { ownedBySpawner = beatLineContainer.name != null && beatLineContainer.name.StartsWith("BeatLineContainer_"); } catch {}
            if (ownedBySpawner)
            {
                try
                {
                    if (beatLineContainer.gameObject != null)
                    {
                        Object.Destroy(beatLineContainer.gameObject);
                    }
                }
                catch {}
                beatLineContainer = null;
            }
            else
            {
                // Keep inspector/scene-assigned container as-is. If it belongs to a
                // different scene, we still won't destroy it — instead we'll create
                // a new owned container below. This avoids accidentally deleting
                // shared scene objects like the judgment line.
                if (beatLineContainer.gameObject.scene == gameObject.scene)
                {
                    // Use the existing container; we won't reparent or reset it
                    // to avoid unexpected visual movement of designer-placed objects.
                    // However, if the existing container is a UI element (RectTransform
                    // or under a Canvas), it's unsuitable for world-space beatlines
                    // so treat it as missing and create an owned world-space container
                    // instead.
                    bool isUI = false;
                    bool hasCameraAncestor = false;
                    try {
                        isUI = beatLineContainer.GetComponent<RectTransform>() != null || beatLineContainer.GetComponentInParent<Canvas>() != null;
                        hasCameraAncestor = beatLineContainer.GetComponentInParent<Camera>() != null;
                    } catch {}
                    if (isUI || hasCameraAncestor)
                    {
                        // ignore assigned UI container
                        beatLineContainer = null;
                    }
                }
                else
                {
                    // The assigned container is not in our active scene; ignore it
                    // and proceed to create a fresh owned container instead.
                    beatLineContainer = null;
                }
            }
        }

        // Only create a new owned container if we don't already have a usable one
        if (beatLineContainer == null)
        {
            string containerName = $"BeatLineContainer_{gameObject.name}_{GetInstanceID()}";
            GameObject newContainer = new GameObject(containerName);
            // Determine a sensible world-space parent for runtime beatlines.
            // Prefer explicit trackTransform, then try to find a GameObject named "Track"
            // or "GameplayRoot" in the scene. As a last resort fall back to this
            // spawner's root transform.
            Transform parentForContainer = null;
            if (trackTransform != null)
            {
                parentForContainer = trackTransform;
            }
            else
            {
                var foundTrack = GameObject.Find("Track");
                if (foundTrack != null) parentForContainer = foundTrack.transform;
                else
                {
                    var foundRoot = GameObject.Find("GameplayRoot");
                    if (foundRoot != null) parentForContainer = foundRoot.transform;
                }
            }
            if (parentForContainer == null)
            {
                // Use the scene root of this spawner to avoid parenting under UI GameManager
                parentForContainer = (transform.root != null) ? transform.root : transform;
            }
            newContainer.transform.SetParent(parentForContainer, false);
            newContainer.transform.localPosition = Vector3.zero;
            newContainer.transform.localRotation = Quaternion.identity;
            newContainer.transform.localScale = Vector3.one;
            beatLineContainer = newContainer.transform;
        }
    //global::DebugUtil.LogDev("BeatLineSpawner Initialized.");
        // Initialize pool if prefab is available
        if (beatLinePrefab2D != null)
        {
            // Use beatLineContainer if available; otherwise use the same world-space
            // fallback parent we used when creating containers to keep pooled objects
            // in the correct coordinate space.
            Transform fallbackParent = (trackTransform != null) ? trackTransform : (GameObject.Find("Track")?.transform ?? GameObject.Find("GameplayRoot")?.transform ?? (transform.root != null ? transform.root : transform));
            Transform parentForPool = (beatLineContainer != null) ? beatLineContainer : fallbackParent;
            // If an existing pool exists but its container differs, recreate it to ensure pooled
            // instances are under the correct container (avoids stale pool children staying elsewhere).
            if (beatLinePool == null || beatLinePool.Container != parentForPool)
            {
                beatLinePool = new BeatLinePool(beatLinePrefab2D, parentForPool, 128);
            }
            else
            {
                // Ensure pool's container is parented correctly
                beatLinePool.Container.SetParent(transform, false);
            }
        }
#if false
        // Development-only diagnostic: report initialization state to help debug missing beatlines
        BuildLogger.Log($"[BeatLineSpawner] Initialize: prefabAssigned={(beatLinePrefab2D!=null)}, container={(beatLineContainer!=null?beatLineContainer.name:"<null>")}, containerWorldPos={(beatLineContainer!=null?beatLineContainer.position:Vector3.zero)}, track={(trackTransform!=null?trackTransform.name:"<null>")}, trackWorldPos={(trackTransform!=null?trackTransform.position:Vector3.zero)}, chartBeatsCount={(chart!=null && chart.beat_timings!=null?chart.beat_timings.Count:0)}, conductorPresent={(conductor!=null)}");
        // Print container parent chain for diagnostics
        if (beatLineContainer != null)
        {
            string chain = beatLineContainer.name;
            Transform p = beatLineContainer.parent;
            while (p != null)
            {
                chain = p.name + "/" + chain;
                p = p.parent;
            }
            BuildLogger.Log($"[BeatLineSpawner] Container parent chain: {chain}");
            bool isUI = false;
            try { isUI = beatLineContainer.GetComponent<RectTransform>() != null || beatLineContainer.GetComponentInParent<Canvas>() != null; } catch {}
            BuildLogger.Log($"[BeatLineSpawner] Container isUI={isUI}, activeInHierarchy={beatLineContainer.gameObject.activeInHierarchy}, layer={beatLineContainer.gameObject.layer}");
        }
        // Prefab renderer inspection
        if (beatLinePrefab2D != null)
        {
            var sr = beatLinePrefab2D.GetComponentInChildren<UnityEngine.SpriteRenderer>();
            var mr = beatLinePrefab2D.GetComponentInChildren<MeshRenderer>();
            if (sr != null)
            {
                BuildLogger.Log($"[BeatLineSpawner] Prefab SpriteRenderer: sortingLayer={sr.sortingLayerName}, order={sr.sortingOrder}, enabled={sr.enabled}, color={sr.color}");
            }
            if (mr != null)
            {
                BuildLogger.Log($"[BeatLineSpawner] Prefab MeshRenderer: enabled={mr.enabled}, shadowCastingMode={mr.shadowCastingMode}, receiveShadows={mr.receiveShadows}");
            }
        }
#endif
    }

    /// <summary>
    /// Preload beat line instances for the given chart to avoid runtime instantiation spikes.
    /// This will compute cached scale for the container and create a number of disabled beat line objects under the container.
    /// </summary>
    public void PreloadAllBeatLines(Chart chart)
    {
        if (chart == null) return;
        Transform parent = (beatLineContainer != null) ? beatLineContainer : transform;
        Vector3 parentLossy = parent.lossyScale;
        Vector3 desiredWorld = defaultBeatLineScale;
        cachedCorrectedLocalScale = new Vector3(
            parentLossy.x != 0f ? desiredWorld.x / parentLossy.x : desiredWorld.x,
            parentLossy.y != 0f ? desiredWorld.y / parentLossy.y : desiredWorld.y,
            parentLossy.z != 0f ? desiredWorld.z / parentLossy.z : desiredWorld.z
        );
        cachedScaleValid = true;

        int required = (chart.beat_timings != null) ? chart.beat_timings.Count : 0;
        required = Mathf.CeilToInt(required * 1.05f) + 8;
        if (beatLinePool != null)
        {
            beatLinePool.EnsureCapacity(required);
        }
        else
        {
            // Create disabled instances to warm-up the engine if no pool system exists
            for (int i = 0; i < Mathf.Min(256, required); i++)
            {
                var go = Instantiate(beatLinePrefab2D, parent);
                global::RuntimeDiagnostics.RegisterInstantiate();
                go.transform.localScale = cachedCorrectedLocalScale;
                go.SetActive(false);
            }
        }
    //Debug.Log($"BeatLineSpawner PreloadAllBeatLines created {Mathf.Min(256, required)} warmed beat line instances.");
    }

    void Update()
    {
        // Prefer 2D prefab; log an error if beatLinePrefab2D is missing.
        if (!isInitialized || conductor == null || !conductor.isActive || beatLinePrefab2D == null)
        {
                #if false
                if (!_reportedSpawnBlock)
                {
                    string why = "";
                    if (!isInitialized) why += "isInitialized=false; ";
                    if (conductor == null) why += "conductor=null; ";
                    else if (!conductor.isActive) why += "conductor.isActive=false; ";
                    if (beatLinePrefab2D == null) why += "beatLinePrefab2D=null; ";
                    BuildLogger.LogWarning($"[BeatLineSpawner] Update blocked: {why} (spawner={gameObject.name})");
                    _reportedSpawnBlock = true;
                }
                #endif
            return;
        }

        // Determine dynamic lookahead: ensure it's at least the remaining pre-roll so visuals appear immediately
        // Dynamic lookahead: travel-time vs remaining pre-roll, then scale by runwayFactor
        float travelLookaheadMs = (travelTimeSeconds > 0f ? travelTimeSeconds * 1000f : spawnLookahead);
        float preRollMs = 0f;
        if (conductor != null)
        {
            preRollMs = conductor.isVisualPlaying ? Mathf.Max(0f, -conductor.visualSongPosition) : conductor.RemainingPreRollMs;
        }
        float spawnWindowMs = (travelLookaheadMs + preRollMs) * Mathf.Max(0.01f, runwayFactor);
        // Precompute negative beats once we know the effective lookahead and bpm
        if (!precomputedNegativeBeats && conductor != null)
        {
            float beatIntervalMs = GetBeatIntervalMs();
            // Generate beats strictly < 0 down to -lookaheadMs
            for (float t = 0f - beatIntervalMs; t >= -spawnWindowMs; t -= beatIntervalMs)
            {
                negativeBeats.Add(t);
            }
            // We'll spawn them in Update based on effectiveSongPosition threshold
            precomputedNegativeBeats = true;
            // Optional: log count
            // Debug.Log($"Precomputed {negativeBeats.Count} negative beat lines for pre-roll.");
        }

        // Spawn negative-time beats during pre-roll
        if (precomputedNegativeBeats && negativeBeats.Count > 0)
        {
            // Work from the end (closest to zero) to the beginning for stability
            for (int i = negativeBeats.Count - 1; i >= 0; i--)
            {
                float beatTime = negativeBeats[i];
                float timeToBeat = beatTime - conductor.effectiveSongPosition; // beatTime is negative here
                if (timeToBeat <= spawnWindowMs)
                {
                    Transform parent = (beatLineContainer != null && beatLineContainer.gameObject.scene.IsValid() && beatLineContainer.gameObject.scene == gameObject.scene)
                        ? beatLineContainer : transform;
                    GameObject beatLineObject = null;
                    if (beatLinePool != null)
                    {
                        beatLineObject = beatLinePool.Spawn(parent);
                    }
                    else
                    {
                        beatLineObject = Instantiate(beatLinePrefab2D, parent);
                        global::RuntimeDiagnostics.RegisterInstantiate();
                    }
                    // Dev log spawn event
#if false
                    try {
                        BuildLogger.Log($"[BeatLineSpawner] SpawnNegativeBeat: beatTime={beatTime} timeToBeat={timeToBeat:F1} spawnWindowMs={spawnWindowMs:F1} parent={(parent!=null?parent.name:"<null>")}");
                    } catch {}
#endif
                    // Compensate for parent scale so beat lines have consistent world size
                    Vector3 desiredWorld = defaultBeatLineScale;
                    Vector3 parentLossy = parent.lossyScale;
                    Vector3 correctedLocal = new Vector3(
                        parentLossy.x != 0f ? desiredWorld.x / parentLossy.x : desiredWorld.x,
                        parentLossy.y != 0f ? desiredWorld.y / parentLossy.y : desiredWorld.y,
                        parentLossy.z != 0f ? desiredWorld.z / parentLossy.z : desiredWorld.z
                    );
                    beatLineObject.transform.localScale = correctedLocal;
                    var ctrl = beatLineObject.GetComponent<BeatLineController>();
                    if (ctrl != null)
                    {
                        ctrl.Initialize(beatTime, this, timeToBeat);
                    }
                    negativeBeats.RemoveAt(i);
                }
            }
        }

        if (chart.beat_timings != null && nextBeatIndex < chart.beat_timings.Count)
        {
            float nextBeatTime = chart.beat_timings[nextBeatIndex];
            float timeToBeat = nextBeatTime - conductor.effectiveSongPosition;
            if (timeToBeat <= spawnWindowMs)
            {
                Transform parent = transform;
                if (beatLineContainer != null)
                {
                    if (beatLineContainer.gameObject.scene.IsValid() && beatLineContainer.gameObject.scene == gameObject.scene)
                    {
                        parent = beatLineContainer;
                    }
                    else
                    {
                        //Debug.LogWarning("BeatLineSpawner: Assigned BeatLineContainer is not in the active scene. Using spawner's transform instead.");
                    }
                }
                // If parent resolves to the spawner itself but the spawner is under a UI/root
                // (like GameManager with RectTransform), prefer a world-space fallback.
                if (parent == transform)
                {
                    bool spawnerIsUI = false;
                    try { spawnerIsUI = transform.GetComponent<RectTransform>() != null || transform.GetComponentInParent<Canvas>() != null; } catch {}
                    if (spawnerIsUI)
                    {
                        Transform fallback = (trackTransform != null) ? trackTransform : (GameObject.Find("Track")?.transform ?? GameObject.Find("GameplayRoot")?.transform ?? (transform.root != null ? transform.root : transform));
                        if (fallback != null) parent = fallback;
                    }
                }
                GameObject beatLineObject = null;
                if (beatLinePool != null)
                {
                    beatLineObject = beatLinePool.Spawn(parent);
                }
                else
                {
                    beatLineObject = Instantiate(beatLinePrefab2D, parent);
                    global::RuntimeDiagnostics.RegisterInstantiate();
                }
                    // Dev log spawn event
    #if false
                    try {
                        BuildLogger.Log($"[BeatLineSpawner] SpawnBeat: nextIndex={nextBeatIndex} nextBeatTime={nextBeatTime} timeToBeat={timeToBeat:F1} spawnWindowMs={spawnWindowMs:F1} parent={(parent!=null?parent.name:"<null>")}");
                    } catch {}
    #endif
                Vector3 desiredWorld2 = defaultBeatLineScale;
                Vector3 parentLossy2 = parent.lossyScale;
                Vector3 correctedLocal2 = new Vector3(
                    parentLossy2.x != 0f ? desiredWorld2.x / parentLossy2.x : desiredWorld2.x,
                    parentLossy2.y != 0f ? desiredWorld2.y / parentLossy2.y : desiredWorld2.y,
                    parentLossy2.z != 0f ? desiredWorld2.z / parentLossy2.z : desiredWorld2.z
                );
                beatLineObject.transform.localScale = correctedLocal2;
                
                BeatLineController beatLineController = beatLineObject.GetComponent<BeatLineController>();
                if (beatLineController != null)
                {
                    beatLineController.Initialize(nextBeatTime, this, timeToBeat);
                    // Dev: log spawned object's transform after initialization
#if false
                    try {
                    // include renderer diagnostics for spawned object
                    var sr = beatLineObject.GetComponentInChildren<UnityEngine.SpriteRenderer>();
                    var mr = beatLineObject.GetComponentInChildren<MeshRenderer>();
                    string rendInfo = "";
                    if (sr != null) rendInfo = $"SpriteRenderer(enabled={sr.enabled}, sortingLayer={sr.sortingLayerName}, order={sr.sortingOrder})";
                    else if (mr != null) rendInfo = $"MeshRenderer(enabled={mr.enabled})";
                    else rendInfo = "NoRenderer";
                    BuildLogger.Log($"[BeatLineSpawner] SpawnedBeatObject: name={beatLineObject.name}, worldPos={beatLineObject.transform.position}, localPos={beatLineObject.transform.localPosition}, parent={(beatLineObject.transform.parent!=null?beatLineObject.transform.parent.name:"<null>")}, parentLossy={((beatLineObject.transform.parent!=null)?beatLineObject.transform.parent.lossyScale:Vector3.one)}, active={beatLineObject.activeSelf}, renderer={rendInfo}");
                    } catch {}
#endif
                }
                else
                {
                    //Debug.LogError("Beat Line Prefab is missing the BeatLineController script!");
                }
                nextBeatIndex++;
            }
        }
    }

    /// <summary>
    /// Return a beat line instance back to pool or destroy if no pool.
    /// Called by BeatLineController when the beatline lifetime is over.
    /// </summary>
    public void ReturnBeatLine(GameObject go)
    {
        if (go == null) return;
        if (beatLinePool != null)
        {
            beatLinePool.Despawn(go);
        }
        else
        {
            Destroy(go);
        }
    }

    /// <summary>
    /// Clear all spawned beat line instances. Prefer clearing the pool container to ensure
    /// pooled transient children are removed as well.
    /// </summary>
    public void ClearAllSpawned()
    {
        if (beatLinePool != null)
        {
            Transform activeParent = (beatLineContainer != null) ? beatLineContainer : transform;
            if (activeParent != null)
            {
#if false
                //Debug.Log($"BeatLineSpawner.ClearAllSpawned: activeParent='{activeParent.name}' childCountBefore={activeParent.childCount}");
#endif
                    // Iterate by index to avoid allocating a temporary List when clearing children
                    for (int i = activeParent.childCount - 1; i >= 0; --i)
                    {
                        Transform child = activeParent.GetChild(i);
                        if (child == null || child.gameObject == null) continue;
                        GameObject go = child.gameObject;
                        if (beatLinePool != null)
                            beatLinePool.Despawn(go);
                        else
                            Object.Destroy(go);
                    }
#if false
                //Debug.Log($"BeatLineSpawner.ClearAllSpawned: activeParent='{activeParent.name}' childCountAfter={activeParent.childCount}");
#endif
            }
            return;
        }

        Transform container = beatLineContainer != null ? beatLineContainer : transform;
        if (container != null)
        {
            for (int i = container.childCount - 1; i >= 0; --i)
            {
                var child = container.GetChild(i);
                if (child != null && child.gameObject != null)
                {
                    global::RuntimeDiagnostics.RegisterDestroy();
                    Object.Destroy(child.gameObject);
                }
            }
        }
    }

    /// <summary>
    /// Seek the beat spawner to the first beat after the specified time (ms) and clear spawned beatlines.
    /// </summary>
    public void SeekToMs(float ms)
    {
        if (chart == null || chart.beat_timings == null) return;
        int idx = 0;
        while (idx < chart.beat_timings.Count)
        {
            if (chart.beat_timings[idx] > ms) break;
            idx++;
        }
        nextBeatIndex = Mathf.Clamp(idx, 0, chart.beat_timings.Count);
        ClearAllSpawned();
    }

    /// <summary>
    /// Fully destroy the associated beat line pool and its container if owned by the pool.
    /// </summary>
    public void DestroyPool()
    {
        try
        {
            if (beatLinePool != null)
            {
                beatLinePool.DestroyPool();
                beatLinePool = null;
            }
        }
        catch (System.Exception)
        {
            // ignore cleanup errors
        }
    }

    public void SpawnBeatLineAtPosition(Vector2 localPosition)
    {
        if (beatLinePrefab2D == null || trackTransform == null)
        {
            //Debug.LogError("BeatLineSpawner: Missing beatLinePrefab2D or trackTransform.");
            return;
        }

    GameObject beatLine = Instantiate(beatLinePrefab2D, trackTransform);
    global::RuntimeDiagnostics.RegisterInstantiate();
        beatLine.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0);
        Vector3 trackLossy = trackTransform != null ? trackTransform.lossyScale : Vector3.one;
        Vector3 corrected = new Vector3(
            trackLossy.x != 0f ? defaultBeatLineScale.x / trackLossy.x : defaultBeatLineScale.x,
            trackLossy.y != 0f ? defaultBeatLineScale.y / trackLossy.y : defaultBeatLineScale.y,
            trackLossy.z != 0f ? defaultBeatLineScale.z / trackLossy.z : defaultBeatLineScale.z
        );
        beatLine.transform.localScale = corrected; // Use corrected local scale

    //global::DebugUtil.LogDev($"SpawnBeatLineAtPosition: Beat line spawned at local position {beatLine.transform.localPosition} relative to TRACK {trackTransform.name}.");
    }
}

