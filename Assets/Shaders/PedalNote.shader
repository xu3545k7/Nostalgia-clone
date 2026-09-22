Shader "Nostalgia/Pedal Note"
{
    // The body is a unit quad stretched between the head and the terminator, so its
    // depth profile cannot be baked into vertex colours: the ramps are a constant
    // length in chart time, which is a different fraction of every note. They arrive
    // per instance as fractions of the body instead, from a MaterialPropertyBlock.
    //
    // _TMin.._TMax says which part of the note this quad is drawing, so a note being
    // consumed at the judgment line keeps its ramps where the whole note put them
    // instead of re-running the gradient over whatever is left.
    //
    // The terminator line uses this same shader with both ramps at zero and all three
    // depths at one, which flattens it to a solid bar.
    //
    // This must stay in step with PedalNoteShape.AlphaAt, which is the tested copy.
    Properties
    {
        _Color ("Tint", Color) = (1, 0.52, 0.16, 1)
        _HeadRamp ("Head Ramp (fraction)", Range(0, 1)) = 0.1
        _TailRamp ("Tail Ramp (fraction)", Range(0, 1)) = 0.1
        _HeadAlpha ("Head Depth", Range(0, 1)) = 1
        _IdleAlpha ("Held Depth", Range(0, 1)) = 0.72
        _TailAlpha ("Terminator Depth", Range(0, 1)) = 1
        _TMin ("Drawn From", Range(0, 1)) = 0
        _TMax ("Drawn To", Range(0, 1)) = 1
        [HDR] _EdgeColor ("Brass Edge", Color) = (1.3, 0.72, 0.2, 1)
        _DarkColor ("Pedal-Up Boundary", Color) = (0.008, 0.005, 0.003, 1)
        _EdgeGlow ("Edge Glow", Range(0, 4)) = 1.15
        _EdgeWidth ("Edge Width", Range(0.005, 0.2)) = 0.045
    }

    SubShader
    {
        Tags
        {
            // Transparent cue over the opaque track, still below notes/effects.
            "Queue"="Geometry+9"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // The cue must remain readable on the opaque track. Its held state is made
        // almost transparent by PedalNoteRenderer instead of by the TRACK surface.
        ZTest Always
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "PedalNote"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _HeadRamp;
                float _TailRamp;
                float _HeadAlpha;
                float _IdleAlpha;
                float _TailAlpha;
                float _TMin;
                float _TMax;
                half4 _EdgeColor;
                half4 _DarkColor;
                float _EdgeGlow;
                float _EdgeWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // uv.y runs 0 at the head end of the quad to 1 at the terminator end;
                // t is where that lands in the note as a whole.
                float t = lerp(_TMin, _TMax, saturate(input.uv.y));

                float head = _HeadRamp > 0 ? 1.0 - saturate(t / _HeadRamp) : 0.0;
                float tail = _TailRamp > 0 ? 1.0 - saturate((1.0 - t) / _TailRamp) : 0.0;

                // Squared so each end's darkness is concentrated at the end itself,
                // and combined with max so a note too short to hold both ramps keeps
                // its darker end rather than averaging them away.
                float endDarkness = max(head * head, tail * tail);
                float depth = max(lerp(_IdleAlpha, _HeadAlpha, head * head),
                                  lerp(_IdleAlpha, _TailAlpha, tail * tail));

                // Alpha keeps the orange held region visible. Across the width, the
                // glass stays restrained while the two outside rails define it.
                float side = abs(input.uv.x * 2.0 - 1.0);
                float edge = smoothstep(1.0 - max(_EdgeWidth, 0.0001), 1.0, side);
                float glassProfile = lerp(0.52, 1.0, edge);
                half3 orangeGlass = _Color.rgb * glassProfile +
                                    _EdgeColor.rgb * edge * _EdgeGlow;
                // Held pedal stays orange. At either side of the real pedal-up gap,
                // colour (not alpha) approaches black: tail orange->black, then the
                // next head black->orange. This leaves a readable black interval
                // without stacking bright bars on the same timestamp.
                half3 rgb = lerp(orangeGlass, _DarkColor.rgb, endDarkness);
                // The broad centre is only a faint coloured glass wash, while the
                // two narrow rails keep enough alpha to make the pedal cue readable.
                // This avoids both failure modes: hiding the marble with a solid
                // rectangle, or making the complete pedal cue disappear.
                float alpha = _Color.a * depth * lerp(0.45, 1.0, edge);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
