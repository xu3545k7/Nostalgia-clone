Shader "Custom/PremiumJudgmentGlow"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (1,0.82,0.42,1)
        _GlowIntensity ("Glow Intensity", Range(0,12)) = 5.2
        _CoreIntensity ("White Core", Range(0,12)) = 8.0
        _EdgeSoftness ("Edge Softness", Range(0.01,0.5)) = 0.18
        _ShimmerStrength ("Shimmer", Range(0,1)) = 0.16
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
                float _ShimmerStrength;
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

                // Soft silhouette removes the rectangular edge of the source quad.
                float softEdge = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, x);
                float lengthFade = pow(saturate(1.0 - y), 0.72);

                // Fine glass-like side rails with a soft coloured aura.
                float sideRail = exp2(-pow(abs(x - 0.76) * 13.0, 2.0));
                float aura = exp2(-x * x * 2.1);

                // Narrow ivory core and a concentrated strike at the judgment line.
                float whiteCore = exp2(-x * x * 9.0) * pow(saturate(1.0 - y), 1.18);
                float strike = exp2(-y * y * 110.0) * (1.0 - smoothstep(0.9, 1.0, x));

                // Very subtle moving caustic line; enough to feel like glass, not noise.
                float shimmerBand = 0.5 + 0.5 * sin(y * 42.0 - _Time.y * 28.0 + x * 5.0);
                float shimmer = pow(shimmerBand, 10.0) * _ShimmerStrength * lengthFade;

                float vertexAlpha = saturate(input.color.a * _Color.a);
                float alphaShape = (aura * 0.42 + sideRail * 0.7 + whiteCore * 1.15 + strike * 1.35 + shimmer);
                float alpha = saturate(alphaShape * softEdge * lengthFade * vertexAlpha);

                float3 tint = max(_Color.rgb, float3(0.001, 0.001, 0.001));
                float3 warmWhite = float3(1.0, 0.965, 0.82);
                float3 colouredGlow = tint * (aura * 0.55 + sideRail * 1.15 + shimmer) * _GlowIntensity;
                float3 coreGlow = warmWhite * (whiteCore + strike * 1.4) * _CoreIntensity;

                return half4(colouredGlow + coreGlow, alpha);
            }
            ENDHLSL
        }
    }
}
