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
        [Range(0f, 1f)]
        public float bubbleInt = 0f; // 气泡强度
    }

    // 液体层列表
    [SerializeField] 
    private List<LiquidLayer> liquidLayers = new List<LiquidLayer>();

    public int GetLayerCount()
    {
        return liquidLayers.Count;
    }
    
    public Color GetLayerColor(int index)
    {
        if (index >= 0 && index < liquidLayers.Count)
        {
            return liquidLayers[index].color;
        }
        return Color.clear;
    }
    public Texture2D GetLayerMaskTex(int index)
    {
        if (index >= 0 && index < liquidLayers.Count)
        {
            return liquidLayers[index].maskTex;
        }
        return null;
    }

    public float GetLayerLerpRange(int index)
    {
        if (index >= 0 && index < liquidLayers.Count)
        {
            return liquidLayers[index].lerpRange;
        }
        return 0.15f;
    }

    public float GetLayerBubbleInt(int index)
    {
        if (index >= 0 && index < liquidLayers.Count)
        {
            return liquidLayers[index].bubbleInt;
        }
        return 0f;
    }
} 