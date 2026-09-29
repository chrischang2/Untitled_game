// Recolours Kenney's two-tone fish (blue body / red fins in the palette texture) per species,
// keeping the palette's shading and dark/white details (eyes).
Shader "Comfy/Fish"
{
    Properties
    {
        _BaseMap ("Palette", 2D) = "white" {}
        _BodyColor ("Body", Color) = (0.5, 0.6, 0.8, 1)
        _FinColor ("Fins", Color) = (0.9, 0.4, 0.3, 1)
        [HDR] _Glow ("Glow", Color) = (0,0,0,0)
        _SrcBody ("Palette body colour", Color) = (0.42, 0.44, 0.53, 1)
        _SrcFin ("Palette fin colour", Color) = (0.88, 0.35, 0.29, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BodyColor;
            half4 _FinColor;
            half4 _Glow;
            half4 _SrcBody;
            half4 _SrcFin;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #include "ComfyLighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                // Nearest palette colour decides body vs fin; keep the palette's relative shading.
                half dBody = distance(t, _SrcBody.rgb);
                half dFin = distance(t, _SrcFin.rgb);
                bool fin = dFin < dBody;
                half3 target = fin ? _FinColor.rgb : _BodyColor.rgb;
                half3 src = fin ? _SrcFin.rgb : _SrcBody.rgb;
                half shade = saturate(dot(t, half3(0.3, 0.59, 0.11)) / max(dot(src, half3(0.3, 0.59, 0.11)), 1e-3));
                half match = 1 - smoothstep(0.2, 0.4, min(dBody, dFin)); // eyes / whites stay as they are
                half3 albedo = lerp(t, target * lerp(0.75, 1.15, shade), match);
                half3 c = ComfyShade(albedo, i.positionWS, normalize(i.normalWS)) + _Glow.rgb;
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
