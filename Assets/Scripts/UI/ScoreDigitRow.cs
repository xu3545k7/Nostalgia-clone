using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一排數字，一位數一格。
/// </summary>
/// <remarks>
/// **為什麼要自己排，而不是設一行字。** 分數的字型（Zen Antique）數字是比例寬度
/// 的：`1` 窄、`0` 寬。一行字排出來的縫因此不均勻，而且位數一變、整串的寬度就跟
/// 著變，數字會在原地左右挪。固定寬度的格子讓每一位站在自己格子的正中央，間距
/// 於是由格子決定，和字型無關。
///
/// **為什麼用舊的 <see cref="Text"/> 而不是 TMP。** 這一行分數本來就刻意避開 TMP
/// —— 某些圖形後端下，這一行大字的 TMP mesh 會是空的（見 ResultsScreen 的註解）。
/// 一位數一格反而把 TMP 那條路唯一的好處（等寬設定）也繞開了。
///
/// **壓印不是投影。** 每個字底下墊一道**紙色**的高光，看起來是壓進紙裡；投影是
/// 把字放在紙上再打一盞燈。這一頁是一本書，不是一塊看板。
/// </remarks>
[DisallowMultipleComponent]
public sealed class ScoreDigitRow : MonoBehaviour
{
    private readonly List<Text> cells = new List<Text>(8);

    private Font face;
    private int size = 76;
    private Color ink = Color.black;
    private Color emboss = new Color(1f, 1f, 1f, 0.8f);
    private float cell = 62f;

    /// <summary>整排數字目前有多寬。底下那條線靠它決定長度。</summary>
    public float Width { get; private set; }

    public RectTransform rectTransform => (RectTransform)transform;

    public void Configure(Font font, int fontSize, Color inkColour, Color embossColour, float cellWidth)
    {
        face = font;
        size = Mathf.Max(8, fontSize);
        ink = inkColour;
        emboss = embossColour;
        this.cell = Mathf.Max(4f, cellWidth);
        for (int i = 0; i < cells.Count; i++) Paint(cells[i]);
        Layout();
        // 格寬變了，整排的寬度就變了。以前這裡漏掉，Width 還停在上一個格寬算出
        // 來的數字 —— 而底下那條線和標籤的位置都是照 Width 排的，於是版面用一個
        // 尺寸畫數字、用另一個尺寸排它旁邊的東西。
        Width = VisibleCount() * cell;
    }

    public void SetValue(string text)
    {
        text ??= string.Empty;
        while (cells.Count < text.Length) cells.Add(NewCell(cells.Count));
        for (int i = 0; i < cells.Count; i++)
        {
            bool used = i < text.Length;
            if (cells[i].gameObject.activeSelf != used) cells[i].gameObject.SetActive(used);
            if (used) cells[i].text = text[i].ToString();
        }
        Width = text.Length * cell;
        Layout();
    }

    private Text NewCell(int index)
    {
        var host = new GameObject("Digit" + index, typeof(RectTransform), typeof(CanvasRenderer));
        host.layer = gameObject.layer;
        host.transform.SetParent(transform, false);
        var label = host.AddComponent<Text>();
        label.raycastTarget = false;
        Paint(label);

        // 壓印要在字之後加：mesh effect 照元件順序跑，而它做的是把整份 mesh 再
        // 複製一份墊在後面。
        var lift = host.AddComponent<Shadow>();
        lift.effectColor = emboss;
        lift.effectDistance = new Vector2(0f, -2f);
        return label;
    }

    private void Paint(Text label)
    {
        if (label == null) return;
        if (face != null) label.font = face;
        label.fontSize = size;
        // **不設粗體。** Zen Antique 沒有粗體字重，Unity 會自己把字往外抹一圈，
        // 抹出來的粗細不均勻 —— 那就是上一版看起來最廉價的地方。
        label.fontStyle = FontStyle.Normal;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = ink;
        label.resizeTextForBestFit = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        var lift = label.GetComponent<Shadow>();
        if (lift != null) lift.effectColor = emboss;
    }

    /// <summary>把每一格擺到自己的位置：整排以中心對齊。</summary>
    private int VisibleCount()
    {
        int count = 0;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].gameObject.activeSelf) count++;
        return count;
    }

    private void Layout()
    {
        int count = VisibleCount();
        float half = (count - 1) * 0.5f;

        int placed = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            if (!cells[i].gameObject.activeSelf) continue;
            RectTransform rect = cells[i].rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(cell, size * 1.6f);
            rect.anchoredPosition = new Vector2((placed - half) * cell, 0f);
            placed++;
        }
    }
}
