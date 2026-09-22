using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.UI;
using TMPro;
using UnityEngine.Video;

public class GameBackgroundManager : MonoBehaviour
{
    public RawImage backgroundImage; // 顯示單張背景圖片或黑背景
    public RawImage songImage; // 指定用於放置曲子繪圖的 RawImage 區域 (由外部繪圖工具填入)
    public TMP_Text songTitleText; // 顯示曲名 (TextMeshPro)
    public TMP_Text songAuthorText; // 顯示作者 (TextMeshPro)
    [Tooltip("Optional: displays current DSP time (ms) and chart music_finish_time_msec as current/finish.")]
    [SerializeField] private TMP_Text dspProgressText;
    [Header("Video Backdrop")]
    [Tooltip("Optional backdrop RawImage shown behind the video while waiting to start (cover or black).")]
    public RawImage videoBackdrop;
    [Header("Gameplay HUD")]
    [Tooltip("Optional: Text element in the gameplay HUD to show selected difficulty")]
    [SerializeField] private TMP_Text gameplayDifficultyText;
    public VideoPlayer videoPlayer;  // 用於播放背景影片
    [Header("Video Overlay")]
    [Tooltip("Runtime-created semi-transparent overlay above the video background (created automatically)")]
    private UnityEngine.UI.RawImage videoOverlay;
    [Tooltip("Overlay color (default: semi-transparent black)")]
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.5f);
    [Tooltip("Opacity applied to the video overlay to dim the video (0=transparent, 1=fully black).")]
    [Range(0f, 1f)] [SerializeField] private float videoOverlayOpacity = 0.65f;
    private Color _backgroundImageBaseColor = Color.white;
    private bool _backgroundImageBaseColorCached;
    private bool _backgroundDimApplied;
    private static Texture2D sBlackTexture;
    [Header("Track Dimmer")]
    [SerializeField]
    private float sideMarginPixels = 200f; // 左右預留的黑邊寬度
    [Header("Masking")]
    [SerializeField] private bool maskVideoGraphics = true;
    [SerializeField] private bool maskSongImageGraphic = false;
    [SerializeField] private bool maskSongTextGraphics = false;
    [SerializeField] private bool maskGameplayDifficultyGraphic = false;

    [Header("Song Title Marquee")]
    [SerializeField, Min(1f)] private float songTitleMarqueeSpeed = 58f;
    [SerializeField, Min(0f)] private float songTitleMarqueeStartDelay = 0.8f;

    private RectTransform _songTitleViewport;
    private RectTransform _songTitleRect;
    private Canvas _songTitleFrontCanvas;
    private Vector2 _songTitleOriginalSize;
    private TextAlignmentOptions _songTitleOriginalAlignment;
    private string _songTitleSource = string.Empty;
    private bool _songTitleMarqueeActive;
    private float _songTitleMarqueeOffset;
    private float _songTitleMarqueeLoopWidth;
    private float _songTitleMarqueeElapsed;
    private RectTransform _songAuthorViewport;
    private RectTransform _songAuthorRect;
    private Canvas _songAuthorFrontCanvas;
    private Vector2 _songAuthorOriginalSize;
    private TextAlignmentOptions _songAuthorOriginalAlignment;
    private string _songAuthorSource = string.Empty;
    private bool _songAuthorMarqueeActive;
    private float _songAuthorMarqueeOffset;
    private float _songAuthorMarqueeLoopWidth;
    private float _songAuthorMarqueeElapsed;
    private RectTransform _difficultyViewport;
    private RectTransform _difficultyRect;
    private Canvas _difficultyFrontCanvas;
    private DifficultyFrameGraphic _difficultyFrame;
    private int _difficultyThemeLevel = 1;
    private RectTransform _classicalSongInfoPanel;
    private RectTransform _coverFrame;
    private Canvas _classicalSongInfoOverlay;
    private RectTransform _songProgressKnob;
    private TextMeshProUGUI _songProgressTime;
    private static Sprite _songProgressKnobSprite;
    // These HUD elements are detached to the root canvas for foreground sorting.
    // They therefore need an explicit presentation state when gameplayRoot is hidden.
    private bool _gameplayHudPresentationVisible = true;

    private string coverResourcePath;
    private string videoPath;
    // Remember last assigned resource to avoid redundant re-assigns
    private string _lastAssignedResourcePath;
    private Texture2D _lastAssignedTexture;
    // Cache original RectTransform values for songImage to avoid accidental stretch/anchor changes
    private Vector2 _songAnchorMin, _songAnchorMax, _songPivot;
    private Vector2 _songOffsetMin, _songOffsetMax, _songAnchoredPosition, _songSizeDelta;
    private bool _songRectCached = false;
    private RectTransform _songRectTransform;
    private UnityEngine.UI.LayoutElement _songLayoutElement;
    private Transform _originalSongImageParent;
    private int _originalSongImageSiblingIndex;
    private RectTransform _runtimeSongImageParent;
    private Canvas _songImageFrontCanvas;
    [Header("Song Image Rect Guard")]
    [SerializeField] private bool lockSongImageRectDuringPlay = true;
    [SerializeField] private bool logSongImageRectChanges = false;
    [SerializeField] private bool detachSongImageFromLayouts = true;
    // Serialized copy of the editor-time RectTransform values (populated in editor via OnValidate or context menu)
    [SerializeField] private Vector2 serializedSongAnchorMin;
    [SerializeField] private Vector2 serializedSongAnchorMax;
    [SerializeField] private Vector2 serializedSongPivot;
    [SerializeField] private Vector2 serializedSongOffsetMin;
    [SerializeField] private Vector2 serializedSongOffsetMax;
    [SerializeField] private Vector2 serializedSongAnchoredPosition;
    [SerializeField] private Vector2 serializedSongSizeDelta;
    [SerializeField] private bool serializedSongRectAvailable = false;

    // Track if the difficulty text was explicitly set by the player
    private bool _playerSetDifficulty = false;
    // DSP-scheduled video start time (AudioSettings.dspTime). If >0, video playback will wait until this DSP time.
    private double scheduledDspStart = -1.0;
    // Optional per-song requested video start delay in seconds (video waits this long after dspStart before starting from t=0)
    private double scheduledVideoStartOffsetSec = 0.0;
    // Keep track of any active coroutine waiting for a DSP start time
    private Coroutine videoPlaybackRoutine;
    // Flag when strict-delay playback should wait for SchedulePlaybackAtDsp before starting
    private bool awaitingStrictSchedule = false;
    // Cached per-song requested video start offset (seconds) from SongSelectionManager metadata
    private double perSongVideoStartOffsetSec = 0.0;

    /// <summary>When Prepare() was asked for, so a slow decode can be named as such.</summary>
    private float _prepareStartedRealtime = -1f;
    private bool _prepareReported = false;

    // Track dimmer support when a background video is active
    private bool _videoRequestedForTrackDimmer = false;
    private float _currentTrackDimmerApplied = 0f;
    private Transform _cachedTrackRoot;
    private readonly List<TrackRendererEntry> _trackRendererEntries = new List<TrackRendererEntry>();
    private Coroutine _trackDimmerRetryRoutine;
    private Coroutine _trackDimmerVerifyRoutine;
    private bool _loggedMissingTrackRoot = false;
    private static readonly int ShaderColorId = Shader.PropertyToID("_Color");
    private static readonly int ShaderBaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ShaderTintColorId = Shader.PropertyToID("_TintColor");

    // cache for DSP text to avoid redundant TMP updates
    private string _lastDspProgressText = null;
    private long _lastDspProgressSecond = -1;

    void Update()
    {
        UpdateDspProgress();
        UpdateSongTitleMarquee();
        UpdateSongAuthorMarquee();
        WarnIfPreparationStalled();
    }

    /// <summary>
    /// Says so, once, when a video is still decoding long after it was asked for.
    /// </summary>
    /// <remarks>
    /// A large file simply takes time, and until it lands the background is
    /// whatever was there before — indistinguishable from a video that failed,
    /// a path that was wrong, or a player that was never wired up. Naming it
    /// separates "slow" from "broken" without any further investigation.
    /// </remarks>
    private void WarnIfPreparationStalled()
    {
        if (_prepareReported || _prepareStartedRealtime < 0f || videoPlayer == null) return;
        if (Time.realtimeSinceStartup - _prepareStartedRealtime < 10f) return;
        _prepareReported = true;
        Debug.LogWarning($"[Background] Video still not prepared after 10s: " +
            $"isPrepared={videoPlayer.isPrepared} url={videoPlayer.url}. " +
            "A large or awkwardly encoded file decodes slowly; the picture will " +
            "appear whenever preparation finishes.");
    }

    void Start()
    {
        // 假設選擇的歌曲資訊已經存儲在一個全局管理器中
        // 嘗試從 SongSelectionManager 安全地取得目前被選取的歌曲（如果存在）
        var selector = FindAnyObjectByType<SongSelectionManager>();
        if (selector != null)
        {
            var selectedSong = selector.GetSelectedSong();
            if (selectedSong != null)
            {
                coverResourcePath = selectedSong.coverResourcePath;
                videoPath = selectedSong.videoPath;
                perSongVideoStartOffsetSec = selectedSong.videoStartTimeSec;
                LoadBackground(coverResourcePath, videoPath);
                // 嘗試顯示曲名與作者（如果資料結構包含）
                try
                {
                    string title = null;
                    string author = null;
                    // 以動態方式嘗試讀取常見欄位名稱
                    var songType = selectedSong.GetType();
                    var titleProp = songType.GetProperty("title") ?? songType.GetProperty("name");
                    var authorProp = songType.GetProperty("author") ?? songType.GetProperty("artist");
                    if (titleProp != null) title = titleProp.GetValue(selectedSong)?.ToString();
                    if (authorProp != null) author = authorProp.GetValue(selectedSong)?.ToString();
                    SetSongInfo(title, author);
                    // 如果 coverResourcePath 或 title/author 尚未填入，嘗試從 displayName 或 UI 的 songTitleText 裡解析
                    bool needCover = string.IsNullOrEmpty(coverResourcePath);
                    bool needTitle = string.IsNullOrEmpty(title);
                    bool needAuthor = string.IsNullOrEmpty(author);
                    if (needCover || needTitle || needAuthor)
                    {
                        string fallbackSource = null;
                        var displayNameProp = songType.GetProperty("displayName");
                        if (displayNameProp != null)
                        {
                            fallbackSource = displayNameProp.GetValue(selectedSong)?.ToString();
                        }
                        if (string.IsNullOrWhiteSpace(fallbackSource) && songTitleText != null)
                        {
                            fallbackSource = songTitleText.text;
                        }

                        if (!string.IsNullOrWhiteSpace(fallbackSource))
                        {
                            string parsedPath, parsedTitle, parsedAuthor;
                            if (TryParseSongTitleString(fallbackSource, out parsedPath, out parsedTitle, out parsedAuthor))
                            {
                                BuildLogger.Log($"GameBackgroundManager: Parsed fallback song info -> path='{parsedPath}' title='{parsedTitle}' author='{parsedAuthor}'");
                                if (needCover && !string.IsNullOrWhiteSpace(parsedPath))
                                {
                                    // 如果解析出來的路徑看起來合理，採用它並載入
                                    coverResourcePath = parsedPath;
                                    LoadBackground(coverResourcePath, videoPath);
                                }
                                if (needTitle && !string.IsNullOrWhiteSpace(parsedTitle)) title = parsedTitle;
                                if (needAuthor && !string.IsNullOrWhiteSpace(parsedAuthor)) author = parsedAuthor;
                                SetSongInfo(title, author);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // If no selected song, start a short coroutine to retry because SongSelectionManager
        // might initialize after this object in some execution orders.
        StartCoroutine(EnsureSelectionThenRefresh());

        // Note: we no longer cache from runtime state here. Instead we prefer the editor-captured
        // serialized rect values (populated via OnValidate or the context menu). If those aren't
        // available at runtime, fall back to caching current RectTransform in Awake.
    }

    private void Awake()
    {
        EnsureBlackTexture();
        if (backgroundImage != null)
        {
            _backgroundImageBaseColor = backgroundImage.color;
            _backgroundImageBaseColorCached = true;
        }
        // Restore cached editor rects into runtime cache before other Start() calls run
        _songRectTransform = songImage != null ? songImage.GetComponent<RectTransform>() : null;
        if (songImage != null && detachSongImageFromLayouts)
        {
            DetachSongImageFromLayouts();
        }
        EnsureSongImageLayoutIgnored();
        EnsureVideoBackdrop();
        EnsureVideoOverlay();
        EnsureSongTitlePresentation();
        EnsureSongAuthorPresentation();
        EnsureDifficultyPresentation();

        if (serializedSongRectAvailable && songImage != null)
        {
            _songAnchorMin = serializedSongAnchorMin;
            _songAnchorMax = serializedSongAnchorMax;
            _songPivot = serializedSongPivot;
            _songOffsetMin = serializedSongOffsetMin;
            _songOffsetMax = serializedSongOffsetMax;
            _songAnchoredPosition = serializedSongAnchoredPosition;
            _songSizeDelta = serializedSongSizeDelta;
            _songRectCached = true;
            BuildLogger.Log("GameBackgroundManager: Loaded serialized songImage rect for runtime restore.");
        }
        else
        {
            // If no serialized data was captured in the editor, capture current values now as a fallback
            CacheSongImageRect();
            BuildLogger.Log("GameBackgroundManager: No serialized song rect found; cached current Rect as fallback.");
        }
        // Start a robust restore task that will re-apply the cached/editor rect several times
        // across frames to beat other layout scripts that may run in Start/Awake.
        // Ensure the cached rect is applied once up-front
        RestoreSongImageRect();
        RebuildClassicalSongInfoPanel();

        ApplyMaskPreferences();
    }

    private void RebuildClassicalSongInfoPanel()
    {
        if (_classicalSongInfoPanel != null || songImage == null) return;
        Canvas sourceCanvas = backgroundImage != null ? backgroundImage.canvas : songImage.canvas;
        Canvas rootCanvas = sourceCanvas != null ? sourceCanvas.rootCanvas : null;
        if (rootCanvas == null) return;

        GameObject overlayObject = new GameObject("ClassicalSongInfoOverlay", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        overlayObject.layer = 5;
        overlayObject.transform.SetParent(transform, false);
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayRect.localScale = Vector3.one;
        _classicalSongInfoOverlay = overlayObject.GetComponent<Canvas>();
        _classicalSongInfoOverlay.renderMode = RenderMode.ScreenSpaceOverlay;
        _classicalSongInfoOverlay.overrideSorting = true;
        _classicalSongInfoOverlay.sortingOrder = 23980;
        CanvasScaler overlayScaler = overlayObject.GetComponent<CanvasScaler>();
        overlayScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        overlayScaler.referenceResolution = new Vector2(2560f, 1440f);
        overlayScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        overlayScaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new GameObject("ClassicalSongInfoPanel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        panelObject.layer = 5;
        _classicalSongInfoPanel = panelObject.GetComponent<RectTransform>();
        _classicalSongInfoPanel.SetParent(overlayObject.transform, false);
        _classicalSongInfoPanel.anchorMin = new Vector2(0f, 1f);
        _classicalSongInfoPanel.anchorMax = new Vector2(0f, 1f);
        _classicalSongInfoPanel.pivot = new Vector2(0f, 1f);
        // Keep the complete card inside the screen safe area. The former 650 px
        // card intruded into the track; this is exactly three quarters as wide.
        _classicalSongInfoPanel.anchoredPosition = new Vector2(40f, -28f);
        // BPM reaches 228 px from the top; keep only a slim 14 px paper margin.
        _classicalSongInfoPanel.sizeDelta = new Vector2(487.5f, 242f);

        Image panel = panelObject.GetComponent<Image>();
        panel.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        panel.type = Image.Type.Tiled;
        panel.pixelsPerUnitMultiplier = 0.72f;
        panel.color = new Color(0.94f, 0.90f, 0.82f, 0.98f);
        panel.raycastTarget = false;
        Outline border = panelObject.AddComponent<Outline>();
        border.effectColor = new Color(0.42f, 0.27f, 0.13f, 0.82f);
        border.effectDistance = new Vector2(2f, -2f);
        Shadow shadow = panelObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
        shadow.effectDistance = new Vector2(8f, -8f);

        RectMask2D panelMask = panelObject.GetComponent<RectMask2D>();
        panelMask.padding = Vector4.zero;
        panelMask.softness = new Vector2Int(2, 2);

        // 裝飾框是紙卡的**兄弟**而不是子物件：紙卡帶著 RectMask2D，掛進去的東西
        // 會被裁在紙邊上，而那正好就是紋飾要待的地方。
        _difficultyFrame = DifficultyFrameGraphic.Attach(_classicalSongInfoPanel, 11f);
        ApplyDifficultyTheme(gameplayDifficultyText != null ? gameplayDifficultyText.text : null);

        // The gameplay thumbnail gets the same framed treatment as the results
        // book: a mount, a gilt edge and a drop shadow, with the frame itself
        // taking the artwork's proportions instead of padding it out to a square.
        GameObject frameObject = new GameObject("CoverFrame", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        frameObject.layer = 5;
        _coverFrame = frameObject.GetComponent<RectTransform>();
        _coverFrame.SetParent(_classicalSongInfoPanel, false);
        SetPanelRect(_coverFrame, new Vector2(24f, -22f), new Vector2(CoverFrameLongEdge, CoverFrameLongEdge));

        Image frameFill = frameObject.GetComponent<Image>();
        frameFill.color = new Color(0.985f, 0.972f, 0.90f, 1f);
        frameFill.raycastTarget = false;
        Outline frameEdge = frameObject.AddComponent<Outline>();
        frameEdge.effectColor = new Color(0.76f, 0.57f, 0.25f, 0.9f);
        frameEdge.effectDistance = new Vector2(2f, -2f);
        Shadow frameShadow = frameObject.AddComponent<Shadow>();
        frameShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        frameShadow.effectDistance = new Vector2(5f, -5f);

        RectTransform cover = songImage.rectTransform;
        cover.SetParent(_coverFrame, false);
        cover.anchorMin = Vector2.zero;
        cover.anchorMax = Vector2.one;
        cover.pivot = new Vector2(0.5f, 0.5f);
        cover.offsetMin = new Vector2(CoverMount, CoverMount);
        cover.offsetMax = new Vector2(-CoverMount, -CoverMount);
        cover.localScale = Vector3.one;
        cover.localRotation = Quaternion.identity;
        songImage.transform.SetAsLastSibling();
        ApplyCoverAspect(songImage.texture);

        if (_songTitleViewport != null)
        {
            _songTitleViewport.SetParent(_classicalSongInfoPanel, false);
            SetPanelRect(_songTitleViewport, new Vector2(220f, -22f), new Vector2(243f, 48f));
            _songTitleOriginalSize = new Vector2(243f, 48f);
            if (_songTitleFrontCanvas != null)
            {
                Destroy(_songTitleFrontCanvas);
                _songTitleFrontCanvas = null;
            }
        }
        if (_songAuthorViewport != null)
        {
            _songAuthorViewport.SetParent(_classicalSongInfoPanel, false);
            SetPanelRect(_songAuthorViewport, new Vector2(220f, -79f), new Vector2(243f, 32f));
            _songAuthorOriginalSize = new Vector2(243f, 32f);
            if (_songAuthorFrontCanvas != null)
            {
                Destroy(_songAuthorFrontCanvas);
                _songAuthorFrontCanvas = null;
            }
        }
        if (_difficultyViewport != null)
        {
            _difficultyViewport.SetParent(_classicalSongInfoPanel, false);
            SetPanelRect(_difficultyViewport, new Vector2(220f, -121f), new Vector2(243f, 32f));
            if (_difficultyFrontCanvas != null)
            {
                Destroy(_difficultyFrontCanvas);
                _difficultyFrontCanvas = null;
            }
        }

        EnsureSongProgressBar();

        BpmDisplay bpmDisplay = FindFirstObjectByType<BpmDisplay>();
        if (bpmDisplay != null && bpmDisplay.bpmText != null)
        {
            RectTransform bpmRect = bpmDisplay.bpmText.rectTransform;
            bpmRect.SetParent(overlayObject.transform, false);
            bpmRect.anchorMin = new Vector2(0f, 0.5f);
            bpmRect.anchorMax = new Vector2(0f, 0.5f);
            bpmRect.pivot = new Vector2(0f, 0.5f);
            bpmRect.anchoredPosition = new Vector2(48f, 0f);
            bpmRect.sizeDelta = new Vector2(430f, 180f);
            bpmRect.localScale = Vector3.one;
            bpmRect.localRotation = Quaternion.identity;
            Canvas bpmCanvas = bpmDisplay.bpmText.GetComponent<Canvas>();
            if (bpmCanvas != null) Destroy(bpmCanvas);
            bpmDisplay.ConfigureFloatingPresentation();
            bpmRect.SetAsLastSibling();
        }

        // From this point the cover's canonical rect is panel-local; update the
        // guard cache so legacy authored coordinates cannot pull it back out.
        CacheSongImageRect();
        overlayObject.SetActive(_gameplayHudPresentationVisible);
    }

    private void EnsureSongProgressBar()
    {
        if (_classicalSongInfoPanel == null || _songProgressKnob != null) return;

        GameObject barObject = new GameObject("SongProgressBar", typeof(RectTransform));
        barObject.layer = 5;
        RectTransform barRect = barObject.GetComponent<RectTransform>();
        barRect.SetParent(_classicalSongInfoPanel, false);
        SetPanelRect(barRect, new Vector2(216f, -168f), new Vector2(247f, 48f));

        GameObject timeObject = new GameObject("SongProgressTime", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        timeObject.layer = 5;
        RectTransform timeRect = timeObject.GetComponent<RectTransform>();
        timeRect.SetParent(barRect, false);
        timeRect.anchorMin = new Vector2(0f, 0.48f);
        timeRect.anchorMax = new Vector2(1f, 1f);
        timeRect.offsetMin = timeRect.offsetMax = Vector2.zero;
        _songProgressTime = timeObject.GetComponent<TextMeshProUGUI>();
        _songProgressTime.text = "00:00 / 00:00";
        _songProgressTime.alignment = TextAlignmentOptions.Center;
        _songProgressTime.fontSize = 17f;
        _songProgressTime.fontStyle = FontStyles.Bold;
        _songProgressTime.color = new Color(0.20f, 0.12f, 0.075f, 1f);
        _songProgressTime.raycastTarget = false;

        GameObject lineObject = new GameObject("ProgressLine", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        lineObject.layer = 5;
        RectTransform lineRect = lineObject.GetComponent<RectTransform>();
        lineRect.SetParent(barRect, false);
        lineRect.anchorMin = new Vector2(0.04f, 0.20f);
        lineRect.anchorMax = new Vector2(0.96f, 0.20f);
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineRect.sizeDelta = new Vector2(0f, 3f);
        lineObject.GetComponent<Image>().color = new Color(0.82f, 0.65f, 0.31f, 0.95f);
        lineObject.GetComponent<Image>().raycastTarget = false;

        GameObject knobObject = new GameObject("ProgressKnob", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        knobObject.layer = 5;
        _songProgressKnob = knobObject.GetComponent<RectTransform>();
        _songProgressKnob.SetParent(barRect, false);
        _songProgressKnob.anchorMin = _songProgressKnob.anchorMax = new Vector2(0.04f, 0.20f);
        _songProgressKnob.pivot = new Vector2(0.5f, 0.5f);
        _songProgressKnob.sizeDelta = new Vector2(15f, 15f);
        _songProgressKnob.anchoredPosition = Vector2.zero;
        Image knobImage = knobObject.GetComponent<Image>();
        knobImage.sprite = GetSongProgressKnobSprite();
        knobImage.color = new Color(0.33f, 0.72f, 1f, 1f);
        knobImage.raycastTarget = false;
    }

    private static Sprite GetSongProgressKnobSprite()
    {
        if (_songProgressKnobSprite != null) return _songProgressKnobSprite;
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "SongProgressKnobTexture",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear
        };
        Color[] pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(radius + 0.8f - distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        _songProgressKnobSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f);
        _songProgressKnobSprite.name = "SongProgressKnobSprite";
        return _songProgressKnobSprite;
    }

    /// <summary>Longest edge of the framed thumbnail, mount included.</summary>
    private const float CoverFrameLongEdge = 170f;

    /// <summary>Paper mount between the gilt edge and the artwork.</summary>
    private const float CoverMount = 5f;

    /// <summary>
    /// Reshapes the thumbnail frame to the artwork's own proportions.
    /// </summary>
    /// <remarks>
    /// Stretching a non-square cover into a square rect distorts it; padding it
    /// leaves bars that read as damage rather than design. The frame follows the
    /// picture instead — the longer edge keeps the size the square frame had, so
    /// the card's layout never has to move, and the shorter one gives way.
    /// </remarks>
    private void ApplyCoverAspect(Texture texture)
    {
        if (_coverFrame == null) return;
        float width = texture != null ? texture.width : 0f;
        float height = texture != null ? texture.height : 0f;
        float aspect = (width > 0f && height > 0f) ? width / height : 1f;

        float inner = CoverFrameLongEdge - CoverMount * 2f;
        Vector2 art = aspect >= 1f
            ? new Vector2(inner, inner / aspect)
            : new Vector2(inner * aspect, inner);
        _coverFrame.sizeDelta = art + new Vector2(CoverMount * 2f, CoverMount * 2f);
    }

    private static void SetPanelRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void UpdateDspProgress()
    {
        double elapsedMs = 0.0;
        double finishMs = 0.0;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            try
            {
                if (gm.Conductor != null)
                {
                    AudioSource source = gm.Conductor.audioSync != null
                        ? gm.Conductor.audioSync.audioSource
                        : gm.Conductor.GetComponent<AudioSource>();
                    if (source != null && source.clip != null)
                    {
                        // clip.length 和 source.time 都是**檔案裡的秒數**，和牆上時間
                        // 差一個 pitch。練習模式把音源放慢、譜面拉長，實際要播的時間
                        // 是檔案長度除以速率 —— 不除的話進度條走得比標示慢，而總長
                        // 永遠停在原曲的長度上。譜面自己的 audioSpeedEvents 也一樣。
                        double rate = Mathf.Abs(source.pitch) > 0.001f ? source.pitch : 1.0;
                        finishMs = source.clip.length * 1000.0 / rate;
                        elapsedMs = source.time * 1000.0 / rate;
                    }
                    else
                    {
                        elapsedMs = gm.Conductor.songPosition;
                    }
                }
                if (finishMs <= 0.0 && gm.CurrentChart != null &&
                    gm.CurrentChart.music_finish_time_msec > 0)
                {
                    finishMs = gm.CurrentChart.music_finish_time_msec;
                }
                else if (finishMs <= 0.0 && gm.CurrentChartHeader != null &&
                    gm.CurrentChartHeader.music_finish_time_msec > 0)
                {
                    finishMs = gm.CurrentChartHeader.music_finish_time_msec;
                }
            }
            catch { }
        }

        float progress = finishMs > 0.0
            ? Mathf.Clamp01((float)(elapsedMs / finishMs))
            : 0f;
        if (_songProgressKnob != null)
        {
            float anchorX = Mathf.Lerp(0.04f, 0.96f, progress);
            _songProgressKnob.anchorMin = _songProgressKnob.anchorMax =
                new Vector2(anchorX, 0.20f);
            _songProgressKnob.anchoredPosition = Vector2.zero;
        }

        string timeDisplay = $"{FormatSongTime(elapsedMs)} / {FormatSongTime(finishMs)}";
        if (_songProgressTime != null) _songProgressTime.text = timeDisplay;
    }

    private static string FormatSongTime(double milliseconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt((float)(milliseconds / 1000.0)));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private static void EnsureBlackTexture()
    {
        if (sBlackTexture != null) return;
        sBlackTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "GameBackgroundManager_Black",
            hideFlags = HideFlags.HideAndDontSave
        };
        sBlackTexture.SetPixel(0, 0, Color.black);
        sBlackTexture.Apply(false, true);
    }

    private void EnsureVideoOverlay()
    {
        if (videoOverlay != null || backgroundImage == null) return;
        try
        {
            var go = new GameObject("VideoOverlay_Runtime", typeof(UnityEngine.UI.RawImage));
            var ri = go.GetComponent<UnityEngine.UI.RawImage>();
            ri.color = overlayColor;
            ri.raycastTarget = false;
            var parent = backgroundImage.transform.parent != null ? backgroundImage.transform.parent : backgroundImage.transform;
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            try
            {
                int bgIndex = backgroundImage.transform.GetSiblingIndex();
                int targetIndex = Mathf.Min(parent.childCount - 1, bgIndex + 1);
                go.transform.SetSiblingIndex(targetIndex);
            }
            catch { go.transform.SetAsLastSibling(); }
            ri.gameObject.SetActive(false);
            videoOverlay = ri;
            BuildLogger.Log("GameBackgroundManager: Created runtime video overlay RawImage.");
            try
            {
                if (backgroundImage != null)
                {
                    var canvasTransform = backgroundImage.canvas != null ? backgroundImage.canvas.transform : backgroundImage.transform.parent;
                    if (canvasTransform != null)
                    {
                        backgroundImage.transform.SetParent(canvasTransform, false);
                        backgroundImage.transform.SetAsFirstSibling();
                        int bgIndex = backgroundImage.transform.GetSiblingIndex();
                        int overlayIndex = Mathf.Min(canvasTransform.childCount - 1, bgIndex + 1);
                        videoOverlay.transform.SetSiblingIndex(overlayIndex);
                        // Keep backdrop under the background image on the same canvas to avoid covering video
                        try
                        {
                            if (videoBackdrop != null)
                            {
                                videoBackdrop.transform.SetParent(canvasTransform, false);
                                int backdropIndex = Mathf.Max(0, backgroundImage.transform.GetSiblingIndex());
                                videoBackdrop.transform.SetSiblingIndex(backdropIndex);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: Failed to reorder background/overlay siblings: {ex.Message}");
            }
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: Failed to create runtime video overlay: {ex.Message}");
        }
    }

    private void EnsureVideoBackdrop()
    {
        if (videoBackdrop != null || backgroundImage == null) return;
        try
        {
            var go = new GameObject("VideoBackdrop_Runtime", typeof(UnityEngine.UI.RawImage));
            var ri = go.GetComponent<UnityEngine.UI.RawImage>();
            ri.raycastTarget = false;
            ri.texture = sBlackTexture;
            ri.color = Color.black;
            var parent = backgroundImage.transform.parent != null ? backgroundImage.transform.parent : backgroundImage.transform;
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            // Place just before the backgroundImage so video sits above it
            try
            {
                int bgIndex = backgroundImage.transform.GetSiblingIndex();
                int targetIndex = Mathf.Max(0, bgIndex);
                go.transform.SetSiblingIndex(targetIndex);
            }
            catch { go.transform.SetAsFirstSibling(); }
            videoBackdrop = ri;
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: Failed to create runtime video backdrop: {ex.Message}");
        }
    }


    private void ConfigureMaskableGraphic(Component component, bool maskable)
    {
        if (component == null) return;
        if (component is MaskableGraphic maskableGraphic)
        {
            maskableGraphic.maskable = maskable;
            maskableGraphic.SetMaterialDirty();
            maskableGraphic.SetVerticesDirty();
        }
    }

    private void ApplyMaskPreferences()
    {
        bool unifiedPanel = _classicalSongInfoPanel != null;
        ConfigureMaskableGraphic(backgroundImage, maskVideoGraphics);
        ConfigureMaskableGraphic(videoOverlay, maskVideoGraphics);
        // Every graphic inside the rebuilt card must participate in its RectMask2D.
        // Inspector flags belong to the legacy independent HUD and must not disable
        // clipping after the unified panel has already been constructed.
        ConfigureMaskableGraphic(songImage, unifiedPanel || maskSongImageGraphic);
        ConfigureMaskableGraphic(songTitleText, unifiedPanel || maskSongTextGraphics);
        ConfigureMaskableGraphic(songAuthorText, unifiedPanel || maskSongTextGraphics);
        ConfigureMaskableGraphic(gameplayDifficultyText, unifiedPanel || maskGameplayDifficultyGraphic);
    }

    private void LateUpdate()
    {
        if (!lockSongImageRectDuringPlay || !_songRectCached || _songRectTransform == null) return;
        if (!SongRectMatchesCache(_songRectTransform, out var diffSummary))
        {
            if (logSongImageRectChanges)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: Detected external change to songImage RectTransform -> {diffSummary}. Restoring cached values.");
            }
            RestoreSongImageRect();
        }
    }

    private void OnEnable()
    {
        Canvas.willRenderCanvases += GuardSongImageRect;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= GuardSongImageRect;
    }

    // Retry a few frames until SongSelectionManager provides a selection, then refresh
    private System.Collections.IEnumerator EnsureSelectionThenRefresh()
    {
        int attempts = 0;
        while (attempts < 12)
        {
            var selector = SongSelectionManager.Instance ?? FindAnyObjectByType<SongSelectionManager>();
            if (selector != null)
            {
                var sel = selector.GetSelectedSong();
                if (sel != null)
                {
                    perSongVideoStartOffsetSec = sel.videoStartTimeSec;
                    RefreshFromSelected(selector);
                    // subscribe to live selection changes
                    try
                    {
                        selector.OnSelectionChanged -= OnSongSelectionChanged;
                        selector.OnSelectionChanged += OnSongSelectionChanged;
                    }
                    catch (System.Exception) { }
                    yield break;
                }
            }
            attempts++;
            yield return null; // wait a frame
        }
        // final attempt even if selector missing
        var fallbackSelector = SongSelectionManager.Instance ?? FindAnyObjectByType<SongSelectionManager>();
        if (fallbackSelector != null)
        {
            try { perSongVideoStartOffsetSec = fallbackSelector.GetSelectedSong()?.videoStartTimeSec ?? 0.0; }
            catch { perSongVideoStartOffsetSec = 0.0; }
            RefreshFromSelected(fallbackSelector);
        }

        yield break;
    }

    private void OnSongSelectionChanged(SongSelectionManager.SongOption option)
    {
        if (option == null) return;

        try
        {
            // If this manager is inactive or disabled, ignore selection events to avoid
            // performing UI changes or starting coroutines on an inactive GameObject.
            if (!isActiveAndEnabled)
            {
                BuildLogger.Log("GameBackgroundManager: OnSongSelectionChanged received while inactive — ignoring.");
                try
                {
                    if (backgroundImage != null)
                    {
                        backgroundImage.texture = sBlackTexture;
                        backgroundImage.color = Color.black;
                        backgroundImage.gameObject.SetActive(true);
                    }
                    if (videoBackdrop != null)
                    {
                        videoBackdrop.texture = sBlackTexture;
                        videoBackdrop.color = Color.black;
                        videoBackdrop.gameObject.SetActive(true);
                    }
                    if (songImage != null)
                    {
                        // Keep songImage visible only if it has texture
                        songImage.gameObject.SetActive(songImage.texture != null);
                        // The frame is a sibling-level object, so it has to be
                        // hidden with the artwork or an empty mount is left behind.
                        if (_coverFrame != null)
                            _coverFrame.gameObject.SetActive(songImage.texture != null);
                    }
                }
                catch { }

                return;
            }

            if (option.difficultyVariants != null && option.difficultyVariants.Count > 1)
            {
                // multiple variants — wait for player's explicit choice before showing difficulty
                SetSongDifficulty(string.Empty);
            }
            else
            {
                SetSongDifficultyFromOption(option);
            }
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: OnSongSelectionChanged handler error: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        try
        {
            var s = SongSelectionManager.Instance;
            if (s != null) s.OnSelectionChanged -= OnSongSelectionChanged;
        }
        catch (System.Exception) { }

        if (detachSongImageFromLayouts && songImage != null && _originalSongImageParent != null)
        {
            songImage.transform.SetParent(_originalSongImageParent, false);
            songImage.transform.SetSiblingIndex(_originalSongImageSiblingIndex);
        }

        RestoreTrackDimmerImmediate();
        _videoRequestedForTrackDimmer = false;
    }

    // Public helper to refresh background/metadata from SongSelectionManager
    public void RefreshFromSelected(SongSelectionManager selector = null)
    {
        var sel = selector ?? (SongSelectionManager.Instance ?? FindAnyObjectByType<SongSelectionManager>());
        if (sel == null)
        {
            BuildLogger.Log("GameBackgroundManager: No SongSelectionManager available to refresh from.");
            return;
        }
        var selectedSong = sel.GetSelectedSong();
        if (selectedSong == null)
        {
            BuildLogger.Log("GameBackgroundManager: SongSelectionManager returned null selected song.");
            return;
        }

        coverResourcePath = selectedSong.coverResourcePath;
        videoPath = selectedSong.videoPath;
        perSongVideoStartOffsetSec = selectedSong.videoStartTimeSec;
        BuildLogger.Log($"GameBackgroundManager: RefreshFromSelected coverPath='{coverResourcePath}' videoPath='{videoPath}' displayName='{selectedSong.displayName}' author='{selectedSong.author}'");
        LoadBackground(coverResourcePath, videoPath);

        // try title/author
        string title = selectedSong.displayName;
        string author = selectedSong.author;
        if (string.IsNullOrWhiteSpace(title) && songTitleText != null)
        {
            title = songTitleText.text;
        }
        // parse fallback if needed
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(coverResourcePath))
        {
            string fallback = selectedSong.displayName;
            if (string.IsNullOrWhiteSpace(fallback) && songTitleText != null) fallback = songTitleText.text;
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                string parsedPath, parsedTitle, parsedAuthor;
                if (TryParseSongTitleString(fallback, out parsedPath, out parsedTitle, out parsedAuthor))
                {
                    BuildLogger.Log($"GameBackgroundManager: RefreshFromSelected parsed fallback -> path='{parsedPath}' title='{parsedTitle}' author='{parsedAuthor}'");
                    if (string.IsNullOrWhiteSpace(coverResourcePath) && !string.IsNullOrWhiteSpace(parsedPath))
                    {
                        coverResourcePath = parsedPath;
                        LoadBackground(coverResourcePath, videoPath);
                    }
                    if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(parsedTitle)) title = parsedTitle;
                    if (string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(parsedAuthor)) author = parsedAuthor;
                }
            }
        }

        SetSongInfo(title, author);
        // Only update difficulty display automatically when there is a single difficulty.
        try
        {
            if (selectedSong.difficultyVariants != null && selectedSong.difficultyVariants.Count > 1)
            {
                // Only clear auto-difficulty if the player hasn't already chosen a difficulty
                if (!_playerSetDifficulty) SetSongDifficulty(string.Empty);
            }
            else
            {
                SetSongDifficultyFromOption(selectedSong);
            }
        }
        catch { }
    }

    // Public API: set difficulty text directly
    public void SetSongDifficulty(string difficultyText)
    {
        if (gameplayDifficultyText == null) return;
        try
        {
            gameplayDifficultyText.text = string.IsNullOrEmpty(difficultyText) ? string.Empty : difficultyText;
            gameplayDifficultyText.gameObject.SetActive(!string.IsNullOrEmpty(gameplayDifficultyText.text));
            if (_difficultyViewport != null)
            {
                _difficultyViewport.gameObject.SetActive(
                    _gameplayHudPresentationVisible && !string.IsNullOrEmpty(gameplayDifficultyText.text));
            }
            ApplyDifficultyTheme(gameplayDifficultyText.text);
            BuildLogger.Log($"GameBackgroundManager: SetSongDifficulty -> '{gameplayDifficultyText.text}'");
            // If we clear the difficulty text, clear player-set flag as well
            if (string.IsNullOrEmpty(difficultyText)) _playerSetDifficulty = false;
        }
        catch { }
    }

    /// <summary>
    /// Colours the difficulty caption and the card's frame for the chart shown.
    /// </summary>
    /// <remarks>
    /// The level is read back out of the caption rather than plumbed through:
    /// <see cref="SetSongDifficulty"/> is the public entry point and several
    /// callers reach it with a string they built themselves, so a parameter
    /// would be right only for the callers that happen to remember it.
    /// </remarks>
    private void ApplyDifficultyTheme(string caption)
    {
        int parsed = ParseDifficultyLevel(caption);
        if (parsed <= 0) parsed = LevelFromDifficultyName(caption);
        if (parsed > 0) _difficultyThemeLevel = parsed;

        // 顏色照**難度名稱**（Normal 綠 / Hard 黃 / Expert 紅 / 其他紫），不看
        // 等級——同一個名字在不同曲子的等級差很多，照等級上色會讓同一階難度
        // 在不同曲子間變色。剖不出名字時（例如「Lv. 16~8」這種還沒選的區間）
        // 才退回等級。
        string parsedName = ParseDifficultyName(caption);
        Color theme = string.IsNullOrEmpty(parsedName)
            ? DifficultyVisualPalette.ForLevel(_difficultyThemeLevel)
            : DifficultyVisualPalette.For(parsedName, _difficultyThemeLevel);
        if (gameplayDifficultyText != null)
        {
            // 紙卡是米色的，飽和度全開的黃或綠在上面幾乎讀不到，所以壓暗再用。
            gameplayDifficultyText.color = new Color(theme.r * 0.68f, theme.g * 0.68f,
                theme.b * 0.68f, 1f);
        }

        if (_difficultyFrame != null)
        {
            // Level 決定外框的裝飾階數（愈難愈華麗），顏色則由 OverrideInk
            // 帶進去，兩者刻意分開。
            _difficultyFrame.Level = _difficultyThemeLevel;
            _difficultyFrame.OverrideInk = theme;
        }
    }

    /// <summary>
    /// 從「Expert Lv. 14」這種標題裡取出難度名（"Lv." 之前那一段）。
    /// 只有等級、沒有名字（"Lv. 16~8"）時回空字串。
    /// </summary>
    private static string ParseDifficultyName(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return string.Empty;
        int marker = caption.IndexOf("Lv.", System.StringComparison.OrdinalIgnoreCase);
        string name = marker > 0 ? caption.Substring(0, marker) : caption;
        return name.Trim();
    }

    /// <summary>
    /// Pulls the level out of captions like "Master Lv. 10" or "Lv. 16~13".
    /// </summary>
    /// <remarks>
    /// A range shows the highest first, and the highest is the one that should
    /// decide the colour, so the first run of digits is the right one to take.
    /// </remarks>
    private static int ParseDifficultyLevel(string caption)
    {
        if (string.IsNullOrEmpty(caption)) return 0;

        int marker = caption.IndexOf("Lv.", System.StringComparison.OrdinalIgnoreCase);
        int cursor = marker >= 0 ? marker + 3 : 0;
        int value = 0;
        bool found = false;
        for (; cursor < caption.Length; cursor++)
        {
            char c = caption[cursor];
            if (c >= '0' && c <= '9')
            {
                value = value * 10 + (c - '0');
                found = true;
            }
            else if (found)
            {
                break;
            }
        }

        return found ? value : 0;
    }

    /// <summary>Fallback for captions that carry a name but no number.</summary>
    private static int LevelFromDifficultyName(string caption)
    {
        if (string.IsNullOrEmpty(caption)) return 0;
        string name = caption.ToLowerInvariant();
        if (name.Contains("real")) return 13;
        if (name.Contains("master") || name.Contains("extreme")) return 11;
        if (name.Contains("hard")) return 8;
        if (name.Contains("normal") || name.Contains("easy")) return 4;
        return 0;
    }

    // Public helper: derive difficulty display from a SongSelectionManager.SongOption
    /// <summary>
    /// 正在遊玩的那一個難度，靠譜面檔名比對出來；沒在遊玩就回 null。
    /// </summary>
    private static SongSelectionManager.SongOption ResolvePlayingVariant(
        SongSelectionManager.SongOption option)
    {
        if (option == null) return null;
        string playing = null;
        try
        {
            playing = GameManager.Instance != null ? GameManager.Instance.chartFileName : null;
        }
        catch { }
        if (string.IsNullOrWhiteSpace(playing)) return null;

        if (string.Equals(option.chartFileName, playing, System.StringComparison.OrdinalIgnoreCase)
            && option.difficultyLevel > 0
            && (option.difficultyVariants == null || option.difficultyVariants.Count <= 1))
        {
            return option;
        }
        if (option.difficultyVariants == null) return null;
        foreach (var variant in option.difficultyVariants)
        {
            if (variant == null) continue;
            if (string.Equals(variant.chartFileName, playing,
                    System.StringComparison.OrdinalIgnoreCase))
                return variant;
        }
        return null;
    }

    public void SetSongDifficultyFromOption(SongSelectionManager.SongOption option)
    {
        if (option == null)
        {
            SetSongDifficulty(string.Empty);
            return;
        }

        // If the selection manager stored a player-chosen concrete variant on the group,
        // prefer that variant to avoid showing the aggregated range.
        bool forceUseVariant = false;
        try
        {
            if (option.selectedVariant != null)
            {
                // If selectedVariant points to a different object, switch to it.
                if (!object.ReferenceEquals(option, option.selectedVariant))
                {
                    option = option.selectedVariant;
                }
                else
                {
                    // selectedVariant references the same instance (primary selected).
                    // In that case we still want to treat the primary as a concrete
                    // variant (not show the aggregated range), so set a flag.
                    forceUseVariant = true;
                }
            }
        }
        catch { }

        try
        {
            BuildLogger.Log($"GameBackgroundManager: SetSongDifficultyFromOption called -> displayName='{option.displayName}' difficulty='{option.difficultyName}' level={option.difficultyLevel} variants={(option.difficultyVariants!=null?option.difficultyVariants.Count:0)} selectedVariant={(option.selectedVariant!=null?option.selectedVariant.displayName:"null")} forceUseVariant={forceUseVariant}");
        }
        catch { }

        // 已經定下來是哪一個難度時，就顯示那一個——玩家要看的是「我現在打的是
        // 哪個難度、幾級」，不是這首歌涵蓋的級數範圍。「Lv. 16~8」只在還沒選、
        // 游標停在歌曲群組上時才有意義。
        //
        // 判斷順序：正在遊玩的譜面檔 > 選單記下的 selectedVariant。前者最可靠
        // ——遊玩中一定只有一份譜在跑，而 selectedVariant 會被
        // SongSelectionManager 幾處清成 null，清掉之後標籤就退回區間了。
        SongSelectionManager.SongOption picked =
            ResolvePlayingVariant(option) ?? option.selectedVariant;
        if (picked != null && picked.difficultyLevel > 0)
        {
            SetSongDifficulty(string.IsNullOrWhiteSpace(picked.difficultyName)
                ? $"Lv. {picked.difficultyLevel}"
                : $"{picked.difficultyName} Lv. {picked.difficultyLevel}");
            _playerSetDifficulty = true;
            return;
        }

        // If grouped variants exist, show range "Lv.max~min" (unless we're forcing use of a concrete variant)
        if (option.difficultyVariants != null && option.difficultyVariants.Count > 1 && !forceUseVariant)
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
                if (min == max) { SetSongDifficulty($"Lv. {max}"); _playerSetDifficulty = true; return; }
                SetSongDifficulty($"Lv. {max}~{min}"); _playerSetDifficulty = true; return;
            }

            if (names.Count > 1) { SetSongDifficulty(string.Join("/", names)); _playerSetDifficulty = true; return; }
            if (names.Count == 1) { foreach (var n in names) { SetSongDifficulty(n); _playerSetDifficulty = true; return; } }
        }

        // Fallback to single option display
        if (string.IsNullOrWhiteSpace(option.difficultyName))
        {
            SetSongDifficulty(option.difficultyLevel > 0 ? $"Lv. {option.difficultyLevel}" : string.Empty);
            _playerSetDifficulty = true;
            return;
        }

        SetSongDifficulty(option.difficultyLevel > 0 ? $"{option.difficultyName} Lv. {option.difficultyLevel}" : option.difficultyName);
        _playerSetDifficulty = true;
    }

    private void LoadBackgroundImage(string resourcePath, bool skipVideoStateChanges = false)
    {
        BuildLogger.Log($"GameBackgroundManager: Attempting to load background resource '{resourcePath}'");
        BuildLogger.Log($"GameBackgroundManager: UI refs -> backgroundImage assigned={(backgroundImage!=null)} songImage assigned={(songImage!=null)} backgroundActive={(backgroundImage!=null?backgroundImage.gameObject.activeInHierarchy:false)}");
        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            // Prefer using SongSelectionManager's loading logic if available (keeps behavior consistent)
            var selector = SongSelectionManager.Instance ?? FindAnyObjectByType<SongSelectionManager>();
            if (selector != null)
            {
                var selOption = selector.GetSelectedSong();
                if (selOption != null)
                {
                    BuildLogger.Log($"GameBackgroundManager: SongSelectionManager selected -> displayName='{selOption.displayName}' coverResourcePath='{selOption.coverResourcePath}' coverSpriteSet={(selOption.coverSprite!=null)}");
                }

                try
                {
                    sprite = selector.LoadCoverSprite(resourcePath, resourcePath);
                    if (sprite != null)
                    {
                        texture = sprite.texture;
                        BuildLogger.Log($"GameBackgroundManager: Loaded Sprite via SongSelectionManager '{resourcePath}' name='{sprite.name}' size={texture.width}x{texture.height}");
                    }
                    else
                    {
                        BuildLogger.Log($"GameBackgroundManager: SongSelectionManager.LoadCoverSprite returned null for '{resourcePath}'");
                    }
                }
                    catch (System.Exception ex)
                    {
                        BuildLogger.LogWarning($"GameBackgroundManager: SongSelectionManager.LoadCoverSprite threw: {ex.Message}");
                    }
            }

            // Fallback to direct Resources.Load if selector didn't provide a sprite
            if (texture == null)
            {
                // Try Sprite first (assets imported as Sprite)
                sprite = Resources.Load<Sprite>(resourcePath);
                if (sprite != null)
                {
                    texture = sprite.texture;
                    BuildLogger.Log($"GameBackgroundManager: Loaded Sprite resource '{resourcePath}' name='{sprite.name}' size={texture.width}x{texture.height}");
                }
                else
                {
                    texture = Resources.Load<Texture2D>(resourcePath);
                    if (texture != null)
                    {
                        BuildLogger.Log($"GameBackgroundManager: Loaded Texture2D resource '{resourcePath}' size={texture.width}x{texture.height}");
                    }
                }
            }
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: Resources.Load threw: {ex.Message}");
            }

        // Fallback: try to locate by filename in all Resources (expensive but useful for troubleshooting)
        if (texture == null)
        {
            try
            {
                string fileName = System.IO.Path.GetFileName(resourcePath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var allSprites = Resources.LoadAll<Sprite>("");
                    foreach (var s in allSprites)
                    {
                        if (s == null) continue;
                        if (string.Equals(s.name, fileName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            sprite = s;
                            texture = s.texture;
                            BuildLogger.Log($"GameBackgroundManager: Fallback found Sprite by name '{s.name}' size={texture.width}x{texture.height}");
                            break;
                        }
                    }

                    if (texture == null)
                    {
                        var allTex = Resources.LoadAll<Texture2D>("");
                        foreach (var t in allTex)
                        {
                            if (t == null) continue;
                            if (string.Equals(t.name, fileName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                texture = t;
                                BuildLogger.Log($"GameBackgroundManager: Fallback found Texture2D by name '{t.name}' size={texture.width}x{texture.height}");
                                break;
                            }
                        }
                    }
                }
                }
                catch (System.Exception ex)
                {
                    BuildLogger.LogWarning($"GameBackgroundManager: Fallback search exception: {ex.Message}");
                }
        }

        if (texture == null)
        {
            BuildLogger.LogError($"GameBackgroundManager: 無法載入背景圖片: {resourcePath} (tried SongSelectionManager/Resources and fallback search)");
            return;
        }

        if (!skipVideoStateChanges)
        {
            videoPlayer?.gameObject.SetActive(false);
        }

        // Prefer displaying the artwork in `songImage` (used for chart drawing) when available.
        if (songImage != null)
        {
            BuildLogger.Log($"GameBackgroundManager: Assigning loaded texture to songImage (preferred)");
            // Avoid redundant assigns for same resource/texture
            if (string.Equals(_lastAssignedResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase) && _lastAssignedTexture == texture)
            {
                BuildLogger.Log("GameBackgroundManager: Skipping assign — same resource already set on songImage.");
            }
            else
            {
                // clear background so it doesn't occlude the songImage
                try
                {
                    if (backgroundImage != null)
                    {
                        // Clear texture so it doesn't accidentally show the previous image,
                        // but do not deactivate the GameObject here — leave activation
                        // control to the fallback logic or video player.
                        backgroundImage.texture = sBlackTexture;
                        // Keep a neutral black fill so the pre-roll phase stays clean instead of flashing white
                        backgroundImage.color = Color.black;
                        backgroundImage.gameObject.SetActive(true);
                    }
                }
                catch { }

                ShowImageOnRawImage(songImage, texture);
                _lastAssignedResourcePath = resourcePath;
                _lastAssignedTexture = texture;
                // re-run layout adjustment next frame to capture correct rect sizes
                if (isActiveAndEnabled)
                {
                    StartCoroutine(RefreshImageLayoutNextFrame(songImage));
                }
                else
                {
                    // If the manager is not active, do a best-effort immediate adjust
                    try { AdjustImageSize(songImage); } catch { }
                }
            }
        }
        else if (backgroundImage != null)
        {
            BuildLogger.Log($"GameBackgroundManager: Assigning loaded texture to backgroundImage (fallback)");
            if (!(string.Equals(_lastAssignedResourcePath, resourcePath, StringComparison.OrdinalIgnoreCase) && _lastAssignedTexture == texture))
            {
                ShowImageOnRawImage(backgroundImage, texture);
                _lastAssignedResourcePath = resourcePath;
                _lastAssignedTexture = texture;
                if (isActiveAndEnabled)
                {
                    StartCoroutine(RefreshImageLayoutNextFrame(backgroundImage));
                }
                else
                {
                    try { AdjustImageSize(backgroundImage); } catch { }
                }
            }
            else
            {
                BuildLogger.Log("GameBackgroundManager: Skipping assign — same resource already set on backgroundImage.");
            }
        }
        else
        {
            BuildLogger.LogWarning("GameBackgroundManager: 未設定可用的 RawImage 以顯示背景或曲譜封面。");
        }
    }

    private void PlayBackgroundVideo(string videoPath)
    {
        if (videoPlayer == null)
        {
            // Silent until now: every other report on this path is a stripped
            // BuildLogger.Log, so a missing player looked exactly like a missing
            // video file.
            Debug.LogWarning($"[Background] No VideoPlayer assigned; '{videoPath}' cannot play.");
            return;
        }
        // Normalize path separators
        string normalizedPath = videoPath.Replace("\\", "/");
            if (videoBackdrop != null)
            {
                // Keep backdrop as last loaded cover or black; if not set, ensure black
                if (videoBackdrop.texture == null)
                {
                    videoBackdrop.texture = sBlackTexture;
                    videoBackdrop.color = Color.black;
                }
                videoBackdrop.gameObject.SetActive(true);
            }
        // Imported songs live under UserSongs, not StreamingAssets: their register
        // paths come back from ExternalSongLibrary carrying the "external://"
        // prefix and an absolute path.  Joining that onto streamingAssetsPath
        // produced a path that never exists, so every imported song's video was
        // silently skipped with only a stripped BuildLogger line to show for it.
        string externalPath = ExternalSongLibrary.ToLocalPath(videoPath);
        bool isExternal = !string.IsNullOrEmpty(externalPath);
        string candidate = isExternal
            ? externalPath.Replace("\\", "/")
            : System.IO.Path.Combine(Application.streamingAssetsPath, normalizedPath).Replace("\\", "/");

        // Prefer explicit file:// URL on Windows for the VideoPlayer.  Imported
        // songs are handed the bare path instead: their folders are named by the
        // player and routinely hold non-ASCII characters and '+', which a
        // hand-built file:/// URL does not escape and the player then mis-reads.
        string fullUrl = candidate;
        if (!isExternal && System.IO.Path.IsPathRooted(candidate) && !candidate.StartsWith("file://"))
        {
            fullUrl = "file:///" + candidate;
        }

        // Defensive: check file existence and try common extensions if missing
        string filesystemPath = candidate;
        bool exists = false;
        try { exists = System.IO.File.Exists(filesystemPath); } catch { exists = false; }
        if (!exists)
        {
            // Try with .mp4 extension
            try
            {
                if (!filesystemPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    var tryMp4 = filesystemPath + ".mp4";
                    if (System.IO.File.Exists(tryMp4))
                    {
                        filesystemPath = tryMp4;
                        fullUrl = isExternal
                            ? tryMp4.Replace("\\", "/")
                            : "file:///" + tryMp4.Replace("\\", "/");
                        exists = true;
                    }
                }
            }
            catch { }
        }

        if (!exists)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: Video file not found at '{filesystemPath}'. URL used='{fullUrl}'");
            return;
        }

        // Configure VideoPlayer for API usage and wire up events so we can assign its texture to UI
        try
        {
            videoPlayer.errorReceived -= OnVideoErrorReceived;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
                videoPlayer.started -= OnVideoStarted;
        }
        catch { }
        videoPlayer.errorReceived += OnVideoErrorReceived;
        videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.started += OnVideoStarted;
        try
        {
            if (videoPlayer.audioOutputMode != VideoAudioOutputMode.None)
            {
                videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                BuildLogger.Log("GameBackgroundManager: Disabled VideoPlayer audio output (mute).");
            }
        }
        catch (Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: Failed to disable VideoPlayer audio output: {ex.Message}");
        }

        // Pre-fill background with black so the pre-roll phase never shows a white RawImage while the video prepares
        try
        {
            if (backgroundImage != null)
            {
                backgroundImage.texture = sBlackTexture;
                backgroundImage.color = Color.black;
                backgroundImage.gameObject.SetActive(true);
            }
        }
        catch { }

        // assign url and prepare; actual Play() will be triggered in prepare callback
        _prepareStartedRealtime = Time.realtimeSinceStartup;
        _prepareReported = false;
        videoPlayer.url = fullUrl;
        videoPlayer.gameObject.SetActive(true);
        if (backgroundImage != null)
        {
            // keep backgroundImage active so we can assign texture when ready
            backgroundImage.gameObject.SetActive(true);
        }
        // If we previously assigned artwork to songImage, keep it visible while video prepares
        try
        {
            if (songImage != null && songImage.texture != null)
            {
                songImage.gameObject.SetActive(true);
            }
        }
        catch { }
        // Use Prepare to ensure texture is available before attempting to read videoPlayer.texture
        try { videoPlayer.Prepare(); } catch (Exception ex) { BuildLogger.LogWarning($"GameBackgroundManager: VideoPlayer.Prepare() threw: {ex}"); }
    }

    private void OnVideoErrorReceived(VideoPlayer source, string message)
    {
        Debug.LogWarning($"[Background] VideoPlayer error: {message} url={source.url}");
        // On error, hide overlay and ensure player is stopped
        try
        {
            SetVideoOverlay(false);
            if (source.isPlaying) source.Stop();
            source.errorReceived -= OnVideoErrorReceived;
            source.prepareCompleted -= OnVideoPrepared;
            videoPlayer.gameObject.SetActive(false);
        }
        catch { }
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        // Always on. Everything else on this path logs through BuildLogger.Log,
        // which is stripped unless NOSTALGIA_VERBOSE_LOGGING is defined — in the
        // editor too — so how long preparation took was invisible even while
        // watching the console.
        float prepareSeconds = _prepareStartedRealtime >= 0f
            ? Time.realtimeSinceStartup - _prepareStartedRealtime
            : -1f;
        _prepareReported = true;
        Debug.Log($"[Background] Video prepared in {prepareSeconds:F2}s " +
            $"({source.width}x{source.height}, {source.length:F1}s, {source.frameRate:F1}fps)");
        try
        {
            // Assign the video texture to the background RawImage so the UI shows the video.
            // Keep it black until the actual playback start event fires.
            if (backgroundImage != null && source.texture != null)
            {
                backgroundImage.texture = source.texture;
                backgroundImage.color = Color.black;
                backgroundImage.raycastTarget = false;
                // LoadBackground calls AdjustVideoPlayerSize() straight after
                // PlayBackgroundVideo(), but that is only a Prepare() — the texture
                // does not exist yet, so the sizing returned immediately and the
                // RawImage kept the AspectRatioFitter the *cover* left behind.
                // A square 500x500 cover therefore squeezed a 16:9 video into a
                // square envelope.  Now that the texture is real, size it for the
                // video.
                AdjustVideoPlayerSize();
            }

            // Apply visual pre-roll offset from Conductor/Settings so video aligns with pre-rolled notes
            float preRollSec = 0f;
            var gm = GameManager.Instance;
            Conductor c = null;
            try { c = gm != null ? gm.Conductor : FindAnyObjectByType<Conductor>(); } catch { c = null; }
            if (c != null)
            {
                float preRollMs = c.isVisualPlaying ? Mathf.Max(0f, -c.visualSongPosition) : c.RemainingPreRollMs;
                preRollSec = preRollMs / 1000f;
            }

            // If a per-song video offset was provided via SchedulePlaybackAtDsp:
            // - If scheduledVideoStartOffsetSec > 0: interpret it as "start the video at this time offset when dspStart occurs"
            //   (i.e. do NOT play early to show pre-roll frames). This is the strict offset mode requested by user.
            // - Otherwise, fallback to using conductor pre-roll behavior (play earlier so pre-roll frames are visible).
            if (scheduledVideoStartOffsetSec <= 0.0)
            {
                try
                {
                    var selectedSong = SongSelectionManager.Instance != null ? SongSelectionManager.Instance.GetSelectedSong() : null;
                    if (selectedSong != null && selectedSong.videoStartTimeSec > 0f)
                    {
                        scheduledVideoStartOffsetSec = selectedSong.videoStartTimeSec;
                    }
                }
                catch { }
            }
            double effectivePreRoll = preRollSec;
            bool strictDelayMode = scheduledVideoStartOffsetSec > 0.0;
            BuildLogger.Log($"GameBackgroundManager: OnVideoPrepared preRollSec={preRollSec:F3} strictDelayMode={strictDelayMode} delaySec={scheduledVideoStartOffsetSec:F3} canSetTime={source.canSetTime}, url={source.url}, length={source.length:F3}, frameRate={source.frameRate}");

            // ensure playOnAwake is false so we control when playback starts
            try { source.playOnAwake = false; } catch { }

            bool seeked = false;
            bool skipPlaybackScheduling = false;

            if (strictDelayMode && scheduledDspStart <= 0.0)
            {
                awaitingStrictSchedule = true;
                skipPlaybackScheduling = true;
                try
                {
                    if (source.isPlaying) source.Pause();
                }
                catch { }
                try
                {
                    if (source.canSetTime)
                    {
                        source.time = 0.0;
                    }
                }
                catch { }
                if (videoPlaybackRoutine != null)
                {
                    try { StopCoroutine(videoPlaybackRoutine); }
                    catch { }
                    videoPlaybackRoutine = null;
                }
                BuildLogger.Log("GameBackgroundManager: StrictDelayMode pending dsp schedule; awaiting SchedulePlaybackAtDsp before starting video.");
            }
            else
            {
                awaitingStrictSchedule = false;
            }

            // Decide how to apply pre-roll: prefer starting the video earlier so it can show pre-roll frames
            // i.e. play the video at (dspStart - preRollSec) from time 0 so at dspStart the video reached preRollSec.
            // If we cannot start earlier (too close to now), fallback to seeking to preRollSec and playing at dspStart.
            if (!skipPlaybackScheduling)
            {
                try
                {
                    if (scheduledDspStart > 0)
                    {
                        double nowDsp = AudioSettings.dspTime;
                        if (strictDelayMode)
                        {
                            // STRICT DELAY MODE: wait for (dspStart + delay) before starting playback, beginning at t=0.
                            double playbackDsp = scheduledDspStart + scheduledVideoStartOffsetSec;
                            if (source.canSetTime)
                            {
                                try
                                {
                                    source.time = 0.0;
                                    seeked = true;
                                    BuildLogger.Log($"GameBackgroundManager: StrictDelayMode: reset VideoPlayer.time to 0.000s (will Play at dsp={playbackDsp:F3})");
                                }
                                catch (Exception ex)
                                {
                                    BuildLogger.LogWarning($"GameBackgroundManager: StrictDelayMode failed to reset time pre-start: {ex}");
                                }
                            }
                            else
                            {
                                // If we cannot set time now, we'll still wait and Play at the delayed dsp start; it's best-effort.
                                BuildLogger.LogWarning($"GameBackgroundManager: StrictDelayMode but VideoPlayer cannot set time before play; will attempt to Play at dsp-delay and hope default position is 0.");
                            }

                            try
                            {
                                if (source.isPlaying) source.Pause();
                            }
                            catch { }
                            BeginWaitForDsp(source, playbackDsp, seeked, /*strictDelayMode*/ true);
                        }
                        else
                        {
                            double desiredVideoStartDsp = scheduledDspStart - (double)effectivePreRoll;
                            // small safety buffer to avoid scheduling in the past
                            double minStart = nowDsp + 0.02;
                            if (desiredVideoStartDsp <= minStart)
                            {
                                // Preparation finished after the moment the video
                                // was supposed to begin — a big file, a cold disk,
                                // a slow decoder. Playing from zero now would put
                                // the picture exactly that far behind the music and
                                // keep it there for the whole song, which is the
                                // "it starts eventually but the timing is wrong"
                                // case. Start from where the song already is.
                                double lateBy = nowDsp - desiredVideoStartDsp;
                                if (lateBy > 0.05 && source.canSetTime)
                                {
                                    try
                                    {
                                        double target = Math.Max(0.0, lateBy);
                                        if (source.length > 0.0 && target < source.length)
                                        {
                                            source.time = target;
                                            seeked = true;
                                            Debug.Log($"[Background] Video prepared {lateBy:F2}s late; " +
                                                $"seeking to {target:F2}s instead of starting from zero.");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        BuildLogger.LogWarning($"GameBackgroundManager: late-prepare seek failed: {ex}");
                                    }
                                }
                                try { BeginWaitForDsp(source, desiredVideoStartDsp, seeked, /*strictDelayMode*/ false); }
                                catch (Exception ex) { BuildLogger.LogWarning($"GameBackgroundManager: Failed to start WaitForDspThenPlay coroutine (late pre-roll): {ex}"); }
                            }
                            else
                            {
                                // Start the video earlier so pre-roll frames play naturally. Do not seek; play from 0 at desiredVideoStartDsp.
                                try { BeginWaitForDsp(source, desiredVideoStartDsp, /*seeked*/ false, /*strictDelayMode*/ false); }
                                catch (Exception ex) { BuildLogger.LogWarning($"GameBackgroundManager: Failed to start WaitForDspThenPlay coroutine (early start): {ex}"); }
                            }
                        }
                    }
                    else if (c != null && !c.isVisualPlaying)
                    {
                        // Start a coroutine that polls conductor state and starts playback when visual pre-roll begins
                        try { StartCoroutine(WaitForConductorThenPlay(source, c, seeked)); }
                        catch (Exception ex) { BuildLogger.LogWarning($"GameBackgroundManager: Failed to start WaitForConductorThenPlay coroutine: {ex}"); }
                    }
                    else
                    {
                        try
                        {
                            source.Play();
                            BuildLogger.Log($"GameBackgroundManager: VideoPlayer.Play() called; seeked={seeked}");
                        }
                        catch (Exception ex)
                        {
                            BuildLogger.LogWarning($"GameBackgroundManager: VideoPlayer.Play() after prepare threw: {ex}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    BuildLogger.LogWarning($"GameBackgroundManager: OnVideoPrepared pre-roll scheduling error: {ex}");
                }
            }

            // Adjust sizing after a frame so texture dimensions are available
            StartCoroutine(DelayedAdjustVideoSize());
        }
        catch (Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: OnVideoPrepared exception: {ex}");
        }
    }

    private IEnumerator DelayedAdjustVideoSize()
    {
        // wait one frame for texture to be fully available
        yield return null;
        AdjustVideoPlayerSize();
    }

    private IEnumerator WaitForConductorThenPlay(VideoPlayer source, Conductor conductor, bool seeked)
    {
        float timeout = 10f; // seconds
        float waited = 0f;
        float pollInterval = 0.05f;
        while (waited < timeout)
        {
            try
            {
                if (conductor != null && conductor.isVisualPlaying)
                {
                    try { source.Play(); BuildLogger.Log($"GameBackgroundManager: WaitForConductorThenPlay - started playback after conductor visual pre-roll began; seeked={seeked}"); } catch (Exception ex) { BuildLogger.LogWarning($"GameBackgroundManager: Failed to Play after conductor visual start: {ex}"); }
                    yield break;
                }
            }
            catch { }
            yield return new WaitForSecondsRealtime(pollInterval);
            waited += pollInterval;
        }
        // Timeout: fallback to playing anyway
        try { source.Play(); BuildLogger.Log($"GameBackgroundManager: WaitForConductorThenPlay - timeout reached, starting playback; seeked={seeked}"); } catch (Exception ex) { BuildLogger.LogWarning($"GameBackgroundManager: Failed to Play after timeout: {ex}"); }
    }

    private void BeginWaitForDsp(VideoPlayer source, double dspStart, bool seeked, bool strictDelayMode)
    {
        if (source == null) return;
        if (videoPlaybackRoutine != null)
        {
            try { StopCoroutine(videoPlaybackRoutine); }
            catch { }
            videoPlaybackRoutine = null;
        }
        videoPlaybackRoutine = StartCoroutine(WaitForDspThenPlay(source, dspStart, seeked, strictDelayMode));
    }

    private IEnumerator WaitForDspThenPlay(VideoPlayer source, double dspStart, bool seeked, bool strictDelayMode)
    {
        double timeout = dspStart + 10.0; // give 10s grace beyond scheduled time
        try
        {
            BuildLogger.Log($"GameBackgroundManager: WaitForDspThenPlay started -> dspStart={dspStart:F3}, strictDelay={strictDelayMode}, seeked={seeked}");
        }
        catch { }
        float originalSpeed = 1f;
        if (strictDelayMode)
        {
            try
            {
                originalSpeed = source.playbackSpeed;
                source.playbackSpeed = 0f;
                BuildLogger.Log("GameBackgroundManager: WaitForDspThenPlay clamped playbackSpeed to 0 while waiting.");
            }
            catch (Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: WaitForDspThenPlay failed to clamp playbackSpeed: {ex.Message}");
            }
        }
            bool initialWait = strictDelayMode;
            double lastLoggedRemaining = double.MaxValue;
            while (AudioSettings.dspTime < dspStart && AudioSettings.dspTime < timeout)
        {
            double remaining = dspStart - AudioSettings.dspTime;
            bool videoPlaying = false;
            try { videoPlaying = source.isPlaying; }
            catch { }
            if (strictDelayMode && initialWait)
            {
                    if (remaining <= 0.5)
                    {
                        BuildLogger.Log($"GameBackgroundManager: WaitForDspThenPlay waiting... remaining={remaining * 1000.0:F1}ms strictDelay={strictDelayMode} videoPlaying={videoPlaying}");
                        if (videoPlaying)
                        {
                            try
                            {
                                BuildLogger.LogWarning("GameBackgroundManager: WaitForDspThenPlay detected playback during wait; issuing Pause().");
                                source.Pause();
                            }
                            catch (Exception ex)
                            {
                                BuildLogger.LogWarning($"GameBackgroundManager: WaitForDspThenPlay failed to Pause during wait: {ex}");
                            }
                        }
                        initialWait = false;
                        lastLoggedRemaining = remaining;
                    }
                    else if (remaining < lastLoggedRemaining - 0.5)
                    {
                        BuildLogger.Log($"GameBackgroundManager: WaitForDspThenPlay waiting... remaining={remaining:F3}s strictDelay={strictDelayMode} videoPlaying={videoPlaying}");
                        if (videoPlaying)
                        {
                            try
                            {
                                BuildLogger.LogWarning("GameBackgroundManager: WaitForDspThenPlay detected playback during wait; issuing Pause().");
                                source.Pause();
                            }
                            catch (Exception ex)
                            {
                                BuildLogger.LogWarning($"GameBackgroundManager: WaitForDspThenPlay failed to Pause during wait: {ex}");
                            }
                        }
                        lastLoggedRemaining = remaining;
                    }
            }
                if (remaining > 0.05)
                {
                    yield return new WaitForSecondsRealtime(0.04f);
                }
                else if (remaining > 0.005)
                {
                    float wait = Mathf.Clamp((float)(remaining - 0.002), 0.001f, 0.02f);
                    yield return new WaitForSecondsRealtime(wait);
                }
                else
                {
                    yield return null; // poll every frame when within 5ms
                }
        }
        try
        {
            if (strictDelayMode && !seeked)
            {
                if (source.canSetTime)
                {
                    try
                    {
                        source.time = 0.0;
                        seeked = true;
                        BuildLogger.Log("GameBackgroundManager: WaitForDspThenPlay - strict delay ensure time reset to 0 before Play.");
                    }
                    catch (Exception ex)
                    {
                        BuildLogger.LogWarning($"GameBackgroundManager: WaitForDspThenPlay failed to reset time pre-play: {ex}");
                    }
                }
            }
            if (strictDelayMode)
            {
                try
                {
                    source.playbackSpeed = originalSpeed <= 0f ? 1f : originalSpeed;
                    BuildLogger.Log($"GameBackgroundManager: WaitForDspThenPlay restored playbackSpeed to {source.playbackSpeed:F2} before Play().");
                }
                catch (Exception ex)
                {
                    BuildLogger.LogWarning($"GameBackgroundManager: WaitForDspThenPlay failed to restore playbackSpeed: {ex.Message}");
                }
            }
            try { BuildLogger.Log($"GameBackgroundManager: WaitForDspThenPlay invoking Play() at dsp={AudioSettings.dspTime:F3}"); }
            catch { }
            source.Play();
            double actualDsp = AudioSettings.dspTime;
            double deltaMs = (actualDsp - dspStart) * 1000.0;
            BuildLogger.Log($"GameBackgroundManager: WaitForDspThenPlay - started playback at dsp={actualDsp:F3}, scheduled={dspStart:F3}, offset={deltaMs:+0.0;-0.0;0.0}ms, seeked={seeked}, strictDelay={strictDelayMode}");
        }
        catch (Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: WaitForDspThenPlay failed to Play (strictDelay={strictDelayMode}): {ex}");
        }
        finally
        {
            if (strictDelayMode)
            {
                try
                {
                    source.playbackSpeed = originalSpeed <= 0f ? 1f : originalSpeed;
                }
                catch { }
            }
        }
        videoPlaybackRoutine = null;
    }

    /// <summary>
    /// Everything the video path switches on, so it can be checked afterwards.
    /// </summary>
    /// <remarks>
    /// The glow appears once a video song has been played and then survives into
    /// songs that have no video at all. That shape of fault is state left behind,
    /// not something being drawn wrongly now — and every candidate here is set by
    /// the video path and cleared somewhere else, which is exactly where a value
    /// gets stranded.
    /// </remarks>
    public string DescribeVideoState()
    {
        string overlay = videoOverlay != null
            ? $"active={videoOverlay.gameObject.activeInHierarchy} colour={videoOverlay.color}"
            : "<none>";
        string background = backgroundImage != null
            ? $"active={backgroundImage.gameObject.activeInHierarchy} colour={backgroundImage.color} " +
              $"tex={(backgroundImage.texture != null ? backgroundImage.texture.name : "<null>")}"
            : "<none>";
        string backdrop = videoBackdrop != null
            ? $"active={videoBackdrop.gameObject.activeInHierarchy} colour={videoBackdrop.color} " +
              $"tex={(videoBackdrop.texture != null ? videoBackdrop.texture.name : "<null>")}"
            : "<none>";
        string player = videoPlayer != null
            ? $"active={videoPlayer.gameObject.activeInHierarchy} playing={videoPlayer.isPlaying} " +
              $"prepared={videoPlayer.isPrepared}"
            : "<none>";
        return $"videoOverlay[{overlay}] background[{background}] backdrop[{backdrop}] " +
            $"player[{player}] dimApplied={_backgroundDimApplied} " +
            $"dimmerRequested={_videoRequestedForTrackDimmer} dimmer={_currentTrackDimmerApplied:F2} " +
            $"dimmerEntries={_trackRendererEntries.Count}";
    }

    private void SetVideoOverlay(bool show)
    {
        if (videoOverlay == null) return;
        try
        {
            // Dim the background pixels themselves. A full-screen overlay Canvas is
            // rendered after world geometry on some targets and can darken the Track.
            // Multiplying the background keeps the exact order: background/dim -> Track -> HUD.
            if (backgroundImage != null)
            {
                if (!_backgroundImageBaseColorCached)
                {
                    _backgroundImageBaseColor = backgroundImage.color;
                    _backgroundImageBaseColorCached = true;
                }
                if (show)
                {
                    if (!_backgroundDimApplied)
                        _backgroundImageBaseColor = backgroundImage.color;
                    float brightness = 1f - Mathf.Clamp01(videoOverlayOpacity);
                    Color baseColor = _backgroundImageBaseColor;
                    backgroundImage.color = new Color(baseColor.r * brightness,
                        baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
                    _backgroundDimApplied = true;
                }
                else
                {
                    backgroundImage.color = _backgroundImageBaseColor;
                    _backgroundDimApplied = false;
                }
                videoOverlay.color = Color.clear;
                videoOverlay.raycastTarget = false;
                videoOverlay.gameObject.SetActive(false);
                return;
            }

            if (show)
            {
                var dimColor = overlayColor;
                dimColor.a = Mathf.Clamp01(videoOverlayOpacity);
                videoOverlay.color = dimColor;
            }
            else
            {
                videoOverlay.color = Color.clear;
            }
            videoOverlay.raycastTarget = false;
            if (show)
            {
                videoOverlay.gameObject.SetActive(true);
                // place overlay directly above backgroundImage (so it doesn't cover HUD elements)
                try
                {
                    if (backgroundImage != null && videoOverlay.transform.parent == backgroundImage.transform.parent)
                    {
                        int bgIndex = backgroundImage.transform.GetSiblingIndex();
                        int targetIndex = Mathf.Min(videoOverlay.transform.parent.childCount - 1, bgIndex + 1);
                        videoOverlay.transform.SetSiblingIndex(targetIndex);
                    }
                }
                catch { }
                // Ensure songImage (cover) and HUD remain above overlay if possible
                try
                {
                    if (songImage != null)
                    {
                        songImage.transform.SetAsLastSibling();
                        songImage.gameObject.SetActive(songImage.texture != null);
                    }
                }
                catch { }
            }
            else
            {
                videoOverlay.gameObject.SetActive(false);
            }
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"GameBackgroundManager: SetVideoOverlay failed: {ex.Message}");
        }
    }

    private void StopVideoPlayback()
    {
        try
        {
            if (videoPlayer != null)
            {
                try { videoPlayer.started -= OnVideoStarted; } catch { }
                if (videoPlayer.isPlaying) videoPlayer.Stop();
                try { videoPlayer.errorReceived -= OnVideoErrorReceived; } catch { }
                try { videoPlayer.prepareCompleted -= OnVideoPrepared; } catch { }
                videoPlayer.gameObject.SetActive(false);
                // if we were using a targetTexture, release it
                try
                {
                    if (videoPlayer.targetTexture != null)
                    {
                        var rt = videoPlayer.targetTexture;
                        videoPlayer.targetTexture = null;
                        // Do not Destroy immediate; keep ownership to scene if assigned externally
                        // Destroy(rt);
                    }
                }
                catch { }
            }
        }
        catch { }
        if (videoPlaybackRoutine != null)
        {
            try { StopCoroutine(videoPlaybackRoutine); }
            catch { }
            videoPlaybackRoutine = null;
        }
        // Hide overlay when stopping
        SetVideoOverlay(false);
        SetVideoVisible(false);
        // reset scheduled dsp start
        scheduledDspStart = -1.0;
        scheduledVideoStartOffsetSec = 0.0;
        awaitingStrictSchedule = false;
        // Clear the background image texture and restore to black so switching songs doesn't show white
        try
        {
            if (backgroundImage != null)
            {
                backgroundImage.texture = sBlackTexture;
                backgroundImage.color = Color.black;
                backgroundImage.gameObject.SetActive(true);
            }
            if (videoBackdrop != null)
            {
                videoBackdrop.texture = sBlackTexture;
                videoBackdrop.color = Color.black;
                videoBackdrop.gameObject.SetActive(true);
            }
            if (songImage != null)
            {
                // Keep songImage visible only if it has texture
                songImage.gameObject.SetActive(songImage.texture != null);
            }
        }
        catch { }
    }

    private void SetVideoVisible(bool visible)
    {
        try
        {
            if (backgroundImage != null)
            {
                backgroundImage.color = visible ? Color.white : Color.black;
            }
            if (videoBackdrop != null)
            {
                if (visible)
                {
                    // Hide backdrop so it doesn't cover the playing video
                    videoBackdrop.gameObject.SetActive(false);
                }
                else
                {
                    videoBackdrop.texture = videoBackdrop.texture ?? sBlackTexture;
                    videoBackdrop.color = Color.black;
                    videoBackdrop.gameObject.SetActive(true);
                }
            }
        }
        catch { }
    }

    private void OnVideoStarted(VideoPlayer source)
    {
        SetVideoVisible(true);
    }

    // External API: schedule video playback to start at given DSP time (AudioSettings.dspTime base)
    public void SchedulePlaybackAtDsp(double dspStart, double videoDelaySec = 0.0)
    {
        scheduledDspStart = dspStart;
        scheduledVideoStartOffsetSec = videoDelaySec;
        awaitingStrictSchedule = false;
        BuildLogger.Log($"GameBackgroundManager: SchedulePlaybackAtDsp called, dspStart={dspStart:F3}, videoDelay={videoDelaySec:F3}");
        // if already prepared, start waiting for the desired video start time (dspStart + delay or dspStart - preRoll)
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            try
            {
                bool strictDelay = scheduledVideoStartOffsetSec > 0.0;
                double desiredStart;
                bool seeked = false;
                if (strictDelay)
                {
                    // Wait until dspStart + delay and start from t=0
                    desiredStart = dspStart + scheduledVideoStartOffsetSec;
                    if (videoPlayer.canSetTime)
                    {
                        try
                        {
                            videoPlayer.time = 0.0;
                            seeked = true;
                            BuildLogger.Log($"GameBackgroundManager: SchedulePlaybackAtDsp strict delay -> reset VideoPlayer.time to 0 before waiting.");
                        }
                        catch (Exception ex)
                        {
                            BuildLogger.LogWarning($"GameBackgroundManager: SchedulePlaybackAtDsp strict delay failed to reset time: {ex}");
                        }
                    }
                }
                else if (scheduledVideoStartOffsetSec > 0.0)
                {
                    desiredStart = dspStart + scheduledVideoStartOffsetSec;
                }
                else
                {
                    desiredStart = dspStart;
                }
                BeginWaitForDsp(videoPlayer, desiredStart, seeked, strictDelay);
            }
            catch { }
        }
    }

    private void AdjustBackgroundSize()
    {
        if (backgroundImage != null && backgroundImage.texture != null)
        {
            RectTransform rectTransform = backgroundImage.GetComponent<RectTransform>();
            // 將背景設為 stretch 充滿父物件，再用 AspectRatioFitter 保持寬高比
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            // 設定 RawImage 保持比例顯示（RawImage 沒有 preserveAspect 屬性，AspectRatioFitter 用來處理）

            // 加入或設定 AspectRatioFitter（FitInParent）以在不同 CanvasScaler 下穩定表現
            var arf = backgroundImage.GetComponent<UnityEngine.UI.AspectRatioFitter>();
            if (arf == null)
            {
                arf = backgroundImage.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            }
            try
            {
                // Use EnvelopeParent to ensure the background covers the parent (may crop),
                // which prevents visible empty margins on the sides.
                arf.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
                arf.aspectRatio = (backgroundImage.texture.width > 0 && backgroundImage.texture.height > 0)
                    ? (float)backgroundImage.texture.width / (float)backgroundImage.texture.height
                    : 1f;
            }
            catch { }

            // Ensure backgroundImage does not block raycasts for UI interaction
            backgroundImage.raycastTarget = false;
        }
    }

    private void AdjustSplitBackgroundSize(Texture2D texture)
    {
        // Split 背景功能已移除：改為使用單一 backgroundImage 與獨立的 songImage 作為繪圖區域
        return;
    }

    // ConfigureSplitRect 已移除

    private void AdjustVideoPlayerSize()
    {
        if (videoPlayer == null) return;

        // Prefer the actual video texture; if not available, try targetTexture
        Texture tex = null;
        try { tex = videoPlayer.texture != null ? videoPlayer.texture : (Texture)videoPlayer.targetTexture; } catch { tex = null; }
        if (tex == null) return;

        float videoAspect = (tex.width > 0 && tex.height > 0) ? ((float)tex.width / (float)tex.height) : 1f;
        float screenAspect = (float)Screen.width / (float)Screen.height;

        // If we have a UI RawImage for background, set an AspectRatioFitter on it so the UI displays correctly.
        if (backgroundImage != null)
        {
            var arf = backgroundImage.GetComponent<UnityEngine.UI.AspectRatioFitter>();
            if (arf == null)
            {
                arf = backgroundImage.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            }
            try
            {
                arf.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
                arf.aspectRatio = videoAspect;
            }
            catch { }

            // Ensure RawImage scales to its parent
            var rt = backgroundImage.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            // Fallback surface is switched off in LoadBackground, which runs
            // whether or not the video has finished decoding by now.
            return;
        }

        // Fallback: if Video Player GameObject has a RectTransform (e.g. placed under Canvas), adjust it.
        RectTransform vpRect = null;
        try { videoPlayer.TryGetComponent<RectTransform>(out vpRect); } catch { vpRect = null; }
        if (vpRect != null)
        {
            if (videoAspect > screenAspect)
            {
                vpRect.sizeDelta = new Vector2(Screen.height * videoAspect, Screen.height);
            }
            else
            {
                vpRect.sizeDelta = new Vector2(Screen.width, Screen.width / videoAspect);
            }
        }
    }

    /// <summary>
    /// Switches off the VideoPlayer's own RawImage, the fallback display.
    /// </summary>
    /// <remarks>
    /// Only safe because backgroundImage is showing the same video; the caller
    /// checks that. Disabling the component rather than the GameObject leaves the
    /// VideoPlayer itself running.
    /// </remarks>
    private void HideFallbackVideoSurface()
    {
        if (videoPlayer == null || backgroundImage == null) return;
        try
        {
            // The component may sit on the VideoPlayer or on a child of it, so
            // look for both rather than assuming. Says what it found either way:
            // "no fallback exists" and "the fallback is still on" need different
            // fixes and are indistinguishable from silence.
            var fallback = videoPlayer.GetComponent<RawImage>()
                ?? videoPlayer.GetComponentInChildren<RawImage>(true);
            if (fallback == null)
            {
                Debug.LogWarning("[Background] No RawImage on the VideoPlayer to disable; " +
                    $"the fallback video surface is somewhere else (player='{videoPlayer.name}').");
                return;
            }
            if (fallback == backgroundImage || fallback == videoBackdrop || fallback == songImage)
            {
                Debug.LogWarning($"[Background] The VideoPlayer's RawImage is '{fallback.name}', " +
                    "which is a display we rely on; leaving it alone.");
                return;
            }
            if (!fallback.enabled) return;
            fallback.enabled = false;
            Debug.Log($"[Background] Disabled fallback video surface '{fallback.name}'; " +
                "backgroundImage is the display and the fallback was never positioned.");
        }
        catch { }
    }

    private void ShowSingleBackground(Texture2D texture)
    {
        // Backwards-compatible helper: show texture on the background image
        ShowImageOnRawImage(backgroundImage, texture);
    }

    // Show a Texture2D on the given RawImage and apply sizing/diagnostics.
    private void ShowImageOnRawImage(RawImage img, Texture2D texture)
    {
        if (img == null) return;
        try
        {
            // If this is the songImage, ensure its RectTransform anchors/offsets are the original ones
            if (img == songImage)
            {
                RestoreSongImageRect();
            }
            img.texture = texture;
            img.uvRect = new Rect(0f, 0f, 1f, 1f);
            img.gameObject.SetActive(true);
            img.color = Color.white;
            if (img == songImage) ApplyCoverAspect(texture);
            // Bring to front temporarily for diagnostics
            img.transform.SetAsLastSibling();
            var rt = img.GetComponent<RectTransform>();
            string targetName = (img == songImage ? "songImage" : (img == backgroundImage ? "backgroundImage" : img.gameObject.name));
            BuildLogger.Log($"GameBackgroundManager: ShowImageOnRawImage -> target={targetName}, rect={rt.rect.width}x{rt.rect.height}, parent={(img.transform.parent!=null?img.transform.parent.name:"null")}");

            // Detailed diagnostics: path, instance id, active state, sibling index
            string path = GetHierarchyPath(img.transform);
            BuildLogger.Log($"GameBackgroundManager: Target path={path}, instanceId={img.gameObject.GetInstanceID()}, activeInHierarchy={img.gameObject.activeInHierarchy}, siblingIndex={img.transform.GetSiblingIndex()}");

            var c = img.canvas;
            if (c != null)
            {
                BuildLogger.Log($"GameBackgroundManager: Canvas for target name={c.gameObject.name} renderMode={c.renderMode} sortingOrder={c.sortingOrder} overrideSorting={c.overrideSorting}");
            }

            // Print all GameBackgroundManager instances to detect duplicates
            var allBgManagers = FindObjectsByType<GameBackgroundManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allBgManagers.Length; i++)
            {
                var gm = allBgManagers[i];
                BuildLogger.Log($"GameBackgroundManager: Scene instance[{i}] -> go={GetHierarchyPath(gm.transform)} instanceId={gm.gameObject.GetInstanceID()} songImageRef={(gm.songImage!=null?gm.songImage.gameObject.GetInstanceID():-1)} backgroundImageRef={(gm.backgroundImage!=null?gm.backgroundImage.gameObject.GetInstanceID():-1)}");
            }

            // Print other RawImage objects that currently have textures (helps find other background-like images)
            var allRaw = FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allRaw.Length; i++)
            {
                var r = allRaw[i];
                if (r == null) continue;
                if (r.texture != null)
                {
                    BuildLogger.Log($"GameBackgroundManager: RawImage[{i}] path={GetHierarchyPath(r.transform)} instanceId={r.gameObject.GetInstanceID()} texName={(r.texture!=null?r.texture.name:"null")} texEqualsAssigned={(r.texture==texture)} activeInHierarchy={r.gameObject.activeInHierarchy} siblingIndex={r.transform.GetSiblingIndex()}");
                }
            }
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: ShowImageOnRawImage failed: {ex.Message}");
            }

        // Adjust sizing for whichever image we updated
        AdjustImageSize(img);
    }

    // Adjust a RawImage's RectTransform and AspectRatioFitter so it fills its parent correctly.
    private void AdjustImageSize(RawImage img)
    {
        if (img == null || img.texture == null) return;
        RectTransform rectTransform = img.GetComponent<RectTransform>();

        // If this is the songImage (chart-area artwork), do NOT stretch it to fill the parent.
        // Instead keep its existing RectTransform size and use an AspectRatioFitter with
        // FitInParent so the texture scales within the original rect without altering anchors.
        if (img == songImage)
        {
            // Any AspectRatioFitter attached here will keep forcing anchors/offsets to stretch,
            // which is exactly what we're guarding against, so remove it at runtime.
            var existingFitter = img.GetComponent<UnityEngine.UI.AspectRatioFitter>();
            if (existingFitter != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existingFitter);
                }
                else
                {
                    DestroyImmediate(existingFitter);
                }
            }

            img.raycastTarget = false;
            // The frame carries the aspect now, and the artwork simply fills it,
            // so a fitter here would only fight the frame.
            ApplyCoverAspect(img.texture);
            return;
        }

        // For backgroundImage and other images, stretch to fill parent (keep previous behaviour)
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        var arfBg = img.GetComponent<UnityEngine.UI.AspectRatioFitter>();
        if (arfBg == null)
        {
            arfBg = img.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
        }
        try
        {
            arfBg.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
            arfBg.aspectRatio = (img.texture.width > 0 && img.texture.height > 0)
                ? (float)img.texture.width / (float)img.texture.height
                : 1f;
        }
        catch { }

        img.raycastTarget = false;
    }

    private System.Collections.IEnumerator RefreshImageLayoutNextFrame(RawImage img)
    {
        // Wait end of frame to allow UI layout to settle, then adjust size again.
        yield return new WaitForEndOfFrame();
        yield return null; // one more frame for safety
        try
        {
            AdjustImageSize(img);
            var rt = img.GetComponent<RectTransform>();
            BuildLogger.Log($"GameBackgroundManager: RefreshImageLayoutNextFrame -> target={(img==songImage?"songImage":"backgroundImage")}, rect={rt.rect.width}x{rt.rect.height}");
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: RefreshImageLayoutNextFrame failed: {ex.Message}");
            }
    }

    private void CacheSongImageRect()
    {
        if (songImage == null) return;
        try
        {
            var rt = _songRectTransform ?? songImage.GetComponent<RectTransform>();
            _songAnchorMin = rt.anchorMin;
            _songAnchorMax = rt.anchorMax;
            _songPivot = rt.pivot;
            _songOffsetMin = rt.offsetMin;
            _songOffsetMax = rt.offsetMax;
            _songAnchoredPosition = rt.anchoredPosition;
            _songSizeDelta = rt.sizeDelta;
            _songRectCached = true;
            BuildLogger.Log($"GameBackgroundManager: Cached songImage rect anchors({_songAnchorMin}/{_songAnchorMax}) offsets({_songOffsetMin}/{_songOffsetMax})");
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: CacheSongImageRect failed: {ex.Message}");
            }
    }

#if UNITY_EDITOR
    // Editor-time helper: capture current inspector values into serialized fields so they persist into Play mode
    private void OnValidate()
    {
        // Only run when the target is assigned
        if (songImage != null)
        {
            try
            {
                var rt = songImage.GetComponent<RectTransform>();
                serializedSongAnchorMin = rt.anchorMin;
                serializedSongAnchorMax = rt.anchorMax;
                serializedSongPivot = rt.pivot;
                serializedSongOffsetMin = rt.offsetMin;
                serializedSongOffsetMax = rt.offsetMax;
                serializedSongAnchoredPosition = rt.anchoredPosition;
                serializedSongSizeDelta = rt.sizeDelta;
                serializedSongRectAvailable = true;
            }
            catch { }
        }

        if (!Application.isPlaying)
        {
            ApplyMaskPreferences();
        }
    }

    [UnityEngine.ContextMenu("Capture SongImage Rect (Editor)")]
    private void CaptureSongImageRectEditor()
    {
        if (songImage == null) return;
        var rt = songImage.GetComponent<RectTransform>();
        serializedSongAnchorMin = rt.anchorMin;
        serializedSongAnchorMax = rt.anchorMax;
        serializedSongPivot = rt.pivot;
        serializedSongOffsetMin = rt.offsetMin;
        serializedSongOffsetMax = rt.offsetMax;
        serializedSongAnchoredPosition = rt.anchoredPosition;
        serializedSongSizeDelta = rt.sizeDelta;
        serializedSongRectAvailable = true;
        BuildLogger.Log("GameBackgroundManager: Captured songImage rect into serialized fields (editor).");
    }
#endif

    private void RestoreSongImageRect()
    {
        if (songImage == null || !_songRectCached) return;
        try
        {
            var rt = _songRectTransform ?? songImage.GetComponent<RectTransform>();
            rt.anchorMin = _songAnchorMin;
            rt.anchorMax = _songAnchorMax;
            rt.pivot = _songPivot;
            rt.offsetMin = _songOffsetMin;
            rt.offsetMax = _songOffsetMax;
            rt.anchoredPosition = _songAnchoredPosition;
            rt.sizeDelta = _songSizeDelta;
            BuildLogger.Log("GameBackgroundManager: Restored songImage rect to cached values");
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: RestoreSongImageRect failed: {ex.Message}");
            }
    }

    private bool SongRectMatchesCache(RectTransform rt, out string diffSummary)
    {
        diffSummary = string.Empty;
        if (rt == null || !_songRectCached) return true;

        System.Text.StringBuilder sb = null;

        void Record(string label, Vector2 current, Vector2 target)
        {
            if (ApproximatelyVec2(current, target)) return;
            sb ??= new System.Text.StringBuilder();
            sb.Append(label).Append(": cur=").Append(current).Append(" target=").Append(target).Append("; ");
        }

        Record("anchorMin", rt.anchorMin, _songAnchorMin);
        Record("anchorMax", rt.anchorMax, _songAnchorMax);
        Record("offsetMin", rt.offsetMin, _songOffsetMin);
        Record("offsetMax", rt.offsetMax, _songOffsetMax);
        Record("anchoredPos", rt.anchoredPosition, _songAnchoredPosition);
        Record("sizeDelta", rt.sizeDelta, _songSizeDelta);
        Record("pivot", rt.pivot, _songPivot);

        if (sb != null)
        {
            diffSummary = sb.ToString();
            return false;
        }

        return true;
    }

    private bool ApproximatelyVec2(Vector2 a, Vector2 b)
    {
        return (a - b).sqrMagnitude <= 0.001f;
    }

    private void GuardSongImageRect()
    {
        if (!isActiveAndEnabled || !lockSongImageRectDuringPlay || _songRectTransform == null || !_songRectCached) return;
        if (!SongRectMatchesCache(_songRectTransform, out var diffSummary))
        {
                if (logSongImageRectChanges)
                {
                    BuildLogger.LogWarning($"GameBackgroundManager: Canvas hook detected songImage rect drift -> {diffSummary}; restoring.");
                }
            RestoreSongImageRect();
        }
    }

    private void EnsureSongImageLayoutIgnored()
    {
        if (songImage == null) return;
        try
        {
            _songLayoutElement = songImage.GetComponent<UnityEngine.UI.LayoutElement>();
            if (_songLayoutElement == null)
            {
                _songLayoutElement = songImage.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            }
            _songLayoutElement.ignoreLayout = true;
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: EnsureSongImageLayoutIgnored failed: {ex.Message}");
            }
    }

    private void DetachSongImageFromLayouts()
    {
        if (songImage == null) return;
        try
        {
            _originalSongImageParent = songImage.transform.parent;
            _originalSongImageSiblingIndex = songImage.transform.GetSiblingIndex();
            var rootCanvas = _songRectTransform != null ? (_songRectTransform.GetComponentInParent<Canvas>()?.rootCanvas) : null;
            Transform targetParent = rootCanvas != null ? rootCanvas.transform : transform;

            if (_runtimeSongImageParent == null)
            {
                var go = new GameObject("SongImage_RuntimeContainer", typeof(RectTransform));
                _runtimeSongImageParent = go.GetComponent<RectTransform>();
                _runtimeSongImageParent.SetParent(targetParent, false);
                _runtimeSongImageParent.anchorMin = Vector2.zero;
                _runtimeSongImageParent.anchorMax = Vector2.one;
                _runtimeSongImageParent.offsetMin = Vector2.zero;
                _runtimeSongImageParent.offsetMax = Vector2.zero;
                _runtimeSongImageParent.pivot = new Vector2(0.5f, 0.5f);
                _runtimeSongImageParent.localScale = Vector3.one;
                _runtimeSongImageParent.SetSiblingIndex(targetParent.childCount - 1);
            }
            else
            {
                _runtimeSongImageParent.SetParent(targetParent, false);
            }

            // The cover is the topmost element of the song-info composition.
            // Marquee text stays foreground HUD, but visually passes behind the image.
            if (_songImageFrontCanvas == null)
            {
                _songImageFrontCanvas = _runtimeSongImageParent.GetComponent<Canvas>();
                if (_songImageFrontCanvas == null)
                    _songImageFrontCanvas = _runtimeSongImageParent.gameObject.AddComponent<Canvas>();
            }
            _songImageFrontCanvas.overrideSorting = true;
            _songImageFrontCanvas.sortingLayerID = rootCanvas != null ? rootCanvas.sortingLayerID : 0;
            _songImageFrontCanvas.sortingOrder = 23992;

            songImage.transform.SetParent(_runtimeSongImageParent, false);
            songImage.raycastTarget = false;
            Outline coverOutline = songImage.GetComponent<Outline>();
            if (coverOutline == null) coverOutline = songImage.gameObject.AddComponent<Outline>();
            coverOutline.effectColor = new Color(0.80f, 0.62f, 0.28f, 0.92f);
            coverOutline.effectDistance = new Vector2(3f, -3f);
            coverOutline.useGraphicAlpha = true;
            BuildLogger.Log("GameBackgroundManager: Detached songImage to runtime container to avoid layout interference.");
        }
            catch (System.Exception ex)
            {
                BuildLogger.LogWarning($"GameBackgroundManager: DetachSongImageFromLayouts failed: {ex.Message}");
            }
    }


    private void DisplaySplitBackground(Texture2D texture)
    {
        // Split 背景已移除，直接退回單一背景顯示
        ShowSingleBackground(texture);
    }

    // Convenience API: external 工具可以呼叫這個方法把曲譜/繪圖結果放到 songImage 上
    public void SetSongTexture(Texture2D texture)
    {
        if (songImage == null) return;
        songImage.texture = texture;
        songImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        songImage.gameObject.SetActive(true);
    }

    public void ClearSongTexture()
    {
        if (songImage == null) return;
        songImage.texture = null;
        songImage.gameObject.SetActive(false);
    }

    // 設定顯示的曲名與作者（可以為 null 或空字串以清除）
    public void SetSongInfo(string title, string author)
    {
        if (songTitleText != null)
        {
            SetSongTitle(title);
        }

        if (songAuthorText != null)
        {
            SetSongAuthor(author);
        }
    }

    /// <summary>
    /// Controls the detached foreground HUD as one unit. Selection and difficulty
    /// screens live outside GameplayRoot, so leaving these canvases enabled would
    /// cover the carousel after returning from the first song.
    /// </summary>
    public void SetGameplayHudPresentationVisible(bool visible)
    {
        _gameplayHudPresentationVisible = visible;
        try { Judgment.JudgmentManager.Instance?.SetClassicalHudVisible(visible); } catch { }

        if (_classicalSongInfoOverlay != null)
            _classicalSongInfoOverlay.gameObject.SetActive(visible);

        if (_runtimeSongImageParent != null)
            _runtimeSongImageParent.gameObject.SetActive(visible);

        if (_songTitleViewport != null)
            _songTitleViewport.gameObject.SetActive(visible && !string.IsNullOrEmpty(_songTitleSource));

        if (_songAuthorViewport != null)
            _songAuthorViewport.gameObject.SetActive(visible && !string.IsNullOrEmpty(_songAuthorSource));

        if (_difficultyViewport != null)
        {
            bool hasDifficulty = gameplayDifficultyText != null &&
                                 !string.IsNullOrEmpty(gameplayDifficultyText.text);
            _difficultyViewport.gameObject.SetActive(visible && hasDifficulty);
        }
    }

    private void EnsureSongTitlePresentation()
    {
        if (songTitleText == null || _songTitleViewport != null) return;

        _songTitleRect = songTitleText.rectTransform;
        Transform originalParent = _songTitleRect.parent;
        if (originalParent == null) return;

        int originalIndex = _songTitleRect.GetSiblingIndex();
        _songTitleOriginalSize = _songTitleRect.sizeDelta;
        _songTitleOriginalAlignment = songTitleText.alignment;

        var viewportObject = new GameObject("SongTitle_ForegroundMarquee",
            typeof(RectTransform), typeof(Canvas), typeof(RectMask2D));
        viewportObject.layer = songTitleText.gameObject.layer;
        _songTitleViewport = viewportObject.GetComponent<RectTransform>();
        _songTitleViewport.SetParent(originalParent, false);
        _songTitleViewport.anchorMin = _songTitleRect.anchorMin;
        _songTitleViewport.anchorMax = _songTitleRect.anchorMax;
        _songTitleViewport.pivot = _songTitleRect.pivot;
        _songTitleViewport.anchoredPosition = _songTitleRect.anchoredPosition;
        _songTitleViewport.sizeDelta = _songTitleRect.sizeDelta;
        _songTitleViewport.localRotation = _songTitleRect.localRotation;
        _songTitleViewport.localScale = _songTitleRect.localScale;
        _songTitleViewport.SetSiblingIndex(originalIndex);

        _songTitleFrontCanvas = viewportObject.GetComponent<Canvas>();
        Canvas rootCanvas = originalParent.GetComponentInParent<Canvas>();
        _songTitleFrontCanvas.overrideSorting = true;
        _songTitleFrontCanvas.sortingLayerID = rootCanvas != null ? rootCanvas.sortingLayerID : 0;
        _songTitleFrontCanvas.sortingOrder = 23991;

        var mask = viewportObject.GetComponent<RectMask2D>();
        mask.padding = new Vector4(2f, 1f, 2f, 1f);
        mask.softness = new Vector2Int(8, 0);

        _songTitleRect.SetParent(_songTitleViewport, false);
        _songTitleRect.localRotation = Quaternion.identity;
        _songTitleRect.localScale = Vector3.one;
        songTitleText.maskable = true;
        songTitleText.raycastTarget = false;
        songTitleText.textWrappingMode = TextWrappingModes.NoWrap;
        songTitleText.overflowMode = TextOverflowModes.Masking;
        if (songTitleText is TextMeshProUGUI titleUi)
        {
            ClassicalBookUITheme.StyleText(titleUi, new Color(0.18f, 0.105f, 0.06f, 1f), 23f, FontStyles.Bold);
            titleUi.characterSpacing = 0f;
            titleUi.wordSpacing = 0f;
            titleUi.enableAutoSizing = true;
            titleUi.fontSizeMin = 15f;
            titleUi.fontSizeMax = 23f;
            titleUi.outlineWidth = 0.12f;
            titleUi.outlineColor = new Color(0.10f, 0.045f, 0.025f, 0.90f);
        }
        _songTitleViewport.SetAsLastSibling();
    }

    private void SetSongTitle(string title)
    {
        EnsureSongTitlePresentation();
        _songTitleSource = string.IsNullOrEmpty(title) ? string.Empty : title;
        bool hasTitle = !string.IsNullOrEmpty(_songTitleSource);
        songTitleText.gameObject.SetActive(hasTitle);
        if (_songTitleViewport != null)
            _songTitleViewport.gameObject.SetActive(_gameplayHudPresentationVisible && hasTitle);
        if (!hasTitle)
        {
            songTitleText.text = string.Empty;
            _songTitleMarqueeActive = false;
            return;
        }

        _songTitleMarqueeActive = DoesSongInfoTextOverflow(
            songTitleText,
            _songTitleViewport,
            _songTitleRect,
            _songTitleOriginalSize,
            _songTitleOriginalAlignment,
            _songTitleSource);
        _songTitleMarqueeOffset = 0f;
        _songTitleMarqueeElapsed = 0f;

        if (!_songTitleMarqueeActive)
        {
            songTitleText.text = _songTitleSource;
            songTitleText.alignment = _songTitleOriginalAlignment;
            _songTitleViewport.sizeDelta = _songTitleOriginalSize;
            _songTitleRect.anchorMin = Vector2.zero;
            _songTitleRect.anchorMax = Vector2.one;
            _songTitleRect.pivot = new Vector2(0.5f, 0.5f);
            _songTitleRect.anchoredPosition = new Vector2(5f, 0f);
            _songTitleRect.sizeDelta = new Vector2(-10f, 0f);
            return;
        }

        const string marqueeGap = "      ";

        songTitleText.text = _songTitleSource + marqueeGap + _songTitleSource;
        songTitleText.alignment = TextAlignmentOptions.MidlineLeft;
        songTitleText.ForceMeshUpdate();

        // Keep the authored HUD cell exactly as-is. RectMask2D clips all marquee
        // content to this original rectangle, regardless of title length.
        _songTitleViewport.sizeDelta = _songTitleOriginalSize;

        float fullWidth = songTitleText.GetPreferredValues(songTitleText.text).x;
        _songTitleMarqueeLoopWidth = Mathf.Max(1f,
            songTitleText.GetPreferredValues(_songTitleSource + marqueeGap).x);
        _songTitleRect.anchorMin = new Vector2(0f, 0f);
        _songTitleRect.anchorMax = new Vector2(0f, 1f);
        _songTitleRect.pivot = new Vector2(0f, 0.5f);
        _songTitleRect.anchoredPosition = new Vector2(5f, 0f);
        _songTitleRect.sizeDelta = new Vector2(fullWidth + 8f, 0f);
    }

    private void UpdateSongTitleMarquee()
    {
        if (!_songTitleMarqueeActive || _songTitleRect == null || !songTitleText.gameObject.activeInHierarchy) return;

        _songTitleMarqueeElapsed += Time.unscaledDeltaTime;
        if (_songTitleMarqueeElapsed < songTitleMarqueeStartDelay) return;

        _songTitleMarqueeOffset += songTitleMarqueeSpeed * Time.unscaledDeltaTime;
        if (_songTitleMarqueeOffset >= _songTitleMarqueeLoopWidth)
        {
            _songTitleMarqueeOffset = 0f;
            _songTitleMarqueeElapsed = 0f;
        }

        Vector2 position = _songTitleRect.anchoredPosition;
        position.x = 5f - _songTitleMarqueeOffset;
        _songTitleRect.anchoredPosition = position;
    }

    private void EnsureSongAuthorPresentation()
    {
        if (songAuthorText == null || _songAuthorViewport != null) return;

        _songAuthorRect = songAuthorText.rectTransform;
        Transform originalParent = _songAuthorRect.parent;
        if (originalParent == null) return;

        int originalIndex = _songAuthorRect.GetSiblingIndex();
        _songAuthorOriginalSize = _songAuthorRect.sizeDelta;
        _songAuthorOriginalAlignment = songAuthorText.alignment;

        var viewportObject = new GameObject("SongAuthor_ForegroundMarquee",
            typeof(RectTransform), typeof(Canvas), typeof(RectMask2D));
        viewportObject.layer = songAuthorText.gameObject.layer;
        _songAuthorViewport = viewportObject.GetComponent<RectTransform>();
        _songAuthorViewport.SetParent(originalParent, false);
        _songAuthorViewport.anchorMin = _songAuthorRect.anchorMin;
        _songAuthorViewport.anchorMax = _songAuthorRect.anchorMax;
        _songAuthorViewport.pivot = _songAuthorRect.pivot;
        _songAuthorViewport.anchoredPosition = _songAuthorRect.anchoredPosition;
        _songAuthorViewport.sizeDelta = _songAuthorRect.sizeDelta;
        _songAuthorViewport.localRotation = _songAuthorRect.localRotation;
        _songAuthorViewport.localScale = _songAuthorRect.localScale;
        _songAuthorViewport.SetSiblingIndex(originalIndex);

        _songAuthorFrontCanvas = viewportObject.GetComponent<Canvas>();
        Canvas rootCanvas = originalParent.GetComponentInParent<Canvas>();
        _songAuthorFrontCanvas.overrideSorting = true;
        _songAuthorFrontCanvas.sortingLayerID = rootCanvas != null ? rootCanvas.sortingLayerID : 0;
        _songAuthorFrontCanvas.sortingOrder = 23990;

        var mask = viewportObject.GetComponent<RectMask2D>();
        mask.padding = new Vector4(2f, 1f, 2f, 1f);
        mask.softness = new Vector2Int(8, 0);

        _songAuthorRect.SetParent(_songAuthorViewport, false);
        _songAuthorRect.localRotation = Quaternion.identity;
        _songAuthorRect.localScale = Vector3.one;
        songAuthorText.maskable = true;
        songAuthorText.raycastTarget = false;
        songAuthorText.textWrappingMode = TextWrappingModes.NoWrap;
        songAuthorText.overflowMode = TextOverflowModes.Masking;
        if (songAuthorText is TextMeshProUGUI authorUi)
        {
            ClassicalBookUITheme.StyleText(authorUi, new Color(0.27f, 0.17f, 0.10f, 1f), 17f, FontStyles.Italic);
            authorUi.characterSpacing = 0f;
            authorUi.wordSpacing = 0f;
            authorUi.enableAutoSizing = true;
            authorUi.fontSizeMin = 13f;
            authorUi.fontSizeMax = 17f;
            authorUi.outlineWidth = 0.10f;
            authorUi.outlineColor = new Color(0.10f, 0.045f, 0.025f, 0.86f);
        }
        _songAuthorViewport.SetAsLastSibling();
    }

    private void SetSongAuthor(string author)
    {
        EnsureSongAuthorPresentation();
        _songAuthorSource = string.IsNullOrEmpty(author) ? string.Empty : author;
        bool hasAuthor = !string.IsNullOrEmpty(_songAuthorSource);
        songAuthorText.gameObject.SetActive(hasAuthor);
        if (_songAuthorViewport != null)
            _songAuthorViewport.gameObject.SetActive(_gameplayHudPresentationVisible && hasAuthor);
        if (!hasAuthor)
        {
            songAuthorText.text = string.Empty;
            _songAuthorMarqueeActive = false;
            return;
        }

        _songAuthorMarqueeActive = DoesSongInfoTextOverflow(
            songAuthorText,
            _songAuthorViewport,
            _songAuthorRect,
            _songAuthorOriginalSize,
            _songAuthorOriginalAlignment,
            _songAuthorSource);
        _songAuthorMarqueeOffset = 0f;
        _songAuthorMarqueeElapsed = 0f;

        if (!_songAuthorMarqueeActive)
        {
            songAuthorText.text = _songAuthorSource;
            songAuthorText.alignment = _songAuthorOriginalAlignment;
            _songAuthorViewport.sizeDelta = _songAuthorOriginalSize;
            _songAuthorRect.anchorMin = Vector2.zero;
            _songAuthorRect.anchorMax = Vector2.one;
            _songAuthorRect.pivot = new Vector2(0.5f, 0.5f);
            _songAuthorRect.anchoredPosition = new Vector2(5f, 0f);
            _songAuthorRect.sizeDelta = new Vector2(-10f, 0f);
            return;
        }

        const string marqueeGap = "      ";

        songAuthorText.text = _songAuthorSource + marqueeGap + _songAuthorSource;
        songAuthorText.alignment = TextAlignmentOptions.MidlineLeft;
        songAuthorText.ForceMeshUpdate();

        _songAuthorViewport.sizeDelta = _songAuthorOriginalSize;

        float fullWidth = songAuthorText.GetPreferredValues(songAuthorText.text).x;
        _songAuthorMarqueeLoopWidth = Mathf.Max(1f,
            songAuthorText.GetPreferredValues(_songAuthorSource + marqueeGap).x);
        _songAuthorRect.anchorMin = new Vector2(0f, 0f);
        _songAuthorRect.anchorMax = new Vector2(0f, 1f);
        _songAuthorRect.pivot = new Vector2(0f, 0.5f);
        _songAuthorRect.anchoredPosition = new Vector2(5f, 0f);
        _songAuthorRect.sizeDelta = new Vector2(fullWidth + 8f, 0f);
    }

    private void UpdateSongAuthorMarquee()
    {
        if (!_songAuthorMarqueeActive || _songAuthorRect == null || !songAuthorText.gameObject.activeInHierarchy) return;

        _songAuthorMarqueeElapsed += Time.unscaledDeltaTime;
        if (_songAuthorMarqueeElapsed < songTitleMarqueeStartDelay) return;

        _songAuthorMarqueeOffset += songTitleMarqueeSpeed * Time.unscaledDeltaTime;
        if (_songAuthorMarqueeOffset >= _songAuthorMarqueeLoopWidth)
        {
            _songAuthorMarqueeOffset = 0f;
            _songAuthorMarqueeElapsed = 0f;
        }

        Vector2 position = _songAuthorRect.anchoredPosition;
        position.x = 5f - _songAuthorMarqueeOffset;
        _songAuthorRect.anchoredPosition = position;
    }

    private static bool DoesSongInfoTextOverflow(
        TMP_Text text,
        RectTransform viewport,
        RectTransform textRect,
        Vector2 viewportSize,
        TextAlignmentOptions alignment,
        string value)
    {
        if (text == null || viewport == null || textRect == null || string.IsNullOrEmpty(value))
            return false;

        // A previous song may have left this RectTransform expanded for its
        // duplicated marquee text. Measure every new value in the real HUD cell.
        viewport.sizeDelta = viewportSize;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        text.alignment = alignment;
        text.text = value;
        text.ForceMeshUpdate(true, true);

        float availableWidth = viewport.rect.width;
        if (availableWidth <= 1f)
            availableWidth = viewportSize.x;

        // textBounds reflects the font fallback, character widths and the final
        // auto-sized font. The small inset matches RectMask2D's horizontal padding.
        float renderedWidth = text.textBounds.size.x;
        return text.isTextOverflowing || renderedWidth > Mathf.Max(1f, availableWidth - 14f);
    }

    private void EnsureDifficultyPresentation()
    {
        if (gameplayDifficultyText == null || _difficultyViewport != null) return;

        _difficultyRect = gameplayDifficultyText.rectTransform;
        Transform originalParent = _difficultyRect.parent;
        if (originalParent == null) return;

        // Anchor against the actual HUD root. A foreground Canvas nested under an
        // arbitrary layout/viewport inherits that parent's coordinate space and
        // can appear on the left at other aspect ratios.
        Canvas parentCanvas = originalParent.GetComponentInParent<Canvas>();
        Canvas rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
        Transform presentationParent = rootCanvas != null ? rootCanvas.transform : originalParent;

        var viewportObject = new GameObject("Difficulty_Foreground",
            typeof(RectTransform), typeof(Canvas));
        viewportObject.layer = gameplayDifficultyText.gameObject.layer;
        _difficultyViewport = viewportObject.GetComponent<RectTransform>();
        _difficultyViewport.SetParent(presentationParent, false);

        // Place difficulty directly under the cover and align their left edges.
        // The cover may live in its own foreground container, but both containers
        // stretch across the same root Canvas, so its anchored rect is reusable.
        RectTransform coverRect = _songRectTransform != null
            ? _songRectTransform
            : (songImage != null ? songImage.rectTransform : null);
        Vector2 coverAnchor = coverRect != null ? coverRect.anchorMin : new Vector2(0f, 1f);
        Vector2 coverPosition = coverRect != null ? coverRect.anchoredPosition : new Vector2(75f, -200f);
        Vector2 coverSize = coverRect != null ? coverRect.rect.size : new Vector2(150f, 150f);
        Vector2 coverPivot = coverRect != null ? coverRect.pivot : new Vector2(0.5f, 0.5f);
        float coverLeft = coverPosition.x - coverSize.x * coverPivot.x;
        float coverBottom = coverPosition.y - coverSize.y * coverPivot.y;

        _difficultyViewport.anchorMin = coverAnchor;
        _difficultyViewport.anchorMax = coverAnchor;
        _difficultyViewport.pivot = new Vector2(0f, 1f);
        _difficultyViewport.anchoredPosition = new Vector2(coverLeft, coverBottom - 10f);
        _difficultyViewport.sizeDelta = new Vector2(
            Mathf.Max(320f, _difficultyRect.sizeDelta.x),
            Mathf.Max(50f, _difficultyRect.sizeDelta.y));
        _difficultyViewport.localRotation = Quaternion.identity;
        _difficultyViewport.localScale = Vector3.one;

        _difficultyFrontCanvas = viewportObject.GetComponent<Canvas>();
        _difficultyFrontCanvas.overrideSorting = true;
        _difficultyFrontCanvas.sortingLayerID = rootCanvas != null ? rootCanvas.sortingLayerID : 0;
        _difficultyFrontCanvas.sortingOrder = 23989;

        _difficultyRect.SetParent(_difficultyViewport, false);
        _difficultyRect.anchorMin = Vector2.zero;
        _difficultyRect.anchorMax = Vector2.one;
        _difficultyRect.pivot = new Vector2(0.5f, 0.5f);
        _difficultyRect.anchoredPosition = new Vector2(5f, 0f);
        _difficultyRect.sizeDelta = new Vector2(-10f, 0f);
        _difficultyRect.localRotation = Quaternion.identity;
        _difficultyRect.localScale = Vector3.one;
        gameplayDifficultyText.alignment = TextAlignmentOptions.MidlineLeft;
        gameplayDifficultyText.textWrappingMode = TextWrappingModes.NoWrap;
        gameplayDifficultyText.overflowMode = TextOverflowModes.Masking;
        gameplayDifficultyText.maskable = true;
        gameplayDifficultyText.raycastTarget = false;
        if (gameplayDifficultyText is TextMeshProUGUI difficultyUi)
        {
            ClassicalBookUITheme.StyleText(difficultyUi, new Color(0.31f, 0.18f, 0.09f, 1f), 18f, FontStyles.Bold);
            difficultyUi.characterSpacing = 0f;
            difficultyUi.wordSpacing = 0f;
            difficultyUi.enableAutoSizing = true;
            difficultyUi.fontSizeMin = 13f;
            difficultyUi.fontSizeMax = 18f;
            difficultyUi.outlineWidth = 0.10f;
            difficultyUi.outlineColor = new Color(0.10f, 0.045f, 0.025f, 0.84f);
        }
        _difficultyViewport.SetAsLastSibling();
    }

    // 嘗試從單一字串中解析出 cover 路徑、曲名與作者
    // 支援常見分隔符號：'|'、';'、"\n"、" - "。若其中某一欄看起來像 Resources 路徑 (包含 '/'), 會視為 cover 路徑，並嘗試 Resources.Load 檢查存在性。
    private bool TryParseSongTitleString(string src, out string coverPath, out string title, out string author)
    {
        coverPath = null; title = null; author = null;
        if (string.IsNullOrWhiteSpace(src)) return false;

        // Normalize delimiters
        var parts = src.Split(new char[] {'|', ';'}, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            // Try newline or ' - ' split
            if (src.Contains("\n")) parts = src.Split(new string[] {"\n"}, StringSplitOptions.RemoveEmptyEntries);
            else if (src.Contains(" - ")) parts = src.Split(new string[] {" - "}, StringSplitOptions.RemoveEmptyEntries);
        }

        for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();

        // Heuristics:
        // - If a token contains '/' assume it's a resource path candidate.
        // - If >=3 tokens: [path, title, author]
        // - If 2 tokens: [title, author] or [path, title]
        if (parts.Length >= 3)
        {
            // try detect path token among first two
            if (parts[0].Contains("/")) coverPath = parts[0];
            title = parts[1];
            author = parts[2];
        }
        else if (parts.Length == 2)
        {
            if (parts[0].Contains("/"))
            {
                coverPath = parts[0];
                title = parts[1];
            }
            else
            {
                title = parts[0];
                author = parts[1];
            }
        }
        else if (parts.Length == 1)
        {
            var token = parts[0];
            // if token has slash, consider path; else treat as title
            if (token.Contains("/"))
            {
                coverPath = token;
                // try to infer title from last segment
                int idx = token.LastIndexOf('/');
                if (idx >= 0 && idx + 1 < token.Length) title = token.Substring(idx + 1);
            }
            else
            {
                title = token;
            }
        }

        // Verify coverPath by trying Resources.Load<Texture2D> (without extension)
        if (!string.IsNullOrWhiteSpace(coverPath))
        {
            try
            {
                var tex = Resources.Load<Texture2D>(coverPath);
                if (tex == null)
                {
                    // fallback: if coverPath had extension, strip it and retry
                    var noExt = System.IO.Path.ChangeExtension(coverPath, null);
                    if (!string.Equals(noExt, coverPath, StringComparison.OrdinalIgnoreCase))
                    {
                        tex = Resources.Load<Texture2D>(noExt);
                        if (tex != null) coverPath = noExt;
                    }
                }
                if (tex == null)
                {
                    // if still null, we won't claim success on path
                    coverPath = null;
                }
            }
            catch { coverPath = null; }
        }

        // success if at least title or coverPath parsed
        return !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(coverPath) || !string.IsNullOrWhiteSpace(author);
    }

    public void LoadBackground(string coverPath, string videoPath)
    {
        // Stop any previous video first to ensure overlays and player state are clean
        StopVideoPlayback();

        bool hasVideo = !string.IsNullOrEmpty(videoPath);
        _videoRequestedForTrackDimmer = hasVideo;
        // Always on, in builds too. Whether a video was even asked for is the
        // first fork of every "the video is missing" investigation, and the rest
        // of this path logs through BuildLogger.Log, which release builds strip.
        Debug.Log($"[Background] LoadBackground hasVideo={hasVideo} " +
            $"video='{videoPath}' cover='{coverPath}'");

        if (hasVideo)
        {
            // When a video is requested, prime the scheduled offset from the selected song metadata.
            scheduledVideoStartOffsetSec = perSongVideoStartOffsetSec;
            if (!string.IsNullOrEmpty(coverPath))
            {
                // Ensure song artwork is still visible even when a video background is used
                LoadBackgroundImage(coverPath, skipVideoStateChanges: true);
                if (videoBackdrop != null)
                {
                    // Mirror the cover onto the backdrop so it shows while waiting for video start
                    videoBackdrop.texture = _lastAssignedTexture != null ? _lastAssignedTexture : songImage?.texture;
                    if (videoBackdrop.texture == null)
                    {
                        videoBackdrop.texture = sBlackTexture;
                        videoBackdrop.color = Color.black;
                    }
                    else
                    {
                        videoBackdrop.color = Color.white;
                    }
                    videoBackdrop.gameObject.SetActive(true);
                }
            }
            // Start video playback and enable overlay
            PlayBackgroundVideo(videoPath);
            AdjustVideoPlayerSize(); // Adjust video size after loading
            SetVideoOverlay(true);
            UpdateTrackDimmer(true, true);
        }
        else if (!string.IsNullOrEmpty(coverPath))
        {
            // Show static cover image and ensure overlay is disabled
            LoadBackgroundImage(coverPath);
            AdjustBackgroundSize(); // Adjust image size after loading
            SetVideoOverlay(false);
            UpdateTrackDimmer(false, true);
        }
        else
        {
            BuildLogger.LogWarning("GameBackgroundManager: Both coverPath and videoPath are empty.");
            // Ensure background is black when nothing to show
            try
            {
                if (backgroundImage != null)
                {
                    backgroundImage.texture = sBlackTexture;
                    backgroundImage.color = Color.black;
                    backgroundImage.gameObject.SetActive(true);
                }
            }
            catch { }
            SetVideoOverlay(false);
            UpdateTrackDimmer(false, true);
        }
    }

    // Called when the song finishes to stop video immediately.
    public void OnSongEnded()
    {
        StopVideoPlayback();
    }

    // Called from song selection to stop any video preview and show only the cover image.
    public void ShowCoverOnlyFromSelection()
    {
        try
        {
            var selector = SongSelectionManager.Instance ?? FindAnyObjectByType<SongSelectionManager>();
            var selected = selector != null ? selector.GetSelectedSong() : null;
            string cover = selected != null ? selected.coverResourcePath : null;

            StopVideoPlayback();

            if (!string.IsNullOrEmpty(cover))
            {
                LoadBackground(cover, null);
            }
            else
            {
                // No cover; ensure background stays clean black
                SetVideoOverlay(false);
                UpdateTrackDimmer(false, true);
            }
        }
        catch
        {
            StopVideoPlayback();
        }
    }

    private float GetTrackDimmerSetting()
    {
        try
        {
            var settings = SettingsManager.Instance;
            if (settings != null)
            {
                return Mathf.Clamp01(settings.TrackVideoDimmer);
            }
        }
        catch { }
        return 0f;
    }

    private void UpdateTrackDimmer(bool videoActive, bool force = false, float? explicitIntensity = null)
    {
        _videoRequestedForTrackDimmer = videoActive;

        if (!videoActive)
        {
            if (_trackDimmerRetryRoutine != null)
            {
                try { StopCoroutine(_trackDimmerRetryRoutine); } catch { }
                _trackDimmerRetryRoutine = null;
            }
            RestoreTrackDimmerImmediate();
            _currentTrackDimmerApplied = 0f;
            return;
        }

        float target = explicitIntensity.HasValue ? Mathf.Clamp01(explicitIntensity.Value) : GetTrackDimmerSetting();
        BuildLogger.Log($"GameBackgroundManager: UpdateTrackDimmer video={videoActive} force={force} requested={target:F3} current={_currentTrackDimmerApplied:F3}");
        if (!force && Mathf.Approximately(target, _currentTrackDimmerApplied)) return;

        if (!EnsureTrackRendererCache())
        {
            BuildLogger.Log("GameBackgroundManager: Track dimmer cache unavailable; scheduling retry or restoring baseline.");
            ScheduleTrackDimmerRetry(target);
            return;
        }

        bool refreshOriginal = target <= 0.0001f;
        ApplyTrackDimmerToCachedRenderers(target, refreshOriginal);
        BuildLogger.Log($"GameBackgroundManager: Applied track dimmer target={target:F3} refreshOriginal={refreshOriginal} entries={_trackRendererEntries.Count}");
        _currentTrackDimmerApplied = target;
    }

    private bool EnsureTrackRendererCache()
    {
        for (int i = _trackRendererEntries.Count - 1; i >= 0; i--)
        {
            if (_trackRendererEntries[i] == null || !_trackRendererEntries[i].IsValid)
            {
                _trackRendererEntries.RemoveAt(i);
            }
        }

        var trackRoot = TryResolveTrackRoot();

        if (trackRoot == null)
        {
            if (!_loggedMissingTrackRoot)
            {
                BuildLogger.LogWarning("GameBackgroundManager: Track dimmer could not locate a track transform. Assign GameManager.trackTransform or name the track object 'Track'.");
                _loggedMissingTrackRoot = true;
            }
            return _trackRendererEntries.Count > 0;
        }

        _loggedMissingTrackRoot = false;

        if (_cachedTrackRoot != trackRoot)
        {
            RestoreTrackDimmerImmediate();
            _trackRendererEntries.Clear();
            _cachedTrackRoot = trackRoot;
            BuildLogger.Log($"GameBackgroundManager: Track dimmer caching new root {GetHierarchyPath(trackRoot)}");
        }

        if (_trackRendererEntries.Count == 0)
        {
            var renderers = trackRoot.GetComponentsInChildren<Renderer>(true);
            BuildLogger.Log($"GameBackgroundManager: Track dimmer discovered {renderers.Length} renderer(s) under {trackRoot.name}");
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!ShouldIncludeRendererForTrackDimmer(renderer, trackRoot))
                {
                    continue;
                }

                if (renderer is SpriteRenderer spriteRenderer)
                {
                    _trackRendererEntries.Add(new TrackRendererEntry(spriteRenderer));
                    BuildLogger.Log($"GameBackgroundManager: Cached SpriteRenderer {GetHierarchyPath(spriteRenderer.transform)}");
                    continue;
                }

                var sharedMats = renderer.sharedMaterials;
                if (sharedMats == null || sharedMats.Length == 0) continue;

                for (int matIndex = 0; matIndex < sharedMats.Length; matIndex++)
                {
                    var sharedMat = sharedMats[matIndex];
                    if (sharedMat == null) continue;

                    var colorStates = new List<TrackRendererEntry.ColorPropertyState>(3);
                    void TryAddProperty(int propertyId, string propertyName)
                    {
                        if (propertyId < 0) return;
                        if (!sharedMat.HasProperty(propertyId)) return;
                        Color baseColor;
                        try { baseColor = sharedMat.GetColor(propertyId); }
                        catch { baseColor = Color.white; }
                        colorStates.Add(new TrackRendererEntry.ColorPropertyState(propertyId, propertyName, baseColor));
                    }

                    // Prioritise URP/Standard property names; allow shader to expose multiple.
                    TryAddProperty(ShaderBaseColorId, "_BaseColor");
                    TryAddProperty(ShaderColorId, "_Color");
                    TryAddProperty(ShaderTintColorId, "_TintColor");

                    if (colorStates.Count == 0) continue;

                    _trackRendererEntries.Add(new TrackRendererEntry(renderer, matIndex, sharedMat, colorStates));
                    var shaderName = sharedMat != null && sharedMat.shader != null ? sharedMat.shader.name : "<null>";
                    BuildLogger.Log($"GameBackgroundManager: Cached Renderer {GetHierarchyPath(renderer.transform)} materialIndex={matIndex} shader={shaderName} properties={string.Join(",", colorStates.ConvertAll(cs => cs.PropertyName ?? cs.PropertyId.ToString()))}");
                }
            }
            BuildLogger.Log($"GameBackgroundManager: Track dimmer cache entries now {_trackRendererEntries.Count}");
        }

        return _trackRendererEntries.Count > 0;
    }

    // Simple bridge for SettingsManager: apply dimmer using current video state (no-op when video inactive).
    public void ApplyTrackDimmerSetting(float dimStrength)
    {
        // Interpret dimStrength as the desired overlay opacity to dim the video.
        videoOverlayOpacity = Mathf.Clamp01(dimStrength);
        if (_videoRequestedForTrackDimmer) SetVideoOverlay(true);
        UpdateTrackDimmer(_videoRequestedForTrackDimmer, true, Mathf.Clamp01(dimStrength));
    }

    private Transform TryResolveTrackRoot()
    {
        var gm = GameManager.Instance ?? FindAnyObjectByType<GameManager>();
        Transform trackRoot = null;

        if (gm != null)
        {
            trackRoot = gm.trackTransform;
            if (trackRoot == null && gm.gameplayRoot != null)
            {
                try
                {
                    trackRoot = gm.gameplayRoot.transform.Find("Track");
                    if (trackRoot != null)
                    {
                        BuildLogger.Log($"GameBackgroundManager: Found track via gameplayRoot: {GetHierarchyPath(trackRoot)}");
                    }
                }
                catch { trackRoot = null; }
            }
            else if (trackRoot != null)
            {
                BuildLogger.Log($"GameBackgroundManager: Using GameManager.trackTransform -> {GetHierarchyPath(trackRoot)}");
            }
        }

        if (trackRoot == null)
        {
            var spawner = FindAnyObjectByType<NoteSpawner>();
            if (spawner != null && spawner.trackTransform != null)
            {
                trackRoot = spawner.trackTransform;
                BuildLogger.Log($"GameBackgroundManager: Falling back to NoteSpawner.trackTransform -> {GetHierarchyPath(trackRoot)}");
            }
        }

        if (trackRoot == null)
        {
            var beatSpawner = FindAnyObjectByType<BeatLineSpawner>();
            if (beatSpawner != null && beatSpawner.trackTransform != null)
            {
                trackRoot = beatSpawner.trackTransform;
                BuildLogger.Log($"GameBackgroundManager: Falling back to BeatLineSpawner.trackTransform -> {GetHierarchyPath(trackRoot)}");
            }
        }

        if (trackRoot == null)
        {
            try
            {
                var trackObj = GameObject.Find("Track");
                if (trackObj != null)
                {
                    trackRoot = trackObj.transform;
                    BuildLogger.Log($"GameBackgroundManager: Found track via GameObject.Find -> {GetHierarchyPath(trackRoot)}");
                }
            }
            catch { trackRoot = null; }
        }

        return trackRoot;
    }

    private static readonly string[] TrackDimmerExcludeKeywords =
    {
        "note",
        "judge",
        "hit",
        "effect",
        "particle",
        "spark",
        "trail",
        "tail"
    };

    private static readonly string[] TrackDimmerIncludeKeywords =
    {
        "track",
        "floor",
        "lane",
        "board",
        "surface"
    };

    private bool ShouldIncludeRendererForTrackDimmer(Renderer renderer, Transform trackRoot)
    {
        if (renderer == null || trackRoot == null) return false;

        if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
        {
            return false;
        }

        var t = renderer.transform;
        if (t == null) return false;
        if (!t.IsChildOf(trackRoot))
        {
            return false;
        }

        string path = GetHierarchyPath(t);
        if (!string.IsNullOrEmpty(path))
        {
            string lower = path.ToLowerInvariant();
            foreach (var keyword in TrackDimmerExcludeKeywords)
            {
                if (lower.Contains(keyword))
                {
                    return false;
                }
            }
        }

        // Include the track root itself
        if (t == trackRoot)
        {
            return true;
        }

        // Allow any descendant under the track root (not just direct children)
        // as long as the object name suggests it is part of the visible track surface.
        if (!t.IsChildOf(trackRoot))
        {
            return false;
        }

        return HasRenderableSurface(t.name);
    }

    private bool HasRenderableSurface(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return false;

        string lower = objectName.ToLowerInvariant();
        foreach (var keyword in TrackDimmerIncludeKeywords)
        {
            if (lower.Contains(keyword))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyTrackDimmerToCachedRenderers(float intensity, bool refreshOriginalWhenZero)
    {
        for (int i = _trackRendererEntries.Count - 1; i >= 0; i--)
        {
            var entry = _trackRendererEntries[i];
            if (entry == null || !entry.IsValid)
            {
                _trackRendererEntries.RemoveAt(i);
                continue;
            }

            entry.Apply(intensity, refreshOriginalWhenZero && intensity <= 0.0001f);
            entry.LogState(i, intensity);
        }

        ScheduleTrackDimmerVerification(intensity);
    }

    private void ScheduleTrackDimmerVerification(float targetIntensity)
    {
        if (!isActiveAndEnabled) return;
        if (_trackDimmerVerifyRoutine != null)
        {
            try { StopCoroutine(_trackDimmerVerifyRoutine); } catch { }
            _trackDimmerVerifyRoutine = null;
        }
        _trackDimmerVerifyRoutine = StartCoroutine(VerifyTrackDimmerNextFrame(targetIntensity));
    }

    private IEnumerator VerifyTrackDimmerNextFrame(float targetIntensity)
    {
        yield return null;
        if (_trackRendererEntries.Count == 0)
        {
            BuildLogger.Log("GameBackgroundManager: VerifyTrackDimmerNextFrame -> no entries to inspect");
            _trackDimmerVerifyRoutine = null;
            yield break;
        }

        BuildLogger.Log($"GameBackgroundManager: VerifyTrackDimmerNextFrame target={targetIntensity:F3} entries={_trackRendererEntries.Count}");

        for (int i = 0; i < _trackRendererEntries.Count; i++)
        {
            var entry = _trackRendererEntries[i];
            if (entry == null || !entry.IsValid) continue;

            string rendererName = entry.Renderer != null ? entry.Renderer.name : "<null>";
            string spriteName = entry.SpriteRenderer != null ? entry.SpriteRenderer.name : "<null>";
            float spriteAlpha = entry.SpriteRenderer != null ? entry.SpriteRenderer.color.a : -1f;

            if (entry.SpriteRenderer != null)
            {
                BuildLogger.Log($"GameBackgroundManager: verify entry[{i}] renderer={rendererName} sprite={spriteName} spriteAlpha={spriteAlpha:F3} target={targetIntensity:F3} (SpriteRenderer)");
                continue;
            }

            if (entry.Renderer == null || !entry.HasMaterialProperties) continue;

            var pb = entry.PropertyBlock ?? new MaterialPropertyBlock();
            entry.PropertyBlock = pb;
            try
            {
                entry.Renderer.GetPropertyBlock(pb, entry.MaterialIndex);
            }
            catch { }

            Material sharedMat = null;
            try
            {
                var sharedMats = entry.Renderer.sharedMaterials;
                if (sharedMats != null && entry.MaterialIndex >= 0 && entry.MaterialIndex < sharedMats.Length)
                {
                    sharedMat = sharedMats[entry.MaterialIndex];
                }
            }
            catch { sharedMat = null; }

            foreach (var slot in entry.ColorProperties)
            {
                if (slot == null) continue;

                string propertyLabel = slot.PropertyName ?? slot.PropertyId.ToString();

                Color blockColor = Color.clear;
                bool blockAvailable = false;
                if (pb != null)
                {
                    try
                    {
                        blockColor = pb.GetColor(slot.PropertyId);
                        blockAvailable = true;
                    }
                    catch { blockAvailable = false; }
                }

                float sharedAlpha = -1f;
                if (sharedMat != null)
                {
                    try
                    {
                        if (sharedMat.HasProperty(slot.PropertyId))
                        {
                            sharedAlpha = sharedMat.GetColor(slot.PropertyId).a;
                        }
                    }
                    catch { sharedAlpha = -1f; }
                }

                string blockText = blockAvailable ? blockColor.ToString() : "<none>";
                string sharedText = sharedAlpha >= 0f ? sharedAlpha.ToString("F3") : "<n/a>";

                BuildLogger.Log($"GameBackgroundManager: verify entry[{i}] renderer={rendererName} sprite={spriteName} spriteAlpha={spriteAlpha:F3} materialIndex={entry.MaterialIndex} property={propertyLabel} blockColor={blockText} sharedAlpha={sharedText} origAlpha={slot.OriginalColor.a:F3} target={targetIntensity:F3}");
            }
        }

        _trackDimmerVerifyRoutine = null;
    }

    private void RestoreTrackDimmerImmediate()
    {
        if (_trackRendererEntries.Count == 0) return;
        for (int i = _trackRendererEntries.Count - 1; i >= 0; i--)
        {
            var entry = _trackRendererEntries[i];
            if (entry == null || !entry.IsValid)
            {
                _trackRendererEntries.RemoveAt(i);
                continue;
            }
            entry.Apply(0f, true);
        }
        _currentTrackDimmerApplied = 0f;
    }

    private void ScheduleTrackDimmerRetry(float targetIntensity)
    {
        if (_trackDimmerRetryRoutine != null) return;
        _trackDimmerRetryRoutine = StartCoroutine(RetryApplyTrackDimmer(targetIntensity));
    }

    private IEnumerator RetryApplyTrackDimmer(float targetIntensity)
    {
        targetIntensity = Mathf.Clamp01(targetIntensity);
        const int attempts = 10;
        for (int i = 0; i < attempts; i++)
        {
            yield return null;
            if (EnsureTrackRendererCache())
            {
                bool refreshOriginal = targetIntensity <= 0.0001f;
                ApplyTrackDimmerToCachedRenderers(targetIntensity, refreshOriginal);
                _currentTrackDimmerApplied = targetIntensity;
                _trackDimmerRetryRoutine = null;
                yield break;
            }
        }

        _trackDimmerRetryRoutine = null;
    }

    private sealed class TrackRendererEntry
    {
        public sealed class ColorPropertyState
        {
            public int PropertyId { get; }
            public string PropertyName { get; }
            public Color OriginalColor;

            public ColorPropertyState(int propertyId, string propertyName, Color originalColor)
            {
                PropertyId = propertyId;
                PropertyName = propertyName;
                OriginalColor = originalColor;
            }
        }

        public Renderer Renderer;
        public SpriteRenderer SpriteRenderer;
        public int MaterialIndex = -1;
        public MaterialPropertyBlock PropertyBlock;

        public Color SpriteOriginalColor;

        private readonly List<ColorPropertyState> _colorProperties;
        public IReadOnlyList<ColorPropertyState> ColorProperties => _colorProperties;
        public bool HasMaterialProperties => _colorProperties != null && _colorProperties.Count > 0;
        public int PrimaryPropertyId => HasMaterialProperties ? _colorProperties[0].PropertyId : -1;

        private Material _originalSharedMaterial;
        private Material _transparentMaterial;
        private bool _transparentMaterialApplied;
        private int _originalRenderQueue = -1;

        public TrackRendererEntry(SpriteRenderer spriteRenderer)
        {
            Renderer = spriteRenderer;
            SpriteRenderer = spriteRenderer;
            SpriteOriginalColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
            _colorProperties = null;
        }

        public TrackRendererEntry(Renderer renderer, int materialIndex, Material sharedMaterial, List<ColorPropertyState> colorStates)
        {
            Renderer = renderer;
            MaterialIndex = Mathf.Max(materialIndex, 0);
            _colorProperties = colorStates != null ? new List<ColorPropertyState>(colorStates.Count) : new List<ColorPropertyState>();
            _originalSharedMaterial = sharedMaterial;
            _originalRenderQueue = sharedMaterial != null ? sharedMaterial.renderQueue : -1;

            if (colorStates != null)
            {
                foreach (var state in colorStates)
                {
                    if (state == null || state.PropertyId < 0) continue;
                    // Clone the state so cached originals remain stable even if the source list is reused
                    _colorProperties.Add(new ColorPropertyState(state.PropertyId, state.PropertyName, state.OriginalColor));
                }
            }

            if (Renderer != null && HasMaterialProperties)
            {
                try
                {
                    PropertyBlock = new MaterialPropertyBlock();
                    Renderer.GetPropertyBlock(PropertyBlock, MaterialIndex);
                    if (PropertyBlock == null)
                    {
                        PropertyBlock = new MaterialPropertyBlock();
                    }

                    if (sharedMaterial != null)
                    {
                        foreach (var slot in _colorProperties)
                        {
                            try
                            {
                                PropertyBlock.SetColor(slot.PropertyId, slot.OriginalColor);
                            }
                            catch { }
                        }
                    }

                    Renderer.SetPropertyBlock(PropertyBlock, MaterialIndex);
                }
                catch { }
            }
        }

        public bool IsValid => Renderer != null && (SpriteRenderer != null || HasMaterialProperties);

        public void Apply(float intensity, bool refreshOriginal)
        {
            intensity = Mathf.Clamp01(intensity);

            if (SpriteRenderer != null)
            {
                if (intensity <= 0f)
                {
                    SpriteRenderer.color = SpriteOriginalColor;
                    if (refreshOriginal)
                    {
                        SpriteOriginalColor = SpriteRenderer.color;
                    }
                }
                else
                {
                    Color dimmedColor = SpriteOriginalColor;
                    float brightness = Mathf.Lerp(1f, 0.22f, intensity);
                    dimmedColor.r *= brightness;
                    dimmedColor.g *= brightness;
                    dimmedColor.b *= brightness;
                    dimmedColor.a = SpriteOriginalColor.a;
                    SpriteRenderer.color = dimmedColor;
                }
                return;
            }

            if (Renderer == null || !HasMaterialProperties) return;

            // The track is a solid gameplay surface. Video dimming must lower
            // its brightness rather than convert it to a transparent material.
            // Otherwise the marble texture disappears as soon as video mode is active.
            RestoreOriginalMaterial();

            PropertyBlock ??= new MaterialPropertyBlock();

            Renderer.GetPropertyBlock(PropertyBlock, MaterialIndex);
            if (PropertyBlock == null)
            {
                PropertyBlock = new MaterialPropertyBlock();
                Renderer.GetPropertyBlock(PropertyBlock, MaterialIndex);
            }

            if (refreshOriginal)
            {
                try
                {
                    var sharedMats = Renderer.sharedMaterials;
                    if (sharedMats != null && MaterialIndex >= 0 && MaterialIndex < sharedMats.Length)
                    {
                        var sharedMat = sharedMats[MaterialIndex];
                        if (sharedMat != null)
                        {
                            foreach (var slot in _colorProperties)
                            {
                                try
                                {
                                    if (sharedMat.HasProperty(slot.PropertyId))
                                    {
                                        slot.OriginalColor = sharedMat.GetColor(slot.PropertyId);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            foreach (var slot in _colorProperties)
            {
                Color targetColor = slot.OriginalColor;
                float brightness = Mathf.Lerp(1f, 0.22f, intensity);
                targetColor.r *= brightness;
                targetColor.g *= brightness;
                targetColor.b *= brightness;
                targetColor.a = slot.OriginalColor.a;
                try
                {
                    PropertyBlock.SetColor(slot.PropertyId, targetColor);
                }
                catch { }
            }

            try
            {
                Renderer.SetPropertyBlock(PropertyBlock, MaterialIndex);
            }
            catch { }
        }

        private void UpdateMaterialTransparency(float intensity)
        {
            bool needsTransparent = intensity > 0.001f;
            if (needsTransparent)
            {
                ActivateTransparentMaterial();
            }
            else
            {
                RestoreOriginalMaterial();
            }
        }

        private void ActivateTransparentMaterial()
        {
            if (Renderer == null) return;
            if (_transparentMaterialApplied && _transparentMaterial != null) return;

            Material sharedMat = null;
            try
            {
                var sharedMats = Renderer.sharedMaterials;
                if (sharedMats == null || MaterialIndex < 0 || MaterialIndex >= sharedMats.Length)
                {
                    return;
                }

                sharedMat = sharedMats[MaterialIndex];
                if (_originalSharedMaterial == null)
                {
                    _originalSharedMaterial = sharedMat;
                }
            }
            catch
            {
                return;
            }

            if (sharedMat == null && _originalSharedMaterial == null)
            {
                return;
            }

            if (_transparentMaterial == null)
            {
                var source = sharedMat != null ? sharedMat : _originalSharedMaterial;
                if (source == null) return;
                _transparentMaterial = new Material(source)
                {
                    name = source.name + " (TrackDimmer)"
                };
                ConfigureMaterialForTransparency(_transparentMaterial);
                if (_originalRenderQueue >= 0)
                {
                    _transparentMaterial.renderQueue = _originalRenderQueue;
                }
            }

            try
            {
                var sharedMats = Renderer.sharedMaterials;
                if (sharedMats == null || MaterialIndex < 0 || MaterialIndex >= sharedMats.Length) return;
                if (ReferenceEquals(sharedMats[MaterialIndex], _transparentMaterial))
                {
                    _transparentMaterialApplied = true;
                    return;
                }

                var newMats = (Material[])sharedMats.Clone();
                newMats[MaterialIndex] = _transparentMaterial;
                Renderer.sharedMaterials = newMats;
                _transparentMaterialApplied = true;
            }
            catch { }
        }

        private void RestoreOriginalMaterial()
        {
            if (!_transparentMaterialApplied) return;
            if (Renderer == null) return;

            try
            {
                var sharedMats = Renderer.sharedMaterials;
                if (sharedMats != null && MaterialIndex >= 0 && MaterialIndex < sharedMats.Length)
                {
                    var newMats = (Material[])sharedMats.Clone();
                    newMats[MaterialIndex] = _originalSharedMaterial != null ? _originalSharedMaterial : sharedMats[MaterialIndex];
                    Renderer.sharedMaterials = newMats;
                }
            }
            catch { }

            if (_transparentMaterial != null)
            {
                try { UnityEngine.Object.Destroy(_transparentMaterial); }
                catch { }
            }

            _transparentMaterial = null;
            _transparentMaterialApplied = false;
        }

        private void ConfigureMaterialForTransparency(Material material)
        {
            if (material == null) return;

            try
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                if (material.HasFloat("_Surface"))
                {
                    material.SetFloat("_Surface", 1f); // Transparent
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
                }

                if (material.HasFloat("_Blend")) material.SetFloat("_Blend", 0f);
                if (material.HasFloat("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                if (material.HasFloat("_Mode")) material.SetFloat("_Mode", 3f); // Standard shader transparent

                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasInt("_SrcBlendAlpha")) material.SetInt("_SrcBlendAlpha", (int)UnityEngine.Rendering.BlendMode.One);
                if (material.HasInt("_DstBlendAlpha")) material.SetInt("_DstBlendAlpha", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                if (material.HasFloat("_ZWrite")) material.SetFloat("_ZWrite", 0f);

                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
            }
            catch { }
        }

        public void LogState(int index, float intensity)
        {
            try
            {
                string rendererName = Renderer != null ? Renderer.name : "<null>";
                string spriteName = SpriteRenderer != null ? SpriteRenderer.name : "<null>";
                float spriteAlpha = SpriteRenderer != null ? SpriteRenderer.color.a : -1f;

                List<string> propertySummaries = null;
                bool transparentActive = _transparentMaterialApplied;
                if (Renderer != null && HasMaterialProperties)
                {
                    propertySummaries = new List<string>(_colorProperties.Count);
                    Material sharedMat = null;
                    try
                    {
                        var sharedMats = Renderer.sharedMaterials;
                        if (sharedMats != null && MaterialIndex >= 0 && MaterialIndex < sharedMats.Length)
                        {
                            sharedMat = sharedMats[MaterialIndex];
                        }
                    }
                    catch { sharedMat = null; }

                    try
                    {
                        PropertyBlock ??= new MaterialPropertyBlock();
                        Renderer.GetPropertyBlock(PropertyBlock, MaterialIndex);
                    }
                    catch { }

                    foreach (var slot in _colorProperties)
                    {
                        string propertyLabel = slot.PropertyName ?? slot.PropertyId.ToString();
                        float sharedAlpha = -1f;
                        if (sharedMat != null)
                        {
                            try
                            {
                                if (sharedMat.HasProperty(slot.PropertyId))
                                {
                                    sharedAlpha = sharedMat.GetColor(slot.PropertyId).a;
                                }
                            }
                            catch { sharedAlpha = -1f; }
                        }

                        string blockInfo = "<none>";
                        if (PropertyBlock != null)
                        {
                            try
                            {
                                var blockColor = PropertyBlock.GetColor(slot.PropertyId);
                                blockInfo = blockColor.ToString();
                            }
                            catch { blockInfo = "<error>"; }
                        }

                        propertySummaries.Add($"{propertyLabel}:orig={slot.OriginalColor.a:F3} block={blockInfo} sharedAlpha={(sharedAlpha >= 0f ? sharedAlpha.ToString("F3") : "<n/a>")}");
                    }
                }

                string propertySummaryText = propertySummaries != null && propertySummaries.Count > 0
                    ? string.Join(" | ", propertySummaries)
                    : "<none>";

                BuildLogger.Log($"GameBackgroundManager: entry[{index}] renderer={rendererName} sprite={spriteName} spriteAlpha={spriteAlpha:F3} intensity={intensity:F3} matIndex={MaterialIndex} transparent={transparentActive} props={propertySummaryText}");
            }
            catch { }
        }
    }

    // Helper to get the full hierarchy path for nicer diagnostics
    private string GetHierarchyPath(Transform t)
    {
        if (t == null) return "<null>";
        string path = t.name;
        var cur = t;
        while (cur.parent != null)
        {
            cur = cur.parent;
            path = cur.name + "/" + path;
        }
        return path;
    }

}
