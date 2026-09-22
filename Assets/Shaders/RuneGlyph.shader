Shader "Nostalgia/RuneGlyph"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CoreThreshold ("Core Threshold", Range(0,1)) = 0.55
        _Glow ("Glow", Range(0,4)) = 1.0
        // The glyph texture is white; all colour comes from here. A single
        // tinted white stroke reads as flat chalk, so the stroke's solid core
        // and its falloff are given separate colours: pale in the middle,
        // saturated in the halo. That contrast is the glow.
        [HDR] _HaloColor ("Halo Colour", Color) = (0.22,0.62,1.35,1)
        [HDR] _CoreColor ("Core Colour", Color) = (0.80,0.94,1.25,1)
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
                float _CoreThreshold;
                float _Glow;
                float4 _HaloColor;
                float4 _CoreColor;
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
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 rgb = lerp(_HaloColor.rgb, _CoreColor.rgb,
                    smoothstep(_CoreThreshold, 1.0h, tex.a));
                return half4(rgb * _Glow, tex.a * input.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
