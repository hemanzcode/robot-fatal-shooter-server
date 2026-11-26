// Superficie PBR do jogo (URP). Mapeamento pelo mundo (sem UVs), com mapas de cor/normal/mascara,
// janelas emissivas, grade neon e chuva (superficies molhadas e pocas).
Shader "Neon/Surface"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _BaseMap ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal (RGB, GL)", 2D) = "bump" {}
        _MaskMap ("Mask (R metal, G AO, B roughness)", 2D) = "white" {}
        _UseMaps ("Use PBR maps", Float) = 0
        _TexScale ("Texture Scale", Float) = 1
        _NormalStrength ("Normal Strength", Float) = 1
        _Smoothness ("Smoothness", Range(0,2)) = 0.6
        _Metallic ("Metallic", Range(0,1)) = 0.3
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _EmisTex ("Emission Mask", 2D) = "black" {}
        _EmisScale ("Emission/Detail Scale", Float) = 1
        _EmisTexColor ("Emission Mask Color", Color) = (0,0,0,1)
        _DetailTex ("Detail (RGB mult, A glass)", 2D) = "white" {}
        _UseDetail ("Use detail", Float) = 0
        _GridColor ("Grid Color", Color) = (0,1,1,1)
        _GridScale ("Grid Scale", Float) = 0
        _Wet ("Wetness", Range(0,1)) = 0
        _Local ("Object-space mapping", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _EmissionColor;
            float4 _EmisTexColor;
            float4 _GridColor;
            float _UseMaps;
            float _TexScale;
            float _NormalStrength;
            float _Smoothness;
            float _Metallic;
            float _EmisScale;
            float _UseDetail;
            float _GridScale;
            float _Wet;
            float _Local;
        CBUFFER_END

        float _NeonRain;   // global: 1 quando chove

        TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_MaskMap);   SAMPLER(sampler_MaskMap);
        TEXTURE2D(_EmisTex);   SAMPLER(sampler_EmisTex);
        TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float NeonHash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float NeonNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(NeonHash(i), NeonHash(i + float2(1, 0)), f.x),
                        lerp(NeonHash(i + float2(0, 1)), NeonHash(i + float2(1, 1)), f.x), f.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 pw = i.positionWS;
                float3 N = normalize(i.normalWS);
                // Objetos que se movem (armas, granadas) mapeiam a textura no espaco do proprio objeto.
                float3 p = lerp(pw, i.positionOS, _Local);
                float3 an = abs(lerp(N, normalize(i.normalOS), _Local));
                float sx = step(0.5, an.x), sy = step(0.5, an.y);

                // Mapeamento planar pelo eixo dominante da normal.
                float2 uv = lerp(lerp(p.xy, p.zy, sx), p.xz, sy);
                float3 T = lerp(lerp(float3(1, 0, 0), float3(0, 0, 1), sx), float3(1, 0, 0), sy);
                float3 B = lerp(float3(0, 1, 0), float3(0, 0, 1), sy);
                if (_Local > 0.5)
                {
                    T = TransformObjectToWorldDir(T);
                    B = TransformObjectToWorldDir(B);
                }

                float2 tuv = uv * _TexScale;
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, tuv);
                half3 nt = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, tuv).rgb * 2.0 - 1.0;
                half3 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, tuv).rgb;

                half3 albedo = _Color.rgb * base.rgb;
                half metallic = saturate(lerp(_Metallic, mask.r * 0.9 + _Metallic, _UseMaps));
                half smoothness = saturate(lerp(_Smoothness, (1.0 - mask.b) * _Smoothness, _UseMaps));
                half occlusion = lerp(1.0, mask.g, _UseMaps);
                nt.xy *= _NormalStrength * _UseMaps;

                // Fachadas: janelas (vidro liso e escuro) e luzes acesas.
                float2 euv = uv * _EmisScale;
                half4 det = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, euv);
                half glass = det.a * _UseDetail;
                albedo *= lerp(half3(1, 1, 1), det.rgb, _UseDetail);
                smoothness = lerp(smoothness, 0.94, glass);
                metallic = lerp(metallic, 0.0, glass);
                nt.xy *= 1.0 - glass;
                half3 emission = _EmissionColor.rgb + SAMPLE_TEXTURE2D(_EmisTex, sampler_EmisTex, euv).rgb * _EmisTexColor.rgb;

                float2 g = abs(frac(uv * _GridScale) - 0.5);
                float edge = smoothstep(0.465, 0.5, max(g.x, g.y)) * step(0.0001, _GridScale);
                emission += _GridColor.rgb * edge;

                // Chuva: tudo fica um pouco mais liso e escuro; no chao formam-se pocas.
                half up = saturate(N.y);
                half puddle = smoothstep(0.52, 0.7, NeonNoise(p.xz * 0.23) * 0.65 + NeonNoise(p.xz * 0.9 + 17.0) * 0.35) * up * _Wet * _NeonRain;
                half damp = _Wet * _NeonRain * (0.35 + 0.35 * up);
                albedo *= lerp(1.0, 0.62, max(damp, puddle));
                smoothness = lerp(smoothness, max(smoothness, 0.72), damp);
                smoothness = lerp(smoothness, 0.97, puddle);
                metallic = lerp(metallic, 0.0, puddle * 0.7);
                nt.xy *= 1.0 - puddle;

                float3 normalWS = normalize(T * nt.x + B * nt.y + N * max(nt.z, 0.2));

                InputData inputData = (InputData)0;
                inputData.positionWS = pw;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(pw);
                inputData.shadowCoord = TransformWorldToShadowCoord(pw);
                inputData.fogCoord = InitializeInputDataFog(float4(pw, 1.0), i.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = metallic;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = occlusion;
                surfaceData.emission = emission;
                surfaceData.alpha = 1.0;
                surfaceData.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
