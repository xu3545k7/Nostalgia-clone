using UnityEngine;

/// <summary>
/// Centralizes note placement/width rules so gameplay can keep its 28-lane logic
/// while visuals optionally expand to a full 88-key piano layout when pitch data exists.
/// </summary>
public static class PianoVisualLayout
{
    public const int LegacyLaneCount = 28;
    public const int PianoKeyCount = 88;
    public const int PianoMidiMin = 21;
    public const int PianoMidiMax = 108;

    private const float DefaultTrackWidth = 105f;
    // Keeps the same visual gap ratio as the old 28-lane formula (gap=1 at width 105).
    private const float GapRatioPerLane = 1f / 3.75f;

    public static bool HasPianoPitch(NoteData note)
    {
        var settings = SettingsManager.Instance;
        if (settings != null && !settings.UsePiano88VisualLayout)
        {
            return false;
        }

        return ResolveMidiPitch(note) >= PianoMidiMin;
    }

    public static int ResolveMidiPitch(NoteData note)
    {
        if (note == null)
        {
            return -1;
        }

        if (note.pitch >= PianoMidiMin && note.pitch <= PianoMidiMax)
        {
            return note.pitch;
        }

        if (note.scale_piano >= 1 && note.scale_piano <= PianoKeyCount)
        {
            return PianoMidiMin + (note.scale_piano - 1);
        }

        return -1;
    }

    public static int ResolveVisualSlotCount(NoteData note)
    {
        return HasPianoPitch(note) ? PianoKeyCount : LegacyLaneCount;
    }

    public static float ResolveTrackWidth(Transform trackTransform, float fallbackWidth = DefaultTrackWidth)
    {
        float trackWidth = fallbackWidth;

        try
        {
            if (trackTransform != null)
            {
                var renderer = trackTransform.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    trackWidth = renderer.bounds.size.x;
                }
                else
                {
                    float inferred = Mathf.Abs(trackTransform.lossyScale.x);
                    if (inferred > 0.001f)
                    {
                        trackWidth = inferred * 100f;
                    }
                }
            }
        }
        catch
        {
        }

        return Mathf.Max(0.01f, trackWidth);
    }

    public static float ResolveLaneWidth(float trackWidth, NoteData note)
    {
        return ResolveLaneWidth(trackWidth, ResolveVisualSlotCount(note));
    }

    public static float ResolveLaneWidth(float trackWidth, int slotCount)
    {
        return Mathf.Max(0.0001f, trackWidth / Mathf.Max(1, slotCount));
    }

    public static float ResolveVisualWidth(NoteData note, float trackWidth)
    {
        float laneWidth = ResolveLaneWidth(trackWidth, note);
        int span = HasPianoPitch(note)
            ? 1
            : Mathf.Max(1, Mathf.Abs(note.endLane - note.startLane) + 1);
        float gap = laneWidth * GapRatioPerLane;
        return Mathf.Max(laneWidth * 0.5f, (span * laneWidth) - gap);
    }

    public static float ResolveCenterX(NoteData note, float trackWidth)
    {
        float laneWidth = ResolveLaneWidth(trackWidth, note);
        float centerSlot = ResolveCenterSlot(note);
        return (centerSlot * laneWidth) - (trackWidth * 0.5f);
    }

    public static float ResolveCenterSlot(NoteData note)
    {
        if (HasPianoPitch(note))
        {
            int midiPitch = ResolveMidiPitch(note);
            float slot = Mathf.Clamp(midiPitch - PianoMidiMin, 0, PianoKeyCount - 1);
            return slot + 0.5f;
        }

        int minLane = Mathf.Min(note.startLane, note.endLane);
        int maxLane = Mathf.Max(note.startLane, note.endLane);
        return ((minLane + maxLane) * 0.5f) + 0.5f;
    }
}
