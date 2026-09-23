using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.IO;
using Newtonsoft.Json.Linq;

public class SongSelectionManager : MonoBehaviour
{
    public static SongSelectionManager Instance { get; private set; }

    [Serializable]
    private class SongListData
    {
        public List<SongMetadata> songs = new List<SongMetadata>();
    }

    /// <summary>
    /// 一段音訊視窗的所有欄位。巢狀的子視窗用的就是這一型。
    /// </summary>
    /// <remarks>
    /// **為什麼要拆成 base + 子類。** 原本 <see cref="AudioPauseResumeWindow"/> 的
    /// pauseWindows 是 List&lt;AudioPauseResumeWindow&gt;，也就是自己包自己。Unity
    /// 的序列化沒有循環偵測，只能硬性地在第 10 層停手，每次載入都噴一大段
    /// "Serialization depth limit 10 exceeded"。
    ///
    /// 實際的資料只巢狀一層（解析端也只讀一層），所以把「能被巢狀的那一型」和
    /// 「能巢狀別人的那一型」分開，循環就沒了 —— 欄位名一個都沒變，JsonUtility
    /// 讀舊的 metadata.json 照樣讀得到。
    /// </remarks>
    [Serializable]
    public class AudioWindow
    {
        // Legacy numeric fields (legacy JSON may use "pause"/"resume")
        public int pause;
        public int resume;
        // New explicit mute keys (preferred name for legacy mute behavior)
        public int mute;
        public int muteResume;
        // Pauses this audio source only; gameplay time intentionally keeps advancing
        // because the authored chart includes the pause span in its note timestamps.
        public int pausePlayback;
        public int resumePlayback;
        public float originalBPM;
        public float newBPM;
        public bool useMixer = true;
        // Optional fade-out start time (ms). If >0, volume will fade to 0 starting at this time.
        public int fade;
        // Optional fade duration (ms). If <=0, defaults will be applied in GameManager.
        public int fadeDurationMs;
        // Optional DSP-time (ms) for speed change; if >0, apply newBPM/originalBPM at that time.
        public int audiochangeTime;
        // Optional DSP-time (ms) to return to original tempo (factor 1.0)
        public int atempo;
        // Optional skip sections attached to a resource window
        public List<SkipSection> skipSections;
        // Runtime hints set by the parser (not serialized)
        [NonSerialized] public bool isMuteWindow = false;
        [NonSerialized] public bool isPauseWindow = false;
    }

    [Serializable]
    public class AudioPauseResumeWindow : AudioWindow
    {
        // Optional nested windows for convenience (allow multiple pause/mute
        // windows per resource). 一層就夠 —— 型別本身擋住了第二層。
        public List<AudioWindow> pauseWindows;
    }

    [Serializable]
    public class AudioSpeedChange
    {
        public float originalBPM;
        public float newBPM;
                // 若有其他 UI panel 也要隱藏，請在這裡加上 SetActive(false)
                // 例如: if (settingsPanel != null) settingsPanel.SetActive(false);
        public bool useMixer = true;
        public int audiochangeTime;

        public float GetFactor()
        {
            if (originalBPM <= 0f || newBPM <= 0f) return 1f;
            return newBPM / originalBPM;
        }
    }

    [Serializable]
    private class AudioManageEntry
    {
        public List<AudioPauseResumeWindow> audioResource = new List<AudioPauseResumeWindow>();
        public List<AudioPauseResumeWindow> pianoAudioResource = new List<AudioPauseResumeWindow>();
        public List<AudioSpeedChange> audioSpeed = new List<AudioSpeedChange>();
    }

    [Serializable]
    private class SongDifficulty
    {
        public string difficultyName;
        public int difficultyLevel;
        public string chartFileName;
        public string audioResourcePath;
        public string pianoAudioResourcePath;
        public string coverResourcePath;
        public string videoPath;
        // optional: how many seconds before the audio start the video should begin (can be used to align visuals)
                // 若有其他 UI panel 也要隱藏，請在這裡加上 SetActive(false)
                // 例如: if (settingsPanel != null) settingsPanel.SetActive(false);
        public float videostartTime;
        public List<AudioManageEntry> audioManage;
        // fallback when JSON uses a single object instead of an array
        public AudioManageEntry audioManageSingle;
        /// <summary>True when the chart ships no recording and only the
        /// player's own keysounds should sound.</summary>
        public bool noBackgroundMusic;
    }

    [Serializable]
    private class SongMetadata
    {
        // Top-level display fields
        public string displayName;
        public string author;

        // New format: array of difficulties
        public List<SongDifficulty> difficulties;

        // For backward compatibility when discovering single-file meta JSONs,
        // we may encounter old-style metadata with chartFileName at top-level.
        // We'll handle that in discovery code.
        public string chartFileName;
        public string audioResourcePath;
        public string pianoAudioResourcePath;
        public string coverResourcePath;
        public string difficultyName;
        public int difficultyLevel;
        public List<AudioManageEntry> audioManage;
        // fallback when JSON uses a single object instead of an array
        public AudioManageEntry audioManageSingle;
        /// <summary>True when the chart ships no recording and only the
        /// player's own keysounds should sound.</summary>
        public bool noBackgroundMusic;
        [NonSerialized] public string category;
        [NonSerialized] public string externalId;
    }

    public class SongOption
    {
        public string externalId;
        public string category;
        public string displayName;
        public string chartFileName;
        public string audioResourcePath;
        public string pianoAudioResourcePath;
        public AudioClip audioClip;
        public AudioClip pianoAudioClip;
        /// <summary>
        /// What the song select screen plays instead of a silent gameplay track.
        /// Null for every ordinary song. See EnsurePreviewOverrideClip.
        /// </summary>
        public AudioClip previewOverrideClip;
        public bool previewOverrideChecked;
        public Button selectButton;
        public TextMeshProUGUI label;
        public string coverResourcePath;
        public string difficultyName;
        public int difficultyLevel;
        public string author;
        public Sprite coverSprite;
        public string videoPath; // 新增 videoPath 屬性
        public float videoStartTimeSec; // optional offset (秒)
        // All difficulty variants for this song (primary is this option)
        public List<SongOption> difficultyVariants;
        // If the player explicitly chose one variant for this group, store it here
        public SongOption selectedVariant;
        public List<AudioPauseResumeWindow> audioManageMain;
        public List<AudioPauseResumeWindow> audioManagePiano;
        public float audioSpeedFactor = 1f;
        public bool useMixer = false;
        public List<AudioSpeedEvent> audioSpeedEvents;
        public float pianoAudioSpeedFactor = 1f;
        public bool pianoUseMixer = false;
        public List<AudioSpeedEvent> pianoAudioSpeedEvents;
        /// <summary>True when the chart ships no recording and only the
        /// player's own keysounds should sound.</summary>
        public bool noBackgroundMusic;
        // Optional skip sections parsed from audio/piano resource entries
        public List<SkipSection> skipSections;
    }

    [Serializable]
    public class SkipSection
    {
        public int startMs;
        public int endMs;
    }

    [Serializable]
    public class AudioSpeedEvent
    {
        public int audiochangeTimeMs;
        public float factor = 1f;
        public bool useMixer = true;
    }

    // 當選擇項目改變時會觸發（可供 UI/背景管理訂閱）
    public event Action<SongOption> OnSelectionChanged;

    [Serializable]
    private class SongPreviewSlot
    {
        public RectTransform root;
        public Image coverImage;
        public TextMeshProUGUI titleLabel;
        public TextMeshProUGUI difficultyLabel;
        public TextMeshProUGUI authorLabel;
        public Button selectButton;
    }

    [Header("UI References")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private TextMeshProUGUI currentSongLabel;
    [SerializeField] private TextMeshProUGUI authorLabel;
    [SerializeField] private RectTransform songButtonContainer;
    [SerializeField] private Button songButtonPrefab;

    [Header("Carousel Layout")]
    [SerializeField] private SongPreviewSlot centerSlot;
    [SerializeField] private SongPreviewSlot leftSlot;
    [SerializeField] private SongPreviewSlot rightSlot;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private float centerScale = 1.0f;
    [SerializeField] private float sideScale = 0.8f;

    [Header("Category Carousel")]
    [SerializeField] private RectTransform categoryCarouselRoot;
    [SerializeField] private TextMeshProUGUI categoryCarouselLabel;
    [SerializeField] private Button previousCategoryButton;
    [SerializeField] private Button nextCategoryButton;

    [Header("Data Settings")]
    [SerializeField] private string songListResourcePath = "song_list";

    [Header("Auto Discovery (Resources)")]
    [Tooltip("啟動時從 Resources/songs 掃描 register.json 並合併到歌曲列表。")]
    [SerializeField] private bool autoDiscoverOnStart = true;
    [Tooltip("Resources 下自動發現歌曲的根目錄。如 'songs' => Assets/Resources/songs")]
    [SerializeField] private string autoDiscoverResourcesRoot = "songs";

    [Header("Audio Preview")]
    [SerializeField] private AudioSource previewAudioSource;
    [SerializeField] private AudioSource previewPianoAudioSource;

    private readonly List<SongOption> songOptions = new List<SongOption>();
    private readonly List<SongOption> allSongOptions = new List<SongOption>();
    private readonly List<string> songCategories = new List<string>();
    private readonly List<string> discoveredCategoryOrder = new List<string>();
    private int currentCategoryIndex;
    private bool pointerOverCategoryCarousel;
    // Runtime-created difficulty selector panel instance
    private GameObject difficultySelectorInstance = null;
    private RectTransform recitalToggleRoot;
    private Image recitalToggleFace;
    private Outline recitalToggleEdge;
    private TextMeshProUGUI recitalToggleLabel;
    private RectTransform inlineDifficultyListRoot;
    private int currentIndex = 0;
    // Temporary override used only while the settings page is running a real
    // gameplay preview. It never changes currentIndex or selectedVariant.
    private SongOption settingsGameplayPreviewOption;
    private bool settingsGameplayPreviewRunning;
    private Coroutine settingsGameplayPreviewLoop;
    public bool IsSettingsGameplayPreviewRunning => settingsGameplayPreviewRunning;

    /// <summary>
    /// 選曲畫面開著、而且沒有正在切歌或啟動難度——這時候重整曲庫不會打斷任何東西。
    /// 製譜器改了曲庫時 EditorLibraryWatcher 用它判斷能不能馬上重整。
    /// </summary>
    public bool CanRefreshLibraryNow =>
        selectionPanel != null && selectionPanel.activeInHierarchy &&
        !difficultyLaunchInProgress && !carouselTransitioning && !settingsGameplayPreviewRunning;
    private bool carouselInitialized = false;
    private bool carouselTransitioning;
    private bool difficultyLaunchInProgress;
    private Coroutine carouselTransitionRoutine;
    private Coroutine carouselAudioLoadRoutine;
    private int carouselAudioLoadVersion;
    private AudioClip carouselLoadingMainClip;
    private AudioClip carouselLoadingPianoClip;
    private Vector2 centerBookRestPosition;
    private Vector2 leftBookRestPosition;
    private Vector2 rightBookRestPosition;
    private bool bookRestPoseCached;
    // Audio clip cache to avoid runtime hiccups
    private readonly Dictionary<string, AudioClip> _audioCache = new Dictionary<string, AudioClip>();
    private readonly HashSet<string> _missingAudioWarnings = new HashSet<string>();
    // Cache for runtime-created sprites from Texture2D (keyed by resource path)
    private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
    // ---- Continuous carousel motion ---------------------------------------
    // The carousel is driven by a single floating point position expressed in
    // "songs".  A card with role r is drawn (r - frac) song-widths from the
    // centre, so scrolling never has to wait for an animation to finish: input
    // only moves the target, and the position glides towards it.
    [Header("Carousel Feel")]
    [Tooltip("Seconds for the snap spring to reach the target song (smaller = snappier).")]
    [SerializeField] private float carouselSnapSmoothTime = 0.11f;
    [Tooltip("Upper bound on scroll speed, in songs per second.")]
    [SerializeField] private float carouselMaxSpeed = 16f;
    [Tooltip("How quickly a flick loses speed. Larger = shorter coast.")]
    [SerializeField] private float carouselFlickDamping = 7f;
    [Tooltip("Quiet time after the motion stops before the preview audio loads.")]
    [SerializeField] private float carouselSettleDelay = 0.12f;
    [Tooltip("How many songs the scroll target may run ahead of the visible card.")]
    [SerializeField] private float carouselMaxQueuedSteps = 5f;
    [Tooltip("Give every card the same size and styling. Required for seamless scrolling; "
        + "turn off to go back to a distinctly styled centre card.")]
    [SerializeField] private bool uniformCardStyling = true;

    private class CarouselCard
    {
        public SongPreviewSlot slot;
        public int role;
        public CanvasGroup group;
    }

    private readonly CarouselScroller songScroller = new CarouselScroller();
    private readonly List<CarouselCard> carouselCards = new List<CarouselCard>();
    private int carouselDisplayedBase = int.MinValue;  // base index the cards currently show
    private bool carouselMoving;                       // motion prepared, focus not yet committed
    private float carouselSlotSpacing = 800f;
    private bool carouselDragActive;
    private bool carouselDragMoved;
    private float carouselDragStartPointerX;
    private float carouselClickSuppressedUntil;
    private bool carouselPointerWasPressed;
    private Canvas carouselCanvas;

    [Header("Grouping")]
    [Tooltip("In level grouping, every level above this shares one \"N+\" card.")]
    [SerializeField] private int levelBucketCap = 16;

    private readonly ChartAnalysisPanel analysisPanel = new ChartAnalysisPanel();
    private Coroutine analysisRoutine;
    private SongSelectionAtmosphere selectionAtmosphere;

    private const string GroupingModePrefsKey = "song_grouping_mode";
    private const string UnknownLevelCategory = "?";
    private SongGroupingMode groupingMode = SongGroupingMode.Genre;
    private readonly SongGroupingBar groupingBar = new SongGroupingBar();
    private SongOption currentPreviewOption = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        SettingsManager.LanguageChanged += HandleLanguageChanged;
        SongDifficultyStrip.PreferenceChanged += RestoreSelectionAtmosphereDifficulty;
        BuildLogger.Log("SongSelectionManager: Awake - instance assigned.");

        EnsurePreviewSources();
    }

    private void OnDestroy()
    {
        SettingsManager.LanguageChanged -= HandleLanguageChanged;
        SongDifficultyStrip.PreferenceChanged -= RestoreSelectionAtmosphereDifficulty;
        if (Instance == this) Instance = null;
    }

    private void HandleLanguageChanged(AppLanguage _)
    {
        RefreshLocalization();
    }

    public void RefreshLocalization()
    {
        groupingBar.RefreshLocalization();
        UpdateCategoryCarouselLabel();
        UpdateCarouselVisuals(false);
    }

    private void Start()
    {
        if (centerSlot != null && centerSlot.root == null && songButtonContainer != null)
        {
            centerSlot.root = songButtonContainer;
        }

        groupingMode = (SongGroupingMode)Mathf.Clamp(
            PlayerPrefs.GetInt(GroupingModePrefsKey, (int)SongGroupingMode.Genre), 0, 1);
        ApplyCarouselTuning();

        ApplyClassicalBookTheme();
        selectionAtmosphere = SongSelectionAtmosphere.Attach(selectionPanel);
        RefreshRecitalToggle();

        LoadSongOptions();
        EnsureCategoryCarousel();
        PushCategoriesToBar();
        UpdateCategoryCarouselLabel();
        BuildLogger.Log($"SongSelectionManager: Start - loaded {songOptions.Count} options.");
        ConfigureCarouselControls();
        EnsureCarouselCards();
        UpdateCarouselVisuals();
        ShowSelection();
        RuntimeSongLibraryPanel.Attach(this, selectionPanel);
    }

    private void ApplyClassicalBookTheme()
    {
        ClassicalBookUITheme.EnsureBookBackdrop(selectionPanel, "NOSTALGIA  SCORE  LIBRARY", true);
        ClassicalBookUITheme.ConfigureFocusCarousel(centerSlot?.root, leftSlot?.root, rightSlot?.root, true);
        if (uniformCardStyling && centerSlot?.root != null)
        {
            // A continuous carousel hands a card its next song at the midpoint
            // between two slots.  That handover is only invisible when the two
            // cards are identical, so every card gets the centre card's size and
            // styling and focus is carried purely by scale, height and fade.
            Vector2 cardSize = centerSlot.root.sizeDelta;
            if (leftSlot?.root != null) leftSlot.root.sizeDelta = cardSize;
            if (rightSlot?.root != null) rightSlot.root.sizeDelta = cardSize;
        }
        if (centerSlot != null)
            ClassicalBookUITheme.StyleCard(centerSlot.root, centerSlot.coverImage, centerSlot.titleLabel,
                centerSlot.difficultyLabel, centerSlot.authorLabel, true);
        if (leftSlot != null)
            ClassicalBookUITheme.StyleCard(leftSlot.root, leftSlot.coverImage, leftSlot.titleLabel,
                leftSlot.difficultyLabel, leftSlot.authorLabel, uniformCardStyling);
        if (rightSlot != null)
            ClassicalBookUITheme.StyleCard(rightSlot.root, rightSlot.coverImage, rightSlot.titleLabel,
                rightSlot.difficultyLabel, rightSlot.authorLabel, uniformCardStyling);
        ClassicalBookUITheme.StyleButton(previousButton);
        ClassicalBookUITheme.StyleButton(nextButton);
        EnsureInlineDifficultyList();
        CacheBookRestPositions();
    }

    private void RebuildCategoriesAndVisibleSongs()
    {
        string previousCategory = songCategories.Count > 0 && currentCategoryIndex >= 0 &&
                                  currentCategoryIndex < songCategories.Count
            ? songCategories[currentCategoryIndex]
            : null;

        songCategories.Clear();
        if (groupingMode == SongGroupingMode.Level) BuildLevelCategories();
        else BuildGenreCategories();

        int restoredIndex = !string.IsNullOrWhiteSpace(previousCategory)
            ? songCategories.FindIndex(item => string.Equals(item, previousCategory, StringComparison.OrdinalIgnoreCase))
            : -1;
        currentCategoryIndex = restoredIndex >= 0 ? restoredIndex : 0;
        ApplyCurrentCategoryFilter();
        PushCategoriesToBar();
    }

    private void BuildGenreCategories()
    {
        songCategories.Add(ExternalSongLibrary.AllCategory);
        for (int i = 0; i < discoveredCategoryOrder.Count; i++)
        {
            string category = discoveredCategoryOrder[i];
            if (!string.IsNullOrWhiteSpace(category) &&
                allSongOptions.Exists(option => option != null &&
                    string.Equals(option.category, category, StringComparison.OrdinalIgnoreCase)) &&
                !songCategories.Exists(item =>
                    string.Equals(item, category, StringComparison.OrdinalIgnoreCase)))
                songCategories.Add(category);
        }
        for (int i = 0; i < allSongOptions.Count; i++)
        {
            string category = string.IsNullOrWhiteSpace(allSongOptions[i]?.category)
                ? "UNCATEGORIZED"
                : allSongOptions[i].category;
            if (!songCategories.Exists(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase)))
                songCategories.Add(category);
        }
    }

    /// <summary>
    /// One card per difficulty level that actually exists, hardest first, with
    /// everything above the cap folded into a single "16+" card.
    /// </summary>
    private void BuildLevelCategories()
    {
        songCategories.Add(ExternalSongLibrary.AllCategory);

        var levels = new List<int>();
        bool hasAboveCap = false;
        bool hasUnknown = false;
        for (int i = 0; i < allSongOptions.Count; i++)
        {
            List<SongOption> variants = VariantsOf(allSongOptions[i]);
            if (variants == null) continue;
            for (int v = 0; v < variants.Count; v++)
            {
                int level = variants[v] != null ? variants[v].difficultyLevel : 0;
                if (level <= 0) hasUnknown = true;
                else if (level > levelBucketCap) hasAboveCap = true;
                else if (!levels.Contains(level)) levels.Add(level);
            }
        }

        if (hasAboveCap) songCategories.Add(levelBucketCap.ToString(InvariantCulture) + "+");
        levels.Sort();
        for (int i = levels.Count - 1; i >= 0; i--)
            songCategories.Add(levels[i].ToString(InvariantCulture));
        if (hasUnknown) songCategories.Add(UnknownLevelCategory);
    }

    private static System.Globalization.CultureInfo InvariantCulture =>
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>The level card a single chart belongs to.</summary>
    private string GetLevelBucket(SongOption variant)
    {
        int level = variant != null ? variant.difficultyLevel : 0;
        if (level <= 0) return UnknownLevelCategory;
        return level > levelBucketCap
            ? levelBucketCap.ToString(InvariantCulture) + "+"
            : level.ToString(InvariantCulture);
    }

    /// <summary>The charts of one song, whether or not the group was ever split.</summary>
    /// <summary>教學曲（資料夾裡有 tutorial.json）。不進 ALL，演奏會開關由課程決定。</summary>
    private static bool IsTutorialOption(SongOption option, out TutorialSession.RecitalRule rule)
    {
        rule = TutorialSession.RecitalRule.None;
        List<SongOption> variants = VariantsOf(option);
        if (variants == null) return false;
        for (int i = 0; i < variants.Count; i++)
        {
            if (variants[i] != null && TutorialSession.TryDescribe(variants[i].chartFileName, out rule))
                return true;
        }
        return false;
    }

    private static bool IsTutorialOption(SongOption option)
    {
        return IsTutorialOption(option, out _);
    }

    /// <summary>這一首是不是新手教學的課程。</summary>
    /// <remarks>
    /// 練習室要把教學課程從一般曲目裡分出來（它們是課，不是曲子），判斷的依據必須
    /// 和選曲畫面一致 —— 那裡靠的是 TutorialSession 認得出這份譜，不是看分類名。
    /// </remarks>
    public static bool IsTutorialSong(SongOption option) => IsTutorialOption(option);

    private static List<SongOption> VariantsOf(SongOption group)
    {
        if (group == null) return null;
        if (group.difficultyVariants != null && group.difficultyVariants.Count > 0)
            return group.difficultyVariants;
        return new List<SongOption> { group };
    }

    private static string GetSongIdentityKey(SongOption option)
    {
        return !string.IsNullOrWhiteSpace(option?.externalId)
            ? "external:" + option.externalId
            : "builtin:" + (option?.displayName ?? option?.chartFileName ?? string.Empty);
    }

    private void ApplyCurrentCategoryFilter()
    {
        songOptions.Clear();
        if (songCategories.Count == 0) return;
        currentCategoryIndex = ((currentCategoryIndex % songCategories.Count) + songCategories.Count) % songCategories.Count;
        string category = songCategories[currentCategoryIndex];
        if (groupingMode == SongGroupingMode.Level) ApplyLevelFilter(category);
        else ApplyGenreFilter(category);
        currentIndex = 0;
    }

    private void ApplyGenreFilter(string category)
    {
        bool showAll = string.Equals(category, ExternalSongLibrary.AllCategory,
            StringComparison.OrdinalIgnoreCase);
        HashSet<string> allKeys = showAll
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : null;
        for (int i = 0; i < allSongOptions.Count; i++)
        {
            SongOption option = allSongOptions[i];
            string optionCategory = string.IsNullOrWhiteSpace(option?.category) ? "UNCATEGORIZED" : option.category;
            if (showAll)
            {
                // 教學曲只在自己的分類裡：它不是一首拿來打分數的歌。
                if (IsTutorialOption(option)) continue;
                if (allKeys.Add(GetSongIdentityKey(option))) songOptions.Add(option);
            }
            else if (string.Equals(optionCategory, category, StringComparison.OrdinalIgnoreCase))
                songOptions.Add(option);
        }
    }

    /// <summary>
    /// Level grouping ignores collections, so a song filed under two of them
    /// must appear once.
    /// </summary>
    /// <remarks>
    /// 等級只決定「哪些歌出現」：有任何一個難度落在這個等級就列出來，卡片本身
    /// 還是曲庫裡那一張，所有難度照樣都在。以前會把其他難度從書籤裡拿掉，挑
    /// 了 12 級進去就沒辦法順手換成同一首的 14 級。
    /// </remarks>
    private void ApplyLevelFilter(string bucket)
    {
        bool showAll = string.Equals(bucket, ExternalSongLibrary.AllCategory,
            StringComparison.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < allSongOptions.Count; i++)
        {
            SongOption group = allSongOptions[i];
            List<SongOption> variants = VariantsOf(group);
            if (variants == null || variants.Count == 0) continue;

            if (showAll && IsTutorialOption(group)) continue;
            bool matches = showAll;
            for (int v = 0; !matches && v < variants.Count; v++)
            {
                SongOption variant = variants[v];
                matches = variant != null && string.Equals(GetLevelBucket(variant), bucket,
                    StringComparison.OrdinalIgnoreCase);
            }
            if (!matches) continue;
            if (!seenKeys.Add(GetSongIdentityKey(group))) continue;

            songOptions.Add(group);
        }
    }

    private void EnsureCategoryCarousel()
    {
        if (selectionPanel == null) return;
        if (categoryCarouselRoot == null)
        {
            GameObject rootObject = new GameObject("CategoryCarousel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(EventTrigger));
            rootObject.layer = selectionPanel.layer;
            categoryCarouselRoot = rootObject.GetComponent<RectTransform>();
            categoryCarouselRoot.SetParent(selectionPanel.transform, false);
            categoryCarouselRoot.anchorMin = new Vector2(0.30f, 1f);
            categoryCarouselRoot.anchorMax = new Vector2(0.70f, 1f);
            categoryCarouselRoot.pivot = new Vector2(0.5f, 1f);
            categoryCarouselRoot.anchoredPosition = new Vector2(0f, -72f);
            categoryCarouselRoot.sizeDelta = new Vector2(0f, 58f);

            Image background = rootObject.GetComponent<Image>();
            background.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
            background.type = Image.Type.Tiled;
            background.color = new Color(0.16f, 0.07f, 0.035f, 0.96f);
            Outline outline = rootObject.GetComponent<Outline>();
            outline.effectColor = ClassicalBookUITheme.Gold;
            outline.effectDistance = new Vector2(2f, -2f);

            previousCategoryButton = CreateCategoryButton(categoryCarouselRoot, "PreviousCategory", "<", true);
            nextCategoryButton = CreateCategoryButton(categoryCarouselRoot, "NextCategory", ">", false);

            GameObject labelObject = new GameObject("CategoryLabel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = rootObject.layer;
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(categoryCarouselRoot, false);
            labelRect.anchorMin = new Vector2(0.14f, 0f);
            labelRect.anchorMax = new Vector2(0.86f, 1f);
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);
            categoryCarouselLabel = labelObject.GetComponent<TextMeshProUGUI>();
            categoryCarouselLabel.alignment = TextAlignmentOptions.Center;
            categoryCarouselLabel.enableAutoSizing = true;
            categoryCarouselLabel.fontSizeMin = 17f;
            categoryCarouselLabel.fontSizeMax = 27f;
            categoryCarouselLabel.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            categoryCarouselLabel.color = ClassicalBookUITheme.ParchmentLight;
            categoryCarouselLabel.raycastTarget = false;
        }

        EnsureRecitalToggle();
        categoryCarouselRoot.SetAsLastSibling();
        if (recitalToggleRoot != null) recitalToggleRoot.SetAsLastSibling();
        if (previousCategoryButton != null)
        {
            previousCategoryButton.onClick.RemoveListener(ShowPreviousCategory);
            previousCategoryButton.onClick.AddListener(ShowPreviousCategory);
        }
        if (nextCategoryButton != null)
        {
            nextCategoryButton.onClick.RemoveListener(ShowNextCategory);
            nextCategoryButton.onClick.AddListener(ShowNextCategory);
        }

        // The sliding strip reads the wheel and hover state itself in Update,
        // so the frame no longer needs event triggers of its own.
        EventTrigger legacyTrigger = categoryCarouselRoot.GetComponent<EventTrigger>();
        if (legacyTrigger != null) legacyTrigger.triggers = new List<EventTrigger.Entry>();

        if (!groupingBar.IsBuilt)
        {
            groupingBar.Build(categoryCarouselRoot, categoryCarouselLabel, groupingMode);
            groupingBar.CategoryCommitted += OnCategoryCommitted;
            groupingBar.ModeChanged += OnGroupingModeChanged;
        }
    }

    /// <summary>
    /// The recital-mode switch, sitting immediately right of the category strip.
    /// </summary>
    /// <remarks>
    /// **Why it is not in the settings menu any more.** It is not a preference
    /// like a volume or an offset -- it is which of two ways you are about to
    /// play, and you decide that while looking at the songs, not three screens
    /// away. Next to the category strip it is on the one bar that is already
    /// about "what am I looking at".
    ///
    /// **Why it changes the room immediately.** Pressing it swaps the library's
    /// backdrop for a rehearsal room, and the hall is what waits on the other
    /// side when the song starts. That is the whole feedback: no dialog, no
    /// caption -- you prepare in one room and perform in the other.
    /// </remarks>
    private void EnsureRecitalToggle()
    {
        if (selectionPanel == null || categoryCarouselRoot == null) return;
        if (recitalToggleRoot == null)
        {
            GameObject rootObject = new GameObject("RecitalToggle", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            rootObject.layer = selectionPanel.layer;
            recitalToggleRoot = rootObject.GetComponent<RectTransform>();
            recitalToggleRoot.SetParent(selectionPanel.transform, false);
            // 貼著分類條的右緣，同一個高度、同一條基線。
            recitalToggleRoot.anchorMin = new Vector2(0.705f, 1f);
            recitalToggleRoot.anchorMax = new Vector2(0.815f, 1f);
            recitalToggleRoot.pivot = new Vector2(0.5f, 1f);
            recitalToggleRoot.anchoredPosition = new Vector2(0f, -72f);
            recitalToggleRoot.sizeDelta = new Vector2(0f, 58f);

            recitalToggleFace = rootObject.GetComponent<Image>();
            recitalToggleFace.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
            recitalToggleFace.type = Image.Type.Tiled;
            recitalToggleEdge = rootObject.GetComponent<Outline>();
            recitalToggleEdge.effectDistance = new Vector2(2f, -2f);

            GameObject labelObject = new GameObject("RecitalLabel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = rootObject.layer;
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(recitalToggleRoot, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);
            recitalToggleLabel = labelObject.GetComponent<TextMeshProUGUI>();
            recitalToggleLabel.alignment = TextAlignmentOptions.Center;
            recitalToggleLabel.enableAutoSizing = true;
            recitalToggleLabel.fontSizeMin = 13f;
            recitalToggleLabel.fontSizeMax = 21f;
            recitalToggleLabel.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            recitalToggleLabel.raycastTarget = false;

            Button button = rootObject.GetComponent<Button>();
            button.targetGraphic = recitalToggleFace;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(ToggleRecitalMode);
            // 教學曲會把開關鎖成課程規定的樣子，所以換卡片時要重畫。
            OnSelectionChanged += _ => RefreshRecitalToggle();
        }

        RefreshRecitalToggle();
    }

    /// <summary>
    /// 模式開關：演奏會 ↔ 練習。它同時也是練習室的入口。
    /// </summary>
    /// <remarks>
    /// 兩個模式各自是一個空間，不是一個勾選框 —— 轉到練習就該進練習室（那裡才有
    /// 速度、看譜面、難點這些東西），轉回演奏會就回到這張選曲轉盤。所以入口不另外
    /// 做一顆按鈕，就是這一顆。
    /// </remarks>
    private void ToggleRecitalMode()
    {
        SettingsManager settings = SettingsManager.Instance;
        if (settings == null) return;
        // 教學曲的模式由課程決定，開關鎖住。
        if (IsTutorialOption(GetSelectedSong())) return;
        bool recital = !settings.RecitalMode;
        settings.SetRecitalMode(recital);
        RefreshRecitalToggle();

        if (recital)
        {
            if (PracticeRoomScreen.Instance != null) PracticeRoomScreen.Instance.Close();
        }
        else
        {
            PracticeRoomScreen.Open();
        }
    }

    /// <summary>Puts the switch, and the room behind it, in step with the setting.</summary>
    private void RefreshRecitalToggle()
    {
        SettingsManager settings = SettingsManager.Instance;
        bool on = settings != null && settings.RecitalMode;
        bool locked = IsTutorialOption(GetSelectedSong(), out TutorialSession.RecitalRule rule);
        if (locked) on = rule == TutorialSession.RecitalRule.Always;

        if (recitalToggleFace != null)
            recitalToggleFace.color = on
                ? new Color(0.28f, 0.14f, 0.055f, 0.98f)
                : new Color(0.16f, 0.07f, 0.035f, 0.96f);
        if (recitalToggleEdge != null)
            recitalToggleEdge.effectColor = on
                ? ClassicalBookUITheme.Gold
                : new Color(ClassicalBookUITheme.Gold.r * 0.45f, ClassicalBookUITheme.Gold.g * 0.45f,
                    ClassicalBookUITheme.Gold.b * 0.45f, 1f);
        if (recitalToggleLabel != null)
        {
            recitalToggleLabel.text = !locked
                ? Localize.T("演奏會模式", "演奏会模式", "RECITAL")
                : rule == TutorialSession.RecitalRule.Always
                    ? Localize.T("演奏會（課程）", "演奏会（课程）", "RECITAL (LESSON)")
                    : rule == TutorialSession.RecitalRule.Partly
                        ? Localize.T("依課程切換", "依课程切换", "BY LESSON")
                        : Localize.T("一般（課程）", "一般（课程）", "STANDARD (LESSON)");
            recitalToggleLabel.color = on
                ? new Color(1f, 0.88f, 0.62f, 1f)
                : new Color(0.62f, 0.55f, 0.45f, 1f);
            // 鎖住的時候淡一點，看得出來按不動。
            if (locked) recitalToggleLabel.color *= new Color(1f, 1f, 1f, 0.7f);
            ClassicalBookUITheme.ApplyLocalizedFont(recitalToggleLabel);
        }

        if (selectionAtmosphere != null) selectionAtmosphere.SetRecitalRoom(on);
    }

    private Button CreateCategoryButton(RectTransform parent, string objectName, string caption, bool left)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = parent.gameObject.layer;
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(left ? 0f : 0.86f, 0f);
        rect.anchorMax = new Vector2(left ? 0.14f : 1f, 1f);
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, -4f);

        GameObject textObject = new GameObject("Label", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = buttonObject.layer;
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = caption;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 24f;
        label.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        ClassicalBookUITheme.StyleButton(button);
        return button;
    }

    public void ShowPreviousCategory() => ChangeCategory(-1);
    public void ShowNextCategory() => ChangeCategory(1);

    private void ChangeCategory(int direction)
    {
        if (songCategories.Count <= 1 || difficultyLaunchInProgress) return;

        if (groupingBar.IsBuilt)
        {
            // The strip slides now and reports back once it has settled.
            groupingBar.Step(direction);
            return;
        }

        CancelCarouselAudioLoad();
        StopPreviewAudio();
        currentCategoryIndex = (currentCategoryIndex + (direction >= 0 ? 1 : -1) + songCategories.Count) % songCategories.Count;
        ApplyCurrentCategoryFilter();
        UpdateCategoryCarouselLabel();
        UpdateCarouselVisuals(false);
        BeginCarouselAudioLoadAfterAnimation();
    }

    private void PushCategoriesToBar()
    {
        if (!groupingBar.IsBuilt) return;
        groupingBar.SetEntries(songCategories, currentCategoryIndex);
    }

    /// <summary>
    /// Swapping collections reloads the whole song list, so it waits until the
    /// category strip has actually come to rest on a different card.
    /// </summary>
    private void OnCategoryCommitted(int index)
    {
        if (difficultyLaunchInProgress || songCategories.Count == 0) return;
        index = Mathf.Clamp(index, 0, songCategories.Count - 1);
        if (index == currentCategoryIndex) return;

        CancelCarouselAudioLoad();
        StopPreviewAudio();
        currentCategoryIndex = index;
        ApplyCurrentCategoryFilter();
        UpdateCarouselVisuals();
    }

    private void OnGroupingModeChanged(SongGroupingMode mode)
    {
        if (groupingMode == mode) return;
        groupingMode = mode;
        PlayerPrefs.SetInt(GroupingModePrefsKey, (int)mode);
        PlayerPrefs.Save();

        CancelCarouselAudioLoad();
        StopPreviewAudio();
        // The old category name means nothing in the new mode; start at ALL.
        currentCategoryIndex = 0;
        RebuildCategoriesAndVisibleSongs();
        currentCategoryIndex = 0;
        ApplyCurrentCategoryFilter();
        PushCategoriesToBar();
        UpdateCarouselVisuals();
    }

    private void UpdateCategoryCarouselLabel()
    {
        int songCount = songOptions != null ? songOptions.Count : 0;
        int songNumber = songCount > 0 ? Mathf.Clamp(currentIndex, 0, songCount - 1) + 1 : 0;
        bool multiple = songCategories.Count > 1;
        if (previousCategoryButton != null) previousCategoryButton.interactable = multiple;
        if (nextCategoryButton != null) nextCategoryButton.interactable = multiple;

        if (groupingBar.IsBuilt)
        {
            // The card captions carry the category; the frame only shows where
            // in that collection the player currently is.
            groupingBar.SetSongPosition(songNumber, songCount);
            return;
        }

        if (categoryCarouselLabel == null) return;
        if (songCategories.Count == 0)
        {
            categoryCarouselLabel.text = Localize.T("沒有曲目分類", "没有曲目分类", "NO COLLECTIONS");
            ClassicalBookUITheme.ApplyLocalizedFont(categoryCarouselLabel);
            return;
        }
        string category = songCategories[Mathf.Clamp(currentCategoryIndex, 0, songCategories.Count - 1)];
        categoryCarouselLabel.text = $"{category}   {songNumber} / {songCount}";
    }

    private void EnsureInlineDifficultyList()
    {
        if (centerSlot == null || centerSlot.root == null) return;
        Transform existing = centerSlot.root.Find("InlineDifficultyList");
        if (existing != null)
        {
            inlineDifficultyListRoot = existing as RectTransform;
            VerticalLayoutGroup existingLayout =
                existing.GetComponent<VerticalLayoutGroup>();
            if (existingLayout != null)
                existingLayout.childControlHeight = true;
            return;
        }

        GameObject listObject = new GameObject("InlineDifficultyList", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
        listObject.layer = centerSlot.root.gameObject.layer;
        inlineDifficultyListRoot = listObject.GetComponent<RectTransform>();
        inlineDifficultyListRoot.SetParent(centerSlot.root, false);
        // Hanging off the right edge of the book, clear of the cover.
        inlineDifficultyListRoot.anchorMin = new Vector2(1.02f, 0.52f);
        inlineDifficultyListRoot.anchorMax = new Vector2(1.36f, 0.94f);
        inlineDifficultyListRoot.offsetMin = Vector2.zero;
        inlineDifficultyListRoot.offsetMax = Vector2.zero;
        inlineDifficultyListRoot.SetAsLastSibling();

        Image background = listObject.GetComponent<Image>();
        background.color = new Color(1f, 0.985f, 0.93f, 0.12f);
        background.raycastTarget = false;
        Outline outline = listObject.AddComponent<Outline>();
        outline.effectColor = new Color(ClassicalBookUITheme.Gold.r, ClassicalBookUITheme.Gold.g,
            ClassicalBookUITheme.Gold.b, 0.22f);
        outline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup layout = listObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(5, 5, 6, 6);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void UpdateInlineDifficultyButtons(SongOption group)
    {
        // The bookmarks about to be destroyed own the hover that opened it.
        HideChartAnalysis();
        EnsureInlineDifficultyList();
        if (inlineDifficultyListRoot == null) return;
        for (int i = inlineDifficultyListRoot.childCount - 1; i >= 0; i--)
            Destroy(inlineDifficultyListRoot.GetChild(i).gameObject);

        List<SongOption> variants = group?.difficultyVariants;
        if (variants == null || variants.Count == 0)
            variants = group != null ? new List<SongOption> { group } : new List<SongOption>();

        for (int i = 0; i < variants.Count; i++)
        {
            SongOption choice = variants[i];
            if (choice == null) continue;
            IReadOnlyList<LocalScoreEntry> topScores =
                LocalScoreRecords.GetTopScores(choice);
            GameObject buttonObject = new GameObject($"Difficulty_{choice.difficultyName}_{i}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(BookmarkGraphic), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(inlineDifficultyListRoot, false);
            BookmarkGraphic goldEdge = buttonObject.GetComponent<BookmarkGraphic>();
            goldEdge.color = ClassicalBookUITheme.GetDifficultyGlow(choice.difficultyName, choice.difficultyLevel);
            goldEdge.NotchDepth = 17f;
            Shadow shadow = buttonObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.12f, 0.055f, 0.025f, 0.48f);
            shadow.effectDistance = new Vector2(4f, -4f);

            GameObject faceObject = new GameObject("BookmarkFace", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(BookmarkGraphic));
            faceObject.transform.SetParent(buttonObject.transform, false);
            RectTransform faceRect = faceObject.GetComponent<RectTransform>();
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = new Vector2(3f, 3f);
            faceRect.offsetMax = new Vector2(-3f, -3f);
            BookmarkGraphic face = faceObject.GetComponent<BookmarkGraphic>();
            face.color = ClassicalBookUITheme.GetDifficultyFace(choice.difficultyName, choice.difficultyLevel);
            face.NotchDepth = 15f;

            GameObject spineObject = new GameObject("GoldSpine", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            spineObject.transform.SetParent(faceObject.transform, false);
            RectTransform spineRect = spineObject.GetComponent<RectTransform>();
            spineRect.anchorMin = new Vector2(0f, 0.15f);
            spineRect.anchorMax = new Vector2(0f, 0.85f);
            spineRect.pivot = new Vector2(0f, 0.5f);
            spineRect.anchoredPosition = new Vector2(11f, 0f);
            spineRect.sizeDelta = new Vector2(3f, 0f);
            spineObject.GetComponent<Image>().color = new Color(
                ClassicalBookUITheme.Gold.r, ClassicalBookUITheme.Gold.g,
                ClassicalBookUITheme.Gold.b, 0.82f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = face;
            float bookmarkHeight = 40f + topScores.Count * 30f;
            buttonObject.GetComponent<LayoutElement>().preferredHeight = bookmarkHeight;
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x, bookmarkHeight);
            ColorBlock buttonColors = button.colors;
            buttonColors.normalColor = Color.white;
            buttonColors.highlightedColor = new Color(1.18f, 1.08f, 0.88f, 1f);
            buttonColors.pressedColor = new Color(0.72f, 0.64f, 0.52f, 1f);
            buttonColors.selectedColor = buttonColors.highlightedColor;
            buttonColors.fadeDuration = 0.08f;
            button.colors = buttonColors;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(20f, 0f);
            labelRect.offsetMax = new Vector2(-25f, 0f);
            if (topScores.Count > 0)
            {
                labelRect.anchorMin = new Vector2(0f, 1f);
                labelRect.anchorMax = Vector2.one;
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.sizeDelta = new Vector2(0f, 38f);
                labelRect.anchoredPosition = Vector2.zero;
            }
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            string difficultyName = string.IsNullOrWhiteSpace(choice.difficultyName) ? "Normal" : choice.difficultyName;
            label.text = choice.difficultyLevel > 0
                ? $"{difficultyName}   Lv. {choice.difficultyLevel}"
                : difficultyName;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            ClassicalBookUITheme.StyleText(label,
                ClassicalBookUITheme.GetDifficultyColour(choice.difficultyName, choice.difficultyLevel), 17f, FontStyles.Bold);

            for (int scoreIndex = 0; scoreIndex < topScores.Count; scoreIndex++)
                AddInlineLocalScoreRow(
                    buttonObject.transform, topScores[scoreIndex], scoreIndex);

            SongOption capturedChoice = choice;
            RectTransform capturedBookmark = buttonObject.GetComponent<RectTransform>();
            button.onClick.AddListener(() => StartInlineDifficulty(group, capturedChoice, capturedBookmark));

            // Hovering a bookmark opens that chart's analysis beside it.
            var hover = buttonObject.AddComponent<EventTrigger>();
            AddPointerTrigger(hover, EventTriggerType.PointerEnter, () =>
            {
                SetSelectionAtmosphereDifficulty(capturedChoice.difficultyName,
                    capturedChoice.difficultyLevel);
                ShowChartAnalysis(capturedChoice, capturedBookmark);
            });
            AddPointerTrigger(hover, EventTriggerType.PointerExit, () =>
            {
                HideChartAnalysis();
                RestoreSelectionAtmosphereDifficulty();
            });
        }
    }

    private static void AddPointerTrigger(EventTrigger trigger, EventTriggerType type,
        System.Action callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => callback());
        trigger.triggers.Add(entry);
    }

    /// <summary>
    /// Opens the floating analysis for one chart.  The chart file is far too big
    /// to parse in a frame, so the panel opens immediately and fills in when the
    /// cache has finished reading; a chart already read appears instantly.
    /// </summary>
    private void ShowChartAnalysis(SongOption choice, RectTransform bookmark)
    {
        if (choice == null || !analysisPanel.IsBuilt) return;
        if (carouselMoving || difficultyLaunchInProgress) return;

        string chartFile = choice.chartFileName;
        if (string.IsNullOrWhiteSpace(chartFile)) return;

        analysisPanel.ShowLoading(BuildAnalysisTitle(choice),
            ClassicalBookUITheme.GetDifficultyColour(choice.difficultyName, choice.difficultyLevel), chartFile, bookmark);

        if (ChartAnalysisCache.TryGet(chartFile, out ChartAnalysis ready))
        {
            analysisPanel.SetAnalysis(ready);
            return;
        }

        if (analysisRoutine != null) StopCoroutine(analysisRoutine);
        analysisRoutine = StartCoroutine(ChartAnalysisCache.Build(chartFile, analysis =>
        {
            // The pointer may have moved to another bookmark while this was read.
            if (!string.Equals(analysisPanel.ShownChartFileName, chartFile,
                    StringComparison.OrdinalIgnoreCase)) return;
            if (analysis != null) analysisPanel.SetAnalysis(analysis);
            else analysisPanel.SetUnavailable();
        }));
    }

    private void HideChartAnalysis(bool immediate = false)
    {
        if (analysisPanel.IsBuilt) analysisPanel.Hide(immediate);
    }

    private static string BuildAnalysisTitle(SongOption choice)
    {
        string name = string.IsNullOrWhiteSpace(choice.difficultyName) ? "Normal" : choice.difficultyName;
        return choice.difficultyLevel > 0 ? $"{name}   Lv. {choice.difficultyLevel}" : name;
    }

    private void SetSelectionAtmosphereDifficulty(int level)
    {
        SetSelectionAtmosphereDifficulty(null, level);
    }

    private void SetSelectionAtmosphereDifficulty(string difficultyName, int level)
    {
        if (selectionAtmosphere != null)
            selectionAtmosphere.SetDifficulty(difficultyName, level);
    }

    private void RestoreSelectionAtmosphereDifficulty()
    {
        SongOption focused = songOptions.Count > 0 && currentIndex >= 0 && currentIndex < songOptions.Count
            ? songOptions[currentIndex]
            : null;
        SetSelectionAtmosphereDifficulty(GetAtmosphereName(focused),
            GetAtmosphereDifficulty(focused));
    }

    /// <summary>
    /// The chart the room is lit for: the one the player's own difficulty choice
    /// points at.
    /// </summary>
    /// <remarks>
    /// 以前這裡走的是「這首歌最高的那一階」，於是不管玩家在難度牌上點了哪一
    /// 顆寶石，房間永遠是最高難度的顏色 —— 選擇看得到、卻沒有任何效果。現在
    /// 顏色、書本的裝幀和牌子上高亮的那一格全部由同一個選擇決定。
    ///
    /// 已經進到某一首的難度清單（selectedVariant）時仍然以那一個為準：那是比
    /// 「我平常練哪一階」更明確的一次表態。
    /// </remarks>
    private static SongOption GetAtmosphereChart(SongOption option)
    {
        if (option == null) return null;
        if (option.selectedVariant != null) return option.selectedVariant;
        return SongDifficultyStrip.PreferredChart(option) ?? option;
    }

    private static string GetAtmosphereName(SongOption option)
    {
        SongOption chart = GetAtmosphereChart(option);
        return chart != null ? chart.difficultyName : null;
    }

    private static int GetAtmosphereDifficulty(SongOption option)
    {
        SongOption chart = GetAtmosphereChart(option);
        return chart != null ? Mathf.Max(1, chart.difficultyLevel) : 1;
    }

    private static void AddInlineLocalScoreRow(
        Transform parent, LocalScoreEntry entry, int index)
    {
        if (parent == null || entry == null) return;
        GameObject rowObject = new GameObject(
            $"LocalScore_{index + 1}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rowObject.transform.SetParent(parent, false);
        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = Vector2.one;
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.sizeDelta = new Vector2(0f, 27f);
        rowRect.anchoredPosition = new Vector2(0f, -39f - index * 30f);
        Image rowBackground = rowObject.GetComponent<Image>();
        rowBackground.color = new Color(1f, 0.92f, 0.73f, index == 0 ? 0.13f : 0.07f);
        rowBackground.raycastTarget = false;

        GameObject scoreObject = new GameObject(
            "Score", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        scoreObject.transform.SetParent(rowObject.transform, false);
        RectTransform scoreRect = scoreObject.GetComponent<RectTransform>();
        scoreRect.anchorMin = Vector2.zero;
        scoreRect.anchorMax = Vector2.one;
        scoreRect.offsetMin = new Vector2(21f, 0f);
        scoreRect.offsetMax = new Vector2(-48f, 0f);
        TextMeshProUGUI scoreLabel = scoreObject.GetComponent<TextMeshProUGUI>();
        scoreLabel.text = $"#{index + 1}   {entry.score:N0}";
        scoreLabel.alignment = TextAlignmentOptions.MidlineLeft;
        scoreLabel.raycastTarget = false;
        ClassicalBookUITheme.StyleText(
            scoreLabel, ClassicalBookUITheme.ParchmentLight, 14f,
            index == 0 ? FontStyles.Bold : FontStyles.Normal);

        Texture2D badgeTexture = LocalScoreRecords.LoadRankTexture(entry.rank);
        if (badgeTexture != null)
        {
            GameObject badgeObject = new GameObject(
                "RankBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            badgeObject.transform.SetParent(rowObject.transform, false);
            RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 0.5f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.sizeDelta = new Vector2(18f, 25f);
            badgeRect.anchoredPosition = new Vector2(-25f, 0f);
            RawImage badge = badgeObject.GetComponent<RawImage>();
            badge.texture = badgeTexture;
            badge.color = Color.white;
            badge.raycastTarget = false;
        }
        else
        {
            GameObject rankObject = new GameObject(
                "RankFallback", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            rankObject.transform.SetParent(rowObject.transform, false);
            RectTransform rankRect = rankObject.GetComponent<RectTransform>();
            rankRect.anchorMin = rankRect.anchorMax = new Vector2(1f, 0.5f);
            rankRect.sizeDelta = new Vector2(42f, 25f);
            rankRect.anchoredPosition = new Vector2(-26f, 0f);
            TextMeshProUGUI rankLabel = rankObject.GetComponent<TextMeshProUGUI>();
            rankLabel.text = entry.rank;
            rankLabel.alignment = TextAlignmentOptions.Center;
            rankLabel.raycastTarget = false;
            ClassicalBookUITheme.StyleText(
                rankLabel, ClassicalBookUITheme.Gold, 15f, FontStyles.Bold);
        }
    }

    private void StartInlineDifficulty(SongOption group, SongOption choice, RectTransform bookmark)
    {
        if (choice == null || difficultyLaunchInProgress || carouselTransitioning) return;
        if (IsCarouselClickSuppressed()) return;
        HideChartAnalysis(true);
        StartCoroutine(PullBookmarkAndStart(group, choice, bookmark));
    }

    private IEnumerator PullBookmarkAndStart(SongOption group, SongOption choice, RectTransform bookmark)
    {
        difficultyLaunchInProgress = true;
        Button[] difficultyButtons = inlineDifficultyListRoot != null
            ? inlineDifficultyListRoot.GetComponentsInChildren<Button>(true)
            : Array.Empty<Button>();
        for (int i = 0; i < difficultyButtons.Length; i++)
            difficultyButtons[i].interactable = false;

        Vector2 startPosition = bookmark != null ? bookmark.anchoredPosition : Vector2.zero;
        CanvasGroup bookmarkCanvas = bookmark != null ? bookmark.GetComponent<CanvasGroup>() : null;
        if (bookmark != null && bookmarkCanvas == null)
            bookmarkCanvas = bookmark.gameObject.AddComponent<CanvasGroup>();
        Vector2 centerStart = centerSlot?.root != null ? centerSlot.root.anchoredPosition : centerBookRestPosition;

        const float duration = 0.28f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            if (bookmark != null)
            {
                bookmark.anchoredPosition = startPosition + Vector2.right * (175f * eased);
                bookmark.localScale = Vector3.one * Mathf.Lerp(1f, 1.045f, Mathf.Sin(t * Mathf.PI));
            }
            if (bookmarkCanvas != null) bookmarkCanvas.alpha = Mathf.Lerp(1f, 0.18f, t * t);
            if (centerSlot?.root != null)
                centerSlot.root.anchoredPosition = centerStart + Vector2.left * (22f * eased);
            yield return null;
        }

        if (centerSlot?.root != null) centerSlot.root.anchoredPosition = centerBookRestPosition;
        CompleteInlineDifficulty(group, choice);
        difficultyLaunchInProgress = false;
    }

    private void CompleteInlineDifficulty(SongOption group, SongOption choice)
    {
        if (group != null) group.selectedVariant = choice;
        SongDifficultyStrip.RememberChoice(choice);
        if (difficultySelectorInstance != null)
        {
            Destroy(difficultySelectorInstance);
            difficultySelectorInstance = null;
        }
        UpdateDetailPanel(group ?? choice);
        StartChosenSong(choice);
    }

    private void CacheBookRestPositions()
    {
        if (centerSlot?.root != null) centerBookRestPosition = centerSlot.root.anchoredPosition;
        if (leftSlot?.root != null) leftBookRestPosition = leftSlot.root.anchoredPosition;
        if (rightSlot?.root != null) rightBookRestPosition = rightSlot.root.anchoredPosition;
        bookRestPoseCached = true;

        // The authored left/right rest positions define one "song" of travel.
        float authored = Mathf.Abs(rightBookRestPosition.x - leftBookRestPosition.x) * 0.5f;
        if (authored > 1f) carouselSlotSpacing = authored;
    }

    private void LoadSongOptions()
    {
        // All playable songs come from the writable portable library. Bundled
        // defaults are seeded into UserSongs by the build/editor setup, so they
        // use the same edit/delete/difficulty-management path as imported songs.
        if (songOptions != null)
        {
            foreach (var so in songOptions) if (so != null) so.selectedVariant = null;
        }
        discoveredCategoryOrder.Clear();
        var discovered = new List<SongMetadata>();
        var externalWarnings = new List<string>();
        foreach (ExternalSongLibrary.LoadedSong loaded in ExternalSongLibrary.LoadAll(out externalWarnings))
        {
            try
            {
                SongMetadata metadata = JsonUtility.FromJson<SongMetadata>(loaded.metadataJson);
                if (metadata == null || metadata.difficulties == null || metadata.difficulties.Count == 0) continue;
                metadata.category = string.IsNullOrWhiteSpace(loaded.entry.category)
                    ? ExternalSongLibrary.DefaultCategory
                    : loaded.entry.category;
                metadata.externalId = loaded.entry.id;
                discovered.Add(metadata);
                if (!discoveredCategoryOrder.Exists(item =>
                        string.Equals(item, metadata.category, StringComparison.OrdinalIgnoreCase)))
                    discoveredCategoryOrder.Add(metadata.category);
            }
            catch (Exception ex)
            {
                externalWarnings.Add($"{loaded.entry.folderName}：{ex.Message}");
            }
        }
        foreach (string warning in externalWarnings)
            BuildLogger.LogWarning("[ExternalSongLibrary] " + warning);
        BuildLogger.Log($"SongSelectionManager: Portable library returned {discovered.Count} categorized entries");
        if (discovered == null || discovered.Count == 0)
        {
            BuildLogger.LogWarning("SongSelectionManager: No songs found in the portable UserSongs library.");
        }
        // Build a flat list of per-difficulty options first
        var flatOptions = new List<SongOption>();
        foreach (SongMetadata metadata in discovered)
        {
            if (metadata == null) continue;
            BuildLogger.Log($"SongSelectionManager: processing metadata displayName='{metadata.displayName}' difficulties={(metadata.difficulties==null?0:metadata.difficulties.Count)}");
            if (metadata.difficulties != null && metadata.difficulties.Count > 0)
            {
                foreach (var diff in metadata.difficulties)
                {
                    try
                    {
                        var option = CreateSongOption(metadata, diff);
                        if (option != null)
                        {
                            flatOptions.Add(option);
                            BuildLogger.Log($"SongSelectionManager: created SongOption '{option.displayName}' ({option.difficultyName})");
                        }
                        else
                        {
                            BuildLogger.LogWarning($"SongSelectionManager: CreateSongOption returned null for displayName='{metadata.displayName}' diff='{diff?.difficultyName}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        BuildLogger.LogWarning($"SongSelectionManager: Exception creating option for '{metadata.displayName}' diff='{diff?.difficultyName}': {ex.Message}");
                    }
                }
            }
            else
            {
                var single = new SongDifficulty
                {
                    difficultyName = metadata.difficultyName,
                    difficultyLevel = metadata.difficultyLevel,
                    chartFileName = metadata.chartFileName,
                    audioResourcePath = metadata.audioResourcePath,
                    pianoAudioResourcePath = metadata.pianoAudioResourcePath,
                    coverResourcePath = metadata.coverResourcePath,
                    videoPath = metadata.coverResourcePath
                };
                var option = CreateSongOption(metadata, single);
                if (option != null) flatOptions.Add(option);
            }
        }
        // Group by displayName so the carousel shows one entry per song and keeps difficultyVariants
        songOptions.Clear();
        allSongOptions.Clear();
        var groupMap = new Dictionary<string, SongOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in flatOptions)
        {
            if (opt == null) continue;
            string titleKey = string.IsNullOrWhiteSpace(opt.displayName) ? opt.chartFileName ?? string.Empty : opt.displayName;
            string key = (opt.category ?? string.Empty) + "\u001f" + titleKey;
            if (groupMap.TryGetValue(key, out var primary))
            {
                if (primary.difficultyVariants == null) primary.difficultyVariants = new List<SongOption> { primary };
                primary.difficultyVariants.Add(opt);
            }
            else
            {
                // use this opt as primary for the group
                opt.difficultyVariants = new List<SongOption> { opt };
                groupMap[key] = opt;
                allSongOptions.Add(opt);
            }
        }
        RebuildCategoriesAndVisibleSongs();
        currentIndex = Mathf.Clamp(currentIndex, 0, Mathf.Max(0, songOptions.Count - 1));

        // 沒有影片、沒有封面路徑是**常態**，不是故障：大部分曲子本來就只有一張
        // 圖或什麼都沒有。一首一行警告的話，整個曲庫掃完 log 裡就只剩這個，真
        // 正壞掉的那一行反而被埋掉 —— 一個永遠在響的警報等於沒有警報。
        //
        // 所以留一行總數就好，而且走 verbose：要查的時候開起來看得到，平常不吵。
        int noCover = 0;
        int noVideo = 0;
        foreach (var option in songOptions)
        {
            if (string.IsNullOrWhiteSpace(option.coverResourcePath)) noCover++;
            if (string.IsNullOrWhiteSpace(option.videoPath)) noVideo++;
        }
        if (noCover > 0 || noVideo > 0)
        {
            BuildLogger.Log($"[SongSelection] {songOptions.Count} 首：{noCover} 首沒有封面路徑、"
                + $"{noVideo} 首沒有影片。");
        }
    }

    // 已移除 song_list.json 載入，僅保留自動掃描

    private List<AudioPauseResumeWindow> FlattenAudioManage(List<AudioManageEntry> entries, AudioManageEntry singleEntry, bool piano)
    {
        var result = new List<AudioPauseResumeWindow>();
        IEnumerable<AudioManageEntry> source = entries;
        if ((source == null || (entries != null && entries.Count == 0)) && singleEntry != null)
        {
            source = new List<AudioManageEntry> { singleEntry };
        }
        if (source == null) return result;

        foreach (var entry in source)
        {
            if (entry == null) continue;
            var src = piano ? entry.pianoAudioResource : entry.audioResource;
            if (src == null) continue;
            foreach (var window in src)
            {
                if (window == null) continue;
                // If this resource entry contains nested pauseWindows, flatten them first (inherit parent fields when missing)
                if (window.pauseWindows != null && window.pauseWindows.Count > 0)
                {
                    foreach (var pw in window.pauseWindows)
                    {
                        if (pw == null) continue;
                        int childStartMs = 0;
                        int childEndMs = 0;
                        bool childIsMute = false;
                        bool childIsPause = false;

                        if (pw.mute > 0)
                        {
                            childStartMs = pw.mute;
                            childEndMs = pw.muteResume > 0 ? pw.muteResume : pw.resume;
                            childIsMute = true;
                        }
                        else if (pw.pausePlayback > 0)
                        {
                            childStartMs = pw.pausePlayback;
                            childEndMs = pw.resumePlayback > 0 ? pw.resumePlayback : pw.resume;
                            childIsPause = true;
                        }
                        else if (pw.pause > 0)
                        {
                            childStartMs = pw.pause;
                            childEndMs = pw.resume;
                            childIsMute = true;
                        }

                        if (!childIsMute && !childIsPause && pw.fade <= 0) continue;

                        var childOutWin = new AudioPauseResumeWindow
                        {
                            pause = childStartMs,
                            resume = childEndMs,
                            fade = pw.fade > 0 ? pw.fade : window.fade,
                            fadeDurationMs = pw.fadeDurationMs > 0 ? pw.fadeDurationMs : window.fadeDurationMs,
                            audiochangeTime = pw.audiochangeTime > 0 ? pw.audiochangeTime : window.audiochangeTime,
                            atempo = pw.atempo > 0 ? pw.atempo : window.atempo,
                            originalBPM = pw.originalBPM > 0f ? pw.originalBPM : window.originalBPM,
                            newBPM = pw.newBPM > 0f ? pw.newBPM : window.newBPM,
                            useMixer = pw.useMixer || window.useMixer,
                            skipSections = pw.skipSections ?? window.skipSections
                        };
                        childOutWin.isMuteWindow = childIsMute;
                        childOutWin.isPauseWindow = childIsPause;
                        result.Add(childOutWin);
                    }
                    // proceed to next resource window after processing nested ones
                    continue;
                }
                // Determine whether this entry is a mute window or a pause (playback) window
                int startMs = 0;
                int endMs = 0;
                bool isMute = false;
                bool isPause = false;

                if (window.mute > 0)
                {
                    startMs = window.mute;
                    endMs = window.muteResume > 0 ? window.muteResume : window.resume;
                    isMute = true;
                }
                else if (window.pausePlayback > 0)
                {
                    startMs = window.pausePlayback;
                    endMs = window.resumePlayback > 0 ? window.resumePlayback : window.resume;
                    isPause = true;
                }
                else if (window.pause > 0)
                {
                    // Legacy: treat 'pause' as mute by default for backward compatibility
                    startMs = window.pause;
                    endMs = window.resume;
                    isMute = true;
                }
                // Skip entries that have neither mute/pause/fade
                if (!isMute && !isPause && window.fade <= 0) continue;

                var outWin = new AudioPauseResumeWindow
                {
                    pause = startMs,
                    resume = endMs,
                    fade = window.fade,
                    fadeDurationMs = window.fadeDurationMs,
                    audiochangeTime = window.audiochangeTime,
                    atempo = window.atempo,
                    originalBPM = window.originalBPM,
                    newBPM = window.newBPM,
                    useMixer = window.useMixer,
                    skipSections = window.skipSections
                };
                outWin.isMuteWindow = isMute;
                outWin.isPauseWindow = isPause;
                result.Add(outWin);
            }
        }

        result.Sort((a, b) => a.pause.CompareTo(b.pause));
        return result;
    }

    private List<SkipSection> ExtractSkipSections(List<AudioManageEntry> entries, AudioManageEntry singleEntry)
    {
        var result = new List<SkipSection>();
        IEnumerable<AudioManageEntry> source = entries;
        if ((source == null || (entries != null && entries.Count == 0)) && singleEntry != null)
        {
            source = new List<AudioManageEntry> { singleEntry };
        }
        if (source == null) return result;

        foreach (var entry in source)
        {
            if (entry == null) continue;
            var allWindows = new List<AudioPauseResumeWindow>();
            if (entry.audioResource != null) allWindows.AddRange(entry.audioResource);
            if (entry.pianoAudioResource != null) allWindows.AddRange(entry.pianoAudioResource);
            foreach (var w in allWindows)
            {
                if (w == null) continue;
                // direct skipSections on this window
                if (w.skipSections != null)
                {
                    foreach (var s in w.skipSections)
                    {
                        if (s == null) continue;
                        if (s.endMs <= s.startMs) continue;
                        result.Add(new SkipSection { startMs = s.startMs, endMs = s.endMs });
                    }
                }
                // nested pauseWindows may also contain skipSections
                if (w.pauseWindows != null)
                {
                    foreach (var pw in w.pauseWindows)
                    {
                        if (pw == null || pw.skipSections == null) continue;
                        foreach (var s in pw.skipSections)
                        {
                            if (s == null) continue;
                            if (s.endMs <= s.startMs) continue;
                            result.Add(new SkipSection { startMs = s.startMs, endMs = s.endMs });
                        }
                    }
                }
            }
        }
        result.Sort((a, b) => a.startMs.CompareTo(b.startMs));
        return result;
    }

    private float ExtractSpeedPlan(List<AudioManageEntry> entries, AudioManageEntry singleEntry, bool piano, out bool useMixer, List<AudioSpeedEvent> speedEvents)
    {
        useMixer = false;
        if (speedEvents != null) speedEvents.Clear();
        IEnumerable<AudioManageEntry> source = entries;
        if ((source == null || (entries != null && entries.Count == 0)) && singleEntry != null)
        {
            source = new List<AudioManageEntry> { singleEntry };
        }
        if (source == null) return 1f;

        float initial = 1f;
        bool initialSet = false;

        foreach (var entry in source)
        {
            if (entry == null) continue;
            if (entry.audioSpeed != null)
            {
                foreach (var sp in entry.audioSpeed)
                {
                    if (sp == null) continue;
                    float f = sp.GetFactor();
                    if (f > 0f)
                    {
                        int changeMs = sp.audiochangeTime;
                        if (changeMs > 0)
                        {
                            if (speedEvents != null)
                            {
                                speedEvents.Add(new AudioSpeedEvent { audiochangeTimeMs = changeMs, factor = f, useMixer = sp.useMixer });
                            }
                        }
                        else if (!initialSet)
                        {
                            initial = f;
                            useMixer = sp.useMixer;
                            initialSet = true;
                        }
                        else if (speedEvents != null)
                        {
                            speedEvents.Add(new AudioSpeedEvent { audiochangeTimeMs = 0, factor = f, useMixer = sp.useMixer });
                        }
                    }
                }
            }
            // Fallback: allow audioResource entries to carry BPM change fields directly (with optional timed change)
            var resourceWindows = piano ? entry.pianoAudioResource : entry.audioResource;
            if (resourceWindows != null)
            {
                foreach (var win in resourceWindows)
                {
                    if (win == null) continue;
                    if (win.atempo > 0)
                    {
                        if (speedEvents != null)
                        {
                            speedEvents.Add(new AudioSpeedEvent { audiochangeTimeMs = win.atempo, factor = 1f, useMixer = win.useMixer });
                        }
                    }
                    if (win.originalBPM > 0f && win.newBPM > 0f)
                    {
                        float f = win.newBPM / win.originalBPM;
                        if (f > 0f)
                        {
                            int changeMs = win.audiochangeTime;
                            bool mixerFlag = win.useMixer;
                            if (changeMs > 0)
                            {
                                if (speedEvents != null)
                                {
                                    speedEvents.Add(new AudioSpeedEvent { audiochangeTimeMs = changeMs, factor = f, useMixer = mixerFlag });
                                }
                            }
                            else if (!initialSet)
                            {
                                initial = f;
                                useMixer = mixerFlag;
                                initialSet = true;
                            }
                            else if (speedEvents != null)
                            {
                                speedEvents.Add(new AudioSpeedEvent { audiochangeTimeMs = 0, factor = f, useMixer = mixerFlag });
                            }
                        }
                    }
                }
            }
        }
        return initial;
    }

    private SongOption CreateSongOption(SongMetadata metadata)
    {
        // Deprecated single-difficulty constructor (kept for safety)
        string displayName = string.IsNullOrWhiteSpace(metadata.displayName) ? metadata.chartFileName : metadata.displayName;
        AudioClip audioClip = LoadAudioClip(metadata.audioResourcePath, displayName);
        AudioClip pianoClip = LoadAudioClip(metadata.pianoAudioResourcePath, displayName + " (Piano)");
        Sprite coverSprite = LoadCoverSprite(metadata.coverResourcePath, displayName);

        var mainAudioManage = FlattenAudioManage(metadata.audioManage, null, false);
        var pianoAudioManage = FlattenAudioManage(metadata.audioManage, null, true);
        bool useMixerFlag;
        var speedEvents = new List<AudioSpeedEvent>();
        bool pianoUseMixerFlag;
        var pianoSpeedEvents = new List<AudioSpeedEvent>();
        float speedFactor = ExtractSpeedPlan(metadata.audioManage, null, false, out useMixerFlag, speedEvents);
        float pianoSpeedFactor = ExtractSpeedPlan(metadata.audioManage, null, true, out pianoUseMixerFlag, pianoSpeedEvents);

        var optOut = new SongOption
        {
            externalId = metadata.externalId,
            category = metadata.category,
            displayName = displayName,
            chartFileName = metadata.chartFileName,
            audioResourcePath = metadata.audioResourcePath,
            pianoAudioResourcePath = metadata.pianoAudioResourcePath,
            audioClip = audioClip,
            pianoAudioClip = pianoClip,
            coverResourcePath = metadata.coverResourcePath,
            difficultyName = metadata.difficultyName,
            difficultyLevel = metadata.difficultyLevel,
            author = metadata.author,
            coverSprite = coverSprite,
            videoPath = metadata.coverResourcePath,
            videoStartTimeSec = 0f,
            audioManageMain = mainAudioManage,
            audioManagePiano = pianoAudioManage,
            audioSpeedFactor = speedFactor,
            useMixer = useMixerFlag,
            audioSpeedEvents = speedEvents,
            pianoAudioSpeedFactor = pianoSpeedFactor,
            pianoUseMixer = pianoUseMixerFlag,
            pianoAudioSpeedEvents = pianoSpeedEvents,
            noBackgroundMusic = metadata.noBackgroundMusic
        };
        // Extract skip sections from any audio/piano resource windows
        optOut.skipSections = ExtractSkipSections(metadata.audioManage, metadata.audioManageSingle);
        if (coverSprite == null)
        {
            BuildLogger.LogWarning($"SongSelectionManager: Cover sprite not found for '{displayName}' at Resources path '{metadata.coverResourcePath}'");
        }
        return optOut;
        
    }

    // New: create option from a SongMetadata + SongDifficulty pair
    private SongOption CreateSongOption(SongMetadata metadata, SongDifficulty diff)
    {
        if (diff == null) return null;
        string displayName = string.IsNullOrWhiteSpace(metadata.displayName) ? diff.chartFileName : metadata.displayName;
        AudioClip audioClip = LoadAudioClip(diff.audioResourcePath ?? metadata.audioResourcePath, displayName);
        AudioClip pianoClip = LoadAudioClip(diff.pianoAudioResourcePath ?? metadata.pianoAudioResourcePath, displayName + " (Piano)");
        Sprite coverSprite = LoadCoverSprite(diff.coverResourcePath ?? metadata.coverResourcePath, displayName);

        // Only apply speed/windows if the specific difficulty declares them; avoid inheriting from other difficulties.
        var mergedAudioManage = (diff.audioManage != null && diff.audioManage.Count > 0) ? diff.audioManage : null;
        var mainAudioManage = FlattenAudioManage(mergedAudioManage, null, false);
        var pianoAudioManage = FlattenAudioManage(mergedAudioManage, null, true);
        bool useMixerFlag;
        var speedEvents = new List<AudioSpeedEvent>();
        bool pianoUseMixerFlag;
        var pianoSpeedEvents = new List<AudioSpeedEvent>();
        float speedFactor = ExtractSpeedPlan(mergedAudioManage, null, false, out useMixerFlag, speedEvents);
        float pianoSpeedFactor = ExtractSpeedPlan(mergedAudioManage, null, true, out pianoUseMixerFlag, pianoSpeedEvents);

        var opt = new SongOption
        {
            externalId = metadata.externalId,
            category = metadata.category,
            displayName = displayName,
            chartFileName = diff.chartFileName ?? metadata.chartFileName,
            audioResourcePath = diff.audioResourcePath ?? metadata.audioResourcePath,
            pianoAudioResourcePath = diff.pianoAudioResourcePath ?? metadata.pianoAudioResourcePath,
            audioClip = audioClip,
            pianoAudioClip = pianoClip,
            coverResourcePath = diff.coverResourcePath ?? metadata.coverResourcePath,
            difficultyName = diff.difficultyName ?? metadata.difficultyName,
            difficultyLevel = diff.difficultyLevel,
            author = metadata.author,
            coverSprite = coverSprite,
            videoPath = diff.videoPath,
            videoStartTimeSec = diff.videostartTime,
            // 難度沒宣告就沿用整首歌的設定
            noBackgroundMusic = diff.noBackgroundMusic || metadata.noBackgroundMusic,
            audioManageMain = mainAudioManage,
            audioManagePiano = pianoAudioManage,
            audioSpeedFactor = speedFactor,
            useMixer = useMixerFlag,
            audioSpeedEvents = speedEvents,
            pianoAudioSpeedFactor = pianoSpeedFactor,
            pianoUseMixer = pianoUseMixerFlag,
            pianoAudioSpeedEvents = pianoSpeedEvents
        };
        // Extract skip sections for this difficulty
        opt.skipSections = ExtractSkipSections(mergedAudioManage, null);
        if (coverSprite == null)
        {
            BuildLogger.LogWarning($"SongSelectionManager: Cover sprite not found for '{displayName}' at Resources path '{diff.coverResourcePath ?? metadata.coverResourcePath}'");
        }
        return opt;
    }

    private void ConfigureCarouselControls()
    {
        if (carouselInitialized)
        {
            return;
        }

        Debug.Log("[SongSelectionManager] ConfigureCarouselControls 啟動");

        // We now use mouse-wheel for song navigation. Disable the Next/Previous buttons.
        if (nextButton != null)
        {
            try { nextButton.onClick.RemoveAllListeners(); } catch { }
            try { nextButton.gameObject.SetActive(false); } catch { }
            Debug.Log($"[SongSelectionManager] nextButton disabled: {nextButton.name}");
        }

        if (previousButton != null)
        {
            try { previousButton.onClick.RemoveAllListeners(); } catch { }
            try { previousButton.gameObject.SetActive(false); } catch { }
            Debug.Log($"[SongSelectionManager] previousButton disabled: {previousButton.name}");
        }

        // Center slot: prefer using assigned selectButton (e.g., TC_Button) and stretch it over cover image
        if (centerSlot != null)
        {
            if (centerSlot.selectButton != null)
            {
                OverlayButtonOnCard(centerSlot.selectButton, centerSlot.root);
            }
            else if (centerSlot.selectButton == null)
            {
                if (centerSlot.coverImage != null)
                {
                    // Use the existing cover image as the clickable area but DO NOT change its alpha
                    centerSlot.selectButton = GetOrAddButtonOn(centerSlot.coverImage.gameObject, false);
                }
                else if (centerSlot.root != null)
                {
                    // If there's no image, attach a transparent image on the root to receive clicks
                    centerSlot.selectButton = GetOrAddButtonOn(centerSlot.root.gameObject, true);
                }
            }

            if (centerSlot.selectButton != null)
            {
                centerSlot.selectButton.onClick.RemoveAllListeners();
                centerSlot.selectButton.onClick.AddListener(SelectCurrentSong);
                centerSlot.selectButton.interactable = true;
                Debug.Log($"[SongSelectionManager] centerSlot.selectButton: {centerSlot.selectButton.name} interactable={centerSlot.selectButton.interactable}");
            }

            // Ensure the cover image does not intercept raycasts (button overlay should receive them)
            if (centerSlot.coverImage != null)
            {
                centerSlot.coverImage.raycastTarget = false;
            }
        }

        if (leftSlot != null)
        {
            if (leftSlot.selectButton != null)
            {
                OverlayButtonOnCard(leftSlot.selectButton, leftSlot.root);
            }
            else if (leftSlot.selectButton == null)
            {
                if (leftSlot.coverImage != null)
                {
                    leftSlot.selectButton = GetOrAddButtonOn(leftSlot.coverImage.gameObject, false);
                }
                else if (leftSlot.root != null)
                {
                    leftSlot.selectButton = GetOrAddButtonOn(leftSlot.root.gameObject, true);
                }
                else
                {
                    //Debug.LogWarning("SongSelectionManager: Left slot has neither coverImage nor root assigned — click will not work. Please assign them in Inspector.");
                }
            }

            if (leftSlot.selectButton != null)
            {
                leftSlot.selectButton.onClick.RemoveAllListeners();
                leftSlot.selectButton.onClick.AddListener(() =>
                {
                    //Debug.Log($"SongSelectionManager: Left cover clicked → start index {idx}.");
                    ShowPreviousSong();
                });
                Debug.Log($"[SongSelectionManager] leftSlot.selectButton: {leftSlot.selectButton.name} interactable={leftSlot.selectButton.interactable}");
            }

            if (leftSlot.coverImage != null)
            {
                leftSlot.coverImage.raycastTarget = false;
            }
        }

        if (rightSlot != null)
        {
            if (rightSlot.selectButton != null)
            {
                OverlayButtonOnCard(rightSlot.selectButton, rightSlot.root);
            }
            else if (rightSlot.selectButton == null)
            {
                if (rightSlot.coverImage != null)
                {
                    rightSlot.selectButton = GetOrAddButtonOn(rightSlot.coverImage.gameObject, false);
                }
                else if (rightSlot.root != null)
                {
                    rightSlot.selectButton = GetOrAddButtonOn(rightSlot.root.gameObject, true);
                }
                else
                {
                    //Debug.LogWarning("SongSelectionManager: Right slot has neither coverImage nor root assigned — click will not work. Please assign them in Inspector.");
                }
            }

            if (rightSlot.selectButton != null)
            {
                rightSlot.selectButton.onClick.RemoveAllListeners();
                rightSlot.selectButton.onClick.AddListener(() =>
                {
                    //Debug.Log($"SongSelectionManager: Right cover clicked → start index {idx}.");
                    ShowNextSong();
                });
                Debug.Log($"[SongSelectionManager] rightSlot.selectButton: {rightSlot.selectButton.name} interactable={rightSlot.selectButton.interactable}");
            }

            if (rightSlot.coverImage != null)
            {
                rightSlot.coverImage.raycastTarget = false;
            }
        }

        carouselInitialized = true;
    }

    private Button GetOrAddButtonOn(GameObject go, bool makeTransparentImage)
    {
        if (go == null)
        {
            return null;
        }

        var btn = go.GetComponent<Button>();
        if (btn == null)
        {
            var img = go.GetComponent<Image>();
            if (img == null)
            {
                img = go.AddComponent<Image>();
            }

            if (makeTransparentImage)
            {
                var c = img.color;
                c.a = 0f; // invisible hit area
                img.color = c;
            }

            img.raycastTarget = true;
            btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
        }

        return btn;
    }

    /// <summary>
    /// Stretches the select button over the whole card, so anywhere on the book
    /// starts it.  Placed just above the page background and below everything
    /// else, which leaves the difficulty bookmarks free to take their own clicks.
    /// </summary>
    private static void OverlayButtonOnCard(Button btn, RectTransform card)
    {
        if (btn == null || card == null) return;

        var rect = btn.GetComponent<RectTransform>();
        if (rect == null) return;
        rect.SetParent(card, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.SetSiblingIndex(Mathf.Min(1, card.childCount - 1));

        var image = btn.GetComponent<Image>();
        if (image == null) image = btn.gameObject.AddComponent<Image>();
        image.raycastTarget = true;
        Color colour = image.color;
        colour.a = 0f;
        image.color = colour;

        btn.transition = Selectable.Transition.None;
    }

    private void OverlayButtonOnCover(Button btn, Image coverImg, bool makeTransparent)
    {
        if (btn == null || coverImg == null) return;

        var btnRT = btn.GetComponent<RectTransform>();
        var coverRT = coverImg.rectTransform;
        if (btnRT == null || coverRT == null) return;

        // Reparent under the cover image and stretch to fill
        btnRT.SetParent(coverRT, false);
        btnRT.anchorMin = Vector2.zero;
        btnRT.anchorMax = Vector2.one;
        btnRT.offsetMin = Vector2.zero;
        btnRT.offsetMax = Vector2.zero;
        btnRT.pivot = new Vector2(0.5f, 0.5f);

        // Ensure an Image exists for raycasts, and make it invisible
        var img = btn.GetComponent<Image>();
        if (img == null) img = btn.gameObject.AddComponent<Image>();
        img.raycastTarget = true;
        if (makeTransparent)
        {
            var c = img.color; c.a = 0f; img.color = c;
        }

        btn.transition = Selectable.Transition.None;
    }

    private AudioClip LoadAudioClip(string resourcePath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        if (_audioCache.TryGetValue(resourcePath, out var cached) && cached != null)
        {
            return cached;
        }

        if (ExternalSongLibrary.ToLocalPath(resourcePath) != null)
            return null; // Loaded asynchronously by PreloadClipAsync.

        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null)
        {
            if (_missingAudioWarnings.Add(resourcePath))
            {
                BuildLogger.LogWarning($"SongSelectionManager: Audio clip for '{displayName}' not found at Resources path '{resourcePath}'.");
            }
            _audioCache.Remove(resourcePath);
            return null;
        }

        _missingAudioWarnings.Remove(resourcePath);

        // Do not call LoadAudioData here.  This method runs while the complete
        // song catalogue is being built, so eager loading would decompress every
        // song in Resources at once.  Player builds have a much tighter memory
        // ceiling than the Editor and late catalogue entries can consequently
        // retain an AudioClip object whose sample data failed to load.  Preview
        // preloading and GameManager.EnsureAudioClipReady load only the clip that
        // is actually about to be played.
        _audioCache[resourcePath] = clip;

        return clip;
    }

    void Update()
    {
        if (!carouselInitialized) return;
        // The song cards themselves are UI, so rejecting pointer-over-UI made wheel
        // navigation fail precisely when the cursor was over a card. Only gate on
        // whether the selection screen is actually visible.
        if (selectionPanel == null || !selectionPanel.activeInHierarchy) return;
        // PullBookmarkAndStart animates the centre card itself; the per-frame
        // layout would overwrite it on the very next frame.
        if (difficultyLaunchInProgress) return;

        // 譜面檢視器蓋在最上面的時候，轉盤一律不動。
        //
        // 它的滾輪走 UI 事件，這裡卻是直接讀滑鼠的 —— 不擋的話滾一格會同時捲譜面
        // 和換歌，關掉檢視器才發現選到別首去了。拖曳同理。
        if (ChartOverviewViewer.Instance != null && ChartOverviewViewer.Instance.IsOpen) return;

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        ReadPointer(out bool pointerPressed, out Vector2 pointerPosition);
        float wheelDelta = ReadWheelDelta();

        groupingBar.Tick(deltaTime, wheelDelta, pointerPressed, pointerPosition, GetCarouselUICamera());
        pointerOverCategoryCarousel = groupingBar.PointerOverBar;

        // One notch must never move both strips.
        bool barOwnsWheel = pointerOverCategoryCarousel || groupingBar.IsPopupOpen;
        HandleCarouselWheel(barOwnsWheel ? 0f : wheelDelta);
        HandleCarouselDrag(deltaTime, pointerPressed, pointerPosition);
        UpdateCarouselMotion(deltaTime);
        LayoutCarouselCards();
        UpdateInlineDifficultyFade(deltaTime);
        analysisPanel.Tick(deltaTime);
        carouselPointerWasPressed = pointerPressed;
    }

    private void ApplyCarouselTuning()
    {
        songScroller.snapSmoothTime = carouselSnapSmoothTime;
        songScroller.maxSpeed = carouselMaxSpeed;
        songScroller.flickDamping = carouselFlickDamping;
        songScroller.settleDelay = carouselSettleDelay;
        songScroller.maxQueuedSteps = carouselMaxQueuedSteps;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyCarouselTuning();
    }
#endif

    /// <summary>
    /// The bookmark list vanishes the moment the strip starts moving and eases
    /// back in once the new song has settled, so it never appears to slide.
    /// </summary>
    private void UpdateInlineDifficultyFade(float deltaTime)
    {
        if (inlineDifficultyListRoot == null) return;
        if (carouselMoving || !inlineDifficultyListRoot.gameObject.activeSelf) return;

        CanvasGroup group = GetInlineDifficultyGroup();
        if (group == null || group.alpha >= 1f) return;
        group.alpha = Mathf.MoveTowards(group.alpha, 1f, 6f * deltaTime);
        group.interactable = group.alpha > 0.6f;
        group.blocksRaycasts = group.alpha > 0.6f;
    }

    private CanvasGroup GetInlineDifficultyGroup()
    {
        if (inlineDifficultyListRoot == null) return null;
        CanvasGroup group = inlineDifficultyListRoot.GetComponent<CanvasGroup>();
        if (group == null) group = inlineDifficultyListRoot.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private void HideInlineDifficultyList()
    {
        if (inlineDifficultyListRoot == null) return;
        CanvasGroup group = GetInlineDifficultyGroup();
        if (group != null)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        inlineDifficultyListRoot.gameObject.SetActive(false);
    }

    private static void ReadPointer(out bool pressed, out Vector2 screenPosition)
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        pressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
        screenPosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    #elif ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
        if (Mouse.current != null)
        {
            pressed = Mouse.current.leftButton.isPressed;
            screenPosition = Mouse.current.position.ReadValue();
        }
        else
        {
            pressed = UnityEngine.Input.GetMouseButton(0);
            screenPosition = UnityEngine.Input.mousePosition;
        }
    #else
        pressed = UnityEngine.Input.GetMouseButton(0);
        screenPosition = UnityEngine.Input.mousePosition;
    #endif
    }

    private static readonly List<RaycastResult> carouselRaycastResults = new List<RaycastResult>();

    /// <summary>
    /// Only the empty backdrop and the song cards themselves scrub the carousel.
    /// Pressing inside the library panel, the settings page or a bookmark list
    /// must be free to drag without dragging the song strip along with it.
    /// </summary>
    private bool IsPointerOnCarouselSurface(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return true;

        PointerEventData pointerData = new PointerEventData(eventSystem) { position = screenPosition };
        carouselRaycastResults.Clear();
        eventSystem.RaycastAll(pointerData, carouselRaycastResults);
        if (carouselRaycastResults.Count == 0) return true;

        Transform hit = carouselRaycastResults[0].gameObject.transform;
        for (int i = 0; i < carouselCards.Count; i++)
        {
            RectTransform root = carouselCards[i].slot?.root;
            if (root != null && (hit == root || hit.IsChildOf(root))) return true;
        }

        // Anything that owns its own interaction -- the library list, the
        // settings page, the bar's buttons -- keeps the pointer to itself.
        // Plain decoration such as the book backdrop does not, so a drag that
        // starts on the empty page still scrubs the strip.
        if (hit.GetComponentInParent<ScrollRect>() != null) return false;
        if (hit.GetComponentInParent<Selectable>() != null) return false;
        return selectionPanel == null || hit.IsChildOf(selectionPanel.transform);
    }

    private static float ReadWheelDelta()
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
    #elif ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
        if (Mouse.current != null) return Mouse.current.scroll.ReadValue().y;
        return UnityEngine.Input.mouseScrollDelta.y;
    #else
        return UnityEngine.Input.mouseScrollDelta.y;
    #endif
    }

    /// <summary>
    /// Wheel notches accumulate into the snap target instead of being rejected
    /// while an animation runs, which is what lets the wheel spin continuously.
    /// </summary>
    private void HandleCarouselWheel(float delta)
    {
        if (carouselDragActive || Mathf.Abs(delta) < 0.01f) return;
        int steps = songScroller.ConsumeWheel(delta);
        if (steps == 0) return;
        // Wheel up (positive delta) walks towards the previous song.
        NudgeCarousel(-steps);
    }

    /// <summary>
    /// Click-and-drag scrubbing.  A release hands the accumulated velocity to
    /// the inertia phase, which coasts and then snaps to the nearest song.
    /// </summary>
    private void HandleCarouselDrag(float deltaTime, bool pressed, Vector2 pointerPosition)
    {
        if (songOptions.Count <= 1)
        {
            carouselDragActive = false;
            carouselDragMoved = false;
            return;
        }

        float pointerX = pointerPosition.x;

        if (!carouselDragActive)
        {
            // Only a fresh press starts a drag, so a press that began on the
            // category bar cannot grab the song strip when the pointer wanders
            // over a card.
            if (!pressed || carouselPointerWasPressed) return;
            if (pointerOverCategoryCarousel || groupingBar.IsPopupOpen || difficultyLaunchInProgress) return;
            if (!IsPointerOnCarouselSurface(pointerPosition)) return;
            carouselDragActive = true;
            carouselDragMoved = false;
            carouselDragStartPointerX = pointerX;
            return;
        }

        if (pressed)
        {
            float pixels = pointerX - carouselDragStartPointerX;
            // Below the threshold this is still a click on a card or a bookmark.
            if (!carouselDragMoved && Mathf.Abs(pixels) < 14f) return;
            if (!carouselDragMoved)
            {
                carouselDragMoved = true;
                BeginCarouselMotion();
                songScroller.BeginDrag();
            }

            float pixelsPerSong = Mathf.Max(1f, carouselSlotSpacing * GetCarouselCanvasScale());
            songScroller.DragTo(songScroller.DragAnchorScroll - pixels / pixelsPerSong, deltaTime);
            return;
        }

        carouselDragActive = false;
        if (carouselDragMoved)
        {
            songScroller.EndDrag();
            // Swallow the click that the drag would otherwise deliver on release.
            carouselClickSuppressedUntil = Time.unscaledTime + 0.15f;
        }
        carouselDragMoved = false;
    }

    private bool IsCarouselClickSuppressed()
    {
        return carouselDragMoved || Time.unscaledTime < carouselClickSuppressedUntil;
    }

    private void EnsureCarouselCanvas()
    {
        if (carouselCanvas == null && centerSlot?.root != null)
            carouselCanvas = centerSlot.root.GetComponentInParent<Canvas>();
    }

    private float GetCarouselCanvasScale()
    {
        EnsureCarouselCanvas();
        return carouselCanvas != null ? Mathf.Max(0.01f, carouselCanvas.scaleFactor) : 1f;
    }

    /// <summary>Null for an overlay canvas, which is what the rect helpers expect.</summary>
    private Camera GetCarouselUICamera()
    {
        EnsureCarouselCanvas();
        if (carouselCanvas == null) return null;
        return carouselCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : carouselCanvas.worldCamera;
    }

    private void EnsureOptionClips(SongOption option)
    {
        if (option == null) return;

        if (option.audioClip == null && !string.IsNullOrWhiteSpace(option.audioResourcePath))
        {
            option.audioClip = LoadAudioClip(option.audioResourcePath, option.displayName);
        }

        if (option.pianoAudioClip == null && !string.IsNullOrWhiteSpace(option.pianoAudioResourcePath))
        {
            option.pianoAudioClip = LoadAudioClip(option.pianoAudioResourcePath, option.displayName + " (Piano)");
        }

        EnsurePreviewOverrideClip(option);
    }

    /// <summary>
    /// Finds something audible for a song whose gameplay track is silent.
    /// </summary>
    /// <remarks>
    /// A keysound-only song points <c>audioResourcePath</c> at a silent file --
    /// literally all zero samples -- so that the timeline runs while the notes
    /// the player strikes make every sound there is. That works in gameplay and
    /// leaves the song select screen mute, and "this song has no preview" and
    /// "this song is broken" look identical from the player's side.
    ///
    /// The library's convention is that the real recording sits beside the
    /// silent one under the same name: <c>X_silent</c> next to <c>X</c>. Looked
    /// up once and cached; a song that does not follow the convention simply
    /// finds nothing and behaves as before.
    /// </remarks>
    private void EnsurePreviewOverrideClip(SongOption option)
    {
        const string SilentSuffix = "_silent";
        if (option.previewOverrideChecked) return;
        option.previewOverrideChecked = true;

        if (!option.noBackgroundMusic) return;
        string path = option.audioResourcePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        // 兩條路。有真錄音的走第一條；完全沒有錄音的曲子（整個資料夾只有一份
        // 全零的 wav）由 qt_editor/render_song_preview.py 從譜面本身合出一段
        // <name>_preview.wav，用的就是遊戲合成這首歌時會播的同一批取樣。
        if (path.EndsWith(SilentSuffix, System.StringComparison.OrdinalIgnoreCase))
        {
            string audible = path.Substring(0, path.Length - SilentSuffix.Length);
            option.previewOverrideClip = LoadAudioClip(audible, option.displayName + " (Preview)");
        }

        if (option.previewOverrideClip == null)
            option.previewOverrideClip = LoadAudioClip(path + "_preview", option.displayName + " (Preview)");

        if (option.previewOverrideClip != null)
            BuildLogger.Log($"SongSelectionManager: '{option.displayName}' is keysound-only; previewing '{option.previewOverrideClip.name}'.");
    }

    private bool ShouldPlayPianoLayer(SongOption option)
    {
        if (option == null) return false;
        if (string.IsNullOrWhiteSpace(option.pianoAudioResourcePath)) return false;

        var settings = SettingsManager.Instance;
        if (settings == null) return false;
        bool enabled = settings.EnablePianoPreview;
            BuildLogger.Log($"SongSelectionManager: ShouldPlayPianoLayer '{option.displayName}' -> {enabled}");
        return enabled;
    }

    private void EnsurePreviewSources()
    {
        if (previewAudioSource == null)
        {
            previewAudioSource = GetComponent<AudioSource>();
            if (previewAudioSource == null)
            {
                previewAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        previewAudioSource.playOnAwake = false;
        previewAudioSource.loop = true;

        if (previewPianoAudioSource == null)
        {
            // Reuse the same GameObject to keep spatial/mixer settings consistent with the main preview.
            previewPianoAudioSource = gameObject.AddComponent<AudioSource>();
        }

        previewPianoAudioSource.playOnAwake = false;
        previewPianoAudioSource.loop = true;
        previewPianoAudioSource.spatialBlend = previewAudioSource.spatialBlend;
        previewPianoAudioSource.outputAudioMixerGroup = previewAudioSource.outputAudioMixerGroup;
    }

    private AudioSource EnsurePianoPreviewSource()
    {
        if (previewPianoAudioSource == null)
        {
            EnsurePreviewSources();
        }
        return previewPianoAudioSource;
    }

    private void PlayPreviewForOption(SongOption option, bool restart)
    {
        EnsurePreviewSources();
            BuildLogger.Log($"SongSelectionManager: PlayPreviewForOption called. restart={restart} option={(option != null ? option.displayName : "null")}");
        currentPreviewOption = option;

        if (previewAudioSource == null)
        {
            BuildLogger.LogWarning("SongSelectionManager: previewAudioSource is null; cannot play preview.");
            return;
        }

        if (!isActiveAndEnabled || option == null)
        {
            StopPreviewAudio();
            return;
        }

        EnsureOptionClips(option);

        var settings = SettingsManager.Instance;
        float musicVolume = settings != null ? settings.MusicVolume : 1f;
        float pianoVolume = settings != null ? settings.PianoVolume : musicVolume;

        // 沒有背景音樂軌的曲子（register.json 的 noBackgroundMusic）：遊玩時聲音
        // 全部來自打鍵音，但選歌畫面沒有人在打鍵，照原本的流程會直接靜音 ——
        // 「這首歌沒有預覽」和「這首歌壞了」在玩家那裡是同一件事。改成把鋼琴軌
        // 當主軌播，音量也跟著鋼琴走。
        AudioClip mainClip = option.previewOverrideClip != null ? option.previewOverrideClip : option.audioClip;
        float mainVolume = musicVolume;
        bool substituted = option.previewOverrideClip != null;
        if (mainClip == null && option.pianoAudioClip != null)
        {
            mainClip = option.pianoAudioClip;
            mainVolume = pianoVolume;
            substituted = true;
        }

        if (mainClip == null)
        {
            BuildLogger.LogWarning($"SongSelectionManager: No main audio clip available for '{option.displayName}'.");
            StopPreviewAudio();
            return;
        }

        // 響度校正。**這個畫面比遊戲中更需要它**：玩家在這裡一直在換歌，每換一
        // 首就被音量突襲一次；遊戲中至少一首只發生一次，而且那時候他還有心理
        // 準備。
        //
        // 增益從實際在播的那條主軌算（被鋼琴軌頂替的時候就是它），鋼琴預覽套
        // 同一個值 —— 和遊戲中是同一條規則、同一份量測，所以從選歌切進遊戲的
        // 時候音量不會跳。
        // 預覽也要把鋼琴分軌算進去，而且條件要和底下真的播不播完全一致 ——
        // 算進一條不會響的分軌，校正就會偏低。
        // 鋼琴軌已經當成主軌在播了，再疊一層就是同一份聲音播兩次。
        bool shouldPlayPiano = !substituted && ShouldPlayPianoLayer(option);
        AudioClip previewPiano = shouldPlayPiano ? option.pianoAudioClip : null;
        float previewGain = 1f;
        float previewPianoGain = 1f;
        try
        {
            // 離線量過就用那份資料，和遊戲中走同一條路 —— 從選歌切進遊戲時音量才
            // 不會跳。沒量過才退回這裡的估算，那時候兩軌共用同一個值。
            string mainPath = substituted ? option.pianoAudioResourcePath : option.audioResourcePath;
            if (!LoudnessNormalizer.TryStoredGains(mainPath, option.pianoAudioResourcePath,
                    previewPiano != null, out previewGain, out previewPianoGain))
            {
                previewGain = LoudnessNormalizer.GainFor(mainClip, previewPiano);
                previewPianoGain = previewGain;
            }
        }
        catch { }
        mainVolume = Mathf.Clamp01(mainVolume * previewGain);
        pianoVolume = Mathf.Clamp01(pianoVolume * previewPianoGain);

        bool needRestartMain = restart || previewAudioSource.clip != mainClip || !previewAudioSource.isPlaying;
        previewAudioSource.loop = true;
        if (needRestartMain)
        {
            previewAudioSource.Stop();
            previewAudioSource.clip = mainClip;
            previewAudioSource.time = 0f;
            previewAudioSource.volume = mainVolume;
            previewAudioSource.Play();
            Debug.Log($"[SongSelectionManager] Preview playback started: clip='{mainClip.name}' state={mainClip.loadState} volume={previewAudioSource.volume:0.###} mute={previewAudioSource.mute} active={previewAudioSource.gameObject.activeInHierarchy} substituted={substituted}");
        }
        else
        {
            previewAudioSource.volume = mainVolume;
        }

        if (restart)
        {
                BuildLogger.Log($"SongSelectionManager: Preview '{option.displayName}' main={(option.audioClip != null ? option.audioClip.name : "null")} piano={(option.pianoAudioClip != null ? option.pianoAudioClip.name : "null")} pianoEnabled={shouldPlayPiano}");
        }
        if (!shouldPlayPiano)
        {
            if (previewPianoAudioSource != null)
            {
                previewPianoAudioSource.Stop();
                previewPianoAudioSource.clip = null;
            }
                    try
                    {
                        var bg = FindAnyObjectByType<GameBackgroundManager>();
                        if (bg != null)
                        {
                            bg.SetSongDifficultyFromOption(option);
                        }
                    }
                    catch { }
            if (previewPianoAudioSource != null)
            {
                previewPianoAudioSource.Stop();
                previewPianoAudioSource.clip = null;
            }
            return;
        }

        var pianoSource = EnsurePianoPreviewSource();
        if (pianoSource == null)
        {
            return;
        }

        bool needRestartPiano = needRestartMain || pianoSource.clip != option.pianoAudioClip || !pianoSource.isPlaying;
        pianoSource.loop = true;
        if (needRestartPiano)
        {
            pianoSource.Stop();
            pianoSource.clip = option.pianoAudioClip;
            pianoSource.time = 0f;
            pianoSource.volume = pianoVolume;
            pianoSource.Play();
                BuildLogger.Log($"SongSelectionManager: Started piano preview for '{option.displayName}' clip={pianoSource.clip?.name} volume={pianoSource.volume}");
        }
        else
        {
            pianoSource.volume = pianoVolume;
        }
    }

    private void StopPreviewAudio(AudioClip preserveMain = null, AudioClip preservePiano = null)
    {
        currentPreviewOption = null;
        AudioClip mainClip = previewAudioSource != null ? previewAudioSource.clip : null;
        AudioClip pianoClip = previewPianoAudioSource != null ? previewPianoAudioSource.clip : null;
        if (previewAudioSource != null)
        {
            previewAudioSource.Stop();
            previewAudioSource.clip = null;
        }
        if (previewPianoAudioSource != null)
        {
            previewPianoAudioSource.Stop();
            previewPianoAudioSource.clip = null;
        }
        if (mainClip != preserveMain && mainClip != preservePiano)
            ReleasePreviewClipData(mainClip, null);
        if (pianoClip != mainClip && pianoClip != preserveMain && pianoClip != preservePiano)
            ReleasePreviewClipData(pianoClip, null);
    }

    private void ReleasePreviewClipData(AudioClip clip, AudioSource sourceUsingClip)
    {
        if (clip == null) return;
        if (sourceUsingClip != null && sourceUsingClip.clip == clip && sourceUsingClip.isPlaying) return;

        // AudioClips created by UnityWebRequest cannot reliably reload their
        // sample data after UnloadAudioData. Remove every cached/reference copy
        // so returning to song selection creates a fresh external clip.
        ForgetExternalPreviewClip(clip);

        try
        {
            if (clip.loadState != AudioDataLoadState.Unloaded)
                clip.UnloadAudioData();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SongSelectionManager] Could not release preview clip '{clip.name}': {ex.Message}");
        }
    }

    private void ForgetExternalPreviewClip(AudioClip clip)
    {
        if (clip == null) return;

        var keysToRemove = new List<string>();
        foreach (var pair in _audioCache)
        {
            if (pair.Value == clip && ExternalSongLibrary.ToLocalPath(pair.Key) != null)
                keysToRemove.Add(pair.Key);
        }

        if (keysToRemove.Count == 0)
            return;

        for (int i = 0; i < keysToRemove.Count; i++)
            _audioCache.Remove(keysToRemove[i]);

        var visited = new HashSet<SongOption>();
        ClearExternalClipReferences(allSongOptions, clip, visited);
        ClearExternalClipReferences(songOptions, clip, visited);
    }

    private static void ClearExternalClipReferences(
        IList<SongOption> options, AudioClip clip, HashSet<SongOption> visited)
    {
        if (options == null || clip == null) return;

        for (int i = 0; i < options.Count; i++)
        {
            SongOption option = options[i];
            if (option == null || !visited.Add(option)) continue;

            if (option.audioClip == clip &&
                ExternalSongLibrary.ToLocalPath(option.audioResourcePath) != null)
                option.audioClip = null;

            if (option.pianoAudioClip == clip &&
                ExternalSongLibrary.ToLocalPath(option.pianoAudioResourcePath) != null)
                option.pianoAudioClip = null;

            if (option.difficultyVariants == null) continue;
            for (int variantIndex = 0; variantIndex < option.difficultyVariants.Count; variantIndex++)
            {
                SongOption variant = option.difficultyVariants[variantIndex];
                if (variant == null || !visited.Add(variant)) continue;

                if (variant.audioClip == clip &&
                    ExternalSongLibrary.ToLocalPath(variant.audioResourcePath) != null)
                    variant.audioClip = null;

                if (variant.pianoAudioClip == clip &&
                    ExternalSongLibrary.ToLocalPath(variant.pianoAudioResourcePath) != null)
                    variant.pianoAudioClip = null;
            }
        }
    }

    private bool IsSelectionPanelActive()
    {
        return selectionPanel == null || selectionPanel.activeInHierarchy;
    }

    public void RefreshPreviewAudio(bool restart)
    {
            BuildLogger.Log($"SongSelectionManager: RefreshPreviewAudio called. restart={restart} selectionActive={IsSelectionPanelActive()} options={(songOptions != null ? songOptions.Count : 0)} currentIndex={currentIndex}");
        if (!IsSelectionPanelActive())
        {
            StopPreviewAudio();
            return;
        }

        if (songOptions == null || songOptions.Count == 0)
        {
            StopPreviewAudio();
            return;
        }

        int clampedIndex = Mathf.Clamp(currentIndex, 0, songOptions.Count - 1);
        SongOption option = songOptions[clampedIndex];
        if (restart || option?.audioClip == null || option.audioClip.loadState != AudioDataLoadState.Loaded)
        {
            BeginCarouselAudioLoadAfterAnimation();
            return;
        }
        PlayPreviewForOption(option, false);
    }

    public Sprite LoadCoverSprite(string resourcePath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        string externalPath = ExternalSongLibrary.ToLocalPath(resourcePath);
        if (!string.IsNullOrEmpty(externalPath) && File.Exists(externalPath))
        {
            if (_spriteCache.TryGetValue(resourcePath, out var externalCached) && externalCached != null)
                return externalCached;
            try
            {
                byte[] bytes = File.ReadAllBytes(externalPath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(bytes))
                {
                    texture.name = Path.GetFileNameWithoutExtension(externalPath);
                    var externalSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _spriteCache[resourcePath] = externalSprite;
                    return externalSprite;
                }
                Destroy(texture);
            }
            catch (Exception ex)
            {
                BuildLogger.LogWarning($"SongSelectionManager: Failed to load external cover '{externalPath}': {ex.Message}");
            }
            return null;
        }

        // 1) Try direct Sprite load (image imported as Sprite (2D and UI) or sliced sprites)
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            return sprite;
        }

        // 2) Try load from a sprite sheet (LoadAll will return all sliced sprites)
        Sprite[] sheetSprites = Resources.LoadAll<Sprite>(resourcePath);
        if (sheetSprites != null && sheetSprites.Length > 0)
        {
            return sheetSprites[0];
        }

        // 3) Fallback: load Texture2D and create a Sprite at runtime (covers images imported as Default)
        Texture2D tex = Resources.Load<Texture2D>(resourcePath);
        if (tex != null)
        {
            // Return cached sprite if we've already created one for this resource path
            if (_spriteCache.TryGetValue(resourcePath, out var cached) && cached != null)
            {
                return cached;
            }

            var created = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _spriteCache[resourcePath] = created;
            return created;
        }

        //Debug.LogWarning(
        //    $"SongSelectionManager: Cover art for '{displayName}' not found at Resources path '{resourcePath}'. " +
        //    "Ensure the image is placed under 'Assets/Resources', and its Texture Type is 'Sprite (2D and UI)' or provide a valid resource path without extension.");

        return null;
    }

    // ------ Auto discovery helpers ------
    private List<SongMetadata> DiscoverSongsFromResources(string rootPath)
    {
        List<SongMetadata> discoveredSongs = new List<SongMetadata>();
        try
        {
            // 先讀 songlist.json
            TextAsset songListAsset = Resources.Load<TextAsset>(rootPath + "/songlist");
            if (songListAsset == null) {
                Debug.LogWarning($"[SongSelectionManager] 無法載入 {rootPath}/songlist.json");
                return discoveredSongs;
            }
            List<KeyValuePair<string, string>> categoryFolders = ParseSongCategoryFolders(songListAsset.text);
            if (categoryFolders.Count == 0) {
                Debug.LogWarning("[SongSelectionManager] songlist.json contains no category folders.");
                return discoveredSongs;
            }
            foreach (var categoryFolder in categoryFolders)
            {
                string category = categoryFolder.Key;
                string folder = categoryFolder.Value;
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string path = $"{rootPath}/{folder}/register";
                var ta = Resources.Load<TextAsset>(path);
                Debug.Log($"[SongSelectionManager] 嘗試載入: {path} 結果: {(ta == null ? "null" : "OK")}");
                if (ta == null || string.IsNullOrEmpty(ta.text)) continue;
                string txt = ta.text.Trim();
                try
                {
                    var meta = JsonUtility.FromJson<SongMetadata>(txt);
                    if (meta != null && meta.difficulties != null && meta.difficulties.Count > 0)
                    {
                        if (string.IsNullOrWhiteSpace(meta.displayName))
                            meta.displayName = meta.difficulties[0].chartFileName;
                        meta.category = category;
                        discoveredSongs.Add(meta);
                        BuildLogger.Log($"SongSelectionManager: parsed register -> displayName='{meta.displayName}' difficulties={meta.difficulties.Count}");
                        continue;
                    }

                    // If initial parse did not produce expected difficulties, try a safer overwrite parse
                    BuildLogger.LogWarning($"[SongSelectionManager] 初次解析 {path} 未產生 difficulties，嘗試備援解析。文本長度={txt.Length}");
                    try
                    {
                        var fallback = new SongMetadata();
                        JsonUtility.FromJsonOverwrite(txt, fallback);
                        if (fallback != null && fallback.difficulties != null && fallback.difficulties.Count > 0)
                        {
                            if (string.IsNullOrWhiteSpace(fallback.displayName))
                                fallback.displayName = fallback.difficulties[0].chartFileName;
                            fallback.category = category;
                            discoveredSongs.Add(fallback);
                            BuildLogger.Log($"SongSelectionManager: fallback parsed register -> displayName='{fallback.displayName}' difficulties={fallback.difficulties.Count}");
                            continue;
                        }
                        // Log snippet to help debugging
                        int snippetLen = Math.Min(512, txt.Length);
                        BuildLogger.LogWarning($"[SongSelectionManager] 備援解析也失敗，前 {snippetLen} 字元: {txt.Substring(0, snippetLen).Replace('\n',' ')}");
                    }
                    catch (Exception ex2)
                    {
                        BuildLogger.LogWarning($"[SongSelectionManager] 備援解析 {path} 發生例外: {ex2.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SongSelectionManager] 解析 {path} 失敗: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex) {
            Debug.LogWarning($"[SongSelectionManager] DiscoverSongsFromResources 發生例外: {ex.Message}");
        }
        return discoveredSongs;
    }

    private List<KeyValuePair<string, string>> ParseSongCategoryFolders(string json)
    {
        var result = new List<KeyValuePair<string, string>>();
        discoveredCategoryOrder.Clear();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            JObject root = JObject.Parse(json);
            JObject categoryObject = root["categories"] as JObject;
            if (categoryObject != null)
            {
                foreach (JProperty categoryProperty in categoryObject.Properties())
                    AppendCategoryFolders(result, categoryProperty.Name, categoryProperty.Value);
                return result;
            }

            // Backward compatibility for the original { "folders": [...] } list.
            JToken legacyFolders = root["folders"];
            if (legacyFolders is JArray)
            {
                AppendCategoryFolders(result, "ALL SONGS", legacyFolders);
                return result;
            }

            // Concise form is accepted too: { "DEEMO": [...], "NOSTALGIA": [...] }.
            foreach (JProperty property in root.Properties())
            {
                if (property.Value is JArray)
                    AppendCategoryFolders(result, property.Name, property.Value);
            }
        }

        catch (Exception ex)
        {
            Debug.LogWarning($"[SongSelectionManager] Failed to parse categorized songlist.json: {ex.Message}");
        }
        return result;
    }

    private void AppendCategoryFolders(List<KeyValuePair<string, string>> destination,
        string category, JToken foldersToken)
    {
        if (destination == null || foldersToken == null || foldersToken.Type != JTokenType.Array) return;
        string normalizedCategory = string.IsNullOrWhiteSpace(category) ? "UNCATEGORIZED" : category.Trim();
        if (!discoveredCategoryOrder.Exists(item =>
                string.Equals(item, normalizedCategory, StringComparison.OrdinalIgnoreCase)))
            discoveredCategoryOrder.Add(normalizedCategory);

        foreach (JToken folderToken in foldersToken.Children())
        {
            if (folderToken.Type != JTokenType.String) continue;
            string folder = folderToken.Value<string>();
            if (!string.IsNullOrWhiteSpace(folder))
                destination.Add(new KeyValuePair<string, string>(normalizedCategory, folder.Trim()));
        }
    }

    // 用於解析 songlist.json
    [System.Serializable]
    private class SongFolderList
    {
        public List<string> folders;
    }

    private void MergeDiscoveredSongs(List<SongMetadata> baseList, List<SongMetadata> discovered)
    {
        if (baseList == null || discovered == null) return;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // collect existing chart paths from baseList (support both old and new formats)
        foreach (var s in baseList)
        {
            if (s == null) continue;
            if (!string.IsNullOrWhiteSpace(s.chartFileName)) existing.Add(s.chartFileName);
            if (s.difficulties != null)
            {
                foreach (var diff in s.difficulties) if (diff != null && !string.IsNullOrWhiteSpace(diff.chartFileName)) existing.Add(diff.chartFileName);
            }
        }

        foreach (var d in discovered)
        {
            if (d == null) continue;
            if (d.difficulties != null && d.difficulties.Count > 0)
            {
                foreach (var diff in d.difficulties)
                {
                    if (diff == null || string.IsNullOrWhiteSpace(diff.chartFileName)) continue;
                    if (!existing.Contains(diff.chartFileName))
                    {
                        // create a lightweight SongMetadata with a single difficulty to preserve original list shape
                        var single = new SongMetadata
                        {
                            displayName = d.displayName,
                            author = d.author,
                            difficulties = new List<SongDifficulty> { diff }
                        };
                        baseList.Add(single);
                        existing.Add(diff.chartFileName);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(d.chartFileName))
            {
                if (!existing.Contains(d.chartFileName))
                {
                    var single = new SongMetadata
                    {
                        displayName = d.displayName,
                        author = d.author,
                        difficulties = new List<SongDifficulty>
                        {
                            new SongDifficulty {
                                chartFileName = d.chartFileName,
                                audioResourcePath = d.audioResourcePath,
                                pianoAudioResourcePath = d.pianoAudioResourcePath,
                                coverResourcePath = d.coverResourcePath,
                                difficultyName = d.difficultyName,
                                difficultyLevel = d.difficultyLevel
                            }
                        }
                    };
                    baseList.Add(single);
                    existing.Add(d.chartFileName);
                }
            }
        }
    }

#if UNITY_EDITOR
    private void TryWriteBackSongListJson(SongListData data)
    {
        try
        {
            if (data == null) return;
            string assetsPath = Application.dataPath; // .../Assets
            string target = Path.Combine(assetsPath, "Resources", "song_list.json");
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(target, json);
            //Debug.Log($"SongSelectionManager: Wrote merged song list to {target}.");
            UnityEditor.AssetDatabase.Refresh();
        }
        catch (Exception)
        {
            //Debug.LogWarning($"SongSelectionManager: Failed to write back song_list.json");
        }
    }
#endif

    public void ShowNextSong()
    {
        if (IsCarouselClickSuppressed()) return;
        NudgeCarousel(1);
    }

    public void ShowPreviousSong()
    {
        if (IsCarouselClickSuppressed()) return;
        NudgeCarousel(-1);
    }

    /// <summary>
    /// Move the snap target by <paramref name="steps"/> songs.  Safe to call at
    /// any time: repeated calls stack instead of being dropped, so spinning the
    /// wheel produces one continuous glide.
    /// </summary>
    private void NudgeCarousel(int steps)
    {
        if (songOptions.Count <= 1 || steps == 0 || difficultyLaunchInProgress) return;
        songScroller.SetCount(songOptions.Count);
        if (!songScroller.Nudge(steps)) return;
        BeginCarouselMotion();
    }

    private void BeginCarouselMotion()
    {
        if (carouselMoving) return;
        carouselMoving = true;
        carouselTransitioning = true;
        CancelCarouselAudioLoad();
        StopPreviewAudio();
        HideChartAnalysis();
        // The bookmark list is rebuilt from scratch per song, far too expensive
        // to follow a moving card.  Hide it until the scroll settles.
        if (inlineDifficultyListRoot != null)
            inlineDifficultyListRoot.gameObject.SetActive(false);
    }

    private void UpdateCarouselMotion(float deltaTime)
    {
        songScroller.SetCount(songOptions.Count);
        if (songOptions.Count == 0) return;

        songScroller.Tick(deltaTime, out bool baseChanged, out bool settled);
        if (baseChanged) OnCarouselBaseIndexChanged();
        if (settled) CommitCarouselFocus();
    }

    /// <summary>
    /// The scroll has passed the halfway point, so the cards roll their songs
    /// forward by one.  Role 0 stays the card nearest the centre.
    /// </summary>
    private void OnCarouselBaseIndexChanged()
    {
        int count = songOptions.Count;
        if (count == 0) return;

        if (carouselDisplayedBase >= 0 && carouselDisplayedBase < count &&
            songOptions[carouselDisplayedBase] != null)
            songOptions[carouselDisplayedBase].selectedVariant = null;

        currentIndex = songScroller.BaseIndex;
        RefreshCarouselCardContents();
        UpdateCategoryCarouselLabel();
    }

    /// <summary>Bring the cards in line with the scroller without moving anything.</summary>
    private void EnsureCarouselContents()
    {
        if (songOptions.Count == 0) return;
        if (carouselDisplayedBase == songScroller.BaseIndex) return;
        OnCarouselBaseIndexChanged();
    }

    /// <summary>Everything too expensive to run per frame happens here, once, on settle.</summary>
    private void CommitCarouselFocus()
    {
        carouselMoving = false;
        carouselTransitioning = false;
        if (songOptions.Count == 0) return;

        currentIndex = songScroller.BaseIndex;
        SongOption focused = songOptions[currentIndex];
        UpdateDetailPanel(focused);
        // Difficulty selection now lives inside the opened full-screen score
        // volume, so the old bookmarks must not obscure the book cover.
        HideInlineDifficultyList();
        UpdateCategoryCarouselLabel();

        try
        {
            OnSelectionChanged?.Invoke(focused);
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"SongSelectionManager: OnSelectionChanged handler threw: {ex.Message}");
        }

        if (IsSelectionPanelActive()) BeginCarouselAudioLoadAfterAnimation();
    }

    private void SyncCarouselToIndex(bool rebuildContents)
    {
        songScroller.SetCount(songOptions.Count);
        currentIndex = songOptions.Count > 0 ? GetWrappedIndex(currentIndex) : 0;
        songScroller.SnapTo(currentIndex);
        carouselMoving = false;
        carouselTransitioning = false;
        carouselDragActive = false;
        carouselDragMoved = false;
        if (rebuildContents) carouselDisplayedBase = int.MinValue;
    }

    // ---- Card pool --------------------------------------------------------

    /// <summary>
    /// Five cards (roles -2..2) so a card is always available on both edges
    /// while the strip sits half a step between two songs.  The outermost two
    /// are runtime clones of the authored side card.
    /// </summary>
    private void EnsureCarouselCards()
    {
        if (carouselCards.Count > 0) return;
        if (centerSlot == null || centerSlot.root == null) return;

        int insertIndex = centerSlot.root.GetSiblingIndex();
        if (leftSlot?.root != null) insertIndex = Mathf.Min(insertIndex, leftSlot.root.GetSiblingIndex());
        if (rightSlot?.root != null) insertIndex = Mathf.Min(insertIndex, rightSlot.root.GetSiblingIndex());

        // Cloned below the authored cards, which keeps the existing draw order:
        // outer cards behind the neighbours, neighbours behind the centre.  The
        // ordering is correct for every scroll position because role 0 is always
        // the card closest to the middle.
        RegisterCarouselCard(CloneSideSlot("SongCard_FarLeft", insertIndex), -2);
        RegisterCarouselCard(CloneSideSlot("SongCard_FarRight", insertIndex), 2);
        RegisterCarouselCard(leftSlot, -1);
        RegisterCarouselCard(rightSlot, 1);
        RegisterCarouselCard(centerSlot, 0);

        // The panel belongs to the focused card, so it inherits the card's pose.
        analysisPanel.Build(centerSlot.root);

    }

    private void RegisterCarouselCard(SongPreviewSlot slot, int role)
    {
        if (slot == null || slot.root == null) return;
        CanvasGroup group = slot.root.GetComponent<CanvasGroup>();
        if (group == null) group = slot.root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        carouselCards.Add(new CarouselCard { slot = slot, role = role, group = group });
    }

    private SongPreviewSlot CloneSideSlot(string cloneName, int siblingIndex)
    {
        if (leftSlot == null || leftSlot.root == null || leftSlot.root.parent == null) return null;

        string coverPath = GetRelativePath(leftSlot.root, leftSlot.coverImage != null ? leftSlot.coverImage.transform : null);
        string titlePath = GetRelativePath(leftSlot.root, leftSlot.titleLabel != null ? leftSlot.titleLabel.transform : null);
        string difficultyPath = GetRelativePath(leftSlot.root, leftSlot.difficultyLabel != null ? leftSlot.difficultyLabel.transform : null);
        string authorPath = GetRelativePath(leftSlot.root, leftSlot.authorLabel != null ? leftSlot.authorLabel.transform : null);

        GameObject clone = Instantiate(leftSlot.root.gameObject, leftSlot.root.parent, false);
        clone.name = cloneName;
        RectTransform root = clone.GetComponent<RectTransform>();
        if (root == null)
        {
            Destroy(clone);
            return null;
        }
        root.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, root.parent.childCount - 1));

        // The bookmark list only ever belongs to the focused card.
        Transform strayList = root.Find("InlineDifficultyList");
        if (strayList != null) Destroy(strayList.gameObject);

        // Decorative copies must never take clicks.
        foreach (Button button in clone.GetComponentsInChildren<Button>(true))
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }
        foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        return new SongPreviewSlot
        {
            root = root,
            coverImage = FindByPath<Image>(root, coverPath),
            titleLabel = FindByPath<TextMeshProUGUI>(root, titlePath),
            difficultyLabel = FindByPath<TextMeshProUGUI>(root, difficultyPath),
            authorLabel = FindByPath<TextMeshProUGUI>(root, authorPath),
        };
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == null || target == null) return null;
        if (target == root) return string.Empty;
        string path = target.name;
        Transform cursor = target.parent;
        while (cursor != null && cursor != root)
        {
            path = cursor.name + "/" + path;
            cursor = cursor.parent;
        }
        return cursor == root ? path : null;
    }

    private static T FindByPath<T>(Transform root, string path) where T : Component
    {
        if (root == null || path == null) return null;
        Transform node = path.Length == 0 ? root : root.Find(path);
        return node != null ? node.GetComponent<T>() : null;
    }

    private void RefreshCarouselCardContents()
    {
        int count = songOptions.Count;
        for (int i = 0; i < carouselCards.Count; i++)
        {
            CarouselCard card = carouselCards[i];
            SongOption option = count > 0
                ? songOptions[GetWrappedIndex(songScroller.BaseIndex + card.role)]
                : null;
            UpdatePreviewSlot(card.slot, option, card.role == 0);
        }
        carouselDisplayedBase = count > 0 ? songScroller.BaseIndex : int.MinValue;
    }

    /// <summary>Position, scale and fade every card from the current scroll position.</summary>
    private void LayoutCarouselCards()
    {
        int count = songOptions.Count;
        if (carouselCards.Count == 0) return;

        float frac = songScroller.Frac;
        float centerY = centerBookRestPosition.y;
        float sideY = leftBookRestPosition.y;
        int maxRole = count >= 5 ? 2 : (count >= 2 ? 1 : 0);

        for (int i = 0; i < carouselCards.Count; i++)
        {
            CarouselCard card = carouselCards[i];
            if (card.slot?.root == null) continue;

            float distance = card.role - frac;
            float absDistance = Mathf.Abs(distance);
            float weight = Mathf.Clamp01(absDistance);
            float eased = weight * weight * (3f - 2f * weight);
            float alpha = Mathf.Clamp01((2.3f - absDistance) / 0.7f);

            bool visible = count > 0 && alpha > 0.01f && Mathf.Abs(card.role) <= maxRole;
            if (card.slot.root.gameObject.activeSelf != visible)
                card.slot.root.gameObject.SetActive(visible);
            if (!visible) continue;

            // Every property here is a function of `distance`, never of `role`,
            // which is what keeps the midpoint handover invisible: the card
            // taking over a song inherits the exact pose the outgoing card had.
            card.slot.root.anchoredPosition =
                new Vector2(carouselSlotSpacing * distance, Mathf.Lerp(centerY, sideY, eased));
            card.slot.root.localScale = Vector3.one * Mathf.Lerp(centerScale, sideScale, eased);
            if (card.group != null) card.group.alpha = alpha;
        }
    }

    private void CancelCarouselAudioLoad()
    {
        carouselAudioLoadVersion++;
        if (carouselAudioLoadRoutine != null)
        {
            StopCoroutine(carouselAudioLoadRoutine);
            carouselAudioLoadRoutine = null;
        }
        ReleasePreviewClipData(carouselLoadingMainClip, previewAudioSource);
        ReleasePreviewClipData(carouselLoadingPianoClip, previewPianoAudioSource);
        carouselLoadingMainClip = null;
        carouselLoadingPianoClip = null;
    }

    private void BeginCarouselAudioLoadAfterAnimation()
    {
        CancelCarouselAudioLoad();
        int requestVersion = carouselAudioLoadVersion;
        int requestedIndex = currentIndex;
        carouselAudioLoadRoutine = StartCoroutine(
            LoadFocusedPreviewAfterAnimation(requestedIndex, requestVersion));
    }

    private IEnumerator LoadFocusedPreviewAfterAnimation(int requestedIndex, int requestVersion)
    {
        // Let the final book pose reach the screen before any Resources/audio
        // work begins.  This guarantees that loading cannot interrupt movement.
        yield return null;

        if (requestVersion != carouselAudioLoadVersion || carouselTransitioning ||
            requestedIndex != currentIndex || !IsSelectionPanelActive())
        {
            carouselAudioLoadRoutine = null;
            yield break;
        }

        SongOption option = songOptions[GetWrappedIndex(requestedIndex)];
        EnsureOptionClips(option);

        AudioClip mainClip = option != null ? option.audioClip : null;
        if (option != null && mainClip == null &&
            ExternalSongLibrary.ToLocalPath(option.audioResourcePath) != null)
        {
            yield return PreloadClipAsync(option.audioResourcePath, clip =>
            {
                if (option != null) option.audioClip = clip;
                mainClip = clip;
            });

            if (requestVersion != carouselAudioLoadVersion || carouselTransitioning ||
                requestedIndex != currentIndex || !IsSelectionPanelActive())
            {
                carouselAudioLoadRoutine = null;
                yield break;
            }
        }

        carouselLoadingMainClip = mainClip;
        if (mainClip != null && mainClip.loadState != AudioDataLoadState.Loaded)
        {
            if (mainClip.loadState == AudioDataLoadState.Failed)
                mainClip.UnloadAudioData();
            bool loadStarted = mainClip.LoadAudioData();
            if (!loadStarted && mainClip.loadState != AudioDataLoadState.Loading &&
                mainClip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError($"[SongSelectionManager] Preview audio load could not start: path='{option.audioResourcePath}' clip='{mainClip.name}' state={mainClip.loadState}");
            }
            while (mainClip.loadState == AudioDataLoadState.Loading)
            {
                if (requestVersion != carouselAudioLoadVersion)
                {
                    carouselAudioLoadRoutine = null;
                    yield break;
                }
                yield return null;
            }
            if (mainClip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError($"[SongSelectionManager] Preview audio failed to load: path='{option.audioResourcePath}' clip='{mainClip.name}' state={mainClip.loadState}");
            }
            else
            {
                Debug.Log($"[SongSelectionManager] Preview audio loaded: path='{option.audioResourcePath}' clip='{mainClip.name}' length={mainClip.length:0.###}s");
            }
        }

        if (option != null && ShouldPlayPianoLayer(option))
        {
            AudioClip pianoClip = option.pianoAudioClip;
            if (pianoClip == null &&
                ExternalSongLibrary.ToLocalPath(option.pianoAudioResourcePath) != null)
            {
                yield return PreloadClipAsync(option.pianoAudioResourcePath, clip =>
                {
                    if (option != null) option.pianoAudioClip = clip;
                    pianoClip = clip;
                });

                if (requestVersion != carouselAudioLoadVersion || carouselTransitioning ||
                    requestedIndex != currentIndex || !IsSelectionPanelActive())
                {
                    carouselAudioLoadRoutine = null;
                    yield break;
                }
            }

            carouselLoadingPianoClip = pianoClip;
            if (pianoClip != null && pianoClip.loadState != AudioDataLoadState.Loaded)
            {
                if (pianoClip.loadState == AudioDataLoadState.Failed)
                    pianoClip.UnloadAudioData();
                pianoClip.LoadAudioData();
                while (pianoClip.loadState == AudioDataLoadState.Loading)
                {
                    if (requestVersion != carouselAudioLoadVersion)
                    {
                        carouselAudioLoadRoutine = null;
                        yield break;
                    }
                    yield return null;
                }
            }
        }

        if (requestVersion == carouselAudioLoadVersion && !carouselTransitioning &&
            requestedIndex == currentIndex && IsSelectionPanelActive())
        {
            if (mainClip != null && mainClip.loadState == AudioDataLoadState.Loaded)
                PlayPreviewForOption(option, true);
            // Neighbour metadata can be prepared only after the focused preview;
            // it must never compete with the book animation or selected song.
            PreloadAroundIndex(requestedIndex);
        }

        carouselLoadingMainClip = null;
        carouselLoadingPianoClip = null;
        carouselAudioLoadRoutine = null;
    }

    private static float SmoothStep01(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * (3f - 2f * t);
    }

    private void RestoreBookPose()
    {
        if (!bookRestPoseCached) return;
        SyncCarouselToIndex(false);
        if (carouselCards.Count == 0)
        {
            RestoreBook(centerSlot?.root, centerBookRestPosition, centerScale);
            RestoreBook(leftSlot?.root, leftBookRestPosition, sideScale);
            RestoreBook(rightSlot?.root, rightBookRestPosition, sideScale);
            return;
        }
        EnsureCarouselContents();
        LayoutCarouselCards();
    }

    private static void RestoreBook(RectTransform book, Vector2 position, float scale)
    {
        if (book == null) return;
        book.anchoredPosition = position;
        book.localScale = Vector3.one * scale;
        CanvasGroup group = book.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = 1f;
    }

    private void SetCarouselInteraction(bool enabled)
    {
        if (leftSlot?.selectButton != null) leftSlot.selectButton.interactable = enabled;
        if (rightSlot?.selectButton != null) rightSlot.selectButton.interactable = enabled;
    }

    private void SelectCurrentSong()
    {
        // The whole book takes clicks now, so a drag that ends on it must not
        // also start the song.
        if (IsCarouselClickSuppressed() || carouselMoving || difficultyLaunchInProgress) return;
    #if UNITY_EDITOR
    Debug.Log("[SongSelectionManager] SelectCurrentSong 被呼叫");
    #endif
    //Debug.Log($"SongSelectionManager: Center cover clicked → start index {currentIndex}.");
    SelectSong(currentIndex);
    }

    private void UpdateCarouselVisuals(bool refreshAudio = true)
    {
        UpdateCategoryCarouselLabel();
        if (!isActiveAndEnabled)
        {
            return;
        }

        EnsureCarouselCards();
        SyncCarouselToIndex(true);

        if (songOptions.Count == 0)
        {
            for (int i = 0; i < carouselCards.Count; i++)
                UpdatePreviewSlot(carouselCards[i].slot, null, carouselCards[i].role == 0);
            if (currentSongLabel != null)
            {
                currentSongLabel.text = Localize.T("沒有曲目", "没有曲目", "No Songs");
                ClassicalBookUITheme.ApplyLocalizedFont(currentSongLabel);
            }
            if (authorLabel != null)
            {
                authorLabel.text = string.Empty;
                authorLabel.gameObject.SetActive(false);
            }
            return;
        }

        SongOption centerOption = songOptions[currentIndex];

        EnsureCarouselContents();
        RefreshCarouselCardContents();
        LayoutCarouselCards();
        UpdateDetailPanel(centerOption);
        HideInlineDifficultyList();

        // Notify subscribers that the currently-focused selection changed (live preview)
        try
        {
            OnSelectionChanged?.Invoke(centerOption);
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"SongSelectionManager: OnSelectionChanged handler threw: {ex.Message}");
        }

        if (refreshAudio && IsSelectionPanelActive())
        {
            // Player builds do not implicitly load sample data when an unloaded
            // clip is passed to AudioSource.Play. Use the same explicit async
            // path for initial display, returning from gameplay and book moves.
            BeginCarouselAudioLoadAfterAnimation();
        }
        else if (refreshAudio)
        {
            StopPreviewAudio();
        }
    }

    private void UpdatePreviewSlot(SongPreviewSlot slot, SongOption option, bool isCenter)
    {
        if (slot == null || slot.root == null)
        {
            return;
        }

        bool hasOption = option != null;
        if (slot.root.gameObject.activeSelf != hasOption)
        {
            slot.root.gameObject.SetActive(hasOption);
        }

        // 每張卡都有自己的難度牌，而且就在這裡跟著換 —— 這是卡片拿到曲子的唯一
        // 入口，所以輪播把某張卡交接給下一首時，牌面一定同時換過去。掛在中央那
        // 一張的話，左右兩張就永遠是空的。
        SongDifficultyStrip strip = SongDifficultyStrip.Attach(slot.root);
        if (strip != null) strip.Show(option);

        if (!hasOption)
        {
            if (slot.authorLabel != null)
            {
                slot.authorLabel.text = string.Empty;
                slot.authorLabel.gameObject.SetActive(false);
            }
            return;
        }

        if (slot.coverImage != null)
        {
            if (option.coverSprite != null)
            {
                slot.coverImage.sprite = option.coverSprite;
                slot.coverImage.enabled = true;
            }
            else
            {
                slot.coverImage.enabled = false;
            }
        }

        if (slot.titleLabel != null)
        {
            slot.titleLabel.text = option.displayName;
            ClassicalBookUITheme.ApplyContentFont(slot.titleLabel);
        }

        if (slot.difficultyLabel != null)
        {
            slot.difficultyLabel.text = BuildDifficultyText(option);
        }

        if (slot.authorLabel != null)
        {
            string authorText = option.author ?? string.Empty;
            slot.authorLabel.text = authorText;
            ClassicalBookUITheme.ApplyContentFont(slot.authorLabel);
            slot.authorLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(authorText));
        }

        ApplySlotScale(slot.root, isCenter);
    }

    private void ApplySlotScale(RectTransform slotTransform, bool isCenter)
    {
        if (slotTransform == null)
        {
            return;
        }

        float targetScale = isCenter ? centerScale : sideScale;
        slotTransform.localScale = Vector3.one * targetScale;
    }

    private void UpdateDetailPanel(SongOption option)
    {

        // If player has explicitly chosen a variant for this group, prefer showing that
        SongOption toShow = option;
        if (option != null && option.selectedVariant != null)
        {
            toShow = option.selectedVariant;
        }

        SetSelectionAtmosphereDifficulty(GetAtmosphereName(toShow),
            GetAtmosphereDifficulty(toShow));

        if (currentSongLabel != null)
        {
            currentSongLabel.text = toShow != null ? toShow.displayName : string.Empty;
            ClassicalBookUITheme.ApplyContentFont(currentSongLabel);
        }

        if (authorLabel != null)
        {
            string text = toShow != null ? toShow.author ?? string.Empty : string.Empty;
            authorLabel.text = text;
            ClassicalBookUITheme.ApplyContentFont(authorLabel);
            authorLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(text));
        }

        if (centerSlot != null)
        {
            if (centerSlot.difficultyLabel != null)
            {
                centerSlot.difficultyLabel.text = BuildDifficultyText(toShow);
            }

            if (centerSlot.authorLabel != null)
            {
                string authorText = toShow != null ? toShow.author ?? string.Empty : string.Empty;
                centerSlot.authorLabel.text = authorText;
                ClassicalBookUITheme.ApplyContentFont(centerSlot.authorLabel);
                centerSlot.authorLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(authorText));
            }

            if (centerSlot.coverImage != null)
            {
                if (toShow != null && toShow.coverSprite != null)
                {
                    centerSlot.coverImage.sprite = toShow.coverSprite;
                    centerSlot.coverImage.enabled = true;
                }
                else
                {
                    centerSlot.coverImage.enabled = false;
                }
            }
        }
    }

    private string BuildDifficultyText(SongOption option)
    {
        string baseText = BuildDifficultyTextCore(option);
        int best = 0;
        if (option != null && option.selectedVariant != null)
        {
            best = LocalScoreRecords.GetBestScore(option.selectedVariant);
        }
        else if (option != null && option.difficultyVariants != null &&
                 option.difficultyVariants.Count > 0)
        {
            for (int i = 0; i < option.difficultyVariants.Count; i++)
                best = Mathf.Max(
                    best, LocalScoreRecords.GetBestScore(option.difficultyVariants[i]));
        }
        else
        {
            best = LocalScoreRecords.GetBestScore(option);
        }
        return best > 0 ? $"{baseText}   BEST {best:N0}" : baseText;
    }

    private string BuildDifficultyTextCore(SongOption option)
    {
        if (option == null) return string.Empty;

        // If grouped variants exist, show range like "Lv.18~16"
        if (option.selectedVariant != null)
        {
            // If player selected a concrete variant for this group, show it
            var v = option.selectedVariant;
            if (v == null) return string.Empty;
            if (string.IsNullOrWhiteSpace(v.difficultyName))
            {
                return v.difficultyLevel > 0 ? $"Lv. {v.difficultyLevel}" : string.Empty;
            }
            return v.difficultyLevel > 0 ? $"{v.difficultyName} Lv. {v.difficultyLevel}" : v.difficultyName;
        }

        if (option.difficultyVariants != null && option.difficultyVariants.Count > 1)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            bool hasLevel = false;
            var names = new HashSet<string>();
            foreach (var v in option.difficultyVariants)
            {
                if (v == null) continue;
                if (!string.IsNullOrWhiteSpace(v.difficultyName)) names.Add(v.difficultyName);
                if (v.difficultyLevel > 0)
                {
                    hasLevel = true;
                    if (v.difficultyLevel < min) min = v.difficultyLevel;
                    if (v.difficultyLevel > max) max = v.difficultyLevel;
                }
            }

            if (hasLevel && min <= max)
            {
                if (min == max) return $"Lv. {max}";
                return $"Lv. {max}~{min}";
            }

            // Fallback to listing names if no numeric levels available
            if (names.Count > 1) return string.Join("/", names);
            if (names.Count == 1) foreach (var n in names) return n;
            // otherwise fallback to single variant display
        }

        if (string.IsNullOrWhiteSpace(option.difficultyName))
        {
            return option.difficultyLevel > 0 ? $"Lv. {option.difficultyLevel}" : string.Empty;
        }

        return option.difficultyLevel > 0
            ? $"{option.difficultyName} Lv. {option.difficultyLevel}"
            : option.difficultyName;
    }

    private int GetWrappedIndex(int index)
    {
        if (songOptions.Count == 0)
        {
            return 0;
        }

        int wrapped = index % songOptions.Count;
        if (wrapped < 0)
        {
            wrapped += songOptions.Count;
        }

        return wrapped;
    }

    public void SelectSong(int index)
    {
    #if UNITY_EDITOR
    Debug.Log($"[SongSelectionManager] SelectSong 被呼叫 index={index}");
    #endif
    //Debug.Log($"SongSelectionManager: SelectSong called with index {index}.");
        if (index < 0 || index >= songOptions.Count)
        {
            //Debug.LogWarning($"SelectSong index {index} is out of range.");
            return;
        }

        SongOption option = songOptions[index];
        GameManager manager = GameManager.Instance;
        if (manager == null)
        {
            //Debug.LogError("GameManager instance not found. Cannot start song.");
            return;
        }

        currentIndex = index;
        UpdateCarouselVisuals();

        SongScoreBookOverlay.Open(option, choice =>
        {
            if (choice == null) return;
            option.selectedVariant = choice;
            SongDifficultyStrip.RememberChoice(choice);
            UpdateDetailPanel(option);
            StartChosenSong(choice);
        });
    }

    private void StartChosenSong(SongOption option)
    {
        if (option == null) return;
        GameManager manager = GameManager.Instance;
        if (manager == null) return;

        // The score book transition always enters a fixed, readable three-second
        // count-in. This is intentionally not persisted over the user's setting.
        manager.SetStartDelay(3f);

        // Ensure audio clips loaded
        AudioClip clipToUse = option.audioClip;
        if ((clipToUse == null) && !string.IsNullOrEmpty(option.audioResourcePath))
        {
            _audioCache.TryGetValue(option.audioResourcePath, out clipToUse);
            if (clipToUse == null)
            {
                clipToUse = LoadAudioClip(option.audioResourcePath, option.displayName);
            }
            if (clipToUse != null) option.audioClip = clipToUse;
        }
        AudioClip pianoClipToUse = option.pianoAudioClip;
        if ((pianoClipToUse == null) && !string.IsNullOrEmpty(option.pianoAudioResourcePath))
        {
            pianoClipToUse = LoadAudioClip(option.pianoAudioResourcePath, option.displayName + " (Piano)");
            if (pianoClipToUse != null) option.pianoAudioClip = pianoClipToUse;
        }

        bool externalMain = ExternalSongLibrary.ToLocalPath(option.audioResourcePath) != null;
        bool externalPiano = ExternalSongLibrary.ToLocalPath(option.pianoAudioResourcePath) != null;
        // Gameplay owns a fresh external clip. Preview clips belong to the
        // selection screen and may be unloaded when that screen closes.
        if (externalMain) clipToUse = null;
        if (externalPiano) pianoClipToUse = null;
        StopPreviewAudio(clipToUse, pianoClipToUse);
        // External audio is loaded by a coroutine on this component. Keep the
        // selection hierarchy active until loading has finished, then switch
        // panels immediately before GameManager starts the chart.
        HideSelection(clipToUse, pianoClipToUse);
        SetGameplayHudPresentationVisible(true);
        // Debug: log the chart path when starting chosen difficulty
        try { BuildLogger.Log($"[SongSelectionManager] StartChosenSong: chartFile='{option.chartFileName}' displayName='{option.displayName}' audioPath='{option.audioResourcePath}'"); } catch { }
        manager.StartSong(option.chartFileName, clipToUse, option.displayName, pianoClipToUse,
            option.pianoAudioResourcePath, option.audioResourcePath);
        if (currentSongLabel != null) currentSongLabel.text = option.displayName;
        if (authorLabel != null)
        {
            var authorText = option.author ?? string.Empty;
            authorLabel.text = authorText;
            authorLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(authorText));
        }
        try
        {
            var bg = FindAnyObjectByType<GameBackgroundManager>();
            if (bg != null) bg.SetSongDifficultyFromOption(option);
        }
        catch { }
    }

    private void ShowDifficultySelector(SongOption groupPrimary, List<SongOption> options, Action<SongOption> onSelected)
    {
        if (groupPrimary == null || options == null || options.Count == 0) return;
        if (difficultySelectorInstance != null) Destroy(difficultySelectorInstance);

        // Find a parent canvas
        // SelectSong hides SelectionPanel before this method runs. Resolve the
        // canvas from the panel's still-active parent, not from an inactive label
        // and not from an arbitrary gameplay canvas.
        Canvas parentCanvas = null;
        if (selectionPanel != null && selectionPanel.transform.parent != null)
            parentCanvas = selectionPanel.transform.parent.GetComponentInParent<Canvas>();
        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null && currentSongLabel != null) parentCanvas = currentSongLabel.canvas;
        if (parentCanvas == null)
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null && canvases[i].isRootCanvas)
                {
                    parentCanvas = canvases[i];
                    break;
                }
            }
        }
        if (parentCanvas == null)
        {
            BuildLogger.LogWarning("SongSelectionManager: No Canvas found for difficulty selector.");
            return;
        }

        // Create root panel
        var root = new GameObject("DifficultySelector");
        root.layer = parentCanvas.gameObject.layer;
        root.transform.SetParent(parentCanvas.transform, false);
        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        // Stay in the selection canvas coordinate system so the authored centered
        // position remains stable. Last sibling is sufficient for this modal.
        root.transform.SetAsLastSibling();

        // Clear gameplay HUD difficulty while waiting for player's explicit choice
        try
        {
            var bg = FindAnyObjectByType<GameBackgroundManager>();
            if (bg != null) bg.SetSongDifficulty(string.Empty);
        }
        catch { }

        var img = root.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);

        // Container for buttons
        var containerGO = new GameObject("Container");
        containerGO.transform.SetParent(root.transform, false);
        var containerRT = containerGO.AddComponent<RectTransform>();
        float selectorHeight = 104f;
        for (int i = 0; i < options.Count; i++)
            selectorHeight += 52f +
                LocalScoreRecords.GetTopScores(options[i]).Count * 30f;
        containerRT.sizeDelta = new Vector2(600, Mathf.Min(760f, selectorHeight));
        containerRT.anchorMin = new Vector2(0.5f, 0.5f); containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.anchoredPosition = Vector2.zero;

        var containerImg = containerGO.AddComponent<UnityEngine.UI.Image>();
        containerImg.color = ClassicalBookUITheme.ParchmentLight;

        var layout = containerGO.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.childControlHeight = true; layout.childControlWidth = true; layout.spacing = 8; layout.padding = new RectOffset(12,12,12,12);

        // Title
        var titleGO = new GameObject("Title"); titleGO.transform.SetParent(containerGO.transform, false);
        var titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = groupPrimary.displayName + Localize.T(
            " — 選擇難度", " — 选择难度", " — Select Difficulty");
        titleText.fontSize = 24; titleText.alignment = TMPro.TextAlignmentOptions.Center;
        var titleRT = titleGO.GetComponent<RectTransform>(); titleRT.sizeDelta = new Vector2(0, 36);

        // Buttons for each difficulty
        var bgManager = FindAnyObjectByType<GameBackgroundManager>();
        foreach (var opt in options)
        {
            var optLocal = opt;
            IReadOnlyList<LocalScoreEntry> topScores =
                LocalScoreRecords.GetTopScores(optLocal);
            var btnGO = new GameObject("Btn_" + (opt.difficultyName ?? "diff"));
            btnGO.transform.SetParent(containerGO.transform, false);
            var btn = btnGO.AddComponent<UnityEngine.UI.Button>();
            var bimg = btnGO.AddComponent<UnityEngine.UI.Image>(); bimg.color = ClassicalBookUITheme.Wood;
            var brt = btnGO.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(0, 44 + topScores.Count * 30f);

            var lblGO = new GameObject("Label"); lblGO.transform.SetParent(btnGO.transform, false);
            var lbl = lblGO.AddComponent<TextMeshProUGUI>(); lbl.text = (optLocal.difficultyName ?? "") + (optLocal.difficultyLevel>0? $" Lv. {optLocal.difficultyLevel}": "");
            lbl.alignment = TMPro.TextAlignmentOptions.Center; lbl.fontSize = 20;
            var lblRT = lblGO.GetComponent<RectTransform>(); lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
            if (topScores.Count > 0)
            {
                lblRT.anchorMin = new Vector2(0f, 1f);
                lblRT.anchorMax = Vector2.one;
                lblRT.pivot = new Vector2(0.5f, 1f);
                lblRT.sizeDelta = new Vector2(0f, 38f);
                lblRT.anchoredPosition = Vector2.zero;
                for (int scoreIndex = 0; scoreIndex < topScores.Count; scoreIndex++)
                    AddInlineLocalScoreRow(btnGO.transform, topScores[scoreIndex], scoreIndex);
            }

            btn.onClick.AddListener(() => {
                try {
                    // Remember this variant on the group primary so UI won't revert to range
                    groupPrimary.selectedVariant = optLocal;
                    // Update detail panel to reflect chosen variant while selection closes
                    try { UpdateDetailPanel(groupPrimary); } catch { }
                    if (bgManager != null) bgManager.SetSongDifficultyFromOption(optLocal);
                    onSelected?.Invoke(optLocal);
                } catch { }
                Destroy(root);
                difficultySelectorInstance = null;
            });
            ClassicalBookUITheme.StyleButton(btn, true);
        }

        // Cancel button
        var cancelGO = new GameObject("Cancel"); cancelGO.transform.SetParent(containerGO.transform, false);
        var cancelBtn = cancelGO.AddComponent<UnityEngine.UI.Button>();
        var cancelImg = cancelGO.AddComponent<UnityEngine.UI.Image>(); cancelImg.color = ClassicalBookUITheme.Wood;
        var cancelLblGO = new GameObject("Label"); cancelLblGO.transform.SetParent(cancelGO.transform, false);
        var cancelLbl = cancelLblGO.AddComponent<TextMeshProUGUI>(); cancelLbl.text = Localize.T("取消", "取消", "Cancel"); cancelLbl.alignment = TMPro.TextAlignmentOptions.Center; cancelLbl.fontSize = 18;
        var cancelRT = cancelGO.GetComponent<RectTransform>(); cancelRT.sizeDelta = new Vector2(0, 36);
        cancelBtn.onClick.AddListener(() => { Destroy(root); difficultySelectorInstance = null; ShowSelection(); });

        ClassicalBookUITheme.StyleButton(cancelBtn);
        ClassicalBookUITheme.StyleDifficultyModal(root, containerGO, titleText);

        difficultySelectorInstance = root;
    }

    // ---------- Audio Preload Helpers ----------
    private void PreloadAroundIndex(int idx)
    {
        if (songOptions == null || songOptions.Count == 0) return;

        int cur = GetWrappedIndex(idx);
        int left = GetWrappedIndex(idx - 1);
        int right = GetWrappedIndex(idx + 1);

        TryPreloadByOption(songOptions[cur]);
        if (songOptions.Count > 1) TryPreloadByOption(songOptions[left]);
        if (songOptions.Count > 2) TryPreloadByOption(songOptions[right]);
    }

    private void TryPreloadByOption(SongOption opt)
    {
        if (opt == null) return;

        PreloadClip(opt.audioResourcePath, clip =>
        {
            if (opt != null && clip != null && opt.audioClip == null)
            {
                opt.audioClip = clip;
            }
        });

        PreloadClip(opt.pianoAudioResourcePath, clip =>
        {
            if (opt != null && clip != null && opt.pianoAudioClip == null)
            {
                opt.pianoAudioClip = clip;
            }
        });
    }

    private void PreloadClip(string resourcePath, Action<AudioClip> onLoaded)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return;

        if (_audioCache.TryGetValue(resourcePath, out var cached) && cached != null)
        {
            onLoaded?.Invoke(cached);
            return;
        }

        StartCoroutine(PreloadClipAsync(resourcePath, onLoaded));
    }

    private IEnumerator PreloadClipAsync(string resourcePath, Action<AudioClip> onLoaded)
    {
        if (string.IsNullOrEmpty(resourcePath)) yield break;
        if (_audioCache.TryGetValue(resourcePath, out var cached) && cached != null)
        {
            onLoaded?.Invoke(cached);
            yield break;
        }

        AudioClip clip;
        string externalPath = ExternalSongLibrary.ToLocalPath(resourcePath);
        if (!string.IsNullOrEmpty(externalPath))
        {
            AudioType audioType = GetExternalAudioType(externalPath);
            // `+` 必須明確編成 %2B，否則下載端會把它解成空格、找不到檔案。
            // 同 GameManager.LoadExternalAudioForGameplay 的說明。
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
                       new Uri(externalPath).AbsoluteUri.Replace("+", "%2B"), audioType))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    BuildLogger.LogWarning($"SongSelectionManager: Failed to load external audio '{externalPath}': {request.error}");
                    onLoaded?.Invoke(null);
                    yield break;
                }
                clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null) clip.name = Path.GetFileNameWithoutExtension(externalPath);
            }
        }
        else
        {
            var req = Resources.LoadAsync<AudioClip>(resourcePath);
            yield return req;
            clip = req.asset as AudioClip;
        }
        if (clip == null)
        {
            if (_missingAudioWarnings.Add(resourcePath))
            {
                BuildLogger.LogWarning($"SongSelectionManager: Audio clip not found while preloading at Resources path '{resourcePath}'.");
            }
            _audioCache.Remove(resourcePath);
            onLoaded?.Invoke(null);
            yield break;
        }

        _missingAudioWarnings.Remove(resourcePath);

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading)
                yield return null;
        }
        _audioCache[resourcePath] = clip;
        // Map to current options by resource path
        foreach (var opt in songOptions)
        {
            if (opt.audioClip == null && string.Equals(opt.audioResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase))
            {
                opt.audioClip = clip;
            }
            if (opt.pianoAudioClip == null && string.Equals(opt.pianoAudioResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase))
            {
                opt.pianoAudioClip = clip;
            }
        }

        onLoaded?.Invoke(clip);
    }

    private static AudioType GetExternalAudioType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".ogg": return AudioType.OGGVORBIS;
            case ".mp3": return AudioType.MPEG;
            case ".aif":
            case ".aiff": return AudioType.AIFF;
            default: return AudioType.WAV;
        }
    }

    public void RefreshSongLibrary()
    {
        StopPreviewAudio();
        _audioCache.Clear();
        _missingAudioWarnings.Clear();
        foreach (Sprite sprite in _spriteCache.Values)
            if (sprite != null) Destroy(sprite);
        _spriteCache.Clear();
        LoadSongOptions();
        EnsureCategoryCarousel();
        UpdateCategoryCarouselLabel();
        UpdateCarouselVisuals();
    }

    public List<string> GetAvailableSongCategories()
    {
        var result = ExternalSongLibrary.GetCategories();
        for (int i = 0; i < discoveredCategoryOrder.Count; i++)
        {
            string category = discoveredCategoryOrder[i];
            if (!string.IsNullOrWhiteSpace(category) &&
                !string.Equals(category, ExternalSongLibrary.AllCategory, StringComparison.OrdinalIgnoreCase) &&
                !result.Exists(item => string.Equals(item, category, StringComparison.OrdinalIgnoreCase)))
                result.Add(category);
        }
        return result;
    }

    public void RefreshSongLibraryAndFocus(string externalId)
    {
        RefreshSongLibrary();
        if (string.IsNullOrWhiteSpace(externalId)) return;

        int allCategoryIndex = songCategories.FindIndex(category =>
            string.Equals(category, ExternalSongLibrary.AllCategory, StringComparison.OrdinalIgnoreCase));
        if (allCategoryIndex >= 0)
        {
            currentCategoryIndex = allCategoryIndex;
            ApplyCurrentCategoryFilter();
        }
        PushCategoriesToBar();

        int targetIndex = songOptions.FindIndex(option => option != null &&
            string.Equals(option.externalId, externalId, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) return;
        currentIndex = targetIndex;
        carouselTransitioning = false;
        difficultyLaunchInProgress = false;
        SetCarouselInteraction(true);
        UpdateCategoryCarouselLabel();
        UpdateCarouselVisuals();
    }

    public string SelectedExternalSongId
    {
        get
        {
            SongOption selected = GetSelectedSong();
            return selected != null ? selected.externalId : null;
        }
    }

    public string SelectedSongTitle => GetSelectedSong()?.displayName ?? string.Empty;
    public string SelectedSongAuthor => GetSelectedSong()?.author ?? string.Empty;
    public string SelectedSongCategory => GetSelectedSong()?.category ?? string.Empty;

    /// <summary>
    /// 回到選曲時，讓畫面落在使用者當下的模式上。
    /// </summary>
    /// <remarks>
    /// 兩個模式各自是一個空間（演奏會＝這張轉盤、練習＝練習室），所以「不是演奏會
    /// 但也不在練習室」這個狀態不該存在 —— 那正是以前那個「演奏會關掉之後的一般
    /// 模式」，它既沒有演奏會的正式感，也沒有練習室的工具。
    /// </remarks>
    private void EnterCurrentMode()
    {
        SettingsManager settings = SettingsManager.Instance;
        bool recital = settings == null || settings.RecitalMode;
        if (recital)
        {
            if (PracticeRoomScreen.Instance != null) PracticeRoomScreen.Instance.Close();
            return;
        }
        if (PracticeRoomScreen.Instance == null) PracticeRoomScreen.Open();
    }

    public void ShowSelection()
    {
        GameplayEntryPresentation.EndGameplay();
        groupingBar.RefreshLocalization();
        groupingBar.SetPopupOpen(false);
        carouselTransitioning = false;
        difficultyLaunchInProgress = false;
        RestoreBookPose();
        SetCarouselInteraction(true);
        SetGameplayHudPresentationVisible(false);

        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);
        }

        if (gameplayPanel != null)
        {
            gameplayPanel.SetActive(false);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetGameplayRootActive(false);
        }

        // 落回使用者當下的模式（演奏會＝這張轉盤，練習＝練習室）。
        EnterCurrentMode();

        // Stop any background video preview while the player is choosing/difficulty is undecided
        try
        {
            var bg = FindAnyObjectByType<GameBackgroundManager>();
            if (bg != null) bg.ShowCoverOnlyFromSelection();
        }
        catch { }

        UpdateCarouselVisuals();
    }

    public void HideSelection(AudioClip preserveMain = null, AudioClip preservePiano = null)
    {
        pointerOverCategoryCarousel = false;
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
        }

        if (gameplayPanel != null)
        {
            try
            {
                gameplayPanel.SetActive(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SongSelectionManager] Gameplay panel activation reported an error, continuing song startup: {ex}");
            }
        }

        if (GameManager.Instance != null)
        {
            try
            {
                GameManager.Instance.SetGameplayRootActive(true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SongSelectionManager] Gameplay root activation reported an error, continuing song startup: {ex}");
            }
        }

        StopPreviewAudio(preserveMain, preservePiano);
    }

    private static void SetGameplayHudPresentationVisible(bool visible)
    {
        try
        {
            var bg = FindAnyObjectByType<GameBackgroundManager>();
            if (bg != null) bg.SetGameplayHudPresentationVisible(visible);
        }
        catch { }
    }

    private IEnumerator LoadExternalAudioAndStart(SongOption option)
    {
        bool mainDone = false;
        AudioClip main = null;
        yield return PreloadClipAsync(option.audioResourcePath, clip =>
        {
            main = clip;
            mainDone = true;
        });
        if (!mainDone || main == null)
        {
            BuildLogger.LogError($"[SongSelectionManager] Cannot start external song '{option.displayName}': audio load failed.");
            ShowSelection();
            yield break;
        }
        option.audioClip = main;

        if (!string.IsNullOrWhiteSpace(option.pianoAudioResourcePath) && option.pianoAudioClip == null)
        {
            yield return PreloadClipAsync(option.pianoAudioResourcePath, clip => option.pianoAudioClip = clip);
        }
        StartChosenSong(option);
    }

    private void OnDisable()
    {
        pointerOverCategoryCarousel = false;
        CancelCarouselAudioLoad();
        if (carouselTransitionRoutine != null)
        {
            StopCoroutine(carouselTransitionRoutine);
            carouselTransitionRoutine = null;
        }
        carouselTransitioning = false;
        difficultyLaunchInProgress = false;
        RestoreBookPose();
        StopPreviewAudio();
    }

    /// <summary>
    /// 只隱藏選擇面板，不啟動遊戲面板 (用於設定面板)
    /// </summary>
    public void HideSelectionPanelOnly()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
        }
        StopPreviewAudio();
    }

    /// <summary>
    /// 只顯示選擇面板，不影響遊戲面板 (用於設定面板)
    /// </summary>
    public void ShowSelectionPanelOnly()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);
            UpdateCarouselVisuals();
        }
    }

    /// <summary>整個曲庫（每一首的群組代表，未經分類篩選）。</summary>
    /// <remarks>
    /// 練習室要自己列一份清單，而它列的是**全部**，不是轉盤當下的分類篩選結果 ——
    /// 在練習室裡「換分類才找得到那首歌」沒有道理。只讀不寫：這份名單的組成規則
    /// 仍然只有一個地方決定。
    /// </remarks>
    public IReadOnlyList<SongOption> AllSongs => allSongOptions;

    public SongOption GetSelectedSong()
    {
        if (settingsGameplayPreviewRunning && settingsGameplayPreviewOption != null)
            return settingsGameplayPreviewOption;
        if (currentIndex < 0 || currentIndex >= songOptions.Count) return null;
        var opt = songOptions[currentIndex];
        // If player picked a specific difficulty variant, return that for gameplay so speed/audioManage match the chosen chart.
        return opt != null && opt.selectedVariant != null ? opt.selectedVariant : opt;
    }

    /// <summary>
    /// Read-only access used by the settings gameplay preview.  Preview navigation
    /// deliberately does not change currentIndex or the player's real song choice.
    /// </summary>
    public int PreviewSongCount => songOptions != null ? songOptions.Count : 0;

    /// <summary>
    /// 設定預覽該顯示哪一首：**畫面上正在看的那一首**。
    /// </summary>
    /// <remarks>
    /// 不能只看 `currentIndex`：套用分類／難度篩選時它會被歸零
    /// （見 ApplyGenreFilter / ApplyLevelFilter 後面那行），於是預覽就跳回第一首。
    /// 轉盤真正停在哪一張是 songScroller 在管的，有它就以它為準。
    /// </remarks>
    public int CurrentSongIndexForPreview
    {
        get
        {
            if (songOptions == null || songOptions.Count == 0) return 0;
            int index = songScroller != null && songScroller.Count == songOptions.Count
                ? songScroller.BaseIndex
                : currentIndex;
            return Mathf.Clamp(index, 0, songOptions.Count - 1);
        }
    }

    /// <summary>
    /// 現在**實際載入在遊戲裡**的那一首在清單中的位置；找不到回 -1。
    /// </summary>
    /// <remarks>
    /// 設定是從遊戲中（含練習模式）叫出來的時候，`currentIndex` 不一定還指著它——
    /// 換過分類或重建清單之後那個值會被歸零，預覽就會跑去第一首。實際在玩的是
    /// 哪一份譜，只有 GameManager 知道，所以照 chartFileName 反查。
    /// </remarks>
    public int PlayingSongIndexForPreview()
    {
        if (songOptions == null || songOptions.Count == 0) return -1;
        GameManager game = GameManager.Instance;
        // chartFileName 有一個寫死的預設值，沒有真的載入譜面時它仍然是滿的，
        // 照它去找會match 到一首根本沒在玩的歌。CurrentChart 才是「真的載入了」。
        if (game == null || game.CurrentChart == null) return -1;
        string playing = game.chartFileName;
        if (string.IsNullOrEmpty(playing)) return -1;
        for (int i = 0; i < songOptions.Count; i++)
        {
            SongOption group = songOptions[i];
            if (group == null) continue;
            if (MatchesChart(group, playing)) return i;
            var variants = group.difficultyVariants;
            if (variants == null) continue;
            for (int v = 0; v < variants.Count; v++)
            {
                if (!MatchesChart(variants[v], playing)) continue;
                // 難度也要對上：把它設成這一組選中的難度，預覽才會播同一個難度。
                group.selectedVariant = variants[v];
                return i;
            }
        }
        return -1;
    }

    private static bool MatchesChart(SongOption option, string chartFileName)
    {
        return option != null && !string.IsNullOrEmpty(option.chartFileName)
               && string.Equals(option.chartFileName, chartFileName,
                                System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>某一首（或它的某個難度）在預覽清單裡的位置；找不到回 -1。</summary>
    public int IndexOfPreviewOption(SongOption option)
    {
        if (option == null || songOptions == null) return -1;
        for (int i = 0; i < songOptions.Count; i++)
        {
            SongOption group = songOptions[i];
            if (group == null) continue;
            if (ReferenceEquals(group, option)) return i;
            var variants = group.difficultyVariants;
            if (variants == null) continue;
            for (int v = 0; v < variants.Count; v++)
                if (ReferenceEquals(variants[v], option)) return i;
        }
        return -1;
    }

    public SongOption GetSongForSettingsPreview(int index)
    {
        if (songOptions == null || songOptions.Count == 0) return null;
        int wrapped = index % songOptions.Count;
        if (wrapped < 0) wrapped += songOptions.Count;
        SongOption group = songOptions[wrapped];
        if (group == null) return null;
        if (group.selectedVariant != null) return group.selectedVariant;
        if (group.difficultyVariants != null && group.difficultyVariants.Count > 0)
            return group.difficultyVariants[0];
        return group;
    }

    /// <summary>
    /// Starts the selected settings-page song through the normal gameplay path.
    /// Notes, audio, camera compensation, materials, particles and MESH effects
    /// are therefore the same runtime objects as a normal play session.
    /// </summary>
    /// <summary>
    /// 讓轉盤的選擇跟著別的畫面（練習室）走。
    /// </summary>
    /// <remarks>
    /// 開始遊戲時有幾樣東西不是從傳進去的 option 拿的，而是回頭問
    /// <see cref="GetSelectedSong"/> —— 音訊的分段管理、速度事件、混音器設定，還有
    /// 背景和「這首要不要用合成鋼琴」。練習室自己有一份清單，如果不把轉盤同步過
    /// 來，那幾項就會套到轉盤當下停的那一首（通常是第一首）身上。
    ///
    /// 轉盤可能正套著分類篩選，所以找不到的時候要先把篩選切成「全部」再找一次。
    /// </remarks>
    public void SyncSelectionTo(SongOption group, SongOption variant)
    {
        if (group == null) return;
        if (variant != null) group.selectedVariant = variant;

        int index = songOptions.IndexOf(group);
        if (index < 0)
        {
            // 練習室列的是整個曲庫，轉盤這邊可能被篩掉了。切回「全部」再找。
            for (int i = 0; i < songCategories.Count; i++)
            {
                if (!string.Equals(songCategories[i], ExternalSongLibrary.AllCategory)) continue;
                currentCategoryIndex = i;
                ApplyCurrentCategoryFilter();
                break;
            }
            index = songOptions.IndexOf(group);
        }
        if (index < 0) return;

        currentIndex = index;
        UpdateCarouselVisuals(false);
    }

    /// <summary>
    /// 從別的畫面（練習室）開始一首歌，走的是和選難度的書完全一樣的流程。
    /// </summary>
    /// <remarks>
    /// 不要自己複製那段開場：它除了呼叫 GameManager.StartSong 之外，還要處理
    /// external 音檔要重載、試聽要停、選曲畫面要收、遊戲根物件要開。漏掉任何一項
    /// 都會變成很難查的「有時候沒有聲音」。
    /// </remarks>
    public void StartSongOption(SongOption option)
    {
        if (option == null) return;
        SongDifficultyStrip.RememberChoice(option);
        StartChosenSong(option);
    }

    public void StartSettingsGameplayPreview(SongOption option)
    {
        if (option == null || GameManager.Instance == null) return;
        if (settingsGameplayPreviewLoop != null)
        {
            StopCoroutine(settingsGameplayPreviewLoop);
            settingsGameplayPreviewLoop = null;
        }
        settingsGameplayPreviewOption = option;
        settingsGameplayPreviewRunning = true;

        // 先把正在播的那一場停掉。預覽是直接 StartSong 開一場新的，之前那一場
        // （練習模式也算）的音樂不會自己停，兩首會疊在一起。
        // 練習室的試聽是它自己的 AudioSource、不歸 Conductor 管，要另外停。
        try { PracticeRoomScreen.StopPreviewAudio(); } catch { }
        try
        {
            GameManager game = GameManager.Instance;
            if (game != null && game.Conductor != null) game.Conductor.Stop();
        }
        catch { }

        AudioClip mainClip = option.audioClip;
        if (mainClip == null && !string.IsNullOrEmpty(option.audioResourcePath))
        {
            _audioCache.TryGetValue(option.audioResourcePath, out mainClip);
            if (mainClip == null) mainClip = LoadAudioClip(option.audioResourcePath, option.displayName);
            if (mainClip != null) option.audioClip = mainClip;
        }

        AudioClip pianoClip = option.pianoAudioClip;
        if (pianoClip == null && !string.IsNullOrEmpty(option.pianoAudioResourcePath))
        {
            pianoClip = LoadAudioClip(option.pianoAudioResourcePath, option.displayName + " (Piano)");
            if (pianoClip != null) option.pianoAudioClip = pianoClip;
        }

        bool externalMain = ExternalSongLibrary.ToLocalPath(option.audioResourcePath) != null;
        bool externalPiano = ExternalSongLibrary.ToLocalPath(option.pianoAudioResourcePath) != null;

        // Built-in preview clips can be shared safely with gameplay, so keep
        // their sample data alive while the selection UI closes. Clips created
        // from UserSongs are owned by the preview screen and cannot reliably be
        // reused after unloading; GameManager loads fresh copies from the paths.
        AudioClip gameplayMainClip = externalMain ? null : mainClip;
        AudioClip gameplayPianoClip = externalPiano ? null : pianoClip;
        HideSelection(gameplayMainClip, gameplayPianoClip);
        SetGameplayHudPresentationVisible(true);
        try
        {
            var background = FindAnyObjectByType<GameBackgroundManager>();
            if (background != null) background.SetSongDifficultyFromOption(option);
        }
        catch { }
        try { Judgment.JudgmentManager.Instance?.ResetSettingsPreviewComboDisplay(); } catch { }

        GameManager.Instance.StartSong(option.chartFileName, gameplayMainClip, option.displayName,
            gameplayPianoClip, option.pianoAudioResourcePath, option.audioResourcePath);
        settingsGameplayPreviewLoop = StartCoroutine(RunSettingsGameplayPreviewLoop(option));
    }

    public void StopSettingsGameplayPreview()
    {
        if (!settingsGameplayPreviewRunning) return;
        if (settingsGameplayPreviewLoop != null)
        {
            StopCoroutine(settingsGameplayPreviewLoop);
            settingsGameplayPreviewLoop = null;
        }
        settingsGameplayPreviewRunning = false;
        settingsGameplayPreviewOption = null;
        GameManager.Instance?.ReselectSong();
    }

    public void RestartSettingsGameplayPreview()
    {
        if (!settingsGameplayPreviewRunning || settingsGameplayPreviewOption == null) return;
        StartSettingsGameplayPreview(settingsGameplayPreviewOption);
    }

    private IEnumerator RunSettingsGameplayPreviewLoop(SongOption option)
    {
        const float segmentDurationMs = 30000f;
        const float fadeDurationSeconds = 5f;
        const float intervalSeconds = 10f;
        GameManager game = GameManager.Instance;

        while (settingsGameplayPreviewRunning && ReferenceEquals(settingsGameplayPreviewOption, option) &&
               game != null && (game.CurrentChart == null || game.Conductor == null || !game.Conductor.isPlaying))
            yield return null;

        if (!settingsGameplayPreviewRunning || !ReferenceEquals(settingsGameplayPreviewOption, option) ||
            game == null || game.CurrentChart?.notes == null || game.CurrentChart.notes.Count == 0)
        {
            settingsGameplayPreviewLoop = null;
            yield break;
        }

        float chartEnd = game.CurrentChart.music_finish_time_msec;
        if (option.audioClip != null)
        {
            float audioEnd = option.audioClip.length * 1000f;
            chartEnd = chartEnd > 0f ? Mathf.Min(chartEnd, audioEnd) : audioEnd;
        }
        if (chartEnd <= 0f)
        {
            for (int i = 0; i < game.CurrentChart.notes.Count; i++)
            {
                NoteData note = game.CurrentChart.notes[i];
                if (note != null) chartEnd = Mathf.Max(chartEnd, Mathf.Max(note.startTime, note.endTime));
            }
        }

        float actualDurationMs = Mathf.Min(segmentDurationMs, Mathf.Max(1000f, chartEnd));
        float segmentStartMs = FindDensestPreviewStart(game.CurrentChart.notes, actualDurationMs, chartEnd);

        while (settingsGameplayPreviewRunning && ReferenceEquals(settingsGameplayPreviewOption, option))
        {
            game.PrepareSettingsPreviewSegment(segmentStartMs);
            game.SetSettingsPreviewAudioFade(0f);
            float segmentEndMs = segmentStartMs + actualDurationMs;

            while (settingsGameplayPreviewRunning && ReferenceEquals(settingsGameplayPreviewOption, option) &&
                   game.Conductor != null && game.Conductor.effectiveSongPosition < segmentEndMs)
            {
                float elapsedSeconds = Mathf.Max(0f,
                    (game.Conductor.effectiveSongPosition - segmentStartMs) * 0.001f);
                float remainingSeconds = Mathf.Max(0f,
                    (segmentEndMs - game.Conductor.effectiveSongPosition) * 0.001f);
                float fadeIn = Mathf.Clamp01(elapsedSeconds / fadeDurationSeconds);
                float fadeOut = Mathf.Clamp01(remainingSeconds / fadeDurationSeconds);
                game.SetSettingsPreviewAudioFade(Mathf.Min(fadeIn, fadeOut));
                yield return null;
            }

            if (!settingsGameplayPreviewRunning || !ReferenceEquals(settingsGameplayPreviewOption, option)) break;
            game.SetSettingsPreviewAudioFade(0f);
            game.PauseSettingsPreviewPlayback();

            float waitUntil = Time.unscaledTime + intervalSeconds;
            while (settingsGameplayPreviewRunning && ReferenceEquals(settingsGameplayPreviewOption, option) &&
                   Time.unscaledTime < waitUntil)
                yield return null;
        }

        settingsGameplayPreviewLoop = null;
    }

    private static float FindDensestPreviewStart(List<NoteData> notes, float durationMs, float chartEndMs)
    {
        var timings = new List<int>(notes.Count);
        for (int i = 0; i < notes.Count; i++)
            if (notes[i] != null) timings.Add(notes[i].startTime);
        if (timings.Count == 0) return 0f;
        timings.Sort();

        int bestStart = 0;
        int bestCount = 0;
        int end = 0;
        for (int start = 0; start < timings.Count; start++)
        {
            if (end < start) end = start;
            while (end < timings.Count && timings[end] < timings[start] + durationMs) end++;
            int count = end - start;
            if (count > bestCount)
            {
                bestCount = count;
                bestStart = start;
            }
        }

        // Start slightly before the densest hit so its first notes have time to
        // enter the runway, while keeping the complete preview at 30 seconds.
        float startMs = Mathf.Max(0f, timings[bestStart] - 2000f);
        return Mathf.Clamp(startMs, 0f, Mathf.Max(0f, chartEndMs - durationMs));
    }
}
