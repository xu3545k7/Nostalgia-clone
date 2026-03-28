using UnityEngine;
using UnityEditor;

/// <summary>
/// Tools -> Convert Selected Textures to Sprite
/// 選取專案視窗中的 PNG(s)，再從上方選單執行此命令，會把選取的貼圖 Importer 設為 Sprite (2D and UI) 並 Reimport。
/// </summary>
public class SetTexturesToSprite
{
    [MenuItem("Tools/Convert Selected Textures to Sprite")] 
    public static void ConvertSelectedToSprite()
    {
        Object[] objs = Selection.objects;
        if (objs == null || objs.Length == 0)
        {
            //Debug.LogWarning("ConvertSelectedToSprite: No objects selected.");
            return;
        }

        int converted = 0;
        foreach (var obj in objs)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning($"ConvertSelectedToSprite: {path} is not a texture importer.");
                continue;
            }

            if (ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                // keep default pixelsPerUnit
                try
                {
                    ti.SaveAndReimport();
                    //Debug.Log($"Converted to Sprite: {path}");
                    converted++;
                }
                catch (System.Exception)
                {
                    //Debug.LogError($"Failed to reimport {path}.");
                }
            }
            else
            {
                //Debug.Log($"Already Sprite: {path}");
            }
        }

    //Debug.Log($"ConvertSelectedToSprite: Completed. Converted {converted} assets.");
    }
}
