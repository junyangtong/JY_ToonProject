Shader "JY/Toon/Liquid"
{
    Properties
    {
        _NoiseTex ("Noise R:LayerWarp G: B: A:", 2D) = "black" {}
        _LayerWarpInt ("LayerWarpInt", Range(0, 1)) = 0.5
        _MaxLiquidHeight("MaxLiquidHeight", Float) = 1.0
        _LiquidHeightOffset ("LiquidHeightOffset", Float)  = 0.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off
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
            CBUFFER_END

            half4 _LiquidLayerColor[MAX_LAYER];
            half _LiquidLayerIsMaked[MAX_LAYER];
            half _LiquidLayerLerpRange[MAX_LAYER];
            half _LiquidHeight01;

            TEXTURE2D(_NoiseTex);    SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }
            
            float4 frag(Varyings input) : SV_Target
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
                int idCurrent = floor(liquidHeight0Max - 0.5);
                int idNext = idCurrent + 1;
                
                // 混合颜色
                half4 colorCurrent = _LiquidLayerColor[idCurrent];
                half4 colorNext = _LiquidLayerColor[idNext];
                half lerpRange = _LiquidLayerLerpRange[idCurrent];
                half lerp01 = smoothstep(idNext - 0.4, idNext + 0.4, liquidHeight0Max);
                half layerWarpMask = 1.0 - abs(lerp01 - 0.5) * 2.0;
                lerp01 = lerp01 + (noise.x - 0.5) * _LayerWarpInt * layerWarpMask;
                half4 colorMixed = lerp(colorCurrent, colorNext, lerp01);
                
                // 遮罩
                half mask = _LiquidLayerIsMaked[idCurrent];

                return colorMixed;
            }
            ENDHLSL
        }
    }
}
