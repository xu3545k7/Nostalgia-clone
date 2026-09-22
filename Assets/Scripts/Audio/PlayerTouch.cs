using UnityEngine;

/// <summary>
/// 把玩家的觸鍵力度換算到這首曲子的力度尺上。
/// </summary>
/// <remarks>
/// **為什麼需要換算。** MIDI 的 0..127 不是一個絕對的音量單位，它是「這台琴的這個
/// 人此刻按得多重」。譜面的力度來自另一個人、另一台琴、另一條力度曲線。實測：這
/// 位玩家的中間值是 37，而譜面是 102 —— 差 65。
///
/// 不換算的話兩件事同時壞掉：
///
/// * **聽感**：Hardcore 模式直接拿原始力度去選取樣層，37 落在 −21 dBFS 的那一層，
///   而伴奏在 −11 dBFS。鋼琴天生就比伴奏低 10 dB，玩家的結論是「聽不到鋼琴」。
/// * **評分**：同一個 37 拿去和譜面的分界比，永遠落在「中」那一段，於是強弱判定
///   要嘛全對要嘛全錯，看那首曲子的分界剛好落在哪裡。
///
/// **為什麼判定和發聲必須共用這一份。** 它們對同一下觸鍵必須有同一個理解 ——
/// 否則會出現「聽起來是重擊、評分卻說你彈輕了」，而那種矛盾無法靠練習修正。
/// 這和音符外殼與力度評分共用 <see cref="VelocityBands"/> 是同一個原則。
///
/// **換算保留相對關係。** 平移對齊中心、縮放對齊幅度，所以「比自己平常重」仍然
/// 是「比平常重」—— 變的只是它被放在哪一把尺上讀。
/// </remarks>
public static class PlayerTouch
{
    /// <summary>
    /// 追蹤得多慢。
    /// </summary>
    /// <remarks>
    /// 每一下只移動 2%，所以它跟的是這個人的**手勁**，不是剛剛那個樂句的強弱。
    /// 跟太快的話，一段強奏會把基準整個抬上去，接下來正常力度的音符就全部被判
    /// 成弱、也全部被彈得太小聲。
    /// </remarks>
    private const float Rate = 0.02f;

    /// <summary>沒有譜面分界可對照時的退路。</summary>
    private const float FallbackMiddle = 90f;
    private const float FallbackSpread = 20f;

    /// <summary>幅度極窄的時候不要無限放大，否則手抖就變成強弱。</summary>
    private const float MinScale = 0.5f;
    private const float MaxScale = 4f;

    /// <summary>幅度的下限。避免剛開始的幾下把倍率推到天上去。</summary>
    private const float MinSpread = 3f;

    private static float level = -1f;
    private static float spread = FallbackSpread;

    /// <summary>玩家自己的中間力度。還沒有樣本的時候是負的。</summary>
    public static float Level => level;

    /// <summary>玩家一下平常偏離自己中間值多少。</summary>
    public static float Spread => spread;

    /// <summary>有沒有累積到可用的樣本。</summary>
    public static bool Known => level >= 0f;

    /// <summary>
    /// 記下一次真正的觸鍵。
    /// </summary>
    /// <remarks>
    /// **只有輸入層該呼叫這個。** 判定和發聲都會對同一下觸鍵各看一次，兩邊都記
    /// 的話這個平均會用兩倍的速度跑 —— 而且跑得多快取決於那一下有沒有被判定、
    /// 有沒有被去重，於是同樣的演奏會得到不一樣的基準。
    /// </remarks>
    public static void Observe(float velocity01)
    {
        if (velocity01 < 0f) return;   // 電腦鍵盤沒有力度
        int velocity = Mathf.Clamp(Mathf.RoundToInt(velocity01 * 127f), 1, 127);
        if (level < 0f)
        {
            level = velocity;
            spread = ChartSpread();
            return;
        }
        level = Mathf.Lerp(level, velocity, Rate);
        spread = Mathf.Lerp(spread, Mathf.Abs(velocity - level), Rate);
    }

    /// <summary>
    /// 把一個原始觸鍵力度換算到譜面的尺上。還沒有基準時原樣回傳。
    /// </summary>
    public static int ToChartScale(int velocity)
    {
        velocity = Mathf.Clamp(velocity, 1, 127);
        if (level < 0f) return velocity;

        float strength = Strength();
        if (strength <= 0.001f) return velocity;

        float scale = Mathf.Clamp(ChartSpread() / Mathf.Max(MinSpread, spread),
            MinScale, MaxScale);
        float aligned = ChartMiddle() + (velocity - level) * scale;

        // 係數是在「原始觸鍵」和「完全對齊」之間插值，不是乘在結果上。
        //
        // 這樣 0 就真的是「什麼都不做」（手輕就是小聲，最真實），1 是「完全照這
        // 首曲子的尺讀」，中間是連續的。乘在結果上的話 0 會變成靜音，那不是任何
        // 人想要的東西。
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(velocity, aligned, strength)), 1, 127);
    }

    /// <summary>
    /// 對齊多少。0 = 照原樣（手輕就是小聲），1 = 完全換算到這首曲子的尺上。
    /// </summary>
    private static float Strength()
    {
        try
        {
            var settings = SettingsManager.Instance;
            return settings != null ? settings.TouchAlignment : 1f;
        }
        catch { return 1f; }
    }

    /// <summary>這首曲子的中間力度。沒有量到分界就用一個健康的預設值。</summary>
    private static float ChartMiddle()
    {
        return VelocityBands.Measured ? VelocityBands.Middle : FallbackMiddle;
    }

    private static float ChartSpread()
    {
        return VelocityBands.Measured
            ? Mathf.Max(MinSpread, VelocityBands.HalfSpan)
            : FallbackSpread;
    }

    /// <summary>換一首歌就重新認識。換歌、換人、換琴都該重來。</summary>
    public static void Reset()
    {
        level = -1f;
        spread = FallbackSpread;
    }
}
