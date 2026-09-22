using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Throws a handful of glowing runes off the judgment line when a note is
/// struck.
/// </summary>
/// <remarks>
/// This rides on top of the light-column mesh, it does not replace it. The
/// scatter is the burst's own — which glyphs come up, where they land sideways,
/// how far each one climbs — and none of it tracks what the player did, so a
/// judgment always reads as the same effect however it was played.
/// </remarks>
[DefaultExecutionOrder(-118)]
public class RuneBurstEmitter : MonoBehaviour
{
    public static RuneBurstEmitter Instance { get; private set; }

    [Header("Burst")]
    [SerializeField, Range(1, 128), Tooltip("Runes thrown per judgment.")]
    private int runesPerHit = 10;
    [SerializeField, Tooltip("Average distance a rune travels, as a multiple of the note's width.")]
    private float riseWidths = 0.675f;
    [SerializeField, Range(0f, 0.9f), Tooltip("How much the travel distance varies between runes. 0 makes every rune fly the same distance.")]
    private float riseJitter = 0.60f;
    [SerializeField, Tooltip("Size of a rune glyph, as a multiple of the note's width.")]
    private float runeWidths = 0.0825f;
    [SerializeField, Tooltip("Seconds from the strike to fully faded.")]
    private float lifetime = 0.52f;
    [SerializeField, Range(0f, 1f), Tooltip("Fraction of the lifetime held at full opacity before the fade begins.")]
    private float fadeStart = 0.55f;
    [SerializeField, Range(0f, 1f), Tooltip("0 climbs at a constant rate, 1 front-loads the whole flight and then coasts at the top.")]
    private float climbEase = 0.45f;
    [SerializeField, Tooltip("Width of the launch band, as a fraction of the note's width. 1 launches them across exactly the note.")]
    private float lateralSpread = 1.0f;
    [SerializeField, Tooltip("Runes launch at a random moment inside this window, so they arrive scattered in time.")]
    private float launchStagger = 0.16f;
    [SerializeField, Tooltip("Maximum tilt a rune is thrown with, in degrees.")]
    private float tiltDegrees = 8f;
    [SerializeField, Range(0f, 0.9f), Tooltip("How much rune size varies between glyphs.")]
    private float sizeJitter = 0.22f;
    [SerializeField, Tooltip("Sideways travel by the end of the flight, as a multiple of the note's width. Independent of the climb, so widening the fan never changes how high the burst reaches.")]
    private float driftWidths = 0.25f;

    [Header("Slide Wake")]
    [SerializeField, Range(0, 64), Tooltip("A slide throws this many extra runes backwards along the track, against the direction it travels.")]
    private int slideWakeRunes = 30;
    [SerializeField, Tooltip("How far a wake rune is thrown, as a multiple of the note's width.")]
    private float slideWakeWidths = 2.0f;
    [SerializeField, Range(0f, 0.9f), Tooltip("Variation in how far back each wake rune gets.")]
    private float slideWakeJitter = 0.55f;
    [SerializeField, Tooltip("How much the wake also lifts off the line, as a multiple of the note's width. Kept small: this is a trail, not a second burst.")]
    private float slideWakeLift = 0.30f;
    [SerializeField, Tooltip("Wake rune size, as a multiple of the note's width.")]
    private float slideWakeRuneWidths = 0.125f;
    [SerializeField, Tooltip("Seconds from the strike to a wake rune being fully faded.")]
    private float slideWakeLifetime = 0.58f;

    [Header("Colour")]
    [SerializeField, ColorUsage(true, true)]
    private Color leftHaloColor = new Color(0.22f, 0.62f, 1.35f, 1f);
    [SerializeField, ColorUsage(true, true)]
    private Color leftCoreColor = new Color(0.80f, 0.94f, 1.25f, 1f);
    [SerializeField, ColorUsage(true, true)]
    private Color rightHaloColor = new Color(1.35f, 0.20f, 0.22f, 1f);
    [SerializeField, ColorUsage(true, true)]
    private Color rightCoreColor = new Color(1.30f, 0.80f, 0.72f, 1f);
    [SerializeField, Range(0.1f, 4f), Tooltip("Multiplies the whole glyph. Has to sit above 1 for the bloom threshold to see it through the alpha blend.")]
    private float glow = 1.7f;

    [Header("Sorting")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField, Tooltip("Base order. Each rune in a burst takes the next one up, so leave headroom for runesPerHit below the 32767 ceiling.")]
    private int sortingOrder = 32620;
    [SerializeField, Tooltip("Idle rune sprites kept for reuse. Has to cover several bursts at once — one burst can now be 32 runes.")]
    private int poolCap = 768;

    private static readonly int HaloColorId = Shader.PropertyToID("_HaloColor");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");

    private sealed class RuneParticle
    {
        public GameObject Object;
        public SpriteRenderer Renderer;
        public Vector3 Origin;
        public Vector3 Travel;
        public Quaternion Rotation;
        public float Age;
        public float Delay;
        public float Lifetime;
        public float Scale;
        public float SpinDegreesPerSecond;
        public float Tilt;
        public float LastAppliedAlpha = -1f;
    }

    private readonly List<RuneParticle> active = new List<RuneParticle>(32);
    private readonly Queue<GameObject> pool = new Queue<GameObject>();
    private Transform container;
    private Material leftMaterial;
    private Material rightMaterial;
    private Camera cachedCamera;

    public static RuneBurstEmitter EnsureCreated()
    {
        if (Instance != null) return Instance;
        // Inactive objects are included deliberately. A scene-authored emitter
        // sitting under a gameplay root that is still switched off is invisible
        // to the default search, and creating a stand-in would leave the
        // configured one to displace it the moment that root wakes.
        var found = FindObjectsByType<RuneBurstEmitter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (found != null && found.Length > 0)
        {
            Instance = found[0];
            return Instance;
        }
        var go = new GameObject("RuneBurstEmitter");
        Instance = go.AddComponent<RuneBurstEmitter>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        container = transform;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (leftMaterial != null) Destroy(leftMaterial);
        if (rightMaterial != null) Destroy(rightMaterial);
        leftMaterial = null;
        rightMaterial = null;
        RuneGlyphs.Release();
    }

    /// <summary>
    /// Throws one burst around a judgment-line position.
    /// </summary>
    /// <remarks>
    /// The frame is built here rather than taken from the caller. Both frames
    /// the mesh manager can hand out are track frames: its anchor rotation lies
    /// flat on the board, and its beam rotation is the judgment line's own
    /// world rotation. In either one a billboard sprite ends up edge-on and its
    /// up axis runs away down the lane, which is why the first version sprayed
    /// into the screen -- the glyphs foreshortened to slivers and the burst had
    /// no vertical spread at all. These face the camera, like the magic circle.
    /// </remarks>
    public void Play(Vector3 position, float width, bool rightHand)
    {
        Camera camera = ResolveCamera();
        Quaternion rotation = camera != null
            ? Quaternion.LookRotation(camera.transform.forward, camera.transform.up)
            : Quaternion.identity;
        // Pulled a little toward the camera, like the magic circle: the shader
        // is foreground-only anyway, but this keeps the glyphs off the plane
        // the note is sitting on.
        if (camera != null) position -= camera.transform.forward * 0.03f;

        Sprite[] glyphs = RuneGlyphs.GetSprites();
        if (glyphs == null || glyphs.Length == 0) return;

        Material material = GetOrCreateMaterial(rightHand);
        if (material == null) return;

        // Everything below is sized off the note rather than in absolute world
        // units. Absolute sizes were the mistake behind both "too small" and
        // "barely flies out": a lane here is a few world units across, so a
        // 0.85-unit glyph travelling 1.15 units is a fraction of one note.
        float noteWidth = Mathf.Max(0.35f, width);

        Vector3 up = rotation * Vector3.up;
        Vector3 right = rotation * Vector3.right;
        float half = noteWidth * 0.5f * Mathf.Max(0f, lateralSpread);
        int count = Mathf.Clamp(runesPerHit, 1, 64);

        for (int i = 0; i < count; i++)
        {
            // One rune per equal slice of the note's width, jittered only
            // inside its own slice. Free random placement clumps and leaves
            // gaps; this stays even however many are thrown.
            float across = ((i + 0.5f) / count) * 2f - 1f;
            across += Random.Range(-1f, 1f) / count;

            // Climb and fan are separate vectors rather than an angle off a
            // cone. Under a cone the sideways reach was a function of how far
            // the rune flew, so widening the fan also raised the burst.
            float distance = noteWidth * riseWidths * (1f + Random.Range(-riseJitter, riseJitter));
            float drift = noteWidth * driftWidths * Random.Range(-1f, 1f);

            // 每顆的飛行距離和左右偏移都隨機，整叢才會散開成一片而不是一條
            // 齊頭的線。（原本刻意固定，是為了讓「同一種判定 = 同一個形狀」；
            //   要回到齊頭就把 riseJitter 和 driftWidths 都設 0。
            //   距離、偏移、大小都是「音符寬度的倍數」，不是絕對世界單位。）
            Spawn(glyphs, material, rotation,
                position + right * (across * half),
                up * distance + right * drift,
                noteWidth * runeWidths * (1f + Random.Range(-sizeJitter, sizeJitter)),
                lifetime, launchStagger, i);
        }
    }

    /// <summary>
    /// The spray a slide leaves behind it: runes thrown back along the track,
    /// against the direction the slide travels.
    /// </summary>
    /// <param name="directionSign">Positive when the slide runs toward higher lanes.</param>
    /// <remarks>
    /// Thrown backwards on purpose. A wake reads as motion because it goes the
    /// other way; particles leaving in the direction of travel would look like
    /// the note is being pushed along, and would sit nearly still relative to
    /// the slide instead of streaming off it.
    /// </remarks>
    public void PlaySlideWake(Vector3 position, float width, bool rightHand, float directionSign)
    {
        if (slideWakeRunes <= 0 || Mathf.Abs(directionSign) < 0.001f) return;

        Camera camera = ResolveCamera();
        Quaternion rotation = camera != null
            ? Quaternion.LookRotation(camera.transform.forward, camera.transform.up)
            : Quaternion.identity;
        if (camera != null) position -= camera.transform.forward * 0.03f;

        Sprite[] glyphs = RuneGlyphs.GetSprites();
        if (glyphs == null || glyphs.Length == 0) return;
        Material material = GetOrCreateMaterial(rightHand);
        if (material == null) return;

        float noteWidth = Mathf.Max(0.35f, width);
        Vector3 up = rotation * Vector3.up;
        Vector3 back = rotation * Vector3.right * -Mathf.Sign(directionSign);
        int count = Mathf.Clamp(slideWakeRunes, 1, 64);

        for (int i = 0; i < count; i++)
        {
            // Spread the launch points back along the lane, not stacked on the
            // note. Firing every rune from one spot made the wake a puff that
            // happened to move sideways; starting them along the path means the
            // trail is already a streak on the frame it appears.
            float alongTrail = ((i + 0.5f) / count) + Random.Range(-0.5f, 0.5f) / count;

            // The variation goes into how far back each one gets, not across
            // the throw: a wake is a trail, so it wants length, not width.
            float throwWidths = slideWakeWidths *
                (1f + Random.Range(-slideWakeJitter, slideWakeJitter));
            Vector3 travel = back * (noteWidth * throwWidths * (1f - 0.45f * alongTrail)) +
                up * (noteWidth * slideWakeLift * Random.Range(-0.4f, 1f));

            Spawn(glyphs, material, rotation,
                position + back * (noteWidth * slideWakeWidths * 0.45f * alongTrail),
                travel,
                noteWidth * slideWakeRuneWidths * (1f + Random.Range(-sizeJitter, sizeJitter)),
                slideWakeLifetime, launchStagger * 0.6f, i);
        }
    }

    private void Spawn(Sprite[] glyphs, Material material, Quaternion rotation, Vector3 origin,
        Vector3 travel, float scale, float life, float stagger, int index)
    {
        GameObject go = Rent(out SpriteRenderer renderer);
        renderer.sprite = glyphs[Random.Range(0, glyphs.Length)];
        renderer.sharedMaterial = material;
        renderer.sortingLayerName = sortingLayerName;
        // Clamped: the per-rune offset would otherwise run past the 32767 a
        // SpriteRenderer accepts once a burst gets large.
        renderer.sortingOrder = Mathf.Clamp(sortingOrder + index, short.MinValue, short.MaxValue);
        renderer.color = new Color(1f, 1f, 1f, 0f);

        var particle = new RuneParticle
        {
            Object = go,
            Renderer = renderer,
            Origin = origin,
            Travel = travel,
            Rotation = rotation,
            Age = 0f,
            // Random rather than in order: an even stagger reads as a queue of
            // runes leaving one after another.
            Delay = Random.Range(0f, Mathf.Max(0f, stagger)),
            Lifetime = Mathf.Max(0.05f, life),
            Scale = scale,
            Tilt = Random.Range(-tiltDegrees, tiltDegrees),
            SpinDegreesPerSecond = Random.Range(-22f, 22f)
        };

        ApplyFrame(particle);
        active.Add(particle);
    }

    private void Update()
    {
        if (active.Count == 0) return;
        float deltaTime = Time.deltaTime;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            RuneParticle particle = active[i];
            if (particle == null || particle.Object == null)
            {
                active.RemoveAt(i);
                continue;
            }

            particle.Age += deltaTime;
            if (particle.Age >= particle.Delay + particle.Lifetime)
            {
                Return(particle.Object);
                active.RemoveAt(i);
                continue;
            }
            ApplyFrame(particle);
        }
    }

    private void ApplyFrame(RuneParticle particle)
    {
        float local = particle.Age - particle.Delay;
        if (local < 0f)
        {
            // Still waiting its turn: parked on its launch point, not left
            // wherever the pooled object last was. It is invisible either way,
            // but a stale pose is one frame away from being seen if anything
            // ever makes it visible early.
            SetAlpha(particle, 0f);
            PlaceAt(particle, 0f, 0f);
            return;
        }

        float u = Mathf.Clamp01(local / particle.Lifetime);
        float inv = 1f - u;
        // Blended toward linear rather than a straight ease-out. A cubic
        // ease-out covered 90% of the flight in the first half, so the runes
        // hit the top of their travel almost at once and then hung there while
        // they faded; climbEase is how much of that front-loading is kept.
        float climb = Mathf.Lerp(u, 1f - inv * inv, climbEase);

        // Barely a fade at all. A longer one let each rune climb a third of its
        // distance before it was fully visible, so the burst looked like it
        // materialised in the air instead of leaving the line.
        float alpha = Mathf.Clamp01(u / 0.03f);
        alpha *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.Clamp01(fadeStart), 1f, u));

        PlaceAt(particle, climb, local);
        SetAlpha(particle, alpha);
    }

    private static void PlaceAt(RuneParticle particle, float climb, float local)
    {
        Transform t = particle.Object.transform;
        t.SetPositionAndRotation(
            particle.Origin + particle.Travel * climb,
            particle.Rotation * Quaternion.AngleAxis(
                particle.Tilt + particle.SpinDegreesPerSecond * local, Vector3.forward));
        t.localScale = Vector3.one * particle.Scale;
    }

    private static void SetAlpha(RuneParticle particle, float alpha)
    {
        if (particle.Renderer == null) return;
        if (Mathf.Abs(alpha - particle.LastAppliedAlpha) <= 0.002f) return;
        particle.Renderer.color = new Color(1f, 1f, 1f, alpha);
        particle.LastAppliedAlpha = alpha;
    }

    private Camera ResolveCamera()
    {
        if (cachedCamera != null) return cachedCamera;
        cachedCamera = Camera.main;
        return cachedCamera;
    }

    private GameObject Rent(out SpriteRenderer renderer)
    {
        while (pool.Count > 0)
        {
            GameObject pooled = pool.Dequeue();
            if (pooled == null) continue;
            renderer = pooled.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                Destroy(pooled);
                continue;
            }
            pooled.SetActive(true);
            return pooled;
        }

        var go = new GameObject("Rune");
        go.transform.SetParent(container, false);
        renderer = go.AddComponent<SpriteRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    private void Return(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        // Cleared rather than left at the end of its flight: a rented object
        // must not be able to show the previous burst's pose.
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        if (pool.Count >= Mathf.Max(0, poolCap))
        {
            Destroy(go);
            return;
        }
        go.transform.SetParent(container, false);
        pool.Enqueue(go);
    }

    /// <summary>
    /// One material per hand, rather than one material plus a per-renderer
    /// property block.
    /// </summary>
    /// <remarks>
    /// The colour only ever takes two values, and a property block is
    /// per-renderer state that stops the sprites batching. At a few runes a
    /// burst that did not matter; at a few dozen it is a few dozen draw calls
    /// per note. Two materials and one atlas let the whole burst batch.
    ///
    /// The colours are re-applied on every call so the Inspector fields still
    /// take effect while playing.
    /// </remarks>
    private Material GetOrCreateMaterial(bool rightHand)
    {
        ref Material slot = ref rightHand ? ref rightMaterial : ref leftMaterial;
        if (slot == null)
        {
            Shader shader = Shader.Find("Nostalgia/RuneGlyph");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            slot = new Material(shader)
            {
                name = rightHand ? "Rune Glyph Right (Runtime)" : "Rune Glyph Left (Runtime)",
                renderQueue = 4200,
                hideFlags = HideFlags.DontSave
            };
        }

        if (slot.HasProperty(HaloColorId))
            slot.SetColor(HaloColorId, rightHand ? rightHaloColor : leftHaloColor);
        if (slot.HasProperty(CoreColorId))
            slot.SetColor(CoreColorId, rightHand ? rightCoreColor : leftCoreColor);
        // Same reason as the magic circle's: these are alpha blended, so the
        // authored HDR colour is scaled down by the glyph's own alpha before it
        // ever reaches the bloom threshold.
        if (slot.HasProperty(GlowId)) slot.SetFloat(GlowId, glow);
        return slot;
    }

    /// <summary>Builds the atlas and materials before the first note lands.</summary>
    public void Prewarm()
    {
        RuneGlyphs.GetSprites();
        GetOrCreateMaterial(false);
        GetOrCreateMaterial(true);
    }

    /// <summary>Hands the runtime materials to the shader warm-up sweep.</summary>
    public void CollectWarmupMaterials(List<Material> into)
    {
        if (into == null) return;
        Material left = GetOrCreateMaterial(false);
        if (left != null) into.Add(left);
        Material right = GetOrCreateMaterial(true);
        if (right != null) into.Add(right);
    }
}
