using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runtime-created song management window. It is opened only from Settings.
/// </summary>
public sealed class RuntimeSongLibraryPanel : MonoBehaviour
{
    public enum ManagementMode
    {
        Add,
        Edit,
        Delete,
        CreateCategory
    }

    private static RuntimeSongLibraryPanel activeInstance;
    private SongSelectionManager selection;
    private GameObject window;
    private TextMeshProUGUI windowTitle;
    private TextMeshProUGUI statusLabel;
    private TMP_InputField chartInput;
    private TMP_InputField addTitleInput;
    private TMP_InputField addAuthorInput;
    private TMP_InputField editTitleInput;
    private TMP_InputField editAuthorInput;
    private TMP_InputField audioInput;
    private TMP_InputField pianoInput;
    private TMP_InputField coverInput;
    private TMP_InputField difficultyInput;
    private TMP_InputField levelInput;
    private TMP_InputField newCategoryInput;
    private TMP_InputField editDifficultyNameInput;
    private TMP_InputField editDifficultyLevelInput;
    private TMP_InputField editDifficultyChartInput;
    private TMP_InputField editDifficultyAudioInput;
    private TMP_InputField editDifficultyPianoInput;
    private TMP_InputField editDifficultyCoverInput;
    private CategorySelector addCategoryDropdown;
    private CategorySelector editCategoryDropdown;
    private CategorySelector difficultyDropdown;
    private Button saveButton;
    private Button deleteButton;
    private Button manageDifficultyButton;
    private Button addDifficultySaveButton;
    private Button updateDifficultySaveButton;
    private GameObject editCategoryReadOnly;
    private TextMeshProUGUI editCategoryLabel;
    private GameObject addGroup;
    private GameObject selectionGroup;
    private GameObject songDetailsGroup;
    private GameObject difficultyManagementGroup;
    private GameObject categoryGroup;
    private List<ExternalSongLibrary.DifficultyInfo> difficultyInfos =
        new List<ExternalSongLibrary.DifficultyInfo>();
    private int selectedDifficultyIndex = -1;
    private ManagementMode managementMode;
    private float deleteArmedUntil;
    private float difficultyDeleteArmedUntil;

    public static void Attach(SongSelectionManager manager, GameObject selectionPanel)
    {
        if (manager == null || selectionPanel == null) return;
        RuntimeSongLibraryPanel existing =
            selectionPanel.GetComponentInChildren<RuntimeSongLibraryPanel>(true);
        if (existing != null)
        {
            activeInstance = existing;
            existing.selection = manager;
            return;
        }

        var host = new GameObject("RuntimeSongLibraryPanel", typeof(RectTransform),
            typeof(RuntimeSongLibraryPanel));
        host.layer = selectionPanel.layer;
        RectTransform rect = host.GetComponent<RectTransform>();
        rect.SetParent(selectionPanel.transform, false);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        RuntimeSongLibraryPanel panel = host.GetComponent<RuntimeSongLibraryPanel>();
        activeInstance = panel;
        panel.selection = manager;
        panel.Build();
        host.SetActive(false);
    }

    public static void OpenFromSettings(Transform settingsParent,
        ManagementMode mode = ManagementMode.Add)
    {
        if (activeInstance == null)
        {
            RuntimeSongLibraryPanel[] panels = Resources.FindObjectsOfTypeAll<RuntimeSongLibraryPanel>();
            if (panels != null && panels.Length > 0) activeInstance = panels[0];
        }
        if (activeInstance == null || settingsParent == null) return;
        activeInstance.managementMode = mode;
        activeInstance.RefreshLocalization();
        activeInstance.OpenWindowAt(settingsParent);
    }

    public static void RefreshActiveLocalization()
    {
        if (activeInstance != null) activeInstance.RefreshLocalization();
    }

    private void Build()
    {
        window = new GameObject("SongLibraryWindow", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Outline));
        window.layer = gameObject.layer;
        RectTransform windowRect = window.GetComponent<RectTransform>();
        windowRect.SetParent(transform.parent, false);
        windowRect.anchorMin = new Vector2(0.12f, 0.035f);
        windowRect.anchorMax = new Vector2(0.88f, 0.965f);
        windowRect.offsetMin = windowRect.offsetMax = Vector2.zero;
        window.GetComponent<Image>().color = new Color(0.10f, 0.045f, 0.025f, 0.992f);
        window.GetComponent<Outline>().effectColor = new Color(0.82f, 0.64f, 0.25f);
        window.GetComponent<Outline>().effectDistance = new Vector2(3f, -3f);

        windowTitle = AddLabel(window.transform, T("新增曲目", "新增曲目", "Add Song"),
            0.045f, 0.925f, 0.78f, 0.985f, 28f, TextAlignmentOptions.Left);
        AddButton(window.transform, T("關閉", "关闭", "Close"),
            0.82f, 0.925f, 0.96f, 0.985f, CloseWindow);

        addGroup = AddGroup(window.transform, "StructuredImport");
        BuildAddForm(addGroup.transform);
        selectionGroup = AddGroup(window.transform, "SelectionManagement");
        BuildSelectionForm(selectionGroup.transform);
        manageDifficultyButton = AddButton(selectionGroup.transform,
            T("管理難度", "管理难度", "Manage Difficulties"),
            0.34f, 0.055f, 0.66f, 0.15f, OpenDifficultyManagement);
        difficultyManagementGroup = AddFullGroup(selectionGroup.transform, "DifficultyManagement");
        BuildDifficultyManagementForm(difficultyManagementGroup.transform);
        difficultyManagementGroup.SetActive(false);
        categoryGroup = AddGroup(window.transform, "CategoryManagement");
        BuildCategoryForm(categoryGroup.transform);

        statusLabel = AddLabel(window.transform, string.Empty,
            0.045f, 0.015f, 0.955f, 0.115f, 15f, TextAlignmentOptions.TopLeft);
        statusLabel.enableAutoSizing = true;
        statusLabel.fontSizeMin = 11f;
        statusLabel.fontSizeMax = 16f;
        ApplyManagementMode();
        window.SetActive(false);
    }

    private void BuildAddForm(Transform parent)
    {
        chartInput = AddPathRow(parent, T("譜面 JSON / XML", "谱面 JSON / XML", "Chart JSON / XML"),
            0.84f, out Button chartButton);
        chartButton.onClick.AddListener(() => PickFile(chartInput,
            "Chart files\0*.json;*.xml\0JSON\0*.json\0XML\0*.xml\0\0",
            T("選擇譜面", "选择谱面", "Choose Chart")));

        addTitleInput = AddTextRow(parent, T("曲子名稱", "曲子名称", "Song Title"), 0.755f,
            T("必填", "必填", "Required"));
        addAuthorInput = AddTextRow(parent, T("作者", "作者", "Author"), 0.67f,
            T("必填", "必填", "Required"));

        audioInput = AddPathRow(parent, T("歌曲音訊", "歌曲音频", "Music Audio"), 0.585f,
            out Button audioButton);
        audioButton.onClick.AddListener(() => PickFile(audioInput,
            "Audio\0*.wav;*.ogg;*.mp3;*.aif;*.aiff\0\0",
            T("選擇歌曲音訊", "选择歌曲音频", "Choose Music Audio")));

        pianoInput = AddPathRow(parent, T("鋼琴音訊（選填）", "钢琴音频（选填）", "Piano Audio (optional)"),
            0.50f, out Button pianoButton);
        pianoButton.onClick.AddListener(() => PickFile(pianoInput,
            "Audio\0*.wav;*.ogg;*.mp3;*.aif;*.aiff\0\0",
            T("選擇鋼琴音訊", "选择钢琴音频", "Choose Piano Audio")));

        coverInput = AddPathRow(parent, T("曲繪（選填）", "曲绘（选填）", "Cover (optional)"),
            0.415f, out Button coverButton);
        coverButton.onClick.AddListener(() => PickFile(coverInput,
            "Images\0*.png;*.jpg;*.jpeg\0\0",
            T("選擇曲繪", "选择曲绘", "Choose Cover")));

        difficultyInput = AddTextRow(parent, T("難度名稱", "难度名称", "Difficulty"), 0.33f,
            T("例如 Real", "例如 Real", "e.g. Real"));
        levelInput = AddTextRow(parent, T("難度等級", "难度等级", "Level"), 0.25f, "1 - 99");
        levelInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        levelInput.text = "1";

        AddLabel(parent, T("分類", "分类", "Category"), 0.045f, 0.105f, 0.21f, 0.17f,
            17f, TextAlignmentOptions.MidlineLeft);
        addCategoryDropdown = AddDropdown(parent, 0.22f, 0.105f, 0.56f, 0.17f);
        AddButton(parent, T("驗證", "验证", "Validate"), 0.58f, 0.105f, 0.75f, 0.17f, ValidateStructured);
        AddButton(parent, T("匯入並重整", "导入并刷新", "Import & Refresh"),
            0.77f, 0.105f, 0.955f, 0.17f, ImportStructured);

        AddButton(parent, T("開啟曲庫資料夾", "打开曲库文件夹", "Open Library Folder"),
            0.045f, 0.02f, 0.30f, 0.08f, OpenLibraryFolder);
    }

    private void BuildSelectionForm(Transform parent)
    {
        editTitleInput = AddTextRow(parent, T("曲子名稱", "曲子名称", "Song Title"), 0.80f, string.Empty);
        editAuthorInput = AddTextRow(parent, T("作者", "作者", "Author"), 0.63f, string.Empty);
        AddLabel(parent, T("分類", "分类", "Category"), 0.07f, 0.42f, 0.22f, 0.53f,
            18f, TextAlignmentOptions.MidlineLeft);
        editCategoryDropdown = AddDropdown(parent, 0.23f, 0.41f, 0.91f, 0.53f);
        editCategoryReadOnly = new GameObject("CategoryReadOnly", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        editCategoryReadOnly.layer = parent.gameObject.layer;
        SetRect(editCategoryReadOnly.GetComponent<RectTransform>(), parent, 0.23f, 0.41f, 0.91f, 0.53f);
        editCategoryReadOnly.GetComponent<Image>().color = new Color(0.78f, 0.75f, 0.68f, 0.96f);
        editCategoryLabel = AddLabel(editCategoryReadOnly.transform, string.Empty,
            0.04f, 0.08f, 0.96f, 0.92f,
            17f, TextAlignmentOptions.MidlineLeft);
        editCategoryLabel.color = new Color(0.18f, 0.11f, 0.07f);
        editCategoryReadOnly.SetActive(false);
        AddButton(parent, T("載入目前曲目", "载入当前曲目", "Load Selected"),
            0.08f, 0.19f, 0.38f, 0.31f, LoadSelected);
        saveButton = AddButton(parent, T("儲存並重整", "保存并刷新", "Save & Refresh"),
            0.55f, 0.19f, 0.91f, 0.31f, SaveSelected);
        deleteButton = AddButton(parent, T("刪除目前曲目", "删除当前曲目", "Delete Selected"),
            0.55f, 0.19f, 0.91f, 0.31f, DeleteSelected);
    }

    private void BuildDifficultyManagementForm(Transform parent)
    {
        AddButton(parent, T("返回", "返回", "Back"), 0.045f, 0.90f, 0.17f, 0.975f,
            CloseDifficultyManagement);
        AddLabel(parent, T("目前難度", "当前难度", "Difficulty"),
            0.20f, 0.90f, 0.35f, 0.975f, 17f, TextAlignmentOptions.MidlineLeft);
        difficultyDropdown = AddDropdown(parent, 0.35f, 0.90f, 0.955f, 0.975f,
            _ => LoadDifficultyForm());

        AddButton(parent, T("新增難度", "新增难度", "Add Difficulty"),
            0.045f, 0.80f, 0.31f, 0.875f, BeginAddDifficulty);
        AddButton(parent, T("修改難度", "修改难度", "Edit Difficulty"),
            0.37f, 0.80f, 0.63f, 0.875f, BeginEditDifficulty);
        AddButton(parent, T("刪除難度", "删除难度", "Delete Difficulty"),
            0.69f, 0.80f, 0.955f, 0.875f, DeleteDifficulty);

        editDifficultyChartInput = AddPathRow(parent,
            T("譜面 JSON / XML", "谱面 JSON / XML", "Chart JSON / XML"),
            0.735f, out Button chartButton);
        chartButton.onClick.AddListener(() => PickFile(editDifficultyChartInput,
            "Chart files\0*.json;*.xml\0JSON\0*.json\0XML\0*.xml\0\0",
            T("選擇譜面", "选择谱面", "Choose Chart")));
        editDifficultyNameInput = AddTextRow(parent,
            T("難度名稱", "难度名称", "Difficulty"), 0.645f,
            T("例如 Real", "例如 Real", "e.g. Real"));
        editDifficultyLevelInput = AddTextRow(parent,
            T("難度等級", "难度等级", "Level"), 0.555f, "1 - 99");
        editDifficultyLevelInput.contentType = TMP_InputField.ContentType.IntegerNumber;

        editDifficultyAudioInput = AddPathRow(parent,
            T("歌曲音訊", "歌曲音频", "Music Audio"), 0.465f, out Button audioButton);
        audioButton.onClick.AddListener(() => PickFile(editDifficultyAudioInput,
            "Audio\0*.wav;*.ogg;*.mp3;*.aif;*.aiff\0\0",
            T("選擇歌曲音訊", "选择歌曲音频", "Choose Music Audio")));
        editDifficultyPianoInput = AddPathRow(parent,
            T("鋼琴音訊（選填）", "钢琴音频（选填）", "Piano Audio (optional)"),
            0.375f, out Button pianoButton);
        pianoButton.onClick.AddListener(() => PickFile(editDifficultyPianoInput,
            "Audio\0*.wav;*.ogg;*.mp3;*.aif;*.aiff\0\0",
            T("選擇鋼琴音訊", "选择钢琴音频", "Choose Piano Audio")));
        editDifficultyCoverInput = AddPathRow(parent,
            T("曲繪（選填）", "曲绘（选填）", "Cover (optional)"),
            0.285f, out Button coverButton);
        coverButton.onClick.AddListener(() => PickFile(editDifficultyCoverInput,
            "Images\0*.png;*.jpg;*.jpeg\0\0",
            T("選擇曲繪", "选择曲绘", "Choose Cover")));

        AddLabel(parent,
            T("新增時音訊與曲繪留空會沿用原曲；修改時未變更的檔案會保留。",
                "新增时音频与曲绘留空会沿用原曲；修改时未变更的文件会保留。",
                "Blank new assets inherit from the song; unchanged edit paths are retained."),
            0.045f, 0.17f, 0.955f, 0.235f, 14f, TextAlignmentOptions.Center);
        addDifficultySaveButton = AddButton(parent,
            T("新增並重整", "新增并刷新", "Add & Refresh"),
            0.34f, 0.065f, 0.66f, 0.15f, SaveNewDifficulty);
        updateDifficultySaveButton = AddButton(parent,
            T("儲存並重整", "保存并刷新", "Save & Refresh"),
            0.34f, 0.065f, 0.66f, 0.15f, SaveDifficultyChanges);
        addDifficultySaveButton.gameObject.SetActive(false);
    }

    private void BuildCategoryForm(Transform parent)
    {
        AddLabel(parent, T("建立新分類", "创建新分类", "Create Category"),
            0.08f, 0.63f, 0.92f, 0.75f, 23f, TextAlignmentOptions.Center);
        newCategoryInput = AddInput(parent, 0.12f, 0.47f, 0.88f, 0.58f,
            T("分類名稱", "分类名称", "Category name"));
        AddButton(parent, T("建立分類", "创建分类", "Create"),
            0.32f, 0.31f, 0.68f, 0.41f, CreateCategory);
        AddLabel(parent, T("建立後會出現在新增與修改曲目的分類下拉選單中。",
                "创建后会出现在新增与修改曲目的分类下拉选单中。",
                "The category will appear in the Add and Edit dropdowns."),
            0.10f, 0.18f, 0.90f, 0.28f, 16f, TextAlignmentOptions.Center);
    }

    private void OpenWindowAt(Transform parent)
    {
        if (window == null || parent == null) return;
        window.transform.SetParent(parent, false);
        ApplyManagementMode();
        window.SetActive(true);
        window.transform.SetAsLastSibling();
        if (managementMode == ManagementMode.Add)
        {
            RefreshCategoryDropdown(ExternalSongLibrary.DefaultCategory);
            if (string.IsNullOrWhiteSpace(difficultyInput.text)) difficultyInput.text = "Real";
            if (string.IsNullOrWhiteSpace(levelInput.text)) levelInput.text = "1";
            SetStatus(T("請分別選擇譜面、音訊與曲繪；匯入前會檢查格式與曲目結構。",
                "请分别选择谱面、音频与曲绘；导入前会检查格式与曲目结构。",
                "Choose each asset separately. The chart and generated song structure are validated before import."));
        }
        else if (managementMode == ManagementMode.CreateCategory)
        {
            SetStatus(T("ALL 會自動彙整全部曲目，不能建立或指派；一般曲目預設使用 Other。",
                "ALL 会自动汇总全部曲目，不能创建或指派；一般曲目默认使用 Other。",
                "ALL is generated automatically and cannot be assigned. Songs default to Other."));
        }
        else LoadSelected();
    }

    private void ApplyManagementMode()
    {
        bool add = managementMode == ManagementMode.Add;
        bool edit = managementMode == ManagementMode.Edit;
        bool delete = managementMode == ManagementMode.Delete;
        addGroup.SetActive(add);
        selectionGroup.SetActive(edit || delete);
        categoryGroup.SetActive(managementMode == ManagementMode.CreateCategory);
        if (saveButton != null) saveButton.gameObject.SetActive(edit);
        if (deleteButton != null) deleteButton.gameObject.SetActive(delete);
        if (manageDifficultyButton != null) manageDifficultyButton.gameObject.SetActive(edit);
        if (difficultyManagementGroup != null) difficultyManagementGroup.SetActive(false);
        SetSongDetailsVisible(true);
        if (editTitleInput != null) editTitleInput.interactable = edit;
        if (editAuthorInput != null) editAuthorInput.interactable = edit;
        if (editCategoryDropdown != null)
        {
            editCategoryDropdown.visible = edit;
            editCategoryDropdown.interactable = edit;
        }
        if (editCategoryReadOnly != null) editCategoryReadOnly.SetActive(delete);

        if (windowTitle == null) return;
        switch (managementMode)
        {
            case ManagementMode.Edit:
                windowTitle.text = T("修改曲目", "修改曲目", "Edit Song");
                break;
            case ManagementMode.Delete:
                windowTitle.text = T("刪除曲目", "删除曲目", "Delete Song");
                break;
            case ManagementMode.CreateCategory:
                windowTitle.text = T("建立分類", "创建分类", "Create Category");
                break;
            default:
                windowTitle.text = T("新增曲目", "新增曲目", "Add Song");
                break;
        }
    }

    private ExternalSongLibrary.StructuredImportRequest ReadRequest()
    {
        int.TryParse(levelInput?.text, out int level);
        return new ExternalSongLibrary.StructuredImportRequest
        {
            chartPath = chartInput?.text,
            displayName = addTitleInput?.text,
            author = addAuthorInput?.text,
            audioPath = audioInput?.text,
            pianoAudioPath = pianoInput?.text,
            coverPath = coverInput?.text,
            difficultyName = difficultyInput?.text,
            difficultyLevel = level,
            category = SelectedCategory()
        };
    }

    private void ValidateStructured()
    {
        ExternalSongLibrary.ValidationResult result =
            ExternalSongLibrary.ValidateStructured(ReadRequest());
        SetStatus(result.ToDisplayText(), result.IsValid);
    }

    private void ImportStructured()
    {
        if (!ExternalSongLibrary.ImportStructured(ReadRequest(), out string id, out string message))
        {
            SetStatus(message, false);
            return;
        }

        selection.RefreshSongLibraryAndFocus(id);
        ClearAddForm();
        RefreshCategoryDropdown(ExternalSongLibrary.DefaultCategory);
        SetStatus(message + "\n" + T("已重新整理選歌畫面並定位到剛加入的曲子；仍可自由切換曲目。",
            "已刷新选歌画面并定位到刚加入的曲子；仍可自由切换曲目。",
            "Song selection was refreshed and focused on the new song. Navigation remains unlocked."), true);
    }

    private void LoadSelected()
    {
        string id = selection.SelectedExternalSongId;
        editTitleInput.text = selection.SelectedSongTitle;
        editAuthorInput.text = selection.SelectedSongAuthor;
        RefreshCategoryDropdown(selection.SelectedSongCategory);
        if (editCategoryLabel != null)
        {
            editCategoryLabel.text = string.IsNullOrWhiteSpace(selection.SelectedSongCategory)
                ? ExternalSongLibrary.DefaultCategory
                : selection.SelectedSongCategory;
            ClassicalBookUITheme.ApplyLocalizedFont(editCategoryLabel);
        }
        bool editable = !string.IsNullOrEmpty(id);
        editTitleInput.interactable = editable && managementMode == ManagementMode.Edit;
        editAuthorInput.interactable = editable && managementMode == ManagementMode.Edit;
        editCategoryDropdown.interactable = editable && managementMode == ManagementMode.Edit;
        if (saveButton != null) saveButton.interactable = editable;
        if (deleteButton != null) deleteButton.interactable = editable;
        SetStatus(editable
            ? T("已載入目前選擇的玩家曲目。", "已载入当前选择的玩家曲目。",
                "Loaded the selected imported song.")
            : T("內建曲目只能查看，不能修改或刪除。", "内置曲目只能查看，不能修改或删除。",
                "Built-in songs are read-only."), editable);
    }

    private void SaveSelected()
    {
        string id = selection.SelectedExternalSongId;
        if (string.IsNullOrEmpty(id))
        {
            SetStatus(T("請先在選歌畫面選擇玩家匯入的曲目。", "请先在选歌画面选择玩家导入的曲目。",
                "Select an imported song first."), false);
            return;
        }
        if (ExternalSongLibrary.Update(id, editTitleInput.text, editAuthorInput.text, SelectedCategory(false),
                out string message))
        {
            selection.RefreshSongLibraryAndFocus(id);
            SetStatus(message, true);
        }
        else SetStatus(message, false);
    }

    private void OpenDifficultyManagement()
    {
        string id = selection.SelectedExternalSongId;
        if (string.IsNullOrEmpty(id))
        {
            SetStatus(T("內建曲目無法修改難度。", "内置曲目无法修改难度。",
                "Built-in song difficulties are read-only."), false);
            return;
        }

        RefreshDifficultyList();
        SetSongDetailsVisible(false);
        difficultyManagementGroup.SetActive(true);
        difficultyManagementGroup.transform.SetAsLastSibling();
        BeginEditDifficulty();
    }

    private void CloseDifficultyManagement()
    {
        if (difficultyManagementGroup != null) difficultyManagementGroup.SetActive(false);
        SetSongDetailsVisible(true);
    }

    private void SetSongDetailsVisible(bool visible)
    {
        if (selectionGroup == null) return;
        for (int i = 0; i < selectionGroup.transform.childCount; i++)
        {
            GameObject child = selectionGroup.transform.GetChild(i).gameObject;
            if (child == difficultyManagementGroup) continue;
            if (child.name == "CategoryOptions")
            {
                child.SetActive(false);
                continue;
            }
            child.SetActive(visible);
        }
        if (!visible) return;

        bool edit = managementMode == ManagementMode.Edit;
        bool delete = managementMode == ManagementMode.Delete;
        if (saveButton != null) saveButton.gameObject.SetActive(edit);
        if (deleteButton != null) deleteButton.gameObject.SetActive(delete);
        if (manageDifficultyButton != null) manageDifficultyButton.gameObject.SetActive(edit);
        if (editCategoryDropdown != null) editCategoryDropdown.visible = edit;
        if (editCategoryReadOnly != null) editCategoryReadOnly.SetActive(delete);
    }

    private void RefreshDifficultyList(string preferred = null)
    {
        string id = selection.SelectedExternalSongId;
        difficultyInfos = ExternalSongLibrary.GetDifficulties(id, out string message);
        var labels = new List<string>();
        for (int i = 0; i < difficultyInfos.Count; i++)
            labels.Add(difficultyInfos[i].DisplayLabel);

        if (difficultyDropdown != null)
            difficultyDropdown.SetOptions(labels,
                string.IsNullOrEmpty(preferred) && labels.Count > 0 ? labels[0] : preferred);
        LoadDifficultyForm();
        SetStatus(message, difficultyInfos.Count > 0);
    }

    private ExternalSongLibrary.DifficultyInfo GetSelectedDifficulty()
    {
        string selected = difficultyDropdown?.SelectedValue;
        ExternalSongLibrary.DifficultyInfo info = difficultyInfos.Find(item =>
            item != null && string.Equals(item.DisplayLabel, selected, StringComparison.Ordinal));
        if (info == null && difficultyInfos.Count > 0) info = difficultyInfos[0];
        return info;
    }

    private void LoadDifficultyForm()
    {
        ExternalSongLibrary.DifficultyInfo info = GetSelectedDifficulty();
        selectedDifficultyIndex = info?.index ?? -1;
        if (info == null) return;
        editDifficultyNameInput.text = info.difficultyName ?? string.Empty;
        editDifficultyLevelInput.text = info.difficultyLevel.ToString();
        editDifficultyChartInput.text = info.chartPath ?? string.Empty;
        editDifficultyAudioInput.text = info.audioPath ?? string.Empty;
        editDifficultyPianoInput.text = info.pianoAudioPath ?? string.Empty;
        editDifficultyCoverInput.text = info.coverPath ?? string.Empty;
    }

    private void BeginAddDifficulty()
    {
        selectedDifficultyIndex = -1;
        editDifficultyNameInput.text = string.Empty;
        editDifficultyLevelInput.text = "1";
        editDifficultyChartInput.text = string.Empty;
        editDifficultyAudioInput.text = string.Empty;
        editDifficultyPianoInput.text = string.Empty;
        editDifficultyCoverInput.text = string.Empty;
        addDifficultySaveButton.gameObject.SetActive(true);
        updateDifficultySaveButton.gameObject.SetActive(false);
        SetStatus(T("新增難度需要譜面、難度名稱與等級；音訊和曲繪留空會沿用原曲。",
            "新增难度需要谱面、难度名称与等级；音频和曲绘留空会沿用原曲。",
            "A chart, difficulty name and level are required. Blank assets inherit from the song."));
    }

    private void BeginEditDifficulty()
    {
        LoadDifficultyForm();
        addDifficultySaveButton.gameObject.SetActive(false);
        updateDifficultySaveButton.gameObject.SetActive(true);
        SetStatus(T("修改名稱或等級即可；只有重新選擇的檔案才會被替換。",
            "修改名称或等级即可；只有重新选择的文件才会被替换。",
            "Edit the name or level; only newly selected files are replaced."), true);
    }

    private ExternalSongLibrary.DifficultyEditRequest ReadDifficultyRequest(bool adding)
    {
        int.TryParse(editDifficultyLevelInput?.text, out int level);
        ExternalSongLibrary.DifficultyInfo current = adding ? null : GetSelectedDifficulty();
        return new ExternalSongLibrary.DifficultyEditRequest
        {
            difficultyName = editDifficultyNameInput?.text,
            difficultyLevel = level,
            chartPath = ChangedPath(editDifficultyChartInput?.text, current?.chartPath, adding),
            audioPath = ChangedPath(editDifficultyAudioInput?.text, current?.audioPath, adding),
            pianoAudioPath = ChangedPath(editDifficultyPianoInput?.text, current?.pianoAudioPath, adding),
            coverPath = ChangedPath(editDifficultyCoverInput?.text, current?.coverPath, adding)
        };
    }

    private static string ChangedPath(string entered, string existing, bool adding)
    {
        string value = (entered ?? string.Empty).Trim().Trim('"');
        if (!adding && string.Equals(value, existing ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    private void SaveNewDifficulty()
    {
        string id = selection.SelectedExternalSongId;
        if (!ExternalSongLibrary.AddDifficulty(id, ReadDifficultyRequest(true), out string message))
        {
            SetStatus(message, false);
            return;
        }

        string preferredName = editDifficultyNameInput.text.Trim();
        selection.RefreshSongLibraryAndFocus(id);
        RefreshDifficultyListByName(preferredName);
        SetStatus(message, true);
        BeginEditDifficulty();
    }

    private void SaveDifficultyChanges()
    {
        ExternalSongLibrary.DifficultyInfo current = GetSelectedDifficulty();
        if (current == null)
        {
            SetStatus(T("請先選擇難度。", "请先选择难度。", "Select a difficulty first."), false);
            return;
        }
        string id = selection.SelectedExternalSongId;
        string preferredName = editDifficultyNameInput.text.Trim();
        if (!ExternalSongLibrary.UpdateDifficulty(id, current.index,
                ReadDifficultyRequest(false), out string message))
        {
            SetStatus(message, false);
            return;
        }

        selection.RefreshSongLibraryAndFocus(id);
        RefreshDifficultyListByName(preferredName);
        SetStatus(message, true);
    }

    private void DeleteDifficulty()
    {
        ExternalSongLibrary.DifficultyInfo current = GetSelectedDifficulty();
        if (current == null)
        {
            SetStatus(T("請先選擇難度。", "请先选择难度。", "Select a difficulty first."), false);
            return;
        }
        if (Time.unscaledTime > difficultyDeleteArmedUntil)
        {
            difficultyDeleteArmedUntil = Time.unscaledTime + 4f;
            SetStatus(T($"再次按下「刪除難度」以確認刪除 {current.DisplayLabel}。",
                $"再次按下“删除难度”以确认删除 {current.DisplayLabel}。",
                $"Press Delete Difficulty again to remove {current.DisplayLabel}."));
            return;
        }

        difficultyDeleteArmedUntil = 0f;
        string id = selection.SelectedExternalSongId;
        if (!ExternalSongLibrary.DeleteDifficulty(id, current.index, out string message))
        {
            SetStatus(message, false);
            return;
        }
        selection.RefreshSongLibraryAndFocus(id);
        RefreshDifficultyList();
        SetStatus(message, true);
        BeginEditDifficulty();
    }

    private void RefreshDifficultyListByName(string preferredName)
    {
        string id = selection.SelectedExternalSongId;
        difficultyInfos = ExternalSongLibrary.GetDifficulties(id, out string message);
        var labels = new List<string>();
        string preferred = null;
        for (int i = 0; i < difficultyInfos.Count; i++)
        {
            labels.Add(difficultyInfos[i].DisplayLabel);
            if (string.Equals(difficultyInfos[i].difficultyName, preferredName,
                    StringComparison.OrdinalIgnoreCase))
                preferred = difficultyInfos[i].DisplayLabel;
        }
        difficultyDropdown.SetOptions(labels, preferred);
        LoadDifficultyForm();
        SetStatus(message, difficultyInfos.Count > 0);
    }

    private void DeleteSelected()
    {
        string id = selection.SelectedExternalSongId;
        if (string.IsNullOrEmpty(id))
        {
            SetStatus(T("內建曲目不能刪除。", "内置曲目不能删除.", "Built-in songs cannot be deleted."), false);
            return;
        }
        if (Time.unscaledTime > deleteArmedUntil)
        {
            deleteArmedUntil = Time.unscaledTime + 4f;
            SetStatus(T("這會刪除玩家曲庫中的副本；請在 4 秒內再按一次確認。",
                "这会删除玩家曲库中的副本；请在 4 秒内再按一次确认。",
                "This removes the imported copy. Press again within 4 seconds to confirm."), false);
            return;
        }
        deleteArmedUntil = 0f;
        if (ExternalSongLibrary.Delete(id, out string message))
        {
            selection.RefreshSongLibrary();
            editTitleInput.text = editAuthorInput.text = string.Empty;
            SetStatus(message, true);
        }
        else SetStatus(message, false);
    }

    private void CreateCategory()
    {
        if (ExternalSongLibrary.CreateCategory(newCategoryInput.text, out string message))
        {
            newCategoryInput.text = string.Empty;
            SetStatus(message, true);
        }
        else SetStatus(message, false);
    }

    private void RefreshCategoryDropdown(string preferred)
    {
        CategorySelector dropdown = managementMode == ManagementMode.Add
            ? addCategoryDropdown
            : editCategoryDropdown;
        if (dropdown == null) return;
        List<string> categories = selection != null
            ? selection.GetAvailableSongCategories()
            : ExternalSongLibrary.GetCategories();
        categories.RemoveAll(value =>
            string.Equals(value, ExternalSongLibrary.AllCategory, StringComparison.OrdinalIgnoreCase));
        if (!categories.Exists(value => string.Equals(value,
                ExternalSongLibrary.DefaultCategory, StringComparison.OrdinalIgnoreCase)))
            categories.Insert(0, ExternalSongLibrary.DefaultCategory);
        dropdown.SetOptions(categories, preferred);
    }

    private string SelectedCategory(bool add = true)
    {
        CategorySelector dropdown = add ? addCategoryDropdown : editCategoryDropdown;
        return dropdown != null ? dropdown.SelectedValue : ExternalSongLibrary.DefaultCategory;
    }

    private void ClearAddForm()
    {
        chartInput.text = addTitleInput.text = addAuthorInput.text = audioInput.text =
            pianoInput.text = coverInput.text = string.Empty;
        difficultyInput.text = "Real";
        levelInput.text = "1";
    }

    private void OpenLibraryFolder()
    {
        try
        {
            ExternalSongLibrary.EnsureCreated();
            Process.Start(new ProcessStartInfo
            {
                FileName = ExternalSongLibrary.RootPath,
                UseShellExecute = true
            });
            SetStatus(T("已開啟玩家曲庫資料夾。", "已打开玩家曲库文件夹。",
                "Opened the player song library folder."), true);
        }
        catch (Exception ex)
        {
            SetStatus(T("無法開啟資料夾：", "无法打开文件夹：", "Unable to open folder: ") + ex.Message);
        }
    }

    private void PickFile(TMP_InputField target, string filter, string title)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string path = WindowsFilePicker.TryPick(filter, title);
        if (!string.IsNullOrEmpty(path)) target.text = path;
#else
        SetStatus(T("此平台請直接貼上完整檔案路徑。", "此平台请直接粘贴完整文件路径。",
            "Paste the full file path on this platform."));
#endif
    }

    private void CloseWindow() => window.SetActive(false);

    private void SetStatus(string text, bool success = false)
    {
        if (statusLabel == null) return;
        statusLabel.text = text;
        statusLabel.color = success ? new Color(0.66f, 0.92f, 0.62f) : new Color(1f, 0.76f, 0.52f);
    }

    private void RefreshLocalization()
    {
        bool reopen = window != null && window.activeSelf;
        Transform parent = window != null ? window.transform.parent : transform.parent;
        if (window != null) DestroyImmediate(window);
        Build();
        if (window != null && parent != null) window.transform.SetParent(parent, false);
        if (reopen) OpenWindowAt(parent);
    }

    // 同一份判斷曾經有好幾份拷貝，現在集中在 Localize。這裡保留同名的短捷徑，
    // 因為這個檔案裡有幾十個呼叫點。
    private static string T(string traditional, string simplified, string english)
        => Localize.T(traditional, simplified, english);

    private static GameObject AddGroup(Transform parent, string name)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = parent.gameObject.layer;
        SetRect(root.GetComponent<RectTransform>(), parent, 0f, 0.10f, 1f, 0.92f);
        return root;
    }

    private static GameObject AddFullGroup(Transform parent, string name)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = parent.gameObject.layer;
        SetRect(root.GetComponent<RectTransform>(), parent, 0f, 0f, 1f, 1f);
        return root;
    }

    private static TMP_InputField AddPathRow(Transform parent, string label, float top,
        out Button chooseButton)
    {
        AddLabel(parent, label, 0.045f, top - 0.065f, 0.22f, top, 16f,
            TextAlignmentOptions.MidlineLeft);
        TMP_InputField input = AddInput(parent, 0.22f, top - 0.065f, 0.79f, top, string.Empty);
        chooseButton = AddButton(parent, T("選擇", "选择", "Choose"),
            0.805f, top - 0.065f, 0.955f, top, null);
        return input;
    }

    private static TMP_InputField AddTextRow(Transform parent, string label, float top, string placeholder)
    {
        AddLabel(parent, label, 0.045f, top - 0.065f, 0.22f, top, 16f,
            TextAlignmentOptions.MidlineLeft);
        return AddInput(parent, 0.22f, top - 0.065f, 0.955f, top, placeholder);
    }

    private static TMP_InputField AddInput(Transform parent, float x1, float y1, float x2, float y2,
        string placeholder)
    {
        var root = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(TMP_InputField));
        root.layer = parent.gameObject.layer;
        SetRect(root.GetComponent<RectTransform>(), parent, x1, y1, x2, y2);
        root.GetComponent<Image>().color = new Color(0.94f, 0.89f, 0.78f, 0.98f);
        TMP_InputField input = root.GetComponent<TMP_InputField>();
        TextMeshProUGUI text = AddLabel(root.transform, string.Empty,
            0.025f, 0.05f, 0.975f, 0.95f, 16f, TextAlignmentOptions.MidlineLeft);
        text.color = new Color(0.12f, 0.07f, 0.04f);
        TextMeshProUGUI hint = AddLabel(root.transform, placeholder,
            0.025f, 0.05f, 0.975f, 0.95f, 15f, TextAlignmentOptions.MidlineLeft);
        hint.color = new Color(0.35f, 0.28f, 0.22f, 0.60f);
        input.textComponent = text;
        input.textViewport = text.rectTransform;
        input.placeholder = hint;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onValueChanged.AddListener(_ => ClassicalBookUITheme.ApplyLocalizedFont(text));
        return input;
    }

    private static CategorySelector AddDropdown(Transform parent, float x1, float y1, float x2, float y2)
    {
        return AddDropdown(parent, x1, y1, x2, y2, null);
    }

    private static CategorySelector AddDropdown(Transform parent, float x1, float y1, float x2, float y2,
        Action<string> onSelected)
    {
        var root = new GameObject("Dropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Button));
        root.layer = parent.gameObject.layer;
        SetRect(root.GetComponent<RectTransform>(), parent, x1, y1, x2, y2);
        root.GetComponent<Image>().color = new Color(0.94f, 0.89f, 0.78f, 0.98f);
        TextMeshProUGUI caption = AddLabel(root.transform, ExternalSongLibrary.DefaultCategory,
            0.04f, 0.05f, 0.84f, 0.95f, 16f, TextAlignmentOptions.MidlineLeft);
        caption.color = new Color(0.12f, 0.07f, 0.04f);
        var arrowObject = new GameObject("DropdownArrow", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(ClassicalArrowGraphic));
        arrowObject.layer = root.layer;
        RectTransform arrowRect = arrowObject.GetComponent<RectTransform>();
        arrowRect.SetParent(root.transform, false);
        arrowRect.anchorMin = new Vector2(0.87f, 0.28f);
        arrowRect.anchorMax = new Vector2(0.95f, 0.72f);
        arrowRect.offsetMin = Vector2.zero;
        arrowRect.offsetMax = Vector2.zero;
        ClassicalArrowGraphic arrow = arrowObject.GetComponent<ClassicalArrowGraphic>();
        arrow.PointsUp = false;
        arrow.color = new Color(0.25f, 0.12f, 0.05f);
        arrow.raycastTarget = false;
        caption.raycastTarget = false;
        return new CategorySelector(root.GetComponent<RectTransform>(), parent,
            root.GetComponent<Button>(), caption, onSelected);
    }

    private static Button AddButton(Transform parent, string caption, float x1, float y1, float x2,
        float y2, UnityEngine.Events.UnityAction action)
    {
        var root = new GameObject(caption, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(Button), typeof(Outline));
        root.layer = parent.gameObject.layer;
        SetRect(root.GetComponent<RectTransform>(), parent, x1, y1, x2, y2);
        root.GetComponent<Image>().color = new Color(0.28f, 0.12f, 0.055f, 1f);
        root.GetComponent<Outline>().effectColor = new Color(0.75f, 0.57f, 0.22f);
        Button button = root.GetComponent<Button>();
        if (action != null) button.onClick.AddListener(action);
        AddLabel(root.transform, caption, 0f, 0f, 1f, 1f, 16f,
            TextAlignmentOptions.Center).raycastTarget = false;
        return button;
    }

    private static TextMeshProUGUI AddLabel(Transform parent, string caption, float x1, float y1,
        float x2, float y2, float size, TextAlignmentOptions alignment)
    {
        var root = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        root.layer = parent.gameObject.layer;
        SetRect(root.GetComponent<RectTransform>(), parent, x1, y1, x2, y2);
        TextMeshProUGUI label = root.GetComponent<TextMeshProUGUI>();
        label.text = caption;
        label.fontSize = size;
        label.color = new Color(0.95f, 0.87f, 0.67f);
        label.alignment = alignment;
        ClassicalBookUITheme.ApplyLocalizedFont(label);
        return label;
    }

    private static void SetRect(RectTransform rect, Transform parent,
        float x1, float y1, float x2, float y2)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x1, y1);
        rect.anchorMax = new Vector2(x2, y2);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private sealed class CategorySelector
    {
        private readonly List<string> options = new List<string>();
        private readonly RectTransform root;
        private readonly Transform popupParent;
        private Button button;
        private TextMeshProUGUI caption;
        private GameObject popup;
        private string selectedValue = ExternalSongLibrary.DefaultCategory;
        private readonly Action<string> onSelected;

        public string SelectedValue => string.IsNullOrWhiteSpace(selectedValue)
            ? ExternalSongLibrary.DefaultCategory
            : selectedValue;

        public bool interactable
        {
            get => button != null && button.interactable;
            set
            {
                if (button != null) button.interactable = value;
                if (!value && popup != null) popup.SetActive(false);
            }
        }

        public bool visible
        {
            get => root != null && root.gameObject.activeSelf;
            set
            {
                if (root != null) root.gameObject.SetActive(value);
                if (!value && popup != null) popup.SetActive(false);
            }
        }

        public CategorySelector(RectTransform targetRoot, Transform targetPopupParent,
            Button targetButton, TextMeshProUGUI targetCaption, Action<string> selectedCallback)
        {
            root = targetRoot;
            popupParent = targetPopupParent;
            button = targetButton;
            caption = targetCaption;
            onSelected = selectedCallback;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(TogglePopup);
        }

        public void SetOptions(List<string> values, string preferred)
        {
            options.Clear();
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    string value = values[i]?.Trim();
                    if (string.IsNullOrEmpty(value) ||
                        string.Equals(value, ExternalSongLibrary.AllCategory,
                            StringComparison.OrdinalIgnoreCase) ||
                        options.Exists(item => string.Equals(item, value,
                            StringComparison.OrdinalIgnoreCase)))
                        continue;
                    options.Add(value);
                }
            }
            if (options.Count == 0) options.Add(ExternalSongLibrary.DefaultCategory);
            int selectedIndex = options.FindIndex(item =>
                string.Equals(item, preferred, StringComparison.OrdinalIgnoreCase));
            Select(options[selectedIndex >= 0 ? selectedIndex : 0]);
            RebuildPopup();
        }

        private void TogglePopup()
        {
            if (!interactable) return;
            if (popup == null) RebuildPopup();
            if (popup == null) return;
            popup.SetActive(!popup.activeSelf);
            if (popup.activeSelf) popup.transform.SetAsLastSibling();
        }

        private void RebuildPopup()
        {
            if (popup != null)
            {
                popup.SetActive(false);
                UnityEngine.Object.Destroy(popup);
            }

            popup = new GameObject("CategoryOptions", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Outline));
            popup.layer = root.gameObject.layer;
            RectTransform rect = popup.GetComponent<RectTransform>();
            rect.SetParent(popupParent, false);
            bool openDownward = root.anchorMin.y > 0.55f;
            float anchorY = openDownward ? root.anchorMin.y : root.anchorMax.y;
            rect.anchorMin = new Vector2(root.anchorMin.x, anchorY);
            rect.anchorMax = new Vector2(root.anchorMax.x, anchorY);
            rect.pivot = new Vector2(0.5f, openDownward ? 1f : 0f);
            rect.anchoredPosition = new Vector2(0f, openDownward ? -5f : 5f);
            rect.sizeDelta = new Vector2(0f, Mathf.Min(420f, Mathf.Max(42f, options.Count * 38f)));
            popup.GetComponent<Image>().color = new Color(0.96f, 0.91f, 0.80f, 1f);
            popup.GetComponent<Outline>().effectColor = new Color(0.70f, 0.48f, 0.16f, 1f);
            popup.transform.SetAsLastSibling();

            int count = Mathf.Max(1, options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                string optionValue = options[i];
                float y2 = 1f - i / (float)count;
                float y1 = 1f - (i + 1) / (float)count;
                Button optionButton = AddButton(popup.transform, optionValue,
                    0.015f, y1 + 0.01f, 0.985f, y2 - 0.01f, () =>
                    {
                        Select(optionValue);
                        popup.SetActive(false);
                    });
                optionButton.GetComponent<Image>().color =
                    string.Equals(optionValue, selectedValue, StringComparison.OrdinalIgnoreCase)
                        ? new Color(0.45f, 0.21f, 0.08f, 1f)
                        : new Color(0.28f, 0.12f, 0.055f, 1f);
            }
            popup.SetActive(false);
        }

        private void Select(string value)
        {
            selectedValue = string.IsNullOrWhiteSpace(value)
                ? ExternalSongLibrary.DefaultCategory
                : value;
            if (caption != null)
            {
                caption.text = selectedValue;
                ClassicalBookUITheme.ApplyLocalizedFont(caption);
            }
            onSelected?.Invoke(selectedValue);
        }
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private static class WindowsFilePicker
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDir;
            public IntPtr title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public IntPtr defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reserved2;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName(ref OpenFileName data);

        public static string TryPick(string filter, string title)
        {
            const int capacity = 32768;
            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr filterBuffer = IntPtr.Zero;
            IntPtr titleBuffer = IntPtr.Zero;
            try
            {
                fileBuffer = Marshal.AllocHGlobal(capacity * sizeof(char));
                for (int offset = 0; offset < capacity * sizeof(char); offset += sizeof(long))
                    Marshal.WriteInt64(fileBuffer, offset, 0L);
                filterBuffer = Marshal.StringToHGlobalUni(filter);
                titleBuffer = Marshal.StringToHGlobalUni(title);
                var data = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    filter = filterBuffer,
                    filterIndex = 1,
                    file = fileBuffer,
                    maxFile = capacity,
                    title = titleBuffer,
                    flags = 0x00001000 | 0x00000800 | 0x00000008
                };
                return GetOpenFileName(ref data) ? Marshal.PtrToStringUni(fileBuffer) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RuntimeSongLibraryPanel] File picker failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (titleBuffer != IntPtr.Zero) Marshal.FreeHGlobal(titleBuffer);
                if (filterBuffer != IntPtr.Zero) Marshal.FreeHGlobal(filterBuffer);
                if (fileBuffer != IntPtr.Zero) Marshal.FreeHGlobal(fileBuffer);
            }
        }
    }
#endif
}
