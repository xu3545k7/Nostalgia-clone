using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 從正上方看譜面的檢視器：鍵道橫著排、時間往上走，跟著音樂捲動。
/// </summary>
/// <remarks>
/// **為什麼不是把攝影機轉成俯視。** 場上的音符只活在判定線前後約兩秒（NoteSpawner
/// 的 travelTime×speed），而且判定線自己每幀會被 JudgmentLineBar 依攝影機角度重算
/// —— 把主攝影機轉到 90° 會讓判定線的 Z 跑掉，所有未判定的音符跟著追。所以這個檢
/// 視器不碰攝影機，直接從 <see cref="Chart"/> 畫：想看幾小節就畫幾小節，也不受物件
/// 池 512 顆的上限。
///
/// **它回答三個問題**：音符排在哪些鍵道、哪裡太擠（看排列）；整首的時間結構（小節
/// 線、拍線、細分線）；還有右邊每一條格線是幾分音符、每顆音符離下一顆同手音符是幾
/// 分 —— 後兩項是「這段到底要多快」的答案，光看排列是看不出來的。
/// </remarks>
[DisallowMultipleComponent]
public sealed class ChartOverviewViewer : MonoBehaviour
{
    // HUD 的 classicalHudCanvas 是 24000、選難度的書是 26000 —— 同一個數字的話
    // 誰蓋誰要看建立順序，實測是書蓋在檢視器前面。所以這裡明確地再高一階。
    private const int SortingOrder = 26500;
    private const float PlayheadViewport = 0.18f;   // 播放頭離畫面底的比例
    private const float DefaultPxPerMs = 0.32f;     // 1 秒 = 320 px（以 1080 為基準）
    private const float MinPxPerMs = 0.06f;
    private const float MaxPxPerMs = 1.60f;
    private const float LeftRail = 96f;             // 左邊：小節號
    private const float RightRail = 104f;           // 右邊：格線是幾分
    private const float TopBar = 74f;               // 上面：BPM／最密／音符數
    private const int MaxQuads = 6000;
    private const int MaxLabels = 320;

    private static readonly Color Backdrop = new Color(0.035f, 0.030f, 0.045f, 1f);
    private static readonly Color LaneLine = new Color(1f, 1f, 1f, 0.028f);
    private static readonly Color OctaveLine = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color BarLine = new Color(0.86f, 0.80f, 0.62f, 0.42f);
    private static readonly Color BeatLine = new Color(0.80f, 0.82f, 0.90f, 0.22f);
    private static readonly Color SubLine = new Color(0.72f, 0.76f, 0.88f, 0.10f);
    private static readonly Color RightHand = new Color(0.95f, 0.48f, 0.46f, 0.95f);
    private static readonly Color LeftHand = new Color(0.46f, 0.68f, 0.98f, 0.95f);
    private static readonly Color HoldTint = new Color(1f, 1f, 1f, 0.42f);
    private static readonly Color Playhead = new Color(1f, 0.80f, 0.36f, 0.95f);
    private static readonly Color RailInk = new Color(0.74f, 0.70f, 0.60f, 0.85f);
    private static readonly Color GapInk = new Color(0.92f, 0.88f, 0.74f, 0.72f);

    public static ChartOverviewViewer Instance { get; private set; }

    private static Sprite whitePixel;

    /// <summary>一格白點。沒有 sprite 的 Image 在這個專案裡畫不出來。</summary>
    /// <remarks>
    /// 實測：同一個 canvas 上，有 sprite 的按鈕底板畫得出來，sprite 給 null 的底色
    /// 完全不見 —— 文字（TMP 走自己的材質）照常顯示，所以看起來像「只有黑底和按鈕」。
    /// 與其猜是哪一層把無貼圖的 UI 材質吃掉，不如一律給貼圖。
    /// </remarks>
    private static Sprite WhitePixel()
    {
        if (whitePixel == null)
        {
            Texture2D texture = Texture2D.whiteTexture;
            whitePixel = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            whitePixel.name = "ChartOverviewWhite";
        }
        return whitePixel;
    }

    /// <summary>沒有的話就建一個。它自己一顆根物件，不掛在 gameplay root 底下。</summary>
    /// <remarks>
    /// 刻意不放進 gameplay root：那顆在選曲時是關著的，find-or-create 在那個時機會
    /// 生出一個永遠看不到的替身（同一個坑在別的管理器上踩過）。
    /// </remarks>
    public static ChartOverviewViewer EnsureCreated()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<ChartOverviewViewer>(FindObjectsInactive.Include);
        if (existing != null) return existing;
        var go = new GameObject("ChartOverviewViewer");
        return go.AddComponent<ChartOverviewViewer>();
    }

    private struct ViewNote
    {
        public float start;          // ms
        public float end;            // ms（非長條時等於 start）
        public float centre;         // 鍵道中心，0..1
        public float halfWidth;      // 佔整條軌道的比例
        public int hand;             // 0=右 1=左
        public bool hold;
        public string gap;           // 離下一顆同手音符是幾分
    }

    private struct GridLine
    {
        public float ms;
        public int level;            // 0=小節 1=四分 2=細分
        public int bar;              // level 0 時的小節號，其餘 -1
    }

    private struct Quad
    {
        public Rect rect;
        public Color colour;
    }

    private Canvas canvas;
    private RectTransform viewerRoot;       // 整個檢視器，關著的時候整棵收起來
    private RectTransform followButton;     // 自己捲過之後才出現
    private TextMeshProUGUI playLabel;      // 播放／暫停
    private RectTransform content;          // 畫格線和音符的區域
    private ChartOverviewGraphic graphic;
    private RawImage stageView;             // 舞台拍出來的畫面
    private ChartOverviewStage stage;
    private TextMeshProUGUI header;
    private TextMeshProUGUI hint;
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>();
    private readonly List<Quad> quads = new List<Quad>();
    private int labelsUsed;

    private Chart preparedChart;
    private Chart stagedChart;              // 舞台上排的是哪一份
    private readonly List<ViewNote> notes = new List<ViewNote>();
    private readonly List<GridLine> grid = new List<GridLine>();
    private float chartBpm = 120f;
    private int chartNumerator = 4;
    private int densestSubdivision = 16;   // 整首最密的音符值（只用來報告）
    private int gridSubdivision = 16;      // 格線畫到幾分（取穩健值）
    private int slotCount = PianoVisualLayout.LegacyLaneCount;
    private float pxPerMs = DefaultPxPerMs;
    private bool open;
    private bool following = true;          // 跟著音樂跑，還是使用者自己捲
    private bool playing;                   // 選曲畫面的自走播放
    private string audioStatus;             // 音檔還沒好／沒有音檔時顯示在標題列
    private float silenceTimer;
    private float viewMs;                   // 播放頭上的時刻

    // 選曲畫面開的時候，譜面是自己讀進來的（還沒開始遊戲，GameManager 手上沒有）。
    private Chart loadedChart;
    private AudioClip songClip;             // 按播放時要放的音樂
    private AudioSource audioSource;
    private readonly List<AudioSource> silenced = new List<AudioSource>();
    private string loadedChartFile;
    private string loadedTitle;
    private bool loading;
    /// <summary>畫不出來的原因。一片黑沒辦法除錯，所以任何失敗都寫成一句話。</summary>
    private string status;

    public bool IsOpen => open;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Build();
        SetOpen(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Toggle() => SetOpen(!open);

    public void SetOpen(bool value)
    {
        open = value;
        if (viewerRoot != null) viewerRoot.gameObject.SetActive(value);
        // 開著的時候選曲畫面的試聽要閉嘴：看譜面的時候聽到的應該是這份譜面的音
        // 樂（按播放才放），不是背景那段一直循環的試聽片段。
        SilenceOtherAudio(value);
        if (value)
        {
            ResumeFollow();
            Refresh();
        }
        else
        {
            // 關掉就把自己讀進來的那份丟掉：下次在遊戲中按 F2 要看的是正在打的譜，
            // 不是上次在選曲畫面點開的那一份。
            loadedChart = null;
            loadedChartFile = null;
            loadedTitle = null;
            preparedChart = null;
            stagedChart = null;
            songClip = null;
            if (audioSource != null) audioSource.Stop();
            if (stage != null)
            {
                Destroy(stage.gameObject);
                stage = null;
            }
        }
    }

    /// <summary>
    /// 從選曲畫面打開某一個難度的譜面（還沒開始遊戲）。
    /// </summary>
    /// <remarks>
    /// 自己把 JSON 讀進來解析，不走 GameManager.LoadChartData —— 那一條會順便設
    /// CurrentChart、改 Conductor 的 BPM、餵踏板給取樣器，等於「開始載入這首歌」。
    /// 只是想看一眼譜面不該有那些副作用。解析丟到工作緒（Newtonsoft 是執行緒安全
    /// 的，JsonUtility 不是），大譜面才不會讓選曲畫面卡一下。
    /// </remarks>
    public void OpenForChart(string chartFileName, string title, AudioClip clip, string audioPath)
    {
        songClip = clip;
        audioStatus = null;
        // 匯入的曲目在選曲畫面拿到的 audioClip 通常是 null —— 那些是 external 路徑，
        // 遊戲本身也是等到要播的時候才非同步載。檢視器自己載一份。
        if (songClip == null && !string.IsNullOrWhiteSpace(audioPath))
        {
            audioStatus = "音檔載入中";
            StartCoroutine(LoadAudioRoutine(audioPath));
        }
        else if (songClip == null)
        {
            audioStatus = "沒有音檔";
        }
        loadedChart = null;
        preparedChart = null;
        status = null;
        loadedChartFile = chartFileName;
        loadedTitle = title;
        SetOpen(true);
        // SetOpen 會把「跟著音樂跑」打開，所以這兩行要在它之後：從選曲畫面看譜是
        // 從頭開始、自己捲的。
        viewMs = 0f;
        following = false;
        if (followButton != null) followButton.gameObject.SetActive(false);
        if (!string.IsNullOrEmpty(chartFileName)) StartCoroutine(LoadChartRoutine(chartFileName));
    }

    private IEnumerator LoadChartRoutine(string chartFile)
    {
        loading = true;
        status = "讀取檔案…";
        string json = null;
        try
        {
            TextAsset asset = GameManager.LoadChartJsonAsset(chartFile);
            if (asset != null) json = asset.text;
        }
        catch (System.Exception error)
        {
            status = "讀檔失敗：" + error.Message;
        }

        if (string.IsNullOrEmpty(json))
        {
            if (status == "讀取檔案…") status = "找不到譜面檔：" + chartFile;
            loading = false;
            yield break;
        }
        status = "解析中…";

        Task<Chart> task = null;
        try { task = Task.Run(() => Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(json)); }
        catch { }

        if (task != null)
        {
            while (!task.IsCompleted) yield return null;
            try { if (task.Status == TaskStatus.RanToCompletion) loadedChart = task.Result; }
            catch { }
        }

        if (loadedChart == null || loadedChart.notes == null)
        {
            // Newtonsoft 沒吃下去就退回 JsonUtility（主緒），總比什麼都不顯示好。
            try { loadedChart = JsonUtility.FromJson<Chart>(json); }
            catch (System.Exception error) { status = "解析失敗：" + error.Message; }
        }
        try { loadedChart?.NormaliseHiddenNotes(); } catch { }
        if (loadedChart == null) status = "解析不出譜面：" + chartFile;
        else if (loadedChart.notes == null || loadedChart.notes.Count == 0)
            status = "這份譜面沒有音符：" + chartFile;
        else status = null;
        loading = false;
    }

    /// <summary>回到跟著音樂跑。</summary>
    public void ResumeFollow()
    {
        following = true;
        playing = false;
        if (audioSource != null) audioSource.Pause();
        SettingsUiKit.SetText(playLabel, "播放");
        if (followButton != null) followButton.gameObject.SetActive(false);
    }

    private void BeginManualScroll()
    {
        // 手指一碰就停下自走播放：要拖著看的時候，畫面不該還在自己跑。
        playing = false;
        if (audioSource != null) audioSource.Pause();
        SettingsUiKit.SetText(playLabel, "播放");
        following = false;
        if (followButton != null) followButton.gameObject.SetActive(true);
    }

    /// <summary>手指拉多少，譜面跟著走多少（內容跟著手指，不是相反）。</summary>
    private void ScrollBy(float screenDeltaY)
    {
        float scale = canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;
        viewMs -= (screenDeltaY / scale) / Mathf.Max(0.0001f, pxPerMs);
        viewMs = Mathf.Max(-2000f, viewMs);
    }

    private void Update()
    {
        if (!open) return;
        HandleZoomKeys();
        // 選曲畫面在捲動或換歌時會重新開始播放，所以要持續蓋。
        silenceTimer -= Time.unscaledDeltaTime;
        if (silenceTimer <= 0f)
        {
            silenceTimer = 0.25f;
            SilenceOtherAudio(true);
        }

        if (playing && !following)
        {
            if (audioSource != null && audioSource.isPlaying)
            {
                viewMs = audioSource.time * 1000f;
            }
            else
            {
                // 沒有音樂（或放完了）就自己走。用 unscaled：暫停選單把 timeScale
                // 歸零的時候，檢視器還是要能看。
                viewMs += Time.unscaledDeltaTime * 1000f;
            }
        }
        Refresh();
    }

    /// <summary>
    /// 自走播放。有音樂就放音樂、並且用音樂的時間當時鐘；沒有就自己往前推。
    /// </summary>
    /// <remarks>
    /// 用音訊的播放位置當時鐘，而不是自己累加 deltaTime：只要兩者的速率差一點點，
    /// 看久了譜面就會和聽到的音樂錯開，而這個檢視器的用途正是「對照著聽」。
    /// </remarks>
    private void TogglePlay()
    {
        playing = !playing;
        if (playing)
        {
            following = false;
            if (followButton != null) followButton.gameObject.SetActive(true);
            StartAudio();
        }
        else if (audioSource != null)
        {
            audioSource.Pause();
        }
        SettingsUiKit.SetText(playLabel, playing ? "暫停" : "播放");
    }

    /// <summary>把音檔載進來。external:// 的走 UnityWebRequest，內建的走 Resources。</summary>
    /// <remarks>
    /// 和 SongSelectionManager.PreloadClipAsync 同一套做法，包括那個 `+` 要編成
    /// %2B 的坑：不編的話下載端會把它解成空格，檔名裡有加號的曲子就載不到。
    /// </remarks>
    private IEnumerator LoadAudioRoutine(string audioPath)
    {
        string external = null;
        try { external = ExternalSongLibrary.ToLocalPath(audioPath); } catch { }

        if (!string.IsNullOrEmpty(external))
        {
            AudioType type = AudioType.UNKNOWN;
            string extension = Path.GetExtension(external).ToLowerInvariant();
            if (extension == ".ogg") type = AudioType.OGGVORBIS;
            else if (extension == ".wav") type = AudioType.WAV;
            else if (extension == ".mp3") type = AudioType.MPEG;

            using (var request = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(
                       new Uri(external).AbsoluteUri.Replace("+", "%2B"), type))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    songClip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(request);
                    if (songClip != null) songClip.name = Path.GetFileNameWithoutExtension(external);
                }
                else
                {
                    audioStatus = "音檔載入失敗";
                    yield break;
                }
            }
        }
        else
        {
            var load = Resources.LoadAsync<AudioClip>(audioPath);
            yield return load;
            songClip = load.asset as AudioClip;
        }

        audioStatus = songClip != null ? null : "沒有音檔";
        // 載入的時候已經按了播放，就立刻接上去。
        if (playing && songClip != null) StartAudio();
    }

    private void StartAudio()
    {
        if (songClip == null) return;
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }
        audioSource.clip = songClip;
        audioSource.volume = SettingsManager.Instance != null
            ? Mathf.Clamp01(SettingsManager.Instance.MusicVolume) : 0.8f;
        audioSource.time = Mathf.Clamp(viewMs / 1000f, 0f, Mathf.Max(0f, songClip.length - 0.05f));
        audioSource.Play();
    }

    /// <summary>檢視器開著的時候，場上其他聲音一律閉嘴；關掉再照名單還原。</summary>
    /// <remarks>
    /// 只靜音 SongSelectionManager 身上那兩顆不夠：選曲畫面的音樂不只從那裡出來
    /// （選單 BGM 和試聽是分開的）。與其一條一條追是誰在響，不如把「現在正在響的」
    /// 記下來靜音、關的時候照名單還原 —— 名單只記**我們動過的**，本來就靜音的不會
    /// 被我們弄成有聲。
    ///
    /// 而且要反覆套用：選曲畫面在捲動、換歌時會重新開始試聽，那是新的播放，不再蓋
    /// 一次就又會出聲。
    /// </remarks>
    private void SilenceOtherAudio(bool silence)
    {
        if (!silence)
        {
            for (int i = 0; i < silenced.Count; i++)
            {
                if (silenced[i] != null) silenced[i].mute = false;
            }
            silenced.Clear();
            return;
        }

        var sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null || source == audioSource) continue;
            if (source.mute || !source.isPlaying) continue;
            source.mute = true;
            if (!silenced.Contains(source)) silenced.Add(source);
        }
    }

    /// <summary>縮放用 - 和 =：兩個都不在鍵道表裡（鍵道是 A–Z 加 9、0）。</summary>
    private void HandleZoomKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb[Key.Equals].wasPressedThisFrame || kb[Key.NumpadPlus].wasPressedThisFrame)
            pxPerMs = Mathf.Min(MaxPxPerMs, pxPerMs * 1.25f);
        if (kb[Key.Minus].wasPressedThisFrame || kb[Key.NumpadMinus].wasPressedThisFrame)
            pxPerMs = Mathf.Max(MinPxPerMs, pxPerMs * 0.8f);
    }

    // ------------------------------------------------------------------
    // 介面
    // ------------------------------------------------------------------

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        BuildViewer();
    }

    private void BuildViewer()
    {
        viewerRoot = SettingsUiKit.CreateRect("Viewer", transform);
        SettingsUiKit.Stretch(viewerRoot);

        RectTransform backdrop = SettingsUiKit.CreateRect("Backdrop", viewerRoot);
        SettingsUiKit.Stretch(backdrop);
        SettingsUiKit.AddImage(backdrop.gameObject, WhitePixel(), Backdrop, false);

        // 拖曳面：整個譜面區域都能用手指拉著捲。它是這一層唯一吃射線的東西，而且排
        // 在最底下，所以上面的控制鈕仍然按得到。
        RectTransform surface = SettingsUiKit.CreateRect("DragSurface", viewerRoot);
        SettingsUiKit.Stretch(surface, LeftRail, 24f, RightRail, TopBar);
        SettingsUiKit.AddImage(surface.gameObject, WhitePixel(), new Color(0f, 0f, 0f, 0f), true);
        surface.gameObject.AddComponent<DragSurface>().Bind(this);

        // 舞台的畫面墊在最底下（格線、標註都畫在它上面）。它和 content 同一塊範圍，
        // 所以世界座標和 UI 座標是一對一的。
        RectTransform stageRect = SettingsUiKit.CreateRect("Stage", viewerRoot);
        SettingsUiKit.Stretch(stageRect, LeftRail, 24f, RightRail, TopBar);
        stageView = stageRect.gameObject.AddComponent<RawImage>();
        stageView.raycastTarget = false;
        stageView.color = Color.white;

        content = SettingsUiKit.CreateRect("Content", viewerRoot);
        SettingsUiKit.Stretch(content, LeftRail, 24f, RightRail, TopBar);
        // CanvasRenderer 要自己加。
        //
        // Graphic 身上有 [RequireComponent(typeof(CanvasRenderer))]，但那是靠
        // MonoScript 的中繼資料生效的，而 ChartOverviewGraphic 是巢狀私有類別 ——
        // AddComponent 會成功，CanvasRenderer 卻不會被一起加上。結果是這個 Graphic
        // 每幀乖乖產生頂點、一個三角形也畫不出來，而且完全不報錯（直到有人去讀
        // canvasRenderer 才會炸）。格線、音符、底色一起消失就是這樣來的。
        content.gameObject.AddComponent<CanvasRenderer>();
        graphic = content.gameObject.AddComponent<ChartOverviewGraphic>();
        graphic.raycastTarget = false;

        header = SettingsUiKit.CreateLabel(viewerRoot, "Header", 30f, SettingsUiKit.TextColour,
            TextAlignmentOptions.MidlineLeft);
        // overflowMode 一定要改成 Overflow。
        //
        // CreateLabel 給的是 Ellipsis，而 TMP 在「框高 < 字級 × 1.5」時不是截字，
        // 是**整段不畫** —— 上一版這裡是 30pt 放在 40 高的框裡，所以標題、右邊的
        // 格線標註、音符旁的標註全部是隱形的，畫面上只剩底色。
        header.overflowMode = TextOverflowModes.Overflow;
        SettingsUiKit.Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(36f, -16f), new Vector2(1180f, 52f));

        hint = SettingsUiKit.CreateLabel(viewerRoot, "Hint", 20f, SettingsUiKit.MutedText,
            TextAlignmentOptions.MidlineRight);
        hint.overflowMode = TextOverflowModes.Overflow;
        SettingsUiKit.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-32f, 22f), new Vector2(1500f, 40f));
        SettingsUiKit.SetText(hint, "滾輪或拖曳捲動　＋／－ 縮放　F2 開關");

        // 控制鈕靠右上直排，一律 76 高 —— 一根手指的大小。
        CreateControl("Close", SettingsGlyph.Shape.Back, "關閉", -32f, () => SetOpen(false));
        CreateControl("ZoomIn", SettingsGlyph.Shape.Plus, null, -194f,
            () => pxPerMs = Mathf.Min(MaxPxPerMs, pxPerMs * 1.25f));
        CreateControl("ZoomOut", SettingsGlyph.Shape.Minus, null, -290f,
            () => pxPerMs = Mathf.Max(MinPxPerMs, pxPerMs * 0.8f));
        followButton = CreateControl("Follow", SettingsGlyph.Shape.Play, "跟隨", -386f,
            ResumeFollow);
        followButton.gameObject.SetActive(false);

        // 播放：選曲畫面沒有遊戲時鐘，所以自己往前推。看一段譜面「跑起來是什麼樣
        // 子」比靜態圖有用得多 —— 密到什麼程度、手怎麼換，都要動起來才看得出來。
        Button play = SettingsUiKit.CreateButton(viewerRoot, "Play", SettingsUiKit.Tone.Quiet,
            SettingsGlyph.Shape.Play, "播放", 24f, new Vector2(150f, 76f), out playLabel, out _);
        var playRect = play.GetComponent<RectTransform>();
        SettingsUiKit.Place(playRect, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-546f, -18f), new Vector2(150f, 76f));
        play.onClick.AddListener(TogglePlay);
    }

    private RectTransform CreateControl(string name, SettingsGlyph.Shape shape, string caption,
        float x, UnityEngine.Events.UnityAction action)
    {
        float width = string.IsNullOrEmpty(caption) ? 86f : 150f;
        Button button = SettingsUiKit.CreateButton(viewerRoot, name, SettingsUiKit.Tone.Quiet,
            shape, caption, 24f, new Vector2(width, 76f), out _, out _);
        var rect = button.GetComponent<RectTransform>();
        SettingsUiKit.Place(rect, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(x, -18f), new Vector2(width, 76f));
        button.onClick.AddListener(action);
        return rect;
    }

    private TextMeshProUGUI NextLabel(float size, Color colour, TextAlignmentOptions align)
    {
        TextMeshProUGUI label;
        if (labelsUsed < labels.Count)
        {
            label = labels[labelsUsed];
        }
        else
        {
            label = SettingsUiKit.CreateLabel(content, "L" + labels.Count, size, colour, align);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 0f);
            // 同上：這些標註的框都只有二十幾高，不改成 Overflow 就一個字都不會畫。
            label.overflowMode = TextOverflowModes.Overflow;
            labels.Add(label);
        }
        labelsUsed++;
        label.fontSize = size;
        label.color = colour;
        label.alignment = align;
        label.gameObject.SetActive(true);
        return label;
    }

    private void PlaceLabel(TextMeshProUGUI label, string text, Vector2 pivot, Vector2 position,
        Vector2 size)
    {
        SettingsUiKit.SetText(label, text);
        label.rectTransform.pivot = pivot;
        label.rectTransform.anchoredPosition = position;
        label.rectTransform.sizeDelta = size;
    }

    // ------------------------------------------------------------------
    // 資料
    // ------------------------------------------------------------------

    /// <summary>譜換了才重算。整首的格線和「離下一顆同手音符幾分」都不會隨時間變。</summary>
    private bool PrepareIfNeeded()
    {
        // 選曲畫面自己讀進來的那份優先；遊戲中沒有它，就看正在打的譜。
        Chart chart = loadedChart;
        if (chart == null && string.IsNullOrEmpty(loadedChartFile))
            chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
        if (chart == null || chart.notes == null || chart.notes.Count == 0)
        {
            preparedChart = null;
            return false;
        }
        if (ReferenceEquals(chart, preparedChart)) return true;

        preparedChart = chart;
        chartBpm = chart.first_bpm > 1f ? chart.first_bpm : 120f;
        chartNumerator = ParseNumerator(chart.time_signature);
        BuildGrid(chart);
        BuildNotes(chart);
        return true;
    }

    private static int ParseNumerator(string signature)
    {
        if (!string.IsNullOrEmpty(signature))
        {
            int slash = signature.IndexOf('/');
            if (slash > 0 && int.TryParse(signature.Substring(0, slash), out int n) && n >= 1 && n <= 16)
                return n;
        }
        return 4;
    }

    /// <summary>
    /// 把 beat_timings 換算成一條一條的四分音符線，再每 numerator 條標一次小節。
    /// </summary>
    /// <remarks>
    /// beat_timings 在這個曲庫裡有兩種意思：有的一小節一條、有的一個四分音符一條。
    /// 搞錯的話格線會差四倍 —— AutoPedal.EntriesPerBar 就是為了這件事存在的，這裡
    /// 沿用同一個判斷，不要自己再猜一次。
    /// </remarks>
    private void BuildGrid(Chart chart)
    {
        grid.Clear();
        var beats = chart.beat_timings;
        var quarters = new List<float>();

        if (beats != null && beats.Count >= 2)
        {
            int perBar = AutoPedal.EntriesPerBar(beats, chartBpm, chartNumerator);
            if (perBar <= 1)
            {
                // 一條一小節：小節內照拍號的分子等分。
                for (int i = 0; i + 1 < beats.Count; i++)
                {
                    float a = beats[i];
                    float span = beats[i + 1] - a;
                    for (int k = 0; k < chartNumerator; k++)
                        quarters.Add(a + span * k / chartNumerator);
                }
                quarters.Add(beats[beats.Count - 1]);
            }
            else
            {
                for (int i = 0; i < beats.Count; i++) quarters.Add(beats[i]);
            }
        }
        else
        {
            // 沒有 beat_timings 就照 BPM 鋪。速度變化的譜面會對不準，但總比沒有格線好。
            float quarter = 60000f / chartBpm;
            float finish = chart.music_finish_time_msec > 0 ? chart.music_finish_time_msec : 240000f;
            for (float t = 0f; t <= finish + quarter; t += quarter) quarters.Add(t);
        }

        MeasureSubdivisions(chart, quarters);
        int stepsPerQuarter = Mathf.Max(1, gridSubdivision / 4);

        for (int i = 0; i < quarters.Count; i++)
        {
            float t = quarters[i];
            float next = i + 1 < quarters.Count ? quarters[i + 1] : t + LocalQuarterMs(quarters, i);
            bool isBar = (i % Mathf.Max(1, chartNumerator)) == 0;
            grid.Add(new GridLine
            {
                ms = t,
                level = isBar ? 0 : 1,
                bar = isBar ? (i / Mathf.Max(1, chartNumerator)) + 1 : -1
            });
            for (int k = 1; k < stepsPerQuarter; k++)
            {
                grid.Add(new GridLine
                {
                    ms = Mathf.Lerp(t, next, (float)k / stepsPerQuarter),
                    level = 2,
                    bar = -1
                });
            }
        }
    }

    private static float LocalQuarterMs(List<float> quarters, int index)
    {
        if (quarters.Count < 2) return 500f;
        int i = Mathf.Clamp(index, 0, quarters.Count - 2);
        return Mathf.Max(1f, quarters[i + 1] - quarters[i]);
    }

    /// <summary>算兩個值：整首最密是幾分（報告用），以及格線要畫到幾分。</summary>
    /// <remarks>
    /// 兩個分開，是因為它們回答的不是同一件事。
    ///
    /// **最密**取最短的相鄰起音間隔 —— 這就是「這首最快要彈多快」，即使整首只出現
    /// 過一次也算數。
    ///
    /// **格線**不能跟著它走：實測一首 2644 顆的譜面裡 1/32 只出現 111 次，格線卻
    /// 會整首鋪成 1/32，糊成一片灰，反而看不出結構。所以取第 2 百分位的間隔 ——
    /// 夠細到容得下絕大多數音符，又不會被少數極端值綁架。
    ///
    /// 兩者都不算同刻的和弦（差距小於 15 ms 當成同一下），不然任何有和弦的譜面都
    /// 會被算成無限密。
    /// </remarks>
    private void MeasureSubdivisions(Chart chart, List<float> quarters)
    {
        float quarter = quarters.Count >= 2 ? LocalQuarterMs(quarters, 0) : 60000f / chartBpm;
        var onsets = new List<float>(chart.notes.Count);
        for (int i = 0; i < chart.notes.Count; i++)
        {
            var n = chart.notes[i];
            if (n != null) onsets.Add(n.startTime);
        }
        onsets.Sort();
        var gaps = new List<float>(onsets.Count);
        for (int i = 1; i < onsets.Count; i++)
        {
            float gap = onsets[i] - onsets[i - 1];
            if (gap >= 15f) gaps.Add(gap);
        }
        if (gaps.Count == 0)
        {
            densestSubdivision = 8;
            gridSubdivision = 8;
            return;
        }
        gaps.Sort();
        densestSubdivision = Mathf.Clamp(SnapNoteValue(quarter * 4f / gaps[0]), 4, 48);
        int robustIndex = Mathf.Clamp(Mathf.RoundToInt(gaps.Count * 0.02f), 0, gaps.Count - 1);
        gridSubdivision = Mathf.Clamp(SnapNoteValue(quarter * 4f / gaps[robustIndex]), 4, 24);
    }

    /// <summary>把「幾分音符」吸到常見的值上：附點和三連音也在裡面。</summary>
    private static int SnapNoteValue(float raw)
    {
        int[] allowed = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 };
        int best = allowed[0];
        float bestError = float.MaxValue;
        for (int i = 0; i < allowed.Length; i++)
        {
            float error = Mathf.Abs(Mathf.Log(raw / allowed[i]));
            if (error < bestError)
            {
                bestError = error;
                best = allowed[i];
            }
        }
        return best;
    }

    private void BuildNotes(Chart chart)
    {
        notes.Clear();
        slotCount = PianoVisualLayout.LegacyLaneCount;

        var source = chart.notes;
        var ordered = new List<NoteData>(source.Count);
        for (int i = 0; i < source.Count; i++) if (source[i] != null) ordered.Add(source[i]);
        ordered.Sort((a, b) => a.startTime.CompareTo(b.startTime));

        // 先回頭掃一次，算出每顆音符「下一顆同手音符」在什麼時候。
        var nextSameHand = new float[ordered.Count];
        float nextRight = float.NaN;
        float nextLeft = float.NaN;
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            var n = ordered[i];
            bool right = n.hand == 0;
            nextSameHand[i] = right ? nextRight : nextLeft;
            // 同刻的和弦不算「下一顆」：它是同一下按的。
            if (right)
            {
                if (float.IsNaN(nextRight) || n.startTime < nextRight - 15f) nextRight = n.startTime;
            }
            else
            {
                if (float.IsNaN(nextLeft) || n.startTime < nextLeft - 15f) nextLeft = n.startTime;
            }
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            var n = ordered[i];
            if (PianoVisualLayout.HasPianoPitch(n)) slotCount = PianoVisualLayout.PianoKeyCount;

            // 傳 1 當軌道寬度，拿到的就是佔整條軌道的比例。
            float centre = PianoVisualLayout.ResolveCenterX(n, 1f) + 0.5f;
            float half = PianoVisualLayout.ResolveVisualWidth(n, 1f) * 0.5f;
            bool hold = n.endTime > n.startTime + 1;

            string gap = string.Empty;
            float next = nextSameHand[i];
            if (!float.IsNaN(next))
            {
                float delta = next - n.startTime;
                if (delta > 15f)
                {
                    float quarter = QuarterAt(n.startTime);
                    int value = SnapNoteValue(quarter * 4f / delta);
                    if (value >= 1) gap = "1/" + value;
                }
            }

            notes.Add(new ViewNote
            {
                start = n.startTime,
                end = hold ? n.endTime : n.startTime,
                centre = centre,
                halfWidth = half,
                hand = n.hand,
                hold = hold,
                gap = gap
            });
        }
    }

    /// <summary>那個時刻的四分音符有多長。速度變化的譜面靠格線本身來反映。</summary>
    private float QuarterAt(float ms)
    {
        if (grid.Count < 2) return 60000f / chartBpm;
        float previous = float.NaN;
        for (int i = 0; i < grid.Count; i++)
        {
            if (grid[i].level == 2) continue;
            if (grid[i].ms > ms)
            {
                if (!float.IsNaN(previous)) return Mathf.Max(1f, grid[i].ms - previous);
                break;
            }
            previous = grid[i].ms;
        }
        return 60000f / chartBpm;
    }

    // ------------------------------------------------------------------
    // 畫
    // ------------------------------------------------------------------

    private void Refresh()
    {
        // 整個包起來：Refresh 半路丟例外的話，標題、格線、音符全都停在上一幀的狀
        // 態，畫面上只剩底色和按鈕 —— 那是最難查的一種壞法。寧可把例外寫在標題列。
        try
        {
            RefreshCore();
        }
        catch (System.Exception error)
        {
            quads.Clear();
            if (graphic != null) graphic.SetQuads(quads);
            HideUnusedLabels();
            SettingsUiKit.SetText(header, "畫面出錯：" + error.Message);
            Debug.LogException(error, this);
        }
    }

    private void RefreshCore()
    {
        quads.Clear();
        labelsUsed = 0;

        if (!PrepareIfNeeded())
        {
            SettingsUiKit.SetText(header,
                !string.IsNullOrEmpty(status) ? status
                : loading ? "載入譜面…"
                : "沒有譜面可以看");
            HideUnusedLabels();
            graphic.SetQuads(quads);
            return;
        }

        Rect area = content.rect;
        float width = area.width;
        float height = area.height;
        if (width <= 1f || height <= 1f) return;

        // 從選曲畫面打開時不跟播放：那時在響的是試聽片段，它的 0 秒不是譜面的 0 秒。
        Conductor conductor = string.IsNullOrEmpty(loadedChartFile)
            ? (GameManager.Instance != null ? GameManager.Instance.Conductor : null)
            : null;
        float playMs = conductor != null ? conductor.renderSongPosition : viewMs;
        if (following) viewMs = playMs;
        float songMs = viewMs;
        float playheadY = height * PlayheadViewport;
        float fromMs = songMs - playheadY / pxPerMs;
        float toMs = songMs + (height - playheadY) / pxPerMs;

        // 內容區的外框。沒有音符的時候（還在載入、或譜面是空的）至少看得出這一
        // 塊在哪裡，而不是分不清是沒畫還是畫在畫面外。
        Color frame = new Color(1f, 1f, 1f, 0.12f);
        quads.Add(new Quad { rect = new Rect(0f, 0f, width, 1f), colour = frame });
        quads.Add(new Quad { rect = new Rect(0f, height - 1f, width, 1f), colour = frame });
        quads.Add(new Quad { rect = new Rect(0f, 0f, 1f, height), colour = frame });
        quads.Add(new Quad { rect = new Rect(width - 1f, 0f, 1f, height), colour = frame });

        RenderStage(width, height, songMs);

        DrawLaneLines(width, height);
        DrawGrid(width, height, songMs, playheadY, fromMs, toMs);
        DrawNotes(width, height, songMs, playheadY, fromMs, toMs);

        // 播放頭：判定線在這一條上，往上是還沒到的。
        quads.Add(new Quad
        {
            rect = new Rect(0f, playheadY - 1.5f, width, 3f),
            colour = Playhead
        });

        graphic.SetQuads(quads);
        HideUnusedLabels();
        if (stage != null) SettingsUiKit.SetText(hint, stage.TrackMaterialInfo);

        SettingsUiKit.SetText(header,
            (string.IsNullOrEmpty(loadedTitle) ? string.Empty : loadedTitle + "    ") +
            $"BPM {chartBpm:0.#}    {chartNumerator}/4    最密 1/{densestSubdivision}    " +
            $"格線 1/{gridSubdivision}    音符 {notes.Count}    {FormatTime(songMs)}" +
            (following || conductor == null ? string.Empty : "　（已暫離播放）") +
            (stage != null
                ? (stage.IsBuilding
                    ? $"　建立中 {stage.BuiltNotes}/{stage.TotalNotes}"
                    : $"　舞台 {stage.BuiltNotes}")
                : string.Empty) +
            (stage != null && stage.FailedNotes > 0
                ? $"　失敗 {stage.FailedNotes}（{stage.FirstFailure}）"
                : string.Empty) +
            (stage != null && stage.OrphanNotes > 0
                ? $"　孤兒 {stage.OrphanNotes}"
                : string.Empty) +
            (string.IsNullOrEmpty(audioStatus) ? string.Empty : "　" + audioStatus));
        if (stage != null && stage.OrphanNotes > 0 && !string.IsNullOrEmpty(stage.FirstOrphan))
            SettingsUiKit.SetText(hint, "孤兒音符：" + stage.FirstOrphan);
    }

    private static string FormatTime(float ms)
    {
        float seconds = Mathf.Max(0f, ms) / 1000f;
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return $"{minutes}:{(seconds - minutes * 60f):00.0}";
    }

    /// <summary>
    /// 把舞台拍成一張圖貼在底下。視窗（看得到幾毫秒）由 UI 這邊的縮放決定，所以
    /// 格線和音符永遠對得上 —— 兩邊用的是同一個 pxPerMs。
    /// </summary>
    private void RenderStage(float width, float height, float songMs)
    {
        Chart chart = loadedChart;
        if (chart == null && string.IsNullOrEmpty(loadedChartFile))
            chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
        if (chart == null)
        {
            if (stageView != null) stageView.enabled = false;
            return;
        }

        if (stage == null) stage = ChartOverviewStage.Create();
        if (!ReferenceEquals(chart, stagedChart))
        {
            stagedChart = chart;
            stage.Build(chart);
        }

        float scale = canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;
        int pixelWidth = Mathf.RoundToInt(width * scale);
        int pixelHeight = Mathf.RoundToInt(height * scale);
        float visibleMs = height / Mathf.Max(0.0001f, pxPerMs);
        RenderTexture texture = stage.Render(songMs, visibleMs, PlayheadViewport,
            pixelWidth, pixelHeight);
        if (stageView == null) return;
        stageView.texture = texture;
        stageView.enabled = texture != null;
    }

    private void DrawLaneLines(float width, float height)
    {
        // 88 鍵每一條都畫會變成一片柵欄，所以只畫每個 C（八度）。
        if (slotCount == PianoVisualLayout.PianoKeyCount)
        {
            for (int midi = 24; midi <= PianoVisualLayout.PianoMidiMax; midi += 12)
            {
                float x = (midi - PianoVisualLayout.PianoMidiMin) / (float)slotCount * width;
                quads.Add(new Quad { rect = new Rect(x, 0f, 1f, height), colour = OctaveLine });
            }
            return;
        }
        for (int lane = 1; lane < slotCount; lane++)
        {
            float x = lane / (float)slotCount * width;
            quads.Add(new Quad { rect = new Rect(x, 0f, 1f, height), colour = LaneLine });
        }
    }

    private void DrawGrid(float width, float height, float songMs, float playheadY,
        float fromMs, float toMs)
    {
        float subSpacingPx = 0f;
        if (grid.Count >= 2) subSpacingPx = Mathf.Abs(grid[1].ms - grid[0].ms) * pxPerMs;
        bool labelSubLines = subSpacingPx >= 15f;
        // 縮得太小的時候細分線會糊成一片灰，而且會把頂點預算吃光（畫不完就換播放
        // 頭不見）。擠在一起就整層不畫，小節線和四分線自己還撐得住結構。
        bool drawSubLines = subSpacingPx >= 4f;

        string subText = "1/" + gridSubdivision;
        for (int i = 0; i < grid.Count; i++)
        {
            GridLine line = grid[i];
            if (line.ms < fromMs) continue;
            if (line.ms > toMs) break;
            if (line.level == 2 && !drawSubLines) continue;

            float y = playheadY + (line.ms - songMs) * pxPerMs;
            Color colour = line.level == 0 ? BarLine : (line.level == 1 ? BeatLine : SubLine);
            float thickness = line.level == 0 ? 2f : 1f;
            quads.Add(new Quad { rect = new Rect(0f, y, width, thickness), colour = colour });

            if (labelsUsed >= MaxLabels) continue;
            if (line.level == 2 && !labelSubLines) continue;

            // 右邊：這一條格線是幾分。四分線標 1/4，細分線標最密的那個值。
            var rail = NextLabel(17f, RailInk, TextAlignmentOptions.MidlineLeft);
            PlaceLabel(rail, line.level == 2 ? subText : "1/4", new Vector2(0f, 0.5f),
                new Vector2(width + 10f, y), new Vector2(RightRail - 16f, 30f));

            if (line.level == 0 && labelsUsed < MaxLabels)
            {
                var bar = NextLabel(19f, SettingsUiKit.Gold, TextAlignmentOptions.MidlineRight);
                PlaceLabel(bar, line.bar.ToString(), new Vector2(1f, 0.5f),
                    new Vector2(-12f, y), new Vector2(LeftRail - 20f, 34f));
            }
        }
    }

    private void DrawNotes(float width, float height, float songMs, float playheadY,
        float fromMs, float toMs)
    {
        for (int i = 0; i < notes.Count; i++)
        {
            ViewNote note = notes[i];
            if (note.end < fromMs) continue;
            if (note.start > toMs) break;

            float y0 = playheadY + (note.start - songMs) * pxPerMs;
            float x = note.centre * width;
            float w = Mathf.Max(3f, note.halfWidth * 2f * width);

            // 音符本體由舞台（真的音符物件）畫，這裡只放標註。
            if (note.gap.Length == 0 || labelsUsed >= MaxLabels) continue;
            var label = NextLabel(15f, GapInk, TextAlignmentOptions.MidlineLeft);
            PlaceLabel(label, note.gap, new Vector2(0f, 0.5f),
                new Vector2(x + w * 0.5f + 5f, y0 + 3f), new Vector2(60f, 28f));
        }
    }

    private void HideUnusedLabels()
    {
        for (int i = labelsUsed; i < labels.Count; i++)
        {
            if (labels[i] != null && labels[i].gameObject.activeSelf)
                labels[i].gameObject.SetActive(false);
        }
    }

    /// <summary>手指（或滑鼠）在譜面上拖曳、滾輪滾，都是捲動。</summary>
    /// <remarks>
    /// 滾輪走 UI 事件（IScrollHandler），不自己去讀 Mouse.current —— 這一層是整片
    /// 蓋住畫面的，事件被它吃掉，後面的東西就碰不到。但選曲畫面的滾輪是直接讀滑鼠
    /// 的，不經過 UI 事件，所以那邊另外擋（SongSelectionManager 開頭）。
    /// </remarks>
    private sealed class DragSurface : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
    {
        // 一格滾輪在不同平台的值差很多（Windows 給 ±120，其他平台給 ±1），所以正
        // 規化成「幾格」再乘上自己的步距。
        private const float WheelStepPixels = 140f;

        private ChartOverviewViewer owner;

        public void Bind(ChartOverviewViewer viewer) => owner = viewer;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (owner != null) owner.BeginManualScroll();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner != null) owner.ScrollBy(eventData.delta.y);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (owner == null) return;
            float notches = Mathf.Abs(eventData.scrollDelta.y) > 10f
                ? eventData.scrollDelta.y / 120f
                : eventData.scrollDelta.y;
            if (Mathf.Abs(notches) < 0.01f) return;
            owner.BeginManualScroll();
            owner.ScrollBy(-notches * WheelStepPixels);
        }
    }

    /// <summary>格線和音符全部畫在同一個 mesh 上。</summary>
    /// <remarks>
    /// 一格線一個 Image 的話，畫面上會有好幾百個 UI 物件在每幀重排版；這些東西全是
    /// 實心的矩形，直接丟頂點最省。文字還是得用 TMP，所以那邊另外做池。
    /// </remarks>
    private sealed class ChartOverviewGraphic : MaskableGraphic
    {
        private readonly List<Quad> quads = new List<Quad>();

        /// <summary>沒有貼圖的 UI 材質在這個專案裡畫不出東西，所以明講用白貼圖。</summary>
        public override Texture mainTexture => Texture2D.whiteTexture;

        public void SetQuads(List<Quad> source)
        {
            quads.Clear();
            if (source != null)
            {
                int count = Mathf.Min(source.Count, MaxQuads);
                for (int i = 0; i < count; i++) quads.Add(source[i]);
            }
            SetVerticesDirty();
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (quads.Count == 0) return;

            Rect area = rectTransform.rect;
            var vertex = UIVertex.simpleVert;
            for (int i = 0; i < quads.Count; i++)
            {
                Rect r = quads[i].rect;
                float x0 = area.xMin + r.xMin;
                float x1 = area.xMin + r.xMax;
                float y0 = area.yMin + r.yMin;
                float y1 = area.yMin + r.yMax;
                if (x1 <= x0 || y1 <= y0) continue;

                int index = vh.currentVertCount;
                vertex.color = quads[i].colour;
                vertex.position = new Vector3(x0, y0, 0f); vh.AddVert(vertex);
                vertex.position = new Vector3(x0, y1, 0f); vh.AddVert(vertex);
                vertex.position = new Vector3(x1, y1, 0f); vh.AddVert(vertex);
                vertex.position = new Vector3(x1, y0, 0f); vh.AddVert(vertex);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
