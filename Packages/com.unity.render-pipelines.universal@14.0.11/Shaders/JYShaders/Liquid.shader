Shader "JY/Toon/Liquid"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _MaxLiquidHeight("液面最大高度", Float) = 1.0
        _LiquidHeightOffset ("液面高度矫正", Float)  = 0.0
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
            half4 _MainTex_ST;
            half4 _Color;
            half _MaxLiquidHeight;
            half _LiquidHeightOffset;
            CBUFFER_END

            half4 _LiquidLayerColor[MAX_LAYER];
            half _LiquidLayerIsMaked[MAX_LAYER];
            half _LiquidLayerLerp[MAX_LAYER];
            half _LiquidHeight01;

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);

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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.positionOS = input.positionOS.xyz;
                return output;
            }
            
            float4 frag(Varyings input) : SV_Target
            {
                // 计算相对坐标
                float3 originPos = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 relativePos = input.positionWS.xyz - originPos;

                // 高度裁剪
                float liquidHeightOS = _LiquidHeight01 + _LiquidHeightOffset;
                float clipPos = liquidHeightOS - relativePos.y;
                clip(clipPos);
                
                // 获取每层液体的id
                /* relativePos.y => _LiquidHeight01
                int idCurrent = floor(relativePos.y
 */
                // 计算颜色
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;

                return color;
            }
            ENDHLSL
        }
    }
}
