using UnityEngine;

/// <summary>
/// 介面音效：木頭、毛氈和黃銅，全部由程式合成，不依賴任何音檔。
/// </summary>
/// <remarks>
/// **為什麼要有這個檔案。** 這是一款節奏遊戲，而整個介面在這之前一個聲音都沒有
/// —— 轉盤滾過十張卡、難度按下去、書闔上，手上都是完全沒有回饋的。玩家在進曲子
/// 之前，對「這一下到底有沒有被收到」只能靠眼睛。對一款把耳朵當主角的遊戲來說，
/// 這是最不該留的缺口。
///
/// **為什麼用合成的，不用音檔。** 專案裡一個介面音效的素材都沒有，而要求是不新
/// 增任何資源檔。合成反而有兩個好處：整組聲音的音色是同一套參數調出來的，天生就
/// 是一家人；而且每個聲音都在 180 毫秒以內、單聲道 44100Hz，九個加起來不到
/// 100KB 的記憶體，第一次用到才生、生了就快取。
///
/// **音色的依據。** 這個介面是古典、木頭、書本和鋼琴：所以每個聲音都先想它實際
/// 上是什麼東西在響，再去合成那個東西 ——
/// 游標移動是指腹掃過紙緣（極短的噪音穿過低通，加一點木頭的身體）；
/// 確定是黃銅門閂扣上（非諧音的兩三個分音、極快的起音、音高在前 30 毫秒往上跳）；
/// 返回是同一個門閂鬆開（同樣的材質，音高往下掉，而且更悶）；
/// 開關是譜架上的小銅簧（開往上、關往下，都只有一下，不留餘韻）；
/// 翻開是紙頁帶起的風（起音慢，這是它和「闔上」唯一的差別來源）；
/// 闔上是書板落下（低頻的身體 ＋ 尾巴上那一聲門閂）；
/// 鎖住是兩下敲在實心木頭上的悶響（**故意沒有任何餘韻**，會響的東西聽起來就像
/// 有反應，這裡要的正好相反）；
/// 數值跳一格是最小的那一下木頭輕敲。
///
/// **包絡線為什麼是 (1-t)^curve。** 和介面動畫同一條理由：它在終點剛好等於 0。
/// 指數式的衰減永遠到不了零，波形被剪斷的那一刻就是一個喀聲 —— 一個原本要表現
/// 「細緻」的音效，結尾卻多一個數位雜音。<see cref="FadeTail"/> 是第二道保險，
/// 專門處理成分長度算錯、被緩衝區尾端截掉的情形。
///
/// **建議的接法（這個檔案不碰任何畫面，接線由呼叫端自己做）：**
/// SongSelectionManager.OnCarouselBaseIndexChanged() → Move()。轉盤每滾過一張卡
/// 響一次。不要接在 ShowNextSong／ShowPreviousSong，那樣用拖的滑過去的那幾張會
/// 整批沒有聲音。
/// SongSelectionManager.ChangeCategory(int) → Move()。
/// SongSelectionManager.SelectSong(int) 打開成績書那一行 → Open()。
/// 同一個方法裡 SongScoreBookOverlay.Open 的 choice 回呼 → Select()。
/// SongDifficultyStrip.Select(Tier) → Select()。
/// SongSelectionManager.ToggleRecitalMode() → 走到 SetRecitalMode 的那一行用
/// Toggle(recital)；上面兩個 return（教學曲鎖住、settings 不在）用 Denied()。
/// PracticeRoomScreen.Open() → Open()，PracticeRoomScreen.Close() → Close()。
/// SimpleCarouselSettings.ShowSettings() → Open()，HideSettings() → Close()。
/// SimpleCarouselSettings.StepSetting(int,int)：isToggle 的那一支 → Toggle(新的值
/// 是不是開)，其餘 → Tick()；而
/// `if (Mathf.Approximately(before, setting.value)) return;` 那一行 → Denied()，
/// 那正好就是「推到底了、推不動」的唯一出口。
/// SimpleCarouselSettings.RunSettingAction(int) → Select()。
/// SimpleCarouselSettings.ToggleScreenMode() → Toggle(新的模式)。
/// ChartOverviewViewer.SetOpen(bool) → Open()／Close()。
/// ResultsScreen.OnClosePressed() → Back()，ResultsScreen.ShowResults() → Open()。
/// 任何一顆 interactable 是 false 的按鈕想給回饋，要自己加一層 raycast 收點擊再叫
/// Denied()：uGUI 不會把點擊送進不可互動的按鈕，所以光接 onClick 是收不到的。
///
/// **音量。** 專案沒有獨立的介面音量設定，也不該為了它去動存檔格式，所以跟著
/// <c>hitSoundVolume</c>（設定頁上的「打擊聲音量」）走 —— 那是這款遊戲唯一的音效
/// 匯流，把它關到 0 的玩家要的就是安靜。<see cref="Volume"/> 和
/// <see cref="Enabled"/> 只活在這次執行期間，不寫檔。
/// </remarks>
public static class UiSfx
{
    /// <summary>音效總開關。關掉之後每個方法都是空動作。</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>介面音效的總音量，會再乘上設定頁的打擊聲音量。</summary>
    public static float Volume
    {
        get => volume;
        set => volume = Mathf.Clamp(value, 0f, 2f);
    }

    // ------------------------------------------------------------------
    // 對外的八種聲音
    // ------------------------------------------------------------------

    /// <summary>游標移到另一個項目：指腹掃過紙緣。</summary>
    public static void Move() => Play(Sound.Move);

    /// <summary>確定一個選擇：黃銅門閂扣上，音高往上。</summary>
    public static void Select() => Play(Sound.Select);

    /// <summary>退回上一層：同一個門閂鬆開，音高往下。</summary>
    public static void Back() => Play(Sound.Back);

    /// <summary>開關切換。開往上、關往下，兩邊是不同的聲音而不是同一個。</summary>
    public static void Toggle(bool on) => Play(on ? Sound.ToggleOn : Sound.ToggleOff);

    /// <summary>面板或書翻開：紙頁帶起的風。</summary>
    public static void Open() => Play(Sound.Open);

    /// <summary>面板或書闔上：書板落下，尾巴上有一聲門閂。</summary>
    public static void Close() => Play(Sound.Close);

    /// <summary>按到鎖住的東西：兩下沒有餘韻的悶響。</summary>
    public static void Denied() => Play(Sound.Denied);

    /// <summary>數值跳一格（速度、偏移這類）：最小的木頭輕敲。</summary>
    public static void Tick() => Play(Sound.Tick);

    // ------------------------------------------------------------------
    // 播放
    // ------------------------------------------------------------------

    private enum Sound
    {
        Move,
        Select,
        Back,
        ToggleOn,
        ToggleOff,
        Open,
        Close,
        Denied,
        Tick,
    }

    private const int SoundCount = 9;
    private const int SampleRate = 44100;

    /// <summary>
    /// 同一種聲音在這段時間內只響一次。
    /// </summary>
    /// <remarks>
    /// 轉盤一幀可以滾過不只一張卡，按住方向鍵的連發也是一秒十幾下。同一個波形疊
    /// 在自己身上，聽起來不是「快」，是音量突然變成兩倍的爆音 —— 而且相位幾乎一
    /// 樣，還會有梳狀濾波的金屬味。40 毫秒是人耳分得出兩下和一下的邊界附近，壓在
    /// 這裡既不會漏掉真正的快速操作，也不會讓它們互相疊爆。
    /// </remarks>
    private const float DebounceSeconds = 0.04f;

    private static float volume = 1f;
    private static AudioSource source;
    private static AudioClip[][] bank;
    private static int[] cursor;
    private static readonly float[] lastPlayed = NewStamps();
    private static bool quitting;
    private static bool warned;

    private static SettingsManager settings;
    private static float settingsVolume = 1f;
    private static float settingsVolumeAt = float.NegativeInfinity;

    private static void Play(Sound sound)
    {
        // 介面音效沒有任何一種失敗值得打斷一次點擊，所以整段包起來。
        try
        {
            if (!Enabled || quitting) return;
            // 從 OnValidate、編輯器工具、還沒進 Play 模式的地方叫進來都是合法的，
            // 這種時候不能去生 GameObject。
            if (!Application.isPlaying) return;

            int index = (int)sound;
            float now = Time.unscaledTime;
            if (now - lastPlayed[index] < DebounceSeconds) return;
            lastPlayed[index] = now;

            float gain = volume * SettingsVolume();
            if (gain <= 0.0005f) return;

            AudioSource output = EnsureSource();
            if (output == null) return;

            AudioClip clip = Take(sound);
            if (clip == null) return;

            output.PlayOneShot(clip, gain);
        }
        catch { }
    }

    /// <summary>
    /// 播放用的 AudioSource，第一次要出聲時才生。
    /// </summary>
    /// <remarks>
    /// **為什麼不是 HideFlags.HideAndDontSave。** DontSave 的物件不歸場景管，離開
    /// Play 模式時沒人負責銷毀它，編輯器會一路印 "Cleaning up leaked objects"。
    /// 只藏在階層視窗外、再用 DontDestroyOnLoad 讓它跨場景活著，兩件事都做到，而
    /// 且退出 Play 模式時該死的照樣會死。
    /// </remarks>
    private static AudioSource EnsureSource()
    {
        // Unity 的 == null 會連「已經被銷毀」一起算進去，所以這一行也涵蓋了場景切
        // 換途中主機物件真的被清掉的情形。
        if (source != null) return source;
        if (quitting || !Application.isPlaying) return null;

        GameObject host = new GameObject("UiSfx");
        host.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(host);

        AudioSource created = host.AddComponent<AudioSource>();
        created.playOnAwake = false;
        created.loop = false;
        // 2D。留成 3D 的話這顆物件在世界原點，鏡頭飛到哪裡介面音就小到哪裡。
        created.spatialBlend = 0f;
        // 遊戲場上的殘響和濾鏡會把 30 毫秒的喀聲拖成一團霧，介面音要乾的。
        created.bypassListenerEffects = true;
        created.bypassReverbZones = true;
        // 暫停選單本身就是靠 AudioListener.pause 停住全場的；按鈕還是得有聲音。
        created.ignoreListenerPause = true;
        // 數字越大越先被搶掉。發聲數的預算是留給鋼琴的（見 PianoVoiceManager），
        // 真的撞在一起時，掉一聲點擊遠比掉一顆音符好。
        created.priority = 160;
        source = created;
        return source;
    }

    /// <summary>
    /// 設定頁上的打擊聲音量，最多四分之一秒重讀一次。
    /// </summary>
    /// <remarks>
    /// **為什麼不用 SettingsManager.Instance。** 那個屬性找不到就會**當場生一個**
    /// SettingsManager 物件出來。一個介面音效服務不該有能力改變遊戲的狀態，更不該
    /// 在關閉途中、或在根本沒有設定管理器的測試場景裡憑空長出一個單例。
    ///
    /// 節流是因為 FindAnyObjectByType 會掃全場。每次點擊掃一次其實也還好，但拖
    /// 動轉盤時 Move() 是連續在響的，沒必要每一下都掃。四分之一秒也代表玩家拉動音
    /// 量滑桿時，介面音會在四分之一秒內跟上 —— 聽起來就是即時的。
    /// </remarks>
    private static float SettingsVolume()
    {
        float now = Time.unscaledTime;
        if (now - settingsVolumeAt < 0.25f) return settingsVolume;
        settingsVolumeAt = now;

        try
        {
            // Any 不是 First：場上只會有一個 SettingsManager，而 First 為了保證順序
            // 還得依 instance id 排一輪，那是白花的。
            if (settings == null)
                settings = UnityEngine.Object.FindAnyObjectByType<SettingsManager>();
            settingsVolume = settings != null ? Mathf.Clamp01(settings.HitSoundVolume) : 1f;
        }
        catch
        {
            // 設定還沒載完的時候 HitSoundVolume 會踩到空的 gameSettings。
            settingsVolume = 1f;
        }
        return settingsVolume;
    }

    /// <summary>
    /// 取出下一個要播的波形，需要的話當場合成。
    /// </summary>
    /// <remarks>
    /// 滾動和連發這兩種會連續響的聲音各準備三個版本輪流放。同一份噪音一秒重複十
    /// 幾次，人耳分辨得出那是**同一段錄音**，聽起來就變成機關槍；三個不同種子的版
    /// 本（連木頭的音高也各差一點）就散掉了。
    ///
    /// 每次都檢查快取裡的 clip 還在不在：切場景時 Unity 會卸載沒人參照的資源，萬
    /// 一真的被卸掉，這裡當場補一個回來，而不是從此永遠沒有聲音。
    /// </remarks>
    private static AudioClip Take(Sound sound)
    {
        if (bank == null)
        {
            bank = new AudioClip[SoundCount][];
            cursor = new int[SoundCount];
        }

        int index = (int)sound;
        AudioClip[] variants = bank[index];
        if (variants == null)
        {
            variants = new AudioClip[VariantCount(sound)];
            bank[index] = variants;
        }

        // 游標本身就取模存回去，不是一路往上加：累加的版本總有一天會溢位成負數，
        // 而負數取模在 C# 還是負數，那一刻就是一個索引越界。
        int at = cursor[index] % variants.Length;
        cursor[index] = (at + 1) % variants.Length;

        if (variants[at] == null)
        {
            try
            {
                variants[at] = Build(sound, at);
            }
            catch (System.Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"UiSfx: 合成 {sound} 失敗，介面將維持無聲。{ex.Message}");
                }
                return null;
            }
        }
        return variants[at];
    }

    private static int VariantCount(Sound sound)
        => sound == Sound.Move || sound == Sound.Tick ? 3 : 1;

    private static float[] NewStamps()
    {
        float[] stamps = new float[SoundCount];
        for (int i = 0; i < stamps.Length; i++) stamps[i] = float.NegativeInfinity;
        return stamps;
    }

    /// <summary>
    /// 進入 Play 模式時把 static 欄位歸零。
    /// </summary>
    /// <remarks>
    /// 關掉 Domain Reload 的編輯器裡，static 欄位會活過「結束 Play 再開始 Play」，
    /// 但它們指到的 AudioSource 和 AudioClip 不會 —— 留下來的是一堆已銷毀的 Unity
    /// 物件參照，下一次播放就是一個 MissingReferenceException。時間戳也要一起清：
    /// Time.unscaledTime 重新從 0 算，上一輪留下的大數字會讓開頭幾秒的聲音全部被
    /// 當成抖動擋掉。
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        source = null;
        bank = null;
        cursor = null;
        settings = null;
        settingsVolume = 1f;
        settingsVolumeAt = float.NegativeInfinity;
        quitting = false;
        warned = false;
        for (int i = 0; i < lastPlayed.Length; i++) lastPlayed[i] = float.NegativeInfinity;

        // 先減後加：沒有 Domain Reload 的時候訂閱不會被清掉，直接加會愈疊愈多份。
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    /// <summary>
    /// 關閉途中不再出聲，也不再生任何物件。
    /// </summary>
    /// <remarks>
    /// 畫面被銷毀時常常還會叫一次 Close()。這時候去 new GameObject 換來的是一句
    /// "Some objects were not cleaned up when closing the scene"，而那一聲本來也不
    /// 可能有人聽到。
    /// </remarks>
    private static void OnQuitting() => quitting = true;

    // ------------------------------------------------------------------
    // 合成
    // ------------------------------------------------------------------

    private static AudioClip Build(Sound sound, int variant)
    {
        switch (sound)
        {
            case Sound.Move: return BuildMove(variant);
            case Sound.Select: return BuildSelect();
            case Sound.Back: return BuildBack();
            case Sound.ToggleOn: return BuildToggleOn();
            case Sound.ToggleOff: return BuildToggleOff();
            case Sound.Open: return BuildOpen();
            case Sound.Close: return BuildClose();
            case Sound.Denied: return BuildDenied();
            default: return BuildTick(variant);
        }
    }

    /// <summary>指腹掃過紙緣：一小撮被低通削鈍的噪音，加一點木頭的身體。</summary>
    private static AudioClip BuildMove(int variant)
    {
        float[] buffer = NewBuffer(0.050f);
        AddNoise(buffer, 0f, 0.045f, 1f, 0.0008f, 5f, 2600f, 420f, 17 + variant * 91);
        // 每個版本差 4.5% 的音高。三個版本輪流放，聽起來才不是同一段錄音在重播。
        float wood = 190f * (1f + 0.045f * (variant - 1));
        AddPartial(buffer, 0f, 0.040f, wood, wood * 0.9f, 0.040f, 0.5f, 0.001f, 4f);
        return Finish("UiSfx.Move", buffer, 0.20f);
    }

    /// <summary>黃銅門閂扣上：非諧音的三個分音，音高在前 30 毫秒往上跳一階。</summary>
    private static AudioClip BuildSelect()
    {
        float[] buffer = NewBuffer(0.165f);
        // 前緣的那一下撞擊。沒有它，剩下的分音聽起來像風琴而不是金屬。
        AddNoise(buffer, 0f, 0.030f, 0.8f, 0.0006f, 6f, 5200f, 700f, 331);
        const float baseHz = 523f;
        // 2.76 和 5.40 是鐘／銅器的分音比，刻意不是整數倍 —— 整數倍會合成出一個
        // 樂音，聽起來就是「一個音」，不是「一塊金屬」。
        AddPartial(buffer, 0f, 0.150f, baseHz, baseHz * 1.12f, 0.030f, 1f, 0.002f, 3f);
        AddPartial(buffer, 0f, 0.110f, baseHz * 2.76f, baseHz * 3.09f, 0.030f, 0.35f, 0.0015f, 5f);
        // 高的分音衰減得比低的快，這是金屬和塑膠聽起來不同的主因。
        AddPartial(buffer, 0f, 0.060f, baseHz * 5.40f, baseHz * 5.40f, 0.010f, 0.12f, 0.001f, 7f);
        return Finish("UiSfx.Select", buffer, 0.55f);
    }

    /// <summary>同一個門閂鬆開：音高往下掉，高頻少，所以更悶。</summary>
    private static AudioClip BuildBack()
    {
        float[] buffer = NewBuffer(0.140f);
        AddNoise(buffer, 0f, 0.028f, 0.7f, 0.0006f, 6f, 3200f, 500f, 613);
        const float baseHz = 392f;
        AddPartial(buffer, 0f, 0.130f, baseHz, baseHz * 0.84f, 0.055f, 1f, 0.002f, 3.2f);
        AddPartial(buffer, 0f, 0.085f, baseHz * 2.76f, baseHz * 2.32f, 0.055f, 0.22f, 0.0015f, 5f);
        return Finish("UiSfx.Back", buffer, 0.45f);
    }

    /// <summary>譜架上的小銅簧扣上去。</summary>
    private static AudioClip BuildToggleOn()
    {
        float[] buffer = NewBuffer(0.085f);
        AddNoise(buffer, 0f, 0.018f, 0.9f, 0.0005f, 7f, 6000f, 900f, 1201);
        AddPartial(buffer, 0f, 0.075f, 880f, 988f, 0.012f, 0.7f, 0.0015f, 4f);
        AddPartial(buffer, 0f, 0.040f, 880f * 2.76f, 988f * 2.76f, 0.012f, 0.25f, 0.001f, 6f);
        return Finish("UiSfx.ToggleOn", buffer, 0.38f);
    }

    /// <summary>同一根銅簧鬆開，底下多一顆毛氈的悶響。</summary>
    private static AudioClip BuildToggleOff()
    {
        float[] buffer = NewBuffer(0.090f);
        AddNoise(buffer, 0f, 0.018f, 0.8f, 0.0005f, 7f, 3800f, 700f, 1451);
        AddPartial(buffer, 0f, 0.075f, 740f, 660f, 0.014f, 0.6f, 0.0015f, 4f);
        // 關的那一下落在毛氈上：一顆低頻的身體，沒有上面的亮分音。
        AddPartial(buffer, 0f, 0.070f, 165f, 155f, 0.030f, 0.4f, 0.002f, 4f);
        return Finish("UiSfx.ToggleOff", buffer, 0.34f);
    }

    /// <summary>紙頁帶起的風：起音慢是它和「闔上」唯一的差別來源。</summary>
    private static AudioClip BuildOpen()
    {
        float[] buffer = NewBuffer(0.175f);
        // 35 毫秒的起音。同樣一撮噪音，起音拉長就從「敲」變成「掃」。
        AddNoise(buffer, 0f, 0.170f, 1f, 0.035f, 2.2f, 4200f, 900f, 2711);
        AddPartial(buffer, 0.020f, 0.140f, 150f, 165f, 0.060f, 0.45f, 0.010f, 3f);
        return Finish("UiSfx.Open", buffer, 0.40f);
    }

    /// <summary>書板落下，尾巴上補一聲門閂 —— 那一下才是「關上了」的訊號。</summary>
    private static AudioClip BuildClose()
    {
        float[] buffer = NewBuffer(0.170f);
        AddNoise(buffer, 0f, 0.110f, 1f, 0.0015f, 3.5f, 1800f, 300f, 3301);
        AddPartial(buffer, 0f, 0.150f, 132f, 112f, 0.050f, 0.9f, 0.002f, 3f);
        AddNoise(buffer, 0.095f, 0.020f, 0.35f, 0.0005f, 6f, 6500f, 1200f, 3697);
        return Finish("UiSfx.Close", buffer, 0.50f);
    }

    /// <summary>敲在實心木頭上的兩下悶響。</summary>
    private static AudioClip BuildDenied()
    {
        float[] buffer = NewBuffer(0.150f);
        // 沒有任何會延續的分音：會響的東西聽起來就像有反應，這裡要的正好相反。
        AddNoise(buffer, 0f, 0.050f, 1f, 0.001f, 4f, 1400f, 260f, 4201);
        AddPartial(buffer, 0f, 0.050f, 118f, 105f, 0.030f, 0.8f, 0.002f, 5f);
        // 第二下比第一下低也比較小聲：兩下一樣會像節拍器，不像推不動。
        AddNoise(buffer, 0.075f, 0.050f, 0.75f, 0.001f, 4f, 1200f, 240f, 4507);
        AddPartial(buffer, 0.075f, 0.055f, 110f, 98f, 0.030f, 0.6f, 0.002f, 5f);
        return Finish("UiSfx.Denied", buffer, 0.50f);
    }

    /// <summary>數值跳一格：最小的一下木頭輕敲。</summary>
    private static AudioClip BuildTick(int variant)
    {
        float[] buffer = NewBuffer(0.030f);
        AddNoise(buffer, 0f, 0.026f, 1f, 0.0004f, 6f, 7000f, 1600f, 5003 + variant * 137);
        float hz = 1320f * (1f + 0.03f * (variant - 1));
        AddPartial(buffer, 0f, 0.018f, hz, hz, 0.001f, 0.35f, 0.0006f, 6f);
        return Finish("UiSfx.Tick", buffer, 0.17f);
    }

    // ------------------------------------------------------------------
    // 波形工具
    // ------------------------------------------------------------------

    private static float[] NewBuffer(float seconds)
        => new float[Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate))];

    private static AudioClip Finish(string name, float[] buffer, float peak)
    {
        Normalise(buffer, peak);
        FadeTail(buffer, 0.003f);
        AudioClip clip = AudioClip.Create(name, buffer.Length, 1, SampleRate, false);
        clip.SetData(buffer, 0);
        return clip;
    }

    /// <summary>
    /// 一撮噪音：先低通再扣掉更低的部分，等於留下一條頻帶。
    /// </summary>
    /// <remarks>
    /// 木頭和毛氈的聲音幾乎全是噪音，差別只在哪一段頻率留著、以及衰減多快。低通決
    /// 定材質有多鈍（2000 是木頭，7000 是指甲），扣掉的低頻則是為了不讓每個介面音
    /// 都帶著一團隆隆聲 —— 那會和音樂的低頻打架。
    /// </remarks>
    private static void AddNoise(float[] buffer, float startSec, float lenSec, float amp,
        float attackSec, float curve, float lowHz, float highHz, int seed)
    {
        int start = Mathf.Clamp(Mathf.RoundToInt(startSec * SampleRate), 0, buffer.Length);
        int count = Mathf.Min(Mathf.RoundToInt(lenSec * SampleRate), buffer.Length - start);
        if (count <= 1) return;

        noiseState = (uint)seed | 1u;
        float lowK = Coefficient(lowHz);
        float highK = Coefficient(highHz);
        float low = 0f;
        float high = 0f;
        float attack = attackSec / lenSec;

        for (int i = 0; i < count; i++)
        {
            low += (NextNoise() - low) * lowK;
            high += (low - high) * highK;
            buffer[start + i] += (low - high) * amp * Envelope(i / (float)count, attack, curve);
        }
    }

    /// <summary>
    /// 一個分音，前 <paramref name="glideSec"/> 秒之內把音高滑到目標值。
    /// </summary>
    /// <remarks>
    /// 滑音只發生在開頭，不是整段：整段慢慢滑會變成滑管哨子，而門閂扣上時音高的
    /// 變化是在撞擊的瞬間完成的，之後就定住。
    /// </remarks>
    private static void AddPartial(float[] buffer, float startSec, float lenSec, float fromHz,
        float toHz, float glideSec, float amp, float attackSec, float curve)
    {
        int start = Mathf.Clamp(Mathf.RoundToInt(startSec * SampleRate), 0, buffer.Length);
        int count = Mathf.Min(Mathf.RoundToInt(lenSec * SampleRate), buffer.Length - start);
        if (count <= 1) return;

        float glide = Mathf.Max(0.0001f, glideSec);
        float attack = attackSec / lenSec;
        // 相位從 0 出發，sin(0) 就是 0：省掉開頭那個因為波形突然從非零起跳的喀聲。
        float phase = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float hz = t >= glide ? toHz : Mathf.Lerp(fromHz, toHz, t / glide);
            phase += 2f * Mathf.PI * hz / SampleRate;
            buffer[start + i] += Mathf.Sin(phase) * amp * Envelope(i / (float)count, attack, curve);
        }
    }

    /// <summary>起音是直線，衰減是 (1-t)^curve —— 在終點剛好等於 0。</summary>
    private static float Envelope(float t, float attackFraction, float curve)
    {
        if (t <= 0f) return 0f;
        // 起音不准是 0：真的從第一個取樣就滿振幅，那是一個方波緣，聽起來是數位
        // 爆音而不是撞擊。半毫秒已經夠尖銳了。
        float attack = Mathf.Clamp(attackFraction, 0.0005f, 0.95f);
        if (t < attack) return t / attack;
        float fall = (t - attack) / (1f - attack);
        return Mathf.Pow(1f - fall, curve);
    }

    /// <summary>單極濾波器的係數。</summary>
    private static float Coefficient(float cutoffHz)
    {
        if (cutoffHz <= 0f) return 0f;
        if (cutoffHz >= SampleRate * 0.5f) return 1f;
        return 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate);
    }

    private static void Normalise(float[] buffer, float peak)
    {
        float max = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float magnitude = buffer[i] < 0f ? -buffer[i] : buffer[i];
            if (magnitude > max) max = magnitude;
        }
        if (max <= 0.000001f) return;

        // 先正規化再乘上目標峰值，各個聲音之間的相對音量才是這裡寫的那組數字，而
        // 不是「疊了幾個成分」的副產物。
        float scale = peak / max;
        for (int i = 0; i < buffer.Length; i++) buffer[i] *= scale;
    }

    /// <summary>最後幾毫秒收到零，保險用。</summary>
    /// <remarks>
    /// 每個成分的包絡線本來就會走到 0，但只要有一個成分的長度算過頭、被緩衝區尾端
    /// 截掉，結尾就會留下一個非零的取樣 —— 那就是一聲喀。三毫秒短到聽不出來，卻
    /// 能讓這種計算失誤不會變成聽得見的瑕疵。
    /// </remarks>
    private static void FadeTail(float[] buffer, float seconds)
    {
        int count = Mathf.Min(buffer.Length, Mathf.RoundToInt(seconds * SampleRate));
        if (count <= 1) return;
        int start = buffer.Length - count;
        for (int i = 0; i < count; i++)
            buffer[start + i] *= 1f - i / (float)(count - 1);
    }

    private static uint noiseState = 1u;

    /// <summary>
    /// xorshift 白噪音。
    /// </summary>
    /// <remarks>
    /// 不用 UnityEngine.Random 有兩個理由：它是全域狀態，在這裡抽幾千個數會把別人
    /// 依賴的隨機序列整個推移掉；而且合成結果必須每次執行都一模一樣，否則同一顆按
    /// 鈕在不同人的機器上會是不同的音色。
    /// </remarks>
    private static float NextNoise()
    {
        noiseState ^= noiseState << 13;
        noiseState ^= noiseState >> 17;
        noiseState ^= noiseState << 5;
        return (noiseState & 0xFFFFFFu) * (2f / 0xFFFFFF) - 1f;
    }
}
