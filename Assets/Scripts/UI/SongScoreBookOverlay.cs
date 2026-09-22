using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen score book opened from the focused song volume. The left page is
/// the title plate and artwork; the right page is the difficulty contents.
/// </summary>
public sealed class SongScoreBookOverlay : MonoBehaviour
{
    private static SongScoreBookOverlay instance;

    private SongSelectionManager.SongOption group;
    private Action<SongSelectionManager.SongOption> selected;
    private CanvasGroup canvasGroup;
    private RectTransform book;
    private RectTransform leftPage;
    private RectTransform rightPage;
    private Button backButton;
    private bool leaving;
    /// <summary>每按一次 ± 動幾拍。夠細可以對到練習器的刻度，又不必按二十下。</summary>
    private const float BpmStep = 2f;

    private readonly List<DifficultyEntry> difficultyEntries = new List<DifficultyEntry>();

    private sealed class DifficultyEntry
    {
        public SongSelectionManager.SongOption option;
        public LayoutElement layout;
        public RectTransform analysisRoot;
        public CanvasGroup analysisGroup;
        public TextMeshProUGUI analysisText;
        public ChartDensityGraphic densityGraph;
        public ChartRadarGraphic radar;
        public ChartBpmGraphic bpmGraph;
        public RectTransform practiceRow;
        public Button practiceToggle;
        public Image practiceBox;
        public TextMeshProUGUI practiceLabel;
        public TextMeshProUGUI speedLabel;
        public Button slower;
        public Button faster;
        public Button reset;
        public bool practiceAllowed;
        public bool expanded;
        public bool requested;
    }

    public static void Open(SongSelectionManager.SongOption song,
        Action<SongSelectionManager.SongOption> onSelected)
    {
        if (song == null) return;
        if (instance != null) Destroy(instance.gameObject);
        GameObject root = new GameObject("SongScoreBookOverlay");
        instance = root.AddComponent<SongScoreBookOverlay>();
        instance.Build(song, onSelected);
    }

    private void Build(SongSelectionManager.SongOption song,
        Action<SongSelectionManager.SongOption> onSelected)
    {
        group = song;
        selected = onSelected;

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 26000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        Image shade = AddImage(transform, "RoomShade", new Color(0.008f, 0.006f, 0.008f, 0.92f));
        Stretch(shade.rectTransform);

        RectTransform shadow = AddRect(transform, "BookShadow", new Vector2(1710f, 920f),
            new Vector2(12f, -16f));
        Image shadowImage = shadow.gameObject.AddComponent<Image>();
        shadowImage.color = new Color(0f, 0f, 0f, 0.72f);
        shadowImage.raycastTarget = false;

        book = AddRect(transform, "OpenScoreBook", new Vector2(1680f, 900f), Vector2.zero);
        Image leather = book.gameObject.AddComponent<Image>();
        leather.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        leather.type = Image.Type.Tiled;
        leather.color = new Color(0.105f, 0.038f, 0.025f, 1f);
        leather.raycastTarget = false;
        Outline bookRim = book.gameObject.AddComponent<Outline>();
        bookRim.effectColor = ClassicalBookUITheme.Gold;
        bookRim.effectDistance = new Vector2(4f, -4f);

        // 闔起來的板子：邊要有弧度，書口要露出紙。矩形的 Image 換成這個。
        BookBoardGraphic boards = BookBoardGraphic.Attach(book, 7);
        if (boards != null)
        {
            boards.Profile = BookShape.Profile.Volume;
            boards.Set(new Color(0.115f, 0.045f, 0.028f, 1f),
                new Color(0.135f, 0.055f, 0.033f, 1f),
                new Color(0.84f, 0.78f, 0.62f, 1f), ClassicalBookUITheme.Gold);
            Sprite boardGrain = ClassicalBookUITheme.GetBoardTextureSprite();
            if (boardGrain != null) boards.Grain(boardGrain.texture, boardGrain.texture.width);
        }

        // 書板的皮先老化，兩頁才蓋上去 —— 順序決定誰壓在誰上面。
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

        // 板子比書芯大一圈（那一圈叫「溝」），但也只大一圈。留太寬的話，深色
        // 的板子變成畫面的主角，內頁反而像釘在上面的兩張紙 —— 翻開的書看到的
        // 幾乎全是紙，板子只在外面收一道邊。
        leftPage = AddPage("LeftPage", new Vector2(0.034f, 0.052f), new Vector2(0.489f, 0.948f));
        rightPage = AddPage("RightPage", new Vector2(0.511f, 0.052f), new Vector2(0.966f, 0.948f));
        Image spine = AddImage(book, "BookSpine", new Color(0.28f, 0.15f, 0.065f, 0.78f));
        spine.rectTransform.anchorMin = new Vector2(0.497f, 0.045f);
        spine.rectTransform.anchorMax = new Vector2(0.503f, 0.955f);
        spine.rectTransform.offsetMin = spine.rectTransform.offsetMax = Vector2.zero;

        BuildTitlePage(song);
        BuildContents(song);
        BuildBackButton();
        StartCoroutine(OpenAnimation());
    }

    private RectTransform AddPage(string name, Vector2 min, Vector2 max)
    {
        Image page = AddImage(book, name, new Color(0.93f, 0.89f, 0.74f, 1f));
        page.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        page.type = Image.Type.Tiled;
        page.rectTransform.anchorMin = min;
        page.rectTransform.anchorMax = max;
        page.rectTransform.offsetMin = Vector2.zero;
        page.rectTransform.offsetMax = Vector2.zero;
        Outline rim = page.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.47f, 0.31f, 0.13f, 0.65f);
        rim.effectDistance = new Vector2(2f, -2f);

        // 兩頁各給一個不一樣的種子。同一本書的兩頁磨得一模一樣，是唯一會讓
        // 這個手法露餡的地方。
        int seed = name == "LeftPage" ? 23 : 61;
        // 左頁釘在右邊、右頁釘在左邊。訂口那一側是平的，翻口那一側才有弧
        // —— 兩頁用同一個方向的話，攤開的書會有一頁看起來裝反了。
        BookShape.Profile sheetShape = name == "LeftPage"
            ? BookShape.Profile.Page.Mirrored()
            : BookShape.Profile.Page;

        // 紙也不是矩形：翻口那三邊都有一點弧。
        BookBoardGraphic leaf = BookBoardGraphic.Attach(page.rectTransform, seed);
        if (leaf != null)
        {
            leaf.Bound = false;
            leaf.Profile = sheetShape;
            Color sheet = new Color(0.93f, 0.89f, 0.74f, 1f);
            leaf.Set(sheet, sheet, sheet, ClassicalBookUITheme.Gold);
            Sprite fibre = ClassicalBookUITheme.GetPaperTextureSprite();
            if (fibre != null) leaf.Grain(fibre.texture, fibre.texture.width);
        }

        AgedPaperGraphic wear = AgedPaperGraphic.Attach(page.rectTransform, seed, 1);
        if (wear != null)
        {
            wear.Shape(sheetShape);
            wear.Stain = new Color(0.30f, 0.175f, 0.085f, 1f);
            wear.Strength = 0.88f;
            wear.EdgeDepth = 0.075f;
            wear.Mottle = 0.24f;
            wear.Foxing = 6;
        }
        // 版框。這一條比什麼都關鍵：有了它，紙上的東西才是「一頁的內容」。
        // 磨損收一點。斷得太碎讀起來是虛線，那是另一種「工整」。
        PageRuleGraphic rule = PageRuleGraphic.Attach(page.rectTransform, seed,
            new Color(0.22f, 0.115f, 0.05f, 0.90f), 0.040f, 4.4f, true, 2);
        if (rule != null) rule.Wear = 0.26f;
        if (rule != null) rule.Shape(sheetShape);

        return page.rectTransform;
    }

    private void BuildTitlePage(SongSelectionManager.SongOption song)
    {
        Image frame = AddImage(leftPage, "ArtworkFrame", new Color(0.09f, 0.055f, 0.035f, 1f));
        SetAnchors(frame.rectTransform, new Vector2(0.14f, 0.30f), new Vector2(0.86f, 0.84f),
            new Vector2(0f, 0f), new Vector2(0f, 0f));
        Outline frameRim = frame.gameObject.AddComponent<Outline>();
        frameRim.effectColor = ClassicalBookUITheme.Gold;
        frameRim.effectDistance = new Vector2(3f, -3f);
        Image art = AddImage(frame.transform, "Artwork", Color.white);
        Stretch(art.rectTransform, 14f);
        art.preserveAspect = true;
        art.sprite = song.coverSprite;
        art.enabled = song.coverSprite != null;

        TextMeshProUGUI title = AddText(leftPage,
            string.IsNullOrWhiteSpace(song.displayName) ? Localize.T("未命名曲目", "未命名曲目", "UNTITLED") : song.displayName,
            36f, new Color(0.16f, 0.09f, 0.045f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
        SetAnchors(title.rectTransform, new Vector2(0.07f, 0.185f), new Vector2(0.93f, 0.275f),
            Vector2.zero, Vector2.zero);
        title.enableAutoSizing = true;
        title.fontSizeMin = 23f;
        title.fontSizeMax = 36f;
        title.overflowMode = TextOverflowModes.Ellipsis;
        ClassicalBookUITheme.ApplyContentFont(title);

        string authorValue = string.IsNullOrWhiteSpace(song.author)
            ? Localize.T("未知作者", "未知作者", "Unknown Artist") : song.author;
        TextMeshProUGUI author = AddText(leftPage, authorValue, 22f,
            new Color(0.37f, 0.24f, 0.12f, 1f), FontStyles.Italic, TextAlignmentOptions.Center);
        SetAnchors(author.rectTransform, new Vector2(0.09f, 0.115f), new Vector2(0.91f, 0.17f),
            Vector2.zero, Vector2.zero);
        ClassicalBookUITheme.ApplyContentFont(author);

        if (!string.IsNullOrWhiteSpace(song.category))
        {
            TextMeshProUGUI category = AddText(leftPage, song.category, 17f,
                ClassicalBookUITheme.Gold, FontStyles.Bold, TextAlignmentOptions.Center);
            SetAnchors(category.rectTransform, new Vector2(0.10f, 0.055f), new Vector2(0.90f, 0.105f),
                Vector2.zero, Vector2.zero);
            ClassicalBookUITheme.ApplyContentFont(category);
        }
    }

    private void BuildContents(SongSelectionManager.SongOption song)
    {
        // The page used to be headed "CONTENTS". Without it the rule is no longer
        // an underline, so it moves up to sit as the top border of the list.
        Image rule = AddImage(rightPage, "HeadingRule", ClassicalBookUITheme.Gold);
        SetAnchored(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(610f, 2f), new Vector2(0f, -52f));

        RectTransform listRoot = AddRect(rightPage, "DifficultyContents", new Vector2(650f, 660f), new Vector2(0f, -15f));
        VerticalLayoutGroup layout = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 12, 12);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        List<SongSelectionManager.SongOption> difficulties = new List<SongSelectionManager.SongOption>();
        if (song.difficultyVariants != null && song.difficultyVariants.Count > 0)
            difficulties.AddRange(song.difficultyVariants);
        else difficulties.Add(song);

        difficulties.Sort((a, b) => (a?.difficultyLevel ?? 0).CompareTo(b?.difficultyLevel ?? 0));
        for (int i = 0; i < difficulties.Count; i++)
            AddDifficultyEntry(listRoot, difficulties[i], i + 1);
    }

    private void AddDifficultyEntry(RectTransform parent,
        SongSelectionManager.SongOption difficulty, int number)
    {
        if (difficulty == null) return;
        Image plate = AddImage(parent, "Difficulty_" + number,
            new Color(0.91f, 0.85f, 0.69f, 0.98f));
        plate.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        plate.type = Image.Type.Tiled;
        LayoutElement element = plate.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 82f;
        // 每一列是夾在書裡的一張紙條，各有各的磨損。
        AgedPaperGraphic plateWear = AgedPaperGraphic.Attach(plate.rectTransform, 101 + number * 7);
        if (plateWear != null)
        {
            // 夾在書裡的紙條，弧度比書頁更明顯 —— 它薄，所以捲得更兇。
            plateWear.Shape(BookShape.Profile.Slip);
            plateWear.Stain = new Color(0.32f, 0.19f, 0.09f, 1f);
            plateWear.Strength = 0.66f;
            plateWear.EdgeDepth = 0.10f;
            plateWear.Mottle = 0.22f;
            plateWear.Foxing = 2;
        }
        Outline rim = plate.gameObject.AddComponent<Outline>();
        rim.effectColor = new Color(0.31f, 0.14f, 0.065f, 0.92f);
        rim.effectDistance = new Vector2(1f, -1f);
        Button button = plate.gameObject.AddComponent<Button>();
        plate.raycastTarget = true;
        button.targetGraphic = plate;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.07f, 1.02f, 0.88f, 1f);
        colours.pressedColor = new Color(0.83f, 0.76f, 0.62f, 1f);
        colours.fadeDuration = 0.10f;
        button.colors = colours;

        string name = string.IsNullOrWhiteSpace(difficulty.difficultyName)
            ? Localize.T("一般", "普通", "Normal") : difficulty.difficultyName;
        string level = difficulty.difficultyLevel > 0 ? $"Lv. {difficulty.difficultyLevel}" : string.Empty;
        int best = LocalScoreRecords.GetBestScore(difficulty);
        string score = best > 0 ? Localize.T($"最高 {best:N0}", $"最高 {best:N0}", $"BEST {best:N0}") : string.Empty;
        string difficultyHex = ColorUtility.ToHtmlStringRGB(
            DifficultyVisualPalette.For(difficulty.difficultyName, difficulty.difficultyLevel));
        // The font is resolved from the text, and the score line carries CJK
        // ("最高 …") while the line above it usually does not. Building the label
        // from the headline alone and appending the score afterwards keeps a
        // difficulty that has been played in the same face as one that has not.
        string headline = $"{number:00}     <color=#{difficultyHex}>{name}     {level}</color>";
        TextMeshProUGUI label = AddText(plate.transform, headline, 22f,
            new Color(0.22f, 0.115f, 0.055f, 1f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        label.rectTransform.anchorMin = new Vector2(0f, 1f);
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.pivot = new Vector2(0.5f, 1f);
        label.rectTransform.offsetMin = new Vector2(16f, -82f);
        label.rectTransform.offsetMax = new Vector2(-16f, 0f);
        label.lineSpacing = 3f;
        label.richText = true;
        // Keeps the ivory low-level caption readable on score paper while the
        // hue itself still follows the white / gold / purple difficulty rule.
        label.outlineColor = new Color(0.20f, 0.09f, 0.035f, 0.85f);
        label.outlineWidth = 0.08f;
        ClassicalBookUITheme.ApplyContentFont(label);
        // Kept as one string with the empty second line, so an unplayed entry
        // still sits at the same height as a played one.
        label.text = headline + "\n" + score;

        // 加高 72px 給 BPM 線。密度圖說「哪裡忙」，BPM 線說「那裡忙是因為譜變厚
        // 還是因為曲子變快」，兩張圖共用同一條時間軸才回答得了這個問題。
        RectTransform analysisRoot = AddRect(plate.transform, "TechniqueAnalysis",
            new Vector2(0f, 300f), Vector2.zero);
        analysisRoot.anchorMin = new Vector2(0f, 1f);
        analysisRoot.anchorMax = new Vector2(1f, 1f);
        analysisRoot.pivot = new Vector2(0.5f, 1f);
        analysisRoot.anchoredPosition = new Vector2(0f, -82f);
        analysisRoot.sizeDelta = new Vector2(0f, 300f);
        Image analysisPaper = analysisRoot.gameObject.AddComponent<Image>();
        analysisPaper.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        analysisPaper.type = Image.Type.Tiled;
        analysisPaper.color = new Color(0.98f, 0.955f, 0.84f, 0.99f);
        analysisPaper.raycastTarget = false;
        Image separator = AddImage(analysisRoot, "Separator",
            new Color(0.31f, 0.14f, 0.065f, 0.55f));
        separator.rectTransform.anchorMin = new Vector2(0.03f, 1f);
        separator.rectTransform.anchorMax = new Vector2(0.97f, 1f);
        separator.rectTransform.pivot = new Vector2(0.5f, 1f);
        separator.rectTransform.sizeDelta = new Vector2(0f, 1f);
        separator.rectTransform.anchoredPosition = Vector2.zero;
        TextMeshProUGUI analysisText = AddText(analysisRoot,
            Localize.T("讀取技法分析中…", "读取技法分析中…", "Reading technique analysis…"),
            16f, new Color(0.25f, 0.15f, 0.075f, 1f), FontStyles.Normal,
            TextAlignmentOptions.TopLeft);
        // 文字讓出右邊三分之一給雷達圖。那塊本來就是空的 —— 四行字都在左半邊。
        analysisText.rectTransform.anchorMin = new Vector2(0f, 0.60f);
        analysisText.rectTransform.anchorMax = new Vector2(0.64f, 1f);
        analysisText.rectTransform.offsetMin = new Vector2(14f, 4f);
        analysisText.rectTransform.offsetMax = new Vector2(-4f, -12f);
        analysisText.enableWordWrapping = true;
        analysisText.lineSpacing = 4f;

        GameObject graphObject = new GameObject("HandDensityGraph", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(ChartDensityGraphic));
        graphObject.layer = plate.gameObject.layer;
        RectTransform graphRect = graphObject.GetComponent<RectTransform>();
        graphRect.SetParent(analysisRoot, false);
        // 密度圖是滿版的，雷達圖的圖例就落在它上緣 —— 兩者原本各佔到 0.33 和
        // 0.30，重疊了三個百分點。密度圖整條往下移，雷達圖的底再抬高一點。
        graphRect.anchorMin = new Vector2(0.035f, 0.40f);
        graphRect.anchorMax = new Vector2(0.965f, 0.58f);
        graphRect.offsetMin = Vector2.zero;
        graphRect.offsetMax = Vector2.zero;
        ChartDensityGraphic densityGraph = graphObject.GetComponent<ChartDensityGraphic>();
        densityGraph.raycastTarget = false;

        GameObject radarObject = new GameObject("ChartRadar", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(ChartRadarGraphic));
        radarObject.layer = plate.gameObject.layer;
        RectTransform radarRect = radarObject.GetComponent<RectTransform>();
        radarRect.SetParent(analysisRoot, false);
        radarRect.anchorMin = new Vector2(0.655f, 0.60f);
        radarRect.anchorMax = new Vector2(0.985f, 1f);
        radarRect.offsetMin = Vector2.zero;
        radarRect.offsetMax = new Vector2(0f, -8f);
        ChartRadarGraphic radar = radarObject.GetComponent<ChartRadarGraphic>();
        radar.raycastTarget = false;
        // 這一頁是米色的紙，金色會糊掉，所以墨色跟著內文走。
        radar.Ink = new Color(0.36f, 0.18f, 0.08f, 1f);
        GameObject bpmObject = new GameObject("BpmCurve", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(ChartBpmGraphic));
        bpmObject.layer = plate.gameObject.layer;
        RectTransform bpmRect = bpmObject.GetComponent<RectTransform>();
        bpmRect.SetParent(analysisRoot, false);
        bpmRect.anchorMin = new Vector2(0.035f, 0.22f);
        bpmRect.anchorMax = new Vector2(0.965f, 0.38f);
        bpmRect.offsetMin = Vector2.zero;
        bpmRect.offsetMax = Vector2.zero;
        ChartBpmGraphic bpmGraph = bpmObject.GetComponent<ChartBpmGraphic>();
        bpmGraph.raycastTarget = false;

        RectTransform practiceRow = AddRect(analysisRoot, "PracticeRow", Vector2.zero, Vector2.zero);
        practiceRow.anchorMin = new Vector2(0.035f, 0.02f);
        practiceRow.anchorMax = new Vector2(0.965f, 0.19f);
        practiceRow.offsetMin = Vector2.zero;
        practiceRow.offsetMax = Vector2.zero;

        Button practiceToggle = AddFlatButton(practiceRow, "PracticeToggle",
            new Vector2(0f, 0.06f), new Vector2(0.30f, 0.94f));
        Image practiceBox = AddImage(practiceToggle.transform, "Box",
            new Color(0.30f, 0.16f, 0.07f, 0.35f));
        practiceBox.rectTransform.anchorMin = new Vector2(0.02f, 0.22f);
        practiceBox.rectTransform.anchorMax = new Vector2(0.02f, 0.22f);
        practiceBox.rectTransform.pivot = Vector2.zero;
        practiceBox.rectTransform.sizeDelta = new Vector2(15f, 15f);
        practiceBox.raycastTarget = false;

        TextMeshProUGUI practiceLabel = AddText(practiceToggle.transform,
            Localize.T("練習模式", "练习模式", "Practice"), 14f,
            new Color(0.25f, 0.15f, 0.075f, 1f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        practiceLabel.rectTransform.anchorMin = new Vector2(0.02f, 0f);
        practiceLabel.rectTransform.anchorMax = Vector2.one;
        practiceLabel.rectTransform.offsetMin = new Vector2(22f, 0f);
        practiceLabel.rectTransform.offsetMax = Vector2.zero;
        practiceLabel.raycastTarget = false;

        Button slower = AddFlatButton(practiceRow, "Slower",
            new Vector2(0.33f, 0.06f), new Vector2(0.41f, 0.94f));
        AddStepperCaption(slower, "−", 17f);
        TextMeshProUGUI speedLabel = AddText(practiceRow, "1.00×", 14f,
            new Color(0.25f, 0.15f, 0.075f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
        speedLabel.rectTransform.anchorMin = new Vector2(0.42f, 0f);
        speedLabel.rectTransform.anchorMax = new Vector2(0.575f, 1f);
        speedLabel.rectTransform.offsetMin = Vector2.zero;
        speedLabel.rectTransform.offsetMax = Vector2.zero;
        speedLabel.raycastTarget = false;
        Button faster = AddFlatButton(practiceRow, "Faster",
            new Vector2(0.585f, 0.06f), new Vector2(0.665f, 0.94f));
        AddStepperCaption(faster, "+", 17f);
        Button reset = AddFlatButton(practiceRow, "ResetSpeed",
            new Vector2(0.675f, 0.06f), new Vector2(0.775f, 0.94f));
        AddStepperCaption(reset, Localize.T("原速", "原速", "Reset"), 12f);

        // 看譜面：在開始之前先從正上方看一次這個難度的排列。和練習模式、速度同一
        // 列 —— 它們都是「要不要打這一份、怎麼打」的準備動作。
        Button viewChart = AddFlatButton(practiceRow, "ViewChart",
            new Vector2(0.79f, 0.06f), new Vector2(1f, 0.94f));
        AddStepperCaption(viewChart, Localize.T("看譜面", "看谱面", "View"), 12f);

        CanvasGroup analysisGroup = analysisRoot.gameObject.AddComponent<CanvasGroup>();
        analysisGroup.alpha = 0f;
        analysisGroup.blocksRaycasts = false;
        analysisRoot.gameObject.SetActive(false);

        var entry = new DifficultyEntry
        {
            option = difficulty,
            layout = element,
            analysisRoot = analysisRoot,
            analysisGroup = analysisGroup,
            analysisText = analysisText,
            densityGraph = densityGraph,
            radar = radar,
            bpmGraph = bpmGraph,
            practiceRow = practiceRow,
            practiceToggle = practiceToggle,
            practiceBox = practiceBox,
            practiceLabel = practiceLabel,
            speedLabel = speedLabel,
            slower = slower,
            faster = faster,
            reset = reset
        };
        difficultyEntries.Add(entry);

        viewChart.onClick.AddListener(() =>
        {
            try
            {
                ChartOverviewViewer.EnsureCreated()?.OpenForChart(
                    entry.option?.chartFileName,
                    entry.option != null ? entry.option.difficultyName : null,
                    entry.option != null ? entry.option.audioClip : null,
                    entry.option != null ? entry.option.audioResourcePath : null);
            }
            catch { }
        });

        practiceToggle.onClick.AddListener(() =>
        {
            var settings = SettingsManager.Instance;
            if (settings == null || !entry.practiceAllowed) return;
            settings.SetPracticeMode(!settings.PracticeMode);
            RefreshPracticeRow(entry);
        });
        slower.onClick.AddListener(() => NudgeSpeed(entry, -1));
        faster.onClick.AddListener(() => NudgeSpeed(entry, 1));
        reset.onClick.AddListener(() =>
        {
            var settings = SettingsManager.Instance;
            if (settings == null || !entry.practiceAllowed) return;
            settings.SetPracticeSpeed(1f);
            RefreshPracticeRow(entry);
        });
        RefreshPracticeRow(entry);

        EventTrigger hover = plate.gameObject.AddComponent<EventTrigger>();
        AddHoverTrigger(hover, EventTriggerType.PointerEnter, () => ExpandAnalysis(entry));
        AddHoverTrigger(hover, EventTriggerType.PointerExit, () => CollapseAnalysis(entry));

        SongSelectionManager.SongOption captured = difficulty;
        button.onClick.AddListener(() => ChooseDifficulty(captured));
    }

    private static void AddHoverTrigger(EventTrigger trigger, EventTriggerType type, Action callback)
    {
        var item = new EventTrigger.Entry { eventID = type };
        item.callback.AddListener(_ => callback());
        trigger.triggers.Add(item);
    }

    private void ExpandAnalysis(DifficultyEntry entry)
    {
        if (entry == null || leaving) return;
        for (int i = 0; i < difficultyEntries.Count; i++)
            if (difficultyEntries[i] != entry) difficultyEntries[i].expanded = false;
        entry.expanded = true;
        entry.analysisRoot.gameObject.SetActive(true);
        if (entry.requested) return;
        entry.requested = true;
        string chartFile = entry.option != null ? entry.option.chartFileName : null;
        if (string.IsNullOrWhiteSpace(chartFile))
        {
            SetAnalysisUnavailable(entry);
            return;
        }
        if (ChartAnalysisCache.TryGet(chartFile, out ChartAnalysis cached))
        {
            SetAnalysis(entry, cached);
            return;
        }
        StartCoroutine(ChartAnalysisCache.Build(chartFile, analysis =>
        {
            if (analysis != null) SetAnalysis(entry, analysis);
            else SetAnalysisUnavailable(entry);
        }));
    }

    private static void CollapseAnalysis(DifficultyEntry entry)
    {
        if (entry != null) entry.expanded = false;
    }

    private void Update()
    {
        for (int i = 0; i < difficultyEntries.Count; i++)
        {
            DifficultyEntry entry = difficultyEntries[i];
            if (entry?.layout == null || entry.analysisGroup == null) continue;
            // 82 是標題那一條，300 是分析面板 —— 這個數字必須等於兩者之和。
            // 面板加高到 268 時忘了改這裡，於是最下面 72px（BPM 線和練習列）
            // 落在難度卡的外面：滑鼠移過去就算離開卡片，面板當場收起來。
            float targetHeight = entry.expanded ? 382f : 82f;
            entry.layout.preferredHeight = Mathf.MoveTowards(entry.layout.preferredHeight,
                targetHeight, 720f * Time.unscaledDeltaTime);
            // 練習列上的按鈕要按得到，但收起來的時候不能擋住底下的難度卡。
            entry.analysisGroup.blocksRaycasts = entry.expanded;
            entry.analysisGroup.alpha = Mathf.MoveTowards(entry.analysisGroup.alpha,
                entry.expanded ? 1f : 0f, 8f * Time.unscaledDeltaTime);
            if (!entry.expanded && entry.analysisGroup.alpha <= 0.001f &&
                entry.analysisRoot.gameObject.activeSelf)
                entry.analysisRoot.gameObject.SetActive(false);
        }
    }

    /// <summary>The chart's own tempo, as written, for the information block.</summary>
    private static void AppendBpmRange(StringBuilder text, ChartAnalysis analysis)
    {
        if (!analysis.HasBpmCurve) return;

        float low = float.MaxValue;
        float high = float.MinValue;
        for (int i = 0; i < ChartAnalysis.BucketCount; i++)
        {
            float value = analysis.bpmCurve[i];
            if (value <= 0.01f) continue;
            low = Mathf.Min(low, value);
            high = Mathf.Max(high, value);
        }
        if (high < low) return;

        text.Append("     BPM ").Append(Mathf.RoundToInt(high));
        if (high - low >= 2f) text.Append(" – ").Append(Mathf.RoundToInt(low));
    }

    private static void SetAnalysis(DifficultyEntry entry, ChartAnalysis analysis)
    {
        if (entry?.analysisText == null || analysis == null) return;
        var text = new StringBuilder(220);
        text.Append(Localize.T("音符", "音符", "Notes")).Append(' ')
            .Append(analysis.noteCount.ToString("N0"));
        if (analysis.holdCount > 0)
            text.Append("   ").Append(Localize.T("長押", "长按", "Holds")).Append(' ')
                .Append(analysis.holdCount.ToString("N0"));
        text.Append("     ").Append(Localize.T("平均密度", "平均密度", "Density")).Append(' ')
            .Append(analysis.averageDensity.ToString("0.0")).Append("/s")
            .Append("   ").Append(Localize.T("峰值", "峰值", "Peak")).Append(' ')
            .Append(analysis.peakDensity.ToString("0")).Append("/s\n");
        text.Append(Localize.T("和弦", "和弦", "Chord")).Append(' ')
            .Append(analysis.NotesPerOnset.ToString("0.00"))
            .Append("     R ").Append((analysis.RightHandRatio * 100f).ToString("0"))
            .Append("% / L ").Append(((1f - analysis.RightHandRatio) * 100f).ToString("0")).Append("%");
        // 譜面**寫成什麼樣**屬於基本資訊；調整之後實際會彈到的速度在下面的圖表
        // 上，左側刻度會即時跟著練習速度變。兩個放在一起只會擠成一團。
        AppendBpmRange(text, analysis);
        text.Append('\n');
        text.Append(Localize.T("技法", "技法", "Technique")).Append("   ")
            .Append(analysis.hasTechniqueData
                ? ChartAnalysisVocabulary.DescribeTechniques(analysis)
                : Localize.T("無可用音高資料", "无可用音高数据", "No pitch data"))
            .Append('\n')
            // The verdict is the part a player reads first, so the page carries
            // it too rather than leaving it to the hover panel.
            .Append(Localize.T("難度", "难度", "Character")).Append("   ")
            .Append(ChartAnalysisVocabulary.DescribeCharacter(analysis));
        entry.analysisText.text = text.ToString();
        ClassicalBookUITheme.ApplyLocalizedFont(entry.analysisText);
        if (entry.radar != null) entry.radar.SetData(analysis);
        // 只有帶音高的譜可以練習：練習模式關掉背景音樂，聲音全靠合成鋼琴，而沒有
        // 音高的譜合不出東西 —— 開下去會是一場全靜音的練習。
        entry.practiceAllowed = analysis.HasPitchData;

        if (entry.bpmGraph != null) entry.bpmGraph.SetData(analysis);

        // 圖表先有資料，速度標籤才有 BPM 可以顯示。
        RefreshPracticeRow(entry);
        if (entry.densityGraph != null)
            entry.densityGraph.SetData(analysis.leftDensity, analysis.rightDensity,
                analysis.peakBucketTotal);
    }

    private static void SetAnalysisUnavailable(DifficultyEntry entry)
    {
        if (entry?.analysisText == null) return;
        entry.analysisText.text = Localize.T("讀不到技法分析", "无法读取技法分析", "Technique analysis unavailable");
        ClassicalBookUITheme.ApplyLocalizedFont(entry.analysisText);
        if (entry.densityGraph != null) entry.densityGraph.Clear();
        if (entry.radar != null) entry.radar.Clear();
        if (entry.bpmGraph != null) entry.bpmGraph.Clear();
    }

    /// <summary>
    /// The way out, set on the page as printed matter rather than hung on it as
    /// a sign.
    /// </summary>
    /// <remarks>
    /// **Why it is not a plate.** It used to be the same brass plaque the
    /// settings screen uses: a dark slab with a gilt outline, screwed to a sheet
    /// of aged paper. Nothing else inside this book is an object -- the
    /// difficulties, the titles, the analysis are all ink on the page -- so the
    /// one plaque read as a control panel that had wandered into a book.
    ///
    /// **What it is instead.** A ruled label: the worn box that everything else
    /// on these pages is ruled with, the page's own ink for the type, and a
    /// bracket instead of a chevron. The wash behind it is nearly invisible at
    /// rest and comes up under the pointer -- that is the whole hover state, and
    /// on a page it is enough, because a printed thing that lights up is
    /// obviously the thing you can press.
    /// </remarks>
    private void BuildBackButton()
    {
        Image plate = AddImage(leftPage, "BackToSelection", new Color(0.36f, 0.22f, 0.10f, 0.55f));
        SetAnchored(plate.rectTransform, new Vector2(0f, 1f), new Vector2(210f, 48f), new Vector2(26f, -24f));
        plate.raycastTarget = true;
        backButton = plate.gameObject.AddComponent<Button>();
        backButton.targetGraphic = plate;

        // 底色本身很濃，靠 normalColor 的 alpha 壓到幾乎看不見；滑過去才浮上來。
        ColorBlock colours = backButton.colors;
        colours.normalColor = new Color(1f, 1f, 1f, 0.16f);
        colours.highlightedColor = new Color(1f, 0.94f, 0.82f, 0.58f);
        colours.pressedColor = new Color(0.82f, 0.70f, 0.52f, 0.85f);
        colours.selectedColor = colours.highlightedColor;
        colours.fadeDuration = 0.10f;
        backButton.colors = colours;

        Color ink = new Color(0.22f, 0.125f, 0.055f, 0.92f);
        PageRuleGraphic box = PageRuleGraphic.Attach(plate.rectTransform, 91, ink, 0.055f, 2.0f,
            false);
        if (box != null)
        {
            box.Shape(BookShape.Profile.Slip);
            box.Wear = 0.30f;
        }

        TextMeshProUGUI label = AddText(plate.transform,
            Localize.T("[  返回選曲  ]", "[  返回选曲  ]", "[  SONG SELECT  ]"), 19f,
            ink, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 3f);
        ClassicalBookUITheme.ApplyContentFont(label);
        backButton.onClick.AddListener(CloseBook);
    }

    private void ChooseDifficulty(SongSelectionManager.SongOption difficulty)
    {
        if (leaving || difficulty == null) return;
        leaving = true;
        // 開始遊戲前先把譜面檢視器收掉：它開著的話，整座預覽舞台（上千顆音符）會
        // 一路活到遊戲裡。
        try { ChartOverviewViewer.Instance?.SetOpen(false); } catch { }
        if (backButton != null) backButton.interactable = false;
        if (group != null) group.selectedVariant = difficulty;
        StartCoroutine(TurnToScoreAndLeave(difficulty));
    }

    /// <summary>
    /// Ruled but unwritten pages for the turn out of the book.
    /// </summary>
    /// <remarks>
    /// Staves and a title only.  Note heads were tried both as decoration and as
    /// the piece's real contour; at this speed neither is read as music, and the
    /// second one cost a chart lookup to say nothing.  Empty ruling still says
    /// "score", which is the whole job of a page that is on screen for under half
    /// a second.
    /// </remarks>
    private void DrawEmptyScore(RectTransform page)
    {
        TextMeshProUGUI heading = AddText(page, group?.displayName ?? string.Empty, 22f,
            new Color(0.25f, 0.16f, 0.08f, 0.75f), FontStyles.Italic, TextAlignmentOptions.Center);
        SetAnchored(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(620f, 42f), new Vector2(0f, -42f));
        ClassicalBookUITheme.ApplyContentFont(heading);

        for (int staff = 0; staff < 6; staff++)
        {
            float y = -135f - staff * 118f;
            for (int line = 0; line < 5; line++)
            {
                Image staffLine = AddImage(page, $"Staff_{staff}_{line}", new Color(0.18f, 0.12f, 0.075f, 0.48f));
                SetAnchored(staffLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(650f, 1.5f),
                    new Vector2(0f, y - line * 13f));
            }

            // The bar line is most of what tells the eye these are staves rather
            // than five stripes.
            Image bar = AddImage(page, $"Bar_{staff}", new Color(0.18f, 0.12f, 0.075f, 0.62f));
            SetAnchored(bar.rectTransform, new Vector2(0.5f, 1f), new Vector2(2f, 54f),
                new Vector2(-325f, y - 26f));
        }
    }

    private IEnumerator TurnToScoreAndLeave(SongSelectionManager.SongOption difficulty)
    {
        ClearPage(leftPage);
        ClearPage(rightPage);
        DrawEmptyScore(leftPage);
        DrawEmptyScore(rightPage);

        yield return new WaitForSecondsRealtime(0.10f);
        float elapsed = 0f;
        const float duration = 0.42f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = 1f - t * t;
            book.localScale = Vector3.one * Mathf.Lerp(1f, 1.035f, t);
            yield return null;
        }
        selected?.Invoke(difficulty);
        Destroy(gameObject);
    }


    private static void ClearPage(RectTransform page)
    {
        if (page == null) return;
        for (int i = page.childCount - 1; i >= 0; i--) Destroy(page.GetChild(i).gameObject);
    }

    private void CloseBook()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(CloseAnimation());
    }

    private IEnumerator OpenAnimation()
    {
        canvasGroup.alpha = 0f;
        book.localScale = new Vector3(0.08f, 0.94f, 1f);
        float elapsed = 0f;
        while (elapsed < 0.32f)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / 0.32f);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            canvasGroup.alpha = t;
            book.localScale = new Vector3(Mathf.Lerp(0.08f, 1f, eased),
                Mathf.Lerp(0.94f, 1f, eased), 1f);
            yield return null;
        }
        book.localScale = Vector3.one;
    }

    private IEnumerator CloseAnimation()
    {
        float elapsed = 0f;
        while (elapsed < 0.24f)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / 0.24f);
            canvasGroup.alpha = 1f - t;
            book.localScale = new Vector3(Mathf.Lerp(1f, 0.08f, t), 1f, 1f);
            yield return null;
        }
        Destroy(gameObject);
    }

    private static Color DifficultyColour(int level)
    {
        return DifficultyVisualPalette.ForLevel(level);
    }

    private static Color DifficultyColour(string difficultyName, int level)
    {
        return DifficultyVisualPalette.For(difficultyName, level);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>
    /// Steps the practice tempo, in beats per minute rather than in multiples.
    /// </summary>
    /// <remarks>
    /// The player thinks in tempo, not in ratios: "take it at 120" is a musical
    /// instruction and "take it at 0.79x" is not. The number being stepped is the
    /// chart's top speed, because that is the one a player picks a practice tempo
    /// against; everything slower in the piece moves by the same proportion, which
    /// is what keeps a rallentando a rallentando.
    ///
    /// Falls back to stepping the ratio on a chart with no tempo data, so the
    /// control still works where there is no number to name.
    /// </remarks>
    private static void NudgeSpeed(DifficultyEntry entry, int steps)
    {
        var settings = SettingsManager.Instance;
        if (settings == null || !entry.practiceAllowed) return;

        float top = entry.bpmGraph != null && entry.bpmGraph.HasData ? entry.bpmGraph.OriginalHigh : 0f;
        if (top > 1f)
        {
            float target = Mathf.Round(top * settings.PracticeSpeed) + steps * BpmStep;
            settings.SetPracticeSpeed(target / top);
        }
        else
        {
            settings.SetPracticeSpeed(settings.PracticeSpeed + steps * 0.05f);
        }

        RefreshPracticeRow(entry);
    }

    /// <summary>
    /// Puts the practice row in step with the setting, and greys it out on a
    /// chart that cannot be practised.
    /// </summary>
    /// <remarks>
    /// The speed is read back from the settings rather than kept here: the row
    /// exists once per difficulty plate, and a value cached in each of them
    /// disagrees with the others the moment one is used.
    /// </remarks>
    private static void RefreshPracticeRow(DifficultyEntry entry)
    {
        if (entry == null || entry.practiceToggle == null) return;
        var settings = SettingsManager.Instance;
        bool on = entry.practiceAllowed && settings != null && settings.PracticeMode;
        float speed = settings != null ? settings.PracticeSpeed : 1f;

        entry.practiceToggle.interactable = entry.practiceAllowed;
        if (entry.slower != null) entry.slower.interactable = on;
        if (entry.faster != null) entry.faster.interactable = on;
        // 已經是原速就沒什麼可以還原的，按鈕自己說出這件事。
        if (entry.reset != null) entry.reset.interactable = on && !Mathf.Approximately(speed, 1f);

        if (entry.practiceBox != null)
            entry.practiceBox.color = on
                ? new Color(0.55f, 0.30f, 0.10f, 0.95f)
                : new Color(0.30f, 0.16f, 0.07f, 0.30f);

        if (entry.practiceLabel != null)
        {
            entry.practiceLabel.text = entry.practiceAllowed
                ? Localize.T("練習模式", "练习模式", "Practice")
                : Localize.T("練習（無音高）", "练习（无音高）", "No pitch");
            entry.practiceLabel.color = entry.practiceAllowed
                ? new Color(0.25f, 0.15f, 0.075f, 1f)
                : new Color(0.25f, 0.15f, 0.075f, 0.4f);
            ClassicalBookUITheme.ApplyContentFont(entry.practiceLabel);
        }

        // 圖表跟著一起變。玩家調的是速度，看到的必須是調完之後的譜面速度 ——
        // 不然那個數字要對照到哪裡去，得自己心算。
        if (entry.bpmGraph != null) entry.bpmGraph.Scale = on ? speed : 1f;

        if (entry.speedLabel != null)
        {
            float top = entry.bpmGraph != null && entry.bpmGraph.HasData
                ? entry.bpmGraph.OriginalHigh : 0f;
            entry.speedLabel.text = top > 1f
                ? Mathf.RoundToInt(top * speed) + " BPM"
                : speed.ToString("0.00") + "×";
            entry.speedLabel.color = on
                ? new Color(0.25f, 0.15f, 0.075f, 1f)
                : new Color(0.25f, 0.15f, 0.075f, 0.4f);
        }
    }

    /// <summary>A borderless button: a faint plate that lights up on hover.</summary>
    private static Button AddFlatButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rect = AddRect(parent, name, Vector2.zero, Vector2.zero);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image plate = rect.gameObject.AddComponent<Image>();
        plate.color = new Color(0.86f, 0.80f, 0.66f, 0.25f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.15f, 1.10f, 0.98f, 1f);
        colours.pressedColor = new Color(0.80f, 0.72f, 0.58f, 1f);
        colours.disabledColor = new Color(0.9f, 0.88f, 0.84f, 0.35f);
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        return button;
    }

    private static void AddStepperCaption(Button button, string caption, float size)
    {
        TextMeshProUGUI label = AddText(button.transform, caption, size,
            new Color(0.25f, 0.15f, 0.075f, 1f), FontStyles.Bold, TextAlignmentOptions.Center);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.raycastTarget = false;
    }

    private static RectTransform AddRect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static Image AddImage(Transform parent, string name, Color colour)
    {
        RectTransform rect = AddRect(parent, name, Vector2.zero, Vector2.zero);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI AddText(Transform parent, string value, float size,
        Color colour, FontStyles style, TextAlignmentOptions alignment)
    {
        RectTransform rect = AddRect(parent, "Text", Vector2.zero, Vector2.zero);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = colour;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        ClassicalBookUITheme.ApplyLocalizedFont(text);
        return text;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = Vector2.one * -inset;
    }

    private static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
