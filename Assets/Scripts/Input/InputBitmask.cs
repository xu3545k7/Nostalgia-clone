using System;
using UnityEngine;

/// <summary>
/// Aggregates per-lane input states into bitmasks for ultra low-latency polling.
/// Relies on <see cref="RealtimeInputBuffer"/> to supply edge information and
/// exposes the result as bitfields so gameplay systems can avoid per-frame
/// UnityEngine.Input queries.
/// </summary>
[DefaultExecutionOrder(-450)]
public class InputBitmask : MonoBehaviour
{
    public const int LaneCount = 28;

    public static InputBitmask Instance { get; private set; }

    private RealtimeInputBuffer buffer;

    [Tooltip("Keep lanes considered held for a brief period (ms) after a release to tolerate slide transitions and key bounce.")]
    [SerializeField, Range(0f, 30f)] private float releaseHangWindowMs = 6f;

    private uint currentMask;
    private uint previousMask;
    private uint pressedMask;
    private uint releasedMask;

    private readonly double[] releaseHangExpiry = new double[LaneCount];

    public static InputBitmask EnsureCreated()
    {
        if (Instance != null)
        {
            return Instance;
        }

        var existing = FindFirstObjectByType<InputBitmask>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("InputBitmask");
        DontDestroyOnLoad(go);
        return go.AddComponent<InputBitmask>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        buffer = RealtimeInputBuffer.EnsureCreated();
        ResetState();
    }

    private void OnEnable()
    {
        buffer = RealtimeInputBuffer.EnsureCreated();
        ResetState();
    }

    private void Update()
    {
        if (buffer == null)
        {
            buffer = RealtimeInputBuffer.Instance ?? RealtimeInputBuffer.EnsureCreated();
            if (buffer == null)
            {
                return;
            }
        }

        double now = GetRealtimeSinceStartup();
        previousMask = currentMask;
        currentMask = BuildMask(now);

        uint changed = previousMask ^ currentMask;
        pressedMask = changed & currentMask;
        releasedMask = changed & previousMask;

        for (int lane = 0; lane < LaneCount; lane++)
        {
            uint bit = 1u << lane;
            if (buffer.WasPressedThisFrame(lane))
            {
                pressedMask |= bit;
            }
        }
    }

    private uint BuildMask(double now)
    {
        uint mask = 0u;
        double hangSeconds = ReleaseHangSeconds;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if (buffer.IsPressed(lane))
            {
                mask |= (1u << lane);
                if (hangSeconds > 0.0)
                {
                    releaseHangExpiry[lane] = now + hangSeconds;
                }
            }
            else if (hangSeconds > 0.0 && now < releaseHangExpiry[lane])
            {
                mask |= (1u << lane);
            }
        }
        return mask;
    }

    private void ResetState()
    {
        previousMask = 0u;
        currentMask = 0u;
        pressedMask = 0u;
        releasedMask = 0u;
        double resetValue = double.NegativeInfinity;
        for (int i = 0; i < releaseHangExpiry.Length; i++)
        {
            releaseHangExpiry[i] = resetValue;
        }
    }

    private double ReleaseHangSeconds => releaseHangWindowMs > 0f ? releaseHangWindowMs / 1000.0 : 0.0;

    private static double GetRealtimeSinceStartup()
    {
#if UNITY_2020_2_OR_NEWER
        return Time.realtimeSinceStartupAsDouble;
#else
        return Time.realtimeSinceStartup;
#endif
    }

    public uint CurrentMask => currentMask;
    public uint PressedMask => pressedMask;
    public uint ReleasedMask => releasedMask;

    public bool IsPressed(int lane)
    {
        if ((uint)lane >= LaneCount)
        {
            return false;
        }
        return (currentMask & (1u << lane)) != 0u;
    }

    public bool WasPressedThisFrame(int lane)
    {
        if ((uint)lane >= LaneCount)
        {
            return false;
        }
        return (pressedMask & (1u << lane)) != 0u;
    }

    public bool WasReleasedThisFrame(int lane)
    {
        if ((uint)lane >= LaneCount)
        {
            return false;
        }
        return (releasedMask & (1u << lane)) != 0u;
    }
}
