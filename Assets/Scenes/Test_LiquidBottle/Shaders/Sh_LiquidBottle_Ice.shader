Shader "LiquidBottle/Ice"
{
    Properties
    {
        [Space()] [Header(__________________Dither__________________)]
        _DitherOpacity ("扎孔不透明度", Range(0, 1)) = 0.5
        
        [Space()] [Header(__________________Normal__________________)]
        [NoScaleOffset] [Normal] _NormalMap ("法线图", 2D) = "bump" {}
        _NormalInt ("法线强度", Range(0, 2)) = 0.05
        
        [Space()] [Header(__________________MatCap__________________)]
        [NoScaleOffset] _MatCapTex ("MatCap", 2D) = "black" {}
        [HDR]_MatCapTint ("MatCap色调", Color) = (1,1,1,1)
    }
    
    HLSLINCLUDE
    #pragma vertex vert
    #pragma multi_compile_instancing
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    
    sampler2D _NormalMap, _MatCapTex;
    CBUFFER_START(UnityPerMaterial)
    // 扎孔半透
    half _DitherOpacity;
    // 法线
    half _NormalInt;
    // MatCap
    half3 _MatCapTint;
    CBUFFER_END

    static int4x4 ditherMatrixs[] =
    {
        {   // 0/16
            {-1, -1, -1, -1},
            {-1, -1, -1, -1},
            {-1, -1, -1, -1},
            {-1, -1, -1, -1}
        },
        {   // 1/16
            {1, -1, -1, -1},
            {-1, -1, -1, -1},
            {-1, -1, -1, -1},
            {-1, -1, -1, -1}
        },
        {   // 2/16
            {1, -1, -1, -1},
            {-1, -1, -1, -1},
            {-1, -1, 1, -1},
            {-1, -1, -1, -1}
        },
        {   // 3/16
            {1, -1, 1, -1},
            {-1, -1, -1, -1},
            {-1, -1, 1, -1},
            {-1, -1, -1, -1}
        },
        {   // 4/16
            {1, -1, 1, -1},
            {-1, -1, -1, -1},
            {1, -1, 1, -1},
            {-1, -1, -1, -1}
        },
        {   // 5/16
            {1, -1, 1, -1},
            {-1, 1, -1, -1},
            {1, -1, 1, -1},
            {-1, -1, -1, -1}
        },
        {   // 6/16
            {1, -1, 1, -1},
            {-1, 1, -1, -1},
            {1, -1, 1, -1},
            {-1, -1, -1, 1}
        },
        {   // 7/16
            {1, -1, 1, -1},
            {-1, 1, -1, 1},
            {1, -1, 1, -1},
            {-1, -1, -1, 1}
        },
        {   // 8/16
            {1, -1, 1, -1},
            {-1, 1, -1, 1},
            {1, -1, 1, -1},
            {-1, 1, -1, 1}
        },
        // 剩下的直接基于上面取反
    };
    void DitherAlpha(float opacity, float2 uv_Screen)
    {
        uint arrayID = ceil(opacity * 16);
        uint2 id = frac(0.25 * uv_Screen * _ScreenParams.xy) * 4;
        if (arrayID > 8) clip(-ditherMatrixs[16 - arrayID][id.x][id.y]);
        else clip(ditherMatrixs[arrayID][id.x][id.y]);
    }
    
    struct a2v
    {
        float3 posOS	: POSITION;
        half3 nDirOS    : NORMAL;
        half4 tDirOS    : TANGENT;
        float2 uv       : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f
    {
        float4 posCS	    : SV_POSITION;
        float2 uv           : TEXCOORD1;
        float4 posScreen    : TEXCOORD2;
        half3x3 TBN         : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    v2f vert(a2v i)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(i)
        o.posCS = TransformObjectToHClip(i.posOS);
        o.uv = i.uv;
        o.posScreen = ComputeScreenPos(o.posCS);
        half3 nDirWS = TransformObjectToWorldNormal(i.nDirOS);
        half3 tDirWS = TransformObjectToWorldDir(i.tDirOS.xyz);
        half3 bDirWS = normalize(cross(nDirWS, tDirWS.xyz) * i.tDirOS.w);
        o.TBN = half3x3(tDirWS, bDirWS, nDirWS);
        return o;
    }

    half4 DrawIce(v2f i, out half2 nDirTS_XY01)
    {
        // 法线
        half3 nDirTS = UnpackNormal(tex2D(_NormalMap, i.uv));
        nDirTS_XY01 = saturate(0.5 * nDirTS.xy + 0.5);
        nDirTS.xy *= _NormalInt;
        half3x3 TBN = half3x3(normalize(i.TBN[0]), normalize(i.TBN[1]), normalize(i.TBN[2]));
        half3 nDirWS = normalize(mul(nDirTS, TBN));

        // MatCap
        half3 nDirVS = TransformWorldToViewDir(nDirWS);
        float2 uv_MatCap = 0.5 * nDirVS.xy + 0.5;
        half4 matCapRGBA = tex2D(_MatCapTex, uv_MatCap);
        matCapRGBA.rgb *= _MatCapTint;
        return matCapRGBA;
    }
    ENDHLSL
    
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" 
            "Queue" = "Transparent-1"
        }
        Pass
        {
            Name "Default"
            HLSLPROGRAM
            #pragma fragment frag
            
            half4 frag(v2f i) : SV_Target
            {
                // 扎孔半透
                float2 uv_Screen = i.posScreen.xy / i.posScreen.w;
                DitherAlpha(_DitherOpacity, uv_Screen);

                // 绘制
                half2 nDirTS_XY01 = 0;
                return DrawIce(i, nDirTS_XY01);
            }
            ENDHLSL
        }
        Pass
        {
            Name "SRP"
            HLSLPROGRAM
            #include "PackHelper.hlsl"
            #pragma fragment frag
            
            uint4 frag(v2f i) : SV_Target
            {
                // 绘制
                half2 nDirTS_XY01 = 0;
                half4 finalRGBA_Half = DrawIce(i, nDirTS_XY01);

                // PACK
                uint4 finalRGBA_UShort;
                finalRGBA_UShort.rgb = PackHDRToUShort(finalRGBA_Half.rgb);
                finalRGBA_UShort.a = PackAlphaAndVec2(finalRGBA_Half.a, nDirTS_XY01);
                return finalRGBA_UShort;
            }
            ENDHLSL
        }
    }
}