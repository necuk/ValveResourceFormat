#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

layout (local_size_x = 8, local_size_y = 8) in;

#define D_DOF 0

#define D_MSAA_SAMPLES 1

#define D_EXPOSURE 0

uniform bool g_bFlipY = false;

#if D_EXPOSURE == 1
// Engine exposure-chain resolve state (tools/vrf/engine_resolve_forward.py): the resolve applies
// the frame's adapted exposure INSIDE the pack, so this variant needs the same scalar/bias/dither
// state as the tonemap pass.
uniform float g_flToneMapScalarLinear;
uniform float g_flExposureBiasScaleFactor;
uniform vec4 g_vBlueNoiseDitherParams;
layout(binding = 2) uniform sampler2D g_tBlueNoise; // UNORM read, no sRGB decode
#endif

layout(binding = 0) uniform sampler2DMS g_tSourceMsaa;
layout(binding = 1, rgba16f) uniform writeonly image2D g_tDest;

#if D_DOF == 1
uniform mat4 g_invViewProjMatrix;
uniform vec4 g_vLensPlane;
uniform vec4 g_vNearFarScaleBias;
uniform sampler2DMS g_tSceneDepthMsaa; // bound above the reserved slots, this is a per draw input
#endif

// R11G11B10 helpers matching the engine's imageStore byte-exactly
// (byte-exact model: tools/vrf/engine_resolve_forward.py, enc_r11g11b10_rtz / dec_r11g11b10).
uint EncodeChannelRTZ(float x, uint mantBits)
{
    uint f = floatBitsToUint(x);
    int exp = int((f >> 23u) & 0xFFu) - 127 + 15;
    uint keep = (f & 0x7FFFFFu) >> (23u - mantBits);
    uint outBits = (uint(clamp(exp, 0, 31)) << mantBits) | (keep & ((1u << mantBits) - 1u));
    // Round-toward-zero: plain mantissa truncation; subnormals and <=0 flush to 0.
    if (exp <= 0 || x <= 0.0)
        return 0u;
    return outBits;
}

uint EncodeR11G11B10RTZ(vec3 v)
{
    return EncodeChannelRTZ(v.r, 6u)
         | (EncodeChannelRTZ(v.g, 6u) << 11u)
         | (EncodeChannelRTZ(v.b, 5u) << 22u);
}

float DecodeChannel(uint bits, uint mantBits)
{
    uint mant = bits & ((1u << mantBits) - 1u);
    uint exp5 = (bits >> mantBits) & 0x1Fu;
    float mantF = float(mant) / float(1u << mantBits);
    if (exp5 == 0u)
        return ldexp(mantF, -14);
    return ldexp(1.0 + mantF, int(exp5) - 15);
}

vec3 DecodeR11G11B10(uint u)
{
    return vec3(DecodeChannel(u & 0x7FFu, 6u),
                DecodeChannel((u >> 11u) & 0x7FFu, 6u),
                DecodeChannel((u >> 22u) & 0x3FFu, 5u));
}

void main()
{
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = imageSize(g_tDest);

    if (coord.x >= destSize.x || coord.y >= destSize.y)
        return;

    ivec2 sampleCoord = coord;
    sampleCoord.y = g_bFlipY ? textureSize(g_tSourceMsaa).y - sampleCoord.y - 1 : sampleCoord.y;

    float invNumSamples = 1.0 / float(D_MSAA_SAMPLES);
    vec3 resolved = vec3(0.0);

#if D_DOF == 1
    float lowestDepth = 1.0;
    // Alpha carries the circle of confusion for the depth of field pass (set below)
    float outputAlpha = 1.0;
#else
    // Box-average the source alpha so a transparent background survives (e.g. for screenshots)
    float outputAlpha = 0.0;
#endif

    for (int i = 0; i < D_MSAA_SAMPLES; i++)
    {
        vec4 texel = texelFetch(g_tSourceMsaa, sampleCoord, i);
        // Flush NaN/+Inf to 0; clamp(NaN/+Inf, ...) otherwise returns 65504 (white) on NVIDIA and blows up bloom.
        // The range test catches both and survives the fast-math no-NaN folding that would defeat isnan().
        bvec3 valid = lessThanEqual(texel.rgb, vec3(65504.0));
        vec3 s = mix(vec3(0.0), clamp(texel.rgb, vec3(0.0), vec3(65504.0)), valid);
#if D_EXPOSURE == 1
        // Engine: y = clip(msaa, 0, 65504) * E; the pack law needs the exposed sample.
        s *= g_flToneMapScalarLinear * g_flExposureBiasScaleFactor;
#endif
        // Karis average: weight by inverse luminance to reduce fireflies
        resolved += s * (invNumSamples / (max(s.r, max(s.g, s.b)) + 1.0));

#if D_DOF == 1
        lowestDepth = min(texelFetch(g_tSceneDepthMsaa, sampleCoord, i).x, lowestDepth);
#else
        outputAlpha += texel.a * invNumSamples;
#endif
    }

#if D_EXPOSURE == 1
    // Engine resolve law (tools/vrf/engine_resolve_forward.py):
    //   stored = RTZ_pack_R11G11B10( sum_i 1/4 * (E*c_i)/(1 + max(E*c_i))
    //                                + w * (blueNoise_UNORM - 0.5) )
    // The Karis accumulate above IS the packed exposed sum once each sample carries E*c_i; what
    // remains is the dither (same pixel-indexed recipe as post_processing.frag.slang) and the
    // truncating R11G11B10 quantization. The engine's auto-exposure histogram samples its
    // R11G11B10 resolve target and sees Decode(Encode(x)); the in-shader roundtrip reproduces
    // that bit-exactly and stores the QUANTIZED floats to the RGBA16F dest.
    ivec2 noiseCoord = ivec2(mod(floor((vec2(coord) * g_vBlueNoiseDitherParams.z + g_vBlueNoiseDitherParams.xy) * 256.0), 256.0));
    vec3 blueNoise = texelFetch(g_tBlueNoise, noiseCoord, 0).rgb;
    resolved = clamp(resolved + (blueNoise - 0.5) * g_vBlueNoiseDitherParams.w, 0.0, 0.99);
    resolved = DecodeR11G11B10(EncodeR11G11B10RTZ(resolved));
#else
    // Inverse Karis tonemap
    resolved *= 1.0 / (1.0 - max(resolved.r, max(resolved.g, resolved.b)));
#endif

#if D_DOF == 1
    vec2 sampleUV = (vec2(coord) + 0.5) * g_vInvViewportSize.xy;

    // Convert from screen coords to clipspace (normalised device coordinates)
    vec4 screenToClip = vec4(
        sampleUV.x * 2.0 - 1.0,
        sampleUV.y * 2.0 - 1.0,
        LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, lowestDepth),
        1.0
    );

    // Undo view projection to get back 3d world coordinates
    vec4 worldPos = g_invViewProjMatrix * screenToClip;

    if (worldPos.w != 0.0)
    {
        vec4 worldPos3D = vec4(worldPos.xyz / worldPos.w, 1.0);

        // Projecting worldpos onto the lens plane gives us how far away the point is from the plane
        // g_vLensPlane.w is the plane offset, using dot between 4d vectors for this is clever
        float distanceFromLensPlane = dot(g_vLensPlane, worldPos3D);

        float nearScale = g_vNearFarScaleBias.x;
        float nearBias = g_vNearFarScaleBias.y;
        float farScale = g_vNearFarScaleBias.z;
        float farBias = g_vNearFarScaleBias.w;

        float closeCoC = clamp((distanceFromLensPlane * nearScale) + -nearBias, 0.0, 1.0);
        float farCoC = clamp((distanceFromLensPlane * farScale) + farBias, 0.0, 1.0);

        outputAlpha = max(closeCoC, farCoC);
    }
#endif

    imageStore(g_tDest, coord, vec4(resolved, outputAlpha));
}
