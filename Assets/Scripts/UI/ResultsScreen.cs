using System.Collections;
using Judgment;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Classical open-book result screen. The UI is built at runtime so the same
/// presentation is used by every gameplay scene that already owns a ResultsScreen.
/// </summary>
public class ResultsScreen : MonoBehaviour
{
    private static ResultsScreen _instance;
    public static ResultsScreen Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<ResultsScreen>();
            return _instance;
        }
    }

    [Header("Legacy UI References")]
    public GameObject panel;
    public TextMeshProUGUI justText;
    public TextMeshProUGUI greatText;
    public TextMeshProUGUI goodText;
    public TextMeshProUGUI missText;
    public TextMeshProUGUI fastText;
    public TextMeshProUGUI lateText;
    private TextMeshProUGUI centreText;
    private TextMeshProUGUI centreLabelText;
    private TextMeshProUGUI mistouchText;
    private TextMeshProUGUI mistouchLabelText;
    private TextMeshProUGUI preciseText;
    private TextMeshProUGUI preciseLabelText;

    /// <summary>The examiner's panel: four criteria, an average and a grade.</summary>
    private RectTransform assessmentRoot;
    private RectTransform statsRoot;
    private RectTransform scoreRule;
    private RectTransform bottomRule;

    /// <summary>How long a full criterion bar is; the value sits to its right.</summary>
    private const float CriterionBarWidth = 230f;
    /// <summary>Left edge of the bars, and where the mark to their right is centred.</summary>
    private const float CriterionBarLeft = 280f;
    private const float CriterionMarkX = 562f;
    private RectTransform rankMedallion;
    private readonly TextMeshProUGUI[] criterionValues = new TextMeshProUGUI[4];
    private readonly CriterionBarGraphic[] criterionBars = new CriterionBarGraphic[4];
    private TextMeshProUGUI averageText;
    private TextMeshProUGUI gradeText;

    /// <summary>
    /// The verdict's colour, highest first.
    /// </summary>
    /// <remarks>
    /// The rank was carried by a drawn border before this. A border says "this
    /// one is more decorated than that one", which is a comparison you can only
    /// make with both in front of you -- and a player sees one. A colour is
    /// recognised on its own, the same way the difficulty colours are, and it
    /// leaves the word itself set in the display face with nothing drawn around
    /// it competing for the same edge.
    /// </remarks>
    private static readonly Color DistinctionInk = new Color(0.48f, 0.24f, 0.70f, 1f);
    private static readonly Color MeritInk = new Color(0.70f, 0.50f, 0.14f, 1f);
    private static readonly Color PassInk = new Color(0.66f, 0.20f, 0.16f, 1f);
    private static readonly Color FailedInk = new Color(0.20f, 0.34f, 0.66f, 1f);
    public TextMeshProUGUI scoreText;
    public Button closeButton;

    private const string RuntimeRootName = "ClassicalResultsBook";

    // 這本書佔滿螢幕。1920x1080 的設計解析度下留一點邊，板子的弧和書口才看得
    // 到 —— 真的滿版到出血的話，看到的就只剩兩張紙，那本書就不見了。
    private const float BookWidth = 1880f;
    private const float BookHeight = 1046f;
    private const float PageWidth = 855f;
    private const float PageHeight = 937f;
    private const float PageOffset = 448f;

    // 版面照舊在 715x780 的框裡排，整塊放大貼上去。理由寫在 CreatePage。
    private const float FieldWidth = 715f;
    private const float FieldHeight = 780f;
    private const float FieldScale = 1.195f;
    private static readonly Color Backdrop = new Color(0.015f, 0.012f, 0.014f, 0.94f);
    private static readonly Color Leather = new Color(0.105f, 0.058f, 0.035f, 1f);
    private static readonly Color LeatherEdge = new Color(0.255f, 0.135f, 0.065f, 1f);
    // 淡金在米色紙上是這一頁對比最低的組合 —— 抬頭和小標壓暗一階才讀得到。
    private static readonly Color Gold = new Color(0.64f, 0.46f, 0.17f, 1f);
    private static readonly Color PaleGold = new Color(0.93f, 0.79f, 0.47f, 1f);
    private static readonly Color Paper = new Color(0.91f, 0.875f, 0.72f, 1f);
    private static readonly Color PaperLight = new Color(0.985f, 0.972f, 0.86f, 1f);
    private static readonly Color Ink = new Color(0.115f, 0.09f, 0.07f, 1f);
    private static readonly Color MutedInk = new Color(0.31f, 0.25f, 0.18f, 1f);
    /// <summary>
    /// The total: pale green inside, gold all round it.
    /// </summary>
    /// <remarks>
    /// Deliberately the same two colours as the gameplay HUD's score
    /// (<c>StyleScoreNumber</c> in JudgmentManager), so the number a player
    /// watched climbing all song is recognisably the same number here.
    ///
    /// A pale face on cream paper is the one risk in that: it is the gold rim,
    /// not the face, that carries the letterform against this ground. Keep the
    /// rim darker than the paper if either colour is ever retuned -- brightening
    /// it is what would make the total hard to read, not lightening the green.
    /// </remarks>
    /// <summary>
    /// 分數的墨色。深墨綠 —— 舊墨水在奶油色紙上就是這個顏色。
    /// </summary>
    /// <remarks>
    /// 以前是**白字加硬投影**。白色在淺色的紙上沒有東西可以比它更亮，撐不起來，
    /// 只能靠投影把它從背景切出來 —— 而一塊白字加一道 3px 的黑影，就是這一頁最
    /// 像「貼上去的 UI」的東西。深色的墨反過來：它比紙暗，不必借任何東西就看得
    /// 見，而且那正是「印在紙上」的樣子。
    /// </remarks>
    private static readonly Color ScoreInk = new Color(0.075f, 0.145f, 0.098f, 1f);

    /// <summary>壓印：每個字底下墊一道紙色的高光，數字於是壓進紙裡而不是放在紙上。</summary>
    private static readonly Color ScoreEmboss = new Color(0.97f, 0.945f, 0.865f, 0.85f);

    /// <summary>一格多寬、字多大。</summary>
    /// <remarks>
    /// 這個字型 76px 下的數字實際只有 38（`1`）到 45（`0`）像素寬，所以 62 的格子
    /// 等於每個字旁邊各留 9 像素的空 —— 七位數攤開來就是一條鬆散的帶子，讀起來
    /// 不像一個數字，像七個各自站著的字。
    ///
    /// 48 讓最寬的 `0` 兩側各留一點半的空隙：仍然是等寬（位數變了不會抖），但整
    /// 串收成一塊。
    /// </remarks>
    private const float ScoreDigitCell = 48f;
    private const int ScoreDigitSize = 76;
    private const float ScoreRuleY = -172f;

    private static Color ScoreGreen => ClassicalBookUITheme.ScoreFace;
    private static Color ScoreShadow => ClassicalBookUITheme.ScoreShadow;

    private CanvasGroup canvasGroup;
    private TextMeshProUGUI songTitleText;
    private TextMeshProUGUI authorText;
    private TextMeshProUGUI difficultyText;
    private TextMeshProUGUI rankText;
    private TextMeshProUGUI achievementText;
    private TextMeshProUGUI maxComboText;
    private TextMeshProUGUI recordText;
    private TextMeshProUGUI recordKickerText;
    private TextMeshProUGUI scoreKickerText;
    private ScoreDigitRow scoreDigits;
    private readonly System.Collections.Generic.List<TextMeshProUGUI> statLabelTexts =
        new System.Collections.Generic.List<TextMeshProUGUI>();
    private Image scorePlate;
    private Canvas scoreFrontCanvas;
    private Image coverImage;
    private RawImage coverRawImage;
    private RectTransform coverFrameRect;
    private RectTransform coverShadowRect;
    private RawImage rankBadgeImage;
    private RectTransform bookRect;
    private Coroutine revealRoutine;
    private bool lastResultWasAuto;
    private int lastLocalPlacement;
    private bool scoreHandledForOpenResult;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        if (panel == null) panel = gameObject;
        BuildClassicalBook();
        panel.SetActive(false);
    }

    private void BuildClassicalBook()
    {
        if (panel == null) return;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        if (panelRect == null) panelRect = panel.AddComponent<RectTransform>();
        Stretch(panelRect);

        Canvas frontCanvas = panel.GetComponent<Canvas>();
        if (frontCanvas == null) frontCanvas = panel.AddComponent<Canvas>();
        frontCanvas.overrideSorting = true;
        frontCanvas.sortingOrder = 30000;
        if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();

        Transform oldRuntime = panel.transform.Find(RuntimeRootName);
        if (oldRuntime != null) Destroy(oldRuntime.gameObject);

        // Hide the old one-column layout, but retain serialized references for compatibility.
        for (int i = 0; i < panel.transform.childCount; i++)
        {
            Transform child = panel.transform.GetChild(i);
            if (child != null) child.gameObject.SetActive(false);
        }

        GameObject root = RectObject(RuntimeRootName, panel.transform);
        Stretch(root.GetComponent<RectTransform>());
        canvasGroup = root.AddComponent<CanvasGroup>();

        Image shade = AddImage("Backdrop", root.transform, Backdrop);
        Stretch(shade.rectTransform);

        Image ambient = AddImage("WarmAmbient", root.transform, new Color(0.23f, 0.13f, 0.055f, 0.25f));
        ambient.rectTransform.anchorMin = new Vector2(0.08f, 0.08f);
        ambient.rectTransform.anchorMax = new Vector2(0.92f, 0.92f);
        ambient.rectTransform.offsetMin = Vector2.zero;
        ambient.rectTransform.offsetMax = Vector2.zero;

        // 影子掛在書外面。書板是這本書的第一個子物件，影子要是也在書裡就會
        // 蓋在板子上 —— 陰影畫在自己投影的東西上面，是看得出來的。
        Image bookShadow = AddImage("BookShadow", root.transform, new Color(0f, 0f, 0f, 0.70f));
        SetRect(bookShadow.rectTransform, new Vector2(0.5f, 0.5f),
            new Vector2(BookWidth + 26f, BookHeight + 26f), new Vector2(8f, -14f));

        RectTransform book = RectObject("OpenBook", root.transform).GetComponent<RectTransform>();
        bookRect = book;
        book.anchorMin = book.anchorMax = new Vector2(0.5f, 0.5f);
        book.pivot = new Vector2(0.5f, 0.5f);
        book.sizeDelta = new Vector2(BookWidth, BookHeight);
        book.anchoredPosition = Vector2.zero;

        // 這張圖只是墊在底下的顏色和紋理：BookBoardGraphic.Attach 會把它關掉，
        // 並且把它的貼圖接過去當皮紋。
        Image leather = book.gameObject.AddComponent<Image>();
        leather.color = Leather;
        leather.raycastTarget = false;
        Sprite boardGrain = ClassicalBookUITheme.GetBoardTextureSprite();
        if (boardGrain != null)
        {
            leather.sprite = boardGrain;
            leather.type = Image.Type.Tiled;
        }
        Outline coverOutline = book.gameObject.AddComponent<Outline>();
        coverOutline.effectColor = LeatherEdge;
        coverOutline.effectDistance = new Vector2(5f, -5f);

        // 闔起來的板子：四角圓、書口有弧。矩形的封面是這個畫面最像貼圖的地方。
        BookBoardGraphic boards = BookBoardGraphic.Attach(book, 7);
        if (boards != null)
        {
            boards.Profile = BookShape.Profile.Volume;
            boards.Set(Leather, new Color(0.135f, 0.055f, 0.033f, 1f),
                new Color(0.84f, 0.78f, 0.62f, 1f), Gold);
            if (boardGrain != null) boards.Grain(boardGrain.texture, boardGrain.texture.width);
        }

        AgedPaperGraphic boardWear = AgedPaperGraphic.Attach(book, 7);
        if (boardWear != null)
        {
            boardWear.Shape(BookShape.Profile.Volume);
            // 皮磨到的地方是變亮不是變暗，所以髒色比皮本身淺。
            boardWear.Stain = new Color(0.36f, 0.22f, 0.13f, 1f);
            boardWear.Strength = 0.62f;
            boardWear.EdgeDepth = 0.055f;
            boardWear.Mottle = 0.32f;
            boardWear.Foxing = 0;
        }

        RectTransform leftPage = CreatePage("LeftPage", book, new Vector2(-PageOffset, 0f));
        RectTransform rightPage = CreatePage("RightPage", book, new Vector2(PageOffset, 0f));

        Image spineShadow = AddImage("SpineShadow", book, new Color(0.12f, 0.075f, 0.035f, 0.62f));
        SetRect(spineShadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(42f, PageHeight + 10f), Vector2.zero);
        Image spineLight = AddImage("SpineGold", book, new Color(PaleGold.r, PaleGold.g, PaleGold.b, 0.34f));
        SetRect(spineLight.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(3f, PageHeight), new Vector2(-2f, 0f));

        BuildLeftPage(leftPage);
        BuildRightPage(rightPage);
    }

    /// <summary>
    /// One leaf of the book: real paper, and a field to lay content out on.
    /// </summary>
    /// <remarks>
    /// **Why the paper is four graphics and not a cream rectangle.** A flat fill
    /// with a straight edge reads as a card, every time -- the eye finds the edge
    /// first and the edge says what the thing is. The board gives the leaf a
    /// curved fore-edge and rounded corners, the wear gives it stain and foxing,
    /// the printed rule turns it into a page of something, and the tiled fibre
    /// underneath keeps it from going flat where the other three do not reach.
    /// This is the recipe the score book on the selection screen already uses,
    /// which is exactly what this screen has to look like it came from.
    ///
    /// **Why the content sits in a fixed 715x780 field that is then scaled.**
    /// The book is full screen now, so the paper is a fifth larger than the page
    /// these two layouts were drawn against. Every coordinate on them was placed
    /// by hand and checked for overlap at the old size; re-deriving a few hundred
    /// numbers would buy nothing. The field keeps the drawing and prints it
    /// larger.
    /// </remarks>
    private RectTransform CreatePage(string name, Transform parent, Vector2 position)
    {
        Image page = AddImage(name, parent, Paper);
        SetRect(page.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(PageWidth, PageHeight), position);
        Sprite fibre = ClassicalBookUITheme.GetPaperTextureSprite();
        if (fibre != null)
        {
            page.sprite = fibre;
            page.type = Image.Type.Tiled;
        }
        page.raycastTarget = false;

        // 兩頁各給一個種子。同一本書的兩頁磨得一模一樣，是唯一會讓這個手法露餡
        // 的地方。左頁釘在右邊、右頁釘在左邊：訂口那側平，翻口那側才有弧。
        bool left = name == "LeftPage";
        int seed = left ? 23 : 61;
        BookShape.Profile sheet = left
            ? BookShape.Profile.Page.Mirrored()
            : BookShape.Profile.Page;

        BookBoardGraphic leaf = BookBoardGraphic.Attach(page.rectTransform, seed);
        if (leaf != null)
        {
            leaf.Bound = false;
            leaf.Profile = sheet;
            leaf.Set(Paper, Paper, Paper, Gold);
            if (fibre != null) leaf.Grain(fibre.texture, fibre.texture.width);
        }

        AgedPaperGraphic wear = AgedPaperGraphic.Attach(page.rectTransform, seed, 1);
        if (wear != null)
        {
            wear.Shape(sheet);
            wear.Stain = new Color(0.30f, 0.175f, 0.085f, 1f);
            wear.Strength = 0.88f;
            wear.EdgeDepth = 0.075f;
            wear.Mottle = 0.24f;
            wear.Foxing = 6;
        }

        // 版框。有了它，紙上的東西才是「一頁的內容」而不是浮在紙上的字。
        PageRuleGraphic rule = PageRuleGraphic.Attach(page.rectTransform, seed,
            new Color(0.22f, 0.115f, 0.05f, 0.90f), 0.040f, 4.4f, true, 2);
        if (rule != null)
        {
            rule.Wear = 0.26f;
            rule.Shape(sheet);
        }

        RectTransform field = RectObject("PageField", page.transform).GetComponent<RectTransform>();
        SetRect(field, new Vector2(0.5f, 0.5f), new Vector2(FieldWidth, FieldHeight), Vector2.zero);
        field.localScale = new Vector3(FieldScale, FieldScale, 1f);
        return field;
    }

    private void BuildLeftPage(RectTransform page)
    {
        recordKickerText = AddText("RecordKicker", page, Localize.T(
            "NOSTALGIA  ·  演奏紀錄", "NOSTALGIA  ·  演奏记录", "NOSTALGIA  ·  PERFORMANCE RECORD"), 20f, Gold, FontStyles.SmallCaps);
        SetRect(recordKickerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(610f, 34f), new Vector2(0f, -57f));

        songTitleText = AddText("SongTitle", page, "SONG TITLE", 39f, Ink, FontStyles.Bold);
        SetRect(songTitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(610f, 72f), new Vector2(0f, -112f));
        songTitleText.enableAutoSizing = true;
        songTitleText.fontSizeMin = 24f;
        songTitleText.fontSizeMax = 39f;
        songTitleText.overflowMode = TextOverflowModes.Ellipsis;

        authorText = AddText("Author", page, "— ARTIST —", 21f, MutedInk, FontStyles.Italic);
        SetRect(authorText.rectTransform, new Vector2(0.5f, 1f), new Vector2(610f, 34f), new Vector2(0f, -164f));

        AddRule(page, new Vector2(0f, -198f), 575f);

        Image frameShadow = AddImage("CoverShadow", page, new Color(0f, 0f, 0f, 0.35f));
        SetRect(frameShadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(463f, 463f), new Vector2(8f, -56f));
        Image frame = AddImage("CoverFrame", page, new Color(0.09f, 0.065f, 0.045f, 1f));
        SetRect(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(465f, 465f), new Vector2(0f, -47f));
        coverFrameRect = frame.rectTransform;
        coverShadowRect = frameShadow.rectTransform;
        frame.gameObject.AddComponent<RectMask2D>();
        AddBorder(frame.transform, Gold, 3f, 8f);

        coverImage = AddImage("CoverSprite", frame.transform, Color.white);
        Stretch(coverImage.rectTransform, 13f);
        coverImage.preserveAspect = true;
        coverImage.raycastTarget = false;
        coverRawImage = RectObject("CoverTexture", frame.transform).AddComponent<RawImage>();
        Stretch(coverRawImage.rectTransform, 13f);
        coverRawImage.color = Color.white;
        coverRawImage.raycastTarget = false;
        coverRawImage.gameObject.SetActive(false);

        difficultyText = AddText("Difficulty", page, "NORMAL  ·  Lv. 0", 21f, Gold, FontStyles.Bold);
        SetRect(difficultyText.rectTransform, new Vector2(0.5f, 0f), new Vector2(580f, 38f), new Vector2(0f, 47f));
        AddRule(page, new Vector2(0f, 79f), 575f);
    }

    private void BuildRightPage(RectTransform page)
    {
        // 「SCORE」坐在金線上、對齊數字的左邊緣（見 LayoutScoreBlock）。
        //
        // 以前它獨自浮在右頁的左上角，和它在講的那個數字之間隔著一大片空白 ——
        // 一個標籤離它標的東西那麼遠，就只是另一個飄著的元素。坐到線上之後，
        // 標籤、數字、線是同一個東西的三個部分。
        scoreKickerText = AddText("ScoreKicker", page,
            "SCORE", 17f, Gold, FontStyles.Bold);
        scoreKickerText.alignment = TextAlignmentOptions.MidlineLeft;
        scoreKickerText.characterSpacing = 6f;

        // 底板是透明的：留著這個 rect 只是因為分數的字掛在它裡面。一塊奶油色的
        // 方牌壓在做舊的紙上，就是這個畫面最像「貼上去的 UI」的東西 —— 數字直接
        // 印在紙上就好。
        scorePlate = AddImage("ScorePlate", page, Color.clear);
        SetRect(scorePlate.rectTransform, new Vector2(0.5f, 1f), new Vector2(595f, 82f), new Vector2(0f, -113f));
        // Score digits use the source Font directly instead of TMP's runtime-created
        // dynamic atlas. Some standalone graphics backends produced an empty TMP mesh
        // for this one large line even though the smaller result labels rendered.
        scoreFrontCanvas = null;
        scoreText = null;

        // 一位數一格。
        //
        // 上一版是一行字，帶著三個各自會出事的設定：`FontStyle.Bold`（Zen Antique
        // 沒有粗體字重，Unity 只能把字往外抹，粗細因此不均）、`resizeTextForBestFit`
        // （999,999 和 1,000,000 會印成不同大小）、再加 5 的字距（這個字型的數字
        // 是比例寬度，1 窄 0 寬，加字距等於把不平均的縫拉得更開）。
        //
        // 固定寬度的格子把三個一起解掉：每一位站在自己的格子正中央，所以間距絕
        // 對均勻、位數變了也不會抖，而且將來要做跳字動畫是免費的。
        scoreDigits = RectObject("ScoreDigits", scorePlate.transform)
            .AddComponent<ScoreDigitRow>();
        Stretch(scoreDigits.rectTransform, 0f);
        scoreDigits.Configure(ClassicalBookUITheme.GetScoreSourceFont(), ScoreDigitSize,
            ScoreInk, ScoreEmboss, ScoreDigitCell);
        scoreDigits.SetValue("1000000");

        // 金線收在數字自己的寬度上，而且用的是這一頁本來就在用的那一種（兩端各
        // 一顆菱形）。單純在數字底下畫一條深色的線讀起來是「文字加了底線」；同一
        // 個母題重複出現，整頁才像同一本書裡的東西。
        scoreRule = AddRule(page, new Vector2(0f, ScoreRuleY), scoreDigits.Width);
        LayoutScoreBlock(false);

        rankMedallion = RectObject("RankMedallion", page).GetComponent<RectTransform>();
        RectTransform medal = rankMedallion;
        SetRect(medal, new Vector2(0f, 1f), new Vector2(250f, 290f), new Vector2(165f, -310f));
        rankBadgeImage = RectObject("RankBadge", medal).AddComponent<RawImage>();
        Stretch(rankBadgeImage.rectTransform);
        rankBadgeImage.color = Color.white;
        rankBadgeImage.raycastTarget = false;
        AspectRatioFitter badgeFitter = rankBadgeImage.gameObject.AddComponent<AspectRatioFitter>();
        badgeFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        badgeFitter.aspectRatio = 286f / 445f;
        rankText = AddText("RankFallback", medal, "S", 72f, Gold, FontStyles.Bold);
        Stretch(rankText.rectTransform, 45f);
        rankText.gameObject.SetActive(false);

        achievementText = AddText("Achievement", page, "ACHIEVEMENT  0.00%", 23f, Ink, FontStyles.Bold);
        SetRect(achievementText.rectTransform, new Vector2(1f, 1f), new Vector2(365f, 42f), new Vector2(-224f, -216f));

        RectTransform stats = RectObject("JudgmentTable", page).GetComponent<RectTransform>();
        SetRect(stats, new Vector2(1f, 1f), new Vector2(365f, 298f), new Vector2(-224f, -361f));
        statsRoot = stats;
        statLabelTexts.Clear();
        // 判定的名字不翻譯。JUST、GREAT、PRECISE 是這個遊戲裡判定的**名稱**，
        // 和畫面上跳出來的那幾個字是同一個東西 —— 翻成「精準」「優良」以後，
        // 結算表和遊玩中看到的就對不起來了。
        AddStatRow(stats, "JUST", ref justText, 0, new Color(0.57f, 0.39f, 0.08f, 1f));
        AddStatRow(stats, "GREAT", ref greatText, 1, new Color(0.16f, 0.34f, 0.39f, 1f));
        AddStatRow(stats, "GOOD", ref goodText, 2, MutedInk);
        AddStatRow(stats, "MISS", ref missText, 3, new Color(0.50f, 0.13f, 0.13f, 1f));
        AddStatRow(stats, "FAST / LATE", ref fastText, 4, MutedInk);
        lateText = fastText;
        // 演奏會模式才有的幾列。MULTI-KEY：幾顆音符是被同一幀 3 個以上的鍵按下的
        // （本家的 over3key，扣「旋律」分）。
        AddStatRow(stats, "PRECISE", ref preciseText, 5, new Color(0.40f, 0.22f, 0.55f, 1f));
        preciseLabelText = statLabelTexts[statLabelTexts.Count - 1];
        AddStatRow(stats, "MULTI-KEY", ref centreText, 6, new Color(0.36f, 0.24f, 0.46f, 1f));
        centreLabelText = statLabelTexts[statLabelTexts.Count - 1];
        AddStatRow(stats, "MISTOUCH", ref mistouchText, 7, new Color(0.52f, 0.16f, 0.14f, 1f));
        mistouchLabelText = statLabelTexts[statLabelTexts.Count - 1];
        // 這兩列寫的是「數量 / 總數 (百分比)」，比其他列長得多。AddText 一律
        // NoWrap + Truncate，固定字級塞不下就從**尾巴**切 —— 切掉的正好是百分比，
        // 也就是這一列唯一有意義的部分。讓字級自己縮。
        // 這三列的數字都是「幾分之幾 (百分比)」，比其他列長得多，所以都要能縮。
        //
        // PRECISE 以前只印一個數字，不需要縮 —— 加上分母之後它跟另外兩列一樣長
        // 了，而漏掉的那一列不會變小，它會**溢出去壓在自己的標籤上**。
        //
        // 這種漏掉很難在改的當下發現：改的是「印什麼」，壞的是「怎麼排」，而兩
        // 件事寫在不同的地方。
        FitValue(preciseText);
        FitValue(centreText);
        FitValue(mistouchText);

        bottomRule = AddRule(page, new Vector2(0f, -504f), 575f);
        maxComboText = AddText("MaxCombo", page, "MAX COMBO   0", 29f, Ink, FontStyles.Bold);
        SetRect(maxComboText.rectTransform, new Vector2(0.5f, 0f), new Vector2(570f, 48f), new Vector2(0f, 205f));
        recordText = AddText("RecordMessage", page, "PERFORMANCE COMPLETE", 18f, Gold, FontStyles.Italic);
        SetRect(recordText.rectTransform, new Vector2(0.5f, 0f), new Vector2(570f, 34f), new Vector2(0f, 165f));

        BuildAssessment(page);

        closeButton = CreateButton("Close", page,
            Localize.T("返回選曲", "返回选曲", "Back to Song Select"), new Vector2(0f, 76f));
        closeButton.onClick.AddListener(OnClosePressed);
    }

    /// <summary>
    /// Fills the centre-lane row, or hides it outside recital mode.
    /// </summary>
    /// <remarks>
    /// Hidden rather than zeroed: in normal play every lane of a note counts the
    /// same, so a "centre 0 / 0" line would be reporting on a rule that was not
    /// in force.
    /// </remarks>
    /// <summary>Fills the examiner's panel, or puts the page back to normal.</summary>
    private void SetAssessment(Judgment.RecitalAssessment.Report report)
    {
        ApplyRecitalLayout(report.measured);
        if (!report.measured) return;

        float[] marks = { report.completeness, report.melody, report.mistakes, report.expression };
        for (int i = 0; i < marks.Length; i++)
        {
            float mark = Mathf.Clamp(marks[i], 0f, 100f);
            if (criterionValues[i] != null) criterionValues[i].text = $"{mark:0}";
            // 沒到門檻的那幾項自己說出來：金色是達標，暗紅是還沒。
            if (criterionBars[i] != null)
                criterionBars[i].Set(mark * 0.01f,
                    mark >= Judgment.RecitalAssessment.PassMark
                        ? Gold
                        : new Color(0.55f, 0.22f, 0.14f, 1f));
        }

        if (averageText != null)
        {
            averageText.text = Localize.T($"平均  {report.average:0.0}", $"平均  {report.average:0.0}",
                $"AVERAGE  {report.average:0.0}");
            ClassicalBookUITheme.ApplyLocalizedFont(averageText);
        }

        if (gradeText == null) return;
        string grade = report.Grade;
        bool passed = !string.IsNullOrEmpty(grade);
        gradeText.text = passed ? grade : Localize.T("未通過", "未通过", "NOT PASSED");

        // 等第和這一頁其他文字同一支字，差別只在顏色。展示體試過了：它把等第
        // 從「這一頁的一部分」變成「貼在這一頁上的東西」，而且沒有中文字符，
        // 未通過那一行還得走另一條路 —— 為了四個字養兩套規則不划算。
        ClassicalBookUITheme.ApplyLocalizedFont(gradeText);
        // 等第由**顏色**表示。四個都比紙暗，所以在米色上都讀得到。
        gradeText.color =
            report.average >= Judgment.RecitalAssessment.DistinctionMark ? DistinctionInk
            : report.average >= Judgment.RecitalAssessment.MeritMark ? MeritInk
            : passed ? PassInk
            : FailedInk;
    }

    private void SetRecitalRow((int notes, int over3Key, int mistouch, int precise) recital,
        (int earned, int total) precise)
    {
        int total = recital.notes;
        bool show = total > 0;
        foreach (TextMeshProUGUI text in new[]
                 { centreText, centreLabelText, mistouchText, mistouchLabelText,
                   preciseText, preciseLabelText })
            if (text != null) text.gameObject.SetActive(show);
        if (!show) return;

        // PRECISE 也要有分母。旁邊兩列都是「幾分之幾」，只有它是一個孤零零的
        // 數字 —— 而那個數字在 40 顆和 1400 顆的譜上意思完全不同。
        //
        // 分母不是音符總數，是**有機會拿到的那些**：連音和滑音不在裡面，因為它
        // 們本來就不評這件事。拿全部音符當分母會讓一場完美的演奏印出七成。
        if (preciseText != null)
        {
            if (precise.total > 0)
            {
                float share = precise.earned / (float)precise.total;
                preciseText.text =
                    $"{precise.earned:N0}  /  {precise.total:N0}   ({share * 100f:0.0}%)";
            }
            else
            {
                preciseText.text = $"{recital.precise:N0}";
            }
        }
        if (centreText != null)
        {
            float share = recital.over3Key / (float)total;
            centreText.text = $"{recital.over3Key:N0}  /  {total:N0}   ({share * 100f:0.0}%)";
        }
        // 誤觸要對著音符數看才有意義：同樣三次誤觸，在 90 顆的譜上和 1400 顆的
        // 譜上不是同一件事。零也要寫出來 —— 那一局的零是成績，不是沒有資料。
        if (mistouchText != null)
        {
            float rate = recital.mistouch / (float)total;
            mistouchText.text = $"{recital.mistouch:N0}  /  {total:N0}   ({rate * 100f:0.0}%)";
        }
    }

    /// <summary>
    /// The examiner's report: four criteria out of a hundred, then the verdict.
    /// </summary>
    /// <remarks>
    /// **Why it takes the top of the page.** In recital mode this *is* the
    /// result. The seven-digit score is the game's own measure and it stays, but
    /// it goes to the bottom corner where a receipt belongs; what a player came
    /// to find out is which of the four things they need to work on.
    ///
    /// **Why each criterion gets a bar.** Four numbers between 0 and 100 in a
    /// column are read as a list to be added up. A bar is read as a level, and
    /// a row of levels is compared at a glance -- which is the only operation
    /// anybody performs on this panel.
    /// </remarks>
    private void BuildAssessment(RectTransform page)
    {
        // 這一頁是 715 x 780。SetRect 一律把 pivot 放在正中央，所以第三個參數
        // 給的是**中心**的位置，不是上緣 —— 第一版把它當上緣用，面板有一半跑到
        // 紙的外面去了。上緣要在 -42，高 340，中心就是 -212。
        assessmentRoot = RectObject("Assessment", page).GetComponent<RectTransform>();
        SetRect(assessmentRoot, new Vector2(0.5f, 1f), new Vector2(600f, 310f), new Vector2(0f, -197f));

        // 標題。沒有它的話這一區就是四條沒頭沒尾的橫槓 —— 讀者得自己猜這四個
        // 數字是誰打的分數。有了抬頭，下面四列才是「評語」而不是四個統計。
        TextMeshProUGUI heading = AddText("AssessmentTitle", assessmentRoot,
            Localize.T("評審講評", "评审讲评", "EXAMINER'S REPORT"), 22f, Gold, FontStyles.Bold);
        heading.alignment = TextAlignmentOptions.Center;
        SetRect(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(560f, 36f),
            new Vector2(0f, -20f));
        Image headingRule = AddImage("AssessmentTitleRule", assessmentRoot,
            new Color(Gold.r, Gold.g, Gold.b, 0.55f));
        SetRect(headingRule.rectTransform, new Vector2(0.5f, 1f), new Vector2(330f, 5f),
            new Vector2(0f, -44f));
        // 每個框的高度都給到字級的 1.6 倍以上，見 AddText 裡關於 Truncate 的說明。

        string[] names =
        {
            Localize.T("完整度", "完整度", "COMPLETENESS"),
            Localize.T("旋律（不拍鍵）", "旋律（不拍键）", "MELODY"),
            Localize.T("失誤錯音", "失误错音", "CLEANNESS"),
            Localize.T("情感與表情", "情感与表情", "EXPRESSION"),
        };

        for (int i = 0; i < names.Length; i++)
        {
            float y = -68f - i * 44f;
            TextMeshProUGUI label = AddText("Criterion" + i, assessmentRoot, names[i], 19f, Ink,
                FontStyles.Bold);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(250f, 31f), new Vector2(140f, y));
            statLabelTexts.Add(label);

            // 刻度尺，不是進度條：尺身和刻度是墨色印上去的，成績是金色填進去的。
            // 一格色塊套在另一格色塊裡，是這一頁唯一一個只可能來自軟體的形狀。
            // 整條尺是一張網格自己畫的，所以也不再有「SetRect 會把 pivot 蓋掉」
            // 那個坑 —— 填到哪裡是畫出來的，不是靠改 rect 的寬度。
            var bar = RectObject("CriterionBar" + i, assessmentRoot)
                .AddComponent<CriterionBarGraphic>();
            bar.raycastTarget = false;
            bar.Engrave(new Color(0.24f, 0.17f, 0.09f, 1f), 3 + i * 13);
            SetRect(bar.rectTransform, new Vector2(0f, 1f), new Vector2(CriterionBarWidth, 17f),
                new Vector2(CriterionBarLeft + CriterionBarWidth * 0.5f, y - 1f));
            criterionBars[i] = bar;

            TextMeshProUGUI value = AddText("CriterionValue" + i, assessmentRoot, "0", 22f, Ink,
                FontStyles.Bold);
            value.alignment = TextAlignmentOptions.MidlineRight;
            // 數字擺在條子右邊，不是壓在條子上，而且要留在面板寬度（600）裡：
            // 原本 96 寬的數字擺在 588，右緣是 636，整個掉出紙的版框外面。
            SetRect(value.rectTransform, new Vector2(0f, 1f), new Vector2(96f, 36f),
                new Vector2(CriterionMarkX, y));
            criterionValues[i] = value;
        }

        // 平均和等第排成一列，不是上下兩行：這一行是**結論**，橫著讀是
        // 「平均 82.4，所以 MERIT」。分成兩行的話，兩個數字看起來像又兩項成績。
        Image verdictRule = AddImage("VerdictRule", assessmentRoot,
            new Color(Gold.r, Gold.g, Gold.b, 0.42f));
        SetRect(verdictRule.rectTransform, new Vector2(0.5f, 1f), new Vector2(520f, 4f),
            new Vector2(0f, -224f));

        averageText = AddText("Average", assessmentRoot, "AVERAGE  0.0", 19f, MutedInk,
            FontStyles.Bold);
        averageText.alignment = TextAlignmentOptions.MidlineLeft;
        SetRect(averageText.rectTransform, new Vector2(0f, 1f), new Vector2(220f, 31f),
            new Vector2(150f, -264f));

        gradeText = AddText("Grade", assessmentRoot, "PASS", 28f, Ink, FontStyles.Bold);
        gradeText.alignment = TextAlignmentOptions.Center;
        SetRect(gradeText.rectTransform, new Vector2(1f, 1f), new Vector2(300f, 64f),
            new Vector2(-170f, -264f));

        assessmentRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// Moves the page between its two layouts.
    /// </summary>
    /// <remarks>
    /// The same objects, re-anchored, rather than a second page built in
    /// parallel: everything on it -- the score plate, the tallies, the rank --
    /// means the same thing in both modes and is filled in by the same code.
    /// Only where they sit changes, because in recital mode something else has
    /// the top of the page.
    /// </remarks>
    private void ApplyRecitalLayout(bool recital)
    {
        if (assessmentRoot != null) assessmentRoot.gameObject.SetActive(recital);

        // 一頁 715 x 780。演奏會模式由上往下：
        //   評審面板  y  42..352（滿版）
        //   判定表    y 360..658（靠右，x 342..707）
        //   左欄      x  25..315：MAX COMBO、分數小標、分數牌、紀錄訊息
        //   關閉鍵    y 673..735（沒動）
        //
        // MAX COMBO 和最下面那行紀錄訊息是錨在頁面**底部**的。判定表往下移之後
        // 正好壓在它們身上 —— 那才是整頁爆掉的原因，不是表格自己的位置。它們
        // 兩個一起搬到左欄，那塊本來是勳章的位置，而勳章在這個模式裡是收起來的。
        // 判定表有八列，內容從面板頂端算下去 27..346 —— 比它自己的 298 高。
        // 排這一頁的時候要照**內容**的範圍算，照 rect 算就會壓到關閉鍵。
        if (statsRoot != null)
            SetRect(statsRoot, new Vector2(1f, 1f), new Vector2(365f, 298f),
                recital ? new Vector2(-224f, -474f) : new Vector2(-224f, -361f));
        if (scorePlate != null)
            SetRect(scorePlate.rectTransform,
                recital ? new Vector2(0f, 1f) : new Vector2(0.5f, 1f),
                recital ? new Vector2(260f, 72f) : new Vector2(595f, 82f),
                recital ? new Vector2(170f, -532f) : new Vector2(0f, -113f));
        // 數字、金線、標籤三個一起排。分開排的話，改其中一個的位置就要記得另外
        // 兩個也要跟著改 —— 那種「記得」遲早會忘。
        LayoutScoreBlock(recital);
        if (maxComboText != null)
        {
            SetRect(maxComboText.rectTransform,
                recital ? new Vector2(0f, 1f) : new Vector2(0.5f, 0f),
                recital ? new Vector2(260f, 36f) : new Vector2(570f, 48f),
                recital ? new Vector2(170f, -398f) : new Vector2(0f, 205f));
            maxComboText.fontSize = recital ? 22f : 29f;
        }
        if (recordText != null)
        {
            SetRect(recordText.rectTransform,
                recital ? new Vector2(0f, 1f) : new Vector2(0.5f, 0f),
                recital ? new Vector2(260f, 34f) : new Vector2(570f, 34f),
                recital ? new Vector2(170f, -602f) : new Vector2(0f, 165f));
            recordText.fontSize = recital ? 15f : 18f;
        }
        // 上面那條金線改當評審面板的收尾；下面那條在這個排法裡沒有位置站，收起來。
        // 演奏會版的評審面板自己已經有抬頭線和結論線，再來一條橫貫全頁的金線
        // 就只是把版面切碎。
        // 線的顯示與否在這裡決定，長度和位置歸 LayoutScoreBlock。
        if (bottomRule != null) bottomRule.gameObject.SetActive(!recital);
        // 八列的判定表把整頁往下推，返回鍵得再低一點才不會被追上。
        if (closeButton != null)
            SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(290f, 62f), recital ? new Vector2(0f, 66f) : new Vector2(0f, 76f));
        // 兩個版本要一眼分得出來，不能只差在東西擺哪裡。同一本書、同一種紙，
        // 但這一場是評鑑不是練習 —— 抬頭先把話講明白。
        if (recordKickerText != null)
            recordKickerText.text = recital
                ? Localize.T("NOSTALGIA  ·  演奏會評鑑", "NOSTALGIA  ·  演奏会评鉴",
                    "NOSTALGIA  ·  RECITAL ASSESSMENT")
                : Localize.T("NOSTALGIA  ·  演奏紀錄", "NOSTALGIA  ·  演奏记录",
                    "NOSTALGIA  ·  PERFORMANCE RECORD");
        // 勳章和達成率讓位：評級就是演奏會模式的等第，兩個放在一起只會打架。
        if (rankMedallion != null) rankMedallion.gameObject.SetActive(!recital);
        if (achievementText != null)
            achievementText.gameObject.SetActive(!recital);
    }

    /// <summary>Lets a value shrink to fit its box instead of losing its tail.</summary>
    private static void FitValue(TextMeshProUGUI text)
    {
        if (text == null) return;
        text.enableAutoSizing = true;
        // 下限從 13 提到 16：低於這個就不是「小一點」，是「讀不到」。寧可讓極端
        // 的長度去撞版面被發現，也不要讓它安靜地縮成看不見。
        text.fontSizeMin = 16f;
        text.fontSizeMax = 23f;
    }

    private void AddStatRow(RectTransform parent, string label, ref TextMeshProUGUI valueText, int index, Color color)
    {
        // 演奏會模式會用到八列。48 的間距塞不下，40 還是比字高（36）寬。
        float y = 104f - index * 40f;
        TextMeshProUGUI labelText = AddText(label + "Label", parent, label, 21f, color, FontStyles.Bold);
        statLabelTexts.Add(labelText);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        // 這一整列住在一個**只有 365 寬**的框裡（JudgmentTable）。兩個欄位的
        // 寬度加上各自的邊距不能超過它，而 SetRect 的第三個參數是**中心**的位
        // 移 —— 偏移給小了，框就會從另一邊凸出去。上一版把值加寬到 215 卻只留
        // 62 的偏移，右緣直接超出 45px，那就是版面爆掉的原因。
        //
        //   標籤  10 .. 162      （寬 152，中心 86）
        //   值   169 .. 361      （寬 192，中心 365-100=265）
        //
        // 中間留 7px，右邊留 4px。
        SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(152f, 36f), new Vector2(86f, y));
        valueText = AddText(label + "Value", parent, "0", 23f, Ink, FontStyles.Bold);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        // 自動縮放是**保險**不是版面：靠它把「1,304 / 2,278 (57.2%)」擠進 160px
        // 的話，字級會掉到下限，那一列就變成整頁最難讀的東西。先把空間給夠，
        // 縮放才回到它該有的角色 —— 只在極端情況下才作用。
        SetRect(valueText.rectTransform, new Vector2(1f, 0.5f), new Vector2(192f, 36f), new Vector2(-100f, y));
        Image line = AddImage(label + "Rule", parent, new Color(Gold.r, Gold.g, Gold.b, 0.22f));
        SetRect(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(360f, 1f), new Vector2(0f, y - 21f));
    }

    private Button CreateButton(string name, Transform parent, string label, Vector2 position)
    {
        // 一塊深色皮革方塊壓在做舊的紙上，是整頁唯一不屬於這本書的東西 —— 它
        // 不是「按鈕長得不好看」，是它根本不在同一個材質世界裡。改成印在紙上的
        // 一個框：用畫頁面版框的同一支筆，磨損、抖動都和旁邊的線同一套。
        //
        // 底圖留著而且不是全透明：Button 的 highlighted/pressed 是**乘**在
        // targetGraphic 的顏色上的，全透明就沒有任何回饋可乘。
        Image image = AddImage(name, parent, new Color(0.42f, 0.27f, 0.11f, 0.10f));
        SetRect(image.rectTransform, new Vector2(0.5f, 0f), new Vector2(290f, 62f), position);
        PageRuleGraphic rule = PageRuleGraphic.Attach(image.rectTransform, 41,
            new Color(Gold.r, Gold.g, Gold.b, 0.88f), 0.055f, 2.6f, true, 0);
        if (rule != null) rule.Wear = 0.20f;
        Button button = image.gameObject.AddComponent<Button>();
        image.raycastTarget = true;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        // 底圖很淡，倍率要拉開一點才看得出來。
        colors.highlightedColor = new Color(2.1f, 1.85f, 1.35f, 1.9f);
        colors.pressedColor = new Color(1.4f, 1.15f, 0.80f, 2.6f);
        button.colors = colors;
        TextMeshProUGUI text = AddText("Label", image.transform, label, 22f, Ink, FontStyles.Bold);
        Stretch(text.rectTransform, 3f);
        return button;
    }

    private void OnClosePressed()
    {
        Hide();
        SetGameplayHudVisible(true);
        GameManager.Instance?.ReselectSong();
    }

    /// <summary>
    /// 結算開著的時候，把遊玩畫面的 HUD 收起來。
    /// </summary>
    /// <remarks>
    /// COMBO 和分數是**遊玩中**的東西：它們存在的理由是讓玩家在打的時候知道自己
    /// 的狀況。歌結束、結算攤開之後，同一組數字在結算頁上已經有完整的位置（最大
    /// 連擊、分數各有自己的欄），HUD 那一份就只是壓在書頁上的另一層字 —— 而且它
    /// 錨在畫面角落，會跑到書頁外面去。
    ///
    /// `SetClassicalHudVisible` 本來就在那裡，只是從來沒有人從外面叫過它。
    /// </remarks>
    private static void SetGameplayHudVisible(bool visible)
    {
        try
        {
            Judgment.JudgmentManager judge = Judgment.JudgmentManager.Instance;
            if (judge != null) judge.SetClassicalHudVisible(visible);
        }
        catch { }
    }

    private void RefreshLocalization()
    {
        if (recordKickerText != null) recordKickerText.text = Localize.T(
            "NOSTALGIA  ·  演奏紀錄", "NOSTALGIA  ·  演奏记录", "NOSTALGIA  ·  PERFORMANCE RECORD");
        // 標題不翻譯，和判定名一樣的理由：這是數字的名字，不是一句話。
        if (scoreKickerText != null) scoreKickerText.text = "SCORE";

        // 判定的名字不翻譯，而且是**永遠**不翻譯。JUST、GREAT、PRECISE 是這個
        // 遊戲裡判定的名稱，和遊玩中跳出來的那幾個字是同一個東西 —— 翻成「精準」
        // 「優良」以後，結算表和畫面上看到的就對不起來了。這裡本來有一段迴圈，
        // 在 AddStatRow 已經寫好英文名之後又照語系蓋回中文，整張表就這樣被翻掉。

        if (closeButton != null)
        {
            TextMeshProUGUI label = closeButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = Localize.T("返回選曲", "返回选曲", "Back to Song Select");
                ClassicalBookUITheme.ApplyLocalizedFont(label);
            }
        }
        // 整頁掃一次，而不是逐一列出要套哪幾個。理由有兩個：
        //   一是字型是照**當下的字串內容**決定的，這個方法上面剛把十幾個欄位
        //     重寫過，建立時套的那一次對它們已經失效；
        //   二是漏掉一個的代價是那一行整段隱形（不是換個字型而已），而「哪些欄位
        //     有中文」會隨翻譯改變 —— 這種清單一定會過期。
        foreach (TextMeshProUGUI text in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            ClassicalBookUITheme.ApplyLocalizedFont(text);
    }

    public void ShowResults()
    {
        if (panel == null) return;
        GameplayEntryPresentation.EndGameplay();
        // 遊玩中的 COMBO 和分數收起來：那兩個數字在這一頁上各自有欄位，HUD 那一
        // 份只會壓在書頁上，而且它錨在畫面角落，會跑到書頁外面去。
        SetGameplayHudVisible(false);
        RefreshLocalization();
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        FitBookToScreen();

        Judgment.JudgmentManager jm = Judgment.JudgmentManager.Instance;
        if (jm != null)
        {
            try { jm.FlushPendingJudgments(); } catch { }
        }

        StatsManager statsManager = StatsManager.Instance;
        if (statsManager != null)
        {
            var stats = statsManager.GetStatsWithCombo();
            var timing = statsManager.GetTimingStats();
            lastResultWasAuto = jm != null
                ? jm.AutoPlayUsedThisRun
                : SettingsManager.Instance != null && SettingsManager.Instance.DebugModeInPlay;
            SongSelectionManager.SongOption selectedSong =
                SongSelectionManager.Instance?.GetSelectedSong();
            if (!scoreHandledForOpenResult)
            {
                // 練習模式和自動演奏一樣不進榜：速度可以改、背景音樂關著，這一局
                // 的分數和其他人的分數不是同一件事。
                bool practising = SettingsManager.Instance != null
                    && SettingsManager.Instance.PracticeMode;
                lastLocalPlacement = lastResultWasAuto || practising
                    ? 0
                    : LocalScoreRecords.RecordManualScore(selectedSong, stats.score);
                scoreHandledForOpenResult = true;
            }
            SetResults(stats.perfect, stats.great, stats.good, stats.miss, stats.fail,
                stats.maxCombo, stats.score, timing.fast, timing.late);
            var recital = statsManager.GetRecitalStats();
            SetRecitalRow(recital, statsManager.GetRecitalPreciseStats());
            var pedal = statsManager.GetRecitalPedalStats();
            var dynamics = statsManager.GetRecitalDynamicStats();
            var recitalChart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
            var recitalNotes = recitalChart != null ? recitalChart.notes : null;
            float melodyAllowance = Judgment.RecitalAssessment.MelodyAllowance(recitalNotes);
            float mistouchAllowance = Judgment.RecitalAssessment.MistouchAllowance(recitalNotes);
            SetAssessment(Judgment.RecitalAssessment.Evaluate(stats.score, recital.notes,
                recital.over3Key, melodyAllowance, recital.mistouch, mistouchAllowance,
                pedal.correct, pedal.total,
                dynamics.correct, dynamics.total));
            var rawTiming = statsManager.GetRawTimingOffsetStats();
            var inputTiming = jm != null
                ? jm.GetInputTimingStats()
                : (timed: 0, fallback: 0, averageMs: 0f, minMs: 0f, maxMs: 0f);
            // Keep this one compact line in release Player.log.  BuildLogger.Log is
            // intentionally stripped from release builds, which made real-device timing
            // regressions impossible to distinguish from chart/audio offsets.
            // avg alone cannot tell a shifted distribution from a centred one with a
            // heavy early tail (mistouches can only ever pull a sample earlier, never
            // later). median is robust to that tail; shape shows it directly.
            Debug.Log($"[Timing Result] score={stats.score:N0}, FAST/SLOW={timing.fast}/{timing.late}, " +
                $"samples={rawTiming.count}, avg={rawTiming.averageMs:+0.0;-0.0;0.0}ms, " +
                $"median={statsManager.GetRawTimingOffsetMedianMs():+0.0;-0.0;0.0}ms, " +
                $"shape[-150..150/25]={statsManager.GetRawTimingOffsetShape()}, " +
                $"range={rawTiming.minMs:+0.0;-0.0;0.0}..{rawTiming.maxMs:+0.0;-0.0;0.0}ms, " +
                $"input={inputTiming.timed}/{inputTiming.fallback} " +
                $"projection={inputTiming.averageMs:+0.00;-0.00;0.00}ms " +
                Judgment.EarlyClaimCorrector.Format());
        }
        else
        {
            lastResultWasAuto = false;
            lastLocalPlacement = 0;
            BuildLogger.LogWarning("ResultsScreen: StatsManager not found");
            SetResults(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        if (scorePlate != null)
        {
            scorePlate.gameObject.SetActive(true);
            scorePlate.transform.SetAsLastSibling();
        }
        if (scoreText != null)
        {
            scoreText.gameObject.SetActive(true);
            scoreText.enabled = true;
            scoreText.color = Ink;
            scoreText.alpha = 1f;
            scoreText.transform.SetAsLastSibling();
            scoreText.ForceMeshUpdate(true, true);
            scoreText.canvasRenderer.SetAlpha(1f);
            if (scoreFrontCanvas != null)
            {
                scoreFrontCanvas.overrideSorting = true;
                scoreFrontCanvas.sortingOrder = 30020;
                scoreFrontCanvas.enabled = true;
            }
        }
        if (scoreDigits != null)
        {
            scoreDigits.gameObject.SetActive(true);
            scoreDigits.transform.SetAsLastSibling();
        }

        UpdateSongPresentation();
        if (revealRoutine != null) StopCoroutine(revealRoutine);
        revealRoutine = StartCoroutine(Reveal());
    }

    private void SetResults(int just, int great, int good, int miss, int fail, int maxCombo, int score, int fast, int late)
    {
        int totalMiss = miss + fail;
        if (justText != null) justText.text = just.ToString("N0");
        if (greatText != null) greatText.text = great.ToString("N0");
        if (goodText != null) goodText.text = good.ToString("N0");
        if (missText != null) missText.text = totalMiss.ToString("N0");
        if (fastText != null) fastText.text = $"{fast:N0}  /  {late:N0}";
        // 沒有千分位。逗號是試算表的寫法，而這一頁是一本書；等寬的格子已經把
        // 位數分得很清楚，逗號只是在中間插兩個不是數字的東西。
        string formattedScore = Mathf.Clamp(score, 0, 1000000).ToString("0000000");
        if (scoreText != null) scoreText.text = formattedScore;
        if (scoreDigits != null) scoreDigits.SetValue(formattedScore);
        if (maxComboText != null)
        {
            maxComboText.text = Localize.T(
                $"最大連擊   {maxCombo:N0}", $"最大连击   {maxCombo:N0}", $"MAX COMBO   {maxCombo:N0}");
            ClassicalBookUITheme.ApplyLocalizedFont(maxComboText);
        }

        float achievement = Mathf.Clamp01(score / 1000000f) * 100f;
        string rank = LocalScoreRecords.GetRank(score);
        if (rankText != null)
        {
            rankText.text = rank;
            rankText.color = rank == "P" ? Color.white
                : rank == "S" ? Gold
                : rank.StartsWith("A") ? new Color(0.72f, 0.10f, 0.08f)
                : rank.StartsWith("B") ? new Color(0.68f, 0.70f, 0.73f)
                : new Color(0.61f, 0.31f, 0.14f);
        }
        SetRankBadge(rank);
        if (achievementText != null) achievementText.text = Localize.T(
            $"達成率  {achievement:0.00}%", $"达成率  {achievement:0.00}%", $"ACHIEVEMENT  {achievement:0.00}%");

        int judged = just + great + good + totalMiss;
        if (recordText != null)
        {
            recordText.text = judged > 0 && totalMiss == 0
                ? Localize.T("全連擊  ·  完美演奏", "全连击  ·  完美演奏", "FULL COMBO  ·  FLAWLESS PERFORMANCE")
                : Localize.T("演奏完成", "演奏完成", "PERFORMANCE COMPLETE");
            if (lastResultWasAuto)
            {
                recordText.text = Localize.T("自動演奏  ·  不記錄分數", "自动演奏  ·  不记录分数", "AUTO PLAY   ·   SCORE NOT RECORDED");
            }
            else if (lastLocalPlacement > 0)
            {
                string fullCombo = judged > 0 && totalMiss == 0
                    ? Localize.T("   ·   全連擊", "   ·   全连击", "   ·   FULL COMBO")
                    : string.Empty;
                recordText.text = Localize.T(
                    $"本機前三名新紀錄   ·   #{lastLocalPlacement}{fullCombo}",
                    $"本机前三名新纪录   ·   #{lastLocalPlacement}{fullCombo}",
                    $"NEW LOCAL TOP 3   ·   #{lastLocalPlacement}{fullCombo}");
            }
        }
    }

    private void UpdateSongPresentation()
    {
        SongSelectionManager.SongOption song = SongSelectionManager.Instance != null
            ? SongSelectionManager.Instance.GetSelectedSong()
            : null;

        if (songTitleText != null)
            songTitleText.text = song != null && !string.IsNullOrWhiteSpace(song.displayName) ? song.displayName
                : Localize.T("未知曲目", "未知曲目", "UNKNOWN PIECE");
        if (authorText != null)
            authorText.text = song != null && !string.IsNullOrWhiteSpace(song.author) ? $"— {song.author} —"
                : Localize.T("— 未知作者 —", "— 未知作者 —", "— UNKNOWN ARTIST —");
        if (difficultyText != null)
        {
            string difficulty = song != null && !string.IsNullOrWhiteSpace(song.difficultyName) ? song.difficultyName.ToUpperInvariant() : "NORMAL";
            int level = song != null ? song.difficultyLevel : 0;
            difficultyText.text = $"{difficulty}   ·   Lv. {level}";
            // 難度在選歌畫面就有自己的顏色，結算沿用同一份配色 —— 玩家是照顏色
            // 認難度的，到了這一頁全變成金色就等於把那個資訊丟掉。
            string name = song != null ? song.difficultyName : null;
            difficultyText.color = ClassicalBookUITheme.GetDifficultyColour(name, level);
        }
        ClassicalBookUITheme.ApplyContentFont(songTitleText);
        ClassicalBookUITheme.ApplyContentFont(authorText);
        ClassicalBookUITheme.ApplyContentFont(difficultyText);

        Sprite sprite = song != null ? song.coverSprite : null;
        Texture texture = null;
        GameBackgroundManager background = FindFirstObjectByType<GameBackgroundManager>();
        if (background != null && background.songImage != null) texture = background.songImage.texture;

        if (sprite != null)
        {
            coverImage.sprite = sprite;
            coverImage.color = Color.white;
            coverImage.gameObject.SetActive(true);
            coverRawImage.gameObject.SetActive(false);
            ApplyCoverAspect(sprite.rect.width, sprite.rect.height);
        }
        else if (texture != null)
        {
            ApplyCoverAspect(texture.width, texture.height);
            coverRawImage.texture = texture;
            coverRawImage.gameObject.SetActive(true);
            coverImage.gameObject.SetActive(false);
            AspectRatioFitter fitter = coverRawImage.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = coverRawImage.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        }
        else
        {
            // Nothing to shape the frame around, so it goes back to the square
            // it would have been — otherwise it keeps the last song's shape.
            ApplyCoverAspect(1f, 1f);
            coverImage.sprite = null;
            coverImage.color = new Color(0.15f, 0.12f, 0.09f, 1f);
            coverImage.gameObject.SetActive(true);
            coverRawImage.gameObject.SetActive(false);
        }
    }

    private void SetRankBadge(string rank)
    {
        if (rankBadgeImage == null) return;
        Texture2D badge = LocalScoreRecords.LoadRankTexture(rank);
        rankBadgeImage.texture = badge;
        rankBadgeImage.gameObject.SetActive(badge != null);
        if (rankText != null) rankText.gameObject.SetActive(badge == null);
    }

    private IEnumerator Reveal()
    {
        if (canvasGroup == null) yield break;
        canvasGroup.alpha = 0f;
        float elapsed = 0f;
        const float duration = 0.32f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = 1f - Mathf.Pow(1f - t, 3f);
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    public void Hide()
    {
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }
        if (panel != null) panel.SetActive(false);
        scoreHandledForOpenResult = false;
    }

    private void LateUpdate()
    {
        if (panel != null && panel.activeInHierarchy) FitBookToScreen();
    }

    private void FitBookToScreen()
    {
        if (bookRect == null || panel == null) return;
        RectTransform viewport = panel.GetComponent<RectTransform>();
        if (viewport == null || viewport.rect.width <= 1f || viewport.rect.height <= 1f) return;
        float scale = Mathf.Min(1f, viewport.rect.width / 1580f, viewport.rect.height / 900f);
        // Do not force a 0.55 minimum: at 1366x768 and smaller aspect ratios that
        // made the book wider than its viewport and clipped the far-left artwork/text.
        bookRect.localScale = Vector3.one * Mathf.Max(0.1f, scale);
    }

    private static GameObject RectObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image AddImage(string name, Transform parent, Color color)
    {
        Image image = RectObject(name, parent).AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI AddText(string name, Transform parent, string value, float size, Color color, FontStyles style)
    {
        TextMeshProUGUI text = RectObject(name, parent).AddComponent<TextMeshProUGUI>();
        text.text = value;
        ClassicalBookUITheme.StyleText(text, color, size, style);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        // Truncate 在 TMP 裡不只是「切掉超出的部分」：文字**垂直**塞不進 rect 的
        // 時候，它是整段不畫。框高只要小於字級的約 1.5 倍（TMP 的行高倍率）就會
        // 觸發，而且畫面上什麼都沒有 —— 看起來像元素沒被建立，或是跑到畫面外，
        // 於是就往座標和字型去找。SCORE、評審講評、四個項目名、四個分數全都是
        // 這樣消失的，它們的框高/字級分別是 1.53、1.23、1.43、1.28。
        //
        // 換成 Overflow：塞不下就讓它凸出去。凸出去是**看得見**的錯，量一下就知道
        // 要改哪裡；消失是看不見的錯，得靠猜。
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.richText = true;
        return text;
    }

    /// <summary>A gold rule with a lozenge at each end, as one movable object.</summary>
    /// <remarks>
    /// The three pieces used to be parented straight onto the page, each with its
    /// own absolute position. That is fine for a divider that never moves -- and
    /// wrong for these two, which have to shift or step aside when the recital
    /// layout puts something else where they were. Returning the group lets the
    /// caller keep hold of the whole rule instead of three loose graphics.
    /// </remarks>
    /// <summary>
    /// 金線和「SCORE」跟著數字的寬度走。
    /// </summary>
    /// <remarks>
    /// 線的長度、標籤的位置都不是寫死的數字，而是從數字的寬度算出來的：位數變了
    /// 三樣東西一起變，不會出現「線比數字長一截」那種對不齊。
    ///
    /// 標籤壓在線上一點點（+11），看起來是**坐在線上**，不是浮在線的上方。
    /// </remarks>
    private void LayoutScoreBlock(bool recital)
    {
        // 標籤和數字**在 X 軸上不重疊**：一條線底下，左邊是「SCORE」、右邊是數
        // 字，中間留一段空。標籤壓在第一位數底下的話，兩個東西會被讀成疊在一起
        // ——它們是並排的兩件事，不是上下兩層。
        const float labelWidth = 104f;
        const float labelGap = 22f;
        if (scoreDigits != null)
            // 演奏會版的分數牌只有 260 寬，一般版有 595。同一組格子塞不進兩個
            // 寬度，所以字級和格寬跟著模式走 —— 七位數 × 34 = 238，收得進去。
            scoreDigits.Configure(ClassicalBookUITheme.GetScoreSourceFont(),
                recital ? 54 : ScoreDigitSize, ScoreInk, ScoreEmboss,
                recital ? 34f : ScoreDigitCell);

        float digitsWidth = scoreDigits != null ? scoreDigits.Width : ScoreDigitCell * 7f;
        // 線底下站著標籤和數字兩個，所以線的長度是兩個加上中間那段空。
        float width = recital ? digitsWidth : labelWidth + labelGap + digitsWidth;

        // 數字自己的框每次都要重排，**兩個模式都要**。
        //
        // 只排一般版的話，切到演奏會版時上一次那個「往右挪 63 讓出標籤位」的偏移
        // 會留在框上：數字於是整排往右跑，壓到右邊的判定表上。位置是每一次重排都
        // 該重新決定的東西，不是「需要的時候才改」。
        if (scoreDigits != null)
            SetRect(scoreDigits.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(digitsWidth, (recital ? 54f : ScoreDigitSize) * 1.6f),
                recital ? Vector2.zero : new Vector2((labelWidth + labelGap) * 0.5f, 0f));

        if (scoreRule != null && !recital)
            SetRect(scoreRule, new Vector2(0.5f, 1f), new Vector2(width, 8f),
                new Vector2(0f, ScoreRuleY));

        if (scoreKickerText == null) return;
        scoreKickerText.fontSize = recital ? 14f : 17f;
        if (recital)
        {
            // 演奏會版：分數牌只有 260 寬，數字就佔了 238，左邊排不下標籤。沒有
            // 線可以坐，就讓它站在數字**上面**一行 —— 一樣不和數字搶同一段 X。
            // 分數牌錨在左欄、中心在 x=170、寬 260，所以左緣是 40。
            SetRect(scoreKickerText.rectTransform, new Vector2(0f, 1f),
                new Vector2(labelWidth, 22f),
                new Vector2(40f + labelWidth * 0.5f, -486f));
            return;
        }
        SetRect(scoreKickerText.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(labelWidth, 24f),
            new Vector2(-width * 0.5f + labelWidth * 0.5f, ScoreRuleY + 11f));
        scoreKickerText.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private static RectTransform AddRule(Transform parent, Vector2 position, float width)
    {
        RectTransform root = RectObject("Rule", parent).GetComponent<RectTransform>();
        SetRect(root, new Vector2(0.5f, 1f), new Vector2(width, 8f), position);
        Image line = AddImage("GoldRule", root, new Color(Gold.r, Gold.g, Gold.b, 0.60f));
        SetRect(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(width, 2f), Vector2.zero);
        Image dotLeft = AddImage("RuleOrnamentL", root, Gold);
        SetRect(dotLeft.rectTransform, new Vector2(0f, 0.5f), new Vector2(8f, 8f), Vector2.zero);
        dotLeft.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
        Image dotRight = AddImage("RuleOrnamentR", root, Gold);
        SetRect(dotRight.rectTransform, new Vector2(1f, 0.5f), new Vector2(8f, 8f), Vector2.zero);
        dotRight.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
        return root;
    }

    private static void AddBorder(Transform parent, Color color, float thickness, float inset)
    {
        Image top = AddImage("BorderTop", parent, color);
        top.rectTransform.anchorMin = new Vector2(0f, 1f);
        top.rectTransform.anchorMax = Vector2.one;
        top.rectTransform.offsetMin = new Vector2(inset, -inset - thickness);
        top.rectTransform.offsetMax = new Vector2(-inset, -inset);
        Image bottom = AddImage("BorderBottom", parent, color);
        bottom.rectTransform.anchorMin = Vector2.zero;
        bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
        bottom.rectTransform.offsetMin = new Vector2(inset, inset);
        bottom.rectTransform.offsetMax = new Vector2(-inset, inset + thickness);
        Image left = AddImage("BorderLeft", parent, color);
        left.rectTransform.anchorMin = Vector2.zero;
        left.rectTransform.anchorMax = new Vector2(0f, 1f);
        left.rectTransform.offsetMin = new Vector2(inset, inset);
        left.rectTransform.offsetMax = new Vector2(inset + thickness, -inset);
        Image right = AddImage("BorderRight", parent, color);
        right.rectTransform.anchorMin = new Vector2(1f, 0f);
        right.rectTransform.anchorMax = Vector2.one;
        right.rectTransform.offsetMin = new Vector2(-inset - thickness, inset);
        right.rectTransform.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Longest edge of the framed artwork, mount included.</summary>
    private const float CoverFrameLongEdge = 465f;

    /// <summary>Paper mount between the gold border and the artwork.</summary>
    private const float CoverMount = 13f;

    /// <summary>
    /// Reshapes the cover frame to the artwork's own proportions.
    /// </summary>
    /// <remarks>
    /// A fixed square frame can only show a non-square cover by padding it, and
    /// the padding reads as a black bar rather than as part of the design. The
    /// frame follows the picture instead: the longer edge keeps the size the
    /// square frame had, the shorter one gives way, and the mount stays an even
    /// width all the way round because it is added after the aspect is applied
    /// rather than cut out of it.
    /// </remarks>
    private void ApplyCoverAspect(float width, float height)
    {
        if (coverFrameRect == null || width <= 0f || height <= 0f) return;

        float inner = CoverFrameLongEdge - CoverMount * 2f;
        float aspect = width / height;
        Vector2 art = aspect >= 1f
            ? new Vector2(inner, inner / aspect)
            : new Vector2(inner * aspect, inner);
        Vector2 size = art + new Vector2(CoverMount * 2f, CoverMount * 2f);

        coverFrameRect.sizeDelta = size;
        if (coverShadowRect != null) coverShadowRect.sizeDelta = size - new Vector2(2f, 2f);
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
