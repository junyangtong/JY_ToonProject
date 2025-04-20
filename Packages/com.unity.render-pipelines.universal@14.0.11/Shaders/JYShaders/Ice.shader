Shader "JY/Toon/Ice"
{
    Properties
    {
        _MatCapTex ("MatCap Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _RefractIntensity ("Refract Intensity", Float) = 0.0
        _NormalTex ("NormalTex", 2D) = "bump"
        _NormalInt ("NormalInt", Float) = 1.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
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
            // GPU Instancing
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
                float3 tangentWS : TEXCOORD3;
                float3x3 tangentToWorld : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            CBUFFER_START(UnityPerMaterial)
                half _RefractIntensity;
                half4 _BaseColor;
                half _NormalInt;
                half4 _NormalTex_ST;
            CBUFFER_END

            TEXTURE2D(_MatCapTex); SAMPLER(sampler_MatCapTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_BackLiquidColorBuffer);       SAMPLER(sampler_BackLiquidColorBuffer);

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = normalInput.tangentWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentToWorld = CreateTangentToWorld(normalInput.normalWS, normalInput.tangentWS, sign);
                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                // 法线
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, input.uv * _NormalTex_ST.xy + _NormalTex_ST.zw));
                float3 normalWS = normalize(mul(normalTS, input.tangentToWorld)) * _NormalInt;

                // 折射
                float2 screenUV = input.positionCS.xy / _ScreenParams.xy;
                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                float2 distortion = normalVS.xy;
                float2 refractionUV = screenUV + distortion * _RefractIntensity;
                
                half3 sceneColor = SampleSceneColor(refractionUV);
                half4 liquidColor = SAMPLE_TEXTURE2D(_BackLiquidColorBuffer, sampler_BackLiquidColorBuffer, refractionUV);
                half3 refractionColor = lerp(sceneColor, liquidColor.rgb, step(0.001,liquidColor.a));
                
                // MatCap
                float2 matcapUV = normalVS.xy * 0.5 + 0.5;
                half4 matcapColor = SAMPLE_TEXTURE2D(_MatCapTex, sampler_MatCapTex, matcapUV);
                half3 finalColor = lerp(refractionColor, matcapColor.rgb, _BaseColor.a);

                return half4(finalColor * _BaseColor.rgb, input.positionCS.z);
            }
            ENDHLSL
        }
    }
}