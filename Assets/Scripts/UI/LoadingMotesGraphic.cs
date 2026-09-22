using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 載入頁上慢慢往上飄的金色光點。
/// </summary>
/// <remarks>
/// **載入頁需要動，但不能用動來說話。** 玩家在這裡唯一要知道的事情是「還在跑，
/// 沒有當掉」，而那件事進度條已經在講了。所以這一層的工作只是讓畫面不是死的
/// —— 一旦它引人去看，它就搶走了本來該給曲繪和曲名的注意力。
///
/// 飄的東西剛好符合這個條件：沒有起點也沒有終點，看一眼知道還活著，再看第二眼
/// 也讀不出任何額外的資訊，於是眼睛自己會離開。
///
/// 整層是一個 <see cref="Graphic"/>：所有光點在同一份網格裡，一次繪製。載入頁
/// 是效能最緊的時候（解碼、解壓、預熱全在跑），這一層不該再多要幾十個 draw call。
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class LoadingMotesGraphic : MaskableGraphic
{
    /// <summary>幾顆。稀疏比密集耐看，而且這是一層背景。</summary>
    private const int Motes = 46;

    /// <summary>最慢和最快的上升速度，單位是「每秒佔畫面高度的幾分之幾」。</summary>
    private const float SlowestRise = 0.020f;
    private const float FastestRise = 0.062f;

    private float clock;

    private static Texture2D dot;

    /// <summary>
    /// 光點用的柔邊圓點。
    /// </summary>
    /// <remarks>
    /// 不指定貼圖的話 <see cref="Graphic"/> 會用引擎的白色貼圖，於是每一顆都是
    /// 一個**硬邊的正方形**。幾像素大的方塊讀起來是壞掉的像素，不是光；柔邊的
    /// 圓點才讀成飄在空氣裡的東西。
    ///
    /// 16x16 就夠——它只會被畫成幾像素大，而漸層沒有需要保留的細節。
    /// </remarks>
    public override Texture mainTexture
    {
        get
        {
            if (dot != null) return dot;
            const int Size = 16;
            dot = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size * 2f - 1f;
                    float dy = (y + 0.5f) / Size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 平方的衰減：線性的邊緣看得出來是一個圓的輪廓。
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * Size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            dot.SetPixels32(pixels);
            dot.Apply(false, false);
            return dot;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    private void Update()
    {
        // unscaled：載入的時候 timeScale 可能是 0，而這一層仍然要動——它存在的
        // 理由就是證明畫面沒有凍住。
        clock += Time.unscaledDeltaTime;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect area = GetPixelAdjustedRect();
        if (area.width <= 1f || area.height <= 1f) return;

        for (int i = 0; i < Motes; i++)
        {
            // 每顆的參數都從自己的序號用無理數推出來，不存狀態也不用亂數種子：
            // 同一顆永遠是同一顆，重開載入頁不會換一批。
            float lane = Mathf.Repeat(i * 0.6180339f, 1f);
            float grade = Mathf.Repeat(i * 0.7548777f, 1f);
            float phase = Mathf.Repeat(i * 0.4142135f, 1f);

            float rise = Mathf.Lerp(SlowestRise, FastestRise, grade);
            // 走到頂就從底下再進來。每顆的相位不同，所以不會整批一起回頭。
            float travel = Mathf.Repeat(phase + clock * rise, 1f);

            // 橫向的漂移。沿著一條直線上升的光點讀起來是進度條的刻度，不是灰塵。
            float drift = Mathf.Sin(clock * (0.35f + 0.4f * grade) + i * 2.399f) * 0.035f;
            float x = area.xMin + area.width * Mathf.Repeat(lane + drift, 1f);
            float y = area.yMin + area.height * travel;

            // 兩端淡出。從邊界硬生生冒出來的光點會把畫面的邊框畫出來。
            float ends = Mathf.Clamp01(travel / 0.18f) * Mathf.Clamp01((1f - travel) / 0.28f);
            // 大的暗、小的亮：這樣一層讀起來有遠近，而不是同一個平面上的一排點。
            float size = Mathf.Lerp(1.6f, 4.4f, grade);
            float alpha = ends * Mathf.Lerp(0.42f, 0.12f, grade)
                * (0.75f + 0.25f * Mathf.Sin(clock * 1.7f + i * 1.113f));
            if (alpha <= 0.004f) continue;

            Color32 tint = new Color(color.r, color.g, color.b, color.a * alpha);
            int at = vh.currentVertCount;
            vh.AddVert(new Vector3(x - size, y - size), tint, Vector2.zero);
            vh.AddVert(new Vector3(x - size, y + size), tint, Vector2.up);
            vh.AddVert(new Vector3(x + size, y + size), tint, Vector2.one);
            vh.AddVert(new Vector3(x + size, y - size), tint, Vector2.right);
            vh.AddTriangle(at, at + 1, at + 2);
            vh.AddTriangle(at, at + 2, at + 3);
        }
    }
}
