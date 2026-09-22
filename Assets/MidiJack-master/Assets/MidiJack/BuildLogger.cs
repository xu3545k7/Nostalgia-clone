using System;
using System.Reflection;
using UnityEngine;

// Shim BuildLogger inside the MidiJack assembly so MidiJack code can call
// BuildLogger.* without creating an asmdef circular dependency.
// Behavior:
// - In Editor: try to call the project's real BuildLogger (if present) via reflection;
//   otherwise fall back to Debug.*.
// - In Player: always use Debug.* (no editor-only conditional logging behavior).
public static class BuildLogger
{
#if UNITY_EDITOR
    static readonly MethodInfo RealLogMethod;
    static readonly MethodInfo RealLogStringMethod;
    static readonly MethodInfo RealLogWarningMethod;
    static readonly MethodInfo RealLogErrorMethod;
    static readonly MethodInfo RealLogExceptionMethod;
    static readonly MethodInfo RealLogFormatMethod;

    static BuildLogger()
    {
        try
        {
            var currentAsm = typeof(BuildLogger).Assembly;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == currentAsm) continue;
                try
                {
                    var t = asm.GetType("BuildLogger", false, false);
                    if (t != null)
                    {
                        RealLogMethod = t.GetMethod("Log", new Type[] { typeof(object) }) ?? t.GetMethod("Log", new Type[] { typeof(string) });
                        RealLogStringMethod = t.GetMethod("Log", new Type[] { typeof(string) });
                        RealLogWarningMethod = t.GetMethod("LogWarning", new Type[] { typeof(string) });
                        RealLogErrorMethod = t.GetMethod("LogError", new Type[] { typeof(string) });
                        RealLogExceptionMethod = t.GetMethod("LogException", new Type[] { typeof(Exception) });
                        RealLogFormatMethod = t.GetMethod("LogFormat", new Type[] { typeof(string), typeof(object[]) });
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public static void Log(object message)
    {
        if (TryInvoke(RealLogMethod, message)) return;
        Debug.Log(message);
    }

    public static void Log(string message)
    {
        if (TryInvoke(RealLogStringMethod, message)) return;
        Debug.Log(message);
    }

    public static void LogWarning(string message)
    {
        if (TryInvoke(RealLogWarningMethod, message)) return;
        Debug.LogWarning(message);
    }

    public static void LogError(string message)
    {
        if (TryInvoke(RealLogErrorMethod, message)) return;
        Debug.LogError(message);
    }

    public static void LogException(Exception ex)
    {
        if (TryInvoke(RealLogExceptionMethod, ex)) return;
        Debug.LogException(ex);
    }

    public static void LogFormat(string format, params object[] args)
    {
        if (TryInvoke(RealLogFormatMethod, format, args)) return;
        Debug.LogFormat(format, args);
    }

    static bool TryInvoke(MethodInfo mi, params object[] args)
    {
        if (mi == null) return false;
        try
        {
            mi.Invoke(null, args);
            return true;
        }
        catch { return false; }
    }
#else
    public static void Log(object message) { Debug.Log(message); }
    public static void Log(string message) { Debug.Log(message); }
    public static void LogWarning(string message) { Debug.LogWarning(message); }
    public static void LogError(string message) { Debug.LogError(message); }
    public static void LogException(Exception ex) { Debug.LogException(ex); }
    public static void LogFormat(string format, params object[] args) { Debug.LogFormat(format, args); }
#endif
}
