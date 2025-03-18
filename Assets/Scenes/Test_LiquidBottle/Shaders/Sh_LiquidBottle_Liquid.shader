Shader "LiquidBottle/Liquid"
{
    Properties
    {
        [Space()] [Header(__________________Opacity__________________)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("SrcBlend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("DstBlend", Float) = 10
        
        [Space()] [Header(__________________Mask__________________)]
//        [NoScaleOffset] _MaskTex2DArr ("内壁遮罩数组", 2D) = "black" {}
        _MaskUVScale ("遮罩缩放", Float) = 1
        _MaskInt ("遮罩强度", Float) = 1
        
        [Space()] [Header(__________________Warp__________________)]
        _LerpWarpTex ("分界扰动图", 2D) = "linearGray" {}
        _LerpWarpInt ("扰动强度", Range(0, 1)) = 0
        
        [Space()] [Header(__________________Bubble__________________)]
        [NoScaleOffset] _BubbleTex ("泡沫遮罩", 2D) = "black" {}
        _Bubble_SV ("泡沫缩放(XY)速度(ZW)", Vector) = (1,1,0,1)
        _BubblePlxBias ("泡沫视差偏移", Float) = 0.1
        _BubbleInt ("泡沫强度", Float) = 1
        
        [Space()] [Header(__________________Height__________________)]
        [NoScaleOffset] _HeightMap ("液面高度图", 2D) = "black" {}
        _HeightMap_SV ("高度缩放(XY)速度(ZW)", Vector) = (1,1,1,1)
        _HeightWarpInt ("液面高度缩放", Range(0, 0.2)) = 0.05
        _MaxLiquidHeightOS ("最大相对液面高度", Float) = 1
        _LiquidHeight01 ("当前液面高度(归一化)", Range(0, 1)) = 0.5
        
        [Space()] [Header(__________________Rim__________________)]
        _RimInt ("边缘光强度", Float) = 0.5
        _FresnelPow ("边缘光次幂", Range(1, 10)) = 2
        _EdgeWidth ("液面高亮宽度", Range(0, 0.1)) = 0.05
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }
        Pass
        {
            Stencil
            {
	            Ref 10          // 写入的模板值
                Comp NotEqual   // 模板值比较条件
	            Pass Replace    // 通过比较条件时的操作
                ZFail Replace   // 通过比较条件, 但不通过深度测试时的操作
            }
            
            Blend [_SrcBlend] [_DstBlend]
            ZWrite on
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _LerpWarpTex, _BubbleTex, _HeightMap;
            Texture2DArray<half> _MaskTex2DArr;  SamplerState sampler_MaskTex2DArr;
            CBUFFER_START(UnityPerMaterial)
            // 遮罩
            float _MaskUVScale;
            half _MaskInt;
            // 扰动
            float4 _LerpWarpTex_ST;
            half _LerpWarpInt;
            // 视差泡沫
            float4 _Bubble_SV;
            float _BubblePlxBias;
            half _BubbleInt;
            // 高度
            float4 _HeightMap_SV;
            half _HeightWarpInt;
            float _MaxLiquidHeightOS;
            float _LiquidHeight01;
            float4 _RelativeOriginAndFlag = 0;  // cmd.DrawRenderer绘制时, shader取到的包围盒坐标不正确, 从外部传
            // 边缘光
            half _RimInt;
            half _FresnelPow;
            half _EdgeWidth;
            CBUFFER_END

            #define MAX_LAYER_NUM 5
            half4 liquidRGBA[MAX_LAYER_NUM];
            half liquidBubbleInt[MAX_LAYER_NUM];
            half liquidLerpRange[MAX_LAYER_NUM];

            // 基于包围盒获得mesh的局部坐标系原点
            float3 GetRelativeOrigin(float3 posWS)
            {
                float3 posWS_Origin = (unity_RendererBounds_Min + unity_RendererBounds_Max) * 0.5;
                posWS_Origin.y = unity_RendererBounds_Min.y;
                return posWS_Origin;
            }

            // 直线平面交点
            float3 GetPos_PlaneCrossRay(float3 pos_Ray, float3 dir_Ray, float3 pos_Plane, float3 nDir_Plane)
            {
                return pos_Ray + dir_Ray * dot(nDir_Plane, pos_Plane - pos_Ray) / dot(nDir_Plane, dir_Ray);
            }

            // 视空间深度->归一化深度
            float EyeDepthToLinear01(float eyeDepth)
            {
                return (rcp(eyeDepth) - _ZBufferParams.w) / _ZBufferParams.z;
            }
            
            struct a2v
            {
                float3 posOS	: POSITION;
                half3 nDirOS    : NORMAL;
                half4 tDirOS    : TANGENT;
                half color      : COLOR;    // 顶点色.R区分内外侧
                float2 uv       : TEXCOORD0;
            };

            struct v2f
            {
                float4 posCS	        : SV_POSITION;
                float3 posWS            : TEXCOORD0;
                half3 nDirWS            : TEXCOORD1;
                float4 uv_Mask_Bubble   : TEXCOORD2;
                nointerpolation bool bPlane :  TEXCOORD3;
            };

            v2f vert(a2v i)
            {
                v2f o;
                o.posWS = TransformObjectToWorld(i.posOS);
                o.posCS = TransformWorldToHClip(o.posWS);
                o.nDirWS = TransformObjectToWorldNormal(i.nDirOS);
                o.uv_Mask_Bubble.xy = _MaskUVScale * i.uv;
                o.bPlane = i.color.r;
                
                // 视差偏移
                half3 tDirWS = TransformObjectToWorldDir(i.tDirOS.xyz);
                half3 bDirWS = normalize(cross(o.nDirWS, tDirWS.xyz) * i.tDirOS.w);
                half3x3 TBN = half3x3(tDirWS, bDirWS, o.nDirWS);
                half3 vDirVS = TransformWorldToTangent(GetCameraPositionWS() - o.posWS, TBN);
                o.uv_Mask_Bubble.zw = i.uv - vDirVS.xy * _BubblePlxBias / vDirVS.z;
                o.uv_Mask_Bubble.zw = _Bubble_SV.xy * o.uv_Mask_Bubble.zw - _Bubble_SV.zw * _Time.x;
                return o;
            }

            half4 frag(v2f i, out float depthOUT : SV_Depth) : SV_Target
            {
                // 局部坐标
                float3 posWS = i.posWS;
                float3 posWS_Origin = lerp(GetRelativeOrigin(posWS), _RelativeOriginAndFlag.xyz, _RelativeOriginAndFlag.w);
                float3 pos_Relative = i.posWS - posWS_Origin;

                // 边缘高度
                float2 uv_Edge = _HeightMap_SV.xy * pos_Relative.xz + _HeightMap_SV.zw * _Time.x;
                half h_Edge = _HeightWarpInt * (tex2D(_HeightMap, uv_Edge).r - 0.5);

                // clip
                half liquidHeightOS = _MaxLiquidHeightOS * _LiquidHeight01;
                half clipH = liquidHeightOS - pos_Relative.y + h_Edge;
                clip(clipH);
                
                // 边缘光
                half3 nDirWS = normalize(i.nDirWS);
                half3 vDirWS = normalize(GetCameraPositionWS() - posWS);
                half nv = dot(nDirWS, vDirWS);
                half rimMask = saturate(nv);

                // 液面模式
                half posRelative_Y = pos_Relative.y;
                float2 uv_Mask = i.uv_Mask_Bubble.xy;
                depthOUT = i.posCS.z;
                [branch]
                if (i.bPlane)
                {
                    // 假平面
                    float3 posWS_Plane = GetPos_PlaneCrossRay(
                        posWS, vDirWS,
                        float3(0, posWS_Origin.y + liquidHeightOS, 0), float3(0,1,0)
                    );

                    // 用平面的相对坐标采样顶层的液体
                    uv_Mask = (posWS_Plane - posWS_Origin).xz;
                    posRelative_Y = liquidHeightOS;
                    
                    // 边缘光
                    rimMask = vDirWS.y; // dot(float3(0,1,0), vDirWS)

                    // 深度改写为平面
                    depthOUT = EyeDepthToLinear01(dot(posWS_Plane - GetCameraPositionWS(), -UNITY_MATRIX_V[2].xyz));
                }

                // 边缘光
                rimMask = _RimInt * (
                    pow(1 - rimMask, _FresnelPow) +     // nv边缘光
                    smoothstep(_EdgeWidth, 0, clipH)    // 液面厚度
                );

                /////// 液体层混合 //////
                // 液体层混合高度准备
                half liquidHeight01_Current = saturate(posRelative_Y / _MaxLiquidHeightOS);    // 输入高度对应的归一化高度
                uint3 size;
	            _MaskTex2DArr.GetDimensions(size.x, size.y, size.z);
                half liquidHeight_MulLayerNum = liquidHeight01_Current * size.z;
                
                // 计算当前层的上下界ID
                uint id_Floor = floor(max(0, liquidHeight_MulLayerNum - 0.5)); // 当前区间下界的层ID
                uint id_Ceil = id_Floor + 1;
                half mixH = id_Ceil;      // 当前混合区间的起始位置
                half lerpRange = liquidLerpRange[id_Ceil];

                // 混合分界插值系数扰动
                float2 uv_Warp = _LerpWarpTex_ST.xy * uv_Mask + _LerpWarpTex_ST.zw;
                half lerpRangeWarpMask = _LerpWarpInt * (tex2D(_LerpWarpTex, uv_Warp).r - 0.5);
                half lerp01 = smoothstep(mixH-lerpRange, mixH+lerpRange, liquidHeight_MulLayerNum);
                lerp01 = saturate(lerp01 + (1 - abs(lerp01 - 0.5) * 2) * lerpRangeWarpMask);

                // 颜色层
                half4 currentRGBA = lerp(liquidRGBA[id_Floor], liquidRGBA[id_Ceil], lerp01);
                currentRGBA.rgb *= currentRGBA.rgb;     // sRGB, 使调参显示和最终效果尽量保持一致

                // 静态遮罩层
                half mask0 = _MaskTex2DArr.Sample(sampler_MaskTex2DArr, float3(uv_Mask, id_Floor)).r;
                half mask1 = _MaskTex2DArr.Sample(sampler_MaskTex2DArr, float3(uv_Mask, id_Ceil)).r;
                half4 maskRGBA = half4(_MaskInt * currentRGBA.rgb, lerp(mask0, mask1, lerp01));    // 保持原本颜色提亮

                // 动态泡沫层
                half4 bubbleRGBA;
                bubbleRGBA.rgb = _BubbleInt;
                bubbleRGBA.a =
                    lerp(liquidBubbleInt[id_Floor], liquidBubbleInt[id_Ceil], lerp01) *
                    tex2D(_BubbleTex, i.uv_Mask_Bubble.zw).r;

                ////// 混合 //////
                half4 finalRGBA = 0;
                finalRGBA.rgb = currentRGBA.rgb + maskRGBA.rgb*maskRGBA.a + bubbleRGBA.rgb*bubbleRGBA.a;
                finalRGBA.rgb += rimMask * finalRGBA.rgb;
                finalRGBA.a = max(currentRGBA.a, max(maskRGBA.a, bubbleRGBA.a));
                return finalRGBA;
            }
            #undef MAX_LAYER_NUM
            ENDHLSL
        }
    }
}