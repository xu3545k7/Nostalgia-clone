using UnityEngine;

/// <summary>
/// 找場景裡的單例元件，**包含還沒啟用的**。
/// </summary>
/// <remarks>
/// 這個專案的管理器大多是「找不到就自己建一個」。問題在於
/// <c>FindFirstObjectByType</c> / <c>FindAnyObjectByType</c> **看不到未啟用的
/// 物件**，而場景裡設定好的那些管理器全都掛在 gameplay root 底下——那個東西
/// 在遊戲真正開始之前是關著的。
///
/// 於是在那段空窗裡只要有人碰一下 <c>Instance</c>，就會建出一個欄位全是預設值
/// 的替身；等場景那個真的被啟用，它的 <c>Awake</c> 看到 <c>_instance</c> 已經被
/// 佔住，就把**自己**銷毀。結果是設定好的實例消失、留下沒有材質的替身，特效
/// 整個不見——而且不會有任何錯誤訊息。
///
/// 這個洞在載入頁加進來之後特別容易踩到：gameplay root 現在要等音檔和取樣都
/// 載完才啟用，空窗從幾格變成好幾秒。
/// </remarks>
public static class SceneSingleton
{
    /// <summary>
    /// 回傳場景裡的實例，含未啟用的；沒有就回傳 null。
    /// </summary>
    /// <remarks>
    /// <c>Resources.FindObjectsOfTypeAll</c> 連未啟用的都找得到，但它同時會回傳
    /// 專案裡的 prefab 資產，所以一定要用 <c>gameObject.scene.IsValid()</c> 篩掉
    /// ——把 prefab 上的元件當成場景實例會更糟。
    /// </remarks>
    public static T Find<T>() where T : MonoBehaviour
    {
        T[] candidates;
        try { candidates = Resources.FindObjectsOfTypeAll<T>(); }
        catch { return null; }

        for (int i = 0; i < candidates.Length; i++)
        {
            T candidate = candidates[i];
            if (candidate != null && candidate.gameObject.scene.IsValid()) return candidate;
        }
        return null;
    }
}
