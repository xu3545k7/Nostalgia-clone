using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把每首曲子的播放響度拉到同一個高度。
/// </summary>
/// <remarks>
/// **為什麼需要。** 量過這個曲庫的 79 首有登記的曲目：合併響度的十分之一和十分之
/// 九之間差 12.9 dB。那不是「有些曲子比較激烈」，那是**每換一首歌就要重調一次音
/// 量**。而在這個遊戲裡調音量的代價特別高：玩家的手在琴鍵上。
///
/// **量的是響度不是峰值。** 峰值正規化在這裡幾乎什麼都不做——曲庫裡 50 條音軌的
/// 峰值本來就已經是 1.0，而它們之間的實際響度還差十幾 dB。人耳聽的是持續的能量，
/// 所以取的是窗 RMS 的第 85 百分位：丟掉前奏尾奏的無聲，留下「這首曲子大部分時候
/// 有多響」。
///
/// **量的是兩條分軌的和，不是主軌。** 這一點的第一版做錯了，而且錯得很明顯：
/// 〈Alea jacta est!〉的主軌是 −17.4 dBFS，鋼琴分軌卻是 −10.2 dBFS —— 只看主軌會
/// 算出 +4.8 dB，然後把那個增益套到已經很響的鋼琴上，推成 1.73 倍再被 Clamp01 壓
/// 回來。玩家聽到的是兩條的**和**，所以量的就必須是和：改用合併響度之後同一首歌
/// 拿到的是 −1.0 dB。
///
/// **兩條共用同一個增益。** 它們是同一首歌的分軌，各自正規化會把混音毀掉。
///
/// **寧可小聲也不要破音。** 增益永遠受 <c>0.97 / max(peak)</c> 限制，而峰值取的是
/// **兩條裡較大的那個** —— 只看主軌的峰值，正是上面那個 bug 的另一半。
/// </remarks>
public static class LoudnessNormalizer
{
    /// <summary>
    /// 目標響度。
    /// </summary>
    /// <remarks>
    /// 0.27（−11.4 dBFS）是量出來的，不是挑順眼的。拿曲庫的合併響度試過：
    ///
    ///   目標       中位增益       校正後的響度散布
    ///   −11.7 dB   0.87x (−1.2)   4.4 dB
    ///   −11.4 dB   0.90x (−0.9)   4.8 dB   &lt;- 這個
    ///   −11.1 dB   0.94x (−0.6)   5.1 dB
    ///   −10.8 dB   0.97x (−0.3)   5.4 dB
    ///
    /// 原始散布是 12.9 dB。再低一點能收得更緊，但整個遊戲會跟著變小聲；再高一點
    /// 就收不攏。−11.4 dB 把散布壓到 4.8 dB，而中位只掉 0.9 dB —— 沒有人會因為這
    /// 個去動音量鍵。
    /// </remarks>
    private const float TargetRms = 0.27f;

    /// <summary>取樣幾個窗、每個窗多長。整首鋪開，不是只看開頭。</summary>
    private const int Windows = 200;
    private const int WindowFrames = 4096;

    /// <summary>低於這個的窗算無聲，不列入。前奏和尾奏不代表這首曲子多響。</summary>
    private const float SilenceFloor = 0.005f;

    /// <summary>沒有這麼多有聲的窗，這個量測就不值得相信。</summary>
    private const int MinLoudWindows = 8;

    /// <summary>增益的上下限。峰值另外還會再限一次。</summary>
    private const float MinGain = 0.4f;
    private const float MaxGain = 10f;

    /// <summary>
    /// 留給峰值的餘裕。
    /// </summary>
    /// <remarks>
    /// **0.97 是錯的，因為錄音不是輸出的全部。** 這個遊戲在放錄音的同時還在合成
    /// 鋼琴取樣（自動演奏、玩家的觸鍵、打擊聲），那些是**加上去**的。把錄音推到
    /// 距離滿刻度只剩 3% 的地方，等於宣告其餘所有聲音都沒有位置 —— 音符一密集，
    /// 總和就爆掉。
    ///
    /// 0.74 給鋼琴層留下約 2.6 dB。那不是隨便挑的：最重的那一層取樣峰值是
    /// −1.0 dBFS，兩三顆同時響就會用掉這個空間，而密集段落正是兩三顆同時響的
    /// 地方。
    /// </remarks>
    private const float PeakCeiling = 0.74f;

    private readonly struct Reading
    {
        public readonly float Rms;
        public readonly float Peak;
        public Reading(float rms, float peak) { Rms = rms; Peak = peak; }
    }

    private static readonly Dictionary<string, Reading> cache = new Dictionary<string, Reading>();

    // ── 離線量出來的增益 ─────────────────────────────────────────────────────
    //
    // 打包工具（qt_editor 的 loudness_library）照 BS.1770 量過整首歌，把增益寫進
    // register.json。有這份資料就用它，沒有才走下面那套執行時的 RMS 估算。
    //
    // **為什麼離線的比較準。** 這裡的估算取的是窗 RMS 的百分位，和人耳的加權沒有
    // 關係；離線那份是 K 加權、有閘門的 LUFS-I，而且量的是整首、不是抽樣的兩百個
    // 窗，還順便量了真峰值。執行時的版本只是「沒有量測資料時的備案」。

    /// <summary>離線量測的目標。和工具端的 DEFAULT_TARGET_LUFS 是同一個數字。</summary>
    private const float StoredTargetLufs = -14f;

    /// <summary>
    /// 兩軌各自對齊目標時，容許把原本的伴奏／鋼琴平衡改動多少。
    /// </summary>
    /// <remarks>
    /// 使用者要的是兩軌各自對齊。但這兩條是**同一首歌的分軌**，不是兩首歌：
    /// 〈Alea jacta est!〉的伴奏是 −17.4 dBFS、鋼琴是 −10.2 dBFS，完全各自對齊會
    /// 把它們之間 7 dB 的差距抹平，原本的混音就沒了。
    ///
    /// 所以各自對齊，但**平衡最多只准移動這麼多**：明顯走鐘的分軌（多半是後來用
    /// 內建音源補算出來的那種）修得回來，作者刻意做出來的強弱對比留著。
    /// </remarks>
    private const float MaxBalanceShiftDb = 3f;

    /// <summary>
    /// 離線量測算出來的兩軌增益。沒有量測資料就回 false，呼叫端改用估算。
    /// </summary>
    /// <param name="mainPath">實際當主軌播的那個檔案（鋼琴軌頂替時就是它）。</param>
    /// <param name="pianoPath">會一起播的鋼琴分軌，沒有就傳 null。</param>
    /// <param name="pianoPlays">鋼琴分軌這一局真的會響嗎。</param>
    public static bool TryStoredGains(string mainPath, string pianoPath, bool pianoPlays,
        out float mainGain, out float pianoGain)
    {
        mainGain = 1f;
        pianoGain = 1f;
        try
        {
            var settings = SettingsManager.Instance;
            if (settings != null && !settings.NormalizeMusicLoudness) return false;
        }
        catch { }

        if (!ExternalSongLibrary.TryGetTrackLoudness(mainPath, out var main)) return false;

        var pianoInfo = default(ExternalSongLibrary.TrackLoudness);
        bool hasPiano = pianoPlays && !string.IsNullOrEmpty(pianoPath)
            && !string.Equals(pianoPath, mainPath, System.StringComparison.OrdinalIgnoreCase)
            && ExternalSongLibrary.TryGetTrackLoudness(pianoPath, out pianoInfo);

        float mainDb = main.GainDb;
        float pianoDb = hasPiano ? pianoInfo.GainDb : mainDb;

        if (hasPiano)
        {
            // 平衡只准移動 MaxBalanceShiftDb。兩軌的增益差就是平衡被改動的量。
            float shift = pianoDb - mainDb;
            float clamped = Mathf.Clamp(shift, -MaxBalanceShiftDb, MaxBalanceShiftDb);
            pianoDb = mainDb + clamped;

            // 兩條一起響，峰值會相加。各自都在 −1.5 dBTP 以下，加起來仍可能破 0，
            // 所以整體再往下讓 —— 讓的是**兩條一樣多**，平衡不會因此又跑掉。
            float peakSum = Db(main.PeakDbtp + mainDb) + Db(pianoInfo.PeakDbtp + pianoDb);
            if (peakSum > PeakCeiling && peakSum > 0.0001f)
            {
                float trim = 20f * Mathf.Log10(PeakCeiling / peakSum);
                mainDb += trim;
                pianoDb += trim;
            }
        }
        else
        {
            float peak = Db(main.PeakDbtp + mainDb);
            if (peak > PeakCeiling && peak > 0.0001f)
                mainDb += 20f * Mathf.Log10(PeakCeiling / peak);
        }

        // 合成鋼琴的模式下不准把伴奏推大聲，理由和底下估算那條一樣：那些模式裡鋼琴
        // 是玩家自己彈的，推大伴奏等於把他的演奏壓下去。
        try
        {
            var settings = SettingsManager.Instance;
            if (settings != null && settings.UsesSynthesisedPiano)
            {
                mainDb = Mathf.Min(mainDb, 0f);
                pianoDb = Mathf.Min(pianoDb, 0f);
            }
        }
        catch { }

        mainGain = Db(mainDb);
        pianoGain = Db(pianoDb);
        Debug.Log($"[Loudness] stored: main {main.Lufs:0.0} LUFS {mainDb:+0.0;-0.0} dB"
            + (hasPiano ? $" | piano {pianoInfo.Lufs:0.0} LUFS {pianoDb:+0.0;-0.0} dB" : string.Empty)
            + $" (target {StoredTargetLufs:0.0})");
        return true;
    }

    private static float Db(float db) => Mathf.Pow(10f, db / 20f);

    /// <summary>
    /// 這首曲子該乘上多少。量不出來一律回 1——不知道就不要動它。
    /// </summary>
    /// <param name="main">伴奏軌。這首歌沒有的話傳鋼琴軌本身。</param>
    /// <param name="piano">
    /// 會和主軌一起播的鋼琴分軌，沒有就傳 null。**傳進來的必須是真的會播的那一
    /// 條** —— 不播的分軌算進響度只會讓校正偏低。
    /// </param>
    public static float GainFor(AudioClip main, AudioClip piano = null)
    {
        if (main == null) return 1f;
        try
        {
            var settings = SettingsManager.Instance;
            if (settings != null && !settings.NormalizeMusicLoudness) return 1f;
        }
        catch { }

        if (!TryRead(main, out Reading first)) return 1f;

        float rms = first.Rms;
        float peak = first.Peak;
        if (piano != null && piano != main && TryRead(piano, out Reading second))
        {
            // 能量相加再開根號。兩條分軌不是同一個波形的複本，所以是不相干疊加，
            // 不是把振幅直接相加。
            rms = Mathf.Sqrt(rms * rms + second.Rms * second.Rms);
            peak = Mathf.Max(peak, second.Peak);
        }

        if (rms <= 0.0001f) return 1f;
        float gain = Mathf.Clamp(TargetRms / rms, MinGain, MaxGain);
        if (peak > 0.0001f) gain = Mathf.Min(gain, PeakCeiling / peak);

        // **合成鋼琴的模式下不准把伴奏推大聲。**
        //
        // 那些模式裡鋼琴是玩家自己彈出來的，錄音只是伴奏；而伴奏不會被正規化影
        // 響的那一半（玩家的觸鍵）擋在外面。把伴奏調大等於把玩家自己的演奏往下
        // 壓 —— 沒有人會想要那個。
        //
        // 調小仍然允許：太吵的伴奏本來就該讓開。
        try
        {
            var settings = SettingsManager.Instance;
            if (settings != null && settings.UsesSynthesisedPiano) gain = Mathf.Min(gain, 1f);
        }
        catch { }

        Debug.Log($"[Loudness] {main.name}"
            + (piano != null && piano != main ? $" + {piano.name}" : string.Empty)
            + $": rms={rms:F4} ({20f * Mathf.Log10(rms):+0.0;-0.0} dBFS) peak={peak:F3} "
            + $"gain={gain:F2}x ({20f * Mathf.Log10(gain):+0.0;-0.0} dB)");
        return gain;
    }

    /// <summary>
    /// 一條音軌的響度和峰值，量過一次就記住。
    /// </summary>
    /// <remarks>
    /// **只快取量成功的。** 選歌畫面會在音檔還沒解碼完的時候就問一次，那時候必然
    /// 量不出來；把失敗也存起來的話，這首歌就永遠不會被校正了 —— 一個暫時的狀態
    /// 被寫成了永久的答案。
    /// </remarks>
    private static bool TryRead(AudioClip clip, out Reading reading)
    {
        reading = default;
        string key = clip.name + "|" + clip.samples + "|" + clip.channels;
        if (cache.TryGetValue(key, out reading)) return true;
        if (!Measure(clip, out reading)) return false;
        cache[key] = reading;
        return true;
    }

    /// <summary>
    /// 放棄量測，並且**說出為什麼**。
    /// </summary>
    /// <remarks>
    /// 這個檔案第一版的每一條放棄路徑都是靜靜回傳 1 的，於是「沒有效果」和「這首
    /// 歌量不出來」在畫面上完全一樣，只能靠猜。一個會讓功能消失卻不留記錄的分支，
    /// 等於把問題藏起來。
    /// </remarks>
    private static void Bail(string name, string why)
    {
        Debug.Log($"[Loudness] {name}: not measured — {why}.");
    }

    private static bool Measure(AudioClip clip, out Reading reading)
    {
        reading = default;
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            Bail(clip.name, $"loadState={clip.loadState}");
            return false;
        }
        int channels = Mathf.Max(1, clip.channels);
        int frames = clip.samples;
        if (frames < WindowFrames * 2)
        {
            Bail(clip.name, $"only {frames} frames");
            return false;
        }

        var buffer = new float[WindowFrames * channels];
        var loud = new List<float>(Windows);
        float peak = 0f;

        for (int i = 0; i < Windows; i++)
        {
            int offset = (int)((long)i * (frames - WindowFrames) / Windows);
            if (!clip.GetData(buffer, offset))
            {
                Bail(clip.name, $"GetData failed at frame {offset} (loadType={clip.loadType})");
                return false;
            }

            double square = 0.0;
            for (int s = 0; s < buffer.Length; s += channels)
            {
                // 多聲道先合成單聲道。左右互相抵銷的素材很少，而分別量再平均會讓
                // 寬立體聲的曲子算起來比實際響。
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += buffer[s + c];
                float mono = sum / channels;
                square += mono * mono;
                float magnitude = mono < 0f ? -mono : mono;
                if (magnitude > peak) peak = magnitude;
            }

            float rms = Mathf.Sqrt((float)(square / (buffer.Length / channels)));
            if (rms > SilenceFloor) loud.Add(rms);
        }

        if (loud.Count < MinLoudWindows)
        {
            Bail(clip.name, $"only {loud.Count} windows above the silence floor");
            return false;
        }

        loud.Sort();
        // 第 85 百分位，不是平均：平均會被安靜的段落拉下來，而玩家記得的音量是這
        // 首曲子熱鬧的時候有多響。
        float loudness = loud[Mathf.Clamp(
            Mathf.RoundToInt((loud.Count - 1) * 0.85f), 0, loud.Count - 1)];
        if (loudness <= 0.0001f)
        {
            Bail(clip.name, "measured silence");
            return false;
        }

        reading = new Reading(loudness, peak);
        return true;
    }

    /// <summary>曲庫換掉的時候把量測結果丟掉。</summary>
    public static void Forget()
    {
        cache.Clear();
    }
}
