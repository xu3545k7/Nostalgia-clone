using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized pool that spawns hit particles whenever the judgment system reports a successful note hit.
/// Expose the particle prefab and texture variants in the inspector so non-programmers can tweak the visuals freely.
/// </summary>
[DefaultExecutionOrder(-25)]
public class HitParticleManager : MonoBehaviour
{
    public static HitParticleManager Instance { get; private set; }

    [Header("Prefab / Pooling")]
    [SerializeField, Tooltip("Legacy single emitter used by the original main scene.")]
    private ParticleSystem particlePrefab;
    [SerializeField] private ParticleSystem[] particlePrefabs;
    [SerializeField] private int initialPoolSize = 12;
    [SerializeField] private Transform poolRoot;

    [Header("Texture Variants - Normal Notes")]
    [SerializeField] private Texture2D[] leftSparkTextures;
    [SerializeField] private Texture2D[] rightSparkTextures;

    [Header("Texture Variants - Staccato (Sharp) Notes")]
    [SerializeField] private Texture2D[] leftSharpTextures;
    [SerializeField] private Texture2D[] rightSharpTextures;

    [Header("Fallback Textures (optional)")]
    [Tooltip("Used when the specific directional/staccato array is empty.")]
    [SerializeField] private Texture2D[] fallbackTextures;

    [Header("Color Per Judgment")]
    [SerializeField] private Color perfectColor = new Color(1f, 0.95f, 0.7f);
    [SerializeField] private Color greatColor = new Color(0.7f, 0.9f, 1f);
    [SerializeField] private Color goodColor = new Color(0.7f, 0.8f, 1f);
    [SerializeField] private Color leftThemeColor = new Color(0.2745098f, 0.6392157f, 1f, 1f);
    [SerializeField] private Color rightThemeColor = new Color(1f, 0.1764706f, 0.1764706f, 1f);

    [Header("Emission Tweaks")]
    [SerializeField] private Vector2 startSizeRange = new Vector2(0.6f, 1.2f);
    [SerializeField] private Vector2 startSpeedRange = new Vector2(2.5f, 4.5f);
    [SerializeField] private Vector2 lifetimeRange = new Vector2(0.25f, 0.55f);
    [SerializeField] private float rotationJitter = 15f;
    [SerializeField, Tooltip("Global multiplier for normal hit particle size.")]
    private float particleSizeMultiplier = 1.5f;
    [SerializeField, Tooltip("Width multiplier for particle spawn shape when matching note width.")]
    private float particleShapeWidthMultiplier = 1.5f;
    [SerializeField, Tooltip("If true, adjusts the particle shape width to match the note width for standard notes.")]
    private bool adaptShapeToNoteWidth = true;

    [Header("Spawn Placement")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.05f, 0f);
    [SerializeField, Tooltip("How much to offset particles horizontally between each lane when a lane id is provided.")]
    private float laneSpacing = 0.08f;

    [Header("Laser Beam")]
    [SerializeField, Tooltip("If true, spawns the LK laser beam along the same direction as the particles.")]
    private bool enableLaserBeam = false;
    [SerializeField, Tooltip("Prefab that contains a LineRenderer with LK_Lazer.mat and a LaserBeamBurst script.")]
    private LaserBeamBurst laserBeamPrefab;
    [SerializeField, Tooltip("Initial pooled beam instances.")]
    private int laserPoolSize = 6;
    [SerializeField, Tooltip("Offset along the track forward direction so the beam starts slightly ahead/behind the note.")]
    private float laserStartForwardOffset = 0.02f;
    [SerializeField, Tooltip("調整雷射寬度用的倍率。1 代表跟音符寬度相同。")]
    private float laserWidthMultiplier = 1.2f;
    [SerializeField, Tooltip("雷射寬度最小/最大值限制，避免太細或過粗。")]
    private Vector2 laserWidthClamp = new Vector2(0.15f, 1.5f);
    [SerializeField, Tooltip("勾選後沿用 LaserBeamBurst prefab 的 Default Length。要自訂長度請取消勾選並填寫下方覆寫值。")]
    private bool usePrefabLaserLength = true;
    [SerializeField, Tooltip("雷射長度覆寫值。只有在取消上面選項時才會套用，<=0 仍會 fallback 到 prefab。")]
    private float laserLengthOverride = 1.1f;
    [SerializeField, Tooltip("勾選後雷射會固定使用下方顏色，不再依判定結果套色。")]
    private bool overrideLaserColor = false;
    [SerializeField]
    private Color laserOverrideColor = Color.yellow;
    [SerializeField, Tooltip("Override beam duration (<= 0 keeps the prefab default).")]
    private float laserDurationOverride = 0.2f;

    [Header("Lane Split")]
    [SerializeField, Tooltip("Lane index that starts the right-hand area (inclusive). Values below this are treated as left.")]
    private int rightLaneStartIndex = 14; // 0-27 lanes => left 0-13, right 14-27 by default

    [Header("Sharp Flight Settings")]
    [SerializeField, Tooltip("Enable the linear flight behaviour for staccato notes.")]
    private bool enableSharpLinearFlight = true;
    [SerializeField] private Vector2 sharpSizeRange = new Vector2(0.25f, 0.45f);
    [SerializeField] private Vector2 sharpSpeedRange = new Vector2(6f, 9f);
    [SerializeField] private Vector2 sharpLifetimeRange = new Vector2(0.35f, 0.65f);
    [SerializeField, Tooltip("Box thickness (Y/Z) used when emitting sharp particles so they stay inside the note body.")]
    private float sharpSpawnThickness = 0.02f;
    [SerializeField, Tooltip("Multiplier applied to lane spacing when deriving the sharp emission width.")]
    private float sharpWidthMultiplier = 1f;
    [SerializeField, Tooltip("Fallback width so very thin notes still show a visible sharp effect.")]
    private float minSharpWidth = 0.04f;
    
    [Header("Hold Emission")]
    [SerializeField, Tooltip("If true, hold notes keep spawning particles while the key stays pressed.")]
    private bool enableHoldEmissionLoop = true;
    [SerializeField, Tooltip("Fallback interval only when no valid BPM is available."), Min(0.01f)]
    private float holdEmissionInterval = 0.08f;

    private readonly Queue<ParticleSystem> availableSystems = new Queue<ParticleSystem>();
    private readonly HashSet<ParticleSystem> activeSystems = new HashSet<ParticleSystem>();
    private readonly Dictionary<ParticleSystem, ParticleSystemModulesSnapshot> defaultModuleStates = new Dictionary<ParticleSystem, ParticleSystemModulesSnapshot>();
    // Cached ParticleSystemRenderer per pooled system — avoids GetComponent every judgment
    private readonly Dictionary<ParticleSystem, ParticleSystemRenderer> _psRendererCache = new Dictionary<ParticleSystem, ParticleSystemRenderer>();
    private readonly Dictionary<NoteController, HoldEmissionState> holdEmissionStates = new Dictionary<NoteController, HoldEmissionState>();
    private readonly Queue<LaserBeamBurst> availableBeams = new Queue<LaserBeamBurst>();
    private readonly HashSet<LaserBeamBurst> activeBeams = new HashSet<LaserBeamBurst>();
    private MaterialPropertyBlock propertyBlock;

    private struct ParticleSystemModulesSnapshot
    {
        public ParticleSystemShapeType shapeType;
        public Vector3 shapeScale;
        public float shapeAngle;
        public float shapeRadius;
    }
    
    private class HoldEmissionState
    {
        public Coroutine Coroutine;
        public int LaneId;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (poolRoot == null) poolRoot = transform;
        propertyBlock = new MaterialPropertyBlock();
        PrewarmPool(initialPoolSize);
        PrewarmLaserPool(laserPoolSize);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        foreach (var kv in holdEmissionStates)
        {
            try
            {
                if (kv.Value != null && kv.Value.Coroutine != null)
                {
                    StopCoroutine(kv.Value.Coroutine);
                }
            }
            catch { }
        }
        holdEmissionStates.Clear();
    }

    /// <summary>
    /// Stops chart-scoped emission and returns every live effect to its pool.
    /// The prewarmed pool itself is intentionally retained for the next song.
    /// </summary>
    public void ClearActiveEffects()
    {
        StopAllCoroutines();
        holdEmissionStates.Clear();

        var systems = new List<ParticleSystem>(activeSystems);
        activeSystems.Clear();
        for (int i = 0; i < systems.Count; i++)
        {
            ParticleSystem system = systems[i];
            if (system == null) continue;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.gameObject.SetActive(false);
            RestoreSystemDefaults(system);
            availableSystems.Enqueue(system);
        }

        var beams = new List<LaserBeamBurst>(activeBeams);
        activeBeams.Clear();
        for (int i = 0; i < beams.Count; i++)
        {
            LaserBeamBurst beam = beams[i];
            if (beam == null) continue;
            beam.gameObject.SetActive(false);
            availableBeams.Enqueue(beam);
        }
    }

    private void PrewarmPool(int count)
    {
        if (!HasParticlePrefab() || count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            availableSystems.Enqueue(CreateSystemInstance());
        }
    }

    private ParticleSystem CreateSystemInstance()
    {
        ParticleSystem selectedPrefab = PickParticlePrefab();
        if (selectedPrefab == null) return null;
        var instance = Instantiate(selectedPrefab, poolRoot);
        instance.gameObject.SetActive(false);
        instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        CacheDefaultModules(instance);
        return instance;
    }

    private void PrewarmLaserPool(int count)
    {
        if (!enableLaserBeam || laserBeamPrefab == null || count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            availableBeams.Enqueue(CreateLaserInstance());
        }
    }

    private LaserBeamBurst CreateLaserInstance()
    {
        var beam = Instantiate(laserBeamPrefab, poolRoot);
        beam.ConfigureReleaseCallback(ReturnLaserInstance);
        beam.gameObject.SetActive(false);
        return beam;
    }

    private LaserBeamBurst GetLaserInstance()
    {
        if (!enableLaserBeam || laserBeamPrefab == null)
        {
            return null;
        }

        if (availableBeams.Count == 0)
        {
            availableBeams.Enqueue(CreateLaserInstance());
        }

        var beam = availableBeams.Dequeue();
        activeBeams.Add(beam);
        beam.gameObject.SetActive(true);
        return beam;
    }

    private void ReturnLaserInstance(LaserBeamBurst beam)
    {
        if (beam == null)
        {
            return;
        }

        beam.gameObject.SetActive(false);
        activeBeams.Remove(beam);
        availableBeams.Enqueue(beam);
    }

    private ParticleSystem GetSystem()
    {
        if (!HasParticlePrefab())
        {
            Debug.LogWarning("HitParticleManager: no particle emitter prefab is assigned.");
            return null;
        }

        if (availableSystems.Count == 0)
        {
            var created = CreateSystemInstance();
            if (created == null) return null;
            availableSystems.Enqueue(created);
        }

        var system = availableSystems.Dequeue();
        activeSystems.Add(system);
        RestoreSystemDefaults(system);
        return system;
    }

    private bool HasParticlePrefab()
    {
        if (particlePrefab != null) return true;
        if (particlePrefabs == null) return false;
        for (int i = 0; i < particlePrefabs.Length; i++)
        {
            if (particlePrefabs[i] != null) return true;
        }
        return false;
    }

    private ParticleSystem PickParticlePrefab()
    {
        if (particlePrefabs != null && particlePrefabs.Length > 0)
        {
            for (int attempt = 0; attempt < particlePrefabs.Length; attempt++)
            {
                var candidate = particlePrefabs[Random.Range(0, particlePrefabs.Length)];
                if (candidate != null) return candidate;
            }

            for (int i = 0; i < particlePrefabs.Length; i++)
            {
                if (particlePrefabs[i] != null) return particlePrefabs[i];
            }
        }

        return particlePrefab;
    }

    /// <summary>
    /// Spawns a particle burst for the supplied note.
    /// </summary>
    public void PlayHitEffect(NoteController note, int laneId, JudgmentResult result)
    {
        if (note == null) return;
        var system = GetSystem();
        if (system == null) return;

        Vector3 spawnPos = note.transform.position + spawnOffset;
        Quaternion hitRotation = Quaternion.identity;
        bool hasJudgmentAnchor = false;
        try
        {
            var judgeMesh = NoteJudgementMeshManager.EnsureCreated();
            if (judgeMesh != null && judgeMesh.TryGetHitOrigin(
                note, out var hitOrigin, out hitRotation, out _, laneId))
            {
                spawnPos = hitOrigin + hitRotation * spawnOffset;
                hasJudgmentAnchor = true;
            }
        }
        catch { }
        if (laneId < 0 && note.NoteData != null)
        {
            laneId = note.NoteData.startLane;
        }

        // A judgment anchor already contains the exact note/lane centre. The
        // legacy absolute lane offset would apply the lane position twice.
        if (!hasJudgmentAnchor)
        {
            spawnPos.x += ComputeLaneOffsetX(note, laneId);
        }
        Vector3 trackNormal = ResolveTrackNormal(note);

        bool isSharp = note.IsStaccato;
        bool isLeft = DetermineIsLeftLane(laneId, note);
        Color themedHitColor = ResolveThemedColor(result, isLeft);

        var main = system.main;
        if (isSharp && enableSharpLinearFlight)
        {
            main.startSize = Random.Range(sharpSizeRange.x, sharpSizeRange.y) * particleSizeMultiplier;
            main.startSpeed = Random.Range(sharpSpeedRange.x, sharpSpeedRange.y);
            main.startLifetime = Random.Range(sharpLifetimeRange.x, sharpLifetimeRange.y);
            ApplySharpLinearFlight(system, note);
        }
        else
        {
            main.startSize = Random.Range(startSizeRange.x, startSizeRange.y) * particleSizeMultiplier;
            main.startSpeed = Random.Range(startSpeedRange.x, startSpeedRange.y);
            main.startLifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);
            ApplyStandardFlight(system, note);
        }
        main.startRotation = Random.Range(-rotationJitter, rotationJitter) * Mathf.Deg2Rad;
        main.startColor = themedHitColor;

        ApplyTexture(system, isSharp, isLeft);

        var t = system.transform;
        t.position = spawnPos;
        // Cone and Box shapes fire along the emitter's local +Z, so aim that
        // axis at the track normal and the burst leaves the track surface
        // instead of sliding along the lane. The remaining in-plane axis only
        // decides which way the emission box spans, so any of them will do.
        Vector3 emitDirection = trackNormal.sqrMagnitude > 1e-4f ? trackNormal.normalized : Vector3.up;
        Vector3 emitUp = ResolveTrackForward(note);
        if (Mathf.Abs(Vector3.Dot(emitUp, emitDirection)) > 0.999f)
        {
            emitUp = Mathf.Abs(emitDirection.y) < 0.9f ? Vector3.up : Vector3.forward;
        }
        t.rotation = Quaternion.LookRotation(emitDirection, emitUp);
        system.gameObject.SetActive(true);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Play(true);
        StartCoroutine(ReturnWhenFinished(system));

        try
        {
            JudgmentLineGlow.GetOrCreate()?.TriggerGlow();
        }
        catch { }

        EmitLaserBeam(note, spawnPos, trackNormal, result, themedHitColor);
        // Keep repeated Hold flashes centred on the judgment line. The
        // persistent mesh is responsible for enclosing the remaining tail.
        ClassicalHitAccent.Play(spawnPos, t.rotation, ComputeNoteWidth(note), themedHitColor,
            note.GetCurrentWorldHeight(), 0f);
    }

    private IEnumerator ReturnWhenFinished(ParticleSystem system)
    {
        if (system == null) yield break;
        while (system.IsAlive(true))
        {
            yield return null;
        }
        system.gameObject.SetActive(false);
        activeSystems.Remove(system);
        RestoreSystemDefaults(system);
        availableSystems.Enqueue(system);
    }

    private void EmitLaserBeam(NoteController note, Vector3 spawnPos, Vector3 trackNormal, JudgmentResult result, Color themedHitColor)
    {
        var beam = GetLaserInstance();
        if (beam == null)
        {
            return;
        }

        Vector3 trackForward = ResolveTrackForward(note);
        Vector3 start = spawnPos + trackForward * laserStartForwardOffset;
        float length = (!usePrefabLaserLength && laserLengthOverride > 0f) ? laserLengthOverride : -1f;
        float duration = laserDurationOverride > 0f ? laserDurationOverride : -1f;
        float width = ComputeNoteWidth(note);
        Color beamColor = overrideLaserColor ? laserOverrideColor : themedHitColor;
        beam.Play(start, trackForward, beamColor, length, duration, width, trackNormal);
    }

    private float ComputeNoteWidth(NoteController note)
    {
        var nd = note?.NoteData;
        if (nd == null)
        {
            return laneSpacing;
        }

        float trackWidth = PianoVisualLayout.ResolveTrackWidth(note != null ? note.TrackTransform : null);
        float width = PianoVisualLayout.ResolveVisualWidth(nd, trackWidth) * Mathf.Max(0.01f, laserWidthMultiplier);

        if (laserWidthClamp.y > 0f && laserWidthClamp.y > laserWidthClamp.x)
        {
            float min = Mathf.Max(0.01f, laserWidthClamp.x);
            width = Mathf.Clamp(width, min, laserWidthClamp.y);
        }

        return width;
    }

    private void ApplyTexture(ParticleSystem system, bool isSharp, bool isLeft)
    {
        if (!_psRendererCache.TryGetValue(system, out var renderer))
        {
            renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) _psRendererCache[system] = renderer;
        }
        if (renderer == null) return;

        var texture = PickTexture(isSharp, isLeft);
        if (texture == null) return;

        propertyBlock.SetTexture("_BaseMap", texture);
        propertyBlock.SetTexture("_MainTex", texture);
        renderer.SetPropertyBlock(propertyBlock);
    }

    private Texture2D PickTexture(bool isSharp, bool isLeft)
    {
        Texture2D[] bucket = null;

        if (isSharp)
        {
            bucket = isLeft ? leftSharpTextures : rightSharpTextures;
        }
        else
        {
            bucket = isLeft ? leftSparkTextures : rightSparkTextures;
        }

        if (bucket != null && bucket.Length > 0)
        {
            return bucket[Random.Range(0, bucket.Length)];
        }

        if (fallbackTextures != null && fallbackTextures.Length > 0)
        {
            return fallbackTextures[Random.Range(0, fallbackTextures.Length)];
        }

        return null;
    }

    private bool DetermineIsLeftLane(int laneId, NoteController note)
    {
        if (laneId >= 0)
        {
            return laneId < rightLaneStartIndex;
        }

        var nd = note?.NoteData;
        if (nd != null)
        {
            return nd.startLane < rightLaneStartIndex;
        }

        return true;
    }

    public void BeginHoldEmission(NoteController note, int laneId)
    {
        if (!enableHoldEmissionLoop || note == null) return;

        if (!holdEmissionStates.TryGetValue(note, out var state) || state == null)
        {
            state = new HoldEmissionState();
            holdEmissionStates[note] = state;
        }

        state.LaneId = laneId;

        if (state.Coroutine == null)
        {
            state.Coroutine = StartCoroutine(HoldEmissionCoroutine(note));
        }
    }

    public void UpdateHoldEmissionLane(NoteController note, int laneId)
    {
        if (note == null) return;
        if (!holdEmissionStates.ContainsKey(note)) return;
        BeginHoldEmission(note, laneId);
    }

    public void StopHoldEmission(NoteController note)
    {
        if (note == null) return;
        if (holdEmissionStates.TryGetValue(note, out var state))
        {
            if (state != null && state.Coroutine != null)
            {
                try { StopCoroutine(state.Coroutine); } catch { }
            }
            holdEmissionStates.Remove(note);
        }
    }

    private IEnumerator HoldEmissionCoroutine(NoteController note)
    {
        Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
        double nextSixteenthMs = -1d;
        while (enableHoldEmissionLoop)
        {
            if (note == null)
            {
                holdEmissionStates.Remove(note);
                yield break;
            }

            if (!holdEmissionStates.TryGetValue(note, out var state) || state == null)
            {
                yield break;
            }

            float bpm = conductor != null ? conductor.bpm : 0f;
            // Visual glow only: four pulses per former sixteenth-note interval.
            double intervalMs = bpm > 0.001f
                ? 3750d / bpm
                : Mathf.Max(10f, holdEmissionInterval * 250f);
            double songMs = conductor != null
                ? conductor.GetDspSongPositionMs()
                : Time.unscaledTimeAsDouble * 1000d;

            if (nextSixteenthMs < 0d)
            {
                nextSixteenthMs = (System.Math.Floor(songMs / intervalMs) + 1d) * intervalMs;
            }

            if (songMs + 0.5d >= nextSixteenthMs)
            {
                PlayHitEffect(note, state.LaneId, JudgmentResult.Perfect);
                // Advance by whole grid steps so a slow frame never causes a burst storm.
                do { nextSixteenthMs += intervalMs; }
                while (nextSixteenthMs <= songMs);
            }
            yield return null;
        }

        holdEmissionStates.Remove(note);
    }

    private Color ResolveColor(JudgmentResult result)
    {
        switch (result)
        {
            case JudgmentResult.Perfect:
                return perfectColor;
            case JudgmentResult.Great:
                return greatColor;
            case JudgmentResult.Good:
                return goodColor;
            default:
                return Color.white;
        }
    }

    private Color ResolveThemedColor(JudgmentResult result, bool isLeft)
    {
        Color theme = isLeft ? leftThemeColor : rightThemeColor;
        float intensity;
        switch (result)
        {
            case JudgmentResult.Perfect: intensity = 1.25f; break;
            case JudgmentResult.Great: intensity = 1f; break;
            case JudgmentResult.Good: intensity = 0.72f; break;
            default: intensity = 0.85f; break;
        }
        theme.r *= intensity;
        theme.g *= intensity;
        theme.b *= intensity;
        theme.a = 1f;
        return theme;
    }

    private void CacheDefaultModules(ParticleSystem system)
    {
        if (system == null || defaultModuleStates.ContainsKey(system)) return;

        // Cache the renderer alongside modules so ApplyTexture never calls GetComponent
        if (!_psRendererCache.ContainsKey(system))
        {
            var r = system.GetComponent<ParticleSystemRenderer>();
            if (r != null) _psRendererCache[system] = r;
        }

        var shape = system.shape;
        var snapshot = new ParticleSystemModulesSnapshot
        {
            shapeType = shape.shapeType,
            shapeScale = shape.scale,
            shapeAngle = shape.angle,
            shapeRadius = shape.radius
        };

        defaultModuleStates[system] = snapshot;
    }

    private void RestoreSystemDefaults(ParticleSystem system)
    {
        if (system == null) return;
        if (!defaultModuleStates.TryGetValue(system, out var snapshot)) return;

        var shape = system.shape;
        shape.shapeType = snapshot.shapeType;
        shape.scale = snapshot.shapeScale;
        shape.angle = snapshot.shapeAngle;
        shape.radius = snapshot.shapeRadius;

    }

    private void ApplySharpLinearFlight(ParticleSystem system, NoteController note)
    {
        if (!enableSharpLinearFlight || system == null) return;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        float width = ComputeSharpWidth(note);
        shape.scale = new Vector3(width, sharpSpawnThickness, sharpSpawnThickness);
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;

    }

    private void ApplyStandardFlight(ParticleSystem system, NoteController note)
    {
        if (system == null) return;

        if (adaptShapeToNoteWidth)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            float width = ComputeNoteWidth(note) * particleShapeWidthMultiplier;
            shape.scale = new Vector3(width, 0.04f, 0.04f);
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
        }
    }

    private float ComputeSharpWidth(NoteController note)
    {
        var nd = note?.NoteData;
        if (nd != null)
        {
            float trackWidth = PianoVisualLayout.ResolveTrackWidth(note != null ? note.TrackTransform : null);
            float derivedWidth = PianoVisualLayout.ResolveVisualWidth(nd, trackWidth) * sharpWidthMultiplier;
            return Mathf.Max(minSharpWidth, derivedWidth);
        }

        return Mathf.Max(minSharpWidth, laneSpacing * sharpWidthMultiplier);
    }

    private static Vector3 ResolveTrackNormal(NoteController note)
    {
        if (note != null)
        {
            var track = note.TrackTransform;
            if (track != null)
            {
                var trackUp = track.up;
                if (trackUp.sqrMagnitude > 1e-4f)
                {
                    return trackUp.normalized;
                }
            }

            var up = note.transform.up;
            if (up.sqrMagnitude > 1e-4f)
            {
                return up.normalized;
            }

            if (note.transform.parent != null)
            {
                var parentUp = note.transform.parent.up;
                if (parentUp.sqrMagnitude > 1e-4f)
                {
                    return parentUp.normalized;
                }
            }
        }

        return Vector3.up;
    }

    private static Vector3 ResolveTrackForward(NoteController note)
    {
        if (note != null)
        {
            var track = note.TrackTransform;
            if (track != null)
            {
                var forward = track.forward;
                if (forward.sqrMagnitude > 1e-4f)
                {
                    return forward.normalized;
                }
            }

            var noteForward = note.transform.forward;
            if (noteForward.sqrMagnitude > 1e-4f)
            {
                return noteForward.normalized;
            }
        }

        return Vector3.forward;
    }

    private float ComputeLaneOffsetX(NoteController note, int laneId)
    {
        if (note != null && note.NoteData != null && PianoVisualLayout.HasPianoPitch(note.NoteData))
        {
            float width = ComputeNoteWidth(note);
            return Random.Range(-width * 0.5f, width * 0.5f);
        }

        int minLane;
        int maxLane;

        if (note != null && note.NoteData != null)
        {
            minLane = Mathf.Min(note.NoteData.startLane, note.NoteData.endLane);
            maxLane = Mathf.Max(note.NoteData.startLane, note.NoteData.endLane);
        }
        else if (laneId >= 0)
        {
            minLane = maxLane = laneId;
        }
        else
        {
            return 0f;
        }

        minLane = Mathf.Clamp(minLane, 0, 27);
        maxLane = Mathf.Clamp(maxLane, 0, 27);
        if (maxLane < minLane)
        {
            (minLane, maxLane) = (maxLane, minLane);
        }

        float laneMinEdge = minLane - 0.5f;
        float laneMaxEdge = maxLane + 0.5f;
        float randomLane = Random.Range(laneMinEdge, laneMaxEdge);
        return (randomLane - 13f) * laneSpacing;
    }
}
