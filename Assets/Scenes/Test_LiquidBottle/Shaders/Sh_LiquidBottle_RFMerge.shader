Shader "LiquidBottle/RFMerge"
{
    Properties
    {
        _WarpInt ("折射扭曲强度", Float) = 0.5
        _EdgeWidth ("液面高亮宽度", Range(0, 0.1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest always ZWrite on Cull back
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "PackHelper.hlsl"

            sampler2D _SceneColorBuffer, _SceneDepthBuffer;
            sampler2D _LiquidColorBuffer, _LiquidDepthBuffer;
            Texture2D<uint4> _IceColorBuffer;
            Texture2D<half> _IceDepthBuffer;
            half _WarpInt;
            half _EdgeWidth;
            
            half4 frag (Varyings i, out float depthOUT : SV_Depth) : SV_Target
            {
                // 冰块
                uint4 iceRGBA_UShort = _IceColorBuffer.Load(int3(i.positionCS.xy, 0));
                half iceDepth01 = _IceDepthBuffer.Load(int3(i.positionCS.xy, 0)).r;

                // unpack冰块颜色 & 透明度 & 扭曲法线
                half4 iceRGBA_Half;
                iceRGBA_Half.rgb = UnpackHDRByUShort(iceRGBA_UShort);
                half2 warpUV = 0;
                UnpackAlphaAndVec2(iceRGBA_UShort.a, iceRGBA_Half.a, warpUV);
                warpUV = _WarpInt * (warpUV - 0.5) * iceDepth01;    // 因为是屏幕空间扭曲, 所以随深度衰减
                
                // 背景
                float2 uv_Screen = i.texcoord;
                half sceneDepth01 = tex2D(_SceneDepthBuffer, uv_Screen).r;
                half4 sceneRGBA = tex2D(_SceneColorBuffer, uv_Screen + step(sceneDepth01, iceDepth01) * warpUV);
                sceneRGBA.a = 1;

                // 液体
                half liquidDepth01 = tex2D(_LiquidDepthBuffer, uv_Screen).r;
                half4 liquidRGBA = tex2D(_LiquidColorBuffer, uv_Screen + step(liquidDepth01, iceDepth01) * warpUV);

                // 液面交界高亮
                half iceDepthReal = LinearEyeDepth(iceDepth01, _ZBufferParams);
                half liquidDepthReal = LinearEyeDepth(liquidDepth01, _ZBufferParams);
                half deltaDepth = iceDepthReal - liquidDepthReal;
                liquidRGBA.rgb *= 1 + smoothstep(_EdgeWidth, 0, deltaDepth);

                // 从远到近排序
                half4 RGBA[3] = { sceneRGBA, liquidRGBA, iceRGBA_Half };
                half3 depth = half3(sceneDepth01, liquidDepth01, iceDepth01);
                for (uint x = 0; x < 2; x++)
                {
                    for (uint y = 0; y < 2-x; y++)
                    {
                        if (depth[y] > depth[y + 1])
                        {
                            half4 tempRGBA = RGBA[y];
                            RGBA[y] = RGBA[y + 1];
                            RGBA[y + 1] = tempRGBA;
                            
                            half tempZ = depth[y];
                            depth[y] = depth[y + 1];
                            depth[y + 1] = tempZ;
                        }
                    }
                }
                depthOUT = depth[2];
                
                // 从远到近混合
                half4 finalRGBA;
                finalRGBA.rgb = lerp(RGBA[0].rgb, RGBA[1].rgb, RGBA[1].a);
                finalRGBA.rgb = lerp(finalRGBA.rgb, RGBA[2].rgb, RGBA[2].a);
                finalRGBA.a = 1;
                return finalRGBA;
            }
            ENDHLSL
        }
    }
}