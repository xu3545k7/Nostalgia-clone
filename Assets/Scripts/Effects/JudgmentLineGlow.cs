using System.Collections;
using UnityEngine;

/// <summary>
/// Pulses a renderer's emission whenever the judgment line should light up.
/// Assign the glow material in the inspector and call TriggerGlow() after each hit.
/// </summary>
[DefaultExecutionOrder(-15)]
public class JudgmentLineGlow : MonoBehaviour
{
    public static JudgmentLineGlow Instance { get; private set; }

    [SerializeField, Tooltip("Renderer that uses the glowing material (defaults to this GameObject).")]
    private Renderer targetRenderer;

    [SerializeField, Tooltip("Shader property name used for emission color.")]
    private string emissionColorProperty = "_EmissionColor";

    [SerializeField, Tooltip("Color multiplied by the intensity curve while glowing.")]
    private Color glowColor = new Color(1f, 0.5f, 1f, 1f);

    [SerializeField, Tooltip("Curve controlling how the glow fades in/out over the pulse.")]
    private AnimationCurve intensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);

    [SerializeField, Tooltip("Default duration for the glow pulse."), Min(0.01f)]
    private float defaultDuration = 0.18f;

    private Coroutine glowRoutine;
    private MaterialPropertyBlock propertyBlock;
    private int emissionColorPropertyId = -1;

    public static JudgmentLineGlow GetOrCreate()
    {
        if (Instance != null) return Instance;
        GameObject line = GameObject.Find("JudgmentLine");
        if (line == null) return null;
        var glow = line.GetComponent<JudgmentLineGlow>();
        if (glow == null) glow = line.AddComponent<JudgmentLineGlow>();
        return glow;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }
        CachePropertyId();
        EnsurePropertyBlock();
        ApplyIntensity(0f);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDisable()
    {
        if (glowRoutine != null)
        {
            StopCoroutine(glowRoutine);
            glowRoutine = null;
        }
        ApplyIntensity(0f);
    }

    private void CachePropertyId()
    {
        if (string.IsNullOrEmpty(emissionColorProperty))
        {
            emissionColorProperty = "_EmissionColor";
        }
        emissionColorPropertyId = Shader.PropertyToID(emissionColorProperty);
    }

    private void EnsurePropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }

    /// <summary>
    /// Triggers the glow pulse. Call this when a judgment occurs.
    /// </summary>
    /// <param name="duration">Optional override for the pulse duration.</param>
    public void TriggerGlow(float duration = -1f)
    {
        if (!isActiveAndEnabled || targetRenderer == null)
        {
            return;
        }

        float resolvedDuration = duration > 0f ? duration : defaultDuration;
        if (glowRoutine != null)
        {
            StopCoroutine(glowRoutine);
        }
        glowRoutine = StartCoroutine(GlowRoutine(resolvedDuration));
    }

    /// <summary>Small white pulse used when a beat guide reaches the line.</summary>
    public void TriggerBeatPulse(float duration = 0.09f)
    {
        if (!isActiveAndEnabled || targetRenderer == null) return;
        if (glowRoutine != null) StopCoroutine(glowRoutine);
        glowRoutine = StartCoroutine(BeatPulseRoutine(Mathf.Max(0.03f, duration)));
    }

    private IEnumerator BeatPulseRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            // One-frame-fast attack followed by a very short quadratic fade.
            float attack = Mathf.Clamp01(t / 0.12f);
            float fade = 1f - Mathf.Clamp01((t - 0.12f) / 0.88f);
            // A measure/beat line should produce a clearly visible platinum-white sweep,
            // even over bright notes. The shader shapes this into its edge and centre core.
            float intensity = attack * fade * fade * 1.35f;
            ApplyColor(new Color(0.92f, 1f, 1f, 1f), intensity);
            elapsed += Time.deltaTime;
            yield return null;
        }
        ApplyIntensity(0f);
        glowRoutine = null;
    }

    private IEnumerator GlowRoutine(float duration)
    {
        if (duration <= 0f)
        {
            duration = 0.01f;
        }

        float t = 0f;
        while (t < duration)
        {
            float normalized = t / duration;
            float attack = Mathf.Clamp01(normalized / 0.1f);
            float fade = 1f - Mathf.Clamp01((normalized - 0.1f) / 0.9f);
            float intensity = attack * fade * fade;
            ApplyColor(new Color(1f, 0.97f, 0.82f, 1f), intensity);
            t += Time.deltaTime;
            yield return null;
        }

        ApplyIntensity(0f);
        glowRoutine = null;
    }

    private void ApplyIntensity(float intensity)
    {
        ApplyColor(glowColor, intensity);
    }

    private void ApplyColor(Color color, float intensity)
    {
        EnsurePropertyBlock();
        if (targetRenderer == null) return;
        if (emissionColorPropertyId < 0) CachePropertyId();

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(emissionColorPropertyId, color * intensity);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }
        CachePropertyId();
    }
#endif
}
