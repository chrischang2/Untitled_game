// Procedural skybox: gradient, sun + moon discs, twinkling stars and soft drifting clouds.
// All colours come from globals set by DayNightCycle.cs so the whole world shares one palette.
Shader "Comfy/Sky"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _ComfySkyHorizon;
            half4 _ComfySkyZenith;
            half4 _ComfySkyGround;
            half4 _ComfySunColor;
            float4 _ComfySunDir;   // xyz dir towards sun
            float4 _ComfyMoonDir;  // xyz dir towards moon
            half4 _ComfyCloudColor;
            float _ComfyStars;     // 0..1
            float _ComfyCloudCover; // 0..1

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = 0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { v += a * ValueNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;

                half3 col = lerp(_ComfySkyHorizon.rgb, _ComfySkyZenith.rgb, pow(saturate(y), 0.55));
                col = lerp(col, _ComfySkyGround.rgb, saturate(-y * 5));

                // Horizon haze glow towards the sun.
                float sunDot = dot(d, normalize(_ComfySunDir.xyz));
                col += _ComfySunColor.rgb * pow(saturate(sunDot), 8) * 0.25 * saturate(1 - abs(y) * 2);

                // Stars.
                if (_ComfyStars > 0.001 && y > 0)
                {
                    float3 p = d * 220;
                    float3 cell = floor(p);
                    float h = Hash31(cell);
                    float3 center = cell + 0.5 + (float3(Hash31(cell + 1.3), Hash31(cell + 7.1), Hash31(cell + 3.7)) - 0.5) * 0.6;
                    float dist = length(p - center);
                    float star = step(0.985, h) * smoothstep(0.35, 0.0, dist);
                    float tw = 0.6 + 0.4 * sin(_Time.y * (1.5 + h * 4) + h * 50);
                    col += star * tw * _ComfyStars * saturate(y * 4) * 1.6;
                }

                // Clouds on a virtual plane.
                if (y > 0)
                {
                    float2 uv = d.xz / (y + 0.12) * 0.9 + _Time.y * float2(0.004, 0.0015);
                    float n = Fbm(uv * 1.3);
                    float cover = lerp(0.72, 0.42, _ComfyCloudCover);
                    float cloud = smoothstep(cover, cover + 0.22, n) * saturate(y * 6);
                    float lightSide = saturate(Fbm(uv * 1.3 + 0.08) - n + 0.5);
                    half3 cloudCol = _ComfyCloudColor.rgb * lerp(0.82, 1.1, lightSide);
                    col = lerp(col, cloudCol, cloud * 0.85);
                }

                // Sun disc + glow.
                float sunDisc = smoothstep(0.9986, 0.9993, sunDot);
                col += _ComfySunColor.rgb * (sunDisc * 6 + pow(saturate(sunDot), 350) * 1.5);

                // Moon disc.
                float moonDot = dot(d, normalize(_ComfyMoonDir.xyz));
                float moon = smoothstep(0.9991, 0.9995, moonDot);
                col += half3(0.9, 0.93, 1.0) * (moon * 1.4 + pow(saturate(moonDot), 600) * 0.4) * _ComfyStars;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
