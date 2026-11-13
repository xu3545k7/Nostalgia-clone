Shader "Nostalgia/HoldMagicOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Glow ("Glow", Range(0,4)) = 1.00
        _GradientStrength ("Radial Gradient", Range(0,1)) = 1.00
        _StrokeHighlight ("Engraving Highlight", Range(0,1)) = 0.20
        _CoreBoost ("Stroke Core Heat", Range(0,1.5)) = 0.62
        _FeatherFloor ("Feather Floor", Range(0,1)) = 0.38
        _PulseStrength ("Breathing Glow", Range(0,0.2)) = 0.05
        // Maps this sprite's local quad radius onto the shared circle radius so
        // the three concentric layers sample one continuous gradient.
        _RadialScale ("Radial Scale", Range(0.05,4)) = 1.0
        _RadialBias ("Radial Bias", Range(-1,1)) = 0.0
        // Shatter. The seal is cut into wedges that slide out along their own
        // bisectors; _ShardSpread is the slide distance in art-UV units.
        _ShardCount ("Shard Count", Range(3,24)) = 8
        _ShardSpread ("Shard Spread", Range(0,0.5)) = 0.0
        // Quad padding. A shard that slides past the sprite rect has no
        // geometry left to be drawn on, so the burst renders on an enlarged
        // quad and zooms the UV back out to keep the art its original size.
        _UvZoom ("UV Zoom", Range(1,3)) = 1.0
        // Where the theme colour hands over to the gilding. Now a narrow step
        // rather than a long ramp: it is placed inside one of the seal's own
        // stroke gaps (r=0.50), so every engraved ring is wholly one colour
        // instead of being dyed half theme and half gold across its width.
        // Measured gaps: 0.19 / 0.412 / 0.50 / 0.688 / 0.802 / 0.912
        // (tools/preview_hold_magic.py --rings).
        _GradientStart ("Gilding Start", Range(0,1)) = 0.478
        _GradientEnd ("Gilding End", Range(0,1)) = 0.522
        // The highlight lags the ink's handover, so stroke highlights stay
        // theme-tinted further out. Without the lag the crossover band mixes a
        // pale theme tint with a pale gold and reads as a white ring.
        _HighlightGildingBias ("Highlight Gilding Lag", Range(0.25,4)) = 1.7
        // A straight RGB lerp between two colours this far apart in hue passes
        // through their desaturated average. Blue to gold are near-complementary,
        // so the middle of the ramp goes grey. This pushes the chroma back, and
        // only in the middle -- the ends are untouched.
        // Was 0.55 when the crossover spanned 0.40..0.78 and really did pass
        // through grey. Across a 0.044-wide step there is no grey to fix, and
        // the term now only lays a thin chromatic edge right on the seam --
        // which is worth keeping, but not at the old strength.
        _MidSaturation ("Crossover Saturation", Range(0,1.5)) = 0.25

        // ---- 多段：共用半徑上的色階 ----
        // 一條等寬的兩端漸層讀起來就是「一個顏色淡進另一個顏色」。這幾段讓
        // 半徑上有事情發生：白熱的核心 -> 本色 -> 壓深 -> 金 -> 暗金 -> 鉑亮的
        // 主環 -> 外緣收深。換色的半徑全部取自雕花**自己的空檔**，換在筆畫上
        // 只會把一圈染成兩半。
        // _StageStrength 0 = 完全不分段（給接觸核心與線條幾何用：那兩個沒有
        // 同心結構，硬切只會在它們身上切出莫名其妙的環）。
        _StageStrength ("Stage Strength", Range(0,1)) = 1.0
        _StageWidth ("Stage Step Width", Range(0.002,0.3)) = 0.022
        _StageCore ("Stage: Core Ends", Range(0,1)) = 0.190
        _StageDeep ("Stage: Theme Shadow", Range(0,1)) = 0.412
        _StageGoldDeep ("Stage: Gold Shadow", Range(0,1)) = 0.688
        _StageCrown ("Stage: Crown Ring", Range(0,1)) = 0.802
        _StageEdge ("Stage: Outer Edge", Range(0,1)) = 0.912
        // 本色那兩段的明暗。平常是 0（本色就是本色），「完全完美」會把金本體
        // 壓暗一點，讓它變成隔開翡翠和靛藍的那道分隔。
        _ToneBody ("Tone: Theme Body", Range(-1,1)) = 0.0
        _ToneGold ("Tone: Gold Body", Range(-1,1)) = 0.0
        _ToneCore ("Tone: Core", Range(-1,1)) = 0.75
        _ToneDeep ("Tone: Theme Shadow", Range(-1,1)) = -0.85
        _ToneGoldDeep ("Tone: Gold Shadow", Range(-1,1)) = -0.72
        _ToneCrown ("Tone: Crown", Range(-1,1)) = 0.45
        _ToneEdge ("Tone: Outer Edge", Range(-1,1)) = -0.82

        // ---- 藏色：每一段藏的第二個色系 ----
        // 同一個色相換明暗只是同一件事的強弱。讓一圈一圈讀得出來的是**凹下去
        // 的那幾段藏著另一個顏色**：金的凹處藏綠（銅鏽就是這樣來的）、紫的凹處
        // 藏藍（氧化銀）。亮處是新的、凹處是舊的，一圈雕花因此有年份。
        //
        // 而且藏的不是**一個**顏色，是一個**色系**：深／中／淺三支，同一個家族
        // 裡深的偏本色、淺的偏青（真實的藍顏料就是這樣排的 —— 靛偏紫、鈷在中間、
        // 天青偏青；綠也一樣，深的是橄欖銅鏽、淺的是青瓷）。用哪一支**跟著那一段
        // 自己的明暗走**（_AccentFollow）：亮的段配淺支、凹的段配深支，所以整個
        // 第二色系是被同一道光照著的，不是三個顏色輪流貼上去。
        //
        // 三支都由 C# 從本色算（ParticleEffectPlayer.SealAccent），不是手調表，
        // 所以每一種判定色都自動有自己的第二色系。
        _AccentCore ("Accent: Core", Range(0,1)) = 0.12
        // 本色那兩段（判定色本體、金本體）平常是 0：它們就是「本色」，吃了點綴
        // 就不是本色了。只有「完全完美」那一組會開 —— 那一下要的正是整圈都有
        // 第二個顏色在裡面流動的虹彩感。
        _AccentBody ("Accent: Theme Body", Range(0,1)) = 0.0
        _AccentGold ("Accent: Gold Body", Range(0,1)) = 0.0
        _AccentDeep ("Accent: Theme Shadow", Range(0,1)) = 0.60
        _AccentGoldDeep ("Accent: Gold Shadow", Range(0,1)) = 0.85
        _AccentCrown ("Accent: Crown", Range(0,1)) = 0.18
        _AccentEdge ("Accent: Outer Edge", Range(0,1)) = 0.80
        _AccentFollow ("Accent: Follows Light", Range(0,1)) = 0.90
        [HDR] _InnerAccentDeep ("Theme Accent Deep", Color) = (0.16,0.20,0.62,1)
        [HDR] _InnerAccentMid ("Theme Accent Mid", Color) = (0.34,0.58,1.35,1)
        [HDR] _InnerAccentLight ("Theme Accent Light", Color) = (0.92,1.25,1.70,1)
        [HDR] _OuterAccentDeep ("Gilding Accent Deep", Color) = (0.52,0.62,0.18,1)
        [HDR] _OuterAccentMid ("Gilding Accent Mid", Color) = (0.78,1.25,0.34,1)
        [HDR] _OuterAccentLight ("Gilding Accent Light", Color) = (1.05,1.62,0.92,1)
        // A sweeping specular band. Without one the seal has no directional
        // light at all: its colour is purely a function of radius, which is why
        // the gold reads as flat paint rather than metal.
        _SheenStrength ("Sheen", Range(0,4)) = 2.4
        _SheenSharpness ("Sheen Narrowness", Range(1,64)) = 5
        _SheenSpeed ("Sheen Speed", Range(-4,4)) = 0.9
        [HDR] _InnerColor ("Deep Theme Colour", Color) = (0.040,0.140,0.60,1)
        [HDR] _OuterColor ("Gilding Colour", Color) = (1.55,1.00,0.30,1)
        [HDR] _CoreHighlightColor ("Theme Highlight", Color) = (0.42,0.72,1.30,1)
        [HDR] _PlatinumColor ("Gilding Highlight", Color) = (1.75,1.45,0.86,1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Overlay"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        // Alpha blending preserves the hand colour. The previous additive blend
        // mixed the magic circle with the white judgment beam and clipped both
        // red and blue to white before Bloom was evaluated.
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Glow;
                float _GradientStrength;
                float _StrokeHighlight;
                float _CoreBoost;
                float _FeatherFloor;
                float _PulseStrength;
                float _RadialScale;
                float _RadialBias;
                float _ShardCount;
                float _ShardSpread;
                float _UvZoom;
                float _GradientStart;
                float _GradientEnd;
                float _HighlightGildingBias;
                float _MidSaturation;
                float _StageStrength;
                float _StageWidth;
                float _StageCore;
                float _StageDeep;
                float _StageGoldDeep;
                float _StageCrown;
                float _StageEdge;
                float _ToneBody;
                float _ToneGold;
                float _ToneCore;
                float _ToneDeep;
                float _ToneGoldDeep;
                float _ToneCrown;
                float _ToneEdge;
                float _AccentCore;
                float _AccentBody;
                float _AccentGold;
                float _AccentDeep;
                float _AccentGoldDeep;
                float _AccentCrown;
                float _AccentEdge;
                float _AccentFollow;
                float4 _InnerAccentDeep;
                float4 _InnerAccentMid;
                float4 _InnerAccentLight;
                float4 _OuterAccentDeep;
                float4 _OuterAccentMid;
                float4 _OuterAccentLight;
                float _SheenStrength;
                float _SheenSharpness;
                float _SheenSpeed;
                float4 _InnerColor;
                float4 _OuterColor;
                float4 _CoreHighlightColor;
                float4 _PlatinumColor;
            CBUFFER_END

            // 把顏色往「白熱」或「往深處」推。
            //
            // 往上推只**少量**去飽和（0.35）：往自己最亮的通道推太多，金會變成
            // 淡暖白、判定色會變成白 —— 兩個都是把顏色洗掉，而這個效果的重點
            // 正是顏色。亮度交給增益。也**不能 lerp 到 (1,1,1)**：這裡的顏色
            // 是 HDR（峰值 1.05~1.9），推向白等於調暗。
            //
            // 往下推要一邊變暗一邊加彩度。單純乘一個小於 1 的數會往灰色掉，而
            // 這是 SrcAlpha 混色，近黑的墨會從軌道上扣掉光，看起來是髒不是暗。
            half3 SealTone(half3 c, half tone)
            {
                half peak = max(c.r, max(c.g, c.b));
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                half3 up = (c + (peak - c) * 0.35h) * 1.45h;
                // 0.18，不是 0.55。量過：舊的凹段最暗只到線性 2.12，而整個印記最亮
                // 4.62 —— **每一段都在白點以上**，於是 tonemap 的肩部把它們壓成
                // 同一個白，顏色和層次都到不了畫面（使用者「自動演奏看不出差別」
                // 就是這個）。0.18 讓凹段落在 1.0 附近，範圍才打得開。
                half3 down = max((half3)0.0h, (luma + (c - luma) * 1.25h) * 0.18h);
                return c + (up - c) * saturate(tone) + (down - c) * saturate(-tone);
            }

            // 第二色系裡的位置：0 = 深、0.5 = 中、1 = 淺。
            half3 AccentFamily(half3 deep, half3 mid, half3 light, half t)
            {
                return t < 0.5h
                    ? lerp(deep, mid, saturate(t * 2.0h))
                    : lerp(mid, light, saturate((t - 0.5h) * 2.0h));
            }

            half StageStep(half current, half target, half radius, half edge, half width)
            {
                return lerp(current, target, smoothstep(edge - width, edge + width, radius));
            }

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
                float2 sheenOffset : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 centreWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.sheenOffset = positionWS.xy - centreWS.xy;
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 centreOffset = (input.uv - 0.5) * _UvZoom;
                float2 sampleUv = centreOffset + 0.5;
                half shardMask = 1.0h;

                if (_ShardSpread > 0.0001)
                {
                    float sector = 6.2831853 / max(3.0, _ShardCount);
                    float wedge = floor((atan2(centreOffset.y, centreOffset.x) + 3.1415927) / sector);
                    float bisector = -3.1415927 + (wedge + 0.5) * sector;
                    float2 source = centreOffset -
                        float2(cos(bisector), sin(bisector)) * _ShardSpread;

                    // A pixel belongs to this shard only while its source is
                    // still inside the wedge the shard was cut from. That test
                    // is what opens the gaps -- without it neighbouring wedges
                    // smear into each other instead of separating.
                    float delta = atan2(source.y, source.x) - bisector;
                    delta = atan2(sin(delta), cos(delta));
                    shardMask = abs(delta) <= sector * 0.5 ? 1.0h : 0.0h;

                    centreOffset = source;
                    sampleUv = source + 0.5;
                }

                // Clamp addressing would smear the sprite's edge row across the
                // padding, so anything sampled off the sheet is cut outright.
                float2 onSheet = step(0.0, sampleUv) * step(sampleUv, 1.0);
                shardMask *= onSheet.x * onSheet.y;

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUv);
                half coverage = tex.a * shardMask;
                half alpha = coverage * input.color.a;

                // Shared radius: each layer is scaled down relative to the
                // outermost ring, so _RadialScale rescales its local radius back
                // onto the whole circle. Without this every ring re-ran the full
                // deep->luminous ramp and the three read as unrelated bands.
                half localRadius = length(centreOffset) * 2.0h;
                half radius = saturate(localRadius * _RadialScale + _RadialBias);
                half radialRamp = smoothstep(_GradientStart, _GradientEnd, radius);

                // Deep theme colour in the body -> gilding at the rim. Both ends
                // stay clear of black: under SrcAlpha blending a near-black ink
                // subtracts light from the track instead of glowing, which is
                // what made the middle of the seal read as a dirty smudge.
                half gilding = radialRamp * _GradientStrength;
                half3 ink = lerp(_InnerColor.rgb, _OuterColor.rgb, gilding);
                // Peaks at the midpoint and vanishes at both ends, so the
                // endpoints stay exactly as authored while the crossover keeps
                // its colour instead of washing out to grey.
                half inkLuma = dot(ink, half3(0.2126h, 0.7152h, 0.0722h));
                half midBoost = 1.0h + _MidSaturation * 4.0h * gilding * (1.0h - gilding);
                ink = inkLuma + (ink - inkLuma) * midBoost;
                // The engraving highlight follows the same handover, so strokes
                // in the body catch a pale theme tint and only the outer ring
                // reads as polished gold.
                half3 highlight = lerp(_CoreHighlightColor.rgb, _PlatinumColor.rgb,
                    pow(gilding, _HighlightGildingBias));

                // 多段的明暗節奏。色相只在鍍金那一步換一次，亮暗則沿半徑走過
                // 六段，每一段都在雕花的空檔上交棒。墨與高光共用同一條色階，
                // 不然 _StrokeHighlight 的亮筆畫會把剛分出來的段又洗平。
                half goldSeam = (_GradientStart + _GradientEnd) * 0.5h;
                half tone = _ToneCore;
                tone = StageStep(tone, _ToneBody,       radius, _StageCore,     _StageWidth);
                tone = StageStep(tone, _ToneDeep,       radius, _StageDeep,     _StageWidth);
                tone = StageStep(tone, _ToneGold,       radius, goldSeam,       _StageWidth);
                tone = StageStep(tone, _ToneGoldDeep,   radius, _StageGoldDeep, _StageWidth);
                tone = StageStep(tone, _ToneCrown,      radius, _StageCrown,    _StageWidth);
                tone = StageStep(tone, _ToneEdge,       radius, _StageEdge,     _StageWidth);
                tone *= _StageStrength;

                // 藏色的**量**跟明暗相反：凹下去、壓深的那幾段才吃第二色系，
                // 凸起的段（本色、金本色）留本色。兩條曲線同相的話，整圈只會
                // 一起變深又一起變綠，讀起來還是一件事。
                half accent = _AccentCore;
                accent = StageStep(accent, _AccentBody,      radius, _StageCore,     _StageWidth);
                accent = StageStep(accent, _AccentDeep,      radius, _StageDeep,     _StageWidth);
                accent = StageStep(accent, _AccentGold,      radius, goldSeam,       _StageWidth);
                accent = StageStep(accent, _AccentGoldDeep,  radius, _StageGoldDeep, _StageWidth);
                accent = StageStep(accent, _AccentCrown,     radius, _StageCrown,    _StageWidth);
                accent = StageStep(accent, _AccentEdge,      radius, _StageEdge,     _StageWidth);
                accent *= _StageStrength;

                // 藏色的**選支**則跟著明暗走：亮的段配淺支、凹的段配深支。
                // 整個第二色系因此是被同一道光照著的，不是三個顏色輪流貼上去。
                half member = saturate(tone * _AccentFollow + 0.5h);
                half3 accentInk = lerp(
                    AccentFamily(_InnerAccentDeep.rgb, _InnerAccentMid.rgb,
                                 _InnerAccentLight.rgb, member),
                    AccentFamily(_OuterAccentDeep.rgb, _OuterAccentMid.rgb,
                                 _OuterAccentLight.rgb, member),
                    gilding);
                ink = lerp(ink, accentInk, accent);
                // 高光只吃一半的點綴：亮筆畫整個換色的話，雕花的立體感會被顏色
                // 蓋過去，讀起來像換了一張貼圖，而不是同一張的不同段。
                highlight = lerp(highlight, accentInk, accent * 0.7h);

                ink = SealTone(ink, tone);
                highlight = SealTone(highlight, tone * 0.6h);

                // The artwork's own RGB is a flat ivory (luma ~0.89 everywhere),
                // so the old pow(luma,9) engraving term was effectively constant.
                // Alpha is the channel that actually carries stroke weight: solid
                // stroke bodies take the ivory highlight, their hottest centres
                // get a brightness boost, and the anti-aliased feathering is
                // pulled back so fine filigree recedes instead of flooding.
                half stroke = smoothstep(0.80h, 1.0h, coverage);
                half hotCore = smoothstep(0.93h, 1.0h, coverage);
                half feather = lerp(_FeatherFloor, 1.0h, smoothstep(0.0h, 0.45h, coverage));

                ink = lerp(ink, highlight, stroke * _StrokeHighlight);
                ink *= (1.0h + _CoreBoost * hotCore) * feather;

                // One narrow band sweeping the whole seal, in world space, so
                // every ring catches it at the same moment as if lit from one
                // side. Landed on the stroke bodies rather than the whole quad:
                // it is the engraving catching the light, not a wash over it.
                half sweep = cos(atan2(input.sheenOffset.y, input.sheenOffset.x)
                    - _Time.y * _SheenSpeed);
                half sheen = pow(saturate(sweep), _SheenSharpness) * _SheenStrength;
                // Multiplied, not added, and weighted by coverage rather than
                // just the solid strokes. _Glow already puts the seal near the
                // top of the tonemap curve, so an additive highlight lands on
                // the shoulder and is compressed away to nothing; scaling what
                // is there survives, and letting the whole engraving catch it
                // reads as one light passing over rather than the thick strokes
                // blinking.
                ink *= 1.0h + sheen * coverage;

                // Low spatial frequency: the old radius*7 term laid visible
                // concentric brightness rings over the engraving.
                half pulse = 1.0h + sin(_Time.y * 2.0h + radius * 2.0h) * _PulseStrength;
                half3 rgb = ink * (_Glow * pulse);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
