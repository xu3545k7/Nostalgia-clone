using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>How the song list is split into the cards of the top bar.</summary>
public enum SongGroupingMode
{
    /// <summary>The collection each song was filed under.</summary>
    Genre = 0,
    /// <summary>One card per difficulty level, hardest first.</summary>
    Level = 1,
}

/// <summary>
/// The bar above the song carousel: a mode selector plus a sliding strip of
/// category cards.  The strip uses the same <see cref="CarouselScroller"/> as
/// the songs, so it spins continuously and snaps to whichever card is nearest.
/// Changing the collection reloads the whole song list, so the change is only
/// committed once the strip has settled, never mid-slide.
/// </summary>
public sealed class SongGroupingBar
{
    /// <summary>Raised once the strip has come to rest on a different card.</summary>
    public event Action<int> CategoryCommitted;
    /// <summary>Raised when the player picks a different grouping from the dropdown.</summary>
    public event Action<SongGroupingMode> ModeChanged;

    private const int CardCount = 5;      // roles -2 .. +2
    private const int MaxRole = 2;
    private const float SpacingRatio = 0.60f;   // of the viewport width
    private const float CardWidthRatio = 0.56f;

    /// <summary>槽兩側那幾顆控制項的尺寸：兩顆箭頭和漏斗共用一組。</summary>
    /// <remarks>
    /// **為什麼是 72 而不是「看起來夠」。** 牌面是九宮切片、邊框 20，所以寬度扣掉
    /// 左右兩條鎏金線腳才是可以放圖示的凹面。52 寬的漏斗鈕凹面只有 12，而漏斗圖示
    /// 22 寬 —— 圖示是壓在線腳上的；箭頭鈕更慘，31.7 寬連邊框都擺不下（見
    /// <see cref="ClassicalBookUITheme.StylePlate"/>）。72 給出 32×28 的凹面，
    /// 26 的圖示四周各留 3。
    ///
    /// 三顆同尺寸，所以槽兩側讀起來是同一排五金，不是三顆各自長大的鈕。
    /// </remarks>
    public const float SideButtonWidth = 72f;
    public const float SideButtonHeight = 68f;
    /// <summary>控制項彼此之間、以及和槽之間的縫。</summary>
    public const float SideButtonGap = 6f;

    private sealed class Card
    {
        public RectTransform root;
        public Graphic plate;
        public TextMeshProUGUI label;
        public CanvasGroup group;
        public int role;
    }

    private readonly CarouselScroller scroller = new CarouselScroller();
    private readonly List<Card> cards = new List<Card>();
    private readonly List<string> entries = new List<string>();
    private readonly List<Button> modeOptionButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> modeOptionLabels = new List<TextMeshProUGUI>();
    private readonly List<CheckMarkGraphic> modeOptionTicks = new List<CheckMarkGraphic>();
    private readonly List<SongGroupingMode> optionModes = new List<SongGroupingMode>();

    private RectTransform frame;
    private RectTransform viewport;
    private RectTransform songPositionRoot;
    private TextMeshProUGUI songPositionLabel;
    private RectTransform modeButtonRoot;
    private TextMeshProUGUI modeButtonLabel;
    private RectTransform popupRoot;

    private bool dragCandidate;
    private bool dragMoved;
    private bool pointerWasPressed;
    private float dragStartPointerX;
    private float clickSuppressedUntil;

    public SongGroupingMode Mode { get; private set; } = SongGroupingMode.Genre;
    public bool IsBuilt => frame != null;
    public bool PointerOverBar { get; private set; }
    public bool IsPopupOpen => popupRoot != null && popupRoot.gameObject.activeSelf;
    /// <summary>True while the strip is still moving, so the caller can hold off on expensive work.</summary>
    public bool IsSliding => scroller.Moving;
    /// <summary>The card currently nearest the middle, even mid-slide.</summary>
    public int FocusedIndex => scroller.BaseIndex;

    // ---- construction -----------------------------------------------------

    /// <summary>
    /// Upgrades the existing category frame in place: the old single label is
    /// retired in favour of a clipped, sliding strip, and a mode dropdown is
    /// added to its left.
    /// </summary>
    public void Build(RectTransform existingFrame, TextMeshProUGUI retiredLabel, SongGroupingMode initialMode)
    {
        if (existingFrame == null || IsBuilt) return;
        frame = existingFrame;
        Mode = initialMode;

        if (retiredLabel != null) retiredLabel.gameObject.SetActive(false);

        viewport = CreateChild(frame, "CategoryViewport");
        // 吃滿整個槽（原本是 0.14～0.86）。那兩成是留給槽裡面的箭頭的，而箭頭早就
        // 搬到槽外面去了 —— 留著的結果是一條 258 寬的緞帶坐在 640 寬的槽裡，兩邊各
        // 空 95，讀起來是「這塊東西配錯了尺寸」。
        viewport.anchorMin = new Vector2(0.03f, 0f);
        viewport.anchorMax = new Vector2(0.97f, 1f);
        viewport.offsetMin = new Vector2(8f, 4f);
        viewport.offsetMax = new Vector2(-8f, -4f);
        // Neighbouring cards must not spill out over the gold frame.
        var cardMask = viewport.gameObject.AddComponent<RectMask2D>();
        // **柔邊，不是切邊。** 旁邊那兩張是被裁一半的，而硬裁出來的那一條垂直切口
        // 本身就是一條邊 —— 眼睛讀到的是「一張貼圖被切掉」，不是「後面還有」。
        // 柔化之後它是淡出去的，「兩側還有別的分類」這件事才由淡出本身講。
        cardMask.softness = new Vector2Int(30, 0);

        for (int i = 0; i < CardCount; i++)
        {
            int role = i - MaxRole;
            RectTransform cardRoot = CreateChild(viewport, $"CategoryCard_{role}");
            cardRoot.anchorMin = new Vector2(0.5f, 0f);
            cardRoot.anchorMax = new Vector2(0.5f, 1f);
            cardRoot.pivot = new Vector2(0.5f, 0.5f);

            // 分類卡是書籤，不是按鈕。這一列回答的是「現在翻到書的哪一段」，
            // 而這整個畫面是書、燙金和緞帶 —— 矩形是這裡唯一沒有的形狀。
            // CanvasRenderer 明寫出來。分類卡原本掛的是 Image，它自己會帶一個 —— 換
            // 成自訂的 Graphic 之後就沒了，而缺少它只有在 RectMask2D 要裁切的那一刻
            // 才會炸，看起來和繪製完全無關。
            if (cardRoot.GetComponent<CanvasRenderer>() == null)
                cardRoot.gameObject.AddComponent<CanvasRenderer>();
            var plate = cardRoot.gameObject.AddComponent<ClassicalTabGraphic>();
            plate.color = ClassicalBookUITheme.Wood;
            plate.raycastTarget = false;

            RectTransform labelRoot = CreateChild(cardRoot, "Label");
            labelRoot.offsetMin = new Vector2(14f, 5f);
            labelRoot.offsetMax = new Vector2(-14f, -5f);
            var label = labelRoot.gameObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 15f;
            label.fontSizeMax = 27f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            label.color = ClassicalBookUITheme.ParchmentLight;
            label.raycastTarget = false;
            ClassicalBookUITheme.ApplyLocalizedFont(label);

            cards.Add(new Card
            {
                root = cardRoot,
                plate = plate,
                label = label,
                group = cardRoot.gameObject.AddComponent<CanvasGroup>(),
                role = role,
            });
        }

        BuildSongPositionLabel();
        BuildModeSelector();
        UpdateModeButtonCaption();
    }

    private void BuildSongPositionLabel()
    {
        songPositionRoot = CreateChild(frame, "SongPosition");
        songPositionRoot.anchorMin = new Vector2(0f, 0f);
        songPositionRoot.anchorMax = new Vector2(1f, 0f);
        songPositionRoot.pivot = new Vector2(0.5f, 1f);
        songPositionRoot.anchoredPosition = new Vector2(0f, -8f);
        songPositionRoot.sizeDelta = new Vector2(0f, 26f);

        songPositionLabel = songPositionRoot.gameObject.AddComponent<TextMeshProUGUI>();
        songPositionLabel.alignment = TextAlignmentOptions.Center;
        // 14 在這塊畫布上是 10.5 個實體像素（參考解析度 2560 對 1080p ＝ 0.75）——
        // 那是給註腳的字級，而這一行是在回答「我在曲庫的哪裡」。
        songPositionLabel.fontSize = 18f;
        songPositionLabel.fontStyle = FontStyles.Bold;
        songPositionLabel.color = ClassicalBookUITheme.AgedPaper;
        songPositionLabel.raycastTarget = false;
        ClassicalBookUITheme.ApplyLocalizedFont(songPositionLabel);
    }

    /// <summary>分組下拉的寬度。照最長的選項訂，和漏斗鈕的寬度無關。</summary>
    private const float PopupWidth = 230f;
    /// <summary>一列選項多高。</summary>
    /// <remarks>
    /// 38 不夠：選項的底是同一塊九宮切片的牌面，上下兩條線腳各 20，38 高的話
    /// uGUI 會把它們等比壓到 19、中間的凹面歸零 —— 字直接坐在糊掉的線腳上。
    /// 見 <see cref="ClassicalBookUITheme.StylePlate"/>。
    /// </remarks>
    private const float OptionHeight = 44f;
    private const float OptionSpacing = 4f;
    /// <summary>選單四周留多寬。</summary>
    /// <remarks>
    /// 必須大於 20：選單自己也是一塊牌，而它的鎏金線腳就佔掉最外面那 20。留 6 的話
    /// 選項會蓋在外框的線腳上，變成「牌中牌」互相切。
    /// </remarks>
    private const float PopupPadding = 22f;

    private void BuildModeSelector()
    {
        // **掛在槽底下，不是掛在槽的父層。**
        //
        // 原本這顆和槽是兄弟，位置抄了一份 <c>frame.anchoredPosition.y</c>。那是一個
        // **快照**：分類列整條是捲簾（<see cref="RecitalCategoryBlind"/>），拉繩的時候
        // 簾子只搬布和槽，所以槽、箭頭（它們是槽的小孩）一起往下走，而這顆釘在原地
        // 被遮罩一格一格擦出來 —— 整排五金裡就它不會動。
        //
        // 改成槽的小孩，位置改用槽的左緣當錨點：它跟著槽走，y 也不再是抄來的。
        modeButtonRoot = CreateChild(frame, "GroupingModeButton");
        // 改成一顆漏斗：「音樂類型」這四個字只是在說「這是一個篩選器」，
        // 而漏斗圖示說的是同一件事、只要四分之一的寬度。目前選了哪一個
        // 在下拉選單裡用勾勾標記，不必写在鈕上。
        modeButtonRoot.anchorMin = new Vector2(0f, 0.5f);
        modeButtonRoot.anchorMax = new Vector2(0f, 0.5f);
        modeButtonRoot.pivot = new Vector2(1f, 0.5f);
        // 左箭頭佔掉槽緣外的 6..78，漏斗再往外一個縫。
        modeButtonRoot.anchoredPosition =
            new Vector2(-(SideButtonGap * 2f + SideButtonWidth), 0f);
        modeButtonRoot.sizeDelta = new Vector2(SideButtonWidth, SideButtonHeight);

        var buttonImage = modeButtonRoot.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.StylePlate(buttonImage, false);

        RectTransform iconRoot = CreateChild(modeButtonRoot, "Funnel");
        iconRoot.anchorMin = new Vector2(0.5f, 0.5f);
        iconRoot.anchorMax = new Vector2(0.5f, 0.5f);
        iconRoot.pivot = new Vector2(0.5f, 0.5f);
        iconRoot.anchoredPosition = Vector2.zero;
        iconRoot.sizeDelta = new Vector2(26f, 28f);
        var funnel = iconRoot.gameObject.AddComponent<FunnelIconGraphic>();
        funnel.color = ClassicalBookUITheme.ParchmentLight;
        funnel.raycastTarget = false;

        // 標籤還在，只是不上場：<c>UpdateModeButtonCaption</c> 和本地化都會寫它，
        // 拿掉的話那兩條路都要跘空指標。
        RectTransform captionRoot = CreateChild(modeButtonRoot, "Label");
        captionRoot.anchorMin = Vector2.zero;
        captionRoot.anchorMax = Vector2.one;
        captionRoot.offsetMin = Vector2.zero;
        captionRoot.offsetMax = Vector2.zero;
        modeButtonLabel = captionRoot.gameObject.AddComponent<TextMeshProUGUI>();
        modeButtonLabel.raycastTarget = false;
        captionRoot.gameObject.SetActive(false);

        var button = modeButtonRoot.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock buttonColors = button.colors;
        buttonColors.normalColor = Color.white;
        buttonColors.highlightedColor = new Color(1.28f, 1.12f, 0.76f, 1f);
        buttonColors.pressedColor = new Color(0.72f, 0.62f, 0.48f, 1f);
        buttonColors.fadeDuration = 0.10f;
        button.colors = buttonColors;
        button.onClick.AddListener(TogglePopup);

        BuildPopup(frame);
    }

    private void BuildPopup(RectTransform parent)
    {
        popupRoot = CreateChild(parent, "GroupingModePopup");
        // 掛在槽的左下角：右緣和漏斗對齊、往左長、往下掉。
        //
        // 原本是抄漏斗的錯點再往下位移一個槽高。那時它和漏斗同一層，所以位移算的是
        // 「從鈕的上緣往下」—— 現在兩個都掛在槽上，直接用槽的下緣當錨點，少一次換算
        // 也少一個會被槽高改動帶歪的地方。
        popupRoot.anchorMin = new Vector2(0f, 0f);
        popupRoot.anchorMax = new Vector2(0f, 0f);
        popupRoot.pivot = new Vector2(1f, 1f);
        popupRoot.anchoredPosition =
            new Vector2(modeButtonRoot.anchoredPosition.x, -SideButtonGap);

        // **選單就是那顆鈕的另一面，所以用同一塊銅牌。**
        //
        // 原本是「深色紙面 ＋ uGUI 的 Outline 畫一圈金」。Outline 不是描邊，它是把整
        // 張圖再畫四份、各偏移兩格 —— 底是平鋪的紙紋，所以那一圈金其實是四份錯開的
        // 紙紋疊起來，糊、而且四倍的填充。牌面的鎏金是**算過光的**（每一格邊框知道
        // 自己朝哪邊），上緣亮下緣暗，不必再描。
        //
        // 落影留著，而且只留一份：它是這一層浮在絨布上的唯一線索。
        var background = popupRoot.gameObject.AddComponent<Image>();
        ClassicalBookUITheme.StylePlate(background, false);
        var shadow = popupRoot.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(6f, -6f);

        var layout = popupRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        int pad = Mathf.RoundToInt(PopupPadding);
        layout.padding = new RectOffset(pad, pad, pad, pad);
        layout.spacing = OptionSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var modes = (SongGroupingMode[])Enum.GetValues(typeof(SongGroupingMode));
        for (int i = 0; i < modes.Length; i++)
        {
            SongGroupingMode mode = modes[i];
            RectTransform optionRoot = CreateChild(popupRoot, $"Option_{mode}");

            var optionImage = optionRoot.gameObject.AddComponent<Image>();
            ClassicalBookUITheme.StylePlate(optionImage, true);
            var optionLayout = optionRoot.gameObject.AddComponent<LayoutElement>();
            optionLayout.preferredHeight = OptionHeight;

            RectTransform optionLabelRoot = CreateChild(optionRoot, "Label");
            optionLabelRoot.anchorMin = Vector2.zero;
            optionLabelRoot.anchorMax = Vector2.one;
            // 左邊讓開 24、右邊讓開 48。
            //
            // 這一列也是一塊九宮切片的牌，左右兩條鎏金線腳各佔 20 —— 字帶要從 20
            // 以後才開始，不然字是坐在線腳上的。右邊那 48 是線腳加勾勾。
            optionLabelRoot.offsetMin = new Vector2(24f, 0f);
            optionLabelRoot.offsetMax = new Vector2(-48f, 0f);
            var optionLabel = optionLabelRoot.gameObject.AddComponent<TextMeshProUGUI>();
            optionLabel.text = DescribeMode(mode);
            optionLabel.alignment = TextAlignmentOptions.Center;
            // 19 不是 15：一列 44 高，15 級的字在裡面讀起來是標註不是選項。
            optionLabel.fontSize = 19f;
            // 換行要關掉、超長要縮字：這一格的寬度是版面訂的，字長是語言訂的。
            // 預設是會換行的，而一列只有 44 高 —— 換成兩行就是上下都被切掉，
            // 而不是「字小了一點」。
            optionLabel.textWrappingMode = TextWrappingModes.NoWrap;
            optionLabel.overflowMode = TextOverflowModes.Overflow;
            optionLabel.enableAutoSizing = true;
            optionLabel.fontSizeMin = 13f;
            optionLabel.fontSizeMax = 19f;
            optionLabel.fontStyle = FontStyles.Bold;
            optionLabel.color = ClassicalBookUITheme.ParchmentLight;
            optionLabel.raycastTarget = false;
            ClassicalBookUITheme.ApplyLocalizedFont(optionLabel);

            var optionButton = optionRoot.gameObject.AddComponent<Button>();
            optionButton.targetGraphic = optionImage;
            ColorBlock optionColors = optionButton.colors;
            optionColors.normalColor = Color.white;
            optionColors.highlightedColor = new Color(1.34f, 1.16f, 0.76f, 1f);
            optionColors.pressedColor = new Color(0.70f, 0.58f, 0.42f, 1f);
            optionColors.fadeDuration = 0.08f;
            optionButton.colors = optionColors;
            SongGroupingMode captured = mode;
            optionButton.onClick.AddListener(() => SelectMode(captured));
            RectTransform tickRoot = CreateChild(optionRoot, "Tick");
            tickRoot.anchorMin = new Vector2(1f, 0.5f);
            tickRoot.anchorMax = new Vector2(1f, 0.5f);
            tickRoot.pivot = new Vector2(1f, 0.5f);
            // −26 不是 −8：靠到 −8 的話勾勾疊在牌的鎏金線腳上。
            tickRoot.anchoredPosition = new Vector2(-26f, 0f);
            tickRoot.sizeDelta = new Vector2(18f, 18f);
            var tick = tickRoot.gameObject.AddComponent<CheckMarkGraphic>();
            tick.color = ClassicalBookUITheme.Gold;
            tick.raycastTarget = false;

            modeOptionButtons.Add(optionButton);
            modeOptionLabels.Add(optionLabel);
            modeOptionTicks.Add(tick);
            optionModes.Add(mode);
        }

        // **寬度不能留 0，也不能跟著鈕。**
        //
        // 零尺寸錯點，所以 sizeDelta 就是實際尺寸，而原本這裡只給了高。寬 0 的
        // VerticalLayoutGroup 把每一個選項壓成 0 寬，標籤就一個字一行往下排 ——
        // 實機上看到的是一條直的字。
        //
        // 第一次修是改成「跟鈕一樣寬」，那時鈕上還寫著「音樂類型」四個字。鈕後來
        // 換成一顆漏斗圖示、寬度剩 52，這一行卻沒跟著改 —— 選項的字帶等於
        // 52 − 10（左）− 26（右邊留給勾勾）＝ **16 寬**，於是同一個症狀換個樣子
        // 回來了：四個字擠成四行。所以寬度寫死一個「照字訂」的值，和鈕脫鉤。
        //
        // 230：扣掉四周的 22、兩條線腳的 20、右邊勾勾的 48 之後，字帶剩 114，而最長
        // 的選項在各語言量起來是 5 個全角字 —— 19 級時 95 寬，放得下。
        //
        // 高度照內容算，不是一個湊出來的數字：四周 22 ×2 ＋ 每列 44 ＋ 列間 4。
        popupRoot.sizeDelta = new Vector2(PopupWidth,
            PopupPadding * 2f + modes.Length * OptionHeight
                + Mathf.Max(0, modes.Length - 1) * OptionSpacing);
        popupRoot.gameObject.SetActive(false);
        RefreshOptionHighlight();
    }

    /// <summary>Marks which grouping is currently in force.</summary>
    private void RefreshOptionHighlight()
    {
        for (int i = 0; i < modeOptionButtons.Count && i < optionModes.Count; i++)
        {
            var graphic = modeOptionButtons[i].targetGraphic as Image;
            if (graphic == null) continue;
            bool current = optionModes[i] == Mode;
            // 在白色附近動。這組顏色是**乘在**酒紅牌面的貼圖上的，原本那對
            // （0.46／0.30）會把兩張牌都乘到近黑 —— 字是亮的、牌是黑的，所以看起來
            // 兩列都沒有底。這一條在分類卡上已經踩過一次（見 Layout 的註解）。
            graphic.color = current
                ? Color.white
                : new Color(0.70f, 0.64f, 0.60f, 1f);
            // 勾勾是該選項是不是目前這一個的唯一記號 —— 底色的差別在深色選單
            // 上太微弱，靠它一個人讀不出來。
            if (i < modeOptionTicks.Count && modeOptionTicks[i] != null)
                modeOptionTicks[i].gameObject.SetActive(current);
        }
    }

    private static RectTransform CreateChild(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static string DescribeMode(SongGroupingMode mode)
    {
        switch (mode)
        {
            case SongGroupingMode.Level:
                return Localize.T("等級", "等级", "Level");
            default:
                return Localize.T("音樂類型", "音乐类型", "Genre");
        }
    }

    // ---- content ----------------------------------------------------------

    /// <summary>Replace the card captions and place the strip on one of them.</summary>
    public void SetEntries(IReadOnlyList<string> names, int focusedIndex)
    {
        entries.Clear();
        if (names != null)
        {
            for (int i = 0; i < names.Count; i++) entries.Add(names[i] ?? string.Empty);
        }

        scroller.SetCount(entries.Count);
        scroller.SnapTo(focusedIndex);
        RefreshCardText();
        Layout();
    }

    public void SetSongPosition(int songNumber, int songCount)
    {
        if (songPositionLabel == null) return;
        songPositionLabel.text = songCount > 0 ? $"{songNumber} / {songCount}" : string.Empty;
    }

    public void RefreshLocalization()
    {
        UpdateModeButtonCaption();
        for (int i = 0; i < modeOptionLabels.Count && i < optionModes.Count; i++)
        {
            TextMeshProUGUI label = modeOptionLabels[i];
            if (label == null) continue;
            label.text = DescribeMode(optionModes[i]);
            ClassicalBookUITheme.ApplyLocalizedFont(label);
        }
    }

    private void UpdateModeButtonCaption()
    {
        if (modeButtonLabel == null) return;
        // The previous down-triangle is absent from several CJK fallback fonts
        // and appeared as a square. The framed button already communicates that
        // this caption is selectable, so no unsupported glyph is needed.
        modeButtonLabel.text = DescribeMode(Mode);
        // The font is picked from the text, so it has to be re-resolved whenever
        // the caption changes language.
        ClassicalBookUITheme.ApplyLocalizedFont(modeButtonLabel);
    }

    private void RefreshCardText()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            if (card.label == null) continue;
            card.label.text = entries.Count > 0
                ? entries[scroller.WrapIndex(scroller.BaseIndex + card.role)]
                : string.Empty;
            // Category captions come from the song library and must retain their
            // original spelling even when the interface language changes.
            ClassicalBookUITheme.ApplyContentFont(card.label);
        }
    }

    // ---- input and motion -------------------------------------------------

    /// <summary>Step the strip by one card; used by the frame's arrow buttons.</summary>
    public void Step(int direction)
    {
        if (IsClickSuppressed()) return;
        scroller.Nudge(direction >= 0 ? 1 : -1);
    }

    /// <summary>
    /// Drives the strip for one frame.  The caller owns pointer and wheel
    /// reading so the song carousel and this bar cannot both act on one notch.
    /// </summary>
    public void Tick(float deltaTime, float wheelDelta, bool pointerPressed, Vector2 pointerScreenPosition,
        Camera uiCamera)
    {
        if (!IsBuilt) return;

        PointerOverBar = RectTransformUtility.RectangleContainsScreenPoint(
            frame, pointerScreenPosition, uiCamera);

        if (PointerOverBar && Mathf.Abs(wheelDelta) > 0.01f)
        {
            int steps = scroller.ConsumeWheel(wheelDelta);
            // Wheel up walks towards the previous card.
            if (steps != 0) scroller.Nudge(-steps);
        }

        HandleDrag(deltaTime, pointerPressed, pointerScreenPosition, uiCamera);
        HandlePopupDismiss(pointerPressed, pointerScreenPosition, uiCamera);

        scroller.Tick(deltaTime, out bool baseChanged, out bool settled);
        if (baseChanged) RefreshCardText();
        Layout();
        if (settled) CategoryCommitted?.Invoke(scroller.BaseIndex);
        pointerWasPressed = pointerPressed;
    }

    private void HandleDrag(float deltaTime, bool pointerPressed, Vector2 pointerScreenPosition, Camera uiCamera)
    {
        if (entries.Count <= 1)
        {
            dragCandidate = false;
            dragMoved = false;
            return;
        }

        if (!dragCandidate)
        {
            // A press that began elsewhere must not grab the strip on its way past.
            if (!pointerPressed || pointerWasPressed || !PointerOverBar || IsPopupOpen) return;
            dragCandidate = true;
            dragMoved = false;
            dragStartPointerX = pointerScreenPosition.x;
            return;
        }

        if (pointerPressed)
        {
            float pixels = pointerScreenPosition.x - dragStartPointerX;
            // Below the threshold this is still a click on an arrow button.
            if (!dragMoved && Mathf.Abs(pixels) < 12f) return;
            if (!dragMoved)
            {
                dragMoved = true;
                scroller.BeginDrag();
            }
            scroller.DragTo(scroller.DragAnchorScroll - pixels / GetPixelsPerCard(uiCamera), deltaTime);
            return;
        }

        dragCandidate = false;
        if (dragMoved)
        {
            scroller.EndDrag();
            // Swallow the click the drag would otherwise deliver on release.
            clickSuppressedUntil = Time.unscaledTime + 0.15f;
        }
        dragMoved = false;
    }

    private void HandlePopupDismiss(bool pointerPressed, Vector2 pointerScreenPosition, Camera uiCamera)
    {
        if (!IsPopupOpen || !pointerPressed) return;
        bool onPopup = RectTransformUtility.RectangleContainsScreenPoint(
            popupRoot, pointerScreenPosition, uiCamera);
        bool onButton = modeButtonRoot != null && RectTransformUtility.RectangleContainsScreenPoint(
            modeButtonRoot, pointerScreenPosition, uiCamera);
        if (!onPopup && !onButton) SetPopupOpen(false);
    }

    private float GetPixelsPerCard(Camera uiCamera)
    {
        float viewportWidth = viewport != null ? viewport.rect.width : 200f;
        float scale = frame != null ? frame.lossyScale.x : 1f;
        return Mathf.Max(1f, viewportWidth * SpacingRatio * Mathf.Max(0.01f, scale));
    }

    private bool IsClickSuppressed()
    {
        return dragMoved || Time.unscaledTime < clickSuppressedUntil;
    }

    private void Layout()
    {
        if (viewport == null) return;
        Rect rect = viewport.rect;
        float spacing = rect.width * SpacingRatio;
        Vector2 cardSize = new Vector2(rect.width * CardWidthRatio, rect.height);
        float frac = scroller.Frac;

        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            if (card.root == null) continue;

            float distance = card.role - frac;
            float absDistance = Mathf.Abs(distance);
            float weight = Mathf.Clamp01(absDistance);
            float eased = weight * weight * (3f - 2f * weight);

            // The neighbours stay legible but clearly secondary; anything beyond
            // one card out is only there to cover the edge while sliding.
            //
            // 斜率從 0.72 放到 0.50。0.72 的時候隔壁只剩 0.28，再乘上牌面自己的
            // 0.68 —— 實際是 0.19，名字完全讀不出來，於是這一條讀起來是「一條紅
            // 緞帶加兩塊暗斑」，而不是「一排可以翻過去的分類」。**次要不等於看不
            // 見**：隔壁那張的用處就是讓人知道下一個是什麼。
            float alpha = Mathf.Clamp01(1f - absDistance * 0.50f);
            bool visible = entries.Count > 0 && alpha > 0.01f;
            if (card.root.gameObject.activeSelf != visible) card.root.gameObject.SetActive(visible);
            if (!visible) continue;

            card.root.sizeDelta = cardSize;
            card.root.anchoredPosition = new Vector2(spacing * distance, 0f);
            card.root.localScale = Vector3.one * Mathf.Lerp(1f, 0.82f, eased);
            if (card.group != null) card.group.alpha = alpha;
            // 焦點的強調改由牌面的**染色**負責：中間那張是完整的牌，兩側調暗調淡。
            // 原本是把金色描邊加粗加亮，但描邊只是把整張圖再畫一次偏移的副本 ——
            // 加粗只會讓邊緣更糊。而且這裡的染色是乘在牌面貼圖上的，原本那組給
            // 平塗用的暗色（0.10～0.30）會把整張牌乘成近黑。
            // 焦點的強調由緞帶本身的顏色負責：中間那條是完整的酒紅，兩側退回
            // 暗木色並且淡出。緞帶的絲光和金線是從這個顏色推出來的，所以整條
            // 帶子會一起亮起來，而不是只有邊框變色。
            //
            // 兩側退到**暗酒紅**，不是退到木色。退成木色的時候隔壁那張連「它也是
            // 一條緞帶」都看不出來 —— 木色正好和槽底同一個色系，所以它讀起來是槽
            // 上的一塊髒。留著酒紅，退的就只是亮度。
            if (card.plate != null)
                card.plate.color = Color.Lerp(
                    new Color(0.26f, 0.085f, 0.062f, 0.78f),
                    ClassicalBookUITheme.Burgundy, 1f - eased);
        }
    }

    // ---- mode dropdown ----------------------------------------------------

    private void TogglePopup()
    {
        if (IsClickSuppressed()) return;
        SetPopupOpen(!IsPopupOpen);
    }

    public void SetPopupOpen(bool open)
    {
        if (popupRoot == null) return;
        popupRoot.gameObject.SetActive(open);
        if (open) popupRoot.SetAsLastSibling();
    }

    private void SelectMode(SongGroupingMode mode)
    {
        SetPopupOpen(false);
        if (mode == Mode) return;
        Mode = mode;
        UpdateModeButtonCaption();
        RefreshOptionHighlight();
        ModeChanged?.Invoke(mode);
    }
}
