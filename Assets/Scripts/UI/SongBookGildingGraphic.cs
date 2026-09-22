using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The gilt tooling on a song card: fillets round the board, ornament in the
/// corners, and as much more of it as the chosen difficulty earns.
/// </summary>
/// <remarks>
/// **Why the binding carries the difficulty.** The card is a bound volume, and
/// a library binds its books to match what is inside -- a study copy gets a
/// plain fillet, the presentation copy gets corner cartouches and a crest. So
/// the difficulty a player picked decides both the colour of the gold and how
/// much of it there is, and the shelf reads at a glance without a single word.
///
/// **Why it is drawn and not authored.** Five ranks at a card size decided at
/// runtime would be five sets of nine-slices, and a nine-slice cannot grow its
/// corner scrollwork with the rank. Every element here is a few strokes from
/// <see cref="OrnamentPen"/> -- the same brush the selection frame uses, so the
/// two read as one hand.
///
/// **Why each rank adds to the last rather than replacing it.** A binder does
/// not throw away the fillet to add a fleuron. Stacking means the five ranks
/// look like one series instead of five different books.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SongBookGildingGraphic : MaskableGraphic
{
    private const int StrokeSteps = 14;

    /// <summary>Ornament scale, as a fraction of the card's short side.</summary>
    private const float Unit = 0.030f;

    private Color tint = ClassicalBookUITheme.Gold;
    private int rank = -1;
    private bool blank = true;

    /// <summary>The difficulty colour the gold is mixed towards.</summary>
    public Color Tint
    {
        get => tint;
        set
        {
            if (tint == value) return;
            tint = value;
            SetVerticesDirty();
        }
    }

    /// <summary>0 Normal … 4 Special. Decides how much ornament is drawn.</summary>
    public int Rank
    {
        get => rank;
        set
        {
            value = Mathf.Clamp(value, 0, 4);
            if (rank == value) return;
            rank = value;
            SetVerticesDirty();
        }
    }

    /// <summary>Nothing to show (an empty carousel slot): draw no binding at all.</summary>
    public bool Blank
    {
        get => blank;
        set
        {
            if (blank == value) return;
            blank = value;
            SetVerticesDirty();
        }
    }

    private OrnamentPen Pen => new OrnamentPen
    {
        // 卡片比選單的裝飾框小很多，投影要跟著收，否則陰影會粗過線本身。
        shadowOffset = 1.4f,
        shadowStrength = 0.62f,
        relief = 0.5f,
    };

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (blank || rank < 0) return;

        Rect panel = rectTransform.rect;
        if (panel.width <= 60f || panel.height <= 60f) return;

        // 金線畫在封面上，不是畫在格子上：書芯的餘裕要先扣掉，兩條弧線才平行。
        Rect r = BookShape.Body(panel, BookShape.Profile.Volume);

        float unit = Mathf.Min(r.width, r.height) * Unit;
        BookShape.Profile profile = BookShape.Profile.Volume;
        // 內縮改用單位方形的比例來講：金線和板子的弧線是同一個場算出來的，
        // 兩者才會永遠平行。外圈剛好落在書口的紙塊內側。
        float mu = 0.088f;
        float mv = 0.076f;

        // 前兩階是印刷的：一張練習曲集的封面上，框是印上去的墨，不是燙上去
        // 的金。金子從 Expert 才出現 —— 這樣「越難越華麗」講的就不只是線變多，
        // 而是連材質都升了一級。
        Color printed = Color.Lerp(new Color(0.90f, 0.86f, 0.76f, 1f), tint, 0.48f);
        printed.a = 0.88f;
        Color gilt = rank >= 2 ? Color.Lerp(ClassicalBookUITheme.Gold, tint, 0.42f) : printed;
        gilt.a = rank >= 2 ? 0.92f : 0.88f;
        Color bright = Color.Lerp(gilt, new Color(1f, 0.96f, 0.84f, 1f), 0.42f);
        bright.a = 0.95f;
        // 盲壓的線：不上金，只有壓進皮面的暗痕。它是襯托，不是裝飾。
        Color blind = new Color(tint.r * 0.30f, tint.g * 0.26f, tint.b * 0.24f, 0.85f);

        // ── 邊線。每一階多一條，線腳就厚一層 ────────────────────────────
        // 外面那條磨得最兇：它就是書拿在手上會擦到的地方。
        float stepU = unit * 0.52f / r.width;
        float stepV = unit * 0.52f / r.height;
        Fillet(vh, r, mu, mv, unit * 0.155f, gilt, 11, 0.62f);
        if (rank >= 1)
            Fillet(vh, r, mu + stepU, mv + stepV, unit * 0.075f, bright, 27, 0.42f);
        if (rank >= 2)
            Fillet(vh, r, mu - stepU * 0.8f, mv - stepV * 0.8f, unit * 0.055f, blind, 43, 0.75f);
        if (rank >= 4)
            Fillet(vh, r, mu + stepU * 1.85f, mv + stepV * 1.85f, unit * 0.055f, blind, 59, 0.5f);

        // 八個半邊：每個角由兩個半邊共用，角上的飾件只由水平那一個畫。角的位置
        // 也要走書的弧線，否則卷飾會浮在板子外面。
        Vector2 topLeft = BookShape.Map(r, mu, 1f - mv, profile);
        Vector2 topRight = BookShape.Map(r, 1f - mu, 1f - mv, profile);
        Vector2 bottomLeft = BookShape.Map(r, mu, mv, profile);
        Vector2 bottomRight = BookShape.Map(r, 1f - mu, mv, profile);

        OrnamentEdge[] edges =
        {
            new OrnamentEdge(topLeft, Vector2.right, Vector2.down),
            new OrnamentEdge(topRight, Vector2.left, Vector2.down),
            new OrnamentEdge(bottomLeft, Vector2.right, Vector2.up),
            new OrnamentEdge(bottomRight, Vector2.left, Vector2.up),
            new OrnamentEdge(topLeft, Vector2.down, Vector2.right),
            new OrnamentEdge(bottomLeft, Vector2.up, Vector2.right),
            new OrnamentEdge(topRight, Vector2.down, Vector2.left),
            new OrnamentEdge(bottomRight, Vector2.up, Vector2.left),
        };

        float halfWidth = (topRight.x - topLeft.x) * 0.5f;
        float halfHeight = (topLeft.y - bottomLeft.y) * 0.5f;

        for (int i = 0; i < edges.Length; i++)
        {
            bool horizontal = i < 4;
            if (horizontal) Corner(vh, edges[i], unit, gilt, bright, i);

            // 邊上的母題：Expert 起頭尾兩邊有，Real 起連左右也有。
            int wanted = rank >= 4 ? 3 : rank >= 3 ? 2 : rank >= 2 ? 1 : 0;
            if (wanted > 0 && (horizontal || rank >= 3))
                Band(vh, edges[i], unit, horizontal ? halfWidth : halfHeight, wanted, gilt, i);
        }

        // 天頭正中的小冠。只有最上面兩階才配得上，也只有它們需要再多一個記號。
        if (rank >= 3)
            Crest(vh, BookShape.Map(r, 0.5f, 1f - mv, profile) + Vector2.down * (unit * 0.15f),
                unit, bright, rank);
    }

    /// <summary>One rule all the way round, at a fixed inset from the board edge.</summary>
    /// <remarks>
    /// Worn rather than straight, and bent rather than square. A tooled fillet is
    /// pressed into leather by hand and then rubbed by a century of shelving --
    /// it wanders by a hair, it is missing in places, and it follows the swell of
    /// the board it is pressed into. Without all three the card reads as a
    /// rectangle somebody drew rather than a book somebody bound.
    /// </remarks>
    private void Fillet(VertexHelper vh, Rect r, float insetU, float insetV, float width,
        Color colour, int seed, float wear)
    {
        Pen.WornBookFrame(vh, r, BookShape.Profile.Volume, insetU, insetV, width, colour, seed, wear);
    }

    /// <summary>
    /// The ornament sitting in one corner, growing with the rank.
    /// </summary>
    private void Corner(VertexHelper vh, OrnamentEdge edge, float unit, Color gilt, Color bright,
        int seed)
    {
        // 每一階都留著前一階的東西，只往上加。
        Stud(vh, edge, unit, bright, seed);

        if (rank >= 1)
        {
            // 角括號：兩支短線沿著兩邊往內收，把角圍起來。
            Volute(vh, edge, 0.95f, 0.34f, 0f, 0.4f, 1.15f, 0.085f, unit, gilt, seed * 7 + 1);
            Volute(vh, edge, 0.34f, 0.95f, 90f, -0.4f, 1.15f, 0.085f, unit, gilt, seed * 7 + 2);
        }

        if (rank >= 2)
        {
            // 一朵往對角線出去的卷。角落有了方向，不再只是兩條線交會。
            Volute(vh, edge, 0.42f, 0.42f, 45f, 3.4f, 1.9f, 0.10f, unit, gilt, seed * 7 + 3);
        }

        if (rank >= 3)
        {
            // 卡圖什：反向的第二卷加兩支帶向邊的小卷，角上就成了一組。
            Volute(vh, edge, 0.42f, 0.42f, 128f, -3.2f, 1.35f, 0.085f, unit, gilt, seed * 7 + 4);
            Volute(vh, edge, 1.60f, 0.40f, 10f, 4.6f, 0.85f, 0.062f, unit, bright, seed * 7 + 5);
            Volute(vh, edge, 0.40f, 1.60f, 80f, -4.6f, 0.85f, 0.062f, unit, bright, seed * 7 + 6);
        }

        if (rank >= 4)
        {
            // 最後一階：一道斜掃過角的長卷，和一片內葉。
            Volute(vh, edge, 1.05f, 1.05f, 45f, 2.2f, 2.4f, 0.07f, unit, bright, seed * 7 + 7);
            Volute(vh, edge, 0.86f, 0.86f, 225f, 2.2f, 1.1f, 0.055f, unit, gilt, seed * 7 + 8);
        }
    }

    /// <summary>A small four-petal rosette pinning the corner of the fillet.</summary>
    private void Stud(VertexHelper vh, OrnamentEdge edge, float unit, Color colour, int seed)
    {
        for (int i = 0; i < 4; i++)
            Volute(vh, edge, 0.40f, 0.40f, 45f + i * 90f, 7.5f, 0.34f, 0.075f, unit, colour,
                seed * 5 + i);
    }

    /// <summary>The motif repeated along one half-edge, between corner and centre.</summary>
    private void Band(VertexHelper vh, OrnamentEdge edge, float unit, float half, int count,
        Color colour, int seed)
    {
        float start = unit * (rank >= 3 ? 2.6f : 2.0f);
        float finish = half - unit * 0.6f;
        if (finish <= start) return;

        float pitch = (finish - start) / count;
        if (pitch < unit * 0.8f) return;

        // 母題的尺寸不跟著間距長：一邊只排一到三組，照間距算出來的卷會有半個
        // 卡片那麼長。固定大小、拉開距離，讀起來才是「間隔排列的壓花」。
        float reach = Mathf.Min(pitch / unit * 0.42f, 1.2f);
        for (int i = 0; i < count; i++)
        {
            float u = start + pitch * (i + 0.5f);
            // 一組是兩個反向的卷接成的 S。同向排一整排會讀成齒輪。
            Volute(vh, edge, u - pitch * 0.24f, 0.40f, 16f, 4.4f, reach, 0.055f, unit, colour,
                seed * 29 + i * 3 + 1);
            Volute(vh, edge, u + pitch * 0.24f, 0.40f, 164f, -4.4f, reach, 0.055f, unit, colour,
                seed * 29 + i * 3 + 2);
            if (rank >= 4)
                Volute(vh, edge, u, 0.34f, 90f, 3.0f, reach * 0.5f, 0.045f, unit, colour,
                    seed * 29 + i * 3 + 3);
        }
    }

    /// <summary>A palmette on the head edge: three fronds opening from one root.</summary>
    private void Crest(VertexHelper vh, Vector2 root, float unit, Color colour, int rankValue)
    {
        OrnamentPen pen = Pen;
        int fronds = rankValue >= 4 ? 5 : 3;
        for (int i = 0; i < fronds; i++)
        {
            float spread = (i - (fronds - 1) * 0.5f) / Mathf.Max(1f, fronds - 1);
            float angle = 270f + spread * 96f;
            float length = unit * (1.5f - Mathf.Abs(spread) * 0.55f);
            pen.Paint(vh, root, angle, length, unit * 0.07f, -spread * 5.5f, 0.12f, 0.8f,
                colour, StrokeSteps);
        }
        // 根部的一顆珠，把三片葉子的起點收在一起。
        for (int i = 0; i < 4; i++)
            pen.Paint(vh, root, 45f + i * 90f, unit * 0.30f, unit * 0.07f, 7.5f, 0.1f, 0.8f,
                colour, 8);
    }

    /// <summary>One scroll placed in edge coordinates, in multiples of the unit.</summary>
    private void Volute(VertexHelper vh, OrnamentEdge edge, float u, float v, float degrees,
        float curl, float length, float width, float unit, Color colour, int seed)
    {
        Vector2 from = edge.At(u * unit, v * unit);
        float heading = edge.Heading(degrees);
        Pen.Paint(vh, from, heading, length * unit, width * unit, curl * edge.handed, 0.09f, 0.85f,
            Vary(colour, seed), StrokeSteps);
    }

    /// <summary>
    /// Nudges one stroke's gold towards the difficulty colour or away from it.
    /// </summary>
    /// <remarks>
    /// Gilding tarnishes unevenly. One flat gold over a whole card reads as a
    /// decal; a small per-stroke drift makes it metal. Small on purpose -- give
    /// every stroke its own hue and ornament turns into confetti.
    /// </remarks>
    private Color Vary(Color colour, int seed)
    {
        Color drifted = Color.Lerp(colour, tint, OrnamentPen.Wobble(seed, 23) * 0.42f);
        drifted = Color.Lerp(drifted, new Color(1f, 0.94f, 0.80f, 1f),
            OrnamentPen.Wobble(seed, 91) * 0.22f);
        drifted.a = colour.a;
        return drifted;
    }
}
