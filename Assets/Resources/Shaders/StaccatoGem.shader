// Staccato gem inlay (tools/make_staccato_gem.py, NoteController.ApplyStaccatoGem).
//
// The note frame underneath is additive HDR glass (GlassThemeSprite, Blend SrcAlpha One)
// and Bloom spreads it further, so a plain alpha-blended sprite on top just washes
// out. This pass is premultiplied and solid where the texture is, so the frame's glow
// cannot shine through the gem, and it pushes the texture's highlights — the polished
// gold, facet glints and the sparkle — over the bloom threshold so the centre is the
// brightest thing on the note. ZTest Always for the same reason as the frame: notes
// must stay in front of the raised track walls.
//
// Lives under Resources so Shader.Find still finds it in a player build.
Shader "Custom/StaccatoGem"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BodyGain ("Body Brightness", Range(0, 3)) = 1.25
        _HighlightGlow ("Highlight HDR Glow", Range(0, 8)) = 3.2
        _HighlightThreshold ("Highlight Threshold", Range(0, 1)) = 0.5
        _PulseAmount ("Sparkle Pulse", Range(0, 1)) = 0.35
        _PulseSpeed ("Sparkle Pulse Speed", Range(0, 12)) = 5.0
        [HideInInspector] _Color ("Renderer Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _BodyGain;
            float _HighlightGlow;
            float _HighlightThreshold;
            float _PulseAmount;
            float _PulseSpeed;

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
                float alpha = tex.a * i.color.a;
                float peak = max(tex.r, max(tex.g, tex.b));
                float highlight = smoothstep(_HighlightThreshold, 1.0, peak);
                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed);
                float3 rgb = tex.rgb * i.color.rgb;
                rgb = rgb * _BodyGain + rgb * highlight * highlight * _HighlightGlow * pulse;
                return half4(rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
