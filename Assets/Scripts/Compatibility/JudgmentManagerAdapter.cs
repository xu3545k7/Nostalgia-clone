using System;
using UnityEngine;

// Compatibility adapter to allow legacy code that references a global
// `JudgmentManager` component (no namespace) to compile while the real
// implementation lives in the `Judgment` namespace.
// This adapter forwards calls to `Judgment.JudgmentManager.Instance` where available,
// and provides safe no-op fallbacks when not present.
[UnityEngine.DisallowMultipleComponent]
public class JudgmentManager : UnityEngine.MonoBehaviour
{
    private static JudgmentManager _instance;
    public static JudgmentManager Instance
    {
        get
        {
            if (_instance != null) return _instance;
            // Prefer an existing adapter component in the scene
            _instance = UnityEngine.Object.FindFirstObjectByType<JudgmentManager>();
            if (_instance != null) return _instance;
            // If no adapter exists, create one (keeps compatibility with serialized scenes that expect a component)
            var go = new UnityEngine.GameObject("JudgmentManager_Adapter");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<JudgmentManager>();
            try { BuildLogger.Log("[JAdp] Created JudgmentManager_Adapter GameObject and adapter instance"); } catch { }
            // Ensure namespaced Judgment.JudgmentManager exists in the scene; create if missing so forwarding works.
            try
            {
                var ns = Judgment.JudgmentManager.Instance;
                if (ns == null)
                {
                    var go2 = new UnityEngine.GameObject("JudgmentManager_Runtime");
                    UnityEngine.Object.DontDestroyOnLoad(go2);
                    go2.AddComponent(typeof(Judgment.JudgmentManager));
                    try { BuildLogger.Log("[JAdp] Created namespaced Judgment.JudgmentManager at runtime"); } catch { }
                }
            }
            catch { }
            return _instance;
        }
    }

    private void EnsureNamespacedManagerExists()
    {
        try
        {
            var ns = Judgment.JudgmentManager.Instance;
            if (ns == null)
            {
                var go2 = new UnityEngine.GameObject("JudgmentManager_Runtime");
                UnityEngine.Object.DontDestroyOnLoad(go2);
                go2.AddComponent(typeof(Judgment.JudgmentManager));
                try { BuildLogger.Log("[JAdp] EnsureNamespacedManagerExists created Judgment.JudgmentManager"); } catch { }
            }
        }
        catch { }
    }

    public int perfectMs
    {
        get
        {
            try
            {
                var jm = Judgment.JudgmentManager.Instance;
                if (jm == null) return 50;
                var pi = jm.GetType().GetProperty("perfectMs");
                if (pi != null) return Convert.ToInt32(pi.GetValue(jm));
                var fi = jm.GetType().GetField("perfectMs");
                if (fi != null) return Convert.ToInt32(fi.GetValue(jm));
            }
            catch { }
            return 50;
        }
    }

    public int greatMs
    {
        get
        {
            try
            {
                var jm = Judgment.JudgmentManager.Instance;
                if (jm == null) return 100;
                var pi = jm.GetType().GetProperty("greatMs");
                if (pi != null) return Convert.ToInt32(pi.GetValue(jm));
                var fi = jm.GetType().GetField("greatMs");
                if (fi != null) return Convert.ToInt32(fi.GetValue(jm));
            }
            catch { }
            return 100;
        }
    }

    public int goodMs
    {
        get
        {
            try
            {
                var jm = Judgment.JudgmentManager.Instance;
                if (jm == null) return 150;
                var pi = jm.GetType().GetProperty("goodMs");
                if (pi != null) return Convert.ToInt32(pi.GetValue(jm));
                var fi = jm.GetType().GetField("goodMs");
                if (fi != null) return Convert.ToInt32(fi.GetValue(jm));
            }
            catch { }
            return 150;
        }
    }

    private static int GetInt(Func<int> getter, int @default)
    {
        try { return getter(); } catch { return @default; }
    }

    public void ResetStats()
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm == null) return;
            var mi = jm.GetType().GetMethod("ResetStats");
            if (mi != null) { mi.Invoke(jm, null); return; }
        }
        catch { }
    }

    public void EnsureHudVisible()
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm == null) return;
            var mi = jm.GetType().GetMethod("EnsureHudVisible");
            if (mi != null) { mi.Invoke(jm, null); return; }
        }
        catch { }
    }

    public void RegisterNote(NoteController n)
    {
        try { Judgment.JudgmentManager.Instance?.RegisterNote(n); } catch { }
    }

    public void UnregisterNote(NoteController n)
    {
        try { Judgment.JudgmentManager.Instance?.UnregisterNote(n); } catch { }
    }

    public void RecordFail(NoteController n)
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm != null)
            {
                var mi = jm.GetType().GetMethod("RecordFail");
                if (mi != null) { mi.Invoke(jm, new object[] { n }); return; }
            }
        }
        catch { }
        // Fallback: mark the note as failed locally
        try { BuildLogger.Log($"[JAdp] RecordFail fallback invoked for note={(n!=null? n.name : "<null>")}"); } catch { }
        try { n?.OnJudged(JudgmentResult.Fail); } catch { }
    }

    public void AutoStartHold(NoteController n, float startSongPos)
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm != null)
            {
                var mi = jm.GetType().GetMethod("AutoStartHold");
                if (mi != null) { mi.Invoke(jm, new object[] { n, startSongPos }); return; }
            }
        }
        catch { }
    }

    public void AutoJudgeTap(NoteController n)
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm != null)
            {
                var mi = jm.GetType().GetMethod("AutoJudgeTap");
                if (mi != null) { mi.Invoke(jm, new object[] { n }); return; }
            }
        }
        catch { }
        // Fallback: award a perfect in compatibility mode
        try { BuildLogger.Log($"[JAdp] AutoJudgeTap fallback invoked for note={(n!=null? n.name : "<null>")}"); } catch { }
        try { n?.OnJudged(JudgmentResult.Perfect); } catch { }
    }

    public void ProcessButtonPress(int buttonId)
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm != null)
            {
                var mi = jm.GetType().GetMethod("ProcessButtonPress");
                if (mi != null) { mi.Invoke(jm, new object[] { buttonId }); return; }
            }
        }
        catch { }
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try { BuildLogger.Log($"[JAdp] ProcessButtonPress fallback for buttonId={buttonId} (no namespaced manager available)"); } catch { }
        #endif
        // Fallback: nothing to do in compatibility mode if manager doesn't implement it.
    }

    public void FlushPendingJudgments()
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm == null) return;
            var mi = jm.GetType().GetMethod("FlushPendingJudgments");
            if (mi != null) { mi.Invoke(jm, null); return; }
        }
        catch { }
    }

    public (int perfect, int great, int good, int miss, int fail, int combo, int maxCombo, int score) GetStatsWithCombo()
    {
        try
        {
            // Try to forward if underlying manager implements this
            var jm = Judgment.JudgmentManager.Instance;
            if (jm == null) return (0,0,0,0,0,0,0,0);
            var method = jm.GetType().GetMethod("GetStatsWithCombo");
            if (method != null)
            {
                var result = method.Invoke(jm, null);
                return ((int, int, int, int, int, int, int, int))result;
            }
        }
        catch { }
        return (0,0,0,0,0,0,0,0);
    }

    public (int fast, int late) GetTimingStats()
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm == null) return (0,0);
            var method = jm.GetType().GetMethod("GetTimingStats");
            if (method != null)
            {
                var result = method.Invoke(jm, null);
                return ((int, int))result;
            }
        }
        catch { }
        return (0,0);
    }

    // Reflection helper to expose internal precise song position if requested
    public float GetPreciseSongPositionMs()
    {
        try
        {
            var jm = Judgment.JudgmentManager.Instance;
            if (jm == null) return 0f;
            var mi = jm.GetType().GetMethod("GetPreciseSongPositionMs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (mi != null)
            {
                var obj = mi.Invoke(jm, null);
                return Convert.ToSingle(obj);
            }
        }
        catch { }
        return 0f;
    }
}
