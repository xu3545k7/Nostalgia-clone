using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using TMPro;

/// <summary>
/// Backup utility to pause/resume the entire gameplay (freeze game time).
/// This will call Conductor.Pause()/Resume() and disable common spawners to halt updates.
/// Use only as a global pause helper (e.g., for editor/testing or as a fallback).
/// </summary>
public class GamePauseManager : MonoBehaviour
{
    public static GamePauseManager Instance { get; private set; }

    [Header("Pause UI")]
    [Tooltip("Optional Image to use as the pause trigger. If assigned, it will be made clickable.")]
    public Image triggerImage;
    [Tooltip("Optional Canvas to parent the overlay. If null, the script will find a Canvas in scene.")]
    public Canvas parentCanvas;
    [Tooltip("If false, GamePauseManager will not create the on-screen overlay UI and will only pause/resume via input triggers.")]
    public bool showOverlayUI = true;

    private GameObject _overlayRoot;
    private TextMeshProUGUI _countdownLabel;
    private Coroutine _countdownCoroutine;
    private CoroutineRunner _overlayRunner;
    private List<AudioSource> _pausedAudioSources = new List<AudioSource>();

    /// <summary>暫停時被我們停住的背景影片。null ＝ 當時沒有在播。</summary>
    /// <remarks>
    /// **只恢復自己停的那一個**，和 <see cref="_pausedAudioSources"/> 同一個原則：
    /// 無條件呼叫 Play() 會把本來就停著的影片（還沒開始播、已經播完、或使用者在
    /// 設定裡關掉了影片）硬是叫起來。
    ///
    /// 影片是用 DSP 排程對齊歌曲起點的，之後就自由跑、沒有持續校正的迴圈 ——
    /// 所以它必須和音訊**同時**停、**同時**恢復。只停音訊的話，暫停多久影片就
    /// 領先多久，而且再也回不來。
    /// </remarks>
    private UnityEngine.Video.VideoPlayer _pausedVideo;

    private bool _noteSpawnerEnabled;
    private bool _beatSpawnerEnabled;
    // keep references to the specific spawners we disabled so we can restore them
    private NoteSpawner _noteSpawnerRef;
    private BeatLineSpawner _beatSpawnerRef;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);

        if (triggerImage != null)
        {
            var go = triggerImage.gameObject;
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            triggerImage.raycastTarget = true;
            btn.interactable = true;
            btn.targetGraphic = triggerImage;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => ShowPauseOverlay());
        }
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        try { if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) ShowPauseOverlay(); }
        catch { }
#else
        try { if (Input.GetMouseButtonDown(1)) ShowPauseOverlay(); }
        catch { }
#endif
    }

    public void PauseGame(Conductor conductor)
    {
        if (conductor == null) return;
        try
        {
            var ns = FindObjectOfType<NoteSpawner>();
            var bs = FindObjectOfType<BeatLineSpawner>();
            _noteSpawnerRef = ns;
            _beatSpawnerRef = bs;
            if (ns != null) { _noteSpawnerEnabled = ns.enabled; ns.enabled = false; Debug.Log("GamePauseManager: Disabled NoteSpawner"); }
            if (bs != null) { _beatSpawnerEnabled = bs.enabled; bs.enabled = false; Debug.Log("GamePauseManager: Disabled BeatLineSpawner"); }
        }
        catch { }

        try
        {
            _pausedAudioSources.Clear();
            var allAudio = FindObjectsOfType<AudioSource>();
            foreach (var a in allAudio)
            {
                try { if (a != null && a.isPlaying) { a.Pause(); _pausedAudioSources.Add(a); } }
                catch { }
            }
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to pause audio sources: " + ex); }

        try
        {
            var background = FindAnyObjectByType<GameBackgroundManager>();
            var video = background != null ? background.videoPlayer : null;
            if (video != null && video.isPlaying)
            {
                video.Pause();
                _pausedVideo = video;
                Debug.Log("GamePauseManager: Paused background video");
            }
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to pause video: " + ex); }

        try { conductor.Pause(); } catch (Exception ex) { Debug.LogWarning("GamePauseManager: Conductor.Pause failed: " + ex); }
    }

    /// <summary>
    /// 從暫停繼續**同一輪**：先叫 Conductor 接回它暫停時的時間，再把場景復原。
    /// </summary>
    /// <remarks>
    /// **只有「繼續」能走這裡。** <see cref="Conductor.Resume"/> 的前提是「你剛才被
    /// 暫停、現在從原地接下去」—— 它會把 <c>isPlaying</c> 設成 true，並拿音源當下的
    /// 播放位置去重算 DSP 基準。重新開始和離開都不滿足那個前提，見
    /// <see cref="RestoreAfterPause"/>。
    /// </remarks>
    public void ResumeGame(Conductor conductor)
    {
        Debug.Log("GamePauseManager: ResumeGame called");
        if (conductor == null)
        {
            Debug.LogWarning("GamePauseManager: No Conductor found when resuming; still restoring audio and spawners.");
        }
        else
        {
            try { conductor.Resume(); } catch (Exception ex) { Debug.LogWarning("GamePauseManager: Conductor.Resume failed: " + ex); }
        }
        RestoreAfterPause();
    }

    /// <summary>
    /// 把暫停時關掉的東西打開，但**不**碰 Conductor。
    /// </summary>
    /// <remarks>
    /// **為什麼要和 Conductor 分開。** 重新開始的流程是：停掉舊的 → 重新載入 →
    /// 預約一個約兩秒後的起跑（前導，音符先飛一段再對上音樂）。在那兩秒裡
    /// Conductor 的狀態是 <c>hasScheduledStart = true、isPlaying = false</c>，
    /// 有效位置是一個**負數**，從 -2000ms 數到 0。
    ///
    /// 這時候呼叫 <c>Resume()</c> 會把 <c>isPlaying</c> 硬設成 true，於是
    /// <c>Conductor.Update</c> 改走「正在播」那一支，位置從負的前導瞬間跳到 0 ——
    /// 整整兩秒的前導被吃掉。而有效位置是單調鉗住的，真的起跑時再把
    /// <c>songPosition</c> 歸零也救不回來：位置會卡在最高點，等音樂追上來。
    /// 症狀就是重新開始之後音符和音樂對不上。
    ///
    /// 離開也一樣：那時候 Conductor 已經要被拆掉了，叫它「繼續」只是在一個死掉的
    /// 時鐘上設一個 true。
    /// </remarks>
    public void RestoreAfterPause()
    {
        try
        {
            if (_pausedVideo != null)
            {
                _pausedVideo.Play();
                Debug.Log("GamePauseManager: Resumed background video");
            }
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to resume video: " + ex); }
        finally { _pausedVideo = null; }

        try
        {
            int resumed = 0;
            foreach (var a in _pausedAudioSources)
            {
                try { if (a != null) { a.UnPause(); resumed++; } }
                catch { }
            }
            _pausedAudioSources.Clear();
            Debug.Log($"GamePauseManager: Resumed {resumed} AudioSource(s)");
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to resume audio sources: " + ex); }

        try
        {
            // Prefer restoring the exact instances we disabled earlier
            if (_noteSpawnerRef != null)
            {
                try { _noteSpawnerRef.enabled = _noteSpawnerEnabled; Debug.Log("GamePauseManager: Restored NoteSpawner (ref)"); }
                catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to restore NoteSpawner ref: " + ex); }
            }
            else
            {
                var ns2 = FindObjectOfType<NoteSpawner>();
                if (ns2 != null) { ns2.enabled = true; Debug.Log("GamePauseManager: Restored NoteSpawner (found) to enabled"); }
                else Debug.LogWarning("GamePauseManager: No NoteSpawner found to restore");
            }

            if (_beatSpawnerRef != null)
            {
                try { _beatSpawnerRef.enabled = _beatSpawnerEnabled; Debug.Log("GamePauseManager: Restored BeatLineSpawner (ref)"); }
                catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to restore BeatLineSpawner ref: " + ex); }
            }
            else
            {
                var bs2 = FindObjectOfType<BeatLineSpawner>();
                if (bs2 != null) { bs2.enabled = true; Debug.Log("GamePauseManager: Restored BeatLineSpawner (found) to enabled"); }
                else Debug.LogWarning("GamePauseManager: No BeatLineSpawner found to restore");
            }
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to restore spawners: " + ex); }
    }

    /// <summary>暫停畫面開著 —— 這時候的觸控是要點按鈕，不是打鍵道。</summary>
    public bool IsOverlayOpen => _overlayRoot != null;

    [ContextMenu("ShowPauseOverlay")]
    public void ShowPauseOverlay()
    {
        GameplayEntryPresentation.RevealCursor();
        var conductor = FindObjectOfType<Conductor>();
        PauseGame(conductor);

        if (!showOverlayUI) return;
        if (_overlayRoot != null) return;

        Canvas canvas = parentCanvas ?? FindObjectOfType<Canvas>();
        if (canvas == null) { Debug.LogWarning("GamePauseManager: No Canvas found for overlay."); return; }

        _overlayRoot = new GameObject("PauseOverlay", typeof(RectTransform));
        _overlayRoot.transform.SetParent(canvas.transform, false);
        var rt = _overlayRoot.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        // 遮罩：暖的深色而不是純黑。純黑會把背後的軌道切成剪影，而暫停時那片
        // 譜面還是玩家想看的東西 —— 他多半是為了看清楚才按下暫停的。
        var img = _overlayRoot.AddComponent<Image>();
        img.color = new Color(0.020f, 0.012f, 0.010f, 0.80f);
        img.raycastTarget = true;
        var overlayCanvas = _overlayRoot.AddComponent<Canvas>(); overlayCanvas.overrideSorting = true; overlayCanvas.sortingOrder = 9999;
        _overlayRoot.AddComponent<CanvasScaler>(); _overlayRoot.AddComponent<GraphicRaycaster>();
        _overlayRoot.transform.SetAsLastSibling();

        var closeBtn = _overlayRoot.AddComponent<Button>(); closeBtn.transition = Selectable.Transition.None;
        closeBtn.onClick.RemoveAllListeners(); closeBtn.onClick.AddListener(() => HidePauseOverlay());
        var cg = _overlayRoot.AddComponent<CanvasGroup>(); cg.interactable = true; cg.blocksRaycasts = true; cg.ignoreParentGroups = true;
        cg.alpha = 0f;
        _overlayGroup = cg;
        _overlayRunner = _overlayRoot.AddComponent<CoroutineRunner>();

        // 投影。和考場那張榜一樣的做法：圓角、偏移、不吃射線。
        var shadowGO = new GameObject("PanelShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shadowGO.transform.SetParent(_overlayRoot.transform, false);
        var shadowRT = shadowGO.GetComponent<RectTransform>();
        shadowRT.anchorMin = shadowRT.anchorMax = new Vector2(0.5f, 0.5f);
        shadowRT.sizeDelta = new Vector2(PanelWidth + 26f, PanelHeight + 20f);
        shadowRT.anchoredPosition = new Vector2(9f, -13f);
        var shadowImg = shadowGO.GetComponent<Image>();
        shadowImg.color = new Color(0f, 0f, 0f, 0.70f);
        shadowImg.sprite = SettingsUiKit.PlainRoundedSprite(14f);
        shadowImg.type = Image.Type.Sliced;
        shadowImg.pixelsPerUnitMultiplier = 1f;
        shadowImg.raycastTarget = false;

        // **面板是一塊裝幀過的板子，不是一個灰方塊。** 和考程書／考場同一套元件：
        // 皮面、斜面金邊的書板、做舊，裡面貼一張書葉。
        var panelGO = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGO.transform.SetParent(_overlayRoot.transform, false);
        _panel = panelGO.GetComponent<RectTransform>();
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        _panel.anchoredPosition = Vector2.zero;
        var leather = panelGO.GetComponent<Image>();
        ClassicalBookUITheme.ApplyDarkSurface(leather,
            new Color(0.105f, 0.038f, 0.025f, 1f), ClassicalBookUITheme.Surface.Leather, 2.5f);
        leather.raycastTarget = true;

        var boards = BookBoardGraphic.Attach(_panel, 31);
        if (boards != null)
        {
            boards.Profile = BookShape.Profile.Volume;
            boards.Set(new Color(0.115f, 0.045f, 0.028f, 1f),
                new Color(0.135f, 0.055f, 0.033f, 1f),
                new Color(0.84f, 0.78f, 0.62f, 1f), ClassicalBookUITheme.Gold);
            var boardGrain = ClassicalBookUITheme.GetBoardTextureSprite();
            if (boardGrain != null) boards.Grain(boardGrain.texture, boardGrain.texture.width);
        }
        var boardWear = AgedPaperGraphic.Attach(_panel, 31);
        if (boardWear != null)
        {
            boardWear.Shape(BookShape.Profile.Volume);
            boardWear.Stain = new Color(0.36f, 0.22f, 0.13f, 1f);
            boardWear.Strength = 0.6f;
            boardWear.EdgeDepth = 0.055f;
            boardWear.Mottle = 0.3f;
            boardWear.Foxing = 0;
        }

        // 書葉。**單葉要關掉 Bound** —— 不關的話 BookBoardGraphic 會畫出書脊、書溝
        // 和整疊紙那一套深色結構，整張蓋在紙上（考場就是這樣整片變黑的）。
        var leafGO = new GameObject("Leaf", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        leafGO.transform.SetParent(_panel, false);
        var leafRT = leafGO.GetComponent<RectTransform>();
        leafRT.anchorMin = new Vector2(0.05f, 0.055f);
        leafRT.anchorMax = new Vector2(0.95f, 0.945f);
        leafRT.offsetMin = leafRT.offsetMax = Vector2.zero;
        var leafImg = leafGO.GetComponent<Image>();
        leafImg.color = new Color(0.93f, 0.89f, 0.74f, 1f);
        leafImg.sprite = ClassicalBookUITheme.GetPaperTextureSprite();
        leafImg.type = Image.Type.Tiled;
        leafImg.raycastTarget = false;

        var leaf = BookBoardGraphic.Attach(leafRT, 43);
        if (leaf != null)
        {
            leaf.Bound = false;
            leaf.Profile = BookShape.Profile.Page;
            var tone = new Color(0.93f, 0.89f, 0.74f, 1f);
            leaf.Set(tone, tone, tone, ClassicalBookUITheme.Gold);
            var fibre = ClassicalBookUITheme.GetPaperTextureSprite();
            if (fibre != null) leaf.Grain(fibre.texture, fibre.texture.width);
        }
        var leafWear = AgedPaperGraphic.Attach(leafRT, 43, 1);
        if (leafWear != null)
        {
            leafWear.Shape(BookShape.Profile.Page);
            leafWear.Stain = new Color(0.30f, 0.175f, 0.085f, 1f);
            leafWear.Strength = 0.88f;
            leafWear.EdgeDepth = 0.075f;
            leafWear.Mottle = 0.24f;
            leafWear.Foxing = 5;
        }
        var leafRule = PageRuleGraphic.Attach(leafRT, 43,
            new Color(0.22f, 0.115f, 0.05f, 0.90f), 0.040f, 4.4f, true, 2);
        if (leafRule != null) { leafRule.Wear = 0.26f; leafRule.Shape(BookShape.Profile.Page); }

        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(leafRT, false);
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -26f);
        titleRT.sizeDelta = new Vector2(PanelWidth - 120f, 46f);
        var title = titleGO.GetComponent<TextMeshProUGUI>();
        title.text = Localize.T("暫　停", "暂　停", "PAUSED");
        title.fontSize = 34f;
        title.color = new Color(0.22f, 0.115f, 0.055f, 1f);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        title.overflowMode = TextOverflowModes.Overflow;
        ClassicalBookUITheme.ApplyLocalizedFont(title);

        var ruleGO = new GameObject("TitleRule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ruleGO.transform.SetParent(leafRT, false);
        var ruleRT = ruleGO.GetComponent<RectTransform>();
        ruleRT.anchorMin = ruleRT.anchorMax = new Vector2(0.5f, 1f);
        ruleRT.pivot = new Vector2(0.5f, 1f);
        ruleRT.anchoredPosition = new Vector2(0f, -76f);
        ruleRT.sizeDelta = new Vector2(PanelWidth - 150f, 3f);
        var ruleImg = ruleGO.GetComponent<Image>();
        ruleImg.color = new Color(0.52f, 0.37f, 0.16f, 0.65f);
        ruleImg.sprite = SettingsUiKit.PlainRoundedSprite(1.5f);
        ruleImg.type = Image.Type.Sliced;
        ruleImg.pixelsPerUnitMultiplier = 1f;
        ruleImg.raycastTarget = false;

        // **排版群組裡只放按鈕。** 裝飾元件都是 Attach 出來的子物件，混進
        // VerticalLayoutGroup 會被當成排版元素，把按鈕擠掉。
        var container = new GameObject("PauseButtons", typeof(RectTransform));
        container.transform.SetParent(leafRT, false);
        var crt = container.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 1f); crt.anchorMax = new Vector2(0.5f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = new Vector2(0f, -98f);
        crt.sizeDelta = new Vector2(PanelWidth - 120f, 204f);
        var vLayout = container.AddComponent<VerticalLayoutGroup>();
        vLayout.childControlHeight = true; vLayout.childControlWidth = true;
        vLayout.childForceExpandHeight = false;
        vLayout.spacing = 12; vLayout.padding = new RectOffset(0, 0, 0, 0);

        var left = CreateButtonStyled(Localize.T("重新選曲", "重新选曲", "Reselect Song"), container.transform);
        left.onClick.AddListener(() =>
        {
            // 落幕 → 在看不見的時候換畫面 → 開幕。整組動作搬進回呼，所以玩家不會
            // 看到暫停的書先消失、底下的遊玩畫面露一下、選曲才蓋上來。
            StageCurtain.Play(() =>
            {
                RestoreAfterPause();
                try { GameManager.Instance?.ReselectSong(); }
                catch (Exception ex) { Debug.LogError("GamePauseManager: ReselectSong failed: " + ex); }
                HidePauseOverlay(false);
            });
        });

        var middle = CreateButtonStyled(Localize.T("繼續", "继续", "Resume"), container.transform);
        middle.onClick.AddListener(() => { StartCountdown(3); });

        var right = CreateButtonStyled(Localize.T("重新開始", "重新开始", "Restart"), container.transform);
        right.onClick.AddListener(() =>
        {
            // 重來一首也落幕。這裡的轉場不只是好看：重開會把軌道上的音符一次清光、
            // 譜面從頭鋪一遍，那一瞬間的跳動本來就該發生在幕後面。
            //
            // **復原要排在重開之前。** 暫停時 NoteSpawner／BeatLineSpawner 被關掉了，
            // 晚一步打開的話，新的一輪開頭那段生不出音符。
            StageCurtain.Play(() =>
            {
                RestoreAfterPause();
                try { GameManager.Instance?.RestartSong(); }
                catch (Exception ex) { Debug.LogError("GamePauseManager: RestartSong failed: " + ex); }
                HidePauseOverlay(false);
            });
        });

        var countdownGO = new GameObject("CountdownLabel", typeof(RectTransform)); countdownGO.transform.SetParent(leafRT, false);
        var clrt = countdownGO.GetComponent<RectTransform>();
        clrt.anchorMin = clrt.anchorMax = new Vector2(0.5f, 0f);
        clrt.pivot = new Vector2(0.5f, 0f);
        clrt.anchoredPosition = new Vector2(0f, 20f);
        clrt.sizeDelta = new Vector2(PanelWidth - 120f, 30f);
        _countdownLabel = countdownGO.AddComponent<TextMeshProUGUI>();
        _countdownLabel.alignment = TextAlignmentOptions.Center;
        _countdownLabel.fontSize = 20;
        _countdownLabel.color = new Color(0.46f, 0.30f, 0.16f, 1f);
        _countdownLabel.fontStyle = FontStyles.Italic;
        _countdownLabel.raycastTarget = false;
        _countdownLabel.overflowMode = TextOverflowModes.Overflow;
        _countdownLabel.text = string.Empty;
        _countdownLabel.gameObject.SetActive(false);

        // 進場：淡入 + 從略小長到原寸。用 unscaled —— 暫停時 timeScale 是 0。
        StartOverlayCoroutine(FadePanel(true, null));
    }

    private const float PanelWidth = 420f;
    private const float PanelHeight = 384f;
    private CanvasGroup _overlayGroup;
    private RectTransform _panel;

    private Button CreateButtonStyled(string label, Transform parent)
    {
        var go = new GameObject("Btn_" + (label ?? "btn"), typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 52);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 52f; le.preferredWidth = 300f; le.minWidth = 180f; le.flexibleWidth = 1f;

        // 皮面是貼圖，所以圓角走遮罩 —— 疊一塊平色圓角會把紋理整片蓋掉。
        var inside = SettingsUiKit.RoundCorners(rt, 10f);
        var faceGO = new GameObject("Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        faceGO.transform.SetParent(inside, false);
        var faceRT = faceGO.GetComponent<RectTransform>();
        faceRT.anchorMin = Vector2.zero; faceRT.anchorMax = Vector2.one;
        faceRT.offsetMin = faceRT.offsetMax = Vector2.zero;
        var face = faceGO.GetComponent<Image>();
        ClassicalBookUITheme.ApplyDarkSurface(face,
            new Color(0.205f, 0.095f, 0.055f, 1f), ClassicalBookUITheme.Surface.Leather, 1.4f);
        var rule = PageRuleGraphic.Attach(inside, 59,
            new Color(0.82f, 0.63f, 0.28f, 0.95f), 0.075f, 2.2f, true, 0);
        if (rule != null) rule.Wear = 0.12f;

        var lblGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lblGO.transform.SetParent(inside, false);
        var lbl = lblGO.GetComponent<TextMeshProUGUI>();
        lbl.text = label ?? string.Empty;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.fontSize = 23;
        lbl.color = new Color(0.94f, 0.88f, 0.74f, 1f);
        lbl.fontStyle = FontStyles.Bold;
        lbl.textWrappingMode = TextWrappingModes.NoWrap;
        lbl.richText = false;
        lbl.raycastTarget = false;
        lbl.overflowMode = TextOverflowModes.Overflow;
        ClassicalBookUITheme.ApplyLocalizedFont(lbl);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = lblRT.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        // 染色要染看得見的那一層：遮罩圖不畫，染它等於什麼都沒做。
        btn.targetGraphic = face;
        btn.transition = Selectable.Transition.ColorTint;
        var colours = btn.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1.22f, 1.16f, 1.06f, 1f);
        colours.pressedColor = new Color(0.82f, 0.78f, 0.72f, 1f);
        colours.fadeDuration = 0.08f;
        btn.colors = colours;
        return btn;
    }

    /// <summary>
    /// 進場／退場。<paramref name="onDone"/> 在退場做完之後跑。
    /// </summary>
    /// <remarks>
    /// 全程 <c>unscaledDeltaTime</c>：暫停時 <c>Time.timeScale</c> 是 0，用縮放後的時間
    /// 這段動畫一格都不會前進，而那正是「過場沒有出現」最常見的原因。
    /// </remarks>
    private System.Collections.IEnumerator FadePanel(bool appearing, System.Action onDone)
    {
        const float duration = 0.16f;
        CanvasGroup group = _overlayGroup;
        RectTransform panel = _panel;
        for (float at = 0f; at < duration; at += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(at / duration);
            float eased = appearing ? 1f - (1f - k) * (1f - k) : k * k;
            if (group != null) group.alpha = appearing ? eased : 1f - eased;
            if (panel != null)
                panel.localScale = Vector3.one * (appearing
                    ? Mathf.Lerp(0.94f, 1f, eased)
                    : Mathf.Lerp(1f, 0.97f, eased));
            yield return null;
        }
        if (group != null) group.alpha = appearing ? 1f : 0f;
        if (panel != null) panel.localScale = Vector3.one * (appearing ? 1f : 0.97f);
        onDone?.Invoke();
    }

    /// <summary>收起暫停面板。</summary>
    /// <param name="resumeConductor">
    /// 這一次是不是「從原地繼續」。重新開始和離開要傳 false —— 它們自己已經處理過
    /// 時間軸了，再叫 Conductor 繼續會把新的一輪弄壞，見 <see cref="RestoreAfterPause"/>。
    /// </param>
    public void HidePauseOverlay(bool resumeConductor = true)
    {
        Debug.Log($"GamePauseManager: HidePauseOverlay called (resume={resumeConductor})");
        if (_overlayRoot != null)
        {
            if (_countdownCoroutine != null)
            {
                try { StopOverlayCoroutine(_countdownCoroutine); } catch { }
                _countdownCoroutine = null;
            }
            // 先把欄位清空再淡出：這個函式會被好幾個地方呼叫，留著參照的話
            // 淡出途中再按一次就會對同一個物件啟動第二段退場。
            var leaving = _overlayRoot;
            var group = _overlayGroup;
            var panel = _panel;
            _overlayRoot = null;
            _overlayRunner = null;
            _overlayGroup = null;
            _panel = null;
            _countdownLabel = null;
            // 退場跑在常駐 host 上，因為 leaving 自己馬上就會被銷毀。
            var host = GetActiveCoroutineHost();
            host.StartCoroutine(FadeOutAndDestroy(leaving, group, panel));
        }
        if (resumeConductor)
        {
            var conductor = FindObjectOfType<Conductor>();
            ResumeGame(conductor);
        }
    }

    /// <summary>淡出之後才真的銷毀。期間擋掉射線，免得還看得見卻已經按不到。</summary>
    private System.Collections.IEnumerator FadeOutAndDestroy(GameObject leaving,
        CanvasGroup group, RectTransform panel)
    {
        if (leaving == null) yield break;
        if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
        const float duration = 0.12f;
        for (float at = 0f; at < duration; at += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(at / duration);
            if (group != null) group.alpha = 1f - k * k;
            if (panel != null) panel.localScale = Vector3.one * Mathf.Lerp(1f, 0.97f, k);
            yield return null;
        }
        if (leaving != null) Destroy(leaving);
    }

    private System.Collections.IEnumerator StartResumeCountdown(int seconds)
    {
        if (_countdownLabel == null) yield break;
        _countdownLabel.gameObject.SetActive(true);
        for (int i = seconds; i > 0; i--)
        {
            _countdownLabel.text = Localize.T(
                $"{i} 秒後繼續…", $"{i} 秒后继续…", $"Resuming in {i}…");
            ClassicalBookUITheme.ApplyLocalizedFont(_countdownLabel);
            yield return new WaitForSecondsRealtime(1f);
        }
        _countdownLabel.text = "";
        _countdownLabel.gameObject.SetActive(false);
        _countdownCoroutine = null;
        HidePauseOverlay();
    }

    private void StartCountdown(int seconds)
    {
        try
        {
            // If the overlay runner exists but its GameObject is inactive, starting coroutines on it will fail.
            // Prefer the runner only when it's active; otherwise fall back to this MonoBehaviour.
            var host = GetActiveCoroutineHost();
            if (_countdownCoroutine != null) host.StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = host.StartCoroutine(StartResumeCountdown(seconds));
        }
        catch (Exception ex) { Debug.LogWarning("GamePauseManager: Failed to start countdown: " + ex); }
    }

    private Coroutine StartOverlayCoroutine(System.Collections.IEnumerator routine)
    {
        var host = GetActiveCoroutineHost();
        return host.StartCoroutine(routine);
    }

    private void StopOverlayCoroutine(Coroutine c)
    {
        if (c == null) return;
        var host = GetActiveCoroutineHost();
        host.StopCoroutine(c);
    }

    // Returns an active MonoBehaviour to host coroutines. Preference order:
    // 1) overlay runner (if active), 2) persistent host kept alive via DontDestroyOnLoad.
    private MonoBehaviour GetActiveCoroutineHost()
    {
        if (_overlayRunner != null && _overlayRunner.gameObject.activeInHierarchy) return _overlayRunner;
        if (_persistentHost == null)
        {
            var existing = GameObject.Find("GamePauseManager_CoroutineHost");
            if (existing == null)
            {
                existing = new GameObject("GamePauseManager_CoroutineHost");
                UnityEngine.Object.DontDestroyOnLoad(existing);
            }
            _persistentHost = existing.GetComponent<PersistentCoroutineHost>() ?? existing.AddComponent<PersistentCoroutineHost>();
        }
        return _persistentHost;
    }

    // lightweight persistent host
    private static PersistentCoroutineHost _persistentHost;
    private class PersistentCoroutineHost : MonoBehaviour { }

    // Lightweight runner used to host coroutines on the overlay GameObject
    private class CoroutineRunner : MonoBehaviour { }
}
