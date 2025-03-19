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
        public bool isMaked;        // 是否是浑浊液体(被遮罩)
        [Range(0f, 1f)]
        public float lerpRange = 0.15f; // 渐变程度
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
            return liquidLayers[index].lerpRange;
        }
        return 0.15f;
    }

} 