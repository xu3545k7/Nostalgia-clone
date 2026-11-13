Shader "Custom/PremiumJudgmentGlow"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (1,0.82,0.42,1)
        _GlowIntensity ("Glow Intensity", Range(0,12)) = 5.2
        _CoreIntensity ("White Core", Range(0,12)) = 8.0
        _EdgeSoftness ("Edge Softness", Range(0.01,0.5)) = 0.18

        // 柱子有多高，是**橫向位置的函數**：中間最高，往左右收。
        // _HeightEdge 是最邊上還剩幾成高度，_HeightCurve 是收得多急，
        // _HeightShape 是頂端有多圓（2 = 高斯，會收成一個尖；越大越像圓頂）。
        _HeightEdge ("Height At Edge", Range(0,1)) = 0.38
        _HeightCurve ("Height Falloff", Range(0,8)) = 2.4
        _HeightShape ("Height Roundness", Range(1,8)) = 3.2
        // 判定線上那一下橫向有多窄。小 = 整條線都在閃，大 = 只有柱子底下在閃。
        _StrikeWidth ("Strike Width", Range(0.5,12)) = 5.0

        // 輪廓糊不糊，由這兩個決定 —— 兩個都是**邊界的硬度**，不是形狀。
        _FadePower ("Length Fade Power", Range(0.3,4)) = 1.45
        _AlphaGain ("Alpha Gain", Range(0.2,2)) = 0.65
        // 力度彈對時，柱子往上要走向的顏色。_TipBlend 0 = 完全不套用。
        [HDR] _TipColor ("Precise Tip Tint", Color) = (0.56,0.10,0.98,1)
        _TipBlend ("Precise Tip Blend", Range(0,1)) = 0

        // 「完全完美」（準時而且力度對）專用：**中間金、兩側藍、最外緣紫**。
        // 三個都是橫向位置的函數，疊在同一團中央柔光的亮度衰減上 ——
        // 柱子已經沒有欄杆了（見 frag 裡 aura 那一段），所以顏色不是放在
        // 兩條線上，而是沿著那團光自己從中心往外的衰減排。
        // _FlawlessCoreGain 把核心的亮度壓下來：核心平常乘 _CoreIntensity（8），
        // 那個量級下任何顏色都會被推成白，金就不是金了。
        [HDR] _FlawlessCore ("Flawless Core Tint", Color) = (1.00,0.74,0.28,1)
        [HDR] _FlawlessSide ("Flawless Side Tint", Color) = (0.26,0.48,1.25,1)
        [HDR] _FlawlessRim ("Flawless Rim Tint", Color) = (2.29,0.62,3.77,1)
        _FlawlessSideStart ("Flawless Side Start", Range(0,1)) = 0.44
        _FlawlessSideEnd ("Flawless Side End", Range(0,1)) = 0.80
        _FlawlessRimStart ("Flawless Rim Start", Range(0,1)) = 0.80
        _FlawlessBlend ("Flawless Blend", Range(0,1)) = 0
        _FlawlessCoreGain ("Flawless Core Gain", Range(0.1,1)) = 0.28
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _GlowIntensity;
                float _CoreIntensity;
                float _EdgeSoftness;
                float _HeightEdge;
                float _HeightCurve;
                float _HeightShape;
                float _StrikeWidth;
                float _FadePower;
                float _AlphaGain;
                float4 _TipColor;
                float _TipBlend;
                float4 _FlawlessCore;
                float4 _FlawlessSide;
                float4 _FlawlessRim;
                float _FlawlessSideStart;
                float _FlawlessSideEnd;
                float _FlawlessRimStart;
                float _FlawlessBlend;
                float _FlawlessCoreGain;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float x = abs(input.uv.x - 0.5) * 2.0;
                float y = saturate(input.uv.y);

                // **高度沿橫向衰減：中間最高，往左右收。**
                //
                // 做法是把 y 除以一條橫向的高度輪廓，之後所有跟高度有關的衰減
                // 都讀這個正規化後的 yn 而不是 y。於是每一欄都「用完自己那一段
                // 高度」—— 中間那一欄走滿整根，邊上那一欄早早就走完、淡掉了，
                // 柱子的輪廓因此是一道火焰而不是一個方塊。
                //
                // **為什麼不改網格。** 這個 mesh 只有上下兩排頂點（見
                // NoteJudgementMeshManager.ApplyVerticalGradient，每幀照
                // VertexIsTop 寫頂點色）。要讓輪廓是曲線就得補上好幾欄頂點，
                // 而那份逐頂點的工作每幀都要重做一次；曲線本來就該在片段裡算，
                // 頂點少的時候還會把平滑的弧線畫成折線。
                //
                // 判定線上那一下（strike）**不跟著正規化**：它是貼在線上的一條
                // 橫向閃光，要在整個寬度上一樣厚。拿 yn 去算的話它會在兩側被
                // 高度輪廓壓扁，變成中間厚兩邊薄，那就不是一條線了。
                // **指數決定頂端是尖的還是圓的。**
                //
                // 原本是 `exp2(-x*x*k)`，一條高斯。高斯從 x=0 就開始掉，平的那一段
                // 相對於整個寬度很窄，所以最高的只有正中間那一小條 —— 畫出來是一個
                // 錐。把 x 的次方從 2 提到 3.2（super-gaussian）之後，中段幾乎是平的
                // （x=0.5 還有九成高），衰減集中到肩膀上，頂端才圓。
                //
                // 邊緣那一端也一起抬高（0.22 -> 0.38）：中間和兩側差太多本身就是
                // 「極端」的來源，不是只有尖不尖的問題。
                //
                // max() 是因為 pow(0, 非整數) 在某些編譯器上會出 NaN。
                float heightProfile = _HeightEdge
                    + (1.0 - _HeightEdge) * exp2(-pow(max(x, 1e-5), _HeightShape) * _HeightCurve);
                float yn = saturate(y / max(heightProfile, 0.001));

                // Soft silhouette removes the rectangular edge of the source quad.
                float softEdge = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, x);

                // **指數小於 1 的衰減邊界一定是硬的。**
                //
                // 原本是 0.72：`(1-yn)^0.72` 在 yn→1 的導數是**負無限大**，也就是
                // 最後那一點亮度是垂直掉下去的。形狀做成錐體之前看不出來（整根
                // 柱子的頂端被方塊的上緣切掉，本來就沒人在看），一做成錐體，那條
                // 垂直的落差沿著整條斜邊展開，就變成一道描邊。
                //
                // 指數大於 1 的話導數在 yn→1 是 0，亮度是**躺著**接近零的，邊界
                // 自己就糊掉了。1.45 是「還看得出是一根柱子」和「沒有描邊」之間。
                float lengthFade = pow(saturate(1.0 - yn), _FadePower);

                // **只剩中央那一團光。**
                //
                // 這裡本來還有兩條細欄杆（`exp2(-pow(abs(x-0.76)*13,2))`）：窄、亮、
                // 位置釘死在 x≈0.76，所以畫出來是一個框把柱子框起來，而不是一道光。
                // 拿掉之後柱子橫向只剩一個峰，從中心單調地淡出 —— 顏色這才有辦法
                // 按亮度排（中心最亮給金，外面漸暗給藍，最邊緣給紫）。
                //
                // 指數從 2.1 提到 3.2 是跟著收窄：欄杆沒了之後 2.1 的那團光攤得太開，
                // 看起來是一片霧而不是一根柱子。權重（下面的 0.50 / 0.70）則是把欄杆
                // 原本佔掉的亮度補回來一部分，不補的話整根柱子會暗一截。
                float aura = exp2(-x * x * 3.2);

                // Narrow ivory core and a concentrated strike at the judgment line.
                // 和 lengthFade 綁在一起（原本是 1.18 / 0.72 ≈ 1.64 倍），不然芯和
                // 身體會在不同的高度收完，中間留下一截只有身體沒有芯的灰柱子。
                float whiteCore = exp2(-x * x * 9.0) * pow(saturate(1.0 - yn), _FadePower * 1.64);
                // **底下那一下也要收窄。**
                //
                // 它橫向本來只有 `1 - smoothstep(0.9, 1, x)` —— 也就是到 x=0.9
                // 之前一律滿值，所以不管柱子多窄，底部永遠是**整個寬度**都在發白。
                // 柱子收成圓頂之後這件事更明顯：上面是一團有形狀的光，下面卻拖著
                // 一條跟它無關的、從頭亮到尾的橫帶。
                //
                // 加一條自己的橫向衰減。它仍然比柱子本體寬（_StrikeWidth 5.0 比
                // whiteCore 的 9.0 鬆），因為它要讀成「打在線上」而不是「柱子的
                // 一部分」—— 完全跟著柱子收的話，判定線上那一下就不見了。
                // smoothstep 那一項留著，保證在 quad 邊緣剛好是 0，不會有接縫。
                float strike = exp2(-y * y * 110.0) * exp2(-x * x * _StrikeWidth)
                             * (1.0 - smoothstep(0.9, 1.0, x));

                // 這裡本來還有一條流動的焦散線（`pow(0.5+0.5*sin(y*42 - _Time.y*28
                // + x*5), 10)`）。`+ x*5` 那一項讓同一條線在左右兩邊相位不同，所以
                // 它畫出來不是一條橫線，而是一排**往上的箭頭**疊在光柱上 —— 那是
                // 線條，不是光。整條拿掉，柱子只剩 aura、whiteCore 和判定線上那一下。
                //
                // 連帶死掉的還有外面餵進來的 _ShimmerStrength（收束時會拉到 0.34
                // 讓柱子「回流」時更亮一點）。那個亮度現在由 alpha 自己收，
                // 見 NoteJudgementMeshManager 的 Releasing 分支。

                // **alpha 吃到 1 就等於給自己畫了一條邊。**
                //
                // alphaShape 在中段是 1.65，saturate 之後整片都是 1 —— 於是柱子
                // 內部完全不透明、外面立刻掉到 0，中間只剩幾個像素在變化，而那
                // 幾個像素就是看起來的那條描邊。加法混合又把 rgb 疊到白點以上，
                // 等於**切兩次**。
                //
                // 0.65 讓峰值落在 1 以下，整條斜邊從此是一段連續的漸層。代價是
                // 整體暗一些（加法混合的貢獻是 rgb × alpha），那是換來沒有邊的價錢。
                float vertexAlpha = saturate(input.color.a * _Color.a);
                float alphaShape = (aura * 0.50 + whiteCore * 1.15 + strike * 1.35) * _AlphaGain;
                float alpha = saturate(alphaShape * softEdge * lengthFade * vertexAlpha);

                // 力度彈對：柱底留判定色，往上走向 _TipColor。
                //
                // 漸層必須做在這裡而不是頂點色 —— 這個 shader 只讀 input.color 的
                // **alpha**，RGB 全部來自 _Color，所以外面怎麼寫頂點色都不會進來。
                // 用 yn 不是 y：漸層要走完**自己那一欄**的高度。拿 y 的話邊上
                // 那幾欄在淡掉之前只走到漸層的前三分之一，紫色只有正中間看得到。
                float tipRamp = saturate(yn) * _TipBlend;

                float3 tint = max(_Color.rgb, float3(0.001, 0.001, 0.001));
                tint = lerp(tint, _TipColor.rgb, tipRamp);

                // 完全完美：**一條橫向的漸層 —— 中間金、兩側藍、最外緣才是紫。**
                //
                // 欄杆拿掉之後只剩一團從中心往外衰減的光，所以這三個顏色是疊在
                // **同一條亮度斜坡**上的：中間最亮的地方是金，光開始暗下去的肩部
                // 是藍，快消失的那一圈是紫。不必做遮罩，位置由 x 直接說。
                //
                // **紫不用垂直漸層帶。** 拿 _TipColor 往上走的話，整根柱子的上半
                // 都是紫，那就不是「只做邊緣」。紫只由 x 決定。
                //
                // 起點往外推過（0.35/0.78/0.80 -> 0.46/0.86/0.88）：那組數字是對著
                // 欄杆的位置（x≈0.76）訂的，欄杆沒了之後同一組數字會讓藍攤掉半根
                // 柱子，看起來是「藍柱子中間一道金」而不是「金柱子帶藍邊」。
                //
                // 中央那一團也走這條漸層，所以不必再為 aura 開一個 tint；
                // 核心（whiteCore）另外用 _FlawlessCore，見下面。
                float sideRamp = smoothstep(_FlawlessSideStart, _FlawlessSideEnd, x);
                float rimRamp = smoothstep(_FlawlessRimStart, 1.0, x);
                float3 flTint = lerp(_FlawlessCore.rgb, _FlawlessSide.rgb, sideRamp);
                flTint = lerp(flTint, _FlawlessRim.rgb, rimRamp);
                float3 bodyTint = lerp(tint, flTint, _FlawlessBlend);

                // 核心是寫死的暖白乘 _CoreIntensity（8），比外圈亮一個量級。只染外
                // 圈的話，柱子中間那條白仍然會把顏色壓成「白裡透色」—— 所以核心也要
                // 跟著走，只是慢一截：判定線上那一下（strike）要維持白，它是打擊的
                // 瞬間而不是這根柱子的性格。
                float3 warmWhite = float3(1.0, 0.965, 0.82);
                float3 coreTint = lerp(warmWhite, _TipColor.rgb, tipRamp * 0.72);
                // 完全完美的中央是金的，而且**不跟著往紫走** —— 中間要是金，
                // 上下都是。藍紫只在外緣說。
                coreTint = lerp(coreTint, _FlawlessCore.rgb, _FlawlessBlend);
                float coreGain = lerp(1.0, _FlawlessCoreGain, _FlawlessBlend);
                float3 colouredGlow = bodyTint * aura * 0.70 * _GlowIntensity;
                float3 coreGlow = coreTint * whiteCore * _CoreIntensity * coreGain
                                + warmWhite * strike * 1.4 * _CoreIntensity
                                  * lerp(1.0, 0.78, _FlawlessBlend);

                return half4(colouredGlow + coreGlow, alpha);
            }
            ENDHLSL
        }
    }
}
