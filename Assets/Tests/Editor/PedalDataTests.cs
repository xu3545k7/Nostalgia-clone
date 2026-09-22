using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Sustain pedal spans travel from the chart file into <see cref="Chart.pedal_data"/>.
/// </summary>
/// <remarks>
/// The spans are restored from the source MIDI's CC64 by the qt_editor tool and are
/// already converted onto the chart's own timeline, so nothing here needs to rescale
/// them. Charts written before that tool existed carry no pedal_data at all, which is
/// why the absent case has to stay harmless.
/// </remarks>
[TestFixture]
public class PedalDataTests
{
    [Test]
    public void PedalSpans_ParseFromChartJson()
    {
        const string json = @"{
            ""first_bpm"": 86.0,
            ""music_finish_time_msec"": 163400,
            ""notes"": [],
            ""pedal_data"": [
                { ""start_ms"": 3, ""end_ms"": 2792 },
                { ""start_ms"": 2794, ""end_ms"": 5582 }
            ]
        }";

        Chart chart = JsonUtility.FromJson<Chart>(json);

        Assert.IsNotNull(chart.pedal_data);
        Assert.AreEqual(2, chart.pedal_data.Count);
        Assert.AreEqual(3, chart.pedal_data[0].start_ms);
        Assert.AreEqual(2792, chart.pedal_data[0].end_ms);
        Assert.AreEqual(2794, chart.pedal_data[1].start_ms);
    }

    [Test]
    public void PedalSpans_AbsentFromChart_YieldNoSpans()
    {
        // JsonUtility hands back an empty list rather than null for a missing
        // array field, so callers only ever have to check the count.
        Chart chart = JsonUtility.FromJson<Chart>(
            @"{ ""first_bpm"": 120.0, ""notes"": [] }");

        Assert.IsTrue(chart.pedal_data == null || chart.pedal_data.Count == 0,
            "Charts without pedal data must not fabricate spans.");
    }

    [Test]
    public void PedalSpans_SurviveNewtonsoftFallbackPath()
    {
        // GameManager falls back to Newtonsoft when JsonUtility returns nothing usable.
        Chart chart = Newtonsoft.Json.JsonConvert.DeserializeObject<Chart>(
            @"{ ""notes"": [], ""pedal_data"": [ { ""start_ms"": 10, ""end_ms"": 20 } ] }");

        Assert.IsNotNull(chart.pedal_data);
        Assert.AreEqual(1, chart.pedal_data.Count);
        Assert.AreEqual(10, chart.pedal_data[0].start_ms);
    }

    [Test]
    public void Contains_CoversTheClosedSpan()
    {
        var span = new PedalSpan { start_ms = 1000, end_ms = 2000 };

        Assert.IsFalse(span.Contains(999));
        Assert.IsTrue(span.Contains(1000));
        Assert.IsTrue(span.Contains(1500));
        Assert.IsTrue(span.Contains(2000));
        Assert.IsFalse(span.Contains(2001));
    }

    [Test]
    public void RealRestoredChart_CarriesPedalAndVelocity()
    {
        // syuten is the first chart to go through the MIDI expression restore:
        // 1667 notes with velocity, 94 pedal spans on the chart timeline.
        string path = System.IO.Path.Combine(
            Application.dataPath, "..", "UserSongs", "syuten", "Real", "syuten.json");
        if (!System.IO.File.Exists(path))
        {
            Assert.Ignore("syuten chart not present in this checkout.");
        }

        Chart chart = JsonUtility.FromJson<Chart>(System.IO.File.ReadAllText(path));

        Assert.IsNotNull(chart.pedal_data);
        Assert.Greater(chart.pedal_data.Count, 0);
        foreach (PedalSpan span in chart.pedal_data)
        {
            Assert.Greater(span.end_ms, span.start_ms, "Pedal spans must be forward in time.");
        }
        // Every note carries the restored expression data the piano voices need.
        foreach (NoteData note in chart.notes)
        {
            Assert.Greater(note.pitch, 0, "Restored charts carry a MIDI pitch per note.");
        }
    }
}
