using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class ChartEditor : MonoBehaviour
{
    public Chart currentChart;
    public string chartFilePath;

    [Header("UI Elements")]
    public InputField startTimeInput;
    public InputField endTimeInput;
    public InputField startLaneInput;
    public InputField endLaneInput;
    public Dropdown typeDropdown;
    public InputField handInput;
    public Button addButton;
    public Button deleteButton;
    public Button saveButton;

    private int selectedNoteIndex = -1;

    void Start()
    {
        // Load the chart from file
        LoadChart();

        // Bind button events
        addButton.onClick.AddListener(AddNote);
        deleteButton.onClick.AddListener(DeleteNote);
        saveButton.onClick.AddListener(SaveChart);
    }

    void LoadChart()
    {
        if (File.Exists(chartFilePath))
        {
            string json = File.ReadAllText(chartFilePath);
            currentChart = JsonUtility.FromJson<Chart>(json);
        }
    }

    void SaveChart()
    {
        string json = JsonUtility.ToJson(currentChart, true);
        File.WriteAllText(chartFilePath, json);
    }

    public void AddNote()
    {
        NoteData newNote = new NoteData
        {
            startTime = int.Parse(startTimeInput.text),
            endTime = int.Parse(endTimeInput.text),
            startLane = int.Parse(startLaneInput.text),
            endLane = int.Parse(endLaneInput.text),
            type = typeDropdown.options[typeDropdown.value].text,
            hand = int.Parse(handInput.text)
        };

        currentChart.notes.Add(newNote);
    }

    public void DeleteNote()
    {
        if (selectedNoteIndex >= 0 && selectedNoteIndex < currentChart.notes.Count)
        {
            currentChart.notes.RemoveAt(selectedNoteIndex);
        }
    }

    public void SelectNote(int index)
    {
        if (index >= 0 && index < currentChart.notes.Count)
        {
            selectedNoteIndex = index;
            NoteData note = currentChart.notes[index];

            // Populate UI fields with note data
            startTimeInput.text = note.startTime.ToString();
            endTimeInput.text = note.endTime.ToString();
            startLaneInput.text = note.startLane.ToString();
            endLaneInput.text = note.endLane.ToString();
            handInput.text = note.hand.ToString();

            // Set dropdown value
            int dropdownIndex = typeDropdown.options.FindIndex(option => option.text == note.type);
            if (dropdownIndex >= 0)
            {
                typeDropdown.value = dropdownIndex;
            }
        }
    }
}