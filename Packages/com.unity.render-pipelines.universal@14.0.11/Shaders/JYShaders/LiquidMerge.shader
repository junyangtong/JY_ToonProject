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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
            TEXTURE2D(_IceColorBuffer);                 SAMPLER(sampler_IceColorBuffer);
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
                float4 iceColor = SAMPLE_TEXTURE2D(_IceColorBuffer, sampler_IceColorBuffer, input.texcoord);
                float4 frontLiquidColor = SAMPLE_TEXTURE2D(_FrontLiquidColorBuffer, sampler_FrontLiquidColorBuffer, input.texcoord);
                float frontLiquidDepth = SAMPLE_TEXTURE2D(_FrontLiquidDepthBuffer, sampler_FrontLiquidDepthBuffer, input.texcoord).r;
                float4 backLiquidColor = SAMPLE_TEXTURE2D(_BackLiquidColorBuffer, sampler_BackLiquidColorBuffer, input.texcoord);   
                float backLiquidDepth = SAMPLE_TEXTURE2D(_BackLiquidDepthBuffer, sampler_BackLiquidDepthBuffer, input.texcoord).r;

                // 混合颜色
                half4 sceneColor = half4(SampleSceneColor(input.texcoord), 1.0);
                // 冰块
                half4 iceAndBackGround = lerp(sceneColor, iceColor, step(0.001, iceColor.a));

                // 液面
                half4 back = lerp(iceAndBackGround * (1 - backLiquidColor.a) + backLiquidColor * backLiquidColor.a, iceAndBackGround, step(backLiquidDepth, iceColor.a));

                // 前侧液体
                half4 finalColor = back * (1 - frontLiquidColor.a) + frontLiquidColor * frontLiquidColor.a;//front, frontLiquidColor.a);
                
                // 混合深度
                half liquidDepth = max(frontLiquidDepth, backLiquidDepth);
                depthOUT = lerp(liquidDepth, iceColor.a, step(liquidDepth, iceColor.a));
                return finalColor;
            }
            ENDHLSL
        }
    }
}
