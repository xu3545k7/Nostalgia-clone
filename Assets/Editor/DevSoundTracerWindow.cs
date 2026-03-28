using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Editor window to manage DevSoundTracer.EnabledSignatures at edit- and play-time.
public class DevSoundTracerWindow : EditorWindow
{
    const string EditorPrefsKey = "DevSoundTracer.Signatures";

    string newSignature = "";
    Vector2 scroll;
    static HashSet<string> fallbackSet = new HashSet<string>();

    [MenuItem("Window/Dev Sound Tracer")]
    public static void ShowWindow()
    {
        var w = GetWindow<DevSoundTracerWindow>("Dev Sound Tracer");
        w.minSize = new Vector2(300, 200);
    }

    void OnEnable()
    {
        LoadFromPrefs();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("DevSoundTracer Enabled Signatures", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Add lowercase substrings to selectively allow SFX playback when the stacktrace contains any of them. Changes take effect immediately.", MessageType.Info);

        // List current signatures
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var set = GetSignatureSet();
        if (set.Count == 0)
        {
            EditorGUILayout.LabelField("(no signatures) - playback uses normal logic");
        }
        else
        {
            // create copy to avoid mutation issues while iterating
            var arr = set.ToArray();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                // nothing to do here since set represents enabled signatures already
            }
            if (GUILayout.Button("Deselect All"))
            {
                set.Clear();
                ApplyToDevSoundTracer(set);
                SaveToPrefs(set);
            }
            EditorGUILayout.EndHorizontal();

            foreach (var s in arr)
            {
                EditorGUILayout.BeginHorizontal();
                bool enabled = set.Contains(s);
                bool newEnabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(18));
                EditorGUILayout.LabelField(s);
                if (newEnabled != enabled)
                {
                    if (newEnabled) set.Add(s);
                    else set.Remove(s);
                    ApplyToDevSoundTracer(set);
                    SaveToPrefs(set);
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Add new signature (lowercase substring)", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        newSignature = EditorGUILayout.TextField(newSignature);
        if (GUILayout.Button("Add", GUILayout.Width(60)))
        {
            var v = (newSignature ?? "").Trim();
            if (!string.IsNullOrEmpty(v))
            {
                set.Add(v);
                newSignature = "";
                ApplyToDevSoundTracer(set);
                SaveToPrefs(set);
                Repaint();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear All"))
        {
            if (EditorUtility.DisplayDialog("Clear all signatures?", "Remove all enabled signatures (playback will use normal logic).", "Clear", "Cancel"))
            {
                set.Clear();
                ApplyToDevSoundTracer(set);
                SaveToPrefs(set);
            }
        }
        if (GUILayout.Button("Reload from Prefs"))
        {
            LoadFromPrefs();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Notes:");
        EditorGUILayout.LabelField("- Signatures are matched case-insensitive against the callstack.", EditorStyles.helpBox);
        EditorGUILayout.LabelField("- Use short substrings like 'finalizehold(' to target caller methods.", EditorStyles.helpBox);
    }

    static HashSet<string> GetSignatureSet()
    {
        try
        {
            return global::DevSoundTracer.EnabledSignatures;
        }
        catch
        {
            // If DevSoundTracer is not present for some reason, return a fallback set that persists in the window.
            return fallbackSet;
        }
    }

    static void ApplyToDevSoundTracer(HashSet<string> set)
    {
        try
        {
            var target = global::DevSoundTracer.EnabledSignatures;
            target.Clear();
            foreach (var s in set) target.Add(s);
        }
        catch
        {
            // DevSoundTracer not present; update fallback
            fallbackSet.Clear();
            foreach (var s in set) fallbackSet.Add(s);
        }
    }

    static void SaveToPrefs(HashSet<string> set)
    {
        try
        {
            var arr = set.ToArray();
            var json = JsonUtility.ToJson(new SerializationHelper { items = arr });
            EditorPrefs.SetString(EditorPrefsKey, json);
        }
        catch { }
    }

    void LoadFromPrefs()
    {
        try
        {
            if (!EditorPrefs.HasKey(EditorPrefsKey)) return;
            var json = EditorPrefs.GetString(EditorPrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;
            var helper = JsonUtility.FromJson<SerializationHelper>(json);
            if (helper == null || helper.items == null) return;
            var set = GetSignatureSet();
            set.Clear();
            foreach (var s in helper.items) if (!string.IsNullOrEmpty(s)) set.Add(s);
            Repaint();
        }
        catch { }
    }

    [System.Serializable]
    class SerializationHelper { public string[] items; }
}
