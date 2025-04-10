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
    }
    public LiquidLayer data;
} 