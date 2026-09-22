using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the structure of a single note, matching the JSON format.
/// </summary>
[System.Serializable]
public class NoteData
{
    public int startTime;
    public int endTime;
    public int startLane;
    public int endLane;
    // Internal/editor pitch uses MIDI note number (21..108 for A0..C8).
    public int pitch;
    // Compatibility with legacy/offical 1..88 piano indexing when present in JSON.
    public int scale_piano;
    public string type;
    // numeric compatibility field for "soft" notes (0=tap, 1=soft, 2=hold)
    // The JSON converter writes "note_type" for compatibility; JsonUtility
    // will populate this field when the JSON contains that key.
    public int note_type = 0;
    public int hand;
    /// <summary>
    /// MIDI velocity for this note's own strike, 1-127; 0 when the chart never
    /// had expression restored.
    /// </summary>
    /// <remarks>
    /// **This field was missing entirely.** Restored charts write `velocity` on
    /// each note -- 262 of the 324 charts under UserSongs carry it -- but with no
    /// field to land in, JsonUtility dropped it silently, and every note in the
    /// game was struck at the fallback of 80. Nothing failed; the dynamics simply
    /// were not there, which is the hardest kind of missing data to notice.
    ///
    /// The velocity restored into `subNotes` is a different thing: those are the
    /// extra voices of a chord or a trill. This is the note the player presses.
    /// </remarks>
    public int velocity;
    /// <summary>Release velocity, restored alongside; unused so far.</summary>
    public int off_velocity;
    // Gesture-note metadata used by converted official charts.
    public int gateTime;
    public int index = -1;
    public int param1;
    public int param2;
    public int param3;
    /// <summary>
    /// 編輯器的「遊戲譜面隱藏」：這顆不佔按鍵、玩家看不到，只在寄主被打到時
    /// 跟著發聲。編輯器的 JSON 用 hidden + hostIndex 表示，遊戲原本只認
    /// subNotes —— 中間沒有橋接，所以隱藏音符會被當成真的音符生出來，玩家
    /// 平白多出一堆要打的。`Chart.NormaliseHiddenNotes` 負責轉換。
    /// </summary>
    public bool hidden;
    /// <summary>隱藏音符掛在 notes 的第幾顆上；-1 = 沒指定，改用音高最近的。</summary>
    public int hostIndex = -1;
    public List<SubNoteData> subNotes;
    /// <summary>
    /// 新手教學的示範段：自動彈、不計分、不接受玩家按鍵。只在執行期由
    /// <see cref="TutorialSession.Attach"/> 標記，不讀也不寫檔案。
    /// </summary>
    [System.NonSerialized] public bool tutorialDemo;
}

[System.Serializable]
public class SubNoteData
{
    public int start_timing_msec;
    public int end_timing_msec;
    public int scale_piano;
    public int velocity;
    public int track_index;
    public int src_min_key;
    public int src_max_key;
    public int src_note_type;
    public int src_hand;
    public int src_pitch;
    public int src_velocity;
    public int src_track;

    /// <summary>
    /// 這一顆在寄主**之前**發聲，所以不等寄主被打到，照自己的時間自動播。
    /// </summary>
    /// <remarks>
    /// 隱藏音符是「敲到寄主就整組響」，但那只對排在寄主之後的音成立。排在
    /// 寄主之前的音等寄主等不到——時間早就過了，於是整批擠在按鍵那一刻補播，
    /// 寄主一旦漏掉更是整批不響。實測難度生成出來的譜有 34~43% 的隱藏音符
    /// 比寄主早（最多早 352ms）。
    ///
    /// 由 <see cref="Chart.NormaliseHiddenNotes"/> 在折疊時標記，執行期才有
    /// 值——JSON 裡沒有這個欄位。
    /// </remarks>
    [System.NonSerialized] public bool autoPlay;
}

/// <summary>
/// One stretch of held sustain pedal, in song milliseconds.
/// </summary>
/// <remarks>
/// Restored from the source MIDI's CC64 and already converted onto the chart's own
/// timeline, so these can be compared against note timings directly. Charts made
/// before the restore tool simply carry no pedal_data at all.
/// </remarks>
[System.Serializable]
public class PedalSpan
{
    public int start_ms;
    public int end_ms;

    public bool Contains(double songMs) => songMs >= start_ms && songMs <= end_ms;
}

/// <summary>
/// A single time-signature change event inside a chart.
/// </summary>
[System.Serializable]
public class TimeSigChange
{
    public int time_ms;
    public int numerator;
    public int denominator;
}

/// <summary>
/// Defines the overall structure of the chart file, matching the JSON format.
/// </summary>
[System.Serializable]
public class Chart
{
    public float first_bpm;
    public string time_signature;
    public int music_finish_time_msec;
    public List<NoteData> notes;
    public List<int> beat_timings;
    public List<TimeSigChange> time_signature_changes;
    // Sorted, non-overlapping sustain pedal spans. Empty/absent on charts that
    // predate the MIDI expression restore.
    public List<PedalSpan> pedal_data;

    /// <summary>
    /// Stretches every time in the chart by <paramref name="factor"/>.
    /// </summary>
    /// <remarks>
    /// This is how practice speed is applied: the chart is rewritten once, at
    /// load, and everything downstream plays it as an ordinary chart.
    ///
    /// The alternative -- leaving the chart alone and running the clock slow --
    /// was tried and is wrong in two ways. The song position comes from elapsed
    /// DSP time, so scaling it warps the one thing every system trusts; and the
    /// gaps between notes stay where they were while everything crawls, which is
    /// not what a slower tempo means. Stretched here, the notes really are
    /// further apart, the piece really is longer, and the clock, the spawner and
    /// the judgment window never learn that anything unusual happened.
    ///
    /// Everything carrying a time has to move together, including the beat grid
    /// and the pedal spans -- a stretched chart over an unstretched pedal map
    /// holds the damper down in the wrong places.
    /// </remarks>
    public void ApplyTimeStretch(float factor)
    {
        if (factor <= 0.0001f || Mathf.Approximately(factor, 1f)) return;

        music_finish_time_msec = Mathf.RoundToInt(music_finish_time_msec * factor);
        first_bpm /= factor;

        if (notes != null)
        {
            for (int i = 0; i < notes.Count; i++)
            {
                NoteData note = notes[i];
                if (note == null) continue;
                note.startTime = Mathf.RoundToInt(note.startTime * factor);
                note.endTime = Mathf.RoundToInt(note.endTime * factor);
                note.gateTime = Mathf.RoundToInt(note.gateTime * factor);
                if (note.subNotes == null) continue;
                for (int s = 0; s < note.subNotes.Count; s++)
                {
                    SubNoteData sub = note.subNotes[s];
                    if (sub == null) continue;
                    sub.start_timing_msec = Mathf.RoundToInt(sub.start_timing_msec * factor);
                    sub.end_timing_msec = Mathf.RoundToInt(sub.end_timing_msec * factor);
                }
            }
        }

        if (beat_timings != null)
            for (int i = 0; i < beat_timings.Count; i++)
                beat_timings[i] = Mathf.RoundToInt(beat_timings[i] * factor);

        if (time_signature_changes != null)
            for (int i = 0; i < time_signature_changes.Count; i++)
                if (time_signature_changes[i] != null)
                    time_signature_changes[i].time_ms =
                        Mathf.RoundToInt(time_signature_changes[i].time_ms * factor);

        if (pedal_data != null)
        {
            for (int i = 0; i < pedal_data.Count; i++)
            {
                PedalSpan span = pedal_data[i];
                if (span == null) continue;
                span.start_ms = Mathf.RoundToInt(span.start_ms * factor);
                span.end_ms = Mathf.RoundToInt(span.end_ms * factor);
            }
        }
    }

    /// <summary>
    /// 把編輯器格式的隱藏音符（hidden + hostIndex）併進寄主的 subNotes，
    /// 然後從 notes 移除。載入譜面之後、任何系統讀 notes 之前呼叫一次。
    /// </summary>
    /// <remarks>
    /// 隱藏音符的語意是「不佔按鍵、玩家看不到，但寄主被打到時要跟著響」。
    /// 遊戲端一直只認官方格式的 subNotes，對 hidden 這個欄位視而不見，所以
    /// 那些音符會被當成真的音符生出來 —— 玩家平白多出一堆要打的，而且畫面上
    /// 也看得到（編輯器的非音高模式和預覽都已經濾掉了，只有遊戲沒有）。
    ///
    /// 寄主自己那一顆也要放進 subNotes：`PianoVoiceManager.ScheduleHiddenNotes`
    /// 要求 `subNotes.Count >= 2`，而且靠「音高等於寄主、起音時間最接近」把
    /// 寄主那顆挑出來跳過。少了它，整組隱藏音都不會響。
    /// </remarks>
    /// <returns>移除了幾顆。</returns>
    /// <summary>
    /// 排在寄主之前、必須自動播放的隱藏音符（照時間排序）。
    /// <see cref="NormaliseHiddenNotes"/> 之後才有值。
    /// </summary>
    [System.NonSerialized] public List<SubNoteData> autoHidden = new List<SubNoteData>();

    /// <summary>上一次折疊的統計，用來診斷「低難度沒有 keysound」。</summary>
    [System.NonSerialized] public int hiddenByIndex;
    [System.NonSerialized] public int hiddenByFallback;
    [System.NonSerialized] public int hiddenWithoutHost;

    public int NormaliseHiddenNotes()
    {
        autoHidden.Clear();
        if (notes == null || notes.Count == 0) return 0;

        List<NoteData> visible = new List<NoteData>(notes.Count);
        List<NoteData> buried = new List<NoteData>();
        foreach (NoteData note in notes)
        {
            if (note == null) continue;
            if (note.hidden) buried.Add(note);
            else visible.Add(note);
        }
        if (buried.Count == 0) return 0;

        List<NoteData> all = notes;
        hiddenByIndex = hiddenByFallback = hiddenWithoutHost = 0;
        foreach (NoteData note in buried)
        {
            bool direct = note.hostIndex >= 0 && note.hostIndex < all.Count
                          && all[note.hostIndex] != null && !all[note.hostIndex].hidden;
            NoteData host = ResolveHost(note, all, visible);
            if (host == null) { hiddenWithoutHost++; continue; }
            if (direct) hiddenByIndex++; else hiddenByFallback++;
            if (host.subNotes == null) host.subNotes = new List<SubNoteData>();
            if (host.subNotes.Count == 0) host.subNotes.Add(ToSub(host));
            SubNoteData sub = ToSub(note);
            // 比寄主早的音等不到寄主：交給自動播放佇列，照自己的時間出聲。
            sub.autoPlay = note.startTime < host.startTime;
            if (sub.autoPlay) autoHidden.Add(sub);
            host.subNotes.Add(sub);
        }


        autoHidden.Sort((a, b) => a.start_timing_msec.CompareTo(b.start_timing_msec));
        int removed = notes.Count - visible.Count;
        notes = visible;
        return removed;
    }

    /// <summary>hostIndex 指到的那顆；沒指定或指壞了就退回時間最近的。</summary>
    /// <remarks>
    /// hostIndex 是**檔案裡 notes 陣列的位置**（編輯器的寫入端和讀取端都是
    /// 這個意思），所以要拿原始的 <paramref name="all"/> 去索引，不是拿已經
    /// 拔掉隱藏音的 <paramref name="visible"/>。之前索引錯清單時，隱藏音符
    /// 一多就整個錯位——難度生成出來的 normal 有 76% 是隱藏的，833 顆可見對
    /// 3474 顆，幾乎每一個 hostIndex 都指到別人身上或直接越界。
    /// </remarks>
    private NoteData ResolveHost(NoteData note, List<NoteData> all, List<NoteData> visible)
    {
        if (visible.Count == 0) return null;
        if (all != null && note.hostIndex >= 0 && note.hostIndex < all.Count)
        {
            NoteData direct = all[note.hostIndex];
            if (direct != null && !direct.hidden) return direct;
        }
        // 退路：時間最近的那顆，音高只當同分時的比較。
        //
        // 舊版是音高優先（差 1 個半音就抵掉 1000ms），在同一個音高反覆出現的
        // 曲子上會挑到幾十秒外的音符當寄主——sub_note 是「寄主被打到時才排程」
        // 的，寄主選錯等於那個 keysound 晚幾十秒才響，或根本不響。
        NoteData best = null;
        int bestScore = int.MaxValue;
        int pitch = NotePitch(note);
        foreach (NoteData candidate in visible)
        {
            int score = Mathf.Abs(candidate.startTime - note.startTime) * 128
                      + Mathf.Abs(NotePitch(candidate) - pitch);
            if (score < bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private static int NotePitch(NoteData note)
    {
        if (note.pitch >= 21 && note.pitch <= 108) return note.pitch;
        if (note.scale_piano >= 1 && note.scale_piano <= 88) return 20 + note.scale_piano;
        return 0;
    }

    private static SubNoteData ToSub(NoteData note)
    {
        int pitch = NotePitch(note);
        return new SubNoteData
        {
            start_timing_msec = note.startTime,
            end_timing_msec = note.endTime,
            scale_piano = pitch >= 21 ? pitch - 20 : 0,
            src_pitch = pitch,
            velocity = 0,
            src_velocity = 0,
            src_min_key = note.startLane,
            src_max_key = note.endLane,
            src_note_type = note.note_type,
            src_hand = note.hand,
        };
    }
}
