using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace JY.Toon.Bartending
{
    public class BartendingManager : MonoBehaviour
    {
        [Header("LiquidLayerData")]
        [SerializeField] private LiquidLayerData liquidLayerData;
        
        [Header("Liquid Setting")]
        [SerializeField] private GameObject liquidObject;
        [SerializeField] private Color defaultLiquidColor = new Color(0.5f, 0.3f, 0.1f, 0.8f);

        [Header("Animation")]
        [SerializeField] private float liquidPourDuration = 1.0f;
        
        [Header("UI")]
        [SerializeField] private Button pourButton; 
        [SerializeField] private Button resetButton; 
        
        private int maxLayers = 0;
        private Material liquidMaterial;
        private Color[] layerColors;
        private float[] layerLerps;
        private float[] layerIsMaked;
        private float liquidHeight01;
        private int currentLayer = 0;

        void Start()
        {
            if (liquidLayerData != null)
            {
                maxLayers = liquidLayerData.GetLayerCount();
            }
            else
            {
                Debug.LogError("<BartendingManager> liquidLayerData未指定");
            }
            layerColors = new Color[maxLayers];
            layerLerps = new float[maxLayers];
            layerIsMaked = new float[maxLayers];

            if (liquidObject != null)
            {
                Renderer renderer = liquidObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // 获取液体材质
                    liquidMaterial = renderer.material;
                }
            }
            else
            {
                Debug.LogError("<BartendingManager> liquidObject未指定");
            }
            
            // 初始化shader参数
            UpdateShaderProperties();
            
            // 设置UI事件
            SetUI();
        }

        /// <summary>
        /// 更新Shader参数
        /// </summary>
        private void UpdateShaderProperties()
        {
            if (liquidMaterial != null)
            {
                liquidMaterial.SetInt("_MaxLayers", maxLayers);
                liquidMaterial.SetFloat("_LiquidHeight01", liquidHeight01);
                liquidMaterial.SetColorArray("_LiquidLayerColor", layerColors);
                liquidMaterial.SetFloatArray("_LiquidLayerLerpRange", layerLerps);
                liquidMaterial.SetFloatArray("_LiquidLayerIsMaked", layerIsMaked);
                
                Debug.Log($"设置着色器参数: _MaxLayers={maxLayers}");
            }
            else
            {
                Debug.LogError("没有设置玻璃材质!");
            }
        }
        
        /// <summary>
        /// 倒入液体
        /// </summary>
        public async void PourLiquid()
        {
            if (currentLayer >= maxLayers)
            {
                Debug.Log("酒杯已经满了！");
                return;
            }
            if (BartendingAnimation.IsAnimating)
            {
                Debug.Log("正在倒入液体无法添加！");
                return;
            }
            
            //更新shader数组
            layerColors[currentLayer] = liquidLayerData.GetLayerColor(currentLayer);
            layerLerps[currentLayer] = liquidLayerData.GetLayerLerpRange(currentLayer);
            layerIsMaked[currentLayer] = liquidLayerData.GetLayerIsMaked(currentLayer) ? 1.0f : 0.0f;
            
            // 计算当前层高度和下一层高度
            float currentHeight = (float)currentLayer / maxLayers;
            float nextHeight = (float)(currentLayer + 1) / maxLayers;
            
            // 增加当前层数
            currentLayer++;
            
            // 执行液体高度动画
            await BartendingAnimation.AnimateFloatAsync(
                currentHeight, 
                nextHeight, 
                liquidPourDuration, 
                (float value) =>
                {
                    liquidHeight01 = value;
                    UpdateShaderProperties();
                }
            );
            
            Debug.Log($"倒入第 {currentLayer} 层液体");
        }

        /// <summary>
        /// 重置液体
        /// </summary>
        public async void ResetLiquid()
        {
            if (BartendingAnimation.IsAnimating)
            {
                Debug.Log("正在倒入液体，无法重置！");
                return;
            }
            
            // 清空动画
            if (liquidHeight01 > 0)
            {
                await BartendingAnimation.AnimateFloatAsync(
                    liquidHeight01, 
                    0f, 
                    liquidPourDuration, 
                    (float value) =>
                    {
                        liquidHeight01 = value;
                        UpdateShaderProperties();
                    }
                );
            }
            
            currentLayer = 0;
            UpdateShaderProperties();
            Debug.Log("已重置酒杯");
        }

        /// <summary>
        /// 设置UI
        /// </summary>
        private void SetUI()
        {
            if (pourButton != null)
            {
                pourButton.onClick.AddListener(PourLiquid);
            }
            if (resetButton != null)
            {
                resetButton.onClick.AddListener(ResetLiquid);
            }
        }
    }
}