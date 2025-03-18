using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LiquidLayerData", menuName = "调酒/液体层数据")]
public class LiquidLayerData : ScriptableObject
{
    [System.Serializable]
    public class LiquidLayer
    {
        public string layerName;     // 液体层名称
        public Color color;          // 液体颜色
        public bool isMaked;        // 是否是浑浊液体(被遮罩)
        [Range(0f, 1f)]
        public float lerpRange = 0.15f; // 渐变程度
    }

    public List<LiquidLayer> liquidLayers = new List<LiquidLayer>(); // 液体层列表

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
    public bool GetLayerIsMaked(int index)
    {
        if (index >= 0 && index < liquidLayers.Count)
        {
            return liquidLayers[index].isMaked;
        }
        return false;
    }

    public float GetLayerLerpRange(int index)
    {
        if (index >= 0 && index < liquidLayers.Count)
        {
            return 0f;
        }
        return liquidLayers[index].lerpRange;
    }

} 