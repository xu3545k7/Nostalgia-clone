using System.Collections.Generic;
using UnityEngine;

public class BeatLinePool : ISpawnPool
{
    private GameObject prefab;
    private Transform container;
    private bool ownsContainer = false;
    private Stack<GameObject> pool = new Stack<GameObject>();
    private int totalSpawnCalls = 0;
    private int totalDespawnCalls = 0;
    private int poolHits = 0;
    private int poolMisses = 0;

    public BeatLinePool(GameObject prefab, Transform container, int initialSize = 64)
    {
        this.prefab = prefab;
        if (container != null)
        {
            this.container = container;
            this.ownsContainer = false;
        }
        else
        {
            this.container = new GameObject("BeatLinePoolContainer").transform;
            this.ownsContainer = true;
        }
        for (int i = 0; i < initialSize; i++)
        {
            var go = Object.Instantiate(prefab, this.container);
            global::RuntimeDiagnostics.RegisterInstantiate();
            go.SetActive(false);
            var ctrl = go.GetComponent<BeatLineController>();
            if (ctrl != null)
            {
                ctrl.SetOwningPool(this);
            }
            pool.Push(go);
        }
        //Debug.Log($"BeatLinePool: created for prefab '{prefab.name}' with initialSize={initialSize}, poolCount={pool.Count}");
    }

    // Expose the container for external management/cleanup
    public Transform Container => this.container;

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
            var ctrl = go.GetComponent<BeatLineController>();
            if (ctrl != null)
            {
                ctrl.SetOwningPool(this);
            }
            pool.Push(go);
        }
        //Debug.Log($"BeatLinePool: EnsureCapacity created {missing} instances; new poolSize={pool.Count}");
    }

    /// <summary>
    /// Destroy all pooled beatline instances and the container if owned.
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

        if (ownsContainer && container != null)
        {
            try { Object.Destroy(container.gameObject); } catch { }
        }
        container = null;
    }

    public GameObject Spawn(Transform parent)
    {
        totalSpawnCalls++;
        GameObject go;
        if (pool.Count > 0)
        {
            go = pool.Pop();
            poolHits++;
            go.SetActive(true);
        }
        else
        {
            go = Object.Instantiate(prefab, this.container);
            global::RuntimeDiagnostics.RegisterInstantiate();
            var ctrl = go.GetComponent<BeatLineController>();
            if (ctrl != null) ctrl.SetOwningPool(this);
            poolMisses++;
        }
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    public void Despawn(GameObject go)
    {
        if (go == null) return;
        totalDespawnCalls++;
        var ctrl = go.GetComponent<BeatLineController>();
        if (ctrl != null)
        {
            ctrl.CleanupPooled();
        }
        go.SetActive(false);
        go.transform.SetParent(container, false);
        pool.Push(go);
        global::RuntimeDiagnostics.RegisterDespawn();
    }

    public (int totalSpawns, int totalDespawns, int hits, int misses, int poolSize) GetStats()
    {
        return (totalSpawnCalls, totalDespawnCalls, poolHits, poolMisses, pool.Count);
    }
}
