using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem; // Add this for the new Input System
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

    private System.Collections.IEnumerator ParseChartInBackground(string jsonText, string chartFile)
    {
        // Give one frame to let UI update and avoid blocking the immediate startup frame
        yield return null;
        // Additional frame yield to reduce likelihood of a single-frame long parse
        yield return null;

        // Perform full parse on main thread (JsonUtility is not thread-safe). This will still
        // produce a parse spike, but at least it happens after initial UI has rendered.
        Chart parsed = null;
        try
        {
            parsed = JsonUtility.FromJson<Chart>(jsonText);
        }
        catch (System.Exception ex)
        {
            BuildLogger.LogWarning($"[GameManager] Background parse failed for {chartFile}: {ex.Message}");
            // Newtonsoft.Json fallback for background parse
            try
            {
                var chart = Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(jsonText);
                if (chart != null)
                {
                    parsed = chart;
                    BuildLogger.LogWarning($"[GameManager] Background parse: Newtonsoft.Json full parse OK notes={(chart.notes!=null?chart.notes.Count:0)}");
                }
            }
            catch (System.Exception nex)
            {
                BuildLogger.LogError($"[GameManager] Background parse: Newtonsoft.Json full parse failed: {nex.Message}");
            }
        }

        if (parsed != null)
        {
            CurrentChart = parsed;
            try { if (Conductor != null) Conductor.bpm = CurrentChart.first_bpm; } catch { }
            // Notify that full chart is available so StartSong continuation can proceed.
            try { OnFullChartParsed(); } catch { }
        }
        else
        {
            BuildLogger.LogError($"[GameManager] Failed to parse full chart in background for '{chartFile}'");
        }
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

        // Activate gameplay root and continue the startup flow that depends on full chart
        SetGameplayRootActive(true);

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
        startDelayRoutine = StartCoroutine(BeginSongAfterDelay(pendingScheduledDelay));

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

    public Chart CurrentChart { get; private set; }
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
    private AudioClip currentPianoClip;
    private string currentSongDisplayName;
    private string currentPianoResourcePath;
    private AudioSource pianoAudioSource;
    private List<SongSelectionManager.AudioPauseResumeWindow> currentAudioManageMain;
    private List<SongSelectionManager.AudioPauseResumeWindow> currentAudioManagePiano;
    private List<SongSelectionManager.AudioSpeedEvent> currentAudioSpeedEvents;
    private List<SongSelectionManager.AudioSpeedEvent> currentPianoSpeedEvents;
    private Coroutine audioManageRoutine;
    private Coroutine speedEventsRoutine;
    private Coroutine pianoSpeedEventsRoutine;
    private float currentAudioSpeedFactor = 1f;
    private bool currentUseMixer = false;
    private float currentPianoSpeedFactor = 1f;
    private bool currentPianoUseMixer = false;
    private float baseMusicVolume = 1f;
    private float basePianoVolume = 1f;

    // Expose applied audio speed for UI (e.g., BPM display)
    public float CurrentAudioSpeedFactor => currentAudioSpeedFactor <= 0f ? 1f : currentAudioSpeedFactor;
    public bool CurrentUseMixer => currentUseMixer;

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
    bool LoadChartData(string chartFile)
    {
        try { BuildLogger.Log($"[GameManager] LoadChartData: attempting Resources.Load<TextAsset>('{chartFile}')"); } catch { }
        TextAsset jsonFile = Resources.Load<TextAsset>(chartFile);
        try { BuildLogger.Log($"[GameManager] LoadChartData: Resources.Load returned {(jsonFile==null?"null":"TextAsset")}"); } catch { }
        if (jsonFile != null) { try { BuildLogger.Log($"[GameManager] LoadChartData: loaded text length={ (jsonFile.text!=null ? jsonFile.text.Length : 0) }"); } catch { } }
        if (jsonFile == null)
        {
            CurrentChart = null;
            try { BuildLogger.LogWarning($"[GameManager] LoadChartData: Resources.Load<TextAsset> returned null for '{chartFile}'"); } catch { }
            return false;
        }

        // Fast path: parse only header fields so UI/BPM can be shown quickly.
        try
        {
            CurrentChartHeader = JsonUtility.FromJson<ChartHeader>(jsonFile.text);
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
                if (TryExtractHeaderWithRegex(jsonFile.text, out fallback))
                {
                    CurrentChartHeader = fallback;
                    try { BuildLogger.Log($"[GameManager] LoadChartData: header extracted by regex first_bpm={CurrentChartHeader.first_bpm}"); } catch { }
                }
            }
            catch { }
            // Newtonsoft.Json fallback for header
            try
            {
                var header = Newtonsoft.Json.JsonConvert.DeserializeObject<ChartHeader>(jsonFile.text);
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
                string sanitized = SanitizeJsonText(jsonFile.text);
                CurrentChart = JsonUtility.FromJson<Chart>(sanitized);
                try { BuildLogger.Log($"[GameManager] LoadChartData: full parse {(CurrentChart!=null?"OK":"null")} notes={(CurrentChart!=null? (CurrentChart.notes!=null?CurrentChart.notes.Count:0):0)}"); } catch { }
            }
            catch (System.Exception ex)
            {
                CurrentChart = null;
                try { BuildLogger.LogWarning($"[GameManager] LoadChartData: full parse threw: {ex.Message}"); } catch { }
                // Newtonsoft.Json fallback for full chart
                try
                {
                    var chart = Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(jsonFile.text);
                    if (chart != null)
                    {
                        CurrentChart = chart;
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
                StartCoroutine(ParseChartInBackground(jsonFile.text, chartFile));
            }
            catch { }
        }

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

    public void StartSong(string selectedChartFile, AudioClip selectedClip, string displayName = null, AudioClip pianoClip = null, string pianoResourcePath = null)
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

        if (audioManageRoutine != null)
        {
            try { StopCoroutine(audioManageRoutine); } catch { }
            audioManageRoutine = null;
        }
        if (speedEventsRoutine != null)
        {
            try { StopCoroutine(speedEventsRoutine); } catch { }
            speedEventsRoutine = null;
        }
        if (pianoSpeedEventsRoutine != null)
        {
            try { StopCoroutine(pianoSpeedEventsRoutine); } catch { }
            pianoSpeedEventsRoutine = null;
        }

        chartFileName = selectedChartFile;
        currentSongClip = selectedClip != null ? selectedClip : songClip;
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
            float computedAppliedDelay = Mathf.Max(0f, songStartDelaySeconds);
            float computedPlaybackOffsetSec = 0f;
            try { computedPlaybackOffsetSec = SettingsManager.Instance != null ? (SettingsManager.Instance.MusicPlaybackOffsetMs / 1000f) : 0f; }
            catch { computedPlaybackOffsetSec = 0f; }
            float computedScheduledDelay = Mathf.Max(0f, computedAppliedDelay + computedPlaybackOffsetSec);

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

    // Activate gameplay root and start visual pre-roll BEFORE initializing spawners so they
    // see an active Conductor (pre-roll) and can spawn pre-roll/negative-time beats immediately.
    SetGameplayRootActive(true);
    float appliedDelay = Mathf.Max(0f, songStartDelaySeconds);
    float playbackOffsetSec = 0f;
    try { playbackOffsetSec = SettingsManager.Instance != null ? (SettingsManager.Instance.MusicPlaybackOffsetMs / 1000f) : 0f; }
    catch { playbackOffsetSec = 0f; }

    float scheduledDelay = Mathf.Max(0f, appliedDelay + playbackOffsetSec);

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
        Conductor.StartVisualPreRoll(scheduledDelay);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
        StartSong(chartFileName, currentSongClip, currentSongDisplayName, currentPianoClip, currentPianoResourcePath);
        
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
            // Stop playback and any piano preview
            Conductor?.Stop();
            StopPianoLayer();

            // Reset combo/score/statistics so next run starts fresh
            try { Judgment.JudgmentManager.Instance?.ResetStats(); } catch { }
            try { StatsManager.Instance?.ResetStats(); } catch { }

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
        if (SettingsManager.Instance != null && SettingsManager.Instance.InputMode == InputModeType.MIDI)
        {
            EnsureMIDIInputManagerEnabled(true);
        }
        else
        {
            EnsureMIDIInputManagerEnabled(false);
        }
        // 其他輸入已由 InputManager 處理
    }

    private bool midiSubscribed = false;
    private void EnsureMIDIInputManagerEnabled(bool enable)
    {
        var midiObj = FindFirstObjectByType<MIDIInputManager>();
        if (enable)
        {
            if (midiObj == null)
            {
                var go = new GameObject("MIDIInputManager");
                go.AddComponent<MIDIInputManager>();
            }
            if (!midiSubscribed && MIDIInputManager.Instance != null)
            {
                MIDIInputManager.Instance.OnMidiNoteOn += HandleMidiNoteOn;
                MIDIInputManager.Instance.OnMidiNoteOff += HandleMidiNoteOff;
                midiSubscribed = true;
            }
        }
        else
        {
            if (midiSubscribed && MIDIInputManager.Instance != null)
            {
                MIDIInputManager.Instance.OnMidiNoteOn -= HandleMidiNoteOn;
                MIDIInputManager.Instance.OnMidiNoteOff -= HandleMidiNoteOff;
                midiSubscribed = false;
            }
            if (midiObj != null)
            {
                Destroy(midiObj.gameObject);
            }
        }
    }

    // MIDI note on event handler
    private void HandleMidiNoteOn(int note, float velocity)
    {
        int keyIndex = MidiNoteToKeyMapper.GetKeyIndex(note);
        #if UNITY_EDITOR
        if (keyIndex >= 0)
        {
            Debug.Log($"[MIDI] NoteOn: {note} -> 鍵位 {keyIndex}, 力度: {velocity}");
        }
        else
        {
            Debug.Log($"[MIDI] NoteOn: {note} (未對應), 力度: {velocity}");
        }
        #endif
        // 這裡 keyIndex 就是 0~27，velocity 為原始力度
        // 直接呼叫 NoteSpawner 的 TryHitNote 進行判定
        if (keyIndex >= 0 && NoteSpawner != null)
        {
            NoteSpawner.TryHitNote(keyIndex, velocity);
        }
    }

    // MIDI note off event handler
    private void HandleMidiNoteOff(int note, float velocity)
    {
        Debug.Log($"[MIDI] NoteOff: {note}, velocity: {velocity}");
        int keyIndex = MidiNoteToKeyMapper.GetKeyIndex(note);
        if (keyIndex >= 0)
        {
            // Hide persistent mesh for this key if shown (MIDI note off should release holds)
            try
            {
                if (KeyHitEffectManager.Instance != null) KeyHitEffectManager.Instance.HidePersistentMeshForKeyId(keyIndex);
            }
            catch { }

            // Notify JudgmentManager about a potential held-key release so hold notes can finalize
            try
            {
                if (Judgment.JudgmentManager.Instance != null)
                {
                    float songPos = Conductor != null ? Conductor.effectiveSongPosition : 0f;
                    Judgment.JudgmentManager.Instance.HandleHeldKeyRelease(keyIndex, songPos);
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
        // 你可以在這裡加入更多鍵位判斷與遊戲邏輯
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

    private bool ShouldPlayGameplayPianoLayer()
    {
        var settings = SettingsManager.Instance;
        if (settings == null) return false;
        if (!settings.EnablePianoPreview) return false;
        return currentPianoClip != null;
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

        clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading)
        {
            yield return null;
        }
    }

    private IEnumerator BeginSongAfterDelay(float delaySeconds)
    {
        AudioClip pianoClipToPlay = ShouldPlayGameplayPianoLayer() ? currentPianoClip : null;

        yield return EnsureAudioClipReady(currentSongClip);
        yield return EnsureAudioClipReady(pianoClipToPlay);

        double dspNow = AudioSettings.dspTime;
        double dspStart = dspNow + System.Math.Max(0.0, (double)delaySeconds);
        if (dspStart <= dspNow + 0.02)
        {
            dspStart = dspNow + 0.05; // 50ms safety buffer
        }

        AudioSource musicSource = null;

        // Apply pitch-only speed; keep Conductor timing unscaled (audio is independent of gameplay timing)
        float appliedSpeed = CurrentAudioSpeedFactor;
        if (Conductor != null)
        {
            Conductor.playbackSpeed = 1f;
        }

        if (currentSongClip != null)
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
                        targetVol = Mathf.Clamp01(settingsMgr.MusicVolume);
                    }
                    // Keep piano routed through same group for consistent pitch compensation
                    TryAutoAssignMixerGroup(pianoAudioSource);
                }
                catch { }
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
            StartCoroutine(ApplyAudioManageWindows(currentAudioManagePiano, dspStart, pianoAudioSource, basePianoVolume));
        }
        // Start skip-section manager if selected song declares skipSections
        try
        {
            var sel = SongSelectionManager.Instance?.GetSelectedSong();
            if (sel != null && sel.skipSections != null && sel.skipSections.Count > 0)
            {
                StartCoroutine(ManageSkipSections(sel.skipSections, Conductor, NoteSpawner, BeatLineSpawner));
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

            // If this window was marked as a real pausePlayback, pause the conductor (freezes gameplay & audio)
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
            // Wait until effectiveSongPosition reaches or passes startMs
            while (conductor.effectiveSongPosition < s.startMs)
            {
                yield return null;
            }
            // Perform seek to endMs
            float target = Mathf.Max(s.endMs, s.startMs);
            try
            {
                BuildLogger.Log($"[GameManager] SkipSection: seeking from {conductor.effectiveSongPosition:F0}ms to {target}ms");
            }
            catch { }
            conductor.SeekToMs(target);
            // Advance spawners to match new time
            try { if (noteSpawner != null) noteSpawner.SeekToMs(target); } catch { }
            try { if (beatSpawner != null) beatSpawner.SeekToMs(target); } catch { }
            // Give one frame to let systems settle
            yield return null;
            idx++;
        }
    }

    private void ApplySpeedFactorImmediate(float factor, bool useMixerFlag, AudioSource targetSource, bool updateMainState = true)
    {
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
                try {
                    songPosMs = (float)(Conductor.songPosition * 1000.0);
                } catch { }
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

        targetSource.pitch = factor;

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

    public float SongStartDelayMs
    {
        get
        {
            float playbackOffsetSec = 0f;
            try { playbackOffsetSec = SettingsManager.Instance != null ? (SettingsManager.Instance.MusicPlaybackOffsetMs / 1000f) : 0f; }
            catch { playbackOffsetSec = 0f; }
            return Mathf.Max(0f, songStartDelaySeconds + playbackOffsetSec) * 1000f;
        }
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
