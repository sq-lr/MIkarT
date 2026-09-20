// Shared body of the MarioKart/Toon surface shaders: a stepped (cel) lighting
// model with comic-print halftone dots in the shadow bands, a hard rim
// light and a stepped specular dot. Included from Toon.shader and
// ToonNoOutline.shader, which only differ by the inverted-hull outline pass.
//
// Per-material properties are declared in each .shader's Properties block;
// the _Toon* uniforms below are deliberately NOT, so they resolve to shader
// globals that ToonStyle.ApplyPalette sets once per world for every toon
// material at the same time.
#ifndef MARIOKART_TOON_COMMON
#define MARIOKART_TOON_COMMON

sampler2D _MainTex;
fixed4 _Color;
half _RimStrength;
half _SpecStrength;

// Globals (see MarioKart.Rendering.ToonStyle).
half2 _ToonBands;       // x = shadow->mid threshold, y = mid->lit threshold, on half-Lambert 0..1
fixed4 _ToonShadowTint; // colour of the darkest band (multiplied into albedo)
fixed4 _ToonRimColor;   // rgb = rim colour, a = rim intensity
half _ToonRimPower;     // higher = thinner rim
half _ToonDotSize;      // halftone cell size in screen pixels
half _ToonDotStrength;  // 0 = flat bands only, 1 = full-size dots in the darkest band
half2 _ToonDotFade;     // view depth (m) at which dots start to fade, and are gone

struct Input
{
    float2 uv_MainTex;
    float3 viewDir;
    float4 screenPos;
};

// SurfaceOutput plus the halftone term: surf can see the screen position,
// the lighting function can't, so surf hands the dot-grid distance across.
struct SurfaceOutputToon
{
    fixed3 Albedo;
    fixed3 Normal;
    fixed3 Emission;
    half Specular; // stepped highlight strength (0 = none)
    fixed Gloss;
    fixed Alpha;
    half Dot;      // 0 at a dot centre, ~1 at the cell corner (screen-space grid); huge = no dot
};

void surf(Input IN, inout SurfaceOutputToon o)
{
    fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
    o.Albedo = c.rgb;
    o.Alpha = c.a;
    o.Specular = _SpecStrength;
    o.Gloss = 0;

    // Rim goes into Emission so it is added exactly once (the lighting
    // function below runs once per light).
    half facing = saturate(dot(normalize(IN.viewDir), o.Normal));
    half rim = pow(1.0h - facing, max(_ToonRimPower, 0.01h));
    rim = smoothstep(0.45h, 0.55h, rim); // hard-edged band
    o.Emission = _ToonRimColor.rgb * (rim * _ToonRimColor.a * _RimStrength);

    // Halftone grid in screen pixels, rotated 45 degrees as comic printing
    // does, so the dots read as a print pattern rather than a pixel grid.
    float2 px = IN.screenPos.xy / max(IN.screenPos.w, 0.0001) * _ScreenParams.xy;
    px = mul(float2x2(0.7071, -0.7071, 0.7071, 0.7071), px);
    float2 cell = frac(px / max(_ToonDotSize, 1.0h)) - 0.5;

    // Screen-space dots on far surfaces are just noise: fade them out with
    // view depth (screenPos.w) by inflating the distance so nothing counts
    // as "inside a dot" any more.
    half fade = 1.0h - smoothstep(_ToonDotFade.x, _ToonDotFade.y, IN.screenPos.w);
    o.Dot = length(cell) * 2.0 / max(fade, 0.001h);
}

half4 LightingToon(SurfaceOutputToon s, half3 lightDir, half3 viewDir, half atten)
{
    // Cast shadows are binary: a pixel is either in shadow (darkest band)
    // or not. Thresholding the shadow-map attenuation turns any penumbra
    // -- soft shadows, cascade filtering -- into a crisp ink edge.
    half shadow = step(0.5h, atten);

    // Half-Lambert so the back of an object is a lit-from-behind band
    // rather than pitch black, then split into three bands at two
    // thresholds: a wide lit band, a narrow mid band, and shadow.
    half lit = saturate(dot(s.Normal, lightDir) * 0.5h + 0.5h) * shadow;
    half band = (step(_ToonBands.x, lit) + step(_ToonBands.y, lit)) * 0.5h; // 0, 0.5, 1

    // Two shading models blended by _ToonDotStrength:
    //   flat     -- plain cel bands;
    //   halftone -- "ink on paper": lit everywhere except inside dots of
    //               shadow tint whose radius grows as the band gets darker
    //               (none in the lit band, nearly merging in the darkest).
    half3 flat = lerp(_ToonShadowTint.rgb, half3(1, 1, 1), band);
    half radius = 1.0h - band;
    half inDot = 1.0h - smoothstep(radius - 0.08h, radius + 0.08h, s.Dot);
    half3 halftone = lerp(half3(1, 1, 1), _ToonShadowTint.rgb, inDot);
    half3 shade = lerp(flat, halftone, saturate(_ToonDotStrength));

    // One hard specular dot in the lit band (glossy-toy look for the karts).
    half3 h = normalize(lightDir + normalize(viewDir));
    half ndh = saturate(dot(s.Normal, h));
    half spec = smoothstep(0.975h, 0.985h, ndh) * s.Specular * step(0.75h, band);

    half4 c;
    c.rgb = (s.Albedo * shade + spec) * _LightColor0.rgb;
    c.a = s.Alpha;
    return c;
}

#endif
