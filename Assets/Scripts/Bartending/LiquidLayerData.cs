using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LiquidLayerData", menuName = "Bartending/LiquidData")]
public class LiquidLayerData : ScriptableObject
{
    [System.Serializable]
    public class LiquidLayer
    {
        public string layerName;     // 液体层名称
        public Color color;          // 液体颜色
        public Texture2D maskTex;    // mask纹理
        [Range(0f, 1f)]
        public float lerpRange = 0.15f; // 渐变程度
        [Range(0f, 10f)]
        public float bubbleInt = 0f; // 气泡强度
        [Range(0f, 1f)]
        public float lerpWarpInt = 0f; // 混合处扰动强度
        public float lerpWarpSize = 0f; // 混合处扰动噪声图尺寸
    }
    public LiquidLayer data;
} 