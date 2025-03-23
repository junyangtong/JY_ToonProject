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

        _WaveAmplitude ("WaveAmplitude", Float) = 0.3
        _WaveFrequency ("WaveFrequency", Float) = 3.0
        _WaveSpeed ("WaveSpeed", Float) = 1.0

        [Header(Front)]
        _WaterLineWidth ("WaterLineWidth", Float) = 1.0
        _RimInt ("RimInt", Float) = 1.0

        [Header(Back)]
        _ShallowRange("Shallow Range", Float) = 1.0
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
            half _WaterLineWidth;
            half _RimInt;
            half _ShallowRange;
        CBUFFER_END

        half4 _LiquidLayerColor[MAX_LAYER];
        half _LiquidLayerLerpRange[MAX_LAYER];
        half _LiquidLayerMaskInfo[MAX_LAYER];
        half _LiquidHeight01;
        half _WaveAmplitude;
        half _WaveFrequency;
        half _WaveSpeed;

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

        //计算波形
        struct WaveInfo
        {
            float height;
            float3 normal;
        };
        
        WaveInfo CalculateWave (float3 position)
        {
            WaveInfo waveInfo;
            float time = _Time.y * _WaveSpeed;

            float waveHeight = _WaveAmplitude * 0.05 * sin(position.z * _WaveFrequency + time)
                             + _WaveAmplitude * 0.05 * sin(position.x * _WaveFrequency + time);

            float3 normal = float3(0, 1, 0);

            waveInfo.height = waveHeight;
            waveInfo.normal = normal;

            return waveInfo;
        }
        
        ENDHLSL

        // TODO:使用RenderObj代替多pass?
        Pass
        {
            Name "Draw Front"
            Tags {"LightMode" = "SRPDefaultUnlit"}
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                ZFail Replace // 确保在被其他物体遮挡时也能写入模板值
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
                    WaveInfo waveInfo = CalculateWave(relativePos);
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset + waveInfo.height;
                float clipPos = liquidHeightOS - relativePos.y;
                clip(clipPos);
                
                // 获取每层液体的id
                float liquidHeight0Max = relativePos.y / _MaxLiquidHeight * MAX_LAYER;
                uint currentID = floor(liquidHeight0Max - 0.5);
                int nextID = min(MAX_LAYER - 1, currentID + 1);
                
                // 混合颜色
                half4 currentColor = _LiquidLayerColor[currentID];
                half4 nextColor = _LiquidLayerColor[nextID];
                half lerpRange = _LiquidLayerLerpRange[min(MAX_LAYER - 1, currentID+1)];
                half lerp01 = smoothstep(nextID - lerpRange, nextID + lerpRange, liquidHeight0Max);
                half layerWarpMask = 1.0 - abs(lerp01 - 0.5) * 2.0;
                lerp01 = lerp01 + (noise.x - 0.5) * _LayerWarpInt * layerWarpMask;
                half4 colorMixed = lerp(currentColor, nextColor, lerp01);
                
                
                // 边缘光
                half rimMask = _RimInt;
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
                half3 maskColor = lerp(bubbleColor, dirtyMask * colorMixed.rgb, mask);

                half3 finalColor = colorMixed.rgb;
                // 吃水线
                half waterlineMask = smoothstep(_WaterLineWidth, 0, clipPos);
                finalColor = lerp(finalColor, finalColor*0.5, waterlineMask);//TODO:优化吃水线效果
                
                half alpha = _Transparent * colorMixed.a;
                return half4(finalColor.rgb, alpha);
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
                    WaveInfo waveInfo = CalculateWave(relativePos);
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset + waveInfo.height;;
                float clipPos = liquidHeightOS - relativePos.y;
                clip(clipPos);
                
                // 获取每层液体的id
                float liquidHeight0Max = relativePos.y / _MaxLiquidHeight * MAX_LAYER;
                uint currentID = floor(liquidHeight0Max - 0.5);
                int nextID = min(MAX_LAYER - 1, currentID + 1);
                
                // 混合颜色
                half4 currentColor = _LiquidLayerColor[currentID];
                half4 nextColor = _LiquidLayerColor[nextID];
                half lerpRange = _LiquidLayerLerpRange[min(MAX_LAYER - 1, currentID+1)];
                half lerp01 = smoothstep(nextID - lerpRange, nextID + lerpRange, liquidHeight0Max);
                half layerWarpMask = 1.0 - abs(lerp01 - 0.5) * 2.0;
                lerp01 = lerp01 + (noise.x - 0.5) * _LayerWarpInt * layerWarpMask;
                half4 colorMixed = lerp(currentColor, nextColor, lerp01);

                //虚拟液面
                // n * (intersectPos - liquidHeightWS) = 0
                // intersectPos = input.positionWS + t * input.viewDirWS
                half3 liquidHeightWS = float3(0, originPosWS.y + liquidHeightOS, 0);
                half3 n = float3(0,1,0);
                float3 intersectPosWS = input.positionWS + input.viewDirWS * dot(n, liquidHeightWS - input.positionWS) / dot(n, input.viewDirWS);
                // 虚拟平面深度覆盖深度缓冲
                depthOUT = EyeDepthToLinear01(dot(intersectPosWS - GetCameraPositionWS(), -UNITY_MATRIX_V[2].xyz));

                // 菲涅尔
                half fresnel = normalize(input.viewDirWS).y;

                // 气泡
                float2 bubbleUV = input.uv;
                bubbleUV.y += _Time.x * _BubbleSpeed;
                half bubbleMask = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;
                bubbleUV = ParallaxMappingUV(input.uv, input.viewDirTS);
                bubbleUV = (intersectPosWS - originPosWS).xz;    
                bubbleMask += SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV).r;
                half3 bubbleColor = bubbleMask;

                // 杂质
                half dirtyMask = SAMPLE_TEXTURE2D(_DirtyTex, sampler_DirtyTex, input.uv).r;

                // 按浑浊程度遮罩（显示气泡或杂质）
                half mask = colorMixed.a;
                half3 maskColor = lerp(bubbleColor, dirtyMask * colorMixed.rgb, mask);

                half3 finalColor = colorMixed.rgb;
                
                // 深浅水变化
                half shallowFactor = smoothstep(_ShallowRange, _ShallowRange + 0.3, clipPos);
                uint currentIDMax = min(MAX_LAYER - 1, floor(_LiquidHeight01 * MAX_LAYER)); // 取当前最高层颜色
                half4 currentColorMax = _LiquidLayerColor[currentIDMax];
                finalColor = lerp(finalColor, currentColorMax.rgb, shallowFactor);
                half alpha = _Transparent * currentColorMax.a;
                alpha = lerp(alpha*0.2, alpha, shallowFactor);
                
                return half4(finalColor.rgb, alpha);
            }
            ENDHLSL
        }

    }
}
