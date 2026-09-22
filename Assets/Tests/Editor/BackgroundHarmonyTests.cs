using System.Collections.Generic;
using Effects;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 背景配色與調性時間軸的純邏輯測試。
/// </summary>
public class BackgroundHarmonyTests
{
    private static NoteData Note(int startMs, int pitch)
    {
        return new NoteData { startTime = startMs, endTime = startMs + 100, pitch = pitch };
    }

    private static List<int> Beats(int count, int stepMs)
    {
        var beats = new List<int>(count);
        for (int i = 0; i < count; i++) beats.Add(i * stepMs);
        return beats;
    }

    // ── HarmonyTimeline ─────────────────────────────────────────────────

    [Test]
    public void Timeline_FollowsKeyCentre_NotEveryChord()
    {
        // 前 10 秒都在 C（pitch class 0），之後換到 G（7）。中間穿插的過路音不該換色。
        var notes = new List<NoteData>();
        for (int t = 0; t < 10000; t += 250)
        {
            notes.Add(Note(t, 60));                       // C
            if (t % 1000 == 0) notes.Add(Note(t, 62));    // 偶爾出現的 D，不該搶走中心
        }
        for (int t = 10000; t < 20000; t += 250) notes.Add(Note(t, 67));   // G

        var segments = HarmonyTimeline.Build(notes, Beats(40, 500));

        Assert.AreEqual(0, segments[0].pitchClass, "開頭應該落在 C");
        Assert.AreEqual(7, segments[segments.Count - 1].pitchClass, "結尾應該落在 G");
        // 只有一次真正的轉調，不該被過路音切碎。
        Assert.LessOrEqual(segments.Count, 3, $"換色次數過多：{segments.Count}");
    }

    [Test]
    public void Timeline_NeverChangesFasterThanTheFloor()
    {
        // 每 250ms 就換一個音級的極端輸入，仍然不准換得比下限快。
        var notes = new List<NoteData>();
        for (int i = 0; i < 200; i++) notes.Add(Note(i * 250, 60 + (i % 12)));

        var segments = HarmonyTimeline.Build(notes, Beats(200, 250));

        for (int i = 1; i < segments.Count; i++)
        {
            float gap = segments[i].startMs - segments[i - 1].startMs;
            Assert.GreaterOrEqual(gap, HarmonyTimeline.MinChangeMs,
                $"第 {i} 段只隔了 {gap}ms");
        }
    }

    [Test]
    public void Timeline_IndexAt_IsClockDriven()
    {
        var segments = new List<HarmonyTimeline.Segment>
        {
            new HarmonyTimeline.Segment { startMs = 1000, pitchClass = 0 },
            new HarmonyTimeline.Segment { startMs = 5000, pitchClass = 7 },
        };
        Assert.AreEqual(-1, HarmonyTimeline.IndexAt(segments, 0f));      // 還沒開始
        Assert.AreEqual(0, HarmonyTimeline.IndexAt(segments, 1000f));
        Assert.AreEqual(0, HarmonyTimeline.IndexAt(segments, 4999f));
        Assert.AreEqual(1, HarmonyTimeline.IndexAt(segments, 5000f));
        // 倒帶回去要拿回舊的那一段——顏色不是靠事件推進的。
        Assert.AreEqual(0, HarmonyTimeline.IndexAt(segments, 2000f));
    }

    [Test]
    public void HueOffset_WalksTheCircleOfFifths_TakingTheShortWay()
    {
        // 主音自己不偏。
        Assert.AreEqual(0f, HarmonyTimeline.HueOffsetDegrees(0, 0, 10f), 0.001f);
        // C→G 是五度圈上的一步。
        Assert.AreEqual(10f, HarmonyTimeline.HueOffsetDegrees(7, 0, 10f), 0.001f);
        // C→F 是反方向一步，不是十一步。
        Assert.AreEqual(-10f, HarmonyTimeline.HueOffsetDegrees(5, 0, 10f), 0.001f);
        // 半音鄰居在五度圈上是最遠的——這正是要的：C 和 C# 聽起來一點都不像。
        // C→C# 沿五度圈是 7 步，折成走近路的 -5 步。
        Assert.AreEqual(-50f, HarmonyTimeline.HueOffsetDegrees(1, 0, 10f), 0.001f);
    }

    [Test]
    public void Tonic_IsTheKeyHeldLongest_NotTheMostSegments()
    {
        var segments = new List<HarmonyTimeline.Segment>
        {
            new HarmonyTimeline.Segment { startMs = 0, pitchClass = 3 },      // 待 20 秒
            new HarmonyTimeline.Segment { startMs = 20000, pitchClass = 8 },  // 待 3 秒
            new HarmonyTimeline.Segment { startMs = 23000, pitchClass = 8 },
        };
        Assert.AreEqual(3, HarmonyTimeline.Tonic(segments, 26000f));
    }

    // ── BackgroundPalette ───────────────────────────────────────────────

    [Test]
    public void Palette_IgnoresGreyPixels_AndKeepsTheColouredOnes()
    {
        // 大量灰階 + 少量純藍。平均色會是灰的，但識別度在那個藍上。
        var pixels = new List<Color>();
        for (int i = 0; i < 200; i++) pixels.Add(new Color(0.5f, 0.5f, 0.5f));
        for (int i = 0; i < 20; i++) pixels.Add(Color.blue);

        var sky = BackgroundPalette.FromPixels(pixels.ToArray());

        Color.RGBToHSV(Color.blue, out float blueHue, out _, out _);
        Assert.AreEqual(blueHue, sky.hue, 0.02f);
    }

    [Test]
    public void Palette_AveragesHueOnTheColourWheel_NotLinearly()
    {
        // 350° 和 10° 的線性平均是 180°（青色），正確答案是 0°（紅）。
        var a = Color.HSVToRGB(350f / 360f, 1f, 1f);
        var b = Color.HSVToRGB(10f / 360f, 1f, 1f);
        var sky = BackgroundPalette.FromPixels(new[] { a, b });

        float degrees = sky.hue * 360f;
        Assert.IsTrue(degrees < 5f || degrees > 355f, $"色相平均跑到 {degrees:F0}°");
    }

    [Test]
    public void Palette_MonochromeCover_FallsBack()
    {
        var pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.2f, 0.2f, 0.2f);
        Assert.AreEqual(BackgroundPalette.Fallback.hue, BackgroundPalette.FromPixels(pixels).hue, 0.001f);
    }

    [Test]
    public void Palette_StaysDark_HoweverBrightTheCoverIs()
    {
        var pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white * 0.99f;
        var sky = BackgroundPalette.FromPixels(pixels);
        // 判定線那條帶已經是全畫面最擠的地方，背景不准跟著封面一起變亮。
        Color.RGBToHSV(sky.horizon, out _, out _, out float value);
        Assert.Less(value, 0.2f, "背景太亮了");
    }

    [Test]
    public void Palette_ShiftHue_KeepsSaturationAndDarkness()
    {
        var sky = BackgroundPalette.Build(0.1f, 0.4f);
        var shifted = BackgroundPalette.ShiftHue(sky, 60f);
        Assert.AreEqual(sky.saturation, shifted.saturation, 0.001f);
        Color.RGBToHSV(sky.horizon, out _, out _, out float before);
        Color.RGBToHSV(shifted.horizon, out _, out _, out float after);
        Assert.AreEqual(before, after, 0.005f);
    }

    // ---- 影片背景：軌道要用螢幕座標取樣 -------------------------------------
    // 靜態曲繪用軌道 UV 是對的（圖順著跑道往遠處延伸）；影片那樣做會變成
    // 「影片播在軌道上」，跟著跑道一起動。改成螢幕座標之後，軌道上取到的正好是
    // 它擋住的那幾個畫素，和周圍的背景接得起來。

    private static Vector2 Sample(Vector4 rect, Vector2 pixel)
    {
        return new Vector2(pixel.x * rect.x + rect.z, pixel.y * rect.y + rect.w);
    }

    [Test]
    public void CoverRect_FullScreenMapsCornersToUnitSquare()
    {
        Vector4 rect = BackgroundHarmonyDriver.CoverRectFor(Vector2.zero, new Vector2(1920f, 1080f));
        Assert.AreEqual(Vector2.zero, Sample(rect, Vector2.zero));
        Assert.AreEqual(Vector2.one, Sample(rect, new Vector2(1920f, 1080f)));
        Assert.AreEqual(new Vector2(0.5f, 0.5f), Sample(rect, new Vector2(960f, 540f)));
    }

    [Test]
    public void CoverRect_LetterboxedVideoIgnoresTheBars()
    {
        // 16:9 的影片放進 21:9 的視窗，左右各留 240px 黑邊。
        Vector4 rect = BackgroundHarmonyDriver.CoverRectFor(
            new Vector2(240f, 0f), new Vector2(1680f, 1080f));
        Assert.AreEqual(0f, Sample(rect, new Vector2(240f, 0f)).x, 0.0001f);
        Assert.AreEqual(1f, Sample(rect, new Vector2(1680f, 0f)).x, 0.0001f);
        // 影片正中央對應畫面上影片區域的正中央，不是視窗的正中央。
        Assert.AreEqual(0.5f, Sample(rect, new Vector2(960f, 540f)).x, 0.0001f);
    }

    [Test]
    public void CoverRect_DegenerateRectDoesNotDivideByZero()
    {
        Vector4 rect = BackgroundHarmonyDriver.CoverRectFor(Vector2.zero, Vector2.zero);
        Vector2 uv = Sample(rect, new Vector2(100f, 100f));
        Assert.IsFalse(float.IsNaN(uv.x) || float.IsInfinity(uv.x));
        Assert.IsFalse(float.IsNaN(uv.y) || float.IsInfinity(uv.y));
    }
}
