#version 460

#define GameVfx_csgo_environment_blend 0

#include "common/utils.slang"
#include "common/features.slang"
#include "csgo_environment_features.slang"

// MODES
#define S_MODE_DEPTH 0
#define S_MODE_OVERDRAW 0

#define IBL 1

in vec3 vFragPosition;

centroid in vec3 vCentroidNormalOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vTexCoord;
in vec4 vTexCoord2;
in vec4 vTintColor_ModelAmount;
centroid in vec4 vVertexColor_Alpha;
in vec4 vBlendColorTint;


#if (S_MODE_DEPTH == 0) && (S_MODE_OVERDRAW == 0)
    out vec4 outputColor;
#endif

uniform float g_flDebugBlendVis = 0;
float g_flDebugWeight2Out = -1.0;
vec3 g_vDebugCheckpoints[16] = vec3[16](vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0),vec3(0.0));

// Material 1
#if (F_ALPHA_TEST == 1)
    uniform float g_flAlphaTestReference1 = 0.5;
    uniform float g_flAntiAliasedEdgeStrength1 = 1.0;
#endif
uniform sampler2D g_tColor1; // SrgbRead(true)
uniform sampler2D g_tHeight1;
uniform sampler2D g_tNormal1;

#if (F_DETAIL_NORMAL == 1)
    in vec2 vDetailTexCoords;
    uniform sampler2D g_tNormalDetail1;
#endif

uniform bool g_bMetalness1 = true;
uniform bool g_bModelTint1 = true;
uniform int g_nColorCorrectionMode1 = 0; // 1 = adjust the base colour through g_mTextureAdjust1
uniform float g_fTextureNormalContrast1 = 1.0;
uniform float g_fTextureRoughnessBrightness1 = 1.0;
uniform float g_fTextureRoughnessContrast1 = 1.0;
uniform float g_fTintMaskBrightness1 = 1.0;
uniform float g_fTintMaskContrast1 = 1.0;
uniform int g_nVertexColorMode1 = 0; // 0=Masked by tint, 1=Unmasked, 2=Disabled
uniform vec3 g_vAmbientOcclusionLevels1 = vec3(0, 0.5, 1);

uniform float g_flHeightMapScale1 = 1.0;
uniform float g_flHeightMapZeroPoint1 = 0.5; // vfx golden default (m_flDefault)

uniform mat4 g_mTextureColorAdjust1;
uniform mat4 g_mTextureAdjust1;

in vec4 vColorBlendValues;

// Material 2
#if (GameVfx_csgo_environment_blend != 0)
    uniform sampler2D g_tColor2; // SrgbRead(true)
    uniform sampler2D g_tHeight2;
    uniform sampler2D g_tNormal2;

    #if (F_DETAIL_NORMAL == 1)
        uniform sampler2D g_tNormalDetail2;
    #endif

    #if (F_SHARED_COLOR_OVERLAY == 1)
        uniform sampler2D g_tSharedColorOverlay; // SrgbRead(true)

        uniform int g_nColorOverlayMode;                    // "0=Both Layers,1=Layer 1,2=Layer 2"
        uniform int g_nColorOverlayTintMask;                // "0=Mask Both Layers,1=Mask Layer 1,2=Mask Layer 2,3==Unmasked"
        uniform float g_flOverlayDarknessContrast = 1.0;    // 0.2 .. 2.0
        uniform float g_flOverlayBrightnessContrast = 1.0;  // 0.2 .. 2.0
    #endif

    #if (F_BLEND_EFFECTS > 0)
        // ...
    #endif

    uniform bool g_bMetalness2 = true;
    uniform bool g_bModelTint2 = true;
    uniform int g_nColorCorrectionMode2 = 0;
    uniform float g_fTextureNormalContrast2 = 1.0;
    uniform float g_fTextureRoughnessBrightness2 = 1.0;
    uniform float g_fTextureRoughnessContrast2 = 1.0;
    uniform float g_fTintMaskBrightness2 = 1.0;
    uniform float g_flNormalCombine2 = 0.0;
    uniform float g_flNormalReplace2 = 1.0;
    uniform float g_flBevelSpread2 = 0.1;
    uniform float g_flBevelSoftness2 = 0.1;
    uniform float g_flBevelStrength2 = 0.0;
    uniform float g_flBevelCurve2 = 1.0;
    uniform vec2 g_vBevelLayerAmount2 = vec2(1.0, 1.0);
    uniform float g_fTintMaskContrast2 = 1.0;
    uniform int g_nVertexColorMode2 = 0;
    uniform vec3 g_vAmbientOcclusionLevels2 = vec3(0, 0.5, 1);

    uniform float g_flHeightMapScale2 = 1.0; // vfx golden default (m_flDefault)
    uniform float g_flHeightMapZeroPoint2 = 0.5;

    uniform mat4 g_mTextureColorAdjust2;
    uniform mat4 g_mTextureAdjust2;

    #if (F_ENABLE_LAYER_3 == 1)
        in vec4 vTexCoord3;

        uniform sampler2D g_tColor3; // SrgbRead(true)
        uniform sampler2D g_tHeight3;
        uniform sampler2D g_tNormal3;

        uniform bool g_bMetalness3 = true;
        uniform bool g_bModelTint3 = true;
        uniform int g_nColorCorrectionMode3 = 0;
        uniform float g_fTextureNormalContrast3 = 1.0;
        uniform float g_fTextureRoughnessBrightness3 = 1.0;
        uniform float g_fTextureRoughnessContrast3 = 1.0;
        uniform float g_fTintMaskBrightness3 = 1.0;
        uniform float g_fTintMaskContrast3 = 1.0;
        uniform int g_nVertexColorMode3 = 0;
        uniform vec3 g_vAmbientOcclusionLevels3 = vec3(0, 0.5, 1);

        uniform float g_flHeightMapScale3 = 1.0;
        uniform float g_flHeightMapZeroPoint3 = 0.5;

        uniform mat4 g_mTextureColorAdjust3;
        uniform mat4 g_mTextureAdjust3;
    #endif

    // Colour composite parameters, shared by both blend paths. At these defaults the
    // composite is a plain lerp toward the upper layer.
    uniform float g_flColorOverlay2 = 0.0; // engine live table says overlay=1/replace=0, but with the ported factor law that composite still overshoots on part of the materials (see session 2026-08-21_vrf_plates_residual_attack, variants A-C); kept at the lerp form until the overlay weight is aligned per material
    uniform float g_flColorReplace2 = 1.0;
    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flColorOverlay3 = 0.0;
        uniform float g_flColorReplace3 = 1.0;
    #endif

    // Mod2x-overlays the gamma-encoded upper layer onto the lower one, then replaces toward it.
    // overlaySource is the upper layer before its vertex colour is applied, which is what the
    // overlay term is built from.
    vec3 CombineColor(vec3 a, vec3 b, vec3 overlaySource, float weight, float overlay, float replace)
    {
        vec3 overlaid = a;

        if (overlay > 0.0)
        {
            overlaid *= mix(vec3(1.0), SrgbLinearToGamma(saturate(overlaySource)) * 2.0, weight * overlay);
        }

        return mix(overlaid, b, weight * replace);
    }

    // Picks the height-band blend below over the normalized two-height blend in GetBlendWeights.
    uniform int F_USE_NEW_BLENDING;
    #define bUseNewBlending (F_USE_NEW_BLENDING == 1)

    // Engine blend-softness growth (csgo_environment_blend static256.dyn4, dump lines
    // ~549-790). g_flBlendSoftness2 / g_vFacingDirection2 / spread live in the vertex
    // stage (shared global block) and the VS already folds Softness2 into
    // vColorBlendValues.w, so the fragment law below must NOT add it again.
    // Live cbuffer table: inferno draw groups eid4490/4598/5127 (receipt:
    // docs/sessions/2026-08-21_vrf_plates_residual_attack/blend_draw_state.json).
    uniform float g_flBlendSoftnessDistanceModifierStrength = 1.0; // authored per material at every live draw (0.2/0.0/0.1); engine default unobserved, 0.0 keeps the growth term off for unauthored materials

    // Height-band blend. Every layer above the first gets a single [0, 1] weight from a
    // height difference measured against the layers below it, which is carried upward.
    uniform float g_flUnderlyingHeightMapInfluence2 = 1.0;
    uniform float g_flMaskWithHeight2 = 0.0;

    #if (F_ENABLE_LAYER_3 == 1)
        uniform float g_flUnderlyingHeightMapInfluence3 = 1.0;
        uniform float g_flMaskWithHeight3 = 0.0;
    #endif

    struct LayerCarry
    {
        float weight;       // this layer's [0, 1] weight
        float signedRaw;    // 2*bf-1, clamped to >= 0 before the layer above reads it
        float signedCarry;  // signedRaw scaled by weight; read only where the layer above
                            // sets UnderlyingHeightMapInfluence below 1
        float underHeight;  // height the layer above measures its difference against
    };

    float BlendBandWeight(float difference, float bf, float rawHeight, float softness, float maskWithHeight)
    {
        softness = max(softness, 1e-4);

        float quarter = softness * 0.25;
        float edgeDenom = 0.02 + quarter;

        float crossfade = saturate(0.5 + difference / (2.0 * softness));
        float edgeHi = saturate((saturate(bf + difference * 0.1 + 0.2) - (0.98 - quarter)) / edgeDenom);
        crossfade = mix(crossfade, 1.0, edgeHi);
        float edgeLo = saturate((edgeDenom - saturate(bf + difference * 0.05 - 0.05)) / edgeDenom);
        crossfade = mix(crossfade, 0.0, edgeLo);

        return saturate(crossfade * mix(0.5, rawHeight, maskWithHeight) * 2.0);
    }

    // Weighs one layer against everything below it. Layer 2 passes a seed carry holding
    // layer 1's scaled height, layer 3 passes what layer 2 returned.
    LayerCarry BlendLayer(LayerCarry below, float bf, float rawHeight, float zeroPoint, float scale,
                          float softness, float underlyingInfluence, float maskWithHeight)
    {
        LayerCarry o;
        o.weight = 0.0;
        o.signedRaw = 0.0;
        o.signedCarry = 0.0;
        o.underHeight = below.underHeight;

        if (bf <= 0.0)
        {
            return o;
        }

        float signedFactor = 2.0 * bf - 1.0;
        float heightPlusSigned = ((rawHeight - zeroPoint) * scale) + signedFactor + max(below.signedRaw, 0.0);
        float difference = heightPlusSigned - mix(below.signedCarry, below.underHeight, underlyingInfluence);

        o.weight = BlendBandWeight(difference, bf, rawHeight, softness, maskWithHeight);
        o.signedRaw = signedFactor;

        // The weight-scaled carry and the height it is measured against only propagate
        // once the layer is actually present; signedRaw propagates regardless.
        if (o.weight > 0.05)
        {
            o.signedCarry = signedFactor * o.weight;
            o.underHeight = mix(below.underHeight, max(below.underHeight, heightPlusSigned), o.weight);
        }

        return o;
    }

    vec2 GetBlendWeights(vec2 heightTex, vec2 heightScale, vec2 heightZero, vec4 vColorBlendValues, float flBlendSoftness)
    {
        // ENGINE LAW, transcribed from csgo_environment_blend_vulkan_60_ps.vcs
        // static256.dyn4 (the byte-proven live combo of inferno draw groups
        // eid4490/4598/eid5127). heightTex carries the RAW heightmap red channels;
        // zero points and scales are applied here exactly as the engine does.
        float bf = saturate((vColorBlendValues.x * 1.1) - 0.05);
        float bf3 = saturate((vColorBlendValues.y * 1.1) - 0.05);
        vec3 amounts = normalize(vec3(saturate((1.0 - bf) - bf3), bf, bf3));

        float lod1 = textureQueryLod(g_tHeight1, vTexCoord.xy).x;
        float lod2 = textureQueryLod(g_tHeight2, vTexCoord2.xy).x;
        float fw1 = fwidth(heightTex.x * heightScale.x);
        float fw2 = fwidth(heightTex.y * heightScale.y);

        float softA = (((lod1 * lod1) * (0.0125 + fw1)) * 0.02) * g_flBlendSoftnessDistanceModifierStrength * amounts.x;
        float softB = softA + ((((lod2 * lod2) * (0.0125 + fw2)) * 0.02) * g_flBlendSoftnessDistanceModifierStrength * amounts.y);
        float softBase = max(min(vColorBlendValues.w + flBlendSoftness, 1.0), 1e-4) + softA;
        float softTotal = softBase + softB;
        g_vDebugCheckpoints[4] = vec3(softA);
        g_vDebugCheckpoints[5] = vec3(softBase);

        float zero1 = heightZero.x + (softA * 0.5);
        float scale1 = heightScale.x + softBase;
        float zero2 = heightZero.y + (softB * 0.5);
        float scale2Full = heightScale.y + softTotal;
        float scale2 = scale2Full - (softTotal * 2.0);
        scale2 = (scale2 == 0.0) ? 1e-4 : scale2;

        // Facing mask: at every live material Softness2 = 0.0, so the saturate() is 1 and
        // facing reduces to the blend amount. The general form mixes the geometric and
        // normal-mapped layer-1 normal and is ported when a material authors Softness2 > 0.
        float facing = bf;

        float offset = mix((((-zero1 * scale1) - ((1.0 - zero2) * scale2Full)) - softTotal), ((1.0 - zero1) * scale1) - ((-zero2) * scale2Full), facing);
        float d1 = (heightTex.x - zero1) * scale1;
        float d2 = ((heightTex.y - zero2) * scale2) + offset;

        vec2 w = max((vec2(d1, d2) - vec2(max(d1, d2))) + vec2(softTotal), vec2(0.0));
        float maskHeight = mix(0.5, heightTex.y, g_flMaskWithHeight2);
        float w2 = saturate((w.y / (w.x + 1e-4 + w.y)) * maskHeight * 2.0);
        g_vDebugCheckpoints[6] = vec3(1.0 - w2);
        g_vDebugCheckpoints[7] = vec3(w2);

        return vec2(1.0 - w2, w2);
    }
#endif

uniform float g_flModelTintAmount = 1.0;

#include "common/ViewConstants.slang"
#include "common/LightingConstants.slang"

#include "common/fullbright.slang"
#include "common/texturing.slang"
#include "common/wetness.slang"

#include "common/fog.slang"

#if (S_MODE_DEPTH == 1) || (S_MODE_OVERDRAW == 1)

    #if (S_MODE_OVERDRAW == 1)
        #include "common/quad_overdraw.slang"
    #endif

    #if (F_ALPHA_TEST == 1) || (S_MODE_OVERDRAW == 1)
        out vec4 outputColor;
    #endif

    #if (S_MODE_OVERDRAW == 1) || (F_ALPHA_TEST == 0)
        layout (early_fragment_tests) in;
    #endif

    void main()
    {
        #if (F_ALPHA_TEST == 1)
            outputColor = vec4(0.0, 0.0, 0.0, AlphaTestAntiAliasing(texture(g_tColor1, vTexCoord.xy).a, vTexCoord.xy, g_flAlphaTestReference1, g_flAntiAliasedEdgeStrength1));
        #elif (S_MODE_OVERDRAW == 1)
            outputColor = vec4(0.0, 0.0, 0.0, 1.0);
        #endif

        #if (S_MODE_OVERDRAW == 1)
            CountQuadOverdraw();
        #endif
    }

#else

// The dielectric reflectance the specular colour is built from. Wetness pulls it toward water.
uniform float g_flReflectance = 0.04;

// Must be last
#include "common/lighting.slang"

// Get material properties (possibly from two layers)
MaterialProperties_t GetMaterial(vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut, vBitangentOut);
    g_vDebugCheckpoints[3] = vec3(vColorBlendValues.w);

    // vTexCoord = Layer1 (xy) shared color overlay (zw)
    // vTexCoord2 = Layer2 (xy) UV2 (zw)
    // vDetailTexCoords = Detail (normal) UVs

    vec4 color = texture(g_tColor1, vTexCoord.xy);
    vec4 height = texture(g_tHeight1, vTexCoord.xy);
    g_vDebugCheckpoints[1] = vec3(height.r);

    // Where the water line is judged against.
    float rawHeight = height.r;
    vec4 normal = texture(g_tNormal1, vTexCoord.xy);

    vec3 aoLevels = g_vAmbientOcclusionLevels1.xyz;

    float tintMask1 = saturate(((height.g - 0.5) * g_fTintMaskContrast1 + 0.5) * g_fTintMaskBrightness1);
    height.a = g_bMetalness1 ? height.a : 0.0;
    normal.b = saturate(((normal.b - 0.5) * g_fTextureRoughnessContrast1 + 0.5) * g_fTextureRoughnessBrightness1);

    vec3 overlayFactor = vec3(1.0);

    #if (GameVfx_csgo_environment_blend != 0 && F_SHARED_COLOR_OVERLAY == 1)
        vec3 overlay = texture(g_tSharedColorOverlay, vTexCoord.zw).rgb * 2.0 - 1.0;
        vec3 _15235 = (((vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), overlay), vec3(g_flOverlayBrightnessContrast))) * g_flOverlayBrightnessContrast)
            + ((SafePow(vec3(1.0) + min(vec3(0.0), overlay), vec3(g_flOverlayDarknessContrast)) - vec3(1.0)) * g_flOverlayDarknessContrast)) + vec3(1.0);
        overlayFactor = max(vec3(0.0), _15235);
    #endif
    g_vDebugCheckpoints[11] = overlayFactor;

    vec3 tintColorNorm = normalize(max(vTintColor_ModelAmount.xyz, vec3(0.001)));
    float tintColorNormLuma = GetLuma(tintColorNorm);

    vec4 colorMaybeColorCorrected = mix(color, vec4((color * g_mTextureAdjust1).rgb, color.a), bvec4(g_nColorCorrectionMode1 == 1));
    vec4 colorColorCorrectedTinted = color * g_mTextureColorAdjust1;

    vec3 colorTintedMaybeColorCorrected = mix(colorMaybeColorCorrected.rgb, colorColorCorrectedTinted.rgb, tintMask1);
    g_vDebugCheckpoints[9] = colorTintedMaybeColorCorrected;

    float tintAdjustedLuma = GetLuma(colorTintedMaybeColorCorrected);

    float tintColorAdjustedLumaRatio = tintAdjustedLuma / tintColorNormLuma;
    float tintAdjustedLuma3x = 3.0 * tintAdjustedLuma;

    float tintColorHighestPoint = max(vTintColor_ModelAmount.x, max(vTintColor_ModelAmount.y, vTintColor_ModelAmount.z));
    float highPointXLuma = tintAdjustedLuma3x * tintColorHighestPoint;
    vec3 tintResult = saturate(mix(colorTintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjustedLumaRatio, highPointXLuma), vec3((vTintColor_ModelAmount.w * tintMask1) * float(g_bModelTint1))));

    color.rgb = tintResult;

    #if (GameVfx_csgo_environment_blend != 0)
        float flVertexColorMask = float(g_nVertexColorMode1 != 2) * mix(height.g, 1.0, g_nVertexColorMode1 == 0);
        color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask);
    #else
        // note: g_nVertexColorMode ignored in non-blend
        color.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, height.g);
    #endif

    // Blending
#if (GameVfx_csgo_environment_blend != 0)
    vec3 engineBlendWorldNormal = vec3(0.0, 0.0, 1.0);
    vec4 color2 = texture(g_tColor2, vTexCoord2.xy);
    vec4 height2 = texture(g_tHeight2, vTexCoord2.xy);
    vec4 normal2 = texture(g_tNormal2, vTexCoord2.xy);
    g_vDebugCheckpoints[2] = vec3(height2.r);
    #if (F_DETAIL_NORMAL == 1)
        vec2 detailNormal2 = texture(g_tNormalDetail2, vDetailTexCoords).rg;
    #endif

    float tintMask2 = saturate(((height2.g - 0.5) * g_fTintMaskContrast2 + 0.5) * g_fTintMaskBrightness2);
    height2.a = g_bMetalness2 ? height2.a : 0.0;
    normal2.b = saturate(((normal2.b - 0.5) * g_fTextureRoughnessContrast2 + 0.5) * g_fTextureRoughnessBrightness2);

    vec4 color2MaybeColorCorrected = mix(color2, vec4((color2 * g_mTextureAdjust2).rgb, color2.a), bvec4(g_nColorCorrectionMode2 == 1));
    vec4 color2ColorCorrectedTinted = color2 * g_mTextureColorAdjust2;

    vec3 color2TintedMaybeColorCorrected = mix(color2MaybeColorCorrected.rgb, color2ColorCorrectedTinted.rgb, tintMask2);
    g_vDebugCheckpoints[10] = color2TintedMaybeColorCorrected;

    float tintAdjusted2Luma = GetLuma(color2TintedMaybeColorCorrected);

    float tintColorAdjusted2LumaRatio = tintAdjusted2Luma / tintColorNormLuma;
    float tintAdjusted2Luma3x = 3.0 * tintAdjusted2Luma;

    vec3 tintResult2 = saturate(mix(color2TintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjusted2LumaRatio, tintAdjusted2Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask2) * float(g_bModelTint2))));

    color2.rgb = tintResult2;

    float flVertexColorMask2 = float(g_nVertexColorMode2 != 2) * mix(height2.g, 1.0, g_nVertexColorMode2 == 0);
    vec3 color2Overlay = color2.rgb;
    color2.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask2);

    // Both blend paths composite the same way and differ only in how they weigh the layer.
    float baseHeight1 = height.r - g_flHeightMapZeroPoint1;
    float baseHeight2 = height2.r - g_flHeightMapZeroPoint2;

    LayerCarry carry2 = LayerCarry(0.0, 0.0, 0.0, 0.0);
    float weight2;
    float weightBase2;

    if (bUseNewBlending)
    {
        LayerCarry carry1 = LayerCarry(0.0, 0.0, 0.0, baseHeight1 * g_flHeightMapScale1);
        carry2 = BlendLayer(carry1, saturate(vColorBlendValues.x * 1.1 - 0.05), height2.r,
            g_flHeightMapZeroPoint2, g_flHeightMapScale2, vColorBlendValues.w,
            g_flUnderlyingHeightMapInfluence2, g_flMaskWithHeight2);

        weight2 = carry2.weight;
        weightBase2 = 1.0 - weight2;
    }
    else
    {
        vec2 weights = GetBlendWeights(
            vec2(height.r, height2.r),
            vec2(g_flHeightMapScale1, g_flHeightMapScale2),
            vec2(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2),
            vColorBlendValues,
            g_flBlendSoftness2
        );

        weight2 = weights.y;
        weightBase2 = weights.x;
    }

    g_flDebugWeight2Out = weight2;
    if (g_flDebugBlendVis >= 10.0)
    {
        weight2 = saturate(g_flDebugBlendVis - 10.0);
        weightBase2 = 1.0 - weight2;
    }

    rawHeight = (rawHeight * weightBase2) + (height2.r * weight2);

    height.r = baseHeight1;
    height2.r = baseHeight2;

    color.rgb = CombineColor(color.rgb, color2.rgb, color2.rgb, weight2, g_flColorOverlay2, g_flColorReplace2); // engine overlays srgbEncode(final layer2) (dump _16054 == _13174's replace target), not the pre-vertex-color copy
    color.a = color.a * weightBase2 + color2.a * weight2;
    // Engine per-property composites at the live default table (all Combine = 1, all
    // Replace = 0): base * mix(1, upper, w) - AO and metalness Mod2x for metalness,
    // roughness r1 * (1 + (r2 - r1) * 2w). Normal .rg stays a lerp here; the engine's
    // normal law mixes decoded world normals and needs the decode restructured first.
    height.a = height.a * mix(1.0, height2.a * 2.0, weight2);
    height.r = height.r * weightBase2 + height2.r * weight2;
    height.g = height.g * weightBase2 + height2.g * weight2;
    height.b = height.b * weightBase2 + height2.b * weight2;
    {
        vec3 N1w = calculateWorldNormal(DecodeHemiOctahedronNormal(normal.rg), mat.GeometricNormal, mat.Tangent, mat.Bitangent);
        N1w = normalize(mix(mat.GeometricNormal, N1w, g_fTextureNormalContrast1));
        vec3 N2w = calculateWorldNormal(DecodeHemiOctahedronNormal(normal2.rg), mat.GeometricNormal, mat.Tangent, mat.Bitangent);
        N2w = normalize(mix(mat.GeometricNormal, N2w, g_fTextureNormalContrast2));

        vec3 N1composite = N1w;
        float bbf = saturate((vColorBlendValues.x * 1.1) - 0.05);
        float bbf3 = saturate((vColorBlendValues.y * 1.1) - 0.05);
        vec3 bamounts = normalize(vec3(saturate((1.0 - bbf) - bbf3), bbf, bbf3));
        float blod1 = textureQueryLod(g_tHeight1, vTexCoord.xy).x;
        float blod2 = textureQueryLod(g_tHeight2, vTexCoord2.xy).x;
        float bfw1 = fwidth(height.r * g_flHeightMapScale1);
        float bfw2 = fwidth(height2.r * g_flHeightMapScale2);
        float bsoftA = (((blod1 * blod1) * (0.0125 + bfw1)) * 0.02) * g_flBlendSoftnessDistanceModifierStrength * bamounts.x;
        float bsoftB = (((blod2 * blod2) * (0.0125 + bfw2)) * 0.02) * g_flBlendSoftnessDistanceModifierStrength * bamounts.y;
        float bsoftBase = max(min(vColorBlendValues.w + g_flBlendSoftness2, 1.0), 1e-4) + bsoftA;
        float bsoftTotal = bsoftBase + bsoftB;
        float bzero1 = g_flHeightMapZeroPoint1 + (bsoftA * 0.5);
        float bscale1 = g_flHeightMapScale1 + bsoftBase;
        float bzero2 = g_flHeightMapZeroPoint2 + (bsoftB * 0.5);
        float bscale2Full = g_flHeightMapScale2 + bsoftTotal;
        float bscale2 = bscale2Full - (bsoftTotal * 2.0);
        bscale2 = (bscale2 == 0.0) ? 1e-4 : bscale2;
        float boffset = mix((((-bzero1 * bscale1) - ((1.0 - bzero2) * bscale2Full)) - bsoftTotal), ((1.0 - bzero1) * bscale1) - ((-bzero2) * bscale2Full), bbf);
        float pbd1 = (height.r - bzero1) * bscale1;
        float pbd2 = ((height2.r - bzero2) * bscale2) + boffset;
        vec2 pzone = max((vec2(pbd1, pbd2) - vec2(max(pbd1, pbd2))) + vec2(bsoftTotal), vec2(0.0));
        float pw = pzone.y / ((pzone.x + 1e-4) + pzone.y);
        float wBand = saturate(pw * mix(0.5, height2.r, g_flMaskWithHeight2) * 2.0);
        float hMask = saturate((pbd2 + bzero2) - pbd1);
        g_vDebugCheckpoints[8] = vec3(wBand);
        if (g_flBevelStrength2 != 0.0 && (g_vBevelLayerAmount2.x != 0.0 || g_vBevelLayerAmount2.y != 0.0))
        {
            float bd1 = (height.r - bzero1) * (bscale1 + g_flBevelSoftness2);
            float bd2 = ((height2.r - bzero2) * (bscale2 - g_flBevelSoftness2)) + boffset + g_flBevelSpread2;
            float bm = max(bd1, bd2) - bsoftTotal - g_flBevelSoftness2;
            vec2 bband = max(vec2(bd1, bd2) - vec2(bm), vec2(0.0));
            float bfrac = bband.y / (bband.x + 0.001 + bband.y);
            float bcurve = max(0.2, g_flBevelCurve2 * 2.0);
            float brising = pow(smoothstep(0.0, 0.5, bfrac), bcurve);
            float bfalling = pow(smoothstep(1.0, 0.5, bfrac), bcurve);
            vec3 bperturbed = vFragPosition + (mat.GeometricNormal * (((brising + 1.0) - bfalling) * g_flBevelStrength2)) * 100.0;
            vec3 bgrad = cross(dFdyFine(bperturbed), dFdxFine(bperturbed));
            if (length(bgrad) > 0.0)
            {
                float bwindow = saturate(bbf * 10.0) * saturate((1.0 - bbf) * 10.0);
                float bweight = (((brising * bfalling) * smoothstep(0.0, g_flBevelSoftness2, bbf)) * bwindow) * mix(1.0, g_vBevelLayerAmount2.x, weight2) * mix(g_vBevelLayerAmount2.y, 1.0, weight2);
                vec3 bgradN = normalize(mix(mat.GeometricNormal, normalize(bgrad), bweight));
                N1composite = normalize(mix(mat.GeometricNormal, (N1w + bgradN) * 0.5, 2.0));
            }
        }

        vec3 blendN = mix(N1composite, normalize(mix(mat.GeometricNormal, (N1composite + N2w) * 0.5, 2.0)), wBand * g_flNormalCombine2);
        engineBlendWorldNormal = mix(blendN, N2w, (wBand * hMask) * g_flNormalReplace2);

        #if (F_SHARED_COLOR_OVERLAY == 1)
            const bool g_bColorOverlayLayer1 = (g_nColorOverlayMode == 0) || (g_nColorOverlayMode == 1);
            const bool g_bColorOverlayLayer2 = (g_nColorOverlayMode == 0) || (g_nColorOverlayMode == 2);
            const bool g_bColorOverlayMaskLayer1 = (g_nColorOverlayTintMask == 0) || (g_nColorOverlayTintMask == 1);
            const bool g_bColorOverlayMaskLayer2 = (g_nColorOverlayTintMask == 0) || (g_nColorOverlayTintMask == 2);

            float overlayAmount = ((float(g_bColorOverlayLayer1) * (g_bColorOverlayMaskLayer1 ? tintMask1 : 1.0) * (1.0 - wBand)) +
                                  (float(g_bColorOverlayLayer2) * (g_bColorOverlayMaskLayer2 ? tintMask2 : 1.0) * wBand));

            if (overlayAmount > 0.0)
            {
                vec2 overlayUV = (g_nColorOverlayUVSet == 0) ? vTexCoord.zw : (g_nColorOverlayUVSet == 1) ? vTexCoord.xy : vTexCoord2.zw;
                vec3 overlay = texture(g_tSharedColorOverlay, overlayUV).rgb * 2.0 - 1.0;
                vec3 overlayAdjusted = max(vec3(0.0), ((vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), overlay), vec3(g_flOverlayBrightnessContrast))) * g_flOverlayBrightnessContrast) +
                    ((SafePow(vec3(1.0) + min(vec3(0.0), overlay), vec3(g_flOverlayDarknessContrast)) - vec3(1.0)) * g_flOverlayDarknessContrast) + vec3(1.0));
                color.rgb = color.rgb * mix(vec3(1.0), overlayAdjusted, vec3(overlayAmount));
            }
        #endif
    }
    normal.b = normal.b * (1.0 + ((normal2.b - normal.b) * 2.0 * weight2));
    aoLevels = aoLevels * mix(vec3(1.0), g_vAmbientOcclusionLevels2.xyz, weight2);

    #if (F_ENABLE_LAYER_3 == 1)
        vec4 color3 = texture(g_tColor3, vTexCoord3.xy);
        vec4 height3 = texture(g_tHeight3, vTexCoord3.xy);
        vec4 normal3 = texture(g_tNormal3, vTexCoord3.xy);

        float tintMask3 = saturate(((height3.g - 0.5) * g_fTintMaskContrast3 + 0.5) * g_fTintMaskBrightness3);
        height3.a = g_bMetalness3 ? height3.a : 0.0;
        normal3.rg = (normal3.rg - 0.5) * g_fTextureNormalContrast3 + 0.5;
        normal3.b = saturate(((normal3.b - 0.5) * g_fTextureRoughnessContrast3 + 0.5) * g_fTextureRoughnessBrightness3);

        vec4 color3MaybeColorCorrected = mix(color3, vec4((color3 * g_mTextureAdjust3).rgb, color3.a), bvec4(g_nColorCorrectionMode3 == 1));
        vec4 color3ColorCorrectedTinted = color3 * g_mTextureColorAdjust3;

        vec3 color3TintedMaybeColorCorrected = mix(color3MaybeColorCorrected.rgb, color3ColorCorrectedTinted.rgb, tintMask3);
        float tintAdjusted3Luma = GetLuma(color3TintedMaybeColorCorrected);

        float tintColorAdjusted3LumaRatio = tintAdjusted3Luma / tintColorNormLuma;
        float tintAdjusted3Luma3x = 3.0 * tintAdjusted3Luma;

        vec3 tintResult3 = saturate(mix(color3TintedMaybeColorCorrected, tintColorNorm * min(tintColorAdjusted3LumaRatio, tintAdjusted3Luma3x * tintColorHighestPoint), vec3((vTintColor_ModelAmount.w * tintMask3) * float(g_bModelTint3))));

        color3.rgb = tintResult3;

        float flVertexColorMask3 = float(g_nVertexColorMode3 != 2) * mix(height3.g, 1.0, g_nVertexColorMode3 == 0);
        vec3 color3Overlay = color3.rgb;
        color3.rgb *= mix(vec3(1.0), vVertexColor_Alpha.rgb, flVertexColorMask3);

        float baseHeight3 = height3.r - g_flHeightMapZeroPoint3;
        float weight3;

        if (bUseNewBlending)
        {
            LayerCarry carry3 = BlendLayer(carry2, saturate(vColorBlendValues.y * 1.1 - 0.05), height3.r,
                g_flHeightMapZeroPoint3, g_flHeightMapScale3, vColorBlendValues.w,
                g_flUnderlyingHeightMapInfluence3, g_flMaskWithHeight3);

            weight3 = carry3.weight;
        }
        else
        {
            vec2 weights3 = GetBlendWeights(
                vec2(max(height.r, height2.r), baseHeight3),
                vec2(max(g_flHeightMapScale1, g_flHeightMapScale2), g_flHeightMapScale3),
                vec2(max(g_flHeightMapZeroPoint1, g_flHeightMapZeroPoint2), g_flHeightMapZeroPoint3),
                vec4(vColorBlendValues.y, 0.0, 0.0, vColorBlendValues.w),
                g_flBlendSoftness3
            );

            weight3 = weights3.y;
        }

        rawHeight = (rawHeight * (1.0 - weight3)) + (height3.r * weight3);

        height3.r = baseHeight3;

        float weightBase3 = 1.0 - weight3;

        color.rgb = CombineColor(color.rgb, color3.rgb, color3Overlay, weight3, g_flColorOverlay3, g_flColorReplace3);
        color.a = color.a * weightBase3 + color3.a * weight3;
        height = height * weightBase3 + height3 * weight3;
        normal = normal * weightBase3 + normal3 * weight3;
        aoLevels = aoLevels * weightBase3 + g_vAmbientOcclusionLevels3.xyz * weight3;
    #endif
#endif

    mat.Albedo = color.rgb;
    g_vDebugCheckpoints[12] = mat.Albedo;
    mat.AmbientOcclusion = color.a;

    // Alpha test
    #if (F_ALPHA_TEST == 1)
        mat.AmbientOcclusion = height.b;
        mat.Opacity = AlphaTestAntiAliasing(color.a, vTexCoord.xy, g_flAlphaTestReference1, g_flAntiAliasedEdgeStrength1);
    #endif

    mat.AmbientOcclusion = mix(aoLevels.x, aoLevels.z, pow(max(mat.AmbientOcclusion, 0.0), max(aoLevels.y, 0.001)));

    // Normals and Roughness
    #if (GameVfx_csgo_environment_blend != 0)
        mat.NormalMap = vec3(0.0, 0.0, 1.0);
    #else
        mat.NormalMap = DecodeHemiOctahedronNormal(normal.rg);
    #endif
    mat.RoughnessTex = normal.bb;

    // Detail texture
    #if (F_DETAIL_NORMAL == 1)
        // this is wrong. todo
        vec2 detailNormal = texture(g_tNormalDetail1, vDetailTexCoords).rg;
        mat.NormalMap.xy = (mat.NormalMap.xy + detailNormal) * vec2(0.5);
    #endif

    mat.Normal = calculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.Normal = normalize(mix(mat.GeometricNormal, mat.Normal, g_fTextureNormalContrast1));

    #if (GameVfx_csgo_environment_blend != 0)
        mat.Normal = engineBlendWorldNormal;
    #endif
    g_vDebugCheckpoints[13] = mat.Normal * 0.5 + vec3(0.5);

    mat.Height = height.r;
    mat.Metalness = height.a;

    mat.AmbientNormal = mat.Normal;
    mat.AmbientGeometricNormal = mat.GeometricNormal;

    float reflectance = g_flReflectance;
    ApplyWetness(mat, rawHeight, vColorBlendValues.z, vColorBlendValues.w, reflectance);
    AdjustRoughnessByGeometricNormal(mat);

    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;

	mat.SpecularColor = mix(vec3(reflectance), mat.Albedo, mat.Metalness);

    mat.DiffuseAO = vec3(mat.AmbientOcclusion);
    mat.SpecularAO = mat.AmbientOcclusion;

    return mat;
}

void main()
{
    vec3 vertexNormal = SwitchCentroidNormal(vNormalOut, vCentroidNormalOut);

    MaterialProperties_t mat = GetMaterial(vertexNormal);

#if (GameVfx_csgo_environment_blend != 0)
    if (g_flDebugBlendVis > 0.5 && g_flDebugBlendVis < 1.5)
    {
        outputColor = vec4(g_flDebugWeight2Out.xxx, 1.0);
        return;
    }
#endif

    LightingTerms_t lighting = CalculateLighting(mat);
    g_vDebugCheckpoints[14] = lighting.DiffuseIndirect;

    // Combining pass
    ApplyAmbientOcclusion(lighting, mat);

    vec3 diffuseLighting = lighting.DiffuseDirect + lighting.DiffuseIndirect;

    float flRoughAvg = dot(mat.Roughness.xy, vec2(0.5));
    float flRoughSq = flRoughAvg * flRoughAvg;
    float flNoV = max(0.0, dot(mat.AmbientNormal, mat.ViewDir));
    vec4 vBrdfPoly = flRoughAvg * vec4(-1.0, -0.0275, -0.572, 0.022) + vec4(1.0, 0.0425, 1.04, -0.04);
    float flA004 = min(vBrdfPoly.x * vBrdfPoly.x, exp2(-9.28 * flNoV)) * vBrdfPoly.x + vBrdfPoly.y;
    vec2 vBrdfAB = vec2(-1.04, 1.04) * flA004 + vBrdfPoly.zw;
    vec3 vEnvBrdf = mat.SpecularColor * vBrdfAB.x + vBrdfAB.y;

    g_flEnvLodOverride = 5.0 * sqrt(flRoughAvg);
    g_flEnvRoughnessSquaredOverride = flRoughSq;
    vec3 vEnvSample = GetEnvironmentNoBRDF(mat, 1.0);
    g_flEnvLodOverride = -1.0;
    g_flEnvRoughnessSquaredOverride = -1.0;

    vec3 vLightingSum = lighting.DiffuseDirect + lighting.DiffuseIndirect;
    vec3 vLightingTint = normalize(vLightingSum + vec3(0.001)) * clamp(length(vLightingSum), 0.0, 1.75);
    vec3 vIndirectBoost = vec3(1.0) + normalize(lighting.DiffuseIndirect + vec3(0.001)) * 0.5;
    vec3 specularLighting = lighting.SpecularDirect + vEnvBrdf * vEnvSample * vLightingTint * vIndirectBoost * mat.SpecularAO;

    vec3 combinedLighting = mat.DiffuseColor * diffuseLighting + specularLighting;
    g_vDebugCheckpoints[15] = combinedLighting;

    ApplyFog(combinedLighting, mat.PositionWS);

    outputColor = vec4(combinedLighting, mat.Opacity);

    if (HandleMaterialRenderModes(outputColor, mat)
    || HandleLightingRenderModes(outputColor, mat, lighting)
    || HandleUVRenderModes(outputColor, mat, g_tColor1, vTexCoord.xy))
    {
        //
    }
    else if (g_iRenderMode == renderMode_Cubemaps)
    {
        // No bumpmaps, full reflectivity
        vec3 viewmodeEnvMap = GetEnvironment(mat).rgb;
        outputColor.rgb = SrgbLinearToGamma(viewmodeEnvMap);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        outputColor = vec4(vTintColor_ModelAmount.rgb, vVertexColor_Alpha.a);
    }
    else if (g_iRenderMode == renderMode_VertexColor)
    {
        outputColor.rgb = vVertexColor_Alpha.rgb;
    }
#if (GameVfx_csgo_environment_blend != 0)
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        outputColor.rgb = vColorBlendValues.rgb;
    }
#endif

    if (g_nDebugFragCheckpoint != 0)
    {
        outputColor.rgb = g_vDebugCheckpoints[g_nDebugFragCheckpoint];
    }
}

#endif
