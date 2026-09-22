using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Simple particle effect player that spawns pooled particle systems at note hit positions.
/// </summary>
[DefaultExecutionOrder(-25)]
public class ParticleEffectPlayer : MonoBehaviour
{
    public static ParticleEffectPlayer Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private ParticleSystem[] particleEffects;
    [SerializeField, Tooltip("Legacy Ring / Flower / Pin burst on ordinary judgments. Keep disabled when these decorations are reserved for Hold notes.")]
    private bool playDecorativeHitParticles = false;
    
    [Header("Special Effect (Independent)")]
    [SerializeField] private ParticleSystem specialEffect;
    [SerializeField] private Vector3 specialEffectSpawnOffset = Vector3.zero;
    [SerializeField] private int specialEffectPoolSize = 5;
    [SerializeField] private float specialEffectSizeScale = 1.0f;
    [SerializeField] private bool specialEffectAdaptToNoteWidth = true;
    [SerializeField] private float specialEffectWidthMultiplier = 0.8f;
    [SerializeField] private float specialEffectParticleSizeRatio = 0.75f;
    [SerializeField] private Vector2 specialEffectParticleSizeClamp = new Vector2(0.2f, 1.2f);
    [SerializeField] private float specialEffectShapeThickness = 0.04f;
    [SerializeField] private float specialEffectZOffset = 0.02f;
    [SerializeField] private bool playSpecialEffectOnHit = true;

    [Header("Pooling")]
    [SerializeField] private int poolSize = 20;
    [SerializeField] private Transform poolRoot;

    [Header("Spawn Settings")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.05f, 0f);
    [SerializeField] private float particleSizeScale = 1.5f;

    [Header("Classical Hit Accent")]
    [SerializeField] private bool playClassicalHitAccent = true;
    [SerializeField] private int rightLaneStartIndex = 14;
    [SerializeField] private Color leftAccentColor = new Color(0.2745098f, 0.6392157f, 1f, 1f);
    [SerializeField] private Color rightAccentColor = new Color(1f, 0.1764706f, 0.1764706f, 1f);
    [SerializeField, Tooltip("Keeps hit particles above notes and the judgment mesh.")]
    private int foregroundSortingOrder = 32750;

    [Header("Judgment Line Star Fan")]
    [SerializeField] private bool enableJudgmentLineStarFan = true;
    [SerializeField, Range(1, 128)] private int judgmentStarBurstCount = 48;
    [SerializeField, Range(1f, 80f)] private float judgmentStarHoldRate = 56.5f;
    [SerializeField, Range(0.1f, 15f)] private float judgmentStarSpeed = 6.2f;
    [SerializeField, Range(5f, 80f)] private float judgmentStarHalfAngle = 42f;
    [SerializeField, Range(0.05f, 2f)] private float judgmentStarLifetime = 0.5f;
    [SerializeField, Range(0.02f, 1.5f)] private float judgmentStarSize = 0.62f;

    [Header("Hold Rotation")]
    [SerializeField, Tooltip("A layered classical magic circle stays visible and rotates behind a Hold while it is pressed.")]
    private bool enableHoldRotation = true;
    [SerializeField] private float holdFlowerDegreesPerSecond = 42f;
    [SerializeField] private float holdPinDegreesPerSecond = -68f;

    [Header("Magic Circle Burst")]
    [SerializeField, Tooltip("Every judgment opens the seal and shatters it outward. A Hold then fades its persistent circle in underneath.")]
    private bool enableMagicShatterBurst = true;
    [SerializeField, Tooltip("Slides skip the seal. It is a wide circle centred on one lane, and it covers the wake that carries the slide's direction.")]
    private bool suppressShatterOnSlide = true;
    [SerializeField, Tooltip("Wedges the seal breaks into.")]
    [Range(3, 24)] private int magicShatterShards = 8;
    [SerializeField, Tooltip("How far a shard slides out, in fractions of the sprite's own width.")]
    private float magicShatterSpread = 0.20f;
    [SerializeField, Tooltip("Seconds from contact to fully faded.")]
    private float magicShatterLifetime = 0.38f;
    [SerializeField, Tooltip("Fraction of the lifetime spent at full opacity before the fade starts.")]
    [Range(0f, 0.9f)] private float magicShatterFadeStart = 0.30f;
    [SerializeField, Tooltip("Spin rate of the shattering burst relative to a resting Hold circle's.")]
    private float magicShatterSpinScale = 0.55f;
    [SerializeField, Tooltip("Seconds the Hold's persistent circle takes to fade in under the burst.")]
    private float holdMagicFadeInTime = 0.22f;
    [SerializeField, Tooltip("Idle magic-circle sprites kept for reuse. Spawning these per hit is what would otherwise churn GameObjects on dense charts.")]
    private int magicSpritePoolCap = 64;

    private Queue<ParticleSystem> availableSystems = new Queue<ParticleSystem>();
    private HashSet<ParticleSystem> activeSystems = new HashSet<ParticleSystem>();
    private Queue<ParticleSystem> availableSpecialSystems = new Queue<ParticleSystem>();
    private HashSet<ParticleSystem> activeSpecialSystems = new HashSet<ParticleSystem>();
    private readonly Dictionary<NoteController, List<HoldRotatingEffect>> activeHoldEffects =
        new Dictionary<NoteController, List<HoldRotatingEffect>>();
    private readonly Dictionary<NoteController, HoldRotatingEffect> activeHoldContactCores =
        new Dictionary<NoteController, HoldRotatingEffect>();
    private readonly List<NoteController> staleHoldEffects = new List<NoteController>();
    // Non-Hold circles are not keyed by note: they outlive the judgment that
    // spawned them and hold their own world transform.
    private readonly List<HoldRotatingEffect> transientMagicEffects = new List<HoldRotatingEffect>();
    private readonly Queue<GameObject> magicSpritePool = new Queue<GameObject>();
    private static readonly Dictionary<string, Sprite> holdSpriteCache = new Dictionary<string, Sprite>();
    private static Sprite holdContactCoreSprite;
    private static readonly Sprite[] holdMagicLayerSprites = new Sprite[3];
    private static Material holdMagicForegroundMaterial;
    private static Material holdMagicGeometryMaterial;
    private static Material holdMagicContactCoreMaterial;
    /// <summary>
    /// The next seal to be built takes the violet rim instead of the gilt one.
    /// </summary>
    /// <remarks>
    /// A flag rather than a parameter because the palette is applied three or
    /// four layers down a call that already carries a dozen arguments, and it is
    /// only ever true for the length of one synchronous spawn: the caller sets
    /// it, fires the effect, and clears it. Nothing reads it later -- a seal
    /// keeps whatever rim it was built with for as long as it is held.
    /// </remarks>
    public static bool NextSealIsPrecise;

    private static readonly int HoldInnerColorId = Shader.PropertyToID("_InnerColor");
    private static readonly int HoldOuterColorId = Shader.PropertyToID("_OuterColor");
    private static readonly int HoldPlatinumColorId = Shader.PropertyToID("_PlatinumColor");
    private static readonly int HoldCoreHighlightColorId = Shader.PropertyToID("_CoreHighlightColor");
    private static readonly int HoldRadialScaleId = Shader.PropertyToID("_RadialScale");
    private static readonly int HoldRadialBiasId = Shader.PropertyToID("_RadialBias");
    private static readonly int HoldShardCountId = Shader.PropertyToID("_ShardCount");
    private static readonly int HoldShardSpreadId = Shader.PropertyToID("_ShardSpread");
    private static readonly int HoldUvZoomId = Shader.PropertyToID("_UvZoom");
    private static MaterialPropertyBlock sharedMagicBlock;

    private sealed class HoldRotatingEffect
    {
        public GameObject Object;
        public SpriteRenderer Renderer;
        public float DegreesPerSecond;
        public float Angle;
        /// <summary>Resting scale. The burst curve multiplies this, it does not replace it.</summary>
        public Vector3 BaseScale;
        public float Opacity;
        public float Age;
        /// <summary>Zero for a Hold's persistent circle; a fade-out duration for a shattering burst.</summary>
        public float Lifetime;
        public float LastAppliedSpread;
        /// <summary>Only meaningful for transient circles, which do not follow a note.</summary>
        public Vector3 Position;
        public Quaternion Rotation;
        public NoteController Owner;
        public float LastAppliedAlpha;
    }

    public bool TryGetJudgmentStarFanSettings(out int burstCount, out float holdRate,
        out float speed, out float halfAngle, out float lifetime, out float size)
    {
        burstCount = Mathf.Clamp(judgmentStarBurstCount, 1, 128);
        holdRate = Mathf.Max(1f, judgmentStarHoldRate);
        speed = Mathf.Max(0.1f, judgmentStarSpeed);
        halfAngle = Mathf.Clamp(judgmentStarHalfAngle, 5f, 80f);
        lifetime = Mathf.Max(0.05f, judgmentStarLifetime);
        size = Mathf.Max(0.02f, judgmentStarSize);
        return enableJudgmentLineStarFan;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (poolRoot == null) poolRoot = transform;
        // Build the shared judgment-fan pool while the scene is loading.  No
        // ParticleSystem components should be created on the first dense chord.
        NoteFanParticleEmitter.PrewarmSharedPool();
        // Ring / Flower / Pin are intentionally reserved for Hold feedback.
        // Avoid allocating their ordinary-hit pool while that layer is disabled.
        if (playDecorativeHitParticles) InitializePool();
        InitializeSpecialEffectPool();
    }

    private bool holdAssetsPrewarmStarted;

    private void OnEnable()
    {
        // The Hold magic circle's textures and materials are the one thing left
        // that was still built on first contact; see PrewarmHoldAssets.
        //
        // Started here rather than in Awake because the scene serialises this
        // emitter inactive and HitEffectRouter wakes it: inside that SetActive
        // the component is not enabled yet, and a coroutine started from a
        // disabled behaviour does not run.
        if (holdAssetsPrewarmStarted) return;
        holdAssetsPrewarmStarted = true;
        StartCoroutine(PrewarmHoldAssets());
    }

    private void OnDestroy()
    {
        ClearHoldEffects();
        if (Instance == this)
        {
            Instance = null;
            ReleaseHoldMagicRuntimeAssets();
        }
    }

    private static void ReleaseHoldMagicRuntimeAssets()
    {
        for (int i = 0; i < holdMagicLayerSprites.Length; i++)
        {
            Sprite sprite = holdMagicLayerSprites[i];
            if (sprite == null) continue;
            Texture2D texture = sprite.texture;
            Destroy(sprite);
            if (texture != null) Destroy(texture);
            holdMagicLayerSprites[i] = null;
        }
        if (holdContactCoreSprite != null)
        {
            Texture2D texture = holdContactCoreSprite.texture;
            Destroy(holdContactCoreSprite);
            if (texture != null) Destroy(texture);
            holdContactCoreSprite = null;
        }
        if (holdMagicForegroundMaterial != null)
        {
            Destroy(holdMagicForegroundMaterial);
            holdMagicForegroundMaterial = null;
        }
        if (holdMagicGeometryMaterial != null)
        {
            Destroy(holdMagicGeometryMaterial);
            holdMagicGeometryMaterial = null;
        }
        if (holdMagicContactCoreMaterial != null)
        {
            Destroy(holdMagicContactCoreMaterial);
            holdMagicContactCoreMaterial = null;
        }
    }

    private void InitializePool()
    {
        if (particleEffects == null || particleEffects.Length == 0)
        {
            Debug.LogWarning("ParticleEffectPlayer: No particle effects assigned.");
            return;
        }

        for (int i = 0; i < poolSize; i++)
        {
            CreatePooledInstance();
        }
    }

    private void CreatePooledInstance()
    {
        ParticleSystem selectedPrefab = particleEffects[Random.Range(0, particleEffects.Length)];
        var instance = Instantiate(selectedPrefab, poolRoot);
        ConfigureForegroundRenderer(instance);
        instance.gameObject.SetActive(false);
        instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        availableSystems.Enqueue(instance);
    }

    private ParticleSystem GetPooledInstance()
    {
        if (availableSystems.Count == 0)
        {
            CreatePooledInstance();
        }

        var system = availableSystems.Dequeue();
        activeSystems.Add(system);
        return system;
    }

    private void ReturnToPool(ParticleSystem system)
    {
        if (system != null)
        {
            system.gameObject.SetActive(false);
            activeSystems.Remove(system);
            availableSystems.Enqueue(system);
        }
    }

    private void InitializeSpecialEffectPool()
    {
        if (specialEffect == null)
        {
            return;
        }

        for (int i = 0; i < specialEffectPoolSize; i++)
        {
            CreateSpecialPooledInstance();
        }
    }

    private void CreateSpecialPooledInstance()
    {
        var instance = Instantiate(specialEffect, poolRoot);
        ConfigureForegroundRenderer(instance);
        instance.gameObject.SetActive(false);
        instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        availableSpecialSystems.Enqueue(instance);
    }

    private ParticleSystem GetSpecialPooledInstance()
    {
        if (specialEffect == null) return null;

        if (availableSpecialSystems.Count == 0)
        {
            CreateSpecialPooledInstance();
        }

        var system = availableSpecialSystems.Dequeue();
        activeSpecialSystems.Add(system);
        return system;
    }

    private void ReturnSpecialToPool(ParticleSystem system)
    {
        if (system != null)
        {
            system.gameObject.SetActive(false);
            activeSpecialSystems.Remove(system);
            availableSpecialSystems.Enqueue(system);
        }
    }

    /// <summary>
    /// Play a particle effect at the specified position.
    /// </summary>
    public void PlayEffect(Vector3 position)
    {
        var system = GetPooledInstance();
        if (system == null) return;

        var main = system.main;
        main.startSize = Random.Range(0.5f, 1f) * particleSizeScale;

        system.transform.position = position + spawnOffset;
        system.gameObject.SetActive(true);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Play(true);

        StartCoroutine(ReturnWhenFinished(system));
    }

    /// <summary>
    /// Play a particle effect when a note is hit.
    /// </summary>
    public void PlayHitEffect(NoteController note, int laneId, JudgmentResult result)
    {
        if (note == null) return;

        // This is the shared path used by TAP, STAC, SOFT and Hold judgments.
        // Trigger the judgment-line fan here instead of coupling it to the
        // optional mesh overlay, so non-Hold notes cannot silently miss it.
        try { note.PlayJudgmentLineFanParticles(false, laneId); } catch { }

        if (playDecorativeHitParticles)
        {
            ComputeSpecialEffectTransform(note, laneId, out var hitPosition, out var hitRotation, out _);
            PlayEffectOnJudgmentPlane(hitPosition, hitRotation);
        }
        if (playSpecialEffectOnHit)
        {
            PlaySpecialEffect(note, laneId);
        }

        // Keep the premium white/gold burst on the same path as the existing
        // particle emitter. This guarantees every actual hit emission also
        // drives the accent and the judgment-line flash.
        PlayClassicalAccent(note, laneId, result);

        // Only a clean hit opens a seal, matching PlayClassicalAccent.
        if (result == JudgmentResult.Perfect || result == JudgmentResult.Great ||
            result == JudgmentResult.Good)
        {
            SpawnMagicShatter(note, laneId);
        }
    }

    /// <summary>
    /// Keeps only Flower and Pin alive behind the note at the judgment line while a Hold is
    /// pressed. Pin remains an instantaneous hit layer.
    /// </summary>
    public void BeginHoldRotation(NoteController note, int laneId = -1)
    {
        if (note == null) return;

        // Continuous Hold emission is independent of the decorative rotating
        // sprites and therefore remains available when those sprites are off.
        try { note.PlayJudgmentLineFanParticles(true, laneId); } catch { }

        if (!IsHoldMagicCircleVisible() || activeHoldEffects.ContainsKey(note) ||
            activeHoldContactCores.ContainsKey(note)) return;

        // The head judgment usually already opened a shattering burst through
        // PlayHitEffect. It is kept: the burst is the contact, and the
        // persistent seal fades in underneath as it breaks apart.
        SpawnMagicShatter(note, laneId);

        var effects = new List<HoldRotatingEffect>(3);
        ComputeHoldMagicTransform(note, laneId, out var position, out var rotation);
        float visualSize = Mathf.Max(0.25f, ComputeNoteWidth(note) * 1.15f);
        ResolveHoldBackgroundSorting(note, out int sortingLayerId, out int sortingOrder);
        bool rightHand = ResolveIsRightHand(note, laneId);

        // These are authored transparent sprites instead of runtime LineRenderer
        // geometry.  Their fine engraving survives the close gameplay camera,
        // while independent rotation keeps the original mechanical feel.
        AddHoldSpriteEffect(effects, HoldMagicSpritePaths[0],
            "HoldMagic_Outer_Ornate", -Mathf.Abs(holdFlowerDegreesPerSecond),
            position, rotation, visualSize * 1.025f, rightHand, 0.82f, 0f,
            sortingLayerId, sortingOrder, 1f, 1f);
        AddHoldSpriteEffect(effects, HoldMagicSpritePaths[1],
            "HoldMagic_Triangle_Ornate", Mathf.Abs(holdPinDegreesPerSecond),
            position, rotation, visualSize * 0.83f, rightHand,
            0.90f, 17f, sortingLayerId, sortingOrder + 1, 0.83f / 1.025f, 1f);
        AddHoldSpriteEffect(effects, HoldMagicSpritePaths[2],
            "HoldMagic_InnerSeal_Ornate", -Mathf.Abs(holdFlowerDegreesPerSecond) * 0.65f,
            position, rotation, visualSize * 0.65f, rightHand,
            0.84f, -11f, sortingLayerId, sortingOrder + 2, 0.65f / 1.025f, 1f);

        CreateHoldContactCore(note, position, rotation, visualSize, rightHand,
            sortingLayerId, sortingOrder + 12);

        if (effects.Count > 0) activeHoldEffects[note] = effects;
    }

    /// <summary>
    /// Opens the seal at the judgment line and immediately breaks it apart.
    /// This is the contact itself, so every judged note gets one; a Hold simply
    /// fades its persistent circle in underneath while this burst clears.
    /// </summary>
    private void SpawnMagicShatter(NoteController note, int laneId)
    {
        if (note == null || !enableMagicShatterBurst) return;
        if (!IsHoldMagicCircleVisible()) return;
        // The seal is as wide as the note and centred on it; a slide's wake
        // streams sideways straight underneath it.
        if (suppressShatterOnSlide && IsSlideNote(note)) return;
        // The Hold path and the ordinary hit path can both reach here for the
        // same note; only the first one gets to break it.
        if (HasTransientMagicFor(note)) return;

        ComputeHoldMagicTransform(note, laneId, out var position, out var rotation);
        float visualSize = Mathf.Max(0.25f, ComputeNoteWidth(note) * 1.15f);
        ResolveHoldBackgroundSorting(note, out int sortingLayerId, out int sortingOrder);
        bool rightHand = ResolveIsRightHand(note, laneId);
        float lifetime = Mathf.Max(0.05f, magicShatterLifetime);
        float spin = Mathf.Max(0f, magicShatterSpinScale);
        // Room for a shard to slide out on both sides of the quad.
        float padding = 1f + 2f * Mathf.Max(0f, magicShatterSpread);

        AddTransientMagicLayer("Effects/HoldMagic/hold_magic_outer_v2",
            "MagicShatter_Outer_Ornate", -Mathf.Abs(holdFlowerDegreesPerSecond) * spin,
            position, rotation, visualSize * 1.025f, rightHand, 0.82f, 0f,
            sortingLayerId, sortingOrder + 13, 1f, padding, lifetime, note);
        AddTransientMagicLayer("Effects/HoldMagic/hold_magic_triangle_v2",
            "MagicShatter_Triangle_Ornate", Mathf.Abs(holdPinDegreesPerSecond) * spin,
            position, rotation, visualSize * 0.83f, rightHand, 0.90f, 17f,
            sortingLayerId, sortingOrder + 14, 0.83f / 1.025f, padding, lifetime, note);
        AddTransientMagicLayer("Effects/HoldMagic/hold_magic_inner_v2",
            "MagicShatter_InnerSeal_Ornate", -Mathf.Abs(holdFlowerDegreesPerSecond) * 0.65f * spin,
            position, rotation, visualSize * 0.65f, rightHand, 0.84f, -11f,
            sortingLayerId, sortingOrder + 15, 0.65f / 1.025f, padding, lifetime, note);
    }

    private void AddTransientMagicLayer(string resourcePath, string objectName, float speed,
        Vector3 position, Quaternion rotation, float visualSize, bool rightHand, float opacity,
        float initialAngle, int sortingLayerId, int sortingOrder, float radialScale,
        float uvZoom, float lifetime, NoteController owner)
    {
        HoldRotatingEffect effect = AddHoldSpriteEffect(null, resourcePath, objectName, speed,
            position, rotation, visualSize, rightHand, opacity, initialAngle,
            sortingLayerId, sortingOrder, radialScale, uvZoom);
        if (effect == null) return;
        effect.Lifetime = lifetime;
        effect.Owner = owner;
        // Re-seeded through the transient branch of the curve, which the
        // persistent defaults in AddHoldSpriteEffect could not have used.
        EvaluateMagicEffect(effect, out float alpha, out _, out float spread);
        ApplyMagicEffectFrame(effect, position, rotation, alpha, spread);
        transientMagicEffects.Add(effect);
    }

    private bool HasTransientMagicFor(NoteController note)
    {
        for (int i = 0; i < transientMagicEffects.Count; i++)
        {
            HoldRotatingEffect effect = transientMagicEffects[i];
            if (effect != null && effect.Owner == note) return true;
        }
        return false;
    }

    /// <summary>
    /// Applies the Display setting immediately. Turning the magic circle off only
    /// removes its decorative sprite/core hierarchy; Hold judgment particles keep
    /// running and StopHoldRotation still owns their eventual shutdown.
    /// </summary>
    public void SetHoldMagicCircleVisible(bool visible)
    {
        if (!visible) ClearHoldEffects();
    }

    private bool IsHoldMagicCircleVisible()
    {
        if (!enableHoldRotation) return false;
        SettingsManager settings = SettingsManager.Instance;
        return settings == null || settings.HoldMagicCircleEnabled;
    }

    public void StopHoldRotation(NoteController note)
    {
        if (ReferenceEquals(note, null)) return;
        try { note.StopJudgmentLineFanParticles(); } catch { }

        if (activeHoldEffects.TryGetValue(note, out var effects))
        {
            for (int i = 0; i < effects.Count; i++) ReleaseMagicEffect(effects[i]);
            activeHoldEffects.Remove(note);
        }

        if (activeHoldContactCores.TryGetValue(note, out HoldRotatingEffect core))
        {
            ReleaseMagicEffect(core);
            activeHoldContactCores.Remove(note);
        }
    }

    private void ClearHoldEffects()
    {
        foreach (var pair in activeHoldEffects)
        {
            List<HoldRotatingEffect> effects = pair.Value;
            for (int i = 0; i < effects.Count; i++) ReleaseMagicEffect(effects[i]);
        }
        activeHoldEffects.Clear();

        foreach (var pair in activeHoldContactCores) ReleaseMagicEffect(pair.Value);
        activeHoldContactCores.Clear();

        for (int i = 0; i < transientMagicEffects.Count; i++)
            ReleaseMagicEffect(transientMagicEffects[i]);
        transientMagicEffects.Clear();
    }

    /// <summary>Removes effects that belong to the chart being unloaded.</summary>
    public void ClearAllSongEffects()
    {
        ClearHoldEffects();
    }

    /// <summary>
    /// Hands out a magic-circle sprite object, building one only when the pool
    /// is dry. Ordinary notes now open a circle too, so a chart can ask for
    /// several of these per beat; allocating a GameObject each time is exactly
    /// the kind of churn <see cref="PrewarmHoldAssets"/> exists to avoid.
    /// </summary>
    private GameObject RentMagicSprite(string objectName, out SpriteRenderer renderer)
    {
        while (magicSpritePool.Count > 0)
        {
            GameObject pooled = magicSpritePool.Dequeue();
            if (pooled == null) continue;
            renderer = pooled.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                Destroy(pooled);
                continue;
            }
            pooled.name = objectName;
            pooled.SetActive(true);
            return pooled;
        }

        var go = new GameObject(objectName);
        go.transform.SetParent(poolRoot, false);
        renderer = go.AddComponent<SpriteRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    private void ReturnMagicSprite(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        if (magicSpritePool.Count >= Mathf.Max(0, magicSpritePoolCap))
        {
            Destroy(go);
            return;
        }
        go.transform.SetParent(poolRoot, false);
        magicSpritePool.Enqueue(go);
    }

    private void ReleaseMagicEffect(HoldRotatingEffect effect)
    {
        if (effect == null) return;
        ReturnMagicSprite(effect.Object);
        effect.Object = null;
        effect.Renderer = null;
        effect.Owner = null;
    }

    /// <summary>
    /// Contact shatters the seal: the wedges slide apart, quickly at first and
    /// then ever slower, while the whole thing fades. A Hold's persistent
    /// circle is the other case -- it does not shatter, it just fades in
    /// underneath the burst and eases up to speed.
    /// </summary>
    private void EvaluateMagicEffect(HoldRotatingEffect effect, out float alpha,
        out float spin, out float spread)
    {
        if (effect.Lifetime > 0f)
        {
            float u = Mathf.Clamp01(effect.Age / effect.Lifetime);
            float inv = 1f - u;
            // Ease-out cubic: nearly all of the separation happens in the first
            // third, so the break reads as a snap that then drifts to a stop.
            spread = magicShatterSpread * (1f - inv * inv * inv);
            alpha = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(Mathf.Clamp01(magicShatterFadeStart), 1f, u));
            spin = 1f;
            return;
        }

        spread = 0f;
        float fade = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(effect.Age / Mathf.Max(0.001f, holdMagicFadeInTime)));
        alpha = fade;
        spin = fade;
    }

    private static void ApplyMagicEffectFrame(HoldRotatingEffect effect, Vector3 position,
        Quaternion rotation, float alpha, float spread)
    {
        Transform t = effect.Object.transform;
        t.SetPositionAndRotation(position,
            rotation * Quaternion.AngleAxis(effect.Angle, Vector3.forward));
        // The seal keeps its size throughout. What grows is the distance
        // between its pieces, and that lives in the shader.
        t.localScale = effect.BaseScale;

        if (effect.Renderer == null) return;

        float target = effect.Opacity * alpha;
        if (Mathf.Abs(target - effect.LastAppliedAlpha) > 0.002f)
        {
            effect.Renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(target));
            effect.LastAppliedAlpha = target;
        }

        if (Mathf.Abs(spread - effect.LastAppliedSpread) > 0.0005f)
        {
            sharedMagicBlock ??= new MaterialPropertyBlock();
            // Reads the renderer's existing block first, so the palette this
            // effect was given survives the per-frame spread write.
            effect.Renderer.GetPropertyBlock(sharedMagicBlock);
            sharedMagicBlock.SetFloat(HoldShardSpreadId, spread);
            effect.Renderer.SetPropertyBlock(sharedMagicBlock);
            effect.LastAppliedSpread = spread;
        }
    }

    private HoldRotatingEffect AddHoldSpriteEffect(List<HoldRotatingEffect> effects, string resourcePath,
        string objectName, float speed, Vector3 position, Quaternion rotation, float visualSize,
        bool rightHand, float opacity, float initialAngle, int sortingLayerId, int sortingOrder,
        float radialScale, float uvZoom)
    {
        Sprite sprite = LoadHoldSprite(resourcePath);
        if (sprite == null) return null;

        GameObject go = RentMagicSprite(objectName, out SpriteRenderer renderer);
        renderer.sprite = sprite;
        renderer.sharedMaterial = GetOrCreateHoldMagicForegroundMaterial();
        ApplyHoldMagicPalette(renderer, rightHand, opacity, radialScale, 0f,
            magicShatterShards, uvZoom);
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = sortingOrder;

        float spriteWidth = Mathf.Max(0.001f, sprite.bounds.size.x);
        // The quad is grown by the padding and the shader zooms the UV back by
        // the same factor, so the art stays this size while the shards gain
        // room to slide into.
        float scale = visualSize * Mathf.Max(1f, uvZoom) / spriteWidth;
        var effect = new HoldRotatingEffect
        {
            Object = go,
            Renderer = renderer,
            DegreesPerSecond = speed,
            Angle = initialAngle,
            BaseScale = new Vector3(scale, scale, 1f),
            Opacity = opacity,
            LastAppliedAlpha = opacity,
            LastAppliedSpread = 0f,
            Position = position,
            Rotation = rotation
        };
        EvaluateMagicEffect(effect, out float startAlpha, out _, out float startSpread);
        ApplyMagicEffectFrame(effect, position, rotation, startAlpha, startSpread);
        effects?.Add(effect);
        return effect;
    }

    private void AddHoldMagicLayer(List<HoldRotatingEffect> effects, int layer,
        string objectName, float speed, Vector3 position, Quaternion rotation,
        float visualSize, Color theme, int sortingLayerId, int sortingOrder)
    {
        Sprite sprite = GetOrCreateHoldMagicLayerSprite(layer);
        if (sprite == null) return;

        var go = new GameObject(objectName);
        go.transform.SetParent(poolRoot, false);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = GetOrCreateHoldMagicForegroundMaterial();
        theme.a = layer == 0 ? 0.82f : layer == 1 ? 0.92f : 0.78f;
        renderer.color = theme;
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        float scale = visualSize / Mathf.Max(0.001f, sprite.bounds.size.x);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        go.transform.SetPositionAndRotation(position, rotation);
        effects.Add(new HoldRotatingEffect
        {
            Object = go,
            DegreesPerSecond = speed,
            Angle = layer * 17f
        });
    }

    private void AddHoldMagicGeometryLayer(List<HoldRotatingEffect> effects, int layer,
        string objectName, float speed, Vector3 position, Quaternion rotation,
        float visualSize, Color theme, int sortingLayerId, int sortingOrder)
    {
        var root = new GameObject(objectName);
        root.transform.SetParent(poolRoot, false);
        Material material = GetOrCreateHoldMagicGeometryMaterial();
        float lineWidth = Mathf.Max(0.018f, visualSize * 0.018f);
        theme.a = layer == 0 ? 0.84f : layer == 1 ? 0.94f : 0.80f;

        if (layer == 0)
        {
            // Counter-clockwise outer mechanism: three interrupted arcs, a
            // second inset orbit, and three outward alchemical pointers.
            for (int sector = 0; sector < 3; sector++)
            {
                float centre = 90f + sector * 120f;
                CreateMagicLine(root.transform, $"OuterArc_{sector}",
                    BuildArc(visualSize * 0.48f, centre - 48f, centre + 48f, 28), false,
                    lineWidth, theme, material, sortingLayerId, sortingOrder);
                CreateMagicLine(root.transform, $"InnerArc_{sector}",
                    BuildArc(visualSize * 0.415f, centre - 43f, centre + 43f, 24), false,
                    lineWidth * 0.52f, WithAlpha(theme, 0.62f), material,
                    sortingLayerId, sortingOrder);

                float radians = centre * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
                Vector3 side = new Vector3(-dir.y, dir.x, 0f);
                Vector3[] marker =
                {
                    dir * (visualSize * 0.54f),
                    dir * (visualSize * 0.43f) + side * (visualSize * 0.055f),
                    dir * (visualSize * 0.43f) - side * (visualSize * 0.055f)
                };
                CreateMagicLine(root.transform, $"Pointer_{sector}", marker, true,
                    lineWidth * 0.82f, theme, material, sortingLayerId, sortingOrder + 1);
            }
        }
        else if (layer == 1)
        {
            // Clockwise ornate heart: opposed triangles, a circular seal and
            // three double-ring nodes at the primary triangle vertices.
            CreateMagicLine(root.transform, "PrimaryTriangle",
                BuildPolygon(3, visualSize * 0.46f, 90f), true,
                lineWidth, theme, material, sortingLayerId, sortingOrder);
            CreateMagicLine(root.transform, "InverseTriangle",
                BuildPolygon(3, visualSize * 0.35f, -90f), true,
                lineWidth * 0.72f, WithAlpha(theme, 0.76f), material,
                sortingLayerId, sortingOrder);
            CreateMagicLine(root.transform, "TriangleOrbit",
                BuildCircle(visualSize * 0.37f, 64, Vector3.zero), true,
                lineWidth * 0.54f, WithAlpha(theme, 0.66f), material,
                sortingLayerId, sortingOrder);
            for (int node = 0; node < 3; node++)
            {
                float radians = (90f + node * 120f) * Mathf.Deg2Rad;
                Vector3 centre = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) *
                                 (visualSize * 0.37f);
                CreateMagicLine(root.transform, $"Seal_{node}",
                    BuildCircle(visualSize * 0.052f, 24, centre), true,
                    lineWidth * 0.70f, theme, material, sortingLayerId, sortingOrder + 1);
                CreateMagicLine(root.transform, $"SealCore_{node}",
                    BuildCircle(visualSize * 0.022f, 18, centre), true,
                    lineWidth * 0.42f, WithAlpha(theme, 0.72f), material,
                    sortingLayerId, sortingOrder + 1);
            }
        }
        else
        {
            // Slow inner rune plate: twelve-lobed rosette, script-like rings and
            // a hexagonal centre seal.
            CreateMagicLine(root.transform, "Rosette",
                BuildRosette(12, visualSize * 0.29f, visualSize * 0.045f, 96), true,
                lineWidth * 0.72f, theme, material, sortingLayerId, sortingOrder);
            CreateMagicLine(root.transform, "ScriptRingOuter",
                BuildCircle(visualSize * 0.23f, 72, Vector3.zero), true,
                lineWidth * 0.50f, WithAlpha(theme, 0.82f), material,
                sortingLayerId, sortingOrder);
            CreateMagicLine(root.transform, "ScriptRingInner",
                BuildCircle(visualSize * 0.145f, 60, Vector3.zero), true,
                lineWidth * 0.42f, WithAlpha(theme, 0.70f), material,
                sortingLayerId, sortingOrder);
            CreateMagicLine(root.transform, "CentreHex",
                BuildPolygon(6, visualSize * 0.18f, 30f), true,
                lineWidth * 0.60f, theme, material, sortingLayerId, sortingOrder + 1);
        }

        root.transform.SetPositionAndRotation(position, rotation);
        effects.Add(new HoldRotatingEffect
        {
            Object = root,
            DegreesPerSecond = speed,
            Angle = layer * 11f
        });
    }

    private static void CreateMagicLine(Transform parent, string name, Vector3[] points,
        bool loop, float width, Color color, Material material,
        int sortingLayerId, int sortingOrder)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        var line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.widthMultiplier = width;
        line.sharedMaterial = material;
        line.startColor = color;
        line.endColor = color;
        line.sortingLayerID = sortingLayerId;
        line.sortingOrder = sortingOrder;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private static Vector3[] BuildArc(float radius, float startDegrees,
        float endDegrees, int segments)
    {
        segments = Mathf.Max(2, segments);
        var points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments) * Mathf.Deg2Rad;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }
        return points;
    }

    private static Vector3[] BuildCircle(float radius, int segments, Vector3 centre)
    {
        segments = Mathf.Max(8, segments);
        var points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            points[i] = centre + new Vector3(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius, 0f);
        }
        return points;
    }

    private static Vector3[] BuildPolygon(int sides, float radius, float rotationDegrees)
    {
        sides = Mathf.Max(3, sides);
        var points = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = (rotationDegrees + i * 360f / sides) * Mathf.Deg2Rad;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }
        return points;
    }

    private static Vector3[] BuildRosette(int lobes, float radius,
        float amplitude, int segments)
    {
        segments = Mathf.Max(24, segments);
        var points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float r = radius + Mathf.Cos(angle * lobes) * amplitude;
            points[i] = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
        }
        return points;
    }

    private static Color WithAlpha(Color color, float multiplier)
    {
        color.a *= Mathf.Clamp01(multiplier);
        return color;
    }

    private void CreateHoldContactCore(NoteController note, Vector3 position, Quaternion rotation,
        float visualWidth, bool rightHand, int sortingLayerId, int sortingOrder)
    {
        Sprite sprite = GetOrCreateHoldContactCoreSprite();
        if (sprite == null) return;

        const float coreOpacity = 0.72f;
        GameObject go = RentMagicSprite("HoldContactCore_Foreground", out SpriteRenderer renderer);
        renderer.sprite = sprite;
        renderer.sharedMaterial = GetOrCreateHoldContactCoreMaterial();
        // A small bias lifts the core clear of the deepest theme colour without
        // pushing it into the gilding, which belongs to the rim. Its own material
        // keeps the glow low enough that the seal's engraving still reads through
        // the swell rather than being blown out by it.
        ApplyHoldMagicPalette(renderer, rightHand, coreOpacity, 1f, 0.15f,
            magicShatterShards, 1f);
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = sortingOrder;

        float targetWidth = Mathf.Max(0.35f, visualWidth * 1.18f);
        float targetHeight = Mathf.Max(0.45f, note.GetCurrentWorldHeight() * 1.45f);
        var effect = new HoldRotatingEffect
        {
            Object = go,
            Renderer = renderer,
            DegreesPerSecond = 0f,
            BaseScale = new Vector3(
                targetWidth / Mathf.Max(0.001f, sprite.bounds.size.x),
                targetHeight / Mathf.Max(0.001f, sprite.bounds.size.y),
                1f),
            Opacity = coreOpacity,
            LastAppliedAlpha = coreOpacity,
            Position = position,
            Rotation = rotation,
            Owner = note
        };
        // Fades in on the same curve as the rings, so the whole seal arrives
        // as one object rather than a core that is already lit.
        EvaluateMagicEffect(effect, out float coreAlpha, out _, out float coreSpread);
        ApplyMagicEffectFrame(effect, position, rotation, coreAlpha, coreSpread);
        activeHoldContactCores[note] = effect;
    }

    /// <summary>
    /// Supplies one layer's slice of the seal's shared colour ramp.
    /// <paramref name="radialScale"/> is this sprite's size relative to the
    /// outermost ring, so a layer drawn at 65% of the circle only spans the
    /// inner 65% of the gradient instead of re-running the whole ramp inside
    /// its own quad. <paramref name="radialBias"/> pushes a layer towards the
    /// luminous end regardless of where it sits.
    /// </summary>
    private static void ApplyHoldMagicPalette(SpriteRenderer renderer, bool redSide, float opacity,
        float radialScale, float radialBias, int shardCount, float uvZoom)
    {
        if (renderer == null) return;
        // Both hands share one gilded rim; only the body of the seal carries the
        // hand colour. Neither end sits near black, because under SrcAlpha
        // blending a dark ink subtracts light from the track instead of glowing,
        // which is what turned the middle of the seal into a muddy smear.
        Color inner = redSide
            ? new Color(1.05f, 0.11f, 0.09f, 1f)
            : new Color(0.10f, 0.40f, 1.05f, 1f);
        // The theme highlight keeps a strong hue cast but carries the other two
        // channels as well, so the brightest strokes in the body have somewhere
        // to go. The old red highlight was almost pure red and clipped flat.
        Color coreHighlight = redSide
            ? new Color(1.55f, 0.52f, 0.32f, 1f)
            : new Color(0.55f, 0.88f, 1.55f, 1f);
        // Shared by both hands: the theme colour hands over to this at the rim.
        Color gilding = new Color(1.58f, 0.98f, 0.26f, 1f);
        Color gildingHighlight = new Color(1.80f, 1.28f, 0.48f, 1f);
        if (NextSealIsPrecise)
        {
            // 紫的邊，但**亮度和金的一樣**。這兩組是照亮度（0.2126/0.7152/0.0722）
            // 算過再縮回去的：金 1.056、亮金 1.333，紫的也一樣。不這樣做的話，
            // 換個色相就等於換了曝光，泛光的門檻也跟著跑掉。
            gilding = new Color(1.35f, 0.91f, 1.58f, 1f);
            gildingHighlight = new Color(1.70f, 1.16f, 1.99f, 1f);
        }

        // RGB is supplied explicitly through the material block. Only alpha is
        // left in the SpriteRenderer vertex colour, avoiding pipelines that drop
        // or whiten SpriteRenderer tint before the custom fragment shader.
        renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor(HoldInnerColorId, inner);
        block.SetColor(HoldOuterColorId, gilding);
        block.SetColor(HoldCoreHighlightColorId, coreHighlight);
        block.SetColor(HoldPlatinumColorId, gildingHighlight);
        block.SetFloat(HoldRadialScaleId, Mathf.Max(0.05f, radialScale));
        block.SetFloat(HoldRadialBiasId, radialBias);
        block.SetFloat(HoldShardCountId, Mathf.Max(3, shardCount));
        block.SetFloat(HoldUvZoomId, Mathf.Max(1f, uvZoom));
        // Reset explicitly: these renderers come from a pool, and a block left
        // over from a burst would otherwise shatter a resting Hold circle.
        block.SetFloat(HoldShardSpreadId, 0f);
        renderer.SetPropertyBlock(block);
    }

    /// <summary>
    /// Resolves a note's hand the way the note resolves its own art: from
    /// <c>NoteData.hand</c> (0 is the right hand). The lane index is only a
    /// fallback for callers with no note data.
    /// </summary>
    /// <remarks>
    /// The lane index is not a substitute for this. It agrees with the hand
    /// only while the hands stay on their own halves of the board — the moment
    /// a passage crosses them, a lane-derived colour contradicts the note it is
    /// drawn behind.
    /// </remarks>
    private static bool IsSlideNote(NoteController note)
    {
        try
        {
            return note != null && note.IsSlide;
        }
        catch
        {
            return false;
        }
    }

    private bool ResolveIsRightHand(NoteController note, int laneId)
    {
        try
        {
            if (note != null && note.NoteData != null) return note.NoteData.hand == 0;
        }
        catch { }

        if (laneId < 0) return false;
        return laneId >= Mathf.Max(1, rightLaneStartIndex);
    }

    private static Material GetOrCreateHoldMagicForegroundMaterial()
    {
        if (holdMagicForegroundMaterial != null) return holdMagicForegroundMaterial;
        Shader shader = Shader.Find("Nostalgia/HoldMagicOverlay");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        holdMagicForegroundMaterial = new Material(shader)
        {
            name = "Hold Magic Foreground (Runtime)",
            renderQueue = 4200,
            hideFlags = HideFlags.DontSave
        };
        if (holdMagicForegroundMaterial.HasProperty("_Glow"))
            // Above 1 on purpose. Alpha blending puts rgb * alpha in the
            // framebuffer, so an HDR colour authored at 1.58 lands at ~1.4
            // on its most opaque pixels and below the bloom threshold
            // everywhere else -- the glow was being authored and then
            // thrown away at the blend.
            holdMagicForegroundMaterial.SetFloat("_Glow", 1.90f);
        if (holdMagicForegroundMaterial.HasProperty("_GradientStrength"))
            holdMagicForegroundMaterial.SetFloat("_GradientStrength", 1.00f);
        // Driven by stroke coverage rather than the artwork's RGB, which is a
        // flat ivory across the whole sheet and carried no engraving signal.
        if (holdMagicForegroundMaterial.HasProperty("_StrokeHighlight"))
            holdMagicForegroundMaterial.SetFloat("_StrokeHighlight", 0.20f);
        if (holdMagicForegroundMaterial.HasProperty("_CoreBoost"))
            holdMagicForegroundMaterial.SetFloat("_CoreBoost", 0.62f);
        if (holdMagicForegroundMaterial.HasProperty("_FeatherFloor"))
            holdMagicForegroundMaterial.SetFloat("_FeatherFloor", 0.38f);
        if (holdMagicForegroundMaterial.HasProperty("_PulseStrength"))
            holdMagicForegroundMaterial.SetFloat("_PulseStrength", 0.05f);
        return holdMagicForegroundMaterial;
    }

    private static Material GetOrCreateHoldContactCoreMaterial()
    {
        if (holdMagicContactCoreMaterial != null) return holdMagicContactCoreMaterial;
        Shader shader = Shader.Find("Nostalgia/HoldMagicOverlay");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        holdMagicContactCoreMaterial = new Material(shader)
        {
            name = "Hold Magic Contact Core (Runtime)",
            renderQueue = 4200,
            hideFlags = HideFlags.DontSave
        };
        // The core is a soft blob, not line work: the stroke-core heat and the
        // feather rolloff that give the engraving its relief would just turn it
        // into a white hole punched through the middle of the seal.
        if (holdMagicContactCoreMaterial.HasProperty("_Glow"))
            holdMagicContactCoreMaterial.SetFloat("_Glow", 1.15f);
        if (holdMagicContactCoreMaterial.HasProperty("_GradientStrength"))
            holdMagicContactCoreMaterial.SetFloat("_GradientStrength", 1.00f);
        if (holdMagicContactCoreMaterial.HasProperty("_StrokeHighlight"))
            holdMagicContactCoreMaterial.SetFloat("_StrokeHighlight", 0.32f);
        if (holdMagicContactCoreMaterial.HasProperty("_CoreBoost"))
            holdMagicContactCoreMaterial.SetFloat("_CoreBoost", 0.12f);
        if (holdMagicContactCoreMaterial.HasProperty("_FeatherFloor"))
            holdMagicContactCoreMaterial.SetFloat("_FeatherFloor", 0f);
        if (holdMagicContactCoreMaterial.HasProperty("_PulseStrength"))
            holdMagicContactCoreMaterial.SetFloat("_PulseStrength", 0.05f);
        // No sweep on the core. The sheen is a highlight running along engraved
        // line work; on a soft blob it just swings the whole thing brighter and
        // darker.
        if (holdMagicContactCoreMaterial.HasProperty("_SheenStrength"))
            holdMagicContactCoreMaterial.SetFloat("_SheenStrength", 0f);
        return holdMagicContactCoreMaterial;
    }

    private static Material GetOrCreateHoldMagicGeometryMaterial()
    {
        if (holdMagicGeometryMaterial != null) return holdMagicGeometryMaterial;
        Shader shader = Shader.Find("Nostalgia/HoldMagicOverlay");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;
        holdMagicGeometryMaterial = new Material(shader)
        {
            name = "Hold Magic Geometry (Runtime)",
            renderQueue = 4210,
            hideFlags = HideFlags.DontSave
        };
        if (holdMagicGeometryMaterial.HasProperty("_MainTex"))
            holdMagicGeometryMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
        if (holdMagicGeometryMaterial.HasProperty("_Glow"))
            holdMagicGeometryMaterial.SetFloat("_Glow", 1.45f);
        return holdMagicGeometryMaterial;
    }

    private static Sprite GetOrCreateHoldMagicLayerSprite(int layer)
    {
        layer = Mathf.Clamp(layer, 0, holdMagicLayerSprites.Length - 1);
        if (holdMagicLayerSprites[layer] != null) return holdMagicLayerSprites[layer];

        const int size = 512;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = $"Hold Magic Circle Layer {layer} (Runtime)",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float ny = ((y + 0.5f) / size - 0.5f) * 2f;
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size - 0.5f) * 2f;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float angle = Mathf.Atan2(ny, nx);
                float alpha = 0f;
                Vector2 point = new Vector2(nx, ny);

                if (layer == 0)
                {
                    // Three interrupted orbital bands, fine radial ticks and
                    // outward alchemical pointers. This is the slow CCW frame.
                    float arcGate = Mathf.SmoothStep(0.08f, 0.22f,
                        Mathf.Abs(Mathf.Sin(angle * 1.5f)));
                    alpha = Mathf.Max(alpha, SoftRing(radius, 0.92f, 0.010f) * arcGate);
                    alpha = Mathf.Max(alpha, SoftRing(radius, 0.84f, 0.006f) * arcGate * 0.72f);
                    float ticks = 1f - Mathf.SmoothStep(0.025f, 0.065f,
                        Mathf.Abs(Mathf.Sin(angle * 24f)));
                    if (radius > 0.86f && radius < 0.985f)
                        alpha = Mathf.Max(alpha, ticks * 0.82f);
                    for (int marker = 0; marker < 3; marker++)
                    {
                        float markerAngle = Mathf.PI * 0.5f + marker * Mathf.PI * 2f / 3f;
                        Vector2 dir = new Vector2(Mathf.Cos(markerAngle), Mathf.Sin(markerAngle));
                        Vector2 side = new Vector2(-dir.y, dir.x);
                        Vector2 tip = dir * 0.995f;
                        Vector2 left = dir * 0.82f + side * 0.095f;
                        Vector2 right = dir * 0.82f - side * 0.095f;
                        alpha = Mathf.Max(alpha, TriangleEdgeAlpha(point, tip, left, right, 0.010f));
                    }
                }
                else if (layer == 1)
                {
                    // Counterpart to the reference's ornate central triangle:
                    // two opposed triangles, an orbit and three circular seals.
                    alpha = Mathf.Max(alpha, RegularPolygonEdgeAlpha(point, 3, 0.76f,
                        Mathf.PI * 0.5f, 0.010f));
                    alpha = Mathf.Max(alpha, RegularPolygonEdgeAlpha(point, 3, 0.58f,
                        -Mathf.PI * 0.5f, 0.008f) * 0.82f);
                    alpha = Mathf.Max(alpha, SoftRing(radius, 0.64f, 0.007f) * 0.72f);
                    for (int node = 0; node < 3; node++)
                    {
                        float nodeAngle = Mathf.PI * 0.5f + node * Mathf.PI * 2f / 3f;
                        Vector2 centre = new Vector2(Mathf.Cos(nodeAngle), Mathf.Sin(nodeAngle)) * 0.64f;
                        float nodeRadius = Vector2.Distance(point, centre);
                        alpha = Mathf.Max(alpha, SoftRing(nodeRadius, 0.082f, 0.009f));
                        alpha = Mathf.Max(alpha, SoftRing(nodeRadius, 0.035f, 0.005f) * 0.72f);
                    }
                }
                else
                {
                    // Dense inner seal: rosette, concentric script bands and
                    // twelve short rays. It turns more slowly beneath the triangle.
                    float rosette = 0.43f + Mathf.Cos(angle * 12f) * 0.065f;
                    alpha = Mathf.Max(alpha, SoftRing(radius, rosette, 0.010f));
                    alpha = Mathf.Max(alpha, SoftRing(radius, 0.34f, 0.006f) * 0.88f);
                    alpha = Mathf.Max(alpha, SoftRing(radius, 0.23f, 0.008f) * 0.82f);
                    alpha = Mathf.Max(alpha, RegularPolygonEdgeAlpha(point, 6, 0.30f,
                        Mathf.PI / 6f, 0.006f) * 0.74f);
                    float rays = 1f - Mathf.SmoothStep(0.018f, 0.052f,
                        Mathf.Abs(Mathf.Sin(angle * 12f)));
                    if (radius > 0.47f && radius < 0.61f)
                        alpha = Mathf.Max(alpha, rays * 0.88f);
                    float core = Mathf.Pow(Mathf.Clamp01(1f - radius / 0.19f), 2.2f);
                    alpha = Mathf.Max(alpha, core * 0.36f);
                }

                float edgeFade = 1f - Mathf.SmoothStep(0.97f, 1.01f, radius);
                byte alphaByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha * edgeFade) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alphaByte);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        sprite.name = $"Hold Magic Circle Layer {layer}";
        holdMagicLayerSprites[layer] = sprite;
        return sprite;
    }

    private static float SoftRing(float radius, float target, float halfWidth)
    {
        return 1f - Mathf.SmoothStep(halfWidth, halfWidth * 2.2f,
            Mathf.Abs(radius - target));
    }

    private static float SegmentAlpha(Vector2 point, Vector2 a, Vector2 b, float halfWidth)
    {
        Vector2 ab = b - a;
        float denominator = Mathf.Max(0.000001f, Vector2.Dot(ab, ab));
        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
        float distance = Vector2.Distance(point, a + ab * t);
        return 1f - Mathf.SmoothStep(halfWidth, halfWidth * 2.2f, distance);
    }

    private static float TriangleEdgeAlpha(Vector2 point, Vector2 a, Vector2 b,
        Vector2 c, float halfWidth)
    {
        return Mathf.Max(SegmentAlpha(point, a, b, halfWidth),
            Mathf.Max(SegmentAlpha(point, b, c, halfWidth),
                SegmentAlpha(point, c, a, halfWidth)));
    }

    private static float RegularPolygonEdgeAlpha(Vector2 point, int sides,
        float radius, float rotation, float halfWidth)
    {
        float alpha = 0f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = rotation + i * Mathf.PI * 2f / sides;
            float a1 = rotation + (i + 1) * Mathf.PI * 2f / sides;
            Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            alpha = Mathf.Max(alpha, SegmentAlpha(point, p0, p1, halfWidth));
        }
        return alpha;
    }

    private static Sprite GetOrCreateHoldContactCoreSprite()
    {
        if (holdContactCoreSprite != null) return holdContactCoreSprite;

        const int width = 128;
        const int height = 64;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Hold Contact Platinum Core (Runtime)",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[width * height];
        Color edgeGold = new Color(0.78f, 0.88f, 1f, 1f);
        for (int y = 0; y < height; y++)
        {
            float ny = ((y + 0.5f) / height - 0.5f) * 2f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((x + 0.5f) / width - 0.5f) * 2f;
                float radial = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny)), 1.35f);
                float streak = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(ny)), 7f) *
                               Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(nx)), 0.45f);
                float coreDistance = Mathf.Sqrt(nx * nx * 2.6f + ny * ny * 5.2f);
                float core = Mathf.Pow(Mathf.Clamp01(1f - coreDistance), 0.55f);
                float alpha = Mathf.Clamp01(Mathf.Max(radial * 0.9f, streak * 0.82f) + core * 0.72f);
                Color color = Color.Lerp(edgeGold, Color.white, Mathf.Clamp01(core + streak * 0.55f));
                color.a = alpha;
                pixels[y * width + x] = color;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        holdContactCoreSprite = Sprite.Create(texture, new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f), 100f);
        holdContactCoreSprite.name = "Hold Contact Platinum Core";
        return holdContactCoreSprite;
    }

    private void ResolveHoldBackgroundSorting(NoteController note, out int sortingLayerId,
        out int sortingOrder)
    {
        sortingLayerId = 0;
        sortingOrder = Mathf.Max(1, foregroundSortingOrder - 1);
        if (note == null) return;

        Renderer noteVisual = null;
        Transform runtimeSprite = note.transform.Find("RuntimeSprite");
        if (runtimeSprite != null) noteVisual = runtimeSprite.GetComponent<Renderer>();
        if (noteVisual == null) noteVisual = note.GetComponent<Renderer>();
        if (noteVisual == null) noteVisual = note.GetComponentInChildren<Renderer>();
        if (noteVisual == null) return;

        sortingLayerId = noteVisual.sortingLayerID;
        // One order behind the note keeps the held overlay under the note head,
        // while the note's own +2 judgment-line offset keeps it above TRACK.
        sortingOrder = noteVisual.sortingOrder - 1;
    }

    /// <summary>
    /// The Hold magic-circle sprites, in the order they are layered.
    /// </summary>
    /// <remarks>
    /// A table rather than three literals at the call site so
    /// <see cref="PrewarmHoldAssets"/> cannot warm a path the effect no longer
    /// uses — a silently stale prewarm looks exactly like no prewarm at all.
    /// </remarks>
    private static readonly string[] HoldMagicSpritePaths =
    {
        "Effects/HoldMagic/hold_magic_outer_v2",
        "Effects/HoldMagic/hold_magic_triangle_v2",
        "Effects/HoldMagic/hold_magic_inner_v2",
    };

    /// <summary>
    /// Builds the Hold effect's runtime assets before anything is played.
    /// </summary>
    /// <remarks>
    /// Everything the magic circle needs was created on the first Hold contact:
    /// three 1254×1254 PNGs pulled in with <c>Resources.LoadAll</c> (decode plus
    /// GPU upload, ~6 MB), the procedural contact-core texture, and two runtime
    /// materials. All of it on the main thread, at the exact moment the player
    /// touches the first Hold note — which is felt as the game freezing on that
    /// one note and then never again.
    ///
    /// One asset per frame, starting as the scene loads: the work is the same,
    /// it just happens while there is nothing to interrupt.
    /// </remarks>
    private System.Collections.IEnumerator PrewarmHoldAssets()
    {
        // Let the scene finish coming up first — this is background work.
        yield return null;

        for (int i = 0; i < HoldMagicSpritePaths.Length; i++)
        {
            LoadHoldSprite(HoldMagicSpritePaths[i]);
            yield return null;
        }

        GetOrCreateHoldMagicForegroundMaterial();
        yield return null;
        GetOrCreateHoldContactCoreSprite();
        GetOrCreateHoldContactCoreMaterial();
        yield return null;

        // The rune atlas is rasterised on the CPU -- 24 glyphs of 96x96, each
        // pixel measured against every stroke. Cheap once, but not on the frame
        // the first note lands.
        try { RuneBurstEmitter.EnsureCreated().Prewarm(); } catch { }
        yield return null;

        // Ordinary notes open a circle too, so the first bar of a chart would
        // otherwise build a dozen GameObjects mid-play. Four notes' worth is
        // enough to cover the opening chord; the pool grows from there.
        int prewarmCount = Mathf.Clamp(magicSpritePoolCap, 0, 16);
        for (int i = 0; i < prewarmCount; i++)
        {
            ReturnMagicSprite(RentMagicSprite("MagicCircleLayer", out _));
            if ((i & 3) == 3) yield return null;
        }
    }

    /// <summary>
    /// Hold 特效用的執行期材質，交給 shader 預熱去畫一次。
    /// </summary>
    /// <remarks>
    /// 預熱是靠走訪現有的 Renderer 蒐集材質的，而這幾個材質在第一次碰到 Hold
    /// 之前**不掛在任何 Renderer 上**——承載它們的 SpriteRenderer 是那時候才建的。
    /// 所以掃描看不到它們，它們也就變成掃描之後唯一還會第一次被畫到的東西。
    ///
    /// <see cref="PrewarmHoldAssets"/> 已經把它們建出來了，這裡只是把它們交出去
    /// 讓人畫一次。
    /// </remarks>
    public static void CollectWarmupMaterials(List<Material> into)
    {
        if (into == null) return;
        Material foreground = GetOrCreateHoldMagicForegroundMaterial();
        if (foreground != null) into.Add(foreground);
        Material core = GetOrCreateHoldContactCoreMaterial();
        if (core != null) into.Add(core);
        Material geometry = GetOrCreateHoldMagicGeometryMaterial();
        if (geometry != null) into.Add(geometry);
        // Same reasoning: nothing carries the rune materials until the first
        // non-Hold judgment, so a Renderer sweep cannot find them either.
        if (RuneBurstEmitter.Instance != null)
        {
            RuneBurstEmitter.Instance.CollectWarmupMaterials(into);
        }
    }

    private static Sprite LoadHoldSprite(string resourcePath)
    {
        if (holdSpriteCache.TryGetValue(resourcePath, out Sprite cached) && cached != null) return cached;

        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
        if (sprites != null && sprites.Length > 0)
        {
            holdSpriteCache[resourcePath] = sprites[0];
            return sprites[0];
        }

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null) return null;
        Sprite created = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f);
        holdSpriteCache[resourcePath] = created;
        return created;
    }

    private void Update()
    {
        if (!IsHoldMagicCircleVisible())
        {
            if (activeHoldEffects.Count > 0 || activeHoldContactCores.Count > 0 ||
                transientMagicEffects.Count > 0)
            {
                ClearHoldEffects();
            }
            return;
        }

        float deltaTime = Time.deltaTime;
        AdvanceTransientMagicEffects(deltaTime);

        if (activeHoldEffects.Count == 0 && activeHoldContactCores.Count == 0) return;

        staleHoldEffects.Clear();
        foreach (var pair in activeHoldEffects)
        {
            NoteController note = pair.Key;
            if (note == null)
            {
                staleHoldEffects.Add(note);
                continue;
            }

            ComputeHoldMagicTransform(note, -1, out var position, out var rotation);
            List<HoldRotatingEffect> effects = pair.Value;
            for (int i = 0; i < effects.Count; i++)
            {
                AdvanceMagicEffect(effects[i], position, rotation, deltaTime);
            }
        }


        foreach (var pair in activeHoldContactCores)
        {
            NoteController note = pair.Key;
            if (note == null)
            {
                if (!staleHoldEffects.Contains(note)) staleHoldEffects.Add(note);
                continue;
            }
            ComputeHoldMagicTransform(note, -1, out var position, out var rotation);
            AdvanceMagicEffect(pair.Value, position, rotation, deltaTime);
        }

        for (int i = 0; i < staleHoldEffects.Count; i++)
        {
            StopHoldRotation(staleHoldEffects[i]);
        }
    }

    private void AdvanceMagicEffect(HoldRotatingEffect effect, Vector3 position,
        Quaternion rotation, float deltaTime)
    {
        if (effect == null || effect.Object == null) return;
        effect.Age += deltaTime;
        EvaluateMagicEffect(effect, out float alpha, out float spin, out float spread);
        // A Hold's circle comes up to speed as it fades in, so it is never a
        // fully lit seal sitting still for a frame.
        effect.Angle = Mathf.Repeat(
            effect.Angle + effect.DegreesPerSecond * spin * deltaTime, 360f);
        ApplyMagicEffectFrame(effect, position, rotation, alpha, spread);
    }

    private void AdvanceTransientMagicEffects(float deltaTime)
    {
        for (int i = transientMagicEffects.Count - 1; i >= 0; i--)
        {
            HoldRotatingEffect effect = transientMagicEffects[i];
            if (effect == null || effect.Object == null)
            {
                transientMagicEffects.RemoveAt(i);
                continue;
            }
            if (effect.Age >= effect.Lifetime)
            {
                ReleaseMagicEffect(effect);
                transientMagicEffects.RemoveAt(i);
                continue;
            }
            // These hold the transform they were spawned with: the note that
            // triggered them is usually gone before they finish fading.
            AdvanceMagicEffect(effect, effect.Position, effect.Rotation, deltaTime);
        }
    }

    private void PlayEffectOnJudgmentPlane(Vector3 position, Quaternion rotation)
    {
        var system = GetPooledInstance();
        if (system == null) return;

        var main = system.main;
        main.startSize = Random.Range(0.5f, 1f) * particleSizeScale;

        system.transform.SetPositionAndRotation(position + rotation * spawnOffset, rotation);
        system.gameObject.SetActive(true);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Play(true);
        StartCoroutine(ReturnWhenFinished(system));
    }

    private void ConfigureForegroundRenderer(ParticleSystem system)
    {
        if (system == null) return;
        var renderers = system.GetComponentsInChildren<ParticleSystemRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            renderer.sortingOrder = foregroundSortingOrder;
        }
    }

    private void PlayClassicalAccent(NoteController note, int laneId, JudgmentResult result)
    {
        if (!playClassicalHitAccent || note == null) return;
        if (result != JudgmentResult.Perfect &&
            result != JudgmentResult.Great &&
            result != JudgmentResult.Good)
        {
            return;
        }

        if (laneId < 0 && note.NoteData != null)
        {
            laneId = note.NoteData.startLane;
        }

        ComputeSpecialEffectTransform(note, laneId, out var position, out var rotation, out var anchorWidth);
        float width = anchorWidth > 0.001f ? anchorWidth : ComputeNoteWidth(note);
        bool isLeft = ResolveIsLeft(note, laneId);
        Color theme = isLeft ? leftAccentColor : rightAccentColor;

        // Judgment quality subtly changes intensity while retaining the
        // pale blue/red glass theme beneath the white-gold core.
        float quality = result == JudgmentResult.Perfect ? 1f :
                        result == JudgmentResult.Great ? 0.86f : 0.72f;
        theme.a *= quality;

        float noteHeight = note.GetCurrentWorldHeight();
        // A Hold may have a tall persistent glow that encloses its tail, but
        // every rhythmic flash belongs at the judgment line. Passing the tail
        // length here used to stretch and move this burst up the Hold body.
        ClassicalHitAccent.Play(position, rotation, width, theme, noteHeight, 0f);
        try { JudgmentLineGlow.GetOrCreate()?.TriggerGlow(); } catch { }
    }

    private bool ResolveIsLeft(NoteController note, int laneId)
    {
        return !ResolveIsRightHand(note, laneId);
    }

    /// <summary>
    /// Play the special effect aligned to the note's track space.
    /// </summary>
    public void PlaySpecialEffect(NoteController note, int inputLane = -1)
    {
        if (note == null) return;
        var system = GetSpecialPooledInstance();
        if (system == null) return;

        ComputeSpecialEffectTransform(note, inputLane, out var spawnPosition, out var spawnRotation, out var anchorWidth);
        float noteWidth = anchorWidth > 0.001f ? anchorWidth : ComputeNoteWidth(note);

        var main = system.main;
        float particleSize = ComputeSpecialParticleSize(noteWidth);
        main.startSize = particleSize;

        if (specialEffectAdaptToNoteWidth && note != null)
        {
            float shapeWidth = noteWidth * specialEffectWidthMultiplier;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                Mathf.Max(0.01f, shapeWidth),
                specialEffectShapeThickness * specialEffectSizeScale,
                specialEffectShapeThickness * specialEffectSizeScale);
        }

        system.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        system.gameObject.SetActive(true);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Play(true);

        StartCoroutine(ReturnSpecialWhenFinished(system));
    }

    /// <summary>
    /// Play the special effect when a note is hit.
    /// </summary>
    public void PlaySpecialHitEffect(NoteController note, int laneId, JudgmentResult result)
    {
        if (note == null) return;
        PlaySpecialEffect(note, laneId);
    }

    private void ComputeSpecialEffectTransform(NoteController note, int inputLane, out Vector3 position,
        out Quaternion rotation, out float anchorWidth)
    {
        if (note == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            anchorWidth = 0f;
            return;
        }

        Vector3 anchorPosition = note.transform.position;
        Quaternion anchorRotation = Quaternion.identity;
        anchorWidth = 0f;
        try
        {
            var judgeMesh = NoteJudgementMeshManager.EnsureCreated();
            if (judgeMesh != null)
            {
                judgeMesh.TryGetHitOrigin(note, out anchorPosition, out anchorRotation, out anchorWidth, inputLane);
            }
        }
        catch { }

        Vector3 offset = specialEffectSpawnOffset + new Vector3(0f, 0f, specialEffectZOffset);

        position = anchorPosition + (anchorRotation * offset);
        rotation = anchorRotation;
    }

    /// <summary>
    /// Hold ornaments are 2D sprites.  The judgment mesh rotation is suitable for
    /// mesh geometry but can turn a sprite almost edge-on at some camera angles.
    /// Keep the hit position, then explicitly face the gameplay camera so every
    /// circle layer remains visible and rotates around its own centre.
    /// </summary>
    private void ComputeHoldMagicTransform(NoteController note, int inputLane,
        out Vector3 position, out Quaternion rotation)
    {
        // The judgment line, not the note. A Hold head is centred on the line
        // once accepted, so for Holds the two agreed and the note's own
        // transform was enough -- but an ordinary note is judged wherever it
        // happens to be when it is struck, which for an early or late input is
        // in front of or behind the line. Anchoring to the note put the seal
        // there with it, and left it disagreeing with the rune burst, which is
        // placed on the line.
        //
        // NoteMesh.HitOrigin is still not the answer; see the remarks on
        // NoteJudgementMeshManager.TryGetJudgmentLineOrigin for why.
        position = note != null ? note.transform.position : Vector3.zero;
        var meshManager = NoteJudgementMeshManager.Instance;
        if (note != null && meshManager != null &&
            meshManager.TryGetJudgmentLineOrigin(note, out Vector3 lineOrigin, out _, inputLane))
        {
            position = lineOrigin;
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            rotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
            // Pull the overlay a tiny amount toward the camera.  Its shader is
            // foreground-only, but this also avoids precision overlap at the line.
            position -= camera.transform.forward * 0.025f;
        }
        else
        {
            rotation = Quaternion.identity;
        }
    }

    private float ComputeSpecialParticleSize(float anchorWidth)
    {
        float width = Mathf.Max(0.01f, anchorWidth);
        float unclamped = width * Mathf.Max(0.01f, specialEffectParticleSizeRatio) * specialEffectSizeScale;
        float minSize = Mathf.Max(0.01f, specialEffectParticleSizeClamp.x);
        float maxSize = Mathf.Max(minSize, specialEffectParticleSizeClamp.y);
        return Mathf.Clamp(unclamped, minSize, maxSize);
    }

    private float ComputeNoteWidth(NoteController note)
    {
        if (note == null || note.NoteData == null)
        {
            return 1f;
        }

        try
        {
            float worldWidth = note.GetCurrentWorldWidth();
            if (worldWidth > 0.001f)
            {
                return worldWidth;
            }
        }
        catch { }

        float trackWidth = PianoVisualLayout.ResolveTrackWidth(note.TrackTransform);
        return Mathf.Max(0.01f, PianoVisualLayout.ResolveVisualWidth(note.NoteData, trackWidth));
    }

    private IEnumerator ReturnWhenFinished(ParticleSystem system)
    {
        if (system == null) yield break;
        while (system.IsAlive(true))
        {
            yield return null;
        }
        ReturnToPool(system);
    }

    private IEnumerator ReturnSpecialWhenFinished(ParticleSystem system)
    {
        if (system == null) yield break;
        while (system.IsAlive(true))
        {
            yield return null;
        }
        ReturnSpecialToPool(system);
    }
}

/// <summary>
/// Routes judgment effects to whichever emitter is already present in the
/// loaded scene. It also revives legacy scene emitters that were serialized
/// inactive, without creating a second competing particle setup.
/// </summary>
public static class HitEffectRouter
{
    private static bool loggedEmitterChoice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallScenePrewarm()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The judgment mesh manager builds a 16-quad pool, loads its material and
        // instantiates a runtime copy of it — all inside Awake. It was only ever
        // reached from a hit path (ShowNoteMeshEffect), so that whole setup landed
        // on the first note the player touched. Building it here costs the same
        // work at a moment nobody is playing.
        try { NoteJudgementMeshManager.EnsureCreated(); } catch { }

        // The original scene stores ParticleManager inactive. Wake and prewarm
        // it during scene loading so the first judgment never pays pool setup.
        var modern = FindSceneComponent<ParticleEffectPlayer>();
        if (modern != null)
        {
            PreferModernEmitter(modern);
            return;
        }

        var legacy = FindSceneComponent<HitParticleManager>();
        if (legacy != null) EnsureActive(legacy);
    }

    public static void Play(NoteController note, int laneId, JudgmentResult result)
    {
        if (note == null) return;

        // Every successful judgment funnels through here, which makes it the one
        // place the keysound has to hook to stay in step with the visuals.
        using (HitchProbe.Measure("keysound"))
        {
            try { PianoKeysound.PlayJudgedNote(note, laneId); } catch { }
        }

        var modern = ParticleEffectPlayer.Instance ?? FindSceneComponent<ParticleEffectPlayer>();
        if (modern != null)
        {
            PreferModernEmitter(modern);
            LogEmitterChoice("modern ParticleEffectPlayer", modern.gameObject);
            using (HitchProbe.Measure("hitParticles")) modern.PlayHitEffect(note, laneId, result);
            return;
        }

        var legacy = HitParticleManager.Instance ?? FindSceneComponent<HitParticleManager>();
        if (legacy != null)
        {
            EnsureActive(legacy);
            LogEmitterChoice("legacy HitParticleManager fallback", legacy.gameObject);
            legacy.PlayHitEffect(note, laneId, result);
        }
    }

    public static void BeginHold(NoteController note, int laneId)
    {
        if (note == null) return;

        var modern = ParticleEffectPlayer.Instance ?? FindSceneComponent<ParticleEffectPlayer>();
        if (modern != null)
        {
            PreferModernEmitter(modern);
            LogEmitterChoice("modern ParticleEffectPlayer", modern.gameObject);
            using (HitchProbe.Measure("holdMagic")) modern.BeginHoldRotation(note, laneId);
            return;
        }

        var legacy = HitParticleManager.Instance ?? FindSceneComponent<HitParticleManager>();
        if (legacy != null)
        {
            EnsureActive(legacy);
            legacy.BeginHoldEmission(note, laneId);
        }
    }

    public static void StopHold(NoteController note)
    {
        if (ReferenceEquals(note, null)) return;

        var modern = ParticleEffectPlayer.Instance ?? FindSceneComponent<ParticleEffectPlayer>();
        if (modern != null) modern.StopHoldRotation(note);

        var legacy = HitParticleManager.Instance ?? FindSceneComponent<HitParticleManager>();
        if (legacy != null) legacy.StopHoldEmission(note);
    }

    private static T FindSceneComponent<T>() where T : MonoBehaviour
    {
        var candidates = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate != null && candidate.gameObject.scene.IsValid())
            {
                return candidate;
            }
        }
        return null;
    }

    private static void EnsureActive(Behaviour behaviour)
    {
        if (behaviour == null) return;
        if (!behaviour.gameObject.activeSelf) behaviour.gameObject.SetActive(true);
        if (!behaviour.enabled) behaviour.enabled = true;
    }

    private static void PreferModernEmitter(ParticleEffectPlayer modern)
    {
        if (modern == null) return;
        EnsureActive(modern);

        // Never let the old laser-based emitter compete with the classical
        // Ring / Pin / Flower implementation in a standalone player build.
        var legacyEmitters = Resources.FindObjectsOfTypeAll<HitParticleManager>();
        for (int i = 0; i < legacyEmitters.Length; i++)
        {
            HitParticleManager legacy = legacyEmitters[i];
            if (legacy != null && legacy.gameObject.scene.IsValid()) legacy.enabled = false;
        }
    }

    private static void LogEmitterChoice(string choice, GameObject owner)
    {
        if (loggedEmitterChoice) return;
        loggedEmitterChoice = true;
        Debug.Log($"HitEffectRouter: using {choice} on '{(owner != null ? owner.name : "<null>")}'.");
    }
}
