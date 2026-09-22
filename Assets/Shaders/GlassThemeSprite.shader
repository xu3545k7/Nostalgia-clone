Shader "Custom/GlassThemeSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ThemeColor ("Glass Theme", Color) = (0.2745,0.6392,1,1)
        _Emission ("HDR Inner Glow", Range(0,4)) = 1.45
        _RimGlow ("Glass Rim Glow", Range(0,4)) = 1.3
        _ShimmerStrength ("Moving Reflection", Range(0,1)) = 0.2
        [HDR] _TapGlassColor ("Tap Inner Glass", Color) = (1,1,1,1)
        _TapGlassEnabled ("Tap Inner Glass Enabled", Range(0,1)) = 0
        _TapGlassOpacity ("Tap Inner Glass Opacity", Range(0,1)) = 0.42
        _TapGlassRimGlow ("Tap Inner Rim Glow", Range(0,4)) = 1.45
        _TapGlassInsetX ("Tap Inner Width", Range(0.1,0.48)) = 0.40
        _TapGlassInsetY ("Tap Inner Height", Range(0.05,0.45)) = 0.27
        _TapGlassBorder ("Tap Inner Border", Range(0.005,0.12)) = 0.035
        _TapGlassChamfer ("Tap Inner Chamfer", Range(0,0.15)) = 0.055
        [HideInInspector] _Color ("Renderer Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
          Cull Off
          Lighting Off
          ZWrite Off
          // Notes are the lowest visual-depth/foreground gameplay layer. They
          // must remain readable in front of the raised TRACK side walls.
          ZTest Always
        // Add the HDR body over the track so emission remains visible even
        // before Bloom is applied. Sprite alpha still preserves its silhouette.
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _ThemeColor;
            fixed4 _Color;
            float _Emission;
            float _RimGlow;
            float _ShimmerStrength;
            fixed4 _TapGlassColor;
            float _TapGlassEnabled;
            float _TapGlassOpacity;
            float _TapGlassRimGlow;
            float _TapGlassInsetX;
            float _TapGlassInsetY;
            float _TapGlassBorder;
            float _TapGlassChamfer;

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
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                float peak = max(tex.r, max(tex.g, tex.b));
                float floorValue = min(tex.r, min(tex.g, tex.b));
                float luminance = dot(tex.rgb, float3(0.299, 0.587, 0.114));

                // Detect the opaque side of the sprite silhouette. This keeps
                // the glow attached to the authored note instead of changing
                // its dimensions or producing a rectangular halo.
                float2 px = _MainTex_TexelSize.xy * 1.5;
                float neighbourAlpha = min(
                    min(tex2D(_MainTex, i.uv + float2(px.x, 0)).a,
                        tex2D(_MainTex, i.uv - float2(px.x, 0)).a),
                    min(tex2D(_MainTex, i.uv + float2(0, px.y)).a,
                        tex2D(_MainTex, i.uv - float2(0, px.y)).a));
                float rim = saturate(tex.a - neighbourAlpha);

                // Warm gold has a substantial green component and little blue.
                // Preserve it so only the glass body is remapped.
                float goldRatio = tex.g / max(0.001, tex.r);
                float goldMask = smoothstep(0.28, 0.48, goldRatio) *
                                 (1.0 - smoothstep(0.92, 1.04, goldRatio)) *
                                 smoothstep(0.08, 0.32, tex.r - tex.b) *
                                 smoothstep(0.22, 0.72, peak);

                float detail = lerp(0.58, 1.28, smoothstep(0.04, 0.90, luminance));
                float3 themed = _ThemeColor.rgb * detail;
                float highlight = smoothstep(0.72, 1.0, peak) *
                                  (0.35 + 0.65 * smoothstep(0.18, 0.82, floorValue));
                themed = lerp(themed, float3(1,1,1), highlight * 0.72);

                // A narrow travelling reflection gives the coloured glass some
                // life while remaining subtle enough for chart readability.
                float reflectionPhase = frac(i.uv.x * 0.62 + i.uv.y * 0.38 - _Time.y * 0.32);
                float reflection = pow(saturate(1.0 - abs(reflectionPhase - 0.5) * 12.0), 3.0)
                                   * _ShimmerStrength * tex.a;
                // Alpha supplies the full glass body; luminance only modulates
                // relief, so dark red and blue artwork also emits light.
                float innerCore = lerp(0.42, 1.0, smoothstep(0.08, 0.88, luminance)) * tex.a;
                float breathe = 0.94 + 0.06 * sin(_Time.y * 2.1 + i.uv.x * 3.0);
                float3 emission = _ThemeColor.rgb *
                    (innerCore * _Emission * 0.62 + rim * _RimGlow + reflection * _Emission) * breathe;
                emission += float3(1.0, 0.97, 0.88) * reflection * _Emission * 0.45;

                // White glass timing inset. Its per-renderer enable flag is set
                // by NoteController for TAP and an ordinary HOLD head; STAC and
                // SOFT keep their specialised outer material unchanged.
                // The inset follows the same faceted horizontal silhouette as the note artwork:
                // a translucent glass centre surrounded by one narrow platinum-white ring.
                float2 tapP = abs(i.uv - 0.5);
                float tapVertical = saturate(tapP.y / max(0.001, _TapGlassInsetY));
                float tapEndLimit = _TapGlassInsetX - _TapGlassChamfer * tapVertical;
                float tapAA = max(fwidth(i.uv.x), fwidth(i.uv.y)) * 1.5;
                float tapInsideX = 1.0 - smoothstep(tapEndLimit - tapAA, tapEndLimit + tapAA, tapP.x);
                float tapInsideY = 1.0 - smoothstep(_TapGlassInsetY - tapAA, _TapGlassInsetY + tapAA, tapP.y);
                float tapShape = saturate(tapInsideX * tapInsideY) * tex.a * saturate(_TapGlassEnabled);
                float tapEdgeDistance = min(tapEndLimit - tapP.x, _TapGlassInsetY - tapP.y);
                float tapRing = (1.0 - smoothstep(0.0, _TapGlassBorder, tapEdgeDistance)) * tapShape;
                float tapFill = smoothstep(0.0, _TapGlassBorder * 2.2, tapEdgeDistance) * tapShape;
                float tapTopGlint = (1.0 - smoothstep(_TapGlassBorder,
                    _TapGlassBorder + tapAA * 3.0,
                    abs(tapP.y - (_TapGlassInsetY - _TapGlassBorder * 1.8)))) * tapShape;

                themed = lerp(themed, _TapGlassColor.rgb,
                    saturate(tapFill * _TapGlassOpacity + tapRing * 0.52));
                emission += _TapGlassColor.rgb *
                    (tapRing * _TapGlassRimGlow + tapTopGlint * _TapGlassRimGlow * 0.42 +
                     tapFill * _TapGlassOpacity * 0.34);

                half4 output;
                output.rgb = lerp(themed, tex.rgb, goldMask) + emission;
                output.a = tex.a * i.color.a * _ThemeColor.a;
                return output;
            }
            ENDCG
        }
    }
    FallBack Off
}
