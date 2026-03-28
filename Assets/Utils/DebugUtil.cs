using UnityEngine;

/// <summary>
/// Lightweight developer logging helper. Use DebugUtil.LogDev/Warning/Error for high-frequency
/// informational messages that should be no-ops in release builds.
/// </summary>
public static class DebugUtil
{
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void LogDev(string msg)
    {
        // Debug.Log(msg);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void LogDevWarning(string msg)
    {
        // Debug.LogWarning(msg);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void LogDevError(string msg)
    {
        Debug.LogError(msg);
    }
}
