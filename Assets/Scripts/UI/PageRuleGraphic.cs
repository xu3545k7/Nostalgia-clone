using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The printed frame on a page: a heavy rule with a thin companion inside it,
/// broken and wandering the way old letterpress work always is.
/// </summary>
/// <remarks>
/// **Why the page needs a frame at all.** A block of text floating on parchment
/// reads as text on a background. A ruled border is what makes it a *page* --
/// it gives the paper an edge of its own, inside the physical edge of the panel,
/// and everything set inside it becomes the contents of a book rather than a
/// list on a card.
///
/// **Why it is not an <see cref="Outline"/>.** That component draws the whole
/// graphic again at an offset, so it can only ever be an even, perfectly
/// straight halo hugging the rect. What is wanted is the opposite: a rule set
/// *in* from the edge, uneven in weight, and missing where the plate did not
/// take. That is <see cref="OrnamentPen.WornFrame"/>.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PageRuleGraphic : MaskableGraphic
{
    [SerializeField] private Color ink = new Color(0.20f, 0.115f, 0.055f, 0.92f);
    /// <summary>Where the heavy rule sits, as a fraction of the short side.</summary>
    [SerializeField, Range(0.01f, 0.2f)] private float inset = 0.042f;
    [SerializeField, Range(0.5f, 12f)] private float weight = 4.2f;
    [SerializeField, Range(0f, 1f)] private float wear = 0.55f;
    /// <summary>Draw the thin companion rule inside the heavy one.</summary>
    [SerializeField] private bool doubled = true;
    [SerializeField] private int seed = 5;
    private BookShape.Profile profile = BookShape.Profile.Page;

    /// <summary>Which outline the rule follows.</summary>
    public void Shape(BookShape.Profile value)
    {
        profile = value;
        SetVerticesDirty();
    }

    public Color Ink
    {
        get => ink;
        set { if (ink == value) return; ink = value; SetVerticesDirty(); }
    }

    public int Seed
    {
        get => seed;
        set { if (seed == value) return; seed = value; SetVerticesDirty(); }
    }

    public float Wear
    {
        get => wear;
        set { if (Mathf.Approximately(wear, value)) return; wear = value; SetVerticesDirty(); }
    }

    /// <summary>
    /// Rules one panel. Sits above the paper and below whatever is set on it.
    /// </summary>
    public static PageRuleGraphic Attach(RectTransform page, int seed, Color ink, float inset,
        float weight, bool doubled = true, int siblingIndex = 0)
    {
        if (page == null) return null;

        Transform existing = page.Find("~PageRule");
        if (existing != null) return existing.GetComponent<PageRuleGraphic>();

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 第一次重建就會炸。
        var ruleObject = new GameObject("~PageRule", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(PageRuleGraphic));
        ruleObject.layer = page.gameObject.layer;
        RectTransform rect = ruleObject.GetComponent<RectTransform>();
        rect.SetParent(page, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, page.childCount - 1)));

        PageRuleGraphic rule = ruleObject.GetComponent<PageRuleGraphic>();
        rule.raycastTarget = false;
        rule.seed = seed;
        rule.ink = ink;
        rule.inset = inset;
        rule.weight = weight;
        rule.doubled = doubled;
        return rule;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = BookShape.Body(rectTransform.rect, profile);
        if (r.width <= 40f || r.height <= 40f) return;

        float shortSide = Mathf.Min(r.width, r.height);
        float margin = Mathf.Max(6f, shortSide * inset);

        // 影子壓得很淺：這是印在紙上的墨，不是浮在紙上的金。
        OrnamentPen pen = new OrnamentPen
        {
            shadowOffset = 0.9f,
            shadowStrength = 0.25f,
            relief = 0.22f,
        };

        float mu = margin / r.width;
        float mv = margin / r.height;
        pen.WornBookFrame(vh, r, profile, mu, mv, weight, ink, seed, wear);
        if (doubled)
            pen.WornBookFrame(vh, r, profile, mu + weight * 1.9f / r.width,
                mv + weight * 1.9f / r.height, weight * 0.33f,
                new Color(ink.r, ink.g, ink.b, ink.a * 0.8f), seed + 40, wear * 0.8f);

        // 角上的十字：兩條規線在角落交會後各自超出一點點，是印版對位留下的記號。
        float overshoot = weight * 2.6f;
        Vector2[] corners =
        {
            BookShape.Map(r, mu, mv, profile),
            BookShape.Map(r, mu, 1f - mv, profile),
            BookShape.Map(r, 1f - mu, mv, profile),
            BookShape.Map(r, 1f - mu, 1f - mv, profile),
        };
        for (int i = 0; i < corners.Length; i++)
        {
            float horizontal = corners[i].x < r.center.x ? 180f : 0f;
            float vertical = corners[i].y < r.center.y ? 270f : 90f;
            pen.WornLine(vh, corners[i], horizontal, overshoot, weight * 0.55f, ink,
                seed + 70 + i * 2, wear);
            pen.WornLine(vh, corners[i], vertical, overshoot, weight * 0.55f, ink,
                seed + 71 + i * 2, wear);
        }
    }
}
