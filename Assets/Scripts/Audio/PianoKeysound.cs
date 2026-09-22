using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns judged input into piano voices, following the current
/// <see cref="PianoSoundMode"/>.
/// </summary>
/// <remarks>
/// Velocity for a matched note is read from the note the judgment picked, never
/// from a running counter. That distinction matters: a queue-style "nth hit gets
/// the nth velocity" would smear every later note's dynamics after a single miss,
/// whereas a note's velocity belongs to the note and survives any mistake.
///
/// Hardcore mode instead needs what the player's own key did, which the judgment
/// pipeline does not carry — <see cref="JudgmentManager.ProcessButtonPress"/> only
/// takes a lane. Rather than thread velocity through every layer, the input
/// adapters record the keystroke per lane just before judging, and the hit site
/// reads it back by lane.
/// </remarks>
public static class PianoKeysound
{
    /// <summary>Chart velocity fallback when a note carries no restored velocity.</summary>
    private const int DefaultChartVelocity = 80;
    /// <summary>Stand-in velocity when the device cannot report one (computer keyboard).</summary>
    private const int KeyboardVelocity = 96;
    private const int LaneCount = 28;

    private struct LaneInput
    {
        public int MidiPitch;     // -1 when the device has no pitch (keyboard)
        public float Velocity;    // 0-1, negative when unknown
        public bool Auto;         // 自動演奏（或教學示範）替這條鍵道按的，不是玩家的手
    }

    private static readonly LaneInput[] laneInputs = CreateLaneInputs();

    /// <summary>
    /// Sounding voices keyed by the MIDI key that started them.
    /// </summary>
    /// <remarks>
    /// Keyed by pitch rather than by lane because that is what a note-off
    /// carries, and because a lane is not one key: the 88 keys fold onto 28
    /// lanes, so roughly three piano keys share each one. A per-lane map holds
    /// a single voice, so playing two of a lane's keys together — or any run
    /// inside one lane — orphans every voice but the last, and orphaned voices
    /// ring until they time out and starve the pool.
    /// </remarks>
    private static readonly Dictionary<int, long> pitchVoices = new Dictionary<int, long>(64);

    /// <summary>Fallback for devices that report no pitch, keyed by lane.</summary>
    private static readonly Dictionary<int, long> laneVoices = new Dictionary<int, long>(LaneCount);

    /// <summary>
    /// The chart note each pooled NoteController last struck, so one note can
    /// never be struck twice.
    /// </summary>
    /// <remarks>
    /// The hook point (<see cref="HitEffectRouter.Play"/>) is a visual-effects
    /// funnel, not a note-attack event: slide and trill contact pulses through it
    /// every 75 ms, and hold start/recovery re-enter it for a note already
    /// sounding. Left unchecked a single two-second slide spawns twenty-odd
    /// voices of the same pitch, the pedal keeps them all alive, and the pool is
    /// exhausted within seconds — which sounds like the piano cutting out.
    ///
    /// Keyed by start time rather than a bare flag because NoteControllers are
    /// pooled: the same object comes back as a different note later.
    /// </remarks>
    private static readonly Dictionary<NoteController, int> lastStruckStartTime =
        new Dictionary<NoteController, int>(256);


    private static LaneInput[] CreateLaneInputs()
    {
        var inputs = new LaneInput[LaneCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = new LaneInput { MidiPitch = -1, Velocity = -1f };
        return inputs;
    }

    // Where the trigger chain stops, counted since the last diagnostic report.
    // Every early return below has its own counter, because "no sound" looks
    // identical from outside whether the judgment never called in, the note had
    // no pitch, or the sample bank turned the request down.
    private static int statCalls, statInactive, statNoPitch, statDeduped, statNoVoice, statPlayed;
    /// <summary>敲到寄主之後被排進去的隱藏音數量，用來確認它們真的有響。</summary>
    private static int statHiddenQueued;

    /// <summary>Reads and resets the trigger counters, for diagnostics.</summary>
    public static string TakeStats()
    {
        string report = $"calls={statCalls} played={statPlayed} " +
                        $"hidden={statHiddenQueued} " +
                        $"(inactive={statInactive} noPitch={statNoPitch} " +
                        $"dedup={statDeduped} noVoice={statNoVoice})";
        statCalls = statInactive = statNoPitch = statDeduped = statNoVoice = statPlayed = 0;
        statHiddenQueued = 0;
        return report;
    }

    /// <summary>診斷用：有幾顆寄主被敲到、排進去幾顆隱藏音。</summary>
    internal static int statHiddenHosts;

    private static SettingsManager Settings => SettingsManager.Instance;

    private static bool Active
    {
        get
        {
            SettingsManager settings = Settings;
            return settings != null && settings.UsesSynthesisedPiano;
        }
    }

    /// <summary>
    /// Remembers what the player's key actually did, for the judgment that is
    /// about to run on this lane.
    /// </summary>
    /// <param name="midiPitch">The struck piano key, or -1 from a device without pitch.</param>
    /// <param name="velocity">0-1 from the device, or negative when unknown.</param>
    public static void RecordInput(int lane, int midiPitch, float velocity)
    {
        RecordInput(lane, midiPitch, velocity, false);
    }

    /// <param name="auto">
    /// 自動演奏替玩家按的。它的力度就是譜面要的力度，**不能**進玩家手勁的平均，
    /// 之後也不能再經過手勁換算——換算是把「這個人的輕重」對齊到譜面的尺，而自動
    /// 演奏本來就在譜面的尺上。以前兩件事都做了：手勁基準被譜面力度拖著慢慢跑，
    /// 再拿這個跑到一半的基準去換算譜面力度，強音被換成中音、弱音被換成中音，
    /// 演奏會模式下自動演奏的 PRECISE 就掉了一大截。
    /// </param>
    public static void RecordInput(int lane, int midiPitch, float velocity, bool auto)
    {
        if (lane < 0 || lane >= LaneCount) return;
        laneInputs[lane] = new LaneInput { MidiPitch = midiPitch, Velocity = velocity, Auto = auto };
        // 這裡是**唯一**一次真正的觸鍵。發聲和判定之後都會再看這一下，兩邊都記
        // 的話那個平均會用兩倍的速度跑，而且跑多快取決於這一下有沒有被判定、有
        // 沒有被去重 —— 同樣的演奏會得到不一樣的基準。
        if (!auto)
        {
            try { PlayerTouch.Observe(velocity); } catch { }
        }

        // 判定線上的按鍵提示。**按下去就顯示，不等判定** —— 玩家需要知道的第一
        // 件事是「機器收到我了」，那和「有沒有打中」是兩回事，發生的時間也不同
        // （判定要等一個操作幀）。
        try { Effects.LaneStrikeCue.Press(lane); } catch { }
    }

    /// <summary>
    /// 讀回這條 lane 最後記下的那一下按鍵，給診斷用。
    /// </summary>
    /// <remarks>
    /// 判定端只認得 lane，但要描述一次誤觸得知道實際的琴鍵和力度——那兩個值
    /// 只有輸入端知道，而且已經記在這裡了（<see cref="RecordInput"/>）。
    /// </remarks>
    public static void DescribeLaneInput(int lane, out int midiPitch, out float velocity)
    {
        DescribeLaneInput(lane, out midiPitch, out velocity, out _);
    }

    public static void DescribeLaneInput(int lane, out int midiPitch, out float velocity, out bool auto)
    {
        if (lane < 0 || lane >= LaneCount) { midiPitch = -1; velocity = -1f; auto = false; return; }
        midiPitch = laneInputs[lane].MidiPitch;
        velocity = laneInputs[lane].Velocity;
        auto = laneInputs[lane].Auto;
    }

    /// <summary>Lifts the voice a specific MIDI key started.</summary>
    /// <remarks>
    /// Must be called for every note-off, including ones whose lane still has
    /// other keys held down — the piano string belongs to the key, not the lane.
    /// </remarks>
    public static void ReleasePitch(int midiPitch)
    {
        if (!pitchVoices.TryGetValue(midiPitch, out long handle)) return;
        pitchVoices.Remove(midiPitch);
        ReleaseHandle(handle);
    }

    /// <summary>Lifts whatever voice this lane started, for devices without pitch.</summary>
    public static void ReleaseLane(int lane)
    {
        if (!laneVoices.TryGetValue(lane, out long handle)) return;
        laneVoices.Remove(lane);
        ReleaseHandle(handle);
    }

    private static void ReleaseHandle(long handle)
    {
        // Only Hardcore follows the player's key-up; the other mode already
        // scheduled its release from the chart's gate time.
        SettingsManager settings = Settings;
        if (settings == null || !settings.UsesPlayerTouch) return;
        PianoVoiceManager.Instance?.NoteOff(handle);
    }

    public static void ClearAllLanes()
    {
        for (int i = 0; i < LaneCount; i++)
        {
            laneDeferring[i] = false;
            deferredStrikes[i] = default;
        }
        pitchVoices.Clear();
        laneVoices.Clear();
        lastStruckStartTime.Clear();
        PianoVoiceManager.Instance?.ClearTrills();
        for (int i = 0; i < laneInputs.Length; i++)
            laneInputs[i] = new LaneInput { MidiPitch = -1, Velocity = -1f };
    }

    // ── 暫扣一次擊弦 ─────────────────────────────────────────────────
    //
    // 「只讓聲音等幀」模式用的。判定、計分、畫面都可以立刻送出並在需要時事後
    // 修正，唯獨**聲音發出去就收不回**——所以只有它需要等到幀結算，確認這一下
    // 沒有被規則判成失效之後才發。
    //
    // 以 lane 為單位而不是全域開關：全域一開，自動演奏、Hold 恢復那些路徑的
    // 擊弦也會被吞進來，而它們沒有人負責放行，聲音就永久消失了。
    private struct DeferredStrike
    {
        public NoteController Note;
        public bool Pending;
    }

    private static readonly DeferredStrike[] deferredStrikes = new DeferredStrike[LaneCount];
    private static readonly bool[] laneDeferring = new bool[LaneCount];

    /// <summary>接下來這條 lane 的擊弦先扣住，不要發聲。</summary>
    public static void DeferLane(int lane)
    {
        if ((uint)lane >= LaneCount) return;
        laneDeferring[lane] = true;
        deferredStrikes[lane] = default;
    }

    /// <summary>停止扣留，但還不決定扣住的那一下要不要發。</summary>
    public static void StopDeferring(int lane)
    {
        if ((uint)lane >= LaneCount) return;
        laneDeferring[lane] = false;
    }

    /// <summary>放行扣住的那一下。</summary>
    public static void CommitDeferred(int lane)
    {
        if ((uint)lane >= LaneCount) return;
        DeferredStrike strike = deferredStrikes[lane];
        deferredStrikes[lane] = default;
        laneDeferring[lane] = false;
        if (!strike.Pending || strike.Note == null) return;
        PlayJudgedNote(strike.Note, lane);
    }

    /// <summary>丟掉扣住的那一下——這一鍵被規則判成失效了。</summary>
    public static void DiscardDeferred(int lane)
    {
        if ((uint)lane >= LaneCount) return;
        deferredStrikes[lane] = default;
        laneDeferring[lane] = false;
    }

    /// <summary>Called when a keypress is matched to a note.</summary>
    public static void PlayJudgedNote(NoteController note, int lane)
    {
        // 扣留中：記下來就走，等幀結算再決定。顫音不扣——它靠的是連續的接觸
        // 脈衝，扣住任何一個都會讓它斷掉，而顫音本來就不是誤觸的形狀。
        if ((uint)lane < LaneCount && laneDeferring[lane] && note != null && !note.IsTrill)
        {
            if (!deferredStrikes[lane].Pending)
                deferredStrikes[lane] = new DeferredStrike { Note = note, Pending = true };
            return;
        }

        statCalls++;
        if (note == null || note.NoteData == null) { statInactive++; return; }
        if (!Active) { statInactive++; return; }

        SettingsManager settings = Settings;
        var voices = PianoVoiceManager.EnsureCreated();
        if (voices == null || !voices.IsReady) { statNoVoice++; return; }

        NoteData data = note.NoteData;
        int pitch = PianoVisualLayout.ResolveMidiPitch(data);
        if (pitch < 0) { statNoPitch++; return; }

        // A trill is not one strike and not a fixed repeat either — the chart
        // wrote out every stroke. Hand the schedule to the voice manager, which
        // plays it in song time; this call only says "contact is still there".
        if (note.IsTrill && data.subNotes != null && data.subNotes.Count > 1)
        {
            voices.RefreshTrill(note, data.subNotes, settings.PianoVoiceVolume,
                settings.UsesPlayerTouch ? ResolvePlayerVelocity(lane) : 0);
            statPlayed++;
            return;
        }

        // One strike per chart note, however many times the effect layer fires.
        if (lastStruckStartTime.TryGetValue(note, out int struckAt) && struckAt == data.startTime)
        {
            statDeduped++;
            return;
        }
        // Note objects are pooled, so this stays small; the cap only guards
        // against a chart that destroys and recreates notes instead.
        if (lastStruckStartTime.Count > 1024) lastStruckStartTime.Clear();
        lastStruckStartTime[note] = data.startTime;

        bool playerTouch = settings.UsesPlayerTouch;
        int velocity = playerTouch
            ? ResolvePlayerVelocity(lane)
            : ResolveChartVelocity(data);

        // Performance follows the chart's own key-press length; Hardcore waits
        // for the player to let go, so it schedules no gate at all.
        double gateOff = -1d;
        if (!playerTouch)
        {
            int gate = data.gateTime > 0 ? data.gateTime : Mathf.Max(0, data.endTime - data.startTime);
            // 最短按鍵時間。制音器**不可能**在琴鍵回到頂之前落下，而琴鍵回彈加上
            // 制音器行程本來就要 100ms 以上——所以再怎麼短的觸鍵，弦都會先響這麼久
            // 才開始被壓。原本的下限是 30ms，等於允許物理上做不到的極短音。
            //
            // 同音高在這之內被重擊不會殘留：RetriggerDampMs 會把舊的那顆壓掉。
            gateOff = data.startTime + Mathf.Max(MinKeyDownMs, gate);
        }

        float strikeDelay = ResolveStrikeDelaySeconds(settings, voices, data);
        long handle = voices.NoteOn(pitch, velocity, gateOff, settings.PianoVoiceVolume,
            strikeDelay);
        if (handle == 0) { statNoVoice++; return; }
        statPlayed++;
        TrackVoice(handle, lane);

        // 這顆鍵身上藏了別的音的話，一起把它們排進去。低難度的譜是把和絃裡
        // 多餘的音藏進寄主的 sub_note —— 玩家少按幾下，和絃卻該完整地響
        // （官方 normal 譜有一半以上的音是這樣藏起來的）。每一顆照它自己的
        // start_timing_msec 排進歌曲時間，不是全部擠在按鍵這一刻。
        if (settings.PlayHiddenSubNotes && data.subNotes != null && data.subNotes.Count > 1)
        {
            statHiddenHosts++;
            statHiddenQueued += voices.ScheduleHiddenNotes(
                data.subNotes, pitch, data.startTime, settings.PianoVoiceVolume,
                playerTouch ? velocity : 0, strikeDelay * 1000d);
        }
    }

    /// <summary>
    /// How long to hold a strike back so it lands near the beat instead of on
    /// the player's finger.
    /// </summary>
    /// <remarks>
    /// Performance mode is meant to sound like the piece, played by the player —
    /// not like the piece dragged out of shape by the player's timing. Sounding
    /// every note exactly when the key went down puts the full judgment window
    /// into the music: a note 80 ms early is a wrong note in a way a slightly
    /// soft one never is, because rhythm is what carries the piece.
    ///
    /// So the deviation is scaled rather than reproduced. The whole Good window
    /// maps onto <see cref="PerformanceSpreadMs"/> around the chart's own time,
    /// which keeps the *order* and the relative sense of rushing or dragging
    /// while shrinking it to something musical. Rushing still sounds like
    /// rushing; it just no longer breaks the bar.
    ///
    /// Only the early side can be corrected. A late press has already happened
    /// by the time we hear about it, and nothing can be scheduled into the past,
    /// so it sounds where it fell. That is the cheap half of the trade: it costs
    /// no latency at all, and the measured distribution is early-dominant.
    ///
    /// Hardcore is deliberately exempt — reproducing the player's touch exactly,
    /// timing included, is the entire point of that mode.
    /// </remarks>
    private static float ResolveStrikeDelaySeconds(SettingsManager settings,
        PianoVoiceManager voices, NoteData data)
    {
        if (settings == null || voices == null || data == null) return 0f;
        if (settings.CurrentPianoSoundMode != PianoSoundMode.Performance) return 0f;

        float spread = settings.PianoPerformanceSpreadMs;
        if (spread <= 0f) return 0f;

        float window = 150f;
        try
        {
            var judge = Judgment.JudgmentManager.Instance;
            if (judge != null && judge.goodMs > 0) window = judge.goodMs;
        }
        catch { }
        if (window <= spread) return 0f;

        double songMs = voices.SongMs;
        if (songMs <= 0d) return 0f;

        float actual = (float)(songMs - data.startTime);   // negative when early
        if (actual >= 0f) return 0f;                       // already late, nothing to wait for
        if (actual < -window) return 0f;                   // outside the window this maps

        float mapped = actual * (spread / window);
        return Mathf.Max(0f, (mapped - actual) * 0.001f);
    }

    /// <summary>
    /// Remembers a voice under the key that will report its release.
    /// </summary>
    /// <remarks>
    /// Registered against the pitch the player physically pressed, not the
    /// chart's pitch: those differ whenever a lane's several keys do not all
    /// carry the same note, and the note-off will name the key that was pressed.
    /// </remarks>
    private static void TrackVoice(long handle, int lane)
    {
        int inputPitch = lane >= 0 && lane < LaneCount ? laneInputs[lane].MidiPitch : -1;
        if (inputPitch >= 0)
        {
            // Striking a key that is still ringing stops its own string, pedal or
            // no pedal and whatever the mode — that is what the hammer does. The
            // ordinary release path would defer to the pedal here and let fast
            // repeats stack into each other.
            if (pitchVoices.TryGetValue(inputPitch, out long previous))
            {
                PianoVoiceManager.Instance?.DampForRetrigger(previous);
            }
            pitchVoices[inputPitch] = handle;
        }
        else if (lane >= 0)
        {
            laneVoices[lane] = handle;
        }
    }

    private static int ResolvePlayerVelocity(int lane)
    {
        if (lane < 0 || lane >= LaneCount) return KeyboardVelocity;
        float velocity = laneInputs[lane].Velocity;
        if (velocity < 0f) return KeyboardVelocity;

        // 換算到譜面的尺上再去選取樣層。**和力度評分用的是同一個換算** ——
        // 兩邊對同一下觸鍵必須有同一個理解，否則會出現「聽起來是重擊、評分卻說
        // 你彈輕了」，而那種矛盾無法靠練習修正。
        int raw = Mathf.Clamp(Mathf.RoundToInt(velocity * 127f), 1, 127);
        // 自動演奏的力度本來就是譜面的力度，不做手勁換算。
        return laneInputs[lane].Auto ? raw : PlayerTouch.ToChartScale(raw);
    }

    /// <summary>
    /// The velocity restored from the source MIDI. Chords keep one sub-note per
    /// pitch, so the first sub-note is this note's own strike.
    /// </summary>
    /// <summary>琴鍵回彈加制音器行程的下限，短於這個的觸鍵在物理上不存在。</summary>
    private const int MinKeyDownMs = 150;

    /// <summary>
    /// The velocity this note is meant to be struck at, for anything that wants
    /// to *show* it rather than sound it.
    /// </summary>
    /// <remarks>
    /// Deliberately the same call the sound uses. A note drawn as loud that then
    /// sounds soft is worse than not marking it at all, and two separate readings
    /// of "how hard is this note" would drift apart the first time either side
    /// changed its fallback.
    /// </remarks>
    public static int ChartVelocity(NoteData data)
    {
        return ResolveChartVelocity(data);
    }

    /// <summary>What a note carries when nothing restored a velocity for it.</summary>
    public static int NeutralVelocity => DefaultChartVelocity;

    private static int ResolveChartVelocity(NoteData data)
    {
        // 音符自己的力度優先。這是還原工具寫進去的那一個，也是玩家實際按下的
        // 那一顆；subNotes 是和弦或顫音的其他聲部，只有主音沒有力度時才拿它頂。
        if (data.velocity > 0) return Expand(data.velocity);

        if (data.subNotes != null)
        {
            for (int i = 0; i < data.subNotes.Count; i++)
            {
                SubNoteData sub = data.subNotes[i];
                if (sub != null && sub.velocity > 0) return Mathf.Clamp(sub.velocity, 1, 127);
            }
        }
        return DefaultChartVelocity;
    }

    /// <summary>
    /// Opens up the chart's dynamics by the player's設定 factor.
    /// </summary>
    /// <remarks>
    /// Only the sound goes through this. The dynamics wash and the note shells
    /// are drawn from the raw velocities, because the bands they are compared
    /// against were measured from raw velocities too -- expanding one side and
    /// not the other would move every note relative to its own marking. The
    /// expansion is monotonic, so what is drawn louder is still played louder.
    /// </remarks>
    private static int Expand(int velocity)
    {
        velocity = Mathf.Clamp(velocity, 1, 127);
        var settings = SettingsManager.Instance;
        float factor = settings != null ? settings.PianoDynamicExpansion : 1f;
        if (factor <= 1.001f || !VelocityBands.Measured) return velocity;

        float middle = VelocityBands.Middle;
        return Mathf.Clamp(Mathf.RoundToInt(middle + (velocity - middle) * factor), 1, 127);
    }

    private static double CurrentSongMs()
    {
        try
        {
            Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
            // Same clock the voice manager and the judgment use, so a wrong-key
            // gate lands where the notes around it do.
            if (conductor != null) return conductor.effectiveSongPosition;
        }
        catch { }
        return Time.unscaledTimeAsDouble * 1000d;
    }
}
