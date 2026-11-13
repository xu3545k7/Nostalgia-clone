using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ornamental border around the gameplay song card, themed on the chart's
/// difficulty colour and getting more elaborate the harder the chart is.
/// </summary>
/// <remarks>
/// **Why it is a sibling of the card, not a child.** The card carries a
/// <see cref="RectMask2D"/>, so anything parented to it is clipped at the paper
/// edge -- which is exactly where the ornament has to sit. It is placed next to
/// the card instead and given the same rect plus a margin.
///
/// **Why the tiers are drawn, not authored.** Four difficulty bands each want a
/// different amount of gilding on a card whose size is decided at runtime.
/// Sprites would need four sets of nine-slices and would still not let the
/// corner scrollwork grow with the tier, so every element is a few quads
/// generated from the same rect.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DifficultyFrameGraphic : MaskableGraphic
{
    private int level = 1;
    private Color? overrideInk;

    /// <summary>
    /// Forces the frame's colour instead of taking it from the difficulty.
    /// </summary>
    /// <remarks>
    /// The gameplay card wants the chart's own colour; a gilt frame around a
    /// panel wants gold whatever is inside it. One nullable override beats a
    /// second copy of the drawing code.
    /// </remarks>
    public Color? OverrideInk
    {
        get => overrideInk;
        set
        {
            overrideInk = value;
            SetVerticesDirty();
        }
    }

    /// <summary>Chart level. Decides both the colour and how much ornament.</summary>
    public int Level
    {
        get => level;
        set
        {
            if (level == value) return;
            level = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// Builds the frame next to <paramref name="card"/> and returns it.
    /// </summary>
    public static DifficultyFrameGraphic Attach(RectTransform card, float margin)
    {
        if (card == null || card.parent == null) return null;

        Transform existing = card.parent.Find("DifficultyFrame");
        if (existing != null) return existing.GetComponent<DifficultyFrameGraphic>();

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 第一次重建就會炸。
        var frameObject = new GameObject("DifficultyFrame", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(DifficultyFrameGraphic));
        frameObject.layer = card.gameObject.layer;

        RectTransform rect = frameObject.GetComponent<RectTransform>();
        rect.SetParent(card.parent, false);
        rect.anchorMin = card.anchorMin;
        rect.anchorMax = card.anchorMax;
        rect.pivot = card.pivot;
        rect.anchoredPosition = card.anchoredPosition + new Vector2(-margin, margin);
        rect.sizeDelta = card.sizeDelta + new Vector2(margin * 2f, margin * 2f);
        rect.SetSiblingIndex(card.GetSiblingIndex() + 1);

        DifficultyFrameGraphic frame = frameObject.GetComponent<DifficultyFrameGraphic>();
        frame.raycastTarget = false;
        frame.margin = margin;
        return frame;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 4f || r.height <= 4f) return;

        Color theme = overrideInk ?? DifficultyVisualPalette.ForLevel(level);
        Color ink = new Color(theme.r * 0.55f, theme.g * 0.55f, theme.b * 0.55f, 0.95f);
        Color gilt = Color.Lerp(theme, new Color(1f, 0.93f, 0.72f, 1f), 0.45f);
        gilt.a = 0.95f;

        int tier = Tier(level);

        // 每一階都疊在前一階上面，而不是各畫各的 —— 難度愈高就是「同一個框再加東西」，
        // 這樣四階看起來才是一套，不是四個不同的框。
        DrawRule(vh, r, 0f, 2.4f, ink);
        DrawCornerStuds(vh, r, 3.5f, gilt);

        if (tier >= 1)
        {
            DrawRule(vh, r, 6f, 1.2f, gilt);
            DrawCornerBrackets(vh, r, 22f, 2.2f, gilt);
        }

        if (tier >= 2)
        {
            // **彎的那兩樣拿掉了**（角落的螺旋 DrawCornerScrolls、邊上的葉飾
            // DrawEdgeFleurons）。
            //
            // 這張卡是遊玩中掛在左上角的讀數板 —— 曲名、難度、時間、進度條，全部
            // 是方正的東西，而曲繪本身也是一個矩形。一圈捲曲的藤蔓和它們沒有一條
            // 線對得上，所以它讀起來不是裝飾，是雜訊。留下來的都是直的、和版面的
            // 邊平行的元素（框線、角釘、角托、齒飾），四階才仍然是「同一個框再加
            // 東西」。
            //
            // 兩個 Draw 方法留著沒刪：它們是通用的幾何，別的地方可能還要用。
            DrawRule(vh, r, 9f, 1.0f, gilt);
        }

        if (tier >= 3)
        {
            DrawRule(vh, r, 13f, 0.9f, ink);
            DrawDentils(vh, r, 13f, 3.4f, gilt);
            DrawCornerFinials(vh, r, 9f, theme);
        }
    }

    /// <summary>Four bands, matching the palette's own thresholds.</summary>
    private static int Tier(int level)
    {
        if (level >= 13) return 3;
        if (level >= 10) return 2;
        if (level >= 7) return 1;
        return 0;
    }

    /// <summary>右下角被切掉的那一塊（和卡片用同一組數字）。</summary>
    /// <remarks>
    /// 外框是卡片的**兄弟**不是子物件（卡上有 RectMask2D，當子物件會被裁在紙邊上），
    /// 所以它不會自動知道卡片變成 L 了 —— 缺口必須由外面餵進來，否則框會橫過缺口，
    /// 在沒有紙的地方畫一圈金線。
    /// </remarks>
    private Vector2 notch;

    /// <summary>這個框比卡片大出多少（四邊都是）。缺口要換算時用得到。</summary>
    private float margin;

    /// <summary>
    /// 更新缺口。傳進來的是**卡片座標**的缺口，這裡換算成框自己的座標。
    /// </summary>
    /// <remarks>
    /// 框的 rect 比卡片四邊各大 <see cref="margin"/>，而缺口的那個轉角在世界上是
    /// 同一個點 —— 所以從框的右下角量過去，缺口要大 margin。不換算的話，框會在缺口
    /// 裡留下一條 margin 寬的 L 形細邊。
    /// </remarks>
    public void SetNotch(Vector2 cardNotch)
    {
        Vector2 value = cardNotch.sqrMagnitude <= 0.01f
            ? Vector2.zero
            : cardNotch + new Vector2(margin, margin);
        if (notch == value) return;
        notch = value;
        SetVerticesDirty();
    }

    // ------------------------------------------------------------- elements --

    /// <summary>
    /// 沿著輪廓走一圈的細線。斜切之後是五條邊，其中一條是斜的。
    /// </summary>
    private void DrawRule(VertexHelper vh, Rect r, float inset, float thickness, Color colour)
    {
        if (r.width - inset * 2f < thickness * 2f) return;
        if (r.height - inset * 2f < thickness * 2f) return;

        Vector2[] path = OutlinePath(r, inset);
        for (int i = 0; i < path.Length; i++)
            StrokeEdge(vh, path[i], path[(i + 1) % path.Length], thickness, colour);
    }

    /// <summary>
    /// 輪廓上的一段，往內長出厚度。
    /// </summary>
    /// <remarks>
    /// 以前是四到六次 <see cref="AddRect"/> 拼出來的，而方塊畫不出斜邊 —— 斜切之後
    /// 那一段會變成一個蓋住缺口的大方塊。改成沿著線段自己長：
    ///
    /// 內側法線取 <c>(dy, -dx)</c>，因為輪廓是順時針的（左上→右上→…），順時針多邊形
    /// 的內部在前進方向的**右**手邊。
    ///
    /// 兩端各往外延伸一個厚度，讓相鄰兩段在轉角重疊。不延伸的話每個外角會缺一個小
    /// 三角形；直角處剛好補滿，斜切那兩個鈍角會多蓋一點點，但線只有 1 單位寬，看不
    /// 出來 —— 比做真正的斜接便宜太多。
    /// </remarks>
    private static void StrokeEdge(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color colour)
    {
        Vector2 delta = b - a;
        float length = delta.magnitude;
        if (length <= 0.01f) return;
        Vector2 dir = delta / length;
        Vector2 inward = new Vector2(dir.y, -dir.x) * thickness;
        Vector2 p0 = a - dir * thickness;
        Vector2 p1 = b + dir * thickness;

        int first = vh.currentVertCount;
        AddVertex(vh, p0, colour);
        AddVertex(vh, p1, colour);
        AddVertex(vh, p1 + inward, colour);
        AddVertex(vh, p0 + inward, colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    /// <summary>A small square at each corner: the plainest tier still needs a stop.</summary>
    private void DrawCornerStuds(VertexHelper vh, Rect r, float size, Color colour)
    {
        foreach (Vector2 corner in Corners(r, 1.5f))
            AddRect(vh, corner.x - size * 0.5f, corner.y - size * 0.5f,
                corner.x + size * 0.5f, corner.y + size * 0.5f, colour);
    }

    /// <summary>
    /// 每個轉角一個 L 形角托，讀起來像斜接的榫。
    /// </summary>
    /// <remarks>
    /// **這裡原本自己認矩形**：直接拿 <c>r</c> 的四個角算，所以卡片的右下角
    /// 被斜切掉之後，它照樣在那個已經沒有紙的直角上畫了一個金色的 L，浮在
    /// 軌道上。框線、齒飾、角釣、角針都已經改成読 <see cref="OutlinePath"/>，
    /// 只剩這一項漏掉。
    ///
    /// 改法：沿輪廓走，每個頂點的兩隻手臂各自順著相鄰的那一條邊長出去。
    /// 矩形的角畫出來和舊版完全一樣（往內縮 5、一隻横一隻竖），而斜線
    /// 兩端的那兩個鈐角則有一隻手臂貼著斜線走 —— 角托本來要說的就是「這裡是
    /// 兩條邊接起來的地方」，它就該指在真的那兩條邊上。
    ///
    /// 手臂長度夾在那條邊的 0.4 以內：缺口切出來的邊比較短，照 <paramref name="arm"/>
    /// 畫會從下一個轉角伸出去；太短到放不下的那個角整個跳過。
    /// </remarks>
    private void DrawCornerBrackets(VertexHelper vh, Rect r, float arm, float thickness, Color colour)
    {
        Vector2[] path = OutlinePath(r, 6f);
        if (path.Length < 3) return;

        for (int i = 0; i < path.Length; i++)
        {
            Vector2 here = path[i];
            Vector2 toPrev = path[(i + path.Length - 1) % path.Length] - here;
            Vector2 toNext = path[(i + 1) % path.Length] - here;
            float prevLen = toPrev.magnitude;
            float nextLen = toNext.magnitude;
            if (prevLen <= 0.01f || nextLen <= 0.01f) continue;
            toPrev /= prevLen;
            toNext /= nextLen;

            float prevArm = Mathf.Min(arm, prevLen * 0.4f);
            float nextArm = Mathf.Min(arm, nextLen * 0.4f);
            if (prevArm < thickness * 2f || nextArm < thickness * 2f) continue;

            // 角本身往內縮 5（兩條邊的方向各 5），和舊版的 ix／iy 同一個點。
            Vector2 origin = here + (toPrev + toNext) * 5f;
            AddArm(vh, origin, toNext, nextArm, toPrev, thickness, colour);
            AddArm(vh, origin, toPrev, prevArm, toNext, thickness, colour);
        }
    }

    /// <summary>
    /// 角托的一隻手臂：從 <paramref name="origin"/> 沿 <paramref name="along"/> 長出去，
    /// 往 <paramref name="across"/> 加厚。
    /// </summary>
    /// <remarks>
    /// 不用 <see cref="AddRect"/>：那個只接軸向對齊的矩形，畫不出貼著斜線的手臂。
    /// </remarks>
    private static void AddArm(VertexHelper vh, Vector2 origin, Vector2 along, float length,
        Vector2 across, float thickness, Color colour)
    {
        Vector2 end = origin + along * length;
        Vector2 offset = across * thickness;
        int first = vh.currentVertCount;
        AddVertex(vh, origin, colour);
        AddVertex(vh, end, colour);
        AddVertex(vh, end + offset, colour);
        AddVertex(vh, origin + offset, colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    /// <summary>
    /// A quarter scroll curling inwards from each corner.
    /// </summary>
    /// <remarks>
    /// A plain arc reads as a rounded corner; the radius has to shrink along the
    /// sweep for it to read as scrollwork, which is why this is a spiral.
    /// </remarks>
    private void DrawCornerScrolls(VertexHelper vh, Rect r, float outer, float inner,
        float thickness, Color colour)
    {
        if (r.width < outer * 4f || r.height < outer * 4f) return;

        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? 1f : -1f;
            float sy = i < 2 ? 1f : -1f;
            Vector2 pivot = new Vector2(
                sx > 0f ? r.xMin + outer + 8f : r.xMax - outer - 8f,
                sy > 0f ? r.yMax - outer - 8f : r.yMin + outer + 8f);

            // 從指向角落的方向起卷，往內收 270 度。
            float start = Mathf.Atan2(-sy, -sx) * Mathf.Rad2Deg;
            AddSpiral(vh, pivot, outer, inner, start, sx * sy > 0f ? 265f : -265f,
                thickness, colour);
        }
    }

    /// <summary>A diamond with two beads at the middle of every edge.</summary>
    private void DrawEdgeFleurons(VertexHelper vh, Rect r, float size, Color colour)
    {
        Vector2 centre = r.center;
        Vector2[] spots =
        {
            new Vector2(centre.x, r.yMax - 6f),
            new Vector2(centre.x, r.yMin + 6f),
            new Vector2(r.xMin + 6f, centre.y),
            new Vector2(r.xMax - 6f, centre.y),
        };

        bool[] horizontal = { true, true, false, false };
        for (int i = 0; i < spots.Length; i++)
        {
            AddDiamond(vh, spots[i], size, colour);
            Vector2 step = horizontal[i] ? new Vector2(size * 1.9f, 0f) : new Vector2(0f, size * 1.9f);
            AddDiamond(vh, spots[i] - step, size * 0.42f, colour);
            AddDiamond(vh, spots[i] + step, size * 0.42f, colour);
        }
    }

    /// <summary>A repeating tooth course just inside the outer rule.</summary>
    /// <summary>
    /// 沿著輪廓排一圈小方塊（齒飾）。
    /// </summary>
    /// <remarks>
    /// 逐段排，每一段各自置中，所以每條邊兩端的留白是對稱的；從一端排起會在另一端
    /// 切半顆。太短的邊（放不下一顆）整段跳過 —— 缺口的那兩條短邊常常就是這種。
    /// </remarks>
    private void DrawDentils(VertexHelper vh, Rect r, float spacing, float size, Color colour)
    {
        Vector2[] path = OutlinePath(r, 13f);
        if (path.Length < 3) return;
        float half = size * 0.5f;

        for (int i = 0; i < path.Length; i++)
        {
            Vector2 a = path[i];
            Vector2 b = path[(i + 1) % path.Length];
            float len = Vector2.Distance(a, b);
            int count = Mathf.FloorToInt(len / spacing);
            if (count < 1) continue;
            Vector2 dir = (b - a) / Mathf.Max(len, 0.0001f);
            // 置中：整排佔 (count-1)*spacing，兩端各留一半的剩餘。
            float start = (len - (count - 1) * spacing) * 0.5f;
            for (int k = 0; k < count; k++)
            {
                Vector2 p = a + dir * (start + k * spacing);
                AddRect(vh, p.x - half, p.y - half, p.x + half, p.y + half, colour);
            }
        }
    }

    /// <summary>A jewel sitting on each corner of the outermost rule.</summary>
    private void DrawCornerFinials(VertexHelper vh, Rect r, float size, Color colour)
    {
        Color core = new Color(colour.r, colour.g, colour.b, 0.95f);
        Color halo = new Color(colour.r, colour.g, colour.b, 0.28f);
        foreach (Vector2 corner in Corners(r, 1.5f))
        {
            AddDiamond(vh, corner, size * 1.8f, halo);
            AddDiamond(vh, corner, size, core);
        }
    }

    // ------------------------------------------------------------ primitives --

    /// <summary>
    /// 輪廓的轉角 —— 就是輪廓的頂點本身。
    /// </summary>
    /// <remarks>
    /// 不處理的話，角釘和角托會畫在矩形的右下角 —— 那裡已經沒有紙了，看起來是一塊
    /// 浮在軌道上的金屬。斜切之後數量從 4 變 5：斜線的兩端各是一個外角。
    ///
    /// 直接轉呼叫 <see cref="OutlinePath"/>，不自己再算一次形狀：形狀寫在兩個地方，
    /// 就一定會有一個忘記跟著改。
    /// </remarks>
    private Vector2[] Corners(Rect r, float inset) => OutlinePath(r, inset);

    /// <summary>
    /// 輪廓的頂點，順時針。沒有缺口是四個，斜切之後是五個。
    /// </summary>
    /// <remarks>
    /// 沿著它走，任何一種「繞一圈」的裝飾（點線、細線）都不必各自去判斷形狀 ——
    /// 第一版的點線就是自己認矩形，所以卡片切成 L 之後，它照樣在缺口上畫了一圈。
    /// </remarks>
    private Vector2[] OutlinePath(Rect r, float inset)
    {
        float x0 = r.xMin + inset, x1 = r.xMax - inset;
        float y0 = r.yMin + inset, y1 = r.yMax - inset;
        float cutW = Mathf.Max(0f, notch.x - inset);
        float cutH = Mathf.Max(0f, notch.y - inset);
        bool bevelled = cutW > 1f && cutH > 1f
                        && cutW < (x1 - x0) - 1f && cutH < (y1 - y0) - 1f;
        if (!bevelled)
        {
            return new[]
            {
                new Vector2(x0, y1), new Vector2(x1, y1),
                new Vector2(x1, y0), new Vector2(x0, y0),
            };
        }
        return new[]
        {
            new Vector2(x0, y1), new Vector2(x1, y1),
            new Vector2(x1, y0 + cutH),   // 斜線的上端
            new Vector2(x1 - cutW, y0),   // 斜線的下端
            new Vector2(x0, y0),
        };
    }

    /// <summary>A stroked spiral: the radius shrinks as the sweep advances.</summary>
    private static void AddSpiral(VertexHelper vh, Vector2 centre, float outer, float inner,
        float startDegrees, float sweepDegrees, float thickness, Color colour)
    {
        const int Steps = 18;
        int first = vh.currentVertCount;
        for (int i = 0; i <= Steps; i++)
        {
            float t = i / (float)Steps;
            float angle = (startDegrees + sweepDegrees * t) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(outer, inner, t);
            Vector2 unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            // 尾端收細，卷起來才有粗細變化，不是一條等寬的彎管。
            float half = thickness * (1f - t * 0.55f) * 0.5f;
            AddVertex(vh, centre + unit * (radius - half), colour);
            AddVertex(vh, centre + unit * (radius + half), colour);
        }

        for (int i = 0; i < Steps; i++)
        {
            int a = first + i * 2;
            vh.AddTriangle(a, a + 1, a + 3);
            vh.AddTriangle(a + 3, a + 2, a);
        }
    }

    private static void AddDiamond(VertexHelper vh, Vector2 centre, float size, Color colour)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, new Vector2(centre.x, centre.y + size), colour);
        AddVertex(vh, new Vector2(centre.x + size * 0.62f, centre.y), colour);
        AddVertex(vh, new Vector2(centre.x, centre.y - size), colour);
        AddVertex(vh, new Vector2(centre.x - size * 0.62f, centre.y), colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static void AddRect(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
    {
        int first = vh.currentVertCount;
        AddVertex(vh, new Vector2(x0, y0), colour);
        AddVertex(vh, new Vector2(x0, y1), colour);
        AddVertex(vh, new Vector2(x1, y1), colour);
        AddVertex(vh, new Vector2(x1, y0), colour);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first + 2, first + 3, first);
    }

    private static void AddVertex(VertexHelper vh, Vector2 position, Color colour)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = colour;
        vh.AddVert(vertex);
    }
}
