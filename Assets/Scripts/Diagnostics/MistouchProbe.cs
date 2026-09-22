using UnityEngine;

/// <summary>
/// 量「誤觸長什麼樣」，好讓四道防線的參數用資料訂，不是用猜的。
/// </summary>
/// <remarks>
/// 擦碰防護有四層（力度地板、<c>LooksLikeBrush</c>、誤觸吸收、早搶限制），
/// 每一層都有自己的門檻。門檻沒調對的時候，症狀都是「誤觸還是很多」，看不出
/// 是哪一層漏的——是力度地板太低？擦碰窗太短？還是差了三個半音所以 semitones=2
/// 抓不到？
///
/// 所以這裡記三件事，每一件都直接對應一個門檻：
///
/// * <c>vel</c>（力度）    → <c>minNoteOnVelocity</c> 要設多少。
/// * <c>semi</c>（半音距） → <c>brushSemitones</c> 夠不夠寬。
/// * <c>dt</c>（時間差）   → <c>brushWindowMs</c> 夠不夠長，以及**正負號**：
///   負的代表擦碰比正主**早到**，那是 <c>LooksLikeBrush</c> 結構上抓不到的
///   （它只往回看），只能靠吸收補救。
///
/// 只統計「沒被吸收、真的響出來」的那些——被上游擋掉或被吸收掉的不是問題。
/// </remarks>
public static class MistouchProbe
{
    /// <summary>最近接受過的音符，用來算誤觸離正主多遠。</summary>
    private const int HistorySize = 32;

    private struct Press
    {
        public int Pitch;
        public float Velocity01;
        public double Time;
    }

    private static readonly Press[] history = new Press[HistorySize];
    private static int historyNext;

    // 每一層擋下多少，以及漏過來多少。
    private static int acceptedCount, floorRejects, brushRejects, matchedCount;
    private static int strayAbsorbed, straySounded;

    // 漏過來的那些長什麼樣。用桶子而不是存清單：一首歌可能有上千次。
    private static readonly int[] velocityBuckets = new int[8];    // 每 16 一格 (0-127)
    private static readonly int[] semitoneBuckets = new int[8];    // 0,1,2,3,4,5,6,>=7
    private static readonly int[] earlyLateBuckets = new int[6];   // 見 DescribeDelta
    private static int noNeighbourCount;

    /// <summary>接受了一個 note-on。</summary>
    public static void Accepted(int pitch, float velocity01, double timestamp)
    {
        acceptedCount++;
        history[historyNext] = new Press { Pitch = pitch, Velocity01 = velocity01, Time = timestamp };
        historyNext = (historyNext + 1) % HistorySize;
    }

    public static void RejectedByFloor() => floorRejects++;
    public static void RejectedByBrush() => brushRejects++;
    public static void Matched() => matchedCount++;
    public static void StrayAbsorbed() => strayAbsorbed++;

    /// <summary>
    /// 一下按鍵沒配對到任何音符，也沒被吸收——真的當成誤觸響出來了。
    /// </summary>
    /// <param name="pitch">實際按下的琴鍵，裝置回報不了就傳 -1。</param>
    public static void StraySounded(int pitch, float velocity01, double timestamp)
    {
        straySounded++;
        if (velocity01 >= 0f)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(velocity01 * 127f / 16f), 0, velocityBuckets.Length - 1);
            velocityBuckets[index]++;
        }
        if (pitch < 0) { noNeighbourCount++; return; }

        // 找時間上最近的那個「正主」，算出半音距和時間差。
        int nearestSemitones = int.MaxValue;
        double nearestDelta = 0d;
        bool found = false;
        for (int i = 0; i < HistorySize; i++)
        {
            Press press = history[i];
            if (press.Velocity01 <= 0f) continue;
            if (press.Pitch == pitch && System.Math.Abs(press.Time - timestamp) < 0.0005) continue; // 自己
            double delta = (press.Time - timestamp) * 1000d;   // 正 = 正主比較晚
            if (System.Math.Abs(delta) > 120d) continue;
            int semitones = Mathf.Abs(press.Pitch - pitch);
            if (semitones >= nearestSemitones) continue;
            nearestSemitones = semitones;
            nearestDelta = delta;
            found = true;
        }

        if (!found) { noNeighbourCount++; return; }
        semitoneBuckets[Mathf.Clamp(nearestSemitones, 0, semitoneBuckets.Length - 1)]++;
        earlyLateBuckets[DeltaBucket(nearestDelta)]++;
    }

    /// <summary>
    /// 時間差分桶。**正負號比大小重要**：正的代表正主比誤觸晚到，也就是誤觸
    /// 先到——那是 <c>LooksLikeBrush</c> 結構上抓不到的方向。
    /// </summary>
    private static int DeltaBucket(double deltaMs)
    {
        if (deltaMs > 40d) return 0;    // 誤觸早到 >40ms
        if (deltaMs > 15d) return 1;    // 誤觸早到 15-40ms
        if (deltaMs > 0d) return 2;     // 誤觸早到 <15ms
        if (deltaMs > -15d) return 3;   // 誤觸晚到 <15ms
        if (deltaMs > -40d) return 4;   // 誤觸晚到 15-40ms
        return 5;                        // 誤觸晚到 >40ms
    }

    /// <summary>Reads and clears the counters.</summary>
    public static string TakeReport()
    {
        if (acceptedCount == 0 && straySounded == 0 && floorRejects == 0 && brushRejects == 0)
        {
            return null;
        }

        // 模式印在最前面：同一組數字在批次化開／關、精準／幀量化之下意義
        // 完全不同，事後回頭看沒有這個就分不清是哪一次測的。
        SettingsManager settings = SettingsManager.Instance;
        string mode = settings == null ? "?"
            : settings.InputFrameSoundOnly ? "frame/sound-only"
            : settings.InputFrameBatching
                ? (settings.UsesFrameQuantizedJudgment ? "frame/quantized" : "frame/precise")
                : "per-key";

        var b = new System.Text.StringBuilder(320);
        b.Append("[Mistouch] mode=").Append(mode).Append(' ')
         .Append("accepted=").Append(acceptedCount)
         .Append(" matched=").Append(matchedCount)
         .Append(" | blocked: floor=").Append(floorRejects).Append(" brush=").Append(brushRejects)
         .Append(" | stray: absorbed=").Append(strayAbsorbed).Append(" SOUNDED=").Append(straySounded);

        if (straySounded > 0)
        {
            b.Append("\n  vel  ");
            for (int i = 0; i < velocityBuckets.Length; i++)
                if (velocityBuckets[i] > 0) b.Append($"[{i * 16}-{i * 16 + 15}]={velocityBuckets[i]} ");
            b.Append("\n  semi ");
            for (int i = 0; i < semitoneBuckets.Length; i++)
                if (semitoneBuckets[i] > 0)
                    b.Append(i == semitoneBuckets.Length - 1 ? $"[>={i}]={semitoneBuckets[i]} " : $"[{i}]={semitoneBuckets[i]} ");
            b.Append("\n  dt   ");
            string[] labels = { "早>40ms", "早15-40", "早<15", "晚<15", "晚15-40", "晚>40" };
            for (int i = 0; i < earlyLateBuckets.Length; i++)
                if (earlyLateBuckets[i] > 0) b.Append($"[{labels[i]}]={earlyLateBuckets[i]} ");
            if (noNeighbourCount > 0) b.Append($"[附近沒有正主]={noNeighbourCount} ");
        }

        acceptedCount = floorRejects = brushRejects = matchedCount = 0;
        strayAbsorbed = straySounded = noNeighbourCount = 0;
        System.Array.Clear(velocityBuckets, 0, velocityBuckets.Length);
        System.Array.Clear(semitoneBuckets, 0, semitoneBuckets.Length);
        System.Array.Clear(earlyLateBuckets, 0, earlyLateBuckets.Length);
        return b.ToString();
    }

    public static void Reset()
    {
        for (int i = 0; i < HistorySize; i++) history[i] = default;
        historyNext = 0;
        TakeReport();
    }
}
