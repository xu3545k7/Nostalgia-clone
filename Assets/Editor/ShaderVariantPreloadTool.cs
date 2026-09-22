using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把「實際玩到的 shader 變體」存成資產，並掛進 Graphics 的 Preloaded Shaders。
/// </summary>
/// <remarks>
/// **時機是這件事唯一的難處，而且我們還沒確定哪個時機才對。**
///
/// Unity 追蹤到的變體清單是有壽命的：停止遊玩之後再存，拿到的是空的
/// （實測存出來的資產是 <c>m_Shaders: {}</c>）。掛在
/// <see cref="PlayModeStateChange.ExitingPlayMode"/> 上自動存也一樣是空的，
/// 所以那條路已經拿掉了。Unity 自己的文件說的是「**在 Play Mode 中**按 Save」，
/// 這是唯一還沒被推翻的講法——所以現在只留手動，而且要在還在玩的時候按。
///
/// 這個工具曾經在自動模式下往 <c>m_PreloadedShaders</c> 塞了一千多筆空參考。
/// 現在的防線是：**空的集合絕對不寫進設定**，而且每次寫之前先把空參考清掉。
///
/// 用法：
///
///   1. 進 Play Mode，玩一首涵蓋 Hold / Trill / 踏板的譜面。
///   2. **不要按停止。** 直接執行 <c>Tools ▸ 鋼琴譜面 ▸ 儲存 Shader 變體（要在遊玩中）</c>。
///   3. 對話框會說記錄到幾個 shader、幾個變體。是 0 就代表這條路在這個 Unity
///      版本上行不通，別再試——遊戲內的 <c>GameplayShaderWarmup</c> 已經在做
///      同一件事的近似版本。
/// </remarks>
public static class ShaderVariantPreloadTool
{
    private const string AssetFolder = "Assets/ShaderVariants";
    private const string AssetPath = AssetFolder + "/GameplayVariants.shadervariants";

    [MenuItem("Tools/鋼琴譜面/儲存 Shader 變體（要在遊玩中）", priority = 100)]
    private static void SaveNow()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("要在 Play Mode 中執行",
                "Unity 追蹤到的變體清單在停止遊玩後就沒了，現在存一定是空的。\n\n" +
                "請先進 Play Mode 玩一首譜面（最好包含 Hold、Trill 和踏板），" +
                "然後不要按停止，直接回來執行這個選單。", "好");
            return;
        }

        MethodInfo save = FindShaderUtilMethod("SaveCurrentShaderVariantCollection");
        if (save == null)
        {
            EditorUtility.DisplayDialog("找不到 API",
                "ShaderUtil.SaveCurrentShaderVariantCollection 在這個 Unity 版本上找不到。" +
                "請改用 Project Settings ▸ Graphics 裡算繪管線那一區的 Save 按鈕。", "好");
            return;
        }

        // 先存到暫存路徑。直接寫正式路徑的話，一次空的儲存就會把先前好的成果蓋掉。
        string tempPath = AssetFolder + "/~Tracked.shadervariants";
        try
        {
            if (!Directory.Exists(AssetFolder)) Directory.CreateDirectory(AssetFolder);
            save.Invoke(null, new object[] { tempPath });
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("儲存失敗", (e.InnerException ?? e).Message, "好");
            return;
        }

        AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);
        var tracked = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(tempPath);

        // 數量從存好的資產上讀。ShaderUtil 的 internal 計數器反射抓不到時會回 0，
        // 那會讓「有記錄到」和「抓不到計數方法」看起來一模一樣。
        int shaders = tracked != null ? tracked.shaderCount : 0;
        int variants = tracked != null ? tracked.variantCount : 0;
        if (shaders == 0 || variants == 0)
        {
            AssetDatabase.DeleteAsset(tempPath);
            EditorUtility.DisplayDialog("沒有追蹤到任何變體",
                "存出來的集合是空的，所以什麼都沒有改動。\n\n" +
                "這代表這個 Unity 版本的變體追蹤在這裡拿不到東西。不用再試了——" +
                "遊戲內的 GameplayShaderWarmup 已經在做同一件事的近似版本，" +
                "而真正該比的數字是 build 的 [Frame Pacing]，不是 Editor 的。", "好");
            return;
        }

        AssetDatabase.DeleteAsset(AssetPath);
        AssetDatabase.MoveAsset(tempPath, AssetPath);
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetPath);
        if (collection == null)
        {
            EditorUtility.DisplayDialog("讀不回來", $"存了 {AssetPath} 但讀不回來。", "好");
            return;
        }

        string outcome = AssignToPreloadedShaders(collection);
        Debug.Log($"ShaderVariantPreloadTool: {shaders} 個 shader、{variants} 個變體 → {AssetPath}。{outcome}");
        EditorUtility.DisplayDialog("完成",
            $"記錄了 {shaders} 個 shader、共 {variants} 個變體。\n\n{AssetPath}\n{outcome}", "好");
    }

    /// <summary>
    /// 在所有 UnityEditor 組件裡找 <c>ShaderUtil</c> 上的那個方法。
    /// </summary>
    /// <remarks>
    /// 這些方法是 internal（Graphics 設定頁那顆 Save 按鈕就是呼叫它們），直接寫
    /// 會編譯不過。不寫死 <c>typeof(ShaderUtil)</c>：它在 <c>UnityEditor.dll</c> 和
    /// <c>UnityEditor.CoreModule.dll</c> 之間搬過家。
    /// </remarks>
    private static MethodInfo FindShaderUtilMethod(string name)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        MethodInfo direct = typeof(ShaderUtil).GetMethod(name, flags);
        if (direct != null) return direct;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.FullName.StartsWith("UnityEditor", StringComparison.Ordinal)) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (Type type in types)
            {
                if (type == null || type.Name != "ShaderUtil") continue;
                MethodInfo method = type.GetMethod(name, flags);
                if (method != null) return method;
            }
        }
        return null;
    }

    /// <summary>
    /// 把資產加進 <c>GraphicsSettings</c> 的 <c>m_PreloadedShaders</c>，順便清掉空參考。
    /// </summary>
    /// <remarks>
    /// 清空參考不是順手做好事：這個工具的自動版本曾經每次執行都往清單裡塞一筆
    /// 存不起來的參考，累積到一千多筆。所以每次寫之前都先掃一遍。
    ///
    /// 走 SerializedObject 是因為這個欄位沒有公開 setter；名稱直接對應
    /// ProjectSettings/GraphicsSettings.asset 裡的同名欄位。
    /// </remarks>
    private static string AssignToPreloadedShaders(ShaderVariantCollection collection)
    {
        UnityEngine.Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (assets == null || assets.Length == 0)
            return "讀不到 GraphicsSettings.asset，請手動把資產拖進 Preloaded Shaders。";

        var settings = new SerializedObject(assets[0]);
        SerializedProperty list = settings.FindProperty("m_PreloadedShaders");
        if (list == null)
            return "找不到 m_PreloadedShaders，請手動把資產拖進 Preloaded Shaders。";

        int removed = 0;
        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty element = list.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue == collection) return "先前就已經掛著。";
            if (element.objectReferenceValue != null) continue;
            list.DeleteArrayElementAtIndex(i);
            removed++;
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = collection;
        settings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        // 立刻讀回來確認參考真的存下去了。存不起來會變成 {fileID: 0}，靜靜地
        // 什麼都不預熱——這正是先前累積出一千多筆空參考的原因。
        var verify = new SerializedObject(assets[0]);
        SerializedProperty after = verify.FindProperty("m_PreloadedShaders");
        bool stuck = false;
        for (int i = 0; after != null && i < after.arraySize; i++)
        {
            if (after.GetArrayElementAtIndex(i).objectReferenceValue == collection) stuck = true;
        }

        string cleaned = removed > 0 ? $"（順便清掉 {removed} 筆空參考）" : "";
        return stuck
            ? $"已掛進 Graphics ▸ Preloaded Shaders。{cleaned}"
            : $"參考存不進設定檔，請手動把 {AssetPath} 拖進 " +
              $"Project Settings ▸ Graphics ▸ Preloaded Shaders。{cleaned}";
    }
}
