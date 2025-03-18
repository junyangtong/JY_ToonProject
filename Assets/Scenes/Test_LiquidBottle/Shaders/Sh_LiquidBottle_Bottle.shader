Shader "LiquidBottle/Bottle"
{
    Properties
    {
        _Opacity ("不透明度", Range(0, 1)) = 0.5
        [Space()] [Header(__________________Normal__________________)]
        [NoScaleOffset] [Normal] _NormalMap ("法线图", 2D) = "bump" {}
        _NormalUVScale ("uv缩放", Float) = 1
        _NormalInt ("法线强度", Range(0, 2)) = 0.05
        
        [Space()] [Header(__________________Ambient__________________)]
        [NoScaleOffset] _MatCapTex ("MatCap", 2D) = "black" {}
        [HDR] _MatCapTint ("MatCap色调", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+1"
        }
        Pass
        {
            Blend SrcAlpha One//MinusSrcAlpha
            ZWrite off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _NormalMap, _MatCapTex;
            CBUFFER_START(UnityPerMaterial)
            half _Opacity;
            // 法线
            half _NormalUVScale;
            half _NormalInt;
            // MatCap
            half3 _MatCapTint;
            CBUFFER_END

            struct a2v
            {
                half3 posOS	    : POSITION;
                half3 nDirOS    : NORMAL;
                half4 tDirOS    : TANGENT;
                float2 uv       : TEXCOORD0;
            };

            struct v2f
            {
                float4 posCS	    : SV_POSITION;
                float3 posWS        : TEXCOORD0;
                float2 uv           : TEXCOORD1;
                float4 pos_Screen   : TEXCOORD2;
                half3x3 TBN         : TEXCOORD3;
            };

            v2f vert(a2v i)
            {
                v2f o;
                o.posWS = TransformObjectToWorld(i.posOS);
                o.posCS = TransformWorldToHClip(o.posWS);
                o.uv = _NormalUVScale * i.uv;
                o.pos_Screen = ComputeScreenPos(o.posCS);
                half3 nDirWS = TransformObjectToWorldNormal(i.nDirOS);
                half3 tDirWS = TransformObjectToWorldDir(i.tDirOS.xyz);
                half3 bDirWS = normalize(cross(nDirWS, tDirWS.xyz) * i.tDirOS.w);
                o.TBN = half3x3(tDirWS, bDirWS, nDirWS);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // 法线
                half3 nDirTS = UnpackNormal(tex2D(_NormalMap, i.uv));
                nDirTS.xy *= _NormalInt;
                half3x3 TBN = half3x3(normalize(i.TBN[0]), normalize(i.TBN[1]), normalize(i.TBN[2]));
                half3 nDirWS = normalize(mul(nDirTS, TBN));

                // MatCap
                half3 nDirVS = TransformWorldToViewDir(nDirWS);
                float2 uv_MatCap = 0.5 * nDirVS.xy + 0.5;
                half3 matCapCol = _MatCapTint * tex2D(_MatCapTex, uv_MatCap).rgb;

                // 混合
                half3 finalCol = matCapCol;
                return half4(finalCol, _Opacity);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}