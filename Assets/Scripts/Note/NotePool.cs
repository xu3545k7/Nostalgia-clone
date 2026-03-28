using System.Collections.Generic;
using UnityEngine;
#pragma warning disable CS0414

public class NotePool
{
    private GameObject prefab;
    private Transform container;
    private bool ownsContainer = false;
    private Stack<GameObject> pool = new Stack<GameObject>();
    // Runtime statistics for verification
    private int totalSpawnCalls = 0;
    private int totalDespawnCalls = 0;
    private int poolHits = 0; // returned from pool
    private int poolMisses = 0; // had to instantiate
    private int logInterval = 500; // log every N spawns to avoid spamming console

    public NotePool(GameObject prefab, Transform container, int initialSize = 64)
    {
        this.prefab = prefab;
        if (container != null)
        {
            this.container = container;
            this.ownsContainer = false;
        }
        else
        {
            this.container = new GameObject("NotePoolContainer").transform;
            this.ownsContainer = true;
        }
        // Pre-instantiate
        for (int i = 0; i < initialSize; i++)
        {
            var go = Object.Instantiate(prefab, this.container);
            global::RuntimeDiagnostics.RegisterInstantiate();
            go.SetActive(false);
            var noteCtrl = go.GetComponent<NoteController>();
            if (noteCtrl != null)
            {
                noteCtrl.SetOwningPool(this);
                // Allow note controller to pre-create its HoldTail (to avoid allocations at spawn)
                noteCtrl.PrecreateHoldTail();
            }
            pool.Push(go);
        }
    //Debug.Log($"NotePool: created for prefab '{prefab.name}' with initialSize={initialSize}, poolCount={pool.Count}");
    }

    // Expose the container so pooled objects (or helper components) can parent transient children
    // into the pool container to ensure they are cleaned up together with pooled instances.
    public Transform Container => this.container;

    /// <summary>
    /// Ensure the pool has at least <paramref name="targetCapacity"/> inactive instances pre-instantiated.
    /// This is used to preload objects for an entire chart to avoid runtime instantiation spikes.
    /// </summary>
    public void EnsureCapacity(int targetCapacity)
    {
        if (targetCapacity <= 0) return;
        int missing = targetCapacity - pool.Count;
        if (missing <= 0) return;
        for (int i = 0; i < missing; i++)
        {
            var go = Object.Instantiate(prefab, this.container);
            global::RuntimeDiagnostics.RegisterInstantiate();
            go.SetActive(false);
            var noteCtrl = go.GetComponent<NoteController>();
            if (noteCtrl != null)
            {
                noteCtrl.SetOwningPool(this);
                noteCtrl.PrecreateHoldTail();
            }
            pool.Push(go);
        }
    //Debug.Log($"NotePool: EnsureCapacity created {missing} instances; new poolSize={pool.Count}");
    }

    /// <summary>
    /// Destroy all pooled instances and optionally the pool container if the pool owns it.
    /// Use this when you want to fully reclaim pooled GameObjects (e.g., on song end).
    /// </summary>
    public void DestroyPool()
    {
        while (pool.Count > 0)
        {
            var go = pool.Pop();
            if (go != null)
            {
                try { Object.Destroy(go); } catch { }
            }
        }

        // Destroy container only if pool created it
        if (ownsContainer && container != null)
        {
            try { Object.Destroy(container.gameObject); } catch { }
        }
        container = null;
    }

    /// <summary>
    /// Asynchronously ensure the pool has at least <paramref name="targetCapacity"/> instances.
    /// Instantiates objects in batches and yields between batches to avoid blocking one frame.
    /// </summary>
    public System.Collections.IEnumerator EnsureCapacityAsync(int targetCapacity, int batchSize = 32)
    {
        if (targetCapacity <= 0) yield break;
        // instantiate in small batches to avoid frame hitching
        while (pool.Count < targetCapacity)
        {
            int toCreate = Mathf.Min(batchSize, targetCapacity - pool.Count);
            for (int i = 0; i < toCreate; i++)
            {
                var go = Object.Instantiate(prefab, this.container);
                global::RuntimeDiagnostics.RegisterInstantiate();
                go.SetActive(false);
                var noteCtrl = go.GetComponent<NoteController>();
                if (noteCtrl != null)
                {
                    noteCtrl.SetOwningPool(this);
                    noteCtrl.PrecreateHoldTail();
                }
                pool.Push(go);
            }
            // yield one frame between batches
            yield return null;
        }
    }

    public GameObject Spawn(Transform parent)
    {
        GameObject go;
        totalSpawnCalls++;
        if (pool.Count > 0)
        {
            go = pool.Pop();
            var noteCtrl = go.GetComponent<NoteController>();
            // Defensive: ensure pooled object is cleaned/reset before reuse
            if (noteCtrl != null)
            {
                try
                {
                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    // Debug.Log($"[NotePool] Reusing pooled object id={go.GetInstanceID()} - cleaning previous state before spawn");
                    #endif
                    noteCtrl.CleanupPooled();
                }
                catch { }
            }
            if (parent != null)
                go.transform.SetParent(parent, false);
            // ensure object is inactive-clean before activation
            try { go.SetActive(true); } catch { }
            poolHits++;
        }
        else
        {
            // Instantiate directly under the desired parent to avoid a visible frame at the pool container
            go = Object.Instantiate(prefab, parent != null ? parent : this.container);
            global::RuntimeDiagnostics.RegisterInstantiate();
            var noteCtrl = go.GetComponent<NoteController>();
            if (noteCtrl != null)
            {
                noteCtrl.SetOwningPool(this);
            }
            poolMisses++;
        }
        

        // Periodic concise logging for runtime verification
        //        if (totalSpawnCalls % logInterval == 0)
        //        {
        //            Debug.Log($"NotePool Spawn stats for '{prefab.name}': totalSpawns={totalSpawnCalls}, poolHits={poolHits}, poolMisses={poolMisses}, currentPoolSize={pool.Count}");
        //        }
        return go;
    }

    public void Despawn(GameObject go)
    {
        if (go == null) return;
        totalDespawnCalls++;
        // For debugging: detect if this note has a HoldTail child before cleanup
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Debug info: inspect whether a HoldTail child existed before cleanup (kept minimal to avoid unused variable warnings)
        string tailParentBefore = "(none)";
        bool tailActiveBefore = false;
        for (int ci = go.transform.childCount - 1; ci >= 0; --ci)
        {
            var c = go.transform.GetChild(ci);
            if (c != null && c.gameObject != null && c.gameObject.name == "HoldTail")
            {
                tailParentBefore = c.parent != null ? c.parent.name : "(null)";
                tailActiveBefore = c.gameObject.activeInHierarchy;
                break;
            }
        }
        #endif
        // Clean up transient children created by scripts (e.g., tail)
        var noteController = go.GetComponent<NoteController>();
        if (noteController != null)
        {
            noteController.CleanupPooled();
        }
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        // After cleanup, optionally check if a HoldTail exists under the pool container (kept minimal)
        string foundContainerChild = "(none)";
        if (container != null)
        {
            for (int ci = container.childCount - 1; ci >= 0; --ci)
            {
                var c = container.GetChild(ci);
                if (c != null && c.gameObject != null && c.gameObject.name == "HoldTail")
                {
                    foundContainerChild = c.name;
                    break;
                }
            }
        }
        // Debug.Log($"NotePool.Despawn: go='{go.name}', tailParentBefore={tailParentBefore}, tailActiveBefore={tailActiveBefore}, foundHoldTailInPoolContainerChild={foundContainerChild}, poolSizeBefore={pool.Count}");
        #endif
        
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try {
            string stack = System.Environment.StackTrace;
            float posZ = float.NaN;
            try { posZ = go.transform.position.z; } catch {}
            // Debug.Log($"[NotePool] Despawn: go='{go.name}' id={go.GetInstanceID()} posZ={posZ} poolSizeBefore={pool.Count} totalDespawns={totalDespawnCalls}\nCallStack:\n{stack}");
        } catch { }
        #endif
        go.SetActive(false);
        go.transform.SetParent(container, false);
        pool.Push(go);
    global::RuntimeDiagnostics.RegisterDespawn();

        // Occasionally log despawn counts as well (less frequent)
        //        if (totalDespawnCalls % (logInterval * 2) == 0)
        //        {
        //            Debug.Log($"NotePool Despawn summary for '{prefab.name}': totalDespawn={totalDespawnCalls}, poolSize={pool.Count}");
        //        }
    }

    // Expose lightweight stats for debug UI or on-demand checks
    public (int totalSpawns, int totalDespawns, int hits, int misses, int poolSize) GetStats()
    {
        return (totalSpawnCalls, totalDespawnCalls, poolHits, poolMisses, pool.Count);
    }
}
