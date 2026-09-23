using UnityEngine;

/// <summary>
/// 本家的音符落下弧線：音符先被「拋起來」，再落到判定線上。
///
/// 反組譯 nostalgia.dll 得到的做法（見 PAN-001-2024102200_extracted/音符落下軌跡.md）：
///
///   p = clamp((C - d) / C, 0, 1)     d = 剩餘時間，C = 可視距離 → p 是 0（剛出現）到 1（判定線）
///   q = p * p                        ← 拋物線就是這個平方
///   q &lt; 0.5 : Y = 300 - 250 * sin((q / 0.5) * π/2)
///   q ≥ 0.5 : Y = 50 + 420 * (1 - cos(((q - 0.5) / 0.5) * π/2))
///
/// 那是本家 1280×720 螢幕座標（Y 向下），三個錨點是「出現 300 → 頂點 50 → 判定線 470」。
/// 這裡把它換成「離判定線平面多高」的比例：出現時已經在 0.405，中途升到 1，
/// 到線時回到 0。clone 是 3D、Z 軸負責距離，所以只借它的垂直輪廓，Z 仍然照時間線性，
/// 拍子線與判定時機都不受影響。
/// </summary>
public static class NoteArc
{
    // 本家的三個錨點（螢幕座標，Y 向下）
    const float SpawnY = 300f;
    const float ApexY = 50f;
    const float LineY = 470f;        // 720 - judge_bar_pos(250)
    const float NearSpan = LineY - ApexY;   // 420：頂點到判定線的落差

    /// <summary>出現時的高度比例（本家是 0.405，不是 0）。</summary>
    public static float SpawnHeight01 => (LineY - SpawnY) / NearSpan;

    /// <summary>頂點的高度比例（Height01 的最大值，約 1.0）。</summary>
    const float ApexHeight01 = 1f;

    /// <summary>落下曲線的形狀。</summary>
    public enum Shape
    {
        /// <summary>本家那條（反組譯得到）。不是拋物線。</summary>
        Arcade = 0,
        /// <summary>真正的二次函數。</summary>
        Parabola = 1,
    }

    /// <summary>
    /// 現在用哪一條。唯一的寫入者是 SettingsManager。
    /// </summary>
    /// <remarks>
    /// 做成靜態值而不是參數，是因為查這條曲線的有音符、長押尾巴、踏板、拍子線
    /// 四邊，各自傳參數的話漏改一個就會有兩條曲線並存——弧線長度就發生過這件事。
    /// </remarks>
    public static Shape CurrentShape = Shape.Arcade;

    /// <summary>
    /// 正規化成「頂點 = 1、判定線 = 0」的高度。畫面座標映射用的就是這個
    /// （見 NoteArcScreen）：它是**畫面高度的比例**，不是世界單位。
    /// </summary>
    public static float HeightNorm(float progress01)
    {
        return CurrentShape == Shape.Parabola
            ? ParabolaHeight01(progress01)
            : Height01(progress01) / ApexHeight01;
    }

    /// <summary>
    /// 頂點所在的進度（0 = 剛生成、1 = 判定線）。淡入要用它當終點。
    /// </summary>
    public static float ApexProgress01 =>
        CurrentShape == Shape.Parabola ? 1f - ParabolaApexU : Mathf.Sqrt(0.5f);

    /// <summary>頂點落在行程的幾成（從判定線往回算）。</summary>
    static readonly float ParabolaApexU = 1f / (1f + Mathf.Sqrt(1f - 170f / 420f));

    /// <summary>
    /// 真正的二次函數版本：h(u) = 1 − ((u−a)/a)²，u = 還剩多少行程。
    /// </summary>
    /// <remarks>
    /// 三個端點取得和本家一樣（落在線上 = 0、生成高度 = 0.405、頂點 = 1），於是
    /// a 就被決定成 0.565——頂點在行程的 56%，不是本家的 29%。這不是調得出來的，
    /// 是拋物線的性質：它的頂點一定在兩個零點的正中間，所以「頂點在 29%」和
    /// 「生成高度 0.405」湊不到同一條二次函數上。
    ///
    /// 代價是落地的垂直速度：本家 dh/du = 2π = 6.28，這條是 2/a = 3.54（56%），
    /// 也就是碰到判定線時比較平。
    /// </remarks>
    public static float ParabolaHeight01(float progress01)
    {
        float u = 1f - Mathf.Clamp01(progress01);
        float r = (u - ParabolaApexU) / ParabolaApexU;
        return Mathf.Max(0f, 1f - r * r);
    }

    /// <summary>
    /// 進度 0（剛出現）～1（判定線）對應的高度比例。
    /// 回傳 0 = 貼在判定線的平面上，1 = 弧線頂點。
    /// </summary>
    public static float Height01(float progress01)
    {
        float p = Mathf.Clamp01(progress01);
        float q = p * p;
        float y;
        if (q < 0.5f)
        {
            // 前半段：從出現位置往上拋，到頂點
            y = SpawnY - (SpawnY - ApexY) * Mathf.Sin((q / 0.5f) * Mathf.PI * 0.5f);
        }
        else
        {
            // 後半段：從頂點落到判定線
            y = ApexY + NearSpan * (1f - Mathf.Cos(((q - 0.5f) / 0.5f) * Mathf.PI * 0.5f));
        }
        return (LineY - y) / NearSpan;
    }

    // 世界座標版的位移（WorldOffset）與落地角公式（LandingAngleDeg）都已經拿掉：
    // 兩個都假設「弧高是世界單位」。現在弧高是**畫面高度的比例**，落地角由
    // NoteArcScreen 解出來的那張表決定（SlopeAtZ），不是一條閉合公式。

}
