using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem; // Add this for the new Input System
using UnityEngine.Networking;
using System.IO;
// SettingsManager 沒有 namespace，無需額外 using

// It's good practice to ensure required components are present.
[RequireComponent(typeof(Conductor), typeof(NoteSpawner), typeof(BeatLineSpawner))]
public class GameManager : MonoBehaviour // Renamed class to match file name "GameManager.cs"
{
    private static GameManager _instance;
    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<GameManager>();
            }
            return _instance;
        }
        private set => _instance = value;
    }

    private Coroutine chartParseRoutine;
    private int chartLoadGeneration;

    private System.Collections.IEnumerator ParseChartInBackground(string jsonText, string chartFile, int generation)
    {
        // Give one frame to let UI update and avoid blocking the immediate startup frame
        yield return null;
        if (generation != chartLoadGeneration || !string.Equals(chartFile, chartFileName,
                System.StringComparison.Ordinal)) yield break;
        // Additional frame yield to reduce likelihood of a single-frame long parse
        yield return null;
        if (generation != chartLoadGeneration || !string.Equals(chartFile, chartFileName,
                System.StringComparison.Ordinal)) yield break;

        // ── 解析搬到工作執行緒 ────────────────────────────────────────
        //
        // 探針量到 chartParse=18.9ms，而且它產生的垃圾大到之後有一格被 GC 收掉
        // 88MB。這首譜面 2314 顆音符、每顆還帶 subNotes。
        //
        // JsonUtility **不能**跨執行緒（原本的註解就是這麼寫的），但這裡本來就
        // 有一條 Newtonsoft 的後備路徑，而 Newtonsoft 是執行緒安全的。所以順序
        // 反過來：先讓工作執行緒用 Newtonsoft 解析，主執行緒完全不付錢；只有在
        // 它失敗或解出來的東西不對時，才退回原本的 JsonUtility 主執行緒路徑。
        //
        // 「不對」的定義刻意收得很窄——null 或沒有音符。這裡不是在挑剔差異，
        // 是在確認後備路徑該不該接手。
        Chart parsed = null;
        System.Threading.Tasks.Task<Chart> parseTask = null;
        try
        {
            parseTask = System.Threading.Tasks.Task.Run(
                () => Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(jsonText));
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"[GameManager] Background parse: could not start worker task: {ex.Message}");
            parseTask = null;
        }

        if (parseTask != null)
        {
            // 等它跑完，但不要卡住這一格。前奏有兩秒，這段時間畫面照常更新。
            while (!parseTask.IsCompleted) yield return null;
            if (parseTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
            {
                parsed = parseTask.Result;
            }
            else
            {
                BuildLogger.LogWarning("[GameManager] Background parse: worker task failed — " +
                    (parseTask.Exception != null ? parseTask.Exception.GetBaseException().Message : "unknown"));
            }
        }

        if (parsed == null || parsed.notes == null || parsed.notes.Count == 0)
        {
            if (parsed != null)
            {
                BuildLogger.LogWarning("[GameManager] Background parse: worker result had no notes; " +
                                       "falling back to JsonUtility on the main thread.");
            }
            try
            {
                using (HitchProbe.Measure("chartParse"))
                {
                    parsed = JsonUtility.FromJson<Chart>(jsonText);
                }
            }
            catch (System.Exception ex)
            {
                BuildLogger.LogError($"[GameManager] Background parse failed for {chartFile}: {ex.Message}");
                parsed = null;
            }
        }

        if (generation != chartLoadGeneration || !string.Equals(chartFile, chartFileName,
                System.StringComparison.Ordinal)) yield break;

        if (parsed != null)
        {
            CurrentChart = parsed;
            FinaliseLoadedChart(chartFile);
            try { if (Conductor != null) Conductor.bpm = CurrentChart.first_bpm; } catch { }
            // Notify that full chart is available so StartSong continuation can proceed.
            try { OnFullChartParsed(); } catch { }
        }
        else
        {
            BuildLogger.LogError($"[GameManager] Failed to parse full chart in background for '{chartFile}'");
        }
        if (generation == chartLoadGeneration) chartParseRoutine = null;
    }

    private void OnFullChartParsed()
    {
        // If StartSong previously stored pending parameters, continue initialization now
        if (string.IsNullOrEmpty(pendingChartFileName))
        {
            return;
        }

        // Configure expected judgments and initialize spawners now that full chart is ready
        try
        {
            StatsManager.Instance.ConfigureExpectedJudgments(CurrentChart);
        }
        catch { }

        if (NoteSpawner == null)
        {
            NoteSpawner = GetComponent<NoteSpawner>();
            if (NoteSpawner == null)
            {
                NoteSpawner = FindFirstObjectByType<NoteSpawner>();
            }
        }
        if (BeatLineSpawner == null)
        {
            BeatLineSpawner = GetComponent<BeatLineSpawner>();
            if (BeatLineSpawner == null)
            {
                BeatLineSpawner = FindFirstObjectByType<BeatLineSpawner>();
            }
        }

        if (NoteSpawner == null || BeatLineSpawner == null)
        {
            BuildLogger.LogError("[GameManager] OnFullChartParsed: Missing spawners after chart parse");
            return;
        }

        // 先擋住畫面，把該載的都載完，再讓遊戲開始。
        //
        // 原本這裡直接往下跑，於是兩秒的前奏裡塞了譜面解析、兩個上百 MB 的音檔
        // 解碼、183 個鋼琴取樣的 Vorbis 解壓、音符池和節拍線池、還有每個材質的
        // 第一次繪製。每一項都可以個別優化，但只要它們仍然發生在音符已經在跑的
        // 時候，就一定看得出來。同樣的工作、同樣的總時間，差別只在畫面是靜止的。
        StartCoroutine(BeginGameplayWhenReady(ContinueGameplayStart));
    }

    /// <summary>
    /// 在載入頁後面把重活做完，然後才進入前奏。
    /// </summary>
    /// <remarks>
    /// 順序是有講究的：軌道要先真的開始畫，材質才會被送出繪製，
    /// <see cref="GameplayShaderWarmup"/> 也才有東西可以暖。所以
    /// <see cref="ContinueGameplayStart"/> 之後還要再留幾格，才把畫面放掉。
    /// </remarks>
    /// <summary>載入頁最多擋這麼久，之後不管備妥沒有都放行。</summary>
    private const double LoadingTimeoutSeconds = 30d;

    private System.Collections.IEnumerator BeginGameplayWhenReady(System.Action continueStart)
    {
        if (continueStart == null) yield break;
        GameplayLoadingScreen screen = GameplayLoadingScreen.EnsureCreated();
        DressLoadingScreen(screen);
        screen.Show(Localize.T("準備中…", "准备中…", "Preparing…"));
        yield return null;

        // 逾時保險。等待條件有兩個都在腳本之外（音檔解碼、取樣解壓），任何一個
        // 卡住都不該讓玩家永遠停在載入頁——寧可帶著沒暖完的資產開始，也不要開不了。
        double deadline = Time.unscaledTimeAsDouble + LoadingTimeoutSeconds;

        BeginExternalAudioPreload();
        while (externalAudioPreloadRoutine != null && Time.unscaledTimeAsDouble < deadline)
        {
            screen.SetProgress(Localize.T("載入音訊…", "载入音讯…", "Loading audio…"), 0.15f);
            yield return null;
        }

        // 取樣的解壓是引擎在整合非同步載入時做的，發生在腳本之外，所以只能問
        // IsPrewarming，沒辦法用計時器判斷它做完沒有。
        PianoVoiceManager voices = PianoVoiceManager.Instance;
        while (voices != null && voices.IsPrewarming && Time.unscaledTimeAsDouble < deadline)
        {
            screen.SetProgress(Localize.T("載入鋼琴取樣…", "载入钢琴取样…", "Loading piano samples…"),
                0.2f + 0.65f * voices.PrewarmProgress01);
            yield return null;
        }

        if (Time.unscaledTimeAsDouble >= deadline)
        {
            BuildLogger.LogWarning($"[GameManager] 載入等待超過 {LoadingTimeoutSeconds} 秒，" +
                "帶著尚未備妥的資產開始。第一次用到的取樣或材質可能會卡一下。");
        }

        screen.SetProgress(Localize.T("準備軌道…", "准备轨道…", "Preparing track…"), 0.9f);
        yield return null;

        continueStart();

        // 材質要等軌道真的在畫之後才送得出繪製，所以留幾格給預熱把 shader 編譯
        // 完，再把畫面放掉。這幾格花掉的是前奏的開頭，代價很小。
        for (int i = 0; i < 6; i++) yield return null;

        screen.SetProgress(Localize.T("開始", "开始", "Ready"), 1f);
        screen.Hide();
    }

    /// <summary>譜面備妥、資產也備妥之後，原本 OnFullChartParsed 的尾段。</summary>
    private void ContinueGameplayStart()
    {
        // Activate gameplay root and continue the startup flow that depends on full chart
        SetGameplayRootActive(true);

        // The header-first/background-parse path must enter the same visual
        // pre-roll state as the synchronous path before the spawners inspect
        // the Conductor.  Without this, every note is initialized at t=0 and
        // then the render clock jumps backwards by the scheduled delay when
        // PlayScheduled is called (normally about two seconds).  That presents
        // as a whole-chart jump/shake even though the object pools are full.
        // 同樣要在前奏之前開始解碼——這是延後完整解析時走的另一條進入路徑。
        BeginExternalAudioPreload();

        float scheduledDelay = Mathf.Max(0f, pendingScheduledDelay);
        if (scheduledDelay > 0f && Conductor != null)
        {
            scheduledDelay += ArmRollIn();
            Conductor.StartVisualPreRoll(scheduledDelay);
        }

        // Preload shared note resources (sprites, sounds)
        try
        {
            if (notePrefab2D != null)
            {
                NoteController.PreloadSharedResources(notePrefab2D);
            }
        }
        catch { }

        NoteSpawner.Initialize(CurrentChart, Conductor);
        BeatLineSpawner.Initialize(CurrentChart, Conductor);

        try { Judgment.JudgmentManager.Instance?.EnsureHudVisible(); } catch { }

        // Apply settings and UI after spawners are initialized
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ApplySettings();
        }

        if (UIManager == null)
        {
            UIManager = FindFirstObjectByType<UIManager>();
        }

        if (UIManager != null)
        {
            UIManager.ApplyCurrentSpeedToSpawners();
        }
        else
        {
            float defaultSpeed = SettingsManager.Instance != null ? SettingsManager.Instance.DefaultSpeed : 30f;
            if (NoteSpawner != null) NoteSpawner.speed = defaultSpeed;
            if (BeatLineSpawner != null) BeatLineSpawner.Speed = defaultSpeed;
        }

        // Integrate GameBackgroundManager to load background
        var backgroundManager = FindAnyObjectByType<GameBackgroundManager>();
        if (backgroundManager != null)
        {
            var selectedSong = SongSelectionManager.Instance?.GetSelectedSong();
            if (selectedSong != null)
            {
                backgroundManager.RefreshFromSelected();
            }
        }

        // Begin the song after the visual pre-roll; the coroutine handles whether audio is present.
        startDelayRoutine = StartCoroutine(BeginSongAfterDelay(scheduledDelay));

        // Clear pending values
        pendingChartFileName = null;
        pendingSongClip = null;
        pendingSongDisplayName = null;
        pendingPianoClip = null;
        pendingPianoResourcePath = null;
        pendingScheduledDelay = 0f;
    }

    [Header("Default Song (Optional)")]
    public string chartFileName = "anima-xi-deemo-short_03real/anima-xi-deemo-short_Real_13";
    public AudioClip songClip; // Optional default song clip

    [Header("Gameplay Toggle")]
    [Tooltip("Root object that contains the gameplay world (track, spawners, etc). Will be toggled on/off with the gameplay panel.")]
    public GameObject gameplayRoot;

    [Header("Dependencies")]
    public Transform trackTransform; // TRACK transform to attach notes and beat lines
    public GameObject notePrefab2D; // Prefab for 2D notes
    public GameObject beatLinePrefab2D; // Prefab for 2D beat lines

    /// <summary>預備拍數幾下。指揮起拍就是三下。</summary>
    private const int CountInBeats = 3;

    private Chart currentChart;

    /// <summary>
    /// The chart being played. Assigning it also points the piano keysound at the
    /// chart's sustain pedal, so the two can never drift apart.
    /// </summary>
    public Chart CurrentChart
    {
        get => currentChart;
        private set
        {
            currentChart = value;
            try
            {
                var voices = PianoVoiceManager.EnsureCreated();
                // Tearing a song down clears the pedal, but a background full
                // parse that came back without pedal data must not wipe what the
                // header already provided.
                if (value == null) voices?.SetPedalData(null);
                else if (value.pedal_data != null && value.pedal_data.Count > 0)
                    voices?.SetPedalData(value.pedal_data);

                // 先把這首歌用得到的鋼琴取樣載進來。不預熱的話，第一次敲到某個
                // (音域, 力度層) 的那一顆音要等 Resources.Load 把整段 Vorbis 解壓
                // 完才發聲——那是判定當下最大的一段延遲，而且完全發生在按鍵之後。
                if (value != null) voices?.PrewarmForChart(value.notes);
            }
            catch { }
            try { PianoKeysound.ClearAllLanes(); } catch { }
        }
    }
    // Lightweight header parsed quickly to expose BPM / time signature before full parse
    public ChartHeader CurrentChartHeader { get; private set; }

    [System.Serializable]
    public class ChartHeader
    {
        public float first_bpm = 0f;
        public int time_signature_numerator = 4;
        public int time_signature_denominator = 4;
        public string time_signature = "4/4";
        public float music_finish_time_msec = 0f;
        // Sustain pedal lives here as well as on the full Chart, because the
        // header is the path a healthy chart actually takes — the full parse
        // only runs when header parsing failed. Reading the pedal only from
        // Chart meant the keysound never received any, so every note damped a
        // fraction of a second after it was struck.
        public System.Collections.Generic.List<PedalSpan> pedal_data;
        // Beat positions, used to synthesise a pedal for charts whose source MIDI
        // carried none. Read here for the same reason pedal_data is: the header
        // is the path a healthy chart actually takes.
        public System.Collections.Generic.List<int> beat_timings;
    }
    public Conductor Conductor { get; private set; }
    public NoteSpawner NoteSpawner { get; private set; }
    public BeatLineSpawner BeatLineSpawner { get; private set; }
    public UIManager UIManager { get; private set; }
    [Tooltip("If true, auto-stop and cleanup when chart time reaches music_finish_time_msec. Disable to let gameplay run independently of audio length.")]
    public bool autoStopOnChartEnd = true;
    [Header("Auto Resize")]
    [Tooltip("If true, GameManager will attempt to auto-resize the JudgmentLine to match the track width on Start(). Disable if you prefer to control sizing in the Inspector or via scene setup.")]
    public bool autoResizeJudgmentLine = false; // conservative default: do not auto-resize to avoid unexpected overrides
    //public HitSoundManager HitSoundManager { get; private set; }

    private AudioClip currentSongClip;
    private string currentSongResourcePath;
    private AudioClip currentPianoClip;
    private string currentSongDisplayName;
    private string currentPianoResourcePath;
    private AudioSource pianoAudioSource;
    private List<SongSelectionManager.AudioPauseResumeWindow> currentAudioManageMain;
    private List<SongSelectionManager.AudioPauseResumeWindow> currentAudioManagePiano;
    private List<SongSelectionManager.AudioSpeedEvent> currentAudioSpeedEvents;
    private List<SongSelectionManager.AudioSpeedEvent> currentPianoSpeedEvents;
    private Coroutine audioManageRoutine;
    private Coroutine pianoAudioManageRoutine;
    private Coroutine speedEventsRoutine;
    private Coroutine pianoSpeedEventsRoutine;
    private Coroutine skipSectionsRoutine;
    private float currentAudioSpeedFactor = 1f;
    private bool currentUseMixer = false;
    private float currentPianoSpeedFactor = 1f;
    private bool currentPianoUseMixer = false;
    private float baseMusicVolume = 1f;
    private float basePianoVolume = 1f;

    /// <summary>
    /// 這首曲子的響度校正倍率。1 = 照原樣播。
    /// </summary>
    /// <remarks>
    /// **從主音軌算，鋼琴分軌照套。** 兩者是同一首歌的分軌，鋼琴那一條本來就混
    /// 得比較低。各自量各自校正的話會被拉成一樣響，那不是修正，是把混音毀掉。
    /// </remarks>
    private float songLoudnessGain = 1f;

    /// <summary>
    /// 鋼琴分軌自己的響度增益。
    /// </summary>
    /// <remarks>
    /// 離線量測是兩軌各自對齊目標的，所以分軌不再和主軌共用同一個倍率。沒有量測
    /// 資料時兩者會是同一個值（執行時的估算只算得出一個），行為和以前一樣。
    /// </remarks>
    private float songPianoLoudnessGain = 1f;

    // Expose applied audio speed for UI (e.g., BPM display)
    public float CurrentAudioSpeedFactor => currentAudioSpeedFactor <= 0f ? 1f : currentAudioSpeedFactor;
    public bool CurrentUseMixer => currentUseMixer;
    public IReadOnlyList<SongSelectionManager.AudioSpeedEvent> CurrentAudioSpeedEvents =>
        currentAudioSpeedEvents;

    [Header("Audio Speed/Pitch")]
    [Tooltip("If true, apply pitch compensation via mixer parameter so speed change keeps pitch.")]
    public bool compensatePitch = true; // Always on: mixer pitch is driven to preserve original pitch when speed changes
    [Tooltip("Exposed mixer pitch parameter name. For Pitch Shifter use its semitone param (default name is 'Pitch'). Set to empty to disable.")]
    public string mixerPitchParam = "Pitch";
    [Tooltip("If true, mixerPitchParam expects semitone offset (Pitch Shifter). If false, expects linear multiplier (Group Pitch).")]
    public bool mixerPitchParamIsSemitone = true;
    [Header("Audio Mixer Routing")]
    [Tooltip("Fallback mixer group for music if the Conductor's AudioSource has none assigned.")]
    public AudioMixerGroup defaultMusicMixerGroup;
    [Tooltip("If true, try to auto-assign a mixer group (by exposed pitch param) when none is set on the AudioSource.")]
    public bool autoAssignMixerGroup = true;
    [Header("Start Delay")]
    [Tooltip("Delay in seconds before audio and note/beat spawning begin.")]
    [SerializeField] private float songStartDelaySeconds = 2f;
    private Coroutine startDelayRoutine;
    private AudioSource cachedAudioSource;
    // When StartSong defers full parsing, hold pending values here until full chart parse completes
    private float pendingScheduledDelay = 0f;
    private string pendingChartFileName = null;
    private AudioClip pendingSongClip = null;
    private string pendingSongDisplayName = null;
    private AudioClip pendingPianoClip = null;
    private string pendingPianoResourcePath = null;

    void Awake()
    {
        // Force auto-stop even if prefab/scene serialized the old default
        autoStopOnChartEnd = true;
        // Singleton pattern to ensure only one GameManager exists
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Get the Conductor component attached to this GameObject
        Conductor = GetComponent<Conductor>();
        cachedAudioSource = GetComponent<AudioSource>();
        NoteSpawner = GetComponent<NoteSpawner>();
        BeatLineSpawner = GetComponent<BeatLineSpawner>();
        UIManager = FindFirstObjectByType<UIManager>(); // Find the UIManager in the scene
        
        // Initialize HitSoundManager (will be enabled later when Unity compiles the new script)
        /*
        HitSoundManager = FindFirstObjectByType<HitSoundManager>();
        if (HitSoundManager == null)
        {
            // Create HitSoundManager if it doesn't exist
            GameObject hitSoundObj = new GameObject("HitSoundManager");
            HitSoundManager = hitSoundObj.AddComponent<HitSoundManager>();
            BuildLogger.Log("GameManager: Created HitSoundManager automatically");
        }
        */
        
        // Always find GameplayRoot by name to ensure correct reference
        if (gameplayRoot == null)
        {
            GameObject foundGameplayRoot = GameObject.Find("GameplayRoot");
            if (foundGameplayRoot != null)
            {
                gameplayRoot = foundGameplayRoot;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                //Debug.Log($"GameManager.Awake: Found and assigned GameplayRoot: {gameplayRoot.name}");
#endif
            }
            else
            {
                gameplayRoot = gameObject; // Last resort fallback
            }
        }

        // Ensure mixer routing is set even when spawned at runtime
        TryAutoAssignMixerGroup(cachedAudioSource);
    }

    // Attempt lightweight sanitization to remove common problematic tokens
    // that break Unity's JsonUtility (trailing commas, stray control chars).
    private string SanitizeJsonText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Remove control chars except for common whitespace (tab, LF, CR)
        System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 0x20 || c == '\n' || c == '\r' || c == '\t') sb.Append(c);
            // else skip
        }
        string cleaned = sb.ToString();
        try
        {
            // Heuristic replacements to remove trailing commas before ] or }
            cleaned = cleaned.Replace(",\r\n]", "]");
            cleaned = cleaned.Replace(",\n]", "]");
            cleaned = cleaned.Replace(",\r]", "]");
            cleaned = cleaned.Replace(", ]", "]");
            cleaned = cleaned.Replace(",\r\n}", "}");
            cleaned = cleaned.Replace(",\n}", "}");
            cleaned = cleaned.Replace(",\r}", "}");
            cleaned = cleaned.Replace(", }", "}");
        }
        catch { }
        return cleaned;
    }

    // Try to extract header fields with regex when JsonUtility fails.
    private bool TryExtractHeaderWithRegex(string text, out ChartHeader hdr)
    {
        hdr = null;
        if (string.IsNullOrEmpty(text)) return false;
        try
            {
                var re = new System.Text.RegularExpressions.Regex(@"""first_bpm""\s*:\s*([0-9+\-.eE]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
            var m = re.Match(text);
            float bpm = 0f;
            if (m.Success)
            {
                float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bpm);
            }
            int num = 4, den = 4;
                var reNum = new System.Text.RegularExpressions.Regex(@"""time_signature_numerator""\s*:\s*([0-9]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
            var mNum = reNum.Match(text);
            if (mNum.Success) int.TryParse(mNum.Groups[1].Value, out num);
                var reDen = new System.Text.RegularExpressions.Regex(@"""time_signature_denominator""\s*:\s*([0-9]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
            var mDen = reDen.Match(text);
            if (mDen.Success) int.TryParse(mDen.Groups[1].Value, out den);
            double finish = 0.0;
                var reFinish = new System.Text.RegularExpressions.Regex(@"""music_finish_time_msec""\s*:\s*([0-9+\-.eE]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
            var mFin = reFinish.Match(text);
            if (mFin.Success) double.TryParse(mFin.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out finish);

            hdr = new ChartHeader()
            {
                first_bpm = bpm,
                time_signature_numerator = num,
                time_signature_denominator = den,
                music_finish_time_msec = (float)finish
            };
            return true;
        }
        catch { hdr = null; return false; }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private static bool LooksLikeChartJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\uFEFF' || char.IsWhiteSpace(c)) continue;
            return c == '{';
        }
        return false;
    }

    /// <summary>
    /// Resources paths omit extensions. When a source XML and the generated JSON
    /// share a basename, Resources.Load may return either TextAsset. Prefer the
    /// JSON-shaped candidate deterministically.
    /// </summary>
    internal static TextAsset LoadChartJsonAsset(string chartFile)
    {
        if (string.IsNullOrWhiteSpace(chartFile)) return null;
        string externalPath = ExternalSongLibrary.ToLocalPath(chartFile);
        if (!string.IsNullOrEmpty(externalPath))
        {
            try
            {
                if (!File.Exists(externalPath))
                {
                    BuildLogger.LogError($"[GameManager] External chart does not exist: '{externalPath}'.");
                    return null;
                }
                string externalJson = File.ReadAllText(externalPath);
                if (!LooksLikeChartJson(externalJson))
                {
                    BuildLogger.LogError($"[GameManager] External chart is not JSON: '{externalPath}'.");
                    return null;
                }
                return new TextAsset(externalJson) { name = Path.GetFileNameWithoutExtension(externalPath) };
            }
            catch (System.Exception ex)
            {
                BuildLogger.LogError($"[GameManager] Failed reading external chart '{externalPath}': {ex.Message}");
                return null;
            }
        }
        string normalized = chartFile.Replace('\\', '/');
        int extension = normalized.LastIndexOf('.');
        int slash = normalized.LastIndexOf('/');
        if (extension > slash) normalized = normalized.Substring(0, extension);

        TextAsset direct = Resources.Load<TextAsset>(normalized);
        if (direct != null && LooksLikeChartJson(direct.text)) return direct;

        slash = normalized.LastIndexOf('/');
        string folder = slash >= 0 ? normalized.Substring(0, slash) : string.Empty;
        string basename = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        TextAsset[] candidates = Resources.LoadAll<TextAsset>(folder);
        for (int i = 0; i < candidates.Length; i++)
        {
            TextAsset candidate = candidates[i];
            if (candidate == null || !string.Equals(candidate.name, basename,
                    System.StringComparison.OrdinalIgnoreCase)) continue;
            if (LooksLikeChartJson(candidate.text))
            {
                if (direct != null && direct != candidate)
                    BuildLogger.LogWarning($"[GameManager] Resource basename collision for '{normalized}'; selected JSON instead of non-JSON TextAsset.");
                return candidate;
            }
        }

        if (direct != null)
        {
            string trimmed = direct.text != null ? direct.text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n') : string.Empty;
            string first = trimmed.Length > 0 ? trimmed.Substring(0, 1) : "<empty>";
            BuildLogger.LogError($"[GameManager] Chart resource '{normalized}' resolved to non-JSON content beginning with '{first}'.");
        }
        return null;
    }

    bool LoadChartData(string chartFile)
    {
        // Never let a deferred load reuse the previous song for even one frame.
        // Without this reset, CurrentChart remains non-null while the new header
        // is parsed, so StartSong initializes both spawners with the song that
        // was selected immediately before this one.
        CurrentChart = null;
        CurrentChartHeader = null;
        try { BuildLogger.Log($"[GameManager] LoadChartData: attempting Resources.Load<TextAsset>('{chartFile}')"); } catch { }
        TextAsset jsonFile = LoadChartJsonAsset(chartFile);
        try { BuildLogger.Log($"[GameManager] LoadChartData: Resources.Load returned {(jsonFile==null?"null":"TextAsset")}"); } catch { }
        if (jsonFile != null) { try { BuildLogger.Log($"[GameManager] LoadChartData: loaded text length={ (jsonFile.text!=null ? jsonFile.text.Length : 0) }"); } catch { } }
        if (jsonFile == null)
        {
            CurrentChart = null;
            try { BuildLogger.LogWarning($"[GameManager] LoadChartData: Resources.Load<TextAsset> returned null for '{chartFile}'"); } catch { }
            return false;
        }
        string chartJson = SanitizeJsonText(jsonFile.text);

        // Fast path: parse only header fields so UI/BPM can be shown quickly.
        try
        {
            CurrentChartHeader = JsonUtility.FromJson<ChartHeader>(chartJson);
            try { BuildLogger.Log($"[GameManager] LoadChartData: header parse {(CurrentChartHeader!=null?"OK":"null")} first_bpm={(CurrentChartHeader!=null?CurrentChartHeader.first_bpm:0f)}"); } catch { }
        }
        catch (System.Exception ex)
        {
            CurrentChartHeader = null;
            try { BuildLogger.LogWarning($"[GameManager] LoadChartData: header parse threw: {ex.Message}"); } catch { }
            // try regex fallback to at least extract BPM / time signature
            try
            {
                ChartHeader fallback;
                if (TryExtractHeaderWithRegex(chartJson, out fallback))
                {
                    CurrentChartHeader = fallback;
                    try { BuildLogger.Log($"[GameManager] LoadChartData: header extracted by regex first_bpm={CurrentChartHeader.first_bpm}"); } catch { }
                }
            }
            catch { }
            // Newtonsoft.Json fallback for header
            try
            {
                var header = Newtonsoft.Json.JsonConvert.DeserializeObject<ChartHeader>(chartJson);
                if (header != null)
                {
                    CurrentChartHeader = header;
                    BuildLogger.LogWarning($"[GameManager] LoadChartData: Newtonsoft.Json header parse OK first_bpm={header.first_bpm}");
                }
            }
            catch (System.Exception nex)
            {
                BuildLogger.LogError($"[GameManager] LoadChartData: Newtonsoft.Json header parse failed: {nex.Message}");
            }
        }

        if (CurrentChartHeader == null)
        {
            // As a fallback, try full parse (if header mapping doesn't work for some charts)
            try
            {
                CurrentChart = JsonUtility.FromJson<Chart>(chartJson);
                FinaliseLoadedChart(chartFile);
                try { BuildLogger.Log($"[GameManager] LoadChartData: full parse {(CurrentChart!=null?"OK":"null")} notes={(CurrentChart!=null? (CurrentChart.notes!=null?CurrentChart.notes.Count:0):0)}"); } catch { }
            }
            catch (System.Exception ex)
            {
                CurrentChart = null;
                try { BuildLogger.LogWarning($"[GameManager] LoadChartData: full parse threw: {ex.Message}"); } catch { }
                // Newtonsoft.Json fallback for full chart
                try
                {
                    var chart = Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(chartJson);
                    if (chart != null)
                    {
                        CurrentChart = chart;
                        FinaliseLoadedChart(chartFile);
                        BuildLogger.LogWarning($"[GameManager] LoadChartData: Newtonsoft.Json full parse OK notes={(chart.notes!=null?chart.notes.Count:0)}");
                    }
                }
                catch (System.Exception nex)
                {
                    BuildLogger.LogError($"[GameManager] LoadChartData: Newtonsoft.Json full parse failed: {nex.Message}");
                }
            }
            if (CurrentChart == null)
            {
                return false;
            }
            // Set conductor BPM from full chart if header parse failed
            Conductor.bpm = CurrentChart.first_bpm;
        }
        else
        {
            // We have a header; apply BPM immediately and kick off background parse for full chart
            Conductor.bpm = CurrentChartHeader.first_bpm;
            // Start background parse on next frame(s) to avoid blocking startup. The coroutine will
            // replace CurrentChart when complete and continue initialization via OnFullChartParsed.
            try
            {
                int generation = ++chartLoadGeneration;
                chartParseRoutine = StartCoroutine(ParseChartInBackground(chartJson, chartFile, generation));
            }
            catch { }
        }

        // Hand the keysound its sustain pedal now rather than waiting for the
        // background full parse: the header path is the one a healthy chart
        // takes, and a few seconds of a song with no sustain is very audible.
        try
        {
            var spans = CurrentChartHeader != null && CurrentChartHeader.pedal_data != null
                ? CurrentChartHeader.pedal_data
                : CurrentChart?.pedal_data;
            var voices = PianoVoiceManager.EnsureCreated();
            if (spans != null && spans.Count > 0)
            {
                voices?.SetPedalData(spans);
                try { BuildLogger.Log($"[GameManager] LoadChartData: pedal spans={spans.Count}"); } catch { }
            }
            else if (voices != null)
            {
                // No pedal in the source MIDI. Build a substitute from the beats
                // so the option is there; the voice manager only reaches for it
                // when the player has asked for one.
                voices.SetPedalData(null);
                var beats = CurrentChartHeader != null && CurrentChartHeader.beat_timings != null
                    ? CurrentChartHeader.beat_timings
                    : CurrentChart?.beat_timings;
                int beatsPerBar = CurrentChartHeader != null && CurrentChartHeader.time_signature_numerator > 0
                    ? CurrentChartHeader.time_signature_numerator
                    : 4;
                float bpm = CurrentChartHeader != null && CurrentChartHeader.first_bpm > 0f
                    ? CurrentChartHeader.first_bpm
                    : (CurrentChart != null ? CurrentChart.first_bpm : 0f);
                // beat_timings 有的譜面是一小節一條、有的是一個四分音符一條，
                // 換算過才知道幾條算一小節（見 AutoPedal.EntriesPerBar）。
                voices.SetSubstitutePedalSource(
                    beats, AutoPedal.EntriesPerBar(beats, bpm, beatsPerBar), CurrentChart?.notes);
                try { BuildLogger.Log($"[GameManager] LoadChartData: no pedal data; " +
                                      $"beats available={(beats != null ? beats.Count : 0)}"); } catch { }
            }

            // The pedal notes resolve chart-versus-substitute through the voice
            // manager, so they only have to be told that the chart changed. Null here
            // simply means there is no track to draw on yet.
            PedalNoteRenderer.GetOrCreate()?.Rebuild();

            // 背景的顏色來自封面、隨調性中心走。封面這裡可能還沒載好，傳 null 就
            // 沿用上一首的底色，等 GameBackgroundManager 載完會再叫一次。
            try
            {
                Texture cover = null;
                var backgrounds = FindAnyObjectByType<GameBackgroundManager>();
                if (backgrounds != null && backgrounds.backgroundImage != null)
                    cover = backgrounds.backgroundImage.texture;
                Effects.BackgroundHarmonyDriver.GetOrCreate()?.Rebuild(CurrentChart, cover);
            }
            catch { }
        }
        catch { }

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            var snippet = jsonFile.text.Length > 200 ? jsonFile.text.Substring(0, 200) + "..." : jsonFile.text;
            var bpmVal = CurrentChart != null ? CurrentChart.first_bpm : (CurrentChartHeader != null ? CurrentChartHeader.first_bpm : 0f);
            BuildLogger.Log($"[GameManager] LoadChartData('{chartFile}') parsed first_bpm={bpmVal} ; jsonSnippet={snippet}");
        }
        catch { }
        #endif
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        //Debug.Log($"Successfully loaded chart '{chartFile}' with {CurrentChart.notes.Count} notes. BPM: {Conductor.bpm}");
        #endif
        return true;
    }

    public void StartSong(string selectedChartFile, AudioClip selectedClip, string displayName = null,
        AudioClip pianoClip = null, string pianoResourcePath = null, string mainResourcePath = null)
    {
        try { BuildLogger.Log($"[GameManager] StartSong called with chartFile='{selectedChartFile}' displayName='{displayName}' audioClip={(selectedClip!=null?selectedClip.name:"null")} pianoResourcePath='{pianoResourcePath}'"); } catch { }
        // Cancel any pending delayed start
        if (startDelayRoutine != null)
        {
            StopCoroutine(startDelayRoutine);
            startDelayRoutine = null;
        }

        // Reset judgement/gameplay statistics at start of song to ensure a clean run
        // This clears perfect/great/good/miss/fail counters so Results and UI start from zero.
        try { Judgment.JudgmentManager.Instance?.ResetStats(); } catch { }

        if (Conductor == null)
        {
            Conductor = GetComponent<Conductor>();
            if (Conductor == null)
            {
                return;
            }
        }

        // Fully cleanup any existing gameplay resources from a previous run
        CleanupAllResources();
        // Cleanup clears pending entries from the previous chart. Re-enable
        // protection immediately for the new chart so the persistent manager
        // cannot remain disabled after the first song transition.
        try
        {
            bool enabled = SettingsManager.Instance == null || SettingsManager.Instance.ProtectionEnabled;
            Judgment.Protection.SetProtectionEnabled(enabled);
        }
        catch { }

        if (Conductor != null)
        {
            if (Conductor.audioSync != null && Conductor.audioSync.audioSource != null)
            {
                cachedAudioSource = Conductor.audioSync.audioSource;
            }
            else
            {
                cachedAudioSource = Conductor.GetComponent<AudioSource>();
            }
            if (cachedAudioSource != null)
            {
                baseMusicVolume = cachedAudioSource.volume;
            }
        }

        // Reset and populate per-track audio manage windows from the selected option
            currentAudioManageMain = null;
            currentAudioManagePiano = null;
            currentAudioSpeedFactor = 1f;
            currentUseMixer = false;
            currentAudioSpeedEvents = null;
            currentPianoSpeedFactor = 1f;
            currentPianoUseMixer = false;
            currentPianoSpeedEvents = null;
        if (SongSelectionManager.Instance != null)
        {
            var sel = SongSelectionManager.Instance.GetSelectedSong();
            if (sel != null)
            {
                currentAudioManageMain = sel.audioManageMain;
                currentAudioManagePiano = sel.audioManagePiano;
                currentAudioSpeedFactor = sel.audioSpeedFactor;
                currentUseMixer = sel.useMixer;
                currentAudioSpeedEvents = sel.audioSpeedEvents;
                currentPianoSpeedFactor = sel.pianoAudioSpeedFactor;
                currentPianoUseMixer = sel.pianoUseMixer;
                currentPianoSpeedEvents = sel.pianoAudioSpeedEvents;
            }
        }
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try { BuildLogger.Log($"[GameManager] StartSong speedFactor={currentAudioSpeedFactor:F3} audioManageWindows={(currentAudioManageMain!=null?currentAudioManageMain.Count:0)}"); } catch { }
    #endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        try { BuildLogger.Log($"[GameManager] StartSong speedFactor={currentAudioSpeedFactor:F3} audioManageWindows={(currentAudioManageMain!=null?currentAudioManageMain.Count:0)}"); } catch { }
#endif

        CancelPerSongCoroutines();

        // 從按下選歌的那一刻就蓋住。延後解析那條路要等背景解析完成才會進到
        // BeginGameplayWhenReady，中間那段空窗不遮的話玩家會先看到半成品的軌道。
        try
        {
            GameplayLoadingScreen screen = GameplayLoadingScreen.EnsureCreated();
            DressLoadingScreen(screen);
            screen.Show(Localize.T("準備中…", "准备中…", "Preparing…"));
        }
        catch { }

        chartFileName = selectedChartFile;
        CurrentChart = null;
        CurrentChartHeader = null;
        bool externalMainRequested = ExternalSongLibrary.ToLocalPath(mainResourcePath) != null;
        currentSongClip = externalMainRequested
            ? selectedClip
            : selectedClip != null ? selectedClip : songClip;
        currentSongResourcePath = mainResourcePath;
        currentSongDisplayName = string.IsNullOrEmpty(displayName) ? chartFileName : displayName;
        currentPianoResourcePath = pianoResourcePath;
        currentPianoClip = ResolvePianoClip(pianoClip, pianoResourcePath);

        if (!LoadChartData(chartFileName))
        {
            return;
        }

        // If LoadChartData returned with only a header parsed, defer heavy initialization
        // until the background parser completes. Store pending parameters and return.
        if (CurrentChart == null && CurrentChartHeader != null)
        {
            // Compute scheduled delay now and store pending state so OnFullChartParsed can continue
            float computedScheduledDelay = CountInSeconds;

            pendingScheduledDelay = computedScheduledDelay;
            pendingChartFileName = chartFileName;
            pendingSongClip = currentSongClip;
            pendingSongDisplayName = currentSongDisplayName;
            pendingPianoClip = currentPianoClip;
            pendingPianoResourcePath = currentPianoResourcePath;

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            BuildLogger.Log($"[GameManager] StartSong: deferred full chart parse and initialization for '{chartFileName}' (header-only available). ScheduledDelay={computedScheduledDelay:F2}");
            #endif
            return;
        }

        try
        {
            StatsManager.Instance.ConfigureExpectedJudgments(CurrentChart);
        }
        catch { }

        // Ensure spawners exist
        if (NoteSpawner == null)
        {
            NoteSpawner = GetComponent<NoteSpawner>();
            if (NoteSpawner == null)
            {
                NoteSpawner = FindFirstObjectByType<NoteSpawner>();
            }
        }
        if (BeatLineSpawner == null)
        {
            BeatLineSpawner = GetComponent<BeatLineSpawner>();
            if (BeatLineSpawner == null)
            {
                BeatLineSpawner = FindFirstObjectByType<BeatLineSpawner>();
            }
        }

        if (NoteSpawner == null || BeatLineSpawner == null)
        {
            return;
        }

        // 直接路徑（譜面一開始就是完整的，不走背景解析）也要擋在載入頁後面，
        // 理由跟 OnFullChartParsed 那條一樣：重活不能發生在音符已經在跑的時候。
        StartCoroutine(BeginGameplayWhenReady(ContinueDirectStart));
    }

    /// <summary>譜面一開始就完整的那條路徑，原本 StartSong 的尾段。</summary>
    private void ContinueDirectStart()
    {
    // 外部音檔的解碼要在前奏開始**之前**啟動。
    //
    // 這兩個檔是 140 秒左右的 WAV，解出來各約 50MB 的 PCM，建立 AudioClip 那一下
    // 是主執行緒上 60~90ms 的尖峰（探針量到 frame=93.8ms、heapDelta=+16.9MB，
    // scripts=0.0ms）。原本它是在 BeginSongAfterDelay 裡做的，而那個協程是在
    // StartVisualPreRoll 之後才啟動——等於整個解碼都落在玩家看得到的前奏裡，
    // 音符已經在跑了才卡。搬到這裡之後它跟譜面初始化重疊，那時候畫面還是靜的。
    BeginExternalAudioPreload();

    // Activate gameplay root and start visual pre-roll BEFORE initializing spawners so they
    // see an active Conductor (pre-roll) and can spawn pre-roll/negative-time beats immediately.
    SetGameplayRootActive(true);
    float appliedDelay = CountInSeconds;
    float scheduledDelay = appliedDelay;

    // Preload shared note resources (sprites, sounds) before starting visual pre-roll to avoid hitches
    try
    {
        if (notePrefab2D != null)
        {
            NoteController.PreloadSharedResources(notePrefab2D);
        }
    }
    catch { }

    if (scheduledDelay > 0.0f)
    {
        scheduledDelay += ArmRollIn();
        Conductor.StartVisualPreRoll(scheduledDelay);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Convert project-wide playback offset (ms) to seconds for logging
    float playbackOffsetSec = 0f;
    try
    {
        var sm = SettingsManager.Instance;
        playbackOffsetSec = (sm != null ? sm.MusicPlaybackOffsetMs : 0f) * 0.001f;
    }
    catch { playbackOffsetSec = 0f; }

    BuildLogger.Log($"[GameManager] StartSong: appliedDelay={appliedDelay:F2}, offset={playbackOffsetSec:F3}, scheduledDelay={scheduledDelay:F2}, Conductor.isActive={Conductor.isActive}");
#endif

    NoteSpawner.Initialize(CurrentChart, Conductor);
    BeatLineSpawner.Initialize(CurrentChart, Conductor);

    try { Judgment.JudgmentManager.Instance?.EnsureHudVisible(); } catch { }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Report spawners' view of the conductor after Initialize
    bool nsConductorActive = NoteSpawner != null && NoteSpawner.Conductor != null ? NoteSpawner.Conductor.isActive : false;
    bool bsExists = BeatLineSpawner != null;
    BuildLogger.Log($"[GameManager] After Initialize: GameManager.Conductor.isActive={Conductor.isActive}, NoteSpawner.Conductor.isActive={nsConductorActive}, BeatLineSpawner.exists={bsExists}");
#endif

    // If spawners still see the conductor as inactive, try re-triggering visual pre-roll as a defensive fallback.
    if (!Conductor.isActive)
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        BuildLogger.LogWarning("[GameManager] Conductor not active after Initialize — re-invoking StartVisualPreRoll as fallback");
        #endif
        Conductor.StartVisualPreRoll(scheduledDelay);
    }

        // Ensure settings are applied before spawning notes (speed, delay, etc.)
        if (SettingsManager.Instance != null)
        {
            SettingsManager.Instance.ApplySettings();
        }

        if (UIManager == null)
        {
            UIManager = FindFirstObjectByType<UIManager>();
        }

        if (UIManager != null)
        {
            // UIManager knows how to push its currentSpeed to spawners
            UIManager.ApplyCurrentSpeedToSpawners();
        }
        else
        {
            // As a fallback (UIManager not present), apply DefaultSpeed directly to spawners
            float defaultSpeed = SettingsManager.Instance != null ? SettingsManager.Instance.DefaultSpeed : 30f;
            if (NoteSpawner != null)
            {
                NoteSpawner.speed = defaultSpeed;
            }
            if (BeatLineSpawner != null)
            {
                BeatLineSpawner.Speed = defaultSpeed;
            }
        }

        // Integrate GameBackgroundManager to load background
        var backgroundManager = FindAnyObjectByType<GameBackgroundManager>(); // 更新為建議的方法
        if (backgroundManager != null)
        {
            var selectedSong = SongSelectionManager.Instance?.GetSelectedSong();
            if (selectedSong != null)
            {
                // Prefer using the refresh helper so title/author parsing and fallback logic run
                backgroundManager.RefreshFromSelected();
            }
        }

        // Begin the song after the visual pre-roll; the coroutine handles whether audio is present.
        startDelayRoutine = StartCoroutine(BeginSongAfterDelay(scheduledDelay));
    }

    public void RestartSong()
    {
        if (string.IsNullOrEmpty(chartFileName))
        {
            return;
        }

        // Stop the music
        Conductor.Stop();

        // Cancel any pending delayed start
        if (startDelayRoutine != null)
        {
            StopCoroutine(startDelayRoutine);
            startDelayRoutine = null;
        }

        ClearSpawnedObjects();

        // Restart the song
        StartSong(chartFileName, currentSongClip, currentSongDisplayName, currentPianoClip,
            currentPianoResourcePath, currentSongResourcePath);
        
        // Hide the restart button again
        if (UIManager != null)
        {
        }
    }

    public void ReselectSong()
    {
        // Ensure everything from the last run is torn down
        CleanupAllResources();
        CurrentChart = null;
        currentPianoClip = null;
        currentPianoResourcePath = null;
        if (UIManager != null)
        {
        }

        // Reset judgment stats so selections / subsequent plays start from zero
        try { Judgment.JudgmentManager.Instance?.ResetStats(); } catch { }

        SongSelectionManager.Instance?.ShowSelection();
        SetGameplayRootActive(false);
    }

    private void ClearSpawnedObjects()
    {
        // Prefer spawner-level clear operations so pools and their transient children are handled correctly
        if (NoteSpawner != null)
        {
            // Dev log removed
            NoteSpawner.ClearAllSpawned();
        }

        if (BeatLineSpawner != null)
        {
            // Dev log removed
            BeatLineSpawner.ClearAllSpawned();
        }

        try
        {
            var hitEffects = KeyHitEffectManager.TryGetExistingInstance();
            if (hitEffects != null)
            {
                hitEffects.ClearAllEffects();
            }
        }
        catch { }
    }

    /// <summary>
    /// Perform a full best-effort cleanup of gameplay resources. This will stop audio,
    /// cancel pending coroutines, clear active spawned objects, and destroy pools/containers
    /// so memory can be reclaimed when switching songs or returning to selection.
    /// </summary>
    public void CleanupAllResources()
    {
        try
        {
            // A previous song's deferred parse must never finish after a song
            // switch and overwrite CurrentChart/spawner initialization.
            chartLoadGeneration++;
            if (chartParseRoutine != null)
            {
                try { StopCoroutine(chartParseRoutine); } catch { }
                chartParseRoutine = null;
            }
            pendingChartFileName = null;
            pendingSongClip = null;
            pendingSongDisplayName = null;
            pendingPianoClip = null;
            pendingPianoResourcePath = null;
            pendingScheduledDelay = 0f;

            // These routines retain references to the previous song's AudioSources,
            // skip windows and spawners. Stop every one before resetting those objects.
            CancelPerSongCoroutines();

            // Stop playback and any piano preview
            Conductor?.Stop();
            StopPianoLayer();

            // Reset combo/score/statistics so next run starts fresh
            try { Judgment.JudgmentManager.Instance?.ResetStats(); } catch { }
            try { StatsManager.Instance?.ResetStats(); } catch { }
            try { Judgment.Protection.SetProtectionEnabled(false); } catch { }

            // Cancel startup coroutine if running
            if (startDelayRoutine != null)
            {
                try { StopCoroutine(startDelayRoutine); } catch { }
                startDelayRoutine = null;
            }

            // Clear active instances first so transient children are returned to pool
            try { ClearSpawnedObjects(); } catch { }

            // Destroy note pool and its container if present
            try
            {
                if (NoteSpawner != null)
                {
                    NoteSpawner.ClearAllSpawned();
                    NoteSpawner.DestroyPool();
                }
            }
            catch { }

            // Destroy beat line pool and its container
            try
            {
                if (BeatLineSpawner != null)
                {
                    BeatLineSpawner.ClearAllSpawned();
                    BeatLineSpawner.DestroyPool();
                }
            }
            catch { }

            // Clear UI/effect managers
            try
            {
                var hitEffects = KeyHitEffectManager.TryGetExistingInstance();
                if (hitEffects != null)
                {
                    hitEffects.ClearAllEffects();
                }
            }
            catch { }

            try
            {
                var noteJudg = NoteJudgementMeshManager.Instance;
                if (noteJudg != null)
                {
                    noteJudg.HideAllEffects();
                }
            }
            catch { }

            try { ParticleEffectPlayer.Instance?.ClearAllSongEffects(); } catch { }
            try { HitParticleManager.Instance?.ClearActiveEffects(); } catch { }
            try { IvoryLaneKeyboard.ResetAllPressedStates(); } catch { }

            // Stop the previous song's video immediately instead of waiting for the
            // next background to finish loading.
            try
            {
                var backgroundManager = FindAnyObjectByType<GameBackgroundManager>();
                backgroundManager?.OnSongEnded();
            }
            catch { }

            try
            {
                var src = cachedAudioSource != null ? cachedAudioSource : (Conductor != null ? Conductor.GetComponent<AudioSource>() : null);
                if (src != null)
                {
                    src.volume = baseMusicVolume;
                }
            }
            catch { }

        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"[GameManager] CleanupAllResources encountered error: {ex.Message}");
        }
    }

    // --- Example of accessing data ---
    void Update()
    {
        MirrorMidiKeysToKeyboard();

        // Optional auto-stop when chart time reaches end
        float endMs = 0f;
        if (CurrentChart != null)
        {
            endMs = CurrentChart.music_finish_time_msec;
        }
        else if (CurrentChartHeader != null)
        {
            endMs = CurrentChartHeader.music_finish_time_msec;
        }

        if (autoStopOnChartEnd && Conductor.isPlaying && endMs > 0f && Conductor.songPosition >= endMs)
        {
            if (SongSelectionManager.Instance != null &&
                SongSelectionManager.Instance.IsSettingsGameplayPreviewRunning)
            {
                // The settings preview owns its 30-second segment loop and
                // 10-second interval; never open Results or restart the song here.
                return;
            }
            Conductor.Stop();
            StopPianoLayer();
            var backgroundManager = FindAnyObjectByType<GameBackgroundManager>();
            if (backgroundManager != null)
            {
                backgroundManager.OnSongEnded();
            }
            if (UIManager != null)
            {
                UIManager.ShowRestartButton();
            }
            // Show results screen if available
            try
            {
                // Avoid compile-time dependency on ResultsScreen by finding component by name and invoking via reflection.
                var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                foreach (var mb in all)
                {
                    if (mb == null) continue;
                    if (mb.GetType().Name == "ResultsScreen")
                    {
                        var mi = mb.GetType().GetMethod("ShowResults");
                        if (mi != null) mi.Invoke(mb, null);
                        break;
                    }
                }
            }
            catch { }
                // Keep gameplay root visible; buttons are always visible now
        }

        // MIDI 輸入模式自動啟用/停用
        bool wantsMidi = SettingsManager.Instance != null &&
                         SettingsManager.Instance.InputMode != InputModeType.Keyboard;
        if (!midiModeStateInitialized || wantsMidi != midiModeEnabled ||
            (wantsMidi && MIDIInputManager.Instance == null))
        {
            EnsureMIDIInputManagerEnabled(wantsMidi);
            midiModeEnabled = wantsMidi;
            midiModeStateInitialized = true;
        }
        // 其他輸入已由 InputManager 處理
    }

    private bool midiSubscribed = false;
    private bool midiModeStateInitialized;
    private bool midiModeEnabled;
    private bool midiTimingPathLogged;

    private void EnsureMIDIInputManagerEnabled(bool enable)
    {
        var midiObj = MIDIInputManager.Instance;
        if (enable && midiObj == null)
            midiObj = FindFirstObjectByType<MIDIInputManager>();
        if (enable)
        {
            if (midiObj == null)
            {
                var go = new GameObject("MIDIInputManager");
                go.AddComponent<MIDIInputManager>();
            }
            if (!midiSubscribed && MIDIInputManager.Instance != null)
            {
                MIDIInputManager.Instance.OnMidiNoteOnTimed += HandleMidiNoteOnTimed;
                MIDIInputManager.Instance.OnMidiNoteOffTimed += HandleMidiNoteOffTimed;
                midiSubscribed = true;
            }
        }
        else
        {
            ReleaseAllActiveMidiLanes(midiObj);
            if (midiSubscribed && MIDIInputManager.Instance != null)
            {
                MIDIInputManager.Instance.OnMidiNoteOnTimed -= HandleMidiNoteOnTimed;
                MIDIInputManager.Instance.OnMidiNoteOffTimed -= HandleMidiNoteOffTimed;
                midiSubscribed = false;
            }
            if (midiObj != null)
            {
                Destroy(midiObj.gameObject);
            }
        }
    }

    /// <summary>
    /// Keeps the drawn keyboard equal to what the MIDI device is actually holding.
    /// </summary>
    /// <remarks>
    /// The visual used to be accumulated from note-on/note-off events, which
    /// leaks: a note-off can be lost, carry a pitch that maps to no lane, or map
    /// to a different lane than its note-on did. Any one of those leaves a key
    /// lit for the rest of the song, and its emission sits at 2.17 — well past
    /// the 1.05 bloom threshold — so it blooms into a ball of light stuck on the
    /// judgment line. Two of them were exactly that. Reading the device's own
    /// state each frame has no such failure: a key that is not held is not lit.
    ///
    /// Only the MIDI half is mirrored. Computer keys always report their release,
    /// and their state stays event-driven so nothing here can clear a real press.
    /// </remarks>
    private void MirrorMidiKeysToKeyboard()
    {
        var midi = MIDIInputManager.Instance;
        if (midi == null) return;
        for (int lane = 0; lane < 28; lane++)
        {
            bool held;
            try { held = midi.IsLanePressed(lane); }
            catch { return; }
            IvoryLaneKeyboard.SetMidiLanePressed(lane, held);
        }
    }

    // MIDI note on event handler
    private void HandleMidiNoteOnTimed(int note, int keyIndex, float velocity, double eventRealtime,
        long inputEventId)
    {
        // 這裡 keyIndex 就是 0~27，velocity 為原始力度
        // 直接呼叫 NoteSpawner 的 TryHitNote 進行判定
        if (keyIndex >= 0 && NoteSpawner != null)
        {
            IvoryLaneKeyboard.SetLanePressed(keyIndex, true);
            float preciseSongPos = float.NaN;
            if (Conductor != null)
            {
                preciseSongPos = TimingMath.ProjectSongPosToEvent(
                    Conductor.effectiveSongPosition, Conductor.TimingSampleRealtime,
                    eventRealtime, maxDeltaMs: 500.0, fallback: float.NaN);
                if (!float.IsNaN(preciseSongPos))
                {
                    float judgmentOffset = SettingsManager.Instance != null
                        ? SettingsManager.Instance.JudgmentOffsetMs
                        : 0f;
                    preciseSongPos += judgmentOffset;
                }
            }
            if (!midiTimingPathLogged)
            {
                midiTimingPathLogged = true;
                double ageMs = System.Math.Max(0.0,
                    (Time.realtimeSinceStartupAsDouble - eventRealtime) * 1000.0);
                float projectionMs = Conductor != null && !float.IsNaN(preciseSongPos)
                    ? preciseSongPos - Conductor.effectiveSongPosition
                    : 0f;
                Debug.Log($"[MIDI Input Path] age={ageMs:0.00}ms, " +
                    $"projection={projectionMs:+0.00;-0.00;0.00}ms");
            }
            // Judgment only carries a lane, so stash what the key actually did
            // before judging — Hardcore mode and the wrong-key sound read it back.
            try { PianoKeysound.RecordInput(keyIndex, note, velocity); } catch { }
            NoteSpawner.TryHitNote(keyIndex, velocity, preciseSongPos, inputEventId);
        }
    }

    private void ReleaseAllActiveMidiLanes(MIDIInputManager midi)
    {
        if (midi == null) return;
        float songPos = Conductor != null ? Conductor.effectiveSongPosition : 0f;
        float judgmentOffset = SettingsManager.Instance != null
            ? SettingsManager.Instance.JudgmentOffsetMs
            : 0f;
        for (int lane = 0; lane < 28; lane++)
        {
            if (!midi.IsLanePressed(lane)) continue;
            IvoryLaneKeyboard.SetLanePressed(lane, false);
            try { KeyHitEffectManager.Instance?.HidePersistentMeshForKeyId(lane); } catch { }
            try { PianoKeysound.ReleaseLane(lane); } catch { }
            try
            {
                Judgment.JudgmentManager.Instance?.HandleHeldKeyRelease(
                    lane, songPos + judgmentOffset);
            }
            catch { }
        }
    }

    // MIDI note off event handler
    private void HandleMidiNoteOffTimed(int note, int keyIndex, float velocity, double eventRealtime,
        long inputEventId)
    {
        // Before anything lane-based: a piano string belongs to the key, not to
        // the lane it is drawn in. Roughly three keys share a lane, so releasing
        // only when the whole lane is free would leave every other voice ringing.
        try { PianoKeysound.ReleasePitch(note); } catch { }

        if (keyIndex >= 0)
        {
            bool laneStillPressed = MIDIInputManager.Instance != null && MIDIInputManager.Instance.IsLanePressed(keyIndex);
            IvoryLaneKeyboard.SetLanePressed(keyIndex, laneStillPressed);
            // Another MIDI channel/key can still own this visual lane. Only
            // release gameplay and effects when its aggregate press count is zero.
            if (laneStillPressed) return;
            try { KeyHitEffectManager.Instance?.HidePersistentMeshForKeyId(keyIndex); }
            catch { }
            // Hardcore mode damps on the player's key-up rather than the chart's
            // gate time; the pedal can still hold the note past this point.
            try { PianoKeysound.ReleaseLane(keyIndex); } catch { }

            // Notify JudgmentManager about a potential held-key release so hold notes can finalize
            try
            {
                if (Judgment.JudgmentManager.Instance != null)
                {
                    // Match the judgment clock (effectiveSongPosition + offset) so MIDI hold release
                    // finalizes on the same time base as keyboard input and auto-miss.
                    float songPos = Conductor != null ? Conductor.effectiveSongPosition : 0f;
                    if (Conductor != null)
                    {
                        float subFrameSongPos = TimingMath.ProjectSongPosToEvent(
                            songPos, Conductor.TimingSampleRealtime, eventRealtime,
                            maxDeltaMs: 500.0);
                        if (subFrameSongPos >= 0f) songPos = subFrameSongPos;
                    }
                    float judgmentOffset = SettingsManager.Instance != null ? SettingsManager.Instance.JudgmentOffsetMs : 0f;
                    Judgment.JudgmentManager.Instance.HandleHeldKeyRelease(keyIndex, songPos + judgmentOffset);
                }
            }
            catch { }
        }
    }
    private void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnKeyPressed += HandleKeyPressed;
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnKeyPressed -= HandleKeyPressed;
    }

    private void HandleKeyPressed(UnityEngine.InputSystem.Key key)
    {
        // 範例：偵測空白鍵
        if (key == UnityEngine.InputSystem.Key.Space)
        {
            //Debug.Log($"Current Song Time: {Conductor.songPosition:F0} ms");
        }

        // F2：俯視看譜。F 排不在鍵道表裡（鍵道是 A–Z 加 9、0），所以按它不會同時
        // 彈出一個音。
        if (key == UnityEngine.InputSystem.Key.F2)
        {
            try { ChartOverviewViewer.EnsureCreated()?.Toggle(); } catch { }
        }

        // F3：把畫面上「材質壞掉」的物件列出來。洋紅色是 Unity 的錯誤材質，光看畫
        // 面分不出是誰的，這個鍵直接問場景。
        if (key == UnityEngine.InputSystem.Key.F3)
        {
            try { ReportBrokenMaterials(); } catch { }
        }
        // 你可以在這裡加入更多鍵位判斷與遊戲邏輯
    }

    /// <summary>找出用錯誤材質（洋紅）畫出來的東西，連路徑和位置一起印出來。</summary>
    private static void ReportBrokenMaterials()
    {
        var stage = FindFirstObjectByType<ChartOverviewStage>(FindObjectsInactive.Include);
        Debug.LogWarning($"[材質診斷] 預覽舞台={(stage != null ? $"存在，音符 {stage.BuiltNotes}" : "沒有")}");

        int reported = 0;
        var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                bool broken = material == null || material.shader == null ||
                              material.shader.name.Contains("InternalError") ||
                              !material.shader.isSupported;
                if (!broken) continue;

                string path = renderer.name;
                for (Transform t = renderer.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Debug.LogWarning($"[材質診斷] {path}  material={(material != null ? material.name : "null")}  " +
                                 $"shader={(material != null && material.shader != null ? material.shader.name : "null")}  " +
                                 $"layer={LayerMask.LayerToName(renderer.gameObject.layer)}  pos={renderer.transform.position}");
                if (++reported >= 20) return;
            }
        }
        if (reported == 0) Debug.LogWarning("[材質診斷] 沒有找到壞掉的材質。");
    }

    public void SetGameplayRootActive(bool isActive)
    {
        if (gameplayRoot != null)
        {
            gameplayRoot.SetActive(isActive);
        }
    }

    public void SetPianoLayerVolume(float volume)
    {
        if (pianoAudioSource != null)
        {
            pianoAudioSource.volume = Mathf.Clamp01(volume);
        }
    }

    private void CancelPerSongCoroutines()
    {
        // 已經排進 DSP 佇列的鼓棒聲不會因為協程被停掉就不響 —— 排程是音訊執行緒
        // 的事。退出、換歌、重開都要明確地收掉，否則會在選歌畫面聽到三下。
        try { CountInSticks.Cancel(); } catch { }
        StopTrackedCoroutine(ref startDelayRoutine);
        StopTrackedCoroutine(ref audioManageRoutine);
        StopTrackedCoroutine(ref pianoAudioManageRoutine);
        StopTrackedCoroutine(ref speedEventsRoutine);
        StopTrackedCoroutine(ref pianoSpeedEventsRoutine);
        StopTrackedCoroutine(ref skipSectionsRoutine);
    }

    private void StopTrackedCoroutine(ref Coroutine routine)
    {
        if (routine == null) return;
        try { StopCoroutine(routine); } catch { }
        routine = null;
    }

    public void PrepareSettingsPreviewSegment(float targetMs)
    {
        if (Conductor == null) return;
        targetMs = Mathf.Max(0f, targetMs);
        Conductor.Pause();
        if (pianoAudioSource != null && pianoAudioSource.isPlaying) pianoAudioSource.Pause();
        Conductor.SeekToMs(targetMs);
        if (pianoAudioSource != null && pianoAudioSource.clip != null)
        {
            float maxTime = Mathf.Max(0f, pianoAudioSource.clip.length - 0.01f);
            pianoAudioSource.time = Mathf.Clamp(targetMs * 0.001f, 0f, maxTime);
        }
        NoteSpawner?.SeekToMs(targetMs);
        BeatLineSpawner?.SeekToMs(targetMs);

        // 跳過去之前，把鋼琴這一側整個歸零。
        //
        // **這是設定預覽會爆音的原因。** 排進去的隱藏音、顫音、還在響的聲部，
        // 它們的時間戳都留在舊的位置；跳到後面之後那些時間全部變成「過去」，於是
        // 下一幀一次補完 —— 幾十顆音同時發聲，總和直接破表。
        //
        // 一次 seek 就是「從這裡重新開始」，而重新開始的意思包括**之前排的東西
        // 都不算數了**。少清這一邊，等於讓上一段的尾巴掉進新的段落裡。
        try
        {
            PianoVoiceManager.Instance?.StopAll();
            PianoKeysound.ClearAllLanes();
        }
        catch { }

        RestartTimedAudioRoutinesAfterSeek(targetMs);
        try { Judgment.JudgmentManager.Instance?.ResetStats(); } catch { }
        try { StatsManager.Instance?.ResetStats(); } catch { }
        try { Judgment.JudgmentManager.Instance?.BeginSettingsPreviewCombo(targetMs); } catch { }
        try { KeyHitEffectManager.TryGetExistingInstance()?.ClearAllEffects(); } catch { }
        Conductor.Resume();
        if (pianoAudioSource != null && pianoAudioSource.clip != null) pianoAudioSource.UnPause();
    }

    /// <summary>
    /// Rebuilds song-relative audio schedules after a seek. The original routines
    /// are based on the song's initial DSP start and otherwise keep firing at the
    /// old timestamps after the playhead has jumped.
    /// </summary>
    private void RestartTimedAudioRoutinesAfterSeek(float targetMs)
    {
        StopTrackedCoroutine(ref audioManageRoutine);
        StopTrackedCoroutine(ref pianoAudioManageRoutine);
        StopTrackedCoroutine(ref speedEventsRoutine);
        StopTrackedCoroutine(ref pianoSpeedEventsRoutine);

        AudioSource musicSource = cachedAudioSource != null
            ? cachedAudioSource
            : (Conductor != null && Conductor.audioSync != null
                ? Conductor.audioSync.audioSource
                : null);

        if (musicSource != null)
        {
            musicSource.volume = baseMusicVolume;
            musicSource.priority = PianoVoiceManager.MusicPriority;
        }
        if (pianoAudioSource != null) pianoAudioSource.volume = basePianoVolume;

        // Make every existing song-relative DSP timestamp line up with the new
        // playhead. Expired windows are filtered inside ApplyAudioManageWindows.
        double adjustedDspStart = AudioSettings.dspTime - Mathf.Max(0f, targetMs) * 0.001;
        if (currentAudioManageMain != null && currentAudioManageMain.Count > 0 && musicSource != null)
            audioManageRoutine = StartCoroutine(ApplyAudioManageWindows(
                currentAudioManageMain, adjustedDspStart, musicSource, baseMusicVolume));
        if (currentAudioManagePiano != null && currentAudioManagePiano.Count > 0 &&
            ShouldPlayGameplayPianoLayer() && pianoAudioSource != null)
            pianoAudioManageRoutine = StartCoroutine(ApplyAudioManageWindows(
                currentAudioManagePiano, adjustedDspStart, pianoAudioSource, basePianoVolume));
        if (currentAudioSpeedEvents != null && currentAudioSpeedEvents.Count > 0 && musicSource != null)
            speedEventsRoutine = StartCoroutine(ApplySpeedEvents(
                currentAudioSpeedEvents, adjustedDspStart, musicSource, true));
        if (currentPianoSpeedEvents != null && currentPianoSpeedEvents.Count > 0 &&
            ShouldPlayGameplayPianoLayer() && pianoAudioSource != null)
            pianoSpeedEventsRoutine = StartCoroutine(ApplySpeedEvents(
                currentPianoSpeedEvents, adjustedDspStart, pianoAudioSource, false));
    }

    public void PauseSettingsPreviewPlayback()
    {
        Conductor?.Pause();
        if (pianoAudioSource != null) pianoAudioSource.Pause();
        NoteSpawner?.ClearAllSpawned();
        BeatLineSpawner?.ClearAllSpawned();
        try { KeyHitEffectManager.TryGetExistingInstance()?.ClearAllEffects(); } catch { }
    }

    public void SetSettingsPreviewAudioFade(float fade)
    {
        fade = Mathf.Clamp01(fade);
        SettingsManager settings = SettingsManager.Instance;
        float music = settings != null ? settings.GameplayMusicVolume : baseMusicVolume;
        float piano = settings != null ? settings.PianoVolume : basePianoVolume;
        music = Mathf.Clamp01(music * songLoudnessGain);
        piano = Mathf.Clamp01(piano * songPianoLoudnessGain);
        Conductor?.SetMusicVolume(music * fade);
        if (pianoAudioSource != null) pianoAudioSource.volume = piano * fade;
    }

    public void OnPianoPreviewToggleChanged(bool enabled)
    {
        if (!enabled)
        {
            StopPianoLayer();
        }
    }

    private AudioSource EnsurePianoAudioSource()
    {
        if (pianoAudioSource != null)
        {
            return pianoAudioSource;
        }

        if (cachedAudioSource == null)
        {
            cachedAudioSource = GetComponent<AudioSource>();
        }
        if (cachedAudioSource == null && Conductor != null)
        {
            if (Conductor.audioSync != null && Conductor.audioSync.audioSource != null)
            {
                cachedAudioSource = Conductor.audioSync.audioSource;
            }
            else
            {
                cachedAudioSource = Conductor.GetComponent<AudioSource>();
            }
        }

        pianoAudioSource = gameObject.AddComponent<AudioSource>();
        pianoAudioSource.playOnAwake = false;
        pianoAudioSource.loop = false;
        pianoAudioSource.spatialBlend = 0f;
        // Song playback outranks every effect voice, so a dense chord can never
        // push it past the real-voice budget and mute the music.
        pianoAudioSource.priority = PianoVoiceManager.MusicPriority;
        if (cachedAudioSource != null)
        {
            pianoAudioSource.outputAudioMixerGroup = cachedAudioSource.outputAudioMixerGroup;
        }
        return pianoAudioSource;
    }

    private void StopPianoLayer()
    {
        if (pianoAudioSource != null)
        {
            pianoAudioSource.Stop();
            pianoAudioSource.clip = null;
        }
    }

    private AudioClip ResolvePianoClip(AudioClip providedClip, string resourcePath)
    {
        if (providedClip != null)
        {
            return providedClip;
        }

        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        AudioClip loaded = Resources.Load<AudioClip>(resourcePath);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (loaded == null)
        {
            BuildLogger.LogWarning($"GameManager: Unable to load piano audio at Resources path '{resourcePath}'.");
        }
#endif
        return loaded;
    }

    /// <summary>
    /// 把編輯器格式的隱藏音符併進寄主的 subNotes 再移出 notes。
    /// 譜面有三條解析路徑（header + 背景、立即 JsonUtility、Newtonsoft 後備），
    /// 三條都要過這裡，漏一條就會在那條路徑上長出多餘的音符。
    /// </summary>
    /// <summary>
    /// The one thing every load path runs once the chart is in memory.
    /// </summary>
    /// <remarks>
    /// There are three parse routes into <c>CurrentChart</c>, and each of them
    /// used to call the hidden-note merge by hand. Anything else that has to
    /// happen to a freshly loaded chart -- practice stretching, for one -- would
    /// have had to be remembered in three places, so they all come here instead.
    /// </remarks>
    private void FinaliseLoadedChart(string chartFile)
    {
        NormaliseHiddenNotes(CurrentChart, chartFile);
        // 強弱是這首曲子自己的標準，不是一組寫死的數字 —— 理由在 VelocityBands。
        VelocityBands.Prepare(CurrentChart);

        var settings = SettingsManager.Instance;
        float speed = settings != null ? settings.PracticeSpeed : 1f;
        float before = CurrentChartHeader != null ? CurrentChartHeader.music_finish_time_msec : -1f;
        float factor = speed > 0.0001f ? 1f / speed : 1f;
        if (!Mathf.Approximately(speed, 1f))
        {
            CurrentChart?.ApplyTimeStretch(factor);
        }

        // 表頭也要跟著伸縮。它不是完整譜面的摘要而是**另一份資料**，而且健康的
        // 譜面走的正是它：總長、first_bpm、踏板、節拍格線都是從這裡讀的。只改
        // 完整譜面的話，音符拉開了但曲子長度沒變、踏板踩在原來的位置。
        if (CurrentChartHeader != null && !Mathf.Approximately(speed, 1f))
        {
            CurrentChartHeader.music_finish_time_msec *= factor;
            CurrentChartHeader.first_bpm /= factor;
            if (CurrentChartHeader.beat_timings != null)
                for (int i = 0; i < CurrentChartHeader.beat_timings.Count; i++)
                    CurrentChartHeader.beat_timings[i] =
                        Mathf.RoundToInt(CurrentChartHeader.beat_timings[i] * factor);
            if (CurrentChartHeader.pedal_data != null)
            {
                for (int i = 0; i < CurrentChartHeader.pedal_data.Count; i++)
                {
                    PedalSpan span = CurrentChartHeader.pedal_data[i];
                    if (span == null) continue;
                    span.start_ms = Mathf.RoundToInt(span.start_ms * factor);
                    span.end_ms = Mathf.RoundToInt(span.end_ms * factor);
                }
            }
        }

        // 無條件印，而且印伸縮前後 —— 「沒看到這行」要能明確代表「這條路徑沒跑到」，
        // 不能同時代表「跑到了但練習模式是關的」。表頭和完整譜面是兩份資料，
        // 只有一邊被改到過一次，所以兩個都印。
        Debug.Log($"[GameManager] practiceMode={(settings != null && settings.PracticeMode)} " +
            $"speed={speed:0.000} x{factor:0.000} header {before:0}ms -> " +
            $"{(CurrentChartHeader != null ? CurrentChartHeader.music_finish_time_msec : -1f):0}ms " +
            $"chart={(CurrentChart != null ? CurrentChart.music_finish_time_msec : -1)}ms");

        // 教學課：段落時間要和音符一起伸縮，所以排在練習速度之後。不是教學的曲子
        // 會在這裡清掉上一課。
        try { TutorialSession.Attach(CurrentChart, chartFile, factor); }
        catch (System.Exception ex) { Debug.LogWarning($"[Tutorial] attach failed: {ex.Message}"); }
    }

    private static void NormaliseHiddenNotes(Chart chart, string chartFile)
    {
        if (chart == null) return;
        try
        {
            int removed = chart.NormaliseHiddenNotes();
            if (removed > 0)
            {
                var settings = SettingsManager.Instance;
                BuildLogger.Log(
                    $"[GameManager] '{chartFile}': folded {removed} hidden note(s) " +
                    $"(by hostIndex {chart.hiddenByIndex}, by fallback " +
                    $"{chart.hiddenByFallback}, no host {chart.hiddenWithoutHost}); " +
                    $"autoplay queue {chart.autoHidden.Count}; " +
                    $"PlayHiddenSubNotes={(settings == null ? "?" : settings.PlayHiddenSubNotes.ToString())}, " +
                    $"synthesisedPiano={(settings == null ? "?" : settings.UsesSynthesisedPiano.ToString())}");
            }
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning(
                $"[GameManager] NormaliseHiddenNotes failed for '{chartFile}': {ex.Message}");
        }
    }

    private bool ShouldPlayGameplayPianoLayer()
    {
        var settings = SettingsManager.Instance;
        if (settings == null) return false;
        // The performance modes synthesise every note as it is hit. Leaving the
        // recorded stem running would put a second piano underneath, playing the
        // notes the player just missed.
        if (settings.UsesSynthesisedPiano) return false;
        if (!settings.EnablePianoPreview) return false;
        return currentPianoClip != null;
    }

    /// <summary>Runs while nothing is moving yet; null once the clips are decoded.</summary>
    private Coroutine externalAudioPreloadRoutine;

    /// <summary>
    /// 開始解碼這首歌的外部音檔，不等它。
    /// </summary>
    /// <remarks>
    /// 呼叫點必須在 <c>StartVisualPreRoll</c> 之前——重點不是「早一點載」，而是
    /// 「在還沒有東西在動的時候載」。解碼那一下無論如何都會吃掉一格，差別只在
    /// 玩家看不看得出來。
    /// </remarks>
    private void BeginExternalAudioPreload()
    {
        if (externalAudioPreloadRoutine != null) return;

        bool needsSong = currentSongClip == null &&
                         ExternalSongLibrary.ToLocalPath(currentSongResourcePath) != null;
        bool needsPiano = currentPianoClip == null &&
                          ExternalSongLibrary.ToLocalPath(currentPianoResourcePath) != null;
        if (!needsSong && !needsPiano) return;

        externalAudioPreloadRoutine = StartCoroutine(PreloadExternalAudio(needsSong, needsPiano));
    }

    private IEnumerator PreloadExternalAudio(bool needsSong, bool needsPiano)
    {
        if (needsSong)
        {
            yield return LoadExternalAudioForGameplay(currentSongResourcePath,
                clip => currentSongClip = clip);
        }
        if (needsPiano)
        {
            yield return LoadExternalAudioForGameplay(currentPianoResourcePath,
                clip => currentPianoClip = clip);
        }
        externalAudioPreloadRoutine = null;
    }

    private IEnumerator LoadExternalAudioForGameplay(string resourcePath,
        System.Action<AudioClip> onLoaded)
    {
        string localPath = ExternalSongLibrary.ToLocalPath(resourcePath);
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            Debug.LogError($"[GameManager] External audio file does not exist: '{localPath}'.");
            onLoaded?.Invoke(null);
            yield break;
        }

        AudioType audioType;
        switch (Path.GetExtension(localPath).ToLowerInvariant())
        {
            case ".ogg": audioType = AudioType.OGGVORBIS; break;
            case ".mp3": audioType = AudioType.MPEG; break;
            case ".aif":
            case ".aiff": audioType = AudioType.AIFF; break;
            default: audioType = AudioType.WAV; break;
        }

        // `Uri.AbsoluteUri` 會把非 ASCII 正確地 percent-encode，但 **`+` 原封
        // 不動留著**（實測：'..._+1000ms.wav' → '..._+1000ms.wav'）。而 `+` 在
        // URL 解碼時代表空格，下載端一解就變成 '..._ 1000ms.wav'，檔案當然
        // 找不到 —— 曲庫裡「華麗なる大犬円舞曲_+1000ms.wav」和
        // 「End_Time_ele+100.wav」就是這樣載不起來的。明確編成 %2B 才沒有
        // 兩種解讀。file URI 沒有 query string，整串換掉是安全的。
        string uri = new System.Uri(localPath).AbsoluteUri.Replace("+", "%2B");
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
        {
            DownloadHandlerAudioClip handler = request.downloadHandler as DownloadHandlerAudioClip;
            if (handler != null) handler.streamAudio = false;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[GameManager] External audio load failed: '{localPath}' ({request.error}).");
                onLoaded?.Invoke(null);
                yield break;
            }

            // GetContent 才是主執行緒真正付錢的地方：140 秒的 WAV 要在這裡變成
            // 約 50MB 的 AudioClip。標記起來，下次探針就會直接說出它的名字，
            // 不會再被歸進「腳本之外」。
            AudioClip clip;
            using (HitchProbe.Measure("audioDecode"))
            {
                clip = DownloadHandlerAudioClip.GetContent(request);
            }
            if (clip != null)
            {
                clip.name = Path.GetFileNameWithoutExtension(localPath);
                if (clip.loadState == AudioDataLoadState.Loading)
                {
                    while (clip.loadState == AudioDataLoadState.Loading) yield return null;
                }
                Debug.Log($"[GameManager] External gameplay audio loaded: '{localPath}', " +
                          $"state={clip.loadState}, length={clip.length:0.###}s.");
            }
            onLoaded?.Invoke(clip);
        }
    }

    private IEnumerator EnsureAudioClipReady(AudioClip clip)
    {
        if (clip == null)
        {
            yield break;
        }

        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            yield break;
        }

        // A previous eager/background load can leave the clip in Failed state in
        // a memory-constrained Player.  Reset it before one explicit retry now
        // that this is the only song being started.
        if (clip.loadState == AudioDataLoadState.Failed)
        {
            clip.UnloadAudioData();
        }

        bool loadStarted = clip.LoadAudioData();
        Debug.Log($"[GameManager] Audio load requested: clip='{clip.name}' started={loadStarted} state={clip.loadState} length={clip.length:0.###}s channels={clip.channels} frequency={clip.frequency}");
        while (clip.loadState == AudioDataLoadState.Loading)
        {
            yield return null;
        }

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogError($"[GameManager] Audio data failed to load: clip='{clip.name}' finalState={clip.loadState}. The song will not be scheduled.");
        }
    }

    private IEnumerator BeginSongAfterDelay(float delaySeconds)
    {
        // 預載通常在前奏開始前就跑完了，所以這裡多半不會等到。等不到也沒關係：
        // 前奏本來就有兩秒，等在這裡至少不會讓解碼撞在音符正在跑的時候。
        while (externalAudioPreloadRoutine != null) yield return null;

        // 保底。不是每條進入遊戲的路徑都會經過 BeginExternalAudioPreload。
        if (currentSongClip == null &&
            ExternalSongLibrary.ToLocalPath(currentSongResourcePath) != null)
        {
            yield return LoadExternalAudioForGameplay(currentSongResourcePath,
                clip => currentSongClip = clip);
        }
        if (currentPianoClip == null &&
            ExternalSongLibrary.ToLocalPath(currentPianoResourcePath) != null)
        {
            yield return LoadExternalAudioForGameplay(currentPianoResourcePath,
                clip => currentPianoClip = clip);
        }

        AudioClip pianoClipToPlay = ShouldPlayGameplayPianoLayer() ? currentPianoClip : null;

        yield return EnsureAudioClipReady(currentSongClip);
        yield return EnsureAudioClipReady(pianoClipToPlay);

        // 響度校正在排程之前算好：量測要掃兩百個窗，而這裡還在載入頁後面，
        // 正是唯一可以慢慢做這件事的時候。之後排程一開始就不能再卡了。
        songLoudnessGain = 1f;
        songPianoLoudnessGain = 1f;
        try
        {
            // 離線量過的曲子直接用那份資料：兩軌各自對齊目標（平衡的改動有上限），
            // 而且不必在載入頁尾巴再掃一次波形。
            string mainPath = currentSongClip != null ? currentSongResourcePath : currentPianoResourcePath;
            if (LoudnessNormalizer.TryStoredGains(mainPath, currentPianoResourcePath,
                    pianoClipToPlay != null, out float storedMain, out float storedPiano))
            {
                songLoudnessGain = storedMain;
                songPianoLoudnessGain = storedPiano;
            }
            else if (currentSongClip != null)
            {
                // 鋼琴分軌只有在**真的會播**的時候才算進去。不播的分軌算進響度
                // 只會讓校正偏低，整首歌就被推得太響。
                songLoudnessGain = LoudnessNormalizer.GainFor(currentSongClip, pianoClipToPlay);
                songPianoLoudnessGain = songLoudnessGain;
            }
            else if (pianoClipToPlay != null)
            {
                // 只有鋼琴軌的曲子（沒有伴奏錄音）就拿它自己當主軌——這時候它
                // 不是分軌，它就是這首歌。
                songLoudnessGain = LoudnessNormalizer.GainFor(pianoClipToPlay);
                songPianoLoudnessGain = songLoudnessGain;
            }
            else
            {
                Debug.Log("[Loudness] no clip to measure "
                    + "(keysound-only song, or the audio never loaded).");
            }
        }
        catch (System.Exception ex)
        {
            songLoudnessGain = 1f;
            songPianoLoudnessGain = 1f;
            Debug.LogWarning($"[Loudness] measurement threw: {ex.Message}");
        }

        double dspNow = AudioSettings.dspTime;
        // delaySeconds 已經是「秒倒數 + 三拍進場」的總長（見 RollInSeconds 的呼叫
        // 端）。**音訊和預捲的時鐘必須用同一個數字** —— 把音訊往後推而時鐘沒跟著
        // 推，譜面就會滾到零、停住、等音訊，中間空一段。
        double lead = System.Math.Max(0.0, (double)delaySeconds);
        double dspStart = dspNow + lead;
        if (dspStart <= dspNow + 0.02)
        {
            dspStart = dspNow + 0.05; // 50ms safety buffer
        }

        // 秒倒數只涵蓋前面那一段；最後三拍是譜面在滾，數字不該還在跳。
        double roll = System.Math.Min(RollInSeconds(), lead);

        // 那三拍敲鼓棒。速度是聽的東西不是看的東西 —— 譜面滾進來只說「快開始
        // 了」，敲三下才說「這麼快」。
        //
        // 用 dspStart 往回數，而不是從現在往前加：要對齊的是小節，而上面那個
        // 50ms 的保險有可能已經把 dspStart 推開了。
        CountInSticks.Cancel();
        Debug.Log($"[CountIn] lead={lead:F3}s roll={roll:F3}s rawRoll={RollInSeconds():F3}s "
            + $"beats={CountInBeats} dspStart={dspStart:F3} now={dspNow:F3}");
        if (roll > 0.0) CountInSticks.Schedule(dspStart, roll / CountInBeats);

        // The count-in begins only after parsing, audio decoding and prewarming
        // are complete, so its final second lands where the roll-in begins.
        GameplayEntryPresentation.BeginCountdown((float)(lead - roll), (float)roll);

        AudioSource musicSource = null;

        // Apply pitch-only speed; keep Conductor timing unscaled (audio is independent of gameplay timing)
        float appliedSpeed = CurrentAudioSpeedFactor;
        if (Conductor != null)
        {
            Conductor.playbackSpeed = 1f;
        }

        if (currentSongClip != null && currentSongClip.loadState == AudioDataLoadState.Loaded)
        {
            // Schedule audio playback
            Conductor.PlayScheduled(currentSongClip, dspStart);
            musicSource = Conductor != null && Conductor.audioSync != null ? Conductor.audioSync.audioSource : null;
            if (musicSource == null && Conductor != null)
            {
                musicSource = Conductor.GetComponent<AudioSource>();
            }
            TryAutoAssignMixerGroup(musicSource);
            if (musicSource != null)
            {
                float targetVol = musicSource.volume;
                try
                {
                    var settingsMgr = SettingsManager.Instance;
                    if (settingsMgr != null)
                    {
                        targetVol = Mathf.Clamp01(settingsMgr.GameplayMusicVolume);
                    }
                    // Keep piano routed through same group for consistent pitch compensation
                    TryAutoAssignMixerGroup(pianoAudioSource);
                }
                catch { }
                // 響度校正乘在這裡，而不是乘在每一個會動音量的地方：底下的淡入
                // 淡出、靜音視窗、還原，全部是從 baseMusicVolume 推出來的，所以
                // 只要源頭乘過一次，它們就都跟著對。
                targetVol = Mathf.Clamp01(targetVol * songLoudnessGain);
                musicSource.volume = targetVol;
                ApplySpeedFactorImmediate(appliedSpeed, currentUseMixer, musicSource);

                baseMusicVolume = musicSource.volume;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                try
                {
                    float appliedMixerPitch = 0f;
                    bool mixerSet = false;
                    var mixer = musicSource.outputAudioMixerGroup != null ? musicSource.outputAudioMixerGroup.audioMixer : null;
                    if (mixer != null && !string.IsNullOrWhiteSpace(mixerPitchParam))
                    {
                        if (compensatePitch && currentUseMixer)
                        {
                            if (TryGetMixerPitch(mixer, mixerPitchParam, out appliedMixerPitch)) mixerSet = true;
                        }
                        else
                        {
                            // Clean path: we expect mixer to be at neutral (1f or 0 semitone)
                            if (TryGetMixerPitch(mixer, mixerPitchParam, out appliedMixerPitch))
                            {
                                mixerSet = true;
                            }
                            else
                            {
                                appliedMixerPitch = mixerPitchParamIsSemitone ? 0f : 1f;
                            }
                        }
                    }
                    BuildLogger.Log($"[GameManager] ApplySpeed: factor={currentAudioSpeedFactor:F3} srcPitch={musicSource.pitch:F3} mixerSet={mixerSet} mixerVal={(mixerSet ? appliedMixerPitch : 0f):F3} group={(musicSource.outputAudioMixerGroup != null ? musicSource.outputAudioMixerGroup.name : "<null>")} semitone={(mixerPitchParamIsSemitone ? "Y" : "N")} param={mixerPitchParam} useMixer={currentUseMixer}");
                }
                catch { }
#endif
            }
            // If a GameBackgroundManager exists, schedule its video playback to the same DSP time
                try
                {
                    var bg = FindAnyObjectByType<GameBackgroundManager>();
                    if (bg != null)
                    {
                        // If selected song specifies an explicit video start offset, pass it through
                        try
                        {
                            var sel = SongSelectionManager.Instance?.GetSelectedSong();
                            double videoOffset = 0.0;
                            if (sel != null) videoOffset = sel.videoStartTimeSec;
                            bg.SchedulePlaybackAtDsp(dspStart, videoOffset);
                        }
                        catch { bg.SchedulePlaybackAtDsp(dspStart); }
                    }
                }
                catch { }
        }
        else
        {
            if (currentSongClip != null)
            {
                Debug.LogError($"[GameManager] Skipped audio playback because clip '{currentSongClip.name}' is not loaded (state={currentSongClip.loadState}).");
            }
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
            Conductor.StartPlayingWithoutAudio();
        }

        if (pianoClipToPlay != null)
        {
            var source = EnsurePianoAudioSource();
            if (cachedAudioSource == null && Conductor != null)
            {
                cachedAudioSource = Conductor.GetComponent<AudioSource>();
            }
            if (cachedAudioSource != null)
            {
                source.outputAudioMixerGroup = cachedAudioSource.outputAudioMixerGroup;
            }
            float volume = 1f;
            var settingsMgr = SettingsManager.Instance;
            if (settingsMgr != null)
            {
                volume = Mathf.Clamp01(settingsMgr.PianoVolume);
            }
            source.Stop();
            source.clip = pianoClipToPlay;
            source.playOnAwake = false;
            source.loop = false;
            source.time = 0f;
            // 分軌有自己的倍率（離線量測是兩軌各自對齊目標）。沒有量測資料時它和
            // 主軌是同一個值 —— 執行時的估算只算得出一個，兩條共用才不會毀掉混音。
            volume = Mathf.Clamp01(volume * songPianoLoudnessGain);
            source.volume = volume;
            basePianoVolume = volume;
            source.spatialBlend = 0f;
            ApplySpeedFactorImmediate(currentPianoSpeedFactor, currentPianoUseMixer, source, false);
            source.PlayScheduled(dspStart);
        }
        else
        {
            StopPianoLayer();
        }

        if (audioManageRoutine != null)
        {
            try { StopCoroutine(audioManageRoutine); } catch { }
            audioManageRoutine = null;
        }
        if (currentAudioManageMain != null && currentAudioManageMain.Count > 0 && musicSource != null)
        {
            audioManageRoutine = StartCoroutine(ApplyAudioManageWindows(currentAudioManageMain, dspStart, musicSource, baseMusicVolume));
        }
        if (currentAudioManagePiano != null && currentAudioManagePiano.Count > 0 && ShouldPlayGameplayPianoLayer() && pianoAudioSource != null)
        {
            // Start piano-specific audio manage windows (fade/pause for piano layer)
            pianoAudioManageRoutine = StartCoroutine(ApplyAudioManageWindows(currentAudioManagePiano, dspStart, pianoAudioSource, basePianoVolume));
        }
        // Start skip-section manager if selected song declares skipSections
        try
        {
            var sel = SongSelectionManager.Instance?.GetSelectedSong();
            if (sel != null && sel.skipSections != null && sel.skipSections.Count > 0)
            {
                skipSectionsRoutine = StartCoroutine(ManageSkipSections(sel.skipSections, Conductor, NoteSpawner, BeatLineSpawner));
            }
        }
        catch { }
        if (currentAudioSpeedEvents != null && currentAudioSpeedEvents.Count > 0 && musicSource != null)
        {
            speedEventsRoutine = StartCoroutine(ApplySpeedEvents(currentAudioSpeedEvents, dspStart, musicSource, true));
        }
        if (currentPianoSpeedEvents != null && currentPianoSpeedEvents.Count > 0 && ShouldPlayGameplayPianoLayer() && pianoAudioSource != null)
        {
            pianoSpeedEventsRoutine = StartCoroutine(ApplySpeedEvents(currentPianoSpeedEvents, dspStart, pianoAudioSource, false));
        }

        startDelayRoutine = null;
    }

    private void TryAutoAssignMixerGroup(AudioSource source)
    {
        if (!autoAssignMixerGroup || source == null) return;
        if (source.outputAudioMixerGroup != null) return;

        // 1) Use configured default if available
        if (defaultMusicMixerGroup != null)
        {
            source.outputAudioMixerGroup = defaultMusicMixerGroup;
            return;
        }

        // 2) Find any AudioMixer where the pitch param is exposed (GetFloat succeeds) and use its first group
        try
        {
            var mixers = Resources.FindObjectsOfTypeAll<AudioMixer>();
            foreach (var m in mixers)
            {
                if (m == null) continue;
                float dummy;
                if (m.GetFloat(mixerPitchParam, out dummy))
                {
                    var groups = m.FindMatchingGroups(string.Empty);
                    if (groups != null && groups.Length > 0)
                    {
                        source.outputAudioMixerGroup = groups[0];
                        return;
                    }
                }
            }
        }
        catch { }
    }

    private IEnumerator ApplyAudioManageWindows(List<SongSelectionManager.AudioPauseResumeWindow> windows, double dspStart, AudioSource targetSource, float restoreVolume)
    {
        if (windows == null || windows.Count == 0 || targetSource == null)
        {
            yield break;
        }

        // windows already sorted when parsed, but sort defensively
        windows.Sort((a, b) => a.pause.CompareTo(b.pause));

        foreach (var window in windows)
        {
            if (window == null) continue;
            double pauseDsp = dspStart + (window.pause / 1000.0);
            double resumeDsp = dspStart + (window.resume / 1000.0);
            bool stopForRest = window.resume <= 0 || window.resume <= window.pause; // treat missing/invalid resume as "until end"

            // A seek may rebuild this routine after the playhead has already
            // passed the complete window. Do not replay an old mute/pause.
            if (!stopForRest && Conductor != null &&
                Conductor.effectiveSongPosition >= window.resume)
            {
                try { targetSource.volume = restoreVolume; } catch { }
                continue;
            }

            // Handle fade if provided (fade seconds before pause to 0, and fade back after resume)
            if (window.fade > 0)
            {
                // window.fade is specified in seconds in JSON; compute fade duration in ms
                float fadeSeconds = (float)window.fade;
                float durationMs = window.fadeDurationMs > 0 ? window.fadeDurationMs : (fadeSeconds * 1000f);

                // Fade-out should start at pauseDsp - fadeSeconds
                double fadeOutStart = pauseDsp - (fadeSeconds);
                double fadeOutEnd = pauseDsp; // end of fade-out is the pause moment

                // Wait until fade-out start
                try { BuildLogger.Log($"[GameManager] Fade-out scheduled: start={fadeOutStart:F3}s, pause={pauseDsp:F3}s, durationMs={durationMs}"); } catch { }
                while (AudioSettings.dspTime < fadeOutStart)
                {
                    yield return null;
                }

                float startVol = targetSource.volume;
                // Do fade-out over durationMs (clamped to fadeSeconds if fadeDurationMs differs)
                double fadeOutDuration = (durationMs / 1000.0);
                double fadeOutTargetTime = fadeOutStart + fadeOutDuration;
                if (fadeOutTargetTime > fadeOutEnd) fadeOutTargetTime = fadeOutEnd; // don't overshoot

                while (AudioSettings.dspTime < fadeOutTargetTime)
                {
                    double t = (AudioSettings.dspTime - fadeOutStart) * 1000.0;
                    float ratio = (float)Mathf.Clamp01((float)(t / durationMs));
                    try { targetSource.volume = Mathf.Lerp(startVol, 0f, ratio); } catch { }
                    yield return null;
                }
                try { targetSource.volume = 0f; } catch { }

                // At exact pauseDsp, apply playback pause if required
                try { BuildLogger.Log($"[GameManager] Fade-out complete at {AudioSettings.dspTime:F3}s, applying pauseWindow.isPauseWindow={window.isPauseWindow}"); } catch { }
                while (AudioSettings.dspTime < pauseDsp)
                {
                    yield return null;
                }

                if (window.isPauseWindow)
                {
                    try
                    {
                        var audioSyncObj = Conductor != null ? (object)Conductor.audioSync : null;
                        if (audioSyncObj != null)
                        {
                            var mi = audioSyncObj.GetType().GetMethod("Pause");
                            if (mi != null) mi.Invoke(audioSyncObj, null);
                            else targetSource.Pause();
                        }
                        else
                        {
                            targetSource.Pause();
                        }
                    }
                    catch { }
                }

                try { BuildLogger.Log($"[GameManager] Waiting for resume at {resumeDsp:F3}s"); } catch { }

                if (stopForRest)
                {
                    yield break; // stay muted/paused after fade
                }

                // Wait until resume time, then resume playback (if paused) and fade-in
                while (AudioSettings.dspTime < resumeDsp)
                {
                    yield return null;
                }

                if (window.isPauseWindow)
                {
                    try
                    {
                        var audioSyncObj = Conductor != null ? (object)Conductor.audioSync : null;
                        if (audioSyncObj != null)
                        {
                            var mi = audioSyncObj.GetType().GetMethod("Resume");
                            if (mi != null) mi.Invoke(audioSyncObj, null);
                            else targetSource.UnPause();
                        }
                        else
                        {
                            targetSource.UnPause();
                        }
                    }
                    catch { }
                }

                // Fade-in from 0 to restoreVolume starting at resumeDsp over durationMs
                try { BuildLogger.Log($"[GameManager] Starting fade-in at {AudioSettings.dspTime:F3}s to restoreVolume={restoreVolume}"); } catch { }
                double fadeInStart = resumeDsp;
                double fadeInEnd = resumeDsp + (durationMs / 1000.0);
                try { targetSource.volume = 0f; } catch { }
                while (AudioSettings.dspTime < fadeInEnd)
                {
                    double t = (AudioSettings.dspTime - fadeInStart) * 1000.0;
                    float ratio = (float)Mathf.Clamp01((float)(t / durationMs));
                    try { targetSource.volume = Mathf.Lerp(0f, restoreVolume, ratio); } catch { }
                    yield return null;
                }
                try { targetSource.volume = restoreVolume; } catch { }
                continue; // proceed to next window
            }

            // Wait until the scheduled window start
            while (AudioSettings.dspTime < pauseDsp)
            {
                yield return null;
            }

            // pausePlayback pauses the source only. The chart clock intentionally keeps
            // advancing because authored note timestamps include this pause span.
            if (window.isPauseWindow)
            {
                // Pause only the audio playback; leave gameplay timing running.
                try
                {
                    var audioSyncObj = Conductor != null ? (object)Conductor.audioSync : null;
                    if (audioSyncObj != null)
                    {
                        var mi = audioSyncObj.GetType().GetMethod("Pause");
                        if (mi != null) mi.Invoke(audioSyncObj, null);
                        else targetSource.Pause();
                    }
                    else
                    {
                        targetSource.Pause();
                    }
                }
                catch { }
                // Optionally mute audio output while paused (keep 0 volume)
                try { targetSource.volume = 0f; } catch { }

                if (stopForRest)
                {
                    yield break; // keep audio paused for remainder
                }

                // Wait until resume time then resume audio playback from paused position
                while (AudioSettings.dspTime < resumeDsp)
                {
                    yield return null;
                }
                try
                {
                    var audioSyncObj = Conductor != null ? (object)Conductor.audioSync : null;
                    if (audioSyncObj != null)
                    {
                        var mi = audioSyncObj.GetType().GetMethod("Resume");
                        if (mi != null) mi.Invoke(audioSyncObj, null);
                        else targetSource.UnPause();
                    }
                    else
                    {
                        targetSource.UnPause();
                    }
                }
                catch { }
                try { targetSource.volume = restoreVolume; } catch { }
                continue;
            }

            // Otherwise treat as a mute window (legacy behavior)
            try { targetSource.volume = 0f; } catch { }

            if (stopForRest)
            {
                yield break; // keep muted for the remainder of the song
            }

            while (AudioSettings.dspTime < resumeDsp)
            {
                yield return null;
            }

            try { targetSource.volume = restoreVolume; } catch { }
        }
    }

    private IEnumerator ApplySpeedEvents(List<SongSelectionManager.AudioSpeedEvent> events, double dspStart, AudioSource targetSource, bool updateMainState)
    {
        if (events == null || events.Count == 0 || targetSource == null)
        {
            yield break;
        }

        events.Sort((a, b) => a.audiochangeTimeMs.CompareTo(b.audiochangeTimeMs));

        foreach (var ev in events)
        {
            if (ev == null) continue;
            double changeDsp = dspStart + (ev.audiochangeTimeMs / 1000.0);
            while (AudioSettings.dspTime < changeDsp)
            {
                yield return null;
            }

            // 先將 clip 指回原始音檔（完全乾淨）
            if (updateMainState)
            {
                if (targetSource != null && currentSongClip != null)
                    targetSource.clip = currentSongClip;
            }
            else
            {
                if (targetSource != null && currentPianoClip != null)
                    targetSource.clip = currentPianoClip;
            }

            // 再還原（reset）到原始速度/音高
            ApplySpeedFactorImmediate(1f, ev.useMixer, targetSource, updateMainState);
            // 再套用本事件的 factor
            float factor = ev.factor > 0f ? ev.factor : 1f;
            if (factor != 1f)
            {
                ApplySpeedFactorImmediate(factor, ev.useMixer, targetSource, updateMainState);
            }
        }
    }

    private IEnumerator ManageSkipSections(List<SongSelectionManager.SkipSection> sections, Conductor conductor, NoteSpawner noteSpawner, BeatLineSpawner beatSpawner)
    {
        if (sections == null || sections.Count == 0 || conductor == null) yield break;
        // Ensure sorted
        sections.Sort((a, b) => a.startMs.CompareTo(b.startMs));
        int idx = 0;
        while (idx < sections.Count)
        {
            var s = sections[idx];
            if (s == null) { idx++; continue; }

            float target = Mathf.Max(s.endMs, s.startMs);

            // Direct preview seeks and previous skip sections can place the
            // playhead beyond this range. Never seek backwards into an old gap.
            if (conductor.effectiveSongPosition >= target)
            {
                idx++;
                continue;
            }

            // Wait until effectiveSongPosition reaches or passes startMs
            while (conductor.effectiveSongPosition < s.startMs)
            {
                yield return null;
            }

            if (conductor.effectiveSongPosition >= target)
            {
                idx++;
                continue;
            }
            // Perform seek to endMs
            try
            {
                BuildLogger.Log($"[GameManager] SkipSection: seeking from {conductor.effectiveSongPosition:F0}ms to {target}ms");
            }
            catch { }
            conductor.SeekToMs(target);
            // Advance spawners to match new time
            try { if (noteSpawner != null) noteSpawner.SeekToMs(target); } catch { }
            try { if (beatSpawner != null) beatSpawner.SeekToMs(target); } catch { }
            RestartTimedAudioRoutinesAfterSeek(target);
            // Give one frame to let systems settle
            yield return null;
            idx++;
        }
    }

    private void ApplySpeedFactorImmediate(float factor, bool useMixerFlag, AudioSource targetSource, bool updateMainState = true)
    {
        // 練習速度乘在最前面，之後每一段（音源 pitch、mixer 補償、BPM 顯示）看到
        // 的都是同一個係數。譜面本身的 audioSpeedEvents 照樣乘進來，兩者相乘就是
        // 實際在播的速度 —— 而 Conductor 的位置是從音源時鐘讀的，所以譜面自然
        // 跟著一起變速，不需要另外通知它。
        // 練習速度**不**乘進 factor。譜面的 first_bpm 在載入時就已經改寫過，
        // 而這裡會用 CurrentChart.first_bpm * factor 去更新 Conductor.bpm ——
        // 再乘一次就是乘兩次，BPM 顯示和任何從它推導出來的東西都會少一半。
        // 音檔沒有被改寫，所以只有音源的 pitch 要另外乘（見下面）。

        if (updateMainState)
        {
            currentAudioSpeedFactor = factor;
            currentUseMixer = useMixerFlag;
            // 根據 beat_timings 實時計算 BPM（BEAT interpolation）
            if (Conductor != null && CurrentChart != null && CurrentChart.beat_timings != null && CurrentChart.beat_timings.Count > 1)
            {
                // 取得目前播放位置（毫秒）
                float songPosMs = 0f;
                // 嘗試從 Conductor 取得 songPosition，否則用 AudioSource
                try
                {
                    // Conductor.songPosition is already expressed in milliseconds.
                    // Multiplying it by 1000 selected a beat thousands of times too
                    // far ahead whenever an audio-speed event was applied.
                    songPosMs = Conductor.effectiveSongPosition;
                }
                catch { }
                // 若失敗則嘗試從 targetSource
                if (songPosMs <= 0f && targetSource != null)
                {
                    try { songPosMs = targetSource.time * 1000f; } catch { }
                }
                // 找到 beat_timings 中 songPosMs 所在區間
                var beats = CurrentChart.beat_timings;
                int idx = -1;
                for (int i = 0; i < beats.Count - 1; i++)
                {
                    if (songPosMs >= beats[i] && songPosMs < beats[i + 1])
                    {
                        idx = i;
                        break;
                    }
                }
                float bpm = CurrentChart.first_bpm * factor;
                if (idx >= 0)
                {
                    int delta = beats[idx + 1] - beats[idx];
                    if (delta > 0)
                    {
                        bpm = 60000f / delta * factor;
                    }
                }
                Conductor.bpm = bpm;
            }
            else if (Conductor != null && CurrentChart != null && CurrentChart.first_bpm > 0f)
            {
                // fallback: 舊邏輯
                Conductor.bpm = CurrentChart.first_bpm * factor;
            }
        }
        else
        {
            currentPianoSpeedFactor = factor;
            currentPianoUseMixer = useMixerFlag;
            // 若要顯示鋼琴 BPM，可在這裡加 piano BPM 變數
        }

        if (targetSource == null)
        {
            return;
        }

        // 只有這裡乘練習速度：音檔是原長的，要放慢才跟得上被拉長的譜面。
        var practiceForPitch = SettingsManager.Instance;
        targetSource.pitch = factor * (practiceForPitch != null ? practiceForPitch.PracticeSpeed : 1f);

        try
        {
            var mixer = targetSource.outputAudioMixerGroup != null ? targetSource.outputAudioMixerGroup.audioMixer : null;
            if (mixer != null && !string.IsNullOrWhiteSpace(mixerPitchParam))
            {
                bool useMixerFlagEffective = updateMainState ? currentUseMixer : currentPianoUseMixer;
                if (compensatePitch && useMixerFlagEffective)
                {
                    float targetLinear = (factor > 0f) ? (1f / factor) : 1f;
                    TrySetMixerPitch(mixer, mixerPitchParam, mixerPitchParamIsSemitone, targetLinear);
                }
                else
                {
                    // Reset mixer pitch so clean path stays unchanged.
                    TrySetMixerPitch(mixer, mixerPitchParam, mixerPitchParamIsSemitone, 1f);
                }
            }

            // Attempt to retarget mixer routing immediately (MusicPitchWatcher will also correct each frame).
            var musicManager = FindAnyObjectByType<MusicAudioManager>();
            if (musicManager != null)
            {
                musicManager.ApplyRoutingForUseMixer(useMixerFlag);
                if (musicManager.musicSource == targetSource)
                {
                    musicManager.ApplySpeed(factor);
                }
            }
        }
        catch { }
    }

    private bool TrySetMixerPitch(AudioMixer mixer, string primaryParam, bool isSemitone, float targetLinear)
    {
        if (mixer == null) return false;
        string[] candidates = new[] { primaryParam, "Pitch", "MusicPitch" };
        foreach (var name in candidates)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            try
            {
                if (isSemitone)
                {
                    float semitones = 12f * Mathf.Log(targetLinear, 2f);
                    if (mixer.SetFloat(name, semitones)) return true;
                }
                else
                {
                    if (mixer.SetFloat(name, targetLinear)) return true;
                }
            }
            catch { }
        }
        return false;
    }

    private bool TryGetMixerPitch(AudioMixer mixer, string primaryParam, out float value)
    {
        value = 0f;
        if (mixer == null) return false;
        string[] candidates = new[] { primaryParam, "Pitch", "MusicPitch" };
        foreach (var name in candidates)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            try
            {
                if (mixer.GetFloat(name, out value)) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// How long the count-in lasts, taken from the player's setting.
    /// </summary>
    /// <remarks>
    /// Read here rather than trusted from <see cref="SetStartDelay"/>. That is a
    /// push -- SettingsManager calls it when it applies settings -- and a push
    /// only lands if this object already exists at that moment. Reading the
    /// setting at the point of use cannot miss.
    /// </remarks>
    public float CountInSeconds
    {
        get
        {
            var settings = SettingsManager.Instance;
            return Mathf.Max(0f, settings != null ? settings.GameStartDelay : songStartDelaySeconds);
        }
    }

    public float SongStartDelayMs => CountInSeconds * 1000f;

    /// <summary>
    /// 一拍多長，以及第一條小節線落在音檔的第幾秒。
    /// </summary>
    /// <remarks>
    /// beat_timings 在這個曲庫裡有兩種意思：有的譜面一小節一條，有的一個四分音符
    /// 一條。<see cref="AutoPedal.EntriesPerBar"/> 已經有這個判斷，這裡沿用同一份
    /// —— 兩邊各判一次遲早會判出不同的答案。
    ///
    /// 拍長最後拿 60/BPM 當靠山：格線可能不規則（曲庫裡有拍號寫 1/8 的譜面），
    /// 而算出來的拍長只要偏離一個四分音符太多，數出來的三拍就不是這首歌的三拍。
    /// </remarks>
    /// <summary>
    /// 把這首歌的曲繪、曲名、作者、難度交給載入頁。
    /// </summary>
    /// <remarks>
    /// 讀的是選歌畫面**此刻選中的那一項**（含玩家挑的難度變體），而不是
    /// GameManager 自己的欄位：這個方法在兩條進入路徑上都會被呼叫，其中一條
    /// （StartSong）跑到這裡的時候 GameManager 的欄位還沒填完。
    ///
    /// 整段包在 try 裡。載入頁的裝飾壞掉不該把開歌這件事一起拖下水。
    /// </remarks>
    private static void DressLoadingScreen(GameplayLoadingScreen screen)
    {
        if (screen == null) return;
        try
        {
            var option = SongSelectionManager.Instance?.GetSelectedSong();
            if (option == null)
            {
                screen.SetSong(null, null, null, null, 0);
                return;
            }
            screen.SetSong(option.coverSprite, option.displayName, option.author,
                option.difficultyName, option.difficultyLevel);
        }
        catch { }
    }

    /// <summary>三拍有多長。拿不到拍格就是 0，行為退回舊版。</summary>
    private float RollInSeconds()
    {
        return TryBeatCountIn(out double beatSeconds, out _)
            ? (float)(beatSeconds * CountInBeats)
            : 0f;
    }

    /// <summary>
    /// 把進場那一段設成三拍，並回報它有多長，好加進總延遲裡。
    /// </summary>
    /// <remarks>
    /// **這一段本來就存在。** Conductor 的視覺預捲在整段倒數裡是把譜面**停著**
    /// 的，只在最後一小段放它滾進來，原本寫死一秒。所以「倒數到一就開始跑了」不是
    /// 錯覺，那正是它的設計。
    ///
    /// 寫死一秒的問題是它在每首歌底下都是不同的音樂長度：180 BPM 的一秒是三拍，
    /// 60 BPM 的一秒是一拍。玩家在那一段裡要讀的正是「這首歌多快」，而固定秒數
    /// 偏偏不講這件事。
    ///
    /// 改成三拍之後，總延遲也要跟著加長 —— 不然三拍是從秒倒數裡挖走的，秒數會
    /// 變少。加長之後順序才是：秒倒數數完 → 滾三拍 → 第一小節。
    /// </remarks>
    private float ArmRollIn()
    {
        float roll = RollInSeconds();
        Conductor?.SetVisualLeadIn(roll * 1000f);
        return roll;
    }

    private bool TryBeatCountIn(out double beatSeconds, out double firstBarSeconds)
    {
        beatSeconds = 0.0;
        firstBarSeconds = 0.0;

        var beats = CurrentChartHeader != null && CurrentChartHeader.beat_timings != null
            ? CurrentChartHeader.beat_timings
            : CurrentChart?.beat_timings;
        if (beats == null || beats.Count < 2) return false;

        float bpm = CurrentChartHeader != null && CurrentChartHeader.first_bpm > 0f
            ? CurrentChartHeader.first_bpm
            : (CurrentChart != null ? CurrentChart.first_bpm : 0f);
        if (bpm <= 0f) return false;

        int beatsPerBar = CurrentChartHeader != null && CurrentChartHeader.time_signature_numerator > 0
            ? CurrentChartHeader.time_signature_numerator
            : 4;

        double step = (beats[1] - beats[0]) / 1000.0;
        if (step <= 0.0) return false;

        int perBar = AutoPedal.EntriesPerBar(beats, bpm, beatsPerBar);
        double derived = perBar > 1 ? step : step / System.Math.Max(1, beatsPerBar);

        double quarter = 60.0 / bpm;
        // 離一個四分音符太遠就不信它，退回 BPM。差一倍以上的多半是格線的意思被
        // 判錯了，而不是這首歌真的每拍兩秒。
        beatSeconds = (derived > quarter * 0.55 && derived < quarter * 1.85) ? derived : quarter;
        firstBarSeconds = System.Math.Max(0.0, beats[0] / 1000.0);
        return beatSeconds > 0.05;
    }

    /// <summary>
    /// Sets the song start delay from SettingsManager
    /// </summary>
    /// <param name="delaySeconds">Delay in seconds</param>
    public void SetStartDelay(float delaySeconds)
    {
        songStartDelaySeconds = Mathf.Clamp(delaySeconds, 2f, 5f);
    }

    private void ValidatePrefabs()
    {
        if (notePrefab2D == null)
        {
            BuildLogger.LogError("GameManager: notePrefab2D is not assigned in the Inspector.");
        }

        if (beatLinePrefab2D == null)
        {
            BuildLogger.LogError("GameManager: beatLinePrefab2D is not assigned in the Inspector.");
        }
    }

    private void Start()
    {
        ValidatePrefabs();
        // Ensure spawners use only 2D prefabs (use instance references)
        if (this.NoteSpawner != null)
        {
            this.NoteSpawner.notePrefab2D = notePrefab2D;
        }
        if (this.BeatLineSpawner != null)
        {
            this.BeatLineSpawner.beatLinePrefab2D = beatLinePrefab2D;
        }
        // Note: legacy 3D prefab fields were removed from spawners.
        // We assign the 2D prefabs here to ensure spawners use the correct assets.
        if (this.NoteSpawner == null)
        {
        }
        if (this.BeatLineSpawner == null)
        {
        }

        // --- Ensure JudgmentLine width matches track width ---
        if (autoResizeJudgmentLine)
        {
            try
            {
                // Find any active JudgmentLine instances in the scene
                // Use the newer API to avoid deprecation in newer Unity versions
                var judgmentObjs = GameObject.FindObjectsByType<Transform>(UnityEngine.FindObjectsSortMode.None);
                Transform judgmentTransform = null;
                foreach (var t in judgmentObjs)
                {
                    if (t.name == "JudgmentLine")
                    {
                        judgmentTransform = t;
                        break;
                    }
                }

                if (judgmentTransform != null && trackTransform != null)
                {
                    // Try to determine track world width using Renderer bounds if available
                    float trackWorldWidth = 0f;
                    var trackRenderer = trackTransform.GetComponentInChildren<Renderer>();
                    if (trackRenderer != null)
                    {
                        trackWorldWidth = trackRenderer.bounds.size.x;
                    }
                    else
                    {
                        // Fallback: use localScale.x of track's transform as an approximation
                        trackWorldWidth = trackTransform.lossyScale.x;
                    }

                    if (trackWorldWidth > 0.0001f)
                    {
                        // Calculate local scale for judgmentTransform such that its world width ~= trackWorldWidth
                        var parent = judgmentTransform.parent != null ? judgmentTransform.parent : judgmentTransform;
                        Vector3 parentLossy = parent.lossyScale;
                        Vector3 currentLocal = judgmentTransform.localScale;
                        float desiredWorld = trackWorldWidth;
                        float newLocalX = parentLossy.x != 0f ? desiredWorld / parentLossy.x : currentLocal.x;
                        judgmentTransform.localScale = new Vector3(newLocalX, currentLocal.y, currentLocal.z);
                    }
                }
            }
            catch (System.Exception)
            {
            }
        }
        else
        {
            // Auto-resize disabled to avoid unexpected overrides -- user may set JudgmentLine size manually in scene
        }
    }
}
