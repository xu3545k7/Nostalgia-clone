using System.Collections.Generic;
using UnityEngine;

public class MidiNoteToKeyMapper
{
    // 你的 28 鍵 note 對應表
    private static readonly int[] midiNotes = new int[]
    {
        36, 38, 40, 41, 43, 45, 47, 48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83
    };
    // The middle 28 white keys of a standard 88-key piano (A0-C8).
    // Black keys and notes outside this range are intentionally ignored.
    private static readonly int[] centralPianoWhiteNotes = new int[]
    {
        41, 43, 45, 47, 48, 50, 52, 53, 55, 57, 59, 60, 62, 64,
        65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84, 86, 88
    };

    // 建立 note->key 映射表
    private static readonly Dictionary<int, int> noteToKey = new Dictionary<int, int>();
    private static readonly Dictionary<int, int> centralPianoNoteToKey =
        new Dictionary<int, int>();
    static MidiNoteToKeyMapper()
    {
        for (int i = 0; i < midiNotes.Length; i++)
        {
            noteToKey[midiNotes[i]] = i;
        }
        for (int i = 0; i < centralPianoWhiteNotes.Length; i++)
        {
            centralPianoNoteToKey[centralPianoWhiteNotes[i]] = i;
        }
    }

    // 取得對應的遊戲鍵位（找不到回傳 -1）
    public static int GetKeyIndex(int midiNote)
    {
        bool useCentralPiano = SettingsManager.Instance != null &&
            SettingsManager.Instance.InputMode == InputModeType.MidiKeyboard;
        Dictionary<int, int> mapping = useCentralPiano
            ? centralPianoNoteToKey
            : noteToKey;
        if (mapping.TryGetValue(midiNote, out int key))
            return key;
        return -1;
    }
}

// 範例：在 GameManager 或 MIDIInputManager 使用
// HandleMidiNoteOn(int note, float velocity)
// int keyIndex = MidiNoteToKeyMapper.GetKeyIndex(note);
// if (keyIndex >= 0) { /* 處理 keyIndex 與 velocity */ }
