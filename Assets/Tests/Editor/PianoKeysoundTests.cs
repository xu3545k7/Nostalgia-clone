using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The lookup and pedal logic behind the sampled piano keysound.
/// </summary>
/// <remarks>
/// These cover the parts that decide *what* is played and *when it stops*, which
/// is where a wrong answer is silent rather than obvious: a mis-resolved zone just
/// sounds slightly off-pitch, and a mishandled pedal only shows up as notes dying
/// early in a sustained passage.
/// </remarks>
[TestFixture]
public class PianoKeysoundTests
{
    private static PianoSampleManifest BuildManifest()
    {
        // Mirrors the real bank's shape: two velocity bands, each with its own
        // key zones, and a gain table relative to each band's reference velocity.
        var gains = new float[128];
        for (int v = 1; v <= 80; v++) gains[v] = v / 80f;
        for (int v = 81; v <= 127; v++) gains[v] = v / 127f;

        return new PianoSampleManifest
        {
            velocity_gain = gains,
            velocity_bands = new List<PianoVelocityBand>
            {
                new PianoVelocityBand
                {
                    index = 0, low_velocity = 0, high_velocity = 80, reference_velocity = 80,
                    zones = new List<PianoSampleZone>
                    {
                        new PianoSampleZone { low_key = 21, high_key = 23, root_key = 22, sample = "lo_a" },
                        new PianoSampleZone { low_key = 24, high_key = 26, root_key = 25, sample = "lo_b" },
                    },
                },
                new PianoVelocityBand
                {
                    index = 1, low_velocity = 81, high_velocity = 127, reference_velocity = 127,
                    zones = new List<PianoSampleZone>
                    {
                        new PianoSampleZone { low_key = 21, high_key = 26, root_key = 24, sample = "hi_a" },
                    },
                },
            },
        };
    }

    [Test]
    public void Resolve_PicksTheZoneAndBandForTheNote()
    {
        PianoSampleManifest manifest = BuildManifest();

        Assert.IsTrue(manifest.TryResolve(25, 40, out PianoSampleZone zone, out _, out _));
        Assert.AreEqual("lo_b", zone.sample);

        Assert.IsTrue(manifest.TryResolve(25, 100, out zone, out _, out _));
        Assert.AreEqual("hi_a", zone.sample, "Velocity above the split uses the loud layer.");
    }

    [Test]
    public void Resolve_TransposesFromTheZoneRoot()
    {
        PianoSampleManifest manifest = BuildManifest();

        manifest.TryResolve(22, 40, out _, out float atRoot, out _);
        Assert.AreEqual(1f, atRoot, 1e-4f, "The root key plays the sample untouched.");

        manifest.TryResolve(23, 40, out _, out float upOne, out _);
        Assert.AreEqual(Mathf.Pow(2f, 1f / 12f), upOne, 1e-4f);

        manifest.TryResolve(21, 40, out _, out float downOne, out _);
        Assert.AreEqual(Mathf.Pow(2f, -1f / 12f), downOne, 1e-4f);
    }

    [Test]
    public void Resolve_UsesTheMeasuredVelocityCurve()
    {
        PianoSampleManifest manifest = BuildManifest();

        manifest.TryResolve(22, 40, out _, out _, out float half);
        manifest.TryResolve(22, 80, out _, out _, out float full);
        Assert.AreEqual(0.5f, half, 1e-4f);
        Assert.AreEqual(1f, full, 1e-4f);

        // Crossing into the loud band restarts the curve against its own
        // reference, which is what produces the real bank's level jump.
        manifest.TryResolve(22, 81, out _, out _, out float justOver);
        Assert.Less(justOver, full);
    }

    [Test]
    public void Resolve_PitchOutsideEveryZoneBorrowsTheNearest()
    {
        PianoSampleManifest manifest = BuildManifest();

        Assert.IsTrue(manifest.TryResolve(60, 40, out PianoSampleZone zone, out float ratio, out _));
        Assert.AreEqual("lo_b", zone.sample, "The closest zone stands in.");
        Assert.Greater(ratio, 1f, "It transposes up to reach the requested pitch.");
    }

    [Test]
    public void Resolve_RejectsAnEmptyManifest()
    {
        var manifest = new PianoSampleManifest();
        Assert.IsFalse(manifest.IsUsable);
        Assert.IsFalse(manifest.TryResolve(60, 80, out _, out _, out _));
    }

    // ── 踏板 ────────────────────────────────────────────────────────────

    private static PedalTimeline Timeline(params (int start, int end)[] spans)
    {
        var list = new List<PedalSpan>();
        foreach ((int start, int end) in spans)
            list.Add(new PedalSpan { start_ms = start, end_ms = end });
        return new PedalTimeline(list);
    }

    [Test]
    public void Pedal_ReportsWhetherItIsDown()
    {
        PedalTimeline pedal = Timeline((1000, 2000), (2500, 3000));

        Assert.IsFalse(pedal.IsDown(999));
        Assert.IsTrue(pedal.IsDown(1000));
        Assert.IsTrue(pedal.IsDown(1999));
        Assert.IsTrue(pedal.IsDown(2000));
        Assert.IsFalse(pedal.IsDown(2200), "The gap between spans is a pedal change.");
        Assert.IsTrue(pedal.IsDown(2750));
        Assert.IsFalse(pedal.IsDown(3001));
    }

    [Test]
    public void Pedal_ReportsWhenTheCurrentSpanLifts()
    {
        PedalTimeline pedal = Timeline((1000, 2000));

        Assert.AreEqual(2000d, pedal.LiftTimeAt(1500));
        Assert.AreEqual(-1d, pedal.LiftTimeAt(2500));
    }

    [Test]
    public void Pedal_KeepsVeryShortChangesIntact()
    {
        // syuten's pedal changes are 2 ms apart; merging them would let the
        // previous harmony ring through the change.
        PedalTimeline pedal = Timeline((0, 1000), (1002, 2000));

        Assert.AreEqual(2, pedal.SpanCount);
        Assert.IsFalse(pedal.IsDown(1001), "The lift between the two spans must survive.");
    }

    [Test]
    public void Pedal_ReportsAChangeThatFallsBetweenTwoFrames()
    {
        // The real failure this guards: sampling IsDown once a frame reads "down"
        // on both sides of a 2 ms pedal change, so notes released under the pedal
        // never damp, every voice fills up and the piano goes silent mid-song.
        PedalTimeline pedal = Timeline((0, 1000), (1002, 3000));

        Assert.IsTrue(pedal.IsDown(995), "Frame before the change.");
        Assert.IsTrue(pedal.IsDown(1010), "Frame after it — the lift is invisible here.");
        Assert.IsTrue(pedal.LiftedBetween(995, 1010), "But the interval must reveal it.");
    }

    [Test]
    public void Pedal_ReportsNoChangeWhenNoneHappened()
    {
        PedalTimeline pedal = Timeline((0, 1000), (1002, 3000));

        Assert.IsFalse(pedal.LiftedBetween(1010, 1030));
        Assert.IsFalse(pedal.LiftedBetween(500, 900));
        Assert.IsFalse(pedal.LiftedBetween(1000, 1000), "An empty interval cannot contain a lift.");
    }

    [Test]
    public void Pedal_ReportsEveryChangeInsideOneLongFrame()
    {
        // A frame hitch can straddle several changes; one report is enough,
        // since every held voice damps on it.
        PedalTimeline pedal = Timeline((0, 100), (102, 200), (202, 300));

        Assert.IsTrue(pedal.LiftedBetween(50, 250));
    }

    [Test]
    public void Pedal_LiftAtTheIntervalEdgeCountsOnce()
    {
        PedalTimeline pedal = Timeline((0, 1000));

        Assert.IsTrue(pedal.LiftedBetween(990, 1000), "The lift lands on the closing edge.");
        Assert.IsFalse(pedal.LiftedBetween(1000, 1010),
            "And is not reported a second time on the next interval.");
    }

    [Test]
    public void Pedal_RealChartChangesAreAllVisibleFrameByFrame()
    {
        Chart chart = LoadSyuten();
        if (chart == null) Assert.Ignore("syuten chart not present in this checkout.");

        var pedal = new PedalTimeline(chart.pedal_data);
        int lifts = 0;
        // Walk the song at 60 fps the way Update does. The final release lands
        // just past the chart's stated end, so run a little beyond it.
        double end = chart.music_finish_time_msec + 2000;
        for (double t = 0; t < end; t += 16.667)
        {
            if (pedal.LiftedBetween(t - 16.667, t)) lifts++;
        }
        Assert.AreEqual(pedal.SpanCount, lifts,
            "Every pedal change must be seen exactly once at 60 fps.");
    }

    private static Chart LoadSyuten()
    {
        string path = System.IO.Path.Combine(
            Application.dataPath, "..", "UserSongs", "syuten", "Real", "syuten.json");
        return System.IO.File.Exists(path)
            ? JsonUtility.FromJson<Chart>(System.IO.File.ReadAllText(path))
            : null;
    }

    [Test]
    public void Pedal_EmptyChartIsAlwaysUp()
    {
        Assert.IsFalse(PedalTimeline.Empty.IsDown(0));
        Assert.IsFalse(PedalTimeline.Empty.HasPedal);
        Assert.IsFalse(new PedalTimeline(new List<PedalSpan>()).IsDown(1234));
    }

    [Test]
    public void Pedal_IgnoresReversedSpans()
    {
        PedalTimeline pedal = Timeline((2000, 1000), (3000, 4000));

        Assert.AreEqual(1, pedal.SpanCount);
        Assert.IsTrue(pedal.IsDown(3500));
    }

    [Test]
    public void RealBank_ResolvesEveryPianoKeyAtEveryVelocity()
    {
        PianoSampleManifest manifest = PianoSampleManifest.Load();
        if (manifest == null)
        {
            Assert.Ignore("Piano sample bank not rendered in this checkout.");
        }

        for (int pitch = manifest.lowest_pitch; pitch <= manifest.highest_pitch; pitch++)
        {
            foreach (int velocity in new[] { 1, 40, 80, 81, 100, 127 })
            {
                Assert.IsTrue(
                    manifest.TryResolve(pitch, velocity, out PianoSampleZone zone,
                        out float ratio, out float gain),
                    $"pitch {pitch} velocity {velocity} resolved to nothing");
                Assert.IsNotNull(zone.sample);
                Assert.Greater(gain, 0f);
                // Zones cover 1-5 semitones, so no note should need a big shift.
                Assert.Less(Mathf.Abs(pitch - zone.root_key), 4,
                    $"pitch {pitch} transposes too far from {zone.root_key}");
                Assert.Greater(ratio, 0f);
            }
        }
    }
}
