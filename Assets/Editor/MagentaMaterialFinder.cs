using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 找出場景裡用「錯誤材質」畫出來的物件 —— 就是那些洋紅色的東西。
/// </summary>
/// <remarks>
/// 洋紅是 Unity 的最後手段：材質是 null、shader 是 null、或 shader 在目前的算繪管線
/// 下不被支援時，它就用這個顏色畫。光看畫面分不出是誰，而且如果它是執行期殘留下來
/// 的物件（play mode 當掉之後留在場景裡的那種），連場景檔裡都查不到。
///
/// 這支放在 Editor 資料夾，所以**不用進 play mode** 就能跑：選單 Nostalgia →
/// 診斷 → 找出洋紅材質。找到的物件會同時被選取起來，Hierarchy 會跳到它身上。
/// </remarks>
public static class MagentaMaterialFinder
{
    [MenuItem("Nostalgia/診斷/找出洋紅材質")]
    public static void FindBroken()
    {
        var report = new StringBuilder();
        var hits = new System.Collections.Generic.List<Object>();

        Renderer[] renderers = Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                bool broken = material == null
                    || material.shader == null
                    || !material.shader.isSupported
                    || material.shader.name.Contains("InternalError");
                if (!broken) continue;

                hits.Add(renderer.gameObject);
                report.AppendLine($"{Path(renderer.transform)}  slot {i}  " +
                    $"material={(material != null ? material.name : "null")}  " +
                    $"shader={(material != null && material.shader != null ? material.shader.name : "null")}  " +
                    $"layer={LayerMask.LayerToName(renderer.gameObject.layer)}  " +
                    $"pos={renderer.transform.position}  " +
                    $"scene={renderer.gameObject.scene.name}");
                break;
            }
        }

        if (hits.Count == 0)
        {
            Debug.Log("[洋紅診斷] 場景裡沒有壞掉的材質。那片洋紅可能來自執行期才生出來的物件（進 play mode 再跑一次）。");
            return;
        }

        Selection.objects = hits.ToArray();
        Debug.LogWarning($"[洋紅診斷] 找到 {hits.Count} 個，已選取：\n{report}");
    }

    /// <summary>把預覽舞台的殘留物整批刪掉。</summary>
    /// <remarks>
    /// play mode 當掉之後，Unity 還原場景時可能把執行期生出來的物件一起留在編輯場
    /// 景裡。那些東西在磁碟的場景檔裡並不存在，所以重開場景不一定救得回來，但它們
    /// 會一直畫在畫面上（洋紅的那片就是）。這個選單把它們找出來刪掉。
    /// </remarks>
    [MenuItem("Nostalgia/診斷/清掉譜面預覽的殘留")]
    public static void CleanPreviewLeftovers()
    {
        string[] names =
        {
            "ChartOverviewStage", "TrackPreview", "StageCamera", "ChartOverviewViewer", "Notes"
        };
        int removed = 0;
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Transform t in all)
        {
            if (t == null) continue;
            bool match = false;
            for (int i = 0; i < names.Length; i++) if (t.name == names[i]) { match = true; break; }
            // 音符的複本叫 Note(Clone)。編輯模式下場景裡本來就不該有任何 NoteController
            // （確認過：main_scene 沒有放任何 Note prefab 實例），所以看到就是殘留。
            if (!match && !Application.isPlaying && t.GetComponent<NoteController>() != null) match = true;
            // 元件可能已經被銷毀、只剩下帶圖的空殼 —— 那種用元件是找不到的。舞台是
            // 唯一會用 ChartPreview 圖層的東西，所以整層掃一次才抓得乾淨。
            if (!match && t.gameObject.layer == LayerMask.NameToLayer(ChartOverviewStage.PreviewLayerName))
                match = true;
            if (!match) continue;
            Transform top = t;
            while (top.parent != null) top = top.parent;
            Debug.LogWarning($"[預覽清理] 刪除 {Path(top)}  pos={top.position}  子物件 {top.childCount}");
            Object.DestroyImmediate(top.gameObject);
            removed++;
        }
        Debug.Log(removed == 0
            ? "[預覽清理] 沒有找到殘留。"
            : $"[預覽清理] 刪掉 {removed} 顆。記得存檔前再確認一次場景是乾淨的。");
    }

    private static string Path(Transform target)
    {
        string path = target.name;
        for (Transform t = target.parent; t != null; t = t.parent) path = t.name + "/" + path;
        return path;
    }
}
