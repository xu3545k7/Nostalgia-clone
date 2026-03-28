using System;
using UnityEngine;
using System.IO;
using UnityEngine.Profiling;

/// <summary>
/// Lightweight runtime diagnostics to count Instantiate/Destroy/Despawn events
/// and approximate per-second GC allocation delta. Attach this MonoBehaviour
/// to a persistent GameObject at game start (you said you'll place it on an empty object).
/// </summary>
public class RuntimeDiagnostics : MonoBehaviour
{
    private static long lastTotalAllocated = 0;
    private static int instantiates = 0;
    private static int destroys = 0;
    private static int despawns = 0;
    private static long allocBytesAccum = 0;
    // Track maximum per-frame allocation seen within the current logging interval
    private static long maxFrameAlloc = 0;
    private static float maxFrameAllocTime = 0f; // realtimeSinceStartup when max observed
    // Option to immediately log per-frame allocation spikes (bytes)
    public bool enablePerFrameLogging = true;
    public int perFrameAllocLogThresholdBytes = 100 * 1024; // 100KB default
    // Optionally capture a stack trace when a spike is detected (useful to locate caller)
    public bool captureStackOnSpike = false;
    // Limit how often we capture stacks to avoid flood (seconds)
    public float stackCaptureCooldownSeconds = 1.0f;
    private float lastStackCaptureTime = -10f;
    // Ring buffer of recent stack samples (to help capture caller context when spike occurs)
    public bool enableStackSamplingBuffer = true;
    public float stackSampleIntervalSeconds = 0.2f;
    public int stackSampleBufferSize = 16;
    private string[] stackSampleBuffer;
    private float[] stackSampleTimes;
    private int stackSampleIndex = 0;
    private float lastStackSampleTime = 0f;

    // Exposed interval (seconds) for logging
    public float logIntervalSeconds = 1.0f;
    private float timer = 0f;

    // Static registration API
    public static void RegisterInstantiate()
    {
        instantiates++;
    }
    public static void RegisterDestroy()
    {
        destroys++;
    }
    public static void RegisterDespawn()
    {
        despawns++;
    }

    void Awake()
    {
        // initialize baseline
        try { lastTotalAllocated = Profiler.GetTotalAllocatedMemoryLong(); } catch { lastTotalAllocated = 0; }
    }

    void Update()
    {
        // Measure allocation delta this frame (approximate)
        try
        {
            long now = Profiler.GetTotalAllocatedMemoryLong();
            long delta = now - lastTotalAllocated;
            if (delta > 0)
            {
                allocBytesAccum += delta;
                if (delta > maxFrameAlloc)
                {
                    maxFrameAlloc = delta;
                    maxFrameAllocTime = Time.realtimeSinceStartup;
                }
                if (enablePerFrameLogging && delta >= perFrameAllocLogThresholdBytes)
                {
                    // To avoid spamming the Console (which itself can cause lag), write detailed spike info to a log file
                    string shortMsg = $"[RuntimeDiagnostics] High per-frame alloc: {delta} bytes at t={Time.realtimeSinceStartup:F3}s";
                    string logFilePath = null;
                    if (captureStackOnSpike && Time.realtimeSinceStartup - lastStackCaptureTime >= stackCaptureCooldownSeconds)
                    {
                        lastStackCaptureTime = Time.realtimeSinceStartup;
                        try
                        {
                            logFilePath = Path.Combine(Application.persistentDataPath, "RuntimeDiagnostics_spikes.log");
                            using (var sw = File.AppendText(logFilePath))
                            {
                                sw.WriteLine($"==== Spike at t={Time.realtimeSinceStartup:F3}s: alloc={delta} bytes ==== ");
                                if (enableStackSamplingBuffer && stackSampleBuffer != null)
                                {
                                    int count = Math.Min(stackSampleBuffer.Length, stackSampleBufferSize);
                                    int idx = stackSampleIndex;
                                    for (int i = 0; i < count; i++)
                                    {
                                        idx = (idx - 1 + stackSampleBuffer.Length) % stackSampleBuffer.Length;
                                        string s = stackSampleBuffer[idx];
                                        float t = stackSampleTimes[idx];
                                        if (!string.IsNullOrEmpty(s))
                                        {
                                            sw.WriteLine($"-- sample t={t:F3}s --\n{s}");
                                        }
                                    }
                                }
                                else
                                {
                                    string st = System.Environment.StackTrace;
                                    sw.WriteLine(st);
                                }
                                sw.WriteLine();
                            }
                        }
                        catch (System.Exception ex)
                        {
                            // If file writing fails, fall back to console warning (best-effort)
                            Debug.LogWarning($"[RuntimeDiagnostics] Failed to write spike log: {ex}");
                        }
                    }
                    if (!string.IsNullOrEmpty(logFilePath))
                    {
                        Debug.LogWarning(shortMsg + $" (details -> {logFilePath})");
                    }
                    else
                    {
                        Debug.LogWarning(shortMsg);
                    }
                }
            }
            lastTotalAllocated = now;
        }
        catch { }

        // Periodically sample and store a stacktrace into the ring buffer (cheap-ish but allocates)
        if (enableStackSamplingBuffer)
        {
            try
            {
                if (stackSampleBuffer == null || stackSampleBuffer.Length != stackSampleBufferSize)
                {
                    stackSampleBuffer = new string[stackSampleBufferSize];
                    stackSampleTimes = new float[stackSampleBufferSize];
                    stackSampleIndex = 0;
                }
                if (Time.realtimeSinceStartup - lastStackSampleTime >= stackSampleIntervalSeconds)
                {
                    lastStackSampleTime = Time.realtimeSinceStartup;
                    // Capture a stack trace snapshot
                    string s = System.Environment.StackTrace;
                    stackSampleBuffer[stackSampleIndex] = s;
                    stackSampleTimes[stackSampleIndex] = Time.realtimeSinceStartup;
                    stackSampleIndex = (stackSampleIndex + 1) % stackSampleBuffer.Length;
                }
            }
            catch { }
        }

        timer += Time.unscaledDeltaTime;
        if (timer >= logIntervalSeconds)
        {
            // Log a concise summary
            string s = $"[RuntimeDiagnostics] inst/s={instantiates}, destroy/s={destroys}, despawn/s={despawns}, allocBytes/s={allocBytesAccum}";
            // Append peak single-frame allocation information seen during the interval
            if (maxFrameAlloc > 0)
            {
                s += $", maxFrameAlloc={maxFrameAlloc}@{maxFrameAllocTime:F3}s";
            }
            Debug.Log(s);

            // Reset per-interval counters
            instantiates = 0;
            destroys = 0;
            despawns = 0;
            allocBytesAccum = 0;
            maxFrameAlloc = 0;
            maxFrameAllocTime = 0f;
            timer = 0f;
        }
    }
}
