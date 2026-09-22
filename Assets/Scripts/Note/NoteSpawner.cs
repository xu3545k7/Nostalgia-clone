
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NoteSpawner : MonoBehaviour{
    public static NoteSpawner Instance { get; private set; }

    /// <summary>
    /// 由外部（如 GameManager）呼叫，嘗試判定指定鍵位的 note。
    /// </summary>
    public void TryHitNote(int keyIndex, float velocity, float songPosOverride = float.NaN,
        long inputEventId = 0)
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
            // 走輸入幀入口而不是直接判定：同一個 16.66ms 內按下的鍵要整組
            // 一起決定誰有效（判定文件 §5.1）。設定關掉時它會直接轉呼叫原本的
            // ProcessButtonPress，行為和以前一樣。
            using (HitchProbe.Measure("buttonPress"))
                Judgment.JudgmentManager.Instance.EnqueueInputFramePress(
                    keyIndex, songPosOverride, inputEventId);
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
    [Tooltip("Maximum notes spawned in one frame when a seek must catch up a dense section.")]
    public int maxCatchUpSpawnsPerFrame = 512;
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
    private Coroutine poolWarmRoutine;
    // Dev-only: report once why spawning is blocked
    private bool _reportedSpawnBlock = false;
    // Cached corrected local scale computed once for the assigned container/track
    private Vector3 cachedCorrectedLocalScale = Vector3.one;
    private bool cachedScaleValid = false;

    private VelocityWash velocityWash;

    /// <summary>
    /// Lays the dynamics wash along the runway for the chart just loaded.
    /// </summary>
    /// <remarks>
    /// It belongs to the spawner because it needs exactly what the spawner
    /// already holds -- the chart, the track's width and the scroll speed -- and
    /// because there has to be one of it. Built per note it would be several
    /// hundred overlapping copies of the same field.
    /// </remarks>
    private void EnsureVelocityWash()
    {
        if (velocityWash == null)
        {
            // 不掛在軌道底下。音符是用**世界座標**定位的，色場的頂點也是照世界
            // 單位算的 —— 掛進一個有縮放或旋轉的父物件，那些長度就會再被乘一次。
            var host = new GameObject("VelocityWash");
            velocityWash = host.AddComponent<VelocityWash>();
        }

        Transform judgment = null;
        var judgmentGo = GameObject.Find("JudgmentLine");
        if (judgmentGo != null) judgment = judgmentGo.transform;

        velocityWash.Prepare(chart, this, judgment);
    }

    public void Initialize(Chart chart, Conductor conductor)
    {
        if (poolWarmRoutine != null)
        {
            try { StopCoroutine(poolWarmRoutine); } catch { }
            poolWarmRoutine = null;
        }
        this.chart = chart;
        this.conductor = conductor;
        EnsureVelocityWash();
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
            // Pool capacity is based on maximum simultaneous on-screen notes,
            // not total chart length.  Preloading 105% of a 1,600-note chart
            // created thousands of GameObjects and HoldTail renderers during
            // gameplay even though only a few dozen can be visible at once.
            int estimatedRequired = EstimateRequiredPoolCapacity(chart);
            // Finish the bounded visible-window pool before gameplay starts.
            // Spreading Instantiate calls over live frames produced a regular
            // hitch pattern that was much more visible than one loading pause.
            notePool = new NotePool(notePrefab2D, noteContainer, estimatedRequired);
            Debug.Log($"[Note Pool] chartNotes={(chart != null && chart.notes != null ? chart.notes.Count : 0)} " +
                $"visibleCapacity={estimatedRequired} preRollMs={(conductor != null ? conductor.LastPreRollDurationMs : 0f):F0}");
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Development-only diagnostic: report initialization state to help debug missing notes
    BuildLogger.Log($"[NoteSpawner] Initialize: prefabAssigned={(notePrefab2D!=null)}, container={(noteContainer!=null?noteContainer.name:"<null>")}, track={(trackTransform!=null?trackTransform.name:"<null>")}, notesCount={(chart!=null && chart.notes!=null?chart.notes.Count:0)}, conductorPresent={(conductor!=null)}");
#endif
    }

    private int EstimateRequiredPoolCapacity(Chart sourceChart)
    {
        if (sourceChart == null || sourceChart.notes == null || sourceChart.notes.Count == 0)
            return 32;

        int count = sourceChart.notes.Count;
        float baseLookaheadMs = travelTimeSeconds > 0f
            ? travelTimeSeconds * 1000f
            : Mathf.Max(1000f, noteSpawnLookahead);
        // Match the actual runtime spawn window: (travel + remaining pre-roll)
        // multiplied by runwayFactor. Reserving a whole extra travel window
        // overestimated dense charts and created objects that could never be visible.
        float preRollMs = conductor != null ? conductor.LastPreRollDurationMs : 0f;
        float maximumSpawnLeadMs = (baseLookaheadMs + Mathf.Max(0f, preRollMs)) *
            Mathf.Max(0.01f, runwayFactor);

        var starts = new float[count];
        var ends = new float[count];
        int valid = 0;
        for (int i = 0; i < count; i++)
        {
            NoteData note = sourceChart.notes[i];
            if (note == null) continue;
            float start = note.startTime;
            float end = Mathf.Max(start + 350f, Mathf.Max(start, note.endTime) + 350f);
            starts[valid] = start - maximumSpawnLeadMs;
            ends[valid] = end;
            valid++;
        }
        if (valid == 0) return 32;

        System.Array.Sort(starts, 0, valid);
        System.Array.Sort(ends, 0, valid);
        int startIndex = 0;
        int endIndex = 0;
        int active = 0;
        int peak = 0;
        while (startIndex < valid)
        {
            if (endIndex >= valid || starts[startIndex] <= ends[endIndex])
            {
                active++;
                if (active > peak) peak = active;
                startIndex++;
            }
            else
            {
                active = Mathf.Max(0, active - 1);
                endIndex++;
            }
        }

        int margin = Mathf.Max(12, Mathf.CeilToInt(peak * 0.25f));
        return Mathf.Clamp(peak + margin, 32, 512);
    }

    private System.Collections.IEnumerator WarmPoolAsync(int targetCapacity, int batchSize)
    {
        if (notePool == null) yield break;
        // Use public GetStats().Item5 to read current pool size
        while (true)
        {
            var stats = notePool.GetStats();
            int poolSize = notePool.TotalCapacity;
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

    /// <summary>
    /// Reports the component being switched off while the song is still running.
    /// </summary>
    /// <remarks>
    /// A disabled spawner runs no Update, so it cannot report its own silence —
    /// and "the chart stops while the music carries on" looks identical whether
    /// the spawner was disabled or merely blocked. GamePauseManager switches it
    /// off for the pause overlay and back on afterwards; if a resume path ever
    /// fails to restore it, this is the only trace that would exist.
    /// </remarks>
    void OnDisable()
    {
        if (conductor != null && conductor.isActive)
        {
            Debug.LogWarning($"[NoteSpawner] Disabled while the song is active: " +
                $"spawned={nextNoteIndex} songMs={conductor.effectiveSongPosition:F0}");
        }
    }

    void OnEnable()
    {
        if (conductor != null && conductor.isActive)
        {
            Debug.Log($"[NoteSpawner] Re-enabled: spawned={nextNoteIndex} " +
                $"songMs={conductor.effectiveSongPosition:F0}");
        }
    }

    void Update()
    {
        // 生成音符也可能是卡住的那一格的原因。不量的話它會被算進「腳本之外」，
        // 探針就會把生成誤報成算繪。
        using var _probe = HitchProbe.Measure("noteSpawn");
        // Use 2D prefab by default. If notePrefab2D is missing, log an error and skip spawning.
        if (!isInitialized || conductor == null || !conductor.isActive || notePrefab2D == null)
        {
            // Latched per episode, not for the whole session: this always trips
            // once during start-up, and the old permanent latch meant a block
            // that began mid-song — the case where the chart stops while the
            // music keeps going — was reported by nothing at all. Release builds
            // need it too; that is where the failure gets seen.
            if (!_reportedSpawnBlock)
            {
                string why = "";
                if (!isInitialized) why += "isInitialized=false; ";
                if (conductor == null) why += "conductor=null; ";
                else if (!conductor.isActive) why += "conductor.isActive=false; ";
                if (notePrefab2D == null) why += "notePrefab2D=null; ";
                Debug.LogWarning($"[NoteSpawner] Update blocked: {why}" +
                    $"spawned={nextNoteIndex} songMs=" +
                    $"{(conductor != null ? conductor.effectiveSongPosition : float.NaN):F0} " +
                    $"(spawner={gameObject.name})");
                _reportedSpawnBlock = true;
            }
            return;
        }
        if (_reportedSpawnBlock)
        {
            _reportedSpawnBlock = false;
            Debug.Log($"[NoteSpawner] Update resumed: spawned={nextNoteIndex} " +
                $"songMs={conductor.effectiveSongPosition:F0}");
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
        int spawnedThisFrame = 0;
        int catchUpBudget = Mathf.Max(1, maxCatchUpSpawnsPerFrame);
        while (nextNoteIndex < chart.notes.Count && spawnedThisFrame < catchUpBudget)
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
                continue;
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
            // Use the SAME clock as note movement and judgment: Conductor.effectiveSongPosition, which is
            // DSP-based and monotonic. The spawner previously read AudioSync.GetAudioTime() (an
            // AudioSource.time-based value) via reflection. For long, compressed (Vorbis) clips that source
            // can drift or freeze in a BUILD while the DSP clock keeps advancing — the spawner's clock then
            // stalls and stops spawning partway through the song, even though notes keep moving and audio
            // keeps playing (the "chart goes empty after ~3 min, build only" symptom). Reading the conductor
            // here keeps the spawner on one authoritative clock.
            float currentSongPosMs = conductor != null ? conductor.effectiveSongPosition : 0f;
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
                spawnedThisFrame++;
                continue;
            }

            // Notes are time-sorted. Once the first pending note is outside the
            // lookahead window, every following note can wait for a later frame.
            break;
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

        // Preload only the densest visible window. The old implementation used
        // chart.notes.Count, so a 1,600-note chart created ~1,700 GameObjects (and
        // renderers) before playback even though only a small window can be alive.
        int required = EstimateRequiredPoolCapacity(chart);
        if (notePool != null)
        {
            notePool.EnsureCapacity(required);
        }
        else
        {
            // If no pool, optionally pre-instantiate disabled objects into container to warm-up
            for (int i = 0; i < Mathf.Min(32, required); i++)
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
                var activeNotes = new System.Collections.Generic.List<GameObject>();
                for (int i = 0; i < activeParent.childCount; i++)
                {
                    Transform child = activeParent.GetChild(i);
                    if (child == null || child.gameObject == null) continue;
                    GameObject go = child.gameObject;
                    // The pool's inactive reserve uses this same container.
                    // Returning those objects again duplicates references in the
                    // stack, so several chart notes later overwrite one GameObject.
                    if (go.activeSelf && go.GetComponent<NoteController>() != null)
                        activeNotes.Add(go);
                }
                for (int i = 0; i < activeNotes.Count; i++) notePool.Despawn(activeNotes[i]);
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
            // Keep notes exactly on the seek boundary, and also keep a Hold
            // whose head is earlier but whose tail still crosses the target.
            // They will be spawned immediately and resolve against the new clock.
            float noteEndMs = Mathf.Max(n.startTime, n.endTime);
            if (noteEndMs >= ms) break;
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
        if (poolWarmRoutine != null)
        {
            try { StopCoroutine(poolWarmRoutine); } catch { }
            poolWarmRoutine = null;
        }
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
