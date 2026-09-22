using System.Diagnostics;

/// <summary>
/// 判定本身花多少 CPU：每一次按鍵、每一格判定更新。
/// </summary>
/// <remarks>
/// <see cref="HitchProbe"/> 只報超過 40ms 的格子，而 144Hz 下一格只有 6.9ms ——
/// 判定每次多花 3ms 就會掉一格，卻永遠到不了 40ms 的門檻，看起來「幀率正常但判定
/// 有點卡」。這裡把判定的成本單獨拿出來，每秒跟著 <c>[Mistouch]</c> 印一行：
///
/// <code>[JudgeCost] press n=48 avg=0.21ms max=1.90ms | update n=144 avg=0.35ms max=2.10ms</code>
///
/// press 包含這一下按鍵觸發的所有東西（判定、音效、特效），update 是
/// JudgmentManager 每格的例行工作（暫定判定、誤觸、長條、顫音）。
/// </remarks>
public static class JudgeCostProbe
{
    private static readonly double TicksToMs = 1000d / Stopwatch.Frequency;

    private static int pressCount;
    private static long pressTicks;
    private static long pressMaxTicks;

    private static int updateCount;
    private static long updateTicks;
    private static long updateMaxTicks;

    public static long Begin() => Stopwatch.GetTimestamp();

    public static void EndPress(long startTicks)
    {
        long spent = Stopwatch.GetTimestamp() - startTicks;
        pressCount++;
        pressTicks += spent;
        if (spent > pressMaxTicks) pressMaxTicks = spent;
    }

    public static void EndUpdate(long startTicks)
    {
        long spent = Stopwatch.GetTimestamp() - startTicks;
        updateCount++;
        updateTicks += spent;
        if (spent > updateMaxTicks) updateMaxTicks = spent;
    }

    /// <summary>讀出並清空。這一秒沒有按鍵時回傳 null，不洗版。</summary>
    public static string TakeReport()
    {
        if (pressCount == 0) return null;
        var b = new System.Text.StringBuilder(128);
        b.Append("[JudgeCost] press n=").Append(pressCount)
         .Append(" avg=").Append((pressTicks * TicksToMs / pressCount).ToString("0.00")).Append("ms")
         .Append(" max=").Append((pressMaxTicks * TicksToMs).ToString("0.00")).Append("ms");
        if (updateCount > 0)
        {
            b.Append(" | update n=").Append(updateCount)
             .Append(" avg=").Append((updateTicks * TicksToMs / updateCount).ToString("0.00")).Append("ms")
             .Append(" max=").Append((updateMaxTicks * TicksToMs).ToString("0.00")).Append("ms");
        }
        pressCount = updateCount = 0;
        pressTicks = pressMaxTicks = updateTicks = updateMaxTicks = 0;
        return b.ToString();
    }
}
