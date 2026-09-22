// The shards of a broken centre crystal, drawn over everything.
//
// Why it needs its own shader: a plain sprite is depth-tested, and the burst is
// a camera-facing billboard standing in the middle of a track that slopes away
// from the camera. Its lower half is therefore *behind* the track surface in
// depth and gets eaten, which is exactly what the seal's own overlay shader
// exists to avoid. Same treatment here: no depth test, no depth write, drawn in
// the overlay queue.
//
// Why the glow multiplier: alpha blending puts rgb * alpha into the frame
// buffer, so a colour authored at 1 lands well under the bloom threshold on
// anything but a fully opaque pixel. The multiplier is applied before the blend
// so a bright shard still throws light as it fades.
Shader "Nostalgia/CrystalShardOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Glow ("Glow", Float) = 1.6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Glow;

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texel = tex2D(_MainTex, i.uv);
                fixed4 result = texel * i.color;
                result.rgb *= _Glow;
                return result;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
