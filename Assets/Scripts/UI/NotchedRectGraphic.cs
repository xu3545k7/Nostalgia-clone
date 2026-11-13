using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一個右下角被切掉一塊的矩形 —— L 形。
/// </summary>
/// <remarks>
/// **為什麼不是兩張圖拼。** 兩個矩形拼出來的 L，在接縫處各自有自己的邊；卡片的
/// 棕框和落影是 <see cref="Outline"/>／<see cref="Shadow"/> 從 mesh 複製出來的，
/// 兩張圖就會在內部那條接縫上也描一條線，L 看起來像兩張疊在一起的紙。一份 mesh
/// 只有一圈外輪廓，描邊和落影自然只出現在該出現的地方。
///
/// **為什麼切右下。** 這張卡的內容是 L 形的：左邊由上到下排滿（暫停鍵、曲名、
/// 作者、難度牌、時間、進度條），右邊只有上半截的曲繪。曲繪底下那塊紙從來沒有
/// 東西，而一塊空白的紙在畫面上仍然是不透明的 —— 它擋住軌道，卻什麼都沒說。
///
/// **缺口跟著曲繪的實際大小走。** 曲繪的框會照長寬比變矮（<c>ApplyCoverAspect</c>）：
/// 方的圖佔滿 176，而 16:9 的圖只有 99 高 —— 底下就多出 77 的空白紙。缺口照框的
/// 實際下緣切，才是真的「用不到的裁掉」。
///
/// 代價是卡片的輪廓會隨著歌換形狀。但那個形狀**說的是實話**：右邊那一塊紙存在的
/// 唯一理由就是撐住曲繪，曲繪多高、紙就該多高。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NotchedRectGraphic : MaskableGraphic
{
    [SerializeField] private Sprite fill;

    /// <summary>右下角要切掉多大一塊（區域座標，往左往上量）。</summary>
    [SerializeField] private Vector2 notch = new Vector2(196f, 70f);

    /// <summary>貼圖平鋪一格佔幾個 UI 單位。0 ＝ 不平鋪，整張拉滿。</summary>
    [SerializeField] private float tileUnits = 220f;

    public override Texture mainTexture =>
        fill != null && fill.texture != null ? fill.texture : s_WhiteTexture;

    /// <summary>設定外觀。<paramref name="sprite"/> 給 null 就是純色。</summary>
    public void Configure(Sprite sprite, Vector2 notchSize, float tile)
    {
        fill = sprite;
        notch = notchSize;
        tileUnits = tile;
        SetVerticesDirty();
        SetMaterialDirty();
    }

    /// <summary>只改缺口，顏色和貼圖不動。</summary>
    public void SetNotch(Vector2 notchSize)
    {
        if (notch == notchSize) return;
        notch = notchSize;
        SetVerticesDirty();
    }

    public Vector2 Notch => notch;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;

        Vector2[] path = Outline(r);
        // 凸多邊形，所以從第一個點拉扇形就夠，不需要真的做三角化。
        for (int i = 1; i + 1 < path.Length; i++)
        {
            int at = vh.currentVertCount;
            vh.AddVert(path[0], color, Uv(path[0].x, path[0].y, r));
            vh.AddVert(path[i], color, Uv(path[i].x, path[i].y, r));
            vh.AddVert(path[i + 1], color, Uv(path[i + 1].x, path[i + 1].y, r));
            vh.AddTriangle(at, at + 1, at + 2);
        }
    }

    /// <summary>
    /// 輪廓的頂點，順時針。沒有缺口是四個，斜切掉右下角之後是五個。
    /// </summary>
    /// <remarks>
    /// **為什麼是斜的不是直角。** 直角階梯在卡片上多出一個 270 度的內凹角，而那個
    /// 角不對應任何東西 —— 它只是「紙到這裡沒有了」。一條斜線沒有內凹角，它讀起來
    /// 是紙被裁過一刀；而且斜率自己會說話：曲繪越矮、空出來的紙越多，這一刀就越陡。
    ///
    /// 深度不變：右緣還是切到 <c>notch.y</c>，只是往左漸漸收回去，所以曲繪底下那道
    /// 留白維持原樣，被救回來的是左欄底下那塊本來一起被切掉的紙。
    /// </remarks>
    private Vector2[] Outline(Rect r)
    {
        float cutW = Mathf.Clamp(notch.x, 0f, r.width);
        float cutH = Mathf.Clamp(notch.y, 0f, r.height);
        if (cutW <= 0.01f || cutH <= 0.01f)
        {
            return new[]
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
                new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin),
            };
        }
        return new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax, r.yMax),
            new Vector2(r.xMax, r.yMin + cutH),   // 斜線的上端，在右緣
            new Vector2(r.xMax - cutW, r.yMin),   // 斜線的下端，在底緣
        };
    }


    /// <summary>
    /// 平鋪的 UV：**算的是世界位置，不是這一塊 quad 自己的 0–1**。
    /// </summary>
    /// <remarks>
    /// 照每塊三角形自己的 0–1 去貼的話，紙的紋理會在每一條內部邊上錯開 ——
    /// 一眼就看得出這張紙是拼的。
    /// </remarks>
    private Vector2 Uv(float x, float y, Rect bounds)
    {
        if (fill == null || tileUnits <= 0.01f)
        {
            return new Vector2((x - bounds.xMin) / Mathf.Max(bounds.width, 0.01f),
                               (y - bounds.yMin) / Mathf.Max(bounds.height, 0.01f));
        }
        return new Vector2((x - bounds.xMin) / tileUnits, (y - bounds.yMin) / tileUnits);
    }
}
