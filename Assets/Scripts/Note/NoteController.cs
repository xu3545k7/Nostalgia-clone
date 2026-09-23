using System;
using System.Collections.Generic;
using UnityEngine;
#pragma warning disable CS0414
using UnityEngine.Rendering;
using Judgment;

public class NoteController : MonoBehaviour
{
    public Material rightHandMaterial; // 在 Inspector 中指定右手紅色材質
    public Material leftHandMaterial;  // 在 Inspector 中指定左手藍色材質
    public Sprite rightHandSprite; // 可選：在 Inspector 中指定右手的圖片 (優先於 Material)
    public Sprite leftHandSprite;  // 可選：在 Inspector 中指定左手的圖片 (優先於 Material)
    // Staccato-specific sprites (optional). If assigned, these override the normal hand sprites for staccato notes.
    public Sprite staccatoRightSprite;
    public Sprite staccatoLeftSprite;
    // Soft note sprite (type==1). Assign in Inspector to override visual for "soft" notes.
    public Sprite softSprite;
    public Texture2D softTexture;
    public Texture2D rightHandTexture; // 可選：直接指定貼圖檔 (拖 PNG 也行)
    public Texture2D leftHandTexture;  // 可選：直接指定貼圖檔 (拖 PNG 也行)
    [Header("Glass Theme")]
    [SerializeField] private Shader glassThemeShaderOverride;
    [SerializeField] private Color rightThemeColor = new Color(1f, 0.1764706f, 0.1764706f, 1f); // #FF2D2D
    [SerializeField] private Color leftThemeColor = new Color(0.2745098f, 0.6392157f, 1f, 1f);   // #46A3FF
    [SerializeField, ColorUsage(true, true)] private Color softThemeColor = new Color(1f, 0.72f, 0.22f, 1f);
    [Header("Gesture Note Theme")]
    [SerializeField, ColorUsage(true, true)] private Color slideThemeColor = new Color(0.40f, 0.94f, 1f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color trillThemeColor = new Color(1f, 0.1764706f, 0.1764706f, 1f);
    [SerializeField, Range(0f, 4f)] private float noteEmission = 1.45f;
    [SerializeField, Range(0f, 4f)] private float noteRimGlow = 1.3f;
    [SerializeField, Range(0f, 1f)] private float noteShimmerStrength = 0.2f;
    [Header("Tap White Glass Inset")]
    [SerializeField, ColorUsage(true, true)] private Color tapGlassColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float tapGlassOpacity = 0.42f;
    [SerializeField, Range(0f, 4f)] private float tapGlassRimGlow = 1.45f;
    [SerializeField, Range(0.1f, 0.48f)] private float tapGlassInsetX = 0.40f;
    [SerializeField, Range(0.05f, 0.45f)] private float tapGlassInsetY = 0.27f;
    [SerializeField, Range(0.005f, 0.12f)] private float tapGlassBorder = 0.035f;
    [SerializeField, Range(0f, 0.15f)] private float tapGlassChamfer = 0.055f;
    [Header("Note Fan Particles")]
    [SerializeField] private bool enableNoteFanParticles = true;
    [SerializeField] private Texture2D noteFanParticleTexture;
    [SerializeField, Range(1, 128)] private int noteFanBurstCount = 48;
    [SerializeField, Range(1f, 60f)] private float noteFanEmissionRate = 18f;
    [SerializeField, Range(0.1f, 15f)] private float noteFanSpeed = 3.2f;
    [SerializeField, Range(5f, 80f)] private float noteFanHalfAngle = 34f;
    [SerializeField, Range(0.05f, 1.5f)] private float noteFanLifetime = 0.62f;
    [SerializeField, Range(0.02f, 1.5f)] private float noteFanParticleSize = 0.16f;
    [Header("Staccato Indicator")]
    [SerializeField] private Sprite rightStaccatoIndicatorSprite;
    [SerializeField] private Sprite leftStaccatoIndicatorSprite;
    [SerializeField] private Vector3 staccatoIndicatorOffset = new Vector3(0f, 0.6f, 0f);
    [SerializeField] private float staccatoIndicatorScale = 1f;
    [SerializeField] private bool staccatoIndicatorUpright = true;
    [SerializeField] private bool staccatoIndicatorOffsetRelativeToWidth = true;

    // ... (其餘屬性不變) ...
    public float startTime;
    public float endTime;
    
    private float spawnZ;
    private float judgmentZ = 0.0f; // 判定線的 Z 軸位置
    private Transform judgmentLineTransformForNote = null;
    // Cached judgment line renderer sorting info so notes can ensure they render above it
    private int cachedJudgmentSortingOrder = int.MinValue;
    private int cachedJudgmentSortingLayerID = -1;

    private Conductor conductor;
    private bool didTryFindConductor = false;
    private NoteData noteData;
    private NoteSpawner noteSpawner;
    private Vector3 parentLossyScale = Vector3.one;
    private Renderer noteRenderer;
    private SpriteRenderer runtimeSpriteRenderer;
    private MaterialPropertyBlock notePropertyBlock;
    private static readonly int TapGlassEnabledId = Shader.PropertyToID("_TapGlassEnabled");
    private static readonly int TapGlassColorId = Shader.PropertyToID("_TapGlassColor");
    private static readonly int TapGlassOpacityId = Shader.PropertyToID("_TapGlassOpacity");
    private static readonly int TapGlassRimGlowId = Shader.PropertyToID("_TapGlassRimGlow");
    private static readonly int TapGlassInsetXId = Shader.PropertyToID("_TapGlassInsetX");
    private static readonly int TapGlassInsetYId = Shader.PropertyToID("_TapGlassInsetY");
    private static readonly int TapGlassBorderId = Shader.PropertyToID("_TapGlassBorder");
    private static readonly int TapGlassChamferId = Shader.PropertyToID("_TapGlassChamfer");
    private static readonly int ThemeColorId = Shader.PropertyToID("_ThemeColor");
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
    private static readonly int RimGlowId = Shader.PropertyToID("_RimGlow");
    private bool isInitialized = false;
    [Header("Hold Tail Visual")]
    [SerializeField] private Texture2D rightHoldTailTexture;
    [SerializeField] private Texture2D leftHoldTailTexture;
    [SerializeField] private Shader holdTailShaderOverride;
    [SerializeField] private Color rightTailTint = Color.white;
    [SerializeField] private Color leftTailTint = Color.white;

    private GameObject holdTailObject;
    private MeshRenderer holdTailRenderer;
    private MeshFilter holdTailMeshFilter;
    private Mesh gestureTailMesh;
    private GameObject gestureEndCapObject;

    /// <summary>
    /// 一般長條的身體有多寬，佔 TAP 的比例。
    /// </summary>
    /// <remarks>
    /// 0.9：比 TAP 窄一點，頭尾兩顆 TAP 才看得出是「蓋在長條上的兩個端點」，而
    /// 不是和身體黏成同一塊。滑奏另外用 <see cref="holdTailWidthFactor"/>，它的兩
    /// 端要和前後音符接得上，寬度規則不一樣。
    /// </remarks>
    private const float HoldGlassWidthShare = 0.9f;
    private SpriteRenderer gestureEndCapRenderer;
    private float gestureVisualStrength = 1f;
    private float slideFarCenterOffsetWorld;
    private float slideNearWidthWorld;
    private float slideFarWidthWorld;
    private float slideConnectorLengthWorld;
    /// <summary>到下一個節點的間隔（毫秒）。長度要每幀用**現在的**速度換算，
    /// 不能拿生成那一瞬間的速度烤死——速度一變連結就接不到下一顆。</summary>
    private float slideConnectorSpanMs;
    private float lastSlideConnectorRatio = -1f;
    /// <summary>連結段沿 Z 切幾段。</summary>
    /// <remarks>
    /// 它本來是四個頂點的梯形，平躺在軌道上時那樣就夠。拋物線模式下弧線是在
    /// vertex shader 裡照世界 Z 算的，四個角只會讓**兩端**被抬到弧線上、中間是
    /// 一條弦——連結段於是從弧線底下鑽過去，兩頭接不上它連的那兩顆音符。
    /// </remarks>
    private const int SlideConnectorSegments = 12;

    /// <summary>
    /// 横向切四欄：兩邊暗、中間亮。
    /// </summary>
    /// <remarks>
    /// 連結段改成**光束**（加法混合）之後，亮度分布全靠頂點色。
    /// 兩欄只能畫出硬邊緣的帶子，四欄才有內核和向外收束的境。
    /// </remarks>
    private const int SlideConnectorColumns = 4;
    /// <summary>內核離中心多遠（佔半寬的比例）。</summary>
    private const float SlideBeamCoreShare = 0.34f;
    private static readonly float[] SlideBeamColumnX =
        { -1f, -SlideBeamCoreShare, SlideBeamCoreShare, 1f };
    /// <summary>每一欄的亮度：外緣 0，內核 1。</summary>
    private static readonly float[] SlideBeamColumnGlow = { 0f, 1f, 1f, 0f };

    private const int SlideConnectorVertexCount =
        (SlideConnectorSegments + 1) * SlideConnectorColumns;

    private readonly Vector3[] slideConnectorVertices =
        new Vector3[SlideConnectorVertexCount];
    private static readonly Vector2[] SlideConnectorUvs = BuildSlideConnectorUvs();

    private static Vector2[] BuildSlideConnectorUvs()
    {
        var uvs = new Vector2[SlideConnectorVertexCount];
        for (int i = 0; i <= SlideConnectorSegments; i++)
        {
            float t = (float)i / SlideConnectorSegments;
            for (int c = 0; c < SlideConnectorColumns; c++)
                uvs[i * SlideConnectorColumns + c] =
                    new Vector2((SlideBeamColumnX[c] + 1f) * 0.5f, t);
        }
        return uvs;
    }
    /// <summary>
    /// 連結段自己的頂點色。**四個頂點一律相同。**
    /// </summary>
    /// <remarks>
    /// 這裡本來是 0.92 → 0.58 的近亮遠暗漸層，照的是長條的邏輯：長條往譜面深處
    /// 拉，遠端淡掉讀成「它還很長」。
    ///
    /// 滑動不是那樣。它是一串**斜著往下走的音符**，每一段連結的近端貼著前一顆
    /// 音符、遠端貼著後一顆 —— 漸層一放上去，近端就比它蓋住的那顆音符淺（看起來
    /// 是蓋在上面的一片東西），遠端就比它要接上的那顆深（看起來根本沒接上）。
    /// **同一條漸層在兩端各製造了一個不同的錯誤。**
    ///
    /// 沿著連結不做任何明暗變化，兩端才會各自消失在它連的那顆音符裡。
    /// </remarks>
    /// <summary>
    /// 逐頂點的亮度。加法混合（Blend One One）不看 alpha，所以收束要乘進 RGB。
    /// </summary>
    private readonly Color[] slideConnectorColors = new Color[SlideConnectorVertexCount];
    /// <summary>
    /// 光束比音符頭寬幾倍。
    /// </summary>
    /// <remarks>
    /// 略寬一點點，外緣的收束才有地方放；太寬就變成一片霧。
    /// </remarks>
    private const float SlideBeamWidthScale = 1.15f;

    /// <summary>光束的強度。拉到 1 以上才過得了 bloom 的閘值。</summary>
    private const float SlideBeamIntensity = 2.6f;

    /// <summary>
    /// 光束的顏色：右手深紅、左手深藍。
    /// </summary>
    /// <remarks>
    /// 不走 ResolveGestureColor（主題色是 1, 0.18, 0.18 那種偏粉的紅）。加法混合下，
    /// 沒被壓低的綠藍兩個通道一累加就往白跑，看起來是淡紅而不是深紅。
    /// 把綠藍壓到十分之一以下、主通道維持滿值，才會是一條深而飽和的光束——
    /// 而且主通道沒降，可視度和之前一樣。
    /// </remarks>
    private static readonly Color SlideBeamRightColor = new Color(1f, 0.06f, 0.10f, 1f);
    private static readonly Color SlideBeamLeftColor = new Color(0.10f, 0.20f, 1f, 1f);

    /// <summary>這一顆的光束顏色。hand == 0 是右手（見 ResolveGestureColor）。</summary>
    private Color ResolveSlideBeamColor()
    {
        bool rightHand = noteData != null && noteData.hand == 0;
        return rightHand ? SlideBeamRightColor : SlideBeamLeftColor;
    }
    private static readonly int SlideBeamEmissionId = Shader.PropertyToID("_EmissionColor");

    private static Material cachedSlideBeamMaterial;

    /// <summary>
    /// 連結段的光束材質：**加法混合**，只往上加亮。
    /// </summary>
    /// <remarks>
    /// 以前用和長按同一張 alpha 混合的材質，而連結段有九成埋在兩顆音符頭
    /// 底下（節點 26 ms 一顆、中心距 2.08，而音符頭本身就 2.00 深），剩下那一成
    /// 又是深色配黑底 —— 實測畫出來了、位置全對、alpha 0.92，使用者就是看不到。
    ///
    /// 加法混合沒有這個問題：它不取代背景、只累加，所以暗的東西蓋不掉它，
    /// 而且值拉到 1 以上就會被 bloom 採到，從音符邊緣溢出來。
    /// </remarks>
    private static Material ResolveSlideBeamMaterial()
    {
        if (cachedSlideBeamMaterial != null) return cachedSlideBeamMaterial;
        Shader shader = Shader.Find("Custom/UnlitAdditiveEmission");
        if (shader == null) return null;
        cachedSlideBeamMaterial = new Material(shader)
        {
            name = "SlideConnectorBeam",
            hideFlags = HideFlags.DontSave,
        };
        return cachedSlideBeamMaterial;
    }

    private static readonly int[] SlideConnectorTriangles = BuildSlideConnectorTriangles();

    private static int[] BuildSlideConnectorTriangles()
    {
        int quads = SlideConnectorSegments * (SlideConnectorColumns - 1);
        var tris = new int[quads * 6];
        int at = 0;
        for (int i = 0; i < SlideConnectorSegments; i++)
        {
            for (int c = 0; c < SlideConnectorColumns - 1; c++)
            {
                int a = i * SlideConnectorColumns + c;
                int b = a + SlideConnectorColumns;
                tris[at++] = a; tris[at++] = b; tris[at++] = a + 1;
                tris[at++] = b; tris[at++] = b + 1; tris[at++] = a + 1;
            }
        }
        return tris;
    }

    /// <summary>
    /// 連結相對於音符的繪製順序。**一定要是負的。**
    /// </summary>
    /// <remarks>
    /// 這裡本來和音符**同一個 sortingOrder**，於是誰蓋誰是未定義的 —— 實際跑起來
    /// 連結蓋在音符上，一串滑動就變成「紅帶子上面擺著幾塊被吃掉一半的音符」。
    ///
    /// 順序定下來之後，連結兩端**該不該伸進音符裡**這個問題也一起消失了：伸進去
    /// 就好，反正被蓋住。這樣接縫不可能露出來 —— 對齊一條看不見的邊，本來就是
    /// 做不到的事。
    /// </remarks>
    private const int GestureTailOrderOffset = -1;

    /// <summary>
    /// 滑奧的光束畫在音符頭**前面**。
    /// </summary>
    /// <remarks>
    /// 連結段中心連中心，而滑奧節點的中心距（2.08）跟音符頭的深度（2.00）
    /// 差不多 —— 畫在後面的話它幾乎整根都在兩顆頭的 quad 底下。實測：同一根
    /// 幾何、同一個顏色，排在後面看不到，排到前面就出現。
    ///
    /// 畫在前面不會遮住音符：它是**加法混合**，只能把背後的東西加亮、
    /// 永遠不會把它變暗。這也是連結段必須是光束而不是實體的理由：
    /// 實體排在前面會把金色的音符切成兩半。
    /// </remarks>
    private const int SlideBeamOrderOffset = 2;

    private int GestureTailOrder => isSlideCached ? SlideBeamOrderOffset : GestureTailOrderOffset;
    private float streamingMeshLengthWorld;
    private float trillArrowRepeatWorldLength;
    private float streamingVisibleLengthWorld;
    private float currentTailWidthWorld = 0f;
    private static Mesh sharedHoldTailQuad;
    private MaterialPropertyBlock holdTailPropertyBlock;
    private static readonly int HoldTailColorId = Shader.PropertyToID("_Color");
    private static readonly int HoldTailNearColorId = Shader.PropertyToID("_NearColor");
    private static readonly int HoldTailFarColorId = Shader.PropertyToID("_FarColor");
    private static readonly int HoldTailMainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int HoldTailFlowStrengthId = Shader.PropertyToID("_FlowStrength");
    private static readonly int HoldTailFlowColorId = Shader.PropertyToID("_FlowColor");
    private static readonly int HoldTailPreserveGoldId = Shader.PropertyToID("_PreserveGold");
    private static readonly int HoldTailEmissionId = Shader.PropertyToID("_Emission");
    private static readonly int HoldTailEdgeGlowId = Shader.PropertyToID("_EdgeGlow");
    private static readonly int HoldTailFadeStrengthId = Shader.PropertyToID("_TailFadeStrength");
    private static readonly int HoldTailWorldClipEnabledId = Shader.PropertyToID("_WorldClipEnabled");
    // 拋物線走 NoteArcScreen.ApplyTo，不走全域值——全域值要另一個元件去場景裡找
    // 判定線與 spawner，找不到或找到舊的就會整根用同一個高度。
    private static readonly int HoldTailWorldClipMinZId = Shader.PropertyToID("_WorldClipMinZ");
    private static readonly int HoldTailWorldClipMaxZId = Shader.PropertyToID("_WorldClipMaxZ");
    private static readonly int HoldGlassRodId = Shader.PropertyToID("_GlassRod");
    private static readonly int HoldStartZId = Shader.PropertyToID("_HoldStartZ");
    private static readonly int HoldEndZId = Shader.PropertyToID("_HoldEndZ");
    private static readonly int HoldBeatSpacingZId = Shader.PropertyToID("_BeatSpacingZ");
    private static readonly int HoldJudgeZId = Shader.PropertyToID("_JudgeZ");
    private static readonly int HoldTailMainTexStId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int HoldTailFlatFillId = Shader.PropertyToID("_FlatFill");
    private static readonly int HoldTailSmoothBodyId = Shader.PropertyToID("_SmoothBody");
    private static readonly int HoldTailCoreWidthId = Shader.PropertyToID("_CoreWidth");
    private static readonly int HoldTailCoreGlowId = Shader.PropertyToID("_CoreGlow");
    private static readonly int HoldTailCoreJudgedBoostId = Shader.PropertyToID("_CoreJudgedBoost");
    private float lastHoldTailFlowStrength = -1f;
    private float lastAppliedGestureVisualStrength = -1f;
    // Cached soft-note flag to avoid repeated reflection checks
    private bool isSoftCached = false;
    // Cached staccato-note flag to avoid repeated reflection checks
    private bool isStaccatoCached = false;
    private bool isSlideCached = false;
    private bool isTrillCached = false;
    private MeshRenderer velocityHaloRenderer;
    private Mesh velocityHaloMesh;
    private GameObject staccatoIndicatorInstance;
    private StaccatoIndicatorBillboard staccatoIndicatorController;
    private SpriteRenderer staccatoIndicatorRenderer;
    private static Transform staccatoIndicatorRoot;
    // Staccato heads use the ordinary glass frame with a gem set in the centre
    // (tools/make_staccato_gem.py). The gem lies flat on the note like the frame
    // does — it is a flat inlay, so there is nothing to gain from billboarding it.
    // Ratios are for the whole texture, glow included; the solid gold rim is
    // about 75% of it. Notes are wide bars (a 3-lane head is ~10u wide, 2u tall).
    // The gem follows the note's width, clamped between Min and Max times its
    // height. Tuned by eye in play: 0.55 too big, 0.127 a dot, 0.2 then +50%.
    // Constants, not [SerializeField]: the Editor keeps a loaded prefab's
    // serialized values across script reloads, so changing an initializer there
    // silently did nothing until Unity was restarted.
    private const float staccatoGemWidthRatio = 0.3f;
    private const float staccatoGemMinHeightRatio = 1.05f;
    private const float staccatoGemMaxHeightRatio = 1.8f;
    private SpriteRenderer staccatoGemRenderer;
    private static Material staccatoGemMaterial;
    private static bool staccatoGemMaterialResolved;
    private static Sprite staccatoGemRightSprite;
    private static Sprite staccatoGemLeftSprite;
    private static Sprite staccatoMarkRightSprite;
    private static Sprite staccatoMarkLeftSprite;
    // Public accessor for cached soft flag so other managers can read without reflection
    public bool IsSoft => isSoftCached;
    // Public accessor for cached staccato flag
    public bool IsStaccato => isStaccatoCached;
    public bool IsSlide => isSlideCached;
    public bool IsTrill => isTrillCached;
    public void PlayJudgmentLineFanParticles(bool sustainedHold, int inputLane = -1)
    {
        using var _probe = HitchProbe.Measure("fanParticles");
        if (noteFanParticleEmitter == null) return;
        if (inputLane >= 0)
        {
            try
            {
                var meshManager = NoteJudgementMeshManager.EnsureCreated();
                if (meshManager != null &&
                    meshManager.TryGetHitOrigin(this, out var origin, out _, out var laneWidth, inputLane))
                {
                    noteFanParticleEmitter.SetJudgmentOrigin(origin, laneWidth);
                }
            }
            catch { }
        }
        if (sustainedHold) noteFanParticleEmitter.BeginHoldEmission();
        else
        {
            // Current and compatibility judgment routes may report the same
            // hit in one frame. Preserve the fallback without double bursts.
            if (lastNoteFanBurstFrame == Time.frameCount) return;
            lastNoteFanBurstFrame = Time.frameCount;
            bool needsContinuousEmitter = noteData != null &&
                noteData.type == "hold" && !isStaccatoCached;
            noteFanParticleEmitter.EmitJudgmentBurst(!needsContinuousEmitter);
        }
    }

    public void StopJudgmentLineFanParticles()
    {
        noteFanParticleEmitter?.EndHoldEmission();
    }
    private float cachedWorldWidth = 1f;
    [Header("Fixed Note Visual Height")]
    [SerializeField, Min(0.05f)] private float fixedNoteWorldHeight = 2f;

    /// <summary>
    /// 音符貼圖一律平躺（繞 X 轉 −90°）。
    ///
    /// 曾經在拋物線模式下把它立起來，那是配合「鏡頭完全正面」的做法；現在拋物線
    /// 模式改成「鍵盤貼著弧線的切線、鏡頭垂直看鍵盤」，鏡頭仍然是俯視的，平躺的
    /// 貼圖才看得清楚。留著這個函式當單一出口，之後要再改只動這裡。
    /// </summary>
    private static Quaternion NoteSpriteRotation()
    {
        return Quaternion.Euler(-90f, 0f, 0f);
    }

    /// <summary>
    /// 讓平躺的貼圖跟著弧線傾斜，貼圖的平面才含著行進方向。
    /// </summary>
    /// <remarks>
    /// −90° 是平躺（貼圖的 +z 朝世界的 +y）。再繞 X 轉 −atan(斜率)，貼圖朝 +z
    /// 的那一邊就抬到切線上。不做這件事的話，弧線末端那一段很陡，一片水平的
    /// 貼圖會像浮在判定線上方的盤子——即使它的前緣高度是對的。
    /// </remarks>
    /// <remarks>
    /// 曾經照弧線的世界斜率把貼圖轉過去。那個斜率在畫面座標映射下不是玩家看到的
    /// 斜率——它是反解出來的副產物，遠端動輒每單位 Z 掉一兩個單位 Y，貼圖會整片
    /// 立起來變成一條線。音符在畫面上走的本來就是本家那條路徑，貼圖平躺就好。
    /// </remarks>
    private void UpdateSpriteTilt(float worldZ)
    {
        if (runtimeSpriteRenderer == null) return;
        runtimeSpriteRenderer.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
    }

    /// <summary>音符沒有弧線時該在的 Y（判定線所在的平面）。初始化時記起來。</summary>
    private float baseY;
    /// <summary>同理的 X（鍵道中心）。橫向補正是乘上去的，所以不能就地累乘。</summary>
    private float baseX;
    /// <summary>這一幀實際套上去的橫向補正。尾巴要拿它當錨點。</summary>
    private float appliedArcLateral = 1f;
    private bool hasBaseY;

    /// <summary>
    /// 這顆音符現在該被「拋」多高。本家的音符是先上拋再落到判定線上，
    /// 曲線在 NoteArc（反組譯本家得到的）。0 = 關掉，照舊直線落下。
    ///
    /// 長押、滑奏、顫音的尾巴是在 vertex shader 裡沿著同一條弧線彎的
    /// （Shaders/NoteArcCurve.hlsl），所以它們的頭也要用這個位移，兩者才對得上。
    /// </summary>
    /// <param name="speedWorld">
    /// 世界單位／秒。用來把「音符前緣離中心多遠」換算成時間：貼圖是平躺的一片，
    /// **前緣**才是要碰到判定線的那一邊（見 NoteNearEdgeLocalOffsetZ）。照中心的
    /// 時間抬高的話，弧線末端那一段很陡，半個音符長度就會變成看得出來的高度差
    /// ——音符浮在線上方，特效卻在線上。0 = 不修正。
    /// </param>
    /// <summary>
    /// 這一顆現在該被抬多高。**由畫面高度反解**（見 NoteArcScreen）：本家那條
    /// 曲線是螢幕座標，直接加在世界 Y 上會和透視重複算一次。
    /// </summary>
    /// <param name="worldZ">音符**前緣**的世界 Z——前緣才是碰到判定線的那一邊。</param>
    private float CurrentArcOffset(float worldZ)
    {
        PrepareArcScreen();
        return NoteArcScreen.Active ? NoteArcScreen.OffsetAtZ(worldZ) : 0f;
    }

    /// <summary>
    /// 把弧線套到音符本體上：高度、橫向補正、貼圖寬度、淡入。
    /// </summary>
    /// <remarks>
    /// **四件事必須一起做。** 少做淡入，音符就在生成處憑空出現；少做橫向補正，
    /// 音符整段偏向畫面中央、快到判定線才掃回自己那一鍵。
    ///
    /// 有長條的音符更嚴格：尾巴是 shader 照**同一張表**逐頂點算的，而它四件事
    /// 全都做。頭少做哪一件，尾巴就在那一件上和頭分家 —— 滑奏的頭原本只抬高度，
    /// 所以連結段照著表淡掉了、音符頭卻還是全亮，看起來就是「有些連結線不見了」。
    /// </remarks>
    private void ApplyArcToHead(ref Vector3 pos)
    {
        if (!hasBaseY) return;
        float edge = LeadingEdgeZ(pos.z);
        pos.y = baseY + CurrentArcOffset(edge);
        appliedArcLateral = CurrentArcLateral(edge);
        pos.x = baseX * appliedArcLateral;
        ApplyArcLateralWidth();
        ApplyArcFade(edge);
    }

    /// <summary>
    /// 把弧線那張表寫進長條的 property block。
    /// </summary>
    /// <remarks>
    /// 音符本體的 Y 已經是 baseY + 弧線位移，長條是它的子物件、跟著被抬過一次，
    /// 所以要把那一段當錨點扣掉，再讓 shader 逐頂點重算。
    /// </remarks>
    private void WriteArcTo(MaterialPropertyBlock block)
    {
        PrepareArcScreen();
        NoteArcScreen.ApplyTo(block,
                              hasBaseY ? transform.position.y - baseY : 0f,
                              transform.position.x,
                              hasBaseY ? baseX : transform.position.x);
    }

    /// <summary>
    /// 只更新長條的弧線參數，其餘的外觀不動。
    /// </summary>
    /// <remarks>
    /// **每一種長條、每一幀都要送。** `_Arc*` 這幾個 uniform 沒有宣告在 shader 的
    /// Properties 裡，材質給不出預設值——某一次 draw call 沒送，它就沿用常數緩衝區
    /// 裡上一次同一個 shader 留下來的值，也就是**別顆音符的**錨點與淡入範圍。
    ///
    /// 滑奏本來完全沒送過：它走的是 UpdateSlideConnectorMesh，而寫這張表的是
    /// UpdateStreamingLongVisual（只有長押和顫音會呼叫）。於是連結段的高度取決於
    /// 它前面剛好畫了誰——畫面上長押多一根少一根、順序一變，它就**跳一下**；
    /// 而淡入範圍沿用別人的，連顏色也會比自己的音符深一點。
    /// </remarks>
    private void ApplyTailArcBlock()
    {
        if (holdTailRenderer == null) return;
        if (holdTailPropertyBlock == null) holdTailPropertyBlock = new MaterialPropertyBlock();
        holdTailRenderer.GetPropertyBlock(holdTailPropertyBlock);
        // 滑奏的弧線已經烤進頂點了（UpdateSlideConnectorMesh），shader 這邊
        // 要**明確關掉**：不寫的話它會沿用上一個同 shader 的 draw call
        // 留在常數緩衝區裡的錨點與淡入範圍，等於再位移一次。
        // 滑奧的光束走自己的 shader，弧線已經烤進頂點，沒有 _Arc* 要送。
        if (isSlideCached) return;
        WriteArcTo(holdTailPropertyBlock);
        holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
    }


    /// <summary>沒有橫向補正時貼圖該有的寬度。補正是乘上去的，不能就地累乘。</summary>
    private float spriteBaseScaleX = 1f;
    private bool hasSpriteBaseScaleX;
    private float arcFadeWritten = -1f;
    private float arcFadeBaseAlpha = 1f;

    /// <summary>
    /// 生成處全透明、到頂點全不透明。
    /// </summary>
    /// <remarks>
    /// 貼圖的 alpha 還有別人會寫（判定後的淡出之類），所以記著自己上次寫進去的
    /// 值：對不上就表示別人改過，把那個值當成新的基準，不要把別人的淡出吃掉。
    /// </remarks>
    /// <summary>
    /// 貼圖的寬度也要跟著橫向補正走。
    /// </summary>
    /// <remarks>
    /// 補正的意思是「整條跑道的水平縮放」：位置乘了，寬度就得跟著乘，不然音符
    /// 在畫面上會比自己那一鍵窄（遠處差到一半），而長押尾巴和踏板（在 shader／
    /// 逐頂點裡連寬度一起乘了）看起來就比音符頭寬一截。
    /// </remarks>
    private void ApplyArcLateralWidth()
    {
        if (!hasSpriteBaseScaleX || runtimeSpriteRenderer == null) return;
        Transform sprite = runtimeSpriteRenderer.transform;
        Vector3 s = sprite.localScale;
        float want = spriteBaseScaleX * appliedArcLateral;
        if (Mathf.Abs(s.x - want) > 0.0001f)
        {
            s.x = want;
            sprite.localScale = s;
        }
    }

    private void ApplyArcFade(float worldZ)
    {
        if (runtimeSpriteRenderer == null) return;
        float fade = NoteArcScreen.Active ? NoteArcScreen.FadeAtZ(worldZ) : 1f;
        Color c = runtimeSpriteRenderer.color;
        if (arcFadeWritten < 0f || Mathf.Abs(c.a - arcFadeWritten) > 0.001f)
            arcFadeBaseAlpha = c.a;
        float a = arcFadeBaseAlpha * fade;
        if (Mathf.Abs(c.a - a) > 0.001f)
        {
            c.a = a;
            runtimeSpriteRenderer.color = c;
        }
        arcFadeWritten = a;
    }

    /// <summary>
    /// 這一顆的世界 X 要乘多少。垂直修正把音符推遠、在畫面上往中間縮，不乘回去
    /// 的話音符整段都偏向畫面中央，快到判定線才掃回自己的鍵上。
    /// </summary>
    private float CurrentArcLateral(float worldZ)
    {
        PrepareArcScreen();
        return NoteArcScreen.Active ? NoteArcScreen.LateralAtZ(worldZ) : 1f;
    }

    /// <summary>音符前緣的世界 Z（貼圖是平躺的一片，往後推了半個音符高）。</summary>
    private float LeadingEdgeZ(float centreZ)
    {
        return centreZ - ResolveNoteWorldHeight() * 0.5f;
    }

    /// <summary>每幀準備一次那張「世界 Z → 該抬多高」的表。</summary>
    private void PrepareArcScreen()
    {
        SettingsManager settings = SettingsManager.Instance;
        float share = settings != null ? settings.EffectiveNoteArcHeight : 0f;
        if (share <= 0f)
        {
            NoteArcScreen.Disable();
            return;
        }
        // 長度由設定決定，不是由自己的 spawner——三邊要查同一張表，而且條件也要
        // 一樣，否則會有人在某些幀是平的。
        float travelZ = settings.ArcTravelWorldUnits();
        float lineViewY = settings != null ? settings.JudgmentLineScreenHeight : 0.28f;
        float spawnZ = settings != null ? settings.ArcSpawnWorldUnits() : travelZ;
        NoteArcScreen.Prepare(Camera.main, judgmentZ, travelZ,
                              hasBaseY ? baseY : transform.position.y, lineViewY, share,
                              spawnZ);
    }

    private float ResolveNoteWorldHeight()
    {
        SettingsManager settings = SettingsManager.Instance;
        return settings != null
            ? settings.NoteVisualHeight
            : Mathf.Max(0.05f, fixedNoteWorldHeight);
    }

    /// <summary>
    /// Local-Z distance from a flat note sprite's centre to its near (player-facing)
    /// edge. Every flat sprite that marks a chart timestamp is pushed this far AWAY
    /// from the judgment line, so at its timed moment the visible leading edge — not
    /// the sprite centre — meets the line. Centring instead makes the note read as
    /// arriving early by (noteHeight / 2) / scrollSpeed. Note that this is SMALL at
    /// real play speeds: 2u at the shipped default of 190u/s is about 5ms, not the
    /// 33ms that the 30u/s code fallback would suggest. Correct, but never expect it
    /// to account for a large judgment bias on its own.
    /// Staccato heads lie flat like every other head now, so they use it too.
    /// </summary>
    private float NoteNearEdgeLocalOffsetZ()
    {
        float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        return ResolveNoteWorldHeight() * 0.5f / scaleZ;
    }
    [SerializeField]
    private float releaseZThreshold = 0.00f; // world units: how close to judgmentZ before actually releasing
    [SerializeField, Range(0.05f, 1f), Tooltip("Visual speed after an unjudged note crosses the judgment line. Timing and miss calculation remain on the original song clock.")]
    private float postJudgmentVisualSpeed = 0.28f;
    [SerializeField]
    private float holdTailYOffset = 0.0f; // small Y offset to avoid Z-fighting with the track
    [SerializeField]
    private float holdTailShrinkSpeed = 1f; // units per second to smooth the tail shortening (world units/sec)
    [SerializeField]
    private float holdTailWidthFactor = 0.8f; // fraction of note width used for tail thickness (0.8 = 80% of note width)
    [SerializeField]
    private float tailEndOffsetMs = 40f; // tail will end at (endTime - tailEndOffsetMs) milliseconds
    [SerializeField]
    private float minimumHoldTailVisualLength = 0.02f; // ensure very short holds still show a visible tail pre-judgment
    [Header("Streaming Long Note Visual")]
    [SerializeField, Min(2f)] private float streamingTailMaximumVisibleLength = 55f;
    [SerializeField, Min(0.25f)] private float streamingTailRepeatWorldLength = 5f;
    private float currentTailLength = 0f;
    private float initialTailLength = 0f;
    // Cache last applied tail length to avoid redundant hold-tail mesh updates
    private float lastAppliedTailLength = -1f;
    [SerializeField]
    private float tailLengthUpdateThreshold = 0.001f; // world units: minimum change to update endpoint
    private float holdDurationMs = 0f;
    private float holdTailAdjustedEndMs = 0f;
    // Once a Hold/Trill head is judged, its visual consumption point freezes
    // at that exact timing position. Judgment scheduling still uses the chart's
    // immutable start/end times and never derives from this visual state.
    private bool streamingJudgmentAnchorActive;
    private float streamingJudgmentAnchorZ;
    private float streamingAnchorClipMinZ;
    private float streamingAnchorWindowTopZ;
    private float streamingAnchorTailEndZ;
    private float streamingAnchorConsumeStartMs;
    private float streamingAnchorLastSongPosMs;
    private float streamingAnchorTravelWorld;
    private float streamingAnchorVisualEndMs;
    private float streamingAnchorRequiredTailTravelWorld;
    private float streamingAnchorTailTravelWorld;
    private bool isJudged = false; // flag set when judged by JudgmentManager
    // Soft notes: judged immediately, but disappear only when reaching judgment line.
    private bool softJudgedPendingRelease = false;
    private bool headPressed = false; // set when player successfully pressed the head of a hold
    private bool headMissed = false;  // set when head wasn't pressed within allowed window -> make the hold unjudgeable
    private bool judgmentSuppressed = false; // overlapping-later-note protection: keep visual flow, but never judge this note
    
    // Hit sound tracking
    private bool hasTriggeredHitSound = false;
    private static AudioClip cachedHitSound;
    private static Material cachedRightHoldTailMaterial;
    private static Material cachedLeftHoldTailMaterial;
    private static Material cachedRightGlassThemeMaterial;
    private static Material cachedLeftGlassThemeMaterial;
    private static Material cachedSoftGlowMaterial;
    private static Material cachedDefaultSpriteMaterial;
    private NoteFanParticleEmitter noteFanParticleEmitter;
    private int lastNoteFanBurstFrame = -1;
    private static readonly System.Collections.Generic.Dictionary<Texture2D, Sprite> spriteCache = new System.Collections.Generic.Dictionary<Texture2D, Sprite>();
    private NotePool owningPool;

    public Transform TrackTransform => noteSpawner != null ? noteSpawner.trackTransform : null;

    private void Awake()
    {
        if (holdTailPropertyBlock == null)
        {
            holdTailPropertyBlock = new MaterialPropertyBlock();
        }
    }

    [Header("Positioning")]
    [Tooltip("If true, notes will initialize their Y position to match the JudgmentLine's Y. Disable to keep notes at a fixed track Y instead.")]
    [SerializeField]
    private bool alignToJudgmentLineY = true;
    [Tooltip("Vertical offset applied to notes when aligning to the JudgmentLine (world units). Note Y = JudgmentLine.Y + this offset.")]
    [SerializeField]
    private float judgmentYOffset = 0.1f;

    // Helper to detect if NoteData represents a "soft" note (note_type == 1).
    // Uses reflection to be tolerant of different NoteData definitions (string or int fields).
    private static bool IsSoftNote(object nd)
    {
        if (nd == null) return false;
        try
        {
            var t = nd.GetType();
            // common case: a string-typed 'type' property like "tap"/"hold"/"soft" or numeric string
            var propType = t.GetProperty("type");
            if (propType != null)
            {
                var v = propType.GetValue(nd);
                if (v != null)
                {
                    var s = v.ToString();
                    if (string.Equals(s, "soft", StringComparison.OrdinalIgnoreCase)) return true;
                    if (s == "1") return true;
                    if (int.TryParse(s, out int vi) && vi == 1) return true;
                }
            }

            // try common numeric property/field names
            string[] names = new string[] { "note_type", "noteType", "typeId", "typeIndex", "noteTypeId" };
            foreach (var nm in names)
            {
                var p = t.GetProperty(nm);
                if (p != null)
                {
                    var vv = p.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi2) && vi2 == 1) return true;
                }
                var f = t.GetField(nm);
                if (f != null)
                {
                    var vv = f.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi3) && vi3 == 1) return true;
                }
            }
        }
        catch { }
        return false;
    }

    // Helper to detect if NoteData represents a "staccato" note (note_type == 3) or string type "staccato".
    private static bool IsStaccatoNote(object nd)
    {
        if (nd == null) return false;
        try
        {
            var t = nd.GetType();
            var propType = t.GetProperty("type");
            if (propType != null)
            {
                var v = propType.GetValue(nd);
                if (v != null)
                {
                    var s = v.ToString();
                    if (string.Equals(s, "staccato", StringComparison.OrdinalIgnoreCase)) return true;
                    if (s == "3") return true;
                    if (int.TryParse(s, out int vi) && vi == 3) return true;
                }
            }

            string[] names = new string[] { "note_type", "noteType", "typeId", "typeIndex", "noteTypeId" };
            foreach (var nm in names)
            {
                var p = t.GetProperty(nm);
                if (p != null)
                {
                    var vv = p.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi2) && vi2 == 3) return true;
                }
                var f = t.GetField(nm);
                if (f != null)
                {
                    var vv = f.GetValue(nd);
                    if (vv != null && int.TryParse(vv.ToString(), out int vi3) && vi3 == 3) return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>How thick the shell is, as a share of the note's own height.</summary>
    /// <remarks>
    /// Tied to the note rather than to the lane so the shell keeps the same
    /// proportion whatever the note's size: a band measured in lane widths is a
    /// thin rim on a three-lane note and a slab around a one-lane one.
    /// </remarks>
    /// <summary>斷音紋章相對基準寬度再加寬多少。高度不跟著加，所以它會稍微變扁。</summary>
    private const float StaccatoMarkWiden = 1.5f;

    private const float StrongBandShare = 0.55f;
    private const float WeakBandShare = 0.30f;

    /// <summary>
    /// Wraps the note in a shell showing how hard it is meant to be played.
    /// </summary>
    /// <remarks>
    /// Only in recital mode, and never on trills or slides -- those are already
    /// several notes' worth of marks in one lane, and a shell on each would read
    /// as one large smear rather than as a dynamic.
    /// </remarks>
    private void UpdateVelocityHalo()
    {
        bool wanted = false;
        try
        {
            wanted = SettingsManager.Instance != null && SettingsManager.Instance.RecitalModeInPlay
                     && !isTrillCached && !isSlideCached && noteData != null;
        }
        catch { wanted = false; }

        // 強弱的界線是這首曲子自己的（VelocityBands），不是固定值。沒有可用
        // 力度的譜面 Measured 會是 false，整張就一顆殼都不畫。
        int velocity = wanted ? noteData.velocity : 0;
        bool strong = VelocityBands.IsStrong(velocity);
        bool weak = VelocityBands.IsWeak(velocity);
        if (!wanted || (!strong && !weak))
        {
            if (velocityHaloRenderer != null) velocityHaloRenderer.enabled = false;
            return;
        }

        Renderer plate = runtimeSpriteRenderer != null && runtimeSpriteRenderer.enabled
            ? (Renderer)runtimeSpriteRenderer
            : noteRenderer;
        if (plate == null) return;

        Vector3 localSize;
        Vector3 localCentre;
        if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null
            && ReferenceEquals(plate, runtimeSpriteRenderer))
        {
            localSize = runtimeSpriteRenderer.sprite.bounds.size;
            localCentre = runtimeSpriteRenderer.sprite.bounds.center;
        }
        else
        {
            var filter = plate.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            localSize = mesh.bounds.size;
            localCentre = mesh.bounds.center;
        }

        // 殼直接掛在音符的圖底下，尺寸也用圖自己的 local 尺寸 —— 完全不碰縮放。
        //
        // 先前是用世界單位算尺寸，再把 localScale 設成 1/parentLossy 去抵銷父物件
        // 的縮放。父物件同時帶著旋轉**和**非等比縮放的時候，那個補償會在旋轉之後
        // 才作用，形狀就被剪成梯形 —— 螢幕上看到的就是那個。掛在圖底下之後殼和
        // 音符共用同一個變換，兩者不可能對不齊。
        Transform plateTransform = plate.transform;
        float localWidth = Mathf.Abs(localSize.x);
        float localHeight = Mathf.Abs(localSize.y);
        if (localWidth <= 0.0001f || localHeight <= 0.0001f) return;
        float band = localHeight * (strong ? StrongBandShare : WeakBandShare);

        if (velocityHaloRenderer == null)
        {
            var halo = new GameObject("VelocityHalo", typeof(MeshFilter), typeof(MeshRenderer));
            halo.layer = gameObject.layer;
            velocityHaloMesh = new Mesh { name = "VelocityHalo", hideFlags = HideFlags.HideAndDontSave };
            velocityHaloMesh.MarkDynamic();
            halo.GetComponent<MeshFilter>().sharedMesh = velocityHaloMesh;
            velocityHaloRenderer = halo.GetComponent<MeshRenderer>();
            velocityHaloRenderer.sharedMaterial = VelocityHalo.Material;
            velocityHaloRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            velocityHaloRenderer.receiveShadows = false;
        }

        VelocityHalo.Build(velocityHaloMesh, localWidth, localHeight, band,
            strong ? VelocityHalo.Strong : VelocityHalo.Weak);

        velocityHaloRenderer.enabled = true;
        velocityHaloRenderer.sortingLayerID = plate.sortingLayerID;
        // 音符後面，不是前面：殼是襯在後面的光，蓋在音符上就變成一層霧。
        velocityHaloRenderer.sortingOrder = plate.sortingOrder - 1;

        Transform haloTransform = velocityHaloRenderer.transform;
        if (haloTransform.parent != plateTransform)
            haloTransform.SetParent(plateTransform, false);
        haloTransform.localPosition = localCentre;
        haloTransform.localRotation = Quaternion.identity;
        haloTransform.localScale = Vector3.one;

        if (!VelocityBands.ShellReported)
        {
            VelocityBands.ShellReported = true;
            Debug.Log($"[VelocityHalo] first shell strong={strong} v={velocity} " +
                $"local={localWidth:F3}x{localHeight:F3} band={band:F3} " +
                $"order={velocityHaloRenderer.sortingOrder} " +
                $"material={(velocityHaloRenderer.sharedMaterial != null ? "ok" : "MISSING")}");
        }
    }

    private static bool IsGestureType(NoteData data, string typeName, int numericType)
    {
        if (data == null) return false;
        return string.Equals(data.type, typeName, StringComparison.OrdinalIgnoreCase) ||
               data.note_type == numericType;
    }

    private Color ResolveGestureColor()
    {
        if (isSlideCached) return noteData != null && noteData.hand == 0 ? rightThemeColor : leftThemeColor;
        if (isTrillCached) return noteData != null && noteData.hand == 0 ? rightThemeColor : leftThemeColor;
        if (isSoftCached) return softThemeColor;
        return noteData != null && noteData.hand == 0 ? rightThemeColor : leftThemeColor;
    }

    public void SetOwningPool(NotePool pool)
    {
        this.owningPool = pool;
    }

    // Expose note data and active flag for JudgmentManager
    public NoteData NoteData => noteData;
    public bool IsActive => isInitialized && activeInHierarchyCached && !isJudged;

    // gameObject.activeInHierarchy 是原生呼叫，而判定每一下按鍵都會對附近每顆音符問好幾次
    // IsActive。OnEnable／OnDisable 正好在物件於階層中啟用／停用時被叫到（父物件關掉也會），
    // 這個元件本身從不單獨停用，所以兩者等價。
    private bool activeInHierarchyCached;
    private void OnEnable()
    {
        activeInHierarchyCached = true;
        if (liveIndex < 0)
        {
            liveIndex = live.Count;
            live.Add(this);
        }
    }
    private void OnDisable()
    {
        activeInHierarchyCached = false;
        visibleTailClipValid = false;
        if (liveIndex >= 0)
        {
            // 換到最後一格再刪，O(1)。
            int last = live.Count - 1;
            NoteController moved = live[last];
            live[liveIndex] = moved;
            moved.liveIndex = liveIndex;
            live.RemoveAt(last);
            liveIndex = -1;
        }
    }

    // ── 畫面上實際看得到的範圍 ───────────────────────────────────────────────
    //
    // 給要「讓開音符」的特效用（踏板的框）。從譜面時間去推算音符在哪裡是不準的：
    // 長押頭判中後會凍在線上、身體用裁切窗一段一段吃掉、還有平滑偏移和可見跑道上
    // 限。直接問畫出來的東西，才和玩家眼睛看到的一致。

    private static readonly System.Collections.Generic.List<NoteController> live =
        new System.Collections.Generic.List<NoteController>(256);
    private int liveIndex = -1;

    /// <summary>目前在場上（物件啟用中）的音符。</summary>
    public static System.Collections.Generic.IReadOnlyList<NoteController> Live => live;

    private bool visibleTailClipValid;
    private float visibleTailClipMinZ;
    private float visibleTailClipMaxZ;

    /// <summary>音符頭（含力度光暈，如果有畫）現在畫在世界座標的哪裡。</summary>
    /// <param name="withHalo">true 的話把力度光暈也算進去。</param>
    public bool TryGetVisibleHead(bool withHalo, out Bounds bounds)
    {
        bounds = default;
        Renderer head = runtimeSpriteRenderer != null && runtimeSpriteRenderer.enabled
            && runtimeSpriteRenderer.gameObject.activeInHierarchy
            ? runtimeSpriteRenderer
            : (noteRenderer != null && noteRenderer.enabled && noteRenderer.gameObject.activeInHierarchy
                ? noteRenderer : null);
        if (head == null) return false;
        bounds = head.bounds;
        if (withHalo && velocityHaloRenderer != null && velocityHaloRenderer.enabled
            && velocityHaloRenderer.gameObject.activeInHierarchy)
            bounds.Encapsulate(velocityHaloRenderer.bounds);
        // **還原**橫向補正，理由和 TryGetVisibleCorridor 一樣：這個框是給踏板
        // 圍著音符畫包圈、挖缺口用的，而踏板是把整片網格逐頂點乘上同一個補正
        // 才畫出來的。交出已經乘過的座標，那邊會再乘一次 —— 補正在遠處可以小
        // 到 0.6，於是包圈畫在音符和軌道中心的中間，一路差到一個音符寬以上。
        // 走廊那邊修過了，音符頭這邊漏掉，所以框「還是」沒對準。
        float unscale = 1f / Mathf.Max(0.0001f, appliedArcLateral);
        if (Mathf.Abs(unscale - 1f) > 0.0001f)
        {
            Vector3 min = bounds.min, max = bounds.max;
            min.x *= unscale;
            max.x *= unscale;
            bounds.SetMinMax(min, max);
        }
        return true;
    }

    /// <summary>長條的身體（走廊）現在實際看得到的那一段。</summary>
    public bool TryGetVisibleCorridor(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = minZ = maxZ = 0f;
        if (holdTailRenderer == null || !holdTailRenderer.enabled || holdTailObject == null
            || !holdTailObject.activeInHierarchy) return false;
        Bounds b = holdTailRenderer.bounds;
        // **還原**橫向補正。這個框是給踏板挖空隙用的，而踏板是把整片網格逐頂點
        // 乘上同一個補正才畫出來的——這裡若交出已經乘過的座標，那邊會再乘一次，
        // 空隙就和音符對不上了。
        float unscale = 1f / Mathf.Max(0.0001f, appliedArcLateral);
        minX = b.min.x * unscale;
        maxX = b.max.x * unscale;
        minZ = b.min.z;
        maxZ = b.max.z;
        // 串流長條的網格比看得到的長，超出的部分由 shader 依世界 z 裁掉。
        if (visibleTailClipValid)
        {
            minZ = Mathf.Max(minZ, visibleTailClipMinZ);
            maxZ = Mathf.Min(maxZ, visibleTailClipMaxZ);
        }
        return maxZ > minZ;
    }
    // Whether this note can be judged by player input. For holds, becomes false if head was missed.
    public bool IsJudgeable => IsActive && !headMissed && !judgmentSuppressed;
    public bool IsJudged
    {
        get => isJudged;
        set => isJudged = value;
    }
    public bool IsJudgmentSuppressed => judgmentSuppressed;
    /// <summary>新手教學的示範段音符：走自動演奏、不計分。</summary>
    public bool IsTutorialDemo => noteData != null && noteData.tutorialDemo;

    // Clear headMissed flag when player recovers a hold after release (for non-drop behavior)
    public void ClearHeadMissed()
    {
        headMissed = false;
    }

    public void SuppressJudgment()
    {
        judgmentSuppressed = true;
        headMissed = false;
        headPressed = false;
        softJudgedPendingRelease = false;
    }

    public float GetCurrentWorldWidth()
    {
        try
        {
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.enabled)
            {
                float width = runtimeSpriteRenderer.bounds.size.x;
                if (width > 0f)
                {
                    cachedWorldWidth = width;
                    return width;
                }
            }
        }
        catch { }

        try
        {
            if (noteRenderer != null && noteRenderer.enabled)
            {
                float width = noteRenderer.bounds.size.x;
                if (width > 0f)
                {
                    cachedWorldWidth = width;
                    return width;
                }
            }
        }
        catch { }

        return Mathf.Max(0.0001f, cachedWorldWidth);
    }

    public float GetCurrentWorldHeight()
    {
        try
        {
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.enabled)
            {
                Bounds bounds = runtimeSpriteRenderer.bounds;
                float height = Mathf.Max(bounds.size.y, bounds.size.z);
                if (height > 0.0001f) return height;
            }
        }
        catch { }
        return ResolveNoteWorldHeight();
    }

    public float GetCurrentHoldVisualLength()
    {
        if (noteData == null || noteData.type != "hold" || isStaccatoCached) return 0f;
        return Mathf.Max(0f, currentTailLength);
    }

    // Precreate a disabled holdTail to avoid runtime allocations at spawn time
    public void PrecreateHoldTail()
    {
        if (holdTailObject != null) return;
        try
        {
            EnsureHoldTailVisualExists();
            if (holdTailObject != null)
            {
                // Keep the prepared tail owned by its pooled note. Parenting it
                // beside the note leaked orphan HoldTail objects whenever a song
                // pool was destroyed and rebuilt.
                holdTailObject.transform.SetParent(transform, false);
                holdTailObject.SetActive(false);
            }
        }
        catch
        {
            DisposeHoldTailImmediate();
        }
    }

    private void EnsureHoldTailVisualExists()
    {
        if (holdTailObject != null && holdTailRenderer != null && holdTailMeshFilter != null)
        {
            return;
        }

        if (sharedHoldTailQuad == null)
        {
            sharedHoldTailQuad = new Mesh
            {
                name = "HoldTailQuad"
            };
            // 沿 Z 切段：拋物線是在 vertex shader 裡做的，只有四個角的話兩端
            // 被抬起來、中間是一條直線（弦不是弧），長的長押會穿過弧線。
            // 這是所有長押共用的一份網格，段數的成本只付一次。
            const int tailSegments = 48;
            var tailVerts = new Vector3[(tailSegments + 1) * 2];
            var tailUvs = new Vector2[tailVerts.Length];
            var tailColors = new Color[tailVerts.Length];
            var tailTris = new int[tailSegments * 6];
            for (int i = 0; i <= tailSegments; i++)
            {
                float t = (float)i / tailSegments;
                tailVerts[i * 2] = new Vector3(-0.5f, 0f, t);
                tailVerts[i * 2 + 1] = new Vector3(0.5f, 0f, t);
                tailUvs[i * 2] = new Vector2(0f, t);
                tailUvs[i * 2 + 1] = new Vector2(1f, t);
                tailColors[i * 2] = new Color(1f, 1f, 1f, 1f);
                tailColors[i * 2 + 1] = new Color(1f, 1f, 1f, 1f);
                if (i > 0)
                {
                    int b0 = (i - 1) * 2;
                    int tri = (i - 1) * 6;
                    tailTris[tri] = b0;
                    tailTris[tri + 1] = b0 + 2;
                    tailTris[tri + 2] = b0 + 1;
                    tailTris[tri + 3] = b0 + 2;
                    tailTris[tri + 4] = b0 + 3;
                    tailTris[tri + 5] = b0 + 1;
                }
            }
            sharedHoldTailQuad.vertices = tailVerts;
            sharedHoldTailQuad.uv = tailUvs;
            sharedHoldTailQuad.colors = tailColors;
            sharedHoldTailQuad.triangles = tailTris;
            sharedHoldTailQuad.RecalculateBounds();
        }

        if (holdTailObject == null)
        {
            holdTailObject = new GameObject("HoldTail");
            holdTailObject.hideFlags = HideFlags.DontSave;
        }

        holdTailMeshFilter = holdTailObject.GetComponent<MeshFilter>();
        if (holdTailMeshFilter == null)
        {
            holdTailMeshFilter = holdTailObject.AddComponent<MeshFilter>();
        }
        holdTailMeshFilter.sharedMesh = sharedHoldTailQuad;

        holdTailRenderer = holdTailObject.GetComponent<MeshRenderer>();
        if (holdTailRenderer == null)
        {
            holdTailRenderer = holdTailObject.AddComponent<MeshRenderer>();
        }

        holdTailRenderer.shadowCastingMode = ShadowCastingMode.Off;
        holdTailRenderer.receiveShadows = false;
        holdTailRenderer.lightProbeUsage = LightProbeUsage.Off;
        holdTailRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        holdTailRenderer.allowOcclusionWhenDynamic = false;

        holdTailObject.transform.localScale = Vector3.one;
        holdTailObject.transform.localRotation = Quaternion.identity;
        holdTailObject.transform.localPosition = Vector3.zero;
        holdTailObject.SetActive(false);
    }

    private Mesh GetOrCreateGestureTailMesh(string meshName)
    {
        if (gestureTailMesh == null)
        {
            gestureTailMesh = new Mesh { name = meshName };
            gestureTailMesh.MarkDynamic();
        }
        else
        {
            gestureTailMesh.name = meshName;
            gestureTailMesh.Clear();
        }
        if (holdTailMeshFilter != null) holdTailMeshFilter.sharedMesh = gestureTailMesh;
        return gestureTailMesh;
    }

    /// <summary>
    /// 譜面檢視器在選曲畫面用的譜。滑奏要靠它才連得起來。
    /// </summary>
    /// <remarks>
    /// 選曲時 GameManager.CurrentChart 還是 null（那首歌根本還沒載入），滑奏的下
    /// 一個節點就找不到，連結整段不見。與其為了預覽去動 CurrentChart（那會連帶
    /// 改 Conductor 的 BPM、餵踏板給取樣器），不如開一個明講用途的覆寫。
    /// </remarks>
    public static Chart PreviewChartOverride;

    /// <summary>
    /// 正在替譜面檢視器生音符。生出來的東西只是要看的，不該動到全域資源。
    /// </summary>
    /// <remarks>
    /// 斷奏的飄浮標示會掛進一顆**全域**容器、扇形粒子會去借共用粒子池、力度光暈會
    /// 讀演奏會模式的狀態 —— 一次生一千多顆的時候，這三樣都是在遊戲的資源上留下痕
    /// 跡，關掉檢視器之後就變成畫面上的殘留。檢視器不需要它們（斷奏在俯視圖上看寶
    /// 石就夠了），所以整批跳過。
    /// </remarks>
    public static bool PreviewBuildMode;

    /// <summary>這一顆是檢視器生的靜態預覽音符，不是遊戲裡的。</summary>
    private bool isStaticPreview;

    private NoteData FindNextSlideNode()
    {
        if (noteData == null || noteData.param2 < 0) return null;
        try
        {
            // **只有檢視器自己生的音符才可以讀覆寫。**
            //
            // PreviewChartOverride 是静態的，而静態跨場景活著：只要檢視器那個
            // 協程被中途打斷、或者 Clear() 沒跑到（場景換掉、中間丟例外），
            // 它就會帶著選曲畫面那份譜一路進到遊戲裡。接下來每一顆滑奏都去
            // **別份譜**裡找下一個節點 —— index 只是整數，所以往往真的會抄到
            // 一顆，只是完全不相干：間隔算出來是負的（長度掍到下限 0.02）、鍵道
            // 差却是隨便一個值，連結段就變成一條**極矮極寬的橫向薄片**，
            // 看起來就像連結消失了、需底多了幾條質感不明的細線。
            //
            // 改成看這一顆自己是不是檢視器生的，静態有沒有被清掉就不重要了。
            var notes = isStaticPreview && PreviewChartOverride != null
                ? PreviewChartOverride.notes
                : (GameManager.Instance != null && GameManager.Instance.CurrentChart != null
                    ? GameManager.Instance.CurrentChart.notes
                    : null);
            if (notes == null) return null;
            for (int i = 0; i < notes.Count; i++)
            {
                var candidate = notes[i];
                if (candidate == null || candidate.index != noteData.param2) continue;
                if (IsGestureType(candidate, "slide", 4)) return candidate;
            }
        }
        catch { }
        return null;
    }

    private bool BuildSlideConnectorMesh(float trackWidth, float currentWidthWorld,
        float lengthWorld, float speedWorldUnits)
    {
        NoteData next = FindNextSlideNode();
        if (next == null || holdTailMeshFilter == null) return false;

        float currentCenter = PianoVisualLayout.ResolveCenterX(noteData, trackWidth);
        float nextCenter = PianoVisualLayout.ResolveCenterX(next, trackWidth);
        slideFarCenterOffsetWorld = nextCenter - currentCenter;
        slideNearWidthWorld = Mathf.Max(0.01f, currentWidthWorld * holdTailWidthFactor);
        slideFarWidthWorld = Mathf.Max(0.01f,
            PianoVisualLayout.ResolveVisualWidth(next, trackWidth) * holdTailWidthFactor);
        // 長度要量到**下一個節點**，不是這一顆自己的長度。
        //
        // lengthWorld 是從 holdDurationMs 換算的，也就是這一顆音符持續多久。滑動
        // 的節點多半是瞬時的，那個值比到下一個節點的間隔短得多 —— 連結因此在半路
        // 就結束，看起來就是接不上去。
        float spanMs = next.startTime - noteData.startTime;
        float spanWorld = spanMs > 0f && speedWorldUnits > 0f
            ? (spanMs / 1000f) * speedWorldUnits
            : lengthWorld;
        slideConnectorLengthWorld = Mathf.Max(minimumHoldTailVisualLength, spanWorld);
        slideConnectorSpanMs = spanMs > 0f ? spanMs : 0f;
        lastSlideConnectorRatio = -1f;
        UpdateSlideConnectorMesh(1f);
        return true;
    }


    /// <summary>
    /// 連結段的網格。**弧線在這裡用 C# 算好、烤進頂點**，不走 shader。
    /// </summary>
    /// <remarks>
    /// 其他長條是把 NoteArcScreen 那張表送進 shader、由 vertex shader 位移的。
    /// 連結段不行：它走的是完全獨立的一條更新路徑，`_Arc*` 那幾個 uniform 又沒有
    /// 宣告在 Properties 裡（材質給不出預設值），於是「有沒有送到」「錨點對不對」
    /// 都變成要靠呼叫順序保證的事——中間漏一環，整段就飛到別的高度或被淡成透明。
    ///
    /// 這裡改成查同一組函式（OffsetAtZ / LateralAtZ / FadeAtZ），在**世界座標**算出
    /// 每一列該在哪，最後用這個物件本人的 worldToLocal 換回去。弧線關掉時位移是 0、
    /// 倍率是 1、淡入是 1，退化成原本平躺的梯形。
    ///
    /// 代價是每幀重建一次網格（26 個頂點），所以原本那個「比例沒變就不重建」的快取
    /// 拿掉了——音符每幀都在動，弧線的差值每幀都不一樣。
    /// </remarks>
    private void UpdateSlideConnectorMesh(float remainingRatio)
    {
        if (gestureTailMesh == null || !isSlideCached) return;
        if (holdTailObject == null) return;
        remainingRatio = Mathf.Clamp01(remainingRatio);
        lastSlideConnectorRatio = remainingRatio;

        // 先在**世界座標**把每一列該在哪算出來，最後才用這個物件本人的 worldToLocal
        // 換回去。上一版是自己拿 lossyScale 去除、並且假設音符根的 X 已經乘過橫向
        // 補正——音符的 prefab 縮放是 (1, 0.1, 0.2)，而那個假設又只在 ApplyArcToHead
        // 確實跑過的那一幀才成立。用矩陣就沒有任何假設：算出來的世界位置是什麼，
        // 畫出來就是什麼。
        Vector3 root = transform.position;
        float plane = hasBaseY ? baseY : root.y;
        float unscaledRootX = hasBaseY ? baseX : root.x;
        // 長度用現在的捲動速度換算，不用生成時烤的那一個。
        float liveSpeed = noteSpawner != null ? noteSpawner.speed : 0f;
        float spanWorldNow = slideConnectorSpanMs > 0f && liveSpeed > 0.0001f
            ? (slideConnectorSpanMs / 1000f) * liveSpeed
            : slideConnectorLengthWorld;
        spanWorldNow = Mathf.Max(minimumHoldTailVisualLength, spanWorldNow);
        // 連結段比音符平面高一點點：照它自己實際被擺到哪裡量，不再推算。
        float liftWorld = holdTailObject.transform.position.y - root.y;
        Matrix4x4 worldToLocal = holdTailObject.transform.worldToLocalMatrix;
        bool arc = NoteArcScreen.Active;

        float nearHalfWorld = slideNearWidthWorld * 0.5f;
        float farHalfWorld = Mathf.Lerp(slideNearWidthWorld, slideFarWidthWorld, remainingRatio) * 0.5f;
        float farCentreWorld = slideFarCenterOffsetWorld * remainingRatio;
        float lengthWorld = spanWorldNow * remainingRatio;

        // **跟長條一模一樣：每一列用自己的世界 Z 查同一張表。**
        //
        // 長押、滑奏、顫音的尾巴都是在 vertex shader 裡用 worldPos.z 查 NoteArcScreen
        // 那張表彎的（NoteArcCurve.hlsl）。連結段只是改在 C# 算、烤進頂點（理由見上面），
        // 但查的必須是同一個函式、同一個參數，否則它和別的長條就走在兩條不同的弧線上。
        //
        // 曾經改成「兩端錨在兩顆頭、中間拉直線」——那是弦不是弧，與長條不一致。

        // 兩端必須**埋進兩顆音符頭裡**，不能往內縮。
        //
        // 量過：這個曲庫的滑奏節點約 26 ms 一顆，速度 80 時中心距 2.08 個世界
        // 單位，而音符頭本身就有 2.00 深 —— 兩顆頭之間只差 0.08。往內縮的話
        // 連結段就變成一小截浮在中間、兩端都接不到（實測：「沒接好」）。
        // 中心到中心才能讓兩端各自消失在它連的那顆音符裡。
        Color beam = ResolveSlideBeamColor();
        for (int i = 0; i <= SlideConnectorSegments; i++)
        {
            float t = (float)i / SlideConnectorSegments;
            float worldZ = root.z + lengthWorld * t;
            float centre = unscaledRootX + farCentreWorld * t;
            float half = Mathf.Lerp(nearHalfWorld, farHalfWorld, t) * SlideBeamWidthScale;
            float lateral = arc ? NoteArcScreen.LateralAtZ(worldZ) : 1f;
            float worldY = plane + (arc ? NoteArcScreen.OffsetAtZ(worldZ) : 0f) + liftWorld;
            float fade = arc ? NoteArcScreen.FadeAtZ(worldZ) : 1f;

            for (int col = 0; col < SlideConnectorColumns; col++)
            {
                int at = i * SlideConnectorColumns + col;
                float x = centre + half * SlideBeamColumnX[col];
                slideConnectorVertices[at] = worldToLocal.MultiplyPoint3x4(
                    new Vector3(x * lateral, worldY, worldZ));
                // 加法混合不看 alpha，亮度全部乘進 RGB。
                float glow = SlideBeamColumnGlow[col] * fade;
                slideConnectorColors[at] = new Color(beam.r * glow, beam.g * glow, beam.b * glow, 1f);
            }
        }

        // uv 與三角形是定的，只有網格剛被清掉時才需要重建。
        bool rebuildTopology = gestureTailMesh.vertexCount != slideConnectorVertices.Length;
        gestureTailMesh.vertices = slideConnectorVertices;
        if (rebuildTopology)
        {
            gestureTailMesh.uv = SlideConnectorUvs;
            gestureTailMesh.triangles = SlideConnectorTriangles;
        }
        gestureTailMesh.colors = slideConnectorColors;
        gestureTailMesh.RecalculateBounds();
    }

    private void BuildTrillArrowMesh(float widthWorld, float lengthWorld)
    {
        if (holdTailMeshFilter == null) return;
        Mesh mesh = GetOrCreateGestureTailMesh("TrillAlternatingArrowMesh");
        Vector3 scale = transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float sz = Mathf.Max(0.0001f, Mathf.Abs(scale.z));
        float half = Mathf.Max(0.01f, widthWorld * holdTailWidthFactor) * 0.5f / sx;
        float localLength = Mathf.Max(minimumHoldTailVisualLength, lengthWorld) / sz;
        // The requested visual ratio is independent of scroll speed: one full
        // left+right cycle may occupy at most five TAP heights.
        float tapHeightWorld = Mathf.Max(0.05f, ResolveNoteWorldHeight());
        float maximumCycleWorld = tapHeightWorld * 5f;
        int cycleCount = Mathf.Max(1, Mathf.CeilToInt(lengthWorld / maximumCycleWorld));
        int count = cycleCount * 2;
        // The streamed chunk must contain complete left/right pairs.  An odd
        // arrow count flips the phase when the next chunk is recycled and
        // leaves a conspicuous join in the middle of the lane.
        if ((count & 1) != 0) count++;
        // A Trill pattern only repeats after BOTH a left and a right arrow.
        // Repeating after one arrow caused a visible phase seam at each recycle.
        trillArrowRepeatWorldLength = Mathf.Max(0.25f,
            (lengthWorld / Mathf.Max(1, count)) * 2f);

        var vertices = new System.Collections.Generic.List<Vector3>(count * 6);
        var uvs = new System.Collections.Generic.List<Vector2>(count * 6);
        var colors = new System.Collections.Generic.List<Color>(count * 6);
        var triangles = new System.Collections.Generic.List<int>(count * 15);
        float step = localLength / Mathf.Max(1, count);
        // Opposite arrows sit tightly together; separation is created by their
        // opposing lateral fades, not by large longitudinal holes.
        float gap = step * 0.06f;
        // Use a long pointed head rather than a shallow corner chamfer. The
        // larger horizontal inset keeps the diagonal readable in perspective.
        float bevel = Mathf.Min(half * 0.68f, step * 1.25f);

        for (int i = 0; i < count; i++)
        {
            float z0 = i * step + gap * 0.5f;
            float z1 = (i + 1) * step - gap * 0.5f;
            float zm = (z0 + z1) * 0.5f;
            bool pointsLeft = (i & 1) == 0;
            int baseIndex = vertices.Count;
            if (pointsLeft)
            {
                vertices.Add(new Vector3(-half, 0f, zm));
                vertices.Add(new Vector3(-half + bevel, 0f, z0));
                vertices.Add(new Vector3(half, 0f, z0));
                vertices.Add(new Vector3(half, 0f, z1));
                vertices.Add(new Vector3(-half + bevel, 0f, z1));
            }
            else
            {
                vertices.Add(new Vector3(half, 0f, zm));
                vertices.Add(new Vector3(half - bevel, 0f, z0));
                vertices.Add(new Vector3(-half, 0f, z0));
                vertices.Add(new Vector3(-half, 0f, z1));
                vertices.Add(new Vector3(half - bevel, 0f, z1));
            }
            Vector3 center = Vector3.zero;
            for (int v = 0; v < 5; v++) center += vertices[baseIndex + v];
            center /= 5f;
            vertices.Add(center);

            for (int v = 0; v < 6; v++)
            {
                float u = v == 5
                    ? 0.5f
                    : (v == 0 ? (pointsLeft ? 0f : 1f)
                        : (v == 2 || v == 3 ? (pointsLeft ? 1f : 0f) : 0.18f));
                uvs.Add(new Vector2(u, i / (float)count));
                float vertexX = vertices[baseIndex + v].x;
                float towardPoint = pointsLeft
                    ? Mathf.InverseLerp(half, -half, vertexX)
                    : Mathf.InverseLerp(-half, half, vertexX);
                // Almost invisible immediately after crossing the centre line,
                // then rapidly reaches full glass intensity toward its arrow tip.
                float lateralAlpha = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.26f, 0.72f, towardPoint));
                // R carries the directional falloff. B stores the signed
                // horizontal position (0=left, .5=centre, 1=right). Unlike an
                // edge flag this interpolates linearly across triangles, so no
                // mesh edge or centre-fan line can become a light source.
                float lateralPosition = Mathf.Clamp01(
                    vertexX / Mathf.Max(0.0001f, half) * 0.5f + 0.5f);
                colors.Add(new Color(lateralAlpha, 0f, lateralPosition,
                    Mathf.Lerp(0.015f, 1f, lateralAlpha)));
            }
            int centerIndex = baseIndex + 5;
            for (int edge = 0; edge < 5; edge++)
            {
                triangles.Add(centerIndex);
                triangles.Add(baseIndex + edge);
                triangles.Add(baseIndex + ((edge + 1) % 5));
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private void EnsureTrillEndCap(float lengthWorld)
    {
        if (!isTrillCached || runtimeSpriteRenderer == null || runtimeSpriteRenderer.sprite == null) return;
        if (gestureEndCapObject == null)
        {
            gestureEndCapObject = new GameObject("TrillEndSoftNote");
            gestureEndCapObject.hideFlags = HideFlags.DontSave;
            gestureEndCapObject.transform.SetParent(transform, false);
            gestureEndCapRenderer = gestureEndCapObject.AddComponent<SpriteRenderer>();
        }
        gestureEndCapRenderer.sprite = runtimeSpriteRenderer.sprite;
        gestureEndCapRenderer.sharedMaterial = runtimeSpriteRenderer.sharedMaterial;
        gestureEndCapRenderer.color = Color.white;
        runtimeSpriteRenderer.GetPropertyBlock(notePropertyBlock);
        gestureEndCapRenderer.SetPropertyBlock(notePropertyBlock);
        gestureEndCapRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
        gestureEndCapRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder + 1;
        gestureEndCapObject.transform.localRotation = runtimeSpriteRenderer.transform.localRotation;
        gestureEndCapObject.transform.localScale = runtimeSpriteRenderer.transform.localScale;
        // The ending TAP marks endTime and must reach the judgment line at exactly
        // that moment. It is a flat sprite like a TAP head, so it uses the same
        // near-edge anchoring — centring it here would put it half a note-height
        // ahead of the head's convention.
        float localZ = lengthWorld / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        gestureEndCapObject.transform.localPosition =
            new Vector3(0f, 0.015f, localZ + NoteNearEdgeLocalOffsetZ());
        gestureEndCapObject.SetActive(true);
    }

    /// <summary>
    /// 把這顆音符擺成「只給看的」：外觀照遊戲裡的規則長出來，然後凍住。
    /// </summary>
    /// <remarks>
    /// 譜面檢視器要的是**真的那顆音符**，不是重畫一次 —— 圖、材質、寬度、斷奏的
    /// 寶石、滑奏的連結、長條的水晶身體全都由這裡的既有程式產生，所以檢視器不會
    /// 和遊戲各長各的。
    ///
    /// 凍住的意思是：不註冊給判定、不噴粒子、不飄斷奏標示、元件 enabled 關掉（所
    /// 以 Update 不會再把它往判定線推）。長條的身體另外算一次靜態版本：正常的那
    /// 條路會把身體裁在判定線附近的跑道內，而檢視器裡整首譜同時攤在 Z 軸上，照跑
    /// 道裁的話遠處的長條會整條消失。
    /// </remarks>
    public void ConfigureStaticPreview(NoteData data, NoteSpawner spawner, float speedWorldUnits,
        Vector3 worldOffset)
    {
        isStaticPreview = true;
        PreviewBuildMode = true;
        try
        {
            Initialize(data, spawner, data != null ? data.startTime : 0f);
        }
        finally
        {
            PreviewBuildMode = false;
        }

        // 整座舞台搬離遊戲的世界。Initialize 是照判定線的世界座標擺的，所以偏移要
        // 在它之後、長條身體之前 —— 身體的 shader 錨點（_HoldStartZ/_HoldEndZ）是
        // 絕對世界 Z，順序反了就會對不上。
        if (worldOffset != Vector3.zero) transform.position += worldOffset;

        try { Judgment.JudgmentManager.Instance?.UnregisterNote(this); } catch { }
        SuppressJudgment();
        HideStaccatoIndicator();
        if (noteFanParticleEmitter != null)
        {
            noteFanParticleEmitter.StopAndClear();
            noteFanParticleEmitter.enabled = false;
        }

        LayoutStaticPreviewBody(speedWorldUnits);
        enabled = false;
    }

    /// <summary>
    /// 俯視圖上的厚度。縮放變了就重設一次，音符在畫面上才是固定的厚度。
    /// </summary>
    /// <remarks>
    /// 遊戲裡音符的世界厚度是固定的（NoteVisualHeight），從斜上方看剛剛好；但檢視
    /// 器可以把時間軸縮到一格一秒，那個厚度就會細成一條線。所以改由檢視器算「螢幕
    /// 上要幾個像素」，換成世界單位餵回來。
    /// </remarks>
    public void SetPreviewThickness(float worldThickness)
    {
        if (runtimeSpriteRenderer == null || runtimeSpriteRenderer.sprite == null) return;
        Vector2 authoredSize = runtimeSpriteRenderer.sprite.bounds.size;
        float rootScaleZ = Mathf.Abs(transform.lossyScale.z);
        if (authoredSize.y <= 0.0001f || rootScaleZ <= 0.0001f) return;

        Transform child = runtimeSpriteRenderer.transform;
        Vector3 scale = child.localScale;
        scale.y = Mathf.Max(0.0001f, worldThickness) / (authoredSize.y * rootScaleZ);
        child.localScale = scale;
        // 近緣仍然對齊音符的時間點：厚度改了，偏移也要跟著改。localPosition 是
        // 父物件的區域座標，所以世界厚度要先除回根的 Z 縮放（和
        // NoteNearEdgeLocalOffsetZ 同一個換算）。
        child.localPosition = new Vector3(0f, 0f, worldThickness * 0.5f / rootScaleZ);
    }

    /// <summary>長條／顫音的身體：整段畫出來，不做判定線裁切。</summary>
    private void LayoutStaticPreviewBody(float speedWorldUnits)
    {
        if (holdTailObject == null || holdTailRenderer == null) return;

        if (isStaccatoCached)
        {
            SetHoldTailActive(false);
            if (gestureEndCapObject != null) gestureEndCapObject.SetActive(false);
            return;
        }

        // 滑奏要先處理，而且**不能用 endTime 算長度**。
        //
        // 滑奏節點的 endTime 等於 startTime：它自己沒有長度，連結的長度來自「到下
        // 一個節點的距離」，在生成時就由 BuildSlideConnectorMesh 算進 mesh 裡了。
        // 照長度為 0 把它隱藏的話，一整串滑奏就會變成一顆一顆分開的 tap。
        if (isSlideCached)
        {
            holdTailObject.transform.localScale = Vector3.one;
            holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
            SetHoldTailActive(true);
            holdTailRenderer.GetPropertyBlock(holdTailPropertyBlock);
            holdTailPropertyBlock.SetFloat(HoldTailWorldClipEnabledId, 0f);
            // 俯視檢視器不走弧線，但必須**明講**：不寫的話這次 draw call
            // 會沿用常數緩衝區裡上一次同 shader 留下來的表。
            NoteArcScreen.ClearOn(holdTailPropertyBlock);
            holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
            if (gestureEndCapObject != null) gestureEndCapObject.SetActive(false);
            return;
        }

        float lengthWorld = ((endTime - startTime) / 1000f) * Mathf.Max(0.0001f, speedWorldUnits);
        if (lengthWorld <= 0.0001f)
        {
            SetHoldTailActive(false);
            if (gestureEndCapObject != null) gestureEndCapObject.SetActive(false);
            return;
        }

        float headZ = transform.position.z;
        float tailEndZ = headZ + lengthWorld;
        float widthWorld = currentTailWidthWorld > 0f
            ? currentTailWidthWorld
            : Mathf.Max(0.01f, cachedWorldWidth *
                (IsGlassRodHold ? HoldGlassWidthShare : holdTailWidthFactor));

        if (isTrillCached)
        {
            // 顫音的箭頭也是自訂 mesh，但它在生成時是照「跑道長度」建的（會做成
            // 實際長度的兩倍多，好讓它在跑道上循環）。靜態檢視要的是真正的長度，
            // 所以用同一支函式重建一次，縮放維持 1。
            BuildTrillArrowMesh(widthWorld, lengthWorld);
            EnsureTrillEndCap(lengthWorld);
            holdTailObject.transform.localScale = Vector3.one;
            holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
            SetHoldTailActive(true);
        }
        else
        {
            UpdateHoldTailDimensions(widthWorld, lengthWorld, true);
            holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
            SetHoldTailActive(true);
        }

        holdTailRenderer.GetPropertyBlock(holdTailPropertyBlock);
        // 不裁切：檢視器把整首攤開，跑道範圍在這裡沒有意義。
        holdTailPropertyBlock.SetFloat(HoldTailWorldClipEnabledId, 0f);
        NoteArcScreen.ClearOn(holdTailPropertyBlock);
        float repeatWorld = isTrillCached
            ? Mathf.Max(0.25f, trillArrowRepeatWorldLength)
            : Mathf.Max(0.25f, streamingTailRepeatWorldLength);
        holdTailPropertyBlock.SetVector(HoldTailMainTexStId, isTrillCached
            ? new Vector4(1f, Mathf.Max(1f, lengthWorld / repeatWorld), 0f, 0f)
            : new Vector4(1f, 1f, 0f, 0f));
        if (IsGlassRodHold)
        {
            holdTailPropertyBlock.SetFloat(HoldStartZId, headZ);
            holdTailPropertyBlock.SetFloat(HoldEndZId, tailEndZ);
            holdTailPropertyBlock.SetFloat(HoldBeatSpacingZId,
                (HoldBeatMs() / 1000f) * speedWorldUnits);
            // 判定線推到很遠：檢視器裡沒有「正在按」，充能和經過線的高光都不該亮。
            holdTailPropertyBlock.SetFloat(HoldJudgeZId, -1e6f);
        }
        holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);

        if (gestureEndCapObject != null)
        {
            if (!isTrillCached)
            {
                gestureEndCapObject.SetActive(false);
            }
            else
            {
                float localZScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
                gestureEndCapObject.transform.localPosition = new Vector3(
                    0f, 0.015f, (tailEndZ - headZ) / localZScale + NoteNearEdgeLocalOffsetZ());
                gestureEndCapObject.SetActive(true);
            }
        }
    }

    public void SetGestureVisualStrength(float strength)
    {
        if (!isTrillCached) return;
        strength = Mathf.Clamp01(strength);
        if (Mathf.Abs(strength - lastAppliedGestureVisualStrength) < 0.001f) return;
        gestureVisualStrength = strength;
        lastAppliedGestureVisualStrength = strength;
        Color body = ResolveGestureColor();
        body.a = Mathf.Lerp(0f, 1f, gestureVisualStrength);
        if (holdTailRenderer != null)
        {
            holdTailRenderer.GetPropertyBlock(holdTailPropertyBlock);
            holdTailPropertyBlock.SetColor(HoldTailColorId, body);
            holdTailPropertyBlock.SetFloat(HoldTailFlowStrengthId, Mathf.Lerp(0.08f, 0.85f, gestureVisualStrength));
            holdTailPropertyBlock.SetColor(HoldTailFlowColorId, Color.Lerp(body, Color.white, 0.06f));
            holdTailPropertyBlock.SetFloat(HoldTailPreserveGoldId, 0f);
            holdTailPropertyBlock.SetFloat(HoldTailEmissionId, Mathf.Lerp(0.1f, 1.1f, gestureVisualStrength));
            holdTailPropertyBlock.SetFloat(HoldTailEdgeGlowId, Mathf.Lerp(0.15f, 1.4f, gestureVisualStrength));
            holdTailPropertyBlock.SetFloat(HoldTailFadeStrengthId, 0f);
            holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
        }
        if (runtimeSpriteRenderer != null)
        {
            runtimeSpriteRenderer.GetPropertyBlock(notePropertyBlock);
            Color headColor = ResolveGestureColor();
            headColor.a = gestureVisualStrength;
            notePropertyBlock.SetColor(ThemeColorId, headColor);
            notePropertyBlock.SetFloat(EmissionId, noteEmission * Mathf.Lerp(0.25f, 1.55f, gestureVisualStrength));
            notePropertyBlock.SetFloat(RimGlowId, noteRimGlow * Mathf.Lerp(0.2f, 1.55f, gestureVisualStrength));
            notePropertyBlock.SetFloat(TapGlassOpacityId, tapGlassOpacity * gestureVisualStrength);
            runtimeSpriteRenderer.SetPropertyBlock(notePropertyBlock);
        }
        if (gestureEndCapRenderer != null)
        {
            // The ending TAP is a timing marker, not part of the sustained
            // input glow. It remains fully visible when the Trill body fades.
            gestureEndCapRenderer.color = Color.white;
        }
    }

    private void DisposeHoldTailImmediate()
    {
        try
        {
            if (holdTailRenderer != null)
            {
                holdTailRenderer.sharedMaterial = null;
            }
        }
        catch { }

        if (holdTailObject != null)
        {
            try { Destroy(holdTailObject); }
            catch { }
        }

        holdTailObject = null;
        holdTailRenderer = null;
        holdTailMeshFilter = null;
    }

    private void ApplyRuntimeSpriteTheme()
    {
        if (runtimeSpriteRenderer == null) return;

        if (cachedDefaultSpriteMaterial == null)
            cachedDefaultSpriteMaterial = runtimeSpriteRenderer.sharedMaterial;

        Shader shader = glassThemeShaderOverride != null
            ? glassThemeShaderOverride
            : Shader.Find("Custom/GlassThemeSprite");
        if (shader == null) return;

        bool isRight = noteData != null && noteData.hand == 0;
        bool useSoftGestureHead = isSoftCached || isSlideCached;
        Material themed = useSoftGestureHead
            ? cachedSoftGlowMaterial
            : (isRight ? cachedRightGlassThemeMaterial : cachedLeftGlassThemeMaterial);
        if (themed == null || themed.shader != shader)
        {
            themed = new Material(shader)
            {
                name = useSoftGestureHead
                    ? "Glass Theme Gold SOFT (Runtime)"
                    : (isRight ? "Glass Theme Red (Runtime)" : "Glass Theme Blue (Runtime)"),
                hideFlags = HideFlags.DontSave
            };
            if (useSoftGestureHead) cachedSoftGlowMaterial = themed;
            else if (isRight) cachedRightGlassThemeMaterial = themed;
            else cachedLeftGlassThemeMaterial = themed;
        }

        themed.SetColor("_ThemeColor", useSoftGestureHead ? softThemeColor : (isRight ? rightThemeColor : leftThemeColor));
        themed.SetFloat("_Emission", noteEmission);
        themed.SetFloat("_RimGlow", noteRimGlow);
        themed.SetFloat("_ShimmerStrength", noteShimmerStrength);

        runtimeSpriteRenderer.sharedMaterial = themed;
        runtimeSpriteRenderer.color = Color.white;

        bool isPlainTap = noteData != null && !isSoftCached && !isStaccatoCached &&
            (string.Equals(noteData.type, "tap", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(noteData.type, "0", StringComparison.OrdinalIgnoreCase) ||
             (string.IsNullOrEmpty(noteData.type) && noteData.note_type == 0));
        bool isOrdinaryHoldHead = noteData != null && !isStaccatoCached &&
            string.Equals(noteData.type, "hold", StringComparison.OrdinalIgnoreCase);
        bool useGlassCore = isPlainTap || isOrdinaryHoldHead || isSlideCached || isTrillCached ||
            isStaccatoCached;
        if (notePropertyBlock == null) notePropertyBlock = new MaterialPropertyBlock();
        runtimeSpriteRenderer.GetPropertyBlock(notePropertyBlock);
        notePropertyBlock.SetColor(ThemeColorId, useSoftGestureHead ? softThemeColor : ResolveGestureColor());
        notePropertyBlock.SetFloat(EmissionId, (isSlideCached || isTrillCached) ? noteEmission * 1.3f : noteEmission);
        notePropertyBlock.SetFloat(RimGlowId, (isSlideCached || isTrillCached) ? noteRimGlow * 1.35f : noteRimGlow);
        notePropertyBlock.SetFloat(TapGlassEnabledId, useGlassCore ? 1f : 0f);
        notePropertyBlock.SetColor(TapGlassColorId, isTrillCached
            ? new Color(0.96f, 0.98f, 1f, 1f)
            : (isSlideCached ? new Color(0.88f, 1f, 1f, 1f) : tapGlassColor));
        notePropertyBlock.SetFloat(TapGlassOpacityId, (isSlideCached || isTrillCached) ? 0.62f : tapGlassOpacity);
        notePropertyBlock.SetFloat(TapGlassRimGlowId, (isSlideCached || isTrillCached) ? tapGlassRimGlow * 1.35f : tapGlassRimGlow);
        notePropertyBlock.SetFloat(TapGlassInsetXId, tapGlassInsetX);
        notePropertyBlock.SetFloat(TapGlassInsetYId, tapGlassInsetY);
        notePropertyBlock.SetFloat(TapGlassBorderId, tapGlassBorder);
        notePropertyBlock.SetFloat(TapGlassChamferId, tapGlassChamfer);
        runtimeSpriteRenderer.SetPropertyBlock(notePropertyBlock);
    }

    private Texture2D ResolveHoldTailTexture(bool isRightHand)
    {
        Texture2D texture = isRightHand ? rightHoldTailTexture : leftHoldTailTexture;
        if (texture != null) return texture;

        // Fallback to hand-specific runtime textures if dedicated tail texture isn't assigned
        texture = isRightHand ? rightHandTexture : leftHandTexture;
        if (texture != null) return texture;

        Sprite fallbackSprite = null;
        if (isRightHand)
        {
            if (rightHandSprite != null) fallbackSprite = rightHandSprite;
            else if (staccatoRightSprite != null) fallbackSprite = staccatoRightSprite;
        }
        else
        {
            if (leftHandSprite != null) fallbackSprite = leftHandSprite;
            else if (staccatoLeftSprite != null) fallbackSprite = staccatoLeftSprite;
        }

        if (fallbackSprite != null)
        {
            try { return fallbackSprite.texture; }
            catch { }
        }

        // Attempt to load common resource paths so users don't have to remember to assign manually.
        string baseName = isRightHand ? "Righthold" : "Lefthold";
        string[] resourceHints = new string[]
        {
            baseName,
            $"HoldTail/{baseName}",
            $"HoldTails/{baseName}",
            $"Textures/{baseName}",
            $"Materials/{baseName}"
        };
        foreach (var hint in resourceHints)
        {
            try
            {
                var tex = Resources.Load<Texture2D>(hint);
                if (tex != null) return tex;
            }
            catch { }
        }

        return null;
    }

    private Material ResolveHoldTailMaterial(bool isRightHand, out Texture2D resolvedTexture)
    {
        resolvedTexture = ResolveHoldTailTexture(isRightHand);
        if (resolvedTexture == null)
        {
            try { BuildLogger.LogWarning($"[NoteController] Hold tail texture missing for {(isRightHand ? "right" : "left")} hand. Assign tail textures in the inspector (Lefthold.png / Righthold.png)." ); } catch { }
            return null;
        }

        Material cached = isRightHand ? cachedRightHoldTailMaterial : cachedLeftHoldTailMaterial;
        if (cached != null)
        {
            if (cached.mainTexture == null)
            {
                cached.mainTexture = resolvedTexture;
            }
            resolvedTexture = cached.mainTexture as Texture2D ?? resolvedTexture;
            cached.color = isRightHand ? rightTailTint : leftTailTint;
            return cached;
        }

        Shader shader = holdTailShaderOverride != null ? holdTailShaderOverride : Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        var material = new Material(shader)
        {
            name = isRightHand ? "RightHoldTailMaterial" : "LeftHoldTailMaterial",
            hideFlags = HideFlags.DontSave
        };
        material.mainTexture = resolvedTexture;
        material.color = isRightHand ? rightTailTint : leftTailTint;
        material.renderQueue = (int)RenderQueue.Transparent;

        if (holdTailShaderOverride == null)
        {
            if (material.HasFloat("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasFloat("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasFloat("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasFloat("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        if (isRightHand)
        {
            cachedRightHoldTailMaterial = material;
        }
        else
        {
            cachedLeftHoldTailMaterial = material;
        }

        return material;
    }

    private void ApplyHoldTailMaterialProperties(bool isRightHand, Texture2D texture)
    {
        if (holdTailRenderer == null) return;
        if (holdTailPropertyBlock == null) holdTailPropertyBlock = new MaterialPropertyBlock();

        holdTailPropertyBlock.Clear();
        if (isSlideCached)
        {
            // 光束走的是另一支 shader（Custom/UnlitAdditiveEmission），長條那一堆參數
            // 它一個都不認得。亮度分布在頂點色裡，這裡只給強度。
            holdTailPropertyBlock.SetColor(HoldTailColorId, Color.white);
            holdTailPropertyBlock.SetColor(SlideBeamEmissionId, Color.white * SlideBeamIntensity);
            holdTailPropertyBlock.SetTexture(HoldTailMainTexId, Texture2D.whiteTexture);
            holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
            return;
        }
        Color tailColor = (isSlideCached || isTrillCached)
            ? ResolveGestureColor()
            : (isRightHand ? rightTailTint : leftTailTint);
        holdTailPropertyBlock.SetColor(HoldTailColorId, tailColor);
        // 近端提亮、遠端壓暗，是長條的透視：一條拉向譜面深處的帶子，這條漸層就
        // 是它的長度。
        //
        // **滑動必須關掉它。** 滑動是一串斜著往下走的音符，每一段連結的近端貼著
        // 前一顆、遠端貼著後一顆。漸層一放上去，近端比它蓋住的那顆淺（讀成蓋在
        // 上面的一片東西），遠端比它要接的那顆深（讀成沒接上）—— 同一條漸層在兩
        // 端各製造了一個不同的錯誤。兩端都用音符自己的顏色，接縫才會不見。
        float nearMix = isSlideCached ? 0f : 0.34f;
        float farShade = isSlideCached ? 1f : 0.38f;
        Color nearColor = Color.Lerp(tailColor, Color.white, nearMix);
        nearColor.a = tailColor.a;
        Color farColor = new Color(
            tailColor.r * farShade,
            tailColor.g * farShade,
            tailColor.b * farShade,
            tailColor.a);
        holdTailPropertyBlock.SetColor(HoldTailNearColorId, nearColor);
        holdTailPropertyBlock.SetColor(HoldTailFarColorId, farColor);
        holdTailPropertyBlock.SetFloat(HoldTailFlowStrengthId, headPressed ? 0.82f : 0.12f);
        Color flowColor = (isTrillCached || isSlideCached)
            ? Color.Lerp(tailColor, Color.white, 0.06f)
            : new Color(1f, 0.76f, 0.28f, 1f);
        holdTailPropertyBlock.SetColor(HoldTailFlowColorId, flowColor);
        holdTailPropertyBlock.SetFloat(HoldTailPreserveGoldId, (isTrillCached || isSlideCached) ? 0f : 1f);
        holdTailPropertyBlock.SetFloat(HoldTailFlatFillId, isTrillCached ? 1f : 0f);
        // 一般長條走玻璃棒的畫法。滑奏、顫音不動：滑奏兩端要和前後的音符接得
        // 上（所以它刻意不做明暗），顫音是深色中心的扁平帶 —— 各有各的理由。
        holdTailPropertyBlock.SetFloat(HoldGlassRodId, IsGlassRodHold ? 1f : 0f);
        holdTailPropertyBlock.SetFloat(HoldTailSmoothBodyId, 1f);
        // **滑奏不要深色中心核。**
        //
        // 那個核是給一般長條用的：未判定時把中心 25% 壓成 `_Color × 0.22`，
        // 讓它讀起來像一根深色玻璃棒。但滑奏的連結段只有一顆音符高那麼長、
        // 兩端還埋在音符頭底下，把中間壓成深紅之後，**在黑底上就等於不存在**——
        // 實測連結段 85×21 px、alpha 0.92、位置全對，使用者却完全看不到它；
        // 漆成亮綠色就立刻出現。
        //
        // 旁邊那句註解寫著「滑奏刻意不做明暗」，而這三個參數正好相反，
        // 一直沒人發現：深色核、以及尾端 0.3 的縱向淡出。兩個都關掉。
        holdTailPropertyBlock.SetFloat(HoldTailCoreWidthId, isSlideCached ? 0.02f : 0.25f);
        holdTailPropertyBlock.SetFloat(HoldTailCoreGlowId, isSlideCached ? 0f : 0.65f);
        holdTailPropertyBlock.SetFloat(HoldTailCoreJudgedBoostId, 1.45f);
        holdTailPropertyBlock.SetFloat(HoldTailEmissionId, isTrillCached ? 1.1f : (isSlideCached ? 1.65f : 1.2f));
        holdTailPropertyBlock.SetFloat(HoldTailEdgeGlowId, isTrillCached ? 1.4f : (isSlideCached ? 1.35f : 0.9f));
        holdTailPropertyBlock.SetFloat(HoldTailFadeStrengthId,
            (isTrillCached || isSlideCached) ? 0f : 1f);
        holdTailPropertyBlock.SetFloat(HoldTailWorldClipEnabledId,
            (isTrillCached || (noteData != null && noteData.type == "hold")) ? 1f : 0f);
        if (isTrillCached)
        {
            // Trill is a clean glass ribbon.  Reusing the Hold texture stamped
            // rectangular ornaments into every arrow and made it look like a
            // row of unrelated note cards.
            holdTailPropertyBlock.SetTexture(HoldTailMainTexId, Texture2D.whiteTexture);
        }
        else if (texture != null)
        {
            holdTailPropertyBlock.SetTexture(HoldTailMainTexId, texture);
        }
        holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
        lastHoldTailFlowStrength = headPressed ? 0.82f : 0.12f;
    }

    /// <summary>一般長條（不是滑奏、不是顫音）。</summary>
    private bool IsGlassRodHold => !isTrillCached && !isSlideCached;

    /// <summary>一拍多少毫秒。譜面是單一 BPM 的，拿 first_bpm 就夠了。</summary>
    private static float HoldBeatMs()
    {
        float bpm = 0f;
        try
        {
            Chart chart = GameManager.Instance != null ? GameManager.Instance.CurrentChart : null;
            if (chart != null) bpm = chart.first_bpm;
        }
        catch { }
        return 60000f / Mathf.Max(1f, bpm > 0f ? bpm : 120f);
    }

    private void UpdateHoldTailFlow()
    {
        if (holdTailRenderer == null || holdTailObject == null || !holdTailObject.activeInHierarchy) return;
        float target = headPressed ? 0.82f : 0.12f;
        if (Mathf.Abs(target - lastHoldTailFlowStrength) < 0.001f) return;
        holdTailRenderer.GetPropertyBlock(holdTailPropertyBlock);
        holdTailPropertyBlock.SetFloat(HoldTailFlowStrengthId, target);
        holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);
        lastHoldTailFlowStrength = target;
    }

    private void SetHoldTailActive(bool active)
    {
        if (holdTailObject == null || holdTailRenderer == null) return;
        if (active && holdTailObject.activeSelf && holdTailRenderer.enabled) return;
        if (!active && !holdTailObject.activeSelf && !holdTailRenderer.enabled) return;
        if (active)
        {
            holdTailRenderer.enabled = true;
            holdTailObject.SetActive(true);
            try
            {
                bool isRightHand = noteData != null && noteData.hand == 0;
                Texture2D currentTexture = null;
                if (holdTailRenderer.sharedMaterial != null)
                {
                    currentTexture = holdTailRenderer.sharedMaterial.mainTexture as Texture2D;
                }
                ApplyHoldTailMaterialProperties(isRightHand, currentTexture);
                if (isTrillCached) SetGestureVisualStrength(gestureVisualStrength);
            }
            catch { }
        }
        else
        {
            holdTailRenderer.enabled = false;
            holdTailObject.SetActive(false);
        }
    }

    private void UpdateHoldTailDimensions(float widthWorld, float lengthWorld, bool forceUpdate = false)
    {
        if (holdTailObject == null) return;

        widthWorld = Mathf.Max(0.0001f, widthWorld);
        lengthWorld = Mathf.Max(0f, lengthWorld);

        Transform parent = holdTailObject.transform.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;

        float localWidth = parentScale.x != 0f ? widthWorld / parentScale.x : widthWorld;
        float localLength = parentScale.z != 0f ? lengthWorld / parentScale.z : lengthWorld;

        Vector3 currentScale = holdTailObject.transform.localScale;
        bool widthChanged = forceUpdate || Mathf.Abs(currentScale.x - localWidth) > tailLengthUpdateThreshold;
        bool lengthChanged = forceUpdate || Mathf.Abs(currentScale.z - localLength) > tailLengthUpdateThreshold;

        if (widthChanged || lengthChanged)
        {
            holdTailObject.transform.localScale = new Vector3(localWidth, 1f, Mathf.Max(0.0001f, localLength));
            holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
        }

        currentTailWidthWorld = widthWorld;
        lastAppliedTailLength = lengthWorld;
    }

    private static float FollowDspVisualTarget(float current, float target, float speedWorldUnits)
    {
        // Conductor.renderSongPosition is already a continuous clock: it runs
        // at realtime rate and only its offset from the quantised DSP sample is
        // filtered. Do NOT re-introduce a per-object deltaTime follower here —
        // it would advance at a slightly different rate, accumulate error, then
        // snap, and each note would drift out of phase with beat lines and
        // TRACK. Smoothing belongs in exactly one place: the render clock.
        return target;
    }

    private void UpdateStreamingLongVisual(float songPos, float speedWorldUnits)
    {
        float movingHeadZ = judgmentZ + ((startTime - songPos) / 1000f) * speedWorldUnits;
        // All long-note parts use the same DSP-derived target as TAP notes and
        // beat lines. Do not add a per-object deltaTime follower here: even a
        // tiny rate mismatch eventually catches up in a visible snap.
        float visualSmoothOffset = 0f;
        float headZ;
        if (streamingJudgmentAnchorActive)
        {
            headZ = streamingJudgmentAnchorZ;
        }
        else
        {
            float smoothed = FollowDspVisualTarget(
                transform.position.z, movingHeadZ, speedWorldUnits);
            headZ = smoothed;
            visualSmoothOffset = headZ - movingHeadZ;
        }
        float visualEndTime = holdTailAdjustedEndMs > startTime ? holdTailAdjustedEndMs : endTime;
        float anchorVisualClock = songPos;
        if (streamingJudgmentAnchorActive)
        {
            anchorVisualClock = Mathf.Max(songPos, streamingAnchorConsumeStartMs);
            float deltaMs = Mathf.Max(0f, anchorVisualClock - streamingAnchorLastSongPosMs);
            streamingAnchorTravelWorld += (deltaMs / 1000f) * speedWorldUnits;
            streamingAnchorLastSongPosMs = Mathf.Max(streamingAnchorLastSongPosMs, anchorVisualClock);

            // Pattern motion follows actual scroll speed, but the authored tail
            // must reach the captured clipping edge exactly at visual end time.
            // This also consumes any small smoothing offset retained on the hit
            // frame instead of leaving a final piece that vanishes abruptly.
            float consumeDurationMs = Mathf.Max(1f,
                streamingAnchorVisualEndMs - streamingAnchorConsumeStartMs);
            float consumeProgress = Mathf.Clamp01(
                (anchorVisualClock - streamingAnchorConsumeStartMs) / consumeDurationMs);
            float requiredTailTravel = streamingAnchorRequiredTailTravelWorld * consumeProgress;
            streamingAnchorTailTravelWorld = Mathf.Max(
                streamingAnchorTailTravelWorld, requiredTailTravel);
        }
        // A FAST head freezes above the line, but the ribbon must continue its
        // authored downward travel immediately. Translate the captured clip
        // window to the judgment line during the early interval; do not hold it
        // in place and append that interval beyond endTime.
        float anchorWindowTranslation = 0f;
        if (streamingJudgmentAnchorActive && streamingAnchorClipMinZ > judgmentZ)
        {
            float preStartDurationMs = Mathf.Max(1f,
                startTime - streamingAnchorConsumeStartMs);
            float preStartProgress = Mathf.Clamp01(
                (anchorVisualClock - streamingAnchorConsumeStartMs) / preStartDurationMs);
            anchorWindowTranslation =
                (streamingAnchorClipMinZ - judgmentZ) * preStartProgress;
        }
        // The judged head/effect may sit above or below the line to communicate
        // timing, but the body never reveals pixels that already crossed its
        // captured lower edge. For an early hit that edge travels to the line.
        float streamOriginZ = streamingJudgmentAnchorActive
            ? Mathf.Max(judgmentZ, streamingAnchorClipMinZ - anchorWindowTranslation)
            : judgmentZ;
        float tailEndZ = streamingJudgmentAnchorActive
            // Keep the authored tail moving in the same direction and at the
            // same world speed after the head freezes. Only the pixels that
            // cross the fixed lower clipping edge are consumed.
            ? streamingAnchorTailEndZ - streamingAnchorTailTravelWorld
            : judgmentZ + ((visualEndTime - songPos) / 1000f) * speedWorldUnits + visualSmoothOffset;
        Vector3 notePos = transform.position;
        notePos.z = headZ;
        // 頭和尾巴走同一條弧線：尾巴是 shader 依世界 Z 彎的，頭這裡用同一條曲線，
        // 依「頭現在在哪個 Z」換算回剩餘時間。
        ApplyArcToHead(ref notePos);
        transform.position = notePos;

        // A judged Hold keeps its exact head visible on the judgment line. Only
        // the ribbon behind it is consumed toward endTime.
        bool headVisible = streamingJudgmentAnchorActive || headZ >= judgmentZ - 0.001f;
        if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = headVisible;

        float visibleRunway = Mathf.Max(streamingTailMaximumVisibleLength, streamingVisibleLengthWorld);
        float streamTopZ = streamingJudgmentAnchorActive
            ? Mathf.Max(streamOriginZ, streamingAnchorWindowTopZ - anchorWindowTranslation)
            : streamOriginZ + Mathf.Min(visibleRunway, Mathf.Max(0f, initialTailLength));
        float clipMinZ;
        float clipMaxZ;
        if (streamingJudgmentAnchorActive)
        {
            // The body keeps streaming toward its captured lower edge. Head and
            // effect position are independent, so a SLOW hit below the line
            // cannot regenerate an already-consumed part of the ribbon.
            clipMinZ = streamOriginZ;
            clipMaxZ = Mathf.Min(streamTopZ, tailEndZ);
        }
        else if (songPos < startTime)
        {
            clipMinZ = Mathf.Max(judgmentZ, headZ);
            clipMaxZ = Mathf.Min(judgmentZ + visibleRunway, tailEndZ);
        }
        else
        {
            // The lower clipping edge never leaves the judgment line. The tail
            // endpoint and every pattern vertex continue downward in chart
            // coordinates; only geometry that crosses the line is destroyed.
            clipMinZ = judgmentZ;
            clipMaxZ = Mathf.Min(streamTopZ, tailEndZ);
        }
        currentTailLength = Mathf.Max(0f, clipMaxZ - clipMinZ);
        bool tailVisible = holdTailObject != null && clipMaxZ > clipMinZ + 0.0001f;
        visibleTailClipValid = tailVisible;
        visibleTailClipMinZ = clipMinZ;
        visibleTailClipMaxZ = clipMaxZ;
        SetHoldTailActive(tailVisible && !isStaccatoCached);
        if (!tailVisible || holdTailObject == null || holdTailRenderer == null)
        {
            if (gestureEndCapObject != null) gestureEndCapObject.SetActive(false);
            return;
        }

        float repeatWorld = isTrillCached
            ? Mathf.Max(0.25f, trillArrowRepeatWorldLength)
            : Mathf.Max(0.25f, streamingTailRepeatWorldLength);
        // Before judgment the whole note root already travels with the chart,
        // so an additional local offset would double its speed. Once the head
        // freezes, continue the Trill pattern from zero at exactly chart speed.
        // Loop only one complete left+right pair; both ends are geometrically
        // identical, making the wrap invisible instead of respawning a runway.
        float passedWorld = streamingJudgmentAnchorActive
            ? streamingAnchorTravelWorld
            : 0f;
        float windowOffsetWorld = isTrillCached
            ? -Mathf.Repeat(passedWorld, repeatWorld)
            : 0f;
        float localZScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        holdTailObject.transform.localPosition = new Vector3(
            0f, holdTailYOffset, windowOffsetWorld / localZScale);

        if (isTrillCached)
        {
            holdTailObject.transform.localScale = Vector3.one;
        }
        else
        {
            float widthWorld = currentTailWidthWorld > 0f
                ? currentTailWidthWorld
                : Mathf.Max(0.01f, cachedWorldWidth *
                    (IsGlassRodHold ? HoldGlassWidthShare : holdTailWidthFactor));
            Vector3 rootScale = transform.lossyScale;
            float localWidth = widthWorld / Mathf.Max(0.0001f, Mathf.Abs(rootScale.x));
            float localLength = streamingMeshLengthWorld / localZScale;
            holdTailObject.transform.localScale = new Vector3(localWidth, 1f, localLength);
        }

        holdTailRenderer.GetPropertyBlock(holdTailPropertyBlock);
        // 尾巴要和頭走同一條弧線：送的是音符頭自己用的那張表，不是另外算一次。
        WriteArcTo(holdTailPropertyBlock);
        holdTailPropertyBlock.SetFloat(HoldTailWorldClipEnabledId, 1f);
        holdTailPropertyBlock.SetFloat(HoldTailWorldClipMinZId, clipMinZ - 0.002f);
        holdTailPropertyBlock.SetFloat(HoldTailWorldClipMaxZId, clipMaxZ);
        if (isTrillCached)
        {
            float tiledLength = Mathf.Max(1f, streamingMeshLengthWorld / repeatWorld);
            holdTailPropertyBlock.SetVector(HoldTailMainTexStId,
                new Vector4(1f, tiledLength, 0f, passedWorld / repeatWorld));
        }
        else
        {
            // Hold textures are commonly imported with Clamp wrapping. Tiling those
            // textures produces a bright/dark line at every repeat boundary, so the
            // body stays single-sampled while its procedural flow supplies motion.
            // Moving/recycling the whole quad caused judged sections to reappear.
            holdTailPropertyBlock.SetVector(HoldTailMainTexStId, new Vector4(1f, 1f, 0f, 0f));
        }
        if (IsGlassRodHold)
        {
            // 長條的真實起點由尾端往回推，而不是讀 headZ：判定之後音符頭會停在判
            // 定線上，但身體照譜面速度繼續走。刻度和紋理要跟著身體走，所以起點要
            // 從還在動的那一端算回來。
            float beatSpacingZ = (HoldBeatMs() / 1000f) * speedWorldUnits;
            float holdStartZ = tailEndZ - ((visualEndTime - startTime) / 1000f) * speedWorldUnits;
            holdTailPropertyBlock.SetFloat(HoldStartZId, holdStartZ);
            holdTailPropertyBlock.SetFloat(HoldEndZId, tailEndZ);
            holdTailPropertyBlock.SetFloat(HoldBeatSpacingZId, beatSpacingZ);
            holdTailPropertyBlock.SetFloat(HoldJudgeZId, judgmentZ);
        }
        holdTailRenderer.SetPropertyBlock(holdTailPropertyBlock);

        if (gestureEndCapObject != null)
        {
            if (!isTrillCached)
            {
                // A pooled controller may retain the object created by an older
                // Trill. Never reactivate that marker for an ordinary Hold.
                gestureEndCapObject.SetActive(false);
            }
            else
            {
                float capWorldZ = tailEndZ;
                float endLocalZ = (capWorldZ - headZ) / localZScale;
                gestureEndCapObject.transform.localPosition =
                    new Vector3(0f, 0.015f, endLocalZ + NoteNearEdgeLocalOffsetZ());
                float capVisibleMaxZ = songPos < startTime
                    ? streamOriginZ + visibleRunway
                    : streamTopZ;
                gestureEndCapObject.SetActive(tailVisible && capWorldZ >= streamOriginZ &&
                    capWorldZ <= capVisibleMaxZ);
            }
        }
    }

    public void BeginStreamingJudgment(float inputSongPosMs)
    {
        if (streamingJudgmentAnchorActive || noteData == null) return;
        bool isLongStream = isTrillCached ||
            (string.Equals(noteData.type, "hold", System.StringComparison.OrdinalIgnoreCase) &&
             !isStaccatoCached);
        if (!isLongStream) return;

        float speedWorldUnits = noteSpawner != null ? noteSpawner.speed : 30f;
        // inputSongPosMs belongs to the judgment clock and may include the
        // player's judgment offset. Capture the ribbon from the raw visual
        // clock so switching to the judged state cannot move its tail.
        float visualSongPosMs = inputSongPosMs;
        try
        {
            Conductor visualConductor = conductor != null
                ? conductor
                : (GameManager.Instance != null ? GameManager.Instance.Conductor : null);
            if (visualConductor != null)
                visualSongPosMs = visualConductor.renderSongPosition;
        }
        catch { }
        float actualVisualHeadZ = judgmentZ +
            ((startTime - visualSongPosMs) / 1000f) * speedWorldUnits;
        // UpdateStreamingLongVisual smooths the moving root toward its DSP
        // target. Snapshot that rendered offset too; otherwise the judgment
        // transition silently removes the smoothing distance from the Trill.
        float renderedVisualHeadZ = transform.position.z;
        float renderedSmoothOffsetZ = renderedVisualHeadZ - actualVisualHeadZ;
        // Freeze the long-note root exactly where it was rendered on the hit
        // frame. FAST/SLOW precision is communicated by the independent MESH
        // and particle origin; moving this root to a separately calculated
        // timestamp changes the head-to-tail distance and looks like growth.
        // Timing precision remains visible through MESH/particles. The physical
        // Hold head itself always locks to the judgment line after acceptance.
        float movingHeadZ = judgmentZ;
        float visualEndTime = holdTailAdjustedEndMs > startTime ? holdTailAdjustedEndMs : endTime;
        float movingTailEndZ = judgmentZ +
            ((visualEndTime - visualSongPosMs) / 1000f) * speedWorldUnits +
            renderedSmoothOffsetZ;
        float visibleRunway = Mathf.Max(streamingTailMaximumVisibleLength, streamingVisibleLengthWorld);
        float oldClipMinZ = judgmentZ;
        float oldClipMaxZ = Mathf.Min(judgmentZ + visibleRunway, movingTailEndZ);

        streamingJudgmentAnchorZ = movingHeadZ;
        // Capture absolute endpoints instead of a length ratio. The hit frame
        // keeps the old upper endpoint exactly where it was; after that, the
        // authored tail continues downward at note speed. This prevents the
        // tail from losing a chunk when a late head is anchored below the line.
        streamingAnchorClipMinZ = oldClipMinZ;
        streamingAnchorWindowTopZ = Mathf.Max(oldClipMinZ, oldClipMaxZ);
        streamingAnchorTailEndZ = movingTailEndZ;
        streamingAnchorConsumeStartMs = visualSongPosMs;
        streamingAnchorLastSongPosMs = streamingAnchorConsumeStartMs;
        streamingAnchorTravelWorld = 0f;
        streamingAnchorVisualEndMs = visualEndTime;
        // The authored end marker always targets the judgment line at endTime,
        // even when the head was accepted early above it.
        streamingAnchorRequiredTailTravelWorld = Mathf.Max(0f,
            streamingAnchorTailEndZ - judgmentZ);
        streamingAnchorTailTravelWorld = 0f;
        streamingJudgmentAnchorActive = true;
        Vector3 position = transform.position;
        position.z = streamingJudgmentAnchorZ;
        transform.position = position;

        // Before judgment, the near edge represents the chart timestamp. Once a
        // Hold/Trill is accepted, its head becomes a stationary contact marker:
        // centre that marker on the now note-thick judgment bar, as in the arcade
        // reference, while the tail continues to be clipped independently.
        if (runtimeSpriteRenderer != null && !isStaccatoCached)
        {
            runtimeSpriteRenderer.transform.localPosition = Vector3.zero;
        }
    }
    
    public void Initialize(NoteData noteData, NoteSpawner spawner, float timeToStartMs)
    {
        visibleTailClipValid = false;
        this.noteData = noteData;
        lastSlideConnectorRatio = -1f;
        lastAppliedGestureVisualStrength = -1f;
        isSoftCached = false;
        isStaccatoCached = false;
        isSlideCached = IsGestureType(noteData, "slide", 4);
        isTrillCached = IsGestureType(noteData, "trill", 64);
        try { isStaccatoCached = IsStaccatoNote(noteData); }
        catch { isStaccatoCached = false; }
        if (!isStaccatoCached)
        {
            try { isSoftCached = IsSoftNote(noteData); }
            catch { isSoftCached = false; }
        }
            if (this.noteData == null)
            {
                // Debug.LogError("NoteController.Initialize called with null noteData. Releasing note to avoid errors.");
                ReleaseOrDestroy();
                return;
            }

            // Defensive reset: clear judgement-related runtime flags to avoid carrying
            // over state from pooled instances (prevents stale isJudged/headMissed etc.).
            try
            {
                isJudged = false;
                softJudgedPendingRelease = false;
                headMissed = false;
                headPressed = false;
                streamingJudgmentAnchorActive = false;
                streamingJudgmentAnchorZ = judgmentZ;
                streamingAnchorClipMinZ = judgmentZ;
                streamingAnchorWindowTopZ = judgmentZ;
                streamingAnchorTailEndZ = judgmentZ;
                streamingAnchorConsumeStartMs = 0f;
                streamingAnchorLastSongPosMs = 0f;
                streamingAnchorTravelWorld = 0f;
                streamingAnchorVisualEndMs = 0f;
                streamingAnchorRequiredTailTravelWorld = 0f;
                streamingAnchorTailTravelWorld = 0f;
                judgmentSuppressed = false;
                lastNoteFanBurstFrame = -1;
                hasTriggeredHitSound = false;
            }
            catch { }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Defensive: if Initialize is called while already initialized, attempt cleanup first
        if (isInitialized)
        {
            //try
            //{
            //    Debug.LogWarning($"[NoteController] Initialize called while already initialized. Cleaning previous state. goId={gameObject.GetInstanceID()} startTime={(this.noteData!=null?this.noteData.startTime:-1)}\nCallStack:\n{System.Environment.StackTrace}");
            //}
            //catch { }
            try { CleanupPooled(); } catch { }
            try { JudgmentManager.Instance?.UnregisterNote(this); } catch { }
            isJudged = false;
            isInitialized = false;
        }
#endif
        // CleanupPooled resets cached type flags. Re-evaluate them after the defensive
        // pooled-instance cleanup so Slide/Trill visuals also work in Editor/Development builds.
        isSoftCached = false;
        isStaccatoCached = false;
        isSlideCached = IsGestureType(noteData, "slide", 4);
        isTrillCached = IsGestureType(noteData, "trill", 64);
        try { isStaccatoCached = IsStaccatoNote(noteData); }
        catch { isStaccatoCached = false; }
        if (!isStaccatoCached)
        {
            try { isSoftCached = IsSoftNote(noteData); }
            catch { isSoftCached = false; }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        //try {
        //    var nd = this.noteData;
        //    if (nd != null)
        //    {
        //        Debug.Log($"[NoteController] Initialize: note startTime={nd.startTime} type={nd.type} lanes={nd.startLane}-{nd.endLane} goId={gameObject.GetInstanceID()}");
        //    }
        //} catch {}
#endif

        try
        {
            // Keep original chart timings; global delay is applied by starting audio later
                startTime = noteData.startTime; 
            endTime = noteData.endTime;
            noteSpawner = spawner;

            // Guard against initialization order: prefer spawner-provided conductor to avoid expensive scene-wide searches
            if (noteSpawner != null)
            {
                try
                {
                    if (noteSpawner.Conductor != null)
                    {
                        conductor = noteSpawner.Conductor;
                    }
                }
                catch
                {
                    // ignore and fallback
                }
            }
            if (conductor == null)
            {
                if (GameManager.Instance != null)
                {
                    conductor = GameManager.Instance.Conductor;
                }
                else
                {
                    // Last resort: expensive lookup - should be rare after the above changes
                    conductor = FindAnyObjectByType<Conductor>();
                }
            }

            float initialSpeed = (noteSpawner != null) ? noteSpawner.speed : 30f;
            float travelSeconds = Mathf.Max(0f, timeToStartMs) / 1000f;
            this.spawnZ = judgmentZ + travelSeconds * initialSpeed;

            // --- Display selection for left/right hand ---
            noteRenderer = GetComponent<Renderer>();
            // Prefer any SpriteRenderer in children so we can ensure a child renderer lies flat on the track
            runtimeSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

            bool hasSprite = (noteData.hand == 0 && rightHandSprite != null) || (noteData.hand != 0 && leftHandSprite != null);
            bool hasTexture = (noteData.hand == 0 && rightHandTexture != null) || (noteData.hand != 0 && leftHandTexture != null);

            if (hasSprite || hasTexture)
            {
                if (noteRenderer != null && !(noteRenderer is SpriteRenderer))
                {
                    noteRenderer.enabled = false;
                }

                if (runtimeSpriteRenderer == null)
                {
                    // Create a child GameObject called RuntimeSprite which will be rotated to lie flat on track
                    try
                    {
                        GameObject child = new GameObject("RuntimeSprite");
                        child.transform.SetParent(transform, false);
                        // Rotate so the sprite's local Y maps to world Z (lay flat)
                        child.transform.localRotation = NoteSpriteRotation();
                        runtimeSpriteRenderer = child.AddComponent<SpriteRenderer>();
                        // Debug.Log($"NoteController.Initialize: Created child SpriteRenderer '{child.name}' for note on GameObject '{gameObject.name}'.");
                    }
                    catch
                    {
                        // Debug.LogWarning($"NoteController.Initialize: Creating child SpriteRenderer failed: {exChild}");
                    }
                }
                else
                {
                    // If the found SpriteRenderer is on the parent object (not a child), move its sprite to a new child rotated to lie flat.
                    if (runtimeSpriteRenderer.gameObject == gameObject)
                    {
                        try
                        {
                            Sprite existingSprite = runtimeSpriteRenderer.sprite;
                            int sortingLayer = runtimeSpriteRenderer.sortingLayerID;
                            int sortingOrder = runtimeSpriteRenderer.sortingOrder;
                            // disable existing renderer to avoid double-draw
                            runtimeSpriteRenderer.enabled = false;
                            GameObject child = new GameObject("RuntimeSprite");
                            child.transform.SetParent(transform, false);
                            child.transform.localRotation = NoteSpriteRotation();
                            var childRenderer = child.AddComponent<SpriteRenderer>();
                            childRenderer.sprite = existingSprite;
                            childRenderer.sortingLayerID = sortingLayer;
                            childRenderer.sortingOrder = sortingOrder;
                            runtimeSpriteRenderer = childRenderer;
                            // Debug.Log("NoteController.Initialize: Moved SpriteRenderer to child and rotated to lie flat.");
                        }
                        catch
                        {
                            // Debug.LogWarning($"NoteController.Initialize: Failed to move parent SpriteRenderer to child: {exMove}");
                        }
                    }
                    else
                    {
                        // Already a child SpriteRenderer; ensure the child is rotated to lie flat
                        runtimeSpriteRenderer.transform.localRotation = NoteSpriteRotation();
                        // Debug.Log($"NoteController.Initialize: Using existing child SpriteRenderer '{runtimeSpriteRenderer.gameObject.name}' and set rotation to lie flat.");
                    }
                }

                if (runtimeSpriteRenderer == null)
                {
                    // Diagnostic dump to help track down why SpriteRenderer cannot be created
                    string compList = "";
                    var comps = GetComponents<Component>();
                    foreach (var c in comps)
                    {
                        if (c == null) continue;
                        compList += c.GetType().Name + ",";
                    }
                    string spawnerName = noteSpawner != null ? noteSpawner.gameObject.name : "(no spawner)";
                    // Debug.LogError($"NoteController.Initialize: failed to create or find SpriteRenderer for note (noteData.hand={(noteData!=null?noteData.hand:-1)}).\nGameObject='{gameObject.name}', Application.isPlaying={Application.isPlaying}, spawner={spawnerName}, components=[{compList}]. Sprite will not be assigned.");
                }
                else
                {
                        try
                        {
                            // Staccato: the ordinary glass frame; the gem inlay marks it
                            // (ApplyStaccatoGem). The old flat staccato sprites are only a
                            // fallback when the frame is missing.
                            if (isStaccatoCached)
                            {
                                if (noteData.hand == 0) // right
                                {
                                    if (rightHandSprite != null) runtimeSpriteRenderer.sprite = rightHandSprite;
                                    else if (rightHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(rightHandTexture);
                                    else if (staccatoRightSprite != null) runtimeSpriteRenderer.sprite = staccatoRightSprite;
                                }
                                else // left
                                {
                                    if (leftHandSprite != null) runtimeSpriteRenderer.sprite = leftHandSprite;
                                    else if (leftHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(leftHandTexture);
                                    else if (staccatoLeftSprite != null) runtimeSpriteRenderer.sprite = staccatoLeftSprite;
                                }
                            }
                            else
                            {
                                if (isSoftCached)
                                {
                                    if (softSprite != null)
                                        runtimeSpriteRenderer.sprite = softSprite;
                                    else if (softTexture != null)
                                        runtimeSpriteRenderer.sprite = GetOrCreateSprite(softTexture);
                                    else
                                    {
                                        // fallback to handed sprites if soft sprite not assigned
                                        if (noteData.hand == 0 && rightHandSprite != null) runtimeSpriteRenderer.sprite = rightHandSprite;
                                        else if (noteData.hand != 0 && leftHandSprite != null) runtimeSpriteRenderer.sprite = leftHandSprite;
                                        else if (noteData.hand == 0 && rightHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(rightHandTexture);
                                        else if (noteData.hand != 0 && leftHandTexture != null) runtimeSpriteRenderer.sprite = GetOrCreateSprite(leftHandTexture);
                                    }
                                }
                                else
                                {
                                    if (noteData.hand == 0) // right
                                    {
                                        if (rightHandSprite != null)
                                            runtimeSpriteRenderer.sprite = rightHandSprite;
                                        else if (rightHandTexture != null)
                                            runtimeSpriteRenderer.sprite = GetOrCreateSprite(rightHandTexture);
                                    }
                                    else // left
                                    {
                                        if (leftHandSprite != null)
                                            runtimeSpriteRenderer.sprite = leftHandSprite;
                                        else if (leftHandTexture != null)
                                            runtimeSpriteRenderer.sprite = GetOrCreateSprite(leftHandTexture);
                                    }
                                }
                            }
                        }
                    catch
                    {
                        // Debug.LogError($"NoteController.Initialize: Exception while assigning sprite: {ex} -- noteData.hand={(noteData!=null?noteData.hand:-1)}");
                    }

                    ApplyRuntimeSpriteTheme();

                    // Copy sorting from existing renderer when possible
                    if (noteRenderer != null)
                    {
                        runtimeSpriteRenderer.sortingLayerID = noteRenderer.sortingLayerID;
                        runtimeSpriteRenderer.sortingOrder = noteRenderer.sortingOrder;
                    }

                    // Ensure the note sprite renders above the JudgmentLine when present.
                    try
                    {
                        if (runtimeSpriteRenderer != null && cachedJudgmentSortingOrder != int.MinValue)
                        {
                            // Align sorting layer to judgment line to make ordering deterministic,
                            // then ensure order is higher than the judgment line's order.
                            runtimeSpriteRenderer.sortingLayerID = cachedJudgmentSortingLayerID;
                            runtimeSpriteRenderer.sortingOrder = Mathf.Max(runtimeSpriteRenderer.sortingOrder, cachedJudgmentSortingOrder + 2);
                        }
                    }
                    catch { }

                    // Also ensure mesh-based renderers (if any) use the same sorting so they render above the judgment line.
                    try
                    {
                        if (noteRenderer != null && runtimeSpriteRenderer != null)
                        {
                            noteRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            noteRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder;
                        }
                        if (holdTailRenderer != null && runtimeSpriteRenderer != null)
                        {
                            holdTailRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            holdTailRenderer.sortingOrder =
                                runtimeSpriteRenderer.sortingOrder + GestureTailOrder;
                        }
                        if (staccatoIndicatorRenderer != null && runtimeSpriteRenderer != null)
                        {
                            staccatoIndicatorRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            staccatoIndicatorRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder;
                        }
                    }
                    catch { }

                    // --- Protective visibility fix for staccato notes ---
                    // Some staccato sprites in authoring may have unexpected alpha/sorting that makes them invisible
                    // (even though they spawn). Apply a conservative, non-destructive fix only for staccato instances
                    // so we can determine if invisibility is due to alpha/sorting issues.
                    try
                    {
            if (isStaccatoCached && runtimeSpriteRenderer != null)
                        {
                            // Ensure the renderer is enabled
                            runtimeSpriteRenderer.enabled = true;

                            // Force opaque alpha if somehow set to transparent
                            try
                            {
                                var col = runtimeSpriteRenderer.color;
                                if (col.a < 0.01f) runtimeSpriteRenderer.color = new Color(col.r, col.g, col.b, 1f);
                            }
                            catch { }

                            // Ensure sprite sorting is above mesh renderer to avoid being occluded
                            try
                            {
                                int meshOrder = 0;
                                if (noteRenderer != null) meshOrder = noteRenderer.sortingOrder;
                                runtimeSpriteRenderer.sortingOrder = Mathf.Max(runtimeSpriteRenderer.sortingOrder, meshOrder + 2);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    #if UNITY_EDITOR || DEVELOPMENT_BUILD
                    // Extra diagnostic for staccato visibility checks
                    try
                    {
                        if (isStaccatoCached)
                        {
                            string sname = (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null) ? runtimeSpriteRenderer.sprite.name : "<none>";
                            var rc = runtimeSpriteRenderer != null ? runtimeSpriteRenderer.color : Color.clear;
                            var rb = runtimeSpriteRenderer != null ? runtimeSpriteRenderer.bounds : new Bounds();
                            BuildLogger.Log($"[NoteController] StaccatoVisibilityCheck: sprite={sname} color={rc} spriteBounds={rb} localScale={transform.localScale}");
                        }
                    }
                    catch { }
                    #endif
                }
            }
            else
            {
                // No sprite/texture provided, fall back to material-based behavior on the existing renderer
                if (noteRenderer != null)
                {
                    if (noteData.hand == 0) // right
                    {
                        if (rightHandMaterial != null)
                            noteRenderer.material = rightHandMaterial;
                    }
                    else
                    {
                        if (leftHandMaterial != null)
                            noteRenderer.material = leftHandMaterial;
                    }
                }
                else
                {
                    // Debug.LogError("Note Prefab is missing a Renderer component!");
                }
            }

            // --- Dynamically calculate position and scale ---
            float trackWidth = PianoVisualLayout.ResolveTrackWidth(
                noteSpawner != null ? noteSpawner.trackTransform : null);
            float width = PianoVisualLayout.ResolveVisualWidth(noteData, trackWidth);
            float positionX = PianoVisualLayout.ResolveCenterX(noteData, trackWidth);

            // If a JudgmentLine exists in scene, align note Y/Z to that line so visuals match popups
            try
            {
                var jlGo = GameObject.Find("JudgmentLine");
                if (jlGo != null)
                        {
                            judgmentLineTransformForNote = jlGo.transform;
                            judgmentZ = judgmentLineTransformForNote.position.z;
                            float jy = judgmentLineTransformForNote.position.y;

                            // Only align note Y to the judgment line if the option is enabled.
                            if (alignToJudgmentLineY)
                            {
                                transform.position = new Vector3(positionX, jy + judgmentYOffset, spawnZ);
                            }
                            else
                            {
                                // Keep existing fallback Y (track or default)
                                float fallbackY = 0.5f;
                                try { if (noteSpawner != null && noteSpawner.trackTransform != null) fallbackY = noteSpawner.trackTransform.position.y; } catch { }
                                transform.position = new Vector3(positionX, fallbackY, spawnZ);
                            }
                            // 弧線是「離這個平面多高」，所以平面高度要記著；
                            // 每幀直接寫 Y（而不是累加），音符才不會愈飄愈高。
                            baseY = transform.position.y;
                            baseX = transform.position.x;
                            hasBaseY = true;

                            // Cache judgment line renderer sorting information (if available)
                            try
                            {
                                var jlRenderer = jlGo.GetComponent<Renderer>();
                                if (jlRenderer != null)
                                {
                                    cachedJudgmentSortingOrder = jlRenderer.sortingOrder;
                                    cachedJudgmentSortingLayerID = jlRenderer.sortingLayerID;
                                }
                            }
                            catch { }
                        }
                else
                {
                    transform.position = new Vector3(positionX, 0.5f, spawnZ);
                }
            }
            catch
            {
                transform.position = new Vector3(positionX, 0.5f, spawnZ);
            }

            parentLossyScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;

            // Determine current world width in a robust way:
            // Prefer the assigned Sprite's intrinsic bounds (sprite.bounds.size.x) multiplied by the current lossyScale,
            // otherwise fall back to renderer.bounds.
            float currentWorldWidth = 1f;
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null)
            {
                // sprite.bounds is in local units at scale=1; multiply by lossyScale.x to get world width
                float spriteLocalWidth = runtimeSpriteRenderer.sprite.bounds.size.x;
                currentWorldWidth = spriteLocalWidth * transform.lossyScale.x;
                if (currentWorldWidth <= 0f)
                {
                    currentWorldWidth = runtimeSpriteRenderer.bounds.size.x > 0f ? runtimeSpriteRenderer.bounds.size.x : 1f;
                }
            }
            else if (noteRenderer != null)
            {
                currentWorldWidth = noteRenderer.bounds.size.x > 0f ? noteRenderer.bounds.size.x : 1f;
            }
            else if (runtimeSpriteRenderer != null)
            {
                currentWorldWidth = runtimeSpriteRenderer.bounds.size.x > 0f ? runtimeSpriteRenderer.bounds.size.x : 1f;
            }

            float desiredWorldWidth = width;
            cachedWorldWidth = Mathf.Max(0.0001f, desiredWorldWidth);
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null)
            {
                // Set an absolute world-space visual size on the rotated child.
                // The scene intentionally gives note roots a non-uniform world scale
                // (notably Z), so scaling the root can never preserve sprite aspect.
                Vector2 authoredSize = runtimeSpriteRenderer.sprite.bounds.size;
                // All note heads use one world-space height. Sprite aspect ratio
                // no longer changes gameplay readability between note types.
                float desiredWorldHeight = ResolveNoteWorldHeight();
                Vector3 noteWorldScale = transform.lossyScale;
                float childScaleX = desiredWorldWidth /
                    Mathf.Max(0.0001f, authoredSize.x * Mathf.Abs(noteWorldScale.x));
                // RuntimeSprite is rotated -90 degrees around X: its local Y maps
                // onto the note parent's Z axis in world space.
                float childScaleY = desiredWorldHeight /
                    Mathf.Max(0.0001f, authoredSize.y * Mathf.Abs(noteWorldScale.z));
                spriteBaseScaleX = childScaleX;
                hasSpriteBaseScaleX = true;
                runtimeSpriteRenderer.transform.localScale =
                    new Vector3(childScaleX * appliedArcLateral, childScaleY, 1f);

                runtimeSpriteRenderer.transform.localRotation = NoteSpriteRotation();

                // The note root represents the chart timestamp. Anchor the near edge to that
                // root so at startTime the visible leading edge, rather than the sprite
                // centre, meets the judgment line. See NoteNearEdgeLocalOffsetZ.
                runtimeSpriteRenderer.transform.localPosition =
                    new Vector3(0f, 0f, NoteNearEdgeLocalOffsetZ());

                ApplyStaccatoGem(desiredWorldWidth, desiredWorldHeight, childScaleX, childScaleY);
            }
            else
            {
                float scaleFactorX = desiredWorldWidth / Mathf.Max(0.0001f, currentWorldWidth);
                Vector3 baseLocal = transform.localScale;
                transform.localScale = new Vector3(
                    baseLocal.x * scaleFactorX,
                    baseLocal.y * scaleFactorX,
                    baseLocal.z);
            }

            // If this is a hold note, create a quad-based tail mesh to visually represent its length
            if (noteData.type == "hold" || isSlideCached || isTrillCached)
            {
                try
                {
                    float speedWorldUnits = (noteSpawner != null) ? noteSpawner.speed : 30f;
                    holdTailAdjustedEndMs = (isSlideCached || isTrillCached)
                        ? Mathf.Max(startTime, endTime)
                        : Mathf.Max(startTime, endTime - tailEndOffsetMs);
                    holdDurationMs = Mathf.Max(1f, holdTailAdjustedEndMs - startTime);
                    float initialLengthWorld = (holdDurationMs / 1000f) * speedWorldUnits;
                    if (initialLengthWorld < minimumHoldTailVisualLength)
                    {
                        initialLengthWorld = minimumHoldTailVisualLength;
                    }
                    // Cover the entire visible runway, rather than beginning a
                    // fixed-size stream halfway down it.  Keep two repeat cells
                    // outside the clipping window so recycling never exposes an
                    // empty frame at either edge.
                    streamingVisibleLengthWorld = Mathf.Max(
                        streamingTailMaximumVisibleLength,
                        Mathf.Abs(spawnZ - judgmentZ));
                    float streamedSpanWorld = Mathf.Min(initialLengthWorld,
                        streamingVisibleLengthWorld);
                    // Two complete runway chunks allow the first to move down
                    // continuously while the following chunk feeds in above it.
                    streamingMeshLengthWorld = streamedSpanWorld * 2f
                        + streamingTailRepeatWorldLength * 2f;

                    EnsureHoldTailVisualExists();
                    if (!isSlideCached && !isTrillCached && holdTailMeshFilter != null)
                        holdTailMeshFilter.sharedMesh = sharedHoldTailQuad;
                    if (holdTailObject != null && holdTailRenderer != null)
                    {
                        holdTailObject.transform.SetParent(this.transform, false);
                        holdTailObject.transform.localPosition = new Vector3(0f, holdTailYOffset, 0f);
                        holdTailObject.transform.localRotation = Quaternion.identity;
                        // 縮放也要歸位。長按跟顫音每幀寫自己的 localScale，滑奏從來不寫
                        // （它的形狀全在網格裡）——而這個子物件是跟著音符被回收重用的。
                        // 上一世是長按的話，這裡還留著 (localWidth, 1, localLength)，滑奏的
                        // 連結段就會被乘上別人的長度與寬度，整段飛到看不見的地方。
                        holdTailObject.transform.localScale = Vector3.one;

                        Texture2D resolvedTexture = null;
                        Material material = isSlideCached
                            ? ResolveSlideBeamMaterial()
                            : ResolveHoldTailMaterial(noteData.hand == 0, out resolvedTexture);
                        if (material != null)
                        {
                            holdTailRenderer.sharedMaterial = material;
                            ApplyHoldTailMaterialProperties(noteData.hand == 0, resolvedTexture);
                        }

                        if (runtimeSpriteRenderer != null)
                        {
                            holdTailRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
                            // Match the note's sorting so the tail is not buried under track/background
                            holdTailRenderer.sortingOrder =
                                runtimeSpriteRenderer.sortingOrder + GestureTailOrder;
                        }
                        else if (noteRenderer != null)
                        {
                            holdTailRenderer.sortingLayerID = noteRenderer.sortingLayerID;
                            holdTailRenderer.sortingOrder =
                                noteRenderer.sortingOrder + GestureTailOrder;
                        }

                        float tailWidthWorld = Mathf.Max(0.01f,
                            width * (IsGlassRodHold ? HoldGlassWidthShare : holdTailWidthFactor));
                        bool showTail = true;
                        if (isSlideCached)
                        {
                            GetOrCreateGestureTailMesh("SlideTrapezoidConnector");
                            showTail = BuildSlideConnectorMesh(
                                trackWidth, width, initialLengthWorld, speedWorldUnits);
                        }
                        else if (isTrillCached)
                        {
                            BuildTrillArrowMesh(width, streamingMeshLengthWorld);
                            EnsureTrillEndCap(initialLengthWorld);
                            gestureVisualStrength = 1f;
                        }
                        else
                        {
                            UpdateHoldTailDimensions(tailWidthWorld, streamingMeshLengthWorld, true);
                        }
                        SetHoldTailActive(showTail);
                    }

                    initialTailLength = initialLengthWorld;
                    currentTailLength = initialTailLength;
                }
                catch
                {
                    SetHoldTailActive(false);
                }
            }

            // Load hit sound once for all notes
            if (cachedHitSound == null)
            {
                cachedHitSound = Resources.Load<AudioClip>("Sound/Tap");
                if (cachedHitSound != null)
                {
                    // Debug.Log("NoteController: Hit sound loaded successfully");
                }
                else
                {
                    // Debug.LogWarning("NoteController: Hit sound not found at Resources/Sound/Tap");
                }
            }

            // Successful initialization
            // Ensure any previous judged/hidden state is cleared when the object is reused from the pool
            isJudged = false;
            softJudgedPendingRelease = false;
            // Re-enable the runtime sprite if present. For the original renderer, only enable it
            // when we're not using a runtime sprite (to avoid double-draw / visible quad behind sprite).
            if (runtimeSpriteRenderer != null)
            {
                runtimeSpriteRenderer.enabled = true;
            }
            if (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null)
            {
                // We're using a sprite for rendering; ensure the original renderer (mesh) stays disabled
                if (noteRenderer != null && !(noteRenderer is SpriteRenderer)) noteRenderer.enabled = false;
            }
            else
            {
                // No runtime sprite assigned: ensure the original renderer is enabled so the note is visible
                if (noteRenderer != null) noteRenderer.enabled = true;
            }
            // 浮在音符上方的斷音紋章。音符正中央的寶石是「這一顆是斷音」的**內
            // 嵌**記號，貼在音符上、跟著音符一起被判掉；紋章是浮在它上方、面向玩
            // 家、上下飄的一塊牌子，遠遠就看得到。兩個各有各的距離。
            if (!PreviewBuildMode) RefreshStaccatoIndicator();
            ParticleEffectPlayer fanParticleController = ParticleEffectPlayer.Instance;
            if (PreviewBuildMode)
            {
                // 檢視器的音符不會被打到，扇形粒子只會去借共用粒子池然後留下殘骸。
                noteFanParticleEmitter?.StopAndClear();
            }
            else if (enableNoteFanParticles || fanParticleController != null)
            {
                if (noteFanParticleEmitter == null)
                    noteFanParticleEmitter = GetComponent<NoteFanParticleEmitter>();
                if (noteFanParticleEmitter == null)
                    noteFanParticleEmitter = gameObject.AddComponent<NoteFanParticleEmitter>();

                Color fanColor = ResolveGestureColor();
                int fanSortingLayer = runtimeSpriteRenderer != null
                    ? runtimeSpriteRenderer.sortingLayerID
                    : (noteRenderer != null ? noteRenderer.sortingLayerID : 0);
                int fanSortingOrder = runtimeSpriteRenderer != null
                    ? runtimeSpriteRenderer.sortingOrder + 1
                    : (noteRenderer != null ? noteRenderer.sortingOrder + 1 : 1);
                noteFanParticleEmitter.Configure(
                    fanParticleController,
                    noteSpawner != null ? noteSpawner.trackTransform : null,
                    judgmentLineTransformForNote,
                    fanColor,
                    desiredWorldWidth,
                    noteFanParticleTexture,
                    noteFanBurstCount,
                    noteFanEmissionRate,
                    noteFanSpeed,
                    noteFanHalfAngle,
                    noteFanLifetime,
                    noteFanParticleSize,
                    fanSortingLayer,
                    fanSortingOrder);
            }
            else
            {
                noteFanParticleEmitter?.StopAndClear();
            }
            if (!PreviewBuildMode) { try { UpdateVelocityHalo(); } catch { } }
            isInitialized = true;
            // Register with JudgmentManager so it can be found when player presses buttons
            try { JudgmentManager.Instance.RegisterNote(this); } catch { }
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                string spriteName = (runtimeSpriteRenderer != null && runtimeSpriteRenderer.sprite != null) ? runtimeSpriteRenderer.sprite.name : "<none>";
                string rendererType = (noteRenderer != null) ? noteRenderer.GetType().Name : "<noRenderer>";
                // extra diagnostics: camera / spawn / timing info to help track invisible notes
                Camera cam = Camera.main;
                string camName = cam != null ? cam.name : "<noMainCam>";
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                float camFar = cam != null ? cam.farClipPlane : -1f;
                float effectiveSongPos = conductor != null ? conductor.effectiveSongPosition : -99999f;
                BuildLogger.Log($"[NoteController] Initialized: goId={gameObject.GetInstanceID()} start={startTime} end={endTime} type={noteData?.type} note_type={(noteData!=null?noteData.note_type:-1)} hand={(noteData!=null?noteData.hand:-1)} sprite={spriteName} renderer={rendererType} pos={transform.position} judgmentZ={judgmentZ} spawnZ={spawnZ} travelSeconds={(timeToStartMs/1000f):F3} initialSpeed={((noteSpawner!=null)?noteSpawner.speed:30f):F1} effectiveSongPos={effectiveSongPos} mainCam={camName} camPos={camPos} camFar={camFar}");
                if (runtimeSpriteRenderer != null)
                {
                    try { BuildLogger.Log($"[NoteController] SpriteRenderer: enabled={runtimeSpriteRenderer.enabled} sortingLayer={runtimeSpriteRenderer.sortingLayerID} order={runtimeSpriteRenderer.sortingOrder} bounds={runtimeSpriteRenderer.bounds}"); } catch { }
                }
                if (noteRenderer != null)
                {
                    try { BuildLogger.Log($"[NoteController] MeshRenderer: enabled={noteRenderer.enabled} bounds={noteRenderer.bounds}"); } catch { }
                }
            }
            catch { }
            #endif
        }
        catch
        {
            // Debug.LogError($"NoteController.Initialize: Exception caught during Initialize: {ex}\nNote data: {noteData}");
            isInitialized = false;
            return;
        }
    }

    // Called by pool when object is returned; cleans up transient children (holdTail) and resets state
    public void CleanupPooled()
    {
        noteFanParticleEmitter?.StopAndClear();
        try
        {
                    if (holdTailObject != null)
                    {
                        SetHoldTailActive(false);
                        holdTailObject.transform.localScale = Vector3.one;
                        holdTailObject.transform.localPosition = Vector3.zero;
                        holdTailObject.transform.SetParent(transform, false);
                    }
                    if (gestureEndCapObject != null) gestureEndCapObject.SetActive(false);
        }
        catch
        {
            // Debug.LogWarning($"CleanupPooled: exception while cleaning tail: {ex}");
        }

    // Reset runtime state
    isInitialized = false;
    hasTriggeredHitSound = false;
    noteData = null;
    // reset cached soft flag
    isSoftCached = false;
    isStaccatoCached = false;
    isSlideCached = false;
    isTrillCached = false;
    softJudgedPendingRelease = false;
    noteSpawner = null;
    cachedWorldWidth = 1f;
    appliedArcLateral = 1f;
    hasSpriteBaseScaleX = false;
        // Reset cached tail length so next Initialize will reapply positions
        lastAppliedTailLength = -1f;
        currentTailLength = 0f;
        initialTailLength = 0f;
        currentTailWidthWorld = 0f;
        holdDurationMs = 0f;
        holdTailAdjustedEndMs = 0f;
        streamingJudgmentAnchorActive = false;
        streamingJudgmentAnchorZ = 0f;
        streamingAnchorClipMinZ = 0f;
        streamingAnchorWindowTopZ = 0f;
        streamingAnchorTailEndZ = 0f;
        streamingAnchorConsumeStartMs = 0f;
        streamingAnchorLastSongPosMs = 0f;
        streamingAnchorTravelWorld = 0f;
        streamingAnchorVisualEndMs = 0f;
        streamingAnchorRequiredTailTravelWorld = 0f;
        streamingAnchorTailTravelWorld = 0f;
        gestureVisualStrength = 1f;
        lastAppliedGestureVisualStrength = -1f;
        lastSlideConnectorRatio = -1f;
        isStaticPreview = false;
        slideFarCenterOffsetWorld = 0f;
        slideNearWidthWorld = 0f;
        slideFarWidthWorld = 0f;
        slideConnectorLengthWorld = 0f;
        slideConnectorSpanMs = 0f;
        streamingMeshLengthWorld = 0f;
        trillArrowRepeatWorldLength = 0f;
        streamingVisibleLengthWorld = 0f;
    // Clear judged flag so pooled notes become active/visible when respawned
    isJudged = false;
        judgmentSuppressed = false;
        // unregister from JudgmentManager
        try { JudgmentManager.Instance.UnregisterNote(this); } catch { }
        HideStaccatoIndicator();
    }

    // Helper to release via PooledObject if available, otherwise destroy
    public void ReleaseOrDestroy()
    {
        // mark as not active for judgment
        isJudged = true;
        HideStaccatoIndicator();
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            // Try to get current songPos for diagnostic
            float songPosNow = float.NaN;
            try
            {
                if (noteData != null) startTime = noteData.startTime;
                var c = conductor != null ? conductor : (GameManager.Instance != null ? GameManager.Instance.Conductor : null);
                if (c != null) songPosNow = c.effectiveSongPosition;
            }
            catch { }

            string stack = System.Environment.StackTrace;
            // Debug.Log($"[NoteController] ReleaseOrDestroy: goId={gameObject.GetInstanceID()} isJudged={isJudged} owningPool={(owningPool!=null?"yes":"no")} songPosNow={songPosNow} startTime={startTime} posZ={transform.position.z} judgmentZ={judgmentZ}\nCallStack:\n{stack}");
        }
        catch { }
        #endif
        if (owningPool != null)
        {
            owningPool.Despawn(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Called by JudgmentManager when this note is judged
    /// <summary>
    /// Raised once per note, the moment it is judged.
    /// </summary>
    /// <remarks>
    /// Every judgment path -- current, legacy and compatibility managers alike --
    /// funnels through <see cref="OnJudged"/>, so this is the one place a visual
    /// can subscribe to "a note was just played" without having to know which
    /// manager is in charge.
    /// </remarks>
    public static event System.Action<NoteData, JudgmentResult> Judged;

    public void OnJudged(JudgmentResult result)
    {
        if (isJudged) return;
        if (noteData != null)
        {
            try { Judged?.Invoke(noteData, result); } catch { }
        }

        // Final fallback shared by the current, legacy and compatibility
        // managers. PlayHitEffect normally gets here first; the frame guard
        // above prevents that route from producing a duplicate burst.
        if (result == JudgmentResult.Perfect || result == JudgmentResult.Great || result == JudgmentResult.Good)
        {
            try { PlayJudgmentLineFanParticles(false); } catch { }
        }

        isJudged = true;
        if (isSoftCached)
        {
            // For soft notes, keep them visible and release when touching judgment line.
            softJudgedPendingRelease = true;
            return;
        }
        // hide visuals (sound is managed centrally by JudgmentManager)
        if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false;
        if (noteRenderer != null) noteRenderer.enabled = false;
        SetHoldTailActive(false);
        HideStaccatoIndicator();
        // Immediately release
        ReleaseOrDestroy();
    }

    // Called when a hold note is pressed at its head. Do not release the note here; keep it active until hold end.
    /// <summary>Raised the moment a hold's head is pressed.</summary>
    public static event System.Action<NoteData, JudgmentResult> HoldStarted;

    public void OnHoldStart(JudgmentResult startResult)
    {
        if (noteData != null)
        {
            try { HoldStarted?.Invoke(noteData, startResult); } catch { }
        }
        headPressed = true;
        // Do not mark as fully judged; play hit sound and ensure tail is visible
        try
        {
            // sound is played centrally by JudgmentManager; mark triggered to avoid any local replays
            hasTriggeredHitSound = true;
        }
        catch { }

        try
        {
            if (isStaccatoCached)
            {
                SetHoldTailActive(false);
                HideStaccatoIndicator();
            }
            else
            {
                SetHoldTailActive(true);
            }
        }
        catch { }

        if (isStaccatoCached)
        {
            try { if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false; } catch { }
            try { if (noteRenderer != null) noteRenderer.enabled = false; } catch { }
            HideStaccatoIndicator();
        }
    }

    // Called when the player releases a hold or the hold naturally ends. This finalizes visuals and releases the note.
    /// <summary>
    /// Raised when a hold finishes.  Holds never reach <see cref="OnJudged"/>:
    /// the manager's hold-end branch calls this and returns, so anything
    /// listening only to Judged silently never sees a long note.
    /// </summary>
    public static event System.Action<NoteData, JudgmentResult> HoldFinished;

    public void OnHoldEnd(JudgmentResult endResult)
    {
        if (isJudged) return;
        if (noteData != null)
        {
            try { HoldFinished?.Invoke(noteData, endResult); } catch { }
        }
        headPressed = false;
        // Tail/end judgement should NOT play a hit sound (head already played on start).
        // Removed PlayHitSound() here to avoid duplicate/undesired audio on release.

        if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false;
        if (noteRenderer != null) noteRenderer.enabled = false;
        SetHoldTailActive(false);
        HideStaccatoIndicator();
        ReleaseOrDestroy();
    }

    void Update()
    {
    if (!isInitialized) return;

    // Camera/layout settings can reposition the judgment line after pooled notes have
    // initialized.  Keep every not-yet-judged head aimed at the line players actually see.
    if (!isJudged && !headPressed && judgmentLineTransformForNote != null)
        judgmentZ = judgmentLineTransformForNote.position.z;
    // Defensive: if noteData becomes null for any reason while marked initialized,
    // avoid NullReferenceExceptions and try to recover by releasing the object.
    if (noteData == null)
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try { BuildLogger.LogWarning($"[NoteController] Update: noteData is null for goId={gameObject.GetInstanceID()} - releasing to avoid NRE"); } catch { }
        #endif
        // Mark judged so it won't be considered active, then release to pool/destroy.
        isJudged = true;
        try { ReleaseOrDestroy(); } catch { }
        return;
    }
    UpdateHoldTailFlow();
    // Try to ensure we have a conductor reference; do the expensive Find only once per instance.
    if (conductor == null && !didTryFindConductor)
    {
        didTryFindConductor = true;
        if (GameManager.Instance != null)
            conductor = GameManager.Instance.Conductor;
        else
            conductor = FindAnyObjectByType<Conductor>();
    }

        float songPos = (conductor != null) ? conductor.effectiveSongPosition : 0f;
        float visualSongPos = (conductor != null) ? conductor.renderSongPosition : songPos;
        float currentSpeed = (noteSpawner != null) ? noteSpawner.speed : 30f;

        // Soft-only delayed disappearance:
        // judged soft notes stay visible until they hit the judgment line.
        if (isSoftCached && isJudged && softJudgedPendingRelease)
        {
            float timeToStartSoft = startTime - visualSongPos;
            float targetZSoft = judgmentZ + (timeToStartSoft / 1000f) * currentSpeed;
            Vector3 posSoft = transform.position;
            posSoft.z = FollowDspVisualTarget(posSoft.z, targetZSoft, currentSpeed);
            if (Mathf.Abs(targetZSoft - posSoft.z) <= 0.0001f) posSoft.z = targetZSoft;
            transform.position = posSoft;

            bool reachedByTime = songPos >= startTime;
            bool reachedByPos = Mathf.Abs(posSoft.z - judgmentZ) <= 0.0005f;
            if (reachedByTime || reachedByPos)
            {
                if (runtimeSpriteRenderer != null) runtimeSpriteRenderer.enabled = false;
                if (noteRenderer != null) noteRenderer.enabled = false;
                SetHoldTailActive(false);
                HideStaccatoIndicator();
                softJudgedPendingRelease = false;
                TryReleaseOrDestroy("SoftJudged reached judgment line", songPos, currentSpeed);
            }
            return;
        }

        // Cache settings once per Update — avoids N×SettingsManager.Instance reads per active note per frame
        var _sm = SettingsManager.Instance;
        // 教學示範段的音符就是這一顆的自動演奏，其餘音符照玩家設定。
        bool _debugMode = (_sm != null && _sm.DebugModeInPlay) || IsTutorialDemo;
        bool _judgmentMode = _sm != null && _sm.JudgmentModeInPlay;
        // Judgment clock: player-facing timing (hit AND miss) runs on effectiveSongPosition + judgment
        // offset, matching JudgmentManager.GetSongPositionMs(). Visual note movement below stays on the
        // raw songPos so notes still align with the audio — ONLY the miss/fail timeout checks use this,
        // otherwise the offset the player sets would have no effect on auto-miss and desync from input.
        float judgmentPos = songPos + (_sm != null ? _sm.JudgmentOffsetMs : 0f);
        if (judgmentSuppressed)
        {
            // Suppressed notes should keep their travel/visual timing but never produce
            // head/tail/miss judgments, so treat them like non-judgment-mode visuals.
            _debugMode = false;
            _judgmentMode = false;
        }

        // --- Debug Mode Auto-Play Logic ---

    if (isTrillCached)
        {
            UpdateStreamingLongVisual(visualSongPos, currentSpeed);
        }
    else if (isSlideCached)
        {
            // Gesture heads travel like TAP notes, then stay on the judgment
            // line while their short ribbon is being checked. JudgmentManager
            // owns completion so the note cannot time out as an ordinary tap.
            if (visualSongPos < startTime)
            {
                float timeToStartGesture = startTime - visualSongPos;
                float targetZGesture = judgmentZ + (timeToStartGesture / 1000f) * currentSpeed;
                Vector3 gesturePos = transform.position;
                gesturePos.z = FollowDspVisualTarget(
                    gesturePos.z, targetZGesture, currentSpeed);
                ApplyArcToHead(ref gesturePos);
                transform.position = gesturePos;
                if (holdTailObject != null)
                {
                    currentTailLength = initialTailLength;
                    if (isSlideCached) UpdateSlideConnectorMesh(1f);
                    else
                    {
                        holdTailObject.transform.localScale = Vector3.one;
                        if (gestureEndCapObject != null)
                        {
                            float endZ = initialTailLength / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
                            gestureEndCapObject.transform.localPosition =
                                new Vector3(0f, 0.015f, endZ + NoteNearEdgeLocalOffsetZ());
                        }
                    }
                    SetHoldTailActive(currentTailLength > 0.0001f && (!isSlideCached || noteData.param2 >= 0));
                }
            }
            else
            {
                Vector3 gesturePos = transform.position;
                gesturePos.z = judgmentZ;
                // 停在線上的時候也要重算，不然它會留著上一幀的弧線狀態。
                ApplyArcToHead(ref gesturePos);
                transform.position = gesturePos;
                float remaining = Mathf.Max(0f, holdTailAdjustedEndMs - visualSongPos);
                float ratio = holdDurationMs > 0f ? Mathf.Clamp01(remaining / holdDurationMs) : 0f;
                currentTailLength = initialTailLength * ratio;
                if (isSlideCached)
                {
                    UpdateSlideConnectorMesh(ratio);
                }
                else if (holdTailObject != null)
                {
                    holdTailObject.transform.localScale = new Vector3(1f, 1f, Mathf.Max(0.0001f, ratio));
                    if (gestureEndCapObject != null)
                    {
                        float endZ = initialTailLength * ratio /
                            Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
                        gestureEndCapObject.transform.localPosition =
                            new Vector3(0f, 0.015f, endZ + NoteNearEdgeLocalOffsetZ());
                    }
                }
                SetHoldTailActive(currentTailLength > 0.0001f && (!isSlideCached || noteData.param2 >= 0));
            }
            // 位置定了、SetHoldTailActive（會 Clear 整個 block）也跑完了，最後才送
            // 弧線的表——順序反過來就等於沒送。
            ApplyTailArcBlock();
        }
    else if (noteData.type == "tap" || isSoftCached || isStaccatoCached)
        {
            // If judgment mode is enabled, do not auto-trigger on reaching startTime.
            // Instead wait for player input; if the note passes beyond the 'good' window, count as fail.
            // If we are in normal JudgmentMode (player must press), enforce miss window.
            // If DebugMode is enabled, auto-judge as Perfect at startTime.
            if (_judgmentMode && !_debugMode)
            {
                if (judgmentPos - startTime > (JudgmentManager.Instance != null ? JudgmentManager.Instance.goodMs : 150))
                {
                    // note missed (failed)
                    try { JudgmentManager.Instance.RecordFail(this); } catch { }
                        TryReleaseOrDestroy("MissTimeout: tap missed window", songPos, currentSpeed);
                    return;
                }
            }
            else
            {
                // Auto path: either not in JudgmentMode, or DebugMode enabled -> auto-trigger behavior
                if (songPos >= startTime)
                {
                    // In DebugMode we want an actual Perfect judgement (so counts and popups are emitted)
                        if (_debugMode)
                        {
                            // Snap head exactly to judgment line so disappearance corresponds to on-line visual
                            transform.position = new Vector3(transform.position.x, transform.position.y, judgmentZ);
                                    // Ensure visuals reflect being on the line; sound is handled by JudgmentManager
                                    if (!hasTriggeredHitSound) { hasTriggeredHitSound = true; }
                            try {
                                // For staccato notes in DebugMode, simulate a hold start so the manager
                                // will auto-finalize the tail at endTime. For normal taps, keep AutoJudgeTap.
                                if (isStaccatoCached)
                                {
                                    if (!headPressed)
                                    {
                                        JudgmentManager.Instance.AutoStartHold(this, startTime);
                                        headPressed = true; // mark as pressed so hold-logic applies
                                    }
                                }
                                else
                                {
                                    JudgmentManager.Instance.AutoJudgeTap(this);
                                }
                            } catch { }
                            return;
                        }

                    // Non-judgment-mode: previous behavior (snap & release)
                    if (!hasTriggeredHitSound)
                    {
                        // sound is handled centrally by JudgmentManager in judgment flows
                        hasTriggeredHitSound = true;
                    }
                    transform.position = new Vector3(transform.position.x, transform.position.y, judgmentZ);
                    TryReleaseOrDestroy("AutoSnap: tap reached startTime (non-judgment)", songPos, currentSpeed);
                    return;
                }
            }

            // Place directly on the shared DSP-derived visual target.
            float timeToStart = startTime - visualSongPos;
            float targetZ = judgmentZ + (timeToStart / 1000f) * currentSpeed;
            if (targetZ < judgmentZ)
            {
                // Crossing still happens at the exact chart time. Only the visual
                // overshoot is compressed, so late notes visibly decelerate without
                // widening or shifting any judgment window.
                targetZ = judgmentZ + (targetZ - judgmentZ) * postJudgmentVisualSpeed;
            }
            Vector3 pos = transform.position;
            pos.z = FollowDspVisualTarget(pos.z, targetZ, currentSpeed);
            // Snap if we are within an imperceptible range to guarantee precise alignment.
            if (Mathf.Abs(targetZ - pos.z) <= 0.0001f) pos.z = targetZ;
            // 本家的拋物線：Z（＝時間）完全不動，只有高度跟著弧線走，
            // 所以拍子線、判定窗、可讀性都和原本一樣。
            ApplyArcToHead(ref pos);
            if (hasBaseY) UpdateSpriteTilt(LeadingEdgeZ(pos.z));
            transform.position = pos;
        }
    else if (noteData.type == "hold")
        {
            // Do NOT immediately destroy when songPos passes endTime. Instead, allow the tail to
            // smoothly shrink to zero, then destroy the GameObject once the tail is effectively gone.
                if (songPos < startTime)
            {
                UpdateStreamingLongVisual(visualSongPos, currentSpeed);

            }
            else
            {
                bool debugModeActive = _debugMode;
                bool judgmentModeActive = _judgmentMode;

                // If DebugMode is enabled and we just reached the startTime, ensure the hold is auto-started
                if (debugModeActive && !headPressed)
                {
                    // start as if pressed at startTime so auto flow always runs before miss logic
                    try { JudgmentManager.Instance.AutoStartHold(this, startTime); } catch { }
                    headPressed = true; // mark so we don't re-trigger
                }

                // If the player hasn't pressed the head within the allowed judgment window (after start), mark as missed
                try
                {
                    if (!headPressed && !headMissed && (judgmentModeActive || debugModeActive))
                    {
                        float allowedMs = (JudgmentManager.Instance != null) ? JudgmentManager.Instance.goodMs : 150f;
                        if (judgmentPos - startTime > allowedMs)
                        {
                            headMissed = true;
                            // immediately avoid snapping to judgment line; we'll let visual continue flowing
                            // show a Miss popup for the head immediately so start-judgement has an icon
                            try
                            {
                                var simple = SimpleJudgePopupManager.Instance;
                                if (simple != null) simple.ShowAtPosition(this.transform.position, JudgmentResult.Miss);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                // Trigger hit sound when hold note starts
                // If the head was missed, do not snap to judgment line nor play hit sound;
                // instead let the note continue flowing past the judgment line as if unjudged.
                if (!headMissed)
                {
                    if (!hasTriggeredHitSound)
                    {
                        // Head sound is managed centrally by JudgmentManager; mark as triggered locally
                        hasTriggeredHitSound = true;
                    }
                }

                // The long-note stream keeps moving with the chart. Geometry below
                // the judgment line and beyond the visible runway is clipped by shader;
                // no endpoint is scaled inward and no very long mesh is created.
                UpdateStreamingLongVisual(visualSongPos, currentSpeed);
                // If we've passed or reached the endTime
                if (songPos >= endTime)
                {
                    if (headMissed)
                    {
                        // For non-debug JudgmentMode: do NOT locally release here. Defer finalization
                        // to JudgmentManager (which uses endTime + goodMs grace) so the player can
                        // re-press and recover; do not destroy the note locally.
                        // Reuse outer scope variables debugModeActive/judgmentModeActive
                        if (judgmentModeActive && !debugModeActive)
                        {
                            // Defer release until manager's grace window expires; but if we've
                            // already passed the grace window locally, release here to avoid
                            // stuck notes when no manager state exists.
                            float grace = (JudgmentManager.Instance != null) ? JudgmentManager.Instance.goodMs : 150f;
                            if (judgmentPos - endTime > grace)
                            {
                                // Head was missed and the player never recovered the hold within the grace
                                // window. The manager has no hold-state for a never-pressed hold, so it will
                                // NOT finalize this note itself — without a RecordFail here the entire hold
                                // would score nothing and leave the player's combo intact. Route a real Miss
                                // (mirrors the tap miss-timeout path). RecordFail also shows the Miss popup,
                                // so we no longer pop one manually.
                                try { JudgmentManager.Instance.RecordFail(this); } catch { }
                                TryReleaseOrDestroy("Hold end: headMissed (grace expired)", songPos, currentSpeed);
                                return;
                            }
                            // otherwise defer to manager
                            return;
                        }
                        else
                        {
                            // Legacy behavior: show end Miss popup then release immediately
                            try
                            {
                                var simple = SimpleJudgePopupManager.Instance;
                                if (simple != null) simple.ShowAtPosition(this.transform.position, JudgmentResult.Miss);
                            }
                            catch { }
                            TryReleaseOrDestroy("Hold end: headMissed", songPos, currentSpeed);
                            return;
                        }
                    }
                    if (debugModeActive)
                    {
                        // In DebugMode let JudgmentManager's auto-finalize logic handle the tail; no local fail needed
                        return;
                    }
                    if (judgmentModeActive)
                    {
                        // Only auto-fail a hold whose head was NEVER pressed. A pressed hold (headPressed==true)
                        // is owned by JudgmentManager: FinalizeExpiredHolds judges its tail at this SAME
                        // endTime+goodMs deadline and then releases the note via OnHoldEnd. Calling RecordFail
                        // here as well double-judges the note and — because both deadlines are now identical —
                        // turns a correctly-held hold into a Miss purely based on Unity's (unspecified) script
                        // execution order. Reaching this branch already implies headMissed==false, so a true
                        // condition here historically always meant headPressed==true; the guard makes that explicit.
                        if (!headPressed && judgmentPos - endTime > (JudgmentManager.Instance != null ? JudgmentManager.Instance.goodMs : 150))
                        {
                            try { JudgmentManager.Instance.RecordFail(this); } catch { }
                            TryReleaseOrDestroy("Hold end: fail timeout", songPos, currentSpeed);
                            return;
                        }
                        // otherwise keep head at judgment line and let the manager finalize the tail
                    }
                    else
                    {
                        TryReleaseOrDestroy("Hold end: non-judgment immediate release", songPos, currentSpeed);
                        return;
                    }
                }
            }
        }

        // Failsafe to destroy notes that are long past
        if (songPos > endTime + 500)
        {
            TryReleaseOrDestroy("Failsafe: past endTime+500", songPos, currentSpeed);
        }
    }

    private static Sprite LoadStaccatoGemSprite(string resourcePath)
    {
        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null) return null;
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), texture.width);
    }

    /// <summary>
    /// Sets the gem inlay in the centre of a staccato head. The gem is a child of
    /// RuntimeSprite, so it inherits the flat orientation and the near-edge offset;
    /// its own scale undoes the frame's non-uniform stretch so it stays round.
    /// </summary>
    private void ApplyStaccatoGem(float noteWorldWidth, float noteWorldHeight,
        float frameScaleX, float frameScaleY)
    {
        if (!isStaccatoCached || runtimeSpriteRenderer == null)
        {
            if (staccatoGemRenderer != null) staccatoGemRenderer.enabled = false;
            return;
        }

        bool isRight = noteData != null && noteData.hand == 0;
        if (isRight && staccatoGemRightSprite == null)
            staccatoGemRightSprite = LoadStaccatoGemSprite("graphic/StaccatoGem_Red");
        if (!isRight && staccatoGemLeftSprite == null)
            staccatoGemLeftSprite = LoadStaccatoGemSprite("graphic/StaccatoGem_Blue");
        Sprite gem = isRight ? staccatoGemRightSprite : staccatoGemLeftSprite;
        if (gem == null)
        {
            if (staccatoGemRenderer != null) staccatoGemRenderer.enabled = false;
            return;
        }

        if (staccatoGemRenderer == null)
        {
            var go = new GameObject("StaccatoGem");
            staccatoGemRenderer = go.AddComponent<SpriteRenderer>();
        }
        Transform gemTransform = staccatoGemRenderer.transform;
        if (gemTransform.parent != runtimeSpriteRenderer.transform)
            gemTransform.SetParent(runtimeSpriteRenderer.transform, false);

        if (!staccatoGemMaterialResolved)
        {
            staccatoGemMaterialResolved = true;
            // Resources/Shaders/StaccatoGem.shader: solid over the additive frame and
            // HDR on the highlights. Without it the gem drowns in the frame's bloom.
            Shader gemShader = Shader.Find("Custom/StaccatoGem");
            if (gemShader != null)
                staccatoGemMaterial = new Material(gemShader)
                {
                    name = "Staccato Gem (Runtime)",
                    hideFlags = HideFlags.DontSave
                };
        }
        if (staccatoGemMaterial != null && staccatoGemRenderer.sharedMaterial != staccatoGemMaterial)
            staccatoGemRenderer.sharedMaterial = staccatoGemMaterial;
        staccatoGemRenderer.sprite = gem;
        // Frame local +Y runs toward the player (bottom of the screen), so the
        // texture's top-left light would land bottom-left. Flip it back.
        staccatoGemRenderer.flipY = true;
        staccatoGemRenderer.color = Color.white;
        staccatoGemRenderer.sortingLayerID = runtimeSpriteRenderer.sortingLayerID;
        staccatoGemRenderer.sortingOrder = runtimeSpriteRenderer.sortingOrder + 1;

        float diameter = Mathf.Clamp(noteWorldWidth * staccatoGemWidthRatio,
            noteWorldHeight * staccatoGemMinHeightRatio,
            noteWorldHeight * Mathf.Max(staccatoGemMinHeightRatio, staccatoGemMaxHeightRatio));
        Vector3 rootScale = transform.lossyScale;
        Vector2 gemSize = gem.bounds.size;
        // Frame local X -> root X, frame local Y -> root Z (see the RuntimeSprite scale).
        float localX = diameter / Mathf.Max(0.0001f,
            gemSize.x * Mathf.Abs(frameScaleX) * Mathf.Abs(rootScale.x));
        float localY = diameter / Mathf.Max(0.0001f,
            gemSize.y * Mathf.Abs(frameScaleY) * Mathf.Abs(rootScale.z));
        gemTransform.localScale = new Vector3(localX, localY, 1f);
        gemTransform.localRotation = Quaternion.identity;
        // Frame local +Z points up (world +Y). Lift a hair so the inlay never
        // z-fights the frame or dips under the track surface.
        gemTransform.localPosition = new Vector3(0f, 0f,
            0.01f / Mathf.Max(0.0001f, Mathf.Abs(rootScale.y)));
        staccatoGemRenderer.enabled = runtimeSpriteRenderer.enabled;
    }

    private void LateUpdate()
    {
        // The frame is hidden from many places (judged, hold start, head clipping);
        // the gem just follows whatever the frame ended up as this frame.
        if (staccatoGemRenderer == null) return;
        bool show = isInitialized && isStaccatoCached && runtimeSpriteRenderer != null &&
            runtimeSpriteRenderer.enabled && staccatoGemRenderer.sprite != null;
        if (staccatoGemRenderer.enabled != show) staccatoGemRenderer.enabled = show;
    }

    private void RefreshStaccatoIndicator()
    {
        if (!isStaccatoCached || noteData == null)
        {
            HideStaccatoIndicator();
            return;
        }

        // 紅是右手、藍是左手 —— 和音符本身同一套顏色。看 NoteData.hand，不是看鍵
        // 道：交叉手的時候鍵道會猜錯。
        bool rightHand = true;
        try { rightHand = noteData.hand == 0; }
        catch { rightHand = true; }

        Sprite indicatorSprite = rightHand ? rightStaccatoIndicatorSprite : leftStaccatoIndicatorSprite;
        if (indicatorSprite == null)
        {
            // prefab 沒指定（或指到了已經換掉的舊圖）就自己載。寶石也是這樣載的。
            if (rightHand && staccatoMarkRightSprite == null)
                staccatoMarkRightSprite = LoadStaccatoGemSprite("graphic/StaccatoMark_Red");
            if (!rightHand && staccatoMarkLeftSprite == null)
                staccatoMarkLeftSprite = LoadStaccatoGemSprite("graphic/StaccatoMark_Blue");
            indicatorSprite = rightHand ? staccatoMarkRightSprite : staccatoMarkLeftSprite;
        }

        if (indicatorSprite == null)
        {
            HideStaccatoIndicator();
            return;
        }

        if (staccatoIndicatorInstance == null)
        {
            staccatoIndicatorInstance = new GameObject("StaccatoIndicator");
            staccatoIndicatorInstance.transform.SetParent(GetStaccatoIndicatorRoot(), false);
            staccatoIndicatorRenderer = staccatoIndicatorInstance.AddComponent<SpriteRenderer>();
            staccatoIndicatorController = staccatoIndicatorInstance.AddComponent<StaccatoIndicatorBillboard>();
        }
        else
        {
            if (staccatoIndicatorRenderer == null)
            {
                staccatoIndicatorRenderer = staccatoIndicatorInstance.GetComponent<SpriteRenderer>();
                if (staccatoIndicatorRenderer == null)
                {
                    staccatoIndicatorRenderer = staccatoIndicatorInstance.AddComponent<SpriteRenderer>();
                }
            }

            if (staccatoIndicatorController == null)
            {
                staccatoIndicatorController = staccatoIndicatorInstance.GetComponent<StaccatoIndicatorBillboard>();
                if (staccatoIndicatorController == null)
                {
                    staccatoIndicatorController = staccatoIndicatorInstance.AddComponent<StaccatoIndicatorBillboard>();
                }
            }
        }

        // Keep the indicator outside the Note root's non-uniform scale. A
        // camera-facing child under that transform is sheared and appears to
        // lie on the track even when its world rotation is camera-aligned.
        Transform indicatorRoot = GetStaccatoIndicatorRoot();
        if (staccatoIndicatorInstance.transform.parent != indicatorRoot)
        {
            staccatoIndicatorInstance.transform.SetParent(indicatorRoot, true);
        }

        if (staccatoIndicatorRenderer == null)
        {
            HideStaccatoIndicator();
            return;
        }

        staccatoIndicatorRenderer.sprite = indicatorSprite;
        staccatoIndicatorRenderer.enabled = true;
        staccatoIndicatorRenderer.color = Color.white;

        int sortingLayerId = 0;
        int sortingOrder = 0;
        if (runtimeSpriteRenderer != null)
        {
            sortingLayerId = runtimeSpriteRenderer.sortingLayerID;
            sortingOrder = runtimeSpriteRenderer.sortingOrder;
        }
        else if (noteRenderer != null)
        {
            sortingLayerId = noteRenderer.sortingLayerID;
            sortingOrder = noteRenderer.sortingOrder;
        }

        staccatoIndicatorRenderer.sortingLayerID = sortingLayerId;
        staccatoIndicatorRenderer.sortingOrder = sortingOrder + 5;

        Vector3 parentScale = indicatorRoot != null ? indicatorRoot.lossyScale : Vector3.one;
        float noteWorldWidth = Mathf.Max(0.0001f, cachedWorldWidth);
        float widthScaleFactor = Mathf.Max(0.01f, staccatoIndicatorScale);

        // 尺寸以**一個鍵道**為準，不是以音符自己的寬度。
        //
        // 照音符寬度算的話，三鍵寬的音符會得到三倍大的紋章 —— 而紋章講的是「這是
        // 斷音」，那件事和音符佔幾個鍵道完全無關。同一件事在畫面上有三種大小，讀
        // 到的會是「大的那個比較重要」。
        //
        // 88 鍵模式下一顆音符本來就只佔一個鍵，不必再除。
        int laneSpan = 1;
        if (noteData != null && !PianoVisualLayout.HasPianoPitch(noteData))
            laneSpan = Mathf.Max(1, Mathf.Abs(noteData.endLane - noteData.startLane) + 1);
        float singleLaneWidth = noteWorldWidth / laneSpan;
        float baseWidth = Mathf.Max(0.0001f, singleLaneWidth * widthScaleFactor);

        // 寬度也是統一的：**每一顆斷音的紋章都一模一樣大**，不管音符佔幾個鍵道。
        // 加寬 50% 是套在所有人身上的一個固定比例，不是給寬音符的特例 —— 紋章因
        // 此比基準寬一點、扁一點，一排排下來的輪廓才整齊。
        float targetWorldWidth = baseWidth * StaccatoMarkWiden;

        float spriteWidthUnits = 1f;
        float spriteHeightUnits = 1f;
        if (staccatoIndicatorRenderer.sprite != null)
        {
            var spriteBounds = staccatoIndicatorRenderer.sprite.bounds;
            spriteWidthUnits = Mathf.Max(0.0001f, spriteBounds.size.x);
            spriteHeightUnits = Mathf.Max(0.0001f, spriteBounds.size.y);
        }

        // 高度用**沒有加寬**的基準寬度算，所以每一顆斷音的紋章都一樣高 —— 加寬
        // 只往兩側長。高度統一，一整排斷音的紋章才會排在同一條線上。
        float targetWorldHeight = Mathf.Max(0.01f,
            baseWidth * (spriteHeightUnits / spriteWidthUnits));

        float parentScaleX = Mathf.Max(0.0001f, Mathf.Abs(parentScale.x));
        float parentScaleY = Mathf.Max(0.0001f, Mathf.Abs(parentScale.y));
        float parentScaleZ = Mathf.Max(0.0001f, Mathf.Abs(parentScale.z));

        float localX = targetWorldWidth / (spriteWidthUnits * parentScaleX);
        float localY = targetWorldHeight / (spriteHeightUnits * parentScaleY);
        float localZ = targetWorldWidth / (spriteWidthUnits * parentScaleZ);
        staccatoIndicatorInstance.transform.localScale = new Vector3(localX, localY, localZ);
        staccatoIndicatorInstance.transform.localRotation = Quaternion.identity;

        if (staccatoIndicatorController != null)
        {
            Transform trackSpace = (noteSpawner != null) ? noteSpawner.trackTransform : null;
            staccatoIndicatorController.Configure(
                this.transform,
                staccatoIndicatorOffset,
                staccatoIndicatorUpright,
                staccatoIndicatorOffsetRelativeToWidth ? this : null,
                staccatoIndicatorOffsetRelativeToWidth,
                trackSpace,
                targetWorldHeight
            );
        }
        else
        {
            staccatoIndicatorInstance.transform.position = transform.position + staccatoIndicatorOffset;
        }

        staccatoIndicatorInstance.SetActive(true);
    }

    private static Transform GetStaccatoIndicatorRoot()
    {
        if (staccatoIndicatorRoot != null) return staccatoIndicatorRoot;
        GameObject root = new GameObject("Staccato Indicator Container");
        staccatoIndicatorRoot = root.transform;
        return staccatoIndicatorRoot;
    }

    private void HideStaccatoIndicator()
    {
        if (staccatoIndicatorRenderer != null)
        {
            staccatoIndicatorRenderer.enabled = false;
        }
        if (staccatoIndicatorInstance != null)
        {
            staccatoIndicatorInstance.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (staccatoIndicatorInstance != null)
        {
            Destroy(staccatoIndicatorInstance);
            staccatoIndicatorInstance = null;
        }
    }

    // Defensive release wrapper: log reasons and avoid releasing a note until it's actually
    // reached the judgment line (to avoid premature disappearance). This is conservative
    // and only defers release for a short time while the note approaches the judgment Z.
    private bool deferredReleaseLogged = false;
    private void TryReleaseOrDestroy(string reason, float songPos, float currentSpeed)
    {
        // If already judged, allow release
        if (isJudged)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[NoteController] TryReleaseOrDestroy(allow) reason={reason} goId={gameObject.GetInstanceID()} isJudged={isJudged} posZ={transform.position.z} judgmentZ={judgmentZ} songPos={songPos} startTime={startTime}"); } catch { }
            // #endif
            ReleaseOrDestroy();
            return;
        }

        // Compute expected Z for the note head based on songPos and currentSpeed.
        // targetZ = judgmentZ + ((startTime - songPos)/1000f) * currentSpeed
        float targetZ = judgmentZ + ((startTime - songPos) / 1000f) * currentSpeed;
        float deltaToTarget = Mathf.Abs(transform.position.z - targetZ);
        // thresholds: z threshold (world units) to allow release when near expected position
        const float zThreshold = 0.05f; // world units
        // allow immediate release if the note is very close to where it should be
        if (deltaToTarget <= zThreshold)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[NoteController] TryReleaseOrDestroy(permitted) reason={reason} goId={gameObject.GetInstanceID()} deltaToTarget={deltaToTarget} targetZ={targetZ} posZ={transform.position.z} songPos={songPos} startTime={startTime}"); } catch { }
            // #endif
            ReleaseOrDestroy();
            deferredReleaseLogged = false;
            return;
        }

        // If we've already passed far beyond endTime, allow release regardless (failsafe)
        if (songPos > endTime + 500)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.Log($"[NoteController] TryReleaseOrDestroy(failsafeRelease) reason={reason} goId={gameObject.GetInstanceID()} songPos={songPos} endTime={endTime}"); } catch { }
            // #endif
            ReleaseOrDestroy();
            deferredReleaseLogged = false;
            return;
        }

        // Otherwise defer release and log once to avoid spamming.
        if (!deferredReleaseLogged)
        {
            // #if UNITY_EDITOR || DEVELOPMENT_BUILD
            // try { Debug.LogWarning($"[NoteController] Deferred release for goId={gameObject.GetInstanceID()} reason={reason} posZ={transform.position.z} judgmentZ={judgmentZ} deltaToTarget={deltaToTarget} songPos={songPos} startTime={startTime}"); } catch { }
            // #endif
            deferredReleaseLogged = true;
        }
        // Do not call ReleaseOrDestroy now; Update will be called next frame and this guard
        // will be re-evaluated. If the situation persists far beyond expected time, the
        // existing failsafe (songPos > endTime + 500) will eventually force a release.
    }
    
    /// <summary>
    /// Plays hit sound effect using GameManager's HitSoundManager or fallback to AudioSource.PlayClipAtPoint
    /// </summary>
    private void PlayHitSound()
    {
        if (cachedHitSound == null)
        {
            // Debug.LogWarning("PlayHitSound: cachedHitSound is null!");
            return;
        }
        
        // Development-only diagnostics: log caller context to help find unexpected release sounds
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
            string t = noteData != null ? (noteData.type ?? "<null>") : "<noNoteData>";
            int nt = (noteData != null) ? noteData.note_type : -1;
            BuildLogger.Log($"[NoteController] PlayHitSound invoked: type={t} note_type={nt} start={startTime} end={endTime} headPressed={headPressed} isStaccato={isStaccatoCached} hasTriggeredHitSound={hasTriggeredHitSound}\nCallStack:\n{System.Environment.StackTrace}");
        }
        catch { }
        #endif
        // NOTE: Local per-note playback is intentionally disabled.
        // All hit sound playback is centralized in JudgmentManager.PlayHitSoundCentral()
        // to avoid duplicate or unexpected audio (tails/hold-ends). Keep the
        // diagnostic log above so developers can still trace who invoked this
        // method, but do not actually play any clip here.
        return;
    }

    // Create or retrieve a cached Sprite for the given Texture2D to avoid repeated allocations
    private Sprite GetOrCreateSprite(Texture2D tex)
    {
        if (tex == null) return null;
        Sprite s;
        if (spriteCache.TryGetValue(tex, out s)) return s;
        try
        {
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            spriteCache[tex] = s;
            return s;
        }
        catch
        {
            // Debug.LogWarning($"GetOrCreateSprite failed for texture {tex.name}: {ex}");
            return null;
        }
    }

    // Preload shared resources used by NoteController instances to avoid hitches during pre-roll.
    // Call this before visual pre-roll so static cached assets and sprite cache are warmed.
    public static void PreloadSharedResources(GameObject notePrefab)
    {
        try
        {
            if (cachedHitSound == null)
            {
                cachedHitSound = Resources.Load<AudioClip>("Sound/Tap");
            }

            if (notePrefab == null) return;

            // Inspect the prefab for NoteController and any configured textures/sprites
            var nc = notePrefab.GetComponentInChildren<NoteController>();
            if (nc == null) return;

            // Pre-cache textures -> sprites using the same logic as instance method to avoid duplication
            try
            {
                if (nc.softTexture != null && !spriteCache.ContainsKey(nc.softTexture))
                {
                    var s = Sprite.Create(nc.softTexture, new Rect(0, 0, nc.softTexture.width, nc.softTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    spriteCache[nc.softTexture] = s;
                }
                if (nc.rightHandTexture != null && !spriteCache.ContainsKey(nc.rightHandTexture))
                {
                    var s = Sprite.Create(nc.rightHandTexture, new Rect(0, 0, nc.rightHandTexture.width, nc.rightHandTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    spriteCache[nc.rightHandTexture] = s;
                }
                if (nc.leftHandTexture != null && !spriteCache.ContainsKey(nc.leftHandTexture))
                {
                    var s = Sprite.Create(nc.leftHandTexture, new Rect(0, 0, nc.leftHandTexture.width, nc.leftHandTexture.height), new Vector2(0.5f, 0.5f), 100f);
                    spriteCache[nc.leftHandTexture] = s;
                }
                // If softSprite (Sprite) exists on prefab, ensure it's usable by leaving as-is (no creation needed)
            }
            catch { }
        }
        catch { }
    }
}

/// <summary>
/// Emits a low-count, track-flat particle fan behind a moving note. Velocities
/// are assigned per particle so no mixed ParticleSystem velocity curves exist.
/// </summary>
public sealed class NoteFanParticleEmitter : MonoBehaviour
{
    private static Texture2D proceduralSparkTexture;
    private static Transform particleRoot;
    private static NoteFanParticlePool particlePool;
    private static Camera cachedParticleCamera;
    private static readonly Dictionary<Texture, Material> particleMaterialCache =
        new Dictionary<Texture, Material>(4);
    private static readonly Queue<ParticleSystem> availableParticleSystems =
        new Queue<ParticleSystem>(24);
    private const int PrewarmParticleSystems = 24;

    private ParticleSystem particleSystemInstance;
    private ParticleEffectPlayer particleController;
    private Transform trackSpace;
    private Transform judgmentLine;
    private Color particleColor = Color.white;
    private float noteWidth = 1f;
    private int burstCount = 48;
    private float emissionRate = 18f;
    private float particleSpeed = 3.2f;
    private float fanHalfAngle = 34f;
    private float particleLifetime = 0.62f;
    private float particleSize = 0.16f;
    private float emissionAccumulator;
    private bool configured;
    private bool holdEmissionActive;
    private bool hasJudgmentOrigin;
    private Vector3 judgmentOrigin;
    private Texture2D configuredTexture;
    private int configuredSortingLayerId;
    private int configuredSortingOrder;

    public static void PrewarmSharedPool()
    {
        GetParticleRoot();
    }

    public void Configure(ParticleEffectPlayer newParticleController, Transform newTrackSpace,
        Transform newJudgmentLine, Color newColor, float newWidth, Texture2D texture,
        int newBurstCount, float newEmissionRate, float newSpeed, float newHalfAngle, float newLifetime, float newSize,
        int sortingLayerId, int sortingOrder)
    {
        // Pooled notes are reconfigured many times. A ParticleSystem is only
        // needed after a judgment, so don't create one for every visible note.
        RelinquishParticleSystem(true);
        particleController = newParticleController;
        trackSpace = newTrackSpace;
        judgmentLine = newJudgmentLine;
        configuredTexture = texture;
        configuredSortingLayerId = sortingLayerId;
        configuredSortingOrder = sortingOrder;
        particleColor = newColor;
        particleColor.a = 0.76f;
        noteWidth = Mathf.Max(0.05f, newWidth);
        burstCount = Mathf.Clamp(newBurstCount, 1, 128);
        emissionRate = Mathf.Max(1f, newEmissionRate);
        particleSpeed = Mathf.Max(0.1f, newSpeed);
        fanHalfAngle = Mathf.Clamp(newHalfAngle, 5f, 80f);
        particleLifetime = Mathf.Max(0.05f, newLifetime);
        particleSize = Mathf.Max(0.02f, newSize);
        RefreshControllerSettings();
        emissionAccumulator = 0f;
        holdEmissionActive = false;
        hasJudgmentOrigin = false;
        configured = true;
    }

    public void EmitJudgmentBurst(bool releaseFromNoteAfterEmission = false)
    {
        if (!configured || !RefreshControllerSettings()) return;
        EnsureParticleSystem(configuredTexture);
        if (particleSystemInstance == null) return;
        particleSystemInstance.Play(true);
        ResolveBasis(out var up, out var forward, out var right);
        for (int i = 0; i < burstCount; i++)
            EmitSpark(up, forward, right, true);

        if (releaseFromNoteAfterEmission)
            RetireOneShotSystem();
    }

    public void SetJudgmentOrigin(Vector3 worldPosition, float visualWidth)
    {
        judgmentOrigin = worldPosition;
        hasJudgmentOrigin = true;
        if (visualWidth > 0.001f) noteWidth = visualWidth;
    }

    private void RetireOneShotSystem()
    {
        if (particleSystemInstance == null) return;

        // A TAP/STAC/SOFT note is commonly returned to its pool or destroyed
        // in the judgment frame. Relinquish the already-emitted system so its
        // particles can finish simulating independently of that note.
        ParticleSystem completedBurst = particleSystemInstance;
        particleSystemInstance = null;
        configured = false;
        holdEmissionActive = false;
        emissionAccumulator = 0f;
        completedBurst.transform.SetParent(GetParticleRoot(), true);
        completedBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        GetParticlePool().ReturnWhenFinished(completedBurst);
    }

    public void BeginHoldEmission()
    {
        if (!configured || holdEmissionActive || !RefreshControllerSettings()) return;
        EnsureParticleSystem(configuredTexture);
        if (particleSystemInstance == null) return;
        particleSystemInstance.Play(true);
        holdEmissionActive = true;
        emissionAccumulator = 0f;
    }

    public void EndHoldEmission()
    {
        holdEmissionActive = false;
        emissionAccumulator = 0f;
        if (particleSystemInstance != null)
            particleSystemInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    public void StopAndClear()
    {
        bool wasHolding = holdEmissionActive;
        configured = false;
        holdEmissionActive = false;
        emissionAccumulator = 0f;
        // Tap/STAC/SOFT notes are pooled in the same frame as their judgment.
        // Do not stop their detached burst before it has rendered. Hold streams
        // still need an explicit stop when the sustained note ends.
        if (particleSystemInstance != null)
        {
            if (wasHolding)
                particleSystemInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            RelinquishParticleSystem(wasHolding);
        }
    }

    private void EnsureParticleSystem(Texture2D texture)
    {
        if (particleSystemInstance != null)
        {
            particleSystemInstance.GetComponent<ParticleSystemRenderer>().sharedMaterial = ResolveParticleMaterial(texture);
            return;
        }

        particleSystemInstance = AcquireParticleSystem();
        if (particleSystemInstance == null) return;
        var renderer = particleSystemInstance.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = ResolveParticleMaterial(texture);
        renderer.sortingLayerID = configuredSortingLayerId;
        renderer.sortingOrder = configuredSortingOrder;
    }

    private static Transform GetParticleRoot()
    {
        if (particleRoot != null) return particleRoot;
        var root = new GameObject("NoteFanParticleRoot");
        DontDestroyOnLoad(root);
        particleRoot = root.transform;
        particlePool = root.AddComponent<NoteFanParticlePool>();
        for (int i = 0; i < PrewarmParticleSystems; i++)
            availableParticleSystems.Enqueue(CreateParticleSystem());
        return particleRoot;
    }

    private static NoteFanParticlePool GetParticlePool()
    {
        GetParticleRoot();
        return particlePool;
    }

    private static ParticleSystem AcquireParticleSystem()
    {
        GetParticleRoot();
        ParticleSystem system = null;
        while (availableParticleSystems.Count > 0 && system == null)
            system = availableParticleSystems.Dequeue();
        if (system == null) system = CreateParticleSystem();
        system.transform.SetParent(particleRoot, false);
        system.gameObject.SetActive(true);
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return system;
    }

    private static ParticleSystem CreateParticleSystem()
    {
        var child = new GameObject("NoteFanParticles (Pooled)");
        child.transform.SetParent(particleRoot, false);
        var system = child.AddComponent<ParticleSystem>();

        var main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = 96;
        main.startSpeed = 0f;
        main.startLifetime = 0.62f;
        main.startSize = 0.16f;
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);

        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = false;
        var limitVelocity = system.limitVelocityOverLifetime;
        limitVelocity.enabled = false;

        var colorOverLifetime = system.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0.15f, 0f),
                new GradientAlphaKey(1f, 0.14f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = fade;

        var renderer = child.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        child.SetActive(false);
        return system;
    }

    internal static void ReturnParticleSystem(ParticleSystem system)
    {
        if (system == null) return;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.transform.SetParent(GetParticleRoot(), false);
        system.gameObject.SetActive(false);
        availableParticleSystems.Enqueue(system);
    }

    private void RelinquishParticleSystem(bool preserveLiveParticles)
    {
        if (particleSystemInstance == null) return;
        ParticleSystem system = particleSystemInstance;
        particleSystemInstance = null;
        if (preserveLiveParticles && system.IsAlive(true))
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            GetParticlePool().ReturnWhenFinished(system);
        }
        else
        {
            ReturnParticleSystem(system);
        }
    }

    private static Material ResolveParticleMaterial(Texture2D texture)
    {
        Texture resolvedTexture = texture != null ? texture : CreateProceduralSparkTexture();
        if (resolvedTexture != null &&
            particleMaterialCache.TryGetValue(resolvedTexture, out Material cached) &&
            cached != null)
            return cached;

        Material resourceMaterial = Resources.Load<Material>("Materials/NoteFanParticle");
        Material material;
        if (resourceMaterial != null)
        {
            material = new Material(resourceMaterial);
        }
        else
        {
            Shader shader = Shader.Find("Custom/UnlitAdditiveEmission");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) return null;
            material = new Material(shader);
        }
        material.name = "Note Fan Particle (Runtime)";
        material.hideFlags = HideFlags.DontSave;
        material.renderQueue = 3100;
        material.SetTexture("_MainTex", resolvedTexture);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", new Color(1.8f, 1.8f, 1.8f, 1f));
        if (resolvedTexture != null) particleMaterialCache[resolvedTexture] = material;
        return material;
    }

    private static Texture2D CreateProceduralSparkTexture()
    {
        if (proceduralSparkTexture != null) return proceduralSparkTexture;

        const int size = 32;
        proceduralSparkTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = "Procedural Note Star Spark",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                float radial = Mathf.Exp(-(nx * nx + ny * ny) * 7.5f);
                float horizontal = Mathf.Exp(-Mathf.Abs(ny) * 17f) * Mathf.Exp(-Mathf.Abs(nx) * 2.4f);
                float vertical = Mathf.Exp(-Mathf.Abs(nx) * 17f) * Mathf.Exp(-Mathf.Abs(ny) * 2.4f);
                float intensity = Mathf.Clamp01(radial * 0.82f + Mathf.Max(horizontal, vertical) * 0.72f);
                pixels[y * size + x] = new Color(intensity, intensity, intensity, intensity);
            }
        }
        proceduralSparkTexture.SetPixels(pixels);
        proceduralSparkTexture.Apply(false, true);
        return proceduralSparkTexture;
    }

    private void LateUpdate()
    {
        if (!configured || !holdEmissionActive || particleSystemInstance == null || Time.deltaTime <= 0f) return;
        if (!RefreshControllerSettings())
        {
            EndHoldEmission();
            return;
        }

        ResolveBasis(out var up, out var forward, out var right);
        emissionAccumulator += emissionRate * Time.deltaTime;
        int emitCount = Mathf.Min(5, Mathf.FloorToInt(emissionAccumulator));
        emissionAccumulator -= emitCount;
        for (int i = 0; i < emitCount; i++)
            EmitSpark(up, forward, right, false);
    }

    private void ResolveBasis(out Vector3 up, out Vector3 forward, out Vector3 right)
    {
        up = trackSpace != null ? trackSpace.up : Vector3.up;
        up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;

        // In-plane spread axes for the cone around 'up'. They come from the
        // player's screen axes projected onto the track so the widening burst
        // reads on screen instead of running straight into camera depth.
        Camera viewCamera = cachedParticleCamera;
        if (viewCamera == null) cachedParticleCamera = viewCamera = Camera.main;
        Vector3 desiredForward = viewCamera != null
            ? viewCamera.transform.up
            : (trackSpace != null ? trackSpace.forward : Vector3.forward);
        Vector3 desiredRight = viewCamera != null
            ? viewCamera.transform.right
            : (trackSpace != null ? trackSpace.right : Vector3.right);
        forward = Vector3.ProjectOnPlane(desiredForward, up).normalized;
        right = Vector3.ProjectOnPlane(desiredRight, up).normalized;
        forward = Vector3.ProjectOnPlane(forward, up).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(up, forward).normalized;

        // Preserve a stable right-handed basis after projection.
        if (Vector3.Dot(Vector3.Cross(right, forward), up) < 0f)
            right = -right;
    }

    private bool RefreshControllerSettings()
    {
        if (particleController == null) return true;
        return particleController.TryGetJudgmentStarFanSettings(
            out burstCount,
            out emissionRate,
            out particleSpeed,
            out fanHalfAngle,
            out particleLifetime,
            out particleSize);
    }

    private void EmitSpark(Vector3 up, Vector3 forward, Vector3 right, bool burst)
    {
        float maxAngle = fanHalfAngle * (burst ? 1.18f : 1f);
        float angle = UnityEngine.Random.Range(-maxAngle, maxAngle) * Mathf.Deg2Rad;
        float speedMultiplier = burst
            ? UnityEngine.Random.Range(0.35f, 1.55f)
            : UnityEngine.Random.Range(0.38f, 1.05f);
        // Sparks leave the track surface: the cone is centred on the track
        // normal and only spreads by fanHalfAngle around it. forward/right stay
        // the in-plane axes that give that spread a random azimuth.
        float azimuth = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 spread = right * Mathf.Cos(azimuth) + forward * Mathf.Sin(azimuth);
        Vector3 direction = (up * Mathf.Cos(angle) + spread * Mathf.Sin(angle)).normalized;
        Vector3 lineOrigin = hasJudgmentOrigin ? judgmentOrigin : transform.position;
        if (judgmentLine != null)
        {
            Vector3 toLine = judgmentLine.position - lineOrigin;
            lineOrigin += forward * Vector3.Dot(toLine, forward);
            lineOrigin += up * Vector3.Dot(toLine, up);
        }
        Vector3 origin = lineOrigin
            + up * 0.045f
            + right * UnityEngine.Random.Range(-noteWidth * 0.32f, noteWidth * 0.32f);

        float sizeVariation = UnityEngine.Random.Range(0.48f, 1.35f);
        if (UnityEngine.Random.value < (burst ? 0.22f : 0.1f)) sizeVariation *= 2.2f;
        float whiteMix = burst
            ? UnityEngine.Random.Range(0.48f, 0.86f)
            : UnityEngine.Random.Range(0.18f, 0.58f);
        Color sparkColor = Color.Lerp(particleColor, Color.white, whiteMix);
        sparkColor.a = burst
            ? UnityEngine.Random.Range(0.72f, 1f)
            : UnityEngine.Random.Range(0.42f, 0.82f);

        var emit = new ParticleSystem.EmitParams
        {
            position = origin,
            velocity = direction * particleSpeed * speedMultiplier,
            startLifetime = particleLifetime * UnityEngine.Random.Range(0.72f, 1.3f),
            startSize = particleSize * sizeVariation,
            startColor = sparkColor,
            rotation = UnityEngine.Random.Range(-Mathf.PI, Mathf.PI)
        };
        particleSystemInstance.Emit(emit, 1);
    }

    private void OnDestroy()
    {
        RelinquishParticleSystem(true);
    }
}

/// <summary>
/// Returns detached one-shot fan systems after their existing particles fade.
/// This replaces per-hit GameObject destruction with a fixed reusable pool.
/// </summary>
public sealed class NoteFanParticlePool : MonoBehaviour
{
    private readonly List<ParticleSystem> retiring = new List<ParticleSystem>(24);

    public void ReturnWhenFinished(ParticleSystem system)
    {
        if (system == null || retiring.Contains(system)) return;
        retiring.Add(system);
    }

    private void Update()
    {
        for (int i = retiring.Count - 1; i >= 0; i--)
        {
            ParticleSystem system = retiring[i];
            if (system != null && system.IsAlive(true)) continue;
            retiring.RemoveAt(i);
            if (system != null) NoteFanParticleEmitter.ReturnParticleSystem(system);
        }
    }
}

/// <summary>
/// Keeps a staccato indicator hovering above its parent note and facing the camera.
/// </summary>
public class StaccatoIndicatorBillboard : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.6f, 0f);
    [Tooltip("When enabled, ignores vertical tilt and only rotates around the Y axis.")]
    [SerializeField] private bool freezeYawOnly = false;
    [SerializeField] private bool scaleOffsetByWidth = false;

    private NoteController widthSource;
    private Transform coordinateSpace;
    private float visualHeightWorld = 1f;

    /// <summary>
    /// 同時在跑的紋章數量。
    /// </summary>
    /// <remarks>
    /// **兩個，不是三個。** 三個加上 0.78 的間距，整列有 2.3 個紋章高，在音符上
    /// 方堆成一根柱子 —— 譜面一密就是一片箭頭牆，而且會蓋到上面那一顆音符。
    ///
    /// 兩個、間距收到 0.55：整列只有一個紋章多一點，讀起來是「一個往上跑的東西」，
    /// 而不是「一疊東西」。往上的方向靠**動**講，不靠數量。
    /// </remarks>
    private const int RiseCopies = 2;

    /// <summary>
    /// 可以看到紋章的那一段有多高，佔紋章自己高度的比例。
    /// </summary>
    /// <remarks>
    /// 這就是「碰到就銷毀」的那個高度。窗口的上緣和下緣都會**裁掉**紋章：從下面
    /// 長出來的時候被下緣切，升到上面的時候被上緣切，切口永遠停在同一個高度上。
    /// </remarks>
    private const float RiseWindowShare = 1.5f;

    /// <summary>跑完一整圈要幾秒。</summary>
    private const float RisePeriod = 0.55f;

    /// <summary>窗口下緣和音符之間的空隙，佔紋章自己高度的比例。</summary>
    private const float GapShare = 0.30f;

    private SpriteRenderer sourceRenderer;
    private MeshRenderer riseRenderer;
    private Mesh riseMesh;
    private static System.Collections.Generic.Dictionary<Texture, Material> riseMaterials;
    private readonly System.Collections.Generic.List<Vector3> riseVertices =
        new System.Collections.Generic.List<Vector3>(16);
    private readonly System.Collections.Generic.List<Vector2> riseUv =
        new System.Collections.Generic.List<Vector2>(16);
    private readonly System.Collections.Generic.List<int> riseTriangles =
        new System.Collections.Generic.List<int>(24);

    private Camera cachedCamera;

    public void Configure(Transform newTarget, Vector3 newOffset, bool freezeYaw, NoteController newWidthSource,
        bool scaleByWidth, Transform newCoordinateSpace, float newVisualHeightWorld)
    {
        target = newTarget;
        offset = newOffset;
        freezeYawOnly = freezeYaw;
        widthSource = (scaleByWidth && newWidthSource != null) ? newWidthSource : null;
        scaleOffsetByWidth = scaleByWidth && newWidthSource != null;
        coordinateSpace = newCoordinateSpace;
        visualHeightWorld = Mathf.Max(0.01f, newVisualHeightWorld);
        UpdateTransform();
    }

    public void SetTarget(Transform newTarget, Vector3 newOffset)
    {
        Configure(newTarget, newOffset, freezeYawOnly, widthSource, scaleOffsetByWidth, coordinateSpace, visualHeightWorld);
    }

    private void OnEnable()
    {
        RefreshCamera();
        UpdateTransform();
    }

    private void LateUpdate()
    {
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        Transform basis = coordinateSpace != null ? coordinateSpace : target;
        Vector3 upVector = Vector3.up;
        Vector3 forwardOnPlane = Vector3.forward;
        Vector3 rightOnPlane = Vector3.right;

        if (basis != null)
        {
            upVector = basis.up.sqrMagnitude > 1e-6f ? basis.up.normalized : Vector3.up;

            Vector3 candidateForward = Vector3.ProjectOnPlane(basis.forward, upVector);
            if (candidateForward.sqrMagnitude > 1e-6f)
            {
                forwardOnPlane = candidateForward.normalized;
            }

            Vector3 candidateRight = Vector3.ProjectOnPlane(basis.right, upVector);
            if (candidateRight.sqrMagnitude > 1e-6f)
            {
                rightOnPlane = candidateRight.normalized;
            }
            else
            {
                rightOnPlane = Vector3.Cross(forwardOnPlane, upVector).normalized;
            }

            float widthScale = 1f;
            if (scaleOffsetByWidth && widthSource != null)
            {
                try
                {
                    widthScale = Mathf.Max(0.0001f, widthSource.GetCurrentWorldWidth());
                }
                catch
                {
                    widthScale = 1f;
                }
            }

            Camera viewCamera = GetActiveCamera();
            Vector3 billboardUp = viewCamera != null ? viewCamera.transform.up.normalized : upVector;
            Vector3 billboardRight = viewCamera != null ? viewCamera.transform.right.normalized : rightOnPlane;
            float xOffset = offset.x * (widthScale * 0.5f);

            // 這個物件的原點是**窗口的下緣**，不是紋章的中心：紋章在窗口裡上上下
            // 下，而窗口本身不動 —— 不動的那個才適合當原點。
            Vector3 worldOffset = (billboardRight * xOffset)
                + (upVector * Mathf.Max(0.015f, offset.y))
                + (forwardOnPlane * offset.z)
                + (billboardUp * (visualHeightWorld * GapShare));
            if (target != null)
            {
                transform.position = target.position + worldOffset;
            }
            else
            {
                transform.position = basis.position + worldOffset;
            }
        }

        Camera camera = GetActiveCamera();
        if (camera != null)
        {
            // A SpriteRenderer is parallel to the camera image plane when it
            // shares the camera rotation. This is a true billboard, not an
            // approximation based on the configured camera pitch.
            transform.rotation = camera.transform.rotation;
        }
        else
        {
            Vector3 yawDir = forwardOnPlane.sqrMagnitude > 1e-6f ? forwardOnPlane : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(yawDir.normalized, upVector);
        }

        UpdateRise();
    }

    /// <summary>
    /// 一列往上跑的紋章：升到窗口上緣被裁掉，從下緣再裁著長出來。
    /// </summary>
    /// <remarks>
    /// **為什麼自己畫網格，不用 SpriteRenderer。** 要的效果是**裁減**：切口停在固
    /// 定的高度上，紋章從那條線底下長出來、也在那條線上被削掉。SpriteRenderer 畫
    /// 的永遠是一整張圖，做不到這件事 —— 只能改成淡入淡出（那是另一種效果），或
    /// 者每一幀用 Sprite.Create 重切一張圖（每一幀都在產生垃圾）。
    ///
    /// 網格則是直接把四個頂點放在切口上、UV 跟著切一樣的比例，切口因此是連續的，
    /// 不會因為量化而在邊界上抖。兩個紋章一共八個頂點，每幀重寫一次。
    ///
    /// SpriteRenderer 還留著，因為 NoteController 用它記圖、排序和顯示與否；只是
    /// 把它的**繪製**關掉（forceRenderingOff），那些設定才不會分成兩套。
    /// </remarks>
    private void UpdateRise()
    {
        if (sourceRenderer == null)
        {
            sourceRenderer = GetComponent<SpriteRenderer>();
            if (sourceRenderer == null) return;
        }
        Sprite sprite = sourceRenderer.sprite;
        if (sprite == null || sprite.texture == null) return;

        sourceRenderer.forceRenderingOff = true;
        if (!EnsureRiseRenderer(sprite)) return;

        riseRenderer.enabled = sourceRenderer.enabled;
        riseRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        riseRenderer.sortingOrder = sourceRenderer.sortingOrder;
        if (!riseRenderer.enabled) return;

        float height = Mathf.Max(0.0001f, sprite.bounds.size.y);
        float width = sprite.bounds.size.x;
        float window = height * RiseWindowShare;

        // 一圈 = 窗口高 + 紋章高：從完全在下緣底下，走到完全在上緣外面。
        float cycle = window + height;
        float step = cycle / RiseCopies;
        // **每一顆斷音都在同一個相位上。** 之前每顆給了自己的相位，想讓它們看起來
        // 不像同一塊板子在動 —— 但譜面上的斷音常常是成群出現的，各跑各的會讓那一
        // 群箭頭高高低低，讀起來是噪點。同高同步反而像同一個機制在運作。
        float t = Mathf.Repeat(Time.time / Mathf.Max(0.05f, RisePeriod), 1f);

        Rect uvRect = sprite.textureRect;
        float texW = sprite.texture.width;
        float texH = sprite.texture.height;
        float u0 = uvRect.xMin / texW;
        float u1 = uvRect.xMax / texW;
        float vMin = uvRect.yMin / texH;
        float vMax = uvRect.yMax / texH;

        riseVertices.Clear();
        riseUv.Clear();
        riseTriangles.Clear();

        for (int i = 0; i < RiseCopies; i++)
        {
            float bottom = Mathf.Repeat((i + t) * step, cycle) - height;
            float top = bottom + height;

            // 切口：窗口外面的部分不畫。位置和 UV 切一樣的比例，圖才不會被拉扁。
            float drawBottom = Mathf.Max(bottom, 0f);
            float drawTop = Mathf.Min(top, window);
            if (drawTop - drawBottom <= 0.0005f) continue;

            float k0 = (drawBottom - bottom) / height;
            float k1 = (drawTop - bottom) / height;
            float v0 = Mathf.Lerp(vMin, vMax, k0);
            float v1 = Mathf.Lerp(vMin, vMax, k1);

            int at = riseVertices.Count;
            riseVertices.Add(new Vector3(-width * 0.5f, drawBottom, 0f));
            riseVertices.Add(new Vector3(-width * 0.5f, drawTop, 0f));
            riseVertices.Add(new Vector3(width * 0.5f, drawTop, 0f));
            riseVertices.Add(new Vector3(width * 0.5f, drawBottom, 0f));
            riseUv.Add(new Vector2(u0, v0));
            riseUv.Add(new Vector2(u0, v1));
            riseUv.Add(new Vector2(u1, v1));
            riseUv.Add(new Vector2(u1, v0));
            riseTriangles.Add(at); riseTriangles.Add(at + 1); riseTriangles.Add(at + 2);
            riseTriangles.Add(at); riseTriangles.Add(at + 2); riseTriangles.Add(at + 3);
        }

        riseMesh.Clear();
        if (riseTriangles.Count == 0) return;
        riseMesh.SetVertices(riseVertices);
        riseMesh.SetUVs(0, riseUv);
        riseMesh.SetTriangles(riseTriangles, 0);
        riseMesh.bounds = new Bounds(new Vector3(0f, window * 0.5f, 0f),
            new Vector3(Mathf.Abs(width), window + height, 0.01f));
    }

    private bool EnsureRiseRenderer(Sprite sprite)
    {
        if (riseMesh == null)
        {
            riseMesh = new Mesh { name = "StaccatoRise", hideFlags = HideFlags.HideAndDontSave };
            riseMesh.MarkDynamic();
        }
        if (riseRenderer == null)
        {
            var host = new GameObject("RiseMesh");
            host.layer = gameObject.layer;
            host.transform.SetParent(transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = riseMesh;
            riseRenderer = host.AddComponent<MeshRenderer>();
            riseRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            riseRenderer.receiveShadows = false;
            riseRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            riseRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        // 同一張圖共用一個材質：每一顆斷音各自 new 一個，材質數量會跟著譜面長。
        riseMaterials ??= new System.Collections.Generic.Dictionary<Texture, Material>();
        if (!riseMaterials.TryGetValue(sprite.texture, out Material material) || material == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            material = new Material(shader)
            {
                name = "StaccatoRise (" + sprite.texture.name + ")",
                hideFlags = HideFlags.HideAndDontSave,
            };
            material.mainTexture = sprite.texture;
            riseMaterials[sprite.texture] = material;
        }
        if (riseRenderer.sharedMaterial != material) riseRenderer.sharedMaterial = material;
        return true;
    }

    private Camera GetActiveCamera()
    {
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
        {
            return cachedCamera;
        }

        RefreshCamera();
        return cachedCamera;
    }

    private void RefreshCamera()
    {
        cachedCamera = Camera.main;
        if (cachedCamera == null)
        {
            var cameras = Camera.allCameras;
            if (cameras != null && cameras.Length > 0)
            {
                cachedCamera = cameras[0];
            }
        }
    }
}
