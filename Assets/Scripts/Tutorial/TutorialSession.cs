using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 新手教學：一首歌的資料夾裡有 <c>tutorial.json</c> 就是教學（單課或全課程）。
/// </summary>
/// <remarks>
/// 課程分成「示範段」和「遊玩段」（由 qt_editor/build_tutorial_k265.py 產生）。
///
/// * **示範段**的音符走自動演奏那條路自己彈（<see cref="NoteData.tutorialDemo"/>），
///   不進分數、連段和演奏會評分；這段時間的按鍵整個不判定，所以玩家跟著比劃
///   不會搶走示範的音符，也不會被記成誤觸。
/// * **遊玩段**是一般的譜面，結算畫面的分數只來自這些音符。
/// * 標了 <c>recital</c> 的段落強制用演奏會模式判定，只在那一段有效，不改玩家設定。
///   全課程裡只有最後一課是演奏會，所以這是依時間判斷的，不是整首歌一個開關。
///
/// 段落時間和音符一起跟著練習速度伸縮，否則慢速練習時段落會對不上音符。
/// </remarks>
public static class TutorialSession
{
    public const string FileName = "tutorial.json";

    /// <summary>示範段前後多擋這麼久的按鍵：提早按會「預訂」判定，要擋在預訂窗外。</summary>
    public const float InputGuardMs = 200f;

    [Serializable]
    public class Segment
    {
        public string mode;
        public int startMs;
        public int endMs;
        public string chartBars;
        public string sourceBars;
        public string caption;
        /// <summary>第幾課（全課程裡用來分課）。</summary>
        public int lesson;
        /// <summary>只有一課的第一段有：課名和這一課要學什麼。</summary>
        public string lessonTitle;
        /// <summary>顯示用的完整課名，例如「進階 4：高速八度」。舊檔沒有就用「教學 N：課名」。</summary>
        public string lessonLabel;
        public string goal;
        public bool recital;
        /// <summary>段落開始前敲三下的一拍長度（ms）。0 = 舊檔，不敲。</summary>
        public int beatMs;

        public bool IsDemo => string.Equals(mode, "demo", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class Lesson
    {
        public int version;
        public int lesson;
        public string title;
        public string source;
        public float bpm;
        public bool recital;
        public string goal;
        public List<Segment> segments = new List<Segment>();
    }

    public static Lesson Current { get; private set; }
    public static bool IsActive => Current != null;

    // 演奏會的生效區間：從前一段結束（或曲首）到這一段結束後一秒。
    private static readonly List<Vector2> recitalWindows = new List<Vector2>();
    private static int recitalFrame = -1;
    private static bool recitalNow;

    /// <summary>
    /// 現在這一刻要不要強制演奏會模式。每一幀只算一次——音符每幀都會問。
    /// </summary>
    public static bool ForcesRecitalNow
    {
        get
        {
            if (Current == null || recitalWindows.Count == 0) return false;
            int frame = Time.frameCount;
            if (frame == recitalFrame) return recitalNow;
            recitalFrame = frame;
            recitalNow = false;
            Conductor conductor = GameManager.Instance != null ? GameManager.Instance.Conductor : null;
            if (conductor == null) return false;
            float songPos = conductor.effectiveSongPosition;
            for (int i = 0; i < recitalWindows.Count; i++)
            {
                if (songPos >= recitalWindows[i].x && songPos <= recitalWindows[i].y)
                {
                    recitalNow = true;
                    break;
                }
            }
            return recitalNow;
        }
    }

    /// <summary>
    /// 每次譜面載入完都呼叫一次（練習速度伸縮之後）。不是教學的曲子會清掉上一課。
    /// </summary>
    public static void Attach(Chart chart, string chartFile, float timeFactor)
    {
        Current = null;
        recitalWindows.Clear();
        recitalFrame = -1;
        Lesson lesson = TryLoad(chartFile);
        if (lesson == null || lesson.segments == null || lesson.segments.Count == 0)
        {
            TutorialOverlay.Hide();
            return;
        }

        lesson.segments.RemoveAll(s => s == null || s.endMs <= s.startMs);
        foreach (Segment segment in lesson.segments)
        {
            if (timeFactor > 0.0001f && !Mathf.Approximately(timeFactor, 1f))
            {
                segment.startMs = Mathf.RoundToInt(segment.startMs * timeFactor);
                segment.endMs = Mathf.RoundToInt(segment.endMs * timeFactor);
                segment.beatMs = Mathf.RoundToInt(segment.beatMs * timeFactor);
            }
            // 第一版的 tutorial.json 只有整課一個 recital。
            if (lesson.recital) segment.recital = true;
            if (segment.lesson <= 0) segment.lesson = lesson.lesson;
        }
        lesson.segments.Sort((a, b) => a.startMs.CompareTo(b.startMs));
        if (string.IsNullOrEmpty(lesson.segments[0].lessonTitle))
        {
            lesson.segments[0].lessonTitle = lesson.title;
            lesson.segments[0].goal = lesson.goal;
        }

        for (int i = 0; i < lesson.segments.Count; i++)
        {
            Segment segment = lesson.segments[i];
            if (!segment.recital) continue;
            float from = i == 0 ? 0f : lesson.segments[i - 1].endMs;
            recitalWindows.Add(new Vector2(from, segment.endMs + 1000f));
        }

        int demo = 0;
        if (chart != null && chart.notes != null)
        {
            foreach (NoteData note in chart.notes)
            {
                if (note == null) continue;
                note.tutorialDemo = IsDemoTime(lesson, note.startTime);
                if (note.tutorialDemo) demo++;
            }
        }

        // 強弱分界只看演奏會段落的音。全課程裡其他課的力度都壓在一個窄範圍，整份一起量
        // 會量不出強弱（measured=False），強弱控制那一課的演奏會特效就整個不見。
        if (chart != null && chart.notes != null && recitalWindows.Count > 0)
        {
            var recitalNotes = new List<NoteData>();
            foreach (NoteData note in chart.notes)
            {
                if (note == null) continue;
                foreach (Segment segment in lesson.segments)
                {
                    if (segment.recital && note.startTime >= segment.startMs && note.startTime <= segment.endMs)
                    {
                        recitalNotes.Add(note);
                        break;
                    }
                }
            }
            if (recitalNotes.Count > 0) VelocityBands.Prepare(recitalNotes, "tutorial-recital");
        }

        Current = lesson;
        TutorialOverlay.Show(lesson);
        Debug.Log($"[Tutorial] lesson={lesson.lesson} '{lesson.title}' segments={lesson.segments.Count} " +
                  $"demoNotes={demo}/{chart?.notes?.Count ?? 0} recitalWindows={recitalWindows.Count} " +
                  $"factor={timeFactor:0.###}");
    }

    /// <summary>這一刻的按鍵要不要整個略過（示範段加前後保護）。</summary>
    public static bool BlocksInput(float songPosMs)
    {
        Lesson lesson = Current;
        if (lesson == null) return false;
        foreach (Segment segment in lesson.segments)
        {
            if (!segment.IsDemo) continue;
            if (songPosMs >= segment.startMs - InputGuardMs && songPosMs <= segment.endMs + InputGuardMs)
                return true;
        }
        return false;
    }

    /// <summary>課程對演奏會模式的規定：整份都是、整份都不是、或只有其中幾段是。</summary>
    public enum RecitalRule { None, Always, Partly }

    private static readonly Dictionary<string, RecitalRule?> describeCache =
        new Dictionary<string, RecitalRule?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 選歌畫面用：這份譜面是不是教學，是的話演奏會模式怎麼規定。結果快取，選歌
    /// 畫面每換一張卡片都會問。
    /// </summary>
    public static bool TryDescribe(string chartFile, out RecitalRule rule)
    {
        rule = RecitalRule.None;
        if (string.IsNullOrEmpty(chartFile)) return false;
        if (!describeCache.TryGetValue(chartFile, out RecitalRule? cached))
        {
            cached = null;
            Lesson lesson = TryLoad(chartFile);
            if (lesson != null && lesson.segments != null && lesson.segments.Count > 0)
            {
                int recital = 0;
                foreach (Segment segment in lesson.segments)
                    if (segment != null && (segment.recital || lesson.recital)) recital++;
                cached = recital == 0 ? RecitalRule.None
                    : recital == lesson.segments.Count ? RecitalRule.Always
                    : RecitalRule.Partly;
            }
            describeCache[chartFile] = cached;
        }
        if (cached == null) return false;
        rule = cached.Value;
        return true;
    }

    private static bool IsDemoTime(Lesson lesson, int timeMs)
    {
        foreach (Segment segment in lesson.segments)
            if (segment.IsDemo && timeMs >= segment.startMs && timeMs < segment.endMs) return true;
        return false;
    }

    private static Lesson TryLoad(string chartFile)
    {
        try
        {
            string local = ExternalSongLibrary.ToLocalPath(chartFile);
            if (string.IsNullOrEmpty(local)) return null;
            // 譜面在 <曲目>/<難度>/<名稱>，tutorial.json 在曲目資料夾。往上找兩層，
            // 不寫死層數：匯出工具也可能把譜面直接放在曲目資料夾。
            string dir = Path.GetDirectoryName(local);
            for (int depth = 0; depth < 3 && !string.IsNullOrEmpty(dir); depth++)
            {
                string path = Path.Combine(dir, FileName);
                if (File.Exists(path))
                    return JsonUtility.FromJson<Lesson>(File.ReadAllText(path));
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Tutorial] tutorial.json 讀取失敗：{e.Message}");
        }
        return null;
    }
}
