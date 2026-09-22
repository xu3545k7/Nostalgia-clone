// Flat, unlit, vertex-coloured geometry that *adds* light instead of covering
// what is behind it. The pedal's embers use it.
//
// Why a second shader rather than a flag on the first: alpha blending replaces
// what is under it, so a hundred half-transparent sparks overlapping stay
// exactly as bright as one of them -- the brightest a field of them can ever get
// is the brightest single spark. Additive accumulates, so a dense cluster reads
// as a hot core with a falloff, which is what a shower of embers actually looks
// like and the only way this gets past the bloom threshold without every vertex
// being authored at an absurd value.
//
// No depth write and a normal depth test: the embers float just over the track
// and should still be hidden by anything genuinely in front of them.
Shader "Nostalgia/FlatUnlitAdditive"
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
        Blend SrcAlpha One

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

            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }

            // uv.x = 離中心線多遠（0 在中間、1 在邊），uv.y = 沿著淡出方向走多遠
            // （0 在源頭、1 在末端）。兩軸都用平方的衰減。
            //
            // 這是「光」和「發亮的多邊形」之間真正的差別。頂點色只能在頂點之間
            // 做線性內插，所以形狀的輪廓永遠是一條硬邊 —— 而硬邊會被眼睛判定成
            // 一個有形狀的物件，不是光。在片段裡算衰減，每一個像素都有自己的
            // 亮度，邊緣就化開了，而且不必為此多畫任何一個三角形。
            fixed4 frag(v2f i) : SV_Target
            {
                float across = saturate(1.0 - i.uv.x * i.uv.x);
                float along = saturate(1.0 - i.uv.y);
                float fall = across * along * along;

                fixed4 result = i.color;
                result.a *= fall;
                return result;
            }
            ENDCG
        }
    }

    Fallback Off
}
