using UnityEngine;
using MidiJack;

public class MIDIInputManager : MonoBehaviour
{
    public static MIDIInputManager Instance { get; private set; }

    public delegate void MidiNoteEvent(int note, float velocity);
    public event MidiNoteEvent OnMidiNoteOn;
    public event MidiNoteEvent OnMidiNoteOff;

    private float[] lastVelocities = new float[128];

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        for (int note = 0; note < 128; note++)
        {
            float velocity = MidiMaster.GetKey(note);
            if (velocity > 0f && lastVelocities[note] == 0f)
            {
                OnMidiNoteOn?.Invoke(note, velocity);
            }
            else if (velocity == 0f && lastVelocities[note] > 0f)
            {
                OnMidiNoteOff?.Invoke(note, lastVelocities[note]);
            }
            lastVelocities[note] = velocity;
        }
    }
}
