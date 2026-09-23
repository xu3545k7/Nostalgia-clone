using UnityEngine;

/// <summary>
/// 把本家的落下曲線當成**畫面高度**來用，再反解出該擺在哪個世界 Y（以及被這個
/// 修正帶偏的世界 X）。
/// </summary>
/// <remarks>
/// 本家是 2D：那條曲線就是音符在螢幕上的垂直位置，和下落速度無關——速度只改變
/// 它走完這條路要多久。這裡照著做：先決定「這一刻該出現在畫面的哪個高度」，再解
/// 出對應的世界 Y。落點因此**保證**在判定線上，而且和鏡頭角度、距離、FOV 無關。
///
/// 直接把那條曲線加在世界 Y 上是第一版的錯：透視已經讓音符往下掉了，等於算兩次。
///
/// 反解出來的 Y 會連帶把畫面上的**橫向**位置帶偏：俯視的鏡頭下，把一個點往下壓
/// 等於把它推遠，於是它在畫面上往中間縮。高速時位移動輒幾百個世界單位，音符因此
/// 一路從畫面中央往外掃到自己的鍵上——落地瞬間橫向掃得比垂直還快，看起來就是
/// 「切線該接近垂直，卻接近傾斜」。所以另外存一張橫向補正表，把世界 X 乘回去，
/// 音符整段都待在自己那一鍵的正上方。
///
/// 鏡頭只有俯角、沒有偏擺與翻滾，所以畫面高度只和世界 (y, z) 有關、與 x 無關。
/// 每幀沿著跑道取樣一張表，其他人查表內插就好，不必每個頂點都解一次。
/// </remarks>
public static class NoteArcScreen
{
    /// <summary>取樣點數。跑道再長，64 段的內插誤差也看不出來。</summary>
    const int Samples = 64;

    static readonly float[] offsets = new float[Samples + 1];
    static readonly float[] lateral = new float[Samples + 1];
    static float judgeZ;
    static float travelZ;
    static float fadeStart;
    static float fadeEnd;
    static int preparedFrame = -1;
    static bool active;

    /// <summary>這一幀有沒有弧線可用。</summary>
    public static bool Active => active;

    /// <summary>
    /// 整張表，給 shader 用。
    /// </summary>
    /// <remarks>
    /// shader 曾經自己在 clip 座標上指定畫面高度——那需要假設 clip.y 的正負向，
    /// 而那個約定在不同的繪製路徑下會翻轉，結果長押整根上下顛倒（「hold 跑反了」）。
    /// 改成把 C# 算好的同一張表送進去、在**世界座標**位移，就沒有任何約定要猜。
    /// </remarks>
    public static float[] Table => offsets;
    public static int SampleCount => Samples + 1;
    public static float JudgeZ => judgeZ;
    public static float TravelZ => travelZ;

    /// <summary>
    /// 每幀準備一次。重複呼叫只有第一次會算。
    /// </summary>
    /// <param name="baseY">沒有弧線時音符所在的平面高度。</param>
    /// <param name="lineViewY">判定線在畫面上的高度（0 = 畫面底，1 = 畫面頂）。</param>
    /// <param name="apexShare">弧線頂點比判定線高出畫面高度的幾成。</param>
    /// <param name="judgmentZ">找不到判定線時才會用到的備案。</param>
    /// <param name="baseY">同上。</param>
    /// <param name="spawnDistance">音符從離判定線多遠的地方開始出現。</param>
    public static void Prepare(Camera camera, float judgmentZ, float travelWorldZ,
                               float baseY, float lineViewY, float apexShare,
                               float spawnDistance)
    {
        if (preparedFrame == Time.frameCount) return;
        active = false;
        if (camera == null || travelWorldZ <= 0.0001f || apexShare <= 0.0001f) return;

        // 基準平面和判定線的 Z 一律從判定線本人拿，**不聽呼叫端的**。
        //
        // 這張表一幀只算一次，誰先呼叫誰說了算；而各家的基準平面本來就不一樣
        // （踏板是「判定線 y + 位移」，而且每 120 幀會重新量一次小節線高度），
        // 於是同一顆踏板的高度會隨著「這一幀是音符先跑還是踏板先跑」在兩個值
        // 之間跳——那就是「踏板特效位置不穩定」。
        //
        // 各家仍然是把位移加在自己的平面上，所以彼此之間刻意的高度差還在，
        // 只是那個差變成固定的，不再每幀重算。
        ResolveJudgmentLine();
        if (bar != null)
        {
            // 它平常在 LateUpdate 才挪位置，而這裡是 Update。不先叫醒它，就會變成
            // 「用這一幀的鏡頭瞄上一幀的判定線」。
            bar.PlaceNow();
            // 而且瞄的是它**量到的**畫面高度，不是設定值：只要有人動過它
            // （heightOffset、自動擷取、版面重算），設定值就不再是它真正的位置，
            // 落點會落在一個沒有東西的高度上。量它本人，落點就恆等於它。
            lineViewY = bar.MeasuredViewportY(camera);
        }
        judgeZ = line != null ? line.position.z : judgmentZ;
        baseY = line != null ? line.position.y : baseY;
        travelZ = travelWorldZ;
        // 淡入的範圍：生成的地方全透明，走到頂點時全不透明。
        fadeStart = (1f - NoteArc.ApexProgress01) * travelWorldZ;
        fadeEnd = spawnDistance;
        // 音符不能被推到遠裁切面外面，不然整顆消失。
        float maxOffset = Mathf.Max(200f, camera.farClipPlane * 0.6f);
        Vector3 camPos = camera.transform.position;
        Vector3 camFwd = camera.transform.forward;
        // 投影矩陣一幀不變，抽出來只取一次。
        Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        for (int i = 0; i <= Samples; i++)
        {
            float t = (float)i / Samples;
            float worldZ = judgeZ + travelWorldZ * t;
            float progress = 1f - t;                      // t=0 是判定線
            float target = lineViewY + NoteArc.HeightNorm(progress) * apexShare;
            float offset = SolveWorldYOffset(viewProjection, worldZ, baseY, target, maxOffset);
            offsets[i] = offset;
            lateral[i] = LateralScale(camPos, camFwd, worldZ, baseY, offset);
        }
        preparedFrame = Time.frameCount;   // 算成功才蓋章
        active = true;
    }

    static Transform line;
    static JudgmentLineBar bar;

    static void ResolveJudgmentLine()
    {
        if (line != null) return;
        GameObject go = GameObject.Find("JudgmentLine");
        line = go != null ? go.transform : null;
        bar = go != null ? go.GetComponent<JudgmentLineBar>() : null;
    }

    /// <summary>
    /// 這一幀不要弧線。已經算好的話就不動它——半路插進來的呼叫端不該把別人
    /// 算好的表關掉。
    /// </summary>
    public static void Disable()
    {
        if (preparedFrame == Time.frameCount) return;
        active = false;
    }

    /// <summary>
    /// 被垂直修正推遠（或拉近）之後，世界 X 要乘多少才留在原來的畫面位置。
    /// </summary>
    /// <remarks>
    /// 畫面 X 正比於 x / 鏡頭景深，而沿世界 Y 移動會改變鏡頭景深（俯視的鏡頭下
    /// 「上」是朝著鏡頭的）。所以景深變成幾倍，x 就要乘幾倍。
    /// </remarks>
    static float LateralScale(Vector3 camPos, Vector3 camFwd, float worldZ,
                              float baseY, float offset)
    {
        float flat = Vector3.Dot(new Vector3(0f, baseY, worldZ) - camPos, camFwd);
        if (flat <= 0.001f) return 1f;
        float moved = Vector3.Dot(new Vector3(0f, baseY + offset, worldZ) - camPos, camFwd);
        if (moved <= 0.001f) return 1f;
        return moved / flat;
    }

    /// <summary>
    /// 反解：要讓 (worldZ, y) 這一點出現在畫面高度 target，y 該離 baseY 多遠。
    /// </summary>
    /// <remarks>
    /// **閉式解。** 鏡頭沒有偏擺，所以世界 X 不影響畫面高度，可以固定取 0；那麼
    /// clip.y 和 clip.w 對世界 Y 都只是一次式：
    ///     clip.y = A·y + B,  clip.w = C·y + D,  而 clip.y / clip.w = 2·target − 1
    /// 一元一次方程式，解出來是精確值，而且對任何投影矩陣（含物理鏡頭、斜投影）
    /// 都成立。
    ///
    /// 前兩版都是迭代的：先是「量一次斜率套兩步割線」（誤差 60～295 個世界單位），
    /// 再來是牛頓法跑六步、衝到鏡頭後面就把步伐折半。近處收斂得很好，但遠處要的
    /// 位移會逼近「那個 z 上畫面高度的極點」，函數是雙曲線，六步常常收不完 ——
    /// 而且收不收得完，會隨著鏡頭與判定線每幀那一點點變化而翻面。表上因此有幾格
    /// 在兩個值之間跳。音符只查自己那一格、通常在近處，看不出來；**長條一根橫跨
    /// 整張表**，那幾格一跳整根就抖一下 —— 那就是「長條有時候會跳一下」。
    /// 順帶把先前記錄的「遠端最多差 142 px」一起解掉。
    /// </remarks>
    /// <param name="viewProjection">camera.projectionMatrix × camera.worldToCameraMatrix。</param>
    static float SolveWorldYOffset(Matrix4x4 viewProjection, float worldZ, float baseY,
                                   float target, float maxOffset)
    {
        // x = 0、w = 1 代進去之後只剩下 y 這一個未知數。
        float a = viewProjection.m11;
        float b = viewProjection.m12 * worldZ + viewProjection.m13;
        float c = viewProjection.m31;
        float d = viewProjection.m32 * worldZ + viewProjection.m33;
        float k = target * 2f - 1f;

        // 解不能落到鏡頭平面（clip.w = 0）上或後面去。極點在基準平面的哪一側，
        // 就限制哪一個方向；另一個方向是安全的，維持原本的上限。
        float upLimit = maxOffset;
        float downLimit = -maxOffset;
        if (Mathf.Abs(c) > 1e-9f)
        {
            float pole = -d / c - baseY;
            if (pole > 0f) upLimit = Mathf.Min(upLimit, pole * 0.98f);
            else downLimit = Mathf.Max(downLimit, pole * 0.98f);
        }

        float denom = a - k * c;
        // 分母為 0 ＝ 那個畫面高度在這個 z 上只有無限遠才到得了。目標高度一定在
        // 判定線之上（HeightNorm ≥ 0），所以往上走到允許的極限。
        if (Mathf.Abs(denom) < 1e-9f) return upLimit;

        float offset = (k * d - b) / denom - baseY;
        if (float.IsNaN(offset) || float.IsInfinity(offset)) return 0f;
        return Mathf.Clamp(offset, downLimit, upLimit);
    }

    static readonly int OffsetsId = Shader.PropertyToID("_ArcOffsets");
    static readonly int SampleCountId = Shader.PropertyToID("_ArcSampleCount");
    static readonly int JudgeZId = Shader.PropertyToID("_ArcJudgeZ");
    static readonly int TravelZId = Shader.PropertyToID("_ArcTravelZ");
    static readonly int AnchorYId = Shader.PropertyToID("_ArcAnchorY");
    static readonly int LateralId = Shader.PropertyToID("_ArcLateral");
    static readonly int AnchorXId = Shader.PropertyToID("_ArcAnchorX");
    static readonly int BaseXId = Shader.PropertyToID("_ArcBaseX");
    static readonly int FadeStartId = Shader.PropertyToID("_ArcFadeStart");
    static readonly int FadeEndId = Shader.PropertyToID("_ArcFadeEnd");

    /// <summary>
    /// 把這張表寫進 property block，讓 shader 算出和 C# 一模一樣的位移。
    /// </summary>
    /// <remarks>
    /// 長條狀的幾何（長押尾巴、踏板）頂點太多，不適合每幀在 C# 逐點改；但也不能
    /// 讓 shader 自己算一套——它看不到鏡頭。送同一張表進去就兩全：曲線只有一份，
    /// 數值上保證一致。沒有弧線時寫 0 筆，shader 就原樣不動。
    /// </remarks>
    /// <param name="anchorOffset">
    /// 這個物件的世界 Y 已經被抬高了多少。母體已經照這張表擺好時（長押尾巴掛在
    /// 音符底下）要填進來，不然同一段位移會被算兩次。
    /// </param>
    /// <param name="anchorX">母體現在的世界 X（已經乘過補正的那個）。</param>
    /// <param name="baseX">母體沒被補正過的原始世界 X。</param>
    public static void ApplyTo(MaterialPropertyBlock block, float anchorOffset = 0f,
                               float anchorX = 0f, float baseX = 0f)
    {
        if (block == null) return;
        if (!active)
        {
            block.SetFloat(SampleCountId, 0f);
            // 淡入範圍不歸 SampleCount 管（NoteArcFade 自己判斷），所以也要寫。
            // 這幾個 uniform 沒有宣告在 Properties 裡，材質給不出預設值 —— 不寫
            // 就是沿用常數緩衝區裡上一次同 shader 的 draw call 留下來的值。
            block.SetFloat(FadeStartId, 0f);
            block.SetFloat(FadeEndId, 0f);
            return;
        }
        block.SetFloatArray(OffsetsId, offsets);
        block.SetFloatArray(LateralId, lateral);
        block.SetFloat(SampleCountId, offsets.Length);
        block.SetFloat(JudgeZId, judgeZ);
        block.SetFloat(TravelZId, travelZ);
        block.SetFloat(AnchorYId, anchorOffset);
        block.SetFloat(AnchorXId, anchorX);
        block.SetFloat(BaseXId, baseX);
        block.SetFloat(FadeStartId, fadeStart);
        block.SetFloat(FadeEndId, fadeEnd);
    }

    /// <summary>
    /// 明確關掉這個 block 的弧線，不管這一幀有沒有表。
    /// </summary>
    /// <remarks>
    /// 給「就是不該有弧線」的畫法用（俯視檢視器的靜態長條）。不寫的話那次
    /// draw call 會沿用常數緩衝區裡上一次同 shader 留下來的值。
    /// </remarks>
    public static void ClearOn(MaterialPropertyBlock block)
    {
        if (block == null) return;
        block.SetFloat(SampleCountId, 0f);
        block.SetFloat(FadeStartId, 0f);
        block.SetFloat(FadeEndId, 0f);
    }

    /// <summary>某個世界 Z 該被抬高多少。超出跑道範圍就夾在兩端。</summary>
    public static float OffsetAtZ(float worldZ)
    {
        return Sample(offsets, worldZ, 0f);
    }

    /// <summary>
    /// 某個世界 Z 上的不透明度。生成的地方 0，走到頂點變成 1，之後一直是 1。
    /// </summary>
    /// <remarks>
    /// 生成距離短的時候音符是從曲線中段冒出來的，沒有淡入就會憑空出現一顆。
    /// 終點取在頂點是因為那是曲線上唯一和「遠近」無關的地標。生成距離比頂點
    /// 還近時沒有淡入的空間，直接全不透明。
    /// </remarks>
    public static float FadeAtZ(float worldZ)
    {
        if (!active || fadeEnd <= fadeStart + 0.001f) return 1f;
        float d = worldZ - judgeZ;
        if (d <= fadeStart) return 1f;
        if (d >= fadeEnd) return 0f;
        return 1f - (d - fadeStart) / (fadeEnd - fadeStart);
    }

    /// <summary>某個世界 Z 上，世界 X 要乘多少才留在原來的畫面位置。</summary>
    public static float LateralAtZ(float worldZ)
    {
        return Sample(lateral, worldZ, 1f);
    }

    static float Sample(float[] table, float worldZ, float inactiveValue)
    {
        if (!active) return inactiveValue;
        float t = (worldZ - judgeZ) / travelZ;
        if (t <= 0f) return table[0];
        if (t >= 1f) return table[Samples];
        float f = t * Samples;
        int i = Mathf.Clamp((int)f, 0, Samples - 1);
        return Mathf.Lerp(table[i], table[i + 1], f - i);
    }

    /// <summary>某個世界 Z 上的弧線斜率（dy/dz），給「音符要不要跟著斜」用。</summary>
    public static float SlopeAtZ(float worldZ)
    {
        if (!active) return 0f;
        float step = travelZ / Samples;
        return (OffsetAtZ(worldZ + step) - OffsetAtZ(worldZ - step)) / (2f * step);
    }
}
