using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 選曲畫面最上面那條簾桿，以及掛在上面的三樣東西：左邊「前往練習模式」、中間玩家
/// 的實力評分、右邊設定。另外還有右下角那條評分明細的入口。
/// </summary>
/// <remarks>
/// **為什麼這些東西在桿上。** 這一排講的是「你是誰、你在哪個房間」，不是「你在挑哪
/// 一首歌」—— 挑歌的東西（分類、曲目卡、難度）全部在簾幕底下。把兩件事分到簾的上下
/// 兩側，是這個畫面唯一需要的層級。
///
/// **為什麼評分卡是整塊可以按的。** 它同時是玩家資料的入口（改名、換頭像）。上一版
/// 那個入口藏在左下銘牌的肖像上，而那兩塊銘牌現在拿掉了 —— 如果不把入口接到別的地
/// 方，畫面上就再也沒有任何地方可以改自己的名字。那個錯誤犯過一次了。
///
/// **為什麼右下還要一條評分明細。** 桿上那張卡只印一個數字和一個評語；四項的細項
/// （完整度／旋律／失誤／情感）以前佔掉右下一整塊 780×216 的銘牌，而它們其實是「想
/// 知道為什麼」才會看的東西。收成一條 64 高的入口，數字仍然看得到，細節留給點進去
/// 之後的分析頁。
/// </remarks>
public sealed class SelectionRail : MonoBehaviour
{
    // 字級不寫死：向這個畫布問（見 ClassicalType）。這個畫面的參考解析度是
    // 2560×1440，練習室是 1920×1080，同一個數字在兩邊差四分之一 —— 直接照抄練習室
    // 的值就是「字有點細」看到的東西。另外練習室那張評分卡三行**全是 Bold**，這裡
    // 照做：細體在這個尺寸的深色底上會糊掉。
    private float TextCaption;
    private float TextBody;
    private float TextLabel;
    private float TextTitle;
    private float TextHero;

    private const float CardWidth = 880f;
    private const float CardHeight = 108f;
    /// <summary>
    /// 桿上控制項的規格。設定鈕不是這裡建的（它是設定頁自己的），但它也要落在同一
    /// 條線上，所以這四個值是 internal —— 各寫一份的話兩邊遲早會差幾個像素。
    /// </summary>
    internal const float ControlWidth = 290f;
    internal const float ControlHeight = 108f;
    /// <summary>控制項在桿上的垂直位置：桿頂往下多少。</summary>
    internal const float ControlTop = 16f;
    internal const float SideMargin = 40f;

    /// <summary>下角那兩條的高度。一行字。</summary>
    public const float FootHeight = 64f;
    public const float FootMargin = 24f;

    /// <summary>左右兩塊入口條（左：檢定，右：評分明細）在桿上的規格。</summary>
    /// <remarks>
    /// 原本這兩塊貼在畫面左下和右下。畫面最下面那條 <see cref="KeyZoneBar"/> 是全域
    /// 覆蓋層、不屬於任何畫面，不會把東西推開 —— 兩塊牌會被壓在底下。
    ///
    /// **放進桿上那一排，不是掛在它底下。** 先做過「往下 ControlHeight + 28」那一版，
    /// 結果是兩塊牌懸在導覽列和簾幕之間，既不屬於上面那排也不屬於下面的挑歌區。
    ///
    /// **寬度要收窄。** 它們原本是 536，而桿上左右各只剩 510 的空檔
    /// （前往練習模式佔到 x=330，中央評分卡 880 寬置中從 840 開始）—— 照原寬放進去
    /// 會和中央那張卡疊到。480 兩邊各留約 14 的縫。
    ///
    /// 高度維持它們自己的 <see cref="FootHeight"/>，用垂直置中對齊那一排，
    /// 這樣不必動它們內部的版面。
    /// </remarks>
    public const float NavPlaqueWidth = 480f;

    /// <summary>兩塊牌離畫面左右邊緣多遠。接在左右兩個控制項外側。</summary>
    public const float NavPlaqueInset = SideMargin + ControlWidth + 16f;

    /// <summary>兩塊牌從畫面頂端往下多少（在那一排裡垂直置中）。</summary>
    public const float NavPlaqueDrop = ControlTop + (ControlHeight - FootHeight) * 0.5f;
    private const float FootWidth = 536f;

    private static readonly Color Ink = new Color(0.941f, 0.894f, 0.800f, 1f);
    private static readonly Color InkDim = new Color(0.729f, 0.659f, 0.533f, 1f);
    private static readonly Color Paper = new Color(0.925f, 0.878f, 0.769f, 1f);
    private static readonly Color PaperInk = new Color(0.227f, 0.133f, 0.063f, 1f);
    private static readonly Color PaperInkDim = new Color(0.478f, 0.337f, 0.180f, 1f);

    private SongSelectionManager manager;
    private TextMeshProUGUI ratingCaption;
    private TextMeshProUGUI ratingValue;
    private TextMeshProUGUI ratingTitle;
    private TextMeshProUGUI ratingGap;
    private TextMeshProUGUI footValue;

    /// <summary>換語言之後要跟著換字的兩個抬頭。建好就不再動的話會卡在舊語言。</summary>
    private TextMeshProUGUI practiceLabel;
    private TextMeshProUGUI footCaption;

    public static SelectionRail Attach(RectTransform panel, SongSelectionManager owner)
    {
        if (panel == null) return null;
        SelectionRail existing = panel.GetComponentInChildren<SelectionRail>(true);
        if (existing != null) return existing;

        var host = new GameObject("SelectionRail", typeof(RectTransform));
        host.layer = panel.gameObject.layer;
        var rect = host.GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        SelectionRail rail = host.AddComponent<SelectionRail>();
        rail.manager = owner;
        rail.Build(rect);
        return rail;
    }

    private void Build(RectTransform root)
    {
        TextCaption = ClassicalType.On(this, ClassicalType.Caption);
        TextBody = ClassicalType.On(this, ClassicalType.Body);
        TextLabel = ClassicalType.On(this, ClassicalType.Label);
        TextTitle = ClassicalType.On(this, ClassicalType.Title);
        TextHero = ClassicalType.On(this, ClassicalType.Hero);

        RectTransform bar = Rect(root, "Rail", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, SelectionRailGraphic.Height));
        bar.offsetMin = new Vector2(0f, -SelectionRailGraphic.Height);
        bar.offsetMax = Vector2.zero;
        bar.gameObject.AddComponent<CanvasRenderer>();
        var art = bar.gameObject.AddComponent<SelectionRailGraphic>();
        art.raycastTarget = false;

        BuildPracticeButton(bar);
        BuildRatingCard(bar);
        BuildFootStrip(root);

        Refresh();
        PlayerSlots.Changed += Refresh;
    }

    private void OnDestroy() => PlayerSlots.Changed -= Refresh;

    private void OnEnable() => Refresh();

    /// <summary>成績或玩家變了，外面叫一次。</summary>
    public void RefreshRecord() => Refresh();

    /// <summary>最左：回練習室。桿的左端，離「挑歌」最遠的地方。</summary>
    private void BuildPracticeButton(RectTransform bar)
    {
        Button button = Plate(bar, "ToPractice", new Vector2(0f, 1f),
            new Vector2(SideMargin, -ControlTop), new Vector2(ControlWidth, ControlHeight));
        TextMeshProUGUI label = Label(button.GetComponent<RectTransform>(), "Label",
            PracticeCaption(), TextLabel, ClassicalBookUITheme.ParchmentLight,
            TextAlignmentOptions.Center, FontStyles.Bold);
        practiceLabel = label;
        Stretch(label.rectTransform, 10f);
        button.onClick.AddListener(() =>
        {
            if (manager != null) manager.LeaveForPracticeRoom();
        });
    }

    /// <summary>
    /// 中間：玩家的實力評分。版面和練習室頂欄那張卡一樣。
    /// </summary>
    private void BuildRatingCard(RectTransform bar)
    {
        RectTransform card = Rect(bar, "Rating", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -ControlTop), new Vector2(CardWidth, CardHeight));
        Image face = card.gameObject.AddComponent<Image>();
        face.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        face.type = Image.Type.Tiled;
        face.color = Paper;
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.471f, 0.361f, 0.196f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        ClassicalBookUITheme.AddShadow(card.gameObject, new Color(0f, 0f, 0f, 0.5f),
            new Vector2(4f, -4f));

        var open = card.gameObject.AddComponent<Button>();
        open.targetGraphic = face;
        ColorBlock colours = open.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.06f, 1.04f, 1.00f, 1f);
        colours.pressedColor = new Color(0.90f, 0.87f, 0.82f, 1f);
        open.colors = colours;
        open.onClick.AddListener(() =>
        {
            UiSfx.Open();
            PlayerProfileCard.Show(Refresh);
        });

        // 由上往下：抬頭（小、淡）→ 大數字 → 評語（和數字同一條基線）→ 右邊的差
        // 距。和練習室那張卡同一個排法，字級乘過 4/3。
        ratingCaption = Label(card, "Caption", string.Empty, TextCaption, PaperInkDim,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold, ClassicalType.Tracking);
        Place(ratingCaption.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -10f),
            new Vector2(CardWidth - 52f, 28f));

        ratingValue = Label(card, "Value", "—", TextHero, PaperInk,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(ratingValue.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -38f),
            new Vector2(320f, 64f));

        ratingTitle = Label(card, "Title", string.Empty, TextTitle, PaperInk,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(ratingTitle.rectTransform, new Vector2(0f, 1f), new Vector2(212f, -50f),
            new Vector2(300f, 40f));

        ratingGap = Label(card, "Gap", string.Empty, TextBody, PaperInkDim,
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(ratingGap.rectTransform, new Vector2(1f, 1f), new Vector2(-26f, -52f),
            new Vector2(380f, 36f));
    }

    /// <summary>右下：同一個數字的細節入口。四項長條收掉，留給點進去之後的分析頁。</summary>
    private void BuildFootStrip(RectTransform root)
    {
        Button strip = Plate(root, "RatingDetail", new Vector2(1f, 1f),
            new Vector2(-NavPlaqueInset, -NavPlaqueDrop), new Vector2(NavPlaqueWidth, FootHeight));
        RectTransform rect = strip.GetComponent<RectTransform>();

        TextMeshProUGUI caption = Label(rect, "Caption", DetailCaption(), TextBody, InkDim,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold, ClassicalType.Tracking);
        footCaption = caption;
        Place(caption.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f),
            new Vector2(220f, 32f));

        footValue = Label(rect, "Value", "—", TextTitle, Ink, TextAlignmentOptions.MidlineRight,
            FontStyles.Bold);
        Place(footValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(-46f, 0f),
            new Vector2(260f, 42f));

        TextMeshProUGUI chevron = Label(rect, "More", "›", TextTitle, InkDim,
            TextAlignmentOptions.MidlineRight);
        Place(chevron.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20f, 1f),
            new Vector2(30f, 34f));

        // 分析頁還沒做。現在按下去只出一聲 —— 不接任何東西的按鈕比沒有按鈕糟，但
        // 這一條同時是「你目前幾分」的顯示，所以它留著，按鈕的行為等分析頁。
        strip.onClick.AddListener(UiSfx.Denied);
    }

    /// <summary>
    /// 評分和名字都重畫一次。
    /// </summary>
    /// <remarks>
    /// 讀的是 recital 那一段刻度：轉盤本身就是演奏會的空間。兩段刻度不能混 —— 練習
    /// 模式的旋律和表情有大量「沒被要求就給滿分」，混在一起數字會無緣無故跳一千。
    /// </remarks>
    private void Refresh()
    {
        if (ratingValue == null) return;
        float rating = PlayerRating.Current(true);
        string who = PlayerProfile.Name;
        // 抬頭也在這裡設，不是只在建的時候：換完語言回到選曲畫面（OnEnable →
        // Refresh）才會跟著換，否則這兩行會卡在上一個語言。
        if (practiceLabel != null) practiceLabel.text = PracticeCaption();
        if (footCaption != null) footCaption.text = DetailCaption();
        ratingCaption.text = string.IsNullOrWhiteSpace(who)
            ? Localize.T("實力評分・演奏會", "实力评分・演奏会", "RATING · RECITAL")
            : who + Localize.T("　演奏會評分", "　演奏会评分", "  RECITAL RATING");
        ratingValue.text = rating > 0f ? Mathf.RoundToInt(rating).ToString("N0") : "—";
        ratingTitle.text = PlayerRating.Title(rating, true);
        ratingGap.text = NextStep(rating);
        if (footValue != null)
            footValue.text = rating > 0f ? Mathf.RoundToInt(rating).ToString("N0") : "—";
    }

    /// <summary>距離下一個稱號還差多少。已經最高就印上限。</summary>
    private static string NextStep(float rating)
    {
        const float shift = PlayerRating.RecitalBand;
        float ordinary = PlayerRating.Ordinary + shift;
        float proficient = PlayerRating.Proficient + shift;
        float master = PlayerRating.Ceiling(true);
        // 洞留在格式字串裡（不是內插），繁體那一串才當得了查表的鍵。
        if (rating < ordinary)
            return Localize.F("距普通 {0:N0}", "距普通 {0:N0}", "{0:N0} TO ORDINARY",
                Mathf.CeilToInt(ordinary - rating));
        if (rating < proficient)
            return Localize.F("距精熟 {0:N0}", "距精熟 {0:N0}", "{0:N0} TO PROFICIENT",
                Mathf.CeilToInt(proficient - rating));
        if (rating < master)
            return Localize.F("距名家 {0:N0}", "距名家 {0:N0}", "{0:N0} TO MASTER",
                Mathf.CeilToInt(master - rating));
        return Localize.T("已達上限", "已达上限", "AT MAX");
    }

    private static string PracticeCaption() =>
        Localize.T("‹　前往練習模式", "‹　前往练习模式", "‹  PRACTICE ROOM");

    private static string DetailCaption() =>
        Localize.T("評分明細", "评分明细", "RATING DETAIL");

    // ── 版面小工具 ────────────────────────────────────────────────────

    /// <summary>
    /// 桿上／下角那一族的面：上緣受光、下緣壓暗、一條金髮絲線、一道投影。
    /// </summary>
    /// <remarks>
    /// 左下那條檢定（<see cref="SyllabusPlaque"/>）也用這一支。兩條並排在畫面最下
    /// 緣，各自畫一份的話遲早會漂開 —— 共用建構子是它們長得一樣的唯一保證。
    /// </remarks>
    internal static Button Plate(RectTransform parent, string name, Vector2 corner,
        Vector2 offset, Vector2 size)
    {
        var plateObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button));
        plateObject.layer = parent.gameObject.layer;
        var rect = plateObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        // 用這間房本來就有的鑄板：斜接的黃銅、有方向性的鎏金線腳、凹下去的面、四
        // 角各一顆鉚釘。上一版是平色加一圈外框加上下兩條邊 —— 結構是對的，但在這
        // 間房裡讀起來像「還沒上裝飾」，而這個畫面上其他每一顆按鈕都有線腳。
        Image face = plateObject.GetComponent<Image>();
        ClassicalBookUITheme.StylePlate(face, false);
        ClassicalBookUITheme.AddShadow(plateObject, new Color(0f, 0f, 0f, 0.45f),
            new Vector2(3f, -3f));

        var button = plateObject.GetComponent<Button>();
        button.targetGraphic = face;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.34f, 1.24f, 1.08f, 1f);
        colours.pressedColor = new Color(0.78f, 0.70f, 0.60f, 1f);
        colours.fadeDuration = 0.09f;
        button.colors = colours;
        return button;
    }

    private static RectTransform Rect(RectTransform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot, Vector2 offset, Vector2 size)
    {
        var rectObject = new GameObject(name, typeof(RectTransform));
        rectObject.layer = parent.gameObject.layer;
        var rect = rectObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 corner, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>
    /// 一行字。<paramref name="tracking"/> 只給抬頭用。
    /// </summary>
    /// <remarks>
    /// 字距不是每一行都該有的：抬頭加了字距才像抬頭，但大數字加了字距會散成一排
    /// 互不相干的數字。練習室也是只在抬頭上加。
    /// </remarks>
    private static TextMeshProUGUI Label(RectTransform parent, string name, string text,
        float size, Color colour, TextAlignmentOptions alignment,
        FontStyles style = FontStyles.Normal, float tracking = 0f)
    {
        var labelObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.layer = parent.gameObject.layer;
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(200f, size + 10f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.fontStyle = style;
        label.characterSpacing = tracking;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // TMP 在「框高 < 字級 × 1.5」時整段不畫。這裡的框高是照版面給的。
        label.overflowMode = TextOverflowModes.Overflow;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return label;
    }
}
