using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.IO;

public class SongSelectionManager : MonoBehaviour
{
    public static SongSelectionManager Instance { get; private set; }

    [Serializable]
    private class SongListData
    {
        public List<SongMetadata> songs = new List<SongMetadata>();
    }

    [Serializable]
    public class AudioPauseResumeWindow
    {
        // Legacy numeric fields (legacy JSON may use "pause"/"resume")
        public int pause;
        public int resume;
        // New explicit mute keys (preferred name for legacy mute behavior)
        public int mute;
        public int muteResume;
        // New explicit pause (playback) keys — these will Pause/Resume playback (stop timing)
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
        // Optional nested windows for convenience (allow multiple pause/mute windows per resource)
        public List<AudioPauseResumeWindow> pauseWindows;
        // Runtime hints set by the parser (not serialized)
        [NonSerialized] public bool isMuteWindow = false;
        [NonSerialized] public bool isPauseWindow = false;
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
    }

    public class SongOption
    {
        public string displayName;
        public string chartFileName;
        public string audioResourcePath;
        public string pianoAudioResourcePath;
        public AudioClip audioClip;
        public AudioClip pianoAudioClip;
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
    // Runtime-created difficulty selector panel instance
    private GameObject difficultySelectorInstance = null;
    private int currentIndex = 0;
    private bool carouselInitialized = false;
    // Audio clip cache to avoid runtime hiccups
    private readonly Dictionary<string, AudioClip> _audioCache = new Dictionary<string, AudioClip>();
    private readonly HashSet<string> _missingAudioWarnings = new HashSet<string>();
    // Cache for runtime-created sprites from Texture2D (keyed by resource path)
    private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
    // Mouse scroll navigation
    private float _lastScrollTime = 0f;
    private float _scrollCooldown = 0.12f; // seconds between allowed scroll moves
    private SongOption currentPreviewOption = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildLogger.Log("SongSelectionManager: Awake - instance assigned.");

        EnsurePreviewSources();
    }

    private void Start()
    {
        if (centerSlot != null && centerSlot.root == null && songButtonContainer != null)
        {
            centerSlot.root = songButtonContainer;
        }

        LoadSongOptions();
        BuildLogger.Log($"SongSelectionManager: Start - loaded {songOptions.Count} options.");
        ConfigureCarouselControls();
        UpdateCarouselVisuals();
        // Preload current and neighbor songs on startup for smoother experience
        PreloadAroundIndex(currentIndex);
        ShowSelection();
    }

    private void LoadSongOptions()
    {
        // 只從 Resources/songs 自動掃描 register.json
        if (songOptions != null)
        {
            foreach (var so in songOptions) if (so != null) so.selectedVariant = null;
        }
        var discovered = DiscoverSongsFromResources(autoDiscoverResourcesRoot);
        BuildLogger.Log($"SongSelectionManager: DiscoverSongsFromResources returned { (discovered == null ? 0 : discovered.Count) } entries");
        if (discovered == null || discovered.Count == 0)
        {
            BuildLogger.LogWarning("SongSelectionManager: No songs found in Resources/songs.");
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
        var groupMap = new Dictionary<string, SongOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in flatOptions)
        {
            if (opt == null) continue;
            string key = string.IsNullOrWhiteSpace(opt.displayName) ? opt.chartFileName ?? string.Empty : opt.displayName;
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
                songOptions.Add(opt);
            }
        }
        currentIndex = Mathf.Clamp(currentIndex, 0, Mathf.Max(0, songOptions.Count - 1));
        // Debugging: Ensure coverResourcePath and videoPath are valid
        foreach (var option in songOptions)
        {
            if (string.IsNullOrWhiteSpace(option.coverResourcePath))
            {
                BuildLogger.LogWarning($"Song '{option.displayName}' has an invalid coverResourcePath.");
            }
            if (string.IsNullOrWhiteSpace(option.videoPath))
            {
                BuildLogger.LogWarning($"Song '{option.displayName}' has an invalid videoPath.");
            }
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
            pianoAudioSpeedEvents = pianoSpeedEvents
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
            if (centerSlot.selectButton != null && centerSlot.coverImage != null)
            {
                OverlayButtonOnCover(centerSlot.selectButton, centerSlot.coverImage, true);
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
            if (leftSlot.selectButton != null && leftSlot.coverImage != null)
            {
                OverlayButtonOnCover(leftSlot.selectButton, leftSlot.coverImage, true);
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
                    int idx = GetWrappedIndex(currentIndex - 1);
                    //Debug.Log($"SongSelectionManager: Left cover clicked → start index {idx}.");
                    SelectSong(idx);
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
            if (rightSlot.selectButton != null && rightSlot.coverImage != null)
            {
                OverlayButtonOnCover(rightSlot.selectButton, rightSlot.coverImage, true);
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
                    int idx = GetWrappedIndex(currentIndex + 1);
                    //Debug.Log($"SongSelectionManager: Right cover clicked → start index {idx}.");
                    SelectSong(idx);
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

        try
        {
            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }
        }
        catch { }
        _audioCache[resourcePath] = clip;

        return clip;
    }

    void Update()
    {
        // Mouse-wheel navigation: up/positive -> previous, down/negative -> next
        if (!carouselInitialized) return;

        // If pointer is over UI, don't navigate (avoid interfering with scrollable UI)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        float delta = 0f;
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // New Input System only
        if (Mouse.current != null) delta = Mouse.current.scroll.ReadValue().y;
    #elif ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
        // Prefer new Input System but fall back to legacy if needed
        if (Mouse.current != null) delta = Mouse.current.scroll.ReadValue().y;
        else delta = UnityEngine.Input.mouseScrollDelta.y;
    #else
        // Legacy Input Manager
        delta = UnityEngine.Input.mouseScrollDelta.y;
    #endif

        if (Mathf.Abs(delta) > 0.01f)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastScrollTime < _scrollCooldown) return;

            if (delta > 0f)
            {
                // scroll up -> previous
                ShowPreviousSong();
            }
            else if (delta < 0f)
            {
                // scroll down -> next
                ShowNextSong();
            }

            _lastScrollTime = now;
        }
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

        if (option.audioClip == null)
        {
            BuildLogger.LogWarning($"SongSelectionManager: No main audio clip available for '{option.displayName}'.");
            StopPreviewAudio();
            return;
        }

        bool needRestartMain = restart || previewAudioSource.clip != option.audioClip || !previewAudioSource.isPlaying;
        previewAudioSource.loop = true;
        if (needRestartMain)
        {
            previewAudioSource.Stop();
            previewAudioSource.clip = option.audioClip;
            previewAudioSource.time = 0f;
            previewAudioSource.volume = musicVolume;
            previewAudioSource.Play();
        }
        else
        {
            previewAudioSource.volume = musicVolume;
        }

        bool shouldPlayPiano = ShouldPlayPianoLayer(option);
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

    private void StopPreviewAudio()
    {
        currentPreviewOption = null;
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
        PlayPreviewForOption(songOptions[clampedIndex], restart);
    }

    public Sprite LoadCoverSprite(string resourcePath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
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
            SongFolderList folderList = JsonUtility.FromJson<SongFolderList>(songListAsset.text);
            if (folderList == null || folderList.folders == null) {
                Debug.LogWarning($"[SongSelectionManager] songlist.json 內容異常");
                return discoveredSongs;
            }
            foreach (var folder in folderList.folders)
            {
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
        if (songOptions.Count == 0)
        {
            return;
        }
        // moving selection cancels any explicit variant choice on the previous group
        int prev = GetWrappedIndex(currentIndex);
        if (prev >= 0 && prev < songOptions.Count)
        {
            songOptions[prev].selectedVariant = null;
        }
        currentIndex = GetWrappedIndex(currentIndex + 1);
        UpdateCarouselVisuals();
    }

    public void ShowPreviousSong()
    {
        if (songOptions.Count == 0)
        {
            return;
        }
        // moving selection cancels any explicit variant choice on the previous group
        int prev = GetWrappedIndex(currentIndex);
        if (prev >= 0 && prev < songOptions.Count)
        {
            songOptions[prev].selectedVariant = null;
        }
        currentIndex = GetWrappedIndex(currentIndex - 1);
        UpdateCarouselVisuals();
    }

    private void SelectCurrentSong()
    {
    #if UNITY_EDITOR
    Debug.Log("[SongSelectionManager] SelectCurrentSong 被呼叫");
    #endif
    //Debug.Log($"SongSelectionManager: Center cover clicked → start index {currentIndex}.");
    SelectSong(currentIndex);
    }

    private void UpdateCarouselVisuals()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (songOptions.Count == 0)
        {
            UpdatePreviewSlot(centerSlot, null, true);
            UpdatePreviewSlot(leftSlot, null, false);
            UpdatePreviewSlot(rightSlot, null, false);
            if (currentSongLabel != null)
            {
                currentSongLabel.text = "No Songs";
            }
            if (authorLabel != null)
            {
                authorLabel.text = string.Empty;
                authorLabel.gameObject.SetActive(false);
            }
            return;
        }

    SongOption centerOption = songOptions[currentIndex];
        SongOption leftOption = songOptions[GetWrappedIndex(currentIndex - 1)];
        SongOption rightOption = songOptions[GetWrappedIndex(currentIndex + 1)];

        UpdatePreviewSlot(centerSlot, centerOption, true);
        UpdatePreviewSlot(leftSlot, leftOption, false);
        UpdatePreviewSlot(rightSlot, rightOption, false);
        UpdateDetailPanel(centerOption);

        // Notify subscribers that the currently-focused selection changed (live preview)
        try
        {
            OnSelectionChanged?.Invoke(centerOption);
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"SongSelectionManager: OnSelectionChanged handler threw: {ex.Message}");
        }

        // Proactively preload audio for current and neighbor items
        PreloadAroundIndex(currentIndex);

        if (IsSelectionPanelActive())
        {
            PlayPreviewForOption(centerOption, true);
        }
        else
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
        }

        if (slot.difficultyLabel != null)
        {
            slot.difficultyLabel.text = BuildDifficultyText(option);
        }

        if (slot.authorLabel != null)
        {
            string authorText = option.author ?? string.Empty;
            slot.authorLabel.text = authorText;
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

        if (currentSongLabel != null)
        {
            currentSongLabel.text = toShow != null ? toShow.displayName : string.Empty;
        }

        if (authorLabel != null)
        {
            string text = toShow != null ? toShow.author ?? string.Empty : string.Empty;
            authorLabel.text = text;
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
        HideSelection();

        // If this SongOption groups multiple difficulties, show a difficulty selector
        if (option.difficultyVariants != null && option.difficultyVariants.Count > 1)
        {
            ShowDifficultySelector(option, option.difficultyVariants, chosen =>
            {
                StartChosenSong(chosen);
            });
            return;
        }

        // Try to use cached preloaded clip if available
        AudioClip clipToUse = option.audioClip;
        if ((clipToUse == null) && !string.IsNullOrEmpty(option.audioResourcePath))
        {
            _audioCache.TryGetValue(option.audioResourcePath, out clipToUse);
            if (clipToUse != null) option.audioClip = clipToUse;
        }
        AudioClip pianoClipToUse = option.pianoAudioClip;
        if ((pianoClipToUse == null) && !string.IsNullOrEmpty(option.pianoAudioResourcePath))
        {
            pianoClipToUse = LoadAudioClip(option.pianoAudioResourcePath, option.displayName + " (Piano)");
            if (pianoClipToUse != null) option.pianoAudioClip = pianoClipToUse;
        }
        StopPreviewAudio();
        // Debug: log the chart path being passed to GameManager to help diagnose missing chart issues
        try { BuildLogger.Log($"[SongSelectionManager] Starting song: chartFile='{option.chartFileName}' displayName='{option.displayName}' audioPath='{option.audioResourcePath}'"); } catch { }
        manager.StartSong(option.chartFileName, clipToUse, option.displayName, pianoClipToUse, option.pianoAudioResourcePath);
        if (currentSongLabel != null)
        {
            currentSongLabel.text = option.displayName;
        }
        if (authorLabel != null)
        {
            var authorText = option.author ?? string.Empty;
            authorLabel.text = authorText;
            authorLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(authorText));
        }
        // Update gameplay HUD difficulty via GameBackgroundManager
        try
        {
            var bg = FindAnyObjectByType<GameBackgroundManager>();
            if (bg != null) bg.SetSongDifficultyFromOption(option);
        }
        catch { }
    }

    private void StartChosenSong(SongOption option)
    {
        if (option == null) return;
        GameManager manager = GameManager.Instance;
        if (manager == null) return;

        // Ensure audio clips loaded
        AudioClip clipToUse = option.audioClip;
        if ((clipToUse == null) && !string.IsNullOrEmpty(option.audioResourcePath))
        {
            _audioCache.TryGetValue(option.audioResourcePath, out clipToUse);
            if (clipToUse != null) option.audioClip = clipToUse;
        }
        AudioClip pianoClipToUse = option.pianoAudioClip;
        if ((pianoClipToUse == null) && !string.IsNullOrEmpty(option.pianoAudioResourcePath))
        {
            pianoClipToUse = LoadAudioClip(option.pianoAudioResourcePath, option.displayName + " (Piano)");
            if (pianoClipToUse != null) option.pianoAudioClip = pianoClipToUse;
        }

        StopPreviewAudio();
        // Debug: log the chart path when starting chosen difficulty
        try { BuildLogger.Log($"[SongSelectionManager] StartChosenSong: chartFile='{option.chartFileName}' displayName='{option.displayName}' audioPath='{option.audioResourcePath}'"); } catch { }
        manager.StartSong(option.chartFileName, clipToUse, option.displayName, pianoClipToUse, option.pianoAudioResourcePath);
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
        Canvas parentCanvas = FindFirstObjectByType<Canvas>();
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
        containerRT.sizeDelta = new Vector2(600, 300);
        containerRT.anchorMin = new Vector2(0.5f, 0.5f); containerRT.anchorMax = new Vector2(0.5f, 0.5f);
        containerRT.anchoredPosition = Vector2.zero;

        var containerImg = containerGO.AddComponent<UnityEngine.UI.Image>();
        containerImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

        var layout = containerGO.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.childControlHeight = true; layout.childControlWidth = true; layout.spacing = 8; layout.padding = new RectOffset(12,12,12,12);

        // Title
        var titleGO = new GameObject("Title"); titleGO.transform.SetParent(containerGO.transform, false);
        var titleText = titleGO.AddComponent<TextMeshProUGUI>();
        titleText.text = groupPrimary.displayName + " - Select Difficulty";
        titleText.fontSize = 24; titleText.alignment = TMPro.TextAlignmentOptions.Center;
        var titleRT = titleGO.GetComponent<RectTransform>(); titleRT.sizeDelta = new Vector2(0, 36);

        // Buttons for each difficulty
        var bgManager = FindAnyObjectByType<GameBackgroundManager>();
        foreach (var opt in options)
        {
            var optLocal = opt;
            var btnGO = new GameObject("Btn_" + (opt.difficultyName ?? "diff"));
            btnGO.transform.SetParent(containerGO.transform, false);
            var btn = btnGO.AddComponent<UnityEngine.UI.Button>();
            var bimg = btnGO.AddComponent<UnityEngine.UI.Image>(); bimg.color = new Color(0.2f,0.2f,0.2f,1f);
            var brt = btnGO.GetComponent<RectTransform>(); brt.sizeDelta = new Vector2(0, 44);

            var lblGO = new GameObject("Label"); lblGO.transform.SetParent(btnGO.transform, false);
            var lbl = lblGO.AddComponent<TextMeshProUGUI>(); lbl.text = (optLocal.difficultyName ?? "") + (optLocal.difficultyLevel>0? $" Lv. {optLocal.difficultyLevel}": "");
            lbl.alignment = TMPro.TextAlignmentOptions.Center; lbl.fontSize = 20;
            var lblRT = lblGO.GetComponent<RectTransform>(); lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;

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
        }

        // Cancel button
        var cancelGO = new GameObject("Cancel"); cancelGO.transform.SetParent(containerGO.transform, false);
        var cancelBtn = cancelGO.AddComponent<UnityEngine.UI.Button>();
        var cancelImg = cancelGO.AddComponent<UnityEngine.UI.Image>(); cancelImg.color = new Color(0.15f,0.15f,0.15f,1f);
        var cancelLblGO = new GameObject("Label"); cancelLblGO.transform.SetParent(cancelGO.transform, false);
        var cancelLbl = cancelLblGO.AddComponent<TextMeshProUGUI>(); cancelLbl.text = "Cancel"; cancelLbl.alignment = TMPro.TextAlignmentOptions.Center; cancelLbl.fontSize = 18;
        var cancelRT = cancelGO.GetComponent<RectTransform>(); cancelRT.sizeDelta = new Vector2(0, 36);
        cancelBtn.onClick.AddListener(() => { Destroy(root); difficultySelectorInstance = null; ShowSelection(); });

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

        var req = Resources.LoadAsync<AudioClip>(resourcePath);
        yield return req;

        var clip = req.asset as AudioClip;
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

    public void ShowSelection()
    {
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

        // Stop any background video preview while the player is choosing/difficulty is undecided
        try
        {
            var bg = FindAnyObjectByType<GameBackgroundManager>();
            if (bg != null) bg.ShowCoverOnlyFromSelection();
        }
        catch { }

        UpdateCarouselVisuals();
    }

    public void HideSelection()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
        }

        if (gameplayPanel != null)
        {
            gameplayPanel.SetActive(true);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetGameplayRootActive(true);
        }

        StopPreviewAudio();
    }

    private void OnDisable()
    {
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

    public SongOption GetSelectedSong()
    {
        if (currentIndex < 0 || currentIndex >= songOptions.Count) return null;
        var opt = songOptions[currentIndex];
        // If player picked a specific difficulty variant, return that for gameplay so speed/audioManage match the chosen chart.
        return opt != null && opt.selectedVariant != null ? opt.selectedVariant : opt;
    }
}
