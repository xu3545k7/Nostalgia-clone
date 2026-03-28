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
            float intensity = intensityCurve != null ? Mathf.Max(0f, intensityCurve.Evaluate(normalized)) : 1f;
            ApplyIntensity(intensity);
            t += Time.deltaTime;
            yield return null;
        }

        ApplyIntensity(0f);
        glowRoutine = null;
    }

    private void ApplyIntensity(float intensity)
    {
        EnsurePropertyBlock();
        if (targetRenderer == null) return;

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(emissionColorPropertyId, glowColor * intensity);
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
