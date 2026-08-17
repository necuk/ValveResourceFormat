#version 460

uniform float g_flBloomScale;
uniform float g_flToneMapScalarLinear;
uniform vec2 g_flThresholdParams;

layout(binding = 0) uniform sampler2D inputTexture;
layout(location = 0) out vec4 outputColor;

const vec3 LUMINANCE_WEIGHTS = vec3(0.3, 0.59, 0.11);

float CalculateLuminance(vec3 color) {
    return dot(color, LUMINANCE_WEIGHTS);
}

vec3 ApplyThreshold(vec3 color) {
    float luminance = CalculateLuminance(color);
    float thresholdFactor = g_flToneMapScalarLinear * luminance * g_flThresholdParams.y + g_flThresholdParams.x;
    float thresholdWeight = clamp(thresholdFactor, 0.0, 1.0);
    return color * thresholdWeight;
}

vec4 DownsampleBox(vec2 uv) {
    vec4 topLeft = textureLodOffset(inputTexture, uv, 0.0, ivec2(-1, -1));
    vec4 topRight = textureLodOffset(inputTexture, uv, 0.0, ivec2(1, -1));
    vec4 bottomLeft = textureLodOffset(inputTexture, uv, 0.0, ivec2(-1, 1));
    vec4 bottomRight = textureLodOffset(inputTexture, uv, 0.0, ivec2(1, 1));

    vec4 filtered = topLeft * 0.25 + topRight * 0.25 + bottomLeft * 0.25 + bottomRight * 0.25;

    return clamp(filtered, 0.0, 65504.0);
}

void main() {

    vec2 uv = gl_FragCoord.xy * 4.0 / vec2(textureSize(inputTexture, 0));

    vec4 downsampledColor = DownsampleBox(uv);
    vec3 thresholdedColor = ApplyThreshold(downsampledColor.rgb);
    vec3 bloomColor = thresholdedColor * g_flBloomScale * g_flToneMapScalarLinear;

    outputColor = vec4(bloomColor, 1.0);
}
