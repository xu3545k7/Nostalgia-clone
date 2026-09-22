using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds one carousel card as a volume in the colours of the difficulty the
/// player has chosen for that song: leather, gilding, spine bands and paper.
/// </summary>
/// <remarks>
/// **Why the card and not a label.** The card is already a bound volume, so the
/// difficulty has somewhere to live that is not another caption. A player
/// scrolling the shelf reads the colour of the binding before any text has time
/// to land, and the gem plaque underneath says which chart that colour is.
///
/// **Why the old parts are switched off rather than re-tinted.** The card used
/// to be built from eight straight rectangles -- a spine, a page block, three
/// page edges, two rails and an inner border. Every one of them is a quad with
/// square corners, so on a book whose edges curve they are eight things sticking
/// out through the boards. <see cref="BookBoardGraphic"/> draws all of them as
/// regions of one curved mesh instead; these are hidden, not deleted, because
/// <see cref="ClassicalBookUITheme.StyleCard"/> still expects to find them.
///
/// **Why a component and not a static helper.** Each of the three carousel
/// slots keeps its own card and its own cached parts, and a slot hands its card
/// to a different song as it scrolls.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SongBookDressing : MonoBehaviour
{
    private const string GildingName = "~BookGilding";
    private const string BandName = "~SpineBand";
    private const int MaxBands = 6;

    /// <summary>The straight-edged pieces the curved board takes over from.</summary>
    private static readonly string[] Superseded =
    {
        "CardScoreBackground", "ClosedBookSpine", "ClosedBookSpineGold",
        "ClosedBookRightPages", "ClosedBookPageEdge_0", "ClosedBookPageEdge_1",
        "ClosedBookPageEdge_2", "ArcadeCardTopRail", "ArcadeCardBottomRail",
        "BookInnerBorder",
    };

    /// <summary>The board colour before any difficulty is put on it.</summary>
    private static readonly Color BareLeather = new Color(0.235f, 0.175f, 0.145f, 1f);
    private static readonly Color BareField = new Color(0.265f, 0.200f, 0.165f, 1f);
    private static readonly Color BarePaper = new Color(0.84f, 0.78f, 0.62f, 1f);

    private SongBookGildingGraphic gilding;
    private BookBoardGraphic board;
    private readonly List<Image> spineBands = new List<Image>(MaxBands);
    private bool wired;

    public static SongBookDressing Attach(RectTransform card)
    {
        if (card == null) return null;
        SongBookDressing dressing = card.GetComponent<SongBookDressing>();
        if (dressing == null) dressing = card.gameObject.AddComponent<SongBookDressing>();
        dressing.Wire(card);
        return dressing;
    }

    private void Wire(RectTransform card)
    {
        if (wired) return;
        wired = true;

        for (int i = 0; i < Superseded.Length; i++)
        {
            Transform part = card.Find(Superseded[i]);
            if (part != null) part.gameObject.SetActive(false);
        }

        BuildSpineBands(card);
        BuildBoard(card);
        BuildWear(card);
        BuildGilding(card);
    }

    /// <summary>
    /// The body of the volume, in place of the card's flat rectangle.
    /// </summary>
    private void BuildBoard(RectTransform card)
    {
        board = BookBoardGraphic.Attach(card, Mathf.Abs(card.GetInstanceID()) % 89);
        if (board == null) return;
        board.Profile = BookShape.Profile.Volume;
        // 這個曲庫裝的是練習曲集，不是皮裝全集：一張硬紙折過來釘住，封面是
        // 啞面的、有龜裂的紙，不是打了蠟的皮。
        board.Wrapper = true;
        Sprite cover = ClassicalBookUITheme.GetCoverTextureSprite();
        if (cover != null) board.Grain(cover.texture, cover.texture.width);
    }

    /// <summary>
    /// The raised bands across the spine of a bound volume. How many there are
    /// is how much the binder was asked to do, so the count follows the rank.
    /// </summary>
    private void BuildSpineBands(RectTransform card)
    {
        for (int i = 0; i < MaxBands; i++)
        {
            var bandObject = new GameObject($"{BandName}_{i}", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            bandObject.layer = card.gameObject.layer;
            RectTransform rect = bandObject.GetComponent<RectTransform>();
            rect.SetParent(card, false);
            // 書背在卡片最左邊那一小條上。左端從 1.2% 起算，因為圓背在頭尾會
            // 往內收，貼齊 0 的書帶會凸出板子外面。
            rect.anchorMin = new Vector2(0.014f, 0.5f);
            rect.anchorMax = new Vector2(0.050f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, -3f);
            rect.offsetMax = new Vector2(0f, 3f);
            Image image = bandObject.GetComponent<Image>();
            image.raycastTarget = false;
            bandObject.SetActive(false);
            spineBands.Add(image);
        }
    }

    /// <summary>
    /// The rubbing on the boards. Fixed per card slot, not per song.
    /// </summary>
    /// <remarks>
    /// 三張卡各磨各的，但同一張卡不會因為換一首歌就換一套磨痕 —— 每捲一格就
    /// 重建三張上千個頂點的網格，代價是滾動時的頓挫，換來的是沒有人看得出來
    /// 的差別。
    /// </remarks>
    private void BuildWear(RectTransform card)
    {
        AgedPaperGraphic wear = AgedPaperGraphic.Attach(card, Mathf.Abs(card.GetInstanceID()) % 89, 1);
        if (wear == null) return;
        // 磨損跟著板子的弧線，不然最外圈那道黑會在角落切過書的輪廓。
        wear.Shape(BookShape.Profile.Volume);
        // 髒色一定要比封面暗。之前用的是「皮磨亮」的淺色，但封面換成染過色的
        // 紙以後那個顏色比封面還亮 —— 四邊等於各描了一圈光暈，書口那條紙就
        // 淹沒在裡面，看起來像紙長在書背那一側、又沒接上。紙封面的髒是灰塵和
        // 手汗，那是暗的。
        wear.Stain = new Color(0.10f, 0.075f, 0.065f, 1f);
        wear.Strength = 0.62f;
        wear.EdgeDepth = 0.05f;
        wear.Mottle = 0.30f;
        wear.Foxing = 0;
    }

    private void BuildGilding(RectTransform card)
    {
        Transform existing = card.Find(GildingName);
        if (existing != null)
        {
            gilding = existing.GetComponent<SongBookGildingGraphic>();
            return;
        }

        // CanvasRenderer 要自己列出來：用 new GameObject(types...) 建的時候
        // [RequireComponent] 不會被套用，少了它 Graphic 第一次重建就會炸。
        var gildingObject = new GameObject(GildingName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(SongBookGildingGraphic));
        gildingObject.layer = card.gameObject.layer;
        RectTransform rect = gildingObject.GetComponent<RectTransform>();
        rect.SetParent(card, false);
        // 和板子同一個矩形。金線的弧度是從這個矩形算出來的，差一個像素兩條
        // 弧線就不平行了。
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        gilding = gildingObject.GetComponent<SongBookGildingGraphic>();
        gilding.raycastTarget = false;
    }

    /// <summary>
    /// Puts the whole volume into one difficulty's colours and ornament level.
    /// </summary>
    /// <param name="colour">The difficulty's own colour.</param>
    /// <param name="rank">0 Normal … 4 Special.</param>
    /// <param name="present">False for an empty carousel slot: strip it back.</param>
    public void Show(Color colour, int rank, bool present)
    {
        if (gilding != null)
        {
            gilding.Blank = !present;
            if (present)
            {
                gilding.Tint = colour;
                gilding.Rank = rank;
            }
        }

        if (!present)
        {
            if (board != null) board.Set(BareLeather, BareField, BarePaper, ClassicalBookUITheme.Gold);
            for (int i = 0; i < spineBands.Count; i++)
                if (spineBands[i] != null) spineBands[i].gameObject.SetActive(false);
            return;
        }

        Color gilt = Color.Lerp(ClassicalBookUITheme.Gold, colour, 0.45f);
        Color bright = Color.Lerp(gilt, new Color(1f, 0.95f, 0.82f, 1f), 0.35f);
        // 封面是「染過色的紙」：吃得進難度的顏色，但要先退掉飽和度 —— 練習曲
        // 集的封面是灰藍、土黃這種顏色，不是原色的色紙。字要壓在上面，所以整
        // 體還是暗的。
        Color washed = Wash(colour);
        Color leather = Color.Lerp(BareLeather, washed, 0.62f);
        Color field = Color.Lerp(BareField, washed, 0.52f);
        // 紙不染色。書口是紙，紙就是紙的顏色 —— 這也是整張卡上唯一的亮部。
        if (board != null) board.Set(leather, field, BarePaper, gilt);

        // 紙封面是釘起來的：兩根釘書針，數量不隨難度變 —— 難度是印在封面上，
        // 不是釘出來的。皮裝才用書帶的數量講究竟裝訂得多講究。
        bool stapled = board != null && board.Wrapper;
        LayOutSpineBands(stapled ? 2 : Mathf.Clamp(rank + 2, 2, MaxBands),
            stapled ? new Color(0.44f, 0.42f, 0.39f, 1f) : bright, stapled);
    }

    /// <summary>Spreads the wanted number of bands evenly down the spine.</summary>
    private void LayOutSpineBands(int count, Color colour, bool stapled = false)
    {
        for (int i = 0; i < spineBands.Count; i++)
        {
            Image band = spineBands[i];
            if (band == null) continue;
            bool used = i < count;
            if (band.gameObject.activeSelf != used) band.gameObject.SetActive(used);
            if (!used) continue;

            // 書帶兩端各留 14%；釘書針收得更進來，釘在摺線上。
            float margin = stapled ? 0.28f : 0.14f;
            float t = count == 1 ? 0.5f : margin + (1f - margin * 2f) * (i / (float)(count - 1));
            RectTransform rect = band.rectTransform;
            rect.anchorMin = new Vector2(stapled ? 0.020f : 0.014f, t);
            rect.anchorMax = new Vector2(stapled ? 0.042f : 0.050f, t);
            rect.offsetMin = new Vector2(0f, stapled ? -1.5f : -3f);
            rect.offsetMax = new Vector2(0f, stapled ? 1.5f : 3f);
            band.color = new Color(colour.r, colour.g, colour.b, 0.86f);
        }
    }

    /// <summary>The difficulty colour at a fraction of its brightness.</summary>
    private static Color Dye(Color colour, float level)
    {
        return new Color(colour.r * level, colour.g * level, colour.b * level, 1f);
    }

    /// <summary>
    /// The difficulty colour as a paper would take it: half the saturation, and
    /// dark enough to set pale type on.
    /// </summary>
    private static Color Wash(Color colour)
    {
        Color.RGBToHSV(colour, out float h, out float s, out float v);
        return Color.HSVToRGB(h, s * 0.52f, Mathf.Min(v, 1f) * 0.34f);
    }
}
