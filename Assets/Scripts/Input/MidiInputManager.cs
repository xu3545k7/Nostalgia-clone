using UnityEngine;
using MidiJack;

[DefaultExecutionOrder(-50)]
public class MIDIInputManager : MonoBehaviour
{
    public static MIDIInputManager Instance { get; private set; }
    private const int LaneCount = 28;
    private const int MidiNoteCount = 128;
    private const int MidiChannelCount = 16;

    public delegate void MidiNoteEvent(int note, float velocity);
    public delegate void TimedMidiNoteEvent(int note, int lane, float velocity, double realtime,
        long inputEventId);
    public event MidiNoteEvent OnMidiNoteOn;
    public event MidiNoteEvent OnMidiNoteOff;
    public event TimedMidiNoteEvent OnMidiNoteOnTimed;
    public event TimedMidiNoteEvent OnMidiNoteOffTimed;

    /// <summary>
    /// 同一顆鍵多快之內的第二次 note_on 算彈跳。
    /// </summary>
    /// <remarks>
    /// **保守。** 錄過這台琴的原始訊息：五次按壓 = 五個 on + 五個 off，間隔
    /// 800~900ms，完全沒有彈跳。既然沒有證據說這裡有抖動，這個窗就不該開大 ——
    /// 它擋掉的每一下都是真的音符，而擋錯的代價（漏掉一個音）遠高於漏擋的代價
    /// （多一個音）。
    ///
    /// 8ms 只擋得掉驅動層重送的那一種，那是唯一一種確定存在、而且確定不是演奏
    /// 的東西。真的遇到會抖的琴再往上調。
    /// </remarks>
    [SerializeField, Range(0f, 60f)] private float duplicateNoteOnWindowMs = 8f;

    /// <summary>
    /// 明顯更輕的重複用稍寬一點的窗。
    /// </summary>
    /// <remarks>
    /// 「同一顆鍵、很快又來一次、而且明顯更輕」是機械回彈的特徵，所以這一種可以
    /// 開得比純時間條件寬一點。但也只是一點：30ms 之內的輕重複在真正的演奏裡幾乎
    /// 不存在，而 70ms 就開始吃到快速的輕擊了。
    /// </remarks>
    [SerializeField, Range(0f, 200f)] private float softRepeatWindowMs = 30f;

    /// <summary>輕到這個比例以下才算「明顯更輕」。</summary>
    private const float ChatterSofterShare = 0.62f;

    // ── 誤觸過濾 ─────────────────────────────────────────────────────
    // 排列很緊、彈簧很鬆的塑膠鍵盤上，一根手指常常會擦到隔壁鍵。那種誤觸有兩個
    // 很好認的特徵：**力度很輕**，而且**幾乎和真正要彈的那顆同時發生**。
    //
    // 空打本身不扣分（判定端找不到音符就直接 return），但它會發出一個不該有的
    // 鋼琴音；更糟的是如果那條 lane 上剛好有音符在判定窗內，會被它用很差的時機
    // 提前吃掉。所以在**輸入端**擋掉，比在判定端補救乾淨。
    private readonly double[,] lastAcceptedRealtime = new double[MidiChannelCount, MidiNoteCount];
    private readonly float[,] lastAcceptedVelocity = new float[MidiChannelCount, MidiNoteCount];
    private readonly bool[,] notePressed = new bool[MidiChannelCount, MidiNoteCount];
    private readonly float[,] noteVelocities = new float[MidiChannelCount, MidiNoteCount];
    private readonly int[,] noteLanes = new int[MidiChannelCount, MidiNoteCount];
    private readonly double[,] lastNoteOnRealtime = new double[MidiChannelCount, MidiNoteCount];
    private readonly int[] lanePressCounts = new int[LaneCount];
    // ── 前瞻扣留 ─────────────────────────────────────────
    //
    // LooksLikeBrush 只往回看，所以「擦碰比正主先到」這個方向它結構上抓不到：
    // 那一刻還沒有任何東西可以拿來比。而手指滾到隔壁鍵的時候，擦碰**通常就是
    // 先到的那一下**。
    //
    // 唯一的辦法是等一下再看。代價可以只由可疑的那些付：只扣留力度低於
    // BrushLookaheadMaxVelocity 的 note-on，其餘的照樣立刻送出、完全沒有延遲。
    //
    // 而且被扣留的那些，**判定也不會變差**：送出時帶的是原本的硬體時間戳，
    // 判定端一路用的就是那個值（見 GameManager.HandleMidiNoteOnTimed 的
    // TimingMath.ProjectSongPosToEvent）。晚送出不等於晚判定，只有鋼琴聲會晚。
    private struct HeldNoteOn
    {
        public MidiChannel Channel;
        public int ChannelIndex;
        public int Note;
        public float Velocity;
        public double Timestamp;
    }

    private readonly System.Collections.Generic.List<HeldNoteOn> heldNoteOns =
        new System.Collections.Generic.List<HeldNoteOn>(8);

    private bool wasPollingMidi;
    private bool midiCallbacksSubscribed;
    private bool timingSourceLogged;
    private InputModeType lastInputMode = InputModeType.Keyboard;
    private static long inputEventSequence;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        SubscribeMidiCallbacks();
    }

    private void OnDisable()
    {
        UnsubscribeMidiCallbacks();
        ResetMidiState();
    }

    private void OnDestroy()
    {
        UnsubscribeMidiCallbacks();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        InputModeType inputMode = SettingsManager.Instance != null
            ? SettingsManager.Instance.InputMode
            : InputModeType.Keyboard;
        if (inputMode != lastInputMode)
        {
            ResetMidiState();
            lastInputMode = inputMode;
        }
        bool shouldPoll = inputMode != InputModeType.Keyboard;
        if (!shouldPoll)
        {
            if (wasPollingMidi)
            {
                ResetMidiState();
            }
            wasPollingMidi = false;
            return;
        }
        wasPollingMidi = true;

        // MidiJack drains its native queue the first time it is queried in a
        // frame. Its note delegates then report every queued transition in
        // order, including an off/on pair that ends in the same final state.
        // The old 128-key state scan lost those fast re-strikes completely.
        try
        {
            MidiMaster.GetKey(0);
        }
        catch { }

        // 抽完佇列才放行：這一格新到的 note-on 有機會先揭穿還扣著的擦碰。
        FlushExpiredHeldNoteOns();
    }

    private void SubscribeMidiCallbacks()
    {
        if (midiCallbacksSubscribed) return;
        try
        {
            MidiMaster.noteOnDelegate += HandleNativeNoteOn;
            MidiMaster.noteOffDelegate += HandleNativeNoteOff;
            MidiMaster.knobDelegate += HandleNativeKnob;
            midiCallbacksSubscribed = true;
        }
        catch { midiCallbacksSubscribed = false; }
    }

    private void UnsubscribeMidiCallbacks()
    {
        if (!midiCallbacksSubscribed) return;
        try
        {
            MidiMaster.noteOnDelegate -= HandleNativeNoteOn;
            MidiMaster.noteOffDelegate -= HandleNativeNoteOff;
            MidiMaster.knobDelegate -= HandleNativeKnob;
        }
        catch { }
        midiCallbacksSubscribed = false;
        // A pedal held while the device goes away would otherwise stay stuck
        // down and sustain every later note forever.
        sustainPedalChannels = 0;
    }

    // ── 延音踏板 (CC64) ──────────────────────────────────────────────────

    private const int SustainControlNumber = 64;
    /// <summary>MIDI convention: 64 and above counts as pressed.</summary>
    private const float SustainDownThreshold = 64f / 127f;

    /// <summary>Bitmask of channels currently holding the sustain pedal.</summary>
    private int sustainPedalChannels;

    /// <summary>True while a physical sustain pedal is held on any channel.</summary>
    public bool SustainPedalDown => sustainPedalChannels != 0;

    private void HandleNativeKnob(MidiChannel channel, int knobNumber, float level)
    {
        if (knobNumber != SustainControlNumber) return;
        if (!ShouldAcceptMidi()) return;

        int channelIndex = (int)channel;
        if (channelIndex < 0 || channelIndex >= 16) return;

        int bit = 1 << channelIndex;
        // Tracked per channel rather than as one flag: a keyboard that sends the
        // pedal on a different channel than its keys would otherwise cancel out.
        if (level >= SustainDownThreshold) sustainPedalChannels |= bit;
        else sustainPedalChannels &= ~bit;
    }

    private bool ShouldAcceptMidi()
    {
        var settings = SettingsManager.Instance;
        return settings != null && settings.InputMode != InputModeType.Keyboard;
    }

    /// <summary>
    /// 這一下像不像擦到隔壁鍵。
    /// </summary>
    /// <remarks>
    /// 兩個條件都要成立才擋：**比剛剛那顆明顯輕**，而且**在幾毫秒內、只差一兩個
    /// 半音**。只看力度會誤殺真正的弱奏；只看相鄰會誤殺半音和弦。兩個一起看，
    /// 才分得出「手指擦到」和「刻意彈的小二度」。
    /// </remarks>
    /// <summary>
    /// 到得了自己平常手勁的這個比例，就一定是真音。
    /// </summary>
    /// <remarks>
    /// 0.7 不是隨便挑的：擦到鄰鍵是手指的重量壓下去，那個力道遠低於任何一次刻意
    /// 的觸鍵。留三成餘裕讓最輕的弱奏也過得去，而真正的擦碰通常只有平常手勁的兩
    /// 三成，離這條線還很遠。
    /// </remarks>
    private const float BrushTouchShare = 0.7f;

    /// <summary>還沒認識這個玩家之前用的手勁。</summary>
    private const float FallbackTouchLevel = 64f;

    /// <summary>兩側都有剛被接受的鄰音嗎。有的話這一鍵是被夾住的，不是被蹭到的。</summary>
    private bool HasAcceptedNeighboursBothSides(int channelIndex, int note, int reach,
        double timestamp, double window)
    {
        bool below = false;
        bool above = false;
        for (int other = Mathf.Max(0, note - reach);
             other <= Mathf.Min(MidiNoteCount - 1, note + reach); other++)
        {
            if (other == note) continue;
            if (lastAcceptedVelocity[channelIndex, other] <= 0f) continue;
            if (System.Math.Abs(timestamp - lastAcceptedRealtime[channelIndex, other]) > window) continue;
            if (other < note) below = true;
            else above = true;
        }
        return below && above;
    }

    /// <summary>這一下重到不可能是擦到的嗎。</summary>
    private static bool IsDeliberateTouch(float velocity01)
    {
        float struck = velocity01 * 127f;
        float ownTouch = PlayerTouch.Known ? PlayerTouch.Level : FallbackTouchLevel;
        return struck >= ownTouch * BrushTouchShare;
    }

    private bool LooksLikeBrush(int channelIndex, int note, float velocity, double timestamp)
    {
        var settings = SettingsManager.Instance;
        if (settings == null || !settings.BrushRejectionEnabled) return false;

        float ratio = settings.BrushVelocityRatio;
        double window = settings.BrushWindowMs * 0.001;
        int reach = settings.BrushSemitones;

        // **絕對條件：接近自己平常手勁的那一下，不可能是擦到的。**
        //
        // 原本的規則只有相對條件（比鄰居輕 40% 就擋），而那個假設在真的有強弱的
        // 演奏底下是錯的：左手重音配右手的弱奏旋律、低音鋪底配上面的輕聲部 ——
        // 那些都是「比鄰居輕很多」，但每一顆都是刻意彈的。
        //
        // 擦到的特徵不是「比旁邊輕」，是「輕得不像在彈琴」。所以拿玩家**自己**的
        // 平常力度當尺：到得了七成的那一下，無論旁邊發生什麼都是真音。
        //
        // 用自己的中間值而不是固定數字，是因為手勁因人而異 —— 對重手的人來說 60
        // 是擦到，對輕手的人來說 60 已經是強奏了。
        if (IsDeliberateTouch(velocity)) return false;

        // **被兩邊夾住的那一鍵不可能是擦到的。**
        //
        // 擦碰是手指滑過去的動作，被蹭到的永遠是一串按鍵的**邊緣**；中間那一鍵
        // 要被蹭到，手指得先跨過它兩側的鍵，那不是擦碰，那是整隻手壓下去。
        //
        // 沒有這一條的話，三鍵和弦最常被吃掉的正好是中間那一顆 —— 中指通常比大
        // 拇指和小指輕，兩側各一個更響的鄰居，比例條件同時成立兩次。而在演奏會
        // 模式裡，中間那一顆恰好就是唯一能拿 PRECISE 的那一格。
        if (HasAcceptedNeighboursBothSides(channelIndex, note, reach, timestamp, window))
            return false;

        for (int other = Mathf.Max(0, note - reach);
             other <= Mathf.Min(MidiNoteCount - 1, note + reach); other++)
        {
            if (other == note) continue;
            if (lastAcceptedVelocity[channelIndex, other] <= 0f) continue;
            if (timestamp - lastAcceptedRealtime[channelIndex, other] > window) continue;
            if (velocity < lastAcceptedVelocity[channelIndex, other] * ratio) return true;
        }
        return false;
    }

    private void HandleNativeNoteOn(MidiChannel channel, int note, float velocity)
    {
        if (!ShouldAcceptMidi() || note < 0 || note >= MidiNoteCount || velocity <= 0f) return;

        // 力度地板。鬆彈簧鍵盤的擦碰幾乎都落在很低的力度，這是最直接的一刀。
        var inputSettings = SettingsManager.Instance;
        if (inputSettings != null && velocity * 127f < inputSettings.MinNoteOnVelocity)
        {
            MistouchProbe.RejectedByFloor();
            return;
        }

        int channelIndex = GetChannelIndex(channel);
        if (channelIndex < 0) return;
        int lane = MidiNoteToKeyMapper.GetKeyIndex(note);
        double timestamp = GetHardwareEventTime(out bool usedNativeTimestamp);
        if (!timingSourceLogged)
        {
            timingSourceLogged = true;
            double ageMs = Mathf.Max(0f,
                (float)((GetRealtimeSinceStartup() - timestamp) * 1000.0));
            Debug.Log($"[MIDI Timing] source={(usedNativeTimestamp ? "native QPC" : "current frame")}, " +
                $"callback age={ageMs:0.00}ms");
        }
        // 按鍵抖動保護。
        //
        // **原本只在「這顆鍵還被認為按著」的時候才檢查** —— 而會送出 off 邊緣的
        // 那種彈跳（on → off → on）永遠不滿足那個條件，所以整道保護對它完全無
        // 效。抖動會不會附帶 off，是琴的韌體決定的，不是我們能假設的。
        //
        // 順帶：BlockKeyChatter 這個設定以前**沒有任何地方讀它** —— 介面上調得
        // 動、存得起來，但輸入層從來沒問過。現在接上了。
        var chatterSettings = SettingsManager.Instance;
        if (chatterSettings == null || chatterSettings.BlockKeyChatter)
        {
            double sinceLast = timestamp - lastNoteOnRealtime[channelIndex, note];
            double window = Mathf.Max(0f, duplicateNoteOnWindowMs) * 0.001;

            // 明顯更輕的重複用更寬的窗：那是回彈的完整特徵。
            float previous = lastAcceptedVelocity[channelIndex, note];
            if (previous > 0f && velocity < previous * ChatterSofterShare)
                window = System.Math.Max(window, Mathf.Max(0f, softRepeatWindowMs) * 0.001);

            if (sinceLast >= 0.0 && sinceLast < window)
            {
                MistouchProbe.RejectedByBrush();
                ReportDropped(note, EatenInputProbe.Reason.InputChatter);
                return;
            }
        }

        if (LooksLikeBrush(channelIndex, note, velocity, timestamp))
        {
            MistouchProbe.RejectedByBrush();
            ReportDropped(note, EatenInputProbe.Reason.InputBrush);
            return;
        }

        // 這一下比某個還扣著的更響、而且就在旁邊——那個被扣著的就是擦碰。
        // 這是往回看抓不到的那個方向，靠的就是「先扣住、等正主來揭曉」。
        DropHeldBrushesAgainst(channelIndex, note, velocity, timestamp);

        if (ShouldHoldForLookahead(velocity))
        {
            heldNoteOns.Add(new HeldNoteOn
            {
                Channel = channel,
                ChannelIndex = channelIndex,
                Note = note,
                Velocity = velocity,
                Timestamp = timestamp,
            });
            return;
        }

        DispatchNoteOn(channelIndex, note, velocity, timestamp);
    }

    /// <summary>這一下要不要先扣住，等一下再決定。</summary>
    /// <remarks>
    /// 門檻用絕對力度而不是「比鄰鍵輕多少」：扣留的當下還不知道鄰鍵是什麼，
    /// 那正是問題所在。值該設多少看 <c>[Mistouch]</c> 的 vel 直方圖——設在
    /// 誤觸分布的上緣，真正要彈的音就幾乎不會被扣到。
    /// </remarks>
    private static bool ShouldHoldForLookahead(float velocity)
    {
        SettingsManager settings = SettingsManager.Instance;
        if (settings == null || !settings.BrushRejectionEnabled) return false;
        if (settings.BrushLookaheadMs <= 0f) return false;
        return velocity * 127f < settings.BrushLookaheadMaxVelocity;
    }

    /// <summary>丟掉被這一下揭穿成擦碰的扣留項。</summary>
    private void DropHeldBrushesAgainst(int channelIndex, int note, float velocity, double timestamp)
    {
        if (heldNoteOns.Count == 0) return;
        SettingsManager settings = SettingsManager.Instance;
        if (settings == null) return;

        float ratio = settings.BrushVelocityRatio;
        double window = settings.BrushWindowMs * 0.001;
        int reach = settings.BrushSemitones;

        for (int i = heldNoteOns.Count - 1; i >= 0; i--)
        {
            HeldNoteOn held = heldNoteOns[i];
            if (held.ChannelIndex != channelIndex) continue;
            if (timestamp - held.Timestamp > window) continue;
            if (Mathf.Abs(held.Note - note) > reach) continue;
            if (held.Velocity >= velocity * ratio) continue;
            // 同一條絕對條件。少了這裡的話，被扣留的真音會在下一顆重音到達時
            // 被追溯著刪掉 —— 症狀和即時誤殺一模一樣，但更難查。
            if (IsDeliberateTouch(held.Velocity)) continue;
            // 同一條夾心條件：兩側都有音的那一鍵不是擦到的。
            if (HasAcceptedNeighboursBothSides(channelIndex, held.Note, reach,
                    held.Timestamp, window)) continue;

            heldNoteOns.RemoveAt(i);
            MistouchProbe.RejectedByBrush();
            ReportDropped(held.Note, EatenInputProbe.Reason.InputHeldBrush);
        }
    }

    /// <summary>診斷用：告訴判定端這一下被輸入端丟掉了，讓它追蹤本來會打到的音符。</summary>
    private static void ReportDropped(int midiNote, EatenInputProbe.Reason reason)
    {
        var jm = Judgment.JudgmentManager.Instance;
        if (jm == null) return;
        jm.ReportInputDropped(MidiNoteToKeyMapper.GetKeyIndex(midiNote), reason);
    }

    /// <summary>把等夠久、沒有被揭穿的扣留項放行。</summary>
    private void FlushExpiredHeldNoteOns()
    {
        if (heldNoteOns.Count == 0) return;
        SettingsManager settings = SettingsManager.Instance;
        double lookahead = (settings != null ? settings.BrushLookaheadMs : 0f) * 0.001;
        double now = GetRealtimeSinceStartup();

        for (int i = 0; i < heldNoteOns.Count; i++)
        {
            HeldNoteOn held = heldNoteOns[i];
            if (now - held.Timestamp < lookahead) continue;
            heldNoteOns.RemoveAt(i);
            i--;
            DispatchNoteOn(held.ChannelIndex, held.Note, held.Velocity, held.Timestamp);
        }
    }

    /// <summary>放行一個扣留項，不等它到期。</summary>
    private bool ReleaseHeldNoteOn(int channelIndex, int note)
    {
        for (int i = 0; i < heldNoteOns.Count; i++)
        {
            HeldNoteOn held = heldNoteOns[i];
            if (held.ChannelIndex != channelIndex || held.Note != note) continue;
            heldNoteOns.RemoveAt(i);
            DispatchNoteOn(held.ChannelIndex, held.Note, held.Velocity, held.Timestamp);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 真的把一個 note-on 交出去。狀態記錄全部在這裡，被丟掉的擦碰因此不會
    /// 留下任何痕跡（不會佔著 notePressed，也不會被當成鄰鍵去擋別人）。
    /// </summary>
    private void DispatchNoteOn(int channelIndex, int note, float velocity, double timestamp)
    {
        int lane = MidiNoteToKeyMapper.GetKeyIndex(note);
        bool wasPressed = notePressed[channelIndex, note];

        MistouchProbe.Accepted(note, velocity, timestamp);
        lastAcceptedRealtime[channelIndex, note] = timestamp;
        lastAcceptedVelocity[channelIndex, note] = velocity;

        notePressed[channelIndex, note] = true;
        if (!wasPressed && lane >= 0 && lane < LaneCount) lanePressCounts[lane]++;
        noteVelocities[channelIndex, note] = velocity;
        noteLanes[channelIndex, note] = lane;
        lastNoteOnRealtime[channelIndex, note] = timestamp;

        OnMidiNoteOnTimed?.Invoke(note, lane, velocity, timestamp, NextInputEventId());
        OnMidiNoteOn?.Invoke(note, velocity);
    }

    private void HandleNativeNoteOff(MidiChannel channel, int note)
    {
        if (!ShouldAcceptMidi() || note < 0 || note >= MidiNoteCount) return;

        int channelIndex = GetChannelIndex(channel);
        if (channelIndex < 0) return;

        // 很短的觸鍵可能整個發生在扣留期之內。這時候不能直接丟掉 note-off——
        // 那顆音會變成永遠按著。先把它放行，再照常處理放開。
        if (!notePressed[channelIndex, note]) ReleaseHeldNoteOn(channelIndex, note);
        if (!notePressed[channelIndex, note]) return;
        float velocity = noteVelocities[channelIndex, note];
        int lane = noteLanes[channelIndex, note];
        notePressed[channelIndex, note] = false;
        if (lane >= 0 && lane < LaneCount && lanePressCounts[lane] > 0)
            lanePressCounts[lane]--;
        noteVelocities[channelIndex, note] = 0f;
        noteLanes[channelIndex, note] = -1;

        double timestamp = GetHardwareEventTime(out _);
        OnMidiNoteOffTimed?.Invoke(note, lane, velocity, timestamp, 0);
        OnMidiNoteOff?.Invoke(note, velocity);
    }

    private void ResetMidiState()
    {
        // Release aggregate lane ownership before clearing it. This manager
        // updates before GameManager, so silently clearing here would leave
        // Hold state pressed when the player changes input mode or a manager
        // is disabled during gameplay.
        double releaseTimestamp = GetRealtimeSinceStartup();
        for (int channel = 0; channel < MidiChannelCount; channel++)
        {
            for (int note = 0; note < MidiNoteCount; note++)
            {
                if (!notePressed[channel, note]) continue;
                float velocity = noteVelocities[channel, note];
                notePressed[channel, note] = false;
                int lane = noteLanes[channel, note];
                if (lane >= 0 && lane < LaneCount && lanePressCounts[lane] > 0)
                    lanePressCounts[lane]--;
                OnMidiNoteOffTimed?.Invoke(note, lane, velocity, releaseTimestamp, 0);
                OnMidiNoteOff?.Invoke(note, velocity);
            }
        }
        // 扣留中的直接丟掉，不要放行：這裡是換輸入模式或停止遊玩，那些音已經
        // 沒有意義了，放行只會在剛切換過去的狀態上留下一顆孤兒。
        heldNoteOns.Clear();
        System.Array.Clear(notePressed, 0, notePressed.Length);
        System.Array.Clear(noteVelocities, 0, noteVelocities.Length);
        System.Array.Clear(noteLanes, 0, noteLanes.Length);
        System.Array.Clear(lastNoteOnRealtime, 0, lastNoteOnRealtime.Length);
        System.Array.Clear(lanePressCounts, 0, lanePressCounts.Length);
    }

    private static double GetRealtimeSinceStartup()
    {
#if UNITY_2020_2_OR_NEWER
        return Time.realtimeSinceStartupAsDouble;
#else
        return Time.realtimeSinceStartup;
#endif
    }

    private long NextInputEventId()
    {
        inputEventSequence++;
        if (inputEventSequence <= 0 || inputEventSequence > (long.MaxValue >> 1))
            inputEventSequence = 1;
        // Even ids are reserved for MIDI; keyboard uses odd ids.
        return inputEventSequence << 1;
    }

    private static int GetChannelIndex(MidiChannel channel)
    {
        int index = (int)channel;
        return index >= 0 && index < MidiChannelCount ? index : -1;
    }

    private static double GetHardwareEventTime(out bool usedNativeTimestamp)
    {
        usedNativeTimestamp = false;
        // The Windows native plugin captures QueryPerformanceCounter in the
        // MIDI callback. Stopwatch uses that same clock, letting us preserve
        // timing even when several events are drained in one Unity frame.
        try
        {
            ulong nativeTimestamp = MidiMaster.lastEventTimestampQpc;
            if (nativeTimestamp > 0UL)
            {
                long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                long eventTicks = unchecked((long)nativeTimestamp);
                double ageSeconds = (nowTicks - eventTicks) /
                    (double)System.Diagnostics.Stopwatch.Frequency;
                if (ageSeconds >= 0.0 && ageSeconds <= 1.0)
                {
                    usedNativeTimestamp = true;
                    return GetRealtimeSinceStartup() - ageSeconds;
                }
            }
        }
        catch { }

        // Without a native timestamp we only know when Unity received the event.  Do not
        // guess "half a frame ago": that guess creates a permanent FAST bias whose size
        // changes with frame rate.
        return GetRealtimeSinceStartup();
    }

    public bool IsLanePressed(int lane)
    {
        if (lane < 0 || lane >= lanePressCounts.Length) return false;
        return lanePressCounts[lane] > 0;
    }
}
