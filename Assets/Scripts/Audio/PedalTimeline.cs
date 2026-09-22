using System.Collections.Generic;

/// <summary>
/// Answers "is the sustain pedal down at this moment" for a chart.
/// </summary>
/// <remarks>
/// The spans come from the source MIDI's CC64 and were converted onto the chart's
/// own timeline by the qt_editor restore tool, so they can be compared against
/// song position directly.
///
/// Pedal changes matter more than pedal presence: a pianist lifts and re-presses
/// within a couple of milliseconds to clear the previous harmony, and syuten has
/// 94 spans separated by gaps as short as 2 ms. Those gaps must survive, which is
/// why spans are kept as-is instead of being merged into "pedal mostly down".
/// </remarks>
public sealed class PedalTimeline
{
    private static readonly PedalTimeline EmptyTimeline = new PedalTimeline(null);

    private readonly int[] starts;
    private readonly int[] ends;
    private readonly PedalSpan[] validated;

    public PedalTimeline(IReadOnlyList<PedalSpan> spans)
    {
        int count = spans?.Count ?? 0;
        var validStarts = new List<int>(count);
        var validEnds = new List<int>(count);
        var validSpans = new List<PedalSpan>(count);
        for (int i = 0; i < count; i++)
        {
            PedalSpan span = spans[i];
            if (span == null || span.end_ms <= span.start_ms) continue;
            validStarts.Add(span.start_ms);
            validEnds.Add(span.end_ms);
            validSpans.Add(new PedalSpan { start_ms = span.start_ms, end_ms = span.end_ms });
        }
        starts = validStarts.ToArray();
        ends = validEnds.ToArray();
        validated = validSpans.ToArray();
    }

    public static PedalTimeline Empty => EmptyTimeline;

    public int SpanCount => starts.Length;

    /// <summary>
    /// The spans themselves, for anything that has to draw the pedalling or reshape
    /// it rather than ask about one moment. Copied at construction, so a caller
    /// cannot edit the chart's own list through them.
    /// </summary>
    public IReadOnlyList<PedalSpan> Spans => validated;

    public bool HasPedal => starts.Length > 0;

    /// <summary>True while the pedal is held at this song position.</summary>
    public bool IsDown(double songMs)
    {
        return IndexAt(songMs) >= 0;
    }

    /// <summary>
    /// When the pedal currently holding this moment lifts, or -1 when it is up.
    /// A voice released under the pedal keeps ringing until then.
    /// </summary>
    public double LiftTimeAt(double songMs)
    {
        int index = IndexAt(songMs);
        return index >= 0 ? ends[index] : -1d;
    }

    /// <summary>
    /// True when the pedal came up at any point in (fromMs, toMs].
    /// </summary>
    /// <remarks>
    /// Sampling <see cref="IsDown"/> once a frame cannot see a pedal change: the
    /// gap between two spans is 2 ms at the median in real pedalling, and a frame
    /// is around 16 ms, so the lift falls between two samples and the harmony
    /// never gets cleared. Asking about the whole interval instead catches every
    /// change regardless of frame rate.
    /// </remarks>
    public bool LiftedBetween(double fromMs, double toMs)
    {
        if (ends.Length == 0 || toMs <= fromMs) return false;

        // Ends are sorted, so find the first lift after fromMs and see whether
        // it already happened by toMs.
        int low = 0;
        int high = ends.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (ends[mid] > fromMs)
            {
                candidate = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return candidate >= 0 && ends[candidate] <= toMs;
    }

    /// <summary>Index of the span covering this moment, or -1.</summary>
    private int IndexAt(double songMs)
    {
        if (starts.Length == 0) return -1;

        // Spans are sorted and non-overlapping, so the last one starting at or
        // before songMs is the only candidate.
        int low = 0;
        int high = starts.Length - 1;
        int candidate = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (starts[mid] <= songMs)
            {
                candidate = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        if (candidate < 0) return -1;
        return songMs <= ends[candidate] ? candidate : -1;
    }
}
