// Full-screen image effect: multiplies the frame by a faint, static paper
// grain so the cel-shaded world reads as printed rather than rendered.
// Sampled in screen pixels, so the fibre size does not change with
// resolution or viewport size (each split-screen camera runs its own copy).
// Driven by MarioKart.Rendering.PaperGrainEffect; see docs/decisions/0008.
Shader "Hidden/MarioKart/PaperGrain"
{
    Properties
    {
        _MainTex ("Frame", 2D) = "white" {}
        _GrainTex ("Grain", 2D) = "gray" {}
        _Strength ("Strength", Range(0, 0.3)) = 0.04
        _GrainScale ("Grain Scale (px per texel)", Float) = 1.5
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _GrainTex;
            float4 _GrainTex_TexelSize; // x = 1/width, y = 1/height
            float _Strength;
            float _GrainScale;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);

                // Screen pixel -> grain texel, tiling; the texture wraps.
                float2 px = i.uv * _ScreenParams.xy;
                float2 guv = px * _GrainTex_TexelSize.xy / max(_GrainScale, 0.01);
                fixed g = tex2D(_GrainTex, guv).r * 2.0 - 1.0; // -1..1, mean 0

                c.rgb *= 1.0 + g * _Strength;
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
