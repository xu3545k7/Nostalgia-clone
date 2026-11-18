using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 變速預告時，框住「等一下的 BPM」那個白框。
/// </summary>
/// <remarks>
/// **為什麼是切角不是直角。** 這一塊的周圍全是直角 —— 分數框、字卡、面板。
/// 再多一個小直角框，它會讀成「版面的一部分」，而不是「這個數字被特別標出來」。
/// 切角（連同內側那一條細線）是銘牌的語彙，和整套古典書頁的 UI 同一個家族，
/// 而且它一眼就看得出是**為了這個數字**臨時出現的東西。
///
/// **雙線不是裝飾，是尺度問題。** 單一條線要夠粗才看得見，而夠粗的框在一個
/// 兩位數旁邊就變成一塊實心的牌子，把數字壓掉。細外框＋更細的內線總共佔不到
/// 三個像素，卻有明確的邊界 —— 這是銘牌、書名頁和獎狀用了幾百年的做法。
///
/// 尺寸全部以**框自己的高度**為單位，所以 BPM 從兩位變三位、字級哪天改了，
/// 線寬和切角都跟著走，不用重調。
///
/// 這個類別只會被 <c>AddComponent</c> 叫到，不掛在任何 prefab 上。
/// </remarks>
public sealed class BpmChangeFrame : MaskableGraphic
{
    /// <summary>外框線寬，以框高為單位。</summary>
    private const float OuterWidth = 0.040f;

    /// <summary>內線離外框多遠、多細，一樣以框高為單位。</summary>
    private const float InnerInset = 0.090f;
    private const float InnerWidth = 0.022f;

    /// <summary>四個角切掉多少，以框高為單位。</summary>
    private const float Chamfer = 0.20f;

    /// <summary>內線比外框淡多少。</summary>
    private const float InnerAlpha = 0.38f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;

        float unit = r.height;
        float outer = Mathf.Max(1f, unit * OuterWidth);
        float chamfer = unit * Chamfer;
        Ring(vh, r, chamfer, outer, color);

        float inset = unit * InnerInset;
        if (r.width - inset * 2f <= 2f || r.height - inset * 2f <= 2f) return;
        Color inner = color;
        inner.a *= InnerAlpha;
        Rect innerRect = new Rect(r.xMin + inset, r.yMin + inset,
            r.width - inset * 2f, r.height - inset * 2f);
        Ring(vh, innerRect, Mathf.Max(0f, chamfer - inset),
            Mathf.Max(0.8f, unit * InnerWidth), inner);
    }

    /// <summary>一圈切角的細框：外圈八個點、內圈八個點，之間鋪一圈四邊形。</summary>
    private static void Ring(VertexHelper vh, Rect r, float chamfer, float width, Color colour)
    {
        chamfer = Mathf.Clamp(chamfer, 0f, Mathf.Min(r.width, r.height) * 0.5f);
        float innerChamfer = Mathf.Max(0f, chamfer - width);
        Rect hole = new Rect(r.xMin + width, r.yMin + width,
            Mathf.Max(0.01f, r.width - width * 2f),
            Mathf.Max(0.01f, r.height - width * 2f));

        Vector2[] a = Outline(r, chamfer);
        Vector2[] b = Outline(hole, innerChamfer);

        UIVertex vert = UIVertex.simpleVert;
        vert.color = colour;
        vert.uv0 = Vector2.zero;
        int at = vh.currentVertCount;
        for (int i = 0; i < a.Length; i++)
        {
            vert.position = a[i]; vh.AddVert(vert);
            vert.position = b[i]; vh.AddVert(vert);
        }
        for (int i = 0; i < a.Length; i++)
        {
            int p = at + i * 2;
            int q = at + ((i + 1) % a.Length) * 2;
            vh.AddTriangle(p, p + 1, q + 1);
            vh.AddTriangle(p, q + 1, q);
        }
    }

    private static Vector2[] Outline(Rect r, float c)
    {
        return new[]
        {
            new Vector2(r.xMin + c, r.yMin),
            new Vector2(r.xMax - c, r.yMin),
            new Vector2(r.xMax, r.yMin + c),
            new Vector2(r.xMax, r.yMax - c),
            new Vector2(r.xMax - c, r.yMax),
            new Vector2(r.xMin + c, r.yMax),
            new Vector2(r.xMin, r.yMax - c),
            new Vector2(r.xMin, r.yMin + c),
        };
    }
}
