using UnityEngine;
using UnityEngine.UI;

namespace Effects
{
    /// <summary>
    /// The auditorium seen from the stage: four sweeping balcony tiers with
    /// their seating and downlights, boxes receding down each side wall, a
    /// coffered dome ringed twice with lamps, and a chandelier.
    /// </summary>
    /// <remarks>
    /// **Why it is built from the sides in.** During play the track and the
    /// keyboard own the middle of the frame from the bottom edge up to the
    /// horizon, so anything drawn there is spent effort. What stays visible is
    /// the band above the horizon and the two vertical strips beside the track,
    /// which is exactly where a hall photographed from the stage puts its
    /// balconies and its ceiling. The stalls are deliberately not drawn.
    ///
    /// **Why the tiers bend the way they do.** Every rail converges on the same
    /// vanishing point, so a rail below the camera appears to rise towards the
    /// centre and one above it appears to fall. Drawing them parallel is the
    /// single thing that makes a painted hall look like wallpaper, so the lower
    /// tiers rise inwards and the upper ones drop -- and their thickness shrinks
    /// towards the centre for the same reason.
    ///
    /// **Why every edge is feathered.** A photograph of a hall is not made of
    /// shapes, it is made of a great many small repeated things -- seats, lamps,
    /// ornament panels -- none of which is individually legible. That many
    /// hard-edged rectangles read as a diagram; the same count with their edges
    /// fading out reads as texture. The cover photograph behind this is blurred
    /// for the same reason, and the two have to agree, or the room splits into a
    /// soft layer with a crisp one sitting on top.
    ///
    /// **Why it never animates.** It sits next to the judgment line, which is
    /// the most crowded band on screen. It rebuilds only when the harmony tint
    /// moves, and the tint only reaches the gold.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ConcertHallBackdrop : MaskableGraphic
    {
        private const string ObjectName = "~ConcertHallBackdrop";
        private const int TierColumns = 56;
        private const int DomeSegments = 44;

        [Tooltip("整體亮度。這是一個暗場，調高很快就會蓋過譜面。")]
        [SerializeField, Range(0f, 1f)] private float brightness = 0.5f;
        [Tooltip("牆與座椅的深紅。")]
        [SerializeField] private Color crimson = new Color(0.34f, 0.05f, 0.07f, 1f);
        [Tooltip("欄杆、壁柱與吊燈的金。")]
        [SerializeField] private Color gilt = new Color(0.86f, 0.70f, 0.40f, 1f);
        [Tooltip("調性色混進金色的比例。0 = 永遠是金色。")]
        [SerializeField, Range(0f, 1f)] private float tintBlend = 0.16f;
        [Tooltip("每個元件邊緣的柔化寬度（像素）。細節靠這個變成質地而不是圖表。")]
        [SerializeField, Range(0.5f, 6f)] private float feather = 2.2f;

        private Color tint = Color.white;

        /// <summary>
        /// Installs the hall directly on top of the cover backdrop.
        /// </summary>
        /// <remarks>
        /// The sibling right after <c>backgroundImage</c> is the slot the video
        /// overlay already uses, and it is the one place proven to sit behind
        /// the track: the cover is visibly behind it, and this draws with the
        /// cover, not with the HUD.
        /// </remarks>
        public static ConcertHallBackdrop Attach(RawImage backdrop)
        {
            if (backdrop == null) return null;
            Transform parent = backdrop.transform.parent != null
                ? backdrop.transform.parent
                : backdrop.transform;

            Transform existing = parent.Find(ObjectName);
            if (existing != null) return existing.GetComponent<ConcertHallBackdrop>();

            // CanvasRenderer 要自己列出來。用 new GameObject(types...) 建的時候
            // [RequireComponent] 不會被套用，少了它 Graphic 會在第一次重建時炸掉。
            var hallObject = new GameObject(ObjectName, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(ConcertHallBackdrop));
            hallObject.layer = backdrop.gameObject.layer;

            RectTransform rect = hallObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(backdrop.transform.GetSiblingIndex() + 1);

            ConcertHallBackdrop hall = hallObject.GetComponent<ConcertHallBackdrop>();
            hall.raycastTarget = false;
            return hall;
        }

        /// <summary>Harmony colour. Only the gilding takes it, and only a little.</summary>
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

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width <= 1f || r.height <= 1f) return;

            Color wall = crimson * brightness;
            Color metal = Color.Lerp(gilt, tint, tintBlend) * brightness;

            DrawWalls(vh, r, wall);
            DrawSideBoxes(vh, r, wall, metal);

            // 四層挑臺。下面兩層在鏡頭以下，往中間升；上面兩層在鏡頭以上，往中間降。
            DrawTier(vh, r, 0.18f, 0.30f, 0.030f, 0.013f, wall, metal, 1.00f);
            DrawTier(vh, r, 0.38f, 0.41f, 0.026f, 0.012f, wall, metal, 0.88f);
            DrawTier(vh, r, 0.56f, 0.51f, 0.023f, 0.011f, wall, metal, 0.76f);
            DrawTier(vh, r, 0.74f, 0.61f, 0.020f, 0.010f, wall, metal, 0.64f);

            DrawUpperBoxes(vh, r, metal);
            DrawDome(vh, r, metal, wall);
            DrawChandelier(vh, r, metal);
        }

        /// <summary>The room itself: black at the floor, dark crimson upwards.</summary>
        private void DrawWalls(VertexHelper vh, Rect r, Color wall)
        {
            // 牆不是一張不透明的紙。封面在後面而且是模糊的，讓它透出來房間才有層次。
            Color floor = new Color(wall.r * 0.12f, wall.g * 0.12f, wall.b * 0.15f, 0.70f);
            Color mid = new Color(wall.r, wall.g, wall.b, 0.52f);
            Color high = new Color(wall.r * 0.72f, wall.g * 0.62f, wall.b * 0.62f, 0.58f);

            AddBand(vh, r, 0f, 0.34f, floor, mid);
            AddBand(vh, r, 0.34f, 1f, mid, high);
        }

        /// <summary>
        /// The boxes down each side wall: pilasters whose spacing tightens
        /// towards the centre, with a panel between each pair.
        /// </summary>
        /// <remarks>
        /// The tightening spacing is the whole of the perspective cue -- evenly
        /// spaced uprights read as a fence, not as a wall going away.
        /// </remarks>
        private void DrawSideBoxes(VertexHelper vh, Rect r, Color wall, Color metal)
        {
            float[] stops = { 0.500f, 0.446f, 0.398f, 0.356f, 0.319f, 0.287f, 0.259f };
            for (int i = 0; i < stops.Length; i++)
            {
                float depth = 1f - i / (float)stops.Length;      // 越靠中間越遠、越暗
                float width = r.width * (0.010f * depth + 0.002f);
                float low = TierHeight(stops[i], 0.18f, 0.30f);
                float high = TierHeight(stops[i], 0.74f, 0.61f);

                Color pillar = new Color(metal.r, metal.g, metal.b, 0.16f * depth * depth);
                Color panel = new Color(wall.r * 1.15f, wall.g * 0.55f, wall.b * 0.65f, 0.30f * depth);
                for (float side = -1f; side <= 1f; side += 2f)
                {
                    float x = side * stops[i] * r.width;
                    AddSoftColumn(vh, x - width, r.yMin + r.height * low,
                        x + width, r.yMin + r.height * high, pillar);

                    // 壁柱之間的深紅嵌板。
                    if (i + 1 < stops.Length)
                    {
                        float next = side * stops[i + 1] * r.width;
                        AddSoftColumn(vh, Mathf.Min(x, next) + width, r.yMin + r.height * (low + 0.02f),
                            Mathf.Max(x, next) - width, r.yMin + r.height * (high - 0.02f), panel);
                    }
                }
            }
        }

        /// <summary>
        /// One balcony: its under-shadow, the lit rail, the gilt panels along it,
        /// the downlights beneath and a row of seats above.
        /// </summary>
        private void DrawTier(VertexHelper vh, Rect r, float edgeHeight, float centreHeight,
            float edgeThickness, float centreThickness, Color wall, Color metal, float lit)
        {
            Color rail = new Color(metal.r, metal.g, metal.b, 0.34f * lit);
            Color lip = new Color(metal.r, metal.g, metal.b, 0.52f * lit);
            Color under = new Color(wall.r * 0.10f, wall.g * 0.10f, wall.b * 0.12f, 0.66f);
            Color clearUnder = new Color(under.r, under.g, under.b, 0f);
            Color seats = new Color(wall.r * 1.35f, wall.g * 0.85f, wall.b * 0.85f, 0.40f);
            Color clearSeats = new Color(seats.r, seats.g, seats.b, 0f);
            Color panel = new Color(metal.r, metal.g, metal.b, 0.20f * lit);

            for (int i = 0; i < TierColumns; i++)
            {
                float t0 = i / (float)TierColumns * 2f - 1f;
                float t1 = (i + 1) / (float)TierColumns * 2f - 1f;
                float x0 = t0 * 0.5f * r.width;
                float x1 = t1 * 0.5f * r.width;

                float y0 = r.yMin + r.height * TierHeight(Mathf.Abs(t0) * 0.5f, edgeHeight, centreHeight);
                float y1 = r.yMin + r.height * TierHeight(Mathf.Abs(t1) * 0.5f, edgeHeight, centreHeight);
                float h0 = r.height * Thickness(Mathf.Abs(t0) * 0.5f, edgeThickness, centreThickness);
                float h1 = r.height * Thickness(Mathf.Abs(t1) * 0.5f, edgeThickness, centreThickness);

                // 欄杆下方的陰影，往下淡出。廳堂的暗是從挑臺底下來的。
                AddSlant(vh, x0, x1, y0 - h0 * 2.8f, y1 - h1 * 2.8f, y0, y1, clearUnder, under);
                // 欄杆本身，上緣受光。
                AddSlant(vh, x0, x1, y0, y1, y0 + h0, y1 + h1, rail, lip);
                // 欄杆上一排座席。每隔一格斷開，讀起來才是一排椅子而不是一條紅帶。
                if (i % 2 == 0)
                    AddSlant(vh, x0, x1 - (x1 - x0) * 0.35f, y0 + h0 * 1.15f, y1 + h1 * 1.15f,
                        y0 + h0 * 2.4f, y1 + h1 * 2.4f, seats, clearSeats);

                // 欄杆面上的鑲板，隔幾格一片。
                if (i % 3 == 1)
                    AddSlant(vh, x0 + (x1 - x0) * 0.2f, x1 - (x1 - x0) * 0.2f,
                        y0 + h0 * 0.25f, y1 + h1 * 0.25f, y0 + h0 * 0.75f, y1 + h1 * 0.75f,
                        panel, panel);

                // 挑臺下的嵌燈。參考照片裡這是最密的一排東西。
                if (i % 4 == 2)
                {
                    float cx = (x0 + x1) * 0.5f;
                    float cy = (y0 + y1) * 0.5f - h0 * 1.5f;
                    AddGlow(vh, new Vector2(cx, cy), r.height * 0.010f,
                        new Color(metal.r, metal.g, metal.b, 0.30f * lit));
                }
            }
        }

        /// <summary>
        /// The row of lamps above the top balcony, where the ceiling starts.
        /// </summary>
        /// <remarks>
        /// Drawn as glows only. The first attempt built each opening as a lit
        /// panel inside a frame, which at this size is a rectangle a few dozen
        /// pixels across with hard top and bottom edges -- and seventeen of them
        /// in a row read as squares pasted on the wall, not as lights. What the
        /// eye actually picks out of the reference here is the row of bright
        /// points, so that is all this draws.
        /// </remarks>
        private void DrawUpperBoxes(VertexHelper vh, Rect r, Color metal)
        {
            const int Count = 23;
            float y = r.yMin + r.height * 0.665f;
            for (int i = 0; i < Count; i++)
            {
                float t = (i + 0.5f) / Count * 2f - 1f;
                // 中間的比較遠，所以比較小、比較暗。
                float depth = 0.45f + 0.55f * Mathf.Abs(t);
                Vector2 at = new Vector2(t * 0.45f * r.width, y);
                float size = r.height * 0.012f * depth;
                AddGlow(vh, at, size * 2.6f, new Color(metal.r, metal.g, metal.b, 0.10f * depth));
                AddGlow(vh, at, size, new Color(metal.r, metal.g, metal.b, 0.30f * depth));
            }
        }

        /// <summary>The coffered dome: concentric rings, ringed twice with lamps.</summary>
        private void DrawDome(VertexHelper vh, Rect r, Color metal, Color wall)
        {
            Vector2 centre = new Vector2(0f, r.yMin + r.height * 0.99f);
            float rx = r.width * 0.46f;
            float ry = r.height * 0.34f;

            float[] rings = { 1f, 0.86f, 0.72f, 0.58f, 0.44f, 0.30f };
            for (int i = 0; i < rings.Length; i++)
            {
                float fade = 1f - i * 0.11f;
                AddEllipseRing(vh, centre, new Vector2(rx * rings[i], ry * rings[i]),
                    r.height * 0.005f, new Color(metal.r, metal.g, metal.b, 0.18f * fade));
            }

            // 兩圈燈。參考照片裡最先被認出來的就是這個。
            DrawLampRing(vh, centre, rx * 0.93f, ry * 0.93f, 24, r.height * 0.016f, 0.34f, metal);
            DrawLampRing(vh, centre, rx * 0.65f, ry * 0.65f, 16, r.height * 0.012f, 0.26f, metal);

            // 藻井：相鄰兩圈之間的短肋，只在下半圈，再往內就被吊燈蓋掉。
            Color rib = new Color(wall.r * 0.45f, wall.g * 0.4f, wall.b * 0.45f, 0.30f);
            for (int i = 0; i < 28; i++)
            {
                float a = Mathf.PI + (i + 0.5f) / 28f * Mathf.PI;
                Vector2 unit = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                AddSoftLine(vh, centre + unit * 0.72f, centre + unit * 0.86f, r.height * 0.003f, rib);
            }
        }

        private void DrawLampRing(VertexHelper vh, Vector2 centre, float rx, float ry,
            int count, float size, float strength, Color metal)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI + (i + 0.5f) / count * Mathf.PI;      // 只有下半圈看得到
                Vector2 at = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                AddGlow(vh, at, size * 2.1f, new Color(metal.r, metal.g, metal.b, strength * 0.35f));
                AddGlow(vh, at, size, new Color(metal.r, metal.g, metal.b, strength));
            }
        }

        /// <summary>The chandelier: a wide halo with a tapering cluster inside.</summary>
        private void DrawChandelier(VertexHelper vh, Rect r, Color metal)
        {
            Vector2 hub = new Vector2(0f, r.yMin + r.height * 0.90f);
            AddGlow(vh, hub, r.height * 0.15f, new Color(metal.r, metal.g, metal.b, 0.18f));

            for (int i = 0; i < 46; i++)
            {
                float row = i / 46f;
                float spread = (1f - row) * 0.9f + 0.1f;
                float a = i * 2.399f;                          // 黃金角，避免排成一列
                Vector2 at = hub + new Vector2(
                    Mathf.Cos(a) * r.width * 0.06f * spread,
                    -row * r.height * 0.085f + Mathf.Sin(a) * r.height * 0.009f);
                AddGlow(vh, at, r.height * (0.011f - row * 0.005f),
                    new Color(metal.r, metal.g, metal.b, 0.36f * (1f - row * 0.6f)));
            }
        }

        // ------------------------------------------------------------ shape --

        /// <summary>
        /// Height of a rail at |x| across the frame, as a fraction of the rect.
        /// Flat across the back wall, swooping only once it reaches the sides.
        /// </summary>
        private static float TierHeight(float distanceFromCentre, float edge, float centre)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distanceFromCentre - 0.09f) / 0.41f));
            return Mathf.Lerp(centre, edge, t);
        }

        private static float Thickness(float distanceFromCentre, float edge, float centre)
        {
            return Mathf.Lerp(centre, edge, Mathf.Pow(Mathf.Clamp01(distanceFromCentre * 2f), 1.4f));
        }

        // ---------------------------------------------------------- drawing --

        private static void AddBand(VertexHelper vh, Rect r, float from, float to, Color low, Color high)
        {
            float y0 = r.yMin + r.height * from;
            float y1 = r.yMin + r.height * to;
            AddQuad(vh, new Vector2(r.xMin, y0), new Vector2(r.xMax, y0),
                new Vector2(r.xMax, y1), new Vector2(r.xMin, y1), low, low, high, high);
        }

        /// <summary>
        /// An upright whose four edges all fade out.
        /// </summary>
        /// <remarks>
        /// It used to feather only the left and right sides, which is enough for
        /// something that runs off the top and bottom of its neighbours but not
        /// for anything that ends inside the picture: a hard top and bottom is
        /// exactly what makes a small quad read as a pasted-on square.
        /// </remarks>
        private void AddSoftColumn(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
        {
            if (colour.a <= 0.002f || x1 <= x0 || y1 <= y0) return;
            float ex = Mathf.Min(feather, (x1 - x0) * 0.49f);
            float ey = Mathf.Min(feather, (y1 - y0) * 0.49f);

            // 3x3 的網格：四角是雙向淡出，四邊單向，中間實心。
            float[] xs = { x0, x0 + ex, x1 - ex, x1 };
            float[] ys = { y0, y0 + ey, y1 - ey, y1 };
            for (int cx = 0; cx < 3; cx++)
            {
                for (int cy = 0; cy < 3; cy++)
                {
                    AddQuad(vh,
                        new Vector2(xs[cx], ys[cy]), new Vector2(xs[cx + 1], ys[cy]),
                        new Vector2(xs[cx + 1], ys[cy + 1]), new Vector2(xs[cx], ys[cy + 1]),
                        Fade(colour, cx, cy), Fade(colour, cx + 1, cy),
                        Fade(colour, cx + 1, cy + 1), Fade(colour, cx, cy + 1));
                }
            }
        }

        /// <summary>Full strength only on the two inner grid lines.</summary>
        private static Color Fade(Color colour, int x, int y)
        {
            bool solid = x > 0 && x < 3 && y > 0 && y < 3;
            return solid ? colour : new Color(colour.r, colour.g, colour.b, 0f);
        }

        /// <summary>A column of a sloping band: its two edges sit at different heights.</summary>
        private static void AddSlant(VertexHelper vh, float x0, float x1,
            float lowLeft, float lowRight, float highLeft, float highRight, Color low, Color high)
        {
            AddQuad(vh, new Vector2(x0, lowLeft), new Vector2(x1, lowRight),
                new Vector2(x1, highRight), new Vector2(x0, highLeft), low, low, high, high);
        }

        /// <summary>A line that fades out along both of its long edges.</summary>
        private static void AddSoftLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color colour)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < 1e-4f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width;
            Color clear = new Color(colour.r, colour.g, colour.b, 0f);

            AddQuad(vh, from - normal, from, to, to - normal, clear, colour, colour, clear);
            AddQuad(vh, from, from + normal, to + normal, to, colour, clear, clear, colour);
        }

        private static void AddGlow(VertexHelper vh, Vector2 centre, float radius, Color colour)
        {
            const int Segments = 10;
            Color rim = new Color(colour.r, colour.g, colour.b, 0f);
            int first = vh.currentVertCount;
            AddVertex(vh, centre, colour);
            for (int i = 0; i < Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                AddVertex(vh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, rim);
            }
            for (int i = 0; i < Segments; i++)
                vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % Segments);
        }

        /// <summary>A ring drawn as two fading edges, so it has no hard outline.</summary>
        private static void AddEllipseRing(VertexHelper vh, Vector2 centre, Vector2 radius,
            float thickness, Color colour)
        {
            Color clear = new Color(colour.r, colour.g, colour.b, 0f);
            int first = vh.currentVertCount;
            Vector2 inner = new Vector2(Mathf.Max(1f, radius.x - thickness),
                                        Mathf.Max(1f, radius.y - thickness));
            Vector2 outer = new Vector2(radius.x + thickness, radius.y + thickness);
            for (int i = 0; i < DomeSegments; i++)
            {
                float a = i / (float)DomeSegments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                AddVertex(vh, centre + new Vector2(c * inner.x, s * inner.y), clear);
                AddVertex(vh, centre + new Vector2(c * radius.x, s * radius.y), colour);
                AddVertex(vh, centre + new Vector2(c * outer.x, s * outer.y), clear);
            }
            for (int i = 0; i < DomeSegments; i++)
            {
                int a0 = first + i * 3;
                int a1 = first + ((i + 1) % DomeSegments) * 3;
                vh.AddTriangle(a0, a0 + 1, a1 + 1);
                vh.AddTriangle(a1 + 1, a1, a0);
                vh.AddTriangle(a0 + 1, a0 + 2, a1 + 2);
                vh.AddTriangle(a1 + 2, a1 + 1, a0 + 1);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
            Color c0, Color c1, Color c2, Color c3)
        {
            int index = vh.currentVertCount;
            AddVertex(vh, p0, c0);
            AddVertex(vh, p1, c1);
            AddVertex(vh, p2, c2);
            AddVertex(vh, p3, c3);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index + 2, index + 3, index);
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = colour;
            vh.AddVert(vertex);
        }
    }
}
