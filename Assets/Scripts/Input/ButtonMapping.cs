using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class BottonEntry
{
    public int botton_id;
    public string botton_name;
}

[Serializable]
public class BottonGroup
{
    public string Name;
    public List<BottonEntry> botton_setting;
}

[Serializable]
public class BottonRoot
{
    public List<BottonGroup> bottons;
}

/// <summary>
/// Loads and exposes button mappings stored in Resources/botton_setting.json
/// Format expected: { "bottons": [ { "Name": "...", "botton_setting": [ { "botton_id": 0, "botton_name": "Q" }, ... ] } ] }
/// </summary>
public class ButtonMapping
{
    private Dictionary<int, string> idToName = new Dictionary<int, string>();

    public bool LoadFromResources(string resourceName = "botton_setting")
    {
        try
        {
            TextAsset ta = Resources.Load<TextAsset>(resourceName);
            if (ta == null) return false;
            BottonRoot root = JsonUtility.FromJson<BottonRoot>(ta.text);
            if (root == null || root.bottons == null || root.bottons.Count == 0) return false;
            var group = root.bottons[0];
            idToName.Clear();
            if (group.botton_setting != null)
            {
                foreach (var e in group.botton_setting)
                {
                    if (e != null)
                    {
                        idToName[e.botton_id] = e.botton_name;
                    }
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"ButtonMapping.LoadFromResources failed: {ex}");
            #endif
            return false;
        }
    }

    public string GetNameForId(int id)
    {
        if (idToName.TryGetValue(id, out var n)) return n;
        return null;
    }

    // Try find an id for the given name (case-insensitive). Returns first matching id.
    public bool TryGetIdForName(string name, out int id)
    {
        id = -1;
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var kv in idToName)
        {
            if (string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                id = kv.Key;
                return true;
            }
        }
        return false;
    }

    // Try to convert stored name (single char like "Q" or digit "7" or bracket "[") to a KeyCode
    public static bool TryGetKeyCodeFromName(string name, out KeyCode key)
    {
        key = KeyCode.None;
        if (string.IsNullOrEmpty(name)) return false;
        name = name.Trim();
        // single letter
        if (name.Length == 1)
        {
            char c = name[0];
            if (char.IsLetter(c))
            {
                string enumName = char.ToUpperInvariant(c).ToString();
                if (Enum.TryParse<KeyCode>(enumName, out key)) return true;
            }
            if (char.IsDigit(c))
            {
                // map to Alpha0..Alpha9
                string enumName = "Alpha" + c;
                if (Enum.TryParse<KeyCode>(enumName, out key)) return true;
            }
            // punctuation
            if (c == '[') { key = KeyCode.LeftBracket; return true; }
            if (c == ']') { key = KeyCode.RightBracket; return true; }
            if (c == ';') { key = KeyCode.Semicolon; return true; }
            if (c == '\'') { key = KeyCode.Backslash; return true; }
            if (c == ',') { key = KeyCode.Comma; return true; }
            if (c == '.') { key = KeyCode.Period; return true; }
            if (c == '/') { key = KeyCode.Slash; return true; }
            if (c == '-') { key = KeyCode.Minus; return true; }
            if (c == '=') { key = KeyCode.Equals; return true; }
        }
        // attempt parse by name directly
        if (Enum.TryParse<KeyCode>(name, true, out key)) return true;
        return false;
    }

    // Try convert the stored name to Input System Key (if available)
    public static bool TryGetInputSystemKeyFromName(string name, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrEmpty(name)) return false;
        name = name.Trim();
        if (name.Length == 1)
        {
            char c = name[0];
            if (char.IsLetter(c))
            {
                string enumName = char.ToUpperInvariant(c).ToString();
                if (Enum.TryParse<Key>(enumName, out key)) return true;
            }
            if (char.IsDigit(c))
            {
                // Key.Digit0 ... Digit9
                string enumName = "Digit" + c;
                if (Enum.TryParse<Key>(enumName, out key)) return true;
            }
            if (c == '[') { key = Key.LeftBracket; return true; }
            if (c == ']') { key = Key.RightBracket; return true; }
            if (c == ';') { key = Key.Semicolon; return true; }
            if (c == '\\') { key = Key.Backslash; return true; }
            if (c == ',') { key = Key.Comma; return true; }
            if (c == '.') { key = Key.Period; return true; }
            if (c == '/') { key = Key.Slash; return true; }
            if (c == '-') { key = Key.Minus; return true; }
            if (c == '=') { key = Key.Equals; return true; }
        }

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            try
            {
                var control = keyboard.FindKeyOnCurrentKeyboardLayout(name);
                if (control != null)
                {
                    key = control.keyCode;
                    return true;
                }
            }
            catch { }
        }

        if (Enum.TryParse<Key>(name, true, out key)) return true;
        return false;
    }
}
