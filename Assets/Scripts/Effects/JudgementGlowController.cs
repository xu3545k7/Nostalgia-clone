using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JudgementGlowController : MonoBehaviour
{
    public static JudgementGlowController Instance { get; private set; }

    [Header("Prefab & Pool")]
    public GameObject glowPrefab; // 指向 JudgeGlow.prefab
    public int poolSize = 6;

    [Header("Timing")]
    public float riseDuration = 0.08f; // 光強上升速度
    public float sustain = 0.04f;
    public float fadeDuration = 0.25f;

    [Header("Transforms")]
    public Transform judgementLineTransform; // 若要相對判定線定位

    [Header("Light Settings")]
    public float peakLightIntensity = 2f;
    public float peakEmission = 4f;

    Queue<GameObject> pool = new Queue<GameObject>();

    /// <summary>
    /// Every glow handed out, with the unscaled time by which it must be back.
    /// </summary>
    /// <remarks>
    /// The animation lives entirely in a coroutine, so anything that kills the
    /// coroutine — the controller being disabled, a scene change, a paused
    /// <c>WaitForSeconds</c> that never resumes — strands the object at peak
    /// emission with its Light at full intensity, permanently. On a flat surface
    /// just above the keyboard that reads as a huge pool of light across the
    /// judgment line, which is exactly the reported symptom, and nothing in the
    /// original design could ever recover from it.
    ///
    /// So the pool is no longer trusted to be returned to. Anything still out
    /// past the time its own animation needed is put back by
    /// <see cref="Update"/>, whatever went wrong.
    /// </remarks>
    readonly Dictionary<GameObject, float> liveGlows = new Dictionary<GameObject, float>();
    readonly List<GameObject> strandedGlows = new List<GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // init pool
        for (int i = 0; i < poolSize; i++)
        {
            if (glowPrefab == null) break;
            var go = Instantiate(glowPrefab, transform);
            go.SetActive(false);
            pool.Enqueue(go);
        }
    }

    GameObject GetFromPool()
    {
        if (pool.Count > 0) return pool.Dequeue();
        if (glowPrefab == null) return null;
        var go = Instantiate(glowPrefab, transform);
        go.SetActive(false);
        return go;
    }

    void ReturnToPool(GameObject go)
    {
        if (go == null) return;
        liveGlows.Remove(go);
        Darken(go);
        go.SetActive(false);
        pool.Enqueue(go);
    }

    /// <summary>Puts a glow back to black, independently of its coroutine.</summary>
    static void Darken(GameObject go)
    {
        if (go == null) return;
        var rend = go.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            var mat = rend.material;
            if (mat != null && mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);
        }
        var light = go.GetComponentInChildren<Light>();
        if (light != null) light.intensity = 0f;
    }

    void Update()
    {
        if (liveGlows.Count == 0) return;
        float now = Time.unscaledTime;
        strandedGlows.Clear();
        foreach (var pair in liveGlows)
        {
            if (pair.Key == null || now > pair.Value) strandedGlows.Add(pair.Key);
        }
        for (int i = 0; i < strandedGlows.Count; i++)
        {
            var go = strandedGlows[i];
            liveGlows.Remove(go);
            if (go == null) continue;
            Debug.LogWarning("[JudgementGlow] A glow outlived its animation and was " +
                "left lit; returning it to the pool.");
            ReturnToPool(go);
        }
    }

    /// <summary>
    /// Nothing may stay lit across a disable — the coroutines do not survive it.
    /// </summary>
    void OnDisable()
    {
        foreach (var pair in liveGlows)
        {
            if (pair.Key == null) continue;
            Darken(pair.Key);
            pair.Key.SetActive(false);
            pool.Enqueue(pair.Key);
        }
        liveGlows.Clear();
    }

    // Play at world position; optional followX makes the quad follow x while animating Y/Z stable.
    public void PlayAt(Vector3 worldPos, bool followX = false, Color? color = null)
    {
        StartCoroutine(PlayCoroutine(worldPos, followX, color));
    }

    IEnumerator PlayCoroutine(Vector3 worldPos, bool followX, Color? color)
    {
        var go = GetFromPool();
        if (go == null) yield break;
        go.SetActive(true);
        // Unscaled, and with slack: the animation itself runs on scaled time, so
        // a paused game must not be mistaken for a stranded glow.
        liveGlows[go] = Time.unscaledTime +
            (riseDuration + sustain + fadeDuration) * 2f + 1f;

        var light = go.GetComponentInChildren<Light>();
        var rend = go.GetComponentInChildren<Renderer>();
        Material mat = null;
        if (rend != null)
        {
            // use instance material so we can animate safely
            mat = rend.material;
        }

        if (color.HasValue && mat != null)
        {
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color.Value * 1f);
        }

        float t = 0f;
        float peakE = peakEmission;
        float peakL = peakLightIntensity;

        // initial set
        go.transform.position = worldPos;
        if (mat != null)
        {
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
        }
        if (light != null) light.intensity = 0f;

        // rise
        while (t < riseDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / riseDuration);
            if (mat != null && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", (color ?? Color.white) * (p * peakE));
            if (light != null) light.intensity = p * peakL;
            if (followX && judgementLineTransform != null)
            {
                Vector3 pos = go.transform.position;
                pos.x = judgementLineTransform.position.x;
                go.transform.position = pos;
            }
            yield return null;
        }
        // sustain
        if (mat != null && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", (color ?? Color.white) * peakE);
        if (light != null) light.intensity = peakL;

        yield return new WaitForSeconds(sustain);

        // fade
        t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float p = 1f - Mathf.Clamp01(t / fadeDuration);
            if (mat != null && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", (color ?? Color.white) * (p * peakE));
            if (light != null) light.intensity = p * peakL;
            if (followX && judgementLineTransform != null)
            {
                Vector3 pos = go.transform.position;
                pos.x = judgementLineTransform.position.x;
                go.transform.position = pos;
            }
            yield return null;
        }

        // cleanup
        if (mat != null && mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
        if (light != null) light.intensity = 0f;
        ReturnToPool(go);
    }
}
