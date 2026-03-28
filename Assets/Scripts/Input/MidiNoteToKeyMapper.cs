using System.Collections.Generic;
using UnityEngine;

public class MidiNoteToKeyMapper
{
    // 你的 28 鍵 note 對應表
    private static readonly int[] midiNotes = new int[]
    {
        36, 38, 40, 41, 43, 45, 47, 48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83
    };

    // 建立 note->key 映射表
    private static readonly Dictionary<int, int> noteToKey = new Dictionary<int, int>();
    static MidiNoteToKeyMapper()
    {
        for (int i = 0; i < midiNotes.Length; i++)
        {
            noteToKey[midiNotes[i]] = i;
        }
    }

    // 取得對應的遊戲鍵位（找不到回傳 -1）
    public static int GetKeyIndex(int midiNote)
    {
        if (noteToKey.TryGetValue(midiNote, out int key))
            return key;
        return -1;
    }
}

// 範例：在 GameManager 或 MIDIInputManager 使用
// HandleMidiNoteOn(int note, float velocity)
// int keyIndex = MidiNoteToKeyMapper.GetKeyIndex(note);
// if (keyIndex >= 0) { /* 處理 keyIndex 與 velocity */ }
