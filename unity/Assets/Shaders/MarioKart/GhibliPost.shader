Shader "Hidden/MarioKart/GhibliPost"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Lift ("Lift", Range(0.7, 1.1)) = 0.90
        _Warmth ("Warmth", Range(0, 1)) = 0.35
        _Saturation ("Saturation", Range(0, 1.5)) = 0.88
        _Vignette ("Vignette", Range(0, 0.5)) = 0.18
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Lift, _Warmth, _Saturation, _Vignette;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                c.rgb = pow(max(c.rgb, 0.0001), _Lift);
                c.rgb = lerp(c.rgb, c.rgb * float3(1.08, 1.01, 0.90), _Warmth);
                float g = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp((fixed3)g, c.rgb, _Saturation);
                float2 v = i.uv * 2.0 - 1.0;
                float vig = saturate(1.0 - dot(v, v) * _Vignette);
                c.rgb *= lerp(0.84, 1.0, vig);
                return c;
            }
            ENDCG
        }
    }
}
