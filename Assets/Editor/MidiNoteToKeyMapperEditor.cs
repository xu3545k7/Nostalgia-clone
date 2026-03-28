using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MidiNoteToKeyMapperEditorData))]
public class MidiNoteToKeyMapperEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MidiNoteToKeyMapperEditorData data = (MidiNoteToKeyMapperEditorData)target;
        EditorGUILayout.LabelField("MIDI Note 對應遊戲鍵位 (0~27)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        for (int i = 0; i < data.midiNotes.Length; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"鍵位 {i}", GUILayout.Width(60));
            data.midiNotes[i] = EditorGUILayout.IntField(data.midiNotes[i], GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("儲存對應表"))
        {
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
    }
}

[CreateAssetMenu(fileName = "MidiNoteToKeyMapperEditorData", menuName = "MIDI/MidiNoteToKeyMapperEditorData")]
public class MidiNoteToKeyMapperEditorData : ScriptableObject
{
    public int[] midiNotes = new int[]
    {
        36, 38, 40, 41, 43, 45, 47, 48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83
    };
}
