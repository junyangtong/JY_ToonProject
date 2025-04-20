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
                half4 iceColor = SAMPLE_TEXTURE2D(_IceColorBuffer, sampler_IceColorBuffer, input.texcoord);
                half iceDepth = iceColor.a;
                half4 frontLiquidColor = SAMPLE_TEXTURE2D(_FrontLiquidColorBuffer, sampler_FrontLiquidColorBuffer, input.texcoord);
                half frontLiquidDepth = SAMPLE_TEXTURE2D(_FrontLiquidDepthBuffer, sampler_FrontLiquidDepthBuffer, input.texcoord).r;
                half4 backLiquidColor = SAMPLE_TEXTURE2D(_BackLiquidColorBuffer, sampler_BackLiquidColorBuffer, input.texcoord);   
                half backLiquidDepth = SAMPLE_TEXTURE2D(_BackLiquidDepthBuffer, sampler_BackLiquidDepthBuffer, input.texcoord).r;

                // 混合颜色
                half4 sceneColor = half4(SampleSceneColor(input.texcoord), 1.0);

                // 冰块
                half4 iceAndBackGround = lerp(sceneColor, iceColor, step(0.001, iceDepth));
                
                // 冰块和液面交界高亮
                half backLiquidDepth01 = Linear01Depth(backLiquidDepth, _ZBufferParams);
                half iceDepth01 = Linear01Depth(iceDepth, _ZBufferParams);
                half contactMask = smoothstep(backLiquidDepth01, backLiquidDepth01 + 0.00007, iceDepth01);
                backLiquidColor.rgb = lerp(backLiquidColor.rgb * 1.5, backLiquidColor.rgb, contactMask);

                // 液面
                half4 back = lerp(iceAndBackGround * (1 - backLiquidColor.a) + backLiquidColor * backLiquidColor.a, iceAndBackGround, step(backLiquidDepth, iceDepth));

                // 前侧液体
                half4 finalColor = back * (1 - frontLiquidColor.a) + frontLiquidColor * frontLiquidColor.a;//front, frontLiquidColor.a);
                
                // 混合深度
                half liquidDepth = max(frontLiquidDepth, backLiquidDepth);
                depthOUT = lerp(liquidDepth, iceDepth, step(liquidDepth, iceDepth));
                return finalColor;
            }
            ENDHLSL
        }
    }
}
