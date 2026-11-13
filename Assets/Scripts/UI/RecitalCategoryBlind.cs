using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 演奏會畫面的分類列：捲在一條白樺軸上的簾子，平常只留一條拉繩。
/// </summary>
/// <remarks>
/// **為什麼分類要收起來。** 挑歌的時候真正要一直看的是曲目卡；分類是「偶爾換一次」
/// 的東西，卻橫在畫面正中央佔掉一整條。收起來之後那一條還給簾幕和曲目，要用的時候
/// 拉一下繩子。和練習室的篩選抽屜同一套動作，所以兩個畫面的手勢是同一個。
///
/// **為什麼遮罩要在全開時關掉。** 「音樂類型」的下拉選單掛在分類槽底下 222 單位，
/// 一路伸出簾子的範圍。遮罩一直開著的話，選單會被裁成一條 —— 那是實機上第一次做這
/// 件事最容易踩的坑。全開的時候沒有東西需要裁，直接把 <see cref="RectMask2D"/> 停
/// 掉；只有動畫中和收起來的時候才需要它。
///
/// **為什麼預設是開著的。** 這條同時在<b>顯示</b>目前的分類和「第幾首／共幾首」。
/// 預設收起來的話，玩家一進畫面就不知道自己在哪一段曲庫。第一次進來是開的，之後記
/// 住上一次的狀態。
/// </remarks>
[RequireComponent(typeof(RectTransform))]
public sealed class RecitalCategoryBlind : MonoBehaviour
{
    /// <summary>拉繩的點擊區多寬。</summary>
    private const float CordHitWidth = 34f;
    /// <summary>拉繩離畫面左緣多遠。和練習室一樣靠最左。</summary>
    private const float CordInset = 66f;
    /// <summary>繩有多長。固定值 —— 拉之前之後一樣長。</summary>
    private const float CordIdle = 64f;
    /// <summary>布有多寬。要蓋得住從「音樂類型」到右箭頭那一整段。</summary>
    private const float ClothWidth = 960f;
    /// <summary>布的中心偏左：控制項本來就偏左（−916 到 +673）。</summary>
    private const float ClothCentre = -30f;

    /// <summary>上一次是開還是關。整個 process 記著，回到畫面時不用重找。</summary>
    private static bool remembered = true;

    private RectTransform host;
    private RectTransform strip;
    private RectTransform cord;
    private RectTransform cloth;
    private BlindCordGraphic cordArt;
    private RectMask2D mask;
    private float height;
    private float target;
    private float from;
    private float time = -1f;
    private float span;
    private float full;
    private float gap;

    public static RecitalCategoryBlind Attach(RectTransform panel, RectTransform strip,
        float top, float stripHeight)
    {
        if (panel == null || strip == null) return null;
        Transform existing = panel.Find("CategoryBlind");
        if (existing != null) return existing.GetComponent<RecitalCategoryBlind>();

        var hostObject = new GameObject("CategoryBlind", typeof(RectTransform),
            typeof(RecitalCategoryBlind));
        hostObject.layer = panel.gameObject.layer;
        var host = hostObject.GetComponent<RectTransform>();
        host.SetParent(panel, false);
        host.anchorMin = host.anchorMax = new Vector2(0.5f, 1f);
        host.pivot = new Vector2(0.5f, 1f);
        // **簾口在簾桿的底緣，不是分類槽的上緣。**
        //
        // 桿底在 160、分類在 190，中間空了 30 —— 布從 190 開始的話它和上面那塊板
        // 沒有相接，讀起來是一塊浮在半空中的牌子，不是掛下來的布。
        host.anchoredPosition = new Vector2(0f, -SelectionRailGraphic.Height);
        // 夠寬才不會把「音樂類型」和兩顆箭頭裁掉 —— 它們都掛在分類槽上、位置在槽外。
        host.sizeDelta = new Vector2(1960f, top - SelectionRailGraphic.Height + stripHeight);

        var blind = hostObject.GetComponent<RecitalCategoryBlind>();
        blind.Build(panel, host, strip, top, stripHeight);
        return blind;
    }

    private void Build(RectTransform panel, RectTransform hostRect, RectTransform stripRect,
        float top, float stripHeight)
    {
        host = hostRect;
        strip = stripRect;
        gap = top - SelectionRailGraphic.Height;
        full = gap + stripHeight;
        mask = host.gameObject.AddComponent<RectMask2D>();

        // 布本身。先加，所以畫在分類控制項後面 —— 它們是印在這塊布上的。
        //
        // 沒有它的話，「降下來」的只是一排浮在簷幕前面的按鈕，不是一塊布。
        var clothObject = new GameObject("Cloth", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image));
        clothObject.layer = panel.gameObject.layer;
        cloth = clothObject.GetComponent<RectTransform>();
        cloth.SetParent(host, false);
        cloth.anchorMin = cloth.anchorMax = new Vector2(0.5f, 1f);
        cloth.pivot = new Vector2(0.5f, 1f);
        cloth.anchoredPosition = new Vector2(ClothCentre, 0f);
        // 上緣釘住。捲簾是布從軸上放下來，不是整塊布往下滑。
        cloth.sizeDelta = new Vector2(ClothWidth, full);
        Image clothImage = clothObject.GetComponent<Image>();
        // **這塊布是透明的：簾子後面直接就是演奏會的絨布。**
        //
        // 原本是一塊緋紅的皮革面，左右各一條 7 寬的金立框，下緣再一條金壓條 ——
        // 那一整套是在演「從台口降下來的一塊捲簾」。但它降下來的位置本來就在簾幕
        // 上，而簾幕已經是一匹有褶有光的布；再掛一塊布上去，讀起來不是「簾子前面
        // 有簾子」，而是兩種材質各說各話地疊著，中間那圈金還把它框成一個方盒。
        //
        // 拿掉之後分類列不會變成裸字 —— 它自己有槽（ApplyDarkSurface ＋ 凹槽線），
        // 漏斗和箭頭各自有金框，每張分類卡有自己的木牌。
        //
        // 「這是一塊會捲起來的簾子」現在由拉繩單獨承擔。代價是拉到一半時看不到布
        // 緣在哪裡；換來的是分類列直接坐在絨布上。
        //
        // 元件留著、alpha 不是 0：簾子放下來的時候這塊要擋住 raycast，否則點在分類
        // 列的空白處會穿過去打到後面的曲目轉盤。
        clothImage.color = new Color(0f, 0f, 0f, 0.012f);
        clothImage.raycastTarget = true;

        strip.SetParent(host, false);
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        // 分類槽在布上的位置：桿底下那 30 是布，槽從那裡才開始。
        strip.anchoredPosition = new Vector2(0f, -gap);

        // 拉繩。收起來之後，畫面上這一整條就只剩它。
        var cordObject = new GameObject("Cord", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(BlindCordGraphic), typeof(Button));
        cordObject.layer = panel.gameObject.layer;
        cord = cordObject.GetComponent<RectTransform>();
        cord.SetParent(panel, false);
        cord.anchorMin = cord.anchorMax = new Vector2(0.5f, 1f);
        cord.pivot = new Vector2(0.5f, 1f);
        // 從簾桿的底緣垂下來，不是從分類的簾口。桿本身就是那塊板子，
        // 再畫一根軸只是在布上橫一條白。
        // 靠畫面最左，錯點跟著左緣 —— 用中線偏移的話，寬螢幕上會飛到畫面外。
        cord.anchorMin = cord.anchorMax = new Vector2(0f, 1f);
        cord.anchoredPosition = new Vector2(CordInset, -SelectionRailGraphic.Height);
        // 長度固定：拉之前之後一樣長，只跑一個下去上來的拉扯。
        cord.sizeDelta = new Vector2(CordHitWidth, CordIdle + BlindCordGraphic.BeadReach);
        cordArt = cordObject.GetComponent<BlindCordGraphic>();
        cordArt.raycastTarget = true;
        cordArt.Brass = true;               // 演奏會是金的，練習室是白的
        Button pull = cordObject.GetComponent<Button>();
        pull.transition = Selectable.Transition.None;
        pull.onClick.AddListener(Toggle);

        height = target = remembered ? full : 0f;
        Apply();
    }

    /// <summary>拉一下：開的收起來，收起來的拉下去。</summary>
    public void Toggle()
    {
        target = target > full * 0.5f ? 0f : full;
        remembered = target > 0f;
        UiSfx.Toggle(target > 0f);
        if (cordArt != null) cordArt.Tug();
        from = height;
        // 拉下來比收上去慢一點：布要「走出來」，收的時候是彈回去。
        span = target > from ? 0.28f : 0.21f;
        time = 0f;
    }

    /// <summary>簾子是開的嗎。外面要判斷分類還看不看得到就問這個。</summary>
    public bool IsOpen => target > full * 0.5f;

    /// <summary>不動聲色地收起來。分類以外的東西要獨佔畫面時用。</summary>
    public void Close()
    {
        if (target <= 0f) return;
        target = 0f;
        remembered = false;
        if (cordArt != null) cordArt.Tug();
        from = height;
        span = 0.21f;
        time = 0f;
    }

    private void Update()
    {
        if (time < 0f) return;
        time += Time.unscaledDeltaTime;
        float t = span > 0f ? Mathf.Clamp01(time / span) : 1f;
        // 三次的緩出：拉繩放手之後布自己走完最後一段。
        height = Mathf.Lerp(from, target, 1f - Mathf.Pow(1f - t, 3f));
        if (t >= 1f)
        {
            height = target;
            time = -1f;
        }
        Apply();
    }

    private void Apply()
    {
        if (host == null) return;
        host.sizeDelta = new Vector2(host.sizeDelta.x, height);

        // 內容跟著簾口一起往下走。
        //
        // **釘住不動的那一版讀起來是被刷出來的**：遮罩往下擦、東西原地長出來。
        // 跟著走之後，整塊布和印在上面的東西是一起降下來的，只是還沒降到的那一
        // 段被簾口擋著。
        //
        // 符號要是 **full - height**（正值，往上）：收起來的時候內容在簾口上方，
        // 拉下來就跟著進來。最早那一版寫成 height - full（負值），內容被推到簾口
        // **下面**，所以動畫跑到快結束才從底下冒出來 —— 那是從下往上，不是拉下來。
        float travel = full - height;
        if (cloth != null) cloth.anchoredPosition = new Vector2(ClothCentre, travel);
        if (strip != null) strip.anchoredPosition = new Vector2(0f, travel - gap);

        // 全開就不需要裁了，而「音樂類型」的下拉選單正好伸出簾子外面。
        if (mask != null) mask.enabled = height < full - 0.5f;
    }
}
