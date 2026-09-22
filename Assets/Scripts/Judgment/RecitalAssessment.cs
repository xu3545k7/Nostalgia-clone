using UnityEngine;

namespace Judgment
{
    /// <summary>
    /// The examiner's marks for a recital: four criteria out of a hundred, their
    /// average, and what that average is worth.
    /// </summary>
    /// <remarks>
    /// **Why four numbers and not one.** A single score answers "how well did
    /// that go" with a number whose scale nobody knows. An examiner's report
    /// says *which part* went well: you played the right notes but not cleanly;
    /// you were clean but never on the middle key; the pedal was late all night.
    /// Those are four different practice sessions tomorrow.
    ///
    /// **Why the ranges are what they are.** Every one is calibrated so that the
    /// middle of the band is a real performance rather than a perfect one --
    /// a mark out of a hundred that only moves in its top two percent is a
    /// disguised pass/fail. The constants are in one place here so they can be
    /// argued with as a set.
    /// </remarks>
    public static class RecitalAssessment
    {
        /// <summary>Below this score the piece was not really played through.</summary>
        private const float ScoreFloor = 600000f;
        /// <summary>At this score completeness is full.</summary>
        private const float ScoreCeiling = 950000f;
        public const float PassMark = 75f;
        public const float MeritMark = 85f;
        public const float DistinctionMark = 95f;

        public readonly struct Report
        {
            /// <summary>Did the player get through the piece: read from the score.</summary>
            public readonly float completeness;
            /// <summary>
            /// 旋律（本家的 over3key）：有沒有一掌拍下去 —— 同一幀 3 鍵以上按同一顆音符。
            /// </summary>
            public readonly float melody;
            /// <summary>
            /// 失誤錯音（本家的 mistouch）：判定窗內有音符時，按在所有音符範圍外的次數。
            /// </summary>
            public readonly float mistakes;
            /// <summary>Pedal and dynamics.</summary>
            public readonly float expression;
            public readonly float average;
            public readonly bool measured;

            public Report(float completeness, float melody, float mistakes, float expression,
                bool measured)
            {
                this.completeness = completeness;
                this.melody = melody;
                this.mistakes = mistakes;
                this.expression = expression;
                this.measured = measured;
                average = (completeness + melody + mistakes + expression) * 0.25f;
            }

            /// <summary>PASS / MERIT / DISTINCTION, or nothing if it did not pass.</summary>
            public string Grade =>
                average >= DistinctionMark ? "DISTINCTION"
                : average >= MeritMark ? "MERIT"
                : average >= PassMark ? "PASS"
                : string.Empty;
        }

        /// <summary>
        /// Marks one performance.
        /// </summary>
        /// <param name="pedalTotal">
        /// Notes whose pedalling could be judged. Zero means the player was not
        /// pedalling this run (no pedal data, or the pedal is being played by
        /// the chart), and the criterion falls back to full marks rather than to
        /// zero -- an examiner does not mark down what was never asked for.
        /// </param>
        /// <param name="dynamicTotal">
        /// Notes whose dynamics could be judged at all. Zero means this run had
        /// nothing to judge -- a chart with no restored velocity, or an input
        /// device with no touch -- and the half falls back to full marks for the
        /// same reason the pedal half does: an examiner does not mark down what
        /// was never asked for.
        /// </param>
        public static Report Evaluate(int score, int notes, int over3Key, float melodyAllowance,
            int mistouch, float mistouchAllowance, int pedalCorrect, int pedalTotal,
            int dynamicCorrect, int dynamicTotal)
        {
            if (notes <= 0) return new Report(0f, 0f, 0f, 0f, false);

            float completeness = Mathf.Clamp01((score - ScoreFloor) / (ScoreCeiling - ScoreFloor)) * 100f;

            // 本家的旋律分：100 − 10 × 次數 ÷ 容許量。容許量依音符種類給，見 MelodyAllowance。
            float melody = OfficialMark(over3Key, melodyAllowance);

            // 本家的誤觸分，同一條公式，容許量換一組率。以前是「誤觸率超過 2% 開始扣、
            // 35% 歸零」；本家是每次固定扣 10 ÷ A，音符越少的譜每一次扣得越重。
            float mistakes = OfficialMark(mistouch, mistouchAllowance);

            // 力度和踏板各占這一項的一半。兩者都是「有資料才評」：沒資料時給滿
            // 分而不是零分 —— 沒有被要求的東西不該被扣分。
            float dynamics = dynamicTotal > 0
                ? dynamicCorrect / (float)dynamicTotal * 100f
                : 100f;
            float pedal = pedalTotal > 0 ? pedalCorrect / (float)pedalTotal * 100f : 100f;
            float expression = (dynamics + pedal) * 0.5f;

            return new Report(completeness, melody, mistakes, expression, true);
        }

        /// <summary>本家的扣分公式：100 − 10 × 次數 ÷ 容許量。沒有容許量（空譜）給滿分。</summary>
        private static float OfficialMark(int count, float allowance)
        {
            return allowance > 0f ? Mathf.Clamp(100f - 10f * count / allowance, 0f, 100f) : 100f;
        }

        /// <summary>
        /// 本家 recital_criteria.xml 的 pitch 容許率：每幾顆音符容許一次「一掌拍下去」。
        /// </summary>
        /// <remarks>
        /// 普通音符很寬（140 顆一次），滑奏、顫音很窄（5、10 顆一次）——它們本來就寬，
        /// 手很容易多碰到鍵。容許量 A = Σ 各種類音符數 ÷ 各自的率，扣分是每次 10 ÷ A 分。
        /// </remarks>
        private const float MelodyRateNormal = 140f;
        private const float MelodyRateLong = 75f;
        private const float MelodyRateSlide = 5f;
        private const float MelodyRateTrill = 10f;

        public static float MelodyAllowance(System.Collections.Generic.IList<NoteData> notes)
        {
            return Allowance(notes, MelodyRateNormal, MelodyRateLong, MelodyRateSlide, MelodyRateTrill);
        }

        /// <summary>
        /// 本家 recital_criteria.xml 的 mistouch 容許率：每幾顆音符容許一次誤觸。
        /// </summary>
        /// <remarks>比旋律嚴得多（普通音符 55 顆一次），但滑奏、顫音反而比較寬。</remarks>
        private const float MistouchRateNormal = 55f;
        private const float MistouchRateLong = 45f;
        private const float MistouchRateSlide = 25f;
        private const float MistouchRateTrill = 40f;

        public static float MistouchAllowance(System.Collections.Generic.IList<NoteData> notes)
        {
            return Allowance(notes, MistouchRateNormal, MistouchRateLong, MistouchRateSlide, MistouchRateTrill);
        }

        /// <summary>容許量 A = Σ 各種類音符數 ÷ 各自的率。隱藏音符不用按，不算。</summary>
        private static float Allowance(System.Collections.Generic.IList<NoteData> notes,
            float rateNormal, float rateLong, float rateSlide, float rateTrill)
        {
            if (notes == null) return 0f;
            int normal = 0, hold = 0, slide = 0, trill = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                NoteData nd = notes[i];
                if (nd == null || nd.hidden) continue;
                string type = nd.type ?? string.Empty;
                if (nd.note_type == 4 || string.Equals(type, "slide", System.StringComparison.OrdinalIgnoreCase)) slide++;
                else if (nd.note_type == 64 || string.Equals(type, "trill", System.StringComparison.OrdinalIgnoreCase)) trill++;
                else if (nd.note_type == 2 || nd.note_type == 3 ||
                         string.Equals(type, "hold", System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "staccato", System.StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "stac", System.StringComparison.OrdinalIgnoreCase)) hold++;
                else normal++;
            }
            return normal / rateNormal + hold / rateLong + slide / rateSlide + trill / rateTrill;
        }
    }
}
