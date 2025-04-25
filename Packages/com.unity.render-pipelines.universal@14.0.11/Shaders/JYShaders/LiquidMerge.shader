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
            TEXTURE2D(_LiquidColorBuffer);             SAMPLER(sampler_LiquidColorBuffer);
            TEXTURE2D(_LiquidDepthBuffer);             SAMPLER(sampler_LiquidDepthBuffer);

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
                half4 liquidColor = SAMPLE_TEXTURE2D(_LiquidColorBuffer, sampler_LiquidColorBuffer, input.texcoord);
                half liquidDepth = SAMPLE_TEXTURE2D(_LiquidDepthBuffer, sampler_LiquidDepthBuffer, input.texcoord).r;

                // 混合颜色
                half4 sceneColor = half4(SampleSceneColor(input.texcoord), 1.0);

                // 冰块
                half4 iceAndBackGround = lerp(sceneColor, iceColor, step(0.001, iceDepth));
                
                // 冰块和液面交界高亮
                half liquidDepth01 = Linear01Depth(liquidDepth, _ZBufferParams);
                half iceDepth01 = Linear01Depth(iceDepth, _ZBufferParams);
                half contactMask = smoothstep(liquidDepth01, liquidDepth01 + 0.00007, iceDepth01);
                liquidColor.rgb = lerp(liquidColor.rgb * 1.5, liquidColor.rgb, contactMask);

                half4 finalColor = lerp(iceAndBackGround * (1 - liquidColor.a) + liquidColor * liquidColor.a, iceAndBackGround, step(liquidDepth, iceDepth));
                
                // 混合深度
                depthOUT = lerp(liquidDepth, iceDepth, step(liquidDepth, iceDepth));
                return finalColor;
            }
            ENDHLSL
        }
    }
}
