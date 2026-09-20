Shader "MarioKart/GhibliCloud"
{
    Properties
    {
        _Highlight ("Highlight", Color) = (1, 1, 1, 1)
        _Midtone ("Midtone", Color) = (0.78, 0.86, 0.88, 1)
        _Shadow ("Shadow", Color) = (0.36, 0.48, 0.59, 1)
        _LightDirection ("Light Direction", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" }
        Cull Back ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Highlight, _Midtone, _Shadow, _LightDirection;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.normal = UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float light = dot(normalize(i.normal), normalize(_LightDirection.xyz));
                if (light > 0.35) return _Highlight;
                if (light > -0.2) return _Midtone;
                return _Shadow;
            }
            ENDCG
        }
    }
}
