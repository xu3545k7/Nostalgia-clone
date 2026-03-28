using System;
using UnityEngine;
using Judgment;

// Small static helpers to expose threshold values and IsLikelyHead logic
// to handlers without creating circular dependencies.
public static class JudgmentManagerStatic
{
    public static int GetPerfectMs()
    {
        try { return JudgmentManager.Instance != null ? JudgmentManager.Instance.perfectMs : 50; } catch { return 50; }
    }
    public static int GetGreatMs()
    {
        try { return JudgmentManager.Instance != null ? JudgmentManager.Instance.greatMs : 100; } catch { return 100; }
    }
    public static int GetGoodMs()
    {
        try { return JudgmentManager.Instance != null ? JudgmentManager.Instance.goodMs : 150; } catch { return 150; }
    }

    public static bool IsLikelyHeadStatic(NoteController note, float? songPosOverride = null)
    {
        try
        {
            // mirror JudgmentManager.IsLikelyHead behaviour
            if (note == null) return false;
            var nd = note.NoteData;
            if (nd == null) return false;
            float songPos = songPosOverride.HasValue ? songPosOverride.Value : 0f;
            if (!songPosOverride.HasValue && JudgmentManager.Instance != null)
            {
                try
                {
                    var mi = JudgmentManager.Instance.GetType().GetMethod("GetPreciseSongPositionMs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (mi != null)
                    {
                        var obj = mi.Invoke(JudgmentManager.Instance, null);
                        songPos = Convert.ToSingle(obj);
                    }
                }
                catch { songPos = 0f; }
            }
            float delta = Mathf.Abs(nd.startTime - songPos);
            return delta <= GetGoodMs();
        }
        catch { return false; }
    }
}
