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
            TEXTURE2D(_SceneColorBuffer);               SAMPLER(sampler_SceneColorBuffer);
            TEXTURE2D(_SceneDepthBuffer);               SAMPLER(sampler_SceneDepthBuffer);
            TEXTURE2D(_IceColorBuffer);                 SAMPLER(sampler_IceColorBuffer);
            TEXTURE2D(_IceDepthBuffer);                 SAMPLER(sampler_IceDepthBuffer);
            TEXTURE2D(_FrontLiquidColorBuffer);         SAMPLER(sampler_FrontLiquidColorBuffer);
            TEXTURE2D(_FrontLiquidDepthBuffer);         SAMPLER(sampler_FrontLiquidDepthBuffer);
            TEXTURE2D(_BackLiquidColorBuffer);          SAMPLER(sampler_BackLiquidColorBuffer);
            TEXTURE2D(_BackLiquidDepthBuffer);          SAMPLER(sampler_BackLiquidDepthBuffer);

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
                
                // 采样
                float4 sceneColor = SAMPLE_TEXTURE2D(_SceneColorBuffer, sampler_SceneColorBuffer, input.texcoord);
                float sceneDepth = SAMPLE_TEXTURE2D(_SceneDepthBuffer, sampler_SceneDepthBuffer, input.texcoord).r;
                float4 iceColor = SAMPLE_TEXTURE2D(_IceColorBuffer, sampler_IceColorBuffer, input.texcoord);
                float iceDepth = SAMPLE_TEXTURE2D(_IceDepthBuffer, sampler_IceDepthBuffer, input.texcoord).r;
                float4 frontLiquidColor = SAMPLE_TEXTURE2D(_FrontLiquidColorBuffer, sampler_FrontLiquidColorBuffer, input.texcoord);
                float frontLiquidDepth = SAMPLE_TEXTURE2D(_FrontLiquidDepthBuffer, sampler_FrontLiquidDepthBuffer, input.texcoord).r;
                float4 backLiquidColor = SAMPLE_TEXTURE2D(_BackLiquidColorBuffer, sampler_BackLiquidColorBuffer, input.texcoord);   
                float backLiquidDepth = SAMPLE_TEXTURE2D(_BackLiquidDepthBuffer, sampler_BackLiquidDepthBuffer, input.texcoord).r;

                // 混合
                backLiquidColor = lerp(backLiquidColor, 0.0, step(backLiquidDepth, sceneDepth));
                half4 iceAndBackGround = lerp(sceneColor, iceColor, step(0.001, iceDepth));
                half4 back = lerp(lerp(iceAndBackGround, backLiquidColor, backLiquidColor.a), iceAndBackGround, step(backLiquidDepth, iceDepth));

                half4 frontIceAndBackGround = lerp(iceColor, lerp(sceneColor, frontLiquidColor, frontLiquidColor.a), frontLiquidColor.a);
                half4 front = lerp(frontIceAndBackGround, back, step(frontLiquidDepth, sceneDepth));
                
                depthOUT = sceneDepth;//max(sceneDepth, iceDepth);
                float4 finalColor = front;
                return front;
            }
            ENDHLSL
        }
    }
}
