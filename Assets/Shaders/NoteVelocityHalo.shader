// Flat, unlit, vertex-coloured geometry drawn on the track: the dynamics
// wash and field, and the pedal's bracket at each side.
//
// Why it needs its own shader rather than Sprites/Default: the ring carries its
// gradient entirely in vertex colour and has no texture at all. Sprites/Default
// samples _MainTex and mixes in per-renderer data that only the sprite pipeline
// fills in, so driving it from a MeshRenderer means relying on defaults that are
// not ours to rely on. Twenty lines here is cheaper than that coupling.
//
// It keeps a normal depth test, unlike the crystal's overlay: this ring lies in
// the note's own plane just above the track, so it should be occluded by
// anything genuinely in front of it.
Shader "Nostalgia/FlatUnlitVertexColour"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
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
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
