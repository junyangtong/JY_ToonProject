Shader "JY/Toon/Liquid"
{
    Properties
    {
        _Transparent ("Transparent", Range(0, 1)) = 1.0
        _NoiseTex ("Noise R:LayerWarp G: B: A:", 2D) = "black" {}
        _LayerWarpInt ("LayerWarpInt", Range(0, 1)) = 0.5
        _MaxLiquidHeight("MaxLiquidHeight", Float) = 1.0
        _LiquidHeightOffset ("LiquidHeightOffset", Float)  = 0.0

        _BubbleTex ("BubbleTex", 2D) = "white" {}
        _BubbleParallax ("BubbleParallax", Float) = 0.3
        _BubbleSpeed ("BubbleSpeed", Float) = 1.0

        _DirtyTex ("DirtyTex", 2D) = "white" {}
        _DirtyInt ("DirtyInt", Range(0, 1)) = 1.0
    }
    
    SubShader
    {
        Tags { 
            "RenderType"="Transparent"
                "Queue" = "Transparent" 
            }
        LOD 100
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            #define MAX_LAYER 5

            CBUFFER_START(UnityPerMaterial)
            half _MaxLiquidHeight;
            half _LiquidHeightOffset;
            half _LayerWarpInt;
            half _BubbleSpeed;
            half _BubbleParallax;
            half _DirtyInt;
            half _Transparent;
            CBUFFER_END

            half4 _LiquidLayerColor[MAX_LAYER];
            half _LiquidLayerLerpRange[MAX_LAYER];
            half _LiquidHeight01;

            TEXTURE2D(_NoiseTex);    SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_BubbleTex);   SAMPLER(sampler_BubbleTex);
            TEXTURE2D(_DirtyTex);    SAMPLER(sampler_DirtyTex);
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 viewDirTS : TEXCOORD3;
                float3 viewDirWS : TEXCOORD4;
            };

            // 视差映射
            float2 ParallaxMappingUV(float2 uv, float3 viewDirTS)
            {
                float2 p = viewDirTS.xy / (viewDirTS.z + 0.0001) * _BubbleParallax;
                return uv - p;
            }
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionOS = input.positionOS.xyz;
                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                
                real sign = input.tangentOS.w * GetOddNegativeScale();
                float3 bitangent = cross(normalInput.normalWS, normalInput.tangentWS) * sign;
                float3x3 tangentToWorld = CreateTangentToWorld(normalInput.normalWS, normalInput.tangentWS, sign);
                float3x3 worldToTangent = transpose(tangentToWorld);
                output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                output.viewDirTS = mul(worldToTangent, output.viewDirWS);

                output.uv = input.uv;
                return output;
            }
            
            float4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // 采样噪声图集
                half4 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.uv);

                // 计算相对坐标
                float3 originPos = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPos;

                // 高度裁剪
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset;
                float clipPos = liquidHeightOS - relativePos.y;
                clip(clipPos);
                
                // 获取每层液体的id
                float liquidHeight0Max = relativePos.y / _MaxLiquidHeight * MAX_LAYER;
                uint currentID = floor(liquidHeight0Max - 0.5);
                int nextID = min(MAX_LAYER - 1, currentID + 1);
                
                // 混合颜色
                half4 currentColor = _LiquidLayerColor[currentID];
                half4 nextColor = _LiquidLayerColor[nextID];
                half lerpRange = _LiquidLayerLerpRange[currentID];
                half lerp01 = smoothstep(nextID - 0.4, nextID + 0.4, liquidHeight0Max);
                half layerWarpMask = 1.0 - abs(lerp01 - 0.5) * 2.0;
                lerp01 = lerp01 + (noise.x - 0.5) * _LayerWarpInt * layerWarpMask;
                half4 colorMixed = lerp(currentColor, nextColor, lerp01);
                
                
                // 气泡
                float2 bubbleUV = ParallaxMappingUV(input.uv, input.viewDirTS);
                half bubbleMask = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;
                bubbleUV = input.uv;
                bubbleUV.y += _Time.x * _BubbleSpeed;
                bubbleMask += SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;
                half3 bubbleColor = bubbleMask;
                    //虚拟液面
                    half facing = isFrontFace ? 1.0 : -1.0;
                    float relativeHeight = float3(0, originPos.y + liquidHeightOS, 0);
                    half3 virtualPlaneNormal = float3(0,1,0);
                     // 计算视线与液面平面的交点
                    // 平面方程: y = relativeHeight
                    // 射线方程: intersectPos = input.positionWS + t * input.viewDirWS
                    // 解t: input.positionWS.y + t * input.viewDirWS.y = relativeHeight
                    float t = (relativeHeight - input.positionWS.y) / input.viewDirWS.y;
                    float3 intersectPos = input.positionWS + t * input.viewDirWS;
                // 杂质
                half dirtyMask = SAMPLE_TEXTURE2D(_DirtyTex, sampler_DirtyTex, input.uv).r;

                // 按浑浊程度遮罩（显示气泡或杂质）
                half mask = colorMixed.a;
                half3 maskColor = lerp(bubbleColor, dirtyMask * colorMixed, mask);

                half3 finalColor = colorMixed + maskColor;
                
                
                return half4(finalColor, _Transparent);
            }
            ENDHLSL
        }
    }
}
