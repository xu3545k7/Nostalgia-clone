using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    /// <summary>三欄的底部要內縮多少。</summary>
    /// <remarks>
    /// 畫面最下面那條 <see cref="KeyZoneBar"/> 是全域的覆蓋層，不屬於這個畫面，
    /// 所以它不會自動把內容推開 —— 三欄必須自己讓出那段高度，否則難度鈕和
    /// 「開始練習」會被蓋在底下。
    ///
    /// **讓的是整欄，不是把按鈕搬到別處。** 難度鈕屬於左邊那份曲目清單、開始練習
    /// 屬於右邊那份設定；搬到頂部會把它們和自己的上下文拆開。
    /// </remarks>
    private const float BottomInset = Margin + 92f;
    private const float ListWidth = 520f;
    private const float RailWidth = 520f;
    /// <summary>「全部」這一格的**代號**，不是顯示的字。</summary>
    /// <remarks>
    /// 它同時被 <c>string.Equals</c> 拿去比（分類清單裡的哨兵），所以不能翻 ——
    /// 翻了之後換一次語言，比對就不成立，分類篩選會整個失效。要印出來時才過
    /// <see cref="Localize.S"/>。const 本來也只能放編譯期常數。
    /// </remarks>
    private const string AllCategories = "全部";
    // ── 中欄的高度預算 ────────────────────────────────────────────────
    //
    // 由上往下疊：畫框（底邊釘住）→ 曲名 → 作者 → 難度 → 成績 → 走勢。
    //
    // 每一塊各自寫死一個 y 的那一版，曲名還停在更早的數字（−190），結果作者跑到難
    // 度上面、曲名整片壓在成績卡上 —— 不是「跑掉」，是被夾在兩張卡中間。現在全部從
    // 同一份預算扣出來，下面這幾個數字加起來剛好用完，**不要單獨改其中一個**。
    //
    // 實力評分曾經插在這一欄最上面，而預算早就用完了 —— 那塊 150 高的卡是從畫框
    // （420 → 300）和走勢卡（168 → 136）身上割來的。曲繪是這一欄的主角，不該為了一
    // 塊銘牌縮掉三成，所以它搬到頂欄中央凸出來的那一塊上（見 BuildRatingPlaque）。
    //
    // 那一塊往下凸 40，三欄的頂緣跟著讓 40+6 —— 即使如此，這一欄付出的 46 還是遠小
    // 於評分卡佔的 162，畫框因此從 300 長回 **422**，比它原本被割之前的 420 還大。
    // 走勢卡從 136 再讓 6 給托木，是這一次唯一被收窄的東西。
    //
    // Stand 的高度 = 1080 − 92（頂欄）− 40（托木）− 6（縫）− 36×2（邊距）= 870，
    // 半高 435。由上往下的算式（從 +435 一路減到 −435）：
    //   6（留白）+ 422（框）+ 12 + 64（曲名）+ 4 + 38（作者）+ 8 + 84（難度）
    //   + 12 + 72（成績）+ 12 + 130（走勢）+ 6（留白）= 870
    private const float FrameMax = 422f;        // 框（含卡紙）最大邊長
    private const float FrameMat = 22f;         // 卡紙留白（還沒載到曲繪時的預設）

    /// <summary>線腳畫到框短邊的百分之幾。卡紙的留白也是從這個數字算出來的。</summary>
    /// <remarks>
    /// 兩邊各寫各的就會對不起來：上一版線腳畫到 0.078×短邊 = 33px，而畫只內縮
    /// 22px —— 最亮的珠圓和貼著畫的那條金線**整整兩層**被畫蓋掉，卡紙一個像素都
    /// 看不到，框看起來就是一條平的深棕帶。現在畫的內縮由這個比例反推出來。
    /// </remarks>
    internal const float MouldingRatio = 0.045f;
    private const float MatMargin = 8f;         // 線腳內緣到畫之間還要看得到的卡紙
    private const float FrameBottomY = 7f;      // 框的底邊；框只往上長
    private const float TitleTopY = -5f;        // 框底往下 12
    private const float TitleHeight = 64f;
    private const float AuthorTopY = -73f;
    private const float AuthorHeight = 38f;   // 字級 24 × 1.6，和曲名同一條規則
    private const float DifficultyTopY = -119f;
    private const float DifficultyHeight = 84f;   // 難度鈕 72，上下各留 6
    private const float ScoreTopY = -215f;
    private const float ScoreHeight = 72f;
    private const float TrendTopY = -299f;
    private const float TrendHeight = 130f;     // 底緣 −429，離 Stand 底還有 6
    private const float CentreWidth = 640f;     // 曲名、卡片的共同寬度
    /// <summary>圓角半徑。和 SettingsUiKit.PracticePaperSprite 的半徑一致。</summary>
    private const float CornerRadius = 10f;
    private const float StandDesignWidth = 736f;   // 中欄在 1920×1080 下的大小
    private const float StandDesignHeight = 870f;

    // ── 頂欄中央凸出來的那一塊 ────────────────────────────────────────
    //
    // 它是兩件東西：一塊**梯形的托木**（上寬下窄，同一張白樺紋，兩道斜肩收邊）和
    // 一塊坐在上面的**紙牌**。平白凸出來的一個方塊讀起來是「有人把長方形貼在板子
    // 上」；托木是收邊的語言，斜肩把重量交還給上面那條橫板，而評分本身是要讀的東
    // 西，所以它在紙上 —— 和中欄那三張卡同一套家具。
    //
    // 寬度的上限是左右兩邊的字：標題「練習室」的墨跡到 x≈414 為止（框給 420 是留給
    // 翻譯的，墨只有 108），提示（「Extreme  Lv.15」）靠右對齊、墨跡從 x≈1588 才開
    // 始。托木 1000 寬置中是 460–1460，所以標題的框收到 150、提示收到 240。
    //
    // 它比左右兩欄的外緣（556 / 1364）寬，所以**三欄都要讓**，不能只讓中欄 —— 只讓
    // 中欄的話，托木的兩道斜肩會被曲目清單和右欄的面板蓋掉，斜肩一被削掉就不再是
    // 托木，只是一個被裁過的方塊。
    private const float RatingCorbelWidth = 1000f;   // 托木的上緣
    private const float RatingCorbelInset = 56f;     // 下緣兩側各內縮 → 底寬 888
    private const float RatingPlaqueDrop = 40f;      // 托木凸出頂欄底下多少
    private const float RatingPlaqueGap = 6f;        // 托木底緣到三欄頂之間的縫
    private const float RatingCardWidth = 880f;      // 牌子：比托木底寬（888）再窄一點
    private const float RatingCardTop = -12f;        // 牌子上緣離頂欄頂多少
    private const float RatingCardHeight = 108f;

    /// <summary>三欄共同的頂緣：頂欄 ＋ 凸出的那一塊 ＋ 縫 ＋ 邊距。</summary>
    private const float ColumnTop = TopBarHeight + RatingPlaqueDrop + RatingPlaqueGap + Margin;
    private const float DrawerTop = 62f;    // 簾子從標題底下捲出來
    private const float DrawerHeight = 168f;

    /// <summary>布收起來時繩還看得到的長度。</summary>
    private const float CordIdle = 64f;
    /// <summary>拉繩的點擊區有多寬。繩本身才 5，但那抓不到。</summary>
    private const float CordHitWidth = 30f;

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
    // 牆是深色的胡桃木，頂欄是白樺 —— 一深一淺，頂欄才會像一條架在牆上的板子，
    // 而不是牆的一部分。
    // ── 表面：一階一階往上亮 ──────────────────────────────────────────
    //
    // 上一版有四十幾個現場調出來的棕色，結果是「每一塊都有點不一樣，但看不出誰
    // 在上面」—— 牆和檯面只差 9%、紙卡和白樺木只差 0.5%。這裡收成一道梯子：
    // **牆 → 檯面 → 抬起的檯面 → 控制項 → 清單列 → 紙卡 → 選中的列**，每一階都
    // 拉開到看得出來。任何一個面都從這幾個裡面挑，不再現場調。
    private static readonly Color RoomWood = new Color(0.148f, 0.090f, 0.046f, 1f);    // 牆
    private static readonly Color Felt = new Color(0.235f, 0.150f, 0.088f, 1f);        // 檯面
    private static readonly Color FeltLight = new Color(0.300f, 0.196f, 0.115f, 1f);   // 抬起的檯面
    private static readonly Color Control = new Color(0.845f, 0.788f, 0.672f, 1f);     // 按鈕
    private static readonly Color PaperRow = new Color(0.885f, 0.838f, 0.738f, 1f);    // 清單列
    private static readonly Color Paper = new Color(0.945f, 0.900f, 0.800f, 1f);       // 紙卡
    private static readonly Color PaperSel = new Color(0.988f, 0.955f, 0.868f, 1f);    // 選中的列
    private static readonly Color Birch = new Color(0.880f, 0.838f, 0.742f, 1f);       // 頂端的白樺木

    // 字色。InkMuted 原本對紙面只有 2.9:1，小字看起來像浮水印，壓深到 3.4:1。
    private static readonly Color Ink = new Color(0.185f, 0.108f, 0.055f, 1f);
    private static readonly Color InkMuted = new Color(0.330f, 0.215f, 0.120f, 1f);
    private static readonly Color InkCream = new Color(0.955f, 0.900f, 0.782f, 1f);
    private static readonly Color InkCreamMuted = new Color(0.760f, 0.672f, 0.535f, 1f);

    // 金色只剩兩個意思：**結構的邊**（框、線腳、分隔線）和**唯一的主要動作**。
    // 要強調別的東西就用難度色或深底奶油字 —— 到處都是金的時候，金就不是重點了。
    private static readonly Color Gold = new Color(0.800f, 0.605f, 0.265f, 1f);
    private static readonly Color ButtonPrimary = new Color(0.905f, 0.735f, 0.380f, 1f);
    private static readonly Color Toggled = new Color(0.325f, 0.200f, 0.090f, 1f);     // 開關「開著」
    private static readonly Color WoodRaised = new Color(0.560f, 0.395f, 0.222f, 1f); // 抽屜把手、分類條

    // ── 字級：六階，沒有第七階 ────────────────────────────────────────
    //
    // 上一版從 14 到 40 之間用掉十九種字級。17 和 18 並排時看不出是「兩種東西」，
    // 只看得出沒對齊。階數少，每一階才有意義：差一階就是差一個層級。
    // 字級表搬到 ClassicalType 去了，實際的值在 Build 裡向畫布問出來。
    //
    // **不寫死、也不當常數別名。** 那張表是照這個房間訂的（基準畫布 1080 高），而這
    // 裡的參考解析度目前正好也是 1080，所以換算是恆等的 —— 但正因為「目前正好」，
    // 寫成常數就埋了一個雷：哪天這個房間換了參考解析度，字會整批錯掉，而症狀只是
    // 「看起來怪怪的」。兩個房間都向自己的畫布問，改了誰都自動跟著對。
    //
    // 預設值是基準值，所以萬一有人在 Build 之前就讀到它們，拿到的也是合理的數字。
    private static float TextCaption = ClassicalType.Caption;
    private static float TextBody = ClassicalType.Body;
    private static float TextLabel = ClassicalType.Label;
    private static float TextTitle = ClassicalType.Title;
    private static float TextDisplay = ClassicalType.Display;
    private static float TextHero = ClassicalType.Hero;

    /// <summary>標題和全大寫拉丁字的字距。其餘一律 0。</summary>
    private const float Tracking = 4f;

    /// <summary>按鈕底色先壓暗一格，滑過去時再由 ColorBlock 乘回原色。</summary>
    /// <remarks>
    /// uGUI 的色彩過渡是**相乘**，而結果會夾在 1。上一版把 highlightedColor 設成
    /// 1.12，底色本來就接近白，乘完照樣是原色 —— 等於根本沒有懸停狀態。要讓它亮
    /// 得起來，底色就得先留出這一格空間。
    /// </remarks>
    private const float RestDim = 0.92f;


    private static Sprite woodSprite;
    private static Sprite birchSprite;
    private static Sprite woodAcrossSprite;
    private static Sprite shadowSprite;

    /// <summary>投影貼圖裡，形狀的邊離貼圖邊有多遠。外擴量就是照它算的。</summary>
    private const float ShadowInset = 16f;

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
    private readonly List<EdgeLightGraphic> rowEdges = new List<EdgeLightGraphic>();
    private RectTransform standRoot;
    private RectTransform listPanel;
    private RectTransform railPanel;
    private RectTransform frameHolderRect;
    private Coroutine coverRoutine;
    private Coroutine previewRoutine;
    private CanvasGroup rootGroup;
    private Coroutine revealRoutine;
    private bool closing;

    // 抽屜的補間。上一版是指數收斂，數學上永遠到不了終點：τ=71ms 的話走到剩 0.5px
    // 要 415ms，後面 251ms 都在磨最後 17px，而且**不可能**有「撞到底」的那一下。
    private float drawerFrom;
    private float drawerTime = -1f;
    private float drawerSpan;
    private bool drawerOpening;
    private readonly List<SongSelectionManager.SongOption> rowSongs =
        new List<SongSelectionManager.SongOption>();
    private readonly List<Button> difficultyButtons = new List<Button>();
    private readonly List<SongSelectionManager.SongOption> difficultyVariantsShown =
        new List<SongSelectionManager.SongOption>();
    private readonly List<TextMeshProUGUI> difficultyNames = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> difficultyLevels = new List<TextMeshProUGUI>();
    private readonly List<Color> difficultyTints = new List<Color>();
    private readonly List<DifficultyCardFill> difficultyFills = new List<DifficultyCardFill>();

    private RectTransform rootRect;
    private RectTransform frameHolder;   // 畫框本體：尺寸跟著曲繪的比例走
    private GameObject watchedSettingsPanel;
    private RectTransform listContent;
    private RectTransform listViewport;
    private SmoothList listScroller;
    private TextMeshProUGUI listEmptyLabel;
    private float searchDebounce = -1f;
    private CanvasGroup analysisGroup;
    private Coroutine analysisFade;
    private RectTransform drawer;
    private RectTransform drawerContents;
    private RectTransform drawerHandle;     // 現在是拉繩
    private BlindCordGraphic drawerCord;
    private float drawerHeight;
    private float drawerTarget;
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
    private MarqueeText standTitleMarquee;
    private TextMeshProUGUI standAuthor;
    private RectTransform difficultyRow;

    // ── 中上那塊評分銘牌 ──────────────────────────────────────────────
    private TextMeshProUGUI ratingValue;    // 大數字
    private TextMeshProUGUI ratingTitle;    // 初學／普通／精熟／名家
    private TextMeshProUGUI ratingBest;     // 目前是哪一首撐起來的
    private TextMeshProUGUI ratingNote;     // 上次那一局，含「低於實力所以沒計入」
    private TextMeshProUGUI ratingEmpty;    // 0 分時用一句話代替空條子
    private RectTransform ratingScale;      // 進度條＋門檻＋四項：0 分時整組讓位
    private RatingGaugeGraphic ratingGauge;
    private readonly RatingGaugeGraphic[] criterionGauges = new RatingGaugeGraphic[4];
    private readonly TextMeshProUGUI[] criterionValues = new TextMeshProUGUI[4];
    private Coroutine ratingSweep;

    private TextMeshProUGUI scoreValue;
    private TextMeshProUGUI scoreRank;
    private RawImage scoreRankArt;
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

    /// <summary>開練習室。announce=false 用在「回到上次的模式」這種自動路徑 ——
    /// 那不是使用者的動作，不該出聲。</summary>
    public static void Open(bool announce = true)
    {
        if (announce) UiSfx.Open();
        if (Instance != null)
        {
            Destroy(Instance.gameObject);
            Instance = null;
        }
        var go = new GameObject("PracticeRoomScreen");
        go.AddComponent<PracticeRoomScreen>();
    }

    // ── 離開再回來，要回到原位 ──────────────────────────────────────
    //
    // 這個房間是**每次重建**的：按「開始練習」前會 Destroy 自己，從結算頁回來時再
    // new 一個出來。所以所有的瀏覽狀態（分類、排序、搜尋、選到哪一首、捲到哪）都是
    // 欄位，重建一次就全部歸零 —— 玩家練同一首的第二遍，得重新找一次。
    //
    // 存成 static 而不是寫檔：要回去的是「剛剛那個位置」，而那件事的壽命就是這一次
    // 遊玩。寫檔的話還要處理曲庫被改掉、譜被刪掉、跨玩家等等，換到的只是「關掉遊戲
    // 明天打開還在原地」—— 那不是使用者要的那件事。
    private struct Browsing
    {
        public bool Valid;
        public SortMode Sort;
        public bool Descending;
        public bool Tutorials;
        public SongDifficultyStrip.Tier Tier;
        public string Category;     // 存名字不是索引：曲庫一變，索引就指到別人身上
        public string Search;
        public string Song;         // 和 LoadSongs 去重用的是同一把鍵
        public string Variant;      // chartFileName
        public float Scroll;
        public float Drawer;
    }

    private static Browsing remembered;

    /// <summary>和 <see cref="LoadSongs"/> 去重用的同一把鍵：使用者眼中的「同一首」。</summary>
    private static string SongKey(SongSelectionManager.SongOption song) =>
        song == null
            ? string.Empty
            : ((song.displayName ?? string.Empty) + "|" + (song.author ?? string.Empty))
                .Trim().ToLowerInvariant();

    /// <summary>離開前把瀏覽狀態收起來。每一條離開的路都會經過 OnDestroy。</summary>
    private void Remember()
    {
        remembered.Valid = true;
        remembered.Sort = sortMode;
        remembered.Descending = sortDescending;
        remembered.Tutorials = showTutorials;
        remembered.Tier = tierFilter;
        remembered.Category = categories.Count > 0
            ? categories[Mathf.Clamp(categoryIndex, 0, categories.Count - 1)]
            : AllCategories;
        remembered.Search = searchField != null ? searchField.text : string.Empty;
        remembered.Song = SongKey(selectedSong);
        remembered.Variant = selectedVariant != null ? selectedVariant.chartFileName : string.Empty;
        remembered.Scroll = listScroller != null ? listScroller.Current : 0f;
        remembered.Drawer = drawerTarget;
    }

    /// <summary>篩選條件要在 Build 之前就位 —— 清單是在 Build 裡第一次排出來的。</summary>
    private void RestoreFilters()
    {
        if (!remembered.Valid) return;
        sortMode = remembered.Sort;
        sortDescending = remembered.Descending;
        showTutorials = remembered.Tutorials;
        tierFilter = remembered.Tier;
    }

    /// <summary>搜尋框和分類要等 Build 把它們做出來（而且 LoadCategories 會把索引歸零）。</summary>
    private void RestoreControls()
    {
        if (!remembered.Valid) return;
        if (searchField != null && !string.IsNullOrEmpty(remembered.Search))
        {
            // 不觸發 onValueChanged：那會排一次 debounce 的 RefreshList，而等一下
            // LoadSongs 本來就會排一次，兩次之間清單會閃一下。
            searchField.SetTextWithoutNotify(remembered.Search);
        }
        if (!string.IsNullOrEmpty(remembered.Category))
        {
            int at = categories.IndexOf(remembered.Category);
            categoryIndex = at >= 0 ? at : 0;
        }
        // 抽屜本來開著就讓它開著：篩選條件被記住了，把它藏起來會讓人以為沒生效。
        drawerTarget = Mathf.Clamp(remembered.Drawer, 0f, DrawerHeight);
        drawerHeight = drawerTarget;
        ApplyDrawer();
        RefreshTutorialButton();
    }

    /// <summary>捲動位置。要等版面真的排完才算得出上限。</summary>
    private void RestoreView()
    {
        if (!remembered.Valid || listScroller == null || listContent == null) return;
        // 列是這一幀剛加上去的，而 VerticalLayoutGroup 要到下一次版面重排才知道內容
        // 有多高 —— 不強制重排一次，MaxScroll 還是 0，跳過去會被夾成 0。
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
        listScroller.Jump(remembered.Scroll);
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
        // 這一次遊玩裡待過這個房間的話，剛剛那個位置優先於設定裡的偏好。
        RestoreFilters();
        Build();
        RestoreControls();
        RefreshTierButtons();
        LoadSongs();
        RestoreView();
        RefreshRating();
        // 練習室是全螢幕的，底下的選曲畫面不必繼續跑版面和試聽。
        try { SongSelectionManager.Instance?.HideSelectionPanelOnly(); } catch { }
    }

    private void OnDestroy()
    {
        Remember();
        PlayerSlots.Changed -= HandlePlayerChanged;
        SyllabusProgress.Changed -= RefreshCourse;
        if (Instance == this) Instance = null;
    }

    /// <summary>換人了：清單上的勳章和月桂、還有評分牌，全部是上一位的。</summary>
    private void HandlePlayerChanged()
    {
        RefreshList();
        RefreshRating();
        RefreshCourse();
    }

    // ── 考程入口 ──────────────────────────────────────────────────────
    //
    // 頂欄右邊，和「MAXIMUM Lv.15」並排：那一條是「現在這個畫面的狀態」，而階級也
    // 是狀態，不是一個功能。左邊那三組（切換、標題、實力評分）講的是「你在哪」，右
    // 邊這一排講的是「你是誰、現在什麼設定」。
    //
    // 由右往左：設定(-36) · 難度(-176) · 檢定印(-372) · 技階牌(-434)。實力評分那塊
    // 卡的右緣在 1400，技階牌的左緣是 1428，留 28 的縫。
    private const float ChipSealX = -372f;
    private const float ChipTabletX = -434f;

    private RomanTabletGraphic courseTablet;
    private GradeSealGraphic courseSeal;
    private TextMeshProUGUI courseSealDigit;

    /// <summary>
    /// 頂欄右邊的兩枚記號：技階的黃銅牌、檢定的朱印。按下去翻開考程。
    /// </summary>
    /// <remarks>
    /// **為什麼是圖不是字。** 這一條上已經有五組字了，再加兩組就只是第六第七串 ——
    /// 掃過去分不出哪個是哪個。方牌 vs 圓印、黃銅 vs 朱砂，56px 上不讀字就認得出來
    /// （見 <see cref="SyllabusIcons"/>）。
    ///
    /// **為什麼兩枚都在練習室。** 全新存檔的 `recitalMode` 是 false，開機是轉盤閃一
    /// 下、練習室立刻蓋上來，所以一個從來沒按過「切換至演奏會」的人看不到檢定存在
    /// —— 而檢定是兩條階梯裡現在就拿得到的那一條。
    /// </remarks>
    private void BuildCourseChips(RectTransform bar)
    {
        RectTransform tablet = AddRect("CourseTablet", bar);
        Place(tablet, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(ChipTabletX, 0f), new Vector2(58f, 46f));
        tablet.gameObject.AddComponent<CanvasRenderer>();
        courseTablet = tablet.gameObject.AddComponent<RomanTabletGraphic>();
        Chip(tablet, courseTablet, SyllabusExamHall.Open);

        RectTransform seal = AddRect("CourseSeal", bar);
        Place(seal, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(ChipSealX, 0f), new Vector2(50f, 50f));
        seal.gameObject.AddComponent<CanvasRenderer>();
        courseSeal = seal.gameObject.AddComponent<GradeSealGraphic>();
        Chip(seal, courseSeal, SyllabusExamHall.OpenGrade);

        // 級數是印面上唯一的字。畫在印之後，才蓋得在朱砂上面。
        courseSealDigit = AddText(seal, "Digit", string.Empty, TextLabel,
            new Color(1f, 0.95f, 0.90f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(courseSealDigit.rectTransform, 6f, 5f, 6f, 5f);
        courseSealDigit.overflowMode = TextOverflowModes.Overflow;

        RefreshCourse();
        SyllabusProgress.Changed += RefreshCourse;
    }

    /// <summary>把一枚記號變成可以按的東西：投影、滑過變亮、按了做一件事。</summary>
    /// <remarks>
    /// 投影用 <c>Shadow</c> 不是一張方形底圖：這兩枚記號都不是方的（一塊切角的牌、一
    /// 枚圓印），方形的影子會露出輪廓外面。<c>Shadow</c> 複製的是圖形自己的網格。
    /// </remarks>
    /// <remarks>
    /// 兩枚記號開不同的東西，因為它們代表不同的階梯：**階數牌→技階考場**
    /// （技階的動作在考場）、**級數印→考程書**（檢定的動作在結算頁的「送審」，
    /// 所以印能做的就是翻帳）。兩枚都開書的話，技階永遠沒有入口。
    /// </remarks>
    private void Chip(RectTransform rect, MaskableGraphic art, System.Action open)
    {
        art.raycastTarget = true;
        ClassicalBookUITheme.AddShadow(rect.gameObject, new Color(0f, 0f, 0f, 0.45f),
            new Vector2(3f, -3f));

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = art;
        SilenceNavigation(button);
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.22f, 1.16f, 1.06f, 1f);
        colours.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
        colours.selectedColor = Color.white;
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        button.onClick.AddListener(() =>
        {
            UiSfx.Open();
            open?.Invoke();
        });
    }

    /// <summary>兩枚記號的內容：刻在牌上的階數、蓋在印上的級數與等第。</summary>
    private void RefreshCourse()
    {
        if (courseTablet == null) return;
        courseTablet.Numeral = SyllabusProgress.Stage;
        int grade = SyllabusProgress.Grade;
        courseSeal.Grade = grade;
        courseSeal.Seal = SyllabusLadder.SealRank(SyllabusProgress.Seal);
        courseSealDigit.text = grade > 0 ? grade.ToString() : string.Empty;
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
        if (screen.previewRoutine != null)
        {
            screen.StopCoroutine(screen.previewRoutine);
            screen.previewRoutine = null;
        }
        if (screen.previewSource != null) screen.previewSource.Stop();
    }

    // ──────────────────────────────────────────────────────────────
    //  鍵盤（MIDI）操作
    // ──────────────────────────────────────────────────────────────

    /// <summary>這個房間現在開著嗎。</summary>
    /// <remarks>
    /// 這個畫面是**每次重建**的（按開始練習前會 Destroy 自己），所以不需要額外的
    /// 開關旗標 —— 物件還活著而且沒在關閉途中，就等於開著。
    /// </remarks>
    public bool IsOpen => this != null && !closing && isActiveAndEnabled;

    /// <summary>鍵盤：在曲目清單裡往前／往後一首。</summary>
    /// <remarks>
    /// 走的是 <c>filtered</c>（畫面上真正列出來的那份），不是完整的 <c>songs</c> ——
    /// 有篩選的時候，鍵盤跳到一首看不見的歌會像是壞掉。
    /// </remarks>
    public void StepSelection(int delta)
    {
        if (!IsOpen || filtered.Count == 0 || delta == 0) return;
        int at = selectedSong != null ? filtered.IndexOf(selectedSong) : -1;
        at = at < 0 ? (delta > 0 ? 0 : filtered.Count - 1)
                    : ((at + delta) % filtered.Count + filtered.Count) % filtered.Count;
        SelectSong(filtered[at]);
    }

    /// <summary>鍵盤：開始練習。按鈕自己的 onClick 走一遍，不要複製它的內容。</summary>
    public void ConfirmFromKeyboard()
    {
        if (!IsOpen || startButton == null) return;
        startButton.onClick.Invoke();
    }

    public void Close()
    {
        if (closing) return;
        closing = true;
        UiSfx.Close();
        previewToken++;
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        // 畫面淡出 200ms，聲音也跟著走，不要在畫面還看得見的時候硬切。物件銷毀時
        // AudioSource 會一起消失，所以不必等淡完。
        if (previewSource != null && previewSource.isPlaying) StartCoroutine(FadePreview(0f, 0.18f));
        // 先把選曲畫面叫回來再淡出：兩層疊著交接，中間就不會閃一格空畫面。
        try { SongSelectionManager.Instance?.ShowSelectionPanelOnly(); } catch { }
        if (!isActiveAndEnabled || rootGroup == null)
        {
            Destroy(gameObject);
            return;
        }
        // 進場還沒跑完就要走：先把它停掉，否則兩條協程會同時搶同一個 alpha，誰贏
        // 是 Unity 的協程順序決定的，不是這段程式決定的。
        if (revealRoutine != null)
        {
            StopCoroutine(revealRoutine);
            revealRoutine = null;
        }
        StartCoroutine(FadeOutAndDestroy());
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

        // 字級要在**畫布做出來之後**問，但在建任何東西之前算好。
        //
        // 這個房間的 Canvas 是自己在這裡加的，所以放在方法開頭問的話 GetComponentInParent
        // 什麼都找不到，只會拿到基準值 —— 今天剛好對（基準也是 1080），哪天參考解析度
        // 一改就整批錯掉，而那正是不寫死常數要避開的事。
        TextCaption = ClassicalType.On(this, ClassicalType.Caption);
        TextBody = ClassicalType.On(this, ClassicalType.Body);
        TextLabel = ClassicalType.On(this, ClassicalType.Label);
        TextTitle = ClassicalType.On(this, ClassicalType.Title);
        TextDisplay = ClassicalType.On(this, ClassicalType.Display);
        TextHero = ClassicalType.On(this, ClassicalType.Hero);
        TextScale = new[] { TextCaption, TextBody, TextLabel, TextTitle, TextDisplay, TextHero };

        RectTransform root = AddRect("Root", transform);
        rootRect = root;
        Stretch(root);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();
        Image backdrop = root.gameObject.AddComponent<Image>();
        backdrop.sprite = WoodSprite();
        backdrop.type = Image.Type.Tiled;
        // 貼圖放大到接近兩格鋪滿整面牆。原尺寸鋪的話 1920 寬要重複 3.75 次，而現
        // 在板子的接縫是看得見的 —— 同一組板子出現四次，那就不是牆是壁紙。放大之
        // 後每塊板約 230px 寬，也比較接近真的護牆板。
        backdrop.pixelsPerUnitMultiplier = 0.55f;
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
        shade.color = new Color(0.10f, 0.055f, 0.025f, 0.62f);
        shade.raycastTarget = false;

        BuildTopBar(root);
        BuildSongList(root);
        BuildStand(root);
        BuildRail(root);
        BuildFilterBlind(root);
        BuildDrawerCord(root);

        // 進場：三欄各自淡入並且往自己的方向收進來，錯開 60ms。
        //
        // 同時到位的話，畫面是「啪」一下出現的 —— 那是網頁，不是房間。錯開之後眼
        // 睛會照著順序走一遍：先看到曲目、再看到中間這首、最後才是右邊的數據，剛
        // 好就是使用者接下來要做的事情的順序。
        PlayerSlots.Changed += HandlePlayerChanged;
        revealRoutine = StartCoroutine(RevealScreen());
    }

    /// <summary>
    /// 篩選的布幕：一塊獨立的布，從頂欄底下降下來蓋住曲目框。
    /// </summary>
    /// <remarks>
    /// **為什麼不是曲目面板裡的抽屜。** 上一版把它做成面板內的一個格子，所以
    /// 布是從「曲目」標題底下<b>長出來</b>的，而且清單要跟著讓位 —— 那是抽屜，
    /// 不是布幕。一塊布是掛在房間最上面那塊板子上、降下來蓋在東西前面的，它不在
    /// 任何人的格子裡。所以它直接掛在根節點上，而且建在曲目面板之後。
    ///
    /// 寬度和曲目欄一致，上緣點在頂欄底緣 —— 布從那塊板子底下出來，所以不需要
    /// 另畫一根軸。
    /// </remarks>
    private void BuildFilterBlind(RectTransform root)
    {
        drawer = AddRect("FilterBlind", root);
        drawer.anchorMin = new Vector2(0f, 1f);
        drawer.anchorMax = new Vector2(0f, 1f);
        drawer.pivot = new Vector2(0f, 1f);
        drawer.anchoredPosition = new Vector2(Margin, -TopBarHeight);
        drawer.sizeDelta = new Vector2(ListWidth, 0f);
        drawer.gameObject.AddComponent<RectMask2D>();

        // 裡面再包一層，內容整塊跟著布往下走。
        //
        // 所有東西貼在遽罩上緣、只動遽罩高度的話，那不是布降下來，是一塊遽罩
        // 往下擦、東西「長出來」。真的布是印在上面的東西跟著一起走。
        drawerContents = AddRect("Cloth", drawer);
        drawerContents.anchorMin = new Vector2(0f, 1f);
        drawerContents.anchorMax = new Vector2(1f, 1f);
        drawerContents.pivot = new Vector2(0.5f, 1f);
        drawerContents.offsetMin = new Vector2(0f, 0f);
        drawerContents.offsetMax = new Vector2(0f, 0f);
        drawerContents.sizeDelta = new Vector2(0f, DrawerHeight);

        // 布本身要不透明 —— 它現在是蓋在清單上面，不是嵌在面板裡。
        Image cloth = drawerContents.gameObject.AddComponent<Image>();
        cloth.sprite = WoodSprite();
        cloth.type = Image.Type.Tiled;
        cloth.color = FeltLight;
        cloth.raycastTarget = true;
        AddEdgeLight(drawerContents, false);

        // 下緘的壓條：捲簾底下那根帶重量的木條。少了它，布的下緣是一刀切的。
        // 掛在**簾口的下緣**：壓條永遠在布目前垂到的那一條邊上。
        RectTransform batten = AddRect("Batten", drawer);
        batten.anchorMin = new Vector2(0f, 0f);
        batten.anchorMax = new Vector2(1f, 0f);
        batten.pivot = new Vector2(0.5f, 0f);
        batten.offsetMin = Vector2.zero;
        batten.offsetMax = new Vector2(0f, 9f);
        Image battenImage = batten.gameObject.AddComponent<Image>();
        battenImage.sprite = WoodSpriteAcross();
        battenImage.type = Image.Type.Tiled;
        battenImage.color = WoodRaised;
        battenImage.raycastTarget = false;

        // 點布的空白處就收回去。放在最前面，所以在所有篩選控制項的**後面**
        // —— uGUI 的射線取最上層的那一個，按到按鈕就是按鈕。
        RectTransform catcher = AddRect("Catcher", drawerContents);
        catcher.anchorMin = Vector2.zero;
        catcher.anchorMax = Vector2.one;
        catcher.offsetMin = Vector2.zero;
        catcher.offsetMax = Vector2.zero;
        Image catcherImage = catcher.gameObject.AddComponent<Image>();
        catcherImage.color = new Color(0f, 0f, 0f, 0.0001f);
        var catcherButton = catcher.gameObject.AddComponent<Button>();
        catcherButton.transition = Selectable.Transition.None;
        catcherButton.onClick.AddListener(CloseDrawer);

        BuildDrawerContents(drawerContents);
        ApplyDrawer();
    }

    /// <summary>
    /// 布幕的拉繩：從**頂欄**底下垂下來，落在「練習室」字樣正下方。
    /// </summary>
    /// <remarks>
    /// **為什麼不掛在曲目面板上。** 捲簾是掛在房間最上面那塊板子上的，不是
    /// 掛在清單的標題列上 —— 繩從哪裡出來，眼睛就把那裡讀成軸。掛在面板上的
    /// 那一版，軸讀成了「曲目」那一排，而那排是標題不是木頭。
    ///
    /// x 跟著標題走：標題從 <c>Margin + 270</c> 開始、約 108 寬，所以中心在
    /// <c>Margin + 324</c>。這個 x 落在曲目欄（<c>Margin</c> 到 <c>Margin + ListWidth</c>）
    /// 裡面，所以繩正好在它控制的那塊布的上方。
    ///
    /// 最後才建，而且排在根的最上層：繩要從頂欄一路跨到布的下緣底下，
    /// 沿路會經過頂欄、托木和曲目面板。
    /// </remarks>
    private void BuildDrawerCord(RectTransform root)
    {
        drawerHandle = AddRect("DrawerCord", root);
        drawerHandle.anchorMin = new Vector2(0f, 1f);
        drawerHandle.anchorMax = new Vector2(0f, 1f);
        drawerHandle.pivot = new Vector2(0.5f, 1f);
        // 最左：一條垂在畫面邊上的繩不會和任何東西搶位置。
        drawerHandle.anchoredPosition = new Vector2(Margin + 30f, -TopBarHeight);
        drawerCord = drawerHandle.gameObject.AddComponent<BlindCordGraphic>();
        drawerCord.raycastTarget = true;    // 練習室是白的，演奏會才是金的
        // 長度固定：拉之前之後一樣長，只跑一個下去上來的拉扯。
        drawerHandle.sizeDelta = new Vector2(CordHitWidth, CordIdle + BlindCordGraphic.BeadReach);
        drawerHandle.gameObject.AddComponent<DrawerHandle>().Bind(this);
        drawerHandle.SetAsLastSibling();
        ApplyDrawer();                      // 長度跟著現在的開合狀態
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
        plate.sprite = BirchSprite();
        plate.type = Image.Type.Tiled;
        plate.color = Birch;
        // 下緣一條暗線，把板子和牆分開。
        RectTransform barEdge = AddRect("Edge", bar);
        barEdge.anchorMin = new Vector2(0f, 0f);
        barEdge.anchorMax = new Vector2(1f, 0f);
        barEdge.pivot = new Vector2(0.5f, 0f);
        barEdge.offsetMin = new Vector2(0f, -3f);
        barEdge.offsetMax = new Vector2(0f, 0f);
        Image barEdgeImage = barEdge.gameObject.AddComponent<Image>();
        // 這條線的用途是「頂欄壓在牆前面」的接縫，所以必須比牆**暗**。原本的
        // (0.16,0.095,0.045,0.7) 亮度 0.105，疊在被暗角壓過的牆（0.055）上反而更
        // 亮 —— 接縫變成一條淺色的縫。
        barEdgeImage.color = new Color(0.020f, 0.011f, 0.004f, 0.90f);
        barEdgeImage.raycastTarget = false;

        // 先放人，再放曲子。牌子要壓在那條暗線上面（它蓋掉自己那一段），但要排在
        // 標題和按鈕前面 —— 那兩者才是永遠在最上層的東西。
        BuildRatingPlaque(bar);

        // 左上角是模式切換，不是「返回」—— 練習室和演奏會是兩個空間，互為彼此的
        // 出口。選曲轉盤是演奏會那一側的畫面，所以切過去就等於回去了。
        Button toRecital = MakeButton(bar, "ToRecital", Localize.S("切換至演奏會"), TextLabel, new Vector2(248f, 56f));
        RectTransform toRecitalRect = toRecital.GetComponent<RectTransform>();
        Place(toRecitalRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(Margin, 0f), new Vector2(248f, 56f));
        RectTransform backChevron = AddRect("Chevron", toRecitalRect);
        Place(backChevron, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(12f, 0f), new Vector2(22f, 22f));
        Image backChevronImage = backChevron.gameObject.AddComponent<Image>();
        backChevronImage.sprite = ChevronSprite();
        backChevronImage.color = Ink;
        backChevronImage.raycastTarget = false;
        TextMeshProUGUI toRecitalLabel = toRecital.GetComponentInChildren<TextMeshProUGUI>();
        if (toRecitalLabel != null)
        {
            toRecitalLabel.alignment = TextAlignmentOptions.MidlineRight;
            toRecitalLabel.rectTransform.offsetMax = new Vector2(-12f, toRecitalLabel.rectTransform.offsetMax.y);
        }
        toRecital.onClick.AddListener(SwitchToRecital);

        // 標題的間隔用字距屬性，不是在字串裡打空格 —— 打空格的那一版，選字、搜尋
        // 和翻譯拿到的都是「練 習 室」。
        TextMeshProUGUI title = AddText(bar, "Title", Localize.S("練習室"), TextDisplay, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        // 框從 420 收到 150：「練習室」在 32 級加 4 字距下是 108 寬，420 的框會伸到
        // x=726，和中央的托木（左緣 460）重疊 266。框重疊本身不會畫出東西，但翻譯換
        // 成長一點的字就會撞上去。150 讓框停在 456，留 4 的縫。
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(Margin + 270f, 0f), new Vector2(150f, 52f));
        title.characterSpacing = Tracking;

        // 設定：練習室是一個完整的畫面，不能為了改一個設定還要先切回演奏會。
        Button settings = MakeButton(bar, "Settings", Localize.S("設定"), TextLabel, new Vector2(120f, 56f));
        Place(settings.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-Margin, 0f), new Vector2(120f, 56f));
        settings.onClick.AddListener(OpenSettings);

        BuildCourseChips(bar);

        hintLabel = AddText(bar, "Hint", string.Empty, TextBody, Ink,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal);
        // 同理：它只印「難度  Lv.15」（16 級，約 120 寬），620 的框會一路伸到 x=1088，
        // 壓進托木的右半邊。240 讓框從 1468 開始，托木的右緣是 1460。
        // 框從 240 收到 180：它是右對齊的，框的左半本來就是空的，而那 60 正好是
        // 兩枚記號和它之間要留的縫。「MAXIMUM  Lv.15」在 16 級下是 126 寬。
        Place(hintLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-(Margin + 140f), 0f), new Vector2(180f, 40f));
    }

    private void BuildSongList(RectTransform parent)
    {
        RectTransform panel = MakePanel(parent, "SongList", Felt);
        listPanel = panel;
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.offsetMin = new Vector2(Margin, BottomInset);
        panel.offsetMax = new Vector2(Margin + ListWidth, -ColumnTop);

        TextMeshProUGUI heading = AddText(panel, "Heading", Localize.S("曲目"), TextTitle, InkCream,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, -20f), new Vector2(200f, 40f));
        heading.characterSpacing = Tracking;

        listCount = AddText(panel, "Count", string.Empty, TextBody, InkCreamMuted,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal);
        Place(listCount.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-64f, -20f), new Vector2(110f, 40f));

        LoadCategories();

        // 清單本體
        RectTransform viewport = AddRect("Viewport", panel);
        listViewport = viewport;
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(16f, 74f);
        // 固定值：布幕是**蓋在**清單上面的，不再把清單推下去。
        viewport.offsetMax = new Vector2(-16f, -(DrawerTop + 30f));
        viewport.gameObject.AddComponent<RectMask2D>();
        Image viewportCatcher = viewport.gameObject.AddComponent<Image>();
        viewportCatcher.color = new Color(0f, 0f, 0f, 0.0001f);   // 只為了接滾輪和拖曳

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

        var scroll = panel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = listContent;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.12f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        // 滾輪交給 SmoothList：ScrollRect 自己的滾輪是一格一格跳的。
        scroll.scrollSensitivity = 0f;
        listEmptyLabel = AddText(viewport, "Empty", string.Empty, TextBody, InkCreamMuted,
            TextAlignmentOptions.Center, FontStyles.Normal);
        Place(listEmptyLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), new Vector2(ListWidth - 80f, 40f));
        listEmptyLabel.gameObject.SetActive(false);

        listScroller = viewport.gameObject.AddComponent<SmoothList>();
        listScroller.Bind(scroll);

        // 下緣的難度階：按哪一階，整份清單就用那一階的等級標示與排序。
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
                TextCaption, new Vector2(tierWidth, 44f));
            Place(button.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f + i * (tierWidth + 8f), 16f), new Vector2(tierWidth, 44f));
            button.onClick.AddListener(() => SetTier(tier));
            tierButtons.Add(button);
        }

    }

    /// <summary>抽屜裡的東西：分類、教學、排序、搜尋。</summary>
    private void BuildDrawerContents(RectTransform drawerRoot)
    {
        RectTransform categoryBar = AddRect("CategoryBar", drawerRoot);
        Place(categoryBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -10f), new Vector2(ListWidth - 88f, 46f));
        Image categoryPlate = categoryBar.gameObject.AddComponent<Image>();
        categoryPlate.sprite = WoodSpriteAcross();
        categoryPlate.type = Image.Type.Tiled;
        categoryPlate.color = WoodRaised;
        AddEdgeLight(categoryBar, false);

        Button categoryPrev = MakeButton(categoryBar, "CategoryPrev", string.Empty, TextBody,
            new Vector2(34f, 34f));
        Place(categoryPrev.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(6f, 0f), new Vector2(34f, 34f));
        AddChevron(categoryPrev.GetComponent<RectTransform>(), false);
        categoryPrev.onClick.AddListener(() => StepCategory(-1));

        // 不自動縮放：分類名稱長短不一，自動縮放的結果是每翻一頁字就大小不同，
        // 比「長的那個被截掉」更亂。固定一階，放不下的交給裁切。
        categoryLabel = AddText(categoryBar, "Category", string.Empty, TextBody, InkCream,
            TextAlignmentOptions.Center, FontStyles.Bold);
        Place(categoryLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(ListWidth - 176f, 30f));
        AddSoftShadow(categoryLabel);

        Button categoryNext = MakeButton(categoryBar, "CategoryNext", string.Empty, TextBody,
            new Vector2(34f, 34f));
        Place(categoryNext.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-6f, 0f), new Vector2(34f, 34f));
        AddChevron(categoryNext.GetComponent<RectTransform>(), true);
        categoryNext.onClick.AddListener(() => StepCategory(1));

        tutorialButton = MakeButton(drawerRoot, "Tutorial", Localize.S("新手教學"), TextBody, new Vector2(150f, 34f));
        Place(tutorialButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(8f, -66f), new Vector2(150f, 34f));
        tutorialButton.onClick.AddListener(() =>
        {
            showTutorials = !showTutorials;
            UiSfx.Toggle(showTutorials);
            RefreshTutorialButton();
            RefreshList();
            if (filtered.Count > 0) SelectSong(filtered[0]);
        });
        RefreshTutorialButton();

        Button sortButton = MakeButton(drawerRoot, "Sort", string.Empty, TextBody, new Vector2(130f, 34f));
        RectTransform sortRect = sortButton.GetComponent<RectTransform>();
        Place(sortRect, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-8f, -66f), new Vector2(130f, 34f));
        RectTransform funnelRect = AddRect("Funnel", sortRect);
        Place(funnelRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(10f, 0f), new Vector2(18f, 18f));
        funnelRect.gameObject.AddComponent<CanvasRenderer>();
        var funnel = funnelRect.gameObject.AddComponent<FunnelGlyph>();
        funnel.color = Ink;
        funnel.raycastTarget = false;
        sortLabel = AddText(sortRect, "SortLabel", string.Empty, TextBody, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(sortLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(34f, 0f), new Vector2(92f, 26f));
        sortButton.onClick.AddListener(StepSort);

        Button orderButton = MakeButton(drawerRoot, "SortOrder", string.Empty, TextBody,
            new Vector2(40f, 34f));
        RectTransform orderRect = orderButton.GetComponent<RectTransform>();
        Place(orderRect, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-146f, -66f), new Vector2(40f, 34f));
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

        RectTransform searchRect = AddRect("Search", drawerRoot);
        Place(searchRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -110f), new Vector2(ListWidth - 88f, 46f));
        Image searchPlate = searchRect.gameObject.AddComponent<Image>();
        // 和其他控制項走同一條路：底色壓暗一格，滑過去才會亮起來。少了這一行，
        // 搜尋框滑過去反而變暗，兩個相鄰的東西對同一個動作反應相反。
        PaperFace(searchPlate, Paper);
        // 搜尋框是**凹**進去的：上緣和左緣壓暗、下緣和右緣受光，和凸起的按鈕相反。
        AddEdgeLight(searchRect, true, 0f, 1.25f, CornerRadius).SetSides(new[]
        {
            new Color(0.34f, 0.22f, 0.10f, 0.60f), new Color(0.42f, 0.29f, 0.15f, 0.45f),
            new Color(0.92f, 0.84f, 0.66f, 0.40f), new Color(0.99f, 0.93f, 0.78f, 0.55f)
        });

        TextMeshProUGUI searchText = AddText(searchRect, "Text", string.Empty, TextLabel, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        Stretch(searchText.rectTransform, 16f, 4f, 16f, 4f);
        // 提示字用實色，不用半透明：半透明的字疊在紙紋上會被紋理吃掉一半。
        TextMeshProUGUI placeholder = AddText(searchRect, "Placeholder", Localize.S("搜尋曲名或作曲家"),
            TextLabel, new Color(0.520f, 0.430f, 0.310f, 1f),
            TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        Stretch(placeholder.rectTransform, 16f, 4f, 16f, 4f);

        searchField = searchRect.gameObject.AddComponent<TMP_InputField>();
        searchField.textViewport = searchRect;
        searchField.textComponent = searchText;
        searchField.placeholder = placeholder;
        searchField.targetGraphic = searchPlate;
        SilenceNavigation(searchField);
        // 和按鈕同一組狀態色。不設就是 Unity 的預設（highlighted 0.96），而底色已
        // 經壓暗了一格 —— 滑過去會**變暗**，和旁邊每一顆按鈕的反應相反。
        ColorBlock searchColours = searchField.colors;
        searchColours.normalColor = Color.white;
        searchColours.highlightedColor = new Color(1f / RestDim, 1f / RestDim, 1f / RestDim, 1f);
        searchColours.selectedColor = Color.white;
        searchColours.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        searchColours.fadeDuration = 0.08f;
        searchField.colors = searchColours;
        // 去抖。每打一個字就重建整份清單 —— 58 首＝348 個 GameObject 加 116 次
        // TMP 排版，全擠在同一幀；打字會一格一格卡。
        searchField.onValueChanged.AddListener(_ => searchDebounce = 0.14f);
    }

    /// <summary>把手被拉了多少（往下是開）。</summary>
    internal void DragDrawer(float delta)
    {
        drawerTime = -1f;                       // 手在拉的時候不補間
        drawerTarget = Mathf.Clamp(drawerTarget + delta, 0f, DrawerHeight);
        drawerHeight = drawerTarget;
        ApplyDrawer();
    }

    /// <summary>放手：過半就開，不到一半就收。</summary>
    internal void SettleDrawer()
    {
        drawerTarget = drawerTarget > DrawerHeight * 0.45f ? DrawerHeight : 0f;
        StartDrawerTween();
    }

    /// <summary>收回去。點板子的空白處走這裡。</summary>
    internal void CloseDrawer()
    {
        if (drawerTarget <= 0f) return;
        drawerTarget = 0f;
        UiSfx.Toggle(false);
        if (drawerCord != null) drawerCord.Tug();
        StartDrawerTween();
    }

    internal void ToggleDrawer()
    {
        drawerTarget = drawerTarget > DrawerHeight * 0.5f ? 0f : DrawerHeight;
        UiSfx.Toggle(drawerTarget > 0f);
        if (drawerCord != null) drawerCord.Tug();
        StartDrawerTween();
    }

    /// <summary>從現在的高度補到目標。拉開比收起慢一點，因為東西要「走出來」。</summary>
    private void StartDrawerTween()
    {
        drawerFrom = drawerHeight;
        drawerOpening = drawerTarget > drawerFrom;
        drawerSpan = drawerOpening ? 0.26f : 0.20f;
        drawerTime = 0f;
    }

    private void ApplyDrawer()
    {
        if (drawer == null) return;
        drawer.sizeDelta = new Vector2(drawer.sizeDelta.x, drawerHeight);
        // 內容不位移。
        //
        // 舊的寫法是內容跟著柜面一起走 —— 那是**抽屜**：抽屜正面先出來，所以
        // 東西是從下往上長出來的。捲簾反過來：布捲在上面的軸上，上緣釘死，
        // 拉下來的時候是下緣往下走、上面的內容先露出來。

    }

    /// <summary>
    /// 頂欄中央往下凸出的一塊白樺：演奏者現在的實力。
    /// </summary>
    /// <remarks>
    /// **為什麼從中欄搬上來。** 它本來壓在畫框上面，而中欄的高度預算早就用完了 ——
    /// 那塊 150 高的卡是從畫框（420 → 300）和走勢卡（168 → 136）身上割來的。曲繪是
    /// 那一欄唯一的主角，不該為了一塊銘牌縮掉三成。搬上來之後三欄各讓 52，畫框長回
    /// 430（比被割之前還大 10），走勢卡回到 152。
    ///
    /// **為什麼是「凸出來」不是「貼上去」。** 它用的是頂欄同一張白樺貼圖、同一個
    /// 色，所以讀起來不是一塊另外掛上去的牌子，而是**這片板子中間本來就比較寬**。
    /// 房間裡已經有三種材質（木、毛氈、紙）分別代表房間、家具、要讀的東西；再加第
    /// 四種只會多一個要解釋的東西。凸出的那一段靠一條 3px 暗線和牆分開 —— 頂欄本身
    /// 就是這樣和牆分開的，照抄同一句話。
    ///
    /// **為什麼排成橫的三段。** 高度只有 138（頂欄 92 ＋ 凸出 46），直的排不下。左邊
    /// 是「你現在幾分」、中間是「這條刻度長怎樣」、右邊是「哪一項卡住」—— 由粗到細，
    /// 和讀的順序一致。四項要分開畫，因為卡住的地方不一樣：四條小條和大條是同一個
    /// 刻度（都對滿分），所以「總分沒動」的時候還看得出是哪一項沒動。
    ///
    /// **刻度就是使用者訂的那條**（見 <see cref="PlayerRating"/>）：3000 普通、5000
    /// 精熟、6000 = Lv15 也能 S 且無 Miss。兩條刻線的位置、旁邊那兩個詞、四項的名字
    /// 全部是從 <c>PlayerRating</c> 的常數和 <c>Title()</c> 算出來的，一個字都沒有寫
    /// 死 —— 門檻改了這裡會自己跟著走，不會變成兩套說法。
    ///
    /// 小標寫明「練習模式」不是廢話：演奏會那一段整條刻度高 1000（滿分 7000），不寫
    /// 的話在轉盤那邊看到另一個滿分會以為是兩套規則。
    /// </remarks>
    private void BuildRatingPlaque(RectTransform bar)
    {
        const float pad = 24f;
        const float gaugeX = 244f;
        const float gaugeW = 330f;
        const float cellsX = 594f;
        const float cellW = 58f;      // 「失誤錯音」四個字在 14 級是 56 寬
        const float cellGap = 10f;
        const float rightEdge = RatingCardWidth - pad;      // 856

        // ── 托木 ────────────────────────────────────────────────────
        RectTransform corbel = AddRect("Corbel", bar);
        corbel.gameObject.AddComponent<CanvasRenderer>();
        Place(corbel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -TopBarHeight), new Vector2(RatingCorbelWidth, RatingPlaqueDrop));
        var shape = corbel.gameObject.AddComponent<PracticeCorbelGraphic>();
        shape.raycastTarget = false;
        Sprite birch = BirchSprite();
        shape.Set(birch != null ? birch.texture : null,
            birch != null ? birch.texture.width : 256f, RatingCorbelInset, Birch,
            // 和頂欄底下那條同一個值。它必須比牆**暗**，不然接縫會變成一條淺色的縫。
            new Color(0.020f, 0.011f, 0.004f, 0.90f));

        // 托木要蓋掉頂欄那條橫貫的暗線在自己這一段：板子中間變厚，線就該繞下來。上
        // 緣本來就不畫接縫（見 PracticeCorbelGraphic），所以補一條同色的木頭把它遮
        // 掉 —— 比去改頂欄那條線的寬度便宜，而且頂欄不必知道有人掛了東西在它底下。
        RectTransform patch = AddRect("SeamPatch", bar);
        Place(patch, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -TopBarHeight + 1f), new Vector2(RatingCorbelWidth - 2f, 5f));
        Image patchImage = patch.gameObject.AddComponent<Image>();
        patchImage.sprite = birch;
        patchImage.type = Image.Type.Tiled;
        patchImage.color = Birch;
        patchImage.raycastTarget = false;

        // ── 牌子 ────────────────────────────────────────────────────
        // 評分是要**讀**的東西，所以它在紙上 —— 和中欄那三張卡同一套家具（同一張紙
        // 面、同一道內規線、同一個邊光、同一個投影）。直接刻在木頭上的話，房間裡唯
        // 一一個「木頭上有字」的東西會是它，而那是招牌不是資料。
        RectTransform card = MakeCard(bar, "RatingCard", RatingCardTop,
            RatingCardHeight, RatingCardWidth);
        AddInnerRule(card);

        TextMeshProUGUI caption = AddText(card, "Caption", Localize.S("實力評分・練習模式"), TextCaption,
            InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, -8f), new Vector2(200f, 18f));
        caption.characterSpacing = Tracking;

        // 大數字靠左，評語緊接在右邊。評語的 x 是**固定**的：靠數字的右緣長出來的寫
        // 法會讓「普通」每多一位數就往右跳一次，而位數是會變的。
        ratingValue = AddText(card, "Value", "—", TextHero, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(ratingValue.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, -28f), new Vector2(100f, 46f));

        ratingTitle = AddText(card, "Title", string.Empty, TextTitle, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        // 兩個框的**中心**對齊（−51），不是上緣對齊 —— 40 級和 24 級上緣對齊的話，小
        // 的那個會浮在大的肩膀上。
        Place(ratingTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad + 100f, -34f), new Vector2(100f, 32f));

        // 撐起這個評分的是哪一首。`bestSong` 一直有存，只是從來沒有人顯示過。
        ratingBest = AddText(card, "Best", string.Empty, TextCaption, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        Place(ratingBest.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, -78f), new Vector2(200f, 18f));

        // 有資料的那一整組。0 分的時候整組讓位給一句話：空的條子看起來像壞掉，而新
        // 玩家最需要知道的正好是「這條怎麼長」。
        RectTransform scale = AddRect("Scale", card);
        Stretch(scale);
        ratingScale = scale;

        float ceiling = 6000f;
        try { ceiling = PlayerRating.Ceiling(false); } catch { }
        ceiling = Mathf.Max(1f, ceiling);

        // ── 中：刻度 ────────────────────────────────────────────────
        // 滿分那一格放在條子**上面**的右端。和兩條門檻同一列的話，「精熟」那一格的右
        // 緣（569）會壓到滿分那一格的左緣（494）。
        TextMeshProUGUI top = AddText(scale, "Ceiling",
            Mathf.RoundToInt(ceiling) + " " + PlayerRating.Title(ceiling, false),
            TextCaption, InkMuted, TextAlignmentOptions.MidlineRight, FontStyles.Normal);
        Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(gaugeX + gaugeW, -8f), new Vector2(150f, 18f));

        ratingGauge = AddGauge(scale, "Gauge", new Vector2(gaugeX, -40f), new Vector2(gaugeW, 14f));
        ratingGauge.Marks = new[]
        {
            PlayerRating.Ordinary / ceiling, PlayerRating.Proficient / ceiling
        };
        // 條子是**凹**在紙裡的一道槽（和曲繪同一個道理）：上緣內陰影、下緣一條受光的
        // 細線。四邊一樣亮的框是平的，看起來就是網頁上的進度條。
        AddEdgeLight(ratingGauge.rectTransform, true, 0f, 1.25f, 4f).SetSides(new[]
        {
            new Color(0.02f, 0.01f, 0.00f, 0.55f), new Color(0.04f, 0.02f, 0.01f, 0.40f),
            new Color(0.30f, 0.24f, 0.16f, 0.25f), new Color(1.00f, 0.96f, 0.88f, 0.30f)
        });

        // 門檻的字跟著刻線的位置擺，位置又是從門檻算的 —— 兩邊同一個來源才不會有
        // 「線在這裡、字在那裡」。
        AddThreshold(scale, "Ordinary", PlayerRating.Ordinary, ceiling, gaugeX, gaugeW, -58f);
        AddThreshold(scale, "Proficient", PlayerRating.Proficient, ceiling, gaugeX, gaugeW, -58f);

        // ── 右：哪一項卡住 ──────────────────────────────────────────
        // 四條小條和大條是同一個刻度（都對滿分），所以「總分沒動」的時候還看得出是哪
        // 一項沒動。合成一個數字就只剩「沒進步」三個字。
        for (int i = 0; i < 4; i++)
        {
            float x = cellsX + i * (cellW + cellGap);
            string name = i < PlayerRating.CriterionNames.Length
                ? PlayerRating.CriterionNames[i] : string.Empty;
            TextMeshProUGUI label = AddText(scale, "Criterion" + i, name, TextCaption, InkMuted,
                TextAlignmentOptions.Center, FontStyles.Normal);
            Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -8f), new Vector2(cellW, 18f));

            criterionGauges[i] = AddGauge(scale, "CriterionBar" + i,
                new Vector2(x, -30f), new Vector2(cellW, 9f));

            criterionValues[i] = AddText(scale, "CriterionValue" + i, "—", TextCaption, Ink,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Place(criterionValues[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -41f), new Vector2(cellW, 18f));
        }

        // 上次那一局排在最下面一行 —— 它是一句話，不是一個欄位。它從刻度那一段的左緣
        // 開始而不是牌子的左緣：左邊那一段最底下已經被「目前由《…》撐起」佔住了。
        ratingNote = AddText(scale, "Note", string.Empty, TextCaption, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        Place(ratingNote.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(gaugeX, -80f), new Vector2(rightEdge - gaugeX, 18f));

        // 沒有紀錄的時候講話，不畫空條子。這一句要換行，框也給得比兩行高 —— TMP 在框
        // 高不足時是**整段不畫**。
        //
        // 三個數字和兩個詞也是從常數和 Title() 長出來的：這句話是新玩家唯一看得到刻度
        // 說明的地方，寫死的話門檻一改它就開始騙人。
        // 整句一起翻，不要逐段相接：日文的語序和中文不同，接出來會是中文骨架配
        // 日文零件。數字和兩個評語照樣是參數，門檻一改這句話還是對的。
        string ladder = Localize.SF(
            "還沒有練習模式的紀錄。打完一首就會開始累積：{0} 是{1}、{2} 是{3}，" +
            "{4} 表示 Lv15 也能 S 而且一個 Miss 都沒有。",
            Mathf.RoundToInt(PlayerRating.Ordinary),
            PlayerRating.Title(PlayerRating.Ordinary, false),
            Mathf.RoundToInt(PlayerRating.Proficient),
            PlayerRating.Title(PlayerRating.Proficient, false),
            Mathf.RoundToInt(ceiling));
        ratingEmpty = AddText(card, "Empty", ladder,
            TextCaption, InkMuted, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Place(ratingEmpty.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(gaugeX, -26f), new Vector2(rightEdge - gaugeX, 72f));
        ratingEmpty.textWrappingMode = TextWrappingModes.Normal;
        ratingEmpty.gameObject.SetActive(false);
    }

    /// <summary>一條門檻的刻線標籤。字是 <c>PlayerRating.Title</c> 給的，不是打上去的。</summary>
    private static void AddThreshold(RectTransform parent, string name, float threshold,
        float ceiling, float pad, float barWidth, float y)
    {
        TextMeshProUGUI label = AddText(parent, name,
            Mathf.RoundToInt(threshold) + " " + PlayerRating.Title(threshold, false),
            TextCaption, InkMuted, TextAlignmentOptions.Center, FontStyles.Normal);
        Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f),
            new Vector2(pad + barWidth * Mathf.Clamp01(threshold / ceiling), y),
            new Vector2(100f, 18f));
    }

    /// <summary>一條量表：凹在紙裡的槽，值從左邊長出來。</summary>
    private static RatingGaugeGraphic AddGauge(RectTransform parent, string name,
        Vector2 position, Vector2 size)
    {
        RectTransform rect = AddRect(name, parent);
        Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), position, size);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var gauge = rect.gameObject.AddComponent<RatingGaugeGraphic>();
        gauge.raycastTarget = false;
        gauge.Groove = new Color(Ink.r, Ink.g, Ink.b, 0.16f);
        // 值那一塊上亮下暗（凸），和槽的上暗下亮剛好相反 —— 同一個光源，兩種地形。
        gauge.FillTop = ButtonPrimary;
        gauge.FillBottom = Color.Lerp(Gold, Ink, 0.45f);
        gauge.Mark = new Color(Ink.r, Ink.g, Ink.b, 0.55f);
        return gauge;
    }

    private void BuildStand(RectTransform parent)
    {
        RectTransform centre = AddRect("Stand", parent);
        standRoot = centre;
        centre.anchorMin = new Vector2(0f, 0f);
        centre.anchorMax = new Vector2(1f, 1f);
        centre.offsetMin = new Vector2(Margin * 2f + ListWidth, BottomInset);
        centre.offsetMax = new Vector2(-(Margin * 2f + RailWidth), -ColumnTop);

        // 中間是一幅掛起來的畫：外框、卡紙、曲繪，底下曲名和作者。
        //
        // 譜架那一版做得出來，但它把注意力吃光了 —— 中欄要講的只有「是這一首」，
        // 器材本身不該比封面搶眼。畫框相反：它的作用就是把視線往內收。
        // 畫框由下緣往上長：曲繪的比例不同時，框的高度會變，但底下曲名的位置不
        // 該跟著跳。
        RectTransform frameRect = AddRect("Frame", centre);
        Place(frameRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
            new Vector2(0f, FrameBottomY), new Vector2(FrameMax, FrameMax));
        frameHolder = frameRect;
        frameHolderRect = frameRect;
        frameRect.gameObject.AddComponent<CanvasRenderer>();
        var frameGraphic = frameRect.gameObject.AddComponent<PictureFrameGraphic>();
        frameGraphic.raycastTarget = false;

        RectTransform coverRect = AddRect("Cover", frameRect);
        // 卡紙的留白：框內再退一圈，畫才不會頂到框。
        Stretch(coverRect, FrameMat, FrameMat, FrameMat, FrameMat);
        coverImage = coverRect.gameObject.AddComponent<Image>();
        coverImage.color = new Color(1f, 1f, 1f, 0.12f);
        coverImage.preserveAspect = true;
        // 畫是**凹**在框裡的，不是浮在框上 —— 上一版給了它一個往下的投影，等於在
        // 說畫貼在玻璃前面。改成上緣和左緣的內陰影，下緣補一條受光的細線。
        AddEdgeLight(coverRect, true).SetSides(new[]
        {
            new Color(0.02f, 0.01f, 0.00f, 0.70f), new Color(0.04f, 0.02f, 0.01f, 0.50f),
            new Color(0.30f, 0.24f, 0.16f, 0.30f), new Color(1.00f, 0.96f, 0.88f, 0.22f)
        });

        // 畫框的四角也蓋同一張捲草 —— 真的畫框就是在斜接處蓋裝飾片。往外一點，讓
        // 它壓在線腳上而不是浮在畫面內。
        AddCardOrnament(frameRect, 66f, -8f);


        // 曲名緊接在框底下，作者再接在曲名底下 —— 兩塊都用「頂端對齊」放，y 就
        // 直接是它的上緣，加減看得出來，也不會再排錯順序。
        //
        // 裁切框要比字**高**：RectMask2D 是硬邊，框和字一樣高的話，字的上下緣會
        // 被削掉一條（中文的部首和日文的濁點最先不見）。只裁左右，不裁上下。
        RectTransform titleClip = AddRect("TitleClip", centre);
        Place(titleClip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
            new Vector2(0f, TitleTopY), new Vector2(CentreWidth, TitleHeight));
        titleClip.gameObject.AddComponent<RectMask2D>();

        standTitle = AddText(titleClip, "Title", string.Empty, TextHero, InkCream,
            TextAlignmentOptions.Center, FontStyles.Bold);
        AddSoftShadow(standTitle);
        // 長曲名不縮字級，用跑馬燈跑：縮到 24 的日文長曲名幾乎讀不出來。
        standTitle.overflowMode = TextOverflowModes.Overflow;
        Place(standTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(CentreWidth, TitleHeight - 8f));
        standTitleMarquee = standTitle.gameObject.AddComponent<MarqueeText>();
        standTitleMarquee.Bind(standTitle, CentreWidth);

        // 作者也要裁：640 的框放不下的長掛名，往左會蓋到曲目清單上。
        RectTransform authorClip = AddRect("AuthorClip", centre);
        Place(authorClip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
            new Vector2(0f, AuthorTopY), new Vector2(CentreWidth, AuthorHeight));
        authorClip.gameObject.AddComponent<RectMask2D>();

        standAuthor = AddText(authorClip, "Author", string.Empty, TextTitle, InkCreamMuted,
            TextAlignmentOptions.Center, FontStyles.Normal);
        AddSoftShadow(standAuthor);
        Stretch(standAuthor.rectTransform);

        // 難度、成績、譜面預覽：跟著曲子走的東西放在曲子底下。
        //
        // 右欄留給分析，因為那是「看資料」；這三樣是「選哪一份、我打得怎樣、先看
        // 一眼」—— 它們和中間那張封面是同一件事的三個面向，分開兩欄反而要來回看。
        difficultyRow = MakeCard(centre, "Difficulties", DifficultyTopY, DifficultyHeight, CentreWidth);
        AddInnerRule(difficultyRow);
        difficultyRow.anchorMin = difficultyRow.anchorMax = new Vector2(0.5f, 0.5f);
        difficultyRow.pivot = new Vector2(0.5f, 1f);
        difficultyRow.anchoredPosition = new Vector2(0f, DifficultyTopY);

        RectTransform scoreCard = MakeCard(centre, "ScoreCard", ScoreTopY, ScoreHeight, CentreWidth);
        AddInnerRule(scoreCard);
        scoreCard.anchorMin = scoreCard.anchorMax = new Vector2(0.5f, 0.5f);
        scoreCard.pivot = new Vector2(0.5f, 1f);
        scoreCard.anchoredPosition = new Vector2(0f, ScoreTopY);

        TextMeshProUGUI scoreCaption = AddText(scoreCard, "Caption", Localize.S("最佳成績"), TextCaption, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(scoreCaption.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(24f, 0f), new Vector2(160f, 40f));
        scoreCaption.characterSpacing = Tracking;

        scoreValue = AddText(scoreCard, "Score", Localize.S("無紀錄"), TextHero, Ink,
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(scoreValue.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-96f, 0f), new Vector2(320f, 52f));

        // 等第用畫好的那張圖，文字只是後備。
        //
        // 金色的字打在紙面上只有 2.04:1 —— 那是整張卡上最想被看到的東西，卻是最
        // 看不清楚的。同一套等第圖 Resources/UI/Results/Ranks/ 已經在結算畫面和難
        // 度牌上用了，這裡沒有理由自己用字再畫一次。
        RectTransform rankRect = AddRect("RankArt", scoreCard);
        Place(rankRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f), new Vector2(64f, 52f));
        scoreRankArt = rankRect.gameObject.AddComponent<RawImage>();
        scoreRankArt.raycastTarget = false;
        scoreRankArt.enabled = false;

        scoreRank = AddText(scoreCard, "Rank", string.Empty, TextDisplay, Gold,
            TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        Place(scoreRank.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f), new Vector2(72f, 52f));

        // 整首的走勢：密度、難點、還有你自己的分數曲線。
        //
        // 這是練習室和選曲畫面最大的差別 —— 選曲問「要不要打這首」，練習室問「這
        // 首的哪裡會掉分」。密度和難點是譜面自己的性質，分數曲線是你的，兩者疊在
        // 同一條時間軸上，落差就是該練的地方。
        RectTransform trendCard = MakeCard(centre, "Trend", TrendTopY, TrendHeight, CentreWidth);
        AddInnerRule(trendCard, 8f);
        trendCard.anchorMin = trendCard.anchorMax = new Vector2(0.5f, 0.5f);
        trendCard.pivot = new Vector2(0.5f, 1f);
        trendCard.anchoredPosition = new Vector2(0f, TrendTopY);

        TextMeshProUGUI trendCaption = AddText(trendCard, "Caption", Localize.S("密度走勢・難點・得分"),
            TextCaption, InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(trendCaption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -10f), new Vector2(336f, 22f));
        trendCaption.characterSpacing = Tracking;

        trendNote = AddText(trendCard, "Note", string.Empty, TextCaption, InkMuted,
            TextAlignmentOptions.MidlineRight, FontStyles.Normal);
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
        railPanel = rail;
        rail.anchorMin = new Vector2(1f, 0f);
        rail.anchorMax = new Vector2(1f, 1f);
        rail.pivot = new Vector2(1f, 0.5f);
        rail.offsetMin = new Vector2(-(Margin + RailWidth), BottomInset);
        rail.offsetMax = new Vector2(-Margin, -ColumnTop);

        float cardWidth = RailWidth - 48f;
        float y = -24f;

        // 譜面分析：這一份譜長什麼樣子。
        //
        // 三張圖各回答一個問題 —— 雷達說「靠什麼技巧」、密度說「哪一段最擠、兩手
        // 怎麼分」、BPM 曲線說「速度變不變」。文字那一行是給不想讀圖的人的摘要。
        y = AddSectionHeading(rail, Localize.S("譜面分析"), y, 116f);

        // 譜面預覽接在分析標題旁：它們回答的是同一個問題（這份譜長什麼樣），只是
        // 一個用數字、一個用眼睛。
        Button viewChart = MakeButton(rail, "ViewChart", Localize.S("預覽"), TextLabel, new Vector2(104f, 38f));
        Place(viewChart.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-24f, -20f), new Vector2(104f, 38f));
        viewChart.onClick.AddListener(OpenChartViewer);

        // 分析區用**比例**切，不是寫死高度。
        //
        // 寫死的話，畫面短一點（或之後加一張圖）最底下那張就會被切掉 —— 上一版的
        // BPM 曲線連刻度都露不出來。這裡改成先框出「標題到速度卡之間」的那一塊，
        // 再把四張卡按比例分掉，所以任何高度都排得下。
        RectTransform analysisArea = AddRect("AnalysisArea", rail);
        analysisGroup = analysisArea.gameObject.AddComponent<CanvasGroup>();
        analysisArea.anchorMin = new Vector2(0.5f, 0f);
        analysisArea.anchorMax = new Vector2(0.5f, 1f);
        analysisArea.pivot = new Vector2(0.5f, 0.5f);
        analysisArea.sizeDelta = new Vector2(cardWidth, 0f);
        analysisArea.offsetMin = new Vector2(-cardWidth * 0.5f, 232f);   // 讓出速度＋開始
        analysisArea.offsetMax = new Vector2(cardWidth * 0.5f, y - 8f);

        RectTransform summaryCard = MakeStretchCard(analysisArea, "Summary", 0.80f, 1.00f);
        analysisText = AddText(summaryCard, "Text", Localize.S("分析中…"), TextBody, Ink,
            TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Stretch(analysisText.rectTransform, 18f, 12f, 18f, 12f);
        analysisText.textWrappingMode = TextWrappingModes.Normal;   // 只有這張要換行
        // 這張夠高（>字級×1.5），可以安全用省略號；字級固定之後，太長的分析要收
        // 在卡片裡，不能滿出去壓到底下的雷達圖。
        analysisText.overflowMode = TextOverflowModes.Ellipsis;

        RectTransform radarCard = MakeStretchCard(analysisArea, "Radar", 0.36f, 0.775f);
        RectTransform radarRect = AddRect("Graphic", radarCard);
        Stretch(radarRect, 14f, 14f, 14f, 14f);
        radarRect.gameObject.AddComponent<CanvasRenderer>();
        radar = radarRect.gameObject.AddComponent<ChartRadarGraphic>();
        radar.raycastTarget = false;
        radar.Ink = Ink;

        RectTransform densityCard = MakeStretchCard(analysisArea, "Density", 0.185f, 0.335f);
        AddCardCaption(densityCard, Localize.S("密度／左右手"));
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
        startButton = MakeButton(rail, "Start", Localize.S("開始練習"), TextTitle,
            new Vector2(cardWidth, 80f), true);
        Place(startButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 24f), new Vector2(cardWidth, 80f));
        startButton.onClick.AddListener(StartPractice);

        // 卡片高一點，標題放進卡片內。上一版標題壓在卡片上緣，被上面的元件切掉一半。
        RectTransform speedRow = MakeCard(rail, "SpeedCard", 0f, 96f, cardWidth);
        speedRow.anchorMin = speedRow.anchorMax = new Vector2(0.5f, 0f);
        speedRow.pivot = new Vector2(0.5f, 0f);
        speedRow.anchoredPosition = new Vector2(0f, 118f);

        TextMeshProUGUI speedCaption = AddText(speedRow, "Caption", Localize.S("速度"), TextCaption, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(speedCaption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(18f, -10f), new Vector2(120f, 22f));
        speedCaption.characterSpacing = Tracking;

        Button slower = MakeButton(speedRow, "Slower", "−", TextTitle, new Vector2(64f, 56f));
        Place(slower.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(14f, -12f), new Vector2(64f, 54f));
        // 長按連發：1.00 調到 0.50 要按十下，而調速度本來就是一邊聽一邊找。
        AddRepeat(slower, () => NudgeSpeed(-0.05f));

        speedValue = AddText(speedRow, "Value", "1.00×", TextDisplay, Ink,
            TextAlignmentOptions.Center, FontStyles.Bold);
        Place(speedValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-12f, -12f), new Vector2(170f, 54f));

        Button faster = MakeButton(speedRow, "Faster", "+", TextTitle, new Vector2(64f, 56f));
        Place(faster.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-112f, -12f), new Vector2(64f, 54f));
        AddRepeat(faster, () => NudgeSpeed(0.05f));

        Button reset = MakeButton(speedRow, "Reset", Localize.S("原速"), TextLabel, new Vector2(84f, 56f));
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

        SettingsUiKit.SetText(analysisText, Localize.S("分析中…"));
        StartCoroutine(ChartAnalysisCache.Build(chartFile,
            result => ApplyAnalysis(result, chartFile)));
    }

    private void ApplyAnalysis(ChartAnalysis analysis, string forChartFile)
    {
        // 分析是非同步的，回來的時候使用者可能已經換了難度。
        if (!string.Equals(analysisChartFile, forChartFile)) return;
        FadeInAnalysis();

        if (analysis == null)
        {
            SettingsUiKit.SetText(analysisText, Localize.S("這一份譜沒有分析資料"));
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
            Localize.SF("音符 {0:N0}　長押 {1:N0}", analysis.noteCount,
                analysis.holdCount) + "\n" +
            Localize.SF("平均 {0:0.0}/s　峰值 {1:0}/s", analysis.averageDensity,
                analysis.peakDensity) + "\n" +
            (string.IsNullOrEmpty(techniques) ? character : techniques));

        radar?.SetData(analysis);
        if (trend != null)
        {
            trend.SetChart(analysis.leftDensity, analysis.rightDensity, analysis.peakBucketTotal);
            // 每小節的得分要在遊玩時記錄下來才有；目前還沒有那一份資料。
            trend.SetPlayerScores(null, null);
            SettingsUiKit.SetText(trendNote, Localize.S("尚無遊玩紀錄"));
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
        PaperFace(image, Paper, false);
        AddDropShadow(rect, 18f, 0.48f);
        AddGrain(rect, name, Paper);
        AddEdgeLight(rect, true, 0f, 1.25f, CornerRadius);
        return rect;
    }

    /// <summary>
    /// 四角的捲草角飾。用畫好的圖，不是拿方塊拼的。
    /// </summary>
    /// <remarks>
    /// 幾何拼出來的角飾永遠是直角和粗線 —— 古典裝飾的味道全在**變粗細的曲線**上，
    /// 那是頂點做不出來的。所以改成一張描好的圖（白色描邊、形狀在 alpha 裡），顏色
    /// 交給 UI 的 tint。
    ///
    /// 一張圖轉四個角：鏡像本來就是對稱裝飾的做法，也省下三張貼圖。
    /// </remarks>
    private static void AddCardOrnament(RectTransform card, float size = 34f, float inset = 3f)
    {
        Sprite sprite = CornerSprite();
        if (sprite == null || card == null) return;

        // 四個角用**旋轉**，不是鏡像。
        //
        // 鏡像要配合 pivot 才對得準：pivot 放在角上、再用負的 localScale 翻，翻轉
        // 是繞著那個 pivot 做的，圖案會被甩到框外（就是「沒框準」的樣子）。改成
        // pivot 置中、rect 是正方形，旋轉 90° 的整數倍只是把圖案轉到另一個角，位置
        // 完全不動 —— 而且這張捲草沿對角線對稱，轉過去和鏡像看起來一樣。
        float half = size * 0.5f;
        for (int i = 0; i < 4; i++)
        {
            bool right = (i & 1) != 0;
            bool bottom = (i & 2) != 0;
            RectTransform rect = AddRect("Corner" + i, card);
            Place(rect,
                new Vector2(right ? 1f : 0f, bottom ? 0f : 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(right ? -(inset + half) : inset + half,
                            bottom ? inset + half : -(inset + half)),
                new Vector2(size, size));
            // 原圖的捲草在左上角：右上轉 -90、右下轉 180、左下轉 90。
            float angle = bottom ? (right ? 180f : 90f) : (right ? -90f : 0f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = new Color(0.46f, 0.31f, 0.13f, 0.75f);
            image.raycastTarget = false;
        }
    }

    /// <summary>按鈕上的尖角箭頭。用描好的圖，比字型裡的 ‹ › 精緻得多。</summary>
    /// <remarks>
    /// `‹` 和 `›` 是字元，粗細和字型綁在一起：CJK 字型裡它們常常又細又小，和旁邊
    /// 的裝飾對不起來。改成一張雙線收尖、前端帶小菱形的圖，一張轉兩個方向。
    /// </remarks>
    private static void AddChevron(RectTransform button, bool pointsRight)
    {
        Sprite sprite = ChevronSprite();
        if (sprite == null || button == null) return;
        RectTransform rect = AddRect("Chevron", button);
        Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(24f, 24f));
        if (pointsRight) rect.localRotation = Quaternion.Euler(0f, 0f, 180f);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Ink;
        image.raycastTarget = false;
    }

    private static Sprite chevronSprite;
    private static bool chevronSpriteLoaded;

    private static Sprite ChevronSprite()
    {
        if (chevronSpriteLoaded) return chevronSprite;
        chevronSpriteLoaded = true;
        try { chevronSprite = Resources.Load<Sprite>("graphic/ornament_chevron"); } catch { }
        if (chevronSprite == null)
        {
            try
            {
                Texture2D texture = Resources.Load<Texture2D>("graphic/ornament_chevron");
                if (texture != null)
                {
                    chevronSprite = Sprite.Create(texture,
                        new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch { }
        }
        return chevronSprite;
    }

    private static Sprite cornerSprite;
    private static bool cornerSpriteLoaded;

    private static Sprite CornerSprite()
    {
        if (cornerSpriteLoaded) return cornerSprite;
        cornerSpriteLoaded = true;
        try { cornerSprite = Resources.Load<Sprite>("graphic/ornament_corner"); } catch { }
        if (cornerSprite == null)
        {
            // 匯入設定不是 Sprite 的話自己包一層，不要整組裝飾就這樣消失。
            try
            {
                Texture2D texture = Resources.Load<Texture2D>("graphic/ornament_corner");
                if (texture != null)
                {
                    cornerSprite = Sprite.Create(texture,
                        new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch { }
        }
        return cornerSprite;
    }

    private static void AddCardCaption(RectTransform card, string text)
    {
        TextMeshProUGUI caption = AddText(card, "Caption", text, TextCaption, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(16f, -8f), new Vector2(260f, 22f));
    }

    private float AddSectionHeading(RectTransform parent, string text, float y,
        float reserveRight = 0f)
    {
        // 標題用奶油字、金色留給底下那條線：字和線都是金的時候，兩個都不是重點。
        TextMeshProUGUI heading = AddText(parent, "H_" + text, text, TextLabel, InkCream,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, y), new Vector2(RailWidth - 48f, 32f));
        heading.characterSpacing = Tracking;

        // 標題底下一條淡金的細線：右欄一路往下都是方塊，沒有分隔就會糊成一片。
        RectTransform rule = AddRect("Rule_" + text, parent);
        // 右邊要讓出「預覽」鈕的位置：線畫到鈕底下就只是被蓋掉，看起來像斷了。
        Place(rule, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(24f, y - 20f), new Vector2(RailWidth - 48f - reserveRight, 1f));
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
        SelectRemembered();
    }

    /// <summary>
    /// 回到剛剛選著的那一首（連同難度）。找不到就退回第一首。
    /// </summary>
    /// <remarks>
    /// 找不到是正常的：篩選條件可能被改過、那首歌可能被刪掉、或者根本是第一次進來。
    /// 退回第一首而不是空著 —— 中欄沒有曲子的時候整排資訊都是破折號。
    /// </remarks>
    private void SelectRemembered()
    {
        if (filtered.Count == 0) return;
        SongSelectionManager.SongOption wanted = null;
        if (remembered.Valid && !string.IsNullOrEmpty(remembered.Song))
        {
            for (int i = 0; i < filtered.Count; i++)
            {
                if (!string.Equals(SongKey(filtered[i]), remembered.Song,
                        System.StringComparison.Ordinal)) continue;
                wanted = filtered[i];
                break;
            }
        }
        SelectSong(wanted ?? filtered[0]);

        // 難度也要回到原來那一份：同一首歌的普通和困難是兩份完全不同的譜，而中欄的
        // 成績、分析、走勢全都跟著難度走。
        if (wanted == null || string.IsNullOrEmpty(remembered.Variant)) return;
        for (int i = 0; i < difficultyVariantsShown.Count; i++)
        {
            if (!string.Equals(difficultyVariantsShown[i].chartFileName, remembered.Variant,
                    System.StringComparison.OrdinalIgnoreCase)) continue;
            SelectVariant(difficultyVariantsShown[i], false);
            break;
        }
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
        // 開著 = 深木底＋奶油字。亮金留給「開始練習」一個人用。
        Image plate = tutorialButton.targetGraphic as Image;
        if (plate != null) SetPlate(plate, showTutorials ? Toggled : Control);
        TextMeshProUGUI label = tutorialButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = showTutorials ? InkCream : Ink;
    }

    private void StepSort()
    {
        sortMode = sortMode == SortMode.Name ? SortMode.Level
            : sortMode == SortMode.Level ? SortMode.Author : SortMode.Name;
        RefreshList();
    }

    private void SetTier(SongDifficultyStrip.Tier tier)
    {
        if (tierFilter != tier) UiSfx.Select();
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
            Color hue = SongDifficultyStrip.ColourOf(tiers[i]);
            Image plate = button.targetGraphic as Image;
            if (plate != null)
            {
                // 這一排坐在深色檯面上，所以「翻到底」的方向和難度卡相反：選中的
                // 變**亮**（淡面＋墨字），沒選中的退進檯面但留著自己的色相。
                SetPlate(plate, active ? HueFace(hue) : Color.Lerp(FeltLight, hue, 0.30f));
            }
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = active ? Ink : InkCream;
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
        // 分類名是使用者自己取的，查不到就原樣印；只有「全部」這個內建的會被翻到。
        SettingsUiKit.SetText(categoryLabel, Localize.S(
            categories.Count > 0 ? categories[Mathf.Clamp(categoryIndex, 0, categories.Count - 1)]
                                 : AllCategories));
        // SetText 會照字串的語言換字型，而換字型會把投影用的材質丟掉。
        AddSoftShadow(categoryLabel);
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
                string name = OneLine(song.displayName).ToLowerInvariant();
                string author = OneLine(song.author).ToLowerInvariant();
                if (!name.Contains(query) && !author.Contains(query)) continue;
            }
            filtered.Add(song);
        }

        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            Destroy(listContent.GetChild(i).gameObject);
        }
        rowPlates.Clear();
        rowEdges.Clear();
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

        SettingsUiKit.SetText(sortLabel, sortMode == SortMode.Name ? Localize.S("曲名")
            : sortMode == SortMode.Level ? Localize.S("等級") : Localize.S("作曲"));

        foreach (var song in filtered) AddSongRow(song);
        HighlightSelectedRow();
        SettingsUiKit.SetText(listCount, Localize.SF("{0} 首", filtered.Count));
        if (listEmptyLabel != null)
        {
            // 空白的清單讀起來像還沒載入完。講清楚是「找不到」還是「這一階沒有」。
            bool searching = searchField != null && !string.IsNullOrWhiteSpace(searchField.text);
            listEmptyLabel.gameObject.SetActive(filtered.Count == 0);
            SettingsUiKit.SetText(listEmptyLabel, searching
                ? Localize.S("找不到符合的曲目")
                : showTutorials ? Localize.S("沒有新手教學曲目") : Localize.S("這一階沒有曲目"));
        }
    }

    /// <summary>把字串裡真正的換行拉直成一行。</summary>
    /// <remarks>
    /// 曲庫裡不少曲名是「中文名＋換行＋英文名」，例如「遠航星的告別」底下接著
    /// 「Voyaging Star's Farewell」。TMP 的 NoWrap 只擋**自動**折行，擋不住字串
    /// 自己帶的換行字元 —— 兩行照樣排下去，而裁切框只有一行高，看起來就是上下
    /// 各半截。
    ///
    /// 清單和標題一律拉成一行，兩段之間用間隔點接起來；長出來的部分本來就交給
    /// 跑馬燈。
    /// </remarks>
    private static string OneLine(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;

        string[] parts = value.Split(new[] { '\r', '\n' },
            System.StringSplitOptions.RemoveEmptyEntries);
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0) continue;
            if (builder.Length > 0) builder.Append("　·　");
            builder.Append(part);
        }
        return builder.Length > 0 ? builder.ToString() : value.Trim();
    }

    private static int CompareName(SongSelectionManager.SongOption x,
        SongSelectionManager.SongOption y)
    {
        return string.Compare(x?.displayName ?? string.Empty, y?.displayName ?? string.Empty,
            System.StringComparison.CurrentCultureIgnoreCase);
    }

    // ── 一列的版面 ────────────────────────────────────────────────────
    //
    // 從 72 高換成 112：同一塊面板裡從約十三首變成八首。**少幾格換更多資訊**是刻意
    // 的交換 —— 挑歌不是逐首讀過去，是掃到認得的那一張封面就停下來，而封面比文字
    // 快得多。
    //
    //   4 │脊 10│ 22 ┌ 縮圖 84² ┐ 122 │ 曲名（一行，偏上）
    //                            │ 作者
    //                            │ Lv.15   月桂   名次勳章
    private const float RowHeight = 112f;
    private const float RowThumb = 84f;
    private const float RowTextX = 122f;

    private void AddSongRow(SongSelectionManager.SongOption song)
    {
        RectTransform row = AddRect("Row", listContent);
        row.sizeDelta = new Vector2(0f, RowHeight);
        var element = row.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = RowHeight;

        Image plate = row.gameObject.AddComponent<Image>();
        // 列是實色的紙，不是半透明疊在檯面上 —— 半透明的那一版，每一列的底色都
        // 被底下的布紋改了一點點，一路滾下來就像紙在呼吸。
        PaperFace(plate, PaperRow);

        int tierLevel = LevelForTier(song);
        Color tierInk = tierLevel > 0
            ? TierInk(SongDifficultyStrip.ColourOf(tierFilter))
            : InkMuted;

        // 色脊。和等級的數字同一種墨 —— 脊比數字亮的話主次會顛倒，要讀的是數字，
        // 脊只是掃清單時的對齊線。
        RectTransform spine = AddRect("Spine", row);
        Place(spine, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(4f, 0f), new Vector2(10f, RowHeight - 20f));
        Image spineImage = spine.gameObject.AddComponent<Image>();
        spineImage.color = tierLevel > 0 ? tierInk : new Color(0.640f, 0.575f, 0.470f, 1f);
        spineImage.raycastTarget = false;

        // 縮圖。封面比曲名快 —— 掃清單的時候認出來的是那張圖，不是那行字。
        //
        // 載入是延遲的：`coverSprite` 在曲庫建立時不一定已經有了，練習室別的地方也
        // 是這樣補的（見 ShowCover）。
        RectTransform thumbRect = AddRect("Thumb", row);
        Place(thumbRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(22f, 0f), new Vector2(RowThumb, RowThumb));
        Image thumb = thumbRect.gameObject.AddComponent<Image>();
        thumb.sprite = ResolveCover(song);
        thumb.preserveAspect = true;
        thumb.color = thumb.sprite != null ? Color.white : new Color(0.62f, 0.55f, 0.45f, 1f);
        thumb.raycastTarget = false;
        // 封面是別人的畫，不是這間房的家具：給它一圈內縮的暗線當卡紙，別讓它直接
        // 貼在紙上。
        AddEdgeLight(thumbRect, true, 0f, 1.25f, 4f).SetSides(new[]
        {
            new Color(0.10f, 0.06f, 0.03f, 0.55f), new Color(0.10f, 0.06f, 0.03f, 0.45f),
            new Color(0.30f, 0.24f, 0.16f, 0.30f), new Color(1.00f, 0.96f, 0.88f, 0.28f)
        });

        // 曲名放在一個會裁切的框裡，太長就自己跑 —— 和遊戲中的曲名一樣。
        float textWidth = ListWidth - RowTextX - 20f;
        RectTransform titleClip = AddRect("TitleClip", row);
        Place(titleClip, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(RowTextX, -12f), new Vector2(textWidth, 36f));
        titleClip.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI title = AddText(titleClip, "Title", OneLine(song.displayName), TextTitle, Ink,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(textWidth, 34f));
        ClassicalBookUITheme.ApplyContentFont(title);
        title.gameObject.AddComponent<MarqueeText>().Bind(title, textWidth);

        TextMeshProUGUI author = AddText(row, "Author", OneLine(song.author), TextBody, InkMuted,
            TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
        Place(author.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(RowTextX, -50f), new Vector2(textWidth, 26f));
        ClassicalBookUITheme.ApplyContentFont(author);

        // ── 最下面一排：等級、全連、名次 ────────────────────────────
        //
        // 難度色**染在等級上**，不是墊一張底票在它後面。數字才是逐列的事實，顏色在
        // 講「這個數字是哪一把尺上的」—— 兩件事要是同一個東西。可以染是因為壓暗過
        // （見 TierInk）：原始那組難度色是為深背景挑的，黃色的 HARD 打在紙上只有
        // 1.1:1。
        TextMeshProUGUI level = AddText(row, "Level",
            tierLevel > 0 ? "Lv." + tierLevel : "–", TextLabel, tierInk,
            TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Place(level.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(RowTextX, -78f), new Vector2(90f, 28f));

        float badgeX = RowTextX + 88f;

        // 全連：一圈金月桂。做到過就算 —— 不是只看最高分那一局，因為全連是一件做
        // 到過就成立的事，而分數是比大小的；綁在同一筆會讓玩家刷分之後失去記號。
        bool fullCombo = false;
        try { fullCombo = LocalScoreRecords.EverFullCombo(song, true); } catch { }
        if (fullCombo)
        {
            RectTransform wreath = AddRect("FullCombo", row);
            wreath.gameObject.AddComponent<CanvasRenderer>();
            // 40 不是 32：三片大葉在 32px 下還是會糊，40 才讀得出是月桂。
            Place(wreath, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(badgeX, -70f), new Vector2(40f, 40f));
            var laurel = wreath.gameObject.AddComponent<LaurelGraphic>();
            laurel.color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
            laurel.raycastTarget = false;
            badgeX += 48f;
        }

        // 名次勳章：結算頁用的那七張。沒打過就不畫 —— 一個空的位置比一個灰掉的
        // 勳章誠實。
        LocalScoreEntry best = null;
        try { best = LocalScoreRecords.GetBest(song, true); } catch { }
        if (best != null && !string.IsNullOrEmpty(best.rank))
        {
            Texture2D badge = LocalScoreRecords.LoadRankTexture(best.rank);
            if (badge != null)
            {
                RectTransform rankRect = AddRect("Rank", row);
                Place(rankRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(badgeX, -74f), new Vector2(28f, 34f));
                var rank = rankRect.gameObject.AddComponent<RawImage>();
                rank.texture = badge;
                rank.raycastTarget = false;
            }
        }

        // 選中的那一列另外描一圈金邊。
        //
        // 只靠底色分不出來：紙和紙之間的差距本來就小，而「滑過去」的那一格亮度又
        // 幾乎把它吃光 —— 剛點完的那一列和隨便滑過的另一列看起來是一樣的。
        // 這一圈是**狀態**不是光照，所以四邊一樣亮。關著的時候整個停用：透明的
        // 網格 uGUI 照樣會生、會送、會在每次重畫時重建，58 列就是 58 份白工。
        EdgeLightGraphic edge = AddEdgeLight(row, true, 0f, 1.5f, CornerRadius);
        Color gold = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
        edge.SetSides(new[] { gold, gold, gold, gold });
        edge.enabled = false;

        var button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        SilenceNavigation(button);
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1f / RestDim, 1f / RestDim, 1f / RestDim, 1f);
        colours.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
        // selectedColor 不設就是 Unity 的預設 0.96，而「選中」的優先權比「滑過」
        // 高 —— 剛點完的那一顆會被壓成 0.96，永遠亮不起來。
        colours.selectedColor = Color.white;
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        SongSelectionManager.SongOption captured = song;
        button.onClick.AddListener(() =>
        {
            UiSfx.Move();
            SelectSong(captured);
        });
        rowPlates.Add(plate);
        rowEdges.Add(edge);
        rowSongs.Add(song);
    }

    /// <summary>選到的那一列要看得出來 —— 不然使用者不知道自己點到了沒有。</summary>
    private void HighlightSelectedRow()
    {
        for (int i = 0; i < rowPlates.Count && i < rowSongs.Count; i++)
        {
            if (rowPlates[i] == null) continue;
            bool active = ReferenceEquals(rowSongs[i], selectedSong);
            SetPlate(rowPlates[i], active ? PaperSel : PaperRow);
            if (i < rowEdges.Count && rowEdges[i] != null) rowEdges[i].enabled = active;
        }
    }

    private void SelectSong(SongSelectionManager.SongOption song)
    {
        selectedSong = song;
        HighlightSelectedRow();
        PlayPreview(song);
        SettingsUiKit.SetText(standTitle, song != null ? OneLine(song.displayName) : string.Empty);
        // 換了曲名要重量一次，否則跑馬燈還照上一首的長度跑。
        if (standTitleMarquee != null) standTitleMarquee.Bind(standTitle, CentreWidth);
        SettingsUiKit.SetText(standAuthor, song != null ? OneLine(song.author) : string.Empty);
        // 字距要重算：AddText 是在建立當下算的，而這兩個標籤是用空字串建的，不
        // 補這一行的話，全大寫的拉丁曲名在清單列有字距、在中間這一塊沒有。
        if (standTitle != null) standTitle.characterSpacing = TrackingFor(standTitle.text);
        if (standAuthor != null) standAuthor.characterSpacing = TrackingFor(standAuthor.text);
        if (standTitle != null) ClassicalBookUITheme.ApplyContentFont(standTitle);
        if (standAuthor != null) ClassicalBookUITheme.ApplyContentFont(standAuthor);
        // 換字型會換掉材質，所以投影要在這之後再設一次 —— 不然每換一首歌就掉一次。
        AddSoftShadow(standTitle);
        AddSoftShadow(standAuthor);

        SwapCover(ResolveCover(song));

        BuildDifficultyButtons();
    }

    /// <summary>這一首的封面。曲庫建立時不一定已經載好，所以這裡補一次。</summary>
    /// <remarks>
    /// `LoadCoverSprite` 自己有快取，所以 58 列各叫一次不會讀 58 次檔。
    /// </remarks>
    private static Sprite ResolveCover(SongSelectionManager.SongOption song)
    {
        if (song == null) return null;
        if (song.coverSprite != null) return song.coverSprite;
        if (string.IsNullOrEmpty(song.coverResourcePath)) return null;
        if (SongSelectionManager.Instance == null) return null;
        return SongSelectionManager.Instance.LoadCoverSprite(song.coverResourcePath, song.displayName);
    }

    /// <summary>換一張畫：先退掉再換，不要用跳的。</summary>
    /// <remarks>
    /// 框的尺寸會跟著曲繪的比例變，所以不能做交叉淡入 —— 兩張不同比例的圖疊在一
    /// 起淡，看起來是錯位不是換片。退到看不見再換、再進來，讀起來才是「換了一張」。
    /// </remarks>
    private void SwapCover(Sprite cover)
    {
        if (coverImage == null) return;
        // 同一張就什麼都不要做。清單列的點擊沒有「已經選中」的護欄，而使用者確認
        // 用的重複點擊很常見 —— 每點一次讓曲繪消失再回來，畫面上卻什麼都沒變。
        if (coverRoutine == null && coverImage.sprite == cover) return;

        bool first = !isActiveAndEnabled || coverImage.sprite == null;
        if (coverRoutine != null) StopCoroutine(coverRoutine);
        if (first)
        {
            // 第一次不要演：開場前 100ms 演一個空框的淡出，那是在演沒有東西。
            coverImage.sprite = cover;
            coverImage.color = cover != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);
            FitFrameToCover(cover);
            coverRoutine = null;
            return;
        }
        coverRoutine = StartCoroutine(SwapCoverRoutine(cover));
    }

    /// <summary>退掉、換片、進來；框的尺寸整段一起補。</summary>
    /// <remarks>
    /// 框會跟著曲繪的比例改尺寸，所以只淡曲繪的話，換到寬幅曲繪的那一幀金框會從
    /// 300 寬爆開到 620 —— 補間的是畫，跳的是框。兩個一起走才看得出是「換了一張」。
    ///
    /// 淡出的長度跟著目前的亮度縮：連點的時候曲繪本來就已經暗了，再花滿 100ms 去
    /// 淡一個看不見的東西，會讓 sprite 那一行永遠輪不到，整段瀏覽期間框裡都是黑的。
    /// </remarks>
    private System.Collections.IEnumerator SwapCoverRoutine(Sprite cover)
    {
        float from = coverImage.color.a;
        float outSpan = 0.10f * Mathf.Clamp01(from);
        float elapsed = 0f;
        while (elapsed < outSpan)
        {
            elapsed += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, 0f, EaseOut(elapsed / outSpan));
            coverImage.color = new Color(1f, 1f, 1f, a);
            yield return null;
        }
        coverImage.color = new Color(1f, 1f, 1f, 0f);

        coverImage.sprite = cover;
        Vector2 sizeFrom = frameHolder != null ? frameHolder.sizeDelta : Vector2.zero;
        float insetFrom = coverImage.rectTransform.offsetMin.x;
        ComputeFrame(cover, out Vector2 sizeTo, out float insetTo);

        float to = cover != null ? 1f : 0.12f;
        elapsed = 0f;
        const float inSpan = 0.22f;
        while (elapsed < inSpan)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = EaseOut(elapsed / inSpan);
            ApplyFrame(Vector2.Lerp(sizeFrom, sizeTo, t), Mathf.Lerp(insetFrom, insetTo, t));
            coverImage.color = new Color(1f, 1f, 1f, Mathf.Lerp(0f, to, t));
            yield return null;
        }
        ApplyFrame(sizeTo, insetTo);
        coverImage.color = new Color(1f, 1f, 1f, to);
        coverRoutine = null;
    }

    /// <summary>把畫框縮到剛好包住這張曲繪。</summary>
    /// <remarks>
    /// 框是固定的正方形、圖用 preserveAspect 縮進去的話，非正方的曲繪上下（或左
    /// 右）就會留出兩條空的卡紙 —— 看起來像裱歪了。真的裱框是照畫的尺寸裁的，所以
    /// 這裡反過來：先照曲繪的比例算出畫要多大，再把框撐成「畫 + 兩圈留白」。
    ///
    /// 框從下緣往上長（pivot 在底邊），所以不管框多高，底下曲名的位置都不會跳。
    /// </remarks>
    private void FitFrameToCover(Sprite cover)
    {
        if (frameHolder == null) return;

        // 高度和寬度各有各的上限。
        //
        // 只用一個上限的話，橫幅的曲繪會因為「長邊不能超過這麼大」而縮得很小，上
        // 面空出一大片牆。橫幅可以往兩側長到和底下的卡片切齊，高度才是真正受限的
        // 那一邊 —— 因為底下還有曲名、難度、成績、走勢要排。
        float maxHeight = FrameMax * (1f - 2f * MouldingRatio) - 2f * MatMargin;
        float maxWidth = CentreWidth - 60f;        // 留給角飾往外凸的那 8px
        float width = maxHeight;
        float height = maxHeight;
        if (cover != null && cover.rect.height > 0.01f)
        {
            float aspect = cover.rect.width / cover.rect.height;
            width = Mathf.Min(maxWidth, maxHeight * aspect);
            height = width / Mathf.Max(0.0001f, aspect);
        }

        // 框的短邊要反推：線腳深度是「**框**短邊」的百分比，而框短邊又等於畫的短
        // 邊加兩倍內縮 —— 兩邊互相依賴，所以解一次，不能直接乘。
        float shortSide = Mathf.Min(width, height);
        float frameShort = (shortSide + 2f * MatMargin) / (1f - 2f * MouldingRatio);
        float inset = Mathf.Max(MatMargin, (frameShort - shortSide) * 0.5f);
        ApplyFrame(new Vector2(width + inset * 2f, height + inset * 2f), inset);
    }

    /// <summary>算出這張曲繪該有的框尺寸和卡紙留白，但先不要套上去。</summary>
    private void ComputeFrame(Sprite cover, out Vector2 size, out float inset)
    {
        float maxHeight = FrameMax * (1f - 2f * MouldingRatio) - 2f * MatMargin;
        float maxWidth = CentreWidth - 60f;
        float width = maxHeight;
        float height = maxHeight;
        if (cover != null && cover.rect.height > 0.01f)
        {
            float aspect = cover.rect.width / cover.rect.height;
            width = Mathf.Min(maxWidth, maxHeight * aspect);
            height = width / Mathf.Max(0.0001f, aspect);
        }
        float shortSide = Mathf.Min(width, height);
        float frameShort = (shortSide + 2f * MatMargin) / (1f - 2f * MouldingRatio);
        inset = Mathf.Max(MatMargin, (frameShort - shortSide) * 0.5f);
        size = new Vector2(width + inset * 2f, height + inset * 2f);
    }

    private void ApplyFrame(Vector2 size, float inset)
    {
        if (frameHolder == null) return;
        frameHolder.sizeDelta = size;
        // 畫的內縮跟著框走，線腳才不會被畫蓋掉。
        if (coverImage != null) Stretch(coverImage.rectTransform, inset, inset, inset, inset);
    }

    private void BuildDifficultyButtons()
    {
        difficultyButtons.Clear();
        difficultyVariantsShown.Clear();
        difficultyNames.Clear();
        difficultyLevels.Clear();
        difficultyTints.Clear();
        difficultyFills.Clear();
        for (int i = difficultyRow.childCount - 1; i >= 0; i--)
        {
            Transform child = difficultyRow.GetChild(i);
            // **只刪自己生的按鈕。**
            //
            // 卡片的裝飾（影子、紙紋、邊光、內線）現在都是子物件，不是掛在卡片上
            // 的元件 —— 用「清空全部子物件」的寫法，這張卡第一次選曲就會被剝光，
            // 變成上下兩張有影子、中間這張沒有。白名單會再壞一次，所以改成白名單
            // 的反面：只認自己生的那個名字。
            if (!child.name.StartsWith("Diff")) continue;
            Destroy(child.gameObject);
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
        //
        // 只有一個例外：六份譜的曲子（6×104 + 5×8 = 664）會撐破 640 的卡片，這時
        // 候才一起收窄。寧可六顆一起變窄，也不要讓最外面那兩顆掉到卡片外面。
        const float gap = 8f;
        float room = CentreWidth - 24f;
        float buttonWidth = Mathf.Min(104f,
            (room - (variants.Count - 1) * gap) / Mathf.Max(1, variants.Count));
        float rowWidth = variants.Count * buttonWidth + (variants.Count - 1) * gap;
        float left = -rowWidth * 0.5f;
        float width = buttonWidth;
        for (int i = 0; i < variants.Count; i++)
        {
            SongSelectionManager.SongOption variant = variants[i];
            if (variant == null) continue;
            Button button = MakeButton(difficultyRow, "Diff" + i, string.Empty, TextLabel,
                new Vector2(width, 72f));
            RectTransform rect = button.GetComponent<RectTransform>();
            Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(left + i * (buttonWidth + gap), 0f), new Vector2(width, 72f));

            // 難度的顏色和遊戲其他地方同一組，不另外發明 —— 而且要和**同一個畫面
            // 上**的階層鈕、清單色脊用同一個分類器。
            //
            // DifficultyVisualPalette.For 是完全相等比對（只認得 Normal/Hard/
            // Expert），SongDifficultyStrip.Classify 是包含比對、還認得中文。同一
            // 份叫「困難」的譜，階層鈕給黃色、難度卡卻掉進紫色那一格 —— 兩個顏色
            // 同時在畫面上，講的是同一件事。
            Color tint = SongDifficultyStrip.ColourOf(
                SongDifficultyStrip.TierOf(variant.difficultyName));
            difficultyTints.Add(tint);

            // 頂緣那一條色帶是**身分**：選中沒選中都在，所以一排掃過去永遠知道哪
            // 顆是 EXPERT、哪顆是 MASTER。它是子物件不是 targetGraphic，所以滑過
            // 去的變色不會把它一起洗淡。
            RectTransform bar = AddRect("Bar", rect);
            Place(bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -3f), new Vector2(width - 18f, 6f));
            Image barImage = bar.gameObject.AddComponent<Image>();
            // 色帶也要壓暗一格，否則長滿之後它比卡面亮 8%，四周會留一圈邊；而滑
            // 鼠一移上去卡面被乘回原色、那圈邊又不見了。
            Color barTone = HueFill(tint);
            barImage.color = new Color(barTone.r * RestDim, barTone.g * RestDim, barTone.b * RestDim, 1f);
            barImage.raycastTarget = false;

            // 邊光要留在最上面：色帶長滿整張卡的時候會把它蓋掉。
            EdgeLightGraphic ring = button.GetComponentInChildren<EdgeLightGraphic>();
            if (ring != null) ring.transform.SetAsLastSibling();

            TextMeshProUGUI name = AddText(rect, "Name", ShortDifficultyName(variant), TextCaption,
                InkMuted, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(width, 20f));
            // 這一格只有一百像素，大寫字距會把 MALICIOUS 頂到隔壁去。
            name.characterSpacing = 0f;
            difficultyNames.Add(name);

            TextMeshProUGUI level = AddText(rect, "Level",
                variant.difficultyLevel > 0 ? variant.difficultyLevel.ToString() : "?", TextDisplay, Ink,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Place(level.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(width - 6f, 40f));
            difficultyLevels.Add(level);

            var fill = button.gameObject.AddComponent<DifficultyCardFill>();
            fill.Bind(bar, button.targetGraphic as Image, ring, name, level,
                HueFace(tint), HueFill(tint), RestDim, new Vector2(width, 72f),
                Ink, InkMuted, InkCream, EdgePaper, EdgeSelected);
            difficultyFills.Add(fill);

            SongSelectionManager.SongOption captured = variant;
            button.onClick.AddListener(() =>
            {
                UiSfx.Select();
                SelectVariant(captured);
            });
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
        // 重建之後的第一次不要演：換一首歌不該讓五張卡一起長出來。
        SelectVariant(initial, false);
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

        // 後綴要留住，不能直接砍。
        //
        // 曲庫裡真的有 Master 和 Master_piano、Master 和 Master_HISPEED 排在一起
        // 的曲子。固定長度硬切會切成「MASTER」和「MASTER_H」，一排裡兩顆幾乎一樣
        // 的字；改成主名加一個記號（MASTER＋P、MASTER＋H），才看得出差在哪。
        int cut = name.IndexOfAny(new[] { '_', ' ', '-', '(', '+' });
        if (cut > 0 && cut < name.Length - 1)
        {
            string head = name.Substring(0, cut).Trim();
            string tail = name.Substring(cut + 1).Trim();
            if (head.Length > 6) head = head.Substring(0, 6);
            if (tail.Length > 0)
            {
                return head.ToUpperInvariant() + "+" + char.ToUpperInvariant(tail[0]);
            }
            name = head;
        }
        // 六個字就截斷會把 MALICIOUS 變成 MALICI —— 看起來像壞掉，不像縮寫。按鈕
        // 一百零四像素、字十四級，放得下八個字母。
        return name.Length <= 8 ? name.ToUpperInvariant() : name.Substring(0, 8).ToUpperInvariant();
    }

    private void SelectVariant(SongSelectionManager.SongOption variant, bool animate = true)
    {
        selectedVariant = variant;
        RefreshAnalysis();
        // 選中的那一個描金框，其餘退回去。選擇狀態要看得出來，不能只靠成績變了。
        for (int i = 0; i < difficultyButtons.Count && i < difficultyVariantsShown.Count; i++)
        {
            Button button = difficultyButtons[i];
            if (button == null) continue;
            bool active = ReferenceEquals(difficultyVariantsShown[i], variant);
            // 未選中不是「沒有顏色」，是**淡**的那一版：身分留著，狀態靠深淺講。
            // 卡面、色帶、字色、邊光全部交給 DifficultyCardFill 用同一條曲線補。
            if (i < difficultyFills.Count && difficultyFills[i] != null)
            {
                difficultyFills[i].SetSelected(active, animate);
            }
        }
        RefreshScore();
        RefreshSpeed();
        RefreshHint();
    }

    /// <summary>
    /// 把評分讀回中上那塊銘牌。
    /// </summary>
    /// <remarks>
    /// **不每幀看，因為不需要。** 評分只有結算那一刻會變，而這個房間一離開就整個銷
    /// 毀（見 <see cref="StartPracticeRoutine"/>：`Destroy(gameObject)` 之後才交棒
    /// 給開場流程），回來的時候是重新蓋的一份。所以「開場讀一次」＋「從設定頁回來
    /// 讀一次」就覆蓋得完 —— 設定頁是唯一一條**不重建**就回到這個畫面的路。
    ///
    /// 讀取本身也不貴：<c>PlayerRating</c> 把整份檔案快取在靜態欄位裡，第一次之後
    /// 都是記憶體。
    /// </remarks>
    private void RefreshRating()
    {
        if (ratingValue == null) return;

        float rating = 0f;
        float ceiling = 6000f;
        float[] parts = null;
        string best = null;
        PlayerRating.Attempt last = null;
        // 評分是讀檔案來的。壞掉的檔案 PlayerRating 自己吞了，但 persistentDataPath
        // 在某些平台上會丟 —— 整間房不該因為一塊銘牌開不起來。
        try
        {
            rating = PlayerRating.Current(false);
            ceiling = Mathf.Max(1f, PlayerRating.Ceiling(false));
            parts = PlayerRating.Criteria(false);
            best = PlayerRating.BestSong(false);
            last = PlayerRating.LastAttempt;
        }
        catch { }

        bool any = rating > 0.5f;
        if (ratingScale != null) ratingScale.gameObject.SetActive(any);
        if (ratingEmpty != null) ratingEmpty.gameObject.SetActive(!any);

        // 0 分的時候不要印「0」：那是一個成績，而這裡的意思是「還沒有」。
        SettingsUiKit.SetText(ratingValue, any ? Mathf.RoundToInt(rating).ToString() : "—");
        SettingsUiKit.SetText(ratingTitle, PlayerRating.Title(rating, false));
        SettingsUiKit.SetText(ratingBest, any && !string.IsNullOrEmpty(best)
            ? Localize.SF("目前由《{0}》撐起", ClipName(best, 14)) : string.Empty);
        SettingsUiKit.SetText(ratingNote, DescribeAttempt(last));
        // 低於目前實力的那一局不會把評分拉下來，但也不能當作沒發生。那一行要讀得清
        // 楚，所以用全墨色，不是註解的淡墨 —— 「怎麼打完數字沒動」就是在這裡回答的。
        if (ratingNote != null)
        {
            ratingNote.color = last != null && last.gap > 0.5f ? Ink : InkMuted;
        }

        var targets = new float[4];
        for (int i = 0; i < 4; i++)
        {
            float part = parts != null && i < parts.Length ? parts[i] : 0f;
            SettingsUiKit.SetText(criterionValues[i], any ? Mathf.RoundToInt(part).ToString() : "—");
            // 四項和大條同一個刻度（都是對滿分），長短才能直接互相比。
            targets[i] = any ? Mathf.Clamp01(part / ceiling) : 0f;
        }
        SweepGauges(any ? Mathf.Clamp01(rating / ceiling) : 0f, targets);
    }

    /// <summary>上次那一局怎麼換算的。低於目前實力就照實說差多少。</summary>
    /// <remarks>
    /// 「只加不減」如果只寫在設計文件裡，玩家看到的就是打完一場**數字沒動** —— 那
    /// 看起來像壞掉。差多少寫出來，它才變成一個追得到的目標。
    ///
    /// 要標明是哪一段刻度：演奏會那一局換算出來的數字比練習高一千，混在一起看就沒
    /// 有意義了。
    /// </remarks>
    private static string DescribeAttempt(PlayerRating.Attempt last)
    {
        if (last == null) return string.Empty;
        string band = last.recital ? Localize.S("演奏會") : Localize.S("練習");
        string head = Localize.SF("上次{0}《{1}》Lv{2} 換算 {3}", band,
            ClipName(last.song, 12), last.level, Mathf.RoundToInt(last.value));
        return last.gap > 0.5f
            ? head + Localize.SF("，比目前實力低 {0}，沒有計入", Mathf.RoundToInt(last.gap))
            : head + Localize.S("，已經計入");
    }

    /// <summary>太長的曲名切短：銘牌上是一行字，滿出去會壓到旁邊那一句。</summary>
    private static string ClipName(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string one = OneLine(value).Trim();
        return one.Length <= max ? one : one.Substring(0, Mathf.Max(1, max - 1)) + "…";
    }

    /// <summary>條子長出來。</summary>
    /// <remarks>
    /// 直接跳到長度的話，看到的是一張靜態圖表；長出來才看得出這是**累積**的。四條
    /// 小的錯開一點跟著跑，眼睛會一條一條看過去，而不是整片一起動。
    ///
    /// 值沒變就不演：從設定頁回來也會重讀一次，那時候重播一遍是雜訊。
    /// </remarks>
    private void SweepGauges(float main, float[] parts)
    {
        if (ratingGauge == null || parts == null) return;
        if (ratingSweep != null)
        {
            StopCoroutine(ratingSweep);
            ratingSweep = null;
        }

        bool changed = Mathf.Abs(ratingGauge.Value01 - main) > 0.002f;
        for (int i = 0; i < 4 && !changed; i++)
        {
            if (criterionGauges[i] == null) continue;
            if (Mathf.Abs(criterionGauges[i].Value01 - parts[i]) > 0.002f) changed = true;
        }
        if (!changed) return;

        // 還沒啟用（或正在關）就別開協程：那條協程永遠不會跑完，條子會停在半路。
        if (!isActiveAndEnabled || closing)
        {
            ApplyGauges(main, parts);
            return;
        }
        ratingSweep = StartCoroutine(SweepGaugesRoutine(main, parts));
    }

    private System.Collections.IEnumerator SweepGaugesRoutine(float main, float[] parts)
    {
        const float span = 0.55f;
        const float stagger = 0.05f;
        float total = span + stagger * 4f;
        float elapsed = 0f;
        // 從**現在畫出來的長度**起跑，不是從 0 —— 補間跑到一半又被重讀一次的時候，
        // 歸零會讓條子先縮回去再長出來（難度卡那邊早就吃過這個教訓）。
        float mainFrom = ratingGauge.Value01;
        var from = new float[4];
        for (int i = 0; i < 4; i++)
        {
            from[i] = criterionGauges[i] != null ? criterionGauges[i].Value01 : 0f;
        }
        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            ratingGauge.Value01 = Mathf.Lerp(mainFrom, main, EaseOut(elapsed / span));
            for (int i = 0; i < 4; i++)
            {
                if (criterionGauges[i] == null) continue;
                criterionGauges[i].Value01 = Mathf.Lerp(from[i], parts[i],
                    EaseOut((elapsed - stagger * (i + 1)) / span));
            }
            yield return null;
        }
        // 明確收尾：終值要寫進去，不是「跑久了自然會到」。
        ApplyGauges(main, parts);
        ratingSweep = null;
    }

    private void ApplyGauges(float main, float[] parts)
    {
        if (ratingGauge != null) ratingGauge.Value01 = main;
        for (int i = 0; i < 4; i++)
        {
            if (criterionGauges[i] != null) criterionGauges[i].Value01 = parts[i];
        }
    }

    private void RefreshScore()
    {
        if (selectedVariant == null)
        {
            SettingsUiKit.SetText(scoreValue, Localize.S("無紀錄"));
            ShowRank(null);
            return;
        }
        int best = 0;
        try { best = LocalScoreRecords.GetBestScore(selectedVariant, true); } catch { }
        if (best <= 0)
        {
            SettingsUiKit.SetText(scoreValue, Localize.S("無紀錄"));
            ShowRank(null);
            return;
        }
        SettingsUiKit.SetText(scoreValue, best.ToString("N0"));
        ShowRank(LocalScoreRecords.GetRank(best));
    }

    /// <summary>等第：有圖就用圖，沒有才退回文字。</summary>
    private void ShowRank(string rank)
    {
        Texture2D art = null;
        if (!string.IsNullOrEmpty(rank))
        {
            try { art = LocalScoreRecords.LoadRankTexture(rank); } catch { }
        }
        if (scoreRankArt != null)
        {
            scoreRankArt.texture = art;
            scoreRankArt.enabled = art != null;
            if (art != null)
            {
                // 照圖自己的長寬比放進 64×52 的格子裡，不要拉扁。
                float aspect = art.width / Mathf.Max(1f, (float)art.height);
                float h = Mathf.Min(52f, 64f / Mathf.Max(0.0001f, aspect));
                scoreRankArt.rectTransform.sizeDelta = new Vector2(h * aspect, h);
            }
        }
        SettingsUiKit.SetText(scoreRank, art != null ? string.Empty : rank ?? string.Empty);
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
    /// <summary>試聽：收掉上一首、等手停下來、再淡進新的。</summary>
    /// <remarks>
    /// 上一版是 `Stop()` 之後立刻載下一首 —— 在清單上連點五首就是五次爆音，而且
    /// 五次都真的去讀檔、解碼。兩件事一起修：
    ///
    /// **淡出淡入**，因為音訊的硬切就是「啪」的一聲：波形在非零的地方被截斷，那個
    /// 不連續點本身就是一個寬頻脈衝。
    ///
    /// **去抖**，因為瀏覽的時候使用者根本還沒決定要聽哪一首。指標停穩之前不要去
    /// 載 —— 手指掃過十首不該換來十次解碼。
    /// </remarks>
    private void PlayPreview(SongSelectionManager.SongOption song)
    {
        previewToken++;
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        previewRoutine = StartCoroutine(PreviewRoutine(song, previewToken));
    }

    private System.Collections.IEnumerator PreviewRoutine(
        SongSelectionManager.SongOption song, int token)
    {
        yield return FadePreview(0f, 0.12f);
        if (previewSource != null) previewSource.Stop();
        if (song == null)
        {
            previewRoutine = null;
            yield break;
        }

        // 淡出那 120ms 已經吃掉一部分等待；再多等一下，總共約 220ms 才動手。
        float wait = 0.10f;
        while (wait > 0f)
        {
            if (token != previewToken) { previewRoutine = null; yield break; }
            wait -= Time.unscaledDeltaTime;
            yield return null;
        }

        // 離開迴圈那一幀要再驗一次：迴圈的檢查在頂端，跨過 0 的那一幀不會再跑
        // 一次 —— 剛好在那一幀被叫停（例如開設定）的話，底下照樣會開始播，而且
        // 是 loop=true 的無限循環。
        if (token != previewToken)
        {
            previewRoutine = null;
            yield break;
        }

        SongSelectionManager.SongOption variant =
            song.difficultyVariants != null && song.difficultyVariants.Count > 0
                ? song.difficultyVariants[0] : song;
        AudioClip clip = song.audioClip ?? variant.audioClip;
        if (clip != null)
        {
            StartPreviewClip(clip);
            previewRoutine = null;
            yield break;
        }

        string path = !string.IsNullOrEmpty(song.audioResourcePath)
            ? song.audioResourcePath : variant.audioResourcePath;
        if (!string.IsNullOrEmpty(path)) yield return LoadPreviewRoutine(path, token);
        previewRoutine = null;
    }

    /// <summary>把試聽的音量推到 target。播放中才有意義。</summary>
    private System.Collections.IEnumerator FadePreview(float target, float span)
    {
        if (previewSource == null || !previewSource.isPlaying) yield break;
        float from = previewSource.volume;
        float elapsed = 0f;
        while (elapsed < span)
        {
            elapsed += Time.unscaledDeltaTime;
            // 音量用線性斜坡：耳朵聽起來比緩出自然，緩出的尾巴會拖成一條細細的
            // 殘響。
            previewSource.volume = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / span));
            yield return null;
        }
        previewSource.volume = target;
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
        float target = settings != null ? Mathf.Clamp01(settings.MusicVolume) * 0.8f : 0.6f;
        previewSource.volume = 0f;
        // 從三分之一處開始：曲子的開頭常常是空的，試聽要聽得到主題。
        previewSource.time = Mathf.Clamp(clip.length * 0.33f, 0f, Mathf.Max(0f, clip.length - 1f));
        previewSource.Play();
        StartCoroutine(FadePreview(target, 0.18f));
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

    /// <summary>緩出三次方。整個畫面的補間都用同一條，不要每個地方各自發明。</summary>
    private static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

    /// <summary>緩出並且過頭一點點：抽屜拉開要有「撞到底」的那一下。</summary>
    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float s = 1.10f;
        float k = t - 1f;
        return 1f + k * k * ((s + 1f) * k + s);
    }

    private System.Collections.IEnumerator RevealScreen()
    {
        var movers = new[] { listPanel, standRoot, railPanel };
        var offsets = new[] { new Vector2(-26f, 0f), new Vector2(0f, -18f), new Vector2(26f, 0f) };
        var start = new Vector2[movers.Length];
        for (int i = 0; i < movers.Length; i++)
        {
            if (movers[i] == null) continue;
            start[i] = movers[i].anchoredPosition;
            movers[i].anchoredPosition = start[i] + offsets[i];
        }
        Vector3 frameFrom = new Vector3(0.965f, 0.965f, 1f);
        if (frameHolderRect != null) frameHolderRect.localScale = frameFrom;
        if (rootGroup != null)
        {
            rootGroup.alpha = 0f;
            // 看不見的畫面不該吃點擊。CanvasGroup 的 blocksRaycasts 預設是 true，
            // 只把 alpha 設成 0 的話，從第一幀起就有一個透明的練習室在接所有滑鼠
            // —— 在選曲畫面連點兩下「練習室」，第二下會落在還沒淡出來的東西上。
            rootGroup.blocksRaycasts = false;
        }

        const float span = 0.34f;
        const float stagger = 0.06f;
        const float fade = 0.22f;
        float elapsed = 0f;
        float total = Mathf.Max(stagger * 2f, 0.10f) + span;
        bool handedOver = false;
        while (elapsed < total)
        {
            if (closing) yield break;           // 有人要走了，別再跟他搶 alpha
            elapsed += Time.unscaledDeltaTime;
            if (rootGroup != null) rootGroup.alpha = EaseOut(elapsed / fade);
            if (!handedOver && elapsed >= fade)
            {
                // 看得見了就把操作交還，不要等整段位移跑完 —— 那還有 240ms。
                handedOver = true;
                if (rootGroup != null) rootGroup.blocksRaycasts = true;
            }
            for (int i = 0; i < movers.Length; i++)
            {
                if (movers[i] == null) continue;
                float t = EaseOut((elapsed - stagger * i) / span);
                movers[i].anchoredPosition = start[i] + offsets[i] * (1f - t);
            }
            if (frameHolderRect != null)
            {
                // 畫框慢半拍才長回原寸：它是這一欄的主角，晚一點到才看得到它到。
                float t = EaseOut((elapsed - 0.10f) / span);
                frameHolderRect.localScale = Vector3.Lerp(frameFrom, Vector3.one, t);
            }
            yield return null;
        }

        if (rootGroup != null)
        {
            rootGroup.alpha = 1f;
            rootGroup.blocksRaycasts = true;
        }
        for (int i = 0; i < movers.Length; i++)
        {
            if (movers[i] != null) movers[i].anchoredPosition = start[i];
        }
        if (frameHolderRect != null) frameHolderRect.localScale = Vector3.one;
        revealRoutine = null;
    }

    private System.Collections.IEnumerator FadeOutAndDestroy()
    {
        // **還看得見就要繼續吃掉點擊。**
        //
        // Close() 已經先把選曲轉盤叫回來墊在底下了，這時候放掉 blocksRaycasts，接
        // 下來 200 毫秒使用者看到的是還很清楚的練習室，點下去卻是底下那張轉盤在
        // 接 —— 點在難度卡的位置會打到轉盤的曲目卡，甚至直接開場。
        //
        // 要關的是 interactable（自己的按鈕不要再回應），不是 blocksRaycasts。
        if (rootGroup != null) rootGroup.interactable = false;
        float elapsed = 0f;
        const float span = 0.20f;
        while (elapsed < span)
        {
            elapsed += Time.unscaledDeltaTime;
            if (rootGroup != null) rootGroup.alpha = 1f - EaseOut(elapsed / span);
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>關掉 uGUI 的方向鍵導覽。這個畫面的鍵盤由 HandleKeyboard 一個人管。</summary>
    private static void SilenceNavigation(Selectable target)
    {
        if (target == null) return;
        Navigation nav = target.navigation;
        nav.mode = Navigation.Mode.None;
        target.navigation = nav;
    }

    /// <summary>按著不放就連發。第一下立刻生效，之後 450ms 才開始重複。</summary>
    private static void AddRepeat(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null) return;
        var repeat = button.gameObject.AddComponent<UIHelpers.RepeatPress>();
        repeat.initialDelay = 0.45f;
        repeat.repeatRate = 0.09f;
        if (repeat.onRepeat == null) repeat.onRepeat = new UnityEngine.Events.UnityEvent();
        repeat.onRepeat.AddListener(action);
    }

    /// <summary>分析資料回來的時候讓右欄淡進來。</summary>
    /// <remarks>
    /// 四張圖原本是同一幀全部就位，而在那之前畫面完全靜止（只有一行「分析中…」）
    /// —— 看起來像卡住然後突然跳完。淡進來至少讓人知道剛剛那段是在算東西。
    /// </remarks>
    private void FadeInAnalysis()
    {
        if (analysisGroup == null || !isActiveAndEnabled) return;
        if (analysisFade != null) StopCoroutine(analysisFade);
        analysisFade = StartCoroutine(FadeInAnalysisRoutine());
    }

    private System.Collections.IEnumerator FadeInAnalysisRoutine()
    {
        analysisGroup.alpha = 0.35f;
        float elapsed = 0f;
        const float span = 0.24f;
        while (elapsed < span)
        {
            elapsed += Time.unscaledDeltaTime;
            analysisGroup.alpha = Mathf.Lerp(0.35f, 1f, EaseOut(elapsed / span));
            yield return null;
        }
        analysisGroup.alpha = 1f;
        analysisFade = null;
    }

    /// <summary>鍵盤操作。</summary>
    /// <remarks>
    /// **這是節奏遊戲。** 玩家的手本來就放在鍵盤上，卻得為了選一首歌把手拿開去抓
    /// 滑鼠 —— 而且選完還要再放回來。上下選曲、左右換難度、Enter 開始、Esc 回演
    /// 奏會，四件事就涵蓋整個畫面的主要動線。
    ///
    /// 搜尋框有焦點的時候整組讓開：那時候上下左右是在編輯文字。
    /// </remarks>
    private void HandleKeyboard()
    {
        if (closing || rootRect == null || !rootRect.gameObject.activeInHierarchy) return;
        // 蓋在上面的東西優先。譜面檢視器是自己一顆根物件、自己的 Canvas（26500 >
        // 這裡的 25000），而且開它的時候**不會**停用 rootRect —— 不擋的話，在檢視
        // 器上按 Enter 會把遊戲直接開起來，而檢視器還整片蓋在最上面，它每 0.25 秒
        // 還會把場上所有 AudioSource 靜音一次。
        if (ChartOverviewViewer.Instance != null && ChartOverviewViewer.Instance.IsOpen) return;
        if (SongScoreBookOverlay.IsOpen) return;
        if (SyllabusBookOverlay.IsOpen || SyllabusExamHall.IsOpen) return;
        if (searchField != null && searchField.isFocused) return;
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            SwitchToRecital();
            return;
        }
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            StartPractice();
            return;
        }
        if (keyboard.downArrowKey.wasPressedThisFrame) { StepSong(1); return; }
        if (keyboard.upArrowKey.wasPressedThisFrame) { StepSong(-1); return; }
        if (keyboard.rightArrowKey.wasPressedThisFrame) { StepDifficulty(1); return; }
        if (keyboard.leftArrowKey.wasPressedThisFrame) { StepDifficulty(-1); return; }
    }

    /// <summary>上下換一首，並且把那一列捲進視野。</summary>
    private void StepSong(int delta)
    {
        if (filtered.Count == 0) return;
        int index = selectedSong != null ? filtered.IndexOf(selectedSong) : -1;
        index = index < 0 ? (delta > 0 ? 0 : filtered.Count - 1)
                          : Mathf.Clamp(index + delta, 0, filtered.Count - 1);
        UiSfx.Move();
        SelectSong(filtered[index]);
        ScrollRowIntoView(index);
    }

    /// <summary>左右換一份譜。順序就是畫面上那一排的順序（由低到高）。</summary>
    private void StepDifficulty(int delta)
    {
        if (difficultyVariantsShown.Count == 0) return;
        int index = selectedVariant != null ? difficultyVariantsShown.IndexOf(selectedVariant) : -1;
        index = index < 0 ? 0 : Mathf.Clamp(index + delta, 0, difficultyVariantsShown.Count - 1);
        UiSfx.Select();
        SelectVariant(difficultyVariantsShown[index]);
    }

    /// <summary>把第 n 列捲到看得到的地方。已經看得到就不要動 —— 每按一次都重新
    /// 置中會讓整份清單一直在腳下滑。</summary>
    private void ScrollRowIntoView(int index)
    {
        if (listScroller == null) return;
        // 列高從 72 換成 112 的時候這裡漏掉了，於是每按一次方向鍵都少捲 40 —— 往下
        // 走十幾首之後選中的那一列就跑到畫面外了。
        const float pitch = RowHeight + 8f;   // 列高 + VerticalLayoutGroup 的間距
        float top = index * pitch;
        float bottom = top + pitch;
        float view = listScroller.ViewportHeight;
        if (view <= 1f) return;
        float current = listScroller.Current;
        if (top < current) listScroller.ScrollTo(top);
        else if (bottom > current + view) listScroller.ScrollTo(bottom - view);
    }

    /// <summary>螢幕不是 16:9 的時候，整個中欄等比縮，不要讓它溢出去。</summary>
    /// <remarks>
    /// CanvasScaler 是 match 0.5，所以邏輯畫布只有在 16:9 時才剛好是 1920×1080。
    /// 21:9 的邏輯高度只有 935，中欄的高度預算（916）就再也塞不下 —— 畫框會戳進
    /// 頂欄、走勢卡會掉出畫面外；3:2 則是寬度不夠，卡片往兩側壓到清單和分析欄。
    ///
    /// 這裡不重排，只把整欄縮小：版面的比例是照著設計走的，不夠大就一起變小，比
    /// 各自去擠更不容易出錯。
    /// </remarks>
    private void FitCentreToScreen()
    {
        if (standRoot == null) return;
        float h = standRoot.rect.height;
        float w = standRoot.rect.width;
        if (h < 1f || w < 1f) return;
        float k = Mathf.Min(1f, h / StandDesignHeight, w / StandDesignWidth);
        if (Mathf.Abs(standRoot.localScale.x - k) < 0.0005f) return;
        standRoot.localScale = new Vector3(k, k, 1f);
    }

    private void Update()
    {
        FitCentreToScreen();
        HandleKeyboard();

        if (searchDebounce >= 0f)
        {
            searchDebounce -= Time.unscaledDeltaTime;
            if (searchDebounce < 0f) RefreshList();
        }

        // 抽屜滑開／收起。有明確的長度和終點，拉開時過頭一點再回來 —— 那一下就
        // 是「撞到底」。
        if (drawerTime >= 0f)
        {
            drawerTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(drawerTime / drawerSpan);
            float k = drawerOpening ? EaseOutBack(t) : EaseOut(t);
            drawerHeight = Mathf.LerpUnclamped(drawerFrom, drawerTarget, k);
            if (t >= 1f)
            {
                drawerHeight = drawerTarget;
                drawerTime = -1f;
            }
            ApplyDrawer();
        }

        // 設定關掉了就把練習室叫回來。設定頁沒有關閉事件可以訂，所以用看的。
        if (watchedSettingsPanel == null) return;
        if (watchedSettingsPanel.activeInHierarchy) return;
        watchedSettingsPanel = null;
        if (rootRect != null) rootRect.gameObject.SetActive(true);
        try { SongSelectionManager.Instance?.HideSelectionPanelOnly(); } catch { }
        RefreshSpeed();
        // 這是唯一一條「回到這個畫面但不重建」的路（其他路都會把整個房間銷毀再蓋一
        // 份），所以評分只在這裡補讀一次；讀的是快取，沒有輪詢。
        RefreshRating();
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
        if (selectedSong == null || selectedVariant == null || closing)
        {
            if (!closing) UiSfx.Denied();       // 還沒選到東西就按了開始
            return;
        }
        closing = true;
        UiSfx.Select();
        previewToken++;
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        var settings = SettingsManager.Instance;
        if (settings != null && !settings.PracticeMode) settings.SetPracticeMode(true);

        selectedSong.selectedVariant = selectedVariant;
        // 轉盤也要指到同一首：開場流程有幾樣東西（音訊分段、速度事件、背景、合成
        // 鋼琴）是回頭問 GetSelectedSong() 的，不同步的話它們會套到轉盤停著的那一
        // 首身上 —— 那正是「不管選哪一首都變成第一首」的來源。
        SongSelectionManager.Instance?.SyncSelectionTo(selectedSong, selectedVariant);
        StartCoroutine(StartPracticeRoutine(selectedVariant));
    }

    /// <summary>開始練習：先退場再交棒，不要在畫面還亮著的時候硬切。</summary>
    private System.Collections.IEnumerator StartPracticeRoutine(
        SongSelectionManager.SongOption variant)
    {
        if (rootGroup != null) rootGroup.interactable = false;
        if (previewSource != null && previewSource.isPlaying)
        {
            StartCoroutine(FadePreview(0f, 0.18f));
        }
        float elapsed = 0f;
        const float span = 0.20f;
        while (elapsed < span)
        {
            elapsed += Time.unscaledDeltaTime;
            if (rootGroup != null) rootGroup.alpha = 1f - EaseOut(elapsed / span);
            yield return null;
        }

        var manager = SongSelectionManager.Instance;
        // 先把選曲畫面放回去再開始：StartSongOption 走的是正規的開場流程，它預期
        // 自己負責把選曲收起來、把遊戲打開。
        Destroy(gameObject);
        if (manager != null)
        {
            manager.ShowSelectionPanelOnly();
            manager.StartSongOption(variant);
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
    // ── 可平鋪的噪聲 ──────────────────────────────────────────────────
    //
    // 這三支本來寫在這裡，現在搬到 `SurfaceNoise` 給整專案共用 ——
    // `ClassicalBookUITheme` 的貼圖產生器也改用同一份。seed 0 的路徑和舊版逐位
    // 相同，所以下面的木紋輸出一個 bit 都沒變。
    private static float Hash(int x, int y, int px, int py)
        => SurfaceNoise.Hash(x, y, px, py);

    private static float TileNoise(float x, float y, int px, int py)
        => SurfaceNoise.Noise(x, y, px, py);

    private static float TileFbm(float u, float v, int px, int py, int octaves)
        => SurfaceNoise.Fbm(u, v, px, py, octaves);

    /// <summary>一整面木牆的像素。木理沿 y 走，年輪橫切 x。</summary>
    /// <remarks>
    /// 程序木紋最容易失敗的地方是**太規律**。上一版是三條 sin 疊起來：六條年輪、
    /// 間距一樣、寬度一樣、對比一樣，而且剖面是上下對稱的正弦 —— 那是波浪板，不
    /// 是木頭。真的木頭有四件事：
    ///
    ///   1. **年輪的剖面不對稱。** 晚材是一條窄而深的線，早材是一大片淺色，兩者
    ///      的寬度差好幾倍。用 sin 畫，亮帶和暗帶一樣寬。
    ///   2. **間距每一年都不同**，而且整條會跟著樹幹扭。
    ///   3. **木頭是一塊一塊拼的。** 每塊的相位、頻率、色調都不一樣，接縫是一條
    ///      暗線加一道受光的亮邊。一整面連續的紋路看起來是印刷品。
    ///   4. **順著木理有極細的導管**（棕眼），比年輪細一個數量級。
    ///
    /// 顏色也不是只有明暗：晚材偏紅、早材偏黃，所以是在兩個顏色之間插值，不是拿
    /// 一個灰階去乘固定的通道比例。
    /// </remarks>
    private static Color[] BuildWoodPixels(int size)
    {
        const int planks = 4;
        var pixels = new Color[size * size];
        Color late = new Color(0.560f, 0.442f, 0.352f, 1f);   // 晚材：窄、深、偏紅
        Color early = new Color(1.000f, 0.968f, 0.902f, 1f);  // 早材：寬、淺、偏黃

        // 板寬不等：等分之後每一條接縫再往左右挪一點。一排一樣寬的板子是地磚。
        var edges = new float[planks + 1];
        for (int i = 0; i <= planks; i++)
        {
            edges[i] = i == 0 || i == planks
                ? i / (float)planks
                : (i + (Hash(i, 3, planks, 16) - 0.5f) * 0.55f) / planks;
        }

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;

                int plank = planks - 1;
                for (int i = 0; i < planks; i++)
                {
                    if (u >= edges[i] && u < edges[i + 1]) { plank = i; break; }
                }
                float left = edges[plank];
                float right = edges[plank + 1];
                float inPlank = (u - left) / Mathf.Max(0.0001f, right - left);
                float seed = Hash(plank, 7, planks, 32);
                float cut = Hash(plank, 19, planks, 32);

                // 年輪整條會扭：低頻大幅度加中頻小幅度。
                float warp = (TileFbm(u, v, 3, 2, 3) - 0.5f) * 0.40f
                           + (TileFbm(u, v, 9, 5, 2) - 0.5f) * 0.09f;

                float freq = 5.0f + seed * 5.0f;
                float r;
                if (cut > 0.42f)
                {
                    // 平鋸板：年輪是繞著樹心的同心圓，斜切出來是**被拉長的**拱，
                    // 也就是俗稱的火焰紋。縱向的權重要遠小於橫向 —— 兩邊一樣大的
                    // 話橢圓會退化成同心圓，看起來是靶心不是木頭。
                    float centre = 0.18f + seed * 0.64f;
                    float dx = (inPlank - centre) + warp * 0.5f;
                    float dv = v - (0.12f + cut * 0.76f);
                    dv -= Mathf.Floor(dv + 0.5f);          // 包到 ±0.5，上下才接得起來
                    float dy = dv * 0.17f;
                    r = Mathf.Sqrt(dx * dx + dy * dy) * freq * 2.3f;
                }
                else
                {
                    // 徑鋸板：直而窄的紋。
                    r = (inPlank + warp) * freq + seed * 13f;
                }

                float t = r - Mathf.Floor(r);
                float band = Mathf.Min(t, 1f - t);
                float lateness = Mathf.Exp(-band * band * 230f);
                float growth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.10f) / 0.62f));
                float grain = Mathf.Clamp01((1f - lateness) * (0.78f + growth * 0.22f));

                // 導管：順著木理的極細線，比年輪細一個數量級。
                float pore = TileNoise(u * 180f, v * 14f, 180, 14);
                grain -= Mathf.Clamp01(pore - 0.60f) * 1.10f;
                grain = Mathf.Clamp01(grain);

                float tone = 0.88f + seed * 0.20f;         // 每塊的色調差
                Color c = Color.Lerp(late, early, grain);
                float cr = c.r * tone, cg = c.g * tone, cb = c.b * tone;

                // 接縫：一條真的縫，右邊那塊的左緣受光。只把 grain 壓暗是不夠的
                // —— 那樣它只是另一條年輪。
                float seamPx = Mathf.Min(u - left, right - u) * size;
                if (seamPx < 1.6f)
                {
                    float k = 1f - seamPx / 1.6f;
                    float dark = 1f - 0.72f * k;
                    cr *= dark; cg *= dark; cb *= dark;
                }
                else if ((u - left) * size < 3.2f)
                {
                    float k = 1f - ((u - left) * size - 1.6f) / 1.6f;
                    float lit = 1f + 0.16f * k;
                    cr *= lit; cg *= lit; cb *= lit;
                }

                pixels[y * size + x] = new Color(
                    Mathf.Clamp01(cr), Mathf.Clamp01(cg), Mathf.Clamp01(cb), 1f);
            }
        }
        return pixels;
    }

    private static Color[] woodPixels;

    private static Sprite MakeWoodSprite(string name, bool across)
    {
        const int size = 512;
        // 五十萬個像素、每個要算六次噪聲 —— 直紋和橫紋共用同一份，不要算兩次。
        Color[] pixels = woodPixels ?? (woodPixels = BuildWoodPixels(size));
        if (across)
        {
            // 橫的板子要橫的木理。沒有一塊家具是把紋路鋸成橫過板寬的。
            var turned = new Color[pixels.Length];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) turned[y * size + x] = pixels[x * size + y];
            }
            pixels = turned;
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels(pixels);
        texture.Apply(true, true);

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    /// <summary>直的木理，給牆用。</summary>
    private static Sprite WoodSprite()
    {
        if (woodSprite == null) woodSprite = MakeWoodSprite("PracticeRoomWood", false);
        return woodSprite;
    }

    /// <summary>橫的木理，給把手、分類條那種橫條用。</summary>
    private static Sprite WoodSpriteAcross()
    {
        if (woodAcrossSprite == null) woodAcrossSprite = MakeWoodSprite("PracticeRoomWoodAcross", true);
        return woodAcrossSprite;
    }

    /// <summary>白樺：細而淡的縱紋，加上疏落的深色皮孔。</summary>
    /// <remarks>
    /// 白樺和一般木頭的差別不在顏色，在**皮孔**：那些短短的深色橫斑是它的識別特
    /// 徵。上一版用 `Floor(v * 26)` 決定斑點在哪一列，結果每一列高度一樣、間距一
    /// 樣，整面排成一個網格 —— 那是格紋不是樹皮。改成用格子的雜湊決定有沒有、在
    /// 哪裡、多長，斑點就散開了。
    /// </remarks>
    private static Sprite BirchSprite()
    {
        if (birchSprite != null) return birchSprite;

        const int size = 512;
        var pixels = new Color[size * size];
        Color dim = new Color(0.760f, 0.735f, 0.690f, 1f);
        Color pale = new Color(1.000f, 0.992f, 0.968f, 1f);

        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size;
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;

                // 很淡的縱紋，只有木頭那一版的三分之一深。
                float warp = (TileFbm(u, v, 4, 2, 3) - 0.5f) * 0.30f;
                float r = (u + warp) * 11f;
                float t = r - Mathf.Floor(r);
                float band = Mathf.Min(t, 1f - t);
                float grain = 1f - Mathf.Exp(-band * band * 150f) * 0.42f;

                float fibre = TileNoise(u * 120f, v * 10f, 120, 10);
                grain -= Mathf.Clamp01(fibre - 0.66f) * 0.45f;

                // 皮孔：每個格子自己決定有沒有、偏多少、多長多厚。
                const int cellsX = 7;
                const int cellsY = 11;
                float cx = u * cellsX;
                float cy = v * cellsY;
                int ix = Mathf.FloorToInt(cx);
                int iy = Mathf.FloorToInt(cy);
                float has = Hash(ix, iy, cellsX, cellsY);
                if (has > 0.55f)
                {
                    float ox = Hash(ix + 31, iy, cellsX + 31, cellsY);
                    float oy = Hash(ix, iy + 17, cellsX, cellsY + 17);
                    float len = 0.12f + has * 0.26f;          // 橫向長度（格內比例）
                    float thick = 0.055f + ox * 0.045f;       // 縱向厚度
                    float dx = Mathf.Abs((cx - ix) - (0.15f + ox * 0.6f));
                    float dy = Mathf.Abs((cy - iy) - (0.2f + oy * 0.6f));
                    if (dx < len && dy < thick)
                    {
                        float fade = (1f - dx / len) * (1f - dy / thick);
                        grain -= fade * fade * 0.38f;
                    }
                }

                grain = Mathf.Clamp01(grain);
                float tone = 0.97f + Hash(ix, iy + 5, cellsX, cellsY + 5) * 0.05f;
                Color c = Color.Lerp(dim, pale, grain);
                pixels[y * size + x] = new Color(
                    Mathf.Clamp01(c.r * tone), Mathf.Clamp01(c.g * tone),
                    Mathf.Clamp01(c.b * tone), 1f);
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        {
            name = "PracticeRoomBirch",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels(pixels);
        texture.Apply(true, true);

        birchSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        birchSprite.name = "PracticeRoomBirch";
        return birchSprite;
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

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot,
        Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
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

    /// <summary>檯面：深色的布，金邊，底下一道影子。</summary>
    private static RectTransform MakePanel(RectTransform parent, string name, Color colour)
    {
        RectTransform rect = AddRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = ClassicalBookUITheme.GetClothTextureSprite();
        image.type = Image.Type.Tiled;
        image.color = colour;
        AddDropShadow(rect, 22f, 0.55f);
        AddEdgeLight(rect, false);
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
        PaperFace(image, Paper, false);
        AddDropShadow(rect, 18f, 0.50f);
        AddGrain(rect, name, Paper);
        AddEdgeLight(rect, true, 0f, 1.25f, CornerRadius);
        return rect;
    }

    /// <summary>字的柔邊投影。</summary>
    /// <remarks>
    /// 上一版掛的是 uGUI 的 `Shadow`，而 **TMP 根本不跑 `IMeshModifier`** —— 那三
    /// 顆元件從頭到尾什麼都沒做，木牆上那行 40 級的大曲名是**完全沒有**投影的。
    ///
    /// TMP 自己有 UNDERLAY：在 shader 裡照字形的距離場長出來再模糊，所以筆畫的內
    /// 角也會有影子，不是整塊字往下偏一格。偏移的方向和畫框、邊光同一個光源。
    ///
    /// 換字型會換掉材質，所以設定完字型之後還要再叫一次（見 SelectSong）。
    /// </remarks>
    private static readonly Dictionary<TextMeshProUGUI, Material> underlayMaterials =
        new Dictionary<TextMeshProUGUI, Material>();

    private static void AddSoftShadow(TextMeshProUGUI label)
    {
        if (label == null) return;
        Material own = label.fontMaterial;      // 讀取就會複製一份，不會污染共用材質
        if (own == null) return;
        // 換字型會讓 TMP 生一份新的材質實例，而舊的那份沒有人負責銷毀 —— 一首日
        // 文曲名每選一次就漏兩份。這裡記住上一份，換掉時順手收掉。
        if (underlayMaterials.TryGetValue(label, out Material previous))
        {
            if (ReferenceEquals(previous, own)) return;
            if (previous != null) Destroy(previous);
        }
        underlayMaterials[label] = own;
        own.EnableKeyword("UNDERLAY_ON");
        own.SetColor("_UnderlayColor", new Color(0.030f, 0.015f, 0.006f, 0.85f));
        own.SetFloat("_UnderlayOffsetX", 0.35f);
        own.SetFloat("_UnderlayOffsetY", -0.65f);
        own.SetFloat("_UnderlayDilate", 0.10f);
        own.SetFloat("_UnderlaySoftness", 0.45f);
        label.UpdateMeshPadding();              // 少這行投影會被字形的邊界裁掉
    }

    /// <summary>柔邊投影。grow 是往外擴幾像素。</summary>
    /// <remarks>
    /// uGUI 的 `Shadow` 是「把整份網格平移一次、染個顏色再畫一遍」—— 畫出來的不是
    /// 影子，是**元件自己的複本**：上下刀切、左右和本體切齊、濃度從頭到尾一樣。更
    /// 糟的是它疊在 `Outline` 後面，而 `Outline` 已經先把網格複製成五份，所以最後
    /// 的暗度是設定值的兩倍 —— 面板寫 0.6，合成出來是 0.89。
    /// </remarks>
    private static void AddDropShadow(RectTransform target, float grow, float alpha)
    {
        if (target == null) return;
        RectTransform shade = AddRect("Shade", target);
        // **外擴量必須讓貼圖的洞剛好蓋住元件。**
        //
        // 九宮格的洞永遠是「這個 rect 往內縮 ShadowInset」，所以 rect 一偏移，洞
        // 就跟著偏 —— 元件露在洞外的那一條會被自己的影子塗黑。上一版為了做出方向
        // 把下緣多擴、上緣少擴，結果每一張卡的上緣都多了一條 4px 的硬黑帶。
        //
        // 方向不能靠位移，只能**烤進貼圖裡**（見 ShadowSprite），而影子要多長則
        // 靠縮放九宮格：pixelsPerUnitMultiplier 把 border 和內縮一起等比縮放。
        Stretch(shade, -grow, -grow, -grow, -grow);
        shade.SetAsFirstSibling();
        Image image = shade.gameObject.AddComponent<Image>();
        image.sprite = ShadowSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = ShadowInset / Mathf.Max(1f, grow);
        image.color = new Color(0.030f, 0.015f, 0.006f, alpha);
        image.raycastTarget = false;
    }

    /// <summary>有方向的柔邊投影貼圖：光從左上來，上緣幾乎不留影、右下最濃。</summary>
    /// <remarks>
    /// 和 SettingsUiKit.GlowSprite 的結構一樣（內部 alpha 0、外面高斯衰減），只多
    /// 一件事：濃度乘上一個跟著**外法線方向**走的權重。四邊一樣濃的不是影子，是
    /// 描邊 —— 那正是 uGUI 的 Shadow 看起來廉價的原因。
    /// </remarks>
    private static Sprite ShadowSprite()
    {
        if (shadowSprite != null) return shadowSprite;

        const int size = 64;
        const int border = 24;
        const float radius = 10f;
        const float sigma = 5.5f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "PracticeRoomShadow",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        var pixels = new Color[size * size];
        float half = size * 0.5f;
        float extent = half - ShadowInset;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;
                float qx = Mathf.Abs(px) - (extent - radius);
                float qy = Mathf.Abs(py) - (extent - radius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                if (d <= 0f)
                {
                    pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                // 這一點是往哪個方向離開形狀的。平邊只有一個分量，轉角兩個都有。
                float dx = qx > 0f ? Mathf.Sign(px) * qx : 0f;
                float dy = qy > 0f ? Mathf.Sign(py) * qy : 0f;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                float hx = len > 0.0001f ? dx / len : 0f;
                float hy = len > 0.0001f ? dy / len : 0f;   // +y 是上（貼圖座標）
                float w = Mathf.Clamp(0.55f + 0.30f * hx - 0.45f * hy, 0.22f, 1.35f);

                float a = Mathf.Exp(-(d * d) / (2f * sigma * sigma)) * w;
                pixels[y * size + x] = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        shadowSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        shadowSprite.name = "PracticeRoomShadow";
        return shadowSprite;
    }

    // 邊光的兩組顏色。光從左上來，所以上緣最亮、左緣次之、右緣和下緣壓暗。
    private static readonly Color[] EdgePaper =
    {
        new Color(0.99f, 0.93f, 0.78f, 0.85f), new Color(0.92f, 0.84f, 0.66f, 0.60f),
        new Color(0.46f, 0.32f, 0.16f, 0.45f), new Color(0.34f, 0.22f, 0.10f, 0.70f)
    };
    /// <summary>選中的難度卡：上緣和左緣打亮，右下壓暗，和光源一致。</summary>
    private static readonly Color[] EdgeSelected =
    {
        new Color(0.98f, 0.92f, 0.78f, 0.85f), new Color(0.98f, 0.92f, 0.78f, 0.85f),
        new Color(0.20f, 0.13f, 0.06f, 0.55f), new Color(0.20f, 0.13f, 0.06f, 0.55f)
    };
    private static readonly Color[] EdgeDark =
    {
        new Color(0.86f, 0.66f, 0.30f, 0.55f), new Color(0.62f, 0.46f, 0.22f, 0.40f),
        new Color(0.22f, 0.14f, 0.07f, 0.50f), new Color(0.14f, 0.08f, 0.04f, 0.75f)
    };

    /// <summary>掛一圈方向性的邊光。paper：淺色面用暖白，深色面用金。</summary>
    private static EdgeLightGraphic AddEdgeLight(RectTransform target, bool paper,
        float inset = 0f, float thickness = 1.25f, float radius = 0f)
    {
        RectTransform rect = AddRect("Edge", target);
        Stretch(rect, inset, inset, inset, inset);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var edge = rect.gameObject.AddComponent<EdgeLightGraphic>();
        edge.raycastTarget = false;
        edge.Thickness = thickness;
        edge.Radius = radius;
        edge.SetSides(paper ? EdgePaper : EdgeDark);
        return edge;
    }

    /// <summary>圓角的紙面：一張反鋸齒的九宮格，顏色由 tint 給。</summary>
    private static void PaperFace(Image image, Color tint, bool interactive = true)
    {
        image.sprite = SettingsUiKit.PracticePaperSprite();
        image.type = Image.Type.Sliced;
        // 壓暗那一格只給會互動的東西：卡片沒有 hover 幫它乘回來，壓了就是一直暗。
        if (interactive) SetPlate(image, tint);
        else image.color = tint;
    }

    /// <summary>在純色的面上補紙紋，而且每一塊取到的位置都不一樣。</summary>
    /// <remarks>
    /// 圓角的面是平的，大面積會像塑膠。紙紋要用 `RawImage` 不能用 `Image` ——
    /// 只有 `RawImage` 有 `uvRect`。而「每一塊都取到貼圖的同一個角落」正是上一版看
    /// 起來像複製貼上的原因：同一顆鏽點會出現在六顆難度鈕的同一個位置。
    ///
    /// 顏色給的是**底色本身**：疊上去的結果是 底×(1−a) + 底×紋×a，等效於相乘，
    /// 不會像白色的紋那樣把卡片洗亮。
    /// </remarks>
    private static void AddGrain(RectTransform target, string seedKey, Color tint,
        float alpha = 0.40f)
    {
        Sprite paper = ClassicalBookUITheme.GetPaperTextureSprite();
        if (paper == null || paper.texture == null || target == null) return;
        RectTransform rect = AddRect("Grain", target);
        Stretch(rect, 6f, 6f, 6f, 6f);          // 內縮 6 > 圓角在 45° 的切入量
        rect.SetAsFirstSibling();
        var grain = rect.gameObject.AddComponent<RawImage>();
        grain.texture = paper.texture;
        grain.color = new Color(tint.r, tint.g, tint.b, alpha);
        grain.raycastTarget = false;
        // 比例不能在這裡算死：靠錨點撐開的卡片（右欄那四張）在 Awake 當下高度還是
        // 0，算出來的紋路會被縱向拉長一倍多。交給一顆跟著 rect 變動的小元件。
        var fit = rect.gameObject.AddComponent<GrainFit>();
        fit.Bind(grain, Mathf.Abs((seedKey ?? "x").GetHashCode() % 997) / 997f);
    }

    /// <summary>卡片內側再描一條細線。角飾在 30px 上只會糊掉，細線不會。</summary>
    private static void AddInnerRule(RectTransform card, float inset = 7f)
    {
        RectTransform rect = AddRect("Rule", card);
        Stretch(rect, inset, inset, inset, inset);
        rect.gameObject.AddComponent<CanvasRenderer>();
        var edge = rect.gameObject.AddComponent<EdgeLightGraphic>();
        edge.raycastTarget = false;
        edge.Thickness = 1f;
        edge.Radius = Mathf.Max(0f, CornerRadius - inset);
        Color line = new Color(Ink.r, Ink.g, Ink.b, 0.20f);
        edge.SetSides(new[] { line, line, line, line });
    }

    /// <summary>
    /// 六階的值，給 <see cref="SnapFontSize"/> 吸附用。
    /// </summary>
    /// <remarks>
    /// 不能是 <c>static readonly</c> 的初始化式：那會在型別初始化時就抓走六個欄位的
    /// <b>預設值</b>，而真正的值要到 Build 向畫布問過才算出來。跟著一起重建。
    /// </remarks>
    private static float[] TextScale =
        { TextCaption, TextBody, TextLabel, TextTitle, TextDisplay, TextHero };

    /// <summary>把字級吸到最近的一階；平手往小的靠。</summary>
    private static float SnapFontSize(float size)
    {
        float best = TextScale[0];
        float gapBest = Mathf.Abs(size - best);
        for (int i = 1; i < TextScale.Length; i++)
        {
            float gap = Mathf.Abs(size - TextScale[i]);
            if (gap < gapBest - 0.001f) { best = TextScale[i]; gapBest = gap; }
        }
        return best;
    }

    /// <summary>全大寫的拉丁字要撐開才讀得順；中日文一律不加字距。</summary>
    private static float TrackingFor(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0f;
        bool upper = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c >= 'a' && c <= 'z') return 0f;
            if (c > 127) return 0f;
            if (c >= 'A' && c <= 'Z') upper = true;
        }
        return upper ? Tracking : 0f;
    }

    // ── 難度色的三個位階 ──────────────────────────────────────────────
    //
    // 難度色來自遊戲共用的那一組（DifficultyVisualPalette），色相不能動 —— 動了
    // 就和選曲畫面對不起來。能動的只有深淺，所以每一個難度都有三個版本：
    //
    //   淡面 HueFace —— 未選中的卡面。墨色字對它 4.7–6.0:1，色相看得出來。
    //   深填 HueFill —— 選中的卡面，也是未選中時頂緣那條色帶。奶油字對它 10:1。
    //
    // 只要兩個深度就夠，而且色帶和選中的填色**就用同一個顏色**：未選中的時候，深
    // 色帶打在淡面上是 3.7–4.2:1，一眼看得到；選中的時候它融進整片填色裡 —— 那正
    // 是想要的效果，色帶長成了整張卡。中間再插一個深度只會兩頭都不夠對比。
    //
    // 上一版只有選中的那一顆上色，而且是壓到同一個明度的近黑色 —— 未選中的四顆
    // 全成了同一塊米色，顏色等於不存在。**顏色是身分，不是狀態**：身分兩個狀態都
    // 要在場，狀態靠明暗翻到底來講 —— 紙上的卡片選中就變深，深色檯面上的階層鈕
    // 選中就變亮，兩邊都是「翻到離自己底色最遠的那一端」。
    private static Color HueFace(Color hue) => Color.Lerp(Control, hue, 0.30f);
    private static Color HueFill(Color hue) => Color.Lerp(hue, Color.black, 0.64f);

    /// <summary>
    /// 難度色壓到能當墨水用：四色各自縮到對紙面 4.5:1，所以它們只差在色相。
    /// </summary>
    /// <remarks>
    /// **為什麼要把對比拉成一樣。** 四色如果各自保留原本的亮度，讀起來就是「四種不
    /// 同粗細的字」而不是「四種顏色的同一個字」—— 黃色會跳出來、紅色會沉下去。壓到
    /// 同一個對比之後四者的 L* 都是 39.8，差別只剩色相，兩兩 ΔE 最小 38.9。
    ///
    /// **為什麼是 4.5:1。** 等級是 32px 粗體，按 WCAG 算大字、門檻只有 3:1，但取一般
    /// 文字的 4.5 才有餘裕 —— 而且再暗下去色相又會開始塌（7:1 的時候 ΔE 掉到 29）。
    /// 選中的那一列底色是更亮的 PaperSel，對比只會往上走。
    ///
    /// 二分找倍率，不是解析解：sRGB 的轉移函數在暗部有一段線性趾部，冪次近似在這裡
    /// 會差到 5%。一次重建裡 58 列用的是同一個色，所以只算一次。
    /// </remarks>
    private static Color TierInk(Color hue)
    {
        if (inkCacheKey == hue) return inkCacheValue;
        float target = (Luminance(PaperRow) + 0.05f) / 4.5f - 0.05f;
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 18; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Luminance(new Color(hue.r * mid, hue.g * mid, hue.b * mid)) >= target) hi = mid;
            else lo = mid;
        }
        inkCacheKey = hue;
        inkCacheValue = new Color(hue.r * hi, hue.g * hi, hue.b * hi, 1f);
        return inkCacheValue;
    }

    private static Color inkCacheKey;
    private static Color inkCacheValue;

    private static float Luminance(Color c)
    {
        return 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);
    }

    private static float Channel(float v)
    {
        return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }


    /// <summary>按鈕底色統一壓暗一格，懸停時才有往上走的空間。</summary>
    private static void SetPlate(Image plate, Color colour)
    {
        if (plate == null) return;
        plate.color = new Color(colour.r * RestDim, colour.g * RestDim, colour.b * RestDim, colour.a);
    }

    private static TextMeshProUGUI AddText(RectTransform parent, string name, string value,
        float size, Color colour, TextAlignmentOptions alignment, FontStyles style)
    {
        // 字級一律吸到六階上：呼叫端寫 17 或 19 不是設計，是手滑。
        size = SnapFontSize(size);
        TextMeshProUGUI label = SettingsUiKit.CreateLabel(parent, name, size, colour, alignment, style);
        // TMP 在「框高 < 字級 × 1.5」時整段不畫，而這裡的框高度是照版面給的。
        label.overflowMode = TextOverflowModes.Overflow;
        // 一律單行。曲名折到第二行會把列高撐開、也會和作者疊在一起；放不下的那一
        // 段交給跑馬燈，不是換行。
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // 字距不是「鬆一點比較高級」，是一種語意：只有全大寫的拉丁字需要。
        label.characterSpacing = TrackingFor(value);
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
        PaperFace(plate, primary ? ButtonPrimary : Control);

        // 小鈕的影子要跟著縮：34px 的箭頭鈕外擴 18 會超出抽屜的裁切框，影子被削
        // 掉一角比沒有影子還糟。
        float small = Mathf.Min(size.x, size.y);
        float grow = Mathf.Clamp(small * 0.36f, 9f, 18f);
        AddDropShadow(rect, grow, primary ? 0.44f : 0.36f);
        AddEdgeLight(rect, true, 0f, 1.25f, CornerRadius);

        if (!string.IsNullOrEmpty(caption))
        {
            TextMeshProUGUI label = AddText(rect, "Label", caption, fontSize, Ink,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(label.rectTransform, 8f, 2f, 8f, 2f);
            // 鈕寬是版面訂的，字長是語言訂的：「預覽」兩個字在日文是「プレビュー」
            // 五個（104 寬的鈕裡要 107px），「原速」在英文是 Reset。AddText 開的是
            // Overflow，所以放不下的字是**畫到鈕外面去**，不是被截斷。
            // 和 SettingsUiKit.CreateButton 同一套處理：放得下就用原字級，放不下
            // 才降，下限 0.72。實測最糟的一顆是 ×1.22，還有餘裕。
            //
            // 這裡不能寫進 AddText：那支也用在清單的曲名上，而曲名放不下是交給
            // TextMarquee 跑的，它的條件是「preferredWidth 比框寬」—— 字先縮到
            // 放得下，跑馬燈就永遠不會動。
            // 上限取 label.fontSize 不是參數 fontSize：AddText 會把字級吸到六階上，
            // 呼叫端給 19 的時候實際畫的是 20，拿參數當上限會把那顆鈕做小一級。
            label.enableAutoSizing = true;
            label.fontSizeMin = label.fontSize * 0.72f;
            label.fontSizeMax = label.fontSize;
            label.raycastTarget = false;
        }

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        // 方向鍵只能有一個消費者。EventSystem 的 Navigate 也吃上下左右，而它會從
        // 清單第一列往上找到**收起來的抽屜裡**那個搜尋框（看不見，但選得到），一
        // 選到 TMP_InputField 就自動取得焦點，之後每一次方向鍵都變成編輯文字。
        SilenceNavigation(button);
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1f / RestDim, 1f / RestDim, 1f / RestDim, 1f);
        colours.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
        // 見清單列那邊的說明：selectedColor 預設 0.96 會把剛點過的按鈕壓暗。
        colours.selectedColor = Color.white;
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        return button;
    }

}

/// <summary>難度卡：選中的時候，頂緣那條色帶長成整張卡。</summary>
/// <remarks>
/// 色帶本來就是這張卡的身分（兩個狀態都在），所以「選中」最自然的講法不是換一個
/// 顏色，是讓那條色帶**長滿**——顏色沒有變，只是佔滿了。底下的卡面同時從淡版補到
/// 深版，補完之後兩者同色，接縫自己消失。
///
/// 收回去比長出來快：長出來是結果要讓人看清楚，收回去只是讓位。
///
/// 補間的狀態是**畫出來的那個值**（k），不是線性進度。用線性進度再套曲線的話，
/// 補到一半反向時曲線會換一條，而兩條曲線只有在 0 和 1 相等 —— 在兩張卡之間快
/// 速來回點，看到的不是補間是抖動（實測一幀跳 41px）。從目前的 k 重新起跑，值就
/// 一定是連續的。
/// </remarks>
internal sealed class DifficultyCardFill : MonoBehaviour
{
    private RectTransform bar;
    private Image plate;
    private EdgeLightGraphic edge;
    private TextMeshProUGUI nameLabel;
    private TextMeshProUGUI levelLabel;
    private Color face;
    private Color fill;
    private Color ink;
    private Color inkMuted;
    private Color cream;
    private Color[] edgeIdle;
    private Color[] edgeLit;
    private Vector2 barIdleSize;
    private Vector2 barFullSize;
    private float barIdleY;

    private float k;            // 畫出來的值：0 = 未選中，1 = 選中
    private float from;
    private float target;
    private float time;
    private float span;
    private bool running;

    public void Bind(RectTransform barRect, Image plateImage, EdgeLightGraphic ring,
        TextMeshProUGUI name, TextMeshProUGUI level, Color faceColour, Color fillColour,
        float dim, Vector2 buttonSize, Color inkColour, Color mutedColour, Color creamColour,
        Color[] idleEdge, Color[] litEdge)
    {
        bar = barRect;
        plate = plateImage;
        edge = ring;
        nameLabel = name;
        levelLabel = level;
        // 底色一律先壓暗一格（和 SetPlate 同一條規則），hover 才有往上走的空間。
        face = new Color(faceColour.r * dim, faceColour.g * dim, faceColour.b * dim, 1f);
        fill = new Color(fillColour.r * dim, fillColour.g * dim, fillColour.b * dim, 1f);
        ink = inkColour;
        inkMuted = mutedColour;
        cream = creamColour;
        edgeIdle = idleEdge;
        edgeLit = litEdge;
        if (bar != null)
        {
            barIdleSize = bar.sizeDelta;
            barIdleY = bar.anchoredPosition.y;
        }
        // 長滿就是真的長滿：連寬度和上緣的內縮一起補回去，不然「長滿」的卡片四周
        // 還留著一圈比色帶暗 8% 的邊。
        barFullSize = buttonSize;
    }

    public void SetSelected(bool selected, bool animate)
    {
        float next = selected ? 1f : 0f;
        if (running && Mathf.Approximately(next, target)) return;
        target = next;
        if (!animate || !isActiveAndEnabled)
        {
            k = target;
            running = false;
            Apply();
            return;
        }
        from = k;                               // 從目前畫出來的地方接著走
        time = 0f;
        span = target > from ? 0.20f : 0.14f;
        running = true;
    }

    private void Update()
    {
        if (!running) return;
        time += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(time / span);
        float eased = target > from ? 1f - Mathf.Pow(1f - t, 3f) : t * t;
        k = Mathf.Lerp(from, target, eased);
        if (t >= 1f)
        {
            k = target;
            running = false;
        }
        Apply();
    }

    private void Apply()
    {
        if (bar != null)
        {
            bar.sizeDelta = Vector2.Lerp(barIdleSize, barFullSize, k);
            bar.anchoredPosition = new Vector2(bar.anchoredPosition.x, Mathf.Lerp(barIdleY, 0f, k));
        }
        if (plate != null) plate.color = Color.Lerp(face, fill, k);
        if (levelLabel != null) levelLabel.color = Color.Lerp(ink, cream, k);
        if (nameLabel != null) nameLabel.color = Color.Lerp(inkMuted, cream, k);
        // 邊光跟著同一條 k 走。上一版它是瞬間切的，而卡面要 140–200ms —— 切難度的
        // 那段時間兩張卡都是深色的，唯一分得出來的反而是那圈 1.25px 的細邊。
        if (edge != null && edgeIdle != null && edgeLit != null)
        {
            edge.SetSides(new[]
            {
                Color.Lerp(edgeIdle[0], edgeLit[0], k), Color.Lerp(edgeIdle[1], edgeLit[1], k),
                Color.Lerp(edgeIdle[2], edgeLit[2], k), Color.Lerp(edgeIdle[3], edgeLit[3], k)
            });
        }
    }
}

/// <summary>紙紋的取樣範圍跟著 rect 走。</summary>
/// <remarks>
/// `RawImage.uvRect` 要的是「這塊面積等於貼圖的幾倍」，而靠錨點撐開的卡片在建立
/// 當下還沒有尺寸 —— 在那時候算一次就會永遠錯。偏移量（每一塊取貼圖的不同位置）
/// 是固定的，只有縮放要跟著大小重算。
/// </remarks>
internal sealed class GrainFit : UIBehaviour
{
    private RawImage target;
    private float seed;

    public void Bind(RawImage image, float offset)
    {
        target = image;
        seed = offset;
        Apply();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Apply();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Apply();
    }

    private void Apply()
    {
        if (target == null) return;
        Rect r = ((RectTransform)transform).rect;
        if (r.width < 1f || r.height < 1f) return;
        target.uvRect = new Rect(seed, Mathf.Repeat(seed * 7.13f, 1f),
            r.width / 256f, r.height / 256f);
    }
}

/// <summary>
/// 四邊各自上色的一圈細邊。
/// </summary>
/// <remarks>
/// uGUI 的 `Outline` 做的是**四份對稱的複本**（±x、±y 各一份），所以
/// `effectDistance` 給 (1,−1) 的負號會被抹掉 —— 它在物理上畫不出方向。而且複本沿
/// 用原本的 UV，底下的紙紋會一起被印進邊線裡，金邊因此沿著邊長忽明忽暗。
///
/// 畫框那段註解早就把道理寫完了：「光從左上來：上緣最亮、左緣次之，右緣和下緣壓
/// 暗。四條一樣亮的話再多層也是平的」。這個 graphic 就是把那句話用到其他地方。
/// </remarks>
public sealed class EdgeLightGraphic : MaskableGraphic
{
    public Color TopColour = Color.white;
    public Color LeftColour = Color.white;
    public Color RightColour = Color.black;
    public Color BottomColour = Color.black;
    public float Thickness = 1.25f;
    /// <summary>圓角半徑。要和底下那張九宮格的半徑一致，不然邊會在四個角戳出去。</summary>
    public float Radius;

    public override Texture mainTexture => Texture2D.whiteTexture;

    /// <summary>四邊的顏色，順序是上、左、右、下。</summary>
    public void SetSides(Color[] sides)
    {
        if (sides == null || sides.Length < 4) return;
        TopColour = sides[0];
        LeftColour = sides[1];
        RightColour = sides[2];
        BottomColour = sides[3];
        SetVerticesDirty();
    }

    /// <remarks>
    /// 沿著輪廓走一圈，每個取樣點吐一對（外、內）頂點接成環。顏色不是按「第幾條
    /// 邊」分的，是按那一點的**外法線**分的 —— 朝上就是上緣的顏色、朝左就是左緣
    /// 的，轉角自然過渡。半徑給 0 就退化成四個直角。
    /// </remarks>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float t = Mathf.Max(0.5f, Thickness);
        if (r.width <= t * 2f || r.height <= t * 2f) return;

        float rad = Mathf.Clamp(Radius, 0f, Mathf.Min(r.width, r.height) * 0.5f - t);
        int seg = Mathf.Clamp(Mathf.CeilToInt(rad * 0.4f), 1, 6);

        // 內側的輪廓是「外輪廓整個往內縮 t」—— 半徑小於 t 的時候它的圓角是 0，
        // 圓心也跟著往內移。照抄外圓心的話，直角會被上下兩條邊各蓋一次、再被退化
        // 的轉角三角形蓋半次，四個角就比邊深三倍。
        float radIn = Mathf.Max(0f, rad - t);
        float offIn = Mathf.Max(t, rad);
        Vector2[] pOut =
        {
            new Vector2(r.xMax - rad, r.yMax - rad), new Vector2(r.xMin + rad, r.yMax - rad),
            new Vector2(r.xMin + rad, r.yMin + rad), new Vector2(r.xMax - rad, r.yMin + rad)
        };
        Vector2[] pIn =
        {
            new Vector2(r.xMax - offIn, r.yMax - offIn), new Vector2(r.xMin + offIn, r.yMax - offIn),
            new Vector2(r.xMin + offIn, r.yMin + offIn), new Vector2(r.xMax - offIn, r.yMin + offIn)
        };

        // 四圈頂點：外側透明、實心兩圈、內側透明。硬邊的圓角在關掉 MSAA 的畫面上
        // 會變成一格一格的階梯，而它底下那張九宮格是反鋸齒的 —— 一邊平滑一邊階
        // 梯，看起來就是壞掉。
        float core = Mathf.Max(0f, t * 0.5f - 0.5f);
        int first = vh.currentVertCount;
        int count = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            for (int i = 0; i <= seg; i++)
            {
                float a = (corner * 90f + i * (90f / seg)) * Mathf.Deg2Rad;
                Vector2 n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 outer = pOut[corner] + n * rad;
                Vector2 inner = pIn[corner] + n * radIn;
                Vector2 mid = (outer + inner) * 0.5f;
                Vector2 step = (outer - inner).normalized;
                Color c = Blend(n);
                Color clear = new Color(c.r, c.g, c.b, 0f);
                Push(vh, mid + step * (core + 1f), clear);
                Push(vh, mid + step * core, c);
                Push(vh, mid - step * core, c);
                Push(vh, mid - step * (core + 1f), clear);
                count++;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int a0 = first + i * 4;
            int b0 = first + ((i + 1) % count) * 4;
            for (int k = 0; k < 3; k++)
            {
                vh.AddTriangle(a0 + k, a0 + k + 1, b0 + k + 1);
                vh.AddTriangle(a0 + k, b0 + k + 1, b0 + k);
            }
        }
    }

    /// <summary>照外法線把四邊的顏色混起來。</summary>
    private Color Blend(Vector2 n)
    {
        float top = Mathf.Max(0f, n.y), bottom = Mathf.Max(0f, -n.y);
        float right = Mathf.Max(0f, n.x), left = Mathf.Max(0f, -n.x);
        float sum = top + bottom + right + left;
        if (sum <= 0.0001f) return TopColour;
        return new Color(
            (TopColour.r * top + BottomColour.r * bottom + RightColour.r * right + LeftColour.r * left) / sum,
            (TopColour.g * top + BottomColour.g * bottom + RightColour.g * right + LeftColour.g * left) / sum,
            (TopColour.b * top + BottomColour.b * bottom + RightColour.b * right + LeftColour.b * left) / sum,
            (TopColour.a * top + BottomColour.a * bottom + RightColour.a * right + LeftColour.a * left) / sum);
    }

    private static void Push(VertexHelper vh, Vector2 p, Color c)
    {
        UIVertex v = UIVertex.simpleVert;
        v.color = c;
        v.uv0 = new Vector2(0.5f, 0.5f);
        v.position = new Vector3(p.x, p.y);
        vh.AddVert(v);
    }
}

/// <summary>
/// 一個有厚度的畫框：外緣線腳、凹槽、珠圓、內緣金線，四角加角飾。
/// </summary>
/// <remarks>
/// **一層斜面只能做出「粗邊框」。** 真的畫框是好幾層線腳疊出來的：外緣一道平的
/// 邊、往內一個凹槽（吃光，所以是暗的）、再一圈凸起的珠圓（所以是亮的）、最後貼
/// 著畫的一條金線。每一層的受光方向都不同，疊起來眼睛就讀得到厚度。
///
/// **四邊不一樣亮。** 光從左上來：上緣最亮、左緣次之、右緣和下緣壓暗。四條一樣
/// 亮的話再多層也是平的。
///
/// **四角的角飾**是舊畫框在斜接處蓋的裝飾片，順便遮住斜接線 —— 我們這裡也一樣，
/// 它蓋住四個角落的接縫。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PictureFrameGraphic : MaskableGraphic
{
    private static readonly Color Deep = new Color(0.180f, 0.105f, 0.042f, 1f);
    private static readonly Color Body = new Color(0.390f, 0.245f, 0.105f, 1f);
    private static readonly Color Lit = new Color(0.640f, 0.455f, 0.205f, 1f);
    private static readonly Color Gilt = new Color(0.880f, 0.700f, 0.335f, 1f);
    private static readonly Color Bright = new Color(0.960f, 0.860f, 0.600f, 1f);
    private static readonly Color Mat = new Color(0.930f, 0.890f, 0.790f, 1f);

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float halfW = r.width * 0.5f;
        float halfH = r.height * 0.5f;
        float cx = r.center.x;
        float cy = r.center.y;
        float unit = Mathf.Min(halfW, halfH);

        // 由外往內的線腳。每一層記「從外緣往內多少」和「這一層的顏色」。
        // 上緣／左緣／右緣／下緣各給一組，光從左上來。
        // 每一層佔**總深度**的幾分之幾；總深度由 PracticeRoomScreen 決定，卡紙的
        // 留白也是從同一個數字算出來的。
        float depth = PracticeRoomScreen.MouldingRatio * unit * 2f;
        float[] steps = { 0f, 0.154f, 0.385f, 0.667f, 0.846f, 1f };
        // 每一層、每一邊都有**外緣→內緣**兩個顏色。
        //
        // 一條帶子填單一個顏色，不管分幾層都是一階一階的色塊 —— 立體感來自帶子
        // 內部的明暗變化，那才是圓的線腳在真實光下的樣子。
        //
        // 凹槽那一層的受光方向要**反過來**：凸面是上緣朝光所以亮，凹面的上緣是朝
        // 下的所以暗、下緣朝上所以亮。上一版四邊全給 Deep，結果右緣和下緣的第 0
        // 層和第 2 層連成一整塊死黑 —— 框只有上下有立體感、左右是平的。
        Color[][] shades =
        {
            //       上外    上內    左外    左內    右外    右內    下外    下內
            new[] { Deep,   Body,   Deep,   Body,   Deep,   Deep,   Deep,   Deep   },
            new[] { Lit,    Body,   Body,   Deep,   Body,   Deep,   Body,   Deep   },
            new[] { Deep,   Lit,    Deep,   Body,   Deep,   Body,   Deep,   Body   },
            new[] { Bright, Gilt,   Gilt,   Body,   Body,   Lit,    Body,   Lit    },
            new[] { Gilt,   Gilt,   Gilt,   Gilt,   Lit,    Lit,    Lit,    Lit    }
        };

        for (int layer = 0; layer < shades.Length; layer++)
        {
            float outer = steps[layer] * depth;
            float inner = steps[layer + 1] * depth;
            AddRing(vh, cx, cy, halfW, halfH, outer, inner, shades[layer]);
        }

        // 卡紙
        float matInset = depth;
        AddQuad(vh,
            new Vector2(cx - halfW + matInset, cy - halfH + matInset),
            new Vector2(cx - halfW + matInset, cy + halfH - matInset),
            new Vector2(cx + halfW - matInset, cy + halfH - matInset),
            new Vector2(cx + halfW - matInset, cy - halfH + matInset),
            Mat, Mat, Mat, Mat);

        // 四角的角飾是一張畫好的捲草（PracticeRoomScreen.AddCardOrnament 掛上去
        // 的），不在這裡畫：曲線的裝飾用頂點拼永遠是粗的。
    }

    /// <summary>一圈有厚度的框。sides 是上、左、右、下各兩個顏色（外緣→內緣）。</summary>
    private static void AddRing(VertexHelper vh, float cx, float cy, float halfW, float halfH,
        float outer, float inner, Color[] sides)
    {
        float ow = halfW - outer;
        float oh = halfH - outer;
        float iw = halfW - inner;
        float ih = halfH - inner;

        AddQuad(vh, new Vector2(cx - ow, cy + oh), new Vector2(cx + ow, cy + oh),
            new Vector2(cx + iw, cy + ih), new Vector2(cx - iw, cy + ih),
            sides[0], sides[0], sides[1], sides[1]);
        AddQuad(vh, new Vector2(cx - ow, cy - oh), new Vector2(cx - ow, cy + oh),
            new Vector2(cx - iw, cy + ih), new Vector2(cx - iw, cy - ih),
            sides[2], sides[2], sides[3], sides[3]);
        AddQuad(vh, new Vector2(cx + ow, cy + oh), new Vector2(cx + ow, cy - oh),
            new Vector2(cx + iw, cy - ih), new Vector2(cx + iw, cy + ih),
            sides[4], sides[4], sides[5], sides[5]);
        AddQuad(vh, new Vector2(cx + ow, cy - oh), new Vector2(cx - ow, cy - oh),
            new Vector2(cx - iw, cy - ih), new Vector2(cx + iw, cy - ih),
            sides[6], sides[6], sides[7], sides[7]);
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

    /// <summary>兩側各羽化一像素的線。</summary>
    /// <remarks>
    /// 硬邊的斜線在 UI 上會變成一階一階的梯子，而且 MSAA 是關的（RP asset 裡
    /// m_MSAA: 1），所以沒有人會幫它平滑。BPM 那張圖早就這樣畫了，註解寫著「硬邊
    /// 的階梯比線本身還顯眼」—— 只是這兩份沒抄過去。
    ///
    /// 實心的部分收窄一格、兩側各補一條 alpha 收到 0 的帶子，墨量不變但邊是連續
    /// 的。厚度 1 的線實心寬度變成 0，整條線就是一道兩像素的斜坡。
    /// </remarks>
    private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness,
        Color colour)
    {
        Vector2 d = to - from;
        if (d.sqrMagnitude < 1e-4f) return;
        d.Normalize();
        Vector2 n = new Vector2(-d.y, d.x);
        float core = Mathf.Max(0f, thickness * 0.5f - 0.5f);
        float edge = core + 1f;
        from -= d * 0.5f;                       // 端蓋補半格，接點才不會缺角
        to += d * 0.5f;
        Color clear = new Color(colour.r, colour.g, colour.b, 0f);
        AddQuad(vh, from - n * edge, from - n * core, to - n * core, to - n * edge,
            clear, colour, colour, clear);
        if (core > 0f)
        {
            AddQuad(vh, from - n * core, from + n * core, to + n * core, to - n * core,
                colour, colour, colour, colour);
        }
        AddQuad(vh, from + n * core, from + n * edge, to + n * edge, to + n * core,
            colour, clear, clear, colour);
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
/// 一條量表：凹在紙裡的槽、從左邊長出來的值、幾條門檻的刻線。
/// </summary>
/// <remarks>
/// **為什麼不是兩張 Image 疊起來。** 紙面那張九宮格的 border 是 12，上下相加 24 就
/// 已經超過這條 14px 高的條子 —— 圓角會被擠爛。而門檻更麻煩：刻線要畫在值的**上
/// 面**，用 Image 做就是四個子物件、四份射線目標、四次版面計算。一顆 graphic 一次
/// 畫完，而且值只是一個 float，補間直接餵它。
///
/// 明暗照整個房間的規則走：光從左上來，所以**槽**是上緣暗、下緣亮（凹下去），**值**
/// 反過來上亮下暗（凸出來）。四邊一樣亮的條子是平的，那就是網頁上的進度條。
/// </remarks>
public sealed class RatingGaugeGraphic : MaskableGraphic
{
    public Color Groove = new Color(0.185f, 0.108f, 0.055f, 0.16f);
    public Color FillTop = new Color(0.905f, 0.735f, 0.380f, 1f);
    public Color FillBottom = new Color(0.523f, 0.381f, 0.171f, 1f);
    public Color Mark = new Color(0.185f, 0.108f, 0.055f, 0.55f);

    /// <summary>門檻的位置（0–1）。null 就不畫刻線。</summary>
    public float[] Marks;

    private float value01;

    /// <summary>0–1。改它就重畫，補間直接寫這裡。</summary>
    public float Value01
    {
        get => value01;
        set
        {
            float next = Mathf.Clamp01(value);
            // 補間每幀都會寫進來，而重建網格不便宜 —— 小於半個千分位的變動看不出
            // 來，也不值得重畫一次。
            if (Mathf.Abs(next - value01) < 0.0005f) return;
            value01 = next;
            SetVerticesDirty();
        }
    }

    public override Texture mainTexture => Texture2D.whiteTexture;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 2f || r.height <= 2f) return;

        AddQuad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin),
            Groove, Groove, Groove, Groove);

        // 凹下去的那兩條：上緣的內陰影、下緣受光的細線。
        Color shade = new Color(0.02f, 0.01f, 0.00f, 0.45f);
        Color lit = new Color(1.00f, 0.96f, 0.88f, 0.30f);
        AddQuad(vh, new Vector2(r.xMin, r.yMax - 1.5f), new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMax - 1.5f),
            shade, shade, shade, shade);
        AddQuad(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMin + 1f),
            new Vector2(r.xMax, r.yMin + 1f), new Vector2(r.xMax, r.yMin),
            lit, lit, lit, lit);

        // 值。四周留一圈唇邊，槽才看得到自己是一個槽。
        const float lip = 1.5f;
        float span = r.width - lip * 2f;
        if (value01 > 0.0005f && span > 1f)
        {
            // 一點點也要看得到：0.3% 算出來不到兩個像素，捨掉的話「打過一場」和「完
            // 全沒打」長得一模一樣。
            float width = Mathf.Min(span, Mathf.Max(2f, span * value01));
            float x0 = r.xMin + lip;
            float x1 = x0 + width;
            float y0 = r.yMin + lip;
            float y1 = r.yMax - lip;
            AddQuad(vh, new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1),
                new Vector2(x1, y0), FillBottom, FillTop, FillTop, FillBottom);
        }

        // 門檻畫在值之後：值長過去了刻線還要看得到，不然就不知道自己過了沒有。上下
        // 各露出 2px，那兩截就是「這是刻度，不是條子的一部分」。
        if (Marks == null) return;
        for (int i = 0; i < Marks.Length; i++)
        {
            float x = r.xMin + Mathf.Clamp01(Marks[i]) * r.width;
            float left = Mathf.Clamp(x - 0.75f, r.xMin, r.xMax - 1.5f);
            AddQuad(vh, new Vector2(left, r.yMin - 2f), new Vector2(left, r.yMax + 2f),
                new Vector2(left + 1.5f, r.yMax + 2f), new Vector2(left + 1.5f, r.yMin - 2f),
                Mark, Mark, Mark, Mark);
        }
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
    private string measuredText;
    private TextAlignmentOptions baseAlignment;
    private bool alignmentCaptured;

    public void Bind(TextMeshProUGUI target, float width)
    {
        label = target;
        rect = target != null ? target.rectTransform : null;
        viewportWidth = width;
        measured = false;
        measuredText = null;
        // 原本的對齊只記一次。第二次 Bind 的時候標籤可能已經被下面改成靠左了，
        // 再記一次就永遠回不去置中。
        if (!alignmentCaptured && target != null)
        {
            baseAlignment = target.alignment;
            alignmentCaptured = true;
        }
    }

    private void Update()
    {
        if (label == null || rect == null) return;

        // 字換了就重量。靠外面記得呼叫 Bind 是遲早會漏的 —— 漏掉的那一次，跑馬燈
        // 會照上一首的長度跑（短名亂跑、長名跑不完）。
        if (measured && !string.Equals(measuredText, label.text))
        {
            measured = false;
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
        }

        if (!measured)
        {
            // 第一幀量不到寬度（TMP 還沒排版），所以量到才開始。
            float preferred = label.preferredWidth;
            if (preferred <= 0.01f && !string.IsNullOrEmpty(label.text)) return;
            measured = true;
            measuredText = label.text;
            overflow = Mathf.Max(0f, preferred - viewportWidth);
            timer = 0f;
            // 置中的字一旦超出框，置中本身已經把它往左推了 overflow/2 —— 再跑馬燈
            // 就會從半個字開始、兩頭都被切、最後往左衝過頭。放得下才置中，放不下
            // 就靠左，從頭跑。
            if (alignmentCaptured && baseAlignment == TextAlignmentOptions.Center)
            {
                label.alignment = overflow > 1f ? TextAlignmentOptions.MidlineLeft : baseAlignment;
            }
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
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

    private void OnDisable()
    {
        // 收起來再打開要從頭跑，不然使用者看到的是半截。
        measured = false;
        if (rect != null) rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
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

/// <summary>抽屜的把手：拉開、或按一下。</summary>
internal sealed class DrawerHandle : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    /// <summary>手指晃過這麼多像素才算「拖」，不到就是一次點擊。</summary>
    /// <remarks>
    /// 滑鼠按下去的那一刻幾乎一定會晃個一兩像素，所以「有沒有收到 OnDrag」不能拿
    /// 來判斷是不是拖曳 —— 上一版就是這樣，結果想按一下收起來，卻因為手抖被當成
    /// 拖曳、放手又滑回全開。改成看實際位移。
    /// </remarks>
    private const float DragThreshold = 10f;

    private PracticeRoomScreen owner;
    private Canvas canvas;
    private float travelled;
    private float pending;
    private bool handled;

    public void Bind(PracticeRoomScreen screen) => owner = screen;

    /// <summary>每次按下去都歸零，不是只有進到拖曳流程才歸零。</summary>
    /// <remarks>
    /// 單純的點擊不會觸發 OnBeginDrag（EventSystem 要走超過十像素才算拖曳），所以
    /// 上一版只在 OnBeginDrag 歸零的話，**整個場次拖過一次之後，累計位移就再也回
    /// 不到 0**，OnPointerClick 的 `travelled > 0` 永遠成立，按一下就此失效。
    /// </remarks>
    public void OnPointerDown(PointerEventData eventData)
    {
        travelled = 0f;
        pending = 0f;
        handled = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        travelled = 0f;
        pending = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (owner == null) return;
        // 門檻用**螢幕**像素量（和 EventSystem 同一把尺），位移要換成**畫布**單位
        // 再交出去 —— 兩者在 1080p 以外的解析度差一個 scaleFactor：2560×1440 時游
        // 標走 126px 抽屜就全開了，手還在半路東西已經到底。
        travelled += Mathf.Abs(eventData.delta.y);
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;
        pending += -eventData.delta.y / scale;
        if (travelled < DragThreshold) return;
        // 跨過門檻的那一下要把之前累積的一起補上，不然把手永遠落後游標十個像素。
        owner.DragDrawer(pending);
        pending = 0f;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (owner == null) return;
        if (travelled >= DragThreshold)
        {
            handled = true;
            owner.SettleDrawer();
            return;
        }
        if (handled) return;                    // 剛剛那一下已經被點擊處理掉了
        handled = true;
        owner.ToggleDrawer();
    }

    /// <summary>一次手勢只能切換一次。</summary>
    /// <remarks>
    /// uGUI 的 pointerClick 是在 endDrag **之前**送的，而兩個介面掛在同一個物件
    /// 上時 eligibleForClick 不會被清掉 —— 所以拖完照樣會收到 click。橫向拖一段
    /// （delta.y 全是 0、travelled 還是 0）再放手，會先 ToggleDrawer 一次、再
    /// ToggleDrawer 一次，兩次互相抵消，抽屜完全沒反應。
    /// </remarks>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (owner == null || handled || travelled >= DragThreshold) return;
        handled = true;
        owner.ToggleDrawer();
    }
}

/// <summary>
/// 順滑的清單捲動。
/// </summary>
/// <remarks>
/// ScrollRect 自己的滾輪是**一格一格跳**的：每一個刻度直接位移一段距離，看起來就
/// 是在閃。這裡把滾輪接過來自己記一個目標位置，再每幀指數收斂過去 —— 和選曲轉盤
/// 的手感同一套做法。
///
/// 拖曳仍然交給 ScrollRect（它的慣性和回彈已經夠好），只是每幀確認一次：如果位置
/// 是被它動的（拖曳或慣性），就把目標同步回來，免得兩邊互相拉扯。
/// </remarks>
[RequireComponent(typeof(RectTransform))]
internal sealed class SmoothList : MonoBehaviour, IScrollHandler
{
    private const float NotchPixels = 150f;

    private ScrollRect scroll;
    private float target;
    private float lastApplied;

    public void Bind(ScrollRect target)
    {
        scroll = target;
    }

    /// <summary>把內容捲到某個位置（順滑地）。給鍵盤選曲用。</summary>
    public void ScrollTo(float y)
    {
        if (scroll == null || scroll.content == null) return;
        target = Mathf.Clamp(y, 0f, MaxScroll());
    }

    /// <summary>直接跳過去，不滑。</summary>
    /// <remarks>
    /// 還原上一次的位置要用這個：滑過去的話，一進房間整份清單會從頭捲到你原本在的
    /// 地方，那看起來像畫面自己在動，而不是「你回到原位」。
    /// </remarks>
    public void Jump(float y)
    {
        if (scroll == null || scroll.content == null) return;
        target = Mathf.Clamp(y, 0f, MaxScroll());
        Vector2 at = scroll.content.anchoredPosition;
        at.y = target;
        scroll.content.anchoredPosition = at;
        lastApplied = target;
    }

    /// <summary>目前捲到哪、以及視窗有多高 —— 外面要自己算哪一列露出來了。</summary>
    public float Current => scroll != null && scroll.content != null
        ? scroll.content.anchoredPosition.y : 0f;

    public float ViewportHeight => scroll != null && scroll.viewport != null
        ? scroll.viewport.rect.height : 0f;

    public void OnScroll(PointerEventData eventData)
    {
        if (scroll == null || scroll.content == null) return;
        float notches = Mathf.Abs(eventData.scrollDelta.y) > 10f
            ? eventData.scrollDelta.y / 120f
            : eventData.scrollDelta.y;
        if (Mathf.Abs(notches) < 0.01f) return;

        target = scroll.content.anchoredPosition.y - notches * NotchPixels;
        target = Mathf.Clamp(target, 0f, MaxScroll());
        scroll.velocity = Vector2.zero;
    }

    private float MaxScroll()
    {
        if (scroll == null || scroll.content == null || scroll.viewport == null) return 0f;
        return Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
    }

    private void LateUpdate()
    {
        if (scroll == null || scroll.content == null) return;
        float current = scroll.content.anchoredPosition.y;

        // 位置被別人動過（拖曳、慣性、清單重建）就跟著它走。
        if (Mathf.Abs(current - lastApplied) > 0.5f) target = Mathf.Clamp(current, 0f, MaxScroll());
        if (Mathf.Abs(current - target) < 0.3f) { lastApplied = current; return; }

        float blend = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
        float next = Mathf.Lerp(current, target, blend);
        scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, next);
        lastApplied = next;
    }
}
