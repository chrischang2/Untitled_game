// Stylised lake water: depth-tinted colour, soft shoreline foam, sky fresnel, sun sparkle,
// gentle vertex waves and gameplay-driven ripple rings (_ComfyRipples, set by WaterRipples.cs).
Shader "Comfy/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.36, 0.78, 0.74, 1)
        _DeepColor ("Deep", Color) = (0.08, 0.32, 0.42, 1)
        _DepthRange ("Depth Range", Float) = 3.5
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Distance", Float) = 0.55
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Tiling", Float) = 0.08
        _NormalStrength ("Normal Strength", Range(0, 1)) = 0.35
        _WaveAmp ("Wave Amplitude", Float) = 0.035
        _Gloss ("Gloss", Float) = 420
        _SpecStrength ("Specular", Float) = 2.2
        _ReflectStrength ("Sky Reflection", Range(0,1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DepthRange;
                half4 _FoamColor;
                float _FoamDistance;
                float4 _NormalMap_ST;
                float _NormalScale;
                float _NormalStrength;
                float _WaveAmp;
                float _Gloss;
                float _SpecStrength;
                float _ReflectStrength;
            CBUFFER_END

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            // Global, driven by DayNightCycle / WaterRipples.
            half4 _ComfySkyHorizon;
            half4 _ComfySkyZenith;
            float4 _ComfyRipples[8]; // xy = world xz, z = start time, w = strength

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                half3 waveNormal : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            float3 Waves(float2 xz, float t, out float3 normal)
            {
                float2 d1 = normalize(float2(1, 0.3));
                float2 d2 = normalize(float2(-0.4, 1));
                float2 d3 = normalize(float2(0.7, -0.8));
                float f1 = dot(d1, xz) * 0.55 + t * 0.9;
                float f2 = dot(d2, xz) * 0.8 + t * 1.2;
                float f3 = dot(d3, xz) * 1.3 + t * 1.6;
                float h = sin(f1) + 0.6 * sin(f2) + 0.3 * sin(f3);
                float2 grad = cos(f1) * d1 * 0.55 + 0.6 * cos(f2) * d2 * 0.8 + 0.3 * cos(f3) * d3 * 1.3;
                normal = normalize(float3(-grad.x * _WaveAmp, 1, -grad.y * _WaveAmp));
                return float3(0, h * _WaveAmp, 0);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 n;
                posWS += Waves(posWS.xz, _Time.y, n);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.waveNormal = n;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 DecodeNormal(half4 t)
            {
                half2 xy = t.rg * 2 - 1;
                return half3(xy, sqrt(saturate(1 - dot(xy, xy))));
            }

            half Ripples(float2 xz)
            {
                half ring = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 r = _ComfyRipples[k];
                    float age = _Time.y - r.z;
                    if (r.w <= 0 || age < 0 || age > 2.6) continue;
                    float d = distance(xz, r.xy);
                    float front = age * 1.1;
                    float band = exp(-pow((d - front) * 7.0, 2));
                    float band2 = exp(-pow((d - front * 0.6) * 9.0, 2)) * 0.5;
                    ring += (band + band2) * r.w * saturate(1 - age / 2.6);
                }
                return saturate(ring);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfEye = i.screenPos.w;
                float depthDiff = max(0, sceneEye - surfEye);

                // Detail normals: two scrolling samples of a tileable normal map.
                float2 uv1 = i.positionWS.xz * _NormalScale + _Time.y * float2(0.012, 0.018);
                float2 uv2 = i.positionWS.xz * _NormalScale * 1.7 + _Time.y * float2(-0.017, 0.009);
                half3 n1 = DecodeNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1));
                half3 n2 = DecodeNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2));
                half3 detail = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                half3 N = normalize(i.waveNormal + half3(detail.x, 0, detail.y) * _NormalStrength);

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = lerp(0.45h, 1.0h, mainLight.shadowAttenuation);

                // Body colour by depth.
                half depthT = saturate(depthDiff / _DepthRange);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, pow(depthT, 0.6));
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 lit = water * (ambient * 0.9 + mainLight.color * 0.45 * shadow);

                // Sky reflection via fresnel.
                half fres = pow(1 - saturate(dot(N, V)), 4);
                half3 sky = lerp(_ComfySkyHorizon.rgb, _ComfySkyZenith.rgb, 0.35);
                lit = lerp(lit, sky, saturate(fres * _ReflectStrength + 0.08));

                // Sun / moon sparkle.
                float3 H = normalize(mainLight.direction + V);
                half spec = pow(saturate(dot(N, H)), _Gloss) * _SpecStrength * shadow;
                lit += mainLight.color * spec;

            #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; ++li)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    float3 Hl = normalize(l.direction + V);
                    lit += l.color * l.distanceAttenuation * (0.15 + pow(saturate(dot(N, Hl)), 120) * 2.0);
                }
            #endif

                // Foam: shoreline + around anything poking through + ripple rings.
                float2 foamUV = i.positionWS.xz * 0.35;
                half foamNoise = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, foamUV + _Time.y * 0.03).a;
                half shore = 1 - saturate(depthDiff / _FoamDistance);
                half foam = smoothstep(0.35, 0.6, shore + (foamNoise - 0.5) * 0.6) * shore;
                foam = max(foam, Ripples(i.positionWS.xz) * 0.85);
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * shadow * 0.8);
                lit = lerp(lit, foamLit, foam);

                half alpha = saturate(lerp(0.45, 0.94, saturate(depthDiff / 1.2)) + foam);
                lit = MixFog(lit, i.fogFactor);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
