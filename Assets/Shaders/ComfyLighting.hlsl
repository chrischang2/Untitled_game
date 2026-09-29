#ifndef COMFY_LIGHTING_INCLUDED
#define COMFY_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Soft "wrap" lighting that keeps shadowed sides readable - suits flat low-poly art.
half3 ComfyShade(half3 albedo, float3 positionWS, half3 normalWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);

    half ndl = dot(normalWS, mainLight.direction);
    half wrap = saturate((ndl + 0.3h) / 1.3h);
    half shadow = mainLight.shadowAttenuation;
    half3 direct = mainLight.color * wrap * lerp(0.25h, 1.0h, shadow);

    half3 ambient = SampleSH(normalWS);
    half3 color = albedo * (direct + ambient);

#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
        Light l = GetAdditionalLight(i, positionWS);
        half lndl = saturate((dot(normalWS, l.direction) + 0.4h) / 1.4h);
        color += albedo * l.color * l.distanceAttenuation * lndl;
    }
#endif
    return color;
}

#endif
