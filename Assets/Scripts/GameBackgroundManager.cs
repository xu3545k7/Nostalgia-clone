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
    private static Texture2D sBlackTexture;
    [Header("Track Dimmer")]
    [SerializeField]
    private float sideMarginPixels = 200f; // 左右預留的黑邊寬度
    [Header("Masking")]
    [SerializeField] private bool maskVideoGraphics = true;
    [SerializeField] private bool maskSongImageGraphic = false;
    [SerializeField] private bool maskSongTextGraphics = false;
    [SerializeField] private bool maskGameplayDifficultyGraphic = false;

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

    void Update()
    {
        UpdateDspProgress();
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
        // Restore cached editor rects into runtime cache before other Start() calls run
        _songRectTransform = songImage != null ? songImage.GetComponent<RectTransform>() : null;
        if (songImage != null && detachSongImageFromLayouts)
        {
            DetachSongImageFromLayouts();
        }
        EnsureSongImageLayoutIgnored();
        EnsureVideoBackdrop();
        EnsureVideoOverlay();

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

        ApplyMaskPreferences();
    }

    private void UpdateDspProgress()
    {
        if (dspProgressText == null) return;

        double elapsedMs = 0.0;
        double finishMs = 0.0;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            try
            {
                if (gm.Conductor != null)
                {
                    // Use Conductor songPosition (ms) as elapsed time so it resets per song.
                    elapsedMs = gm.Conductor.songPosition;
                }
                if (gm.CurrentChart != null && gm.CurrentChart.music_finish_time_msec > 0)
                {
                    finishMs = gm.CurrentChart.music_finish_time_msec;
                }
                else if (gm.CurrentChartHeader != null && gm.CurrentChartHeader.music_finish_time_msec > 0)
                {
                    finishMs = gm.CurrentChartHeader.music_finish_time_msec;
                }
            }
            catch { }
        }

        long current = (long)Math.Round(elapsedMs);
        string text = finishMs > 0 ? $"{current}/{(long)Math.Round(finishMs)}" : $"{current}/-";

        if (_lastDspProgressText != text)
        {
            dspProgressText.text = text;
            _lastDspProgressText = text;
        }
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
        ConfigureMaskableGraphic(backgroundImage, maskVideoGraphics);
        ConfigureMaskableGraphic(videoOverlay, maskVideoGraphics);
        ConfigureMaskableGraphic(songImage, maskSongImageGraphic);
        ConfigureMaskableGraphic(songTitleText, maskSongTextGraphics);
        ConfigureMaskableGraphic(songAuthorText, maskSongTextGraphics);
        ConfigureMaskableGraphic(gameplayDifficultyText, maskGameplayDifficultyGraphic);
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
            BuildLogger.Log($"GameBackgroundManager: SetSongDifficulty -> '{gameplayDifficultyText.text}'");
            // If we clear the difficulty text, clear player-set flag as well
            if (string.IsNullOrEmpty(difficultyText)) _playerSetDifficulty = false;
        }
        catch { }
    }

    // Public helper: derive difficulty display from a SongSelectionManager.SongOption
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
        string candidate = System.IO.Path.Combine(Application.streamingAssetsPath, normalizedPath).Replace("\\", "/");

        // Prefer explicit file:// URL on Windows for the VideoPlayer
        string fullUrl = candidate;
        if (System.IO.Path.IsPathRooted(candidate) && !candidate.StartsWith("file://"))
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
                        fullUrl = "file:///" + tryMp4.Replace("\\", "/");
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
        BuildLogger.LogWarning($"GameBackgroundManager: VideoPlayer errorReceived: {message} url={source.url}");
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
        try
        {
            // Assign the video texture to the background RawImage so the UI shows the video.
            // Keep it black until the actual playback start event fires.
            if (backgroundImage != null && source.texture != null)
            {
                backgroundImage.texture = source.texture;
                backgroundImage.color = Color.black;
                backgroundImage.raycastTarget = false;
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
                                // If we're already between desiredVideoStartDsp and scheduledDspStart, start immediately
                                // so the video can play through pre-roll frames naturally.
                                // 無論是否 late，改成一律等待 desiredVideoStartDsp
                                try { BeginWaitForDsp(source, desiredVideoStartDsp, /*seeked*/ false, /*strictDelayMode*/ false); }
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

    private void SetVideoOverlay(bool show)
    {
        if (videoOverlay == null) return;
        try
        {
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

            songImage.transform.SetParent(_runtimeSongImageParent, false);
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
            songTitleText.text = string.IsNullOrEmpty(title) ? "" : title;
            songTitleText.gameObject.SetActive(!string.IsNullOrEmpty(songTitleText.text));
        }

        if (songAuthorText != null)
        {
            songAuthorText.text = string.IsNullOrEmpty(author) ? "" : author;
            songAuthorText.gameObject.SetActive(!string.IsNullOrEmpty(songAuthorText.text));
        }
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
                    Color fadeColor = SpriteOriginalColor;
                    fadeColor.a = Mathf.Lerp(SpriteOriginalColor.a, 0f, intensity);
                    SpriteRenderer.color = fadeColor;
                }
                return;
            }

            if (Renderer == null || !HasMaterialProperties) return;

            UpdateMaterialTransparency(intensity);

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
                float newAlpha = Mathf.Lerp(slot.OriginalColor.a, 0f, intensity);
                targetColor.a = newAlpha;
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
