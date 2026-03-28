using UnityEngine;
#pragma warning disable CS0414

public class BeatLineController : MonoBehaviour
{
    public float startTime;
    private float spawnZ;
    private float judgmentZ = -2.0f;
    private Conductor conductor;
    private BeatLineSpawner beatLineSpawner;
    private float travelMs = 0f;
    [SerializeField]
    private float minPositionAlpha = 0.02f;
    [SerializeField]
    private float positionAlphaPower = 3f;
        [SerializeField]
        private float positionSmoothSpeed = 20f; // units/sec smoothing parameter (higher -> faster)
    // cached parent transform and scale to avoid per-frame lossyScale lookups
    private Transform _cachedParent;
    private float _cachedParentScaleZ = 1f;

    public void Initialize(float startTime, BeatLineSpawner spawner, float timeToBeatMs)
    {
        this.startTime = startTime;
        this.conductor = GameManager.Instance.Conductor;
        this.beatLineSpawner = spawner;

    float initialSpeed = (beatLineSpawner != null) ? beatLineSpawner.Speed : 30f;
    float travelSeconds = Mathf.Max(0f, timeToBeatMs) / 1000f;
        // Spawn distance equals time-to-judgment based on actual remaining time when spawned
        this.spawnZ = judgmentZ + travelSeconds * initialSpeed;
    this.travelMs = travelSeconds * 1000f;

        // Place the beat line relative to its parent using localPosition so it remains
        // correct even if the parent container has a non-zero world transform
        // spawnZ is in world-space. If this beatline is parented to a container that has a non-1
        // lossyScale, writing spawnZ directly into localPosition will produce an incorrect world Z
        // (worldZ = parent.position.z + localZ * parent.lossyScale.z). Compensate so that the
        // resulting world position matches spawnZ.
        if (transform.parent != null)
        {
            var p = transform.parent;
            float parentScaleZ = Mathf.Abs(p.lossyScale.z) > 1e-6f ? p.lossyScale.z : 1f;
            float parentPosZ = p.position.z;
            float localZ = (spawnZ - parentPosZ) / parentScaleZ;
            transform.localPosition = new Vector3(0f, 0.1f, localZ);
            // cache parent info
            _cachedParent = p;
            _cachedParentScaleZ = parentScaleZ;
        }
        else
        {
            transform.localPosition = new Vector3(0f, 0.1f, spawnZ);
            _cachedParent = null;
            _cachedParentScaleZ = 1f;
        }

        // Editor/debug instrumentation: log local/world placement so we can compare
        try
        {
            if (Debug.isDebugBuild)
            {
                string parentName = transform.parent != null ? transform.parent.name : "<null>";
                Vector3 worldPos = transform.position;
                Vector3 parentLossy = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
                float parentPosZ = transform.parent != null ? transform.parent.position.z : 0f;
                // Debug.Log($"[DEBUG_BEAT] Init beat '{gameObject.name}' judgmentZ={judgmentZ:F3} spawnZ={spawnZ:F3} localZ={transform.localPosition.z:F3} worldZ={worldPos.z:F3} parent={parentName} parentPosZ={parentPosZ:F3} parentLossyScale={parentLossy}");
            }
        }
        catch { }
#if false
    try {
        var p = transform.parent;
        Debug.Log($"[BeatLineController] Initialize: name={gameObject.name}, worldPos={transform.position}, localPos={transform.localPosition}, parent={(p!=null?p.name:"<null>")}, parentLossy={(p!=null?p.lossyScale:Vector3.one)}");
    } catch {}
#endif
    // Preserve the scale from the prefab/spawner. The spawner will apply
    // parent-lossy compensation and desired world size. Avoid overriding
    // localScale here which would negate those corrections.
    }

    // Called by pool when returning object to ensure transient state is cleaned (kept for compatibility)
    public void CleanupPooled()
    {
        // reset any transient state like velocity, children, etc.
        // currently nothing heavy to cleanup, placeholder for future
    }

    // Backwards-compatible setter used by pools when pre-instantiating objects.
    public void SetOwningPool(BeatLinePool pool)
    {
        // No-op for now; spawner manages return-to-pool via ReturnBeatLine.
    }

    void Update()
    {
        // early-out if conductor not ready
        if (conductor == null || !conductor.isActive) return;

    float songPos = conductor.GetDspSongPositionMs();
        float currentSpeed = (beatLineSpawner != null) ? beatLineSpawner.Speed : 30f;

        // If reached or passed, return to pool
        if (songPos >= startTime)
        {
            if (beatLineSpawner != null) beatLineSpawner.ReturnBeatLine(gameObject);
            else Destroy(gameObject);
            return;
        }

        // Calculate the target position directly from DSP timing.
        float timeToStart = startTime - songPos;
        float targetWorldZ = judgmentZ + (timeToStart / 1000f) * currentSpeed;
        float currentWorldZ = transform.position.z;
        float maxStep = currentSpeed * Time.deltaTime;
        float newWorldZ = Mathf.MoveTowards(currentWorldZ, targetWorldZ, maxStep);
        if (Mathf.Abs(targetWorldZ - newWorldZ) <= 0.0001f) newWorldZ = targetWorldZ;

        // Apply the calculated world Z position, adjusting for parent transforms when necessary.
        if (_cachedParent != null)
        {
            float localZ = (_cachedParentScaleZ != 0f) ? (newWorldZ - _cachedParent.position.z) / _cachedParentScaleZ : 0f;
            Vector3 localPos = transform.localPosition;
            localPos.z = localZ;
            transform.localPosition = localPos;
        }
        else
        {
            Vector3 worldPos = transform.position;
            worldPos.z = newWorldZ;
            transform.position = worldPos;
        }

        // safety: if long past startTime, ensure cleanup
        if (songPos > startTime + 500f)
        {
            if (beatLineSpawner != null) beatLineSpawner.ReturnBeatLine(gameObject);
            else Destroy(gameObject);
        }
    }

    // Pending position fields for deferred transform writes
    private Vector3 _pendingLocalPosition;
    private bool _hasPendingPosition = false;

    void LateUpdate()
    {
        if (_hasPendingPosition)
        {
            transform.localPosition = _pendingLocalPosition;
            _hasPendingPosition = false;
        }
    }
}

