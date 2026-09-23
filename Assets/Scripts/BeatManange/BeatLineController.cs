using UnityEngine;
#pragma warning disable CS0414

public class BeatLineController : MonoBehaviour
{
    public float startTime;
    private float spawnZ;
    private float judgmentZ = -2.0f;
    // Cached JudgmentLine transform so beat lines converge onto the SAME (possibly camera-anchored) line
    // as the notes, instead of a hardcoded Z plane. STATIC + shared across all beat lines: GameObject.Find
    // is expensive, so we resolve it at most once (it becomes Unity-null on scene change → re-resolved).
    private static Transform _judgmentLine;
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
        // Resolve the judgment line's Z so we converge onto the same line as the notes. Fall back to the
        // default plane if the object isn't found.
        if (_judgmentLine == null) { var jl = GameObject.Find("JudgmentLine"); if (jl != null) _judgmentLine = jl.transform; }
        if (_judgmentLine != null) judgmentZ = _judgmentLine.position.z;
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

    // Use the same visual clock as notes/TRACK, including the player's
    // MusicPlaybackOffset and pause state.
    float timingSongPos = conductor.effectiveSongPosition;
    float songPos = conductor.renderSongPosition;
        float currentSpeed = (beatLineSpawner != null) ? beatLineSpawner.Speed : 30f;

        // If reached or passed, return to pool
        if (timingSongPos >= startTime)
        {
            try { JudgmentLineGlow.GetOrCreate()?.TriggerBeatPulse(); } catch { }
            if (beatLineSpawner != null) beatLineSpawner.ReturnBeatLine(gameObject);
            else Destroy(gameObject);
            return;
        }

        // Track the (possibly camera-anchored) judgment line's current Z so beat lines meet it exactly.
        if (_judgmentLine != null) judgmentZ = _judgmentLine.position.z;

        // Calculate the target position directly from DSP timing.
        float timeToStart = startTime - songPos;
        float targetWorldZ = judgmentZ + (timeToStart / 1000f) * currentSpeed;
        // renderSongPosition is already continuous, so write the deterministic
        // target directly. A second MoveTowards follower introduces a visible
        // catch-up/snap cycle on long horizontal lines.
        float newWorldZ = targetWorldZ;

        // 拋物線模式：拍子線和音符查**同一張表**（NoteArcScreen）。那張表是從
        // 「這一刻該出現在畫面的哪個高度」反解出來的；各自用世界座標算的話，
        // 音符就會離開自己的格線。
        float arcOffset = 0f;
        float arcLateral = 1f;
        SettingsManager arcSettings = SettingsManager.Instance;
        float arcShare = arcSettings != null ? arcSettings.EffectiveNoteArcHeight : 0f;
        if (arcShare > 0f)
        {
            if (!_hasArcBaseY)
            {
                _arcBaseY = transform.position.y;
                _arcBaseScaleX = transform.localScale.x;
                _hasArcBaseY = true;
            }
            // 長度用設定裡的，不是這一條自己的生成距離——每一條拍子線生成時
            // 離判定線的距離都不一樣，各自帶各自的會讓這張共用表每幀跳來跳去。
            NoteArcScreen.Prepare(Camera.main, judgmentZ,
                                  arcSettings.ArcTravelWorldUnits(), _arcBaseY,
                                  arcSettings.JudgmentLineScreenHeight, arcShare,
                                  arcSettings.ArcSpawnWorldUnits());
            if (NoteArcScreen.Active)
            {
                arcOffset = NoteArcScreen.OffsetAtZ(targetWorldZ);
                ApplyArcFade(targetWorldZ);
                // 拍子線橫跨整個跑道，音符被橫向補正拉寬多少，它也要拉寬多少，
                // 不然音符會跑到線的外面去。
                arcLateral = NoteArcScreen.LateralAtZ(targetWorldZ);
            }
        }

        // Apply the calculated world Z position, adjusting for parent transforms when necessary.
        if (_cachedParent != null)
        {
            float localZ = (_cachedParentScaleZ != 0f) ? (newWorldZ - _cachedParent.position.z) / _cachedParentScaleZ : 0f;
            Vector3 localPos = transform.localPosition;
            localPos.z = localZ;
            if (_hasArcBaseY)
            {
                float parentY = _cachedParent.position.y;
                float scaleY = _cachedParent.lossyScale.y;
                if (Mathf.Abs(scaleY) > 0.0001f)
                    localPos.y = (_arcBaseY + arcOffset - parentY) / scaleY;
            }
            transform.localPosition = localPos;
        }
        else
        {
            Vector3 worldPos = transform.position;
            worldPos.z = newWorldZ;
            if (_hasArcBaseY) worldPos.y = _arcBaseY + arcOffset;
            transform.position = worldPos;
        }
        if (_hasArcBaseY)
        {
            Vector3 scale = transform.localScale;
            scale.x = _arcBaseScaleX * arcLateral;
            transform.localScale = scale;
        }

        // safety: if long past startTime, ensure cleanup
        if (timingSongPos > startTime + 500f)
        {
            if (beatLineSpawner != null) beatLineSpawner.ReturnBeatLine(gameObject);
            else Destroy(gameObject);
        }
    }

    private SpriteRenderer _arcSprite;
    private bool _arcSpriteResolved;
    private float _arcFadeWritten = -1f;
    private float _arcFadeBaseAlpha = 1f;

    /// <summary>拍子線也跟著淡入，不然它會比音符早一截憑空出現。</summary>
    private void ApplyArcFade(float worldZ)
    {
        if (!_arcSpriteResolved)
        {
            _arcSpriteResolved = true;
            _arcSprite = GetComponentInChildren<SpriteRenderer>(true);
        }
        if (_arcSprite == null) return;
        float fade = NoteArcScreen.FadeAtZ(worldZ);
        Color c = _arcSprite.color;
        if (_arcFadeWritten < 0f || Mathf.Abs(c.a - _arcFadeWritten) > 0.001f)
            _arcFadeBaseAlpha = c.a;
        float a = _arcFadeBaseAlpha * fade;
        if (Mathf.Abs(c.a - a) > 0.001f)
        {
            c.a = a;
            _arcSprite.color = c;
        }
        _arcFadeWritten = a;
    }

    /// <summary>拍子線沒有弧線時該在的 Y（第一次套用弧線前記起來）。</summary>
    private float _arcBaseY;
    /// <summary>同理的 X 縮放。橫向補正是乘上去的，所以不能就地累乘。</summary>
    private float _arcBaseScaleX = 1f;
    private bool _hasArcBaseY;

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

