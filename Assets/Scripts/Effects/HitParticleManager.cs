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
    [SerializeField] private ParticleSystem particlePrefab;
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

    [Header("Emission Tweaks")]
    [SerializeField] private Vector2 startSizeRange = new Vector2(0.35f, 0.7f);
    [SerializeField] private Vector2 startSpeedRange = new Vector2(2.5f, 4.5f);
    [SerializeField] private Vector2 lifetimeRange = new Vector2(0.25f, 0.55f);
    [SerializeField] private float rotationJitter = 15f;

    [Header("Spawn Placement")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.05f, 0f);
    [SerializeField, Tooltip("How much to offset particles horizontally between each lane when a lane id is provided.")]
    private float laneSpacing = 0.08f;

    [Header("Laser Beam")]
    [SerializeField, Tooltip("If true, spawns the LK laser beam along the same direction as the particles.")]
    private bool enableLaserBeam = true;
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
    [SerializeField, Tooltip("Seconds between each hold emission burst."), Min(0.01f)]
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
        public bool velocityEnabled;
        public ParticleSystemSimulationSpace velocitySpace;
        public ParticleSystem.MinMaxCurve velocityX;
        public ParticleSystem.MinMaxCurve velocityY;
        public ParticleSystem.MinMaxCurve velocityZ;
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

    private void PrewarmPool(int count)
    {
        if (particlePrefab == null || count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            availableSystems.Enqueue(CreateSystemInstance());
        }
    }

    private ParticleSystem CreateSystemInstance()
    {
        var instance = Instantiate(particlePrefab, poolRoot);
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
        if (particlePrefab == null)
        {
            Debug.LogWarning("HitParticleManager: particlePrefab is not assigned.");
            return null;
        }

        if (availableSystems.Count == 0)
        {
            availableSystems.Enqueue(CreateSystemInstance());
        }

        var system = availableSystems.Dequeue();
        activeSystems.Add(system);
        RestoreSystemDefaults(system);
        return system;
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
        if (laneId < 0 && note.NoteData != null)
        {
            laneId = note.NoteData.startLane;
        }

        spawnPos.x += ComputeLaneOffsetX(note, laneId);
        Vector3 trackNormal = ResolveTrackNormal(note);

        bool isSharp = note.IsStaccato;
        bool isLeft = DetermineIsLeftLane(laneId, note);

        var main = system.main;
        if (isSharp && enableSharpLinearFlight)
        {
            main.startSize = Random.Range(sharpSizeRange.x, sharpSizeRange.y);
            main.startSpeed = 0f;
            main.startLifetime = Random.Range(sharpLifetimeRange.x, sharpLifetimeRange.y);
            ApplySharpLinearFlight(system, note, trackNormal);
        }
        else
        {
            main.startSize = Random.Range(startSizeRange.x, startSizeRange.y);
            main.startSpeed = 0f;
            main.startLifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);
            ApplyStandardFlight(system, trackNormal);
        }
        main.startRotation = Random.Range(-rotationJitter, rotationJitter) * Mathf.Deg2Rad;
        main.startColor = ResolveColor(result);

        ApplyTexture(system, isSharp, isLeft);

        var t = system.transform;
        t.position = spawnPos;
        Vector3 lookDir = trackNormal.sqrMagnitude > 1e-4f ? trackNormal.normalized : Vector3.up;
        if (lookDir.sqrMagnitude < 1e-4f)
        {
            lookDir = Vector3.up;
        }
        t.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
        system.gameObject.SetActive(true);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Play(true);
        StartCoroutine(ReturnWhenFinished(system));

        try
        {
            JudgmentLineGlow.Instance?.TriggerGlow();
        }
        catch { }

        EmitLaserBeam(note, spawnPos, trackNormal, result);
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

    private void EmitLaserBeam(NoteController note, Vector3 spawnPos, Vector3 trackNormal, JudgmentResult result)
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
        Color beamColor = overrideLaserColor ? laserOverrideColor : ResolveColor(result);
        beam.Play(start, trackForward, beamColor, length, duration, width, trackNormal);
    }

    private float ComputeNoteWidth(NoteController note)
    {
        var nd = note?.NoteData;
        if (nd == null)
        {
            return laneSpacing;
        }

        int span = Mathf.Max(1, Mathf.Abs(nd.endLane - nd.startLane) + 1);
        float width = span * laneSpacing * Mathf.Max(0.01f, laserWidthMultiplier);

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
        var wait = new WaitForSeconds(holdEmissionInterval);
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

            PlayHitEffect(note, state.LaneId, JudgmentResult.Perfect);
            yield return wait;
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
        var velocity = system.velocityOverLifetime;

        var snapshot = new ParticleSystemModulesSnapshot
        {
            shapeType = shape.shapeType,
            shapeScale = shape.scale,
            shapeAngle = shape.angle,
            shapeRadius = shape.radius,
            velocityEnabled = velocity.enabled,
            velocitySpace = velocity.space,
            velocityX = velocity.x,
            velocityY = velocity.y,
            velocityZ = velocity.z
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

        var velocity = system.velocityOverLifetime;
        velocity.enabled = snapshot.velocityEnabled;
        velocity.space = snapshot.velocitySpace;
        velocity.x = snapshot.velocityX;
        velocity.y = snapshot.velocityY;
        velocity.z = snapshot.velocityZ;
    }

    private void ApplySharpLinearFlight(ParticleSystem system, NoteController note, Vector3 trackNormal)
    {
        if (!enableSharpLinearFlight || system == null) return;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        float width = ComputeSharpWidth(note);
        shape.scale = new Vector3(width, sharpSpawnThickness, sharpSpawnThickness);
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;

        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        Vector3 forward = trackNormal.sqrMagnitude > 1e-4f ? trackNormal.normalized : ResolveTrackNormal(note);
        float speed = Random.Range(sharpSpeedRange.x, sharpSpeedRange.y);
        velocity.x = new ParticleSystem.MinMaxCurve(forward.x * speed);
        velocity.y = new ParticleSystem.MinMaxCurve(forward.y * speed);
        velocity.z = new ParticleSystem.MinMaxCurve(forward.z * speed);
    }

    private void ApplyStandardFlight(ParticleSystem system, Vector3 trackNormal)
    {
        if (system == null) return;

        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;

        Vector3 dir = trackNormal.sqrMagnitude > 1e-4f ? trackNormal.normalized : Vector3.up;
        float speed = Random.Range(startSpeedRange.x, startSpeedRange.y);
        velocity.x = new ParticleSystem.MinMaxCurve(dir.x * speed);
        velocity.y = new ParticleSystem.MinMaxCurve(dir.y * speed);
        velocity.z = new ParticleSystem.MinMaxCurve(dir.z * speed);
    }

    private float ComputeSharpWidth(NoteController note)
    {
        var nd = note?.NoteData;
        int span = 1;
        if (nd != null)
        {
            span = Mathf.Max(1, nd.endLane - nd.startLane + 1);
        }

        float derived = span * laneSpacing * sharpWidthMultiplier;
        return Mathf.Max(minSharpWidth, derived);
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
