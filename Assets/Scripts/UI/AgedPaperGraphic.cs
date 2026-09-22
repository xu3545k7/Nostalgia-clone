using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The age on a page: edges eaten unevenly dark, heavy wear in the corners,
/// blotchy tone across the field and a few foxing blooms.
/// </summary>
/// <remarks>
/// **Why an overlay and not a texture.** The paper fibre is a small tiled
/// sprite, so anything large drawn into it repeats three or four times across
/// one page -- and repetition is exactly what reads as "made by a machine". The
/// things that make a page look old are all *unique to that page*: this corner
/// is rubbed through, that edge is darker where a thumb held it. So they are
/// drawn per panel, at panel scale, from a seed.
///
/// **Why a vertex field and not shapes.** Stains have no outline. A grid of
/// vertices whose alpha comes from a noise field gives soft, arbitrary shapes
/// for free through interpolation, and one mesh covers edges, corners, mottling
/// and foxing at once -- no masks, no extra draw calls, no shader.
///
/// **Why the edge depth wanders.** A rectangle of even darkness reads as a
/// vignette, which is a photographic effect, not a worn edge. The depth is
/// modulated along each edge, so the dark bites in and retreats the way damp
/// and handling actually damage a page.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AgedPaperGraphic : MaskableGraphic
{
    /// <summary>Roughly how many pixels one grid cell covers.</summary>
    private const float CellSize = 22f;
    private const int MinCells = 6;
    private const int MaxCells = 52;

    [SerializeField] private int seed = 1;
    [SerializeField] private Color stain = new Color(0.28f, 0.16f, 0.075f, 1f);
    /// <summary>How far in the edge wear reaches, as a fraction of the short side.</summary>
    [SerializeField, Range(0.01f, 0.35f)] private float edgeDepth = 0.085f;
    [SerializeField, Range(0f, 1f)] private float strength = 0.80f;
    /// <summary>Blotchiness over the whole field, independent of the edges.</summary>
    [SerializeField, Range(0f, 1f)] private float mottle = 0.20f;
    [SerializeField, Range(0, 12)] private int foxing = 5;
    [SerializeField] private bool shaped;
    private BookShape.Profile profile = BookShape.Profile.Page;

    /// <summary>
    /// Bends the wear onto a book's outline instead of the panel's rectangle.
    /// </summary>
    /// <remarks>
    /// 髒是長在紙的邊上的。紙的邊一旦是弧的，這一層還照著矩形算，最外面那圈
    /// 黑就會在角落切過紙的輪廓 —— 一眼就看得出是兩個東西疊在一起。
    /// </remarks>
    public void Shape(BookShape.Profile value)
    {
        shaped = true;
        profile = value;
        SetVerticesDirty();
    }

    public int Seed
    {
        get => seed;
        set { if (seed == value) return; seed = value; SetVerticesDirty(); }
    }

    public Color Stain
    {
        get => stain;
        set { if (stain == value) return; stain = value; SetVerticesDirty(); }
    }

    public float EdgeDepth
    {
        get => edgeDepth;
        set { if (Mathf.Approximately(edgeDepth, value)) return; edgeDepth = value; SetVerticesDirty(); }
    }

    public float Strength
    {
        get => strength;
        set { if (Mathf.Approximately(strength, value)) return; strength = value; SetVerticesDirty(); }
    }

    public float Mottle
    {
        get => mottle;
        set { if (Mathf.Approximately(mottle, value)) return; mottle = value; SetVerticesDirty(); }
    }

    public int Foxing
    {
        get => foxing;
        set { if (foxing == value) return; foxing = value; SetVerticesDirty(); }
    }

    /// <summary>
    /// Lays the wear over one panel, under whatever that panel already carries.
    /// </summary>
    /// <param name="panel">The page or board to age.</param>
    /// <param name="seed">Anything stable and per-panel; two panels sharing a
    /// seed wear identically, which is the one thing that gives the trick away.</param>
    /// <param name="behindIndex">Sibling index to sit at. The wear belongs over
    /// the paper and under the text.</param>
    public static AgedPaperGraphic Attach(RectTransform panel, int seed, int behindIndex = 0)
    {
        if (panel == null) return null;

        Transform existing = panel.Find("~AgedPaper");
        if (existing != null)
        {
            AgedPaperGraphic found = existing.GetComponent<AgedPaperGraphic>();
            if (found != null) found.Seed = seed;
            return found;
        }

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 第一次重建就會炸。
        var wearObject = new GameObject("~AgedPaper", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(AgedPaperGraphic));
        wearObject.layer = panel.gameObject.layer;
        RectTransform rect = wearObject.GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetSiblingIndex(Mathf.Clamp(behindIndex, 0, panel.childCount - 1));

        AgedPaperGraphic wear = wearObject.GetComponent<AgedPaperGraphic>();
        wear.raycastTarget = false;
        wear.seed = seed;
        return wear;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 16f || r.height <= 16f) return;
        if (shaped) r = BookShape.Body(r, profile);

        float shortSide = Mathf.Min(r.width, r.height);
        float depth = Mathf.Max(4f, shortSide * edgeDepth);
        float offset = (seed % 97) * 13.7f + 3.1f;

        float[] xs = Axis(r.xMin, r.xMax, depth);
        float[] ys = Axis(r.yMin, r.yMax, depth);

        for (int y = 0; y < ys.Length; y++)
        {
            float v = Mathf.InverseLerp(r.yMin, r.yMax, ys[y]);
            for (int x = 0; x < xs.Length; x++)
            {
                float u = Mathf.InverseLerp(r.xMin, r.xMax, xs[x]);
                Vector2 point = shaped
                    ? BookShape.Map(r, u, v, profile)
                    : new Vector2(xs[x], ys[y]);
                float alpha = Wear(point, r, depth, offset, u, v);
                Color colour = stain;
                // 邊上的髒是灰的，紙面上的斑是鏽紅的 —— 一種顏色的髒看起來像
                // 陰影，兩種才像時間。
                colour = Color.Lerp(colour, new Color(0.45f, 0.24f, 0.10f, 1f),
                    Noise(point * 0.012f, offset + 41f) * 0.55f);
                colour.a = alpha;
                OrnamentPen.AddVertex(vh, point, colour);
            }
        }

        int stride = xs.Length;
        for (int y = 0; y < ys.Length - 1; y++)
        {
            for (int x = 0; x < xs.Length - 1; x++)
            {
                int a = y * stride + x;
                vh.AddTriangle(a, a + 1, a + stride + 1);
                vh.AddTriangle(a + stride + 1, a + stride, a);
            }
        }
    }

    /// <summary>
    /// Sample positions along one axis: packed near the edges, even in the middle.
    /// </summary>
    /// <remarks>
    /// A uniform grid cannot hold this. The dark of a worn edge lives in its
    /// outermost few pixels and is gone within a couple of dozen -- sampled every
    /// 20px, that whole falloff sits between two vertices and interpolation
    /// smears it into a soft vignette, which is exactly the even, photographic
    /// look being avoided. Rows are placed at 0, 2, 5, 9, 15 … of the wear depth
    /// so the edge stays a hard, ragged line, and the interior gets the cheap
    /// even spacing it deserves.
    /// </remarks>
    private static float[] Axis(float min, float max, float depth)
    {
        // 邊上的取樣位置，以磨損深度為單位。
        float[] ramp = { 0f, 0.045f, 0.10f, 0.18f, 0.30f, 0.46f, 0.66f, 0.90f, 1.20f, 1.60f };
        var points = new System.Collections.Generic.List<float>(64);
        for (int i = 0; i < ramp.Length; i++)
        {
            float d = ramp[i] * depth;
            if (d >= (max - min) * 0.5f) break;
            points.Add(min + d);
            points.Add(max - d);
        }

        float inner = min + ramp[ramp.Length - 1] * depth;
        float outer = max - ramp[ramp.Length - 1] * depth;
        if (outer - inner > CellSize)
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt((outer - inner) / CellSize), MinCells, MaxCells);
            for (int i = 1; i < steps; i++)
                points.Add(Mathf.Lerp(inner, outer, i / (float)steps));
        }

        points.Sort();
        // 幾乎重疊的取樣點會生出退化的三角形，先清掉。
        for (int i = points.Count - 1; i > 0; i--)
            if (points[i] - points[i - 1] < 0.4f) points.RemoveAt(i);
        return points.ToArray();
    }

    /// <summary>How dirty this point on the page is, 0 to 1.</summary>
    private float Wear(Vector2 point, Rect r, float depth, float offset, float u, float v)
    {
        // ── 四邊。每一邊的吃進來的深度沿著邊自己起伏 ──────────────────
        // 到邊的距離用參數空間量，不是用座標量：紙的輪廓彎掉以後，「離邊多遠」
        // 指的是沿著紙面往內走多遠，不是離某一條直線多遠。
        float left = Bite(u * r.width, depth, Along(point.y, 0.7f, offset));
        float right = Bite((1f - u) * r.width, depth, Along(point.y, 4.3f, offset));
        float bottom = Bite(v * r.height, depth, Along(point.x, 8.1f, offset));
        float top = Bite((1f - v) * r.height, depth, Along(point.x, 12.9f, offset));
        float edge = Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top));

        // ── 角落。書拿在手上是靠角，磨得最兇的永遠是那四個地方 ──────────
        float cornerX = Mathf.Min(u, 1f - u);
        float cornerY = Mathf.Min(v, 1f - v);
        float corner = Mathf.Clamp01(1f - cornerX / 0.24f) * Mathf.Clamp01(1f - cornerY / 0.24f);
        corner *= 0.55f + 0.75f * Noise(point * 0.02f, offset + 19f);
        edge = Mathf.Clamp01(edge + corner * 0.85f);

        // ── 紙面本身的不勻 ────────────────────────────────────────────
        float cloud = Noise(point * 0.006f, offset + 63f);
        float grain = Noise(point * 0.028f, offset + 77f);
        float blotch = Mathf.Clamp01(cloud * 0.75f + grain * 0.25f - 0.42f) * 1.7f;

        float total = edge * strength + blotch * mottle;

        // ── 霉斑。少少幾點，但沒有它就只是一張髒紙，不是一張老紙 ────────
        for (int i = 0; i < foxing; i++)
        {
            float fx = OrnamentPen.Wobble(seed * 31 + i, 11);
            float fy = OrnamentPen.Wobble(seed * 31 + i, 29);
            float radius = Mathf.Lerp(0.045f, 0.11f, OrnamentPen.Wobble(seed * 31 + i, 53))
                           * Mathf.Min(r.width, r.height);
            Vector2 centre = new Vector2(Mathf.Lerp(r.xMin, r.xMax, 0.1f + fx * 0.8f),
                                         Mathf.Lerp(r.yMin, r.yMax, 0.1f + fy * 0.8f));
            float distance = Vector2.Distance(point, centre);
            if (distance >= radius) continue;
            float bloom = 1f - distance / radius;
            // 邊緣濃、中間淡：霉斑是往外長的，不是一個點。
            total += bloom * bloom * (0.35f + 0.5f * Noise(point * 0.05f, offset + i * 7f)) * 0.55f;
        }

        return Mathf.Clamp01(total);
    }

    /// <summary>
    /// How deep the damage reaches at one point along an edge.
    /// </summary>
    /// <remarks>
    /// Two frequencies, not one. The slow term is where the page was damp or
    /// held -- broad areas darker than the rest. The fast one is the tear: the
    /// edge of old paper is ragged at a scale of a few millimetres, and with the
    /// slow term alone the border comes out as a soft, even band, which is a
    /// vignette rather than an edge.
    /// </remarks>
    private static float Along(float position, float lane, float offset)
    {
        float slow = Noise(new Vector2(position * 0.013f, lane), offset);
        float fast = Noise(new Vector2(position * 0.075f, lane + 0.5f), offset + 5f);
        return Mathf.Clamp01(slow * 0.62f + fast * 0.55f);
    }

    /// <summary>
    /// The edge term: 1 at the very edge, gone by <paramref name="depth"/>, with
    /// how far it reaches modulated by <paramref name="wander"/>.
    /// </summary>
    private static float Bite(float distance, float depth, float wander)
    {
        float reach = depth * (0.35f + 1.5f * wander);
        if (distance >= reach || reach <= 0.01f) return 0f;
        float t = 1f - distance / reach;
        // 三次方：最外面一兩公釐幾乎全黑，往內很快就退掉。
        return t * t * t;
    }

    private static float Noise(Vector2 point, float offset)
    {
        return Mathf.PerlinNoise(point.x + offset, point.y + offset * 0.61f);
    }
}
