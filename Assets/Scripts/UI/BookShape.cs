using UnityEngine;

/// <summary>
/// The outline of a bound volume, as a mapping from the unit square onto a rect.
/// </summary>
/// <remarks>
/// **Why a mapping and not a path.** Everything on the card has to follow the
/// same curve -- the leather, the page block, the gilt fillets, the wear on the
/// edges. If each of those carried its own idea of the outline they would drift
/// apart by a pixel here and there, and a gilt rule that crosses its own board
/// edge is worse than no curve at all. One function answers "where is (u, v) on
/// this book", and a rule at u = 0.03 is then automatically parallel to the edge
/// at u = 0, whatever the edge is doing.
///
/// **Why one edge stays straight.** A book is nailed down along its spine. That
/// edge is sewn, glued and pressed flat, and it is the one edge that cannot
/// swell, warp or knock its corners round -- everything a board does, it does
/// more the further it gets from the hinge. Curving all four alike is the
/// difference between a bound volume and a cushion: <see cref="Profile.anchor"/>
/// is how much of that rule applies, and <see cref="Profile.mirror"/> says which
/// side the hinge is on, because the left page of an open book is bound on its
/// right.
///
/// **Why the edges are pulled in, never pushed out.** The shape always stays
/// inside the rect it was given, so a carousel card cannot grow into its
/// neighbour and a page cannot grow off its board.
///
/// **Why it is a struct of fractions.** The card is 620x700 and a page in the
/// score book is nearer 830 square; a curvature in pixels would read as a strong
/// barrel on one and a flat edge on the other.
/// </remarks>
public static class BookShape
{
    /// <summary>How much curve a particular object has, and where it is held.</summary>
    public readonly struct Profile
    {
        /// <summary>Fore-edge fall-off at the corners, as a fraction of width.</summary>
        public readonly float fore;
        /// <summary>The same on the spine side -- the rounded back.</summary>
        public readonly float spine;
        /// <summary>
        /// How far the free end tips up, as a fraction of height.
        /// </summary>
        /// <remarks>
        /// **Why the lift and not a bow.** Bowing head and tail towards each
        /// other pinches the far end, and a rectangle that narrows at one end is
        /// a trapezoid -- the shape stops reading as square and starts reading as
        /// perspective, which is worse than being straight. A cover does not
        /// narrow: it *tips*. The whole free end rises together, so head and tail
        /// stay parallel and the same distance apart everywhere; only the last
        /// third of the card is doing anything at all.
        /// </remarks>
        public readonly float curl;
        /// <summary>Corner rounding, as a fraction of the short side.</summary>
        public readonly float corner;
        /// <summary>
        /// A slow wobble along each edge, as a fraction of the short side.
        /// </summary>
        /// <remarks>
        /// A parabola is still a formula. Real card and paper covers are pressed,
        /// trimmed and then stored leaning against other books, so their edges
        /// are not a clean arc either -- they wander in and out by a millimetre
        /// over the length of the edge. Two sine terms of unrelated frequency are
        /// enough; the point is only that no part of the edge is predictable
        /// from another part.
        /// </remarks>
        public readonly float wave;
        /// <summary>
        /// How far the text block shows out from under the cover, as a fraction
        /// of the short side. 0 for a single sheet, which has nothing under it.
        /// </summary>
        public readonly float block;
        /// <summary>
        /// 0 = a loose sheet, curling equally at both ends. 1 = bound along one
        /// edge, which then stays dead straight while the far edge does all the
        /// moving.
        /// </summary>
        public readonly float anchor;
        /// <summary>True when the binding is on the right (an open book's verso).</summary>
        public readonly bool mirror;

        public Profile(float fore, float spine, float curl, float corner, float anchor,
            float wave = 0f, float block = 0f, bool mirror = false)
        {
            this.fore = fore;
            this.spine = spine;
            this.curl = curl;
            this.corner = corner;
            this.anchor = anchor;
            this.wave = wave;
            this.block = block;
            this.mirror = mirror;
        }

        public Profile Mirrored()
        {
            return new Profile(fore, spine, curl, corner, anchor, wave, block, !mirror);
        }

        // 這是一個方的東西。所有數字加起來只夠讓它「不完全工整」，不夠讓它
        // 看起來是弧形的 —— 620 寬的卡上，書口收進來兩三個像素，尾端翹起來
        // 十來個像素，角只磨掉十個像素。
        /// <summary>A closed volume seen face on, hinged hard along its spine.</summary>
        public static Profile Volume =>
            new Profile(0.005f, 0.001f, 0.018f, 0.016f, 1f, 0.0040f, 0.045f);

        /// <summary>
        /// A leaf in an open book. Pressed flat by the book on top of it: the
        /// corners are cut round and that is all. Curl belongs to the cover.
        /// </summary>
        public static Profile Page => new Profile(0.001f, 0.000f, 0.002f, 0.007f, 0.9f, 0.0008f);

        /// <summary>A slip of paper laid on a page -- held by nothing, lifts at both ends.</summary>
        public static Profile Slip => new Profile(0.004f, 0.004f, 0.014f, 0.014f, 0f, 0.0035f);
    }

    /// <summary>
    /// The rect the cover occupies, once the text block under it has its room.
    /// </summary>
    /// <remarks>
    /// Everything that belongs to the cover -- the boards, the gilding, the
    /// wear, the ruled frame -- has to be laid out inside *this*, not inside the
    /// panel. Move one of them and it stops being parallel to the others, and
    /// the whole point of sharing one outline is lost.
    ///
    /// The leaves show at the bottom right because the light comes from the
    /// upper left: that is the side of a book you can see into.
    /// </remarks>
    /// <summary>
    /// How much of the fore-edge's depth also shows along the tail.
    /// </summary>
    /// <remarks>
    /// Not all of it. A book stands on its tail, so the cover sits down against
    /// the block there and only a sliver shows; the fore-edge is the side you can
    /// actually see into. Equal bands on both read as a slab of something the
    /// book is resting on, not as the book's own leaves.
    /// </remarks>
    public const float TailShare = 0.55f;

    /// <summary>
    /// The rect the cover occupies: the whole panel.
    /// </summary>
    /// <remarks>
    /// The block grows *outwards* rather than the cover shrinking inwards. Both
    /// give the same silhouette, but shrinking the cover moves its centre, and
    /// everything the card already carries -- the plate, the title, the author --
    /// is centred on the panel by somebody else's layout. They would all sit a
    /// couple of percent off the cover they are printed on, which is the kind of
    /// error that has no obvious cause when you look at it.
    ///
    /// Nothing clips it: the difficulty plaque already hangs below the card, and
    /// the carousel leaves 180px between cards for 28px of fore-edge.
    /// </remarks>
    public static Rect Body(Rect r, Profile p)
    {
        return r;
    }

    /// <summary>
    /// The text block under the cover: attached along the spine, and standing
    /// proud of the cover at the fore-edge and the tail.
    /// </summary>
    /// <remarks>
    /// **Why it is not just the cover shifted.** Shifting the whole block down
    /// and to the right pulls it off the spine, and then the strip of leaves
    /// along the tail stops short of the hinge and hangs in mid-air. The block is
    /// sewn to the spine: that edge is exactly where the cover's is, and every
    /// bit of daylight between the two opens up at the far end instead.
    ///
    /// So the block shares the panel's left edge, runs the full width, and is
    /// shorter than the panel by the tail's share -- which is what leaves the
    /// cover overhanging at the head, where a closed book shows nothing.
    /// </remarks>
    public static Rect BlockOf(Rect r, Profile p)
    {
        if (p.block <= 0f) return r;
        float depth = Mathf.Min(r.width, r.height) * p.block;
        float tail = depth * TailShare;
        // 書背那一邊和封面同一條線；書口和地腳往外長出去；天頭被封面壓著，
        // 所以整塊往下對齊，上面不露。
        return new Rect(r.xMin, r.yMin - tail, r.width + depth, r.height);
    }

    /// <summary>
    /// How free to move this point is: 0 at the binding, 1 at the far edge.
    /// </summary>
    /// <remarks>
    /// Anything that happens *because* the board is not held down -- the warp,
    /// the gap that opens against the text block -- has to be weighted by this,
    /// or it appears at the hinge too, where it is physically impossible.
    /// </remarks>
    public static float Free(float u, Profile p)
    {
        float along = p.mirror ? 1f - u : u;
        // 沒有裝訂的東西兩端一樣自由；裝訂過的，離書背越遠越自由。
        return Mathf.Lerp(Mathf.Abs(u * 2f - 1f), along, Mathf.Clamp01(p.anchor));
    }

    /// <summary>
    /// Where (u, v) of the unit square lands on the book filling this rect.
    /// </summary>
    /// <remarks>
    /// Interior points are the bilinear blend of the four edge curves, so a line
    /// of constant v inside the book bows exactly as much as the head does, and a
    /// little less the further in it sits. That is what a curved gilt rule needs,
    /// and it costs four evaluations.
    /// </remarks>
    public static Vector2 Map(Rect r, float u, float v, Profile p)
    {
        float s = u * 2f - 1f;
        float t = v * 2f - 1f;
        float shortSide = Mathf.Min(r.width, r.height);
        float round = shortSide * p.corner;

        // 這一點有多自由。書背那一頭是 0，所以那裡不翹、不收圓、也不鼓。
        float free = Free(u, p);
        // 只有最後三分之一在動。前面該是直的就是直的 —— 從書背就開始爬的話，
        // 整張卡會變成一個斜的平行四邊形。
        float tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, free));

        // 角落的收圓：書口那兩個角磨得兇，書背那兩個角是切方的 —— 裝訂邊是
        // 壓在夾具裡切出來的，而且書芯和封面必須在那一邊完全重合，差一點圓角
        // 就會讓地腳那條紙在書背那頭停下來、貼不上去。
        float held = Mathf.Lerp(1f, free, Mathf.Clamp01(p.anchor));
        float cornerX = round * Fillet(Mathf.Abs(t), round / Mathf.Max(1f, r.height * 0.5f)) * held;
        float cornerY = round * Fillet(Mathf.Abs(s), round / Mathf.Max(1f, r.width * 0.5f)) * held;

        // 書口鼓、書背是直的。裝訂邊上任何一點點鼓或起伏，都會讓疊在一起的
        // 兩個形狀（封面和書芯）在那一邊分開。
        // 裝訂在哪一邊，那一邊就完全不鼓、不晃。
        float loose = 1f - Mathf.Clamp01(p.anchor);
        float rightCoef = p.mirror ? p.spine * loose : p.fore;
        float leftCoef = p.mirror ? p.fore : p.spine * loose;
        // 起伏一律往內，形狀才不會長出配給的矩形。書背那一邊被壓死，不起伏。
        float swell = shortSide * p.wave;
        float held01 = Mathf.Lerp(1f, free, Mathf.Clamp01(p.anchor));
        float waveRight = swell * Wobble(v, 1.7f) * (p.mirror ? loose : held01);
        float waveLeft = swell * Wobble(v, 9.4f) * (p.mirror ? held01 : loose);
        float waveTop = swell * Wobble(u, 4.1f) * held01;
        float waveBottom = swell * Wobble(u, 6.8f) * held01;

        float right = r.xMax - rightCoef * r.width * t * t - cornerX - waveRight;
        float left = r.xMin + leftCoef * r.width * t * t + cornerX + waveLeft;

        // 尾端上翹：天頭地腳一起往上抬同樣的量，所以兩條邊永遠平行、間距不變
        // ——動的是整個尾巴，不是把它掐尖。抬升的餘裕先從上面留出來，形狀才不
        // 會長出配給的矩形。
        float lift = p.curl * r.height;
        float top = r.yMax - lift + lift * tip - cornerY - waveTop;
        float bottom = r.yMin + lift * tip + cornerY + waveBottom;

        return new Vector2(Mathf.Lerp(left, right, u), Mathf.Lerp(bottom, top, v));
    }

    /// <summary>Two sines of unrelated frequency, mapped to 0..1.</summary>
    private static float Wobble(float t, float phase)
    {
        float a = Mathf.Sin(t * 5.3f + phase);
        float b = Mathf.Sin(t * 12.1f + phase * 1.9f);
        return (a * 0.62f + b * 0.38f) * 0.5f + 0.5f;
    }

    /// <summary>
    /// A quarter-circle profile: 0 until the last <paramref name="span"/> of the
    /// axis, then rising to 1 at the very end.
    /// </summary>
    private static float Fillet(float distance, float span)
    {
        span = Mathf.Clamp(span, 0.001f, 1f);
        if (distance <= 1f - span) return 0f;
        float k = (distance - (1f - span)) / span;
        return 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
    }
}
