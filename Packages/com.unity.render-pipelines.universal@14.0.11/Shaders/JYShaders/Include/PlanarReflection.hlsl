void PlanarReflection(inout InputData inputData, inout SurfaceData surfaceData)
{
    float2 screenUV = inputData.normalizedScreenSpaceUV;
    float4 reflectionTex = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, screenUV);
    surfaceData.albedo = lerp(surfaceData.albedo, reflectionTex.rgb, _PlanarReflectionIntensity);
}
