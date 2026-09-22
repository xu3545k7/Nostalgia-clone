Shader "Custom/ClassicalHoldGradient"
{
    Properties
    {
        _MainTex ("Tail Texture", 2D) = "white" {}
        _Color ("Hand Tint", Color) = (1,1,1,1)
        _NearColor ("Near Gradient", Color) = (1,0.78,0.34,1)
        _FarColor ("Far Gradient", Color) = (0.20,0.025,0.04,0)
        _FlowStrength ("Pressed Flow", Range(0,1)) = 0
        _FlowSpeed ("Flow Speed", Float) = 0.65
        _FlowColor ("Flow Color", Color) = (1,0.76,0.28,1)
        _PreserveGold ("Preserve Texture Gold", Range(0,1)) = 1
        _Emission ("Tail Inner Glow", Range(0,4)) = 1.2
        _EdgeGlow ("Tail Edge Glow", Range(0,4)) = 0.9
        _TailFadeStrength ("Longitudinal Fade", Range(0,1)) = 1
        _WorldClipEnabled ("World Clip Enabled", Range(0,1)) = 0
        _WorldClipMinZ ("World Clip Min Z", Float) = -100000
        _WorldClipMaxZ ("World Clip Max Z", Float) = 100000
        _FlatFill ("Flat Gesture Fill", Range(0,1)) = 0
        _SmoothBody ("Smooth Body", Range(0,1)) = 1
        _CoreWidth ("Central Light Width", Range(0.02,0.45)) = 0.25
        _CoreGlow ("Central Light Glow", Range(0,3)) = 0.65
        _CoreJudgedBoost ("Judged Core Boost", Range(0,3)) = 1.45
        // 一般長條的新畫法。滑奏、顫音維持上面那一套（見 frag 裡的說明）。
        _GlassRod ("Glass Rod Body", Range(0,1)) = 0
        _HoldStartZ ("Hold Start Z (world)", Float) = 0
        _HoldEndZ ("Hold End Z (world)", Float) = 0
        _BeatSpacingZ ("Beat Spacing Z (world)", Float) = 0
        _JudgeZ ("Judgment Line Z (world)", Float) = 0
        _InclusionScale ("Inclusion Scale", Float) = 2.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
          Blend SrcAlpha OneMinusSrcAlpha
          Cull Off
          ZWrite Off
          ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 刻度和收頭的粗細用 fwidth 換成像素，才會在任何距離下都是同樣的細。
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _NearColor;
            fixed4 _FarColor;
            float _FlowStrength;
            float _FlowSpeed;
            fixed4 _FlowColor;
            float _PreserveGold;
            float _Emission;
            float _EdgeGlow;
            float _TailFadeStrength;
            float _WorldClipEnabled;
            float _WorldClipMinZ;
            float _WorldClipMaxZ;
            float _FlatFill;
            float _SmoothBody;
            float _CoreWidth;
            float _CoreGlow;
            float _CoreJudgedBoost;
            float _GlassRod;
            float _HoldStartZ;
            float _HoldEndZ;
            float _BeatSpacingZ;
            float _JudgeZ;
            float _InclusionScale;

            static const float3 HoldGold = float3(1.0, 0.76, 0.28);

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1, 0));
                float c = Hash21(cell + float2(0, 1));
                float d = Hash21(cell + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 一般長條：一根玻璃棒。
            //
            // **光在兩側、中間是深的。** 上一版在正中央放一根光柱，長條於是讀成
            // 「一條平面帶子、中間畫了一道線」。真的玻璃棒是反過來的：邊緣的折射
            // 最亮，中間是看進去的深度 —— 厚度就是從這裡讀出來的。
            //
            // **每一拍一道金色刻度。** 一段兩秒的長條從頭到尾是同一個顏色，沒有
            // 任何東西說它有多長；刻度讓它像一把尺，按的時候數得出自己走到第幾拍。
            //
            // **尾端收頭，不淡出。** 淡出讀起來是「沒畫完」。一道金邊加一顆菱形
            // 是在說「到這裡為止」—— 和踏板線斷口、結算頁金線是同一個母題。
            //
            // 刻度和紋理都錨在**長條自己的起點**（_HoldStartZ）上，不是世界座標。
            // 錨在世界的話，長條往下走時紋理會在玻璃裡流動 —— 那就是這支 shader
            // 一直在避免的水波。
            half4 GlassRod(float2 uv, float3 worldPos, float alphaShape, float activation)
            {
                float u = saturate(uv.x);
                float d = abs(u * 2.0 - 1.0);
                float3 tint = _Color.rgb;
                float pressed = smoothstep(0.35, 0.72, activation);

                float z = worldPos.z;
                float pz = max(fwidth(z), 1e-5);          // 一個像素在 z 方向是多遠
                float pu = max(fwidth(u), 1e-5);

                // body：一根水晶棒，一體成形、不透明。
                //
                // 上一版把長條切成一格一格的彩繪玻璃，格線反而讓它讀成「一串東西」
                // 而不是「一段長按」—— 長按本來就是連續的一件事，身體也該是一塊。
                // 半透明也一起拿掉：透出底下的軌道會讓顏色被弄髒，水晶的質感要靠
                // 自己的明暗，不是靠透。
                //
                // 質感來自三層，全部是靜止的、而且錨在長條自己的起點上：
                //   縱向的切面    —— 幾道寬的面，各有各的亮度，交界處一條亮稜線；
                //   內部的光流    —— 很低頻的雜訊，像水晶裡的絮狀內含物；
                //   兩側的內反射  —— 邊緣附近提亮，讀得出它是圓的、有厚度。
                // 顏色是深的：右手酒紅、左手普魯士藍。
                //
                // 手的紅藍（1, 0.18, 0.18）／（0.27, 0.64, 1）是給**音符**用的，音符
                // 只有一小顆，要搶眼；長條佔的面積是音符的幾十倍，同樣的彩度鋪滿整
                // 條會把譜面吵掉。寶石級的深色面積再大也安靜，而且深色正是水晶該有
                // 的樣子 —— 亮的地方留給切面和稜線，不是整塊。
                float3 glass = lerp(float3(0.285, 0.048, 0.092), float3(0.009, 0.090, 0.200),
                                    step(tint.r, tint.b));

                // 橫切面的厚度：正中央最厚（最深），往兩側漸薄。整條只有一個高
                // 點，不再切成三個面 —— 三個面會留下兩道白稜線，正中央那一道才是
                // 主角。
                float3 col = glass * (1.28 - 0.42 * (1.0 - d) * (1.0 - d));

                // 內含物：低頻、靜止。錨在 _HoldStartZ，長條往下走時才不會流動。
                float2 local = float2(u * 2.5, (z - _HoldStartZ) / max(pz / pu * 1.6, 1e-4));
                col *= 0.90 + 0.22 * ValueNoise(local);

                // 兩側的內反射。
                float innerRefl = pow(saturate((d - 0.62) / 0.38), 1.6);
                // 帶多一點玻璃自己的顏色（0.7）、白少一點：身體壓深之後，原本那個
                // 偏白的反射會讓邊緣泛灰，讀起來像蒙了一層霧。
                col += (glass * 0.7 + 0.30) * innerRefl * 0.55;

                // 正中央一道白線。
                //
                // 沒按的時候它細而銳利 —— 深色的水晶裡一條硬邊的白，是「這一條還沒
                // 被碰到」。按到之後它變粗、亮起來，光從線往外暈開：同一個東西的兩
                // 個狀態，眼睛不用比對兩個地方就知道現在有沒有按著。
                //
                // 寬度有像素下限，長條被透視壓窄時線才不會消失。
                float centreD = abs(u - 0.5);
                float lineW = max(lerp(0.010, 0.048, pressed), pu * 1.2);
                float centreLine = 1.0 - smoothstep(lineW * 0.55, lineW, centreD);
                col = lerp(col, float3(1.0, 0.99, 0.96) * (1.0 + pressed * 1.6), centreLine);

                // 金色外框。
                //
                // 左右手不靠框分 —— 水晶本身就是紅的或藍的。框只剩一件事要做：把長
                // 條的左右邊界講死，不靠淡出交代。和踏板線的斷口、結算頁的金線是同
                // 一個母題。
                //
                // 粗細是**長條寬度的比例**（兩側各 4%），不是固定幾個像素；像素那一
                // 項只留作下限（2 px），免得長條被透視壓窄時框糊掉。
                float toEdgeU = (1.0 - d) * 0.5;                 // 離左右邊緣有多遠（u 單位）
                // 框加寬一點（4% → 5.5%）：細線在泛光下會被糊掉，寬一點的金才撐
                // 得住那道暗→亮的落差。
                float outlineW = max(0.055, pu * 2.5);
                float outline = 1.0 - smoothstep(outlineW * 0.65, outlineW, toEdgeU);
                // 金框分兩段：外緣暗金、內緣亮金。
                //
                // 一整條同樣亮度的金在暗底上一定讀成白 —— 眼睛判斷「這是金屬」靠的
                // 是同一塊面上的明暗落差，不是色相。給它一道暗邊，亮的那一半才會被
                // 讀成反光而不是白色的線。亮的那一段也壓在 1 以下，不然泛光會把三
                // 個通道一起推向白。
                float frameT = saturate(toEdgeU / max(outlineW, 1e-5));   // 0=最外緣 1=內緣
                // 亮的那一段往橘金推（藍幾乎歸零）、亮度再收一點；暗邊加重成褐金。
                // 金屬的黃是靠「藍通道低」來的，不是靠亮 —— 藍一上去就往白跑。
                float3 goldCol = lerp(float3(0.30, 0.155, 0.020),
                                      float3(0.90, 0.60, 0.085), smoothstep(0.15, 0.85, frameT));
                col = lerp(col, goldCol, outline);

                // 按住時，判定線上方那一段稍微充能變亮。
                if (_BeatSpacingZ > 1e-4)
                {
                    float ahead = z - _JudgeZ;
                    float charge = pressed * step(0.0, ahead) *
                                   (1.0 - smoothstep(0.0, _BeatSpacingZ * 1.5, ahead));
                    col *= 1.0 + 0.45 * charge;
                }


                // 按住：那條白線往外暈開的光。顏色帶一點手的紅／藍，泛光出去才不
                // 會只是一片白。
                float coreX = (u - 0.5) / 0.11;
                float coreGlow = exp(-coreX * coreX);
                col += (tint * 0.45 + 0.55) * coreGlow * pressed * 1.1;

                // 尾端的金色鑲座。
                //
                // 尾端要講的事只有一件：到這裡放開。一道實的橫帶把長條切斷，位置精
                // 確到一個像素 —— 淡出讀起來是「還沒畫完」，圓頭讀起來是「還會再延
                // 續」，都不行。
                //
                // 它和兩側的金框是同一個材質，所以整根長條讀成一件完整的東西（金屬
                // 鑲座包著水晶），不是身體再加一顆補丁。也因為畫在同一個 mesh 上，
                // 寬度永遠和身體一致。
                //
                // 一樣是外緣暗、內緣亮的倒角：金屬要靠同一面上的明暗落差才讀得出來。
                if (_HoldEndZ > _HoldStartZ + 1e-4)
                {
                    float toEnd = _HoldEndZ - z;                 // 0 = 最尾端
                    // pz / pu 就是長條在世界裡有多寬（一個像素在 z 是 pz、在 u 是
                    // pu，而 u 被寬度正規化過），所以鑲座的高度會跟著寬度走，透視
                    // 壓多扁它在螢幕上的比例都一樣。
                    // 0.10 而不是 0.18：俯視的譜面檢視器把長條看成一塊短方塊，
                    // 兩成的收口在那裡讀起來像「一半都是框」。同時夾在長條長度的
                    // 兩成以內，短長條才不會整條都是座。
                    float mountLen = min(max(pz / pu * 0.10, pz * 4.0),
                                         (_HoldEndZ - _HoldStartZ) * 0.2);
                    float inMount = 1.0 - smoothstep(mountLen * 0.90, mountLen, toEnd);
                    float mt = saturate(toEnd / mountLen);
                    float3 mount = lerp(float3(0.30, 0.155, 0.020),
                                        float3(0.90, 0.60, 0.085), smoothstep(0.08, 0.70, mt));
                    // 最外緣再壓一道暗線，收口才有邊。
                    mount = lerp(float3(0.14, 0.07, 0.010), mount, smoothstep(0.0, 0.14, mt));
                    col = lerp(col, mount, inMount * step(0.0, toEnd));
                }

                half4 output;
                output.rgb = col;
                // 不透明：形狀（貼圖的 alpha）決定哪裡有東西，其餘一律實心。半透
                // 的話底下的軌道會把水晶的顏色弄髒。
                output.a = alphaShape;
                return output;
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                if (_WorldClipEnabled > 0.5)
                {
                    clip(i.worldPos.z - _WorldClipMinZ);
                    clip(_WorldClipMaxZ - i.worldPos.z);
                }
                fixed4 tex = tex2D(_MainTex, i.uv);
                float longitudinal = smoothstep(0.0, 1.0, i.uv.y);
                if (_WorldClipEnabled > 0.5)
                {
                    // Anchor the gradient to the visible runway.  UV-based
                    // fading restarts whenever a streamed mesh is recycled and
                    // creates both seams and frames with no visible gradient.
                    float visibleLength = max(0.001, _WorldClipMaxZ - _WorldClipMinZ);
                    longitudinal = smoothstep(0.0, 1.0,
                        saturate((i.worldPos.z - _WorldClipMinZ) / visibleLength));
                }
                // _FlowStrength is retained for material compatibility, but it
                // now represents a static pressed-glow amount. Nothing here is
                // driven by _Time, so the glass never turns into a water ripple.
                float activation = saturate(_FlowStrength);
                if (_GlassRod > 0.5 && _FlatFill < 0.5)
                {
                    half4 rod = GlassRod(i.uv, i.worldPos, tex.a, activation);
                    rod.a *= i.color.a;
                    return rod;
                }

                float peak = max(tex.r, max(tex.g, tex.b));
                float floorValue = min(tex.r, min(tex.g, tex.b));
                float luminance = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                float2 px = _MainTex_TexelSize.xy * 1.5;
                float neighbourAlpha = min(
                    min(tex2D(_MainTex, i.uv + float2(px.x, 0)).a,
                        tex2D(_MainTex, i.uv - float2(px.x, 0)).a),
                    min(tex2D(_MainTex, i.uv + float2(0, px.y)).a,
                        tex2D(_MainTex, i.uv - float2(0, px.y)).a));
                float rim = saturate(tex.a - neighbourAlpha);
                float goldRatio = tex.g / max(0.001, tex.r);
                float goldMask = smoothstep(0.28, 0.48, goldRatio) *
                                 (1.0 - smoothstep(0.92, 1.04, goldRatio)) *
                                 smoothstep(0.08, 0.32, tex.r - tex.b) *
                                 smoothstep(0.22, 0.72, peak);

                // The authored hold texture contains a strong water pattern.
                // SmoothBody keeps its alpha silhouette but removes that RGB
                // detail, replacing it with a quiet static glass highlight.
                float textureDetail = lerp(0.58, 1.28, smoothstep(0.04, 0.90, luminance));
                float lateral = saturate(i.uv.x);
                float distanceFromCentre = abs(lateral * 2.0 - 1.0);
                float centreDepth = pow(saturate(1.0 - distanceFromCentre), 1.65);
                float outerBevel = smoothstep(0.72, 0.985, distanceFromCentre);
                float innerBevel = smoothstep(0.48, 0.76, distanceFromCentre) *
                                   (1.0 - smoothstep(0.78, 0.94, distanceFromCentre));
                // Two deliberately asymmetric, broad reflections give the
                // surface optical depth without looking tiled or animated.
                float primaryReflection = pow(
                    saturate(1.0 - abs(lateral - 0.30) * 8.0), 4.0);
                float secondaryReflection = pow(
                    saturate(1.0 - abs(lateral - 0.69) * 12.0), 5.0);
                float glassProfile = 0.70 + centreDepth * 0.10 +
                                     innerBevel * 0.09 +
                                     primaryReflection * 0.10 +
                                     secondaryReflection * 0.045;
                float detail = lerp(textureDetail, glassProfile, _SmoothBody);
                // Preserve the original body through 80% of its visible length,
                // then perform the entire gradient in the final 20%.
                float gradientProgress = smoothstep(0.80, 0.90, longitudinal);
                float3 gradientTint = lerp(_Color.rgb, _FarColor.rgb, gradientProgress);
                float3 themedTint = lerp(_Color.rgb, gradientTint, _TailFadeStrength);
                float3 themed = themedTint * detail;
                float highlight = smoothstep(0.72, 1.0, peak) *
                                  (0.35 + 0.65 * smoothstep(0.18, 0.82, floorValue)) *
                                  (1.0 - _SmoothBody);
                themed = lerp(themed, fixed3(1,1,1), highlight * 0.72);

                half4 output;
                // Preserve only the metallic gold detected in the original
                // texture. Its irregular coloured interior is still discarded.
                float preservedGold = goldMask * _PreserveGold;
                output.rgb = lerp(themed, tex.rgb, preservedGold);
                float innerCore = lerp(smoothstep(0.14, 0.9, luminance), 0.66 + centreDepth * 0.20,
                    _SmoothBody) * tex.a;
                output.rgb += _Color.rgb * (innerCore * _Emission * 0.32 + rim * _EdgeGlow);
                float glassEdgeLight = outerBevel * 0.36 + innerBevel * 0.14;
                float pressedLustre = glassEdgeLight +
                                      primaryReflection * 0.16 +
                                      secondaryReflection * 0.08;
                output.rgb += _FlowColor.rgb * pressedLustre * activation * tex.a *
                              (0.34 + _Emission * 0.16);
                // A narrow light pillar gives the smooth Hold a strong optical
                // centre. It is always faintly present and becomes brighter
                // once the head is judged/held. FlatFill (Trill) replaces this
                // result below, preserving its intentionally dark centre.
                float centreDistance = abs(lateral - 0.5);
                float column = 1.0 - smoothstep(
                    _CoreWidth * 0.55, _CoreWidth, centreDistance);
                float columnCore = 1.0 - smoothstep(
                    _CoreWidth * 0.12, _CoreWidth * 0.42, centreDistance);
                // Idle: a dense, deeper hand-coloured core. Once judgment
                // starts, it opens into a pale luminous version of that same
                // red/blue instead of merely increasing white emission.
                float judgedCore = smoothstep(0.35, 0.72, activation);
                float columnIntensity = _CoreGlow *
                    (0.72 + judgedCore * _CoreJudgedBoost);
                float3 deepColumnTint = _Color.rgb * 0.36;
                float3 brightColumnTint = lerp(_Color.rgb, fixed3(1,1,1), 0.74);
                float3 columnTint = lerp(deepColumnTint, brightColumnTint, judgedCore);
                // An idle core must actually read as deep glass. Additive light
                // alone can only brighten it, so first compress the underlying
                // colour inside the broad 50%-width column.
                float idleCoreDensity = column * (1.0 - judgedCore) * 0.72;
                output.rgb = lerp(output.rgb, _Color.rgb * 0.22, idleCoreDensity);
                output.rgb += columnTint *
                    (column * 0.52 + columnCore * 0.48) *
                    columnIntensity * tex.a;
                float tailFade = lerp(1.0, lerp(1.0, 0.012, gradientProgress), _TailFadeStrength);
                output.a = tex.a * _Color.a * tailFade * i.color.a;
                if (_FlatFill > 0.5)
                {
                    // Muted glass body with a dark centre and broad side light.
                    // This is position-based, never mesh-edge-based.
                    float tipGlow = saturate(i.color.r);
                    float lateralPosition = saturate(i.color.b);
                    float flatDistanceFromCentre = abs(lateralPosition * 2.0 - 1.0);
                    float sideReach = lerp(0.14, 1.0,
                        smoothstep(0.02, 0.80, flatDistanceFromCentre));
                    float sideGlow = sideReach * tipGlow;
                    float3 mutedHandTint = lerp(_Color.rgb, fixed3(0.72,0.74,0.78), 0.12);
                    output.rgb = mutedHandTint * 0.038;
                    output.rgb += mutedHandTint * sideGlow * (0.82 + _EdgeGlow * 0.56);
                    output.rgb += mutedHandTint * activation * sideGlow * 0.18;
                    // An opaque dark glass centre hides track/triangle lines;
                    // only the broad side regions gain light and transparency.
                    output.a = _Color.a * lerp(0.52, 0.95, sideGlow);
                }
                return output;
            }
            ENDCG
        }
    }
    FallBack Off
}
