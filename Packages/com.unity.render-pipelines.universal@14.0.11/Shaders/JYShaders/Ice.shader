Shader "JY/Toon/Ice"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
            "CastShadows" = "True"
            "ReceiveShadows" = "True"
        }

        Pass
        {
            Name "ForwardLit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 采样基础纹理
                half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                half3 albedo = albedoAlpha.rgb * _BaseColor.rgb;
                half alpha = albedoAlpha.a * _BaseColor.a;

                // 获取主光源
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirWS = normalize(mainLight.direction + viewDirWS);

                // 计算光照
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half NdotH = saturate(dot(normalWS, halfDirWS));
                half NdotV = saturate(dot(normalWS, viewDirWS));

                // 基础漫反射
                half3 diffuse = albedo * mainLight.color * NdotL;

                // 镜面反射
                half roughness = 1 - _Smoothness;
                half3 specular = mainLight.color * pow(NdotH, 1 / roughness) * _Metallic;

                // 环境光
                half3 ambient = SampleSH(normalWS) * albedo;

                // 最终颜色
                half3 finalColor = diffuse + specular + ambient;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}