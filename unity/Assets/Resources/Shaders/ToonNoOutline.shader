// MarioKart/Toon without the outline pass, for large flat surfaces (ground,
// road) and far horizon silhouettes where an outline adds nothing.
Shader "MarioKart/Toon (No Outline)"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.5
        _SpecStrength ("Specular Dot", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Toon fullforwardshadows addshadow
        #pragma target 3.0
        #include "ToonCommon.cginc"
        ENDCG
    }

    Fallback "Diffuse"
}
