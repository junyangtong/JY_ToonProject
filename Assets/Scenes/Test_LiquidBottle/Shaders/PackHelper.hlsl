#pragma once

// 颜色
static const half maxRGB_Inner = 255;
uint3 PackHDRToUShort(half3 RGB, half maxRGB = maxRGB_Inner)
{
    return clamp(RGB, 0, maxRGB) / maxRGB * 65535;
}
half3 UnpackHDRByUShort(uint3 RGB, half maxRGB = maxRGB_Inner)
{
    return RGB / 65535.0 * maxRGB;
}

// 透明度 & 法线扭曲
static const uint alphaBit_Inner = 4;
static const uint vecBit_Inner = 6;
uint PackAlphaAndVec2(half alpha, half2 vec2, uint alphaBit = alphaBit_Inner, uint vecBit = vecBit_Inner)
{
    half alphaBitMax = 1 << alphaBit;
    half vecBitMax = 1 << vecBit;
    alpha = saturate(alpha);
    vec2 = saturate(vec2);
    return
        uint(alpha * alphaBitMax) |
        (uint(vec2.x * vecBitMax) << alphaBit) |
        (uint(vec2.y * vecBitMax) << (alphaBit + vecBit));
}

void UnpackAlphaAndVec2(
    uint source, out half alpha, out half2 vec2,
    uint alphaBit = alphaBit_Inner, uint vecBit = vecBit_Inner)
{
    uint alphaBitMax = 1 << alphaBit;
    uint vecBitMax = 1 << vecBit;
    
    alpha = half(source & (alphaBitMax - 1)) / alphaBitMax;
    vec2 = half2(
        half((source >> alphaBit) & (vecBitMax - 1)),
        half(source >> (alphaBit + vecBit))
    ) / vecBitMax;
}