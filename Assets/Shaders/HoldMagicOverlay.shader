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
        // Where the theme colour hands over to the gilding. A narrow band keeps
        // the crossover from washing out through desaturated grey, which is what
        // a wide blue-to-gold lerp does.
        _GradientStart ("Gilding Start", Range(0,1)) = 0.40
        _GradientEnd ("Gilding End", Range(0,1)) = 0.78
        // The highlight lags the ink's handover, so stroke highlights stay
        // theme-tinted further out. Without the lag the crossover band mixes a
        // pale theme tint with a pale gold and reads as a white ring.
        _HighlightGildingBias ("Highlight Gilding Lag", Range(0.25,4)) = 1.7
        // A straight RGB lerp between two colours this far apart in hue passes
        // through their desaturated average. Blue to gold are near-complementary,
        // so the middle of the ramp goes grey. This pushes the chroma back, and
        // only in the middle -- the ends are untouched.
        _MidSaturation ("Crossover Saturation", Range(0,1.5)) = 0.55
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
                float _SheenStrength;
                float _SheenSharpness;
                float _SheenSpeed;
                float4 _InnerColor;
                float4 _OuterColor;
                float4 _CoreHighlightColor;
                float4 _PlatinumColor;
            CBUFFER_END

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
