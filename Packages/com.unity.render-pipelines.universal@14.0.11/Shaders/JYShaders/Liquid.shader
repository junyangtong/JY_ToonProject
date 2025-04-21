Shader "JY/Toon/Liquid"
{
    Properties
    {
        _Transparent ("Transparent", Range(0, 1)) = 1.0
        _CubeMap ("CubeMap", Cube) = "white" {}
        _LerpNoise ("LerpNoise", 2D) = "white" {}
        _LayerWarpSize ("LayerWarpSize", Float) = 0.5
        _MaxLiquidHeight("MaxLiquidHeight", Float) = 1.0
        _LiquidHeightOffset ("LiquidHeightOffset", Float)  = 0.0

        _BubbleTex ("BubbleTex", 2D) = "white" {}
        _BubbleOutParallax ("BubbleOutParallax", Float) = 0.02
        _BubbleInParallax ("BubbleInParallax", Float) = 0.3
        _BubbleSpeed ("BubbleSpeed", Float) = 1.0

        _WaveAmplitude ("WaveAmplitude", Float) = 0.3
        _WaveFrequency ("WaveFrequency", Float) = 3.0
        _WaveSpeed ("WaveSpeed", Float) = 1.0

        [Header(Front)]
        _WaterLineWidth ("WaterLineWidth", Float) = 1.0
        _RimInt ("RimInt", Float) = 1.0
        _WaterLineSmoothness ("WaterLineSmoothness", Float) = 1.0

        [Header(Back)]
        _ShallowRange("Shallow Range", Float) = 1.0
        _FakePlaneUV ("Fake Plane Uv X:surface Y:side", Vector) = (0.0, 0.0, 0.0, 0.0)

        [Header(Animation)]
        _UVOffest("UVOffest XY:Blend ZW:Pour", Vector) = (0.0, 0.0, 0.0, 0.0)
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
        #define MAX_LAYER 5 // 最大层数
        #define PI 3.1415926

        CBUFFER_START(UnityPerMaterial)
            half _MaxLiquidHeight;
            half _LiquidHeightOffset;
            half _LayerWarpSize;
            half _BubbleSpeed;
            half _BubbleOutParallax;
            half _BubbleInParallax;
            half _Transparent;
            half _WaterLineWidth;
            half _RimInt;
            half _ShallowRange;
            half4 _BubbleTex_ST;
            half4 _FakePlaneUV;
            half _WaterLineSmoothness;
        CBUFFER_END

        half _MaxLayers; // 当前最大层数
        half4 _LiquidLayerColor[MAX_LAYER];
        half _LiquidLayerLerpRange[MAX_LAYER];
        half _BubbleInt[MAX_LAYER];
        half _LerpWarpInt[MAX_LAYER];
        half _LerpWarpSize[MAX_LAYER];
        half _LiquidHeight01;
        half _WaveAmplitude;
        half _WaveFrequency;
        half _WaveSpeed;
        half4 _UVOffest;
        half _WaveType;

        TEXTURE2D(_BubbleTex);   SAMPLER(sampler_BubbleTex);
        TEXTURECUBE(_CubeMap);   SAMPLER(sampler_CubeMap);
        TEXTURE2D(_LerpNoise);   SAMPLER(sampler_LerpNoise);
        TEXTURE2D_ARRAY(_LiquidLayerMaskTex);   SAMPLER(sampler_LiquidLayerMaskTex);
        
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
            float3 normalWS : TEXCOORD5;
        };

        Varyings vert(Attributes input)
        {
            Varyings output = (Varyings)0;

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
            output.normalWS = normalInput.normalWS;

            output.uv = input.uv;
            return output;
        }

        // 视差映射
        float2 ParallaxMappingUV(float2 uv, float3 viewDirTS, float parallax)
        {
            float2 p = viewDirTS.xy / (viewDirTS.z + 0.0001) * parallax;
            return uv - p;
        }

        // 视空间深度->归一化深度
        float EyeDepthToLinear01(float eyeDepth)
        {
            return (rcp(eyeDepth) - _ZBufferParams.w) / _ZBufferParams.z;
        }

        //极坐标
        float2 Polar(float2 uv)
        {
            float distance = length(uv);
            distance *= 2.0f;
            float angle = atan2(uv.x,uv.y);
            float angle01 = angle / PI * 0.5f + 0.5f;
            return float2(angle01 * 4.0f, distance);
        }

        //计算波形
        struct WaveInfo
        {
            float height;
            float3 normal;
        };
        
        WaveInfo CalculateWave(float3 position)
        {
            WaveInfo waveInfo;
            float time = _Time.y * _WaveSpeed;

            float waveHeight = 0.0;
            if (_WaveType > 0.5)
            {
                waveHeight = _WaveAmplitude * 0.05 * sin(position.z * _WaveFrequency + time)
                             + _WaveAmplitude * 0.05 * sin(position.x * _WaveFrequency + time);
            }
            else
            {
                position.xz = Polar(position.xz);// 极坐标
                waveHeight = _WaveAmplitude * 0.05 * sin(position.x * PI * 3.0  + time);
            }                

            float3 T = float3
            (
                1.0,
                _WaveAmplitude * 0.05 * _WaveFrequency * cos(position.x * _WaveFrequency + time) 
                * _WaveAmplitude * 0.05 * sin(position.z * _WaveFrequency + time),
                0.0
            );
            float3 B = float3
            (
                0.0,
                _WaveAmplitude * 0.05 * _WaveFrequency * sin(position.x * _WaveFrequency + time) 
                * _WaveAmplitude * 0.05 * cos(position.z * _WaveFrequency + time),
                1.0
            );

            float3 N = cross(B, T);
            float3 normal = normalize(N);
            
            waveInfo.normal = normal;
            waveInfo.height = waveHeight;

            return waveInfo;
        }

        // 反射UV
        float3 GetReflectionUV(float3 normalWS, float3 viewDirWS)
        {
            return  reflect(viewDirWS, normalWS);
        }
        
        ENDHLSL

        Pass
        {
            Name "Draw Front"
            Tags {"LightMode" = "SRPDefaultUnlit"}
            /* Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                ZFail Replace // 确保在被其他物体遮挡时也能写入模板值
            } */
            Cull Back
            Blend One Zero
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DrawFrontFrag
            
            float4 DrawFrontFrag(Varyings input) : SV_Target
            {
                // 计算相对坐标
                float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPosWS;

                // 高度裁剪
                    WaveInfo waveInfo = CalculateWave(relativePos);
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset;
                float clipPos = liquidHeightOS - relativePos.y + waveInfo.height;
                clip(clipPos);
                
                // 获取每层液体的id
                float liquidHeight0Max = relativePos.y / _MaxLiquidHeight * _MaxLayers;
                uint currentID = floor(liquidHeight0Max - 0.5);
                int nextID = min(_MaxLayers - 1, currentID + 1);
                
                // 计算混合范围
                half4 currentColor = _LiquidLayerColor[currentID];
                half4 nextColor = _LiquidLayerColor[nextID];
                half lerpRange = _LiquidLayerLerpRange[nextID];
                half lerp01 = smoothstep(nextID - lerpRange, nextID + lerpRange, liquidHeight0Max);
                half lerpWarpInt = _LerpWarpInt[nextID];
                half lerpWarpSize = _LerpWarpSize[nextID];
                half layerWarpMask = 1.0 - abs(lerp01 - 0.5) * 2.0;
                half lerpNoise = SAMPLE_TEXTURE2D(_LerpNoise, sampler_LerpNoise, input.uv * lerpWarpSize * _LayerWarpSize).r;
                lerp01 = lerp01 + (lerpNoise - 0.5) * lerpWarpInt * layerWarpMask;

                // 左右边缘遮罩
                half rimMask = pow(saturate(dot(input.normalWS, normalize(input.viewDirWS))), _RimInt);
                // 上边缘遮罩
                half topMask = smoothstep(0.03, 0.2, clipPos);

                // UV动画
                input.uv += _UVOffest.xy;   // 搅拌
                input.uv += _UVOffest.zw * smoothstep(0.9, 0.2, clipPos); // 倒酒
                
                // 液体纹理
                half mask0 = _LiquidLayerMaskTex.Sample(sampler_LiquidLayerMaskTex, float3(input.uv*10.0, currentID)).r;
                half mask1 = _LiquidLayerMaskTex.Sample(sampler_LiquidLayerMaskTex, float3(input.uv*10.0, nextID)).r;
                half maskMixed = lerp(mask0, mask1, lerp01);
                
                // 混合颜色
                half4 colorMixed = lerp(currentColor, nextColor, lerp01);

                // 气泡
                float2 bubbleUV1 = ParallaxMappingUV(input.uv * _BubbleTex_ST.xy + _BubbleTex_ST.zw, input.viewDirTS, _BubbleOutParallax);
                bubbleUV1.y += _Time.x * _BubbleSpeed;
                float2 bubbleUV2 = ParallaxMappingUV(input.uv * _BubbleTex_ST.xy + _BubbleTex_ST.zw, input.viewDirTS, _BubbleInParallax);
                half bubbleMask = lerp(_BubbleInt[currentID], _BubbleInt[nextID], lerp01);
                half outBubble = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV1).r;
                half innerBubble = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV2).r;
                half3 bubbleCol = bubbleMask * (outBubble + innerBubble) * colorMixed.rgb * rimMask * topMask;

                // 液体纹理颜色
                half3 maskCol = maskMixed.r * colorMixed.rgb * rimMask * topMask;

                half3 finalColor = colorMixed.rgb + maskCol + bubbleCol;
                
                half alpha = _Transparent * colorMixed.a;

                // 吃水线
                    // 折射
                    half waterlineMask = min(smoothstep(0.1, 0.02, clipPos), smoothstep(0, _WaterLineWidth, clipPos)) * pow(saturate(dot(input.normalWS, normalize(input.viewDirWS))), 1);
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    float2 refractionUV = screenUV + waterlineMask * 0.3;
                    half3 waterlineCol = SampleSceneColor(refractionUV) * waterlineMask; 
                    finalColor = lerp(finalColor, finalColor * 0.5, waterlineMask) + waterlineCol * (1.0 - alpha);
                    // 反射
                    float3 reflectVector = reflect(-input.viewDirWS + waterlineMask * 0.2, input.normalWS);
                    half4 reflectColor = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector, 6.0 - _WaterLineSmoothness*6.0)) * (1.0 - alpha);
                    finalColor = finalColor + reflectColor.rgb * waterlineMask;
                
                alpha += waterlineMask + maskMixed;
                return half4(finalColor, saturate(alpha));
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
            Blend One Zero
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DrawBackFrag
            
            float4 DrawBackFrag(Varyings input, out float depthOUT : SV_Depth) : SV_Target
            {
                // 计算相对坐标
                float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPosWS;

                // 高度裁剪
                    WaveInfo waveInfo = CalculateWave(relativePos);
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset;
                float clipPos = liquidHeightOS - relativePos.y + waveInfo.height;
                clip(clipPos);
                
                // 获取每层液体的id
                float liquidHeight0Max = relativePos.y / _MaxLiquidHeight * _MaxLayers;
                uint currentID = floor(liquidHeight0Max - 0.5);
                int nextID = min(_MaxLayers - 1, currentID + 1);

                //虚拟液面
                // n * (intersectPos - liquidHeightWS) = 0
                // intersectPos = input.positionWS + t * input.viewDirWS
                half3 liquidHeightWS = float3(0.0, originPosWS.y + liquidHeightOS, 0.0);
                half3 n = half3(0,1,0);//waveInfo.normal;
                float3 intersectPosWS = input.positionWS + input.viewDirWS * dot(n, liquidHeightWS - input.positionWS) / dot(n, input.viewDirWS);
                // 虚拟平面深度覆盖深度缓冲
                float3 planeViewDirWS = intersectPosWS - GetCameraPositionWS();
                depthOUT = EyeDepthToLinear01(dot(planeViewDirWS, -UNITY_MATRIX_V[2].xyz));
                // 取当前最高层ID
                uint currentIDMax = min(_MaxLayers - 1, floor(_LiquidHeight01 * _MaxLayers)); 
                // 取当前最高层液体纹理
                half mask = _LiquidLayerMaskTex.Sample(sampler_LiquidLayerMaskTex, float3(intersectPosWS.xz, currentIDMax)).r;

                // 深浅水变化
                half shallowFactor = smoothstep(_ShallowRange, _ShallowRange + 0.3, clipPos);
                half4 currentColorMax = _LiquidLayerColor[currentIDMax];// 取当前最高层颜色
                half3 finalColor = currentColorMax.rgb;
                half alpha = _Transparent * currentColorMax.a;
                alpha = lerp(alpha * 0.9, alpha, shallowFactor);
                
                // 上边缘遮罩
                half topMask = smoothstep(0.03, 0.2, clipPos);
                
                // 水面边缘遮罩
                float3 center = originPosWS;
                center.y += liquidHeightOS;
                half circleDistance = length((intersectPosWS - center).xz);
                half circleMask = smoothstep(0.5, 1.0, circleDistance);
                finalColor.rgb = lerp(finalColor.rgb, finalColor.rgb * 1.78, circleMask);//混合深浅颜色

                // 气泡
                float2 bubbleUV1 = input.uv * _FakePlaneUV.y;
                bubbleUV1.y += _Time.x * _BubbleSpeed;
                float2 bubbleUV2 = intersectPosWS.xz * _FakePlaneUV.x;
                half bubbleMask = _BubbleInt[currentID];
                half outBubble = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV1).r * (1.0 - shallowFactor) * smoothstep(0.2, 0.3, liquidHeightOS);  // 液面透出的上升气泡 遮罩底部防止uv拉伸
                half innerBubble = SAMPLE_TEXTURE2D(_BubbleTex, sampler_BubbleTex, bubbleUV2).r * circleMask;           // 液面上的静止气泡
                half3 bubbleCol = bubbleMask * (outBubble + innerBubble) * finalColor * topMask;
                
                // 液体纹理颜色
                half3 maskCol = mask.r * finalColor;
                
                // 反射
                half fresnel = normalize(input.viewDirWS).y;
                float3 reflectionUV = GetReflectionUV(waveInfo.normal, planeViewDirWS);
                half4 reflectionColor = SAMPLE_TEXTURECUBE(_CubeMap, sampler_CubeMap, reflectionUV);
                
                alpha = lerp(alpha, alpha + max(max(reflectionColor.r, reflectionColor.g), reflectionColor.b), fresnel) + mask;   //反射要写入alpha后面混合使用
                finalColor = lerp(finalColor, finalColor + reflectionColor.rgb, fresnel) + maskCol + bubbleCol;
                return half4(finalColor, saturate(alpha));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Draw Mask"
            Tags {"LightMode" = "SRPDefaultUnlit"}
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                ZFail Replace // 确保在被其他物体遮挡时也能写入模板值
            }
            ColorMask 0
            Cull Back
            Blend One Zero
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment DrawMaskFrag
            
            float4 DrawMaskFrag(Varyings input) : SV_Target
            {
                // 计算相对坐标
                float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPosWS;

                // 高度裁剪
                    // 扰动
                    WaveInfo waveInfo = CalculateWave(relativePos);
                    
                float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset + waveInfo.height;
                float clipPos = liquidHeightOS - relativePos.y;
                clip(clipPos);
                
                return half4(0,0,0,0);
            }
            ENDHLSL
        }

    }
}
