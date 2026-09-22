Shader "Nostalgia/Player View Marble Track"
{
    Properties
    {
        [MainTexture] _BaseMap ("Marble Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Brightness", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Compatibility Color", Color) = (1, 1, 1, 1)
        [Enum(Black Marble,0,White Marble,1)] _Theme ("Marble Theme", Float) = 0
        _ViewLightStrength ("Player View Light", Range(0, 0.25)) = 0.08
        [HideInInspector] _ScrollOffset ("Note-synchronised Scroll", Float) = 0
        [HideInInspector] _JudgmentZ ("Judgment Line World Z", Float) = -10000

        // ── 玻璃化 ───────────────────────────────────────────────────────
        // 0 = 實心大理石，1 = 軌道看起來像一片玻璃，透出後面那片天空。
        //
        // **實作上軌道仍然是不透明的**，它只是自己把天空的漸層畫出來。真的做成
        // 透明混合試過，結果是災難：URP 的順序是「不透明 → 天空盒 → 透明」，
        // 軌道一旦不寫深度並搬到天空盒之後，天空盒就會把整片跑道重畫一次，
        // 連帶把畫在 2009/2010 的踏板音符和導引線一起蓋掉。
        //
        // 自己畫天空沒有這個問題：一個 queue 都不用動，也不用碰別人的材質。
        // 金色塵埃在 queue 3100，本來就飄在軌道前面，所以「透出來」的觀感是完整的。
        _Glass ("Glass Amount", Range(0, 1)) = 0
        // 玻璃最多讓多少天空取代大理石。1 = 完全看不到大理石。
        _GlassMax ("Glass Max", Range(0.2, 1)) = 0.85
        // 天空的三段顏色，由 BackgroundHarmonyDriver 推進來，和天空盒同一組值。
        [HideInInspector] _SkyBottom ("Sky Bottom", Color) = (0.012, 0.006, 0.008, 1)
        [HideInInspector] _SkyHorizon ("Sky Horizon", Color) = (0.095, 0.018, 0.025, 1)
        [HideInInspector] _SkyTop ("Sky Top", Color) = (0.018, 0.008, 0.012, 1)
        // 曲繪。天空的亮度只有 0.07~0.29，透出來就是一片黑——玻璃要透出「有內容
        // 的東西」才有意義，而 97 份譜面全部都有封面。沒有封面時 _CoverAmount 是
        // 0，就退回上面那片天空。
        [HideInInspector] _CoverTex ("Cover", 2D) = "black" {}
        [HideInInspector] _CoverAmount ("Cover Amount", Range(0, 1)) = 0
        _CoverBrightness ("Cover Brightness", Range(0, 1)) = 0.45
        // 影片背景要改用**螢幕座標**取樣。用軌道 UV 的話影片會順著跑道鋪上去、
        // 跟著跑道往遠處延伸，看起來就是「影片放在軌道上」——靜態曲繪那樣做是
        // 對的（它本來就該是背景的延伸），會動的影片不行。改成螢幕座標之後，
        // 軌道上取到的正好是它擋住的那幾個畫素，接縫對得起來，才是「透明的軌道
        // 疊在背景影片上」。
        [HideInInspector] _CoverScreenSpace ("Cover In Screen Space", Float) = 0
        // 影片那張 RawImage 在螢幕上的位置（等比例縮放後會有黑邊，不能假設滿版）。
        // xy = 1/寬高，zw = -左下角/寬高，uv = 畫素座標 * xy + zw。
        [HideInInspector] _CoverRect ("Cover Rect", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }
        ZWrite On
        ZTest LEqual
        Cull Off
        // TRACK is an opaque black-marble/crystal surface. Pedal cues are a
        // restrained transparent overlay and no longer require seeing through it.

        Pass
        {
            Name "PlayerViewMarble"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                // 未經 TRANSFORM_TEX 的原始 UV。大理石要平鋪所以吃縮放過的 uv，
                // 但封面只能鋪一次——用同一組的話整張圖會沿跑道重複成壁紙。
                float2 rawUv : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_CoverTex);
            SAMPLER(sampler_CoverTex);
            float4 _BaseMap_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                half _Theme;
                half _ViewLightStrength;
                float _ScrollOffset;
                float _JudgmentZ;
                half _Glass;
                half _GlassMax;
                half _CoverAmount;
                half _CoverBrightness;
                half _CoverScreenSpace;
                float4 _CoverRect;
                half4 _SkyBottom;
                half4 _SkyHorizon;
                half4 _SkyTop;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                // Track is intentionally flattened to zero height, so transform the
                // direction without inverse-scale normal correction.
                output.normalWS = SafeNormalize(TransformObjectToWorldDir(input.normalOS));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.rawUv = input.uv;
                return output;
            }

            half DecorativeLineMask(half3 colour)
            {
                // The source image's straight inlays are warm brass, while the
                // natural marble veins are neutral grey.
                return smoothstep(0.018h, 0.085h, colour.r - colour.b)
                     * smoothstep(0.010h, 0.060h, colour.r - colour.g);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // TRACK exists only on the runway side of the judgment line. The
                // keyboard owns the player side, so marble fragments past the line
                // are discarded instead of continuing underneath it.
                clip(input.positionWS.z - _JudgmentZ);

                float2 movingUv = input.uv + float2(0.0, _ScrollOffset);
                half3 source = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, movingUv).rgb;
                half lineMask = DecorativeLineMask(source);

                // Replace baked diagonal/vertical brass lines with nearby stone.
                // Sampling across a few texels preserves fine marble cracks while
                // removing only the geometrically straight coloured decoration.
                float2 dx = float2(_BaseMap_TexelSize.x * 3.0, 0.0);
                float2 dy = float2(0.0, _BaseMap_TexelSize.y * 3.0);
                half3 sampleL = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, movingUv - dx).rgb;
                half3 sampleR = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, movingUv + dx).rgb;
                half3 sampleD = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, movingUv - dy).rgb;
                half3 sampleU = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, movingUv + dy).rgb;
                half weightL = 1.0h - DecorativeLineMask(sampleL);
                half weightR = 1.0h - DecorativeLineMask(sampleR);
                half weightD = 1.0h - DecorativeLineMask(sampleD);
                half weightU = 1.0h - DecorativeLineMask(sampleU);
                half weightSum = max(0.001h, weightL + weightR + weightD + weightU);
                half3 nearbyStone = (sampleL * weightL + sampleR * weightR
                                   + sampleD * weightD + sampleU * weightU) / weightSum;
                source = lerp(source, nearbyStone, saturate(lineMask * 1.8h));

                half luminance = dot(source, half3(0.299h, 0.587h, 0.114h));

                // Opaque black marble. Preserve enough of the authored stone
                // luminance for its natural veins to remain readable; the earlier
                // crystal pass multiplied this by 0.27 and made it look flat black.
                half sourceGrey = dot(source, half3(0.299h, 0.587h, 0.114h));
                half3 neutralVein = lerp(sourceGrey.xxx, source, 0.10h);
                half stoneContrast = saturate((sourceGrey - 0.018h) * 1.18h + 0.018h);
                half naturalVeinLift = smoothstep(0.065h, 0.24h, sourceGrey);
                half3 blackMarble = lerp(stoneContrast.xxx, neutralVein, 0.22h)
                                  + naturalVeinLift * half3(0.026h, 0.024h, 0.021h)
                                  + half3(0.004h, 0.003h, 0.0025h);

                // Bright ivory stone with darker veins derived from the same marble detail.
                half vein = smoothstep(0.075h, 0.30h, luminance);
                half fineDetail = saturate((source.r - source.b) * 0.6h + luminance * 0.18h);
                half3 whiteMarble = lerp(half3(0.84h, 0.825h, 0.78h),
                                         half3(0.39h, 0.41h, 0.43h), vein);
                whiteMarble += (fineDetail - 0.04h) * half3(0.06h, 0.055h, 0.045h);

                half theme = step(0.5h, _Theme);
                half3 marble = lerp(blackMarble, whiteMarble, theme);

                // The sheen is driven by the camera/player direction. On the flat track it
                // remains centered and symmetrical instead of depending on a world-side light.
                half3 viewDirection = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                half facing = saturate(dot(SafeNormalize(input.normalWS), viewDirection));
                half playerSheen = pow(facing, 4.0h) * _ViewLightStrength;
                // _ViewLightStrength is a linear-light value. Applying all of its
                // 0.08 default to black stone lifts it to a visibly pale grey after
                // display gamma. Black marble only needs a restrained polish; the
                // white theme retains the full player-facing illumination.
                half sheenStrength = lerp(0.12h, 1.0h, theme);
                marble += playerSheen * sheenStrength
                        * lerp(half3(0.84h, 0.81h, 0.76h),
                               half3(1.0h, 0.98h, 0.91h), theme);

                half3 tinted = marble * _BaseColor.rgb;

                // 玻璃化：把後面那片天空自己畫出來。跑道遠端對應天空的上緣、
                // 判定線那端對應下緣，所以沿 UV 的 v 取同一條三段漸層——玩家看到的
                // 就是「跑道底下是同一片天空」，而不是一塊被打亮的板子。
                half skyT = saturate(input.rawUv.y);
                half3 sky = lerp(_SkyBottom.rgb, _SkyHorizon.rgb, saturate(skyT / 0.45h));
                sky = lerp(sky, _SkyTop.rgb, saturate((skyT - 0.45h) / 0.55h));

                // 有封面就讓它取代那片天空。靜態曲繪用軌道的 UV，所以圖是「鋪在
                // 跑道上順著跑道往遠處延伸」，而不是貼在一塊平板上。影片則走螢幕
                // 座標（見 _CoverScreenSpace）。
                float2 coverUv = input.rawUv;
                if (_CoverScreenSpace > 0.5h)
                {
                    // SV_POSITION 在 D3D 是左上為原點，Unity 的螢幕座標是左下，
                    // 兩邊要對齊才不會上下顛倒。
                    float2 pixel = input.positionHCS.xy;
                    #if UNITY_UV_STARTS_AT_TOP
                    pixel.y = _ScreenParams.y - pixel.y;
                    #endif
                    coverUv = saturate(pixel * _CoverRect.xy + _CoverRect.zw);
                    // VideoPlayer RenderTextures arrive with the opposite
                    // vertical orientation to the ordinary cover textures used
                    // by this shader. _CoverScreenSpace is enabled only for that
                    // video path, so invert it once here.
                    coverUv.y = 1.0 - coverUv.y;
                }
                half3 cover = SAMPLE_TEXTURE2D(_CoverTex, sampler_CoverTex, coverUv).rgb;
                sky = lerp(sky, cover * _CoverBrightness, _CoverAmount);

                // 大理石的紋理不會整個消失：即使全玻璃也留一點，跑道才不會變成
                // 一塊純色。留的是亮度變化，不是顏色。
                half vein2 = dot(tinted, half3(0.299h, 0.587h, 0.114h));
                sky *= 0.85h + vein2 * 0.6h;

                // Black marble receives only a very faint, nearly achromatic
                // reflection so the stone texture remains dominant. White marble
                // keeps the original coloured-reflection behaviour.
                half skyLuminance = dot(sky, half3(0.299h, 0.587h, 0.114h));
                half3 neutralReflection = lerp(skyLuminance.xxx, sky, 0.10h);

                // Black marble joins the glass only while a video is the cover.
                //
                // Saved settings from the earlier transparent-track design can
                // still push _Glass to 100%, and honouring that on black marble
                // replaced the stone with a flat background colour the moment
                // gameplay started — which is why it was cut out entirely. That
                // failure needs something behind the track worth seeing, and a
                // video is exactly that: _CoverScreenSpace is raised only for the
                // video path, so the old case stays untouched and the stone can
                // still turn to glass over a moving background.
                //
                // The same factor drives the colour blend, or the video would
                // come through the black stone almost fully desaturated.
                // Both flags, not just the screen-space one. _CoverScreenSpace is
                // pushed when a video starts and is only cleared by a later full
                // re-apply; _CoverAmount is what actually falls to zero the moment
                // the cover goes away. Gating on the first alone would leave black
                // marble stranded in glass after a video song and carry that into
                // every song played afterwards.
                half videoCover = step(0.5h, _CoverScreenSpace) * step(0.5h, _CoverAmount);
                half glassTheme = saturate(theme + videoCover);
                half3 reflectedEnvironment = lerp(neutralReflection, sky, glassTheme);
                half reflectionAmount = saturate(_Glass) * _GlassMax * glassTheme;
                half3 glassed = lerp(tinted, reflectedEnvironment, reflectionAmount);
                return half4(glassed, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
