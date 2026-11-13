using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 換畫面時合上再拉開的布幕。
/// </summary>
/// <remarks>
/// **為什麼是幕，不是淡出淡入。** 淡出是「這一頁沒了」，幕是「這一段結束了」——
/// 而這個遊戲整個背景就是一座音樂廳，一首曲子彈完本來就該落幕。原本的結算是
/// 0.32 秒把整塊 CanvasGroup 的 alpha 推上去，那不是轉場，那是把東西放上來的時候
/// 順手避免了硬切。
///
/// **為什麼幕後面還要墊一塊黑。** 兩片幕的內緣是斜的（布往下散開），合上的時候
/// 上緣會留一道細細的楔形缺口。那道缺口在幕完全合上的那一刻正好對著畫面中央 ——
/// 而那一刻底下正在抽換畫面。墊一塊跟著一起變暗的板子，缺口漏出來的是黑的，
/// 不是上一個畫面的殘影。
///
/// **為什麼全部走 unscaled。** 轉場會發生在 <c>Time.timeScale</c> 不是 1 的時候：
/// 暫停中按離開、練習模式改過速度之後結算。用縮放後的時間，暫停時幕根本不會動。
///
/// **為什麼回呼掛在「完全遮住」那一刻。** 畫面要在看不見的時候換 —— 早一點會看到
/// 舊畫面被抽掉，晚一點會看到新畫面補上來。這個時間點只有幕自己知道，所以由它呼叫，
/// 而不是讓呼叫端自己抓時間。
/// </remarks>
[DisallowMultipleComponent]
public sealed class StageCurtain : MonoBehaviour
{
    /// <summary>合上要多久。</summary>
    /// <remarks>
    /// 比拉開短。幕是被放下來的，重力幫忙；拉開是用絞盤拉上去的，慢而且穩 ——
    /// 兩邊一樣長的話，開幕會顯得很急，而開幕正是要讓人看清楚新畫面的那一段。
    /// </remarks>
    private const float CloseSeconds = 0.50f;

    /// <summary>完全合上之後停多久再拉開。</summary>
    /// <remarks>
    /// 這一段是給抽換畫面用的，但它同時也是節奏上的句點。沒有這個停頓，合上和
    /// 拉開會連成一個「彈回來」的動作，讀起來像門被撞開。
    /// </remarks>
    private const float HoldSeconds = 0.26f;

    /// <summary>拉開要多久。</summary>
    private const float OpenSeconds = 0.72f;

    /// <summary>一片幕佔畫面寬的幾成。</summary>
    /// <remarks>
    /// 超過一半。兩片各自蓋到中線**再多一點**，中縫才是兩片布疊在一起，不是剛好
    /// 碰到 —— 剛好碰到的那一版，只要畫面寬度算出來差一個像素就裂一條縫。
    /// </remarks>
    private const float HalfWidth = 0.56f;

    /// <summary>合上時，中縫往另一邊壓過去多少。</summary>
    /// <remarks>
    /// **要大於布散開的量。** 貼圖上的布往下散開 3.5%，也就是說它的內緣在頂端比在
    /// 底端往內縮了約 50 單位。中縫只壓 8 的那一版，兩片的內緣在頂端差了 90 ——
    /// 中間裂出一個倒 V。缺口是黑的（後面那塊板子），不會漏出畫面，但看起來就是
    /// 幕沒關好。
    ///
    /// 64 讓兩片在**最窄的頂端**還疊著約 28。真的幕本來就是中間互相搭過去的。
    /// </remarks>
    private const float SeamOverlap = 64f;

    /// <summary>合上那一下，布甩過頭再盪回來的幅度。</summary>
    /// <remarks>
    /// 一塊有重量的布停下來的時候不會立刻靜止。幅度只有 14 —— 看不太出來是刻意的
    /// 動作，但拿掉之後會覺得幕是一塊板子。
    /// </remarks>
    private const float SwingAmplitude = 14f;

    /// <summary>幕的布在哪。</summary>
    private const string ClothResource = "UI/StageCurtainHalf";

    /// <summary>貼圖載不到時的平色布。</summary>
    /// <remarks>
    /// 和 <c>bake_curtain.py</c> 的 <c>BODY</c> 同一個值。平的一塊紅當然不如烘好的
    /// 絨布，但「沒有轉場」和「轉場的布是平的」是兩種完全不同的失敗 —— 後者看得見、
    /// 而且一眼就知道是貼圖出了事。
    /// </remarks>
    private static readonly Color FallbackVelvet = new Color(0.218f, 0.014f, 0.044f, 1f);

    private static StageCurtain instance;

    private RectTransform rootRect;
    private Image shade;
    private RectTransform left;
    private RectTransform right;
    private Coroutine running;

    /// <summary>
    /// 合幕、換畫面、開幕。
    /// </summary>
    /// <param name="onCovered">完全遮住的那一刻呼叫。畫面在這裡換。</param>
    /// <param name="onRevealed">開始拉開的那一刻呼叫。新畫面自己的進場動畫接在這裡。</param>
    public static void Play(Action onCovered, Action onRevealed = null)
    {
        StageCurtain curtain = Ensure();
        if (curtain == null)
        {
            // 幕建不起來（沒有貼圖、沒有 Canvas）也不能把畫面卡住 —— 轉場是裝飾，
            // 換畫面才是本體。
            SafeInvoke(onCovered);
            SafeInvoke(onRevealed);
            return;
        }
        if (curtain.running != null) curtain.StopCoroutine(curtain.running);
        curtain.running = curtain.StartCoroutine(curtain.Run(onCovered, onRevealed));
    }

    // ------------------------------------------------------------------

    private static StageCurtain Ensure()
    {
        if (instance != null) return instance;

        // **載不到也要把幕建起來。** 這裡原本是 return null，而 Play 收到 null 就
        // 直接把兩個回呼叫完 —— 畫面照換，只是完全沒有轉場，而且<b>沒有任何訊息</b>。
        // 一個會靜靜地不發生的功能，查起來只能靠猜。
        Texture2D cloth = Resources.Load<Texture2D>(ClothResource);
        if (cloth == null)
        {
            Debug.LogError($"StageCurtain: 載不到 Resources/{ClothResource}。" +
                           "幕會用平色的布代替 —— 檢查貼圖是否在 Assets/Resources/UI/ 底下且已被匯入。");
        }

        var host = new GameObject("StageCurtain");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<StageCurtain>();

        Canvas canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        // 比畫面上任何一層都高：結算的書是 30000，開場的字卡是 31000。幕要蓋住
        // 的就是這些東西。
        canvas.sortingOrder = 32500;

        CanvasScaler scaler = host.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560f, 1440f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 擋輸入。幕合著的時候底下正在抽換畫面，這時候的點擊會打到一個已經不存在
        // 或還沒排好的按鈕上。
        host.AddComponent<GraphicRaycaster>();

        instance.rootRect = host.GetComponent<RectTransform>();

        instance.shade = NewImage("Shade", instance.rootRect);
        instance.shade.color = new Color(0.02f, 0.008f, 0.012f, 0f);
        Stretch(instance.shade.rectTransform);

        instance.left = NewCloth("LeftHalf", instance.rootRect, cloth, false);
        instance.right = NewCloth("RightHalf", instance.rootRect, cloth, true);

        instance.Apply(0f, 0f);
        return instance;
    }

    private static Image NewImage(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go.AddComponent<Image>();
    }

    /// <summary>一片幕。右邊那一片是同一張圖 UV 左右鏡射 —— 兩側本來就該對稱。</summary>
    private static RectTransform NewCloth(string name, RectTransform parent, Texture2D cloth, bool mirror)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        RawImage image = go.AddComponent<RawImage>();
        image.texture = cloth;
        // 沒有貼圖的 RawImage 畫的是一塊實心的 color —— 正好拿來當平色回退。
        image.color = cloth != null ? Color.white : FallbackVelvet;
        image.raycastTarget = false;
        image.uvRect = mirror ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);

        RectTransform rect = (RectTransform)go.transform;
        // 縱向跟著畫面拉滿（anchor 的 y 是 0..1），橫向是固定寬度，由 pivot 那一側
        // 的邊緣去對齊畫面邊。這樣一個 anchoredPosition.x 就能把整片推進推出。
        rect.anchorMin = new Vector2(mirror ? 1f : 0f, 0f);
        rect.anchorMax = new Vector2(mirror ? 1f : 0f, 1f);
        rect.pivot = new Vector2(mirror ? 0f : 1f, 0.5f);
        rect.sizeDelta = new Vector2(0f, 0f);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------------

    private IEnumerator Run(Action onCovered, Action onRevealed)
    {
        // 合上
        float elapsed = 0f;
        while (elapsed < CloseSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            Apply(Smooth(Mathf.Clamp01(elapsed / CloseSeconds)), 0f);
            yield return null;
        }
        Apply(1f, 0f);

        // 完全遮住。畫面在這裡換。
        SafeInvoke(onCovered);

        // 停一下，順便讓布把甩過頭的那一下盪完。
        elapsed = 0f;
        while (elapsed < HoldSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            Apply(1f, Swing(elapsed));
            yield return null;
        }
        Apply(1f, 0f);

        SafeInvoke(onRevealed);

        // 拉開
        elapsed = 0f;
        while (elapsed < OpenSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            Apply(1f - Smooth(Mathf.Clamp01(elapsed / OpenSeconds)), 0f);
            yield return null;
        }
        Apply(0f, 0f);
        running = null;
    }

    /// <summary>
    /// 把幕排到 <paramref name="closed"/>（0 = 全開，1 = 全合）。
    /// </summary>
    /// <remarks>
    /// 寬度每一幀重算，不是建的時候算一次：畫面可以在轉場途中改大小（全螢幕切換、
    /// 拖視窗），算一次的話幕就蓋不滿了。
    /// </remarks>
    private void Apply(float closed, float swing)
    {
        if (rootRect == null) return;
        float width = rootRect.rect.width;
        if (width <= 1f) return;

        float span = width * HalfWidth;
        if (left != null) left.sizeDelta = new Vector2(span, 0f);
        if (right != null) right.sizeDelta = new Vector2(span, 0f);

        float travel = (width * 0.5f + SeamOverlap) * closed + swing;
        if (left != null) left.anchoredPosition = new Vector2(travel, 0f);
        if (right != null) right.anchoredPosition = new Vector2(-travel, 0f);

        if (shade != null)
        {
            Color c = shade.color;
            // 比幕早一點暗下去（開 0.72 次方）。布還沒碰到中線之前，中間那一條就
            // 已經是暗的 —— 不然會看到一條越來越窄的亮縫，像門縫而不是幕。
            c.a = Mathf.Pow(Mathf.Clamp01(closed), 0.72f) * 0.97f;
            shade.color = c;
            shade.raycastTarget = closed > 0.01f;
        }
    }

    /// <summary>停下來那一刻的餘擺。衰減的正弦，第一下最大。</summary>
    private static float Swing(float t)
    {
        return SwingAmplitude * Mathf.Exp(-t * 9f) * Mathf.Sin(t * 26f);
    }

    /// <summary>
    /// 兩頭都慢的緩動。
    /// </summary>
    /// <remarks>
    /// 不是 ease-out。一塊幾十公斤的布起步不可能是最快的那一刻 —— ease-out 讀起來
    /// 是「被彈出來」。smootherstep 兩頭的一階和二階導數都是 0，起步和停止都看不到
    /// 轉折。
    /// </remarks>
    private static float Smooth(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static void SafeInvoke(Action action)
    {
        if (action == null) return;
        // 回呼炸了也要把幕拉開。卡在全黑的畫面是比任何一個例外都嚴重的結果。
        try { action(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
