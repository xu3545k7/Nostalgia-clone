using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 變速預告時，夾在「現在的 BPM」和「等一下的 BPM」之間的那個三角形。
/// </summary>
/// <remarks>
/// **為什麼不是字元。** HUD 的主字型是 ZenAntique，而它**沒有** U+25B6（▶）
/// 這個字 —— 現在印得出來是因為後備鏈上的思源黑體有。那是另一套字面（字重、
/// 尖端角度都不一樣），而且後備鏈是每次 <c>StyleText</c> 重排的，順序哪天一改
/// 就是一顆豆腐印在讀數正中間。這個專案已經為了同一個字做過同一個決定：
/// <c>SettingsGlyph</c> 整個類別存在的理由就是「‹ › − ▶ 某些語言字型沒有」。
///
/// 畫成網格還有第二個好處：大小和位置可以對著**旁邊那個數字實際畫出來的框**
/// 算（見 <c>BpmDisplay.UpdatePendingArrow</c>），而不是對著字級猜 —— 那一塊的
/// 字級是寫死的絕對點數，數字多一位整個就會錯開。
///
/// 這個類別只會被 <c>AddComponent</c> 叫到，不掛在任何 prefab 上。
/// </remarks>
public sealed class BpmChangeArrow : MaskableGraphic
{
    /// <summary>尖端朝左（指著左邊那個預告值）還是朝右。</summary>
    public bool PointsLeft
    {
        get => pointsLeft;
        set
        {
            if (pointsLeft == value) return;
            pointsLeft = value;
            SetVerticesDirty();
        }
    }

    private bool pointsLeft;

    /// <summary>尖端留多鈍，以半高為單位。0 ＝ 真的收到一點。</summary>
    /// <remarks>
    /// 完全尖的那一點在這個尺寸（十幾個像素）下不到一個像素寬，抗鋸齒會把它
    /// 化成一條半透明的毛邊 —— 讀起來是「這個三角形沒畫完」。切掉一點點，
    /// 尖端就永遠有實體，而在這個大小下看起來仍然是尖的。
    /// </remarks>
    private const float TipFlat = 0.13f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0.01f || r.height <= 0.01f) return;

        float centreY = r.center.y;
        float half = r.height * 0.5f;
        float tipHalf = half * TipFlat;

        UIVertex vert = UIVertex.simpleVert;
        vert.color = color;
        vert.uv0 = Vector2.zero;

        float back = pointsLeft ? r.xMax : r.xMin;
        float tip = pointsLeft ? r.xMin : r.xMax;
        vert.position = new Vector3(back, centreY + half); vh.AddVert(vert);
        vert.position = new Vector3(back, centreY - half); vh.AddVert(vert);
        vert.position = new Vector3(tip, centreY - tipHalf); vh.AddVert(vert);
        vert.position = new Vector3(tip, centreY + tipHalf); vh.AddVert(vert);

        // UI 的預設材質是 Cull Off，所以繞向不影響；兩個三角形拼出一個切平尖端的箭頭。
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }
}
