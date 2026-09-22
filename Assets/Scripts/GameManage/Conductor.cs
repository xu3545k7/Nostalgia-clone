using UnityEngine;

[DefaultExecutionOrder(-300)]
public class Conductor : MonoBehaviour
{
    public float bpm = 120.0f;
    public float songPosition { get; private set; } // The current position of the song in milliseconds
    // Visual pre-roll clock (active before audio starts)
    public float visualSongPosition { get; private set; } // Negative to 0 ms during pre-roll

    /// <summary>
    /// The pre-roll position as the chart should see it: frozen a lead-in out
    /// until the count-in has that much left. See StartVisualPreRoll.
    /// </summary>
    private float HeldVisualPosition =>
        visualSongPosition < -VisualLeadInMs ? -VisualLeadInMs : visualSongPosition;
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Debugging")]
    public bool showDebugOverlay = false;
    #endif

    private AudioSource audioSource;
    // Optional runtime reference to the project's AudioSync. If assigned, it is the authoritative
    // timing/audio source (uses DSP-based timing). If null, Conductor falls back to using its
    // own AudioSource and AudioSettings.dspTime where appropriate.
    public AudioSync audioSync;
    public bool isPlaying { get; private set; } = false;
    public bool isVisualPlaying { get; private set; } = false;
    // Unified read-only flags/values for gameplay timing
    public bool isActive => isPlaying || isVisualPlaying || hasScheduledStart;
    // Monotonic cached effective position (ms)
    private float _effectiveSongPositionCached = 0f;
    private bool _effectiveInitialized = false;
    // Per-frame cache of MusicPlaybackOffsetMs to avoid singleton+property access on every read
    private float _cachedPlaybackOffsetMs = 0f;
    // Per-frame cache of AudioSettings.dspTime to avoid repeated P/Invoke
    private double _cachedDspTime;
    private int _dspTimeCachedFrame = -1;
    private double _timingSampleRealtime;
    public double TimingSampleRealtime => _timingSampleRealtime;
    // PlayScheduled and AudioSettings.dspTime already use the same absolute
    // audio timeline. Do not subtract the complete DSP ring-buffer duration:
    // that double compensation makes the judgment clock systematically late
    // and turns correctly played notes into FAST judgments.
    private float _audioOutputLatencyMs = 0f;
    public float AudioOutputLatencyMs => _audioOutputLatencyMs;
    public float effectiveSongPosition => _effectiveSongPositionCached + _cachedPlaybackOffsetMs;

    // Rendering uses a frame-clock interpolated timeline. Judgment continues
    // to use effectiveSongPosition, so smoothing never changes scoring.
    private float _renderSongPositionCached;
    private double _renderClockLastRealtime;
    // Low-passed (songMs - realtimeMs). The render clock is rebuilt from this
    // every frame, so its rate is realtime's rate and only the offset drifts.
    private double _renderClockOffsetMs;
    private bool _renderClockInitialized;
    private bool _renderClockWasActive;
    [SerializeField, Range(30f, 600f), Tooltip("Time constant for reconciling the smooth render clock with the quantised DSP clock. Larger is smoother but slower to follow real drift.")]
    private float renderClockSmoothingMs = 180f;
    // How far the smooth clock may sit from the DSP sample before it is snapped.
    // Derived from the real DSP buffer in Awake — a scene-serialized value could
    // not know the device's buffer size and would pin the clock to the staircase.
    private float _renderClockToleranceMs = 12f;
    // Gap beyond which the render clock is rebuilt instead of advanced (loads, alt-tab).
    private const double MaxRenderClockGapSeconds = 0.25;
    public float renderSongPosition => _renderSongPositionCached + _cachedPlaybackOffsetMs;
    public float RenderJudgmentDeltaMs => _renderSongPositionCached - _effectiveSongPositionCached;

    /// <summary>Returns the current-frame cached AudioSettings.dspTime (avoids repeated P/Invoke).</summary>
    public double CachedDspTime
    {
        get
        {
            int f = Time.frameCount;
            if (f != _dspTimeCachedFrame)
            {
                _cachedDspTime = AudioSettings.dspTime;
                _dspTimeCachedFrame = f;
            }
            return _cachedDspTime;
        }
    }
    private float _lastPreRollDurationMs = 0f;
    public float LastPreRollDurationMs => _lastPreRollDurationMs;
    // Scheduled start support to align audio precisely without stutter
    private bool hasScheduledStart = false;
    private double scheduledDspTime = -1.0;
    // Captured DSP time when audio playback begins (or is scheduled to begin). Used to keep
    // gameplay timing aligned to DSP instead of AudioSource.time, which is affected by pitch.
    private double audioStartDspTime = -1.0;
    // Warn once if audioSource has no valid AudioClip when trying to read time
    private bool _warnedAudioSourceNoClip = false;
    // Playback speed multiplier (kept for compatibility; gameplay timing uses unscaled DSP time)
    public float playbackSpeed = 1f;

    public float RemainingPreRollMs
    {
        get
        {
            // Prefer AudioSync's scheduled time if available
            double scheduled = scheduledDspTime;
            if (audioSync != null)
            {
                double s = audioSync.GetScheduledDspTime();
                if (s > 0.0) scheduled = s;
            }
            if (!hasScheduledStart || scheduled < 0.0) return 0f;
            double now = CachedDspTime;
            double remain = scheduled - now;
            if (remain <= 0.0) return 0f;
            return (float)(remain * 1000.0);
        }
    }

    /// <summary>
    /// Returns the song position in milliseconds using the most precise DSP-aligned clock available.
    /// Negative values indicate time before the scheduled start.
    /// </summary>
    public float GetDspSongPositionMs()
    {
        // Prefer AudioSync when present – it exposes an audio DSP timeline.
        if (audioSync != null)
        {
            double audioTime = audioSync.GetAudioTime();
            if (audioTime > 0.0 || isPlaying)
            {
                return (float)(audioTime * 1000.0) - _audioOutputLatencyMs;
            }

            double scheduled = audioSync.GetScheduledDspTime();
            if (scheduled > 0.0)
            {
                return (float)((CachedDspTime - scheduled) * 1000.0) - _audioOutputLatencyMs;
            }
        }

        // Fall back to our AudioSource when AudioSync is not available.
        if (audioSource != null && audioSource.clip != null)
        {
            double baseline = audioStartDspTime;
            if (baseline > 0.0)
            {
                return (float)((CachedDspTime - baseline) * 1000.0) - _audioOutputLatencyMs;
            }

            if (hasScheduledStart && scheduledDspTime > 0.0)
            {
                return (float)((CachedDspTime - scheduledDspTime) * 1000.0) - _audioOutputLatencyMs;
            }
        }

        // During visual pre-roll use the visual clock so callers still see negative time.
        if (isVisualPlaying)
        {
            return HeldVisualPosition;
        }

        // As a final fallback, return the last known songPosition which is kept in milliseconds.
        return songPosition;
    }

    void Awake()
    {
        _audioOutputLatencyMs = 0f;
        RefreshRenderClockTolerance();
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;

        // Try to bind an AudioSource and an AudioSync if available. Prefer AudioSync.audioSource
        // when present so Conductor and visuals share the same DSP-referenced source.
        audioSource = GetComponent<AudioSource>();
        if (audioSync == null)
        {
            audioSync = GetComponent<AudioSync>();
            if (audioSync == null)
            {
                // Find first AudioSync in the scene as a fallback (keeps backward compatibility)
                audioSync = Object.FindFirstObjectByType<AudioSync>();
            }
        }
        if (audioSync != null && audioSync.audioSource != null)
        {
            audioSource = audioSync.audioSource;
        }
        if (audioSource == null)
        {
            // Add an AudioSource component if it doesn't exist
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        // Initialize music volume from SettingsManager if available
        if (SettingsManager.Instance != null)
        {
            SetMusicVolume(SettingsManager.Instance.GameplayMusicVolume);
        }
    }

    void OnDestroy()
    {
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    private void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        RefreshRenderClockTolerance();
        // The new device restarts the DSP staircase at a different phase.
        // Rebuild the filter instead of letting it chase the jump.
        ResetRenderClock(_effectiveSongPositionCached);
    }

    void Update()
    {
        // Cache dspTime and settings offset once per frame for all consumers
        _cachedDspTime = AudioSettings.dspTime;
        _dspTimeCachedFrame = Time.frameCount;
        _timingSampleRealtime = Time.realtimeSinceStartupAsDouble;
        var sm = SettingsManager.Instance;
        _cachedPlaybackOffsetMs = (sm != null) ? sm.MusicPlaybackOffsetMs : 0f;

        if (isPlaying)
        {
            // 這條時間軸**不**乘練習速度。譜面在載入時就已經被改寫成新的時間，
            // 再把時鐘放慢一次就是慢兩次 —— 音符會用倍率的平方在爬。
            // Update song position: prefer AudioSync's DSP-based time when available
            if (audioSync != null)
            {
                songPosition = (float)(audioSync.GetAudioTime() * 1000.0) - _audioOutputLatencyMs;
            }
            else if (audioSource != null && audioSource.clip != null)
            {
                // Derive time from DSP start to ignore AudioSource pitch scaling
                if (audioStartDspTime > 0.0)
                {
                    songPosition = (float)((_cachedDspTime - audioStartDspTime) * 1000.0) - _audioOutputLatencyMs;
                }
                else
                {
                    // Only read audioSource.time when a valid AudioClip is assigned.
                    songPosition = audioSource.time * 1000.0f;
                }
            }
            else
            {
                if (!_warnedAudioSourceNoClip)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning("Conductor: audioSource.clip is null or not an AudioClip; audio time unavailable.");
#endif
                    _warnedAudioSourceNoClip = true;
                }
            }
        }
        if (isVisualPlaying)
        {
            // Advance visual clock towards 0 during pre-roll
            visualSongPosition += Time.deltaTime * 1000.0f;
            if (visualSongPosition > 0f)
            {
                visualSongPosition = 0f; // Clamp at 0 until audio takes over
            }
        }

        // When scheduled start time arrives, switch from visual pre-roll to audio time
        if (hasScheduledStart)
        {
            if (_cachedDspTime >= scheduledDspTime)
            {
                double scheduled = scheduledDspTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                double offsetMs = (_cachedDspTime - scheduled) * 1000.0;
                BuildLogger.Log($"Conductor: Scheduled audio started at dsp={_cachedDspTime:F3}, scheduled={scheduled:F3}, offset={offsetMs:+0.0;-0.0;0.0}ms");
#endif
                isPlaying = true;
                audioStartDspTime = scheduled;
                StopVisualPreRoll();
                hasScheduledStart = false;
                scheduledDspTime = -1.0;
                songPosition = 0f;
            }
        }

        // Compute and cache a monotonic effective position every frame
        float effNow;
        if (isPlaying)
        {
            effNow = songPosition;
        }
        else if (isVisualPlaying)
        {
            effNow = HeldVisualPosition;
        }
        else if (hasScheduledStart)
        {
            float remainMs = RemainingPreRollMs;
            effNow = (remainMs > 0f) ? -remainMs : 0f;
        }
        else
        {
            effNow = songPosition;
        }
        // Initialize cache when first entering an active timing state (pre-roll or playing)
        if (!_effectiveInitialized)
        {
            _effectiveSongPositionCached = effNow;
            _effectiveInitialized = true;
        }
        else
        {
            _effectiveSongPositionCached = TimingMath.MonotonicClamp(effNow, _effectiveSongPositionCached);
        }

        UpdateRenderClock(_effectiveSongPositionCached);
    }

    /// <summary>
    /// Advances a continuous render-only clock and gently reconciles it with
    /// the authoritative DSP clock. AudioSettings.dspTime only moves when the
    /// audio thread finishes a buffer, so it is a staircase (one DSP block per
    /// step). Writing those steps straight into transforms makes notes, TRACK
    /// and beat lines shake together even at a perfectly stable frame rate,
    /// because the frame period and the DSP block period are not integer
    /// multiples: some frames the position does not move at all and the next
    /// one jumps two blocks.
    /// </summary>
    private void UpdateRenderClock(float authoritativeMs)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        bool active = isActive;
        if (!_renderClockInitialized || active != _renderClockWasActive)
        {
            ResetRenderClock(authoritativeMs, now, active);
            return;
        }

        if (!active)
        {
            ResetRenderClock(authoritativeMs, now, active);
            return;
        }

        double dt = now - _renderClockLastRealtime;
        _renderClockLastRealtime = now;
        if (dt <= 0.0 || dt > MaxRenderClockGapSeconds)
        {
            ResetRenderClock(authoritativeMs, now, active);
            return;
        }

        // Track only the OFFSET between the DSP staircase and the continuous
        // realtime clock, and low-pass it. The rendered position is then
        // rebuilt as realtime + offset, so its rate is exactly realtime's rate
        // — there is no per-frame speed alternation to see — while the slowly
        // moving offset still keeps it locked to the audio over the long run.
        // A previous attempt corrected the *position* every frame instead,
        // which is what made visual speed oscillate.
        double targetOffsetMs = authoritativeMs - now * 1000.0;
        double timeConstant = Mathf.Max(1f, renderClockSmoothingMs) / 1000.0;
        double alpha = 1.0 - System.Math.Exp(-dt / timeConstant);
        _renderClockOffsetMs += (targetOffsetMs - _renderClockOffsetMs) * alpha;

        double renderedMs = now * 1000.0 + _renderClockOffsetMs;
        // Seeks, pauses and audio-device changes move the authoritative clock
        // faster than the filter can follow. Bound the disagreement so a real
        // discontinuity resyncs immediately instead of drifting for a second.
        double delta = renderedMs - authoritativeMs;
        if (delta > _renderClockToleranceMs || delta < -_renderClockToleranceMs)
        {
            renderedMs = authoritativeMs + Mathf.Clamp((float)delta,
                -_renderClockToleranceMs, _renderClockToleranceMs);
            _renderClockOffsetMs = renderedMs - now * 1000.0;
        }

        _renderSongPositionCached = (float)renderedMs;
        _renderClockWasActive = active;
    }

    /// <summary>
    /// Recomputes how far the smooth render clock is allowed to sit from the
    /// DSP sample. The staircase alone accounts for one full block of
    /// disagreement, so the tolerance must be derived from the device's actual
    /// buffer rather than hard-coded.
    /// </summary>
    private void RefreshRenderClockTolerance()
    {
        try
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
            float blockMs = AudioOutputLatency.ComputeMs(bufferLength, 1, AudioSettings.outputSampleRate);
            _renderClockToleranceMs = Mathf.Clamp(blockMs * 2f + 2f, 6f, 40f);
        }
        catch
        {
            _renderClockToleranceMs = 12f;
        }
    }

    private void ResetRenderClock(float positionMs)
    {
        ResetRenderClock(positionMs, Time.realtimeSinceStartupAsDouble, isActive);
    }

    private void ResetRenderClock(float positionMs, double realtime, bool active)
    {
        _renderSongPositionCached = positionMs;
        _renderClockOffsetMs = positionMs - realtime * 1000.0;
        _renderClockLastRealtime = realtime;
        _renderClockInitialized = true;
        _renderClockWasActive = active;
    }

    /// <summary>
    /// Starts playing the song from the beginning.
    /// </summary>
    /// <param name="clip">The audio clip to play.</param>
    public void Play(AudioClip clip)
    {
        if (clip == null)
        {
            //Debug.LogError("AudioClip is null. Cannot play.");
            return;
        }
        // If an AudioSync is available, prefer using it (it manages its own AudioSource and DSP baseline).
        if (audioSync != null && audioSync.audioSource != null)
        {
            var a = audioSync.audioSource;
            a.Stop();
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 0f;
            a.clip = clip;
            a.time = 0f;
            StopVisualPreRoll();
            audioSync.Play();
            audioStartDspTime = AudioSettings.dspTime;
            isPlaying = true;
            songPosition = 0f;
            return;
        }

        // Fallback to using the local AudioSource
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        // Configure a safe 2D audio setup (preserve volume if previously set)
        audioSource.Stop();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f; // 2D
        audioSource.clip = clip;
        audioSource.time = 0f;

        // When actual audio starts, stop visual pre-roll (audio becomes the timing source)
        StopVisualPreRoll();
        audioSource.Play();
        audioStartDspTime = AudioSettings.dspTime;
        isPlaying = true;
        songPosition = 0f;
        //Debug.Log($"Conductor started playback. audioSource.isPlaying={audioSource.isPlaying}");
    }

    /// <summary>
    /// Schedule playback at a future DSP time. Keeps visual pre-roll running until the scheduled time.
    /// </summary>
    /// <summary>
    /// 把預捲的時鐘對回真正的開始時間。
    /// </summary>
    /// <remarks>
    /// 預捲是在載入**之前**就開始跑的，排程卻是在解析、解碼、預熱都做完之後才算
    /// 出來的 —— 兩者相差多少，就是載入花了多久。不對回去的話譜面會提早滾到零、
    /// 停在那裡等音訊，那段停頓正是「滾完了卻還沒開始」的來源。
    ///
    /// **只在還停著的時候對。** 進場那一段已經在動的時候把時鐘往回撥，譜面會倒
    /// 退一下 —— 那比一小段停頓難看得多。停著的區間裡 HeldVisualPosition 是夾住
    /// 的常數，怎麼撥都不會有人看見。
    /// </remarks>
    private void SyncVisualPreRollTo(double dspStart)
    {
        if (!isVisualPlaying) return;
        double remainMs = (dspStart - AudioSettings.dspTime) * 1000.0;
        if (remainMs <= VisualLeadInMs) return;              // 已經在滾了，別碰
        if (visualSongPosition > -VisualLeadInMs) return;    // 同上，保險
        visualSongPosition = (float)(-remainMs);
        ResetRenderClock(HeldVisualPosition);
    }

    public void PlayScheduled(AudioClip clip, double dspStart)
    {
        if (clip == null)
        {
            //Debug.LogError("AudioClip is null. Cannot schedule play.");
            return;
        }

        // Prefer AudioSync for scheduled playback
        if (audioSync != null && audioSync.audioSource != null)
        {
            var a = audioSync.audioSource;
            // Defensive: if the underlying AudioSource is disabled or on an inactive GameObject,
            // attempting PlayScheduled will throw. In that case, fall back to running without audio.
            if (a == null || !a.enabled || !a.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("Conductor: audioSync.audioSource is not available or disabled; scheduling without audio.");
                StartPlayingWithoutAudio();
                return;
            }
            a.Stop();
            a.playOnAwake = false;
            a.loop = false;
            a.spatialBlend = 0f;
            a.clip = clip;
            a.time = 0f;
            audioSync.PlayScheduled(dspStart);
            hasScheduledStart = true;
            scheduledDspTime = dspStart;
            audioStartDspTime = dspStart;
            isPlaying = false; // will flip true at dspStart
            songPosition = 0f;
            SyncVisualPreRollTo(dspStart);
            _effectiveSongPositionCached = -RemainingPreRollMs; // initialize cache for pre-roll
            _effectiveInitialized = true;
            return;
        }

        // Fallback to local AudioSource scheduled playback
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // Defensive: ensure the AudioSource component and its GameObject are active/enabled
        if (audioSource == null || !audioSource.enabled || !audioSource.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"Conductor: local AudioSource is not available or disabled; cannot schedule clip '{clip.name}'. Running without audio.");
            StartPlayingWithoutAudio();
            return;
        }

        audioSource.Stop();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.clip = clip;
        audioSource.time = 0f;

        audioSource.PlayScheduled(dspStart);
        hasScheduledStart = true;
        scheduledDspTime = dspStart;
        audioStartDspTime = dspStart;
        isPlaying = false; // will flip true at dspStart
        songPosition = 0f;
        SyncVisualPreRollTo(dspStart);
        _effectiveSongPositionCached = -RemainingPreRollMs; // initialize cache for pre-roll
        _effectiveInitialized = true;
        //Debug.Log($"Conductor: Scheduled clip '{clip.name}' at dsp={dspStart:F3}");
    }

    /// <summary>
    /// Stops the playback.
    /// </summary>
    public void Stop()
    {
        if (audioSync != null)
        {
            audioSync.Stop();
        }
        else if (audioSource != null)
        {
            audioSource.Stop();
        }
        isPlaying = false;
        StopVisualPreRoll();
        hasScheduledStart = false;
        scheduledDspTime = -1.0;
        audioStartDspTime = -1.0;
        songPosition = 0f;
        _effectiveSongPositionCached = 0f;
        _effectiveInitialized = false;
        ResetRenderClock(0f);
        //Debug.Log("Conductor stopped playback.");
    }

    /// <summary>
    /// Seek playback and gameplay timing to the specified millisecond position.
    /// This adjusts the audio playback position (AudioSync or AudioSource) and updates
    /// Conductor's internal cached timing so gameplay subsystems observe the new position.
    /// </summary>
    public void SeekToMs(float targetMs)
    {
        if (targetMs < 0f) targetMs = 0f;
        double targetSec = targetMs / 1000.0;

        // If using AudioSync, set its audioSource.time then call Pause/Resume pattern
        if (audioSync != null && audioSync.audioSource != null)
        {
            try
            {
                var src = audioSync.audioSource;
                bool wasPlaying = src.isPlaying;
                try { audioSync.Pause(); } catch { }
                try { src.time = (float)targetSec; } catch { }
                try { audioSync.Resume(); } catch { }
                // Ensure Conductor songPosition baseline matches AudioSync after resume
                songPosition = (float)(audioSync.GetAudioTime() * 1000.0) - _audioOutputLatencyMs;
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
                ResetRenderClock(songPosition);
                isPlaying = wasPlaying || isPlaying;
            }
            catch { }
            return;
        }

        // Fallback: operate on local AudioSource
        if (audioSource != null)
        {
            try
            {
                bool wasPlaying = audioSource.isPlaying;
                // Pause, set time, then unpause to avoid audible glitch
                if (wasPlaying)
                {
                    audioSource.Pause();
                }
                audioSource.time = (float)targetSec;
                // Update DSP baseline to keep Conductor timing consistent
                audioStartDspTime = AudioSettings.dspTime - targetSec;
                songPosition = targetMs - _audioOutputLatencyMs;
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
                ResetRenderClock(songPosition);
                isPlaying = wasPlaying || isPlaying;
                if (wasPlaying)
                {
                    audioSource.UnPause();
                }
            }
            catch { }
            return;
        }

        // No audio source available – just update cached positions so visuals advance
        songPosition = targetMs;
        _effectiveSongPositionCached = songPosition;
        _effectiveInitialized = true;
        ResetRenderClock(songPosition);
    }

    /// <summary>
    /// Pause playback/time. Stops audio and freezes Conductor timing until Resume is called.
    /// </summary>
    public void Pause()
    {
        try
        {
            if (audioSync != null && audioSync.audioSource != null)
            {
                audioSync.Pause();
            }
            else if (audioSource != null)
            {
                audioSource.Pause();
            }
        }
        catch { }
        isPlaying = false;
    }

    /// <summary>
    /// Resume playback after Pause(). Restores DSP baseline so timing continues from paused audio time.
    /// </summary>
    public void Resume()
    {
        try
        {
            if (audioSync != null && audioSync.audioSource != null)
            {
                audioSync.Resume();
                // Align songPosition to audioSync's time
                songPosition = (float)(audioSync.GetAudioTime() * 1000.0) - _audioOutputLatencyMs;
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
                ResetRenderClock(songPosition);
            }
            else if (audioSource != null)
            {
                double now = AudioSettings.dspTime;
                double clipTime = audioSource.time; // seconds into clip
                audioStartDspTime = now - clipTime;
                try { audioSource.UnPause(); } catch { }
                songPosition = (float)(clipTime * 1000.0) - _audioOutputLatencyMs;
                _effectiveSongPositionCached = songPosition;
                _effectiveInitialized = true;
                ResetRenderClock(songPosition);
            }
        }
        catch { }
        isPlaying = true;
    }

    /// <summary>
    /// Set the music playback volume (0..1) on the Conductor's AudioSource. This affects music only.
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        if (audioSync != null && audioSync.audioSource != null)
        {
            audioSync.audioSource.volume = Mathf.Clamp01(volume);
            return;
        }
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
        audioSource.volume = Mathf.Clamp01(volume);
    }

    /// <summary>
    /// Starts a visual pre-roll where effectiveSongPosition advances from -delay to 0 before audio starts.
    /// </summary>
    /// <summary>
    /// Holds the chart still through the count-in and lets it in for the last
    /// second.
    /// </summary>
    /// <remarks>
    /// The pre-roll used to start the visual clock at minus the whole delay, so
    /// the notes slid in for the entire count-in -- and since the count-in is a
    /// player setting between two and five seconds, the approach the player had
    /// to read was a different length every time they changed it.
    ///
    /// The clock still spans the whole delay, because it has to arrive at zero
    /// exactly when the audio does. What is clamped is the position it reports:
    /// the chart sits one second out until there is one second left, then moves.
    /// The lead-in is therefore the same on every setting, and the count-in
    /// length only decides how long the player waits before it starts.
    /// </remarks>
    public const float DefaultVisualLeadInMs = 1000f;

    /// <summary>
    /// 進場滾多久。預設一秒，拿得到拍格的譜面改成三拍。
    /// </summary>
    /// <remarks>
    /// 固定一秒的進場在每一首歌底下都是不同的音樂長度：180 BPM 的一秒是三拍，
    /// 60 BPM 的一秒是一拍。玩家在那一秒裡要讀的是「這首歌多快」，而固定秒數
    /// 正好不講這件事。三拍就是指揮起拍 —— 滾完的那一刻是第一小節。
    /// </remarks>
    public float VisualLeadInMs { get; private set; } = DefaultVisualLeadInMs;

    /// <summary>
    /// 設定進場長度。<paramref name="ms"/> 不大於零就回到預設的一秒。
    /// </summary>
    public void SetVisualLeadIn(float ms)
    {
        VisualLeadInMs = ms > 0f ? ms : DefaultVisualLeadInMs;
    }

    public void StartVisualPreRoll(float delaySeconds)
    {
        float delayMs = Mathf.Max(0f, delaySeconds) * 1000f;
        if (delayMs <= 0f)
        {
            return;
        }
        isVisualPlaying = true;
        visualSongPosition = -delayMs;
        _effectiveSongPositionCached = visualSongPosition;
        _effectiveInitialized = true;
        ResetRenderClock(HeldVisualPosition);
    _lastPreRollDurationMs = delayMs;
    //Debug.Log($"Conductor: Visual pre-roll started for {delaySeconds:F2}s (from {visualSongPosition} ms to 0 ms)");
    }

    /// <summary>
    /// Stops the visual pre-roll and resets the visual clock.
    /// </summary>
    public void StopVisualPreRoll()
    {
        if (isVisualPlaying)
        {
            isVisualPlaying = false;
            visualSongPosition = 0f;
            //Debug.Log("Conductor: Visual pre-roll stopped.");
        }
    }

    /// <summary>
    /// Start playback without an audio clip. This advances the timing system into a 'playing' state
    /// so spawners and other gameplay systems will treat timing as active even when no audio is present.
    /// Useful for testing or when audio is intentionally omitted.
    /// </summary>
    public void StartPlayingWithoutAudio()
    {
        // Stop any visual pre-roll and mark as playing so effectiveSongPosition will advance from 0
        StopVisualPreRoll();
        isPlaying = true;
        songPosition = 0f;
        _effectiveSongPositionCached = 0f;
        _effectiveInitialized = true;
        ResetRenderClock(0f);
        hasScheduledStart = false;
        scheduledDspTime = -1.0;
        //Debug.Log("Conductor: Entered playing state without audio.");
    }



    // Reusable buffer for OnGUI to minimize allocations
    private static readonly System.Text.StringBuilder ConductorOnGuiBuffer = new System.Text.StringBuilder(256);

    /// <summary>
    /// Preload spawners (notes / beat lines) for the provided chart. This should be called after spawners are initialized
    /// and before starting playback to reduce runtime instantiation.
    /// It will search the scene for NoteSpawner and BeatLineSpawner components and call their preload methods if available.
    /// </summary>
    public void PreloadSpawners(Chart chart)
    {
        if (chart == null) return;
        // Find all NoteSpawner instances in the scene
    var noteSpawners = Object.FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var ns in noteSpawners)
        {
            try
            {
                ns.PreloadAll(chart);
            }
            catch (System.Exception)
            {
                //Debug.LogWarning("Conductor.PreloadSpawners: NoteSpawner.PreloadAll failed");
            }
        }

    var beatLineSpawners = Object.FindObjectsByType<BeatLineSpawner>(FindObjectsSortMode.None);
        foreach (var bs in beatLineSpawners)
        {
            try
            {
                bs.PreloadAllBeatLines(chart);
            }
            catch (System.Exception)
            {
                //Debug.LogWarning("Conductor.PreloadSpawners: BeatLineSpawner.PreloadAllBeatLines failed");
            }
        }
        //Debug.Log("Conductor: PreloadSpawners completed.");
    }
}
