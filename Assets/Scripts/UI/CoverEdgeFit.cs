using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 曲繪外面那一圈金邊：貼著**畫實際被畫出來的大小**，不是貼著它的框。
/// </summary>
/// <remarks>
/// **為什麼不能用 uGUI 的 `Outline`。** 它是把整份網格平移之後再畫四份，而那四份
/// 沿用原本的 UV —— `UI/Default` 是 `tex2D(_MainTex, uv) * color`，所以「金邊」畫
/// 出來其實是**封面自己的縮圖**乘上金色：每一首歌的邊看起來亮度都不一樣，深色封
/// 面幾乎沒有邊，帶透明角的封面邊會破洞。
///
/// **為什麼要自己算大小。** 曲繪是 `preserveAspect`，所以它在自己的框裡是置中留
/// 白的；照框去描，邊會浮在畫的外面。這裡照 sprite 的長寬比重算一次，貼著畫的四
/// 邊走。
/// </remarks>
internal sealed class CoverEdgeFit : UIBehaviour
{
    private static readonly Color Lit = new Color(0.92f, 0.78f, 0.44f, 1f);
    private static readonly Color Shade = new Color(0.92f * 0.45f, 0.78f * 0.45f, 0.44f * 0.45f, 1f);

    private Image cover;
    private EdgeLightGraphic edge;
    private RectTransform rect;
    private RectTransform box;
    private Sprite lastSprite;
    private Vector2 lastBox;
    private bool lastEnabled;

    public void Bind(Image coverImage, EdgeLightGraphic ring)
    {
        cover = coverImage;
        edge = ring;
        rect = transform as RectTransform;
        box = coverImage != null ? coverImage.rectTransform : null;
        if (edge != null)
        {
            edge.Thickness = 3f;
            edge.Radius = 0f;
            // 光從左上來，和黃銅牌、書籤的線腳同一個方向。
            edge.SetSides(new[] { Lit, Lit, Shade, Shade });
        }
        lastSprite = null;
        Apply();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Apply();
    }

    private void Update()
    {
        if (cover == null || box == null) return;
        // 也要看 enabled：輪播換到沒有曲繪的曲子時，只會把 Image 關掉、不清 sprite
        // —— 只比對 sprite 的話，空白的卡中間會浮著上一首的金框。
        if (cover.sprite != lastSprite || cover.enabled != lastEnabled ||
            box.rect.size != lastBox) Apply();
        // 位置每幀對齊，不進 Apply 的快取判斷：曲繪的**位置**可以在大小不變的
        // 情況下動（卡片滑動、版面重排），而那時候上面三個條件一個都不會變。
        FollowCover();
    }

    private void Apply()
    {
        if (cover == null || box == null || rect == null || edge == null) return;
        lastSprite = cover.sprite;
        lastBox = box.rect.size;
        lastEnabled = cover.enabled;

        if (lastSprite == null || !cover.enabled || lastBox.x < 1f || lastBox.y < 1f)
        {
            edge.enabled = false;
            return;
        }
        edge.enabled = true;

        float aspect = lastSprite.rect.width / Mathf.Max(0.0001f, lastSprite.rect.height);
        float boxAspect = lastBox.x / Mathf.Max(0.0001f, lastBox.y);
        float width = aspect >= boxAspect ? lastBox.x : lastBox.y * aspect;
        float height = aspect >= boxAspect ? lastBox.x / aspect : lastBox.y;
        rect.sizeDelta = new Vector2(width, height);
        FollowCover();
    }

    /// <summary>
    /// 把金邊搬到曲繪的正中心。
    /// </summary>
    /// <remarks>
    /// **用世界座標對齊，不靠錨點推算。** 原本是「取曲繪兩個 anchor 的中點，
    /// anchoredPosition 給 0」—— 那只有在曲繪用**對稱的比例錨點**時才等於它的中心。
    /// 曲繪改成「縱向拉滿 ＋ 不對稱的絕對位移」之後（上下空隙要對稱那次），錨點中點
    /// 變成卡片正中央，金邊就留在那裡，和圖差了一大截。
    ///
    /// 這裡直接問曲繪「你的中心在世界的哪裡」，不管它是怎麼錨的 —— 以後版面再改，
    /// 這一行都不用跟著改。
    /// </remarks>
    private void FollowCover()
    {
        if (cover == null || rect == null) return;
        RectTransform coverRect = cover.rectTransform;
        rect.position = coverRect.TransformPoint(coverRect.rect.center);
    }
}
