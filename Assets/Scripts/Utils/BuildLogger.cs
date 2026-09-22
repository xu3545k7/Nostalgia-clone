using System.Diagnostics;
using UnityEngine;

public static class BuildLogger
{
    // Define NOSTALGIA_VERBOSE_LOGGING only for a dedicated diagnostics build.
    // Conditional removes both the call and interpolated-string allocation.
    [Conditional("NOSTALGIA_VERBOSE_LOGGING")]
    public static void Log(object message)
    {
        UnityEngine.Debug.Log(message);
    }

    [Conditional("NOSTALGIA_VERBOSE_LOGGING")]
    public static void Log(string message)
    {
        UnityEngine.Debug.Log(message);
    }

    // Warnings and errors are NOT stripped. They were editor-only, which meant a
    // failure in a shipped build produced no output whatever — a video that never
    // loaded, a file that was not found, an exception that was caught and logged:
    // all of it silently discarded, leaving nothing to diagnose from. They are
    // rare by construction, so there is no cost worth that blindness.
    public static void LogWarning(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }

    public static void LogError(string message)
    {
        UnityEngine.Debug.LogError(message);
    }

    public static void LogException(System.Exception ex)
    {
        UnityEngine.Debug.LogException(ex);
    }

    [Conditional("NOSTALGIA_VERBOSE_LOGGING")]
    public static void LogFormat(string format, params object[] args)
    {
        UnityEngine.Debug.LogFormat(format, args);
    }
}
