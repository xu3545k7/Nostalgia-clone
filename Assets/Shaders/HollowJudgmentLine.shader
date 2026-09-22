Shader "Custom/HollowJudgmentLine"
{
    Properties
    {
        _Color ("Glass Tint", Color) = (0.64,0.82,0.86,0.32)
        _EdgeColor ("Platinum Edge", Color) = (0.94,1,1,0.92)
        [HDR] _EmissionColor ("Pulse Color", Color) = (0,0,0,0)
        _BorderWidth ("Bevel Width", Range(0.01,0.3)) = 0.075
        _InnerLineWidth ("Fine Rail Width", Range(0.001,0.08)) = 0.018
        _FillOpacity ("Glass Fill", Range(0,1)) = 0.22
        _CoreWidth ("Light Core Width", Range(0.005,0.25)) = 0.055
        _EndCut ("Chamfered Ends", Range(0,0.2)) = 0.08
        _PulseStrength ("Beat Pulse Strength", Range(0,8)) = 3.5
        _ShimmerStrength ("Glass Shimmer", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue"="Transparent+30" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _EdgeColor;
            fixed4 _EmissionColor;
            float _BorderWidth;
            float _InnerLineWidth;
            float _FillOpacity;
            float _CoreWidth;
            float _EndCut;
            float _PulseStrength;
            float _ShimmerStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float aa = max(fwidth(p.x), fwidth(p.y)) * 1.5;

                // Cut all four end corners to produce the faceted glass-rail silhouette
                // used by the reference, while keeping the long centre section straight.
                float endLimit = 0.5 - _EndCut * saturate(abs(p.y) * 2.0);
                float insideX = 1.0 - smoothstep(endLimit - aa, endLimit + aa, abs(p.x));
                float insideY = 1.0 - smoothstep(0.5 - aa, 0.5 + aa, abs(p.y));
                float shape = saturate(insideX * insideY);

                float distanceToX = endLimit - abs(p.x);
                float distanceToY = 0.5 - abs(p.y);
                float edgeDistance = min(distanceToX, distanceToY);
                float bevel = 1.0 - smoothstep(0.0, _BorderWidth, edgeDistance);

                // Platinum rails, a soft glass body, and a narrow white core make the
                // line readable without reverting to a flat opaque rectangle.
                float fineRailDistance = abs(abs(p.y) - (0.5 - _BorderWidth * 1.7));
                float fineRail = 1.0 - smoothstep(_InnerLineWidth, _InnerLineWidth + aa, fineRailDistance);
                float lightCore = 1.0 - smoothstep(_CoreWidth, _CoreWidth + aa * 2.0, abs(p.y));
                float verticalShimmer = pow(saturate(1.0 - abs(p.y) * 2.0), 2.0) * _ShimmerStrength;

                float pulse = max(max(_EmissionColor.r, _EmissionColor.g), max(_EmissionColor.b, _EmissionColor.a));
                pulse *= _PulseStrength;

                fixed3 color = _Color.rgb * (_FillOpacity + verticalShimmer);
                color += _EdgeColor.rgb * (bevel * 0.82 + fineRail * 0.72 + lightCore * 0.52);
                color += _EmissionColor.rgb * (0.9 + bevel * 0.7 + lightCore * 1.25);
                color += _EdgeColor.rgb * pulse * (0.28 + bevel * 0.42 + lightCore * 0.95);

                float alpha = _FillOpacity * _Color.a;
                alpha += bevel * _EdgeColor.a * 0.72;
                alpha += fineRail * 0.52 + lightCore * 0.38;
                alpha += saturate(pulse) * (0.28 + lightCore * 0.55);
                alpha = saturate(alpha) * shape;

                return fixed4(color * shape, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
