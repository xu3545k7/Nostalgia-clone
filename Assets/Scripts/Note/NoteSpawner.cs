
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NoteSpawner : MonoBehaviour{
    public static NoteSpawner Instance { get; private set; }

    /// <summary>
    /// 由外部（如 GameManager）呼叫，嘗試判定指定鍵位的 note。
    /// </summary>
    public void TryHitNote(int keyIndex, float velocity)
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[判定] TryHitNote: key={keyIndex}, 力度={velocity}");
        #endif
        // 觸發鍵位特效（MIDI 也要有）
        if (KeyHitEffectManager.Instance != null)
        {
            KeyHitEffectManager.Instance.ShowPersistentMeshForKeyId(keyIndex);
        }
        else
        {
            Debug.LogWarning($"[KeyHitEffectManager] Instance is null, 無法顯示鍵位特效 (key={keyIndex})");
        }
        // 直接呼叫 JudgmentManager 進行判定，確保 MIDI/鍵盤一致
        if (Judgment.JudgmentManager.Instance != null)
        {
            Judgment.JudgmentManager.Instance.ProcessButtonPress(keyIndex);
        }
        else
        {
            Debug.LogWarning($"[JudgmentManager] Instance is null, 無法判定 (key={keyIndex})");
        }
    }

    void Awake()
    {
        Instance = this;
    }

    [Tooltip("2D prefab for notes. This will be used for spawning notes.")]
    public GameObject notePrefab2D; // 2D prefab for notes
    public Transform noteContainer; // Assign a container object here
    public Transform trackTransform; // TRACK transform to attach notes
    [Tooltip("Legacy lookahead in ms (kept for backward compatibility). If travelTimeSeconds > 0, that value will be used instead.")]
    public float noteSpawnLookahead = 20000f;
    [Header("Visual Travel Time")]
    [Tooltip("How many seconds a note should travel from spawn to judgment. Overrides noteSpawnLookahead if > 0.")]
    public float travelTimeSeconds = 2f;
    [Tooltip("Multiply lookahead distance for a longer visible runway during pre-roll.")]
    public float runwayFactor = 1.0f;
    public float speed = 70f; // Add speed property
    [Header("Visual Scale")]
    [Tooltip("Default local scale applied to spawned 2D note instances (use to correct oversized prefabs).")]
    public Vector3 defaultNoteScale = new Vector3(0.5f, 0.5f, 0.5f);
    [Tooltip("Legacy prefab name (editor-only). If notePrefab2D is empty, OnValidate will try to find a prefab matching this name (e.g. 'noteo4') and assign it.")]
    public string legacyNotePrefabName = "";

    private Chart chart;
    private Conductor conductor;
    // Use reflection to locate AudioSync at runtime to avoid compile-time type dependency
    private Component audioSyncComponent;
    private System.Reflection.MethodInfo audioSync_GetAudioTime_Method;
    // Expose conductor to spawned NoteControllers to avoid expensive FindAnyObjectByType calls
    public Conductor Conductor => conductor;
    private int nextNoteIndex = 0;
    private bool isInitialized = false;
    private NotePool notePool;
    // Dev-only: report once why spawning is blocked
    private bool _reportedSpawnBlock = false;
    // Cached corrected local scale computed once for the assigned container/track
    private Vector3 cachedCorrectedLocalScale = Vector3.one;
    private bool cachedScaleValid = false;

    public void Initialize(Chart chart, Conductor conductor)
    {
        this.chart = chart;
        this.conductor = conductor;
        // prefer an AudioSync component (authoritative DSP-based time) if available
        try
        {
            if (conductor != null)
            {
                var comp = conductor.GetComponent("AudioSync") as Component;
                if (comp != null) audioSyncComponent = comp;
            }
        }
        catch (System.Exception)
        {
            audioSyncComponent = null;
        }
        if (audioSyncComponent == null)
        {
            // search all MonoBehaviours for a type named AudioSync (editor/runtime safe)
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var m in all)
            {
                if (m != null && m.GetType().Name == "AudioSync")
                {
                    audioSyncComponent = m as Component;
                    break;
                }
            }
        }
        if (audioSyncComponent != null)
        {
            audioSync_GetAudioTime_Method = audioSyncComponent.GetType().GetMethod("GetAudioTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        }
        this.nextNoteIndex = 0;
        this.isInitialized = true;
        // Ensure container is valid and in the same scene; otherwise, fallback to self
        if (noteContainer == null || !noteContainer.gameObject.scene.IsValid() || noteContainer.gameObject.scene != gameObject.scene)
        {
            noteContainer = transform;
        }

        // Initialize object pool for notes if we have a prefab
        if (notePrefab2D != null)
        {
            // Create an initial pool size based on chart density to avoid runtime Instantiate
            // too-small pools can cause mid-song Instantiate spikes; compute a modest initial
            // capacity (5% of required, clamped) and warm the remainder asynchronously.
            int estimatedRequired = (chart != null && chart.notes != null) ? chart.notes.Count : 128;
            int initialPoolSize = Mathf.Clamp(Mathf.CeilToInt(estimatedRequired * 0.05f) + 8, 32, 512);
            notePool = new NotePool(notePrefab2D, noteContainer, initialPoolSize);
            // compute and cache corrected local scale for the current container to avoid per-spawn lossyScale math
            Transform parent = (noteContainer != null) ? noteContainer : transform;
            Vector3 parentLossy = parent.lossyScale;
            Vector3 desiredWorldScale = defaultNoteScale;
            cachedCorrectedLocalScale = new Vector3(
                parentLossy.x != 0f ? desiredWorldScale.x / parentLossy.x : desiredWorldScale.x,
                parentLossy.y != 0f ? desiredWorldScale.y / parentLossy.y : desiredWorldScale.y,
                parentLossy.z != 0f ? desiredWorldScale.z / parentLossy.z : desiredWorldScale.z
            );
            cachedScaleValid = true;
        }
        // Preload pool to match chart size. To avoid a single-frame allocation spike,
        // perform the bulk EnsureCapacity asynchronously (split across frames) when possible.
        if (chart != null)
        {
            // Start async preload if we have a pool; fallback to synchronous PreloadAll if not.
            if (notePool != null && Application.isPlaying)
            {
                int required = (chart.notes != null) ? chart.notes.Count : 0;
                required = Mathf.CeilToInt(required * 1.05f) + 8;
                // Use the pool's async EnsureCapacity which instantiates in batches without spawning/despawning
                // (avoids running per-instance Cleanup/Unregister logic and reduces GC churn).
                try { StartCoroutine(notePool.EnsureCapacityAsync(required, 64)); } catch { }
            }
            else
            {
                PreloadAll(chart);
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Development-only diagnostic: report initialization state to help debug missing notes
    BuildLogger.Log($"[NoteSpawner] Initialize: prefabAssigned={(notePrefab2D!=null)}, container={(noteContainer!=null?noteContainer.name:"<null>")}, track={(trackTransform!=null?trackTransform.name:"<null>")}, notesCount={(chart!=null && chart.notes!=null?chart.notes.Count:0)}, conductorPresent={(conductor!=null)}");
#endif
    }

    private System.Collections.IEnumerator WarmPoolAsync(int targetCapacity, int batchSize)
    {
        if (notePool == null) yield break;
        // Use public GetStats().Item5 to read current pool size
        while (true)
        {
            var stats = notePool.GetStats();
            int poolSize = stats.Item5;
            if (poolSize >= targetCapacity) yield break;
            int toCreate = Mathf.Min(batchSize, targetCapacity - poolSize);
            for (int i = 0; i < toCreate; i++)
            {
                var go = notePool.Spawn(noteContainer != null ? noteContainer : transform);
                // immediately return to pool (spawn/despawn sequence will perform standard cleanup)
                notePool.Despawn(go);
            }
            // yield one frame between batches
            yield return null;
        }
    }

#if UNITY_EDITOR
    // Try to auto-assign legacy prefab by name in the Editor when values change in Inspector
    void OnValidate()
    {
        if (notePrefab2D == null && !string.IsNullOrEmpty(legacyNotePrefabName))
        {
            // Search for a prefab asset whose name matches legacyNotePrefabName (case-insensitive)
            string query = "t:prefab " + legacyNotePrefabName;
            string[] guids = AssetDatabase.FindAssets(query);
            if (guids != null && guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    notePrefab2D = prefab;
                    EditorUtility.SetDirty(this);
                }
            }
        }
    }
#endif

    void Update()
    {
        // Use 2D prefab by default. If notePrefab2D is missing, log an error and skip spawning.
        if (!isInitialized || conductor == null || !conductor.isActive || notePrefab2D == null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_reportedSpawnBlock)
            {
                string why = "";
                if (!isInitialized) why += "isInitialized=false; ";
                if (conductor == null) why += "conductor=null; ";
                else if (!conductor.isActive) why += "conductor.isActive=false; ";
                if (notePrefab2D == null) why += "notePrefab2D=null; ";
                BuildLogger.LogWarning($"[NoteSpawner] Update blocked: {why} (spawner={gameObject.name})");
                _reportedSpawnBlock = true;
            }
#endif
            return;
        }

        // Validate chart and notes container prior to indexing to avoid NullReferenceExceptions
        if (chart == null)
        {
            return;
        }
        if (chart.notes == null)
        {
            return;
        }
        if (nextNoteIndex < chart.notes.Count)
        {
            // Defensive: ensure index is within range
            if (nextNoteIndex < 0 || nextNoteIndex >= chart.notes.Count)
            {
                return;
            }

            NoteData nextNote = chart.notes[nextNoteIndex];
            if (nextNote == null)
            {
                nextNoteIndex++;
                return;
            }

            // Use original chart timings; global delay comes from audio start offset
            float effectiveStart = nextNote.startTime;
            // Dynamic lookahead: ensure it's at least the remaining pre-roll so visuals appear immediately
            float travelLookaheadMs = (travelTimeSeconds > 0f ? travelTimeSeconds * 1000f : noteSpawnLookahead);
            float preRollMs = 0f;
            if (conductor != null)
            {
                preRollMs = conductor.isVisualPlaying ? Mathf.Max(0f, -conductor.visualSongPosition) : conductor.RemainingPreRollMs;
            }
            float spawnWindowMs = (travelLookaheadMs + preRollMs) * Mathf.Max(0.01f, runwayFactor);
            // Spawn when the remaining time to start is within travel-time lookahead
            // Determine current song position in ms. Prefer AudioSync.GetAudioTime() when available
            float currentSongPosMs = 0f;
            if (audioSyncComponent != null && audioSync_GetAudioTime_Method != null)
            {
                try
                {
                    var res = audioSync_GetAudioTime_Method.Invoke(audioSyncComponent, null);
                    if (res is double d)
                    {
                        currentSongPosMs = (float)(d * 1000.0);
                    }
                    else if (res is float f)
                    {
                        currentSongPosMs = f * 1000f;
                    }
                }
                catch (System.Exception)
                {
                    currentSongPosMs = conductor != null ? conductor.effectiveSongPosition : 0f;
                }
            }
            else
            {
                currentSongPosMs = conductor != null ? conductor.effectiveSongPosition : 0f;
            }
            float timeToStart = effectiveStart - currentSongPosMs;
            if (timeToStart <= spawnWindowMs)
            {
                // Use the container as the parent if it's assigned
                Transform parent = (noteContainer != null) ? noteContainer : transform;
                GameObject noteObject = null;
                if (notePool != null)
                {
                    noteObject = notePool.Spawn(parent);
                }
                else
                {
                    noteObject = Instantiate(notePrefab2D, parent);
                    global::RuntimeDiagnostics.RegisterInstantiate();
                }
                // Apply cached corrected scale if available; otherwise compute once now
                if (cachedScaleValid)
                {
                    noteObject.transform.localScale = cachedCorrectedLocalScale;
                }
                else
                {
                    Vector3 desiredWorldScale = defaultNoteScale;
                    Vector3 parentLossy = parent.lossyScale;
                    Vector3 correctedLocalScale = new Vector3(
                        parentLossy.x != 0f ? desiredWorldScale.x / parentLossy.x : desiredWorldScale.x,
                        parentLossy.y != 0f ? desiredWorldScale.y / parentLossy.y : desiredWorldScale.y,
                        parentLossy.z != 0f ? desiredWorldScale.z / parentLossy.z : desiredWorldScale.z
                    );
                    noteObject.transform.localScale = correctedLocalScale;
                    cachedCorrectedLocalScale = correctedLocalScale;
                    cachedScaleValid = true;
                }

                NoteController noteController = noteObject.GetComponent<NoteController>();
                if (noteController != null)
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    try {
                        // Include note_type and type in the spawn diagnostic so we can
                        // confirm JsonUtility deserialized numeric "note_type" correctly.
                        BuildLogger.Log($"[NoteSpawner] Spawn: nextNoteIndex={nextNoteIndex} startTime={nextNote.startTime} lanes={nextNote.startLane}-{nextNote.endLane} type={nextNote.type} note_type={nextNote.note_type} spawnedGOId={noteObject.GetInstanceID()}");
                    } catch {}
                    #endif
                    noteController.Initialize(nextNote, this, timeToStart);
                }
                nextNoteIndex++;
            }
        }
    }

    public void SpawnNoteAtPosition(Vector2 localPosition)
    {
        if (notePrefab2D == null || trackTransform == null)
        {
            return;
        }

        GameObject note = null;
        if (notePool != null)
            note = notePool.Spawn(trackTransform);
        else
        {
            note = Instantiate(notePrefab2D, trackTransform);
            global::RuntimeDiagnostics.RegisterInstantiate();
        }
        note.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0);
        // Compensate for track's lossyScale so world size matches configured default
        Vector3 trackLossy = trackTransform != null ? trackTransform.lossyScale : Vector3.one;
        Vector3 correctedLocal = new Vector3(
            trackLossy.x != 0f ? defaultNoteScale.x / trackLossy.x : defaultNoteScale.x,
            trackLossy.y != 0f ? defaultNoteScale.y / trackLossy.y : defaultNoteScale.y,
            trackLossy.z != 0f ? defaultNoteScale.z / trackLossy.z : defaultNoteScale.z
        );
    note.transform.localScale = correctedLocal; // Use corrected local scale

    //global::DebugUtil.LogDev($"SpawnNoteAtPosition: Note spawned at local position {note.transform.localPosition} relative to TRACK {trackTransform.name}.");
    }

    /// <summary>
    /// Preload all note instances for the provided chart so spawning during playback doesn't allocate.
    /// This will compute cached scale and call NotePool.EnsureCapacity to pre-instantiate objects.
    /// </summary>
    public void PreloadAll(Chart chart)
    {
        if (chart == null) return;
        // Ensure container exists
        Transform parent = (noteContainer != null) ? noteContainer : transform;
        // Compute and cache corrected local scale for parent
        Vector3 parentLossy = parent.lossyScale;
        Vector3 desiredWorldScale = defaultNoteScale;
        cachedCorrectedLocalScale = new Vector3(
            parentLossy.x != 0f ? desiredWorldScale.x / parentLossy.x : desiredWorldScale.x,
            parentLossy.y != 0f ? desiredWorldScale.y / parentLossy.y : desiredWorldScale.y,
            parentLossy.z != 0f ? desiredWorldScale.z / parentLossy.z : desiredWorldScale.z
        );
        cachedScaleValid = true;

        // Determine estimate of required note count: use chart.notes.Count
        int required = (chart.notes != null) ? chart.notes.Count : 0;
        // Add some slack to avoid missing in edge cases
        required = Mathf.CeilToInt(required * 1.05f) + 8;
        if (notePool != null)
        {
            notePool.EnsureCapacity(required);
        }
        else
        {
            // If no pool, optionally pre-instantiate disabled objects into container to warm-up
            for (int i = 0; i < Mathf.Min(64, required); i++)
            {
                var go = Instantiate(notePrefab2D, parent);
                global::RuntimeDiagnostics.RegisterInstantiate();
                go.SetActive(false);
            }
        }
    //Debug.Log($"NoteSpawner PreloadAll prepared {required} note instances (pool or temp)." );
    }

    /// <summary>
    /// Clear all spawned note instances. Prefer using the pool container to remove pooled children
    /// so transient objects owned by pooled instances (e.g., world-space tails) are also cleaned.
    /// </summary>
    public void ClearAllSpawned()
    {
        // If we have a pool, clear its container children (these are pooled instances)
        if (notePool != null)
        {
            // Active spawned notes live under noteContainer (or this spawner transform). Use that as source.
            Transform activeParent = (noteContainer != null) ? noteContainer : transform;
            if (activeParent != null)
            {
                // Iterate children by index to avoid allocating a temporary List
                for (int i = activeParent.childCount - 1; i >= 0; --i)
                {
                    Transform child = activeParent.GetChild(i);
                    if (child == null || child.gameObject == null) continue;
                    GameObject go = child.gameObject;
                    if (notePool != null)
                    {
                        notePool.Despawn(go);
                    }
                    else
                    {
                        Object.Destroy(go);
                    }
                }
                // Debug info removed: previously logged activeParent childCountAfter for debugging
            }
            return;
        }

        // Fallback: destroy any children under noteContainer
        if (noteContainer != null)
        {
            // Iterate by index to avoid allocating an enumerator/temporary List
            for (int i = noteContainer.childCount - 1; i >= 0; --i)
            {
                Transform child = noteContainer.GetChild(i);
                if (child == null || child.gameObject == null) continue;
                global::RuntimeDiagnostics.RegisterDestroy();
                Object.Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// Seek the spawner's internal index to the first note after the specified time (ms)
    /// and clear spawned objects so subsequent spawning resumes from that point.
    /// </summary>
    public void SeekToMs(float ms)
    {
        if (chart == null || chart.notes == null) return;
        int idx = 0;
        while (idx < chart.notes.Count)
        {
            var n = chart.notes[idx];
            if (n == null) { idx++; continue; }
            if (n.startTime > ms) break;
            idx++;
        }
        nextNoteIndex = Mathf.Clamp(idx, 0, chart.notes.Count);
        ClearAllSpawned();
    }

    /// <summary>
    /// Fully destroy the internal pool and its container if owned. Use when tearing down a song
    /// or switching tracks to reclaim GameObjects and free memory.
    /// </summary>
    public void DestroyPool()
    {
        try
        {
            if (notePool != null)
            {
                notePool.DestroyPool();
                notePool = null;
            }
        }
        catch (System.Exception)
        {
            // swallow exceptions during cleanup; best-effort
        }
    }
}
