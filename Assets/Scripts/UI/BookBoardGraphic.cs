using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The body of the volume: leather boards with a rounded back, the paper block
/// showing along the three open sides, and every edge on the curve rather than
/// on the rect.
/// </summary>
/// <remarks>
/// **Why this replaces the panel's Image.** An <see cref="Image"/> is a quad. No
/// amount of tinting, outlining or ornament changes the fact that its silhouette
/// is four straight lines meeting at right angles, and that silhouette is the
/// first thing the eye reads -- long before any texture on it. The board is a
/// mapped grid instead, so the fore-edge stands proud in the middle, the corners
/// are knocked round, and head and tail bow.
///
/// **Why it carries the texture itself.** Emptying the Image would throw away
/// the tiled paper fibre with it, and flat colour inside a good outline still
/// reads as a shape rather than a material. The grain is sampled straight off
/// the panel's own sprite, with UVs taken from the vertex position, so the fibre
/// keeps its real size and never stretches over the curve.
///
/// **Why the paper block is drawn here and not as three more objects.** The
/// three light strips along the open sides have to follow the same curve as the
/// leather they sit in, to within a pixel, or the book comes apart. They are
/// regions of this one mesh, decided per vertex, so they cannot drift.
///
/// **Why the light is baked into the vertices.** A book is a solid: the boards
/// catch light along the fore-edge and fall into shadow at the hinge, and the
/// rounded back has a highlight running down it. Flat colour plus an outline
/// reads as a sticker of a book. It costs nothing at runtime -- the colours are
/// computed once, when the mesh is built.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BookBoardGraphic : MaskableGraphic
{
    /// <summary>Where the spine strip ends, in u.</summary>
    private const float SpineEdge = 0.052f;
    /// <summary>Where the paper block starts on the fore-edge, in u.</summary>
    private const float ForeEdge = 0.981f;
    /// <summary>How far the paper block shows along head and tail, in v.</summary>
    private const float HeadEdge = 0.987f;
    private const float TailEdge = 0.013f;
    /// <summary>The recessed field the cover plate sits on.</summary>
    private const float FieldU = 0.088f;
    private const float FieldV = 0.062f;
    /// <summary>The gap that opens between a warped board and the text block.</summary>
    private const float GapU = 0.020f;
    private const float GapV = 0.017f;
    /// <summary>How much of the edge is the board's own thickness.</summary>
    private const float Thickness = 0.016f;

    private Color leather = new Color(0.17f, 0.075f, 0.045f, 1f);
    private Color field = new Color(0.22f, 0.095f, 0.052f, 1f);
    private Color paper = new Color(0.86f, 0.80f, 0.64f, 1f);
    private Color gilt = ClassicalBookUITheme.Gold;
    private Texture grain;
    private float grainScale = 1f;
    private int seed = 3;
    private BookShape.Profile profile = BookShape.Profile.Volume;
    private bool bound = true;
    private bool wrapper;

    /// <summary>
    /// A stitched paper cover rather than leather over boards.
    /// </summary>
    /// <remarks>
    /// What this library is actually full of is study editions: a sheet of
    /// coloured card folded round the leaves and stapled. That cover has no
    /// gilding and no rounded back, and only a sliver of the block shows at the
    /// fore-edge -- the ornament on it is *printed*, which is why the rules on
    /// this card are ink and the gold is kept for the two hardest tiers.
    /// </remarks>
    public bool Wrapper
    {
        get => wrapper;
        set { if (wrapper == value) return; wrapper = value; SetVerticesDirty(); }
    }

    public override Texture mainTexture => grain != null ? grain : base.mainTexture;

    /// <summary>Leather, the recessed field, the paper block and the gilding.</summary>
    public void Set(Color leather, Color field, Color paper, Color gilt)
    {
        this.leather = leather;
        this.field = field;
        this.paper = paper;
        this.gilt = gilt;
        SetVerticesDirty();
    }

    /// <summary>The tiling surface, and how many pixels one tile covers.</summary>
    public void Grain(Texture texture, float pixelsPerTile)
    {
        grain = texture;
        grainScale = Mathf.Max(1f, pixelsPerTile);
        SetMaterialDirty();
        SetVerticesDirty();
    }

    public int Seed
    {
        get => seed;
        set { if (seed == value) return; seed = value; SetVerticesDirty(); }
    }

    /// <summary>
    /// False for a single leaf: no spine, no hinge, no paper block -- one sheet,
    /// evenly lit, whose edges still curve.
    /// </summary>
    public bool Bound
    {
        get => bound;
        set { if (bound == value) return; bound = value; SetVerticesDirty(); }
    }

    public BookShape.Profile Profile
    {
        get => profile;
        set { profile = value; SetVerticesDirty(); }
    }

    /// <summary>
    /// Puts a board behind everything already on <paramref name="panel"/>, and
    /// takes the panel's own <see cref="Image"/> out of the picture.
    /// </summary>
    /// <remarks>
    /// The Image is emptied rather than removed: buttons use it as their raycast
    /// target and their tint target, and anything that looks it up still finds
    /// it. Its <see cref="Outline"/> and <see cref="Shadow"/> are switched off
    /// with it -- both draw the same quad again at an offset, so on a curved book
    /// they are a straight rectangle peeking out from behind the boards.
    /// </remarks>
    public static BookBoardGraphic Attach(RectTransform panel, int seed)
    {
        if (panel == null) return null;

        Transform existing = panel.Find("~BookBoard");
        if (existing != null) return existing.GetComponent<BookBoardGraphic>();

        Texture inherited = null;
        Image quad = panel.GetComponent<Image>();
        if (quad != null)
        {
            if (quad.sprite != null) inherited = quad.sprite.texture;
            Color hidden = quad.color;
            hidden.a = 0f;
            quad.color = hidden;
        }
        Outline outline = panel.GetComponent<Outline>();
        if (outline != null) outline.enabled = false;
        foreach (Shadow shadow in panel.GetComponents<Shadow>())
            if (!(shadow is Outline)) shadow.enabled = false;

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 第一次重建就會炸。
        var boardObject = new GameObject("~BookBoard", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(BookBoardGraphic));
        boardObject.layer = panel.gameObject.layer;
        RectTransform rect = boardObject.GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();

        BookBoardGraphic board = boardObject.GetComponent<BookBoardGraphic>();
        board.raycastTarget = false;
        board.seed = seed;
        if (inherited != null) board.Grain(inherited, inherited.width);
        return board;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect panel = rectTransform.rect;
        if (panel.width <= 32f || panel.height <= 32f) return;

        Rect r = BookShape.Body(panel, profile);
        // 書芯先鋪，封面再蓋上去。厚度是看到內頁看出來的，不是看陰影看出來的。
        if (profile.block > 0f) Leaves(vh, panel);

        float[] us = Samples(26, bound
            ? new[] { SpineEdge, ForeEdge, ForeEdge - GapU, FieldU, 1f - FieldU,
                Thickness, 1f - Thickness }
            : new float[0]);
        float[] vs = Samples(30, bound
            ? new[] { TailEdge, TailEdge + GapV, HeadEdge, HeadEdge - GapV, FieldV, 1f - FieldV,
                Thickness, 1f - Thickness }
            : new float[0]);

        for (int y = 0; y < vs.Length; y++)
        {
            for (int x = 0; x < us.Length; x++)
            {
                Vector2 point = BookShape.Map(r, us[x], vs[y], profile);
                var vertex = UIVertex.simpleVert;
                vertex.position = point;
                vertex.color = Sample(us[x], vs[y]);
                // 貼圖的座標跟著實際位置走，不跟著格子走：弧線把格子拉開的地方，
                // 紋理不會跟著被拉長。
                vertex.uv0 = new Vector2(point.x / grainScale, point.y / grainScale);
                vh.AddVert(vertex);
            }
        }

        int stride = us.Length;
        for (int y = 0; y < vs.Length - 1; y++)
        {
            for (int x = 0; x < us.Length - 1; x++)
            {
                int a = y * stride + x;
                vh.AddTriangle(a, a + 1, a + stride + 1);
                vh.AddTriangle(a + stride + 1, a + stride, a);
            }
        }
    }

    /// <summary>
    /// The text block: the cut edge of the leaves, showing out from under the
    /// cover along the fore-edge and the tail.
    /// </summary>
    /// <remarks>
    /// **Why one strip and not a stack of quads.** The first version was five
    /// copies of the whole outline, each offset a little further. At the size
    /// this is drawn each copy is three or four pixels wide, so what you saw was
    /// five hard-edged slabs with aliased steps between them -- coarse, and
    /// nothing like paper. This is one piece of geometry whose colour varies
    /// across the band, sampled densely enough that the leaves come out as a
    /// smooth striation the renderer can interpolate and antialias for free.
    ///
    /// **Why the samples are packed on two sides.** Only the strip that sticks
    /// out from under the cover is ever visible, so that is where the detail has
    /// to be. The middle of the block is behind the boards and gets four samples
    /// to close the mesh.
    ///
    /// **Why the exposed band is exactly u > 1 - d and v < d.** The block is the
    /// cover's own outline offset by the same amount in both axes, so in the
    /// block's own coordinates the cover hides everything except a band of that
    /// width down the right side and along the bottom. No clipping needed.
    /// </remarks>
    private void Leaves(VertexHelper vh, Rect panel)
    {
        Rect at = BookShape.BlockOf(panel, profile);
        if (at.width <= 4f || at.height <= 4f) return;

        // 露出來的量：書口是整個 depth，地腳只有它的一部分。書背那一側是零
        // ——書芯縫在那裡，和封面同一條邊。
        float depth = Mathf.Min(panel.width, panel.height) * profile.block;
        float du = Mathf.Clamp(depth / at.width, 0.001f, 0.5f);
        float dv = Mathf.Clamp(depth * BookShape.TailShare / at.height, 0.001f, 0.5f);

        float[] us = Band(du, false);
        float[] vs = Band(dv, true);

        for (int y = 0; y < vs.Length; y++)
        {
            for (int x = 0; x < us.Length; x++)
            {
                Vector2 point = BookShape.Map(at, us[x], vs[y], profile);
                var vertex = UIVertex.simpleVert;
                vertex.position = point;
                vertex.color = Leaf(us[x], vs[y], du, dv);
                vertex.uv0 = new Vector2(point.x / grainScale, point.y / grainScale);
                vh.AddVert(vertex);
            }
        }

        int stride = us.Length;
        for (int y = 0; y < vs.Length - 1; y++)
        {
            for (int x = 0; x < us.Length - 1; x++)
            {
                int a = y * stride + x;
                vh.AddTriangle(a, a + 1, a + stride + 1);
                vh.AddTriangle(a + stride + 1, a + stride, a);
            }
        }
    }

    /// <summary>The colour of the block's cut edge at one point.</summary>
    private Color Leaf(float u, float v, float du, float dv)
    {
        // 0 貼著封面，1 在最外面。
        float across = Mathf.Clamp01(Mathf.Max((u - (1f - du)) / du, (dv - v) / dv));

        // 和邊平行的細紋，就是一張一張紙的斷面。頻率高、振幅小 —— 紙疊看起來
        // 是「有紋理的一條」，不是「幾塊拼起來的」。
        float leaf = Mathf.Sin(across * Mathf.PI * 11f) * 0.5f + 0.5f;
        float dust = Mathf.Sin(across * Mathf.PI * 37f + u * 90f) * 0.5f + 0.5f;

        // 書口是一塊亮的實體，不是一條暗邊。它之所以讀得出來是在封面「底下」，
        // 靠的是接縫那道影子，不是靠整條壓暗 —— 整條壓暗只會變成畫在封面外面
        // 的一道輪廓線。
        Color colour = paper * Mathf.Lerp(0.94f, 0.66f, across);
        colour *= 1f - leaf * 0.13f - dust * 0.05f;

        // 封面壓在上面的那道接縫。最外側再收一點，書芯才有自己的輪廓。
        float contact = Mathf.Clamp01(1f - across / 0.14f);
        colour = Color.Lerp(colour, colour * 0.26f, contact * contact);
        colour = Color.Lerp(colour, colour * 0.62f, Mathf.Clamp01((across - 0.86f) / 0.14f));
        colour.a = 1f;
        return colour;
    }

    /// <summary>
    /// Samples along one axis: dense across the exposed band, four in the middle.
    /// </summary>
    private static float[] Band(float depth, bool atStart)
    {
        const int detail = 18;
        var points = new List<float>(detail + 6);
        for (int i = 0; i <= detail; i++)
        {
            float t = i / (float)detail * depth;
            points.Add(atStart ? t : 1f - t);
        }
        // 0 和 1 一定要在裡面。少了遠端那個端點，網格就從 19% 才開始 —— 地腳
        // 那條紙會在離書背五分之一張卡的地方憑空停住，看起來就是「紙沒連著
        // 書背」。這種洞不會報錯，只會少畫一塊。
        for (int i = 0; i <= 5; i++)
        {
            float t = atStart ? Mathf.Lerp(depth, 1f, i / 5f) : Mathf.Lerp(0f, 1f - depth, i / 5f);
            points.Add(t);
        }
        points.Sort();
        for (int i = points.Count - 1; i > 0; i--)
            if (points[i] - points[i - 1] < 0.0004f) points.RemoveAt(i);
        return points.ToArray();
    }

    /// <summary>What the book is made of at (u, v), lit from the upper left.</summary>
    private Color Sample(float u, float v)
    {
        if (!bound)
        {
            // 單張紙：整面同一種材質，只有一點很淡的向光。
            Color sheet = field * (0.96f + 0.09f * v + 0.04f * (1f - u));
            sheet.a = 1f;
            return sheet;
        }

        // 書背那一段的頭尾是被皮包過去的，紙塊到那裡就停 —— 一路露到最左邊
        // 的話，書背就不是包起來的，是黏上去的。
        bool spineStrip = u < SpineEdge;
        // 書芯已經在封面外面實實在在鋪了一疊，封面自己就不必再描一圈紙色的
        // 邊 —— 兩個一起出現會變成兩層書口。
        bool block = !spineStrip && profile.block <= 0f
                     && (u > ForeEdge || v > HeadEdge || v < TailEdge);

        Color colour;
        if (block)
        {
            // 書口。一疊紙的斷面：細密的明暗，越靠外越暗，灰塵都積在那裡。
            // 角落同時屬於兩邊。取比較深的那一個，兩條紙塊才會在角上接成一片，
            // 而不是切出一個缺口。
            float across = 0f;
            if (u > ForeEdge) across = Mathf.Max(across, (u - ForeEdge) / (1f - ForeEdge));
            if (v > HeadEdge) across = Mathf.Max(across, (v - HeadEdge) / (1f - HeadEdge));
            if (v < TailEdge) across = Mathf.Max(across, (TailEdge - v) / TailEdge);
            float leaves = Mathf.Sin((u * 620f + v * 700f) * 0.9f) * 0.5f + 0.5f;
            colour = Color.Lerp(paper, paper * 0.5f, across * 0.75f + leaves * 0.12f);
            colour = Edge(colour, u, v);
        }
        else if (spineStrip)
        {
            float round = u / SpineEdge;
            if (wrapper)
            {
                // 摺過去的紙背：沒有圓背，只有一道摺線和它兩側的明暗。整條都
                // 比封面暗 —— 畫亮的話，書背那一側會比書口還顯眼，看起來像紙
                // 長在錯的一邊。
                float crease = Mathf.Abs(round - 0.62f);
                colour = Color.Lerp(leather * 0.62f, leather * 0.94f, Mathf.Clamp01(round));
                colour = Color.Lerp(colour, colour * 0.6f, Mathf.Clamp01(1f - crease / 0.16f));
            }
            else
            {
                // 圓背：中央受光、兩側沉下去，光帶隨著弧面走。
                float lit = Mathf.Sin(round * Mathf.PI);
                colour = Color.Lerp(leather * 0.42f,
                    Color.Lerp(leather * 1.45f, gilt, 0.22f), lit * lit);
            }
        }
        else
        {
            bool inField = u > FieldU && u < 1f - FieldU && v > FieldV && v < 1f - FieldV;
            colour = inField ? field : leather;

            // 摺口的溝：書背旁邊那一道，皮繞過板子的地方。
            float hinge = Mathf.Clamp01(1f - Mathf.Abs(u - SpineEdge * 1.8f) / 0.055f);
            colour = Color.Lerp(colour, colour * 0.34f, hinge * 0.9f);

            // 字盤的邊：一道壓進皮裡的暗痕，讓中央那一塊沉下去。
            float panel = Mathf.Max(Ridge(u, FieldU), Mathf.Max(Ridge(u, 1f - FieldU),
                Mathf.Max(Ridge(v, FieldV), Ridge(v, 1f - FieldV))));
            colour = Color.Lerp(colour, colour * 0.62f, panel * 0.8f);

            // 板子的光：左上受光，往右下沉。紙是啞面的，明暗要比皮平得多。
            colour *= wrapper
                ? 0.955f + 0.06f * (1f - u) + 0.05f * v
                : 0.88f + 0.15f * (1f - u) + 0.13f * v;

            // ── 翹起來的板緣 ────────────────────────────────────────────
            // 闔上的書，板子不會平貼在書芯上：頭、尾和書口三邊都會離開一點，
            // 露出一道縫。角落張得最開，中間幾乎貼著 —— 這正是那道縫讓封面
            // 看起來是一塊有厚度、會翹的板子，而不是一張貼上去的紙。
            float lift = Mathf.Max(Gap(v, TailEdge, GapV), Gap(1f - v, 1f - HeadEdge, GapV));
            lift = Mathf.Max(lift, Gap(1f - u, 1f - ForeEdge, GapU));
            if (lift > 0f)
            {
                // 翹多少完全看離書背多遠。書背那一頭是縫死壓平的，那裡不可能
                // 有縫；越靠書口板子越自由，縫也張得越開。
                float open = Mathf.Lerp(0.08f, 1f, BookShape.Free(u, profile));
                // 縫本身是暗的，緊接在它內側的皮則被光挑起來 —— 一暗一亮才
                // 讀成「翹起」，只有暗的話讀成「髒」。
                colour = Color.Lerp(colour, colour * 0.30f, lift * open);
                colour = Color.Lerp(colour, colour * 1.5f, Mathf.Clamp01(1f - lift * 2.4f) * open * 0.55f);
            }

            colour = Edge(colour, u, v);
        }

        colour.a = 1f;
        return colour;
    }

    /// <summary>
    /// How far into the gap band a coordinate is: 1 hard against the paper
    /// block, falling to 0 one band-width inside the board.
    /// </summary>
    private static float Gap(float t, float edge, float band)
    {
        if (t < edge || t > edge + band) return 0f;
        return 1f - (t - edge) / band;
    }

    /// <summary>A narrow crease centred on <paramref name="at"/>.</summary>
    private static float Ridge(float t, float at)
    {
        float d = Mathf.Abs(t - at);
        return d > 0.006f ? 0f : 1f - d / 0.006f;
    }

    /// <summary>
    /// The board's own thickness, along the outermost band of the shape.
    /// </summary>
    /// <remarks>
    /// **Why a lit side and a dark side.** A cover is a slab, and what says so
    /// is not its outline but the sliver of its cut edge you can see. Brightening
    /// the whole rim evenly reads as a glow round a flat sticker; lighting the
    /// top and left while dropping the bottom and right reads as something with a
    /// front and a back. The direction matches the light every other part of this
    /// screen is lit from, so the card sits in the same room as the rest of it.
    ///
    /// **Why so thin.** 1.6% of the short side is about ten pixels on a card and
    /// fifteen on the open book -- a board is a few millimetres of card, not a
    /// plank, and any more of it turns the cover into a box lid.
    /// </remarks>
    private Color Edge(Color colour, float u, float v)
    {
        float nu = Mathf.Min(u, 1f - u);
        float nv = Mathf.Min(v, 1f - v);
        float near = Mathf.Min(nu, nv);
        if (near >= Thickness) return colour;

        float k = 1f - near / Thickness;
        // 最近的是哪一邊，就由那一邊決定受光還是背光；上緣和左緣朝著光。
        bool lit = nu < nv ? u < 0.5f : v > 0.5f;
        // 受光那側只能亮一點點。原本乘 1.75，在染過色的紙封面上就變成一條偏
        // 白的窄帶 —— 寬度和位置都和書口一樣，於是書背那側看起來也有一條紙，
        // 書口那條反而不知道接到哪去了。板厚是暗示，不該是畫面上第二亮的東西。
        Color face = lit ? colour * 1.18f : colour * 0.62f;
        colour = Color.Lerp(colour, face, k * 0.9f);

        // 切口和封面之間壓一條暗線，厚度才有稜可看。
        float lip = Mathf.Abs(near - Thickness * 0.86f);
        if (lip < Thickness * 0.14f)
            colour = Color.Lerp(colour, colour * 0.72f, 1f - lip / (Thickness * 0.14f));
        return colour;
    }

    /// <summary>
    /// An even run of samples with the region boundaries forced in, twice each so
    /// a change of material is a hard line rather than a gradient.
    /// </summary>
    private static float[] Samples(int steps, float[] boundaries)
    {
        var points = new List<float>(steps + boundaries.Length * 2 + 2);
        for (int i = 0; i <= steps; i++) points.Add(i / (float)steps);
        for (int i = 0; i < boundaries.Length; i++)
        {
            points.Add(Mathf.Clamp01(boundaries[i] - 0.003f));
            points.Add(Mathf.Clamp01(boundaries[i] + 0.003f));
        }
        points.Sort();
        for (int i = points.Count - 1; i > 0; i--)
            if (points[i] - points[i - 1] < 0.0015f) points.RemoveAt(i);
        return points.ToArray();
    }
}
