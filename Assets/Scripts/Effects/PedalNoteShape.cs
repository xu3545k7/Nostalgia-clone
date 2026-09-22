using UnityEngine;

/// <summary>The shape of a pedal note, and how wide a change may be played.</summary>
/// <remarks>
/// Two presets rather than a difficulty scale, because the modes ask different
/// questions. Standard asks whether the player pressed in the right place and leaves
/// the sound alone. Hardcore asks whether the pedalling made the piece sound good,
/// which is why its numbers are tighter but not by much: the real difference is that
/// its audio follows the foot, so holding too long muddies the harmony on its own and
/// needs no judgment at all.
/// </remarks>
[System.Serializable]
public struct PedalNoteTuning
{
    [Tooltip("Depth at the head, where the pedal goes down.")]
    public float headAlpha;

    [Tooltip("Depth through the middle, where the pedal is merely held. Deliberately almost invisible.")]
    public float idleAlpha;

    [Tooltip("Depth at the terminator, where the pedal comes up.")]
    public float tailAlpha;

    [Tooltip("How long the head stays dark, in ms of chart time. Constant time means constant on-screen length at a given scroll speed.")]
    public float headRampMs;

    [Tooltip("How long before the terminator the body starts darkening, in ms. This is also the window in which releasing early is accepted, so that darkening and 'you may let go' are the same statement.")]
    public float tailRampMs;

    [Tooltip("Cap on either ramp as a fraction of the note, so a short note does not darken end to end and lose its shape.")]
    public float rampSpanFraction;

    [Tooltip("Shortest press-to-press interval a foot can place. Presses closer than this are pushed later to make room.")]
    public int minChangeIntervalMs;

    [Tooltip("How much later a press may be pushed to become reachable. Past this the change is given up and held through instead. Pushing is syncopated pedalling, so a nudge in this direction stays musically correct; hardcore allows less because its audio follows the foot.")]
    public int maxPressShiftMs;

    [Tooltip("Shortest lift worth drawing, so the darkness between two notes survives being sampled once a frame.")]
    public int minVisibleGapMs;

    [Tooltip("Notes shorter than this are dropped.")]
    public int minSpanMs;

    public static PedalNoteTuning Standard => new PedalNoteTuning
    {
        headAlpha = 1f,
        idleAlpha = 0.72f,
        tailAlpha = 1f,
        headRampMs = 250f,
        tailRampMs = 200f,
        rampSpanFraction = 0.4f,
        minChangeIntervalMs = 250,
        maxPressShiftMs = 120,
        minVisibleGapMs = 80,
        minSpanMs = 80,
    };

    public static PedalNoteTuning Hardcore => new PedalNoteTuning
    {
        headAlpha = 1f,
        idleAlpha = 0.72f,
        tailAlpha = 1f,
        headRampMs = 250f,
        tailRampMs = 120f,
        rampSpanFraction = 0.4f,
        minChangeIntervalMs = 120,
        maxPressShiftMs = 90,
        minVisibleGapMs = 80,
        minSpanMs = 80,
    };
}

/// <summary>
/// How dark a pedal note is along its own length.
/// </summary>
/// <remarks>
/// The pedal is down for most of a piece, so a note drawn at an even depth would be a
/// long bright constant carrying no information. What carries information is the two
/// ends: the head is where the pedal goes down as it crosses the judgment line, and
/// the terminator is where it comes up. So the body is dark at both ends and nearly
/// invisible through the middle, which also makes the darkening on the way into the
/// terminator the answer to "when may I let go" — the tolerance never has to be
/// explained, only looked at.
///
/// Both ramps are expressed in milliseconds and converted to a fraction of the note,
/// so they stay a constant length on screen instead of stretching with the note. A
/// seven-second pedal must not start warning about its release six seconds early.
///
/// This mirrors PedalNote.shader exactly. It exists so the curve can be tested and
/// argued about in C# rather than only observed on screen.
/// </remarks>
public static class PedalNoteShape
{
    /// <summary>
    /// A ramp as a fraction of the note it sits on, capped so both ends still leave a
    /// middle. Returns 0 for a degenerate note, which the shader reads as "no ramp".
    /// </summary>
    public static float RampFraction(float rampMs, float noteLengthMs, float spanFraction)
    {
        if (noteLengthMs <= 0f) return 0f;
        float capped = Mathf.Min(Mathf.Max(0f, rampMs), noteLengthMs * Mathf.Max(0f, spanFraction));
        return Mathf.Clamp01(capped / noteLengthMs);
    }

    /// <summary>
    /// Depth at <paramref name="t"/> along the note, 0 at the head and 1 at the
    /// terminator.
    /// </summary>
    public static float AlphaAt(float t, float headRamp, float tailRamp, in PedalNoteTuning tuning)
    {
        t = Mathf.Clamp01(t);

        // Squared so each end's darkness is concentrated at the end itself: the head
        // falls away quickly the way a damper leaves the strings, and the terminator
        // arrives late rather than sitting dark for its whole length.
        float head = headRamp > 0f ? 1f - Mathf.Clamp01(t / headRamp) : 0f;
        float tail = tailRamp > 0f ? 1f - Mathf.Clamp01((1f - t) / tailRamp) : 0f;

        // On a note too short to hold both, the darker end wins rather than the two
        // ramps cancelling each other in the middle.
        return Mathf.Max(
            Mathf.Lerp(tuning.idleAlpha, tuning.headAlpha, head * head),
            Mathf.Lerp(tuning.idleAlpha, tuning.tailAlpha, tail * tail));
    }
}
