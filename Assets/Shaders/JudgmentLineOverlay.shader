Shader "Nostalgia/JudgmentLineOverlay"
{
    // URP unlit overlay used by the judgment-line bar.
    //  - ZTest Always  -> the bar ALWAYS draws on top of the keyboard / notes, never occluded.
    //  - Additive blend -> it reads as a glow over the dark track.
    //  - _EmissionColor is driven per-renderer by JudgmentLineGlow (MaterialPropertyBlock) so the
    //    beat-line white pulse works with no extra wiring.
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        _Intensity ("Base Intensity", Range(0,8)) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "JudgmentLineOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One   // additive: glows over everything, never hidden by depth

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float  _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 col = _BaseColor.rgb * _BaseColor.a * _Intensity + _EmissionColor.rgb;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
