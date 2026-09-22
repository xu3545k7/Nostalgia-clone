using UnityEngine;
using UnityEditor;

public static class TextureImportInspector
{
    [MenuItem("Tools/Debug/Report Non-Sprite Textures in Resources/graphic")]
    public static void ReportNonSpriteTextures()
    {
        string path = "Assets/Resources/graphic";
        var guids = AssetDatabase.FindAssets("t:Texture", new[] { path });
        int nonSpriteCount = 0;
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            if (ti.textureType != TextureImporterType.Sprite)
            {
                Debug.Log($"TextureImportInspector: Non-sprite texture: {p} (type={ti.textureType})");
                nonSpriteCount++;
            }
        }
        Debug.Log($"TextureImportInspector: Found {nonSpriteCount} non-sprite textures under {path} (scanned {guids.Length} textures).");
    }

    [MenuItem("Tools/Debug/Convert Non-Sprite To Sprite/Resources/graphic")]
    public static void ConvertNonSpriteToSprite()
    {
        if (!EditorUtility.DisplayDialog("Convert textures to Sprite?", "This will set Texture Type = Sprite for all textures under Assets/Resources/graphic and reimport them. Continue?", "Yes", "No"))
            return;

        string path = "Assets/Resources/graphic";
        var guids = AssetDatabase.FindAssets("t:Texture", new[] { path });
        int converted = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                if (ti.textureType != TextureImporterType.Sprite)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    try
                    {
                        ti.SaveAndReimport();
                        converted++;
                        Debug.Log($"TextureImportInspector: Converted {p} -> Sprite");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"TextureImportInspector: Failed to reimport {p}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
        Debug.Log($"TextureImportInspector: Converted {converted} textures to Sprite under {path}");
    }
}
