using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// What a chart is actually like to play, summarised from its notes.
/// </summary>
/// <remarks>
/// Everything here is derived, never authored, so it stays correct when a chart
/// is edited or re-imported.  Charts are ~700 KB of JSON with a couple of
/// thousand notes, far too much to touch on the main thread, so this is built by
/// <see cref="ChartAnalysisCache"/> over several frames and kept for the session.
/// </remarks>
public sealed class ChartAnalysis
{
    /// <summary>Columns in the density graph. Enough shape to read, few enough to stay legible small.</summary>
    public const int BucketCount = 48;

    public int noteCount;
    public int holdCount;
    public int slideCount;
    public int trillCount;
    /// <summary>Notes that share their start time with at least one other note.</summary>
    public int chordNoteCount;
    /// <summary>Distinct start times, so the average chord size can be reported.</summary>
    public int onsetCount;
    public int rightHandCount;
    public int leftHandCount;
    /// <summary>Notes that carried usable pitch data. Some converted charts carry none.</summary>
    public int pitchedNoteCount;
    public int lowestPitch;
    public int highestPitch;
    public float lengthSeconds;
    /// <summary>Notes per second across the playable span.</summary>
    public float averageDensity;
    /// <summary>Notes in the busiest one-second window.</summary>
    public float peakDensity;

    /// <summary>False when too few notes carry pitch for technique detection to mean anything.</summary>
    public bool hasTechniqueData;
    /// <summary>Quick same-hand moves that jump or reach an octave or more.</summary>
    public float leapRatio;
    /// <summary>Notes inside a run of same-direction chord-tone steps.</summary>
    public float arpeggioRatio;
    /// <summary>Notes inside a fast, near-stepwise run.</summary>
    public float fingerRunRatio;
    /// <summary>Notes inside a detected two-pitch alternation.</summary>
    public float trillRatio;
    /// <summary>Simultaneous two-hand moments where the left hand sits above the right.</summary>
    public float handCrossRatio;
    /// <summary>Seconds the chart spends above twelve notes a second: what endurance actually costs.</summary>
    public float sustainedSeconds;
    /// <summary>
    /// Notes that begin while an earlier note of the same hand is still held --
    /// one finger sustaining while the others move.  Needs no pitch, so it works
    /// on charts that carry none.
    /// </summary>
    public float independenceRatio;
    /// <summary>Moments a hand grabs exactly an octave.</summary>
    public float octaveRatio;
    /// <summary>Notes inside a run of the same pitch struck again and again.</summary>
    public float repeatRatio;

    /// <summary>
    /// A real passage from the busiest part of the piece, so the score book can
    /// engrave the actual music instead of decorative filler.  Parallel arrays:
    /// start time in ms, MIDI pitch, and 0 for the right hand.
    /// </summary>
    public int[] excerptTimes;
    public int[] excerptPitches;
    public byte[] excerptHands;

    public bool HasExcerpt => excerptPitches != null && excerptPitches.Length > 0;
    /// <summary>
    /// The most strikes one hand makes in a second, counting a chord as one
    /// strike.  Raw finger speed, separate from how thick the writing is.
    /// </summary>
    public float peakHandRate;

    /// <summary>
    /// Busiest second of one hand counted in notes, chord members included.
    /// </summary>
    /// <remarks>
    /// <see cref="peakHandRate"/> counts strikes, so a hand hammering four-note
    /// chords scores the same as one playing single notes at the same tempo --
    /// right for "how fast are the hands moving", wrong for "how much is the
    /// hand being asked to do". Notes per onset has the opposite fault: a slow
    /// chorale of six-note chords tops it and is easy to play.
    /// </remarks>
    public float peakChordRate;

    /// <summary>Notes inside runs taken at roughly fourteen a second or faster.</summary>
    public float fastRunRatio;

    /// <summary>
    /// Doubled onsets inside fast passagework: 1-5 and 3 together in the middle
    /// of a scale, at the tempo of the single notes around it.
    /// </summary>
    public float thickRunRatio;

    /// <summary>
    /// The busiest and the quietest twelve seconds of the chart, measured the
    /// same way, so the radar can show the spread instead of only the average.
    /// </summary>
    /// <remarks>
    /// Only the fields the radar reads are filled in. Stamina is projected --
    /// "how many busy seconds if the whole piece ran like this stretch" --
    /// because a count of seconds cannot reach a chart-scale threshold inside a
    /// twelve second window; everything else the radar plots is a rate or a
    /// ratio and carries over unchanged.
    ///
    /// Null on charts analysed in-game rather than from the cache: the fallback
    /// path measures the whole chart only, so the overlay simply does not draw.
    /// </remarks>
    public ChartAnalysis peakSection;
    public ChartAnalysis quietSection;
    /// <summary>Onsets inside a run of consecutive octaves taken at speed.</summary>
    public float octaveRunRatio;
    /// <summary>Of the moves a hand makes inside 120ms, how many are octave jumps.</summary>
    public float fastLeapRatio;

    public readonly float[] rightDensity = new float[BucketCount];
    public readonly float[] leftDensity = new float[BucketCount];

    /// <summary>
    /// Tempo across the piece, one value per bucket, so it lines up with the
    /// density graph above it.
    /// </summary>
    /// <remarks>
    /// Empty on a chart analysed in-game rather than from the cache: the
    /// in-game parse skips <c>beat_timings</c> on purpose -- decoding it is most
    /// of what makes reading a chart slow -- so there is nothing to derive a
    /// tempo from. The graph simply does not draw.
    /// </remarks>
    public readonly float[] bpmCurve = new float[BucketCount];

    public bool HasBpmCurve
    {
        get
        {
            for (int i = 0; i < BucketCount; i++) if (bpmCurve[i] > 0.01f) return true;
            return false;
        }
    }
    /// <summary>Tallest combined column, for normalising the graph.</summary>
    public float peakBucketTotal;

    public float ChordRatio => noteCount > 0 ? (float)chordNoteCount / noteCount : 0f;
    /// <summary>
    /// Average notes per start time.  This is the chord metric worth reporting:
    /// across this library the "fraction of notes in a chord" sits at 0.77 for a
    /// median chart, so it separates nothing, while this ranges 1.0 to 2.7.
    /// </summary>
    public float NotesPerOnset => onsetCount > 0 ? (float)noteCount / onsetCount : 0f;
    public float RightHandRatio => noteCount > 0 ? (float)rightHandCount / noteCount : 0f;
    public float HoldRatio => noteCount > 0 ? (float)holdCount / noteCount : 0f;
    public float TechnicalRatio => noteCount > 0 ? (float)(trillCount + slideCount) / noteCount : 0f;
    /// <summary>How far the hand split departs from even, 0 to 0.5.</summary>
    public float HandBias => Mathf.Abs(RightHandRatio - 0.5f);
    /// <summary>How spiky the chart is: 1 means perfectly even, 3 means the peaks are three times the average.</summary>
    public float Burstiness => averageDensity > 0.01f ? peakDensity / averageDensity : 0f;

    /// <summary>
    /// Notes per second the chords add on top of the bare striking speed.
    /// </summary>
    /// <remarks>
    /// Measured across the library, peak chord rate and peak hand rate correlate
    /// at 0.83 -- as a radar axis of its own it would be the speed axis drawn
    /// twice. The difference correlates at -0.16, so this asks only the part
    /// speed does not already answer: how much thicker the hand's busiest second
    /// is than the notes it strikes.
    /// </remarks>
    public float ChordLoad => Mathf.Max(0f, peakChordRate - peakHandRate);
    public bool HasPitchData => pitchedNoteCount > 0;
}

/// <summary>
/// Parses charts into <see cref="ChartAnalysis"/> a slice at a time and keeps the
/// result for the rest of the session.
/// </summary>
public static class ChartAnalysisCache
{
    /// <summary>Budget per frame. Small enough that a hover never costs a dropped frame.</summary>
    private const double MillisecondsPerFrame = 2.0;
    private const int NotesBetweenTimeChecks = 256;
    /// <summary>Bump when the cache file's shape changes, so stale files are ignored.</summary>
    private const int CacheVersion = 10;
    private const string CacheSuffix = ".analysis.json";

    private static readonly Dictionary<string, ChartAnalysis> completed =
        new Dictionary<string, ChartAnalysis>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> failed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The analysis right now, from memory or straight off disk.  Reading the
    /// precomputed file is a kilobyte and needs no coroutine, so callers that
    /// cannot wait a frame -- a page turn, for instance -- can just ask.
    /// </summary>
    public static ChartAnalysis GetImmediate(string chartFileName)
    {
        if (TryGet(chartFileName, out ChartAnalysis cached)) return cached;
        ChartAnalysis loaded = TryLoadPrecomputed(chartFileName);
        if (loaded != null) completed[chartFileName] = loaded;
        return loaded;
    }

    public static bool TryGet(string chartFileName, out ChartAnalysis analysis)
    {
        analysis = null;
        return !string.IsNullOrWhiteSpace(chartFileName) &&
               completed.TryGetValue(chartFileName, out analysis);
    }

    /// <summary>True once a chart has been tried and cannot be read; stops the caller retrying forever.</summary>
    public static bool HasFailed(string chartFileName)
    {
        return !string.IsNullOrWhiteSpace(chartFileName) && failed.Contains(chartFileName);
    }

    /// <summary>
    /// Builds the analysis for one chart, spreading the work over frames.  Safe to
    /// start again for a chart already cached: it completes immediately.
    /// </summary>
    public static IEnumerator Build(string chartFileName, Action<ChartAnalysis> onComplete)
    {
        if (string.IsNullOrWhiteSpace(chartFileName)) yield break;
        if (completed.TryGetValue(chartFileName, out ChartAnalysis cached))
        {
            onComplete?.Invoke(cached);
            yield break;
        }
        if (failed.Contains(chartFileName))
        {
            onComplete?.Invoke(null);
            yield break;
        }

        // Almost always the answer is already on disk next to the chart, written
        // by qt_editor/build_chart_analysis.py.  Reading a kilobyte beats reading
        // 700, and it returns before this coroutine ever yields, so the panel
        // fills in on the same frame the pointer arrives.
        ChartAnalysis precomputed = TryLoadPrecomputed(chartFileName);
        if (precomputed != null)
        {
            completed[chartFileName] = precomputed;
            onComplete?.Invoke(precomputed);
            yield break;
        }

        // Reading the asset is the one part that has to happen on the main thread.
        string json = null;
        try
        {
            TextAsset asset = GameManager.LoadChartJsonAsset(chartFileName);
            json = asset != null ? asset.text : null;
        }
        catch (Exception ex)
        {
            BuildLogger.LogWarning($"ChartAnalysisCache: could not read '{chartFileName}': {ex.Message}");
        }

        if (string.IsNullOrEmpty(json))
        {
            failed.Add(chartFileName);
            onComplete?.Invoke(null);
            yield break;
        }

        var times = new List<int>(2048);
        var hands = new List<byte>(2048);
        var pitches = new List<int>(2048);
        var ends = new List<int>(2048);
        var analysis = new ChartAnalysis();
        int finishMs = 0;
        bool parsed = true;

        var stopwatch = Stopwatch.StartNew();
        using (var textReader = new StringReader(json))
        using (var reader = new JsonTextReader(textReader))
        {
            int sinceCheck = 0;
            while (true)
            {
                bool more;
                try
                {
                    more = reader.Read();
                }
                catch (Exception ex)
                {
                    BuildLogger.LogWarning($"ChartAnalysisCache: '{chartFileName}' is not readable JSON: {ex.Message}");
                    parsed = false;
                    break;
                }
                if (!more) break;

                if (reader.TokenType != JsonToken.PropertyName) continue;
                string property = reader.Value as string;

                if (string.Equals(property, "music_finish_time_msec", StringComparison.Ordinal))
                {
                    finishMs = reader.ReadAsInt32() ?? 0;
                    continue;
                }
                if (!string.Equals(property, "notes", StringComparison.Ordinal))
                {
                    // Skip() on a property name consumes its whole value, which is
                    // what keeps beat_timings and pedal_data from being decoded.
                    reader.Skip();
                    continue;
                }

                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) break;
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    if (reader.TokenType != JsonToken.StartObject) continue;
                    ReadOneNote(reader, analysis, times, hands, pitches, ends);

                    if (++sinceCheck < NotesBetweenTimeChecks) continue;
                    sinceCheck = 0;
                    if (stopwatch.Elapsed.TotalMilliseconds < MillisecondsPerFrame) continue;
                    yield return null;
                    stopwatch.Restart();
                }
            }
        }

        if (!parsed || times.Count == 0)
        {
            failed.Add(chartFileName);
            onComplete?.Invoke(null);
            yield break;
        }

        Summarise(analysis, times, hands, ends, finishMs);
        AnalyseTechnique(analysis, times, hands, pitches);
        completed[chartFileName] = analysis;
        // A song imported after the script last ran pays this once, never again.
        WritePrecomputed(chartFileName, analysis);
        onComplete?.Invoke(analysis);
    }

    // ---- the precomputed cache ---------------------------------------------

    /// <summary>
    /// The cache sits beside its chart rather than in one index, so it cannot go
    /// out of step with a song that was moved, renamed or re-imported.  Charts
    /// that live in Resources have no file path and simply fall back to parsing.
    /// </summary>
    private static string GetCachePath(string chartFileName)
    {
        string local = ExternalSongLibrary.ToLocalPath(chartFileName);
        if (string.IsNullOrEmpty(local)) return null;
        return local.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? local.Substring(0, local.Length - 5) + CacheSuffix
            : local + CacheSuffix;
    }

    private static ChartAnalysis TryLoadPrecomputed(string chartFileName)
    {
        string cachePath = GetCachePath(chartFileName);
        if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath)) return null;

        try
        {
            JObject json = JObject.Parse(File.ReadAllText(cachePath));
            if ((int?)json["version"] != CacheVersion) return null;

            // An edited chart must not keep reporting the old numbers.
            string chartPath = ExternalSongLibrary.ToLocalPath(chartFileName);
            if (!string.IsNullOrEmpty(chartPath) && File.Exists(chartPath) &&
                new FileInfo(chartPath).Length != ((long?)json["source_bytes"] ?? -1L))
                return null;

            var analysis = new ChartAnalysis
            {
                noteCount = (int?)json["note_count"] ?? 0,
                holdCount = (int?)json["hold_count"] ?? 0,
                slideCount = (int?)json["slide_count"] ?? 0,
                trillCount = (int?)json["trill_count"] ?? 0,
                chordNoteCount = (int?)json["chord_note_count"] ?? 0,
                onsetCount = (int?)json["onset_count"] ?? 0,
                rightHandCount = (int?)json["right_hand_count"] ?? 0,
                leftHandCount = (int?)json["left_hand_count"] ?? 0,
                pitchedNoteCount = (int?)json["pitched_note_count"] ?? 0,
                lowestPitch = (int?)json["lowest_pitch"] ?? 0,
                highestPitch = (int?)json["highest_pitch"] ?? 0,
                lengthSeconds = (float?)json["length_seconds"] ?? 0f,
                averageDensity = (float?)json["average_density"] ?? 0f,
                peakDensity = (float?)json["peak_density"] ?? 0f,
                peakBucketTotal = (float?)json["peak_bucket_total"] ?? 0f,
                hasTechniqueData = (bool?)json["has_technique"] ?? false,
                leapRatio = (float?)json["leap_ratio"] ?? 0f,
                arpeggioRatio = (float?)json["arpeggio_ratio"] ?? 0f,
                fingerRunRatio = (float?)json["finger_run_ratio"] ?? 0f,
                trillRatio = (float?)json["trill_ratio"] ?? 0f,
                handCrossRatio = (float?)json["hand_cross_ratio"] ?? 0f,
                sustainedSeconds = (float?)json["sustained_seconds"] ?? 0f,
                independenceRatio = (float?)json["independence_ratio"] ?? 0f,
                octaveRatio = (float?)json["octave_ratio"] ?? 0f,
                repeatRatio = (float?)json["repeat_ratio"] ?? 0f,
                peakHandRate = (float?)json["peak_hand_rate"] ?? 0f,
                peakChordRate = (float?)json["peak_chord_rate"] ?? 0f,
                fastRunRatio = (float?)json["fast_run_ratio"] ?? 0f,
                thickRunRatio = (float?)json["thick_run_ratio"] ?? 0f,
                octaveRunRatio = (float?)json["octave_run_ratio"] ?? 0f,
                fastLeapRatio = (float?)json["fast_leap_ratio"] ?? 0f,
            };
            if (analysis.noteCount <= 0) return null;

            analysis.peakSection = ReadSection(json["peak_section"] as JObject);
            analysis.quietSection = ReadSection(json["quiet_section"] as JObject);
            ReadExcerpt(json["excerpt"] as JArray, analysis);
            ReadDensity(json["right_density"] as JArray, analysis.rightDensity);
            ReadDensity(json["left_density"] as JArray, analysis.leftDensity);
            ReadDensity(json["bpm_curve"] as JArray, analysis.bpmCurve);
            return analysis;
        }
        catch (Exception ex)
        {
            BuildLogger.LogWarning($"ChartAnalysisCache: ignoring unreadable cache '{cachePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>One stretch of the chart, carrying only what the radar plots.</summary>
    private static ChartAnalysis ReadSection(JObject json)
    {
        if (json == null) return null;
        var section = new ChartAnalysis
        {
            noteCount = (int?)json["note_count"] ?? 0,
            holdCount = (int?)json["hold_count"] ?? 0,
            peakHandRate = (float?)json["peak_hand_rate"] ?? 0f,
            peakChordRate = (float?)json["peak_chord_rate"] ?? 0f,
            sustainedSeconds = (float?)json["sustained_seconds"] ?? 0f,
            fastLeapRatio = (float?)json["fast_leap_ratio"] ?? 0f,
            octaveRunRatio = (float?)json["octave_run_ratio"] ?? 0f,
            handCrossRatio = (float?)json["hand_cross_ratio"] ?? 0f,
            fingerRunRatio = (float?)json["finger_run_ratio"] ?? 0f,
            arpeggioRatio = (float?)json["arpeggio_ratio"] ?? 0f,
            trillRatio = (float?)json["trill_ratio"] ?? 0f,
            repeatRatio = (float?)json["repeat_ratio"] ?? 0f,
            thickRunRatio = (float?)json["thick_run_ratio"] ?? 0f,
            hasTechniqueData = true,
        };
        return section.noteCount > 0 ? section : null;
    }

    private static JObject WriteSection(ChartAnalysis section)
    {
        if (section == null) return null;
        return new JObject
        {
            ["note_count"] = section.noteCount,
            ["hold_count"] = section.holdCount,
            ["peak_hand_rate"] = section.peakHandRate,
            ["peak_chord_rate"] = section.peakChordRate,
            ["sustained_seconds"] = section.sustainedSeconds,
            ["fast_leap_ratio"] = section.fastLeapRatio,
            ["octave_run_ratio"] = section.octaveRunRatio,
            ["hand_cross_ratio"] = section.handCrossRatio,
            ["finger_run_ratio"] = section.fingerRunRatio,
            ["arpeggio_ratio"] = section.arpeggioRatio,
            ["trill_ratio"] = section.trillRatio,
            ["repeat_ratio"] = section.repeatRatio,
            ["thick_run_ratio"] = section.thickRunRatio,
        };
    }

    private static JArray WriteExcerpt(ChartAnalysis analysis)
    {
        var array = new JArray();
        if (!analysis.HasExcerpt) return array;
        for (int i = 0; i < analysis.excerptPitches.Length; i++)
            array.Add(new JArray(analysis.excerptTimes[i], analysis.excerptPitches[i],
                (int)analysis.excerptHands[i]));
        return array;
    }

    private static void ReadExcerpt(JArray source, ChartAnalysis analysis)
    {
        if (source == null || source.Count == 0) return;

        int count = source.Count;
        analysis.excerptTimes = new int[count];
        analysis.excerptPitches = new int[count];
        analysis.excerptHands = new byte[count];
        for (int i = 0; i < count; i++)
        {
            if (!(source[i] is JArray entry) || entry.Count < 3) continue;
            analysis.excerptTimes[i] = (int)entry[0];
            analysis.excerptPitches[i] = (int)entry[1];
            analysis.excerptHands[i] = (byte)(int)entry[2];
        }
    }

    private static void ReadDensity(JArray source, float[] target)
    {
        if (source == null) return;
        int count = Mathf.Min(source.Count, target.Length);
        for (int i = 0; i < count; i++) target[i] = (float)source[i];
    }

    private static void WritePrecomputed(string chartFileName, ChartAnalysis analysis)
    {
        string cachePath = GetCachePath(chartFileName);
        if (string.IsNullOrEmpty(cachePath)) return;

        try
        {
            string chartPath = ExternalSongLibrary.ToLocalPath(chartFileName);
            long sourceBytes = !string.IsNullOrEmpty(chartPath) && File.Exists(chartPath)
                ? new FileInfo(chartPath).Length
                : -1L;

            var json = new JObject
            {
                ["version"] = CacheVersion,
                ["source_bytes"] = sourceBytes,
                ["note_count"] = analysis.noteCount,
                ["hold_count"] = analysis.holdCount,
                ["slide_count"] = analysis.slideCount,
                ["trill_count"] = analysis.trillCount,
                ["chord_note_count"] = analysis.chordNoteCount,
                ["onset_count"] = analysis.onsetCount,
                ["right_hand_count"] = analysis.rightHandCount,
                ["left_hand_count"] = analysis.leftHandCount,
                ["pitched_note_count"] = analysis.pitchedNoteCount,
                ["lowest_pitch"] = analysis.lowestPitch,
                ["highest_pitch"] = analysis.highestPitch,
                ["length_seconds"] = analysis.lengthSeconds,
                ["average_density"] = analysis.averageDensity,
                ["peak_density"] = analysis.peakDensity,
                ["peak_bucket_total"] = analysis.peakBucketTotal,
                ["has_technique"] = analysis.hasTechniqueData,
                ["leap_ratio"] = analysis.leapRatio,
                ["arpeggio_ratio"] = analysis.arpeggioRatio,
                ["finger_run_ratio"] = analysis.fingerRunRatio,
                ["trill_ratio"] = analysis.trillRatio,
                ["hand_cross_ratio"] = analysis.handCrossRatio,
                ["sustained_seconds"] = analysis.sustainedSeconds,
                ["independence_ratio"] = analysis.independenceRatio,
                ["octave_ratio"] = analysis.octaveRatio,
                ["repeat_ratio"] = analysis.repeatRatio,
                ["peak_hand_rate"] = analysis.peakHandRate,
                ["peak_chord_rate"] = analysis.peakChordRate,
                ["fast_run_ratio"] = analysis.fastRunRatio,
                ["thick_run_ratio"] = analysis.thickRunRatio,
                ["peak_section"] = WriteSection(analysis.peakSection),
                ["quiet_section"] = WriteSection(analysis.quietSection),
                ["octave_run_ratio"] = analysis.octaveRunRatio,
                ["fast_leap_ratio"] = analysis.fastLeapRatio,
                ["excerpt"] = WriteExcerpt(analysis),
                ["right_density"] = new JArray(analysis.rightDensity),
                ["left_density"] = new JArray(analysis.leftDensity),
                ["bpm_curve"] = new JArray(analysis.bpmCurve),
            };
            File.WriteAllText(cachePath, json.ToString(Formatting.None));
        }
        catch (Exception ex)
        {
            // Not being able to cache is not a reason to fail the analysis.
            BuildLogger.LogWarning($"ChartAnalysisCache: could not write '{cachePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Reads only the five fields that matter and skips the rest of the note,
    /// including its sub_notes, which is most of the file's bulk.
    /// </summary>
    private static void ReadOneNote(JsonTextReader reader, ChartAnalysis analysis,
        List<int> times, List<byte> hands, List<int> pitches, List<int> ends)
    {
        int startTime = 0;
        int endTime = 0;
        int pitch = 0;
        int scalePiano = 0;
        int hand = 0;
        int noteType = 0;
        bool hidden = false;

        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName) continue;
            switch (reader.Value as string)
            {
                case "startTime": startTime = reader.ReadAsInt32() ?? 0; break;
                case "endTime": endTime = reader.ReadAsInt32() ?? 0; break;
                case "pitch": pitch = reader.ReadAsInt32() ?? 0; break;
                case "scale_piano": scalePiano = reader.ReadAsInt32() ?? 0; break;
                case "hand": hand = reader.ReadAsInt32() ?? 0; break;
                case "note_type": noteType = reader.ReadAsInt32() ?? 0; break;
                case "hidden": hidden = reader.ReadAsBoolean() ?? false; break;
                default: reader.Skip(); break;
            }
        }

        // 隱藏音符在遊戲裡沒有按鍵——`Chart.NormaliseHiddenNotes` 會把它們折進
        // 寄主的 sub_note，只發聲、不用打。算進分析的話難度雷達就是拿**完整的
        // 事件集**去算，而同一首歌的四個難度事件集本來就完全相同：normal 和
        // real 會畫出一模一樣的雷達圖。實測鬼火的 normal 只有 833 顆要打，
        // 檔案裡卻有 3474 顆。
        if (hidden) return;

        // Legacy charts index the keyboard 1..88 instead of by MIDI number.
        if (pitch <= 0 && scalePiano > 0) pitch = scalePiano + 20;

        analysis.noteCount++;
        // The bitmask vocabulary: 0x02 long, 0x04 slide, 0x40 trill.
        if ((noteType & 0x02) != 0) analysis.holdCount++;
        if ((noteType & 0x04) != 0) analysis.slideCount++;
        if ((noteType & 0x40) != 0) analysis.trillCount++;

        // hand 0 is the right hand; anything else (including the "both" value 2)
        // is drawn on the left side.
        if (hand == 0) analysis.rightHandCount++;
        else analysis.leftHandCount++;

        if (pitch > 0)
        {
            analysis.pitchedNoteCount++;
            if (analysis.lowestPitch == 0 || pitch < analysis.lowestPitch) analysis.lowestPitch = pitch;
            if (pitch > analysis.highestPitch) analysis.highestPitch = pitch;
        }

        times.Add(startTime);
        hands.Add((byte)(hand == 0 ? 0 : 1));
        pitches.Add(pitch);
        ends.Add(Mathf.Max(endTime, startTime));
    }

    /// <summary>One moment of one hand: the lowest and highest note it plays there.</summary>
    private struct Onset
    {
        public int time;
        public int low;
        public int high;
        public float Centre => (low + high) * 0.5f;
    }

    /// <summary>
    /// What the hands actually have to do, read from pitch rather than from the
    /// authored note flags.  The gesture bits (slide, trill) are set in only 14
    /// of this library's 121 charts, so on their own they describe almost
    /// nothing; leaps, arpeggios, finger runs and hand crossings are all visible
    /// in the pitch line of any chart that carries pitch at all.
    /// </summary>
    private static void AnalyseTechnique(ChartAnalysis analysis,
        List<int> times, List<byte> hands, List<int> pitches)
    {
        // Below half coverage the pitch line is too patchy to draw conclusions from.
        if (analysis.noteCount == 0 || analysis.pitchedNoteCount < analysis.noteCount / 2) return;

        var right = new List<Onset>(times.Count / 2 + 1);
        var left = new List<Onset>(times.Count / 2 + 1);
        for (int i = 0; i < times.Count; i++)
        {
            if (pitches[i] <= 0) continue;
            List<Onset> target = hands[i] == 0 ? right : left;
            // The note list is time-ordered, so a repeat of the last time is a chord.
            if (target.Count > 0 && target[target.Count - 1].time == times[i])
            {
                Onset last = target[target.Count - 1];
                if (pitches[i] < last.low) last.low = pitches[i];
                if (pitches[i] > last.high) last.high = pitches[i];
                target[target.Count - 1] = last;
                continue;
            }
            target.Add(new Onset { time = times[i], low = pitches[i], high = pitches[i] });
        }

        int leaps = 0;
        int quickMoves = 0;
        int arpeggioNotes = 0;
        int runNotes = 0;
        int fastRunNotes = 0;
        int thickRunOnsets = 0;
        int trillNotes = 0;
        for (int h = 0; h < 2; h++)
        {
            List<Onset> seq = h == 0 ? right : left;
            CountLeaps(seq, ref leaps, ref quickMoves);
            arpeggioNotes += CountArpeggioNotes(seq);
            runNotes += CountFingerRunNotes(seq, 110, 5);
            fastRunNotes += CountFingerRunNotes(seq, 70, 6);
            thickRunOnsets += CountThickRunOnsets(seq);
            trillNotes += CountTrillNotes(seq);
        }

        analysis.hasTechniqueData = true;
        analysis.leapRatio = quickMoves > 0 ? (float)leaps / quickMoves : 0f;
        analysis.arpeggioRatio = (float)arpeggioNotes / analysis.noteCount;
        analysis.fingerRunRatio = (float)runNotes / analysis.noteCount;
        analysis.fastRunRatio = (float)fastRunNotes / analysis.noteCount;
        analysis.thickRunRatio = (float)thickRunOnsets / analysis.noteCount;
        analysis.trillRatio = (float)trillNotes / analysis.noteCount;
        analysis.handCrossRatio = MeasureHandCrossing(right, left);

        int octaveGrabs = 0;
        int onsets = 0;
        int repeated = 0;
        for (int h = 0; h < 2; h++)
        {
            List<Onset> seq = h == 0 ? right : left;
            onsets += seq.Count;
            for (int i = 0; i < seq.Count; i++)
                if (seq[i].high - seq[i].low == 12) octaveGrabs++;
            repeated += CountRepeatedNotes(seq);
        }
        analysis.octaveRatio = onsets > 0 ? (float)octaveGrabs / onsets : 0f;
        analysis.repeatRatio = (float)repeated / analysis.noteCount;

        int octaveRun = 0;
        int fastLeaps = 0;
        for (int h = 0; h < 2; h++)
        {
            List<Onset> seq = h == 0 ? right : left;
            octaveRun += CountOctaveRunOnsets(seq);
            fastLeaps += CountFastLeaps(seq);
        }
        analysis.octaveRunRatio = (float)octaveRun / analysis.noteCount;
        // 分母和 leapRatio 同一個。除以「快到那個程度的移動」會讓它變成條件機率，
        // 見 CountFastLeaps。
        analysis.fastLeapRatio = quickMoves > 0 ? (float)fastLeaps / quickMoves : 0f;
    }

    /// <summary>
    /// Three or more octaves in a row taken at speed -- an octave passage, which
    /// is a different job from reaching one octave now and then.
    /// </summary>
    private static int CountOctaveRunOnsets(List<Onset> seq)
    {
        int total = 0;
        int i = 0;
        while (i < seq.Count - 1)
        {
            int j = i;
            int length = 1;
            while (j < seq.Count - 1)
            {
                if (seq[j + 1].time - seq[j].time > 180) break;
                if (seq[j].high - seq[j].low != 12) break;
                if (seq[j + 1].high - seq[j + 1].low != 12) break;
                j++;
                length++;
            }
            if (length >= 3 && seq[i].high - seq[i].low == 12) total += length;
            i = Mathf.Max(j, i + 1);
        }
        return total;
    }

    /// <summary>Octave jumps taken inside 120ms, against every move that fast.</summary>
    /// <summary>
    /// Octave jumps taken inside 120ms.
    /// </summary>
    /// <remarks>
    /// Counted, not divided: the caller measures these against every move, the
    /// same denominator <see cref="CountLeaps"/> uses. Dividing by "moves that
    /// fast" instead made this a conditional probability -- among the moves you
    /// happen to take quickly, how many were leaps -- so a sparse chart with
    /// eleven fast moves, three of them jumps, outscored a chart that leaps at
    /// speed the whole way through. It also let the fast ratio exceed the plain
    /// one, which made the pair read as unrelated measures rather than as a
    /// subset of the same thing.
    /// </remarks>
    private static int CountFastLeaps(List<Onset> seq)
    {
        int leaps = 0;
        for (int i = 1; i < seq.Count; i++)
        {
            if (seq[i].time - seq[i - 1].time > 120) continue;
            if (Mathf.Abs(seq[i].Centre - seq[i - 1].Centre) >= 12f) leaps++;
        }

        return leaps;
    }

    /// <summary>
    /// One finger holding while the others keep moving.  A note only counts when
    /// a note that started strictly earlier is still sounding, so the members of
    /// a block chord do not count each other.
    /// </summary>
    private static float MeasureIndependence(List<int> times, List<byte> hands, List<int> ends)
    {
        if (times.Count == 0) return 0f;

        int overlapping = 0;
        for (int h = 0; h < 2; h++)
        {
            // The note list is time-ordered, so each hand's slice is too.
            int cursor = 0;
            int latestEnd = int.MinValue;
            for (int i = 0; i < times.Count; i++)
            {
                if (hands[i] != h) continue;
                // times is globally sorted, so this pointer only ever moves forward.
                while (cursor < times.Count && times[cursor] < times[i])
                {
                    if (hands[cursor] == h && ends[cursor] > latestEnd) latestEnd = ends[cursor];
                    cursor++;
                }
                // 30ms of slack: a chord released a hair late is not independence.
                if (latestEnd > times[i] + 30) overlapping++;
            }
        }
        return (float)overlapping / times.Count;
    }

    /// <summary>
    /// Busiest second of a single hand, counted in strikes rather than notes: a
    /// four-note chord is one movement, not four, so this stays a speed measure
    /// and does not just repeat how chord-heavy the chart is.
    /// </summary>
    /// <summary>Busiest second of one hand counted in notes, not in strikes.</summary>
    private static float MeasurePeakChordRate(List<int> times, List<byte> hands)
    {
        int peak = 0;
        var notes = new List<int>(times.Count / 2 + 1);
        for (int h = 0; h < 2; h++)
        {
            notes.Clear();
            for (int i = 0; i < times.Count; i++)
                if (hands[i] == h) notes.Add(times[i]);

            int start = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                while (notes[i] - notes[start] > 1000) start++;
                int inWindow = i - start + 1;
                if (inWindow > peak) peak = inWindow;
            }
        }
        return peak;
    }

    private static float MeasurePeakHandRate(List<int> times, List<byte> hands)
    {
        int peak = 0;
        var onsets = new List<int>(times.Count / 2 + 1);
        for (int h = 0; h < 2; h++)
        {
            onsets.Clear();
            for (int i = 0; i < times.Count; i++)
            {
                if (hands[i] != h) continue;
                if (onsets.Count > 0 && onsets[onsets.Count - 1] == times[i]) continue;
                onsets.Add(times[i]);
            }

            int start = 0;
            for (int i = 0; i < onsets.Count; i++)
            {
                while (onsets[i] - onsets[start] > 1000) start++;
                int inWindow = i - start + 1;
                if (inWindow > peak) peak = inWindow;
            }
        }
        return peak;
    }

    /// <summary>Four or more strikes of the same single pitch in quick succession.</summary>
    private static int CountRepeatedNotes(List<Onset> seq)
    {
        int total = 0;
        int i = 0;
        while (i < seq.Count - 1)
        {
            int j = i;
            int length = 1;
            while (j < seq.Count - 1)
            {
                if (seq[j + 1].time - seq[j].time > 160) break;
                if (seq[j].low != seq[j].high) break;
                if (seq[j + 1].low != seq[j].low || seq[j + 1].high != seq[j].high) break;
                j++;
                length++;
            }
            if (length >= 4) total += length;
            i = Mathf.Max(j, i + 1);
        }
        return total;
    }

    /// <summary>
    /// A leap is a quick move whose centre jumps an octave, or a single grab that
    /// spans one -- both are the same demand on the hand.
    /// </summary>
    private static void CountLeaps(List<Onset> seq, ref int leaps, ref int quickMoves)
    {
        for (int i = 1; i < seq.Count; i++)
        {
            if (seq[i].time - seq[i - 1].time > 250) continue;
            quickMoves++;
            if (Mathf.Abs(seq[i].Centre - seq[i - 1].Centre) >= 12f ||
                seq[i].high - seq[i].low >= 12) leaps++;
        }
    }

    /// <summary>Four or more chord-tone steps in one direction: a broken chord.</summary>
    private static int CountArpeggioNotes(List<Onset> seq)
    {
        int total = 0;
        int i = 0;
        while (i < seq.Count - 1)
        {
            int j = i;
            int direction = 0;
            int length = 1;
            while (j < seq.Count - 1)
            {
                if (seq[j + 1].time - seq[j].time > 200) break;
                float interval = seq[j + 1].Centre - seq[j].Centre;
                float size = Mathf.Abs(interval);
                if (size < 2f || size > 7f) break;
                int step = interval > 0f ? 1 : -1;
                if (direction == 0) direction = step;
                else if (step != direction) break;
                j++;
                length++;
            }
            if (length >= 4) total += length;
            i = Mathf.Max(j, i + 1);
        }
        return total;
    }

    /// <summary>
    /// Chords sitting inside a fast run: doubling that never slows down.
    /// </summary>
    /// <remarks>
    /// Playing 1-5 and 3 together in the middle of a scale or an arpeggio, at
    /// the same tempo as the single notes around it, is a different job from
    /// either the run or the chord alone: the hand has to keep the passage
    /// moving while some fingers are committed. Counted as the doubled onsets
    /// inside runs that already qualify as fast passagework.
    /// </remarks>
    private static int CountThickRunOnsets(List<Onset> seq)
    {
        int total = 0;
        int i = 0;
        while (i < seq.Count - 1)
        {
            int j = i;
            int length = 1;
            while (j < seq.Count - 1)
            {
                if (seq[j + 1].time - seq[j].time > 110) break;
                if (Mathf.Abs(seq[j + 1].Centre - seq[j].Centre) > 4f) break;
                j++;
                length++;
            }
            if (length >= 5)
                for (int k = i; k <= j; k++)
                    if (seq[k].high > seq[k].low) total++;
            i = Mathf.Max(j, i + 1);
        }
        return total;
    }

    /// <summary>
    /// Near-stepwise notes in a row: a scale or a finger passage.
    /// </summary>
    /// <remarks>
    /// <paramref name="gap"/> is the most time allowed between notes and
    /// <paramref name="minimum"/> the shortest run that counts, so one walk
    /// serves both the ordinary passage and the high-speed one: 110ms is about
    /// nine notes a second, 70ms about fourteen.
    /// </remarks>
    private static int CountFingerRunNotes(List<Onset> seq, int gap, int minimum)
    {
        int total = 0;
        int i = 0;
        while (i < seq.Count - 1)
        {
            int j = i;
            int length = 1;
            while (j < seq.Count - 1)
            {
                if (seq[j + 1].time - seq[j].time > gap) break;
                if (Mathf.Abs(seq[j + 1].Centre - seq[j].Centre) > 4f) break;
                j++;
                length++;
            }
            if (length >= minimum) total += length;
            i = Mathf.Max(j, i + 1);
        }
        return total;
    }

    /// <summary>Six or more fast alternations between the same two pitches.</summary>
    private static int CountTrillNotes(List<Onset> seq)
    {
        int total = 0;
        int i = 0;
        while (i < seq.Count - 2)
        {
            int j = i;
            int length = 1;
            while (j < seq.Count - 2)
            {
                if (seq[j + 1].time - seq[j].time > 140) break;
                if (seq[j + 2].low != seq[j].low || seq[j + 2].high != seq[j].high) break;
                if (seq[j + 1].low == seq[j].low) break;
                j++;
                length++;
            }
            if (length >= 6) total += length;
            i = Mathf.Max(j, i + 1);
        }
        return total;
    }

    /// <summary>
    /// Hand crossing: at a moment both hands play, the left hand's lowest note is
    /// above the right hand's highest.  Strict on purpose -- an overlap of a note
    /// or two is ordinary two-hand writing, not a crossing.
    /// </summary>
    private static float MeasureHandCrossing(List<Onset> right, List<Onset> left)
    {
        int together = 0;
        int crossed = 0;
        int r = 0;
        for (int l = 0; l < left.Count && r < right.Count; l++)
        {
            while (r < right.Count && right[r].time < left[l].time) r++;
            if (r >= right.Count || right[r].time != left[l].time) continue;
            together++;
            if (left[l].low > right[r].high) crossed++;
        }
        return together > 0 ? (float)crossed / together : 0f;
    }

    private static void Summarise(ChartAnalysis analysis, List<int> times, List<byte> hands,
        List<int> ends, int finishMs)
    {
        int lastTime = 0;
        for (int i = 0; i < times.Count; i++)
            if (times[i] > lastTime) lastTime = times[i];

        int spanMs = Mathf.Max(finishMs, lastTime);
        analysis.lengthSeconds = spanMs / 1000f;
        if (spanMs <= 0) return;

        // Chord notes: runs of equal start times.  The note list is written in
        // time order, so one pass over it is enough.
        int runLength = 1;
        for (int i = 1; i <= times.Count; i++)
        {
            bool sameAsPrevious = i < times.Count && times[i] == times[i - 1];
            if (sameAsPrevious)
            {
                runLength++;
                continue;
            }
            if (runLength >= 2) analysis.chordNoteCount += runLength;
            analysis.onsetCount++;
            runLength = 1;
        }

        for (int i = 0; i < times.Count; i++)
        {
            int bucket = Mathf.Clamp(
                Mathf.FloorToInt((float)times[i] / spanMs * ChartAnalysis.BucketCount),
                0, ChartAnalysis.BucketCount - 1);
            if (hands[i] == 0) analysis.rightDensity[bucket] += 1f;
            else analysis.leftDensity[bucket] += 1f;
        }

        // Turn the raw counts into notes per second so the graph is comparable
        // between a two minute song and a six minute one.
        float bucketSeconds = Mathf.Max(0.001f, analysis.lengthSeconds / ChartAnalysis.BucketCount);
        for (int i = 0; i < ChartAnalysis.BucketCount; i++)
        {
            analysis.rightDensity[i] /= bucketSeconds;
            analysis.leftDensity[i] /= bucketSeconds;
            float total = analysis.rightDensity[i] + analysis.leftDensity[i];
            if (total > analysis.peakBucketTotal) analysis.peakBucketTotal = total;
        }

        // Endurance is not "dense on average" but "dense for a long time", so
        // it is measured as the time actually spent above a busy threshold.
        for (int i = 0; i < ChartAnalysis.BucketCount; i++)
            if (analysis.rightDensity[i] + analysis.leftDensity[i] >= 12f)
                analysis.sustainedSeconds += bucketSeconds;

        analysis.independenceRatio = MeasureIndependence(times, hands, ends);
        analysis.peakHandRate = MeasurePeakHandRate(times, hands);
        analysis.peakChordRate = MeasurePeakChordRate(times, hands);
        analysis.averageDensity = analysis.noteCount / Mathf.Max(0.001f, analysis.lengthSeconds);

        // Busiest one-second window, as a sliding window over the sorted times.
        int windowStart = 0;
        for (int i = 0; i < times.Count; i++)
        {
            while (times[i] - times[windowStart] > 1000) windowStart++;
            int inWindow = i - windowStart + 1;
            if (inWindow > analysis.peakDensity) analysis.peakDensity = inWindow;
        }
    }
}
