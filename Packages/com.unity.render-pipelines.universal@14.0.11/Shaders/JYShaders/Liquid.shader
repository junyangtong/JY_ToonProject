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

        _WaveInt ("WaveInt", Float) = 1.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent"
                "Queue" = "Transparent" 
        }
        HLSLINCLUDE
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
            half _WaveInt;
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

        // 视差映射
        float2 ParallaxMappingUV(float2 uv, float3 viewDirTS)
        {
            float2 p = viewDirTS.xy / (viewDirTS.z + 0.0001) * _BubbleParallax;
            return uv - p;
        }

        // 视空间深度->归一化深度
        float EyeDepthToLinear01(float eyeDepth)
        {
            return (rcp(eyeDepth) - _ZBufferParams.w) / _ZBufferParams.z;
        }
        ENDHLSL

        // TODO:使用RenderObj
        Pass
        {
            Name "Draw Front"
            Tags {"LightMode" = "SRPDefaultUnlit"}
            Stencil
            {
                Ref 1
                Comp Always
                Pass replace
            }
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DrawFrontFrag
            
            float4 DrawFrontFrag(Varyings input, out float depthOUT : SV_Depth) : SV_Target
            {
                // 采样噪声图集
                half4 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.uv);

                // 计算相对坐标
                float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPosWS;

                // 高度裁剪
                    // 扰动
                    float wave = noise.y * _WaveInt;
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset + wave;
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
                
                // 边缘光
                half rimMask = 0.0;

                // 气泡
                float2 bubbleUV = input.uv;
                bubbleUV.y += _Time.x * _BubbleSpeed;
                half bubbleMask = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;
                bubbleUV = ParallaxMappingUV(input.uv, input.viewDirTS);

                depthOUT = input.positionCS.z;

                bubbleMask += SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;

                
                half3 bubbleColor = bubbleMask;

                // 杂质
                half dirtyMask = SAMPLE_TEXTURE2D(_DirtyTex, sampler_DirtyTex, input.uv).r;

                // 按浑浊程度遮罩（显示气泡或杂质）
                half mask = colorMixed.a;
                half3 maskColor = lerp(bubbleColor, dirtyMask * colorMixed, mask);

                half3 finalColor = colorMixed + maskColor;
                
                
                return half4(finalColor, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Draw Back"
            Tags {"LightMode" = "UniversalForward"}
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Replace
            }
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DrawBackFrag
            
            float4 DrawBackFrag(Varyings input, out float depthOUT : SV_Depth) : SV_Target
            {
                // 采样噪声图集
                half4 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, input.uv);

                // 计算相对坐标
                float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPosWS;

                // 高度裁剪
                    // 扰动
                    float wave = noise.y * _WaveInt;
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset + wave;
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
                
                // 边缘光
                half rimMask = 0.0;

                // 气泡
                float2 bubbleUV = input.uv;
                bubbleUV.y += _Time.x * _BubbleSpeed;
                half bubbleMask = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;
                bubbleUV = ParallaxMappingUV(input.uv, input.viewDirTS);
                    //虚拟液面
                    // n * (intersectPos - liquidHeightWS) = 0
                    // intersectPos = input.positionWS + t * input.viewDirWS
                        half3 liquidHeightWS = float3(0, originPosWS.y + liquidHeightOS, 0);
                        half3 n = float3(0,1,0);
                        float3 intersectPosWS = input.positionWS + input.viewDirWS * dot(n, liquidHeightWS - input.positionWS) / dot(n, input.viewDirWS);
                        bubbleUV = (intersectPosWS - originPosWS).xz;
                        // 虚拟平面深度覆盖深度缓冲
                        depthOUT = EyeDepthToLinear01(dot(intersectPosWS - GetCameraPositionWS(), -UNITY_MATRIX_V[2].xyz));
                        // 菲涅尔
                        rimMask = normalize(input.viewDirWS).y;
                bubbleMask += SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;

                
                half3 bubbleColor = bubbleMask;

                // 杂质
                half dirtyMask = SAMPLE_TEXTURE2D(_DirtyTex, sampler_DirtyTex, input.uv).r;

                // 按浑浊程度遮罩（显示气泡或杂质）
                half mask = colorMixed.a;
                half3 maskColor = lerp(bubbleColor, dirtyMask * colorMixed, mask);

                half3 finalColor = colorMixed + maskColor;
                
                
                return half4(finalColor, 1);
            }
            ENDHLSL
        }

    }
}
