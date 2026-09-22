using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays the sampled piano as a keysound: one voice per struck note, released by
/// the player's key and the chart's sustain pedal rather than by the sample length.
/// </summary>
/// <remarks>
/// The samples are rendered with no note-off so they decay naturally; deciding
/// when a note stops is this class's job. Two things end a voice:
///
/// * the key lifts (or its chart gate time elapses), and
/// * the pedal is up.
///
/// While the pedal is down a released key keeps ringing — that is the whole point
/// of restoring CC64 — and every voice waiting on the pedal damps together the
/// moment it lifts. syuten holds the pedal 97.6% of the time, so this path is the
/// normal case rather than an edge case.
/// </remarks>
[DefaultExecutionOrder(-30)]
public class PianoVoiceManager : MonoBehaviour
{
    public static PianoVoiceManager Instance { get; private set; }

    // Unity renders only m_RealVoiceCount sources at once and virtualises the
    // rest by priority, where a higher number means less important. The ordering
    // below is the whole point: the song must never drop, but the piano is the
    // instrument being played and has to outrank the hit clicks — ranking it
    // last is what made it disappear under dense playing.

    /// <summary>Priority for song playback, which must never be virtualised.</summary>
    public const int MusicPriority = 0;

    /// <summary>Priority for piano voices: below the song, above mere effects.</summary>
    public const int VoicePriority = 64;

    /// <summary>Priority for hit clicks and other effects — the first to go.</summary>
    public const int EffectPriority = 200;

    /// <summary>
    /// Real voices the audio engine must provide before the piano will fit.
    /// </summary>
    /// <remarks>
    /// Sized by simulating every restored chart rather than by guessing. Peak
    /// demand across 59 charts runs to 150 simultaneous voices — and the worst
    /// offenders are not the fastest ones: Abiogenesis needs 149 at only 11
    /// notes a second because its pedal is held for 7.6 s at a time, while
    /// Rrhar'il at 29 notes a second needs 86 because its pedalling is short.
    /// Pedal length drives this far more than note density. Add the song and the
    /// hit clicks and 192 covers everything measured.
    /// </remarks>
    private const int RequiredRealVoices = 192;

    [Header("Voices")]
    [SerializeField, Tooltip("Simultaneously ringing notes before the quietest is stolen.")]
    // A pedalled piano genuinely holds this many strings at once in dense music.
    // Anything much smaller means stealing notes the pedal was asked to sustain,
    // which is heard as the piano cutting out under fast playing. Measured peak
    // demand across the restored charts is 150; the pool is sized above that so
    // the common case never steals at all.
    private int voiceCount = 160;
    [SerializeField, Range(0f, 2f)] private float masterVolume = 1f;

    [Header("Release")]
    // 140/180 太短。制音器落下不是把聲音關掉，弦的能量要被氈子吸掉，中音區聽得到
    // 的尾巴大約 200~400ms（一般鋼琴音源用的也是這個量級）。舊值讓一顆 109ms 的
    // 音只響 249ms，聽起來每顆都被切斷。拉長實測完全不影響聲音預算：尖峰中位仍是
    // 66、最大仍是 150，因為尖峰是由踏板壓著的段落決定的，不是由放開速度。
    [SerializeField, Min(1f), Tooltip("Damper fall time when a key lifts with the pedal up.")]
    private float keyReleaseMs = 240f;
    [SerializeField, Min(1f), Tooltip("Damper fall time when the pedal lifts under held notes.")]
    private float pedalReleaseMs = 260f;
    [SerializeField, Min(1f)]
    [Tooltip("A voice is damped after this long no matter what, so nothing can accumulate.")]
    private float maxVoiceSeconds = 8f;
    [SerializeField, Min(0.1f)]
    [Tooltip("Roughly how long a held string takes to lose half its loudness. Only used to rank voices for stealing.")]
    private float decayHalfLifeSeconds = 2f;

    // ── 制音器的物理 ──────────────────────────────────────────────────
    // 放開琴鍵不是把聲音關掉。制音器是一塊要落下、壓住、吸收能量的氈子，而它的
    // 大小和重量隨音域變化很大：
    //
    //  * **最高的那一段沒有制音器。** 平台鋼琴的制音器只做到大約 MIDI 88，再上去
    //    的弦短、能量小、自己就衰減得很快，裝制音器沒有意義。那些音放開琴鍵照樣
    //    響到自然衰減，**踏板對它們也完全沒有作用**。
    //  * **低音的制音器又大又重**，落下之後要更久才把弦壓死；高音的又小又輕。
    //
    // 沒有這一段的話，每個音的長度就等於「按了多久 + 一個固定的淡出」，而譜面的
    // 音長是照 MIDI 的按鍵長度來的（中位 109~305ms），聽起來就是每顆音都被切掉。
    [Header("Damper physics")]
    [SerializeField, Range(60, 108)]
    [Tooltip("Above this pitch a grand has no dampers at all: the key release and the pedal stop mattering.")]
    private int noDamperPitch = 88;
    [SerializeField, Range(21, 96)]
    [Tooltip("Dampers start losing their grip above this pitch, reaching none at noDamperPitch.")]
    private int damperTaperPitch = 76;
    [SerializeField, Range(1f, 4f)]
    [Tooltip("How much slower the heaviest bass damper is than a middle-register one.")]
    private float bassDamperMultiplier = 2.2f;
    [SerializeField, Range(1f, 6f)]
    [Tooltip("How much slower a damper is just below the no-damper region.")]
    private float trebleDamperMultiplier = 3f;
    [SerializeField, Range(0.5f, 8f)]
    [Tooltip("How long an undamped top-register string is allowed to ring on its own.")]
    private float undampedRingSeconds = 3f;

    private PianoSampleManifest manifest;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
    private Voice[] voices;
    private PedalTimeline pedal = PedalTimeline.Empty;
    /// <summary>
    /// Invented pedalling, used only when the chart itself has none. Kept apart
    /// from <see cref="pedal"/> so a real pedal is never quietly replaced by a
    /// guess, and so switching the setting takes effect without reloading.
    /// </summary>
    private PedalTimeline substitutePedal = PedalTimeline.Empty;
    private System.Collections.Generic.List<int> substituteBeats;
    private int substituteBeatsPerBar = 4;
    private PianoAutoPedal substituteMode = PianoAutoPedal.Off;
    private System.Collections.Generic.List<NoteData> substituteNotes;
    private bool pedalWasDown;
    private double lastPedalPollMs = double.NegativeInfinity;
    private bool warnedPoolExhausted;
    private int noteOnsSinceLastReport;

    /// <summary>
    /// How long a trill keeps playing after its last contact pulse. Long enough
    /// to bridge the gap between pulses, short enough that letting go stops it.
    /// </summary>
    private const float TrillContactTimeoutSeconds = 0.2f;

    /// <summary>Damper fall when a key is struck again while still ringing.</summary>
    /// <remarks>
    /// Short, because on a real piano the returning hammer stops the string almost
    /// at once — but not instant, which would click.
    /// </remarks>
    private const float RetriggerDampMs = 35f;

    private sealed class TrillSchedule
    {
        public System.Collections.Generic.List<SubNoteData> Subs;
        public int NextIndex;
        public float LastContact;
        public float VolumeScale;
        public int PlayerVelocity;   // 0 when the chart's own velocities apply
        /// <summary>Live voice per pitch, so a re-strike can stop its own string.</summary>
        public readonly Dictionary<int, long> Sounding = new Dictionary<int, long>(4);
    }

    private readonly Dictionary<NoteController, TrillSchedule> trills =
        new Dictionary<NoteController, TrillSchedule>(8);
    private readonly List<NoteController> expiredTrills = new List<NoteController>(8);

    /// <summary>
    /// The sub-notes of one struck host that still have to sound.
    /// </summary>
    /// <remarks>
    /// 低難度的譜是把和絃裡多餘的音**藏進寄主的 sub_note**，玩家少按幾下、
    /// 和絃卻要完整地響（官方 normal 譜有一半以上的音是這樣藏起來的）。
    /// 敲到寄主就等於敲到整組，所以這裡把剩下的每一顆按它自己的
    /// `start_timing_msec` 排進歌曲時間 —— 不是全部擠在按鍵那一刻放。
    ///
    /// 和 trill 的差別：trill 要**持續接觸**才繼續響，放開就停；隱藏音符是
    /// 「敲下去就該完整發出來」，所以排進去之後不需要再接觸，一路播到完。
    /// </remarks>
    private sealed class HiddenSchedule
    {
        public List<SubNoteData> Subs;
        public int NextIndex;
        public float VolumeScale;
        public int PlayerVelocity;   // 0 when the chart's own velocities apply
        public int SkipIndex;        // 寄主自己那一顆，已經由 PlayJudgedNote 敲過
        public double DelayMs;       // 寄主被押後多少，隱藏音要跟著押一樣多
        public bool IsAuto;          // 不綁寄主，照歌曲時間自己播
    }

    private readonly List<HiddenSchedule> hidden = new List<HiddenSchedule>(8);

    /// <summary>Voices currently sounding. Useful when tuning the pool size.</summary>
    public int ActiveVoiceCount
    {
        get
        {
            if (voices == null) return 0;
            int count = 0;
            for (int i = 0; i < voices.Length; i++)
                if (voices[i].Stage != VoiceStage.Idle) count++;
            return count;
        }
    }

    public int VoicePoolSize => voices?.Length ?? 0;

    /// <summary>Notes started since the counter was last read, for diagnostics.</summary>
    public int TakeNoteOnCount()
    {
        int count = noteOnsSinceLastReport;
        noteOnsSinceLastReport = 0;
        return count;
    }

    /// <summary>
    /// Snapshot of what every voice is doing, for diagnosing dropouts.
    /// </summary>
    /// <param name="ringing">Key still down.</param>
    /// <param name="pedalHeld">Key lifted, pedal keeping it alive.</param>
    /// <param name="releasing">Damping.</param>
    /// <param name="audible">Sources Unity is actually still playing.</param>
    public void SampleVoiceStates(out int ringing, out int pedalHeld, out int releasing, out int audible)
    {
        ringing = pedalHeld = releasing = audible = 0;
        if (voices == null) return;
        for (int i = 0; i < voices.Length; i++)
        {
            Voice voice = voices[i];
            switch (voice.Stage)
            {
                case VoiceStage.Ringing: ringing++; break;
                case VoiceStage.PedalHeld: pedalHeld++; break;
                case VoiceStage.Releasing: releasing++; break;
            }
            if (voice.Stage != VoiceStage.Idle && voice.Source != null && voice.Source.isPlaying) audible++;
        }
    }

    /// <summary>Whether the pedal is holding notes right now, and from which source.</summary>
    public bool DiagnosticPedalDown => ResolvePedalDown(CurrentSongMs());

    public int DiagnosticPedalSpanCount => ActivePedal.SpanCount;

    /// <summary>
    /// Hands over the chart's beats so a pedal can be invented for charts whose
    /// source MIDI had no CC64. Kept as beats rather than spans so switching the
    /// mode takes effect immediately instead of on the next song.
    /// </summary>
    public void SetSubstitutePedalSource(System.Collections.Generic.List<int> beatTimings,
        int beatsPerBar,
        System.Collections.Generic.List<NoteData> notes = null)
    {
        substituteBeats = beatTimings;
        // 音符是拿來判斷「這個放開點有沒有落在還按著的長音中間」用的。沒有音符時
        // AutoPedal 會退回純拍線的舊行為。
        substituteNotes = notes;
        substituteBeatsPerBar = Mathf.Max(1, beatsPerBar);
        substitutePedal = PedalTimeline.Empty;
        substituteMode = PianoAutoPedal.Off;
    }

    /// <summary>
    /// The pedalling in force: the chart's own if it has any, otherwise the
    /// substitute — and only when the player asked for one.
    /// </summary>
    private PedalTimeline ActivePedal
    {
        get
        {
            if (pedal.HasPedal) return pedal;

            SettingsManager settings = SettingsManager.Instance;
            PianoAutoPedal mode = settings != null ? settings.CurrentPianoAutoPedal : PianoAutoPedal.Off;
            if (mode == PianoAutoPedal.Off || substituteBeats == null) return PedalTimeline.Empty;

            if (mode != substituteMode)
            {
                substituteMode = mode;
                substitutePedal = new PedalTimeline(
                    AutoPedal.Build(substituteBeats, substituteBeatsPerBar, mode, substituteNotes));
            }
            return substitutePedal;
        }
    }

    /// <summary>
    /// The pedalling in force right now, for anything that has to draw it. Exposed
    /// so the visuals resolve chart-versus-substitute the same way the sound does,
    /// instead of keeping a second copy of that decision that could disagree.
    /// </summary>
    public PedalTimeline ActivePedalTimeline => ActivePedal;
    private long nextVoiceSerial = 1;

    private enum VoiceStage
    {
        Idle,
        Ringing,      // key still down
        PedalHeld,    // key lifted, pedal keeping it alive
        Releasing,
    }

    private sealed class Voice
    {
        public AudioSource Source;
        public VoiceStage Stage;
        public long Serial;         // ordering for voice stealing
        public int Pitch;
        public float Gain;          // level before the release envelope
        public float ReleaseSeconds;
        public float ReleaseElapsed;
        public double GateOffMs;    // scheduled key-off in song time, -1 when driven by the player
        public float StartedRealtime; // wall clock, so the lifetime cap survives a bad song clock
        public int Token;           // invalidates stale NoteOff calls after reuse
        public bool ReleasedByKey;  // damper fell because the key lifted, so the pedal can still catch it
        /// <summary>
        /// 踏板接不到這顆。
        /// </summary>
        /// <remarks>
        /// 給誤觸音用的。踏板是**譜面要求的演奏動作**，玩家踩它是為了讓該延續的
        /// 音延續 —— 不是為了讓剛剛按錯的那一下也跟著延續。
        ///
        /// 少了這個旗標，一顆本來只該響 200ms 的錯音會被踏板撐滿整個踏板段落，
        /// 變成好幾秒的持續錯音。那不只是難聽，它會蓋掉玩家正在跟的旋律。
        /// </remarks>
        public bool IgnoresPedal;
    }

    public bool IsReady => manifest != null && voices != null;

    /// <summary>
    /// Reports whether the engine can actually render as many voices as the piano
    /// needs, and says what to do when it cannot.
    /// </summary>
    /// <remarks>
    /// Deliberately only a check. Raising the budget at runtime means
    /// <see cref="AudioSettings.Reset"/>, which reinitialises the audio device —
    /// on some drivers that leaves the game with no audio at all, which is a far
    /// worse failure than dropped notes. The budget lives in
    /// ProjectSettings/AudioManager.asset instead, and that file is only read at
    /// startup: an editor session already running keeps its old value however
    /// many times the scripts recompile.
    /// </remarks>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CheckVoiceBudget()
    {
        try
        {
            AudioConfiguration config = AudioSettings.GetConfiguration();
            if (config.numRealVoices >= RequiredRealVoices) return;
            Debug.LogWarning(
                $"PianoVoiceManager: audio engine renders only {config.numRealVoices} real voices, " +
                $"and the piano needs {RequiredRealVoices} for dense charts. " +
                "ProjectSettings > Audio > Real Voice Count is already set; " +
                "restart the Unity editor for it to take effect.");
        }
        catch { }
    }

    /// <summary>
    /// Sizes the voice pool to what the audio engine can actually render.
    /// </summary>
    /// <remarks>
    /// This is the difference between graceful and ugly. Asking for more sources
    /// than the engine's real-voice budget does not get more polyphony: Unity
    /// silences the excess itself, re-deciding every frame and by its own
    /// measure of importance — so the note just struck can be the one that
    /// vanishes, and the piano appears to cut in and out at random.
    ///
    /// Staying inside the budget makes the loss ours to choose instead, and
    /// <see cref="TakeVoice"/> always gives up the quietest voice, which by
    /// definition is the one nobody can hear. Measured demand for the densest
    /// restored chart is a median of 17 voices and a peak of 57, so a full
    /// budget removes stealing entirely while a small one degrades quietly.
    /// </remarks>
    private int ResolveVoiceCount()
    {
        int requested = Mathf.Max(4, voiceCount);
        int budget;
        try
        {
            budget = AudioSettings.GetConfiguration().numRealVoices;
        }
        catch
        {
            return requested;
        }
        if (budget <= 0) return requested;

        // Leave room for the song and the hit clicks, which share the budget.
        const int reservedForOtherAudio = 8;
        int usable = Mathf.Clamp(budget - reservedForOtherAudio, 4, requested);
        if (usable < requested)
        {
            Debug.LogWarning(
                $"PianoVoiceManager: using {usable} voices instead of {requested} — the audio engine " +
                $"renders only {budget} real voices. Dense passages will drop their quietest tails. " +
                "Raise ProjectSettings > Audio > Real Voice Count and restart the Unity editor " +
                $"(it is already set to {RequiredRealVoices}; the file is only read at startup).");
        }
        else
        {
            Debug.Log($"PianoVoiceManager: {usable} voices within a {budget}-voice engine budget.");
        }
        return usable;
    }

    public static PianoVoiceManager EnsureCreated()
    {
        if (Instance != null) return Instance;
        var existing = FindFirstObjectByType<PianoVoiceManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }
        var go = new GameObject("PianoVoiceManager");
        DontDestroyOnLoad(go);
        return go.AddComponent<PianoVoiceManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        manifest = PianoSampleManifest.Load();
        if (manifest == null)
        {
            Debug.LogWarning("PianoVoiceManager: no piano sample manifest; " +
                             "run qt_editor/render_piano_samples.py to build the bank.");
            return;
        }

        voices = new Voice[ResolveVoiceCount()];
        for (int i = 0; i < voices.Length; i++)
        {
            var go = new GameObject("PianoVoice" + i);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            // Nothing on this bus wants effects, and skipping them saves a pass
            // per voice — worth having when dozens sound at once.
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            // Unity only renders m_RealVoiceCount sources at once and virtualises
            // the rest by priority. A dense chord lights up dozens of these, so
            // they must rank below the music — otherwise the song itself is what
            // gets muted when the player hits a big chord.
            source.priority = VoicePriority;
            voices[i] = new Voice { Source = source, Stage = VoiceStage.Idle, Token = 1 };
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Points the pedal envelope at the chart being played.</summary>
    public void SetChart(Chart chart) => SetPedalData(chart?.pedal_data);

    /// <summary>
    /// Points the pedal envelope at a chart's spans directly.
    /// </summary>
    /// <remarks>
    /// Taken separately from <see cref="Chart"/> because a healthy chart never
    /// produces one: GameManager parses a lightweight header and only falls back
    /// to a full Chart when that fails.
    /// </remarks>
    public void SetPedalData(System.Collections.Generic.List<PedalSpan> spans)
    {
        pedal = spans != null && spans.Count > 0
            ? new PedalTimeline(spans)
            : PedalTimeline.Empty;
        substitutePedal = PedalTimeline.Empty;
        substituteMode = PianoAutoPedal.Off;
        pedalWasDown = false;
        lastPedalPollMs = double.NegativeInfinity;
        warnedPoolExhausted = false;
        StopAll();
    }

    /// <summary>Drops every trill schedule, for a song change or a stop.</summary>
    public void ClearTrills() => trills.Clear();

    public void StopAll()
    {
        // 重開/換場景時排程要一起清掉，否則上一輪還沒響完的隱藏音會插到
        // 下一輪的開頭。
        hidden.Clear();
        if (voices == null) return;
        trills.Clear();
        for (int i = 0; i < voices.Length; i++)
        {
            Voice voice = voices[i];
            if (voice.Source != null) voice.Source.Stop();
            voice.Stage = VoiceStage.Idle;
            voice.Token++;
        }
    }

    /// <summary>
    /// Strikes a note. Returns a handle for <see cref="NoteOff"/>, or 0 when the
    /// bank is unavailable.
    /// </summary>
    /// <param name="gateOffMs">
    /// Song time to lift the key by itself, for modes that follow the chart's
    /// note length. Pass a negative value when the player's key-up drives it.
    /// </param>
    /// <summary>Song time right now, for callers that have to place a note against it.</summary>
    public double SongMs => CurrentSongMs();

    /// <param name="delaySeconds">
    /// Holds the strike back so it can land somewhere other than the moment the
    /// key went down. Only ever forward — nothing can be pulled earlier.
    /// </param>
    public long NoteOn(int pitch, int velocity, double gateOffMs = -1d, float volumeScale = 1f,
        float delaySeconds = 0f, bool ignorePedal = false)
    {
        if (!IsReady) return 0;
        if (!manifest.TryResolve(pitch, velocity, out PianoSampleZone zone,
                out float pitchRatio, out float gain))
        {
            return 0;
        }

        AudioClip clip = ResolveClipWithoutStall(pitch, velocity, ref zone, ref pitchRatio);
        if (clip == null) return 0;

        Voice voice = TakeVoice();
        voice.Serial = nextVoiceSerial++;
        voice.Token++;
        voice.Pitch = pitch;
        voice.Gain = Mathf.Clamp(gain * volumeScale * masterVolume, 0f, 2f);
        voice.Stage = VoiceStage.Ringing;
        voice.ReleaseElapsed = 0f;
        voice.ReleaseSeconds = 0f;
        voice.GateOffMs = gateOffMs;
        voice.ReleasedByKey = false;
        voice.IgnoresPedal = ignorePedal;

        AudioSource source = voice.Source;
        source.clip = clip;
        source.pitch = pitchRatio;
        source.volume = voice.Gain;
        source.time = 0f;
        if (delaySeconds > 0f)
        {
            voice.StartedRealtime = Time.unscaledTime + delaySeconds;
            source.PlayDelayed(delaySeconds);
        }
        else
        {
            voice.StartedRealtime = Time.unscaledTime;
            source.Play();
        }
        noteOnsSinceLastReport++;
        return Handle(voice);
    }

    /// <summary>
    /// Keeps a trill sounding while the player holds contact, following the
    /// strokes the chart actually wrote.
    /// </summary>
    /// <remarks>
    /// A trill note carries its whole performance in its sub-notes: each stroke's
    /// own time, pitch and velocity, including the accelerando real trills have
    /// (one measured chart goes 100 ms → 75 ms → 37 ms between strokes, alternating
    /// two pitches). Repeating one pitch on the effect layer's fixed pulse would
    /// throw all of that away, so the schedule is played from the chart and only
    /// *gated* by contact.
    ///
    /// Call this on every contact pulse: the first creates the schedule, the rest
    /// keep it alive. Stop calling and it expires, which is how releasing the key
    /// stops the trill without the judgment layer needing to tell us.
    /// </remarks>
    public void RefreshTrill(NoteController note, System.Collections.Generic.List<SubNoteData> subs,
        float volumeScale, int playerVelocity)
    {
        if (!IsReady || note == null || subs == null || subs.Count == 0) return;

        if (!trills.TryGetValue(note, out TrillSchedule schedule))
        {
            schedule = new TrillSchedule { Subs = subs, NextIndex = 0 };
            trills[note] = schedule;
        }
        schedule.LastContact = Time.unscaledTime;
        schedule.VolumeScale = volumeScale;
        schedule.PlayerVelocity = playerVelocity;
    }

    /// <summary>
    /// Queues every hidden sub-note of a host that was just struck.
    /// </summary>
    /// <param name="skipPitch">寄主自己的音高；那一顆已經敲過，不再重複。</param>
    /// <param name="hostStartMs">寄主在譜面上的起音時間，用來挑出寄主自己那顆 sub。</param>
    /// <param name="delayMs">寄主這一擊被押後多少毫秒（演奏模式的 spread）。</param>
    /// <returns>排進去的隱藏音數量（不含寄主自己那一顆）。</returns>
    /// <summary>
    /// 掛上「排在寄主之前」的隱藏音符，照歌曲時間自動播，不等任何按鍵。
    /// </summary>
    /// <remarks>
    /// 一首歌掛一次（換譜面時重掛）。這些音在 <see cref="ScheduleHiddenNotes"/>
    /// 那條路會被跳過，不會重複發聲。
    /// </remarks>
    public int ScheduleAutoHiddenNotes(List<SubNoteData> subs, float volumeScale)
    {
        if (subs == null || subs.Count == 0) return 0;
        hidden.Add(new HiddenSchedule
        {
            Subs = subs,
            NextIndex = 0,
            VolumeScale = volumeScale,
            PlayerVelocity = 0,      // 用譜面自己的力度
            SkipIndex = -1,
            DelayMs = 0d,
            IsAuto = true,
        });
        return subs.Count;
    }

    public int ScheduleHiddenNotes(List<SubNoteData> subs, int skipPitch,
        int hostStartMs, float volumeScale, int playerVelocity, double delayMs)
    {
        if (!IsReady || subs == null || subs.Count < 2) return 0;

        // 挑出「寄主自己那一顆」：音高相同、而且起音時間離寄主最近的那個。
        // 官方的 sub_note 各自帶時間，同一個音高可能出現不只一次，所以不能
        // 只比音高。
        int skip = -1;
        int bestGap = int.MaxValue;
        for (int i = 0; i < subs.Count; i++)
        {
            SubNoteData sub = subs[i];
            if (sub == null) continue;
            if (ResolveSubNotePitch(sub) != skipPitch) continue;
            int gap = Mathf.Abs(sub.start_timing_msec - hostStartMs);
            if (gap < bestGap) { bestGap = gap; skip = i; }
        }

        hidden.Add(new HiddenSchedule
        {
            Subs = subs,
            NextIndex = 0,
            VolumeScale = volumeScale,
            PlayerVelocity = playerVelocity,
            SkipIndex = skip,
            DelayMs = delayMs > 0d ? delayMs : 0d,
        });
        int queued = 0;
        for (int i = 0; i < subs.Count; i++)
        {
            if (subs[i] != null && i != skip && !subs[i].autoPlay) queued++;
        }
        return queued;
    }

    private void UpdateHiddenNotes(double songMs)
    {
        if (hidden.Count == 0) return;

        for (int h = hidden.Count - 1; h >= 0; h--)
        {
            HiddenSchedule schedule = hidden[h];
            while (schedule.NextIndex < schedule.Subs.Count)
            {
                int index = schedule.NextIndex;
                SubNoteData sub = schedule.Subs[index];
                // 自動播放的那些由專屬佇列負責，寄主這條路要跳過，
                // 否則同一顆會響兩次。
                if (sub == null || index == schedule.SkipIndex
                    || (!schedule.IsAuto && sub.autoPlay))
                {
                    schedule.NextIndex++;
                    continue;
                }
                // 還沒到它自己的時間就停在這裡等 —— 整組不是一起響的。
                if (sub.start_timing_msec > songMs) break;

                schedule.NextIndex++;
                int pitch = ResolveSubNotePitch(sub);
                if (pitch < 0) continue;
                int velocity = schedule.PlayerVelocity > 0
                    ? schedule.PlayerVelocity
                    : Mathf.Clamp(sub.velocity > 0 ? sub.velocity : 90, 1, 127);

                // 玩家按晚了的話這一顆的時間可能已經過去，那就立刻補上；
                // 押後量則沿用寄主那一擊的，兩者才會聽起來是同一次觸鍵。
                double gateOff = sub.end_timing_msec > sub.start_timing_msec
                    ? sub.end_timing_msec
                    : -1d;
                NoteOn(pitch, velocity, gateOff, schedule.VolumeScale,
                    schedule.DelayMs > 0d ? (float)(schedule.DelayMs / 1000d) : 0f);
            }
            if (schedule.NextIndex >= schedule.Subs.Count) hidden.RemoveAt(h);
        }
    }

    /// <summary>Drops every queued hidden note, for a restart or a scene change.</summary>
    public void ClearHiddenNotes()
    {
        hidden.Clear();
        autoHiddenChart = null;
    }

    /// <summary>已經掛過自動播放佇列的那份譜面。換一份就要重掛。</summary>
    private Chart autoHiddenChart;

    private void EnsureAutoHiddenScheduled()
    {
        Chart chart = null;
        try
        {
            chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
        }
        catch { }
        if (ReferenceEquals(chart, autoHiddenChart)) return;

        // 舊譜面的自動佇列先撤掉；由寄主觸發的那些讓它們自然走完就好。
        for (int i = hidden.Count - 1; i >= 0; i--)
        {
            if (hidden[i] != null && hidden[i].IsAuto) hidden.RemoveAt(i);
        }
        autoHiddenChart = chart;
        if (chart == null || chart.autoHidden == null || chart.autoHidden.Count == 0) return;

        SettingsManager settings = SettingsManager.Instance;
        if (settings != null && !settings.PlayHiddenSubNotes) return;
        float volume = settings != null ? settings.PianoVoiceVolume : 1f;
        ScheduleAutoHiddenNotes(chart.autoHidden, volume);
    }

    private void UpdateTrills(double songMs)
    {
        if (trills.Count == 0) return;
        float now = Time.unscaledTime;

        expiredTrills.Clear();
        foreach (var pair in trills)
        {
            TrillSchedule schedule = pair.Value;
            if (now - schedule.LastContact > TrillContactTimeoutSeconds)
            {
                expiredTrills.Add(pair.Key);
                continue;
            }

            while (schedule.NextIndex < schedule.Subs.Count)
            {
                SubNoteData sub = schedule.Subs[schedule.NextIndex];
                if (sub == null) { schedule.NextIndex++; continue; }
                if (sub.start_timing_msec > songMs) break;

                schedule.NextIndex++;
                int pitch = ResolveSubNotePitch(sub);
                if (pitch < 0) continue;
                int velocity = schedule.PlayerVelocity > 0
                    ? schedule.PlayerVelocity
                    : Mathf.Clamp(sub.velocity > 0 ? sub.velocity : 90, 1, 127);

                // One voice per pitch, as a piano has one string per key: striking
                // it again stops what was ringing. Without this the pedal holds
                // every stroke — a trill accelerating to 37 ms between strokes
                // stacks twenty-odd copies of the same two pitches, and identical
                // waveforms summing like that is the blowout heard at its tail.
                if (schedule.Sounding.TryGetValue(pitch, out long previous))
                {
                    DampForRetrigger(previous);
                }
                long handle = NoteOn(pitch, velocity, sub.end_timing_msec, schedule.VolumeScale);
                if (handle != 0) schedule.Sounding[pitch] = handle;
            }
            if (schedule.NextIndex >= schedule.Subs.Count) expiredTrills.Add(pair.Key);
        }

        for (int i = 0; i < expiredTrills.Count; i++) trills.Remove(expiredTrills[i]);
    }

    /// <summary>
    /// Stops a voice because its own key is being struck again.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="NoteOff"/> this ignores the pedal: the pedal lifts the
    /// dampers, but a hammer returning to a string stops it either way. Skipping
    /// this is what lets repeated notes stack into a blowout.
    /// </remarks>
    public void DampForRetrigger(long handle)
    {
        Voice voice = Resolve(handle);
        if (voice == null || voice.Stage == VoiceStage.Idle ||
            voice.Stage == VoiceStage.Releasing)
        {
            return;
        }
        BeginRelease(voice, RetriggerDampMs);
    }

    /// <summary>Sub-notes carry the MIDI pitch directly, or the 1..88 index.</summary>
    private static int ResolveSubNotePitch(SubNoteData sub)
    {
        if (sub.src_pitch >= 21 && sub.src_pitch <= 108) return sub.src_pitch;
        if (sub.scale_piano >= 1 && sub.scale_piano <= 88) return 20 + sub.scale_piano;
        return -1;
    }

    /// <summary>Lifts the key for a voice. The pedal may still hold it.</summary>
    public void NoteOff(long handle)
    {
        Voice voice = Resolve(handle);
        if (voice == null || voice.Stage != VoiceStage.Ringing) return;

        // Ask the pedal where it is now rather than trusting last frame's answer:
        // a key can lift in the same frame the pedal came up.
        if (ResolvePedalDown(CurrentSongMs()))
        {
            // Damper stays off the string until the pedal lifts.
            voice.Stage = VoiceStage.PedalHeld;
            return;
        }
        BeginRelease(voice, DamperMsFor(voice.Pitch, keyReleaseMs), byKey: true);
    }

    private void Update()
    {
        if (!IsReady) return;

        double songMs = CurrentSongMs();
        bool pedalDown = ResolvePedalDown(songMs);

        // A pedal change is a lift and an immediate re-press — 2 ms apart at the
        // median in real pedalling, far inside one frame. Comparing this frame's
        // state with the last one would never see it, and every note released
        // under the pedal would ring on until its sample ran out, which fills
        // every voice and silences the piano a minute into a song.
        bool pedalLifted = (pedalWasDown && !pedalDown) || LiftedSince(songMs);
        // The foot goes down after the hand in normal pedalling, so notes whose
        // keys have just lifted still have to be caught. Never on a frame that
        // also saw a lift: that lift is the pedal change asking for the old
        // harmony to go, and catching it back would undo the change.
        bool pedalPressed = !pedalWasDown && pedalDown && !pedalLifted;
        pedalWasDown = pedalDown;
        lastPedalPollMs = songMs;

        // 換譜面（或重玩）時把「排在寄主之前」的隱藏音符重新掛上。放在這裡
        // 而不是載入譜面時掛：載入時這個管理器不一定存在。
        EnsureAutoHiddenScheduled();

        // Trill strokes are scheduled in song time, so they have to be checked
        // every frame rather than on the effect layer's coarser pulse.
        UpdateTrills(songMs);
        // 隱藏音符也排在歌曲時間上，同樣要每格檢查。
        UpdateHiddenNotes(songMs);

        float deltaTime = Time.unscaledDeltaTime;
        for (int i = 0; i < voices.Length; i++)
        {
            Voice voice = voices[i];
            // Hard ceiling on wall-clock lifetime, checked before anything that
            // depends on song time or pedal state. Whatever else goes wrong —
            // a clock that never advances, a note-off that never arrives, a
            // pedal that never reads as lifted — voices cannot pile up and
            // starve the pool. By this point a piano string is inaudible anyway.
            if (voice.Stage != VoiceStage.Idle && voice.Stage != VoiceStage.Releasing &&
                Time.unscaledTime - voice.StartedRealtime > maxVoiceSeconds)
            {
                BeginRelease(voice, DamperMsFor(voice.Pitch, pedalReleaseMs));
            }

            switch (voice.Stage)
            {
                case VoiceStage.Idle:
                    continue;

                case VoiceStage.Ringing:
                    // Modes that follow the chart lift the key on their own.
                    if (voice.GateOffMs >= 0d && songMs >= voice.GateOffMs)
                    {
                        // 誤觸音到期就放，踏板接不到它。
                        if (pedalDown && !voice.IgnoresPedal) voice.Stage = VoiceStage.PedalHeld;
                        else BeginRelease(voice, DamperMsFor(voice.Pitch, keyReleaseMs), byKey: true);
                    }
                    break;

                case VoiceStage.PedalHeld:
                    // Either the pedal changed inside this frame, or it is simply
                    // up now. The second test is what stops a voice from hanging
                    // forever when it starts being held right after a lift.
                    if (pedalLifted || !pedalDown)
                        BeginRelease(voice, DamperMsFor(voice.Pitch, pedalReleaseMs));
                    break;

                case VoiceStage.Releasing:
                    // Still-falling damper, lifted by the key rather than by the
                    // pedal: the pedal arriving now takes it back off the string.
                    if (pedalPressed && voice.ReleasedByKey && !voice.IgnoresPedal)
                    {
                        RecaptureWithPedal(voice);
                        break;
                    }
                    voice.ReleaseElapsed += deltaTime;
                    float t = voice.ReleaseSeconds > 0f
                        ? Mathf.Clamp01(voice.ReleaseElapsed / voice.ReleaseSeconds)
                        : 1f;
                    // Damper fall is closer to exponential than linear; squaring
                    // keeps the tail from ending in an audible step.
                    voice.Source.volume = voice.Gain * (1f - t) * (1f - t);
                    if (t >= 1f) Recycle(voice);
                    continue;
            }

            // A sample that simply ran out frees its voice. A strike still waiting
            // on its delay has not run out — it has not started.
            if (voice.Stage != VoiceStage.Idle && Time.unscaledTime >= voice.StartedRealtime &&
                !voice.Source.isPlaying)
            {
                Recycle(voice);
            }
        }
    }

    /// <summary>
    /// Whether a pedal change happened between the last poll and now. Only the
    /// chart's pedalling needs this — a physical pedal cannot be lifted and
    /// re-pressed inside a single frame.
    /// </summary>
    private bool LiftedSince(double songMs)
    {
        SettingsManager settings = SettingsManager.Instance;
        if (settings != null && settings.CurrentPianoPedalSource == PianoPedalSource.Player &&
            MIDIInputManager.Instance != null)
        {
            return false;
        }
        // A seek or a song restart moves song time backwards; resync rather than
        // reporting a lift for the whole skipped stretch.
        if (double.IsNegativeInfinity(lastPedalPollMs) || songMs < lastPedalPollMs) return false;
        return ActivePedal.LiftedBetween(lastPedalPollMs, songMs);
    }

    /// <summary>
    /// Whether the damper is off the strings right now, from whichever pedal the
    /// player chose.
    /// </summary>
    /// <remarks>
    /// The player's own pedal is only honoured while a MIDI device is actually
    /// present. Falling back to the chart's pedalling when it is not keeps a
    /// keyboard-only player from hearing every sustain-written piece bone dry.
    /// </remarks>
    private bool ResolvePedalDown(double songMs)
    {
        SettingsManager settings = SettingsManager.Instance;
        if (settings != null && settings.CurrentPianoPedalSource == PianoPedalSource.Player)
        {
            MIDIInputManager midi = MIDIInputManager.Instance;
            if (midi != null) return midi.SustainPedalDown;
        }
        return ActivePedal.IsDown(songMs);
    }

    /// <summary>
    /// Song time on the same clock the notes and the judgment use.
    /// </summary>
    /// <remarks>
    /// Deliberately <c>effectiveSongPosition</c> rather than
    /// <c>GetDspSongPositionMs()</c>: only the former carries the player's music
    /// playback offset, and it is the clock a note's <c>startTime</c> is judged
    /// against. Reading the other one puts every gate time and pedal span a
    /// settings-dependent distance away from the notes they belong to.
    /// </remarks>
    private double CurrentSongMs()
    {
        try
        {
            Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
            if (conductor != null) return conductor.effectiveSongPosition;
        }
        catch { }
        return Time.unscaledTimeAsDouble * 1000d;
    }

    /// <summary>
    /// 這顆音的制音器要多久才把弦壓死。<see cref="noDamperPitch"/> 之上回傳的是
    /// 「不制音」——交給取樣自己的自然衰減，由 maxVoiceSeconds 收尾。
    /// </summary>
    private float DamperMsFor(int pitch, float baseMs)
    {
        // 沒有制音器不等於「響到 voice 的壽命上限」。那一段的弦短、能量小，自然
        // 衰減本來就快——一兩秒就沒了。放到 maxVoiceSeconds(8s) 實測會把
        // Rrhar'il 推到 171、MarbleBlue 推到 167，雙雙超過 160 的 voice pool，
        // 那時候管理器開始搶 voice，聽起來就是鋼琴在密集段落斷掉。3 秒讓兩者
        // 分別回到 113 和 121，全曲庫最大 150、沒有一份超過。
        if (pitch >= noDamperPitch) return undampedRingSeconds * 1000f;

        float ms = baseMs;
        if (pitch < 60)
        {
            // A0 最重，中央 C 是基準。
            ms *= Mathf.Lerp(1f, bassDamperMultiplier, Mathf.InverseLerp(60f, 21f, pitch));
        }
        else if (pitch > damperTaperPitch)
        {
            // 越靠近沒有制音器的那一段，抓得越鬆。
            ms *= Mathf.Lerp(1f, trebleDamperMultiplier,
                             Mathf.InverseLerp(damperTaperPitch, noDamperPitch, pitch));
        }
        return ms;
    }

    private void BeginRelease(Voice voice, float milliseconds, bool byKey = false)
    {
        voice.Stage = VoiceStage.Releasing;
        voice.ReleaseSeconds = Mathf.Max(0.001f, milliseconds / 1000f);
        voice.ReleaseElapsed = 0f;
        voice.Gain = voice.Source.volume;
        voice.ReleasedByKey = byKey;
    }

    /// <summary>
    /// Puts a still-falling damper back off the string because the pedal came
    /// down, keeping whatever loudness the string has left.
    /// </summary>
    /// <remarks>
    /// This is how a pianist actually pedals. In syncopated pedalling the foot
    /// goes down just *after* the hand, so a note whose key has already lifted
    /// is caught by the pedal on the way out and rings on — quieter than if the
    /// pedal had been down all along, because the damper did touch the string,
    /// but far from silent. Cutting it dead instead is what makes a restored
    /// chart sound clipped where the recording sounds connected.
    /// </remarks>
    private static void RecaptureWithPedal(Voice voice)
    {
        voice.Stage = VoiceStage.PedalHeld;
        voice.Gain = voice.Source.volume;   // already decayed by the partial fall
        voice.ReleaseElapsed = 0f;
        voice.ReleasedByKey = false;
    }

    private void Recycle(Voice voice)
    {
        voice.Source.Stop();
        voice.Stage = VoiceStage.Idle;
        voice.Token++;
    }

    /// <summary>
    /// How loud a voice still is, as opposed to how loud it started.
    /// </summary>
    /// <remarks>
    /// <c>AudioSource.volume</c> is the level set when the note was struck and
    /// never moves, so a string ringing for eight seconds under the pedal looks
    /// exactly as loud as one struck this frame. Ranking by that steals the
    /// freshly played quiet note and keeps the inaudible old one — heard as the
    /// piano dropping notes while playing.
    ///
    /// A piano string loses roughly half its energy every couple of seconds, so
    /// folding elapsed time in gives an ordering that matches what can actually
    /// be heard. It only has to rank voices against each other, so an
    /// approximation of the decay is enough.
    /// </remarks>
    private float EstimateAudibility(Voice voice)
    {
        if (voice.Stage == VoiceStage.Idle) return 0f;
        float elapsed = Mathf.Max(0f, Time.unscaledTime - voice.StartedRealtime);
        float level = voice.Gain * Mathf.Pow(0.5f, elapsed / Mathf.Max(0.1f, decayHalfLifeSeconds));
        // A voice already damping is leaving regardless, so it is the better victim.
        if (voice.Stage == VoiceStage.Releasing) level *= 0.25f;
        return level;
    }

    /// <summary>
    /// Finds a free voice, otherwise steals the least audible one.
    /// </summary>
    /// <remarks>
    /// Stealing by age would cut a bass note still ringing at full volume while
    /// sparing a treble tail that has already faded to nothing. Taking the
    /// quietest voice instead means the note that disappears is the one closest
    /// to inaudible already, so a stolen voice is not heard as a dropout.
    /// </remarks>
    private Voice TakeVoice()
    {
        Voice quietest = null;
        float quietestLevel = float.MaxValue;

        for (int i = 0; i < voices.Length; i++)
        {
            Voice voice = voices[i];
            if (voice.Stage == VoiceStage.Idle) return voice;

            float level = EstimateAudibility(voice);
            if (level < quietestLevel)
            {
                quietestLevel = level;
                quietest = voice;
            }
        }

        // Running out is normal on a dense passage. It becomes a problem only if
        // it never stops, which is what a leak looks like — say so once, because
        // silent stealing shows up as "the piano cuts out sometimes" instead.
        if (!warnedPoolExhausted)
        {
            warnedPoolExhausted = true;
            Debug.LogWarning($"PianoVoiceManager: all {voices.Length} voices busy; " +
                             "stealing the quietest. Constant stealing means voices are leaking.");
        }

        if (quietest != null && quietest.Source != null && quietest.Source.isPlaying)
        {
            quietest.Source.Stop();
        }
        return quietest ?? voices[0];
    }

    /// <summary>
    /// 找一個現在就能放的取樣，**不在按鍵當下解壓**。
    /// </summary>
    /// <remarks>
    /// 預熱只涵蓋譜面音高 × 譜面力度 ±16。Hardcore 用玩家的觸鍵力度，力度一偏出去、
    /// 或彈到譜面沒有的音，就踩到冷取樣：Resources.Load 在主執行緒把平均 9 秒的
    /// Vorbis 整段解壓完才回來，實測一個和弦 30ms、遊玩中每秒冷載入 1～17 次，
    /// 正是「判定有點卡」的尖峰。
    ///
    /// 冷的時候改成：正確的取樣在背景非同步載入（下一次就是熱的），這一下先用
    /// 已經在快取裡的替身發聲——
    ///
    /// 1. 同一個音高、最接近的其他力度層。變調比例是依替身自己的基準音重算，所以
    ///    音高完全正確，只有音色輕重差一層。
    /// 2. 沒有的話，±3 半音內的取樣，同樣以目標音高重算變調。
    /// 3. 都沒有才退回同步載入（整個音域完全沒碰過的音，極少見）。
    /// </remarks>
    private AudioClip ResolveClipWithoutStall(int pitch, int velocity,
        ref PianoSampleZone zone, ref float pitchRatio)
    {
        if (clipCache.TryGetValue(zone.sample, out AudioClip cached)) return cached;

        LoadClipInBackground(zone.sample);

        // 1. 同音高，往上下力度層找。
        for (int step = 8; step <= 126; step += 8)
        {
            if (TryCachedSubstitute(pitch, pitch, velocity - step, ref zone, ref pitchRatio, out AudioClip below))
                return below;
            if (TryCachedSubstitute(pitch, pitch, velocity + step, ref zone, ref pitchRatio, out AudioClip above))
                return above;
        }

        // 2. 鄰近音高的取樣，以目標音高重算變調。
        for (int semitones = 1; semitones <= 3; semitones++)
        {
            if (TryCachedSubstitute(pitch, pitch - semitones, velocity, ref zone, ref pitchRatio, out AudioClip lower))
                return lower;
            if (TryCachedSubstitute(pitch, pitch + semitones, velocity, ref zone, ref pitchRatio, out AudioClip upper))
                return upper;
        }

        // 3. 真的什麼都沒有：寧可慢一點也要有聲音。
        return ResolveClip(zone.sample);
    }

    private bool TryCachedSubstitute(int targetPitch, int lookupPitch, int velocity,
        ref PianoSampleZone zone, ref float pitchRatio, out AudioClip clip)
    {
        clip = null;
        if (velocity < 1 || velocity > 127) return false;
        if (!manifest.TryResolve(lookupPitch, velocity, out PianoSampleZone candidate, out _, out _)) return false;
        if (candidate == null || string.IsNullOrEmpty(candidate.sample)) return false;
        if (!clipCache.TryGetValue(candidate.sample, out AudioClip found) || found == null) return false;

        zone = candidate;
        pitchRatio = Mathf.Pow(2f, (targetPitch - candidate.root_key) / 12f);
        clip = found;
        coldSubstitutes++;
        return true;
    }

    private readonly HashSet<string> backgroundLoads = new HashSet<string>();
    private int coldSubstitutes;

    /// <summary>非同步載入一個取樣，完成後放進快取。同一個取樣只會排一次。</summary>
    private void LoadClipInBackground(string sampleName)
    {
        if (string.IsNullOrEmpty(sampleName) || !backgroundLoads.Add(sampleName)) return;
        ResourceRequest request = Resources.LoadAsync<AudioClip>(manifest.resource_folder + "/" + sampleName);
        if (request == null)
        {
            backgroundLoads.Remove(sampleName);
            return;
        }
        request.completed += _ =>
        {
            backgroundLoads.Remove(sampleName);
            if (clipCache.ContainsKey(sampleName)) return;
            var clip = request.asset as AudioClip;
            if (clip == null) Debug.LogWarning("PianoVoiceManager: missing sample " + sampleName);
            clipCache[sampleName] = clip;
        };
    }

    private AudioClip ResolveClip(string sampleName)
    {
        if (string.IsNullOrEmpty(sampleName)) return null;
        if (clipCache.TryGetValue(sampleName, out AudioClip cached)) return cached;

        // 冷路徑。預熱沒蓋到的取樣才會走到這裡，而這一行會卡住主執行緒（見
        // PrewarmForChart）。留著是因為「慢一點的聲音」永遠好過「沒有聲音」。
        coldLoads++;
        AudioClip clip;
        using (HitchProbe.Measure("pianoColdLoad")) clip = LoadClip(sampleName);
        clipCache[sampleName] = clip;
        return clip;
    }

    private AudioClip LoadClip(string sampleName)
    {
        string path = manifest.resource_folder + "/" + sampleName;
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null)
        {
            Debug.LogWarning("PianoVoiceManager: missing sample " + path);
        }
        return clip;
    }

    // ── 取樣預熱 ────────────────────────────────────────────────────────
    //
    // 取樣是 Vorbis 壓縮、匯入設定是 Decompress On Load，所以 Resources.Load 會在
    // 呼叫端的執行緒上把整段解壓完才回來。取樣平均 9 秒、最長 15.8 秒，1008 個共
    // 2.6 小時——第一次敲到某個 (音域, 力度層) 時，那一顆音就是要等這段解壓才發聲，
    // 整個 frame 也跟著卡。一首歌會踩到幾百次，聽起來就是「有些音慢半拍才出來」。
    //
    // 預熱把同一批 Load 移到還沒開始彈的時候做，總記憶體不變（本來也是一樣的 clip
    // 留在 clipCache 裡），只是不再發生在按鍵的那一刻。

    /// <summary>Prewarmed clips, so a keystroke never pays a decode.</summary>
    private Coroutine prewarmRoutine;
    private int prewarmDone, prewarmTotal, coldLoads;

    /// <summary>
    /// Clips a single prewarm pass may load, so an unusual chart cannot fill
    /// memory. Each decompressed sample averages 0.77 MB.
    /// </summary>
    private const int PrewarmClipBudget = 512;

    /// <summary>True while samples are still being loaded ahead of play.</summary>
    /// <remarks>
    /// 載入頁靠這個決定什麼時候放行。取樣是 Vorbis + Decompress On Load，解壓由
    /// 引擎在整合非同步載入時做——那發生在腳本之外，所以不能靠計時器判斷它做完
    /// 沒有，只能問這個。
    /// </remarks>
    public bool IsPrewarming => prewarmRoutine != null;

    /// <summary>0-1 progress of the current prewarm pass; 1 when nothing is pending.</summary>
    public float PrewarmProgress01 =>
        prewarmTotal > 0 ? Mathf.Clamp01((float)prewarmDone / prewarmTotal) : 1f;

    /// <summary>Reads and resets the prewarm counters, for diagnostics.</summary>
    public string TakePrewarmReport()
    {
        // coldLoads = 還是同步卡住主執行緒的載入；substitutes = 先用替身、正確取樣在背景載的次數。
        string report = $"prewarm={prewarmDone}/{prewarmTotal} coldLoads={coldLoads} substitutes={coldSubstitutes} bgLoading={backgroundLoads.Count}";
        coldLoads = 0;
        coldSubstitutes = 0;
        return report;
    }

    /// <summary>
    /// Loads the samples this chart will ask for, before the first note is played.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once per song: already-cached clips are skipped, so
    /// the background full parse re-running this costs nothing.
    ///
    /// The chart's own velocities are what Performance mode plays, so they resolve
    /// exactly. Hardcore takes velocity from the player instead, which no chart can
    /// predict — there the neighbouring velocity bands are warmed as well, since a
    /// player who leans harder or softer than the written dynamic would otherwise
    /// land on a cold sample every time.
    /// </remarks>
    public void PrewarmForChart(System.Collections.Generic.List<NoteData> notes)
    {
        if (!IsReady || notes == null || notes.Count == 0) return;
        if (!isActiveAndEnabled) return;

        var wanted = new System.Collections.Generic.List<string>(256);
        var seen = new HashSet<string>();
        bool playerTouch = SettingsManager.Instance != null && SettingsManager.Instance.UsesPlayerTouch;

        for (int i = 0; i < notes.Count && wanted.Count < PrewarmClipBudget; i++)
        {
            NoteData data = notes[i];
            if (data == null) continue;

            CollectSamples(PianoVisualLayout.ResolveMidiPitch(data), ChartVelocityOf(data),
                playerTouch, seen, wanted);

            // A trill writes every stroke out as a sub-note with its own pitch,
            // and a chord's sub-notes carry the per-pitch velocities.
            if (data.subNotes == null) continue;
            for (int j = 0; j < data.subNotes.Count && wanted.Count < PrewarmClipBudget; j++)
            {
                SubNoteData sub = data.subNotes[j];
                if (sub == null) continue;
                CollectSamples(ResolveSubNotePitch(sub), sub.velocity > 0 ? sub.velocity : 90,
                    playerTouch, seen, wanted);
            }
        }

        if (wanted.Count == 0) return;
        if (prewarmRoutine != null) StopCoroutine(prewarmRoutine);
        prewarmRoutine = StartCoroutine(PrewarmClips(wanted));
    }

    private static int ChartVelocityOf(NoteData data)
    {
        if (data.subNotes != null)
        {
            for (int i = 0; i < data.subNotes.Count; i++)
            {
                SubNoteData sub = data.subNotes[i];
                if (sub != null && sub.velocity > 0) return Mathf.Clamp(sub.velocity, 1, 127);
            }
        }
        return 80;
    }

    /// <summary>Adds the sample this note resolves to, and its velocity neighbours.</summary>
    private void CollectSamples(int pitch, int velocity, bool playerTouch,
        HashSet<string> seen, System.Collections.Generic.List<string> wanted)
    {
        if (pitch < 0) return;
        AddSample(pitch, velocity, seen, wanted);
        if (!playerTouch) return;
        // The bands are 8-15 wide, so ±16 reaches the neighbour on either side
        // whichever band this velocity landed in.
        AddSample(pitch, Mathf.Clamp(velocity - 16, 1, 127), seen, wanted);
        AddSample(pitch, Mathf.Clamp(velocity + 16, 1, 127), seen, wanted);
    }

    private void AddSample(int pitch, int velocity,
        HashSet<string> seen, System.Collections.Generic.List<string> wanted)
    {
        if (!manifest.TryResolve(pitch, velocity, out PianoSampleZone zone, out _, out _)) return;
        if (zone == null || string.IsNullOrEmpty(zone.sample)) return;
        if (clipCache.ContainsKey(zone.sample)) return;
        if (!seen.Add(zone.sample)) return;
        wanted.Add(zone.sample);
    }

    /// <summary>
    /// Loads the clips one at a time, in the order the chart plays them.
    /// </summary>
    /// <remarks>
    /// LoadAsync rather than Load: the read and the decode are what cost tens of
    /// milliseconds, and async keeps both off the frame, so the loading screen
    /// this runs behind stays responsive.
    ///
    /// In chart order because the list is built by walking the notes, so a song
    /// that starts before the whole bank is warm has at least warmed the notes it
    /// is about to play.
    /// </remarks>
    private System.Collections.IEnumerator PrewarmClips(System.Collections.Generic.List<string> wanted)
    {
        prewarmDone = 0;
        prewarmTotal = wanted.Count;
        for (int i = 0; i < wanted.Count; i++)
        {
            string sampleName = wanted[i];
            if (clipCache.ContainsKey(sampleName)) { prewarmDone++; continue; }

            // LoadAsync keeps the read and the decode off this frame; the clip is
            // only cached once the request reports it is done.
            ResourceRequest request = Resources.LoadAsync<AudioClip>(
                manifest.resource_folder + "/" + sampleName);
            while (request != null && !request.isDone) yield return null;

            var clip = request != null ? request.asset as AudioClip : null;
            // 取樣是 Vorbis + Decompress On Load，解壓由引擎在整合非同步載入時做，
            // 也就是在腳本跑之前。碼錶量不到它，所以只留記號：這一格如果同時
            // frame 很長而 cs 接近 0，兇手就是這裡，不是 shader。
            if (clip != null) HitchProbe.Mark($"pianoPrewarm({sampleName} {clip.length:0.0}s)");
            if (clip == null)
            {
                Debug.LogWarning("PianoVoiceManager: missing sample " + sampleName);
            }
            clipCache[sampleName] = clip;
            prewarmDone++;
        }
        prewarmRoutine = null;
        Debug.Log($"PianoVoiceManager: prewarmed {prewarmDone} piano samples.");
    }

    // Handles pack the voice index with a reuse token so a NoteOff that arrives
    // after the voice was recycled cannot silence somebody else's note.
    private long Handle(Voice voice)
    {
        int index = System.Array.IndexOf(voices, voice);
        return ((long)(index + 1) << 32) | (uint)voice.Token;
    }

    private Voice Resolve(long handle)
    {
        if (handle <= 0 || voices == null) return null;
        int index = (int)(handle >> 32) - 1;
        if (index < 0 || index >= voices.Length) return null;
        Voice voice = voices[index];
        return voice.Token == (int)(uint)handle ? voice : null;
    }
}
