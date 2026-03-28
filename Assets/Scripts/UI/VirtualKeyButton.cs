using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight helper representing a virtual on-screen key button.
/// Scripts call VirtualKeyButton.GetRectForId(id) to locate the RectTransform
/// for a given lane id. Attach this component to any UI element that represents
/// a virtual key and set its `bottonId` in the inspector.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class VirtualKeyButton : MonoBehaviour
{
    private static readonly Dictionary<int, RectTransform> registry = new Dictionary<int, RectTransform>();

    [Tooltip("Logical button id (lane) this UI element corresponds to")]
    public int bottonId = 0;

    [Tooltip("Optional descriptive name for this virtual key")]
    public string bottonName = "";

    private RectTransform rt;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        Register(bottonId, rt);
    }

    void OnDisable()
    {
        Unregister(bottonId);
    }

    void OnValidate()
    {
        // keep registry in sync in editor
        if (rt == null) rt = GetComponent<RectTransform>();
        if (Application.isPlaying)
        {
            Register(bottonId, rt);
        }
    }

    private static void Register(int id, RectTransform rect)
    {
        if (rect == null) return;
        registry[id] = rect;
    }

    private static void Unregister(int id)
    {
        if (registry.ContainsKey(id)) registry.Remove(id);
    }

    /// <summary>
    /// Returns the RectTransform for the given button id if available, otherwise null.
    /// The method will also attempt to find instances in the scene if the cache is empty.
    /// </summary>
    public static RectTransform GetRectForId(int id)
    {
        if (registry.TryGetValue(id, out var rt)) return rt;

        // try to populate registry by finding existing components
        var all = Object.FindObjectsByType<VirtualKeyButton>(FindObjectsSortMode.None);
        foreach (var v in all)
        {
            if (v == null) continue;
            var r = v.GetComponent<RectTransform>();
            if (r == null) continue;
            registry[v.bottonId] = r;
        }

        return registry.TryGetValue(id, out var found) ? found : null;
    }
}
