using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One rendered sample and the key range it covers.</summary>
[Serializable]
public class PianoSampleZone
{
    public int low_key;
    public int high_key;
    public int root_key;
    public string sample;
    public float seconds;
}

/// <summary>One of the SoundFont's velocity layers and its key zones.</summary>
[Serializable]
public class PianoVelocityBand
{
    public int index;
    public int low_velocity;
    public int high_velocity;
    public int reference_velocity;
    public List<PianoSampleZone> zones;
}

/// <summary>
/// Describes the offline-rendered piano sample bank in Resources/PianoSamples.
/// </summary>
/// <remarks>
/// The layout mirrors the SoundFont's own structure rather than inventing one:
/// UprightPianoKW has exactly two velocity layers (0-80, 81-127), and within a
/// layer velocity is pure attenuation — measured normalised envelopes differ by
/// 0.006 at most. That is why two samples per zone plus <see cref="velocity_gain"/>
/// reproduce any velocity exactly instead of approximating it.
///
/// Written by qt_editor/render_piano_samples.py; re-running that script is the
/// only supported way to change this file.
/// </remarks>
[Serializable]
public class PianoSampleManifest
{
    public const string ResourcePath = "PianoSamples/piano_samples";

    public string soundfont;
    public int sample_rate = 44100;
    public int channels = 1;
    public string resource_folder = "PianoSamples";
    public int lowest_pitch = 21;
    public int highest_pitch = 108;
    /// <summary>
    /// Linear gain for each MIDI velocity, relative to the reference velocity of
    /// the band that velocity falls in. Index 0 is unused (velocity 0 is a note off).
    /// </summary>
    public float[] velocity_gain;
    public List<PianoVelocityBand> velocity_bands;

    public bool IsUsable =>
        velocity_bands != null && velocity_bands.Count > 0 &&
        velocity_gain != null && velocity_gain.Length >= 128;

    public static PianoSampleManifest Load()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset == null) return null;
        var manifest = JsonUtility.FromJson<PianoSampleManifest>(asset.text);
        return manifest != null && manifest.IsUsable ? manifest : null;
    }

    /// <summary>
    /// Picks the sample for a note, plus the playback ratio and volume that turn
    /// it back into that exact pitch and velocity.
    /// </summary>
    /// <param name="pitchRatio">Multiply AudioSource.pitch by this to transpose.</param>
    /// <param name="gain">Linear volume, already accounting for the velocity curve.</param>
    public bool TryResolve(int pitch, int velocity,
        out PianoSampleZone zone, out float pitchRatio, out float gain)
    {
        zone = null;
        pitchRatio = 1f;
        gain = 0f;
        if (!IsUsable) return false;

        velocity = Mathf.Clamp(velocity, 1, 127);
        PianoVelocityBand band = FindBand(velocity);
        if (band == null || band.zones == null || band.zones.Count == 0) return false;

        zone = FindZone(band, pitch);
        if (zone == null) return false;

        // Semitone transposition is a frequency ratio, the same resampling
        // FluidSynth does internally for keys inside a zone.
        pitchRatio = Mathf.Pow(2f, (pitch - zone.root_key) / 12f);
        gain = velocity_gain[velocity];
        return gain > 0f;
    }

    private PianoVelocityBand FindBand(int velocity)
    {
        for (int i = 0; i < velocity_bands.Count; i++)
        {
            PianoVelocityBand band = velocity_bands[i];
            if (velocity >= band.low_velocity && velocity <= band.high_velocity) return band;
        }
        // Velocity outside every band should not happen, but a note must still
        // sound rather than fall silent on a malformed manifest.
        return velocity_bands[velocity_bands.Count - 1];
    }

    private static PianoSampleZone FindZone(PianoVelocityBand band, int pitch)
    {
        PianoSampleZone nearest = null;
        int nearestDistance = int.MaxValue;
        for (int i = 0; i < band.zones.Count; i++)
        {
            PianoSampleZone zone = band.zones[i];
            if (pitch >= zone.low_key && pitch <= zone.high_key) return zone;

            int distance = pitch < zone.low_key ? zone.low_key - pitch : pitch - zone.high_key;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = zone;
            }
        }
        // Notes outside the sampled range (or a chart with a bogus pitch) borrow
        // the closest zone and transpose further rather than going silent.
        return nearest;
    }
}
