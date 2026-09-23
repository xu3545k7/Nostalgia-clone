using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UIHelpers;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public partial class SimpleCarouselSettings : MonoBehaviour
{
    /// <summary>
    /// 設定的第一層：**這是哪一種東西**。
    /// </summary>
    /// <remarks>
    /// 第一層只回答一個問題：你要改的是聲音、是判定、是畫面、還是曲庫。六項，
    /// 一眼掃完，而且互相之間沒有任何重疊 —— 沒有「這個好像兩邊都算」的項目。
    /// </remarks>
    public enum SettingsCategory
    {
        Audio,
        Judgment,
        Visual,
        Gameplay,
        SongManagement,
        System
    }

    /// <summary>
    /// 設定的第二層：**這個東西的哪一部分**。
    /// </summary>
    /// <remarks>
    /// 上一版把「判定線」和「Combo」放成第一層，那是錯的：它們不是和「顯示」並
    /// 列的東西，它們**就是顯示的一部分**。並列之後第一層變成一份長短不一的清
    /// 單，玩家要先知道答案才找得到答案。
    ///
    /// 每一個子分類只負責一件事，而且那件事要能用一個名詞講完 —— 講不完就表示
    /// 它其實是兩件事。
    /// </remarks>
    public enum SettingsGroup
    {
        // 音效
        Volume,
        Piano,
        Pedal,
        Sync,
        // 判定
        JudgmentMode,
        JudgmentDetail,
        Mistouch,
        // 顯示
        Track,
        Notes,
        JudgmentLine,
        Combo,
        Background,
        // 其餘各自只有一組
        Session,
        Library,
        General,
    }

    [System.Serializable]
    public class SettingData
    {
        public string title;
        public float value;
        public float min;
        public float max;
        public float step;
        public string unit;
        public bool isPercentage;
        public bool fastSelect;
        public bool isToggle;
        public bool isHidden;
        public bool isAction;

        public SettingsCategory category;
        public SettingsGroup group;
        public string minLabel;
        public string maxLabel;
        public string[] discreteLabels;

        public string ResolveMinLabel()
        {
            return string.IsNullOrEmpty(minLabel) ? "Off" : minLabel;
        }

        public string ResolveMaxLabel()
        {
            return string.IsNullOrEmpty(maxLabel) ? "On" : maxLabel;
        }
    }

    [Header("UI References")]
    public GameObject settingsPanel;
    public Button settingsButton;
    public Button closeButton;

    [Header("Selection Panel Management")]
    [Tooltip("Open settings while temporarily hiding the song selection panel.")]
    public bool hideSelectionPanelWhenOpen = true;

    [Header("Carousel Style")]
    public GameObject centerBox;
    public GameObject leftBox;
    public GameObject rightBox;
    public Button leftArrow;
    public Button rightArrow;

    [Header("Center Box Content")]
    public TextMeshProUGUI centerTitle;
    public TextMeshProUGUI centerValue;
    public Button minusButton;
    public Button plusButton;

    [Header("Side Boxes Content")]
    public TextMeshProUGUI leftTitle;
    public TextMeshProUGUI leftValue;
    public TextMeshProUGUI rightTitle;
    public TextMeshProUGUI rightValue;

    private SettingData[] settings;
    private int currentIndex = 0;
    private float nextWheelNavigationTime;
    private GameplaySettingsPreview gameplayPreview;
    private TextMeshProUGUI gameplayPreviewTitle;
    private Button previewPreviousSongButton;
    private Button previewNextSongButton;
    private int settingsPreviewSongIndex = -1;

    /// <summary>「一鍵套用本家」剛按過：那一列的按鈕顯示「已套用」，動到其他設定就收掉。</summary>
    private bool arcadeAppliedShown;
    private static readonly SettingsCategory[] CategoryOrder =
    {
        SettingsCategory.Audio,
        SettingsCategory.Judgment,
        SettingsCategory.Visual,
        SettingsCategory.Gameplay,
        SettingsCategory.SongManagement,
        SettingsCategory.System
    };

    /// <summary>每個分類底下有哪些子分類，照玩家最常找的順序排。</summary>
    private static readonly System.Collections.Generic.Dictionary<SettingsCategory, SettingsGroup[]>
        GroupOrder = new System.Collections.Generic.Dictionary<SettingsCategory, SettingsGroup[]>
    {
        { SettingsCategory.Audio, new[] {
            SettingsGroup.Volume, SettingsGroup.Piano, SettingsGroup.Pedal, SettingsGroup.Sync } },
        { SettingsCategory.Judgment, new[] {
            SettingsGroup.JudgmentMode, SettingsGroup.JudgmentDetail, SettingsGroup.Mistouch } },
        { SettingsCategory.Visual, new[] {
            SettingsGroup.Track, SettingsGroup.Notes, SettingsGroup.JudgmentLine,
            SettingsGroup.Combo, SettingsGroup.Background } },
        { SettingsCategory.Gameplay, new[] { SettingsGroup.Session } },
        { SettingsCategory.SongManagement, new[] { SettingsGroup.Library } },
        { SettingsCategory.System, new[] { SettingsGroup.General } },
    };

    private const int InputModeIndex = 0;
    private const int HitSoundVolumeIndex = 1;
    private const int MusicVolumeIndex = 2;
    private const int PianoVolumeIndex = 3;
    private const int DefaultSpeedIndex = 4;
    private const int StartDelayIndex = 5;
    private const int JudgePopupHeightIndex = 6;
    private const int JudgmentLineHeightIndex = 7;
    private const int CameraRotXIndex = 8;
    private const int DebugModeIndex = 9;
    private const int JudgmentOffsetIndex = 10;
    private const int MusicPlaybackOffsetIndex = 11;
    private const int PianoPreviewIndex = 12;
    private const int TrackVideoDimmerIndex = 13;
    private const int VisualLayoutModeIndex = 14;
    private const int NoteVisualHeightIndex = 15;
    private const int TrackGuideLineOpacityIndex = 16;
    private const int TrackMarbleThemeIndex = 17;
    private const int JudgmentMeshHeightIndex = 18;
    private const int TimingSensitiveVisualsIndex = 19;
    private const int KeyboardScreenBottomIndex = 20;
    private const int LanguageIndex = 21;
    private const int ComboDisplayIndex = 22;
    private const int ComboFontSizeIndex = 23;
    private const int TrackComboHeightIndex = 24;
    private const int AddSongsIndex = 25;
    private const int EditSongsIndex = 26;
    private const int DeleteSongsIndex = 27;
    private const int CreateSongCategoryIndex = 28;
    private const int PianoSoundModeIndex = 29;
    private const int PianoPedalSourceIndex = 30;
    private const int PianoAutoPedalIndex = 31;
    private const int ChordSpreadIndex = 32;
    private const int LateHitPriorityIndex = 33;
    private const int EarlyClaimLimitIndex = 34;
    private const int RetimeBetterPressIndex = 35;
    private const int PianoPerformanceSpreadIndex = 36;
    private const int TrackGlassIndex = 37;
    private const int TrackGlassFloorIndex = 38;
    private const int BackgroundHarmonyIndex = 39;
    private const int MinNoteVelocityIndex = 40;
    private const int BrushRejectionIndex = 41;
    private const int KeyChatterGuardIndex = 42;
    private const int HoldMagicCircleIndex = 43;
    private const int ShowPedalCuesIndex = 44;
    // 接在最後面。既有編號一個都不能動——這個檔案到處用索引常數對應
    // settings[] 的位置，中間插一個就全錯位了。
    private const int InputFrameBatchingIndex = 45;
    private const int JudgmentTimingIndex = 46;
    private const int BrushLookaheadIndex = 47;
    private const int PianoDynamicExpansionIndex = 48;
    private const int ShowCentreCueIndex = 49;
    private const int NormalizeLoudnessIndex = 50;
    private const int JudgmentPresetIndex = 51;
    private const int TouchAlignmentIndex = 52;
    private const int StrikeCueHeightIndex = 53;
    private const int ApplyArcadeIndex = 54;
    private const int NoteArcHeightIndex = 55;
    private const int NoteFallModeIndex = 56;
    private const int NoteArcLengthIndex = 57;
    private const int NoteArcCurveIndex = 58;
    private const int NoteArcSpawnIndex = 59;

    /// <summary>
    /// 只有在判定預設是「自訂」的時候才看得到的那幾項。
    /// </summary>
    /// <remarks>
    /// 它們不是被停用，是被**收起來**。預設模式的意思就是「這一組已經配好了」，
    /// 把配好的值攤在旁邊讓人一個一個改，等於在說它其實沒配好。
    /// </remarks>
    private static readonly int[] JudgmentDetailIndices =
    {
        JudgmentOffsetIndex,
        ChordSpreadIndex,
        LateHitPriorityIndex,
        EarlyClaimLimitIndex,
        RetimeBetterPressIndex,
        BrushRejectionIndex,
        KeyChatterGuardIndex,
        InputFrameBatchingIndex,
        JudgmentTimingIndex,
        BrushLookaheadIndex,
        MinNoteVelocityIndex,
    };

    private void Start()
    {
        InitializeSettings();
        SetupButtons();
        try { ApplyClassicalBookTheme(); }
        catch (System.Exception ex) { Debug.LogWarning($"Settings theme fallback: {ex.Message}"); }
        ApplyLocalization();
        UpdateDisplay();

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    private void ApplyClassicalBookTheme()
    {
        ClassicalBookUITheme.EnsureBookBackdrop(settingsPanel, "PLAYER  SETTINGS  SCORE");
        Transform bookBackdrop = settingsPanel != null
            ? settingsPanel.transform.Find("ClassicalBookBackdrop")
            : null;

        // Settings draws its own page and preview rail; the full-screen book
        // backdrop would only sit behind them doing nothing.
        if (bookBackdrop != null) bookBackdrop.gameObject.SetActive(false);

        // The authored full-screen background sits after the content in the old
        // hierarchy and covers every label once the new book backdrop is added.
        Transform legacyBackground = settingsPanel != null ? settingsPanel.transform.Find("BackGround") : null;
        if (legacyBackground != null) legacyBackground.gameObject.SetActive(false);
        Transform content = settingsPanel != null ? settingsPanel.transform.Find("SettingPanelContent") : null;
        if (content != null)
        {
            content.gameObject.SetActive(true);
            content.SetAsLastSibling();
        }
        HideLegacyCarousel();
        ClassicalBookUITheme.StyleSettingsIconButton(settingsButton);

        if (settingsPanel != null)
        {
            Canvas settingsCanvas = settingsPanel.GetComponent<Canvas>();
            if (settingsCanvas == null) settingsCanvas = settingsPanel.AddComponent<Canvas>();
            settingsCanvas.overrideSorting = true;
            settingsCanvas.sortingOrder = 30000;
            if (settingsPanel.GetComponent<GraphicRaycaster>() == null)
                settingsPanel.AddComponent<GraphicRaycaster>();

            // 場景裡原本就有的文字套上字型。放在建新介面**之前**：新介面的字自己
            // 設好了字型和射線，這一圈掃過去只會把它們改回預設。
            TextMeshProUGUI[] allText = settingsPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < allText.Length; i++)
                ClassicalBookUITheme.StyleText(allText[i], allText[i].color,
                    allText[i].fontSize > 0f ? allText[i].fontSize : 20f, allText[i].fontStyle);
        }

        Transform fullscreenOverlay = EnsureFullscreenSettingsOverlay();
        Transform host = fullscreenOverlay != null ? fullscreenOverlay : settingsPanel?.transform;
        EnsureGameplayPreview(host);
        BuildSettingsScreens(host);
        if (fullscreenOverlay != null) fullscreenOverlay.SetAsLastSibling();
    }

    /// <summary>
    /// 舊的旋轉卡片、上下箭頭、關閉鈕。
    /// </summary>
    /// <remarks>
    /// 只是關掉，不刪：它們是場景裡拉好的參考，刪掉的話 Inspector 那幾格會變成
    /// Missing，而換回舊版面時還得重拉一次。
    /// </remarks>
    private void HideLegacyCarousel()
    {
        GameObject[] legacy =
        {
            leftBox, centerBox, rightBox,
            leftArrow != null ? leftArrow.gameObject : null,
            rightArrow != null ? rightArrow.gameObject : null,
            minusButton != null ? minusButton.gameObject : null,
            plusButton != null ? plusButton.gameObject : null,
            closeButton != null ? closeButton.gameObject : null,
        };
        for (int i = 0; i < legacy.Length; i++)
            if (legacy[i] != null) legacy[i].SetActive(false);
    }

    private Transform EnsureFullscreenSettingsOverlay()
    {
        if (settingsPanel == null) return null;
        Transform existing = settingsPanel.transform.Find("SettingsFullscreenOverlay");
        GameObject overlayObject;
        if (existing == null)
        {
            overlayObject = new GameObject("SettingsFullscreenOverlay", typeof(RectTransform));
            overlayObject.layer = settingsPanel.layer;
            overlayObject.transform.SetParent(settingsPanel.transform, false);
        }
        else overlayObject = existing.gameObject;

        RectTransform overlay = overlayObject.GetComponent<RectTransform>();
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.pivot = new Vector2(0.5f, 0.5f);
        overlay.offsetMin = Vector2.zero;
        overlay.offsetMax = Vector2.zero;
        overlay.anchoredPosition = Vector2.zero;
        overlay.localScale = Vector3.one;
        overlay.localRotation = Quaternion.identity;
        overlay.SetAsLastSibling();
        return overlay;
    }

    private void EnsureGameplayPreview(Transform parent)
    {
        if (parent == null) return;
        Transform existing = parent.Find("GameplayPreviewFrame");
        GameObject frameObject;
        if (existing != null)
        {
            frameObject = existing.gameObject;
        }
        else
        {
            frameObject = new GameObject("GameplayPreviewFrame", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Shadow));
            frameObject.layer = parent.gameObject.layer;
            frameObject.transform.SetParent(parent, false);
        }
        gameplayPreviewFrame = frameObject;

        RectTransform frame = frameObject.GetComponent<RectTransform>();
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.pivot = new Vector2(0.5f, 0.5f);
        frame.anchoredPosition = Vector2.zero;
        frame.offsetMin = Vector2.zero;
        frame.offsetMax = Vector2.zero;
        frame.sizeDelta = Vector2.zero;
        frame.localScale = Vector3.one;
        frame.localRotation = Quaternion.identity;

        Image frameImage = frameObject.GetComponent<Image>();
        frameImage.sprite = null;
        frameImage.type = Image.Type.Simple;
        frameImage.color = Color.clear;
        frameImage.raycastTarget = false;
        Outline outline = frameObject.GetComponent<Outline>();
        outline.enabled = false;
        Shadow shadow = null;
        Shadow[] shadows = frameObject.GetComponents<Shadow>();
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i] is not Outline) { shadow = shadows[i]; break; }
        }
        if (shadow == null) shadow = frameObject.AddComponent<Shadow>();
        shadow.enabled = false;

        Transform previewTransform = frame.Find("LiveGameplayPreview");
        if (previewTransform == null)
        {
            GameObject previewObject = new GameObject("LiveGameplayPreview", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(RawImage), typeof(GameplaySettingsPreview));
            previewObject.layer = frameObject.layer;
            previewTransform = previewObject.transform;
            previewTransform.SetParent(frame, false);
        }
        RectTransform previewRect = previewTransform as RectTransform;
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = Vector2.zero;
        previewRect.offsetMax = Vector2.zero;
        gameplayPreview = previewTransform.GetComponent<GameplaySettingsPreview>();
        RawImage previewImage = previewTransform.GetComponent<RawImage>();
        if (previewImage == null) previewImage = previewTransform.gameObject.AddComponent<RawImage>();
        previewImage.raycastTarget = false;

        // 曲名底下墊一塊暗底。它浮在正在跑的譜面上，沒有底的話金色的字會和判定
        // 特效、光柱混成一片。左右留給切歌的兩顆按鈕，右上角留給切換畫面的按鈕。
        Transform plateTransform = frame.Find("PreviewTitlePlate");
        RectTransform plate = plateTransform != null
            ? plateTransform as RectTransform
            : SettingsUiKit.CreateRect("PreviewTitlePlate", frame);
        plate.anchorMin = new Vector2(0.31f, 1f);
        plate.anchorMax = new Vector2(0.81f, 1f);
        plate.pivot = new Vector2(0.5f, 1f);
        plate.anchoredPosition = new Vector2(0f, -32f);
        plate.sizeDelta = new Vector2(0f, 50f);
        SettingsUiKit.AddImage(plate.gameObject, SettingsUiKit.SoftPanelSprite(), Color.white, false);

        Transform titleTransform = frame.Find("PreviewTitle");
        TextMeshProUGUI title;
        if (titleTransform == null)
        {
            GameObject titleObject = new GameObject("PreviewTitle", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            titleObject.layer = frameObject.layer;
            titleObject.transform.SetParent(frame, false);
            title = titleObject.GetComponent<TextMeshProUGUI>();
        }
        else title = titleTransform.GetComponent<TextMeshProUGUI>();
        gameplayPreviewTitle = title;
        PreviewSongScrollRouter titleScroll = title.gameObject.GetComponent<PreviewSongScrollRouter>();
        if (titleScroll == null) titleScroll = title.gameObject.AddComponent<PreviewSongScrollRouter>();
        titleScroll.Owner = this;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0.31f, 1f);
        titleRect.anchorMax = new Vector2(0.81f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -32f);
        titleRect.sizeDelta = new Vector2(-40f, 50f);
        title.text = Localize.T("即時遊玩預覽", "实时游玩预览", "LIVE  GAMEPLAY  PREVIEW");
        title.alignment = TextAlignmentOptions.Center;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        ClassicalBookUITheme.StyleText(title, SettingsUiKit.GoldBright, 22f, FontStyles.SmallCaps);
        // StyleText 會關掉射線；曲名要接滾輪切歌，所以在它之後再打開。
        title.raycastTarget = true;
        previewPreviousSongButton = EnsurePreviewSongButton(frame, "PreviousPreviewSong", true);
        previewNextSongButton = EnsurePreviewSongButton(frame, "NextPreviewSong", false);
        plate.SetAsLastSibling();
        title.transform.SetAsLastSibling();
        previewPreviousSongButton.transform.SetAsLastSibling();
        previewNextSongButton.transform.SetAsLastSibling();
        // Full-screen gameplay is the base layer; the rail and chrome sit above it.
        frame.SetAsFirstSibling();
        ConfigurePreviewSong(true);
        gameplayPreview.RefreshNow();
    }

    private Button EnsurePreviewSongButton(RectTransform parent, string objectName, bool previous)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
        {
            Button kept = existing.GetComponent<Button>();
            if (kept != null && existing.GetComponent<SettingsButtonFeel>() != null) return kept;
            Destroy(existing.gameObject);
        }

        Button button = SettingsUiKit.CreateButton(parent, objectName, SettingsUiKit.Tone.Lacquer,
            previous ? SettingsGlyph.Shape.ChevronLeft : SettingsGlyph.Shape.ChevronRight,
            null, 0f, new Vector2(50f, 50f), out _, out _);
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(previous ? 0.31f : 0.81f, 1f);
        rect.pivot = new Vector2(previous ? 1f : 0f, 1f);
        rect.anchoredPosition = new Vector2(previous ? -10f : 10f, -32f);
        if (previous) button.onClick.AddListener(() => ChangePreviewSong(-1));
        else button.onClick.AddListener(() => ChangePreviewSong(1));
        PreviewSongScrollRouter scrollRouter = button.gameObject.AddComponent<PreviewSongScrollRouter>();
        scrollRouter.Owner = this;
        return button;
    }

    private void ChangePreviewSong(int direction)
    {
        SongSelectionManager manager = SongSelectionManager.Instance;
        if (manager == null || manager.PreviewSongCount <= 0) return;
        if (settingsPreviewSongIndex < 0) settingsPreviewSongIndex = manager.CurrentSongIndexForPreview;
        settingsPreviewSongIndex = (settingsPreviewSongIndex + direction) % manager.PreviewSongCount;
        if (settingsPreviewSongIndex < 0) settingsPreviewSongIndex += manager.PreviewSongCount;
        ConfigurePreviewSong(false);
    }

    public void HandlePreviewSongScroll(float delta)
    {
        if (Mathf.Abs(delta) < 0.01f || Time.unscaledTime < nextWheelNavigationTime) return;
        ChangePreviewSong(delta > 0f ? -1 : 1);
        nextWheelNavigationTime = Time.unscaledTime + 0.16f;
    }

    private void ConfigurePreviewSong(bool resetToCurrent)
    {
        // 設定裡的預覽要自己出聲，所以練習室那份試聽得先閉嘴。它是練習室自己的
        // AudioSource、不歸 Conductor 管，沒人停它就會和預覽疊在一起。這裡停而
        // 不是只在預覽真的開起來時停——預覽開不起來時更不該留著另一首在播。
        try { PracticeRoomScreen.StopPreviewAudio(); } catch { }
        SongSelectionManager manager = SongSelectionManager.Instance;
        if (manager == null || manager.PreviewSongCount <= 0)
        {
            if (gameplayPreviewTitle != null)
            {
                gameplayPreviewTitle.text = Localize.T(
                    "沒有可用的譜面", "没有可用的谱面", "NO  CHART  AVAILABLE");
                ClassicalBookUITheme.ApplyLocalizedFont(gameplayPreviewTitle);
            }
            gameplayPreview?.SetPreviewSong(null);
            return;
        }
        SongSelectionManager.SongOption song = null;
        if (resetToCurrent || settingsPreviewSongIndex < 0 || settingsPreviewSongIndex >= manager.PreviewSongCount)
        {
            // 依序問三個地方，第一個答得出來的就是「使用者現在看著的那一首」：
            //   1. 練習室（它的選擇是自己的欄位，和轉盤無關）
            //   2. 真的載入在遊戲裡的譜面
            //   3. 選歌轉盤實際停在哪一張
            SongSelectionManager.SongOption practice =
                PracticeRoomScreen.CurrentSelectionForPreview();
            if (practice != null)
            {
                song = practice;
                int index = manager.IndexOfPreviewOption(practice);
                settingsPreviewSongIndex = index >= 0 ? index : 0;
            }
            else
            {
                int playing = manager.PlayingSongIndexForPreview();
                settingsPreviewSongIndex = playing >= 0 ? playing : manager.CurrentSongIndexForPreview;
            }
        }
        if (song == null) song = manager.GetSongForSettingsPreview(settingsPreviewSongIndex);
        gameplayPreview?.SetPreviewSong(song);
        if (gameplayPreviewTitle != null)
        {
            string title = song != null && !string.IsNullOrWhiteSpace(song.displayName)
                ? song.displayName
                : Localize.T("未知曲目", "未知曲目", "UNKNOWN PIECE");
            string difficulty = song != null && !string.IsNullOrWhiteSpace(song.difficultyName) ? song.difficultyName : string.Empty;
            string level = song != null && song.difficultyLevel > 0 ? $" Lv.{song.difficultyLevel}" : string.Empty;
            // 左右已經有兩顆切歌的按鈕，字裡不再畫「<  >」。
            gameplayPreviewTitle.text = $"{title}   ·   {difficulty}{level}";
            ClassicalBookUITheme.ApplyContentFont(gameplayPreviewTitle);
        }
    }

    private void InitializeSettings()
    {
        settings = new SettingData[]
        {
            new SettingData { title = "Input Mode", value = 0f, min = 0f, max = 2f, step = 1f, unit = "",
                discreteLabels = new[] { "Keyboard", "MIDI (Legacy)", "MIDI Keyboard" } },
            new SettingData { title = "Hit Sound Volume", value = 0.3f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "Music Volume", value = 0.8f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "Piano Volume", value = 0.8f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "Default Speed", value = 30f, min = 10f, max = 2000f, step = 5f, unit = "" },
            new SettingData { title = "Start Delay", value = 3f, min = 2f, max = 5f, step = 0.5f, unit = "s" },
            new SettingData { title = "Judgment Result Height", value = 30f, min = 30f, max = 100f, step = 0.5f, unit = "" },
            new SettingData { title = "Judgment Line Height", value = 0.28f, min = 0.12f, max = 0.55f, step = 0.01f, unit = "%", isPercentage = true },
            new SettingData { title = "Chart Angle", value = 70f, min = 40f, max = 90f, step = 1f, unit = "deg" },
            new SettingData { title = "Auto Play", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true },
            new SettingData { title = "Judgment Offset", value = 0f, min = -500f, max = 500f, step = 1f, unit = "ms", fastSelect = true },
            new SettingData { title = "Music Playback Offset", value = 0f, min = -3000f, max = 3000f, step = 1f, unit = "ms", fastSelect = true },
            new SettingData { title = "Piano Preview", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true },
            new SettingData { title = "Track Video Dimmer", value = 0.4f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "Display Mode", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true, minLabel = "General", maxLabel = "88 Keys" },
            new SettingData { title = "Note Thickness", value = 2f, min = 0.5f, max = 4f, step = 0.1f, unit = "" },
            new SettingData { title = "Track Line Opacity", value = 1f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "Track Material", value = 0f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true, minLabel = "Black Marble", maxLabel = "White Marble" },
            new SettingData { title = "Hit Light Height", value = 1f, min = 0.25f, max = 4f, step = 0.25f, unit = "x" },
            new SettingData { title = "Hit Effect Position", value = 1f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true, minLabel = "Judgment Line", maxLabel = "Actual Timing" },
            new SettingData { title = "Keyboard Position", value = 1f, min = 0f, max = 1f, step = 1f, unit = "", isToggle = true, minLabel = "Beside Track", maxLabel = "Screen Bottom" },
            new SettingData { title = "Language", value = 0f, min = 0f, max = 2f, step = 1f, unit = "",
                discreteLabels = new[] { "繁體中文", "简体中文", "English" } },
            new SettingData { title = "Combo Position", value = 1f, min = 0f, max = 3f, step = 1f, unit = "",
                discreteLabels = new[] { "Top Left", "Top Center", "On Track", "Center" } },
            new SettingData { title = "Combo Font Size", value = 48f, min = 24f, max = 96f, step = 4f, unit = "" },
            new SettingData { title = "Combo Height", value = 0.35f, min = 0f, max = 1f, step = 0.05f, unit = "%", isPercentage = true },
            new SettingData { title = "Add Songs", isAction = true },
            new SettingData { title = "Edit Songs", isAction = true },
            new SettingData { title = "Delete Songs", isAction = true },
            new SettingData { title = "Create Category", isAction = true },
            // Recorded plays the song's piano stem; the other two synthesise each
            // note as it is hit, so a missed note is genuinely missing.
            new SettingData { title = "Piano Sound", value = 0f, min = 0f, max = 2f, step = 1f, unit = "",
                discreteLabels = new[] { "Recorded", "Performance", "Hardcore" } },
            // Auto replays the pedalling restored from the source MIDI; Pedal
            // follows a real sustain pedal over MIDI CC64.
            new SettingData { title = "Sustain Pedal", value = 0f, min = 0f, max = 1f, step = 1f, unit = "",
                isToggle = true, minLabel = "Auto (Chart)", maxLabel = "My Pedal" },
            // Only reached by charts whose source MIDI had no pedal at all. It is
            // a guess at where the harmony changes, so it stays off by default.
            new SettingData { title = "Pedal If Missing", value = 0f, min = 0f, max = 2f, step = 1f, unit = "",
                discreteLabels = new[] { "Off", "Per Beat", "Per Bar" } },
            // How long a judged note keeps its neighbouring lanes protected. A
            // chord's keys do not land together, and once this expires the late
            // ones claim the next note instead of being ignored.
            new SettingData { title = "Chord Spread", value = 20f, min = 0f, max = 150f, step = 10f,
                unit = " ms" },
            // How much harder it is for a not-yet-due note to take a press. At 1
            // a late hit is usually credited to the next note and scored early.
            new SettingData { title = "Late Hit Priority", value = 1f, min = 1f, max = 3f, step = 0.1f,
                unit = "x" },
            // Refuses a note to a press this far early, so a better-timed press
            // behind it can take the note instead. 0 keeps the window symmetric.
            new SettingData { title = "Early Claim Limit", value = 0f, min = 0f, max = 150f, step = 5f,
                unit = " ms" },
            // Same correction as the limit above, but the note is still awarded
            // and its judgment rewritten instead, so nothing turns into a miss.
            new SettingData { title = "Retime Better Press", value = 0f, min = 0f, max = 1f, step = 1f,
                unit = "", isToggle = true },
            // Performance mode only: how much of the player's timing reaches the
            // music. 0 sounds every note exactly when the key went down.
            new SettingData { title = "Performance Timing Spread", value = 20f, min = 0f, max = 60f,
                step = 5f, unit = " ms" },
            // 軌道玻璃化。0 是現在的實心大理石；往上推，軌道變成一片只把背景壓暗的
            // 玻璃，背景於是能透過整個 playfield 被看見，而可讀性由這片玻璃保證。
            new SettingData { title = "Track Glass", value = 0f, min = 0f, max = 1f, step = 0.05f,
                unit = "%", isPercentage = true },
            // 全玻璃時背景還能透多少。調高更通透，但音符會開始難認。
            new SettingData { title = "Glass Background", value = 0.55f, min = 0.2f, max = 1f,
                step = 0.05f, unit = "%", isPercentage = true },
            // 背景跟著調性變色的強度。1 是實測校準的幅度，2 是誇張版——調不出來
            // 在動的時候先拉到 2 確認，再往回收。
            new SettingData { title = "Background Harmony", value = 1f, min = 0f, max = 2f,
                step = 0.1f, unit = "x" },
            // 排列很緊、彈簧很鬆的鍵盤上，擦到隔壁鍵的力度幾乎都很低。這一刀最直接。
            new SettingData { title = "Min Key Velocity", value = 0f, min = 0f, max = 60f,
                step = 5f, unit = "" },
            // 比鄰鍵明顯輕、又幾乎同時 → 當成擦碰丟掉。兩個條件都要成立才擋，
            // 才不會誤殺真正的弱奏或刻意的小二度和弦。
            new SettingData { title = "Brush Rejection", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "", isToggle = true },
            // 同一顆鍵的接點彈跳。放鍵就解除，所以擋掉的永遠不會是真的重擊。
            new SettingData { title = "Key Chatter Guard", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "", isToggle = true },
            new SettingData { title = "Hold Magic Circle", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "", isToggle = true },
            // 軌道上會捲動的踏板提示。關掉就完全不畫。
            new SettingData { title = "Pedal Cues", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "", isToggle = true },
            // 官方的輸入是每 16.66ms 一個集合，判定成功後同幀落在音符左右各 3 格
            // 的按鍵全部失效——擦碰是被規則吃掉的，不是靠偵測。關掉會退回逐鍵
            // 即時判定，延遲略低但擦碰要靠單向的近似去擋。
            new SettingData { title = "Input Frame", value = 1f, min = 0f, max = 2f,
                step = 1f, unit = "",
                discreteLabels = new[] { "Off", "Full", "Sound" } },
            // 只在輸入幀打開時有作用。幀量化讓一個和弦拿到一致的判定（官方就是
            // 這樣）；精準保留每顆鍵自己的時間戳，精度較高但和弦可能混合判定。
            new SettingData { title = "Judgment Timing", value = 0f, min = 0f, max = 1f,
                step = 1f, unit = "",
                discreteLabels = new[] { "Precise", "Frame" } },
            // 扣住很輕的按鍵一下下，看有沒有更響的鄰鍵跟上。輸入幀打開時通常
            // 不需要（兩者擋的是同一批東西，一起開會疊加延遲）。
            new SettingData { title = "Brush Look-ahead", value = 0f, min = 0f, max = 40f,
                step = 1f, unit = "ms" },
            // 把譜面的力度差距以這首曲子自己的中間值為軸放大幾倍。1 = 照譜面
            // 原樣彈。還原出來的力度多半擠在很窄的一段裡，所以預設就開了一點。
            new SettingData { title = "Dynamic Range", value = 1.6f, min = 1f, max = 3f,
                step = 0.1f, unit = "x" },
            // 演奏會模式裡標在音符正中央的寶石。關掉不影響計分，只是不畫。
            new SettingData { title = "Centre Cue", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "", isToggle = true },
            // 把每首曲子的播放響度拉到同一個高度。這個曲庫的主音軌之間差 22 dB，
            // 所以預設是開的。
            new SettingData { title = "Loudness Match", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "", isToggle = true },
            // 判定的取向。選到「自訂」才會露出底下那十一項細部參數。
            new SettingData { title = "Judgment Preset", value = 2f, min = 0f, max = 2f,
                step = 1f, unit = "",
                discreteLabels = new[] { "Responsive", "Classic", "Custom" } },
            // 玩家的觸鍵力度被換算到譜面的尺上多少。0 = 手輕就是小聲（最真實），
            // 1 = 每一下都對照自己的平均來讀（輕手的人也有飽滿的音色）。
            new SettingData { title = "Touch Alignment", value = 1f, min = 0f, max = 1f,
                step = 0.05f, unit = "%", isPercentage = true },
            // 按下去的那一鍵在判定線上的提示有多長。0 = 不顯示。
            new SettingData { title = "Key Cue Height", value = 1f, min = 0f, max = 3f,
                step = 0.1f, unit = "x" },
            // 一鍵套用本家：判定切類原型，能對應本家的設定一次改好。見
            // SettingsManager.ApplyArcadeSettings。
            new SettingData { title = "Apply Arcade Settings", isAction = true },
            // 音符落下的拋物線高度。本家的音符是先被往上拋、再落到判定線上
            // （曲線見 NoteArc）。0 = 直線落下，也就是這個 clone 原本的樣子。
            new SettingData { title = "Note Arc", value = 0.45f, min = 0f, max = 1f,
                step = 0.05f, unit = "%", isPercentage = true },
            // 音符怎麼落下。傾斜＝原本的直線，高度由鏡頭俯角決定；
            // 拋物線＝本家的做法，先上拋再落到判定線。
            // 這兩個模式各自用一個滑桿：選傾斜時露出「傾斜角度」，
            // 選拋物線時那一列換成「拋物線頂點」（見 RefreshNoteFallVisibility）。
            new SettingData { title = "Note Fall", value = 1f, min = 0f, max = 1f,
                step = 1f, unit = "",
                discreteLabels = new[] { "Tilt", "Arc" } },
            // 弧線的長度（世界單位），也就是拋物線模式的生成距離。用絕對長度
            // 而不是秒數：秒數會被下落速度放大，調快速度整條跑道就變形。
            // 音符、拍子線、踏板都照這一個值生成，三邊才會在同一條弧線上。
            new SettingData { title = "Arc Length", value = 160f, min = 20f, max = 800f,
                step = 10f, unit = "" },
            // 用哪一條落下曲線。本家那條不是拋物線（是 cos(π(p²−0.5))），頂點在
            // 行程 29%、落地很陡；真的二次函數頂點在 56%、落地平一些。
            new SettingData { title = "Arc Curve", value = 0f, min = 0f, max = 1f,
                step = 1f, unit = "",
                discreteLabels = new[] { "Arcade", "Parabola" } },
            // 音符從離判定線多遠開始生成。曲線本身不動——距離短就是從路線上
            // 比較靠近判定線的那一點開始，不是把整條曲線壓進來。
            new SettingData { title = "Spawn Distance", value = 160f, min = 20f, max = 800f,
                step = 10f, unit = "" }
        };

        groupMembers.Clear();

        // 分類的依據是**玩家帶著什麼念頭來**，不是這個值住在哪個類別裡。
        //
        // 每一組只負責一件事，而且那件事要能用一個名詞講完 —— 講不完就表示它其
        // 實是兩件事，該拆開。

        // 音效 ------------------------------------------------------------
        SetGroup(SettingsCategory.Audio, SettingsGroup.Volume,
            MusicVolumeIndex, PianoVolumeIndex, HitSoundVolumeIndex, NormalizeLoudnessIndex);
        SetGroup(SettingsCategory.Audio, SettingsGroup.Piano,
            PianoSoundModeIndex, PianoDynamicExpansionIndex, TouchAlignmentIndex,
            PianoPerformanceSpreadIndex);
        SetGroup(SettingsCategory.Audio, SettingsGroup.Pedal,
            PianoPedalSourceIndex, PianoAutoPedalIndex);
        SetGroup(SettingsCategory.Audio, SettingsGroup.Sync,
            MusicPlaybackOffsetIndex, PianoPreviewIndex);

        // 判定 ------------------------------------------------------------
        SetGroup(SettingsCategory.Judgment, SettingsGroup.JudgmentMode,
            JudgmentPresetIndex, ApplyArcadeIndex, JudgmentOffsetIndex);
        SetGroup(SettingsCategory.Judgment, SettingsGroup.JudgmentDetail,
            InputFrameBatchingIndex, JudgmentTimingIndex, ChordSpreadIndex,
            LateHitPriorityIndex, EarlyClaimLimitIndex, RetimeBetterPressIndex);
        SetGroup(SettingsCategory.Judgment, SettingsGroup.Mistouch,
            MinNoteVelocityIndex, BrushRejectionIndex, BrushLookaheadIndex,
            KeyChatterGuardIndex);

        // 顯示 ------------------------------------------------------------
        // 落下方式和它的那一個數值擺在一起，而且緊接著彼此：選傾斜時第二列是
        // 「傾斜角度」，選拋物線時同一個位置換成「拋物線頂點」，看起來就是
        // 那一列改了意思（實際上是兩列輪流露出，見 RefreshNoteFallVisibility）。
        SetGroup(SettingsCategory.Visual, SettingsGroup.Track,
            NoteFallModeIndex, CameraRotXIndex, NoteArcHeightIndex, NoteArcLengthIndex,
            NoteArcSpawnIndex, NoteArcCurveIndex,
            VisualLayoutModeIndex, TrackMarbleThemeIndex,
            TrackGuideLineOpacityIndex, TrackGlassIndex, TrackGlassFloorIndex,
            KeyboardScreenBottomIndex);
        SetGroup(SettingsCategory.Visual, SettingsGroup.Notes,
            DefaultSpeedIndex, NoteVisualHeightIndex, HoldMagicCircleIndex,
            ShowPedalCuesIndex, ShowCentreCueIndex);
        SetGroup(SettingsCategory.Visual, SettingsGroup.JudgmentLine,
            JudgmentLineHeightIndex, JudgmentMeshHeightIndex, JudgePopupHeightIndex,
            TimingSensitiveVisualsIndex, StrikeCueHeightIndex);
        SetGroup(SettingsCategory.Visual, SettingsGroup.Combo,
            ComboDisplayIndex, ComboFontSizeIndex, TrackComboHeightIndex);
        SetGroup(SettingsCategory.Visual, SettingsGroup.Background,
            BackgroundHarmonyIndex, TrackVideoDimmerIndex);

        // 其餘 ------------------------------------------------------------
        SetGroup(SettingsCategory.Gameplay, SettingsGroup.Session,
            StartDelayIndex, DebugModeIndex);
        SetGroup(SettingsCategory.SongManagement, SettingsGroup.Library,
            AddSongsIndex, EditSongsIndex, DeleteSongsIndex, CreateSongCategoryIndex);
        SetGroup(SettingsCategory.System, SettingsGroup.General,
            InputModeIndex, LanguageIndex);

        LoadFromSettingsManager();
    }

    /// <summary>
    /// 把一組設定放進一個分類的一個子分類。
    /// </summary>
    /// <remarks>
    /// 分類和子分類一起指定，**不能分兩次寫**。分開的話漏掉其中一邊的項目會安靜
    /// 地掉進 enum 的第一個值裡，而那種錯誤要等到有人翻遍設定找不到東西才會被
    /// 發現。
    ///
    /// 呼叫時寫的順序就是畫面上的順序。
    /// </remarks>
    private void SetGroup(SettingsCategory category, SettingsGroup group, params int[] indices)
    {
        if (!groupMembers.TryGetValue(group, out System.Collections.Generic.List<int> members))
        {
            members = new System.Collections.Generic.List<int>();
            groupMembers[group] = members;
        }
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0 || indices[i] >= settings.Length) continue;
            if (settings[indices[i]] == null) continue;
            settings[indices[i]].category = category;
            settings[indices[i]].group = group;
            if (!members.Contains(indices[i])) members.Add(indices[i]);
        }
    }

    private void SetupButtons()
    {
        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(ShowSettings);
            settingsButton.interactable = true;
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(HideSettings);
        }
    }

    private bool IsPointerOverPreviewSongCard(Vector2 screenPoint)
    {
        Canvas canvas = settingsPanel != null ? settingsPanel.GetComponentInParent<Canvas>() : null;
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        if (gameplayPreviewTitle != null && RectTransformUtility.RectangleContainsScreenPoint(
                gameplayPreviewTitle.rectTransform, screenPoint, eventCamera)) return true;
        if (previewPreviousSongButton != null && RectTransformUtility.RectangleContainsScreenPoint(
                previewPreviousSongButton.transform as RectTransform, screenPoint, eventCamera)) return true;
        if (previewNextSongButton != null && RectTransformUtility.RectangleContainsScreenPoint(
                previewNextSongButton.transform as RectTransform, screenPoint, eventCamera)) return true;
        return false;
    }

    public void ShowSettings()
    {
        if (settingsPanel == null) return;

        // Apply saved camera values before the preview camera's first frame.
        // GameplayRoot OnEnable may restore authored transforms, so apply
        // once before and once after the panel reveals its gameplay visuals.
        LoadFromSettingsManager();
        SettingsManager.Instance?.ApplySettings();
        arcadeAppliedShown = false;
        settingsPanel.SetActive(true);
        settingsPanel.transform.SetAsLastSibling();
        Transform legacyBackground = settingsPanel.transform.Find("BackGround");
        if (legacyBackground != null) legacyBackground.gameObject.SetActive(false);
        Transform content = settingsPanel.transform.Find("SettingPanelContent");
        if (content != null) content.gameObject.SetActive(true);
        SettingsManager.Instance?.ApplySettings();
        ConfigurePreviewSong(true);

        // 每次打開都從設定頁開始，而且捲回最上面。上次停在預覽的話，一打開就
        // 會有一首歌開始播，而玩家這一次也許只是來換語言的。
        ResetListScroll(pageList);
        ResetListScroll(railList);
        SetScreenMode(ScreenMode.Page);

        if (hideSelectionPanelWhenOpen && SongSelectionManager.Instance != null)
        {
            SongSelectionManager.Instance.HideSelectionPanelOnly();
        }
    }

    public void HideSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);

            if (hideSelectionPanelWhenOpen && SongSelectionManager.Instance != null)
            {
                SongSelectionManager.Instance.ShowSelectionPanelOnly();
            }
        }
    }

    /// <summary>
    /// 細部判定參數只在「自訂」底下露出來，並且動到它們就自動切成自訂。
    /// </summary>
    /// <remarks>
    /// 後面那一半是關鍵：玩家在自訂裡調完之後，**選擇就停在自訂上**，不會因為
    /// 某個值剛好和某個預設一樣就被認定成那個預設。預設是一個選擇，不是一組值
    /// 的比對結果。
    /// </remarks>
    /// <summary>
    /// 落下模式決定「傾斜角度」和「拋物線頂點」哪一個露出來。
    ///
    /// 兩者是同一件事的兩種講法——音符的高度從哪裡來——所以同時擺出來只會讓人
    /// 以為可以一起用。選了哪個模式就只留哪一個。
    /// </summary>
    private void RefreshNoteFallVisibility()
    {
        if (settings == null || settings.Length <= NoteFallModeIndex) return;
        bool arc = Mathf.RoundToInt(settings[NoteFallModeIndex].value) == 1;
        if (NoteArcHeightIndex < settings.Length && settings[NoteArcHeightIndex] != null)
            settings[NoteArcHeightIndex].isHidden = !arc;
        if (NoteArcLengthIndex < settings.Length && settings[NoteArcLengthIndex] != null)
            settings[NoteArcLengthIndex].isHidden = !arc;
        if (NoteArcCurveIndex < settings.Length && settings[NoteArcCurveIndex] != null)
            settings[NoteArcCurveIndex].isHidden = !arc;
        if (NoteArcSpawnIndex < settings.Length && settings[NoteArcSpawnIndex] != null)
            settings[NoteArcSpawnIndex].isHidden = !arc;
        if (CameraRotXIndex < settings.Length && settings[CameraRotXIndex] != null)
            settings[CameraRotXIndex].isHidden = arc;
    }

    private void RefreshJudgmentDetailVisibility()
    {
        if (settings == null || settings.Length <= JudgmentPresetIndex) return;
        bool custom = Mathf.RoundToInt(settings[JudgmentPresetIndex].value)
            == (int)JudgmentPreset.Custom;
        for (int i = 0; i < JudgmentDetailIndices.Length; i++)
        {
            int index = JudgmentDetailIndices[i];
            if (index >= 0 && index < settings.Length && settings[index] != null)
                settings[index].isHidden = !custom;
        }

        // 和弦落鍵容許調的是舊的鄰鍵停止時間。鄰鍵保護已經換成本家固定的鄰鍵鎖
        // （只擋較晚的音符，和弦裡晚到的手指本來就不會被擋），這一列沒有東西可調了。
        // 索引不能拿掉，只能藏起來。
        if (ChordSpreadIndex < settings.Length && settings[ChordSpreadIndex] != null)
            settings[ChordSpreadIndex].isHidden = true;
        // 中央提示（音符中心格的寶石）已經拿掉：演奏會改評本家的「多鍵」，不評中心格。
        if (ShowCentreCueIndex < settings.Length && settings[ShowCentreCueIndex] != null)
            settings[ShowCentreCueIndex].isHidden = true;
    }

    /// <summary>動到細部參數就等於選了自訂。</summary>
    private void MarkJudgmentCustom(int changedIndex)
    {
        if (settings == null || settings.Length <= JudgmentPresetIndex) return;
        bool isDetail = false;
        for (int i = 0; i < JudgmentDetailIndices.Length; i++)
            if (JudgmentDetailIndices[i] == changedIndex) { isDetail = true; break; }
        if (!isDetail) return;

        var manager = SettingsManager.Instance;
        if (manager == null || manager.CurrentJudgmentPreset == JudgmentPreset.Custom) return;
        settings[JudgmentPresetIndex].value = (int)JudgmentPreset.Custom;
        manager.SetJudgmentPreset(JudgmentPreset.Custom);
    }

    /// <summary>把畫面上所有看得到的東西對齊到目前的值。</summary>
    private void UpdateDisplay()
    {
        if (settings == null || settings.Length == 0) return;
        RefreshChrome();
        RefreshLists();
    }

    /// <summary>
    /// 只有在「譜面預覽」畫面才跑即時的譜面。
    /// </summary>
    /// <remarks>
    /// 預覽是一整場遊戲：放音樂、佔效能。設定頁上看不到它，就不該讓它在背後跑。
    /// 兩個呼叫都有自己的旗標擋著，重複呼叫不會重開。
    /// </remarks>
    private void RefreshGameplayPreview()
    {
        if (gameplayPreview == null) return;
        bool wanted = screenMode == ScreenMode.Preview
            && settingsPanel != null && settingsPanel.activeInHierarchy;
        if (wanted) gameplayPreview.BeginPreviewSession();
        else gameplayPreview.EndPreviewSession();
    }

    private string FormatSettingValue(SettingData setting)
    {
        if (setting == null) return "";
        if (setting.isAction)
        {
            // 按下之後給一句確認，不然一個沒有數值的列按了看起來像沒反應。
            if (arcadeAppliedShown && ReferenceEquals(setting, settings[ApplyArcadeIndex]))
                return CurrentLanguage == AppLanguage.SimplifiedChinese ? "已套用"
                    : IsChinese ? "已套用" : "Applied";
            return "";
        }
        if (setting.discreteLabels != null && setting.discreteLabels.Length > 0)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(setting.value), 0,
                setting.discreteLabels.Length - 1);
            return setting.discreteLabels[index];
        }
        if (setting.isToggle) return setting.value >= (setting.min + setting.max) * 0.5f ? setting.ResolveMaxLabel() : setting.ResolveMinLabel();
        if (setting.isPercentage) return $"{(setting.value * 100):F0}{setting.unit}";
        if (setting.step >= 1f) return $"{setting.value:F0}{setting.unit}";
        if (!Mathf.Approximately(setting.step * 10f, Mathf.Round(setting.step * 10f)))
            return $"{setting.value:F2}{setting.unit}";
        return $"{setting.value:F1}{setting.unit}";
    }

    private void LoadFromSettingsManager()
    {
        SettingsManager manager = SettingsManager.Instance;
        if (manager == null || settings == null) return;

        settings[InputModeIndex].value = Mathf.Clamp((int)manager.InputMode, 0, 2);
        settings[HitSoundVolumeIndex].value = manager.HitSoundVolume;
        settings[MusicVolumeIndex].value = manager.MusicVolume;
        settings[PianoVolumeIndex].value = manager.PianoVolume;
        settings[DefaultSpeedIndex].value = manager.DefaultSpeed;
        settings[StartDelayIndex].value = manager.GameStartDelay;
        settings[JudgePopupHeightIndex].value = manager.JudgePopupHeight;
        settings[JudgmentLineHeightIndex].value = manager.JudgmentLineHeight;
        settings[CameraRotXIndex].value = manager.CameraRotX;
        settings[DebugModeIndex].value = manager.DebugMode ? 1f : 0f;
        settings[JudgmentOffsetIndex].value = manager.JudgmentOffsetMs;
        settings[MusicPlaybackOffsetIndex].value = manager.MusicPlaybackOffsetMs;
        settings[PianoPreviewIndex].value = manager.EnablePianoPreview ? 1f : 0f;
        settings[PianoSoundModeIndex].value = (int)manager.CurrentPianoSoundMode;
        settings[PianoPedalSourceIndex].value = (int)manager.CurrentPianoPedalSource;
        settings[PianoAutoPedalIndex].value = (int)manager.CurrentPianoAutoPedal;
        settings[ChordSpreadIndex].value = manager.SpatialStopWindowMs;
        settings[LateHitPriorityIndex].value = manager.FutureNotePenalty;
        settings[EarlyClaimLimitIndex].value = manager.EarlyClaimLimitMs;
        settings[RetimeBetterPressIndex].value = manager.RetimeBetterPress ? 1f : 0f;
        settings[PianoPerformanceSpreadIndex].value = manager.PianoPerformanceSpreadMs;
        settings[KeyChatterGuardIndex].value = manager.BlockKeyChatter ? 1f : 0f;
        settings[HoldMagicCircleIndex].value = manager.HoldMagicCircleEnabled ? 1f : 0f;
        settings[ShowPedalCuesIndex].value = manager.ShowPedalCues ? 1f : 0f;
        settings[ShowCentreCueIndex].value = manager.ShowCentreCue ? 1f : 0f;
        settings[NormalizeLoudnessIndex].value = manager.NormalizeMusicLoudness ? 1f : 0f;
        settings[JudgmentPresetIndex].value = (int)manager.CurrentJudgmentPreset;
        settings[TouchAlignmentIndex].value = manager.TouchAlignment;
        settings[StrikeCueHeightIndex].value = manager.StrikeCueHeight;
        settings[NoteArcHeightIndex].value = manager.NoteArcHeight;
        settings[NoteFallModeIndex].value = manager.NoteFallMode;
        settings[NoteArcLengthIndex].value = manager.ArcTravelWorldUnits();
        settings[NoteArcCurveIndex].value = manager.NoteArcCurve;
        settings[NoteArcSpawnIndex].value = manager.ArcSpawnWorldUnits();
        RefreshNoteFallVisibility();
        RefreshJudgmentDetailVisibility();
        settings[TrackVideoDimmerIndex].value = manager.TrackVideoDimmer;
        settings[VisualLayoutModeIndex].value = manager.CurrentVisualLayoutMode == VisualLayoutMode.Piano88 ? 1f : 0f;
        settings[NoteVisualHeightIndex].value = manager.NoteVisualHeight;
        settings[TrackGuideLineOpacityIndex].value = manager.TrackGuideLineOpacity;
        settings[TrackGlassIndex].value = manager.TrackGlass;
        settings[TrackGlassFloorIndex].value = manager.TrackGlassFloor;
        settings[BackgroundHarmonyIndex].value = manager.BackgroundHarmonyStrength;
        settings[MinNoteVelocityIndex].value = manager.MinNoteOnVelocity;
        settings[BrushRejectionIndex].value = manager.BrushRejectionEnabled ? 1f : 0f;
        settings[InputFrameBatchingIndex].value = (int)manager.CurrentInputFrameMode;
        settings[JudgmentTimingIndex].value = (int)manager.CurrentJudgmentTiming;
        settings[BrushLookaheadIndex].value = manager.BrushLookaheadMs;
        settings[PianoDynamicExpansionIndex].value = manager.PianoDynamicExpansion;
        settings[TrackMarbleThemeIndex].value = manager.CurrentTrackMarbleTheme == TrackMarbleTheme.White ? 1f : 0f;
        settings[JudgmentMeshHeightIndex].value = manager.JudgmentMeshHeight;
        settings[TimingSensitiveVisualsIndex].value = manager.TimingSensitiveJudgmentVisuals ? 1f : 0f;
        settings[KeyboardScreenBottomIndex].value = manager.AutoAlignKeyboardToScreenBottom ? 1f : 0f;
        settings[LanguageIndex].value = manager.CurrentLanguage == AppLanguage.SimplifiedChinese
            ? 1f
            : manager.CurrentLanguage == AppLanguage.English ? 2f : 0f;
        settings[ComboDisplayIndex].value = (float)manager.CurrentComboDisplayPosition;
        settings[ComboFontSizeIndex].value = manager.ComboFontSize;
        settings[TrackComboHeightIndex].value = manager.TrackComboScreenHeight;
    }

    private void ApplyToSettingsManager()
    {
        // 動到任何一項細部判定參數，選擇就落在「自訂」上。
        //
        // 放在最前面：底下那個 switch 會呼叫 manager 的各個 Setter，而預設本身
        // 也是透過 manager 換的 —— 先把歸屬定下來，後面就不會有一次寫入在兩個
        // 預設之間搖擺。
        MarkJudgmentCustom(currentIndex);
        SettingsManager manager = SettingsManager.Instance;
        if (manager == null || settings == null) return;

        switch (currentIndex)
        {
            case InputModeIndex:
                manager.SetInputMode((InputModeType)Mathf.Clamp(
                    Mathf.RoundToInt(settings[InputModeIndex].value), 0, 2));
                break;
            case HitSoundVolumeIndex:
                manager.SetHitSoundVolume(settings[HitSoundVolumeIndex].value);
                break;
            case MusicVolumeIndex:
                manager.SetMusicVolume(settings[MusicVolumeIndex].value);
                SongSelectionManager.Instance?.RefreshPreviewAudio(false);
                break;
            case PianoVolumeIndex:
                manager.SetPianoVolume(settings[PianoVolumeIndex].value);
                break;
            case DefaultSpeedIndex:
                manager.SetDefaultSpeed(settings[DefaultSpeedIndex].value);
                break;
            case StartDelayIndex:
                manager.SetGameStartDelay(settings[StartDelayIndex].value);
                break;
            case JudgePopupHeightIndex:
                manager.SetJudgePopupHeight(settings[JudgePopupHeightIndex].value);
                break;
            case JudgmentLineHeightIndex:
                manager.SetJudgmentLineHeight(settings[JudgmentLineHeightIndex].value);
                break;
            case CameraRotXIndex:
                manager.SetCameraRotX(settings[CameraRotXIndex].value);
                break;
            case DebugModeIndex:
                manager.SetDebugMode(settings[DebugModeIndex].value >= 0.5f);
                break;
            case JudgmentOffsetIndex:
                manager.SetJudgmentOffsetMs(settings[JudgmentOffsetIndex].value);
                break;
            case MusicPlaybackOffsetIndex:
                manager.SetMusicPlaybackOffsetMs(settings[MusicPlaybackOffsetIndex].value);
                break;
            case PianoPreviewIndex:
                manager.SetEnablePianoPreview(settings[PianoPreviewIndex].value >= 0.5f);
                break;
            case PianoSoundModeIndex:
                manager.SetPianoSoundMode((PianoSoundMode)Mathf.Clamp(
                    Mathf.RoundToInt(settings[PianoSoundModeIndex].value), 0, 2));
                break;
            case PianoPedalSourceIndex:
                manager.SetPianoPedalSource(settings[PianoPedalSourceIndex].value >= 0.5f
                    ? PianoPedalSource.Player
                    : PianoPedalSource.Chart);
                break;
            case PianoAutoPedalIndex:
                manager.SetPianoAutoPedal((PianoAutoPedal)Mathf.Clamp(
                    Mathf.RoundToInt(settings[PianoAutoPedalIndex].value), 0, 2));
                break;
            case ChordSpreadIndex:
                manager.SetSpatialStopWindowMs(
                    Mathf.RoundToInt(settings[ChordSpreadIndex].value));
                break;
            case LateHitPriorityIndex:
                manager.SetFutureNotePenalty(settings[LateHitPriorityIndex].value);
                break;
            case EarlyClaimLimitIndex:
                manager.SetEarlyClaimLimitMs(
                    Mathf.RoundToInt(settings[EarlyClaimLimitIndex].value));
                break;
            case RetimeBetterPressIndex:
                manager.SetRetimeBetterPress(settings[RetimeBetterPressIndex].value >= 0.5f);
                break;
            case PianoPerformanceSpreadIndex:
                manager.SetPianoPerformanceSpreadMs(
                    Mathf.RoundToInt(settings[PianoPerformanceSpreadIndex].value));
                break;
            case KeyChatterGuardIndex:
                manager.SetBlockKeyChatter(settings[KeyChatterGuardIndex].value >= 0.5f);
                break;
            case HoldMagicCircleIndex:
                manager.SetHoldMagicCircleEnabled(settings[HoldMagicCircleIndex].value >= 0.5f);
                break;
            case ShowPedalCuesIndex:
                manager.SetShowPedalCues(settings[ShowPedalCuesIndex].value >= 0.5f);
                break;
            case ShowCentreCueIndex:
                manager.SetShowCentreCue(settings[ShowCentreCueIndex].value >= 0.5f);
                break;
            case NormalizeLoudnessIndex:
                manager.SetNormalizeMusicLoudness(
                    settings[NormalizeLoudnessIndex].value >= 0.5f);
                break;
            case TouchAlignmentIndex:
                manager.SetTouchAlignment(settings[TouchAlignmentIndex].value);
                break;
            case StrikeCueHeightIndex:
                manager.SetStrikeCueHeight(settings[StrikeCueHeightIndex].value);
                break;
            case NoteArcHeightIndex:
                manager.SetNoteArcHeight(settings[NoteArcHeightIndex].value);
                break;
            case NoteArcLengthIndex:
                manager.SetNoteArcLength(settings[NoteArcLengthIndex].value);
                break;
            case NoteArcCurveIndex:
                manager.SetNoteArcCurve(Mathf.RoundToInt(settings[NoteArcCurveIndex].value));
                break;
            case NoteArcSpawnIndex:
                manager.SetNoteArcSpawn(settings[NoteArcSpawnIndex].value);
                break;
            case NoteFallModeIndex:
                manager.SetNoteFallMode(Mathf.RoundToInt(settings[NoteFallModeIndex].value));
                RefreshNoteFallVisibility();
                break;
            case JudgmentPresetIndex:
                manager.SetJudgmentPreset((JudgmentPreset)Mathf.Clamp(
                    Mathf.RoundToInt(settings[JudgmentPresetIndex].value), 0, 2));
                // 預設會把底下那一整組值重寫，所以介面要重讀一次，否則旁邊顯示
                // 的還是上一組。
                LoadFromSettingsManager();
                RefreshJudgmentDetailVisibility();
                break;
            case TrackVideoDimmerIndex:
                manager.SetTrackVideoDimmer(settings[TrackVideoDimmerIndex].value);
                break;
            case VisualLayoutModeIndex:
                manager.SetVisualLayoutMode(settings[VisualLayoutModeIndex].value >= 0.5f ? VisualLayoutMode.Piano88 : VisualLayoutMode.General);
                break;
            case NoteVisualHeightIndex:
                manager.SetNoteVisualHeight(settings[NoteVisualHeightIndex].value);
                break;
            case TrackGuideLineOpacityIndex:
                manager.SetTrackGuideLineOpacity(settings[TrackGuideLineOpacityIndex].value);
                break;
            case TrackGlassIndex:
                manager.SetTrackGlass(settings[TrackGlassIndex].value);
                break;
            case TrackGlassFloorIndex:
                manager.SetTrackGlassFloor(settings[TrackGlassFloorIndex].value);
                break;
            case BackgroundHarmonyIndex:
                manager.SetBackgroundHarmonyStrength(settings[BackgroundHarmonyIndex].value);
                break;
            case MinNoteVelocityIndex:
                manager.SetMinNoteOnVelocity(settings[MinNoteVelocityIndex].value);
                break;
            case InputFrameBatchingIndex:
                manager.SetInputFrameMode((InputFrameMode)Mathf.Clamp(
                    Mathf.RoundToInt(settings[InputFrameBatchingIndex].value), 0, 2));
                break;
            case JudgmentTimingIndex:
                manager.SetJudgmentTiming(settings[JudgmentTimingIndex].value >= 0.5f
                    ? JudgmentTiming.FrameQuantized
                    : JudgmentTiming.Precise);
                break;
            case BrushLookaheadIndex:
                manager.SetBrushLookaheadMs(settings[BrushLookaheadIndex].value);
                break;
            case PianoDynamicExpansionIndex:
                manager.SetPianoDynamicExpansion(settings[PianoDynamicExpansionIndex].value);
                break;
            case BrushRejectionIndex:
                manager.SetBrushRejection(settings[BrushRejectionIndex].value > 0.5f);
                break;
            case TrackMarbleThemeIndex:
                manager.SetTrackMarbleTheme(settings[TrackMarbleThemeIndex].value >= 0.5f
                    ? TrackMarbleTheme.White
                    : TrackMarbleTheme.Black);
                break;
            case JudgmentMeshHeightIndex:
                manager.SetJudgmentMeshHeight(settings[JudgmentMeshHeightIndex].value);
                break;
            case TimingSensitiveVisualsIndex:
                manager.SetTimingSensitiveJudgmentVisuals(
                    settings[TimingSensitiveVisualsIndex].value >= 0.5f);
                break;
            case KeyboardScreenBottomIndex:
                manager.SetAutoAlignKeyboardToScreenBottom(
                    settings[KeyboardScreenBottomIndex].value >= 0.5f);
                break;
            case LanguageIndex:
                int selectedLanguage = Mathf.Clamp(
                    Mathf.RoundToInt(settings[LanguageIndex].value), 0, 2);
                manager.SetLanguage(selectedLanguage == 1
                    ? AppLanguage.SimplifiedChinese
                    : selectedLanguage == 2
                        ? AppLanguage.English
                        : AppLanguage.TraditionalChinese);
                break;
            case ComboDisplayIndex:
                manager.SetComboDisplayPosition((ComboDisplayPosition)Mathf.Clamp(
                    Mathf.RoundToInt(settings[ComboDisplayIndex].value), 0, 3));
                break;
            case ComboFontSizeIndex:
                manager.SetComboFontSize(settings[ComboFontSizeIndex].value);
                break;
            case TrackComboHeightIndex:
                manager.SetTrackComboScreenHeight(settings[TrackComboHeightIndex].value);
                break;
        }

        manager.SaveSettings();
        manager.ApplySettings();
        if (currentIndex == LanguageIndex) ApplyLocalization();
        gameplayPreview?.RefreshNow();
    }

    private AppLanguage CurrentLanguage => SettingsManager.Instance != null
        ? SettingsManager.Instance.CurrentLanguage
        : AppLanguage.TraditionalChinese;
    private bool IsChinese => CurrentLanguage != AppLanguage.English;

    private string GetCategoryName(SettingsCategory category)
    {
        bool simplified = CurrentLanguage == AppLanguage.SimplifiedChinese;
        switch (category)
        {
            case SettingsCategory.Audio:
                return simplified ? "音效设置" : IsChinese ? "音效設定" : "Sound Settings";
            case SettingsCategory.Gameplay:
                return simplified ? "游戏设置" : IsChinese ? "遊戲設定" : "Gameplay";
            case SettingsCategory.Judgment:
                return simplified ? "判定设置" : IsChinese ? "判定設定" : "Judgment";
            case SettingsCategory.Visual:
                return simplified ? "显示设置" : IsChinese ? "顯示設定" : "Display";
            case SettingsCategory.SongManagement:
                return simplified ? "曲目管理" : IsChinese ? "曲目管理" : "Song Management";
            default:
                return simplified ? "系统设置" : IsChinese ? "系統設定" : "System Settings";
        }
    }

    /// <summary>
    /// 子分類的名字。
    /// </summary>
    /// <remarks>
    /// 一律用**名詞**，而且是玩家腦子裡那個詞：他想的是「判定線太低」不是「打擊
    /// 回饋的垂直位置」。名字要能被搜尋 —— 即使這裡沒有搜尋框，玩家的眼睛也是在
    /// 搜尋。
    /// </remarks>
    private string GetGroupName(SettingsGroup group)
    {
        bool simplified = CurrentLanguage == AppLanguage.SimplifiedChinese;
        bool zh = IsChinese;
        switch (group)
        {
            case SettingsGroup.Volume:
                return simplified ? "音量" : zh ? "音量" : "Volume";
            case SettingsGroup.Piano:
                return simplified ? "钢琴" : zh ? "鋼琴" : "Piano";
            case SettingsGroup.Pedal:
                return simplified ? "踏板" : zh ? "踏板" : "Pedal";
            case SettingsGroup.Sync:
                return simplified ? "对拍" : zh ? "對拍" : "Sync";
            case SettingsGroup.JudgmentMode:
                return simplified ? "判定模式" : zh ? "判定模式" : "Mode";
            case SettingsGroup.JudgmentDetail:
                return simplified ? "细部参数" : zh ? "細部參數" : "Details";
            case SettingsGroup.Mistouch:
                return simplified ? "误触防护" : zh ? "誤觸防護" : "Mistouch";
            case SettingsGroup.Track:
                return simplified ? "轨道" : zh ? "軌道" : "Track";
            case SettingsGroup.Notes:
                return simplified ? "音符" : zh ? "音符" : "Notes";
            case SettingsGroup.JudgmentLine:
                return simplified ? "判定线" : zh ? "判定線" : "Judgment Line";
            case SettingsGroup.Combo:
                return "Combo";
            case SettingsGroup.Background:
                return simplified ? "背景" : zh ? "背景" : "Background";
            case SettingsGroup.Library:
                return simplified ? "曲目管理" : zh ? "曲目管理" : "Library";
            case SettingsGroup.Session:
                return simplified ? "开始" : zh ? "開始" : "Session";
            default:
                return simplified ? "一般" : zh ? "一般" : "General";
        }
    }

    private void ApplyLocalization()
    {
        // Guard on the highest index this method touches, not an arbitrary one,
        // so adding a setting can never leave it reading past the array.
        if (settings == null || settings.Length <= ApplyArcadeIndex) return;
        bool zh = IsChinese;
        bool simplified = CurrentLanguage == AppLanguage.SimplifiedChinese;
        string[] titles = zh
            ? new[]
            {
                "輸入模式", "打擊聲音量", "音樂音量", "鋼琴音量", "預設下落速度", "開始延遲",
                "判定結果動畫高度", "判定線高度", "譜面角度", "自動演奏", "判定補償",
                "音樂播放補償", "選歌鋼琴預覽", "軌道影片遮罩", "顯示模式", "音符厚度",
                "軌道線透明度", "軌道材質", "擊中光柱高度", "擊中特效位置",
                "鍵盤位置", "語言", "Combo 顯示位置", "Combo 字體大小",
                "Combo 高度"
            }
            : new[]
            {
                "Input Mode", "Hit Sound Volume", "Music Volume", "Piano Volume", "Default Speed",
                "Start Delay", "Judgment Result Height", "Judgment Line Height", "Chart Angle", "Auto Play",
                "Judgment Offset", "Music Playback Offset", "Piano Preview", "Track Video Dimmer",
                "Display Mode", "Note Thickness", "Track Line Opacity", "Track Material", "Hit Light Height",
                "Hit Effect Position", "Keyboard Position", "Language", "Combo Position",
                "Combo Font Size", "Combo Height"
            };
        if (simplified)
        {
            titles = new[]
            {
                "输入模式", "打击声音量", "音乐音量", "钢琴音量", "默认下落速度", "开始延迟",
                "判定结果动画高度", "判定线高度", "谱面角度", "自动演奏", "判定补偿",
                "音乐播放补偿", "选歌钢琴预览", "轨道影片遮罩", "显示模式", "音符厚度",
                "轨道线透明度", "轨道材质", "击中光柱高度", "击中特效位置",
                "键盘位置", "语言", "Combo 显示位置", "Combo 字体大小", "Combo 高度"
            };
        }
        for (int i = 0; i < titles.Length; i++) settings[i].title = titles[i];
        settings[TrackGlassIndex].title = simplified ? "轨道玻璃化"
            : zh ? "軌道玻璃化" : "Track Glass";
        settings[TrackGlassFloorIndex].title = simplified ? "玻璃透背景"
            : zh ? "玻璃透背景" : "Glass Background";
        settings[BackgroundHarmonyIndex].title = simplified ? "背景随调性变色"
            : zh ? "背景隨調性變色" : "Background Harmony";
        settings[MinNoteVelocityIndex].title = simplified ? "最小触键力度"
            : zh ? "最小觸鍵力度" : "Min Key Velocity";
        settings[BrushRejectionIndex].title = simplified ? "邻键误触过滤"
            : zh ? "鄰鍵誤觸過濾" : "Brush Rejection";
        SetOnOffLabels(BrushRejectionIndex, zh);
        settings[InputFrameBatchingIndex].title = simplified ? "输入帧判定"
            : zh ? "輸入幀判定" : "Input Frame";
        settings[InputFrameBatchingIndex].discreteLabels = simplified
            ? new[] { "关", "判定+声音", "仅声音" }
            : zh
                ? new[] { "關", "判定+聲音", "僅聲音" }
                : new[] { "Off", "Full", "Sound" };
        settings[JudgmentTimingIndex].title = simplified ? "判定计时"
            : zh ? "判定計時" : "Judgment Timing";
        settings[JudgmentTimingIndex].discreteLabels = simplified
            ? new[] { "精准", "帧量化" }
            : zh
                ? new[] { "精準", "幀量化" }
                : new[] { "Precise", "Frame" };
        settings[BrushLookaheadIndex].title = simplified ? "擦碰前瞻"
            : zh ? "擦碰前瞻" : "Brush Look-ahead";
        settings[PianoDynamicExpansionIndex].title = simplified ? "动态系数"
            : zh ? "動態係數" : "Dynamic Range";
        settings[AddSongsIndex].title = IsChinese ? "新增曲目" : "Add Songs";
        settings[EditSongsIndex].title = CurrentLanguage == AppLanguage.SimplifiedChinese
            ? "修改曲目"
            : IsChinese ? "修改曲目" : "Edit Songs";
        settings[DeleteSongsIndex].title = CurrentLanguage == AppLanguage.SimplifiedChinese
            ? "删除曲目"
            : IsChinese ? "刪除曲目" : "Delete Songs";

        settings[CreateSongCategoryIndex].title = CurrentLanguage == AppLanguage.SimplifiedChinese
            ? "创建分类"
            : IsChinese ? "建立分類" : "Create Category";

        settings[InputModeIndex].discreteLabels = simplified
            ? new[] { "键盘", "MIDI（原配置）", "MIDI 键盘" }
            : zh
                ? new[] { "鍵盤", "MIDI（原配置）", "MIDI 鍵盤" }
                : new[] { "Keyboard", "MIDI (Legacy)", "MIDI Keyboard" };
        SetOnOffLabels(DebugModeIndex, zh);
        SetOnOffLabels(PianoPreviewIndex, zh);
        settings[VisualLayoutModeIndex].minLabel = zh ? "一般" : "General";
        settings[VisualLayoutModeIndex].maxLabel = simplified ? "88 键" : zh ? "88 鍵" : "88 Keys";
        settings[TrackMarbleThemeIndex].minLabel = simplified ? "黑色大理石" : zh ? "黑色大理石" : "Black Marble";
        settings[TrackMarbleThemeIndex].maxLabel = simplified ? "白色大理石" : zh ? "白色大理石" : "White Marble";
        settings[TimingSensitiveVisualsIndex].minLabel = simplified ? "判定线位置" : zh ? "判定線位置" : "Judgment Line";
        settings[TimingSensitiveVisualsIndex].maxLabel = simplified ? "实际击打位置" : zh ? "實際擊打位置" : "Actual Timing";
        settings[KeyboardScreenBottomIndex].minLabel = simplified ? "轨道旁" : zh ? "軌道旁" : "Beside Track";
        settings[KeyboardScreenBottomIndex].maxLabel = simplified ? "画面底部" : zh ? "畫面底部" : "Screen Bottom";
        settings[LanguageIndex].discreteLabels = new[] { "繁體中文", "简体中文", "English" };
        settings[ComboDisplayIndex].discreteLabels = IsChinese
            ? new[] { "左上", "中上", "Track 上", "中央" }
            : new[] { "Top Left", "Top Center", "On Track", "Center" };
        settings[StartDelayIndex].unit = zh ? " 秒" : " s";
        settings[CameraRotXIndex].unit = zh ? " 度" : " deg";

        // 鋼琴音源相關的三項。它們的索引在上面那份標題陣列（0~24）之外，
        // 所以要在這裡各自處理，否則會一直停在建構時寫死的英文。
        settings[PianoSoundModeIndex].title = simplified ? "钢琴音源"
            : zh ? "鋼琴音源" : "Piano Sound";
        settings[PianoSoundModeIndex].discreteLabels = simplified
            ? new[] { "录音", "演奏模式", "硬核演奏" }
            : zh
                ? new[] { "錄音", "演奏模式", "硬核演奏" }
                : new[] { "Recorded", "Performance", "Hardcore" };

        settings[PianoPedalSourceIndex].title = simplified ? "延音踏板"
            : zh ? "延音踏板" : "Sustain Pedal";
        settings[PianoPedalSourceIndex].minLabel = simplified ? "自动（谱面）"
            : zh ? "自動（譜面）" : "Auto (Chart)";
        settings[PianoPedalSourceIndex].maxLabel = simplified ? "我的踏板"
            : zh ? "我的踏板" : "My Pedal";

        settings[PianoAutoPedalIndex].title = simplified ? "无踏板时代用"
            : zh ? "無踏板時代用" : "Pedal If Missing";
        settings[PianoAutoPedalIndex].discreteLabels = simplified
            ? new[] { "关闭", "每拍换踏", "每小节换踏" }
            : zh
                ? new[] { "關閉", "每拍換踏", "每小節換踏" }
                : new[] { "Off", "Per Beat", "Per Bar" };

        settings[ChordSpreadIndex].title = simplified ? "和弦落键容许"
            : zh ? "和弦落鍵容許" : "Chord Spread";
        settings[ChordSpreadIndex].unit = zh ? " 毫秒" : " ms";

        settings[LateHitPriorityIndex].title = simplified ? "迟按优先"
            : zh ? "遲按優先" : "Late Hit Priority";
        settings[LateHitPriorityIndex].unit = zh ? " 倍" : "x";

        settings[EarlyClaimLimitIndex].title = simplified ? "提前抢音上限"
            : zh ? "提前搶音上限" : "Early Claim Limit";
        settings[EarlyClaimLimitIndex].unit = zh ? " 毫秒" : " ms";

        settings[RetimeBetterPressIndex].title = simplified ? "更准按键补正"
            : zh ? "更準按鍵補正" : "Retime Better Press";
        SetOnOffLabels(RetimeBetterPressIndex, zh);

        settings[PianoPerformanceSpreadIndex].title = simplified ? "弹奏模式时值偏差"
            : zh ? "彈奏模式時值偏差" : "Performance Timing Spread";
        settings[PianoPerformanceSpreadIndex].unit = zh ? " 毫秒" : " ms";

        settings[KeyChatterGuardIndex].title = simplified ? "按键抖动保护"
            : zh ? "按鍵抖動保護" : "Key Chatter Guard";
        SetOnOffLabels(KeyChatterGuardIndex, zh);

        settings[HoldMagicCircleIndex].title = simplified ? "长条魔法阵"
            : zh ? "長條魔法陣" : "Hold Magic Circle";
        SetOnOffLabels(HoldMagicCircleIndex, zh);

        settings[ShowPedalCuesIndex].title = simplified ? "踏板提示"
            : zh ? "踏板提示" : "Pedal Cues";
        SetOnOffLabels(ShowPedalCuesIndex, zh);

        settings[ShowCentreCueIndex].title = simplified ? "中央提示"
            : zh ? "中央提示" : "Centre Cue";
        SetOnOffLabels(ShowCentreCueIndex, zh);

        settings[NormalizeLoudnessIndex].title = simplified ? "音量正规化"
            : zh ? "音量正規化" : "Loudness Match";
        SetOnOffLabels(NormalizeLoudnessIndex, zh);

        settings[TouchAlignmentIndex].title = simplified ? "触键对齐"
            : zh ? "觸鍵對齊" : "Touch Alignment";

        settings[StrikeCueHeightIndex].title = simplified ? "按键提示长度"
            : zh ? "按鍵提示長度" : "Key Cue Length";

        settings[NoteArcHeightIndex].title = simplified ? "抛物线顶点"
            : zh ? "拋物線頂點" : "Arc Height";
        settings[NoteArcLengthIndex].title = simplified ? "弧线全长"
            : zh ? "弧線全長" : "Arc Span";
        settings[NoteArcSpawnIndex].title = simplified ? "生成距离"
            : zh ? "生成距離" : "Spawn Distance";
        settings[NoteArcCurveIndex].title = simplified ? "落下曲线"
            : zh ? "落下曲線" : "Arc Curve";
        settings[NoteArcCurveIndex].discreteLabels = simplified
            ? new[] { "本家", "抛物线" }
            : zh
                ? new[] { "本家", "拋物線" }
                : new[] { "Arcade", "Parabola" };
        settings[NoteFallModeIndex].title = simplified ? "落下方式"
            : zh ? "落下方式" : "Note Fall";
        settings[NoteFallModeIndex].discreteLabels = simplified
            ? new[] { "倾斜", "抛物线" }
            : zh
                ? new[] { "傾斜", "拋物線" }
                : new[] { "Tilt", "Arc" };
        settings[ApplyArcadeIndex].title = simplified ? "一键套用本家"
            : zh ? "一鍵套用本家" : "Apply Arcade Settings";

        settings[JudgmentPresetIndex].title = simplified ? "判定模式"
            : zh ? "判定模式" : "Judgment Preset";
        settings[JudgmentPresetIndex].discreteLabels = simplified
            ? new[] { "即时反馈", "类原型", "自订" }
            : zh
                ? new[] { "即時反饋", "類原型", "自訂" }
                : new[] { "Responsive", "Classic", "Custom" };

        SetButtonCaption(closeButton, simplified ? "关闭" : zh ? "關閉" : "Close");
        SetButtonCaption(settingsButton, simplified ? "设置" : zh ? "設定" : "Settings");
        if (settingsPanel != null)
        {
            TextMeshProUGUI[] localizedText =
                settingsPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < localizedText.Length; i++)
                ClassicalBookUITheme.ApplyLocalizedFont(localizedText[i]);
        }
        RuntimeSongLibraryPanel.RefreshActiveLocalization();
        ConfigurePreviewSong(false);
        UpdateDisplay();
    }

    private void SetOnOffLabels(int index, bool zh)
    {
        bool simplified = CurrentLanguage == AppLanguage.SimplifiedChinese;
        settings[index].minLabel = simplified ? "关" : zh ? "關" : "Off";
        settings[index].maxLabel = simplified ? "开" : zh ? "開" : "On";
    }

    private static void SetButtonCaption(Button button, string caption)
    {
        if (button == null) return;
        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null && !string.IsNullOrWhiteSpace(label.text))
        {
            label.text = caption;
            ClassicalBookUITheme.ApplyLocalizedFont(label);
        }
    }
}

public sealed class PreviewSongScrollRouter : MonoBehaviour, IScrollHandler
{
    public SimpleCarouselSettings Owner { get; set; }

    public void OnScroll(PointerEventData eventData)
    {
        if (Owner == null || eventData == null) return;
        Owner.HandlePreviewSongScroll(eventData.scrollDelta.y);
        eventData.Use();
    }
}
