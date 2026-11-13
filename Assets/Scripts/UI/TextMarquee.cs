using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一行放不下的字，就讓它自己走過去。
/// </summary>
/// <remarks>
/// **為什麼抽出來。** 這段邏輯在 <c>GameBackgroundManager</c> 裡已經有兩份 —— 曲名一
/// 份、作者一份，各自四個狀態欄位加一個 Update 分支，連「把字搬進一個帶遮罩的視窗」
/// 那三十行都是抄的。第三個要用的地方出現時（考場的曲名欄）就該收攏，否則下次改捲動
/// 手感要改三個地方，而漏掉的那一個會自己走出不一樣的節奏。
///
/// **為什麼是把字搬進視窗，不是裁字串。** 裁成「エンドマークに希望と…」會讓兩首開頭
/// 相同的曲子看起來一模一樣，而玩家要靠這一行分辨要彈哪一首。走過去的字最後會把整個
/// 名字交出來。
///
/// **為什麼放不下才動。** 放得下還在動的字會一直抓走餘光，而這個畫面上真正要被讀的是
/// 難度和成績。<see cref="Refresh"/> 每次換字都量一次，塞得下就停在原位。
/// </remarks>
[RequireComponent(typeof(RectTransform))]
public sealed class TextMarquee : MonoBehaviour
{
    /// <summary>走多快（每秒多少畫素）。</summary>
    private const float Speed = 42f;
    /// <summary>開始走之前先停多久 —— 先讓人讀得到開頭。</summary>
    private const float StartDelay = 1.1f;
    /// <summary>走完一圈之後，頭尾之間留的空檔。</summary>
    private const float LoopPad = 48f;

    private RectTransform viewport;
    private RectTransform inner;
    private TextMeshProUGUI label;
    private float loopWidth;
    private float offset;
    private float elapsed;
    private bool running;

    /// <summary>
    /// 在 <paramref name="parent"/> 底下開一個寬 <paramref name="width"/> 的視窗，裡面放一行字。
    /// </summary>
    /// <remarks>
    /// 回傳的是那個 <see cref="TextMeshProUGUI"/>，呼叫端照常設字型大小、顏色、對齊；
    /// 要換字請走 <see cref="SetText"/>，它會重新量一次寬度。
    /// </remarks>
    public static TextMarquee Attach(Transform parent, Vector2 corner, Vector2 offset,
        Vector2 size)
    {
        var host = new GameObject("Marquee", typeof(RectTransform), typeof(RectMask2D));
        host.layer = parent.gameObject.layer;
        var rect = host.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        // 軟邊：字走到視窗邊緣時淡出，硬切看起來像破圖。只在左右軟，上下留 0 —— 這
        // 一行的高度本來就剛好包住字，上下再軟會把字的頂底也吃掉。
        var mask = host.GetComponent<RectMask2D>();
        mask.softness = new Vector2Int(10, 0);

        var marquee = host.AddComponent<TextMarquee>();
        marquee.viewport = rect;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = parent.gameObject.layer;
        marquee.inner = textObject.GetComponent<RectTransform>();
        marquee.inner.SetParent(rect, false);
        marquee.inner.anchorMin = new Vector2(0f, 0f);
        marquee.inner.anchorMax = new Vector2(0f, 1f);
        marquee.inner.pivot = new Vector2(0f, 0.5f);
        marquee.inner.anchoredPosition = Vector2.zero;
        marquee.inner.sizeDelta = new Vector2(size.x, 0f);

        marquee.label = textObject.GetComponent<TextMeshProUGUI>();
        marquee.label.raycastTarget = false;
        marquee.label.textWrappingMode = TextWrappingModes.NoWrap;
        marquee.label.overflowMode = TextOverflowModes.Overflow;
        marquee.label.alignment = TextAlignmentOptions.MidlineLeft;
        return marquee;
    }

    /// <summary>裡面那行字。設完樣式記得呼叫 <see cref="Refresh"/>。</summary>
    public TextMeshProUGUI Label => label;

    public void SetText(string value)
    {
        if (label == null) return;
        label.text = value ?? string.Empty;
        Refresh();
    }

    /// <summary>重新量一次：放得下就停住，放不下才開始走。</summary>
    public void Refresh()
    {
        if (label == null || viewport == null || inner == null) return;
        label.ForceMeshUpdate();
        float needed = label.preferredWidth;
        float room = viewport.rect.width;
        inner.sizeDelta = new Vector2(Mathf.Max(needed, room), 0f);
        offset = 0f;
        elapsed = 0f;
        inner.anchoredPosition = Vector2.zero;
        // 留一點餘裕才動：差一兩個畫素就開始捲，看起來像在抖。
        running = needed > room + 2f;
        loopWidth = running ? needed - room + LoopPad : 0f;
    }

    private void Update()
    {
        if (!running || inner == null) return;

        // 用 unscaled：暫停時這一行還是該讀得完。
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < StartDelay) return;

        offset += Speed * Time.unscaledDeltaTime;
        if (offset >= loopWidth)
        {
            offset = 0f;
            elapsed = 0f;
        }
        inner.anchoredPosition = new Vector2(-offset, 0f);
    }
}
