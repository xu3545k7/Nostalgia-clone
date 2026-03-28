using System.Collections.Generic;

/// <summary>
/// Defines the structure of a single note, matching the JSON format.
/// </summary>
[System.Serializable]
public class NoteData
{
    public int startTime;
    public int endTime;
    public int startLane;
    public int endLane;
    public string type;
    // numeric compatibility field for "soft" notes (0=tap, 1=soft, 2=hold)
    // The JSON converter writes "note_type" for compatibility; JsonUtility
    // will populate this field when the JSON contains that key.
    public int note_type = 0;
    public int hand;
}

/// <summary>
/// A single time-signature change event inside a chart.
/// </summary>
[System.Serializable]
public class TimeSigChange
{
    public int time_ms;
    public int numerator;
    public int denominator;
}

/// <summary>
/// Defines the overall structure of the chart file, matching the JSON format.
/// </summary>
[System.Serializable]
public class Chart
{
    public float first_bpm;
    public string time_signature;
    public int music_finish_time_msec;
    public List<NoteData> notes;
    public List<int> beat_timings;
    public List<TimeSigChange> time_signature_changes;
}
