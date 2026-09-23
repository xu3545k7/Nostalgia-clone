using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 練習室：左邊挑曲、中間譜架、右邊所有決策。
/// </summary>
/// <remarks>
/// **它和選曲畫面要回答的問題不一樣。** 選曲畫面問「今天彈哪一首」，練習室問「這
/// 一首我要練哪裡、怎麼練」。所以版面也反過來：曲目退到左邊一條清單，中間只留下
/// 「是這一首」的確認（曲繪、曲名、作者），右邊整欄留給決策 —— 難度、成績、速度、
/// 看譜面。
///
/// 右欄的順序就是使用者的思考順序：**難度 → 我的狀況 → 要怎麼練 → 開始**。以前這
/// 些東西全擠在選難度那張卡的一列上，每加一個功能就得從別人身上挖寬度；改成直欄
/// 之後它們是往下長，不互相搶。
///
/// 資料一份都不另外存：曲目來自 <see cref="SongSelectionManager"/>、成績來自
/// <see cref="LocalScoreRecords"/>、速度來自 <see cref="SettingsManager"/>。這個畫
/// 面只是另一種呈現 —— 兩個殼養出兩份資料是這種設計唯一真正危險的地方。
/// </remarks>
[DisallowMultipleComponent]
public sealed class PracticeRoomScreen : MonoBehaviour
{
    private const int SortingOrder = 25000;     // 低於譜面檢視器（26500），它要蓋在這上面
    private const float TopBarHeight = 92f;
    private const float Margin = 36f;
    private const float ListWidth = 520f;
    private const float RailWidth = 520f;
    private const string AllCategories = "全部";

    /// <summary>清單的排序方式。</summary>
    private enum SortMode { Name, Level, Author }

    // 三層明度，不是一種木頭。
    //
    // 上一版整間都是同一塊木紋、同一個亮度，所以看起來平 —— 哪裡是房間、哪裡是
    // 家具、哪裡是要讀的東西分不出來。草稿其實分得很清楚：**暗的房間**（牆和地
    // 板）、**中間調的面板**（櫃子、譜架）、**接近白紙的內容卡**（曲目、成績、樂
    // 譜）。眼睛會自己走到最亮的那一層，而那一層正好就是要讀的東西。
    //
    // 材質也跟著分工：房間和譜架是木頭（看得到紋），面板是深色的毛氈／皮面（吃
    // 光、不反光），內容是紙。三種材質才有房間感，一種材質只有貼圖感。
    private static readonly Color RoomWood = new Color(0.300f, 0.196f, 0.108f, 1f);
    private static readonly Color RoomWoodLit = new Color(0.620f, 0.430f, 0.245f, 1f);
    private static readonly Color WoodDeep = new Color(0.215f, 0.132f, 0.070f, 1f);
    private static readonly Color StandWood = new Color(0.585f, 0.405f, 0.228f, 1f);
    private static readonly Color Felt = new Color(0.235f, 0.150f, 0.088f, 0.985f);
    private static readonly Color FeltLight = new Color(0.300f, 0.196f, 0.115f, 0.985f);
    private static readonly Color Paper = new Color(0.945f, 0.900f, 0.800f, 1f);
    private static readonly Color PaperDim = new Color(0.880f, 0.825f, 0.715f, 1f);
    private static readonly Color Ink = new Color(0.185f, 0.108f, 0.055f, 1f);
    private static readonly Color InkMuted = new Color(0.385f, 0.262f, 0.155f, 1f);
    private static readonly Color InkCream = new Color(0.955f, 0.900f, 0.782f, 1f);
    private static readonly Color InkCreamMuted = new Color(0.760f, 0.672f, 0.535f, 1f);
    private static readonly Color Gold = new Color(0.800f, 0.605f, 0.265f, 1f);
    private static readonly Color ButtonPrimary = new Color(0.905f, 0.735f, 0.380f, 1f);

    // 金屬靠的是「暗底＋一條亮邊」，不是貼圖。桌板黑得下去，邊緣才反得起來。
    private static readonly Color Metal = new Color(0.145f, 0.140f, 0.150f, 1f);
    private static readonly Color MetalLit = new Color(0.320f, 0.315f, 0.330f, 1f);
    private static readonly Color MetalEdge = new Color(0.720f, 0.715f, 0.700f, 1f);

    private static Sprite woodSprite;
    private static Sprite metalSprite;

    public static PracticeRoomScreen Instance { get; private set; }

    /// <summary>
    /// 練習室現在選著的那一首（含難度）。設定裡的預覽要照這個走。
    /// </summary>
    /// <remarks>
    /// 練習室的選擇是自己的欄位，和選歌轉盤的 currentIndex／songScroller 無關，
    /// 所以預覽只看那邊的話，在練習室裡叫出設定會顯示完全不相干的一首。
    /// </remarks>
    public static SongSelectionManager.SongOption CurrentSelectionForPreview()
    {
        PracticeRoomScreen screen = Instance;
        if (screen == null || !screen.isActiveAndEnabled) return null;
        return screen.selectedVariant ?? screen.selectedSong;
    }

    private readonly List<SongSelectionManager.SongOption> songs =
        new List<SongSelectionManager.SongOption>();
    private readonly List<SongSelectionManager.SongOption> filtered =
        new List<SongSelectionManager.SongOption>();
    private readonly List<Image> rowPlates = new List<Image>();
    private readonly List<SongSelectionManager.SongOption> rowSongs =
        new List<SongSelectionManager.SongOption>();
    private readonly List<Button> difficultyButtons = new List<Button>();
    private readonly List<SongSelectionManager.SongOption> difficultyVariantsShown =
        new List<SongSelectionManager.SongOption>();
    private readonly List<TextMeshProUGUI> difficultyNames = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> difficultyLevels = new List<TextMeshProUGUI>();
    private readonly List<Color> difficultyTints = new List<Color>();

    private RectTransform rootRect;
    private GameObject watchedSettingsPanel;
    private RectTransform listContent;
    private TextMeshProUGUI categoryLabel;
    private TextMeshProUGUI sortLabel;
    private SortArrowGlyph sortArrow;
    private SortMode sortMode = SortMode.Name;
    private bool sortDescending;
    private bool showTutorials;
    private Button tutorialButton;
    private SongDifficultyStrip.Tier tierFilter = SongDifficultyStrip.Tier.Normal;
    private readonly List<Button> tierButtons = new List<Button>();
    private readonly List<string> categories = new List<string>();
    private int categoryIndex;
    private TMP_InputField searchField;
    private TextMeshProUGUI listCount;
    private Image coverImage;
    private TextMeshProUGUI standTitle;
    private TextMeshProUGUI standAuthor;
    private RectTransform difficultyRow;
    private TextMeshProUGUI scoreValue;
    private TextMeshProUGUI scoreRank;
    private TextMeshProUGUI speedValue;
    private TextMeshProUGUI analysisText;
    private TextMeshProUGUI trendNote;
    private ChartTrendGraphic trend;
    private ChartRadarGraphic radar;
    private ChartDensityGraphic density;
    private ChartBpmGraphic bpm;
    private string analysisChartFile;
    private string hotspotChartFile;
    private TextMeshProUGUI hintLabel;
    private Button startButton;

    private AudioSource previewSource;
    private int previewToken;

    private SongSelectionManager.SongOption selectedSong;      // 群組（歌）
    private SongSelectionManager.SongOption selectedVariant;   // 難度

    public static void Open()
    {
        if (Instance != null)
        {
            Destroy(Instance.gameObject);
            Instance = null;
        }
        var go = new GameObject("PracticeRoomScreen");
        go.AddComponent<PracticeRoomScreen>();
    }

    private void Awake()
    {
        Instance = this;
        // 進到這個房間就是練習模式：速度、不計分都由它決定。使用者不該還要去別的
        // 地方勾一個開關，這裡的速度才有作用。
        try
        {
            var settings = SettingsManager.Instance;
            if (settings != null && !settings.PracticeMode) settings.SetPracticeMode(true);
        }
        catch { }
        try
        {
            int tier = SettingsManager.Instance != null
                ? SettingsManager.Instance.PreferredDifficultyTier : -1;
            if (tier >= 0) tierFilter = (SongDifficultyStrip.Tier)tier;
        }
        catch { }
        Build();
        RefreshTierButtons();
        LoadSongs();
        // 練習室是全螢幕的，底下的選曲畫面不必繼續跑版面和試聽。
        try { SongSelectionManager.Instance?.HideSelectionPanelOnly(); } catch { }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>切到演奏會：設定改掉、練習室收起來，回到選曲轉盤。</summary>
    private void SwitchToRecital()
    {
        var settings = SettingsManager.Instance;
        if (settings != null)
        {
            settings.SetRecitalMode(true);
            // 練習模式和演奏會是互斥的：一個可以調速度、不計分，一個要正式計分。
            if (settings.PracticeMode) settings.SetPracticeMode(false);
        }
        Close();
    }

    /// <summary>
    /// 把房間自己那份試聽停掉。
    /// </summary>
    /// <remarks>
    /// 練習室自己開了一個 AudioSource（見 PlayPreview），不走 Conductor，所以
    /// 別人把「正在播的那一場」停掉時它不會跟著停——開設定預覽時兩首就疊在一起。
    /// 開放一個靜態入口給那些不該認識練習室內部的呼叫端。
    /// </remarks>
    public static void StopPreviewAudio()
    {
        PracticeRoomScreen screen = Instance;
        if (screen == null) return;
        screen.previewToken++;
        if (screen.previewSource != null) screen.previewSource.Stop();
    }

    public void Close()
    {
        previewToken++;
        if (previewSource != null) previewSource.Stop();
        try { SongSelectionManager.Instance?.ShowSelectionPanelOnly(); } catch { }
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------
    // 版面
    // ------------------------------------------------------------------

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = AddRect("Root", transform);
        rootRect = root;
        Stretch(root);
        Image backdrop = root.gameObject.AddComponent<Image>();
        backdrop.sprite = WoodSprite();
        backdrop.type = Image.Type.Tiled;
        backdrop.color = RoomWood;

        // 房間的深度只用暗角。
        //
        // 原本中間還放了一團放射狀的光暈當「燈」，但那張 sprite 的邊界在木紋上看
        // 得出來，變成畫框後面浮著一圈髒髒的光 —— 與其修它的半徑，不如拿掉：暗角
        // 本來就足夠把視線收到中間。
        RectTransform vignette = AddRect("Vignette", root);
        Stretch(vignette);
        Image shade = vignette.gameObject.AddComponent<Image>();
        shade.sprite = SettingsUiKit.VignetteSprite();
        shade.color = new Color(0.10f, 0.055f, 0.025f, 0.80f);
        shade.raycastTarget = false;

        BuildTopBar(root);
        BuildSongList(root);
        BuildStand(root);
        BuildRail(root);
    }

    private void BuildTopBar(RectTransform parent)
    {
        RectTransform bar = AddRect("TopBar", parent);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(0f, -TopBarHeight);
        bar.offsetMax = Vector2.zero;
        Image plate = bar.gameObject.AddComponent<Image>();
        plate.sprite = WoodSprite();
        plate.type = Image.Type.Tiled;
        plate.color = WoodDeep;

        // 左上角是模式切換，不是「返回」—— 練習室和演奏會是兩個空間，互為彼此的
        // 出口。選曲轉盤是演奏會那一側的畫面，所以切過去就等於回去了。
        Button toRecital = MakeButton(bar, "ToRecital", "‹  切換至演奏會", 22f, new Vector2(240f, 56f));
        Place(toRecital.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(Margin, 0f), new Vector2(240f, 56f));
        toRecital.onClick.AddListener(SwitchToRecital);

        TextMeshProUGUI title = AddText(bar, "Title", "練 習 室", 34f, InkCream,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(Margin + 270f, 0f), new Vector2(420f, 52f));
        title.characterSpacing = 8f;

        // 設定：練習室是一個完整的畫面，不能為了改一個設定還要先切回演奏會。
        Button settings = MakeButton(bar, "Settings", "設 定", 20f, new Vector2(120f, 56f));
        Place(settings.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-Margin, 0f), new Vector2(120f, 56f));
        settings.onClick.AddListener(OpenSettings);

        hintLabel = AddText(bar, "Hint", string.Empty, 20f, InkCreamMuted,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal);
        Place(hintLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-(Margin + 140f), 0f), new Vector2(620f, 40f));
    }

    private void BuildSongList(RectTransform parent)
    {
        RectTransform panel = MakePanel(parent, "SongList", Felt);
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.offsetMin = new Vector2(Margin, Margin);
        panel.offsetMax = new Vector2(Margin + ListWidth, -(TopBarHeight + Margin));

        TextMeshProUGUI heading = AddText(panel, "Heading", "曲 目", 26f, InkCream,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -20f), new Vector2(240f, 40f));
        heading.characterSpacing = 6f;

        listCount = AddText(panel, "Count", string.Empty, 20f, InkCreamMuted,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal);
        Place(listCount.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -20f), new Vector2(110f, 40f));

        // 分類：接在「曲目」右邊的一個小切換器。
        //
        // 選曲畫面上它是一整條橫幅，因為那裡的主角就是「今天要挑什麼」；練習室的
        // 主角是手上這一份譜，分類退成標題旁的一個修飾就好 —— 但不能拿掉，一百多
        // 首只靠捲動太慢。
        LoadCategories();
        Button categoryPrev = MakeButton(panel, "CategoryPrev", "‹", 20f, new Vector2(32f, 32f));
        Place(categoryPrev.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(104f, -22f), new Vector2(32f, 32f));
        categoryPrev.onClick.AddListener(() => StepCategory(-1));

        categoryLabel = AddText(panel, "Category", string.Empty, 18f, InkCream,
            TextAlignmentOptions.Center, FontStyles.Bold);
        Place(categoryLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(140f, -22f), new Vector2(160f, 32f));

        Button categoryNext = MakeButton(panel, "CategoryNext", "›", 20f, new Vector2(32f, 32f));
        Place(categoryNext.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(304f, -22f), new Vector2(32f, 32f));
        categoryNext.onClick.AddListener(() => StepCategory(1));

        // 排序：分類決定「有哪些」，排序決定「先看到哪一首」，兩件事分開。
        Button sortButton = MakeButton(panel, "Sort", string.Empty, 16f, new Vector2(130f, 32f));
        RectTransform sortRect = sortButton.GetComponent<RectTransform>();
        Place(sortRect, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -60f), new Vector2(130f, 32f));
        RectTransform funnelRect = AddRect("Funnel", sortRect);
        Place(funnelRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(10f, 0f), new Vector2(18f, 18f));
        funnelRect.gameObject.AddComponent<CanvasRenderer>();
        var funnel = funnelRect.gameObject.AddComponent<FunnelGlyph>();
        funnel.color = Ink;
        funnel.raycastTarget = false;
        sortLabel = AddText(sortRect, "SortLabel", string.Empty, 16f, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(sortLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(34f, 0f), new Vector2(92f, 26f));
        sortButton.onClick.AddListener(StepSort);

        // 升冪／降冪分開一顆：排序的「依據」和「方向」是兩個決定，擠在同一顆上
        // 就得按三下才回得到原本的依據。
        Button orderButton = MakeButton(panel, "SortOrder", string.Empty, 16f, new Vector2(38f, 32f));
        RectTransform orderRect = orderButton.GetComponent<RectTransform>();
        Place(orderRect, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-160f, -60f), new Vector2(38f, 32f));
        RectTransform arrowRect = AddRect("Arrow", orderRect);
        Place(arrowRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(16f, 18f));
        arrowRect.gameObject.AddComponent<CanvasRenderer>();
        sortArrow = arrowRect.gameObject.AddComponent<SortArrowGlyph>();
        sortArrow.color = Ink;
        sortArrow.raycastTarget = false;
        orderButton.onClick.AddListener(() =>
        {
            sortDescending = !sortDescending;
            if (sortArrow != null) sortArrow.PointsDown = sortDescending;
            RefreshList();
        });

        // 新手教學：平常不混在曲目裡（它們是課，不是曲子），要練的時候按這一顆。
        tutorialButton = MakeButton(panel, "Tutorial", "新 手 教 學", 16f, new Vector2(150f, 32f));
        Place(tutorialButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -60f), new Vector2(150f, 32f));
        tutorialButton.onClick.AddListener(() =>
        {
            showTutorials = !showTutorials;
            RefreshTutorialButton();
            RefreshList();
            if (filtered.Count > 0) SelectSong(filtered[0]);
        });
        RefreshTutorialButton();

        // 搜尋。110 首以上的曲庫沒有搜尋就只能一直捲。
        RectTransform searchRect = AddRect("Search", panel);
        Place(searchRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -124f), new Vector2(ListWidth - 48f, 52f));
        Image searchPlate = searchRect.gameObject.AddComponent<Image>();
        searchPlate.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        searchPlate.type = Image.Type.Tiled;
        searchPlate.color = new Color(0.955f, 0.905f, 0.800f, 0.92f);
        var searchEdge = searchRect.gameObject.AddComponent<Outline>();
        searchEdge.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
        searchEdge.effectDistance = new Vector2(1f, -1f);

        TextMeshProUGUI searchText = AddText(searchRect, "Text", string.Empty, 22f, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        Stretch(searchText.rectTransform, 16f, 4f, 16f, 4f);
        TextMeshProUGUI placeholder = AddText(searchRect, "Placeholder", "搜尋曲名或作曲家", 22f,
            new Color(InkMuted.r, InkMuted.g, InkMuted.b, 0.6f),
            TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
        Stretch(placeholder.rectTransform, 16f, 4f, 16f, 4f);

        searchField = searchRect.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = searchRect;
        searchField.textComponent = searchText;
        searchField.placeholder = placeholder;
        searchField.targetGraphic = searchPlate;
        searchField.onValueChanged.AddListener(_ => RefreshList());

        // 清單本體
        RectTransform viewport = AddRect("Viewport", panel);
        Stretch(viewport, 16f, 74f, 16f, 190f);
        // 用 RectMask2D，不是 Mask。
        //
        // Mask 是靠一張圖的 alpha 寫 stencil 的，而我給的是 alpha ≈ 0 的透明圖 ——
        // 結果不是「看得到但被裁到框內」，是整塊都被裁掉（清單看起來完全是空的，
        // 但 127 首明明有數到）。RectMask2D 只吃矩形範圍，不需要任何圖。
        viewport.gameObject.AddComponent<RectMask2D>();

        listContent = AddRect("Content", viewport);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = new Vector2(1f, 1f);
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.offsetMin = new Vector2(0f, 0f);
        listContent.offsetMax = new Vector2(0f, 0f);
        var layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        var fitter = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 下緣的難度階：按哪一階，整份清單就用那一階的等級標示與排序。
        //
        // 這不是篩選（沒有那一階的曲子仍然留著、只是標成「–」），而是**視角**：
        // 「我現在練的是 EXPERT」這件事會同時決定清單上看到的數字、等級排序的依
        // 據，還有點進去預設選中的那一份譜。它存進 PreferredDifficultyTier，所以
        // 和選曲畫面的寶石牌共用同一個偏好。
        var tiers = new[]
        {
            SongDifficultyStrip.Tier.Normal, SongDifficultyStrip.Tier.Hard,
            SongDifficultyStrip.Tier.Expert, SongDifficultyStrip.Tier.Real
        };
        float tierWidth = (ListWidth - 48f - 3f * 8f) / 4f;
        for (int i = 0; i < tiers.Length; i++)
        {
            SongDifficultyStrip.Tier tier = tiers[i];
            Button button = MakeButton(panel, "Tier" + tier, tier.ToString().ToUpperInvariant(),
                15f, new Vector2(tierWidth, 44f));
            Place(button.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f + i * (tierWidth + 8f), 16f), new Vector2(tierWidth, 44f));
            button.onClick.AddListener(() => SetTier(tier));
            tierButtons.Add(button);
        }

        var scroll = panel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = listContent;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
    }

    private void BuildStand(RectTransform parent)
    {
        RectTransform centre = AddRect("Stand", parent);
        centre.anchorMin = new Vector2(0f, 0f);
        centre.anchorMax = new Vector2(1f, 1f);
        centre.offsetMin = new Vector2(Margin * 2f + ListWidth, Margin);
        centre.offsetMax = new Vector2(-(Margin * 2f + RailWidth), -(TopBarHeight + Margin));

        // 中間是一幅掛起來的畫：外框、卡紙、曲繪，底下曲名和作者。
        //
        // 譜架那一版做得出來，但它把注意力吃光了 —— 中欄要講的只有「是這一首」，
        // 器材本身不該比封面搶眼。畫框相反：它的作用就是把視線往內收。
        RectTransform frameRect = AddRect("Frame", centre);
        Place(frameRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 236f), new Vector2(392f, 392f));
        frameRect.gameObject.AddComponent<CanvasRenderer>();
        var frameGraphic = frameRect.gameObject.AddComponent<PictureFrameGraphic>();
        frameGraphic.raycastTarget = false;

        RectTransform coverRect = AddRect("Cover", frameRect);
        // 卡紙的留白：框內再退一圈，畫才不會頂到框。框變窄了，留白也跟著收。
        Stretch(coverRect, 26f, 26f, 26f, 26f);
        coverImage = coverRect.gameObject.AddComponent<Image>();
        coverImage.color = new Color(1f, 1f, 1f, 0.12f);
        coverImage.preserveAspect = true;
        var coverShadow = coverRect.gameObject.AddComponent<Shadow>();
        coverShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        coverShadow.effectDistance = new Vector2(0f, -4f);

        standTitle = AddText(centre, "Title", string.Empty, 40f, InkCream,
            TextAlignmentOptions.Center, FontStyles.Bold);
        AddSoftShadow(standTitle);
        Place(standTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -16f), new Vector2(640f, 56f));
        // 長曲名很多（日文全形又特別寬），固定字級一定會撞到左右兩塊面板。讓它自
        // 己縮，縮到 24 還放不下才截斷。
        standTitle.enableAutoSizing = true;
        standTitle.fontSizeMin = 24f;
        standTitle.fontSizeMax = 40f;
        standTitle.overflowMode = TextOverflowModes.Ellipsis;

        standAuthor = AddText(centre, "Author", string.Empty, 24f, InkCreamMuted,
            TextAlignmentOptions.Center, FontStyles.Italic);
        AddSoftShadow(standAuthor);
        Place(standAuthor.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -58f), new Vector2(640f, 36f));
        standAuthor.enableAutoSizing = true;
        standAuthor.fontSizeMin = 16f;
        standAuthor.fontSizeMax = 24f;
        standAuthor.overflowMode = TextOverflowModes.Ellipsis;

        // 難度、成績、譜面預覽：跟著曲子走的東西放在曲子底下。
        //
        // 右欄留給分析，因為那是「看資料」；這三樣是「選哪一份、我打得怎樣、先看
        // 一眼」—— 它們和中間那張封面是同一件事的三個面向，分開兩欄反而要來回看。
        float centreWidth = 640f;
        difficultyRow = MakeCard(centre, "Difficulties", -88f, 84f, centreWidth);
        difficultyRow.anchorMin = difficultyRow.anchorMax = new Vector2(0.5f, 0.5f);
        difficultyRow.pivot = new Vector2(0.5f, 1f);
        difficultyRow.anchoredPosition = new Vector2(0f, -88f);

        RectTransform scoreCard = MakeCard(centre, "ScoreCard", -200f, 74f, centreWidth);
        scoreCard.anchorMin = scoreCard.anchorMax = new Vector2(0.5f, 0.5f);
        scoreCard.pivot = new Vector2(0.5f, 1f);
        scoreCard.anchoredPosition = new Vector2(0f, -200f);

        TextMeshProUGUI scoreCaption = AddText(scoreCard, "Caption", "最 佳 成 績", 17f, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(scoreCaption.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(24f, 0f), new Vector2(160f, 40f));
        scoreCaption.characterSpacing = 4f;

        scoreValue = AddText(scoreCard, "Score", "無紀錄", 40f, Ink,
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(scoreValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-96f, 0f), new Vector2(320f, 52f));

        scoreRank = AddText(scoreCard, "Rank", string.Empty, 36f, Gold,
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(scoreRank.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f), new Vector2(72f, 52f));

        // 整首的走勢：密度、難點、還有你自己的分數曲線。
        //
        // 這是練習室和選曲畫面最大的差別 —— 選曲問「要不要打這首」，練習室問「這
        // 首的哪裡會掉分」。密度和難點是譜面自己的性質，分數曲線是你的，兩者疊在
        // 同一條時間軸上，落差就是該練的地方。
        RectTransform trendCard = MakeCard(centre, "Trend", 0f, 176f, centreWidth);
        trendCard.anchorMin = trendCard.anchorMax = new Vector2(0.5f, 0.5f);
        trendCard.pivot = new Vector2(0.5f, 1f);
        trendCard.anchoredPosition = new Vector2(0f, -284f);

        TextMeshProUGUI trendCaption = AddText(trendCard, "Caption", "密 度 走 勢 ・ 難 點 ・ 得 分",
            15f, InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(trendCaption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -10f), new Vector2(360f, 22f));
        trendCaption.characterSpacing = 2f;

        trendNote = AddText(trendCard, "Note", string.Empty, 14f, InkMuted,
            TextAlignmentOptions.MidlineRight, FontStyles.Italic);
        Place(trendNote.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-18f, -10f), new Vector2(260f, 22f));

        RectTransform trendRect = AddRect("Graphic", trendCard);
        Stretch(trendRect, 18f, 14f, 18f, 38f);
        trendRect.gameObject.AddComponent<CanvasRenderer>();
        trend = trendRect.gameObject.AddComponent<ChartTrendGraphic>();
        trend.raycastTarget = false;

    }

    private void BuildRail(RectTransform parent)
    {
        RectTransform rail = MakePanel(parent, "Rail", Felt);
        rail.anchorMin = new Vector2(1f, 0f);
        rail.anchorMax = new Vector2(1f, 1f);
        rail.pivot = new Vector2(1f, 0.5f);
        rail.offsetMin = new Vector2(-(Margin + RailWidth), Margin);
        rail.offsetMax = new Vector2(-Margin, -(TopBarHeight + Margin));

        float cardWidth = RailWidth - 48f;
        float y = -24f;

        // 譜面分析：這一份譜長什麼樣子。
        //
        // 三張圖各回答一個問題 —— 雷達說「靠什麼技巧」、密度說「哪一段最擠、兩手
        // 怎麼分」、BPM 曲線說「速度變不變」。文字那一行是給不想讀圖的人的摘要。
        y = AddSectionHeading(rail, "譜 面 分 析", y);

        // 譜面預覽接在分析標題旁：它們回答的是同一個問題（這份譜長什麼樣），只是
        // 一個用數字、一個用眼睛。
        Button viewChart = MakeButton(rail, "ViewChart", "預 覽", 18f, new Vector2(104f, 38f));
        Place(viewChart.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -20f), new Vector2(104f, 38f));
        viewChart.onClick.AddListener(OpenChartViewer);

        // 分析區用**比例**切，不是寫死高度。
        //
        // 寫死的話，畫面短一點（或之後加一張圖）最底下那張就會被切掉 —— 上一版的
        // BPM 曲線連刻度都露不出來。這裡改成先框出「標題到速度卡之間」的那一塊，
        // 再把四張卡按比例分掉，所以任何高度都排得下。
        RectTransform analysisArea = AddRect("AnalysisArea", rail);
        analysisArea.anchorMin = new Vector2(0.5f, 0f);
        analysisArea.anchorMax = new Vector2(0.5f, 1f);
        analysisArea.pivot = new Vector2(0.5f, 0.5f);
        analysisArea.sizeDelta = new Vector2(cardWidth, 0f);
        analysisArea.offsetMin = new Vector2(-cardWidth * 0.5f, 232f);   // 讓出速度＋開始
        analysisArea.offsetMax = new Vector2(cardWidth * 0.5f, y - 8f);

        RectTransform summaryCard = MakeStretchCard(analysisArea, "Summary", 0.80f, 1.00f);
        analysisText = AddText(summaryCard, "Text", "分析中…", 19f, Ink,
            TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Stretch(analysisText.rectTransform, 18f, 12f, 18f, 12f);
        analysisText.textWrappingMode = TextWrappingModes.Normal;
        analysisText.enableAutoSizing = true;
        analysisText.fontSizeMin = 14f;
        analysisText.fontSizeMax = 19f;

        RectTransform radarCard = MakeStretchCard(analysisArea, "Radar", 0.36f, 0.775f);
        RectTransform radarRect = AddRect("Graphic", radarCard);
        Stretch(radarRect, 14f, 14f, 14f, 14f);
        radarRect.gameObject.AddComponent<CanvasRenderer>();
        radar = radarRect.gameObject.AddComponent<ChartRadarGraphic>();
        radar.raycastTarget = false;
        radar.Ink = Ink;

        RectTransform densityCard = MakeStretchCard(analysisArea, "Density", 0.185f, 0.335f);
        AddCardCaption(densityCard, "密度／左右手");
        RectTransform densityRect = AddRect("Graphic", densityCard);
        Stretch(densityRect, 14f, 12f, 14f, 32f);
        densityRect.gameObject.AddComponent<CanvasRenderer>();
        density = densityRect.gameObject.AddComponent<ChartDensityGraphic>();
        density.raycastTarget = false;

        RectTransform bpmCard = MakeStretchCard(analysisArea, "Bpm", 0f, 0.16f);
        AddCardCaption(bpmCard, "BPM");
        RectTransform bpmRect = AddRect("Graphic", bpmCard);
        // BPM 的刻度數字畫在曲線左邊，所以左邊要多留一點，底下也要留給最低值。
        Stretch(bpmRect, 20f, 16f, 14f, 32f);
        bpmRect.gameObject.AddComponent<CanvasRenderer>();
        bpm = bpmRect.gameObject.AddComponent<ChartBpmGraphic>();
        bpm.raycastTarget = false;

        // 速度和開始固定在最底下：不管分析長多高，手要按的東西永遠在同一個位置。
        startButton = MakeButton(rail, "Start", "開 始 練 習", 28f,
            new Vector2(cardWidth, 80f), true);
        Place(startButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 24f), new Vector2(cardWidth, 80f));
        startButton.onClick.AddListener(StartPractice);

        // 卡片高一點，標題放進卡片內。上一版標題壓在卡片上緣，被上面的元件切掉一半。
        RectTransform speedRow = MakeCard(rail, "SpeedCard", 0f, 96f, cardWidth);
        speedRow.anchorMin = speedRow.anchorMax = new Vector2(0.5f, 0f);
        speedRow.pivot = new Vector2(0.5f, 0f);
        speedRow.anchoredPosition = new Vector2(0f, 118f);

        TextMeshProUGUI speedCaption = AddText(speedRow, "Caption", "速 度", 16f, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(speedCaption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -10f), new Vector2(120f, 22f));
        speedCaption.characterSpacing = 4f;

        Button slower = MakeButton(speedRow, "Slower", "−", 28f, new Vector2(64f, 56f));
        Place(slower.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(14f, -12f), new Vector2(64f, 54f));
        slower.onClick.AddListener(() => NudgeSpeed(-0.05f));

        speedValue = AddText(speedRow, "Value", "1.00×", 30f, Ink,
            TextAlignmentOptions.Center, FontStyles.Bold);
        Place(speedValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-12f, -12f), new Vector2(170f, 54f));

        Button faster = MakeButton(speedRow, "Faster", "+", 28f, new Vector2(64f, 56f));
        Place(faster.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-112f, -12f), new Vector2(64f, 54f));
        faster.onClick.AddListener(() => NudgeSpeed(0.05f));

        Button reset = MakeButton(speedRow, "Reset", "原速", 20f, new Vector2(84f, 56f));
        Place(reset.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-14f, -12f), new Vector2(84f, 54f));
        reset.onClick.AddListener(() => SetSpeed(1f));
    }

    /// <summary>把這一份譜的分析填進右欄。沒有現成的就背景算一份。</summary>
    private void RefreshAnalysis()
    {
        string chartFile = selectedVariant != null ? selectedVariant.chartFileName : null;
        analysisChartFile = chartFile;
        if (string.IsNullOrEmpty(chartFile))
        {
            ApplyAnalysis(null, chartFile);
            return;
        }

        if (ChartAnalysisCache.TryGet(chartFile, out ChartAnalysis cached))
        {
            ApplyAnalysis(cached, chartFile);
            return;
        }

        SettingsUiKit.SetText(analysisText, "分析中…");
        StartCoroutine(ChartAnalysisCache.Build(chartFile,
            result => ApplyAnalysis(result, chartFile)));
    }

    private void ApplyAnalysis(ChartAnalysis analysis, string forChartFile)
    {
        // 分析是非同步的，回來的時候使用者可能已經換了難度。
        if (!string.Equals(analysisChartFile, forChartFile)) return;

        if (analysis == null)
        {
            SettingsUiKit.SetText(analysisText, "這一份譜沒有分析資料");
            trend?.Clear();
            SettingsUiKit.SetText(trendNote, string.Empty);
            radar?.Clear();
            density?.Clear();
            bpm?.Clear();
            return;
        }

        string techniques = string.Empty;
        string character = string.Empty;
        try { techniques = ChartAnalysisVocabulary.DescribeTechniques(analysis); } catch { }
        try { character = ChartAnalysisVocabulary.DescribeCharacter(analysis); } catch { }

        SettingsUiKit.SetText(analysisText,
            $"音符 {analysis.noteCount:N0}　長押 {analysis.holdCount:N0}\n" +
            $"平均 {analysis.averageDensity:0.0}/s　峰值 {analysis.peakDensity:0}/s\n" +
            (string.IsNullOrEmpty(techniques) ? character : techniques));

        radar?.SetData(analysis);
        if (trend != null)
        {
            trend.SetChart(analysis.leftDensity, analysis.rightDensity, analysis.peakBucketTotal);
            // 每小節的得分要在遊玩時記錄下來才有；目前還沒有那一份資料。
            trend.SetPlayerScores(null, null);
            SettingsUiKit.SetText(trendNote, "尚無遊玩紀錄");
        }
        RefreshHotspots(forChartFile);
        density?.SetData(analysis.leftDensity, analysis.rightDensity, analysis.peakBucketTotal);
        if (bpm != null)
        {
            var settings = SettingsManager.Instance;
            bpm.Scale = settings != null ? settings.PracticeSpeed : 1f;
            bpm.SetData(analysis);
        }
    }

    /// <summary>照比例卡在容器裡的紙卡：容器多高，它就跟著多高。</summary>
    private static RectTransform MakeStretchCard(RectTransform parent, string name,
        float anchorBottom, float anchorTop)
    {
        RectTransform rect = AddRect(name, parent);
        rect.anchorMin = new Vector2(0f, anchorBottom);
        rect.anchorMax = new Vector2(1f, anchorTop);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        image.type = Image.Type.Tiled;
        image.color = Paper;
        var shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.05f, 0.025f, 0.01f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -3f);
        return rect;
    }

    private static void AddCardCaption(RectTransform card, string text)
    {
        TextMeshProUGUI caption = AddText(card, "Caption", text, 15f, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(16f, -8f), new Vector2(260f, 22f));
    }

    private float AddSectionHeading(RectTransform parent, string text, float y)
    {
        TextMeshProUGUI heading = AddText(parent, "H_" + text, text, 20f, Gold,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(RailWidth - 48f, 32f));
        heading.characterSpacing = 6f;

        // 標題底下一條淡金的細線：右欄一路往下都是方塊，沒有分隔就會糊成一片。
        RectTransform rule = AddRect("Rule_" + text, parent);
        Place(rule, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y - 20f), new Vector2(RailWidth - 48f, 1f));
        Image ruleImage = rule.gameObject.AddComponent<Image>();
        ruleImage.sprite = SettingsUiKit.RuleSprite(true);
        ruleImage.color = new Color(Gold.r, Gold.g, Gold.b, 0.30f);
        ruleImage.raycastTarget = false;

        return y - 44f;
    }

    // ------------------------------------------------------------------
    // 資料
    // ------------------------------------------------------------------

    private void LoadSongs()
    {
        songs.Clear();
        var manager = SongSelectionManager.Instance;
        if (manager != null)
        {
            // 去重。
            //
            // 同一首歌可能以不同的入口出現在名單裡（分類重複、匯入時的別名、或是
            // 每個難度各自一筆但群組代表沒收乾淨）。轉盤靠分類篩選掩蓋了這件事，
            // 練習室是一整條清單，重複的就直接看得到。用「曲名＋作者」認人，因為
            // 那正是使用者眼中的「同一首」。
            var seen = new HashSet<string>();
            foreach (var song in manager.AllSongs)
            {
                if (song == null) continue;
                string key = ((song.displayName ?? string.Empty) + "|" + (song.author ?? string.Empty))
                    .Trim().ToLowerInvariant();
                if (!seen.Add(key)) continue;
                songs.Add(song);
            }
        }
        RefreshList();
        if (filtered.Count > 0) SelectSong(filtered[0]);
    }

    /// <summary>分類清單：第一個永遠是「全部」。</summary>
    private void LoadCategories()
    {
        categories.Clear();
        categories.Add(AllCategories);
        var manager = SongSelectionManager.Instance;
        if (manager != null)
        {
            try
            {
                foreach (string category in manager.GetAvailableSongCategories())
                {
                    if (!string.IsNullOrWhiteSpace(category) && !categories.Contains(category))
                        categories.Add(category);
                }
            }
            catch { }
        }
        categoryIndex = 0;
    }

    private void RefreshTutorialButton()
    {
        if (tutorialButton == null) return;
        Image plate = tutorialButton.targetGraphic as Image;
        if (plate != null) plate.color = showTutorials ? ButtonPrimary : PaperDim;
        TextMeshProUGUI label = tutorialButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = Ink;
    }

    private void StepSort()
    {
        sortMode = sortMode == SortMode.Name ? SortMode.Level
            : sortMode == SortMode.Level ? SortMode.Author : SortMode.Name;
        RefreshList();
    }

    private void SetTier(SongDifficultyStrip.Tier tier)
    {
        tierFilter = tier;
        try { SettingsManager.Instance?.SetPreferredDifficultyTier((int)tier); } catch { }
        RefreshTierButtons();
        RefreshList();
        // 換了階之後，當下這首也要跟著換到那一份譜。
        if (selectedSong != null) BuildDifficultyButtons();
    }

    private void RefreshTierButtons()
    {
        var tiers = new[]
        {
            SongDifficultyStrip.Tier.Normal, SongDifficultyStrip.Tier.Hard,
            SongDifficultyStrip.Tier.Expert, SongDifficultyStrip.Tier.Real
        };
        for (int i = 0; i < tierButtons.Count && i < tiers.Length; i++)
        {
            Button button = tierButtons[i];
            if (button == null) continue;
            bool active = tiers[i] == tierFilter;
            Image plate = button.targetGraphic as Image;
            if (plate != null)
            {
                plate.color = active ? SongDifficultyStrip.ColourOf(tiers[i]) : PaperDim;
            }
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = active ? InkCream : InkMuted;
        }
    }

    /// <summary>這一份譜算不算在指定的那一階裡。</summary>
    /// <remarks>
    /// **REAL 要寬鬆。** NORMAL／HARD／EXPERT 幾乎每份譜都照著叫，但最高的那一階
    /// 各家取名各有各的（MALICIOUS、MASTER、LUNATIC、ANOTHER……），`Classify` 認不
    /// 出來就一律丟進 Special。使用者要看的是「這首最難的那一份」，不是字面上有沒
    /// 有寫 real，所以 REAL 這一階把 Special 一起收編。
    ///
    /// 其餘三階維持嚴格比對：把叫不出名字的譜算進 HARD 只會讓等級排序變得沒有意義。
    /// </remarks>
    private bool MatchesTier(SongSelectionManager.SongOption variant,
        SongDifficultyStrip.Tier tier)
    {
        if (variant == null) return false;
        SongDifficultyStrip.Tier actual = SongDifficultyStrip.TierOf(variant.difficultyName);
        if (actual == tier) return true;
        return tier == SongDifficultyStrip.Tier.Real &&
               actual == SongDifficultyStrip.Tier.Special;
    }

    /// <summary>這一首在目前這一階有沒有譜、等級是多少（沒有就回 -1）。</summary>
    private int LevelForTier(SongSelectionManager.SongOption song)
    {
        if (song == null) return -1;
        List<SongSelectionManager.SongOption> variants = song.difficultyVariants;
        if (variants == null || variants.Count == 0)
        {
            return MatchesTier(song, tierFilter) ? song.difficultyLevel : -1;
        }
        // 同一階可能有兩份（例如 MASTER 和 LUNATIC 都落在 REAL），取等級最高的那份。
        int best = -1;
        foreach (var variant in variants)
        {
            if (!MatchesTier(variant, tierFilter)) continue;
            if (variant.difficultyLevel > best) best = variant.difficultyLevel;
        }
        return best;
    }

    private void StepCategory(int direction)
    {
        if (categories.Count == 0) return;
        categoryIndex = (categoryIndex + direction + categories.Count) % categories.Count;
        RefreshList();
    }

    private void RefreshList()
    {
        SettingsUiKit.SetText(categoryLabel,
            categories.Count > 0 ? categories[Mathf.Clamp(categoryIndex, 0, categories.Count - 1)]
                                 : AllCategories);
        filtered.Clear();
        string query = searchField != null ? searchField.text : null;
        bool filtering = !string.IsNullOrWhiteSpace(query);
        if (filtering) query = query.Trim().ToLowerInvariant();

        string category = categories.Count > 0
            ? categories[Mathf.Clamp(categoryIndex, 0, categories.Count - 1)] : AllCategories;
        bool byCategory = !string.Equals(category, AllCategories);

        foreach (var song in songs)
        {
            // 教學課程平常不混在曲目裡：它們是課，長度和難度都不能和曲子一起比。
            bool tutorial = false;
            try { tutorial = SongSelectionManager.IsTutorialSong(song); } catch { }
            if (tutorial != showTutorials) continue;
            if (byCategory && !string.Equals(song.category, category)) continue;
            if (filtering)
            {
                string name = song.displayName != null ? song.displayName.ToLowerInvariant() : string.Empty;
                string author = song.author != null ? song.author.ToLowerInvariant() : string.Empty;
                if (!name.Contains(query) && !author.Contains(query)) continue;
            }
            filtered.Add(song);
        }

        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            Destroy(listContent.GetChild(i).gameObject);
        }
        rowPlates.Clear();
        rowSongs.Clear();
        switch (sortMode)
        {
            case SortMode.Level:
                filtered.Sort((x, y) =>
                {
                    int lx = LevelForTier(x);
                    int ly = LevelForTier(y);
                    int compare = lx.CompareTo(ly);
                    return compare != 0 ? compare : CompareName(x, y);
                });
                break;
            case SortMode.Author:
                filtered.Sort((x, y) =>
                {
                    int compare = string.Compare(x.author ?? string.Empty, y.author ?? string.Empty,
                        System.StringComparison.CurrentCultureIgnoreCase);
                    return compare != 0 ? compare : CompareName(x, y);
                });
                break;
            default:
                filtered.Sort(CompareName);
                break;
        }

        if (sortDescending) filtered.Reverse();

        // 沒有這一階的曲子一律沉到最底 —— 不管照什麼排、不管升冪降冪。
        //
        // 它們在當下這個視角裡沒有等級可比，混在中間只會讓人以為排序壞了；升冪時
        // 沉底、降冪時卻浮到最上面更糟。所以方向套用完之後再穩定分一次區。
        var withTier = new List<SongSelectionManager.SongOption>(filtered.Count);
        var withoutTier = new List<SongSelectionManager.SongOption>();
        foreach (var song in filtered)
        {
            if (LevelForTier(song) > 0) withTier.Add(song); else withoutTier.Add(song);
        }
        filtered.Clear();
        filtered.AddRange(withTier);
        filtered.AddRange(withoutTier);

        SettingsUiKit.SetText(sortLabel, sortMode == SortMode.Name ? "曲名"
            : sortMode == SortMode.Level ? "等級" : "作曲");

        foreach (var song in filtered) AddSongRow(song);
        HighlightSelectedRow();
        SettingsUiKit.SetText(listCount, $"{filtered.Count} 首");
    }

    private static int CompareName(SongSelectionManager.SongOption x,
        SongSelectionManager.SongOption y)
    {
        return string.Compare(x?.displayName ?? string.Empty, y?.displayName ?? string.Empty,
            System.StringComparison.CurrentCultureIgnoreCase);
    }

    private void AddSongRow(SongSelectionManager.SongOption song)
    {
        RectTransform row = AddRect("Row", listContent);
        row.sizeDelta = new Vector2(0f, 72f);
        var element = row.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 72f;

        Image plate = row.gameObject.AddComponent<Image>();
        plate.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        plate.type = Image.Type.Tiled;
        plate.color = new Color(0.955f, 0.905f, 0.800f, 0.55f);

        // 曲名放在一個會裁切的框裡，太長就自己跑 —— 和遊戲中的曲名一樣。
        RectTransform titleClip = AddRect("TitleClip", row);
        Place(titleClip, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -8f), new Vector2(ListWidth - 150f, 34f));
        titleClip.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI title = AddText(titleClip, "Title", song.displayName, 24f, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(ListWidth - 150f, 34f));
        ClassicalBookUITheme.ApplyContentFont(title);
        title.gameObject.AddComponent<MarqueeText>().Bind(title, ListWidth - 150f);

        TextMeshProUGUI author = AddText(row, "Author", song.author, 18f, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
        Place(author.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(20f, -40f), new Vector2(ListWidth - 150f, 28f));
        ClassicalBookUITheme.ApplyContentFont(author);

        // 右緣放最高難度的等級：挑歌的時候想知道的是「這首有多難」，不是它有幾個難度。
        // 等級顯示的是**目前這一階**的等級，顏色就用那一階的顏色。
        int tierLevel = LevelForTier(song);
        TextMeshProUGUI level = AddText(row, "Level", tierLevel > 0 ? tierLevel.ToString() : "–",
            28f, tierLevel > 0 ? SongDifficultyStrip.ColourOf(tierFilter)
                               : new Color(InkMuted.r, InkMuted.g, InkMuted.b, 0.5f),
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(level.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-20f, 0f), new Vector2(80f, 42f));

        var button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.14f, 1.10f, 1.04f, 1f);
        colours.pressedColor = new Color(0.86f, 0.82f, 0.74f, 1f);
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        SongSelectionManager.SongOption captured = song;
        button.onClick.AddListener(() => SelectSong(captured));
        rowPlates.Add(plate);
        rowSongs.Add(song);
    }

    /// <summary>選到的那一列要看得出來 —— 不然使用者不知道自己點到了沒有。</summary>
    private void HighlightSelectedRow()
    {
        for (int i = 0; i < rowPlates.Count && i < rowSongs.Count; i++)
        {
            if (rowPlates[i] == null) continue;
            bool active = ReferenceEquals(rowSongs[i], selectedSong);
            rowPlates[i].color = active
                ? new Color(0.985f, 0.945f, 0.845f, 0.95f)
                : new Color(0.955f, 0.905f, 0.800f, 0.55f);
        }
    }

    private void SelectSong(SongSelectionManager.SongOption song)
    {
        selectedSong = song;
        HighlightSelectedRow();
        PlayPreview(song);
        SettingsUiKit.SetText(standTitle, song != null ? song.displayName : string.Empty);
        SettingsUiKit.SetText(standAuthor, song != null ? song.author : string.Empty);
        if (standTitle != null) ClassicalBookUITheme.ApplyContentFont(standTitle);
        if (standAuthor != null) ClassicalBookUITheme.ApplyContentFont(standAuthor);

        Sprite cover = song != null ? song.coverSprite : null;
        if (cover == null && song != null && !string.IsNullOrEmpty(song.coverResourcePath) &&
            SongSelectionManager.Instance != null)
        {
            cover = SongSelectionManager.Instance.LoadCoverSprite(song.coverResourcePath, song.displayName);
        }
        coverImage.sprite = cover;
        coverImage.color = cover != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);

        BuildDifficultyButtons();
    }

    private void BuildDifficultyButtons()
    {
        difficultyButtons.Clear();
        difficultyVariantsShown.Clear();
        difficultyNames.Clear();
        difficultyLevels.Clear();
        difficultyTints.Clear();
        for (int i = difficultyRow.childCount - 1; i >= 0; i--)
        {
            Destroy(difficultyRow.GetChild(i).gameObject);
        }
        if (selectedSong == null) return;

        List<SongSelectionManager.SongOption> variants = selectedSong.difficultyVariants;
        if (variants == null || variants.Count == 0)
        {
            variants = new List<SongSelectionManager.SongOption> { selectedSong };
        }
        else
        {
            // 難度一律由低到高。譜面登記的順序不保證是遞增的（匯入的曲目常常照檔
            // 名排），而「左邊比較簡單」是使用者不會去確認的假設。
            variants = new List<SongSelectionManager.SongOption>(variants);
            variants.RemoveAll(v => v == null);
            variants.Sort((x, y) => x.difficultyLevel.CompareTo(y.difficultyLevel));
        }

        // 按鈕寬度固定，整排置中 —— 用「卡片寬度平均分」的話，難度數量不同的曲
        // 子按鈕會忽大忽小，排起來也不會對齊。
        const float buttonWidth = 104f;
        const float gap = 8f;
        float rowWidth = variants.Count * buttonWidth + (variants.Count - 1) * gap;
        float left = -rowWidth * 0.5f;
        float width = buttonWidth;
        for (int i = 0; i < variants.Count; i++)
        {
            SongSelectionManager.SongOption variant = variants[i];
            if (variant == null) continue;
            Button button = MakeButton(difficultyRow, "Diff" + i, string.Empty, 20f,
                new Vector2(width, 72f));
            RectTransform rect = button.GetComponent<RectTransform>();
            Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(left + i * (buttonWidth + gap), 0f), new Vector2(width, 72f));

            // 難度的顏色和遊戲其他地方同一組，不另外發明。
            //
            // 強調的方式是**整塊填色＋淺色字**，不是一條細邊：五塊並排的時候，細
            // 邊的差別要瞇著眼睛找。未選的退成紙面深字，選中的整塊變成難度的顏色
            // —— 一眼就知道現在停在哪一份譜上。
            Color tint = DifficultyVisualPalette.For(variant.difficultyName, variant.difficultyLevel);
            difficultyTints.Add(tint);

            TextMeshProUGUI name = AddText(rect, "Name", ShortDifficultyName(variant), 14f, InkMuted,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f), new Vector2(width - 6f, 22f));
            name.characterSpacing = 2f;
            difficultyNames.Add(name);

            TextMeshProUGUI level = AddText(rect, "Level",
                variant.difficultyLevel > 0 ? variant.difficultyLevel.ToString() : "?", 34f, Ink,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Place(level.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(width - 6f, 44f));
            difficultyLevels.Add(level);

            SongSelectionManager.SongOption captured = variant;
            button.onClick.AddListener(() => SelectVariant(captured));
            difficultyButtons.Add(button);
            difficultyVariantsShown.Add(variant);
        }

        SongSelectionManager.SongOption initial = null;
        foreach (var variant in variants)
        {
            if (!MatchesTier(variant, tierFilter)) continue;
            if (initial == null || variant.difficultyLevel > initial.difficultyLevel) initial = variant;
        }
        if (initial == null) initial = ResolveInitialVariant(variants);
        SelectVariant(initial);
    }

    /// <summary>預設落在最高難度：練習室的人通常是來練最難的那一份。</summary>
    private static SongSelectionManager.SongOption ResolveInitialVariant(
        List<SongSelectionManager.SongOption> variants)
    {
        SongSelectionManager.SongOption best = null;
        foreach (var variant in variants)
        {
            if (variant == null) continue;
            if (best == null || variant.difficultyLevel > best.difficultyLevel) best = variant;
        }
        return best;
    }

    /// <summary>難度名只留關鍵字：欄寬只有一百出頭，全名一定被壓成一團。</summary>
    private static string ShortDifficultyName(SongSelectionManager.SongOption variant)
    {
        string name = variant != null ? variant.difficultyName : null;
        if (string.IsNullOrEmpty(name)) return "?";
        name = name.Trim();
        return name.Length <= 6 ? name.ToUpperInvariant() : name.Substring(0, 6).ToUpperInvariant();
    }

    private void SelectVariant(SongSelectionManager.SongOption variant)
    {
        selectedVariant = variant;
        RefreshAnalysis();
        // 選中的那一個描金框，其餘退回去。選擇狀態要看得出來，不能只靠成績變了。
        for (int i = 0; i < difficultyButtons.Count && i < difficultyVariantsShown.Count; i++)
        {
            Button button = difficultyButtons[i];
            if (button == null) continue;
            bool active = ReferenceEquals(difficultyVariantsShown[i], variant);
            Color tint = i < difficultyTints.Count ? difficultyTints[i] : Gold;

            Image plate = button.targetGraphic as Image;
            if (plate != null) plate.color = active ? tint : PaperDim;

            Outline outline = button.GetComponent<Outline>();
            if (outline == null) outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = active
                ? new Color(0.98f, 0.92f, 0.78f, 0.9f)
                : new Color(0.38f, 0.255f, 0.13f, 0.45f);
            outline.effectDistance = new Vector2(active ? 2f : 1f, active ? -2f : -1f);

            if (i < difficultyLevels.Count && difficultyLevels[i] != null)
            {
                difficultyLevels[i].color = active ? InkCream : Ink;
                difficultyLevels[i].fontSize = active ? 38f : 34f;
            }
            if (i < difficultyNames.Count && difficultyNames[i] != null)
            {
                difficultyNames[i].color = active
                    ? new Color(InkCream.r, InkCream.g, InkCream.b, 0.85f) : InkMuted;
            }
        }
        RefreshScore();
        RefreshSpeed();
        RefreshHint();
    }

    private void RefreshScore()
    {
        if (selectedVariant == null)
        {
            SettingsUiKit.SetText(scoreValue, "無紀錄");
            SettingsUiKit.SetText(scoreRank, string.Empty);
            return;
        }
        int best = 0;
        try { best = LocalScoreRecords.GetBestScore(selectedVariant); } catch { }
        if (best <= 0)
        {
            SettingsUiKit.SetText(scoreValue, "無紀錄");
            SettingsUiKit.SetText(scoreRank, string.Empty);
            return;
        }
        SettingsUiKit.SetText(scoreValue, best.ToString("N0"));
        SettingsUiKit.SetText(scoreRank, LocalScoreRecords.GetRank(best));
    }

    private void RefreshSpeed()
    {
        var settings = SettingsManager.Instance;
        float speed = settings != null && settings.PracticeMode ? settings.PracticeSpeed : 1f;
        SettingsUiKit.SetText(speedValue, speed.ToString("0.00") + "×");
    }

    private void RefreshHint()
    {
        string difficulty = selectedVariant != null ? selectedVariant.difficultyName : null;
        int level = selectedVariant != null ? selectedVariant.difficultyLevel : 0;
        SettingsUiKit.SetText(hintLabel,
            string.IsNullOrEmpty(difficulty) ? string.Empty : $"{difficulty}  Lv.{level}");
    }

    private void NudgeSpeed(float delta)
    {
        var settings = SettingsManager.Instance;
        float current = settings != null && settings.PracticeMode ? settings.PracticeSpeed : 1f;
        SetSpeed(current + delta);
    }

    private void SetSpeed(float speed)
    {
        var settings = SettingsManager.Instance;
        if (settings == null) return;
        // 在練習室調速度就代表要練習模式 —— 不必再叫使用者去勾一個開關。
        if (!settings.PracticeMode) settings.SetPracticeMode(true);
        settings.SetPracticeSpeed(Mathf.Clamp(speed, 0.5f, 1.5f));
        RefreshSpeed();
        // BPM 曲線要顯示「實際會聽到的速度」，所以跟著練習速度縮放。
        if (bpm != null) bpm.Scale = settings.PracticeSpeed;
    }

    /// <summary>挑曲子的時候要聽得到。</summary>
    /// <remarks>
    /// 選曲畫面的試聽是綁在它自己的面板上的，而練習室把那個面板收起來了，所以要
    /// 自己放一份。用的是同一個音檔（external 的非同步載），音量跟著設定裡的音樂
    /// 音量走 —— 練習模式會把**遊戲中**的伴奏關掉，但那是遊戲裡的事，在房間裡挑
    /// 曲子當然要聽得到。
    /// </remarks>
    private void PlayPreview(SongSelectionManager.SongOption song)
    {
        previewToken++;
        if (previewSource != null) previewSource.Stop();
        if (song == null) return;

        SongSelectionManager.SongOption variant =
            song.difficultyVariants != null && song.difficultyVariants.Count > 0
                ? song.difficultyVariants[0] : song;
        AudioClip clip = song.audioClip ?? variant.audioClip;
        if (clip != null)
        {
            StartPreviewClip(clip);
            return;
        }

        string path = !string.IsNullOrEmpty(song.audioResourcePath)
            ? song.audioResourcePath : variant.audioResourcePath;
        if (!string.IsNullOrEmpty(path)) StartCoroutine(LoadPreviewRoutine(path, previewToken));
    }

    private System.Collections.IEnumerator LoadPreviewRoutine(string audioPath, int token)
    {
        string external = null;
        try { external = ExternalSongLibrary.ToLocalPath(audioPath); } catch { }

        AudioClip clip = null;
        if (!string.IsNullOrEmpty(external))
        {
            AudioType type = AudioType.UNKNOWN;
            string extension = System.IO.Path.GetExtension(external).ToLowerInvariant();
            if (extension == ".ogg") type = AudioType.OGGVORBIS;
            else if (extension == ".wav") type = AudioType.WAV;
            else if (extension == ".mp3") type = AudioType.MPEG;
            using (var request = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(
                       new System.Uri(external).AbsoluteUri.Replace("+", "%2B"), type))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request);
            }
        }
        else
        {
            var load = Resources.LoadAsync<AudioClip>(audioPath);
            yield return load;
            clip = load.asset as AudioClip;
        }

        // 載入的過程中使用者可能已經換了歌。
        if (token != previewToken || clip == null) yield break;
        StartPreviewClip(clip);
    }

    private void StartPreviewClip(AudioClip clip)
    {
        if (previewSource == null)
        {
            previewSource = gameObject.AddComponent<AudioSource>();
            previewSource.playOnAwake = false;
            previewSource.loop = true;
            previewSource.spatialBlend = 0f;
        }
        previewSource.clip = clip;
        var settings = SettingsManager.Instance;
        previewSource.volume = settings != null ? Mathf.Clamp01(settings.MusicVolume) * 0.8f : 0.6f;
        // 從三分之一處開始：曲子的開頭常常是空的，試聽要聽得到主題。
        previewSource.time = Mathf.Clamp(clip.length * 0.33f, 0f, Mathf.Max(0f, clip.length - 1f));
        previewSource.Play();
    }

    /// <summary>算出這一份譜的難點，並標出它難在哪一種。</summary>
    /// <remarks>
    /// 「這裡很難」幫不上忙，「這裡是八度跳躍」才知道要練什麼。ChartAnalysis 有技
    /// 法比例，但那是整首的平均；難點要的是**逐段**的，所以這裡自己把譜讀進來分段
    /// 算。解析丟到工作緒，和譜面檢視器同一套做法。
    /// </remarks>
    private void RefreshHotspots(string chartFile)
    {
        hotspotChartFile = chartFile;
        if (string.IsNullOrEmpty(chartFile)) return;
        StartCoroutine(HotspotRoutine(chartFile));
    }

    private System.Collections.IEnumerator HotspotRoutine(string chartFile)
    {
        string json = null;
        try
        {
            TextAsset asset = GameManager.LoadChartJsonAsset(chartFile);
            if (asset != null) json = asset.text;
        }
        catch { }
        if (string.IsNullOrEmpty(json)) yield break;

        System.Threading.Tasks.Task<List<PracticeHotspot>> task = null;
        try
        {
            task = System.Threading.Tasks.Task.Run(() =>
            {
                Chart chart = Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(json);
                return PracticeHotspot.Find(chart, 3);
            });
        }
        catch { }
        if (task == null) yield break;

        while (!task.IsCompleted) yield return null;
        if (!string.Equals(hotspotChartFile, chartFile)) yield break;

        List<PracticeHotspot> spots = null;
        try { if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) spots = task.Result; }
        catch { }
        if (spots == null || spots.Count == 0) yield break;

        var marks = new List<Vector2>(spots.Count);
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < spots.Count; i++)
        {
            marks.Add(new Vector2(spots[i].start01, spots[i].end01));
            if (i > 0) builder.Append("　");
            builder.Append($"{spots[i].label} {FormatClock(spots[i].startMs)}–{FormatClock(spots[i].endMs)}");
        }
        trend?.SetHotspots(marks);
        SettingsUiKit.SetText(trendNote, builder.ToString());
    }

    private static string FormatClock(float ms)
    {
        int seconds = Mathf.Max(0, Mathf.RoundToInt(ms / 1000f));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>開設定。開著的時候把練習室收起來，免得兩層疊在一起。</summary>
    /// <remarks>
    /// 設定頁是場景裡既有的那一份（SimpleCarouselSettings），不另外做一套 —— 兩份
    /// 設定介面遲早會有一邊漏掉新選項。它會順手把選曲面板叫回來（它本來就是從那裡
    /// 開的），所以關閉之後要再收一次。
    /// </remarks>
    private void OpenSettings()
    {
        var settings = FindFirstObjectByType<SimpleCarouselSettings>(FindObjectsInactive.Include);
        if (settings == null) return;
        settings.ShowSettings();
        watchedSettingsPanel = settings.settingsPanel;
        if (watchedSettingsPanel != null && rootRect != null)
        {
            rootRect.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // 設定關掉了就把練習室叫回來。設定頁沒有關閉事件可以訂，所以用看的。
        if (watchedSettingsPanel == null) return;
        if (watchedSettingsPanel.activeInHierarchy) return;
        watchedSettingsPanel = null;
        if (rootRect != null) rootRect.gameObject.SetActive(true);
        try { SongSelectionManager.Instance?.HideSelectionPanelOnly(); } catch { }
        RefreshSpeed();
    }

    private void OpenChartViewer()
    {
        if (selectedVariant == null) return;
        try
        {
            ChartOverviewViewer.EnsureCreated()?.OpenForChart(
                selectedVariant.chartFileName,
                selectedVariant.difficultyName,
                selectedVariant.audioClip,
                selectedVariant.audioResourcePath);
        }
        catch { }
    }

    private void StartPractice()
    {
        if (selectedSong == null || selectedVariant == null) return;
        previewToken++;
        if (previewSource != null) previewSource.Stop();
        var settings = SettingsManager.Instance;
        if (settings != null && !settings.PracticeMode) settings.SetPracticeMode(true);

        selectedSong.selectedVariant = selectedVariant;
        var manager = SongSelectionManager.Instance;
        // 轉盤也要指到同一首：開場流程有幾樣東西（音訊分段、速度事件、背景、合成
        // 鋼琴）是回頭問 GetSelectedSong() 的，不同步的話它們會套到轉盤停著的那一
        // 首身上 —— 那正是「不管選哪一首都變成第一首」的來源。
        if (manager != null) manager.SyncSelectionTo(selectedSong, selectedVariant);
        // 先把選曲畫面放回去再開始：StartSongOption 走的是正規的開場流程，它預期
        // 自己負責把選曲收起來、把遊戲打開。
        Destroy(gameObject);
        if (manager != null)
        {
            manager.ShowSelectionPanelOnly();
            manager.StartSongOption(selectedVariant);
        }
    }

    // ------------------------------------------------------------------
    // 小工具
    // ------------------------------------------------------------------

    /// <summary>一張會接邊的木紋：縱向的年輪條紋加細纖維。</summary>
    /// <remarks>
    /// 不用 ClassicalBookUITheme 那張板材：它是給暗色書櫃用的，調亮之後紋路會糊掉
    /// （貼圖本身就暗，tint 只能壓不能提）。這裡自己畫一張中性亮度的，實際顏色交給
    /// Image 的 tint —— 同一張貼圖就能鋪背景（深一點）和面板（淺一點）。
    ///
    /// 年輪用 sin(x) 疊兩個不同頻率再取冪，讓深色的線細、淺色的面寬，那是木頭剖面
    /// 的樣子；直接用 sin 會變成等寬的斑馬線。
    /// </remarks>
    private static Sprite WoodSprite()
    {
        if (woodSprite != null) return woodSprite;

        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "PracticeRoomWood",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;

                // 年輪沿著 x 走，並且跟著 y 緩慢彎曲 —— 直的條紋看起來是布，不是木頭。
                float wave = Mathf.Sin((u * 6f + Mathf.Sin(v * Mathf.PI * 2f) * 0.18f) * Mathf.PI * 2f);
                float rings = Mathf.Pow(Mathf.Abs(wave), 0.35f);
                float fine = Mathf.Sin((u * 47f + v * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                float fibre = Mathf.PerlinNoise(u * 24f, v * 3.5f);

                float shade = 0.80f + rings * 0.18f + (fine - 0.5f) * 0.05f + (fibre - 0.5f) * 0.10f;
                shade = Mathf.Clamp01(shade);
                // 木頭的暗處偏紅、亮處偏黃，所以三個通道各自給一點偏移。
                pixels[y * size + x] = new Color(shade, shade * 0.965f, shade * 0.905f, 1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);

        woodSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        woodSprite.name = "PracticeRoomWood";
        return woodSprite;
    }

    /// <summary>拉絲金屬：縱向的細絲加一道橫向的明暗。</summary>
    /// <remarks>
    /// 金屬和木頭的差別不在紋路，在**高光的形狀**：木頭是整面漫反射，金屬是一條
    /// 窄的亮帶加大片的暗。所以這張貼圖的重點是那條亮帶（中間偏上），拉絲只是讓
    /// 它不要太乾淨。
    /// </remarks>
    private static Sprite MetalSprite()
    {
        if (metalSprite != null) return metalSprite;

        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "PracticeRoomMetal",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                // 一條窄亮帶：金屬的高光很集中，散開就變成塑膠。
                float band = Mathf.Exp(-Mathf.Pow((u - 0.34f) / 0.16f, 2f));
                float fine = Mathf.Sin(u * 180f * Mathf.PI) * 0.5f + 0.5f;
                float shade = 0.42f + band * 0.55f + (fine - 0.5f) * 0.05f;
                shade = Mathf.Clamp01(shade);
                pixels[y * size + x] = new Color(shade, shade, shade * 1.02f, 1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);

        metalSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        metalSprite.name = "PracticeRoomMetal";
        return metalSprite;
    }

    /// <summary>一根金屬件：暗底、拉絲、上緣一條亮線。</summary>
    private static RectTransform MakeMetal(RectTransform parent, string name, Vector2 position,
        Vector2 size, float rotation, Color colour)
    {
        RectTransform rect = AddRect(name, parent);
        Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = MetalSprite();
        image.type = Image.Type.Simple;
        image.color = colour;
        image.raycastTarget = false;
        var shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.02f, 0.02f, 0.03f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -3f);
        return rect;
    }

    private static RectTransform AddRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f,
        float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot,
        Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    /// <summary>深色的毛氈面板。它是家具，不是要讀的東西，所以要吃光。</summary>
    private static RectTransform MakePanel(RectTransform parent, string name, Color colour)
    {
        RectTransform rect = AddRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = ClassicalBookUITheme.GetClothTextureSprite();
        image.type = Image.Type.Tiled;
        image.color = colour;
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
        outline.effectDistance = new Vector2(1f, -1f);
        var shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.05f, 0.025f, 0.01f, 0.6f);
        shadow.effectDistance = new Vector2(0f, -6f);
        return rect;
    }

    /// <summary>紙卡：最亮的一層，要讀的東西都放在上面。</summary>
    private static RectTransform MakeCard(RectTransform parent, string name, float y,
        float height, float width)
    {
        RectTransform rect = AddRect(name, parent);
        Place(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(width, height));
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        image.type = Image.Type.Tiled;
        image.color = Paper;
        var shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.05f, 0.025f, 0.01f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -3f);
        return rect;
    }

    private static void AddSoftShadow(TextMeshProUGUI label)
    {
        if (label == null) return;
        var shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.10f, 0.05f, 0.02f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -2f);
    }

    private static TextMeshProUGUI AddText(RectTransform parent, string name, string value,
        float size, Color colour, TextAlignmentOptions alignment, FontStyles style)
    {
        TextMeshProUGUI label = SettingsUiKit.CreateLabel(parent, name, size, colour, alignment, style);
        // TMP 在「框高 < 字級 × 1.5」時整段不畫，而這裡的框高度是照版面給的。
        label.overflowMode = TextOverflowModes.Overflow;
        SettingsUiKit.SetText(label, value ?? string.Empty);
        return label;
    }

    /// <summary>淺木底、深色字、金邊。設定頁那組深色板和這間房的調性衝突。</summary>
    private static Button MakeButton(RectTransform parent, string name, string caption,
        float fontSize, Vector2 size, bool primary = false)
    {
        RectTransform rect = AddRect(name, parent);
        rect.sizeDelta = size;

        Image plate = rect.gameObject.AddComponent<Image>();
        plate.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        plate.type = Image.Type.Tiled;
        plate.color = primary ? ButtonPrimary : PaperDim;

        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = primary
            ? new Color(0.45f, 0.30f, 0.10f, 0.9f)
            : new Color(0.38f, 0.255f, 0.13f, 0.55f);
        outline.effectDistance = new Vector2(1f, -1f);

        var shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.12f, 0.06f, 0.02f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -3f);

        if (!string.IsNullOrEmpty(caption))
        {
            TextMeshProUGUI label = AddText(rect, "Label", caption, fontSize, Ink,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(label.rectTransform, 8f, 2f, 8f, 2f);
            label.characterSpacing = 3f;
            label.raycastTarget = false;
        }

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.12f, 1.10f, 1.06f, 1f);
        colours.pressedColor = new Color(0.86f, 0.82f, 0.76f, 1f);
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        return button;
    }

}

/// <summary>
/// 一個有厚度的畫框：四邊切角的斜面，光從左上來。
/// </summary>
/// <remarks>
/// 畫框的立體感只有一個來源：**四個斜面各自受光不同**。上緣最亮、左緣次之、右緣
/// 和下緣壓暗 —— 四條都一樣亮的話，它就只是一個粗邊框。切角（斜接）也要畫出來，
/// 那是「這是四根木條拼的」的證據。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PictureFrameGraphic : MaskableGraphic
{
    private static readonly Color Deep = new Color(0.230f, 0.140f, 0.060f, 1f);
    private static readonly Color Body = new Color(0.420f, 0.270f, 0.120f, 1f);
    private static readonly Color Lit = new Color(0.680f, 0.490f, 0.230f, 1f);
    private static readonly Color Gilt = new Color(0.870f, 0.690f, 0.330f, 1f);
    private static readonly Color Mat = new Color(0.930f, 0.890f, 0.790f, 1f);

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float outerW = r.width * 0.5f;
        float outerH = r.height * 0.5f;
        float moulding = Mathf.Min(outerW, outerH) * 0.075f;   // 框有多寬（窄框）
        float lip = Mathf.Max(2f, moulding * 0.30f);           // 內緣的金線
        float cx = r.center.x;
        float cy = r.center.y;

        float innerW = outerW - moulding;
        float innerH = outerH - moulding;

        // 卡紙：框內的底。曲繪蓋在它上面，留白才有「裱起來」的感覺。
        AddQuad(vh,
            new Vector2(cx - innerW, cy - innerH), new Vector2(cx - innerW, cy + innerH),
            new Vector2(cx + innerW, cy + innerH), new Vector2(cx + innerW, cy - innerH),
            Mat, Mat, Mat, Mat);

        // 四個斜面。外角到內角的連線就是斜接，所以四邊天生對得起來。
        AddBevel(vh, new Vector2(cx - outerW, cy + outerH), new Vector2(cx + outerW, cy + outerH),
            new Vector2(cx + innerW, cy + innerH), new Vector2(cx - innerW, cy + innerH), Lit, Gilt);
        AddBevel(vh, new Vector2(cx - outerW, cy - outerH), new Vector2(cx - outerW, cy + outerH),
            new Vector2(cx - innerW, cy + innerH), new Vector2(cx - innerW, cy - innerH), Body, Lit);
        AddBevel(vh, new Vector2(cx + outerW, cy + outerH), new Vector2(cx + outerW, cy - outerH),
            new Vector2(cx + innerW, cy - innerH), new Vector2(cx + innerW, cy + innerH), Deep, Body);
        AddBevel(vh, new Vector2(cx + outerW, cy - outerH), new Vector2(cx - outerW, cy - outerH),
            new Vector2(cx - innerW, cy - innerH), new Vector2(cx + innerW, cy - innerH), Deep, Body);

        // 內緣一圈金線：框和畫之間要有一條硬邊，不然畫像是漂在框裡。
        AddRing(vh, cx, cy, innerW, innerH, lip, Gilt);
        // 外緣一圈暗線，把框從牆上切開。
        AddRing(vh, cx, cy, outerW, outerH, lip * 0.6f, new Color(0.12f, 0.07f, 0.03f, 0.9f));
    }

    /// <summary>一條斜面：外側兩點、內側兩點，外亮內暗（或反過來）。</summary>
    private static void AddBevel(VertexHelper vh, Vector2 outerA, Vector2 outerB,
        Vector2 innerB, Vector2 innerA, Color outer, Color inner)
    {
        AddQuad(vh, outerA, outerB, innerB, innerA, outer, outer, inner, inner);
    }

    private static void AddRing(VertexHelper vh, float cx, float cy, float halfW, float halfH,
        float thickness, Color colour)
    {
        float w = halfW;
        float h = halfH;
        float t = thickness;
        AddQuad(vh, new Vector2(cx - w, cy + h - t), new Vector2(cx - w, cy + h),
            new Vector2(cx + w, cy + h), new Vector2(cx + w, cy + h - t), colour, colour, colour, colour);
        AddQuad(vh, new Vector2(cx - w, cy - h), new Vector2(cx - w, cy - h + t),
            new Vector2(cx + w, cy - h + t), new Vector2(cx + w, cy - h), colour, colour, colour, colour);
        AddQuad(vh, new Vector2(cx - w, cy - h), new Vector2(cx - w, cy + h),
            new Vector2(cx - w + t, cy + h), new Vector2(cx - w + t, cy - h), colour, colour, colour, colour);
        AddQuad(vh, new Vector2(cx + w - t, cy - h), new Vector2(cx + w - t, cy + h),
            new Vector2(cx + w, cy + h), new Vector2(cx + w, cy - h), colour, colour, colour, colour);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color ca, Color cb, Color cc, Color cd)
    {
        int i = vh.currentVertCount;
        var v = UIVertex.simpleVert;
        v.color = ca; v.position = a; vh.AddVert(v);
        v.color = cb; v.position = b; vh.AddVert(v);
        v.color = cc; v.position = c; vh.AddVert(v);
        v.color = cd; v.position = d; vh.AddVert(v);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}

/// <summary>
/// 整首曲子的走勢：密度、難點、以及玩家的得分曲線。
/// </summary>
/// <remarks>
/// 三層疊在同一條時間軸上：
///
/// **密度**（填色）是譜面自己的性質 —— 哪一段音符多。
/// **難點**（上緣的金色小標）是密度的峰，也就是最容易崩的地方。
/// **得分**（兩條線）是玩家的：粗線是累積的總分比例（滿分是一路貼著頂端），細線
/// 是每一段當下的得分比例（起伏大代表某幾段特別掉分）。
///
/// 得分那兩條要有「每一段的得分」才畫得出來，而那份資料要在遊玩時記錄。沒有的時
/// 候只畫密度和難點，並在卡片上寫「尚無遊玩紀錄」—— 空著不畫會讓人以為壞了。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ChartTrendGraphic : MaskableGraphic
{
    private static readonly Color DensityFill = new Color(0.42f, 0.30f, 0.17f, 0.30f);
    private static readonly Color DensityEdge = new Color(0.36f, 0.24f, 0.12f, 0.55f);
    private static readonly Color HardMark = new Color(0.82f, 0.58f, 0.20f, 0.95f);
    private static readonly Color TotalLine = new Color(0.20f, 0.36f, 0.58f, 0.95f);
    private static readonly Color SectionLine = new Color(0.62f, 0.26f, 0.24f, 0.85f);
    private static readonly Color Baseline = new Color(0.30f, 0.20f, 0.10f, 0.35f);

    private float[] density;
    private List<Vector2> hotspots;     // x = 起點比例、y = 終點比例
    private float[] totalScore;
    private float[] sectionScore;
    private float peak = 1f;

    public override Texture mainTexture => Texture2D.whiteTexture;

    public void SetChart(float[] left, float[] right, float peakTotal)
    {
        int count = left != null ? left.Length : (right != null ? right.Length : 0);
        density = count > 0 ? new float[count] : null;
        for (int i = 0; i < count; i++)
        {
            float l = left != null && i < left.Length ? left[i] : 0f;
            float r = right != null && i < right.Length ? right[i] : 0f;
            density[i] = l + r;
        }
        peak = Mathf.Max(0.0001f, peakTotal);
        SetVerticesDirty();
    }

    /// <summary>難點的區段（0–1，整首的比例；x 起 y 迄）。</summary>
    public void SetHotspots(List<Vector2> spans)
    {
        hotspots = spans;
        SetVerticesDirty();
    }

    /// <summary>每一段的得分比例（0–1）。沒有資料就傳 null。</summary>
    public void SetPlayerScores(float[] total, float[] section)
    {
        totalScore = total;
        sectionScore = section;
        SetVerticesDirty();
    }

    public void Clear()
    {
        density = null;
        hotspots = null;
        totalScore = null;
        sectionScore = null;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float x0 = r.xMin;
        float y0 = r.yMin;
        float w = r.width;
        float h = r.height;
        if (w <= 1f || h <= 1f) return;

        // 底線：沒有基準的折線讀不出高低。
        AddLine(vh, new Vector2(x0, y0 + 1f), new Vector2(x0 + w, y0 + 1f), 1.5f, Baseline);
        if (density == null || density.Length < 2) return;

        int n = density.Length;
        float step = w / (n - 1);

        // 密度：填色 + 上緣一條線。
        for (int i = 0; i < n - 1; i++)
        {
            float xa = x0 + i * step;
            float xb = x0 + (i + 1) * step;
            float ya = y0 + Mathf.Clamp01(density[i] / peak) * h * 0.82f;
            float yb = y0 + Mathf.Clamp01(density[i + 1] / peak) * h * 0.82f;
            AddQuad(vh, new Vector2(xa, y0), new Vector2(xa, ya), new Vector2(xb, yb),
                new Vector2(xb, y0), DensityFill, DensityFill, DensityFill, DensityFill);
            AddLine(vh, new Vector2(xa, ya), new Vector2(xb, yb), 1.5f, DensityEdge);
        }

        // 難點是**一整段**，所以畫成一條帶子，不是一個點。
        //
        // 帶子用很淡的金色鋪滿那一段的高度，兩端各一條實線把邊界講清楚，上緣再放
        // 一個小標記。整段被塗起來，才看得出「這裡連續幾十秒都在考同一件事」。
        if (hotspots != null)
        {
            for (int i = 0; i < hotspots.Count; i++)
            {
                float xa = x0 + Mathf.Clamp01(hotspots[i].x) * w;
                float xb = x0 + Mathf.Clamp01(hotspots[i].y) * w;
                if (xb - xa < 3f) xb = xa + 3f;            // 太短的也要看得到

                Color band = new Color(HardMark.r, HardMark.g, HardMark.b, 0.16f);
                AddQuad(vh, new Vector2(xa, y0), new Vector2(xa, y0 + h - 12f),
                    new Vector2(xb, y0 + h - 12f), new Vector2(xb, y0), band, band, band, band);
                Color edge = new Color(HardMark.r, HardMark.g, HardMark.b, 0.45f);
                AddLine(vh, new Vector2(xa, y0), new Vector2(xa, y0 + h - 12f), 1f, edge);
                AddLine(vh, new Vector2(xb, y0), new Vector2(xb, y0 + h - 12f), 1f, edge);

                float mid = (xa + xb) * 0.5f;
                AddQuad(vh,
                    new Vector2(mid - 5f, y0 + h - 12f), new Vector2(mid, y0 + h - 1f),
                    new Vector2(mid + 5f, y0 + h - 12f), new Vector2(mid, y0 + h - 18f),
                    HardMark, HardMark, HardMark, HardMark);
            }
        }

        // 玩家的兩條線。
        AddCurve(vh, sectionScore, x0, y0, w, h, 2f, SectionLine);
        AddCurve(vh, totalScore, x0, y0, w, h, 3.5f, TotalLine);
    }

    private static void AddCurve(VertexHelper vh, float[] values, float x0, float y0,
        float w, float h, float thickness, Color colour)
    {
        if (values == null || values.Length < 2) return;
        float step = w / (values.Length - 1);
        for (int i = 0; i < values.Length - 1; i++)
        {
            Vector2 a = new Vector2(x0 + i * step, y0 + Mathf.Clamp01(values[i]) * h * 0.9f);
            Vector2 b = new Vector2(x0 + (i + 1) * step, y0 + Mathf.Clamp01(values[i + 1]) * h * 0.9f);
            AddLine(vh, a, b, thickness, colour);
        }
    }

    private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness,
        Color colour)
    {
        Vector2 dir = (to - from).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x) * thickness * 0.5f;
        AddQuad(vh, from - n, from + n, to + n, to - n, colour, colour, colour, colour);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color ca, Color cb, Color cc, Color cd)
    {
        int i = vh.currentVertCount;
        var v = UIVertex.simpleVert;
        v.color = ca; v.position = a; vh.AddVert(v);
        v.color = cb; v.position = b; vh.AddVert(v);
        v.color = cc; v.position = c; vh.AddVert(v);
        v.color = cd; v.position = d; vh.AddVert(v);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}

/// <summary>
/// 一個難點區段：從哪到哪、難在哪一種技法。
/// </summary>
/// <remarks>
/// **難點是一片，不是一個點。** 一首曲子的大跨度跳躍如果佔了將近一半，那它不是
/// 「第 1:30 有一個難點」，而是「這幾大段都在跳」。所以這裡對**每一個技法**各自掃
/// 出它連續超過門檻的區間，再按「這段有多長 × 平均超出門檻多少」排名 —— 長而穩的
/// 一大片會贏過單一個尖峰，這正是練習時想知道的事。
///
/// **用的是譜面分析自己的技法判準**（雙音跑動、高速八度、琶音、顫音……），只是套
/// 在一段一段的窗上。整首的分析回答「這首靠什麼」，難點回答「哪幾段在考這個」，
/// 兩者用同一套字彙才不會互相矛盾。
///
/// 窗長四秒、允許中間斷一格：技法判準（跑動要連續幾顆、顫音要來回幾次）需要一段
/// 夠長的窗才成立，而真實的樂句中間本來就會有換氣。
/// </remarks>
public sealed class PracticeHotspot
{
    public float startMs;
    public float endMs;
    public float start01;
    public float end01;
    public string label;
    public float severity;

    private const float WindowMs = 4000f;
    private const int MinimumNotes = 8;
    private const int MaxGapWindows = 1;

    public static List<PracticeHotspot> Find(Chart chart, int count)
    {
        var results = new List<PracticeHotspot>();
        if (chart == null || chart.notes == null || chart.notes.Count == 0) return results;

        float last = 0f;
        foreach (NoteData note in chart.notes)
        {
            if (note != null && note.startTime > last) last = note.startTime;
        }
        if (last <= 0f) return results;

        int windows = Mathf.Max(1, Mathf.CeilToInt(last / WindowMs));

        // 每一格窗、每一個技法，記下「超過門檻幾倍」。
        var series = new Dictionary<string, float[]>();
        var traits = new List<ChartAnalysisVocabulary.Trait>(12);
        for (int w = 0; w < windows; w++)
        {
            float from = w * WindowMs;
            ChartAnalysis section = ChartAnalysisCache.AnalyseWindow(chart.notes, from, from + WindowMs);
            if (section == null || section.noteCount < MinimumNotes) continue;

            traits.Clear();
            try { ChartAnalysisVocabulary.CollectTechniques(section, traits); }
            catch { continue; }

            foreach (ChartAnalysisVocabulary.Trait trait in traits)
            {
                if (string.IsNullOrEmpty(trait.name)) continue;
                float ratio = trait.value / Mathf.Max(0.0001f, trait.threshold);
                if (ratio < 1f) continue;                   // 沒過門檻就不算
                if (!series.TryGetValue(trait.name, out float[] values))
                {
                    values = new float[windows];
                    series[trait.name] = values;
                }
                values[w] = ratio;
            }
        }

        // 每個技法各自切出連續的區間。
        var regions = new List<PracticeHotspot>();
        foreach (var pair in series)
        {
            float[] values = pair.Value;
            int start = -1;
            int gap = 0;
            float sum = 0f;
            int hits = 0;

            for (int w = 0; w <= windows; w++)
            {
                bool active = w < windows && values[w] >= 1f;
                if (active)
                {
                    if (start < 0) { start = w; sum = 0f; hits = 0; }
                    gap = 0;
                    sum += values[w];
                    hits++;
                    continue;
                }
                if (start < 0) continue;
                // 中間斷一格還算同一段：樂句本來就會換氣。
                if (w < windows && gap < MaxGapWindows) { gap++; continue; }

                int end = w - gap;
                float mean = hits > 0 ? sum / hits : 0f;
                float lengthMs = (end - start) * WindowMs;
                regions.Add(new PracticeHotspot
                {
                    startMs = start * WindowMs,
                    endMs = end * WindowMs,
                    start01 = Mathf.Clamp01(start * WindowMs / last),
                    end01 = Mathf.Clamp01(end * WindowMs / last),
                    label = pair.Key,
                    // 長度和強度都算：一大片中等強度的跳躍，比一個孤立的尖峰更該練。
                    severity = mean * Mathf.Sqrt(Mathf.Max(1f, lengthMs / WindowMs))
                });
                start = -1;
                gap = 0;
            }
        }

        regions.Sort((x, y) => y.severity.CompareTo(x.severity));

        // 挑出前幾個，重疊超過一半的就跳過 —— 同一段不需要兩個標籤。
        foreach (PracticeHotspot region in regions)
        {
            if (results.Count >= count) break;
            bool overlapped = false;
            foreach (PracticeHotspot chosen in results)
            {
                float overlap = Mathf.Min(region.endMs, chosen.endMs) -
                                Mathf.Max(region.startMs, chosen.startMs);
                float shorter = Mathf.Min(region.endMs - region.startMs,
                                          chosen.endMs - chosen.startMs);
                if (overlap > 0f && shorter > 0f && overlap / shorter > 0.5f) { overlapped = true; break; }
            }
            if (!overlapped) results.Add(region);
        }

        results.Sort((x, y) => x.startMs.CompareTo(y.startMs));
        return results;
    }
}

/// <summary>
/// 放不下就自己跑的字。
/// </summary>
/// <remarks>
/// 縮字（autosize）和截斷（…）都會讓人讀不到完整的曲名，而曲名正是清單上唯一重要
/// 的資訊。跑馬燈是遊戲裡既有的做法（GameBackgroundManager 的曲名條），這裡照同樣
/// 的節奏：先停一下再走，走完整段之後回到原點再停。
///
/// 只有真的放不下才動。放得下的字如果也在跑，整份清單會像在發抖。
/// </remarks>
[RequireComponent(typeof(RectTransform))]
public sealed class MarqueeText : MonoBehaviour
{
    private const float Speed = 42f;        // 每秒幾個像素
    private const float HoldSeconds = 1.2f; // 兩端各停多久

    private TextMeshProUGUI label;
    private RectTransform rect;
    private float viewportWidth;
    private float overflow;
    private float timer;
    private bool measured;

    public void Bind(TextMeshProUGUI target, float width)
    {
        label = target;
        rect = target != null ? target.rectTransform : null;
        viewportWidth = width;
        measured = false;
    }

    private void Update()
    {
        if (label == null || rect == null) return;

        if (!measured)
        {
            // 第一幀量不到寬度（TMP 還沒排版），所以量到才開始。
            float preferred = label.preferredWidth;
            if (preferred <= 0.01f) return;
            measured = true;
            overflow = Mathf.Max(0f, preferred - viewportWidth);
            timer = 0f;
            rect.anchoredPosition = Vector2.zero;
        }

        if (overflow <= 1f) return;

        timer += Time.unscaledDeltaTime;
        float travel = overflow / Speed;
        float cycle = HoldSeconds + travel + HoldSeconds + travel;
        float t = timer % cycle;

        float offset;
        if (t < HoldSeconds) offset = 0f;
        else if (t < HoldSeconds + travel) offset = (t - HoldSeconds) * Speed;
        else if (t < HoldSeconds + travel + HoldSeconds) offset = overflow;
        else offset = overflow - (t - HoldSeconds - travel - HoldSeconds) * Speed;

        rect.anchoredPosition = new Vector2(-offset, rect.anchoredPosition.y);
    }
}

/// <summary>排序用的漏斗圖示。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FunnelGlyph : MaskableGraphic
{
    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float cx = r.center.x;
        float top = r.yMax;
        float bottom = r.yMin;
        float half = r.width * 0.5f;
        float neck = r.width * 0.12f;
        float waist = bottom + r.height * 0.42f;

        // 上面的漏斗口
        AddQuad(vh,
            new Vector2(cx - neck, waist), new Vector2(cx - half, top),
            new Vector2(cx + half, top), new Vector2(cx + neck, waist));
        // 下面的管
        AddQuad(vh,
            new Vector2(cx - neck, bottom), new Vector2(cx - neck, waist),
            new Vector2(cx + neck, waist), new Vector2(cx + neck, bottom));
    }

    private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int i = vh.currentVertCount;
        var v = UIVertex.simpleVert;
        v.color = color;
        v.position = a; vh.AddVert(v);
        v.position = b; vh.AddVert(v);
        v.position = c; vh.AddVert(v);
        v.position = d; vh.AddVert(v);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}

/// <summary>排序方向的箭頭。朝上＝由小到大。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SortArrowGlyph : MaskableGraphic
{
    private bool pointsDown;

    public bool PointsDown
    {
        get => pointsDown;
        set { pointsDown = value; SetVerticesDirty(); }
    }

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float cx = r.center.x;
        float stem = r.width * 0.18f;
        float head = r.width * 0.5f;
        float tip = pointsDown ? r.yMin : r.yMax;
        float tail = pointsDown ? r.yMax : r.yMin;
        float shoulder = Mathf.Lerp(tip, tail, 0.45f);

        AddQuad(vh, new Vector2(cx - head, shoulder), new Vector2(cx, tip),
            new Vector2(cx + head, shoulder), new Vector2(cx, shoulder));
        AddQuad(vh, new Vector2(cx - stem, tail), new Vector2(cx - stem, shoulder),
            new Vector2(cx + stem, shoulder), new Vector2(cx + stem, tail));
    }

    private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int i = vh.currentVertCount;
        var v = UIVertex.simpleVert;
        v.color = color;
        v.position = a; vh.AddVert(v);
        v.position = b; vh.AddVert(v);
        v.position = c; vh.AddVert(v);
        v.position = d; vh.AddVert(v);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
