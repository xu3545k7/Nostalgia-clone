using UnityEngine;

[System.Serializable]
public enum InputModeType { Keyboard, MIDI, MidiKeyboard }

[System.Serializable]
public enum VisualLayoutMode { General, Piano88 }

/// <summary>Where the piano the player hears comes from.</summary>
/// <remarks>
/// GameRecording keeps the song's pre-rendered *_piano.wav stem: the piece sounds
/// the same however you play. The other two synthesise every note from the sampled
/// bank instead, so a missed note leaves a real gap — which means the stem must be
/// muted for them or two pianos play at once.
///
/// Performance still reproduces the original performance's dynamics (each note
/// carries the velocity restored from the source MIDI); Hardcore hands dynamics
/// and note length to the player's own touch. Both take the sustain pedal from
/// the chart rather than a physical pedal.
/// </remarks>
[System.Serializable]
public enum PianoSoundMode
{
    GameRecording = 0,
    Performance = 1,
    Hardcore = 2,
}

/// <summary>Where the sustain pedal the synthesised piano follows comes from.</summary>
/// <remarks>
/// Chart replays the pedalling restored from the source MIDI, so the piece sustains
/// the way it was performed no matter what hardware the player has. Player hands it
/// to a real sustain pedal over MIDI CC64 — with no pedal connected that means notes
/// only ring while their key is held, which is correct but very dry on music written
/// around the pedal.
/// </remarks>
/// <summary>輸入幀要管到哪裡。</summary>
/// <remarks>
/// 幀的價值是「幀內沒有先後」：一顆音符判定成功後，落在它左右各 3 格的按鍵
/// 一律失效，不管誰先到。事件驅動的保護窗做不到這件事，因為它只擋得住**之後**
/// 到的按鍵——而擦碰通常是**先**到的那一下。
///
/// 但幀的代價是延遲，而延遲不必全部付：
///
/// * <b>Off</b>：逐鍵即時判定。零延遲，擦碰只能靠單向的近似去擋。
/// * <b>Full</b>：判定和聲音都等幀結算。完整的官方行為，最多晚一個操作幀。
/// * <b>SoundOnly</b>：判定、計分、視覺回饋**立刻**送出（零延遲），只有鋼琴聲
///   等幀決定要不要發。理由很單純——分數和畫面都可以事後修正，**聲音發出去
///   就收不回**。所以只有它需要等。
/// </remarks>
[System.Serializable]
public enum InputFrameMode
{
    Off = 0,
    Full = 1,
    SoundOnly = 2,
}

/// <summary>同一個輸入幀裡的按鍵，各自評分還是共用一個時間。</summary>
/// <remarks>
/// 這兩者只有在 <c>inputFrameBatching</c> 打開時才有差別——沒有幀就沒有
/// 「共用時間」這回事。
///
/// * <b>Precise（精準）</b>：每顆按鍵用自己的硬體時間戳評分。比官方細，
///   但一個和弦裡的手指相差 10ms 就可能一顆 Perfect、一顆 Great。
/// * <b>FrameQuantized（幀量化）</b>：整幀共用一個 t_I，照判定文件 §2.3
///   的「時間精度為一遊戲操作幀」。和弦一定拿到**一致**的判定，幀內的手指
///   落差完全不影響評分——官方就是這樣，而這正是和弦打起來乾淨的原因。
/// </remarks>
[System.Serializable]
/// <summary>
/// 判定的三種取向。
/// </summary>
/// <remarks>
/// 這不是難度，是**手感的取向**：一邊要反應快，一邊要和原作一樣。兩者都不比對
/// 方「準」，它們在不同的地方讓步。
/// </remarks>
public enum JudgmentPreset
{
    /// <summary>反應優先：逐鍵即時判定，誤判事後改。</summary>
    Responsive = 0,
    /// <summary>原型優先：16.66ms 輸入幀、幀內共用時間、一次性判定。</summary>
    Classic = 1,
    /// <summary>玩家自己那一組。</summary>
    Custom = 2,
}

public enum JudgmentTiming
{
    Precise = 0,
    FrameQuantized = 1,
}

[System.Serializable]
public enum PianoPedalSource
{
    Chart = 0,
    Player = 1,
}

[System.Serializable]
public enum TrackMarbleTheme { Black, White }

[System.Serializable]
public enum AppLanguage { TraditionalChinese, English, SimplifiedChinese }

[System.Serializable]
public enum ComboDisplayPosition { TopLeft, TopCenter, Track, Center }

[System.Serializable]
public class GameSettings
{
    // Version 2 distinguishes a deliberate 0% music volume from legacy saves
    // where missing/uninitialised UI values were accidentally persisted as 0.
    public int settingsVersion = 7;

    [Header("Language")]
    public AppLanguage language = AppLanguage.TraditionalChinese;

    [Header("HUD")]
    public ComboDisplayPosition comboDisplayPosition = ComboDisplayPosition.TopCenter;
    [Range(24f, 96f)]
    public float comboFontSize = 48f;
    [Range(0f, 1f)]
    public float trackComboScreenHeight = 0.35f;

    [Header("Input Mode")]
    public InputModeType inputMode = InputModeType.Keyboard;

    [Header("Visual Layout")]
    [Tooltip("General keeps the classic 28-lane layout. Piano88 expands note visuals across 88 piano keys when pitch data exists.")]
    public VisualLayoutMode visualLayoutMode = VisualLayoutMode.General;

    [Header("Audio Settings")]
    [Range(0f, 1f)]
    public float hitSoundVolume = 1.0f;

    [Range(0f, 1f)]
    public float musicVolume = 0.5f;

    [Range(0f, 1f)]
    public float pianoVolume = 0.5f;

    [Header("Gameplay Settings")]
    [Range(10f, 2000f)]
    public float defaultSpeed = 80f;

    [Range(2f, 5f)]
    public float gameStartDelay = 2f;

    [Tooltip("World-space height used by note heads. Width remains controlled by the chart lanes.")]
    [Range(0.5f, 4f)]
    public float noteVisualHeight = 2f;

    [Tooltip("練習模式：不計分、不放背景音樂、可以改速度。")]
    public bool practiceMode = false;

    [Tooltip("練習模式的速度倍率。")]
    [Range(0.5f, 1.5f)]
    public float practiceSpeed = 1f;

    [Tooltip("演奏會模式：誤觸的鍵會亮紅燈，打在音符正中央的鍵才拿得到滿分。")]
    public bool recitalMode = false;

    [Tooltip("玩家上一次挑的難度階（0 Normal…4 特殊）。-1 表示還沒挑過。")]
    public int preferredDifficultyTier = -1;

    [Tooltip("Player scale applied to the automatic judgment MESH height. 1 keeps the authored size.")]
    [Range(0.25f, 4f)]
    public float judgmentMeshHeight = 1f;

    [Tooltip("Move judgment MESH and hit particles along the chart time axis so FAST/SLOW timing is visible.")]
    public bool timingSensitiveJudgmentVisuals = true;

    [Tooltip("Show the layered rotating magic circle while a Hold is being pressed. Hold particles remain active when this decoration is hidden.")]
    public bool holdMagicCircleEnabled = true;

    [Tooltip("Automatically reposition the ivory keyboard so its player-side edge stays at the bottom of the screen.")]
    public bool autoAlignKeyboardToScreenBottom = true;

    [Tooltip("Master opacity for the Track border and seven-lane guide lines.")]
    [Range(0f, 1f)]
    public float trackGuideLineOpacity = 1f;

    [Tooltip("Marble surface used by the gameplay Track.")]
    public TrackMarbleTheme trackMarbleTheme = TrackMarbleTheme.Black;

    // 軌道玻璃化：0 = 實心大理石，1 = 只把背景壓暗的一片玻璃。做的不是把材質淡出
    // （那會讓大理石整個不見，影片遮罩那邊就踩過），而是預乘 alpha 讓出後面的背景。
    [Tooltip("How much the track becomes a pane of glass that only darkens the background behind it.")]
    [Range(0f, 1f)]
    public float trackGlass = 0f;

    // 全玻璃時背景最多能透過來多少。音符是紅藍玻璃質感，底下太亮就分不出來。
    [Tooltip("How much background still shows through at full glass. 0.55 = background at 45%.")]
    [Range(0.2f, 1f)]
    public float trackGlassFloor = 0.55f;

    // 背景（金色塵埃＋漸層天空盒）跟著曲子調性中心變色的強度。1 = 實測校準的幅度
    // （五度圈每步 10 度，整首歌偏移中位 10 度、最大 60 度）；2 是刻意誇張，用來
    // 確認它真的在動。0 完全關掉。
    [Tooltip("How strongly the background colour follows the music's key centre. 0 = off, 1 = calibrated, 2 = exaggerated.")]
    [Range(0f, 2f)]
    public float backgroundHarmonyStrength = 1f;

    [Header("Gameplay - Judgment")]
    public bool judgmentMode = true; // when true, use manual judgment mode driven by button mappings
    // Z offset for judge popups (world units). Default 0 keeps popup at anchor Z.
    public float judgePopupZOffset = 0f;

    [Tooltip("Height the judge popup flies to (camera-up units from judgment line). Min 30.")]
    public float judgePopupHeight = 30f;

    [Tooltip("Judgment offset in milliseconds. Positive counters FAST; negative counters SLOW/LATE.")]
    public float judgmentOffsetMs = 0f;

    [Tooltip("Chart/audio alignment in milliseconds. Positive advances the chart clock and counters FAST; negative counters SLOW/LATE.")]
    public float musicPlaybackOffsetMs = 0f;

    [Header("Camera")]
    [Tooltip("World-space Z position for the main camera. Changing this will move the main camera along its Z axis.")]
    public float cameraZ = -20f;

    [Tooltip("Rotation X (degrees) for the main camera. Adjusts the camera's X Euler angle.")]
    public float cameraRotX = 50f;

    [Header("Judgment Line")]
    [Range(0.12f, 0.55f)]
    public float judgmentLineScreenHeight = 0.28f;
    // Debug mode toggle exposed to UI
    public bool debugMode = false;

    [Header("Gameplay - Timing & Protection")]
    [Tooltip("Timing windows (ms): perfect/great/good for tap/hold heads")]
    // 對應原版的三階：**Perfect = PJust、Great = Just、Good = Good**。
    //
    //   PJust : ±41ms  ——判定文件 §8.1「PJust 區間（即 41×2 ms）」，明確給的數字
    //   Just  : ±82ms  ——**文件沒有直接寫**。取 41×2 是因為文件自己就用「41×2」
    //                     來描述 PJust 區間的**寬度**，同一個單位往外推一階最自然，
    //                     而且 41/82/108 剛好把 ±108 的窗切成遞增的三段。
    //                     這一階是推論不是引用，要改先改它。
    //   Good  : ±108ms ——判定文件 §2.2「T_n = [t0−108, t0+108]」，有效判定窗的邊界
    //
    // 舊值是 50/100/150：外圈比原版寬 42ms。放寬本身也放大了「兩顆音符同時可判定」
    // 的情況（103716 對 → 照 108 只剩 69422 對），而那正是優先級規則生效的場合。
    public int perfectMs = 41;   // PJust
    public int greatMs = 82;     // Just
    public int goodMs = 108;     // Good

    [Tooltip("Staccato timing windows (ms)")]
    public int stacPerfectMs = 70;
    public int stacGreatMs = 130;
    public int stacGoodMs = 200;

    [Tooltip("Staccato auto-release offset (ms)")]
    public int stacAutoReleaseMs = 60;

    [Tooltip("If true, simulate staccato auto-release for auto-started staccatos")]
    public bool autoSimulateStaccatoRelease = true;

    [Tooltip("Grace window for quick finger swap (ms)")]
    public int swapGraceMs = 80;

    [Header("Gameplay - Protection")]
    [Tooltip("Enable judgment protection (pending head/tail storage)")]
    public bool protectionEnabled = true;

    // **以下三個已無作用**：鄰鍵保護改成本家的鄰鍵鎖，固定在 InputProtection
    // （左右 4 格、3 幀、只擋晚 4 幀以上的音符），所有判定模式都一樣。欄位保留
    // 只為了不動設定檔的 schema。
    [Tooltip("No longer used. Neighbour protection is the arcade's fixed lock (InputProtection).")]
    public bool enableSpatialStop = true;

    [Tooltip("No longer used. Neighbour protection is the arcade's fixed lock (InputProtection).")]
    public int spatialStopRange = 3;

    [Tooltip("No longer used. Neighbour protection is the arcade's fixed lock (InputProtection).")]
    public int spatialStopWindowMs = 20;

    [Tooltip("Shared input threshold (ms) for co-judge behavior")]
    public int sharedInputThresholdMs = 20;

    [Range(1f, 3f)]
    [Tooltip("How much further away a not-yet-due note counts when matching a press. 1 keeps the original rule, where a late hit is often credited to the next note instead and scored as early.")]
    // **已無作用**：NoteSelector 的排序改成「基準時間最早優先」（判定文件 §3.2.3）
    // 之後，這個偏向未來音符的加權不再參與排序。欄位保留只為了不動設定檔 schema。
    public float futureNotePenalty = 1f;

    [Range(0, 150)]
    [Tooltip("How early (ms) a press may still claim a note. 0 disables the limit. Stops a note being spent on a far-early press when a better-timed one follows, at the cost of missing far-early presses that stand alone.")]
    public int earlyClaimLimitMs = 0;

    [Tooltip("Rewrite a judgment when a closer press arrives while the note's window is still open. Same fix as the early-claim limit without the missed notes; set earlyClaimLimitMs to 0 when using it.")]
    public bool retimeBetterPress = false;

    [Tooltip("Draw the scrolling pedal cues on the track. Off hides them entirely, which is also the quickest way to tell whether a glowing bar lying across the track is one of them.")]
    public bool showPedalCues = true;

    [Tooltip("Mark the middle key of each note with a crystal in recital mode. Off leaves the note art plain for players who would rather find the centre by eye.")]
    public bool showCentreCue = true;

    [Tooltip("Match every song's playback loudness so changing song does not mean changing the volume. Off plays each recording at whatever level it was mastered.")]
    public bool normalizeMusicLoudness = true;

    [Tooltip("Which judgment preset is in force. 0 responsive, 1 classic, 2 custom.")]
    public int judgmentPreset = (int)JudgmentPreset.Custom;

    [Tooltip("How far the player's own touch is mapped onto the chart's velocity scale. 0 plays exactly as hard as the key was struck; 1 reads every strike against the player's own average.")]
    [Range(0f, 1f)]
    public float touchAlignment = 1f;

    [Tooltip("Length of the pale-green strip laid on the track from the judgment line whenever a key goes down, judged or not. 0 hides it.")]
    [Range(0f, 3f)]
    public float strikeCueHeight = 1f;

    [Tooltip("Filter a bouncing contact on the key that was just pressed. The block is lifted the moment the key comes up, so a genuine fast re-strike is never what gets stopped.")]
    public bool blockKeyChatter = true;

    [Range(0, 60)]
    [Tooltip("Performance mode only: the whole judgment window is scaled onto this much deviation around the chart's time, so rushing still sounds like rushing without wrecking the rhythm. 0 sounds every note exactly when the key went down.")]
    public int pianoPerformanceSpreadMs = 20;
    /// <summary>動態係數：把譜面的力度差距以這首曲子的中間值為軸放大幾倍。</summary>
    public float pianoDynamicExpansion = 1.6f;
    [Tooltip("Enable layered piano preview playback on the song selection screen when available.")]
    public bool enablePianoPreview = true;

    [Tooltip("Play the notes hidden inside a note's sub_note data when its key is hit, each at its own charted time. Low-difficulty charts hide the rest of a chord this way: fewer keys to press, but the chord still sounds complete.")]
    public bool playHiddenSubNotes = true;

    [Tooltip("Where gameplay piano sound comes from: the recorded stem, or notes synthesised as you hit them.")]
    public PianoSoundMode pianoSoundMode = PianoSoundMode.GameRecording;
    [Range(0f, 2f)]
    [Tooltip("Volume of the synthesised piano in the two performance modes. Above 1 is clean gain, not compression.")]
    // The samples are peak-normalised and then scaled by the measured velocity
    // curve, so a typical mid-velocity note plays well below full scale — as a
    // real piano does. Making up that difference belongs here, where it is a
    // plain multiply, rather than in the samples, where the only way to raise
    // loudness is to squash the attack transients the instrument lives on.
    public float pianoVoiceVolume = 1.6f;
    [Range(1, 127)]
    [Tooltip("Velocity used for a key that hits no note in Performance mode, so mistakes are audible without burying the melody.")]
    public int wrongKeyVelocity = 60;

    // ── 誤觸過濾（鬆彈簧、排列很緊的鍵盤） ────────────────────────────
    // 空打不扣分，但會發出不該有的鋼琴音，也可能用很差的時機提前吃掉附近的音符。
    [Tooltip("Reject MIDI note-ons softer than this. Brushes on a loose keybed land very low.")]
    [Range(0, 60)]
    public int minNoteOnVelocity = 0;

    [Tooltip("Drop a note-on that is much softer than a near-simultaneous neighbouring key.")]
    public bool brushRejection = true;

    [Tooltip("How much softer than the neighbour still counts as a brush.")]
    [Range(0.2f, 0.9f)]
    public float brushVelocityRatio = 0.6f;

    [Tooltip("How close in time a brush has to be to the key it was brushed against.")]
    [Range(5f, 60f)]
    public float brushWindowMs = 25f;

    [Tooltip("Precise grades each key by its own hardware timestamp; FrameQuantized shares one time per input frame, as the official game does.")]
    // 只在 inputFrameBatching 打開時有作用。FrameQuantized 會讓一個和弦拿到
    // 完全一致的判定；Precise 保留每顆鍵自己的時間戳，精度較高但和弦可能
    // 出現混合判定。
    public JudgmentTiming judgmentTiming = JudgmentTiming.Precise;

    [Tooltip("How far the 16.66ms input frame reaches: off, judgment+sound, or sound only.")]
    // 官方的防擦碰是規則自帶的：同幀內一顆音符判定成功之後，落在它左右各 3 格
    // 的按鍵全部失效。SoundOnly 只讓聲音等幀，判定和畫面照樣即時。
    public InputFrameMode inputFrameMode = InputFrameMode.Full;

    [Tooltip("Hold a soft note-on this long to see whether a louder neighbour follows. 0 disables look-ahead.")]
    [Range(0f, 40f)]
    // 擦碰比正主先到的時候，LooksLikeBrush 結構上抓不到——那一刻還沒有東西可以
    // 比。扣住一下再看是唯一的辦法。判定不受影響（送出時帶原本的硬體時間戳），
    // 只有鋼琴聲會晚這麼多，而且只有低於下面那個力度的才會被扣。
    //
    // **預設關掉**：inputFrameBatching 打開之後，同幀的擦碰是被判定規則直接
    // 吃掉的（§4.1 失效），不需要靠力度猜測。兩個一起開會疊加延遲
    // （最多 16.66 + 22ms），而且擋的是同一批東西。只有在關掉幀批次化、
    // 又想擋「先到的擦碰」時才需要它。
    public float brushLookaheadMs = 0f;

    [Tooltip("Only note-ons softer than this are held for look-ahead. Louder ones never wait.")]
    [Range(0, 127)]
    // 看 [Mistouch] 的 vel 直方圖來設：抓在誤觸分布的上緣，真正要彈的音就幾乎
    // 不會被扣到。設太高會讓弱奏的聲音慢一拍，設太低則漏掉力道大一點的擦碰。
    public int brushLookaheadMaxVelocity = 52;

    [Tooltip("How many semitones away still counts as a neighbouring key.")]
    [Range(1, 4)]
    public int brushSemitones = 2;
    [Tooltip("Chart replays the pedalling restored from the source MIDI; Player follows a real sustain pedal over MIDI CC64.")]
    public PianoPedalSource pianoPedalSource = PianoPedalSource.Chart;
    [Tooltip("Invents pedalling for charts whose source MIDI had none. A guess, so it is off by default; charts with real pedal data ignore it.")]
    public PianoAutoPedal pianoAutoPedal = PianoAutoPedal.Off;

    [Header("Video Background")]
    [Tooltip("Black overlay intensity applied to the gameplay track when a background video is active. 0 = no overlay, 1 = fully black.")]
    [Range(0f, 1f)]
    public float trackVideoDimmer = 0.4f;
}

public class SettingsManager : MonoBehaviour
{
    /// <summary>
    /// Raised immediately after the interface language changes. Runtime-built UI
    /// subscribes to this so an open screen and its popup menus do not keep the
    /// language they happened to be created with.
    /// </summary>
    public static event System.Action<AppLanguage> LanguageChanged;
    // 取得目前輸入模式
    public InputModeType InputMode {
        get => gameSettings != null ? gameSettings.inputMode : InputModeType.Keyboard;
    }

    public VisualLayoutMode CurrentVisualLayoutMode
    {
        get => gameSettings != null ? gameSettings.visualLayoutMode : VisualLayoutMode.General;
    }

    public bool UsePiano88VisualLayout => CurrentVisualLayoutMode == VisualLayoutMode.Piano88;

    // 設定輸入模式
    public void SetInputMode(InputModeType mode)
    {
        if (gameSettings != null)
        {
            gameSettings.inputMode = mode;
        }
    }

    public void SetVisualLayoutMode(VisualLayoutMode mode)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }

        gameSettings.visualLayoutMode = mode;
    }
    private static SettingsManager _instance;
    public static SettingsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<SettingsManager>();
                if (_instance == null)
                {
                    GameObject settingsObj = new GameObject("SettingsManager");
                    _instance = settingsObj.AddComponent<SettingsManager>();
                    DontDestroyOnLoad(settingsObj);
                }
            }
            return _instance;
        }
    }

    [Header("Game Settings")]
    public GameSettings gameSettings = new GameSettings();

    private const string SETTINGS_KEY = "GameSettings";
    private const int CURRENT_SETTINGS_VERSION = 7;

    void Awake()
    {
        // Singleton pattern
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        
        // Ensure this is a root GameObject for DontDestroyOnLoad
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);

        // Load settings
        LoadSettings();
    }

    void Start()
    {
        // Apply loaded settings
        ApplySettings();
    }

    public void SaveSettings()
    {
        EnsureJudgmentFlagsConsistency();
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.settingsVersion = CURRENT_SETTINGS_VERSION;
        string json = JsonUtility.ToJson(gameSettings);
        PlayerPrefs.SetString(SETTINGS_KEY, json);
        PlayerPrefs.Save();
        //Debug.Log("Settings saved");
    }

    public void LoadSettings()
    {
        bool settingsChanged = false;
        if (PlayerPrefs.HasKey(SETTINGS_KEY))
        {
            string json = PlayerPrefs.GetString(SETTINGS_KEY);
            gameSettings = JsonUtility.FromJson<GameSettings>(json);
            bool legacySettings = !json.Contains("\"settingsVersion\"");
            if (gameSettings == null)
            {
                gameSettings = new GameSettings();
                settingsChanged = true;
            }
            else if (legacySettings)
            {
                // Older builds could save the runtime settings card's temporary
                // zero before it had copied the intended 80% default.  Only
                // migrate unversioned data; after this, an explicit 0% remains 0.
                if (gameSettings.musicVolume <= 0.0001f)
                    gameSettings.musicVolume = 0.8f;
                if (gameSettings.pianoVolume <= 0.0001f)
                    gameSettings.pianoVolume = gameSettings.musicVolume;
                gameSettings.settingsVersion = CURRENT_SETTINGS_VERSION;
                settingsChanged = true;
            }
            // Version 6 used a 100ms shared-input window. On dense charts this
            // pre-judged real 80ms repetitions with the preceding press, forcing
            // unavoidable FAST/Great results. Version 7 limits sharing to chord jitter.
            if (gameSettings != null && gameSettings.settingsVersion < 7)
            {
                gameSettings.sharedInputThresholdMs = 20;
                gameSettings.settingsVersion = CURRENT_SETTINGS_VERSION;
                settingsChanged = true;
            }
            // A save written before this setting existed deserialises to 0, which
            // would weight future notes to zero distance and swallow every press.
            if (gameSettings != null && gameSettings.futureNotePenalty < 1f)
            {
                gameSettings.futureNotePenalty = 1f;
            }
            if (gameSettings != null && !json.Contains("blockKeyChatter"))
            {
                gameSettings.blockKeyChatter = true;
            }
            if (gameSettings != null && !json.Contains("showPedalCues"))
            {
                gameSettings.showPedalCues = true;
            }
            if (gameSettings != null && !json.Contains("showCentreCue"))
            {
                gameSettings.showCentreCue = true;
            }
            if (gameSettings != null && !json.Contains("normalizeMusicLoudness"))
            {
                gameSettings.normalizeMusicLoudness = true;
            }
            if (gameSettings != null && !json.Contains("touchAlignment"))
            {
                gameSettings.touchAlignment = 1f;
            }
            if (gameSettings != null && !json.Contains("strikeCueHeight"))
            {
                gameSettings.strikeCueHeight = 1f;
            }
            if (gameSettings != null && !json.Contains("judgmentPreset"))
            {
                // 舊存檔一律當成「自訂」。玩家已經調過的那一組值就是他要的，
                // 更新一次遊戲不該把它換成某個預設 —— 那會在他毫無預期的時候
                // 改掉手感。
                gameSettings.judgmentPreset = (int)JudgmentPreset.Custom;
            }
            if (gameSettings != null && !json.Contains("enablePianoPreview"))
            {
                gameSettings.enablePianoPreview = true;
            }
            if (gameSettings != null && !json.Contains("pianoVolume"))
            {
                gameSettings.pianoVolume = gameSettings.musicVolume;
            }
            if (gameSettings != null && !json.Contains("trackVideoDimmer"))
            {
                gameSettings.trackVideoDimmer = 0.4f;
            }
            if (gameSettings != null && !json.Contains("visualLayoutMode"))
            {
                gameSettings.visualLayoutMode = VisualLayoutMode.General;
            }
            if (gameSettings != null && !json.Contains("noteVisualHeight"))
            {
                gameSettings.noteVisualHeight = 2f;
            }
            if (gameSettings != null && !json.Contains("judgmentMeshHeight"))
            {
                gameSettings.judgmentMeshHeight = 1f;
            }
            if (gameSettings != null && !json.Contains("timingSensitiveJudgmentVisuals"))
            {
                gameSettings.timingSensitiveJudgmentVisuals = true;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("holdMagicCircleEnabled"))
            {
                gameSettings.holdMagicCircleEnabled = true;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("autoAlignKeyboardToScreenBottom"))
            {
                gameSettings.autoAlignKeyboardToScreenBottom = true;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("minNoteOnVelocity"))
            {
                gameSettings.minNoteOnVelocity = 0;
                gameSettings.brushRejection = true;
                gameSettings.brushVelocityRatio = 0.6f;
                gameSettings.brushWindowMs = 25f;
                gameSettings.brushSemitones = 2;
            }
            if (gameSettings != null && !json.Contains("inputFrameMode"))
            {
                // 從舊的 bool 欄位搬過來，不要讓已經調過的人被重設。
                gameSettings.inputFrameMode = json.Contains("\"inputFrameBatching\":false")
                    ? InputFrameMode.Off
                    : InputFrameMode.Full;
            }
            if (gameSettings != null && !json.Contains("judgmentTiming"))
            {
                gameSettings.judgmentTiming = JudgmentTiming.Precise;
            }
            if (gameSettings != null && !json.Contains("brushLookaheadMs"))
            {
                gameSettings.brushLookaheadMs = 0f;
                gameSettings.brushLookaheadMaxVelocity = 52;
            }
            if (gameSettings != null && !json.Contains("trackGlass"))
            {
                // 舊的設定檔沒有這個鍵，JsonUtility 會留 0 —— 剛好就是「實心」，
                // 但明寫出來比較不會以為是巧合。
                gameSettings.trackGlass = 0f;
                gameSettings.trackGlassFloor = 0.55f;
                gameSettings.backgroundHarmonyStrength = 1f;
            }
            if (gameSettings != null && !json.Contains("trackGuideLineOpacity"))
            {
                gameSettings.trackGuideLineOpacity = 1f;
            }
            if (gameSettings != null && !json.Contains("trackMarbleTheme"))
            {
                gameSettings.trackMarbleTheme = TrackMarbleTheme.Black;
            }
            if (gameSettings != null && !json.Contains("\"language\""))
            {
                gameSettings.language = AppLanguage.TraditionalChinese;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("\"comboDisplayPosition\""))
            {
                gameSettings.comboDisplayPosition = ComboDisplayPosition.TopCenter;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("\"comboFontSize\""))
            {
                gameSettings.comboFontSize = 48f;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("\"trackComboScreenHeight\""))
            {
                gameSettings.trackComboScreenHeight = 0.35f;
                settingsChanged = true;
            }
            if (gameSettings != null && !json.Contains("\"judgmentLineScreenHeight\""))
            {
                gameSettings.judgmentLineScreenHeight = 0.28f;
                settingsChanged = true;
            }
            //Debug.Log("Settings loaded");
        }
        else
        {
            //Debug.Log("No saved settings found, using defaults");
            if (gameSettings == null)
            {
                gameSettings = new GameSettings();
            }
            gameSettings.trackGuideLineOpacity = 1f;
        }

        // Unity scenes and older JSON saves do not contain newly introduced fields.
        // Treat their zero value as missing so the player starts at the intended 2.0 default.
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }
        if (gameSettings.noteVisualHeight < 0.5f)
        {
            gameSettings.noteVisualHeight = 2f;
        }
        if (gameSettings.judgmentMeshHeight < 0.25f)
        {
            gameSettings.judgmentMeshHeight = 1f;
        }

        // Guarantee we never leave both DebugMode and JudgmentMode disabled simultaneously
        if (EnsureJudgmentFlagsConsistency())
        {
            settingsChanged = true;
        }
        if (settingsChanged) SaveSettings();
        Debug.Log($"[SettingsManager] Audio settings loaded: version={gameSettings.settingsVersion} music={gameSettings.musicVolume:0.###} piano={gameSettings.pianoVolume:0.###}");
        Debug.Log($"[Timing Config] input={gameSettings.inputMode}, judgment={gameSettings.judgmentOffsetMs:+0.0;-0.0;0.0}ms, " +
            $"music={gameSettings.musicPlaybackOffsetMs:+0.0;-0.0;0.0}ms, noteHeight={gameSettings.noteVisualHeight:0.00}u, " +
            $"sharedInput={Mathf.Clamp(gameSettings.sharedInputThresholdMs, 0, 20)}ms");
    }

    private bool EnsureJudgmentFlagsConsistency()
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
            return true;
        }

        bool changed = false;

        // If both flags accidentally end up false, prefer normal judgment mode.
        if (!gameSettings.debugMode && !gameSettings.judgmentMode)
        {
            gameSettings.judgmentMode = true;
            changed = true;
        }
        // If both flags are true (legacy data), disable judgment mode when debug is active.
        else if (gameSettings.debugMode && gameSettings.judgmentMode)
        {
            gameSettings.judgmentMode = false;
            changed = true;
        }

        // Judgment protection is part of the core gameplay contract. Older
        // settings files may contain the temporary disabled value from the
        // preview implementation; migrate those files back to enabled.
        if (!gameSettings.protectionEnabled)
        {
            gameSettings.protectionEnabled = true;
            changed = true;
        }

        return changed;
    }

    public void ApplySettings()
    {
        // Apply music volume to Conductor (so it affects only the music playback, not hit sounds)
        var conductor = FindFirstObjectByType<Conductor>();
        if (conductor != null)
        {
            conductor.SetMusicVolume(gameSettings.musicVolume);
        }

        // Apply game start delay to GameManager
        var gmInstance = GameManager.Instance;
        if (gmInstance != null)
        {
            gmInstance.SetStartDelay(gameSettings.gameStartDelay);
            gmInstance.SetPianoLayerVolume(gameSettings.pianoVolume);
        }
        
        // Apply default speed to UIManager
        UIManager uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager != null)
        {
            uiManager.SetDefaultSpeed(gameSettings.defaultSpeed);
        }

        // Apply the saved border/guide opacity to active and inactive Track visuals.
        SetTrackGuideLineOpacity(gameSettings.trackGuideLineOpacity);
        SetTrackGlass(gameSettings.trackGlass);
        SetBackgroundHarmonyStrength(gameSettings.backgroundHarmonyStrength);

        // Keep the runtime judgment light column in sync with the player setting.
        SetJudgmentMeshHeight(gameSettings.judgmentMeshHeight);

        // Use a camera/player-facing marble shader and apply the selected stone colour.
        SetTrackMarbleTheme(gameSettings.trackMarbleTheme);

        // Apply track dimmer to GameBackgroundManager so track visuals update immediately.
        var backgroundManager = FindFirstObjectByType<GameBackgroundManager>();
        if (backgroundManager != null)
        {
            backgroundManager.ApplyTrackDimmerSetting(gameSettings.trackVideoDimmer);
        }
        
        //Debug.Log($"Settings applied - Hit: {gameSettings.hitSoundVolume}, Music: {gameSettings.musicVolume}, Speed: {gameSettings.defaultSpeed}, Delay: {gameSettings.gameStartDelay}");

        // Apply judge popup Z offset to SimpleJudgePopupManager if present
        var popupMgr = FindFirstObjectByType<SimpleJudgePopupManager>();
        if (popupMgr != null)
        {
            // Assign judgePopupZOffset via reflection if the field/property exists to avoid compile-time dependency
            var pmType = popupMgr.GetType();
            var fz = pmType.GetField("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fz != null)
            {
                fz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
            }
            else
            {
                var pz = pmType.GetProperty("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (pz != null && pz.CanWrite) pz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
            }

            // If a NoteSpawner exists, point the popup manager at it to allow anchor interpolation
            var ns = FindFirstObjectByType<NoteSpawner>();
            if (ns != null)
            {
                // Use reflection to assign spawnAnchorTransform if the field/property exists
                var f = pmType.GetField("spawnAnchorTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(popupMgr, ns.transform);
                }
                else
                {
                    var p = pmType.GetProperty("spawnAnchorTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (p != null && p.CanWrite) p.SetValue(popupMgr, ns.transform);
                }
            }
        }

        // Apply camera settings to the main camera if available
        var cam = Camera.main;
        if (cam != null)
        {
            // Set camera Z while preserving x/y
            var p = cam.transform.position;
            p.z = gameSettings.cameraZ;
            cam.transform.position = p;

            // Set camera rotation X while preserving y/z
            var e = cam.transform.eulerAngles;
            e.x = gameSettings.cameraRotX;
            cam.transform.eulerAngles = e;

            SetJudgmentLineHeight(gameSettings.judgmentLineScreenHeight);
            IvoryLaneKeyboard.SetScreenBottomAlignment(
                gameSettings.autoAlignKeyboardToScreenBottom, cam);
        }

        // Apply timing & protection settings to judgment systems (best-effort)
        try
        {
            try
            {
                Judgment.Protection.SetProtectionEnabled(gameSettings.protectionEnabled);
            }
            catch { }

            // Note matching weight lives on the selector, not the manager — it
            // holds no per-manager state, so it applies even before a scene loads.
            try
            {
                Judgment.NoteSelector.FutureNotePenalty =
                    Mathf.Clamp(gameSettings.futureNotePenalty, 1f, 3f);
                Judgment.NoteSelector.EarlyClaimLimitMs =
                    Mathf.Clamp(gameSettings.earlyClaimLimitMs, 0, 150);
                Judgment.EarlyClaimCorrector.Enabled = gameSettings.retimeBetterPress;
                PedalNoteRenderer.CuesHidden = !gameSettings.showPedalCues;
            }
            catch { }

            // Push values into JudgmentManager instance if present
            try
            {
                var jm = Judgment.JudgmentManager.Instance;
                if (jm != null)
                {
                    jm.perfectMs = gameSettings.perfectMs;
                    jm.greatMs = gameSettings.greatMs;
                    jm.goodMs = gameSettings.goodMs;

                    jm.stacPerfectMs = gameSettings.stacPerfectMs;
                    jm.stacGreatMs = gameSettings.stacGreatMs;
                    jm.stacGoodMs = gameSettings.stacGoodMs;

                    jm.stacAutoReleaseMs = gameSettings.stacAutoReleaseMs;
                    jm.autoSimulateStaccatoRelease = gameSettings.autoSimulateStaccatoRelease;
                    jm.swapGraceMs = gameSettings.swapGraceMs;

                    // sharedInputThresholdMs property routes to the internal InputProtection
                    try { jm.sharedInputThresholdMs = gameSettings.sharedInputThresholdMs; } catch { }
                    try { jm.KeyChatterGuard = gameSettings.blockKeyChatter; } catch { }

                    // 鄰鍵保護不再讀設定：所有判定模式一律套用本家的鄰鍵鎖
                    // （InputProtection：左右 4 格、3 幀、只擋晚 4 幀以上的音符）。
                    // enableSpatialStop / spatialStopRange / spatialStopWindowMs 只留在
                    // 設定檔裡，不動 schema。
                }
            }
            catch { }
        }
        catch { }
    }

    // Judgment mode accessor
    public bool JudgmentMode => gameSettings != null ? gameSettings.judgmentMode : false;

    // Public accessors for other scripts
    public float HitSoundVolume => gameSettings.hitSoundVolume;
    public float MusicVolume => gameSettings.musicVolume;

    /// <summary>
    /// The music volume gameplay should use, which is silence while practising.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MusicVolume"/> on purpose: the song select
    /// preview reads that one, and a player who left practice mode on would
    /// otherwise find the whole song list has gone quiet.
    ///
    /// Silenced rather than stopped. The conductor takes the song position from
    /// the music source's own clock, so the track has to keep running -- which is
    /// the same trick the keysound-only songs already use with their silent file.
    /// </remarks>
    public float GameplayMusicVolume => PracticeMode ? 0f : gameSettings.musicVolume;

    /// <summary>練習模式：不計分、不放背景音樂、速度可調。</summary>
    public bool PracticeMode => gameSettings != null && gameSettings.practiceMode;

    /// <summary>練習模式的速度倍率。不在練習模式時一律是 1。</summary>
    public float PracticeSpeed => PracticeMode
        ? Mathf.Clamp(gameSettings.practiceSpeed, 0.5f, 1.5f)
        : 1f;

    public void SetPracticeMode(bool on)
    {
        if (gameSettings == null) return;
        gameSettings.practiceMode = on;
        SaveSettings();
    }

    /// <summary>演奏會模式：誤觸亮紅燈、鍵道要落在音符正中央才滿分。</summary>
    /// <remarks>
    /// 和練習模式不同，這個模式照常計分、照常進榜——它加的是一個維度，不是
    /// 一個放寬。譜面上一顆音符佔好幾格，一般模式裡打哪一格都一樣；演奏會
    /// 模式把「打在哪一格」也算進去，偏掉的那一下仍然成立，只是拿不到那顆音
    /// 符的全部配分。
    /// </remarks>
    public bool RecitalMode => gameSettings != null && gameSettings.recitalMode;

    /// <summary>
    /// 遊玩中實際生效的演奏會模式：玩家的設定，或這一課教學要求的。選歌畫面的開關
    /// 要讀 <see cref="RecitalMode"/>，不然上完演奏會那一課回來開關會被顯示成開著。
    /// </summary>
    /// <remarks>
    /// 教學曲完全由課程決定，玩家的開關不算：開著演奏會模式去上第 1 課，不該被當成
    /// 演奏會評分；第 14 課也不該因為開關關著就不是演奏會。選歌畫面在選到教學曲時
    /// 會把開關鎖住，兩邊講的是同一件事。
    /// </remarks>
    public bool RecitalModeInPlay => TutorialSession.IsActive
        ? TutorialSession.ForcesRecitalNow
        : RecitalMode;

    public void SetRecitalMode(bool on)
    {
        if (gameSettings == null) return;
        gameSettings.recitalMode = on;
        SaveSettings();
    }

    /// <summary>
    /// 玩家上一次挑的難度階，選歌畫面用它決定書本的顏色和裝飾。-1 = 還沒挑過。
    /// </summary>
    /// <remarks>
    /// 存起來而不是只記在這一輪：玩家挑難度是在表達「我現在練的是這一階」，
    /// 那件事不會因為關掉遊戲就變。沒挑過的時候維持舊行為（照最高難度）。
    /// </remarks>
    public int PreferredDifficultyTier => gameSettings != null ? gameSettings.preferredDifficultyTier : -1;

    public void SetPreferredDifficultyTier(int tier)
    {
        if (gameSettings == null) return;
        if (gameSettings.preferredDifficultyTier == tier) return;
        gameSettings.preferredDifficultyTier = tier;
        SaveSettings();
    }

    public void SetPracticeSpeed(float speed)
    {
        if (gameSettings == null) return;
        gameSettings.practiceSpeed = Mathf.Clamp(speed, 0.5f, 1.5f);
        SaveSettings();
    }
    public float PianoVolume => gameSettings.pianoVolume;
    public float DefaultSpeed => gameSettings.defaultSpeed;

    public float GameStartDelay => gameSettings.gameStartDelay;
    public float NoteVisualHeight => gameSettings != null ? Mathf.Clamp(gameSettings.noteVisualHeight, 0.5f, 4f) : 2f;
    public float JudgmentMeshHeight => gameSettings != null ? Mathf.Clamp(gameSettings.judgmentMeshHeight, 0.25f, 4f) : 1f;
    public bool TimingSensitiveJudgmentVisuals => gameSettings == null || gameSettings.timingSensitiveJudgmentVisuals;
    public bool HoldMagicCircleEnabled => gameSettings == null || gameSettings.holdMagicCircleEnabled;
    public bool AutoAlignKeyboardToScreenBottom => gameSettings == null || gameSettings.autoAlignKeyboardToScreenBottom;
    public float TrackGuideLineOpacity => gameSettings != null ? Mathf.Clamp01(gameSettings.trackGuideLineOpacity) : 1f;
    public float TrackGlass => gameSettings != null ? Mathf.Clamp01(gameSettings.trackGlass) : 0f;
    public float TrackGlassFloor => gameSettings != null
        ? Mathf.Clamp(gameSettings.trackGlassFloor, 0.2f, 1f) : 0.55f;
    public float BackgroundHarmonyStrength => gameSettings != null
        ? Mathf.Clamp(gameSettings.backgroundHarmonyStrength, 0f, 2f) : 1f;
    public TrackMarbleTheme CurrentTrackMarbleTheme => gameSettings != null ? gameSettings.trackMarbleTheme : TrackMarbleTheme.Black;
    public AppLanguage CurrentLanguage => gameSettings != null
        ? gameSettings.language
        : AppLanguage.TraditionalChinese;
    public ComboDisplayPosition CurrentComboDisplayPosition => gameSettings != null
        ? gameSettings.comboDisplayPosition
        : ComboDisplayPosition.TopCenter;
    public float ComboFontSize => gameSettings != null
        ? Mathf.Clamp(gameSettings.comboFontSize, 24f, 96f)
        : 48f;
    public float TrackComboScreenHeight => gameSettings != null
        ? Mathf.Clamp01(gameSettings.trackComboScreenHeight)
        : 0.35f;
    public float TrackVideoDimmer => gameSettings != null ? gameSettings.trackVideoDimmer : 0f;

    // Setters with immediate application
    public void SetHitSoundVolume(float volume)
    {
        gameSettings.hitSoundVolume = Mathf.Clamp01(volume);
    }

    public void SetMusicVolume(float volume)
    {
        gameSettings.musicVolume = Mathf.Clamp01(volume);
        // Apply immediately to Conductor if present (affect music only)
        var conductor = FindFirstObjectByType<Conductor>();
        if (conductor != null)
        {
            conductor.SetMusicVolume(gameSettings.musicVolume);
        }
    }
    
    public void SetPianoVolume(float volume)
    {
        gameSettings.pianoVolume = Mathf.Clamp01(volume);
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SetPianoLayerVolume(gameSettings.pianoVolume);
        }
        var songSelection = SongSelectionManager.Instance;
        if (songSelection != null)
        {
            songSelection.RefreshPreviewAudio(false);
        }
    }

    public void SetDefaultSpeed(float speed)
    {
        gameSettings.defaultSpeed = Mathf.Clamp(speed, 10f, 2000f);
    }

    public void SetGameStartDelay(float delay)
    {
        gameSettings.gameStartDelay = Mathf.Clamp(delay, 2f, 5f);
    }

    public void SetNoteVisualHeight(float height)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }
        gameSettings.noteVisualHeight = Mathf.Clamp(height, 0.5f, 4f);
    }

    public void SetJudgmentMeshHeight(float height)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }
        gameSettings.judgmentMeshHeight = Mathf.Clamp(height, 0.25f, 4f);
        if (NoteJudgementMeshManager.Instance != null)
            NoteJudgementMeshManager.Instance.SetMeshHeight(gameSettings.judgmentMeshHeight);
    }

    public void SetTimingSensitiveJudgmentVisuals(bool enabled)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.timingSensitiveJudgmentVisuals = enabled;
    }

    public void SetHoldMagicCircleEnabled(bool enabled)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.holdMagicCircleEnabled = enabled;
        if (ParticleEffectPlayer.Instance != null)
            ParticleEffectPlayer.Instance.SetHoldMagicCircleVisible(enabled);
    }

    public void SetAutoAlignKeyboardToScreenBottom(bool enabled)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.autoAlignKeyboardToScreenBottom = enabled;
        IvoryLaneKeyboard.SetScreenBottomAlignment(enabled, Camera.main);
    }

    public void SetTrackGuideLineOpacity(float opacity)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }

        gameSettings.trackGuideLineOpacity = Mathf.Clamp01(opacity);
        TrackGuideLineOverlay[] overlays = FindObjectsByType<TrackGuideLineOverlay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < overlays.Length; i++)
        {
            if (overlays[i] != null)
            {
                overlays[i].SetOpacity(gameSettings.trackGuideLineOpacity);
            }
        }
    }

    public void SetTrackGlass(float glass)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.trackGlass = Mathf.Clamp01(glass);
        Effects.BackgroundHarmonyDriver.GetOrCreate()?.SetTrackGlass(
            gameSettings.trackGlass, TrackGlassFloor);
    }

    public void SetTrackGlassFloor(float floor)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.trackGlassFloor = Mathf.Clamp(floor, 0.2f, 1f);
        Effects.BackgroundHarmonyDriver.GetOrCreate()?.SetTrackGlass(
            TrackGlass, gameSettings.trackGlassFloor);
    }

    public void SetBackgroundHarmonyStrength(float strength)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.backgroundHarmonyStrength = Mathf.Clamp(strength, 0f, 2f);
        Effects.BackgroundHarmonyDriver.GetOrCreate()?.SetHarmonyStrength(
            gameSettings.backgroundHarmonyStrength);
    }

    public void SetTrackMarbleTheme(TrackMarbleTheme theme)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }

        gameSettings.trackMarbleTheme = theme;
        TrackMarbleThemeController[] controllers = FindObjectsByType<TrackMarbleThemeController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null)
            {
                controllers[i].SetTheme(theme);
            }
        }
    }

    public void SetTrackVideoDimmer(float value)
    {
        if (gameSettings == null)
        {
            gameSettings = new GameSettings();
        }
        gameSettings.trackVideoDimmer = Mathf.Clamp01(value);
        var backgroundManager = FindFirstObjectByType<GameBackgroundManager>();
        if (backgroundManager != null)
        {
            backgroundManager.ApplyTrackDimmerSetting(gameSettings.trackVideoDimmer);
        }
    }

    public void SetJudgePopupZOffset(float z)
    {
        gameSettings.judgePopupZOffset = z;
        var popupMgr = FindFirstObjectByType<SimpleJudgePopupManager>();
        if (popupMgr != null)
        {
            var pmType = popupMgr.GetType();
            var fz = pmType.GetField("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fz != null)
            {
                fz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
                Debug.Log($"SettingsManager: SetJudgePopupZOffset -> set field on popupMgr to {gameSettings.judgePopupZOffset}");
            }
            else
            {
                var pz = pmType.GetProperty("judgePopupZOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (pz != null && pz.CanWrite) pz.SetValue(popupMgr, gameSettings.judgePopupZOffset);
                Debug.Log($"SettingsManager: SetJudgePopupZOffset -> set property on popupMgr to {gameSettings.judgePopupZOffset}");
            }
        }
    }

    public float CameraZ => gameSettings.cameraZ;
    public float CameraRotX => gameSettings.cameraRotX;
    public float JudgmentLineHeight => gameSettings != null
        ? Mathf.Clamp(gameSettings.judgmentLineScreenHeight, 0.12f, 0.55f)
        : 0.28f;

    public void SetJudgmentLineHeight(float height)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.judgmentLineScreenHeight = Mathf.Clamp(height, 0.12f, 0.55f);
        JudgmentLineBar[] bars = FindObjectsByType<JudgmentLineBar>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bars.Length; i++)
            if (bars[i] != null) bars[i].SetScreenHeight(gameSettings.judgmentLineScreenHeight);
    }

    public void SetCameraZ(float z)
    {
        gameSettings.cameraZ = z;
        var cam = Camera.main;
        if (cam != null)
        {
            var p = cam.transform.position;
            p.z = gameSettings.cameraZ;
            cam.transform.position = p;
            SetJudgmentLineHeight(gameSettings.judgmentLineScreenHeight);
            IvoryLaneKeyboard.SetScreenBottomAlignment(
                gameSettings.autoAlignKeyboardToScreenBottom, cam);
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        if (gameSettings.language == language) return;
        gameSettings.language = language;
        LanguageChanged?.Invoke(language);
    }

    public void SetComboDisplayPosition(ComboDisplayPosition position)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.comboDisplayPosition = position;
        Judgment.JudgmentManager.Instance?.RefreshComboDisplayLayout();
    }

    public void SetComboFontSize(float size)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.comboFontSize = Mathf.Clamp(size, 24f, 96f);
        Judgment.JudgmentManager.Instance?.RefreshComboDisplayLayout();
    }

    public void SetTrackComboScreenHeight(float height)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.trackComboScreenHeight = Mathf.Clamp01(height);
        Judgment.JudgmentManager.Instance?.RefreshComboDisplayLayout();
    }

    private static void UpdateJudgmentLineCameraAnchor(Camera camera)
    {
        JudgmentLineBar[] bars = FindObjectsByType<JudgmentLineBar>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bars.Length; i++)
            if (bars[i] != null) bars[i].ApplyCameraZResponsiveAnchor(camera);
    }

    public void SetCameraRotX(float rotX)
    {
        gameSettings.cameraRotX = rotX;
        var cam = Camera.main;
        if (cam != null)
        {
            var e = cam.transform.eulerAngles;
            e.x = gameSettings.cameraRotX;
            cam.transform.eulerAngles = e;
            SetJudgmentLineHeight(gameSettings.judgmentLineScreenHeight);
            IvoryLaneKeyboard.SetScreenBottomAlignment(
                gameSettings.autoAlignKeyboardToScreenBottom, cam);
        }
    }

    public float JudgePopupZOffset => gameSettings.judgePopupZOffset;

    public float JudgePopupHeight => gameSettings != null ? gameSettings.judgePopupHeight : 30f;
    public void SetJudgePopupHeight(float h)
    {
        gameSettings.judgePopupHeight = Mathf.Max(30f, h);
        SaveSettings();
    }

    public float JudgmentOffsetMs => gameSettings != null ? gameSettings.judgmentOffsetMs : 0f;

    public float MusicPlaybackOffsetMs => gameSettings != null ? gameSettings.musicPlaybackOffsetMs : 0f;

    public bool EnablePianoPreview => gameSettings != null && gameSettings.enablePianoPreview;
    /// <summary>敲到帶隱藏音的鍵時，要不要把那些音照譜面時間放出來。</summary>
    public bool PlayHiddenSubNotes => gameSettings == null || gameSettings.playHiddenSubNotes;

    public PianoSoundMode CurrentPianoSoundMode => gameSettings != null
        ? gameSettings.pianoSoundMode
        : PianoSoundMode.GameRecording;

    /// <summary>
    /// True when the selected chart itself declares that it ships no recording
    /// (<c>noBackgroundMusic</c> in register.json).  Such a song would be
    /// completely silent in GameRecording mode, so it turns the synthesised
    /// piano on regardless of the player's global choice.
    /// </summary>
    /// <remarks>
    /// Cached against the selected option's identity rather than pushed in by
    /// GameManager: this is read once per struck note, and a flag somebody else
    /// has to set and clear goes stale the first time a path forgets to reset it.
    /// </remarks>
    public bool SongForcesSynthesisedPiano
    {
        get
        {
            var selected = SongSelectionManager.Instance != null
                ? SongSelectionManager.Instance.GetSelectedSong()
                : null;
            if (!ReferenceEquals(selected, keysoundOnlySongCacheKey))
            {
                keysoundOnlySongCacheKey = selected;
                keysoundOnlySongCached = selected != null && selected.noBackgroundMusic;
            }
            // 練習模式關掉背景音樂，所以它和 keysound-only 的曲子處境一樣：不合成
            // 的話整局只剩打擊音效。
            return keysoundOnlySongCached || PracticeMode;
        }
    }

    private SongSelectionManager.SongOption keysoundOnlySongCacheKey;
    private bool keysoundOnlySongCached;

    /// <summary>True when notes are synthesised as the player hits them.</summary>
    public bool UsesSynthesisedPiano =>
        CurrentPianoSoundMode != PianoSoundMode.GameRecording || SongForcesSynthesisedPiano;

    /// <summary>True when the player's own touch drives velocity and note length.</summary>
    public bool UsesPlayerTouch => CurrentPianoSoundMode == PianoSoundMode.Hardcore;

    public float PianoVoiceVolume => gameSettings != null
        ? Mathf.Clamp(gameSettings.pianoVoiceVolume, 0f, 2f)
        : 1f;

    /// <summary>
    /// How far the chart's dynamics are opened up before they are played.
    /// </summary>
    /// <remarks>
    /// 1 plays the restored MIDI velocities literally. Above that the distance
    /// from the piece's own middle is multiplied, so soft notes drop further and
    /// loud ones rise further.
    ///
    /// **Why it is wanted at all.** The samples are layered properly -- 35 dB
    /// from the softest to the loudest on middle C -- but the restored charts do
    /// not use that range. In 鬼火, 60% of the notes sit at velocity 112 and 24%
    /// at 96, and those two layers are 2.6 dB apart: 84% of the piece is played
    /// within two and a half decibels of itself, which is why it sounds flat.
    ///
    /// **Why around the piece's own middle rather than 64.** A piece that is
    /// quiet throughout would otherwise be made quieter still, and a loud one
    /// pushed into the ceiling. Expanding about the chart's own centre changes
    /// the *contrast* without moving the overall level.
    ///
    /// This is deliberately not faithful to the source. It is monotonic, so a
    /// note written louder is still played louder, but the distances are not the
    /// composer's -- which is why it is a setting and not a constant.
    /// </remarks>
    public float PianoDynamicExpansion => gameSettings != null
        ? Mathf.Clamp(gameSettings.pianoDynamicExpansion, 1f, 3f)
        : 1.6f;

    public void SetPianoDynamicExpansion(float factor)
    {
        if (gameSettings == null) return;
        float clamped = Mathf.Clamp(factor, 1f, 3f);
        if (Mathf.Approximately(gameSettings.pianoDynamicExpansion, clamped)) return;
        gameSettings.pianoDynamicExpansion = clamped;
        SaveSettings();
        ApplySettings();
    }

    /// <summary>How far a Performance-mode strike may sit from the chart's time.</summary>
    public float PianoPerformanceSpreadMs => gameSettings != null
        ? Mathf.Clamp(gameSettings.pianoPerformanceSpreadMs, 0, 60)
        : 20f;

    public void SetPianoPerformanceSpreadMs(int milliseconds)
    {
        if (gameSettings == null) return;
        int clamped = Mathf.Clamp(milliseconds, 0, 60);
        if (gameSettings.pianoPerformanceSpreadMs == clamped) return;
        gameSettings.pianoPerformanceSpreadMs = clamped;
        SaveSettings();
        ApplySettings();
    }

    public int MinNoteOnVelocity => gameSettings != null
        ? Mathf.Clamp(gameSettings.minNoteOnVelocity, 0, 60) : 0;
    public bool BrushRejectionEnabled => gameSettings == null || gameSettings.brushRejection;
    public float BrushVelocityRatio => gameSettings != null
        ? Mathf.Clamp(gameSettings.brushVelocityRatio, 0.2f, 0.9f) : 0.6f;
    public float BrushWindowMs => gameSettings != null
        ? Mathf.Clamp(gameSettings.brushWindowMs, 5f, 60f) : 25f;
    public InputFrameMode CurrentInputFrameMode => gameSettings != null
        ? gameSettings.inputFrameMode
        : InputFrameMode.Full;

    /// <summary>True when presses are grouped into frames at all.</summary>
    public bool InputFrameBatching => CurrentInputFrameMode != InputFrameMode.Off;

    /// <summary>True when only the keysound waits for the frame; judgment stays immediate.</summary>
    public bool InputFrameSoundOnly => CurrentInputFrameMode == InputFrameMode.SoundOnly;

    public JudgmentTiming CurrentJudgmentTiming => gameSettings != null
        ? gameSettings.judgmentTiming
        : JudgmentTiming.Precise;

    /// <summary>True when every key in an input frame is graded on one shared time.</summary>
    public bool UsesFrameQuantizedJudgment =>
        CurrentJudgmentTiming == JudgmentTiming.FrameQuantized;

    public void SetInputFrameMode(InputFrameMode mode)
    {
        if (gameSettings == null || gameSettings.inputFrameMode == mode) return;
        gameSettings.inputFrameMode = mode;
        SaveSettings();
    }

    public void SetBrushLookaheadMs(float milliseconds)
    {
        if (gameSettings == null) return;
        float clamped = Mathf.Clamp(milliseconds, 0f, 40f);
        if (Mathf.Approximately(gameSettings.brushLookaheadMs, clamped)) return;
        gameSettings.brushLookaheadMs = clamped;
        SaveSettings();
    }

    public void SetJudgmentTiming(JudgmentTiming timing)
    {
        if (gameSettings == null || gameSettings.judgmentTiming == timing) return;
        gameSettings.judgmentTiming = timing;
        SaveSettings();
    }

    public float BrushLookaheadMs => gameSettings != null
        ? Mathf.Clamp(gameSettings.brushLookaheadMs, 0f, 40f)
        : 0f;

    public int BrushLookaheadMaxVelocity => gameSettings != null
        ? Mathf.Clamp(gameSettings.brushLookaheadMaxVelocity, 0, 127)
        : 0;

    public int BrushSemitones => gameSettings != null
        ? Mathf.Clamp(gameSettings.brushSemitones, 1, 4) : 2;

    public void SetMinNoteOnVelocity(float value)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.minNoteOnVelocity = Mathf.Clamp(Mathf.RoundToInt(value), 0, 60);
    }

    public void SetBrushRejection(bool enabled)
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.brushRejection = enabled;
    }

    public void SetPianoSoundMode(PianoSoundMode mode)
    {
        if (gameSettings == null || gameSettings.pianoSoundMode == mode) return;
        gameSettings.pianoSoundMode = mode;
        SaveSettings();
    }

    public PianoPedalSource CurrentPianoPedalSource => gameSettings != null
        ? gameSettings.pianoPedalSource
        : PianoPedalSource.Chart;

    public void SetPianoPedalSource(PianoPedalSource source)
    {
        if (gameSettings == null || gameSettings.pianoPedalSource == source) return;
        gameSettings.pianoPedalSource = source;
        SaveSettings();
    }

    public PianoAutoPedal CurrentPianoAutoPedal => gameSettings != null
        ? gameSettings.pianoAutoPedal
        : PianoAutoPedal.Off;

    public void SetPianoAutoPedal(PianoAutoPedal mode)
    {
        if (gameSettings == null || gameSettings.pianoAutoPedal == mode) return;
        gameSettings.pianoAutoPedal = mode;
        SaveSettings();
    }

    public void SetVisualLayoutMode(bool piano88Enabled)
    {
        SetVisualLayoutMode(piano88Enabled ? VisualLayoutMode.Piano88 : VisualLayoutMode.General);
    }

    public void SetJudgmentOffsetMs(float ms)
    {
        gameSettings.judgmentOffsetMs = ms;
    }

    public void SetMusicPlaybackOffsetMs(float ms)
    {
        // allow a broad range but clamp to reasonable values (-3000ms .. 3000ms)
        gameSettings.musicPlaybackOffsetMs = Mathf.Clamp(ms, -3000f, 3000f);
    }

    public void SetEnablePianoPreview(bool enabled)
    {
        if (gameSettings.enablePianoPreview == enabled) return;

        gameSettings.enablePianoPreview = enabled;
        Debug.Log($"SettingsManager: Piano preview toggled {(enabled ? "ON" : "OFF")}");

        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.OnPianoPreviewToggleChanged(enabled);
        }

        // Refresh song selection preview audio to reflect the new layered state immediately.
        var songSelection = SongSelectionManager.Instance;
        if (songSelection != null)
        {
            Debug.Log("SettingsManager: Notifying SongSelectionManager to refresh preview audio.");
            songSelection.RefreshPreviewAudio(true);
        }
        else
        {
            Debug.LogWarning("SettingsManager: SongSelectionManager instance not available when toggling piano preview.");
        }
    }

    public void SetDebugMode(bool enabled)
    {
        // Debug mode and judgment mode are mutually exclusive: enable debug -> disable judgment, and vice versa
        gameSettings.debugMode = enabled;
        gameSettings.judgmentMode = !enabled;
        EnsureJudgmentFlagsConsistency();
    }

    public bool DebugMode => gameSettings != null ? gameSettings.debugMode : false;

    /// <summary>
    /// 遊玩中實際生效的自動演奏。教學曲一律不用玩家的自動演奏設定：示範段由
    /// <see cref="TutorialSession"/> 逐顆決定自動彈，遊玩段一定要玩家自己打——
    /// 否則開著自動演奏進教學，「換你」那一段也被自動打掉，玩家沒有音符可以按。
    /// 設定畫面要讀 <see cref="DebugMode"/>，不然會顯示成被關掉。
    /// </summary>
    public bool DebugModeInPlay => DebugMode && !TutorialSession.IsActive;

    /// <summary>遊玩中實際生效的判定模式；教學曲一律要判定（見 <see cref="DebugModeInPlay"/>）。</summary>
    public bool JudgmentModeInPlay => JudgmentMode || TutorialSession.IsActive;

    // Allow other systems to explicitly set judgment mode; keep debug mode as inverse
    public void SetJudgmentMode(bool enabled)
    {
        gameSettings.judgmentMode = enabled;
        gameSettings.debugMode = !enabled;
        EnsureJudgmentFlagsConsistency();
    }

    // Exposed timing/protection getters for other systems
    public int PerfectMs => gameSettings != null ? gameSettings.perfectMs : 50;
    public int GreatMs => gameSettings != null ? gameSettings.greatMs : 100;
    public int GoodMs => gameSettings != null ? gameSettings.goodMs : 150;

    public int StacPerfectMs => gameSettings != null ? gameSettings.stacPerfectMs : 70;
    public int StacGreatMs => gameSettings != null ? gameSettings.stacGreatMs : 130;
    public int StacGoodMs => gameSettings != null ? gameSettings.stacGoodMs : 200;

    public int StacAutoReleaseMs => gameSettings != null ? gameSettings.stacAutoReleaseMs : 60;
    public bool AutoSimulateStaccatoRelease => gameSettings != null ? gameSettings.autoSimulateStaccatoRelease : true;

    public int SwapGraceMs => gameSettings != null ? gameSettings.swapGraceMs : 80;

    public bool ProtectionEnabled => gameSettings == null || gameSettings.protectionEnabled;
    public bool EnableSpatialStop => gameSettings != null ? gameSettings.enableSpatialStop : true;
    public int SpatialStopRange => gameSettings != null ? gameSettings.spatialStopRange : 3;
    public int SpatialStopWindowMs => gameSettings != null ? gameSettings.spatialStopWindowMs : 20;

    /// <summary>
    /// Sets how long a judged note keeps protecting its neighbouring lanes.
    /// </summary>
    /// <remarks>
    /// This is the lever for chord spread. A note spans two or three lanes, so a
    /// chord played on a MIDI keyboard arrives as several presses — and fingers
    /// do not land together, typically spreading 30-50 ms. Once the window
    /// expires the leftover presses find their own note already consumed, and
    /// the matcher hands them the *next* note up to 150 ms away, scored as FAST.
    /// Measured on a real session that produced a flat early tail holding 37-46%
    /// of all judgments, which no timing offset can correct because those hits
    /// were never related to the note they were credited to.
    /// </remarks>
    public void SetSpatialStopWindowMs(int milliseconds)
    {
        if (gameSettings == null) return;
        int clamped = Mathf.Clamp(milliseconds, 0, 150);
        if (gameSettings.spatialStopWindowMs == clamped) return;
        gameSettings.spatialStopWindowMs = clamped;
        SaveSettings();
        ApplySettings();
    }

    public float FutureNotePenalty => gameSettings != null
        ? Mathf.Clamp(gameSettings.futureNotePenalty, 1f, 3f)
        : 1f;

    public void SetFutureNotePenalty(float penalty)
    {
        if (gameSettings == null) return;
        float clamped = Mathf.Clamp(penalty, 1f, 3f);
        if (Mathf.Approximately(gameSettings.futureNotePenalty, clamped)) return;
        gameSettings.futureNotePenalty = clamped;
        SaveSettings();
        ApplySettings();
    }

    public int EarlyClaimLimitMs => gameSettings != null
        ? Mathf.Clamp(gameSettings.earlyClaimLimitMs, 0, 150)
        : 0;

    public void SetEarlyClaimLimitMs(int milliseconds)
    {
        if (gameSettings == null) return;
        int clamped = Mathf.Clamp(milliseconds, 0, 150);
        if (gameSettings.earlyClaimLimitMs == clamped) return;
        gameSettings.earlyClaimLimitMs = clamped;
        SaveSettings();
        ApplySettings();
    }

    public bool RetimeBetterPress => gameSettings != null && gameSettings.retimeBetterPress;

    public bool BlockKeyChatter => gameSettings == null || gameSettings.blockKeyChatter;

    public bool ShowPedalCues => gameSettings == null || gameSettings.showPedalCues;

    public void SetShowPedalCues(bool enabled)
    {
        if (gameSettings == null) return;
        if (gameSettings.showPedalCues == enabled) return;
        gameSettings.showPedalCues = enabled;
        SaveSettings();
        ApplySettings();
    }

    /// <summary>演奏會模式裡標在音符正中央的那顆寶石。</summary>
    /// <remarks>
    /// 它是提示不是規則：關掉它，演奏會模式照樣只在打中正中央那一格時給滿分，
    /// 變的只是這件事有沒有被畫出來。想把找中心當成一部分技術的人可以關掉，計
    /// 分不受影響 —— 所以它屬於畫面設定，不是遊戲設定。
    /// </remarks>
    public bool ShowCentreCue => gameSettings == null || gameSettings.showCentreCue;

    /// <summary>
    /// 觸鍵對齊：玩家的力度被換算到譜面的尺上多少。
    /// </summary>
    /// <remarks>
    /// 0 = 照原樣。手輕就是小聲，手重就是大聲，最接近真實的樂器。代價是手輕的人
    /// 整場都被伴奏蓋過去，而且強弱評分永遠落在「中」那一段。
    ///
    /// 1 = 完全換算。每一下都對照玩家自己的平均來讀，所以輕手的人也有飽滿的音色，
    /// 而「比自己平常重」仍然更響。
    ///
    /// 中間是連續的，讓玩家自己決定要多少真實、多少可用。
    /// </remarks>
    public float TouchAlignment => gameSettings != null
        ? Mathf.Clamp01(gameSettings.touchAlignment)
        : 1f;

    public void SetTouchAlignment(float value)
    {
        if (gameSettings == null) return;
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(gameSettings.touchAlignment, clamped)) return;
        gameSettings.touchAlignment = clamped;
        SaveSettings();
    }

    /// <summary>按下去的那一鍵，判定線上那條淡綠提示有多長。0 = 不顯示。</summary>
    public float StrikeCueHeight => gameSettings != null
        ? Mathf.Clamp(gameSettings.strikeCueHeight, 0f, 3f)
        : 1f;

    public void SetStrikeCueHeight(float value)
    {
        if (gameSettings == null) return;
        float clamped = Mathf.Clamp(value, 0f, 3f);
        if (Mathf.Approximately(gameSettings.strikeCueHeight, clamped)) return;
        gameSettings.strikeCueHeight = clamped;
        SaveSettings();
    }

    /// <summary>目前生效的判定預設。</summary>
    public JudgmentPreset CurrentJudgmentPreset => gameSettings == null
        ? JudgmentPreset.Custom
        : (JudgmentPreset)Mathf.Clamp(gameSettings.judgmentPreset, 0, 2);

    /// <summary>
    /// 換一個判定預設，並且把底下那一整組參數一次寫好。
    /// </summary>
    /// <remarks>
    /// **為什麼要有預設。** 判定底下有十來個互相牽連的旋鈕（輸入幀、時間量化、
    /// 擦碰過濾、抖動保護、搶音上限、補正…）。它們單獨看每一個都講得通，湊在一起
    /// 卻可能互相抵銷 —— 例如輸入幀打開的時候，擦碰前瞻擋的是同一批東西，兩個
    /// 一起開只是疊加延遲。要玩家自己找出一組能用的組合，是把設計的工作丟給他。
    ///
    /// 所以給兩組**已經配好**的：一組為了反應快，一組為了貼近原作。剩下的人才
    /// 進自訂。
    ///
    /// <see cref="JudgmentPreset.Custom"/> 不寫任何值 —— 它的意思正是「這一組
    /// 由玩家自己決定」，寫進去就把他調過的東西洗掉了。
    /// </remarks>
    public void SetJudgmentPreset(JudgmentPreset preset)
    {
        if (gameSettings == null) return;
        gameSettings.judgmentPreset = (int)preset;

        switch (preset)
        {
            case JudgmentPreset.Responsive:
                // 反應最快的一組：每一顆鍵用自己的硬體時戳、按下就判定，不做幀量化，
                // 也不為了等更響的鄰鍵而扣住輸入。
                //
                // 防誤觸和類原型是同一套本家規則（鄰鍵鎖、判掉的音符吃掉周圍的多餘
                // 按鍵、可疑的判定先暫定），觀察窗用 50ms（本家 3 幀）。暫定期間琴聲
                // 照樣立刻響 —— 擦碰搶音符的判定會被作廢，但那一聲收不回來，那是拿它
                // 去換「按下去就有反應」。
                gameSettings.inputFrameMode = InputFrameMode.Off;
                gameSettings.judgmentTiming = JudgmentTiming.Precise;
                gameSettings.brushLookaheadMs = 0f;
                gameSettings.retimeBetterPress = true;
                gameSettings.earlyClaimLimitMs = 0;
                gameSettings.blockKeyChatter = true;
                gameSettings.brushRejection = true;
                break;

            case JudgmentPreset.Classic:
                // 原作的輸入處理（反編譯 nostalgia.dll 的結果，見
                // PAN-001-2024102200_extracted/README.md §4、§5），模擬但不降到 60fps：
                // 按下就判定；判中後左右 4 格鎖 3 幀、只擋晚 4 幀以上的音符；判掉的音符
                // 吃掉一幀內周圍的多餘按鍵；可能是擦碰搶音符的判定暫定一幀、琴聲也扣
                // 一幀，被正主鎖到就作廢；提早按下的判定和琴聲都到線才定案，期間更準的一下可以
                // 覆蓋；和弦一幀內共用最早那一下的時間。判定窗和各種音符的判法維持這個
                // 專案自己的。inputFrameMode 仍寫 Full，它現在代表這一套模擬。
                //
                // 所以事後改判要關掉 —— 那是這個專案為了救誤判加的東西，原作沒
                // 有，而且它和「一顆音符只判一次」直接牴觸。
                gameSettings.inputFrameMode = InputFrameMode.Full;
                gameSettings.judgmentTiming = JudgmentTiming.FrameQuantized;
                gameSettings.brushLookaheadMs = 0f;
                gameSettings.retimeBetterPress = false;
                gameSettings.earlyClaimLimitMs = 0;
                gameSettings.blockKeyChatter = true;
                // 本家沒有「依力度猜擦碰」這種輸入端過濾，擦碰全靠判定規則吃掉。實測它
                // 擋掉的按鍵裡約 3% 之後讓音符 Miss，而判定端的規則本來就會處理擦碰。
                gameSettings.brushRejection = false;
                break;

            case JudgmentPreset.Custom:
            default:
                break;
        }

        EnsureJudgmentFlagsConsistency();
        SaveSettings();
        ApplySettings();
    }

    /// <summary>
    /// 一鍵套用本家：判定切到類原型，並把能對應到本家的設定都改成本家的樣子。
    /// </summary>
    /// <remarks>
    /// 依據是反編譯 nostalgia.dll 的結果（PAN-001-2024102200_extracted/README.md）。
    ///
    /// * 判定模式：類原型（本家的輸入規則、鄰鍵鎖、提早預訂、到線才發聲）。
    /// * 判定窗：SuperJust／Just／Good 換成本家的實際寬度 42／75／108ms。本家 Good
    ///   早晚不對稱（早 125、晚 108），這裡只有一個值，取決定 Miss 的晚側。
    /// * 共享輸入：本家一鍵一幀只判一顆，沒有「兩顆重疊音符共用一下」，設為 0。
    /// * 輸入端的力度地板、擦碰過濾、前瞻扣留：本家都沒有，全部關掉。
    ///
    /// **刻意不動的**：畫面、音量、鋼琴音色，以及判定補償與音樂補償——那兩個是
    /// 玩家針對自己的設備量出來的延遲，本家也有對應的調整，不是規則的一部分。
    /// </remarks>
    public void ApplyArcadeSettings()
    {
        if (gameSettings == null) gameSettings = new GameSettings();
        gameSettings.perfectMs = 42;
        gameSettings.greatMs = 75;
        gameSettings.goodMs = 108;
        gameSettings.sharedInputThresholdMs = 0;
        gameSettings.minNoteOnVelocity = 0;
        gameSettings.brushLookaheadMs = 0f;
        // 最後才切預設：它會存檔並把整組設定推到執行中的判定端。
        SetJudgmentPreset(JudgmentPreset.Classic);
    }

    /// <summary>把每首曲子的播放響度拉到同一個高度。</summary>
    /// <remarks>
    /// 這個曲庫的主音軌之間差 22 dB，所以預設是開的。關掉之後每首歌照它自己被
    /// 混音的音量播——想聽原始動態、或是自己已經把檔案處理過的人會要這個。
    /// </remarks>
    public bool NormalizeMusicLoudness =>
        gameSettings == null || gameSettings.normalizeMusicLoudness;

    public void SetNormalizeMusicLoudness(bool enabled)
    {
        if (gameSettings == null) return;
        if (gameSettings.normalizeMusicLoudness == enabled) return;
        gameSettings.normalizeMusicLoudness = enabled;
        SaveSettings();
        ApplySettings();
    }

    public void SetShowCentreCue(bool enabled)
    {
        if (gameSettings == null) return;
        if (gameSettings.showCentreCue == enabled) return;
        gameSettings.showCentreCue = enabled;
        SaveSettings();
        ApplySettings();
    }

    public void SetBlockKeyChatter(bool enabled)
    {
        if (gameSettings == null) return;
        if (gameSettings.blockKeyChatter == enabled) return;
        gameSettings.blockKeyChatter = enabled;
        SaveSettings();
        ApplySettings();
    }

    public void SetRetimeBetterPress(bool enabled)
    {
        if (gameSettings == null) return;
        if (gameSettings.retimeBetterPress == enabled) return;
        gameSettings.retimeBetterPress = enabled;
        SaveSettings();
        ApplySettings();
    }
    public int SharedInputThresholdMs => gameSettings != null
        ? Mathf.Clamp(gameSettings.sharedInputThresholdMs, 0, 20)
        : 20;
}
