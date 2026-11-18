using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 用 MIDI 鍵盤操作選曲與設定，並在畫面下緣畫出每一段鍵現在的作用。
/// </summary>
/// <remarks>
/// **為什麼住在自己的物件上，不掛在選曲面板上。** 打開設定會把選曲面板整個關掉
/// （<c>HideSelectionPanelOnly</c>）—— 掛在上面的話，一進設定這個元件就跟著停了，
/// 而設定正是最需要它的地方。它自己一個常駐物件，每幀決定「現在哪個畫面在前面」，
/// 再把區域條搬過去。
///
/// **滑奏不需要特別處理。** 右半邊每收到一個 note on 就往前一首，所以手指滑過去
/// 自然產生一串 note on，就是一串連續的捲動 —— 和滾輪一格一格轉是同一件事。
/// 特別去偵測「這是不是滑奏」只會多一個會誤判的狀態機，而且慢的滑奏和快的連點
/// 本來就該是同一個結果。
///
/// **為什麼用鍵位不用 MIDI note。** <see cref="MidiNoteToKeyMapper"/> 已經把兩種
/// 裝置配置（28 鍵表、88 鍵中央白鍵表）都收斂成 0–27 的鍵位。照 note 分區的話，
/// 同一段程式在兩種裝置上會切在不同的地方；照鍵位分，「最右邊那個鍵」永遠是 27。
/// </remarks>
[DisallowMultipleComponent]
public sealed class SelectionKeyControl : MonoBehaviour
{
    /// <summary>最高鍵要按多久才切換設定。</summary>
    /// <remarks>
    /// 它同時也在右區裡（右區一路到底）。按一下是往前一首，按住才是開設定 ——
    /// 滑奏掃到最頂端不應該把設定叫出來，而滑奏在每個鍵上停留的時間短一個數量級。
    /// 0.55 秒：比滑奏長一個數量級，又短到不會讓人以為沒反應。
    /// </remarks>
    private const float SettingsHoldSeconds = 0.55f;

    private const int TopKey = KeyZoneBar.KeyCount - 1;

    /// <summary>最右邊幾個鍵專門給「設定」。</summary>
    /// <remarks>
    /// **為什麼要有一塊，而不是只靠按住最高音。** 看不見的手勢等於不存在 ——
    /// 沒人會去猜「按住最右邊那個鍵會開設定」。
    ///
    /// **為什麼這一塊仍然要長按。** 按一下就開的話，**往右滑奏滑到底就會開設定**，
    /// 而滑到底是正常操作。長按把「掃過去」和「停在那裡」分開：滑奏在每個鍵上
    /// 停留的時間比 <see cref="SettingsHoldSeconds"/> 短一個數量級。
    ///
    /// 代價是往右滑奏的最後三個鍵不前進。已經滑到底的人不差那三格。
    /// </remarks>
    private const int SettingsZoneFirstKey = 25;

    /// <summary>最左邊幾個鍵專門給「分類」。</summary>
    /// <remarks>
    /// 和右邊的設定區塊對稱，連「要長按」的理由也一樣：**往左滑奏滑到底就會停在
    /// 這裡**，而滑到底是正常操作。長按把「掃過去」和「停在那裡」分開。
    /// </remarks>
    private const int CategoryZoneLastKey = 2;

    /// <summary>同一個鍵連續觸發的最小間隔，擋住彈跳與重複的 note on。</summary>
    private const float RepeatGuardSeconds = 0.02f;

    /// <summary>移動（上一首／下一首／上下選）的光：暗一階的黃銅。</summary>
    /// <remarks>
    /// **只有兩個顏色，因為只有兩種角色。** 瀏覽是「移動／決定」，設定是「移動／改值」——
    /// 兩個畫面其實是同一組語意。第一版給了三個顏色（亮綠、亮金、亮藍），那是街機的
    /// 配色，和這個遊戲的深紅絨布＋金完全不同家；而且多出來的那個顏色沒有對應到任何
    /// 多出來的意思。
    ///
    /// 色票取自 <see cref="ClassicalBookUITheme"/>（整個遊戲共用的那一組），不自己發明：
    /// 移動是把 Gold 壓暗的黃銅，作用就是 Gold 本身。兩者同一個色相、差在明度 ——
    /// 分得出來，又不會有一塊跳出畫面。
    /// </remarks>
    private static readonly Color MoveGlow = new Color(0.46f, 0.31f, 0.12f, 1f);

    /// <summary>作用（決定／開始練習／改值）的光：直接用主題的金。</summary>
    private static readonly Color ActGlow = ClassicalBookUITheme.Gold;

    /// <summary>設定／返回那一塊：主題的酒紅。和兩個金色分得開，又還在同一組色票裡。</summary>
    private static readonly Color SettingsGlow = new Color(0.46f, 0.12f, 0.11f, 1f);

    /// <remarks>
    /// 設定有兩個模式，因為設定有兩層：停在大項清單上的時候中間那幾塊鍵是「選定」，
    /// 進到一類裡面才是「調小／調大」。印著「調大」卻在清單上按不出任何變化，比沒
    /// 有字還糟 —— 區域條說的必須是按下去真的會發生的事。
    /// </remarks>
    private enum Mode { Browse, Settings, SettingsMenu, Practice, Category }

    private static SelectionKeyControl instance;

    private SongSelectionManager selection;
    private GameObject selectionPanel;
    private KeyZoneBar bar;
    private Mode mode = Mode.Browse;
    private bool zonesBuilt;

    /// <summary>現在訂閱著的那一個管理器。</summary>
    /// <remarks>
    /// 以前這裡是一個 <c>bool subscribed</c>。問題是這個管理器**會死也會換人**：
    /// * 場景裡那一份和 <c>InputManager</c> 同一個物件，掛在 <c>GameplayRoot</c>
    ///   底下，而選曲畫面把那個 root 關掉 —— 關著的物件不是 null，只是不跑。
    /// * <c>GameManager.EnsureMIDIInputManagerEnabled(false)</c> 會把整個物件
    ///   Destroy 掉（輸入模式切回電腦鍵盤時）。
    ///
    /// 一個 bool 記不住「訂的是誰」：綁上去一次之後就永遠是 true，對方死了也不會
    /// 重訂，鍵盤按下去就是一點反應都沒有。改成記得是哪一個，每幀檢查它還活著嗎。
    /// </remarks>
    private MIDIInputManager boundManager;

    /// <summary>第一次綁定時印一行，出問題時才分得出是哪一段斷掉。</summary>
    private static bool bindLogged;

    private readonly bool[] keyDown = new bool[KeyZoneBar.KeyCount];
    private readonly float[] lastTrigger = new float[KeyZoneBar.KeyCount];
    private float topKeyHeldSince = -1f;
    private bool holdIsCategory;
    private readonly List<KeyZoneBar.Zone> zones = new List<KeyZoneBar.Zone>(4);

    public static SelectionKeyControl Attach(SongSelectionManager manager, GameObject panel)
    {
        if (manager == null || panel == null) return null;
        if (instance == null)
        {
            var go = new GameObject("SelectionKeyControl");
            instance = go.AddComponent<SelectionKeyControl>();
        }
        instance.selection = manager;
        instance.selectionPanel = panel;
        return instance;
    }

    private void OnDisable()
    {
        Unsubscribe();
        for (int i = 0; i < keyDown.Length; i++) keyDown[i] = false;
        topKeyHeldSince = -1f;
    }

    private void Update()
    {
        // 每幀確認自己還綁在一個**活著**的管理器上。不是只在一開始綁一次：管理器
        // 會因為 GameplayRoot 被關掉而停擺，也會被 GameManager 直接 Destroy。
        if (boundManager == null || !boundManager.isActiveAndEnabled)
        {
            Unsubscribe();
            EnsureMidiManager();
            Subscribe();
        }

        var settings = SimpleCarouselSettingsRef();
        bool settingsOpen = settings != null && settings.SettingsOpen;
        PracticeRoomScreen practice = PracticeRoomScreen.Instance;
        bool practiceOpen = practice != null && practice.IsOpen;
        bool selectionOpen = selectionPanel != null && selectionPanel.activeInHierarchy;

        if (bar == null) bar = KeyZoneBar.Ensure();

        // 沒有畫面在聽的時候就收起來。條子說的是「現在按下去會發生什麼」——
        // 沒有人在聽的時候它說的就是假話。
        if (!settingsOpen && !practiceOpen && !selectionOpen)
        {
            bar.SetVisible(false);
            bar.SetPressedKeys(null);
            return;
        }
        bar.SetVisible(true);

        // 設定蓋在任何畫面之上，所以它優先；練習室和選曲互斥。
        bool categoryOpen = selectionOpen && selection != null && selection.CategoryMenuOpen;
        Mode want = settingsOpen ? (settings.SettingsMenuShowing ? Mode.SettingsMenu : Mode.Settings)
                  : practiceOpen ? Mode.Practice
                  : categoryOpen ? Mode.Category
                  : Mode.Browse;
        if (want != mode || !zonesBuilt)
        {
            mode = want;
            BuildZones();
            zonesBuilt = true;
        }

        bar.SetPressedKeys(IsKeyDown);

        if (topKeyHeldSince >= 0f && Time.unscaledTime - topKeyHeldSince >= SettingsHoldSeconds)
        {
            topKeyHeldSince = -1f;
            if (holdIsCategory) ToggleCategoryMenu();
            else ToggleSettings();
        }
    }

    // ------------------------------------------------------------------

    private void BuildZones()
    {
        if (bar == null) return;
        zones.Clear();
        if (mode == Mode.Practice)
        {
            // 和瀏覽同一套手勢，只是作用在練習室的曲目清單上 —— 兩個畫面做的是
            // 同一件事（挑一首、開始），手不該重學。
            zones.Add(new KeyZoneBar.Zone(0, 8, Localize.T("◀ 上一首", "◀ 上一首", "◀ PREV"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(9, 17, Localize.T("開始練習", "开始练习", "START"), ActGlow));
            zones.Add(new KeyZoneBar.Zone(18, SettingsZoneFirstKey - 1,
                Localize.T("下一首 ▶", "下一首 ▶", "NEXT ▶"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(SettingsZoneFirstKey, TopKey,
                Localize.T("設定（長按）", "设定（长按）", "SETTINGS (HOLD)"), SettingsGlow));
        }
        else if (mode == Mode.Browse)
        {
            // 左右兩塊故意比中間大：捲動是這個畫面最常做的事，而且滑奏需要一段
            // 夠長的跑道才順手。
            zones.Add(new KeyZoneBar.Zone(0, CategoryZoneLastKey,
                Localize.T("分類（長按）", "分类（长按）", "GENRE (HOLD)"), SettingsGlow));
            zones.Add(new KeyZoneBar.Zone(CategoryZoneLastKey + 1, 9,
                Localize.T("◀ 上一首", "◀ 上一首", "◀ PREV"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(10, 17, Localize.T("決定", "决定", "OK"), ActGlow));
            zones.Add(new KeyZoneBar.Zone(18, SettingsZoneFirstKey - 1,
                Localize.T("下一首 ▶", "下一首 ▶", "NEXT ▶"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(SettingsZoneFirstKey, TopKey,
                Localize.T("設定（長按）", "设定（长按）", "SETTINGS (HOLD)"), SettingsGlow));
        }
        else if (mode == Mode.Category)
        {
            zones.Add(new KeyZoneBar.Zone(0, 8,
                Localize.T("◀ 上一類", "◀ 上一类", "◀ PREV GENRE"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(9, 17, Localize.T("選定", "选定", "CHOOSE"), ActGlow));
            zones.Add(new KeyZoneBar.Zone(18, SettingsZoneFirstKey - 1,
                Localize.T("下一類 ▶", "下一类 ▶", "NEXT GENRE ▶"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(SettingsZoneFirstKey, TopKey,
                Localize.T("返回（長按）", "返回（长按）", "BACK (HOLD)"), SettingsGlow));
        }
        else if (mode == Mode.SettingsMenu)
        {
            // 大項清單：只有「移動」和「選定」兩件事。中間兩塊併成一塊，因為
            // 這一層沒有「調小／調大」可做。
            zones.Add(new KeyZoneBar.Zone(0, 6, Localize.T("▼ 往下", "▼ 往下", "▼ DOWN"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(7, 20, Localize.T("進入", "进入", "OPEN"), ActGlow));
            zones.Add(new KeyZoneBar.Zone(21, SettingsZoneFirstKey - 1,
                Localize.T("▲ 往上", "▲ 往上", "▲ UP"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(SettingsZoneFirstKey, TopKey,
                Localize.T("返回（長按）", "返回（长按）", "BACK (HOLD)"), SettingsGlow));
        }
        else
        {
            // 四等分。最左往下、最右往上 —— 和清單在畫面上的方向一致：
            // 往右是往回（上面），往左是往前（下面）。
            zones.Add(new KeyZoneBar.Zone(0, 6, Localize.T("▼ 往下", "▼ 往下", "▼ DOWN"), MoveGlow));
            zones.Add(new KeyZoneBar.Zone(7, 13, Localize.T("－ 調小", "－ 调小", "− VALUE"), ActGlow));
            zones.Add(new KeyZoneBar.Zone(14, 20, Localize.T("＋ 調大", "＋ 调大", "+ VALUE"), ActGlow));
            zones.Add(new KeyZoneBar.Zone(21, SettingsZoneFirstKey - 1,
                Localize.T("▲ 往上", "▲ 往上", "▲ UP"), MoveGlow));
            // 出口和入口用同一塊鍵、同一個手勢。進得去卻找不到出口是最糟的狀態。
            zones.Add(new KeyZoneBar.Zone(SettingsZoneFirstKey, TopKey,
                Localize.T("返回（長按）", "返回（长按）", "BACK (HOLD)"), SettingsGlow));
        }
        bar.SetZones(zones);
    }

    private bool IsKeyDown(int key) => key >= 0 && key < keyDown.Length && keyDown[key];

    private static SimpleCarouselSettings SimpleCarouselSettingsRef()
    {
        return FindAnyObjectByType<SimpleCarouselSettings>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// 找一個**跑得動的** MIDI 管理器，沒有就建一個不會被畫面切換關掉的。
    /// </summary>
    /// <remarks>
    /// 舊版只問「存不存在」：<c>Instance != null</c> 就回去了。而場景裡那一份和
    /// <c>InputManager</c> 共用 <c>GameplayRoot/inputManager</c> 這個物件 —— 選曲畫面把
    /// <c>GameplayRoot</c> 關掉的時候，它的 <c>Update</c> 不跑（沒人抽 MidiJack 的佇列）、
    /// <c>OnDisable</c> 還把原生回呼退訂掉了。訂到那一份身上永遠等不到事件。
    ///
    /// 所以這裡問的是 <c>isActiveAndEnabled</c> 不是 <c>!= null</c>；搜尋時包含關著的
    /// 物件，是為了分得出「一個都沒有」和「有但都關著」—— 兩種情況的做法一樣（自己
    /// 建一個放在根目錄），但分不出來就會寫成「找得到就回去」的舊錯。
    ///
    /// 自建的那一份住在場景根目錄，沒有任何畫面關得掉它。玩家進曲子時場景那一份會
    /// 醒來，它的 <c>Awake</c> 看到已經有一個活著的就會自己退場，不會變成兩份。
    /// </remarks>
    private void EnsureMidiManager()
    {
        MIDIInputManager live = MIDIInputManager.Instance;
        if (live != null && live.isActiveAndEnabled) return;

        foreach (MIDIInputManager candidate in FindObjectsByType<MIDIInputManager>(
                     FindObjectsInactive.Include))
        {
            if (candidate != null && candidate.isActiveAndEnabled) return;
        }

        var go = new GameObject("MIDIInputManager");
        go.AddComponent<MIDIInputManager>();   // Awake 裡就會接手 Instance
    }

    private void Subscribe()
    {
        MIDIInputManager manager = MIDIInputManager.Instance;
        if (manager == null || !manager.isActiveAndEnabled) return;
        manager.OnMidiNoteOn += HandleNoteOn;
        manager.OnMidiNoteOff += HandleNoteOff;
        boundManager = manager;
        if (!bindLogged)
        {
            bindLogged = true;
            Debug.Log("[SelectionKeys] 綁定 MIDI 管理器：" + ScenePath(manager.transform));
        }
    }

    /// <summary>物件在階層裡的完整路徑。診斷用：名字一樣的物件可能有好幾個。</summary>
    private static string ScenePath(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private void Unsubscribe()
    {
        if (boundManager == null) return;
        boundManager.OnMidiNoteOn -= HandleNoteOn;
        boundManager.OnMidiNoteOff -= HandleNoteOff;
        boundManager = null;
    }

    private void HandleNoteOn(int note, float velocity)
    {
        int key = MidiNoteToKeyMapper.GetKeyIndex(note);
        if (key < 0 || key >= keyDown.Length) return;
        keyDown[key] = true;
        if (key >= SettingsZoneFirstKey || key <= CategoryZoneLastKey)
        {
            topKeyHeldSince = Time.unscaledTime;
            holdIsCategory = key <= CategoryZoneLastKey;
        }

        float now = Time.unscaledTime;
        if (now - lastTrigger[key] < RepeatGuardSeconds) return;
        lastTrigger[key] = now;

        if (mode == Mode.Settings || mode == Mode.SettingsMenu) HandleSettingsKey(key);
        else if (mode == Mode.Practice) HandlePracticeKey(key);
        else if (mode == Mode.Category) HandleCategoryKey(key);
        else HandleBrowseKey(key);
    }

    private void HandleBrowseKey(int key)
    {
        if (selection == null) selection = SongSelectionManager.Instance;
        if (selection == null || !selection.AcceptsKeyboardNavigation) return;
        // 兩端的區塊按一下不做事：它們只認長按，見 SettingsZoneFirstKey／CategoryZoneLastKey。
        if (key >= SettingsZoneFirstKey || key <= CategoryZoneLastKey) return;
        if (key <= 9) selection.NudgeFromKeyboard(-1);
        else if (key <= 17) selection.ConfirmFromKeyboard();
        else selection.NudgeFromKeyboard(1);
    }

    private void HandleCategoryKey(int key)
    {
        if (selection == null) return;
        if (key >= SettingsZoneFirstKey) return;
        if (key <= 8) selection.StepCategoryFromKeyboard(-1);
        else if (key <= 17) selection.SetCategoryMenuOpen(false);   // 選定就收起抽屜
        else selection.StepCategoryFromKeyboard(1);
    }

    private void HandlePracticeKey(int key)
    {
        PracticeRoomScreen practice = PracticeRoomScreen.Instance;
        if (practice == null || !practice.IsOpen) return;
        if (key >= SettingsZoneFirstKey) return;
        if (key <= 8) practice.StepSelection(-1);
        else if (key <= 17) practice.ConfirmFromKeyboard();
        else practice.StepSelection(1);
    }

    private void HandleSettingsKey(int key)
    {
        var settings = SimpleCarouselSettingsRef();
        if (settings == null || !settings.SettingsOpen) return;
        if (key >= SettingsZoneFirstKey) return;
        if (key <= 6) settings.MoveSettingFocus(1);
        // 大項清單上，中間兩塊都是「進入」—— StepFocusedSetting 在那一層不看方向
        // （見它自己的實作）。那一層沒有值可調，分成兩個方向只會讓其中一邊按下去
        // 沒反應。
        else if (key <= 13) settings.StepFocusedSetting(-1);
        else if (key <= 20) settings.StepFocusedSetting(1);
        else settings.MoveSettingFocus(-1);
    }

    private void HandleNoteOff(int note, float velocity)
    {
        int key = MidiNoteToKeyMapper.GetKeyIndex(note);
        if (key < 0 || key >= keyDown.Length) return;
        keyDown[key] = false;
        if (key >= SettingsZoneFirstKey || key <= CategoryZoneLastKey) topKeyHeldSince = -1f;
    }

    /// <summary>按住最左邊那一塊，拉下／收起分類選單。</summary>
    private void ToggleCategoryMenu()
    {
        if (selection == null) selection = SongSelectionManager.Instance;
        if (selection == null || !selection.AcceptsKeyboardNavigation) return;
        selection.SetCategoryMenuOpen(!selection.CategoryMenuOpen);
    }

    /// <summary>按住最高鍵在兩個模式之間切換。進得去就要出得來，用同一個動作。</summary>
    private void ToggleSettings()
    {
        var settings = SimpleCarouselSettingsRef();
        if (settings == null) return;
        // 退一層，不是直接關 —— 和畫面左上那顆「返回」同一個出口。在某一類裡面
        // 按住會回到大項清單，在大項清單上按住才離開設定。
        if (settings.SettingsOpen) settings.BackOrClose();
        else settings.ShowSettings();
    }
}
