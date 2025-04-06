#ifndef UNIVERSAL_GLASS_INCLUDED
#define UNIVERSAL_GLASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

// MatcapUV
float2 GetMatcapUV(float3 normalWS)
{
    float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
    return normalVS.xy * 0.5 + 0.5;
}

half4 GlassFragment(InputData inputData, SurfaceData surfaceData)
{
    // PBR
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);
    
    #if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, surfaceData, debugColor))
    {
        return debugColor;
    }
    #endif

    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    
    half4 shadowMask = CalculateShadowMask(inputData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    

    // Glass
    // 菲涅尔
    half fresnel = pow(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), _Thinkness + 0.0001);
    
    // 折射效果计算
    float2 screenUV = inputData.normalizedScreenSpaceUV;
    float3 normalVS = mul((float3x3)UNITY_MATRIX_V, inputData.normalWS);
    float2 distortion = normalVS.xy;

    // 计算最终折射UV
    float2 refractionUV = screenUV + distortion * _RefractIntensity;
    half3 sceneColor = SAMPLE_TEXTURE2D(_LiquidFinalTexture, sampler_LiquidFinalTexture, refractionUV).rgb;
    half3 refractionColor = surfaceData.albedo * sceneColor;
    surfaceData.albedo = refractionColor;

    float2 matcapUV = GetMatcapUV(inputData.normalWS);
    half4 matcapColor = SAMPLE_TEXTURE2D(_MatCapTex, sampler_MatCapTex, matcapUV);
    
    // Blend
    // 使用Matcap代替主光光照
    lightingData.mainLightColor = surfaceData.albedo + matcapColor.rgb;
    lightingData.mainLightColor *= mainLight.shadowAttenuation;
    
    // 计算最终颜色
    half4 finalColor;
    #if REAL_IS_HALF
    finalColor = min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
    #else
    finalColor = CalculateFinalColor(lightingData, surfaceData.alpha);
    #endif
    
    finalColor.rgb = lerp(surfaceData.albedo, finalColor.rgb, fresnel);
    
    finalColor.a = surfaceData.alpha;
    
    return finalColor;
}

#endif // UNIVERSAL_GLASS_INCLUDED
