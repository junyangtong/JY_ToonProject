Shader "Editor/LiquidBottle_ArrayUI"
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex vert
            #pragma fragment frag
            Texture2DArray<half> _Tex2DArr;  SamplerState sampler_Tex2DArr;
            
            struct a2v
            {
                float3 posOS : POSITION;
                float2 uv0   : TEXCOORD0;
            };
            struct v2f
            {
                float4 posCS              : SV_POSITION;
                noperspective float2 uv0  : TEXCOORD0;
            };
            v2f vert(a2v i)
            {
                v2f o;
                o.posCS = TransformObjectToHClip(i.posOS);
                o.uv0 = i.uv0;
                return o;
            }
            
            half4 frag(v2f i) : SV_Target
            {
                uint3 size;
	            _Tex2DArr.GetDimensions(size.x, size.y, size.z);
                float3 uv = float3(i.uv0, floor(size.z * i.uv0.y));
                return half4(_Tex2DArr.SampleLevel(sampler_Tex2DArr, uv, 0).rrr, 1);
            }
            ENDHLSL
        }
    }
}