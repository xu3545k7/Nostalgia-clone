using UnityEngine;
using UnityEditor;

public static class ResourcesDebugTool
{
    [MenuItem("Tools/Debug/Log Graphic/Judge Sprites")]
    public static void LogJudgeSprites()
    {
        string folder = "graphic/judge";
        var loaded = Resources.LoadAll<Sprite>(folder);
        Debug.Log($"ResourcesDebug: Resources.LoadAll<Sprite>(\"{folder}\") returned {(loaded == null ? 0 : loaded.Length)} entries.");
        if (loaded != null)
        {
            foreach (var s in loaded)
            {
                Debug.Log($"ResourcesDebug: found sprite name='{(s == null ? "(null)" : s.name)}' texture='{(s?.texture != null ? s.texture.name : "(null)")}'");
            }
        }

        var names = new string[] { "Just", "just", "Great", "great", "Good", "good", "Miss", "miss" };
        foreach (var n in names)
        {
            var sp = Resources.Load<Sprite>($"{folder}/{n}");
            Debug.Log($"ResourcesDebug: Resources.Load<Sprite>('{folder}/{n}') -> {(sp == null ? "(null)" : sp.name)}");
        }
    }

    [MenuItem("Tools/Debug/Reimport Resources/graphic/judge")]
    public static void ReimportJudgeFolder()
    {
        string path = "Assets/Resources/graphic/judge";
        AssetDatabase.StartAssetEditing();
        try
        {
            var guids = AssetDatabase.FindAssets("", new[] { path });
            int count = 0;
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".meta")) continue;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                count++;
            }
            Debug.Log($"ResourcesDebug: reimported {count} assets under {path}");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
    }
}
