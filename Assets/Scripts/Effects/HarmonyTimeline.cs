using System.Collections.Generic;
using UnityEngine;

namespace Effects
{
    /// <summary>
    /// 把譜面切成一段一段的「當下調性中心」，給背景顏色用。
    /// </summary>
    /// <remarks>
    /// **追的是調性中心，不是每個和弦。** 一開始是照 `generate_pedal.py` 的規則做的
    /// ——逐拍看低音的音級，換了就翻頁。那對踏板是對的，但拿來換顏色太快：實測 86 份
    /// 譜面的中位是每分鐘 37 次，密譜（Immaculate、MarbleBlue）每 300ms 就換一次，
    /// 那是閃頻不是背景。
    ///
    /// 改成看一個滑動窗內出現最多的音級 —— 也就是「現在在哪個調上」。8 秒窗、3 秒
    /// 下限實測中位每分鐘 9.3 次（約 6.5 秒一次），最忙的那首也只有 13.5 次。那個
    /// 節奏才讀得出是「房間的顏色跟著音樂變了」。
    ///
    /// 這裡刻意**不讀 pedal_data**：126 份譜面有力度但只有 103 份有踏板，其中 20 份
    /// 的踏板本身就是生成的猜測。直接從音符算，每份譜面都適用，也不會把猜測再猜一次。
    /// </remarks>
    public static class HarmonyTimeline
    {
        /// <summary>一段調性：從 startMs 開始，中心落在 pitchClass。</summary>
        public struct Segment
        {
            public float startMs;
            public int pitchClass;   // 0..11，C=0
        }

        /// <summary>統計調性中心的回看長度。</summary>
        public const int WindowMs = 8000;

        /// <summary>兩次換色至少隔這麼久。顏色抖動比聲音抖動更刺眼，下限要比踏板的 300ms 大得多。</summary>
        public const int MinChangeMs = 3000;

        public static List<Segment> Build(IReadOnlyList<NoteData> notes,
                                          IReadOnlyList<int> beats,
                                          int windowMs = WindowMs,
                                          int minChangeMs = MinChangeMs)
        {
            var result = new List<Segment>();
            if (notes == null || notes.Count == 0) return result;

            // 音符照起音排一次，之後和候選點一起單向走完。每個候選點都重掃全部音符是
            // O(N×M)，密譜（4000 音符 × 1500 拍線）就是六百萬次比較。
            var ordered = new List<NoteData>(notes.Count);
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n != null && n.pitch > 0) ordered.Add(n);
            }
            if (ordered.Count == 0) return result;
            ordered.Sort((a, b) => a.startTime.CompareTo(b.startTime));

            // 拍線是候選換色點。沒有拍線資料就退回起音，minChangeMs 仍然擋住抖動。
            var marks = new List<int>();
            if (beats != null && beats.Count > 1)
            {
                for (int i = 0; i < beats.Count; i++)
                    if (i == 0 || beats[i] != beats[i - 1]) marks.Add(beats[i]);
                marks.Sort();
            }
            else
            {
                int last = int.MinValue;
                for (int i = 0; i < ordered.Count; i++)
                    if (ordered[i].startTime != last) { marks.Add(ordered[i].startTime); last = ordered[i].startTime; }
            }
            if (marks.Count == 0) return result;

            var counts = new int[12];
            int lo = 0, hi = 0;
            int heldPc = -1;
            float lastChangeMs = float.NegativeInfinity;

            for (int i = 0; i < marks.Count; i++)
            {
                int at = marks[i];
                while (hi < ordered.Count && ordered[hi].startTime <= at)
                {
                    counts[((ordered[hi].pitch % 12) + 12) % 12]++;
                    hi++;
                }
                while (lo < hi && ordered[lo].startTime < at - windowMs)
                {
                    counts[((ordered[lo].pitch % 12) + 12) % 12]--;
                    lo++;
                }
                if (hi <= lo) continue;                  // 窗內沒有音符

                int pc = 0;
                for (int c = 1; c < 12; c++) if (counts[c] > counts[pc]) pc = c;
                if (pc == heldPc) continue;
                if (at - lastChangeMs < minChangeMs) continue;

                result.Add(new Segment { startMs = at, pitchClass = pc });
                heldPc = pc;
                lastChangeMs = at;
            }
            return result;
        }

        /// <summary>
        /// 整首歌待得最久的音級 —— 當作主音。背景的基底色屬於它，其他調性是相對它的偏移。
        /// </summary>
        public static int Tonic(IReadOnlyList<Segment> segments, float endMs)
        {
            if (segments == null || segments.Count == 0) return 0;
            var weight = new float[12];
            for (int i = 0; i < segments.Count; i++)
            {
                float until = (i + 1 < segments.Count) ? segments[i + 1].startMs : endMs;
                weight[segments[i].pitchClass] += Mathf.Max(0f, until - segments[i].startMs);
            }
            int best = 0;
            for (int pc = 1; pc < 12; pc++) if (weight[pc] > weight[best]) best = pc;
            return best;
        }

        /// <summary>
        /// 音級 → 色相偏移（度）。走的是**五度圈**而不是半音階：C→G→D 在五度圈上相鄰，
        /// 色相也就相鄰，所以近系轉調只讓顏色微微偏，遠系轉調才明顯換色。用半音距離的話
        /// C 和 C# 會差一格，但它們聽起來一點都不像。
        /// </summary>
        public static float HueOffsetDegrees(int pitchClass, int tonic, float degreesPerFifth)
        {
            int steps = (((pitchClass - tonic) * 7) % 12 + 12) % 12;   // 沿五度圈走幾步
            if (steps > 6) steps -= 12;                                // 折成 -5..6，走近路
            return steps * degreesPerFifth;
        }

        /// <summary>songMs 當下屬於第幾段，還沒開始就回 -1。</summary>
        /// <remarks>
        /// 用二分搜尋「現在」而不是靠事件推進 —— 拉時間軸、暫停、倒帶都不會讓顏色跑掉。
        /// 這是這個專案一貫的做法（踏板的視覺也是從時鐘推出來的）。
        /// </remarks>
        public static int IndexAt(IReadOnlyList<Segment> segments, float songMs)
        {
            if (segments == null || segments.Count == 0) return -1;
            if (songMs < segments[0].startMs) return -1;
            int lo = 0, hi = segments.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (segments[mid].startMs <= songMs) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }
    }
}
