// Cel-shaded surface with a dark inverted-hull outline. Built-in render
// pipeline (see docs/decisions/0001 and 0008). Lives under Resources/ so
// it is always included in builds and Shader.Find works at runtime.
Shader "MarioKart/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.5
        _SpecStrength ("Specular Dot", Range(0, 1)) = 0
        _OutlineColor ("Outline Ink", Color) = (0.06, 0.05, 0.08, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.05)) = 0.012
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        // ---- Outline: back faces pushed out along the normal ----------------
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            fixed4 _Color;
            float _OutlineWidth;
            half _ToonOutlineInk; // global: 0 = darkened object colour, 1 = pure ink

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                float4 clip = UnityObjectToClipPos(v.vertex);
                // Offset in clip space along the projected view-space normal
                // (TransformViewToProjection folds in the aspect ratio),
                // scaled by w so the outline keeps a constant screen width
                // regardless of distance.
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                float2 offset = TransformViewToProjection(viewNormal.xy);
                clip.xy += offset * _OutlineWidth * clip.w;
                o.pos = clip;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // A line in the object's own colour, darkened, reads as
                // painted; pure ink reads as comic. _ToonOutlineInk blends.
                fixed3 tinted = _Color.rgb * 0.3;
                return fixed4(lerp(tinted, _OutlineColor.rgb, _ToonOutlineInk), 1);
            }
            ENDCG
        }

        // ---- Toon-lit surface ----------------------------------------------
        CGPROGRAM
        #pragma surface surf Toon fullforwardshadows addshadow
        #pragma target 3.0
        #include "ToonCommon.cginc"
        ENDCG
    }

    Fallback "Diffuse"
}
