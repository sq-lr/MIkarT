Shader "MarioKart/GhibliLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.42, 0.52, 0.48, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.38
        _ShadowSoftness ("Shadow Softness", Range(0.01, 0.6)) = 0.32
        _Fill ("Fill", Range(0, 0.5)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Ghibli fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _ShadowColor;
        half _ShadowThreshold;
        half _ShadowSoftness;
        half _Fill;

        struct Input { float2 uv_MainTex; };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }

        half4 LightingGhibli(SurfaceOutput s, half3 lightDir, half atten)
        {
            half wrap = dot(s.Normal, lightDir) * 0.5 + 0.5;
            half shade = smoothstep(_ShadowThreshold, _ShadowThreshold + _ShadowSoftness, wrap * atten);
            half3 lit = s.Albedo * _LightColor0.rgb;
            half3 sh = s.Albedo * _ShadowColor.rgb;
            half3 col = lerp(sh, lit, shade) + s.Albedo * _Fill;
            return half4(col, s.Alpha);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
