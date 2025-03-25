Shader "JY/Toon/LiquidMerge"
{
    Properties
    {
        _SceneColorTex ("Scene Color", 2D) = "white" {}
        _SceneDepthTex ("Scene Depth", 2D) = "white" {}
        _LiquidColor ("Liquid Color", Color) = (0.5, 0.5, 1, 0.5)
        _LiquidDepth ("Liquid Depth", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "LiquidMerge"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

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
                float fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D_MS(_SceneColorTex);
            TEXTURE2D_MS(_SceneDepthTex);
            SAMPLER(sampler_SceneColorTex);
            SAMPLER(sampler_SceneDepthTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SceneColorTex_ST;
                float4 _SceneDepthTex_ST;
                float4 _LiquidColor;
                float _LiquidDepth;
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
                output.fogFactor = ComputeFogFactor(pos.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 采样场景颜色和深度
                float4 sceneColor = SAMPLE_TEXTURE2D_MS(_SceneColorTex, sampler_SceneColorTex, input.texcoord, 0);
                float sceneDepth = SAMPLE_TEXTURE2D_MS(_SceneDepthTex, sampler_SceneDepthTex, input.texcoord, 0).r;
                
                // 计算混合因子
                float blendFactor = saturate((sceneDepth - _LiquidDepth) * 10);
                
                // 混合液体颜色和场景颜色
                float4 finalColor = lerp(_LiquidColor, sceneColor, blendFactor);
                
                // 应用雾效
                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}
