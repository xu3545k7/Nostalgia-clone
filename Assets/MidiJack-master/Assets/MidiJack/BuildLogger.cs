using System;
using System.Reflection;
using UnityEngine;

// **這個 shim 一定要留在 MidiJack 命名空間裡。** 它和專案自己的 BuildLogger
// 同名，而 Nostalgia.Runtime 參考 MidiJack —— 兩個都在全域命名空間的時候，
// 專案裡每一個 BuildLogger 呼叫都會吃到一條 CS0436「型別衝突」警告（實測 257
// 條，Console 幾乎被它灌滿）。包進命名空間之後：MidiJack 自己的檔案（都宣告
// namespace MidiJack）照樣解析到這一份，專案端解析到自己那一份，誰都不用改。
namespace MidiJack
{
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
}
