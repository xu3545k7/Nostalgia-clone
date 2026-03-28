using UnityEngine;

public static class LogSuppressor
{
    // In non-editor builds, swallow all logs so builds produce no Console output.
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Suppress()
    {
        Application.logMessageReceived += HandleLog;
    }

    private static void HandleLog(string condition, string stackTrace, LogType type)
    {
        // Intentionally do nothing to suppress logs in builds.
    }
#endif
}
