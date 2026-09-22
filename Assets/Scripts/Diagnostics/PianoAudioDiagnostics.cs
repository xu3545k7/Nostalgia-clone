using UnityEngine;

/// <summary>
/// Prints one line a second describing why the piano can or cannot be heard.
/// </summary>
/// <remarks>
/// Written after three failed attempts at "the piano goes quiet after a while",
/// each of which reasoned from the source instead of observing the running game.
/// The line below carries every value needed to tell the candidate causes apart:
///
/// * <c>on/s</c> is 0 → nothing is triggering notes; the fault is upstream of audio.
/// * <c>voices</c> pinned at the pool size → voices are leaking, not being freed.
/// * <c>playing</c> far below <c>voices</c> → Unity is virtualising them, so it is a
///   real-voice budget or priority problem.
/// * <c>real</c> below 64 → the AudioManager change never took effect (it needs an
///   editor restart, not just a recompile).
/// * <c>music</c> not playing → the dropout is the song, not the keysound.
///
/// Toggle with F9 during play, or leave it on: it costs one log line a second.
/// </remarks>
[DefaultExecutionOrder(1000)]
public class PianoAudioDiagnostics : MonoBehaviour
{
    [SerializeField] private bool logging = true;
    [SerializeField, Min(0.1f)] private float intervalSeconds = 1f;

    private float nextReportTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<PianoAudioDiagnostics>() != null) return;
        var go = new GameObject("PianoAudioDiagnostics");
        DontDestroyOnLoad(go);
        go.AddComponent<PianoAudioDiagnostics>();
    }

    private void Update()
    {
        // No key toggle: this project has switched to the Input System package,
        // where touching UnityEngine.Input throws — and throwing here every
        // frame both hides the report below and drags the whole frame down.
        // Uncheck the component in the inspector to silence it instead.
        if (!logging || Time.unscaledTime < nextReportTime) return;
        nextReportTime = Time.unscaledTime + intervalSeconds;

        var settings = SettingsManager.Instance;
        string mode = settings != null ? settings.CurrentPianoSoundMode.ToString() : "?";
        string pedalSource = settings != null ? settings.CurrentPianoPedalSource.ToString() : "?";

        AudioConfiguration config = AudioSettings.GetConfiguration();

        var voices = PianoVoiceManager.Instance;
        string voiceReport;
        if (voices == null)
        {
            voiceReport = "voices=<no manager>";
        }
        else if (!voices.IsReady)
        {
            voiceReport = "voices=<bank missing>";
        }
        else
        {
            voices.SampleVoiceStates(out int ringing, out int pedalHeld, out int releasing, out int playing);
            voiceReport =
                $"on/s={voices.TakeNoteOnCount()} " +
                $"voices={voices.ActiveVoiceCount}/{voices.VoicePoolSize} " +
                $"(ring={ringing} pedal={pedalHeld} rel={releasing}) playing={playing} " +
                $"pedalDown={voices.DiagnosticPedalDown} spans={voices.DiagnosticPedalSpanCount}";
        }

        // 誤觸統計自己一行：它有多行，混進 PianoDiag 那條長行會讀不了。
        string mistouch = MistouchProbe.TakeReport();
        if (mistouch != null) Debug.Log(mistouch);
        string judgeCost = JudgeCostProbe.TakeReport();
        if (judgeCost != null) Debug.Log(judgeCost);
        string eatenReport = EatenInputProbe.TakeReport();
        if (eatenReport != null) Debug.Log(eatenReport);

        Debug.Log($"[PianoDiag] mode={mode} pedalFrom={pedalSource} " +
                  $"real={config.numRealVoices} " +
                  $"{voiceReport} trigger[{PianoKeysound.TakeStats()}] " +
                  $"{DescribeStrikeLatency(voices)} " +
                  $"songMs={DescribeSongClock()} {DescribeMusic()}");
    }

    /// <summary>
    /// The clock the gate times and pedal spans are compared against. If this is
    /// not advancing in step with the music, notes are released at the wrong
    /// moment or never at all.
    /// </summary>
    private static string DescribeSongClock()
    {
        GameManager game = GameManager.Instance;
        Conductor conductor = game != null ? game.Conductor : null;
        if (conductor == null) return "<no conductor>";

        // render-judge is the gap between the clock the notes are drawn on and
        // the clock they are judged on. A player aims at what they see, so a
        // steady gap here is a bias no judgment offset can fix: moving the
        // offset shifts the judgment, the player re-adapts to the unchanged
        // visuals, and the same lead comes straight back.
        SettingsManager settings = SettingsManager.Instance;
        float judgeOffset = settings != null ? settings.JudgmentOffsetMs : 0f;
        return $"{conductor.effectiveSongPosition:0} playing={conductor.isPlaying} " +
               $"render-judge={conductor.RenderJudgmentDeltaMs:+0.0;-0.0;0.0}ms " +
               $"latency={conductor.AudioOutputLatencyMs:0.0}ms judgeOffset={judgeOffset:0.0}ms " +
               $"fps={(Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f):0}";
    }

    /// <summary>
    /// 從按鍵到聽見聲音，中間還剩下哪些固定成本。
    /// </summary>
    /// <remarks>
    /// 這一段是「已經沒有 bug、但物理上還在」的延遲，寫出來是為了不要再去猜：
    ///
    /// * <c>out</c> 是輸出緩衝的長度（bufferLength × numBuffers ÷ 取樣率）。這是
    ///   送進混音器的取樣到真的離開音效卡之間的距離，Unity 只讓我們調 bufferLength
    ///   （ProjectSettings &gt; Audio &gt; DSP Buffer Size，目前已經是最短的 256），
    ///   numBuffers 由平台決定，Windows 通常是 4。
    /// * <c>blk</c> 是一個混音區塊。AudioSource.Play() 只能從下一個區塊的開頭開始，
    ///   所以平均還要再等半個區塊。
    /// * <c>frame</c> 是輸入被讀到的間隔。MIDI 佇列一個 frame 只抽一次
    ///   （MIDIInputManager.Update），而 DisplaySync 把 vSync 鎖成 1，所以這一項
    ///   等於螢幕更新率的倒數——60Hz 螢幕就是最多 16.7ms，144Hz 剩 6.9ms。
    /// * <c>coldLoads</c> 不是 0 就代表還有取樣沒被預熱到，那些音會慢上幾十毫秒。
    /// </remarks>
    private static string DescribeStrikeLatency(PianoVoiceManager voices)
    {
        float outMs = 0f, blockMs = 0f;
        try
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            int rate = AudioSettings.outputSampleRate;
            outMs = AudioOutputLatency.ComputeMs(bufferLength, numBuffers, rate);
            blockMs = AudioOutputLatency.ComputeMs(bufferLength, 1, rate);
        }
        catch { }

        float frameMs = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime * 1000f : 0f;
        string prewarm = voices != null && voices.IsReady ? voices.TakePrewarmReport() : "prewarm=<n/a>";
        return $"strike[out={outMs:0.0}ms blk={blockMs:0.0}ms frame={frameMs:0.0}ms {prewarm}]";
    }

    private static string DescribeMusic()
    {
        GameManager game = GameManager.Instance;
        Conductor conductor = game != null ? game.Conductor : null;
        AudioSource music = conductor != null && conductor.audioSync != null
            ? conductor.audioSync.audioSource
            : null;

        if (music == null) return "music=<none>";
        return $"music={(music.isPlaying ? "playing" : "STOPPED")} " +
               $"vol={music.volume:0.00} prio={music.priority}";
    }
}
