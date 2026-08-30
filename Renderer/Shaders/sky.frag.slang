#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

in vec3 vSkyLookupInterpolant;
out vec4 vColor;

#define VRF_TEXTURE_FORMAT_YCOCG 2
#define VRF_TEXTURE_FORMAT_RGBM_DXT 4
#define VRF_TEXTURE_FORMAT_RGBM 5

// Core: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT 0 // sky

// hlvr: 0="HDR (float16)", 1="RGB (8-bit uncompressed)", 2="YCoCg (dxt compressed)", 3="Dxt1 (LDR)", 4="RGBM (dxt compressed)", 5="RGBM (8-bit uncompressed)", 6="BC6H (HDR compressed - recommended)"
// cs2: 0="BC6H (HDR compressed - recommended)", 1="Dxt1 (LDR)", 2="HDR (float16)", 3="RGB (8-bit uncompressed)", 4="YCoCg (dxt compressed)", 5="RGBM (dxt compressed)", 6="RGBM (8-bit uncompressed)"
#define F_TEXTURE_FORMAT2 0 // sky

#if (F_TEXTURE_FORMAT2 > 0)
    #define TextureFormat F_TEXTURE_FORMAT2
#else
    #define TextureFormat F_TEXTURE_FORMAT
#endif

#if (TextureFormat == VRF_TEXTURE_FORMAT_YCOCG)
    #define ENCODING_YCOCG
#elif (TextureFormat == VRF_TEXTURE_FORMAT_RGBM_DXT || TextureFormat == VRF_TEXTURE_FORMAT_RGBM)
    #define ENCODING_RGBM
#endif

uniform samplerCube g_tSkyTexture; // SrgbRead(true)

uniform float g_flBrightnessExposureBias;
uniform float g_flRenderOnlyExposureBias;
uniform vec3 g_vTint = vec3(1.0, 1.0, 1.0);

const float g_flToneMapScalarLinear = 1.0;

// Haze phase pin: the haze/cloud layer phase is an explicit function of the scene
// clock, never per-frame Random. ViewConstants.g_flTime is written from
// Renderer.Uptime (Renderer/Renderer.cs), which the headless driver seeds via
// --start-time, so at a pinned start time this phase is bit-reproducible and the
// engine's cloud/haze pass (additive SrcAlpha/One over the sky, per
// nades artifacts/vrf_frame_ladder/dust2_sky_cloud_draw_states.json) can be
// matched phase-locked. No fit-to-capture constants: the speed is a plain rate.
uniform float g_flHazePhaseSpeed = 1.0; // haze phase rate, cycles per second

// Haze phase derived from the pinnable scene clock (g_flTime = Renderer.Uptime).
float GetHazePhase()
{
    return g_flTime * g_flHazePhaseSpeed;
}

uniform vec3 g_vHazeColor = vec3(0.0, 0.0, 0.0);
uniform float g_flHazeAmount = 0.0; // 0 = haze off; the built-in path stays bit-identical


void main()
{
    vec3 vEyeToSkyDirWs = normalize(vSkyLookupInterpolant);
    vec4 skyTexel = texture(g_tSkyTexture, vEyeToSkyDirWs);

#if defined(ENCODING_YCOCG)
    vColor.rgb = DecodeYCoCg(skyTexel);
    vColor.rgb = SrgbGammaToLinear(vColor.rgb);
#elif defined(ENCODING_RGBM)
    vColor.rgb = DecodeRGBM(skyTexel);
#else
    vColor.rgb = skyTexel.rgb;
#endif
    // Because we precalculate neither of these, we can at least save one exp2 by substituting them into one factor
    vColor.rgb *= exp2(g_flBrightnessExposureBias + g_flRenderOnlyExposureBias);
    vColor.rgb *= g_vTint;

    // Additive haze layer, phase-pinned to the scene clock (GetHazePhase).
    // Disabled by default (g_flHazeAmount == 0) so existing renders stay bit-identical.
    vColor.rgb += g_vHazeColor * GetHazePhase() * g_flHazeAmount;

    vColor.rgb = ClampToPositive(vColor.rgb);

    //vColor.rgb += (vSkyLookupInterpolant * 0.5);
    vColor.a = 1.0;
}
