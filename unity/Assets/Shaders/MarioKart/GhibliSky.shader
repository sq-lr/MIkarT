Shader "MarioKart/GhibliSky"
{
    Properties
    {
        _SkyTop ("Zenith", Color) = (0.55, 0.73, 0.88, 1)
        _SkyUpperMid ("Upper Mid", Color) = (0.17, 0.55, 0.75, 1)
        _SkyLowerMid ("Lower Mid", Color) = (0.36, 0.76, 0.86, 1)
        _SkyHorizon ("Horizon", Color) = (0.95, 0.90, 0.76, 1)
        _SkyGround ("Below", Color) = (0.62, 0.72, 0.48, 1)
        _SunColor ("Sun Color", Color) = (1, 0.9, 0.6, 1)
        _SunDirection ("Sun Direction", Vector) = (0.2, 0.7, 0.6, 0)
        _SunSize ("Sun Size", Range(0.001, 0.2)) = 0.035
        _StarAmount ("Star Amount", Range(0, 1)) = 0
        _MoonColor ("Moon Color", Color) = (0.8, 0.9, 1, 1)
        _MoonDirection ("Moon Direction", Vector) = (-0.35, 0.55, 0.65, 0)
        _HorizonGlow ("Horizon Glow", Color) = (1, 0.8, 0.55, 1)
        _HorizonGlowAmount ("Horizon Glow Amount", Range(0, 2)) = 0.25
        _PainterlyBands ("Painterly Bands", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _SkyTop, _SkyUpperMid, _SkyLowerMid, _SkyHorizon, _SkyGround;
            float4 _SunColor, _SunDirection, _MoonColor, _MoonDirection;
            float4 _HorizonGlow;
            float _SunSize, _StarAmount, _HorizonGlowAmount, _PainterlyBands;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.dir = vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                float down = saturate(-d.y);
                float t = pow(up, 1.6);
                float3 col;
                if (t < 0.35)
                    col = lerp(_SkyHorizon.rgb, _SkyLowerMid.rgb, smoothstep(0.0, 0.35, t));
                else if (t < 0.65)
                    col = lerp(_SkyLowerMid.rgb, _SkyUpperMid.rgb, smoothstep(0.35, 0.65, t));
                else
                    col = lerp(_SkyUpperMid.rgb, _SkyTop.rgb, smoothstep(0.65, 1.0, t));
                col = lerp(col, _SkyGround.rgb, pow(down, 0.45));
                float band = sin(up * 18.0) * 0.012 * _PainterlyBands;
                col += band;
                float horizonHaze = pow(1.0 - saturate(abs(d.y) * 5.0), 2.0);
                col = lerp(col, _HorizonGlow.rgb, horizonHaze * _HorizonGlowAmount);

                float sunDot = saturate(dot(d, normalize(_SunDirection.xyz)));
                float sunDisc = smoothstep(1.0 - _SunSize * 1.8, 1.0 - _SunSize * 0.35, sunDot);
                float sunGlow = pow(sunDot, 18.0) * 0.16 + pow(sunDot, 180.0) * 0.9;
                col += (_SunColor.rgb * (sunDisc + sunGlow)) * (1.0 - _StarAmount);

                float moonDot = saturate(dot(d, normalize(_MoonDirection.xyz)));
                float moonGlow = pow(moonDot, 10.0) * 0.22;
                float moonDisc = smoothstep(0.998, 0.9995, moonDot) * _StarAmount;
                col += _MoonColor.rgb * (moonDisc * 1.8 + moonGlow * _StarAmount);

                float3 starCell = floor((d + 1.0) * 42.0);
                float starNoise = frac(sin(dot(starCell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * 1.7 + starNoise * 20.0);
                float star = step(0.985, starNoise) * step(0.05, d.y) * _StarAmount * twinkle;
                col += float3(0.72, 0.88, 1.0) * star;
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
