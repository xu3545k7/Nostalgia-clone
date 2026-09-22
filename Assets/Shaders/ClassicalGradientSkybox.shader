Shader "Custom/ClassicalGradientSkybox"
{
    Properties
    {
        _BottomColor ("Bottom", Color) = (0.012, 0.006, 0.008, 1)
        _HorizonColor ("Horizon", Color) = (0.095, 0.018, 0.025, 1)
        _TopColor ("Top", Color) = (0.018, 0.008, 0.012, 1)
        _HorizonHeight ("Horizon Height", Range(-1,1)) = -0.05
        _BlendSoftness ("Blend Softness", Range(0.05,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BottomColor;
            fixed4 _HorizonColor;
            fixed4 _TopColor;
            float _HorizonHeight;
            float _BlendSoftness;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float y = normalize(i.direction).y - _HorizonHeight;
                float lower = smoothstep(-_BlendSoftness, 0.0, y);
                float upper = smoothstep(0.0, _BlendSoftness, y);
                fixed3 color = lerp(_BottomColor.rgb, _HorizonColor.rgb, lower);
                color = lerp(color, _TopColor.rgb, upper);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
