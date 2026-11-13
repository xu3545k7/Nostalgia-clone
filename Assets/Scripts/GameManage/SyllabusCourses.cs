using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 從曲庫裡編定整本考程：技階十門課、檢定八場考卷。
/// </summary>
/// <remarks>
/// **這裡沒有隨機數。** 候選照該階的指標排序，同分先比等級、再比一個穩定雜湊，所以
/// 同一個曲庫在任何機器上、任何時候都編出同一本考程，玩家之間可以互相對照。用亂數種
/// 子的那一版看起來也一樣穩定，但種子一旦要「每次應試重抽」就變成一條單調變簡單的螺
/// 旋 —— 考不過就重抽，遲早抽到全庫最溫和的那幾份。
///
/// **整本一次編完，不是打開哪一格才編那一格。** 檢定考卷會避開已經被用掉的曲目，所
/// 以「打開才編」會讓結果取決於玩家先打開哪一格。固定順序（技階一到十，再檢定一到
/// 八）才編得出唯一的一本。
///
/// **多樣性懲罰只加在檢定上，技階沒有。** 一階的身分<b>就是</b>它的排序鍵：「顫音
/// 階」如果為了不重複而把全庫最顫的那份換掉，考卷上印的就是假的。檢定的鍵是通用的
/// （平均密度／長押比例），同一帶裡換誰都成立，所以那裡可以要求不重複 —— 實測加上
/// 之後六到八級的「歌唱曲」才不會是同一首歌的三個難度。
///
/// **等級帶湊不滿就往外放寬，並且說出來。** 這個曲庫非教學的 Lv1 譜是 0 份、Lv2 是
/// 3 份、Lv5 是 3 份。放寬到 ±3 還湊不滿就是「尚未開課」，牌面直接印缺什麼 —— 寧可
/// 承認曲庫不完整，不要假裝。
/// </remarks>
public static class SyllabusCourses
{
    /// <summary>一個 frame 花在讀分析檔上的預算。和 ChartAnalysisCache 同一個量級。</summary>
    private const double MillisecondsPerFrame = 4.0;

    private sealed class Candidate
    {
        public SongSelectionManager.SongOption Option;
        /// <summary>第幾<b>首歌</b>（不是第幾份譜）。同一門課不能出現兩次同一首。</summary>
        public int Song;
        public int Level;
        public ChartAnalysis Analysis;
        public string Key;
        /// <summary>全庫相對難度 0–100。<see cref="ScoreDifficulty"/> 在建完 pool 後填。</summary>
        public float Difficulty;
    }

    private static readonly Dictionary<string, SongSelectionManager.SongOption> byChart =
        new Dictionary<string, SongSelectionManager.SongOption>(StringComparer.OrdinalIgnoreCase);
    private static string resolvedStamp;

    /// <summary>正在編定中。UI 拿它決定要不要印「編定中…」。</summary>
    public static bool Drafting { get; private set; }

    /// <summary>
    /// 曲庫現在長什麼樣。不一樣就該重編。
    /// </summary>
    /// <remarks>
    /// 只看「有哪些譜、各是幾級」—— 改曲名或換封面不該把玩家的考卷洗掉。不讀任何檔
    /// 案，所以每次進畫面算一次是免費的。
    /// </remarks>
    public static string LibraryStamp(SongSelectionManager manager)
    {
        if (manager?.AllSongs == null) return string.Empty;
        uint hash = 2166136261u;
        int count = 0;
        var keys = new List<string>();
        foreach (SongSelectionManager.SongOption group in manager.AllSongs)
        {
            if (group == null || SongSelectionManager.IsTutorialSong(group)) continue;
            foreach (SongSelectionManager.SongOption variant in Variants(group))
            {
                if (variant == null || string.IsNullOrWhiteSpace(variant.chartFileName)) continue;
                keys.Add(Normalise(variant.chartFileName) + "@" + variant.difficultyLevel);
            }
        }
        // 排序過再雜湊：曲庫的列舉順序不保證穩定（匯入順序、檔案系統），而「同樣的
        // 一批譜」不該因為列舉順序不同就被當成換了曲庫。
        keys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            hash = Fnv(hash, keys[i]);
            count++;
        }
        // 覆寫的版次併進指紋：作者改完考程之後，每位玩家下次進畫面會自己發現對不
        // 上、自己重編。我們不能替所有人重寫檔案 —— 他們的進度不是我們的。
        return count + ":" + hash.ToString("x8") + ":r" + SyllabusOverrides.Revision;
    }

    /// <summary>考程和現在的曲庫對不上（或還沒編過）。</summary>
    public static bool NeedsDraft(SongSelectionManager manager) =>
        !SyllabusProgress.StampMatches(LibraryStamp(manager));

    /// <summary>
    /// 把一份 chartFileName 換回曲庫裡的那一份譜。找不到回 null（譜被刪掉了）。
    /// </summary>
    public static SongSelectionManager.SongOption Resolve(SongSelectionManager manager, string chartFileName)
    {
        if (string.IsNullOrWhiteSpace(chartFileName) || manager?.AllSongs == null) return null;
        string stamp = LibraryStamp(manager);
        if (!string.Equals(stamp, resolvedStamp, StringComparison.Ordinal))
        {
            byChart.Clear();
            foreach (SongSelectionManager.SongOption group in manager.AllSongs)
            {
                if (group == null) continue;
                foreach (SongSelectionManager.SongOption variant in Variants(group))
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.chartFileName)) continue;
                    byChart[Normalise(variant.chartFileName)] = variant;
                }
            }
            resolvedStamp = stamp;
        }
        return byChart.TryGetValue(Normalise(chartFileName), out SongSelectionManager.SongOption found)
            ? found
            : null;
    }

    /// <summary>
    /// 編定整本考程，把讀分析檔的成本攤在好幾個 frame 上。
    /// </summary>
    /// <remarks>
    /// 全庫掃一次是 690 份 2.5 KB 的分析檔，一次做完約有半秒的主執行緒頓。編完會寫
    /// 檔並記下曲庫的指紋，所以這是「換了曲庫才付一次」的成本，不是每次進畫面。
    /// </remarks>
    public static IEnumerator DraftRoutine(SongSelectionManager manager, Action<bool> onDone)
    {
        if (Drafting || manager?.AllSongs == null)
        {
            onDone?.Invoke(false);
            yield break;
        }

        // try/finally 包住整段：開書的那個覆蓋層可能在編到一半時被關掉，協程就跟著
        // 被 Unity 丟掉 —— 沒有 finally 的話旗標會留在 true，這個 process 之後再也
        // 不會編定。（C# 迭代器允許 try 裡面 yield，只要 finally 裡面沒有。）
        Drafting = true;
        bool completed = false;
        try
        {
            string stamp = LibraryStamp(manager);
            var pool = new List<Candidate>();
            var watch = Stopwatch.StartNew();
            int song = 0;

            foreach (SongSelectionManager.SongOption group in manager.AllSongs)
            {
                if (group == null || SongSelectionManager.IsTutorialSong(group)) continue;
                int index = song++;
                foreach (SongSelectionManager.SongOption variant in Variants(group))
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.chartFileName)) continue;
                    if (watch.Elapsed.TotalMilliseconds >= MillisecondsPerFrame)
                    {
                        yield return null;
                        watch.Restart();
                    }
                    ChartAnalysis analysis = ChartAnalysisCache.GetImmediate(variant.chartFileName);
                    // 沒有分析檔的譜不進考程：排序鍵算不出來，而一門排序鍵算不出來的
                    // 課和隨便抽三首沒有差別。
                    if (analysis == null || analysis.noteCount <= 0) continue;
                    pool.Add(new Candidate
                    {
                        Option = variant,
                        Song = index,
                        Level = Mathf.Max(1, variant.difficultyLevel),
                        Analysis = analysis,
                        Key = "chart:" + Normalise(variant.chartFileName),
                    });
                }
            }

            // 釘住的格子要靠 chartFileName 找回候選，所以先做一張表。
            var byKey = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pool.Count; i++)
                byKey[Normalise(pool[i].Option.chartFileName)] = pool[i];

            ScoreDifficulty(pool);

            var used = new HashSet<int>();
            var courses = new List<SyllabusCourse>();
            for (int i = 0; i < SyllabusLadder.Stages.Length; i++)
                courses.Add(DraftCourse(pool, byKey, SyllabusLadder.Stages[i], used));

            var papers = new List<SyllabusPaper>();
            for (int i = 0; i < SyllabusLadder.Grades.Length; i++)
                papers.Add(DraftPaper(pool, byKey, SyllabusLadder.Grades[i], used));

            SyllabusProgress.AdoptDraft(courses, papers, stamp);
            completed = true;
            Debug.Log($"[Syllabus] 考程已編定：候選 {pool.Count} 份譜，曲庫指紋 {stamp}。");
        }
        finally
        {
            Drafting = false;
        }
        onDone?.Invoke(completed);
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// 一門課：先把作者釘住的格子放進去，剩下的照排序鍵補滿。
    /// </summary>
    /// <remarks>
    /// **釘住的格子是先放、再補，不是先抽、再蓋。** 抽完才覆寫的話，被蓋掉的那一首
    /// 可能和另一格重複，而「同一門課不能出現兩次同一首」是這套抽法唯一的硬規則。先
    /// 放好再補，補的時候自然就避開了。
    /// </remarks>
    private static SyllabusCourse DraftCourse(List<Candidate> pool,
        Dictionary<string, Candidate> byKey, SyllabusLadder.Stage stage, HashSet<int> used)
    {
        var course = new SyllabusCourse { stage = stage.Index, widened = -1 };
        int low = SyllabusOverrides.LowOf(stage);
        int high = SyllabusOverrides.HighOf(stage);

        var pinned = new Candidate[SyllabusLadder.CourseSize];
        var claimed = new HashSet<int>();
        int pins = 0;
        for (int i = 0; i < pinned.Length; i++)
        {
            string pin = SyllabusOverrides.PinOf(false, stage.Index, i);
            if (string.IsNullOrEmpty(pin)) continue;
            // 釘住的譜可能已經被刪掉或改過（分析檔對不上就不在 pool 裡）。當作沒釘，
            // 那一格照常抽 —— 總比整階「尚未開課」好。
            if (!byKey.TryGetValue(Normalise(pin), out Candidate found)) continue;
            if (!claimed.Add(found.Song)) continue;
            pinned[i] = found;
            pins++;
        }

        for (int widen = 0; widen <= SyllabusLadder.MaxWidening; widen++)
        {
            List<Candidate> band = Band(pool, low - widen, high + widen);
            band.Sort((x, y) => CompareByMetric(x, y, stage.Key, stage.Ascending,
                stage.Index.ToString(), false, used));
            Candidate[] filled = Fill(pinned, band, claimed);
            if (filled == null)
            {
                if (pins < SyllabusLadder.CourseSize) continue;
                filled = pinned;   // 三格全釘就不需要曲庫
            }

            course.widened = widen;
            for (int i = 0; i < filled.Length; i++)
            {
                course.charts.Add(filled[i].Option.chartFileName);
                used.Add(filled[i].Song);
            }
            // 顯著度看三首裡最極端的那一首，不是第一首 —— 釘過之後第一首不一定是排
            // 序鍵最高的。
            float best = float.NaN;
            for (int i = 0; i < filled.Length; i++)
            {
                float value = SyllabusLadder.Measure(filled[i].Analysis, stage.Key);
                if (!float.IsNaN(value) && (float.IsNaN(best) || value > best)) best = value;
            }
            course.belowSignificance = !stage.Ascending && stage.Significance > 0f &&
                                       (float.IsNaN(best) || best < stage.Significance);
            return course;
        }
        return course;   // 尚未開課。charts 是空的，牌面會印缺什麼。
    }

    private static SyllabusPaper DraftPaper(List<Candidate> pool,
        Dictionary<string, Candidate> byKey, SyllabusLadder.Grade grade, HashSet<int> used)
    {
        var paper = new SyllabusPaper { grade = grade.Index, widened = -1 };
        string salt = "G" + grade.Index;
        int low = SyllabusOverrides.LowOf(grade);
        int high = SyllabusOverrides.HighOf(grade);

        var pinned = new Candidate[SyllabusLadder.PaperSize];
        var claimed = new HashSet<int>();
        int pins = 0;
        for (int i = 0; i < pinned.Length; i++)
        {
            string pin = SyllabusOverrides.PinOf(true, grade.Index, i);
            if (string.IsNullOrEmpty(pin)) continue;
            if (!byKey.TryGetValue(Normalise(pin), out Candidate found)) continue;
            if (!claimed.Add(found.Song)) continue;
            pinned[i] = found;
            pins++;
        }

        for (int widen = 0; widen <= SyllabusLadder.MaxWidening; widen++)
        {
            // 先把帶收成「離本級目標難度最近的那一撮」，角色才在撮裡挑。跳過這一步的
            // 話兩格都會抽到帶內最極端的那一份，而帶內最高值是中位的 3～5 倍。
            List<Candidate> band = NearTarget(Band(pool, low - widen, high + widen), grade.Target);
            var filled = (Candidate[])pinned.Clone();
            var seen = new HashSet<int>(claimed);

            // 技巧曲：整帶裡最密的一份。歌唱曲：長押佔比最高、而且不是同一首歌。
            // 兩格各用各的排序鍵，所以不能共用一次排序。
            if (filled[0] == null)
            {
                band.Sort((x, y) => CompareByMetric(x, y, SyllabusLadder.Metric.AverageDensity,
                    false, salt, true, used));
                filled[0] = FirstUnseen(band, seen);
                if (filled[0] != null) seen.Add(filled[0].Song);
            }
            if (filled[1] == null)
            {
                band.Sort((x, y) => CompareByMetric(x, y, SyllabusLadder.Metric.HoldShare,
                    false, salt, true, used));
                filled[1] = FirstUnseen(band, seen);
                if (filled[1] != null) seen.Add(filled[1].Song);
            }
            if (filled[0] == null || filled[1] == null)
            {
                if (pins < SyllabusLadder.PaperSize) continue;
                filled = pinned;
            }

            paper.widened = widen;
            for (int i = 0; i < filled.Length; i++)
            {
                paper.charts.Add(filled[i].Option.chartFileName);
                used.Add(filled[i].Song);
            }
            return paper;
        }
        return paper;
    }

    /// <summary>照排序後的候選補滿空格。補不滿回 null。</summary>
    private static Candidate[] Fill(Candidate[] pinned, List<Candidate> band, HashSet<int> claimed)
    {
        var filled = (Candidate[])pinned.Clone();
        var seen = new HashSet<int>(claimed);
        int at = 0;
        for (int i = 0; i < filled.Length; i++)
        {
            if (filled[i] != null) continue;
            while (at < band.Count && seen.Contains(band[at].Song)) at++;
            if (at >= band.Count) return null;
            filled[i] = band[at];
            seen.Add(band[at].Song);
            at++;
        }
        return filled;
    }

    private static Candidate FirstUnseen(List<Candidate> band, HashSet<int> seen)
    {
        for (int i = 0; i < band.Count; i++)
            if (!seen.Contains(band[i].Song)) return band[i];
        return null;
    }

    /// <summary>
    /// 給每份譜一個 0–100 的全庫相對難度。
    /// </summary>
    /// <remarks>
    /// **為什麼不用標註的等級。** 實測這個曲庫，等級和實際難度的關係會斷：
    /// Lv6 的速度中位比 Lv5 低、Lv9 比 Lv8 低，而 <c>Conflict (VILA Remix) Normal</c>
    /// 標 Lv4 的速度負荷比 Lv13 的中位還高。拿等級當排序鍵，考題就會徽到這些譜。
    ///
    /// 量的是 <c>speedLoad</c> 與 <c>staminaLoad</c> 的**全庫百分位**，0.35 : 0.65。
    /// 權重偏耐力是量出來的：和標註等級的秩相關，只看速度 0.70、只看耐力 0.92、
    /// 這個混合 0.90 —— 二者都要（快和久是兩回事），但耐力更能代表一份譜的分量。
    ///
    /// 百分位是對**這個曲庫**算的，不是寫死的常數：曲庫改了指紋就會變，整本
    /// 考程重編，這個分數跟著重算。
    /// </remarks>
    private static void ScoreDifficulty(List<Candidate> pool)
    {
        if (pool.Count == 0) return;

        var speed = new float[pool.Count];
        var stamina = new float[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            speed[i] = pool[i].Analysis.speedLoad;
            stamina[i] = pool[i].Analysis.staminaLoad;
        }
        Array.Sort(speed);
        Array.Sort(stamina);

        for (int i = 0; i < pool.Count; i++)
        {
            float a = LowerRank(speed, pool[i].Analysis.speedLoad);
            float b = LowerRank(stamina, pool[i].Analysis.staminaLoad);
            pool[i].Difficulty = 100f * (0.35f * a + 0.65f * b);
        }
    }

    /// <summary>排好的陣列裡有幾成比 <paramref name="value"/> 小。</summary>
    private static float LowerRank(float[] sorted, float value)
    {
        int low = 0;
        int high = sorted.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (sorted[mid] < value) low = mid + 1;
            else high = mid;
        }
        return low / (float)sorted.Length;
    }

    /// <summary>
    /// 帶內離目標難度最近的那一小撮。角色（技巧曲／歌唱曲）在這一撮裡再挑。
    /// </summary>
    /// <remarks>
    /// 不是「只取最近的一份」：那樣兩格會幾乎沒有選擇，也没有空間讓角色、多樣性
    /// 和釘住的格子發揮。一成八或至少六份，夠兩格各自挑且不撞同一首。
    /// </remarks>
    private static List<Candidate> NearTarget(List<Candidate> band, int target)
    {
        int want = Mathf.Max(6, Mathf.RoundToInt(band.Count * 0.18f));
        if (band.Count <= want) return band;

        var near = new List<Candidate>(band);
        near.Sort((x, y) =>
        {
            float dx = Mathf.Abs(x.Difficulty - target);
            float dy = Mathf.Abs(y.Difficulty - target);
            if (dx != dy) return dx.CompareTo(dy);
            return string.CompareOrdinal(x.Key, y.Key);   // 全序，不要靠不穩定排序
        });
        near.RemoveRange(want, near.Count - want);
        return near;
    }

    private static List<Candidate> Band(List<Candidate> pool, int low, int high)
    {
        var band = new List<Candidate>();
        for (int i = 0; i < pool.Count; i++)
            if (pool[i].Level >= low && pool[i].Level <= high) band.Add(pool[i]);
        return band;
    }

    /// <summary>同一首歌只取一份譜。</summary>
    private static List<Candidate> TakeDistinct(List<Candidate> sorted, int want)
    {
        var picked = new List<Candidate>(want);
        var seen = new HashSet<int>();
        for (int i = 0; i < sorted.Count && picked.Count < want; i++)
            if (seen.Add(sorted[i].Song)) picked.Add(sorted[i]);
        return picked;
    }

    /// <summary>
    /// 排序：（可選的多樣性懲罰）→ 指標 → 等級 → 穩定雜湊 → 字串。
    /// </summary>
    /// <remarks>
    /// 最後那一道字串比較不是裝飾：<c>List.Sort</c> 不穩定，而排序鍵是浮點數，很多
    /// 譜在同一個指標上是 0（低階的技法比例、沒有 technique data 的譜）。少了一個全
    /// 序，同一個曲庫在不同機器上會排出不同的順序，「任何機器抽出同一本」就不成立。
    /// </remarks>
    private static int CompareByMetric(Candidate a, Candidate b, SyllabusLadder.Metric metric,
        bool ascending, string salt, bool penaliseUsed, HashSet<int> used)
    {
        if (penaliseUsed)
        {
            int ua = used.Contains(a.Song) ? 1 : 0;
            int ub = used.Contains(b.Song) ? 1 : 0;
            if (ua != ub) return ua - ub;
        }

        float va = SyllabusLadder.Measure(a.Analysis, metric);
        float vb = SyllabusLadder.Measure(b.Analysis, metric);
        // NaN 排到最後，不管遞增遞減 —— 它代表「量不出來」，不是「值很小」。
        if (float.IsNaN(va) != float.IsNaN(vb)) return float.IsNaN(va) ? 1 : -1;
        if (!float.IsNaN(va) && va != vb) return ascending ? va.CompareTo(vb) : vb.CompareTo(va);

        if (a.Level != b.Level) return a.Level.CompareTo(b.Level);
        uint ha = Fnv(Fnv(2166136261u, salt + "|"), a.Key);
        uint hb = Fnv(Fnv(2166136261u, salt + "|"), b.Key);
        if (ha != hb) return ha.CompareTo(hb);
        return string.CompareOrdinal(a.Key, b.Key);
    }

    private static IEnumerable<SongSelectionManager.SongOption> Variants(
        SongSelectionManager.SongOption group)
    {
        if (group?.difficultyVariants != null && group.difficultyVariants.Count > 0)
            return group.difficultyVariants;
        return new[] { group };
    }

    private static string Normalise(string chartFileName) =>
        chartFileName.Replace('\\', '/').Trim().ToLowerInvariant();

    private static uint Fnv(uint hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            // UTF-8 位元組，好讓 C# 和編定原型（Python）的結果逐位元相同。
            char c = text[i];
            if (c < 0x80)
            {
                hash = (hash ^ c) * 16777619u;
            }
            else
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(c.ToString());
                for (int b = 0; b < bytes.Length; b++) hash = (hash ^ bytes[b]) * 16777619u;
            }
        }
        return hash;
    }

    /// <summary>一門課的容錯點。三首共用一份，曲間不回補。</summary>
    public static int AllowanceFor(SongSelectionManager manager, SyllabusCourse course)
    {
        SyllabusLadder.Stage stage = SyllabusLadder.StageAt(course?.stage ?? 0);
        if (stage == null || course?.charts == null) return SyllabusLadder.MinimumAllowance;
        int notes = 0;
        for (int i = 0; i < course.charts.Count; i++)
        {
            SongSelectionManager.SongOption option = Resolve(manager, course.charts[i]);
            ChartAnalysis analysis = option != null
                ? ChartAnalysisCache.GetImmediate(option.chartFileName)
                : null;
            if (analysis != null) notes += analysis.noteCount;
        }
        return Mathf.Max(SyllabusLadder.MinimumAllowance,
            Mathf.CeilToInt(notes * SyllabusOverrides.RateOf(stage)));
    }
}
