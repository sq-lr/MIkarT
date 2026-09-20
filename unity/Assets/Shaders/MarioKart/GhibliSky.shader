Shader "MarioKart/GhibliSky"
{
    Properties
    {
        _SkyTop ("Zenith", Color) = (0.55, 0.73, 0.88, 1)
        _SkyHorizon ("Horizon", Color) = (0.95, 0.90, 0.76, 1)
        _SkyGround ("Below", Color) = (0.62, 0.72, 0.48, 1)
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

            float4 _SkyTop, _SkyHorizon, _SkyGround;

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
                float3 col = lerp(_SkyHorizon.rgb, _SkyTop.rgb, pow(up, 0.55));
                col = lerp(col, _SkyGround.rgb, pow(down, 0.45));
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
