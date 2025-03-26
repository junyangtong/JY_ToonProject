Shader "JY/Toon/LiquidMerge"
{
    Properties
    {
        _WarpInt ("WarpInt", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline"}

        Pass
        {
            Name "LiquidMerge"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            TEXTURE2D(_LiquidColorBuffer);       SAMPLER(sampler_LiquidColorBuffer);
            TEXTURE2D(_LiquidDepthBuffer);       SAMPLER(sampler_LiquidDepthBuffer);
            TEXTURE2D(_SceneColorBuffer);       SAMPLER(sampler_SceneColorBuffer);
            TEXTURE2D(_SceneDepthBuffer);       SAMPLER(sampler_SceneDepthBuffer);

            CBUFFER_START(UnityPerMaterial)
                float _WarpInt;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);

                output.positionCS = pos;
                output.texcoord = uv;
                return output;
            }

            half4 frag(Varyings input, out float depthOUT : SV_Depth) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 采样场景颜色和深度
                float4 sceneColor = SAMPLE_TEXTURE2D(_SceneColorBuffer, sampler_SceneColorBuffer, input.texcoord);
                float sceneDepth = SAMPLE_TEXTURE2D(_SceneDepthBuffer, sampler_SceneDepthBuffer, input.texcoord);
                
                float4 finalColor = sceneColor;
                depthOUT = sceneDepth;
                return finalColor;
            }
            ENDHLSL
        }
    }
}
