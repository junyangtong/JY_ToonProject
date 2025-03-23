using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace JY.Toon.Bartending
{
    public class BartendingManager : MonoBehaviour
    {
        public static BartendingManager Instance { get; private set; }

        [Header("LiquidLayerData")]
        [SerializeField] private LiquidLayerData liquidLayerData;
        
        [Header("Liquid Setting")]
        [SerializeField] private GameObject liquidObject;
        [SerializeField] private float maxLiquidHeight = 1.0f;
        [SerializeField] private Color defaultLiquidColor = new Color(0.5f, 0.3f, 0.1f, 0.8f);
        [SerializeField] private float waveAmplitude = 0.3f;
        [SerializeField] private float waveFrequency = 3.0f;
        [SerializeField] private float waveSpeed = 1.0f;

        [Header("Animation")]
        [SerializeField] private float liquidPourDuration = 1.0f;
        [SerializeField] private AnimationCurve heightCurve;
        [SerializeField] private AnimationCurve warpCurve;
        [SerializeField] private AnimationCurve lerpCurve;
        
        [Header("UI")]
        [SerializeField] private Button pourButton; 
        [SerializeField] private Button resetButton; 
        
        private int maxLayers = 0;
        private Material liquidMaterial;
        private Color[] layerColors;
        private float[] layerLerps;
        private float liquidHeight01 = 0f;
        private int currentLayer = 0;
        private bool shaderNeedUpdate = false;

        public float LiquidHeight01 => liquidHeight01;
        public float MaxLiquidHeight => maxLiquidHeight;
        public float WaveAmplitude => waveAmplitude;
        public float WaveFrequency => waveFrequency;
        public float WaveSpeed => waveSpeed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
        }

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
            InitializeShaderProperties();
            
            // 设置UI事件
            SetUI();
        }
        
        private void Update()
        {
            if (shaderNeedUpdate)
            {
                UpdateShaderProperties();
                shaderNeedUpdate = false;
            }
        }

        private void InitializeShaderProperties()
        {
            if (liquidMaterial != null)
            {
                liquidMaterial.SetInt("_MaxLayers", maxLayers);
                liquidMaterial.SetFloat("_LiquidHeight01", liquidHeight01);
                liquidMaterial.SetFloat("_MaxLiquidHeight", maxLiquidHeight);
                liquidMaterial.SetColorArray("_LiquidLayerColor", layerColors);
                liquidMaterial.SetFloatArray("_LiquidLayerLerpRange", layerLerps);
                liquidMaterial.SetFloat("_WaveAmplitude", 0f);
                liquidMaterial.SetFloat("_WaveFrequency", 1f);
                liquidMaterial.SetFloat("_WaveSpeed", 1f);
            }
            else
            {
                Debug.LogError("没有设置杯子材质!");
            }
        }
        /// <summary>
        /// 更新Shader参数
        /// </summary>
        private void UpdateShaderProperties()
        {
            if (liquidMaterial != null)
            {
                liquidMaterial.SetFloat("_LiquidHeight01", liquidHeight01);
                liquidMaterial.SetColorArray("_LiquidLayerColor", layerColors);
                liquidMaterial.SetFloatArray("_LiquidLayerLerpRange", layerLerps);
                liquidMaterial.SetFloat("_WaveAmplitude", waveAmplitude);
                liquidMaterial.SetFloat("_WaveFrequency", waveFrequency);
                liquidMaterial.SetFloat("_WaveSpeed", waveSpeed);
            }
            else
            {
                Debug.LogError("没有设置杯子材质!");
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
            if (currentLayer < maxLayers - 1) // 防止CurrentColor和NextColor做插值时NextColor为默认颜色，每次填充上面两层
            {
                Color layerColor = liquidLayerData.GetLayerColor(currentLayer);
                layerColors[currentLayer] = layerColors[currentLayer+1] = layerColor;
            }
            else
            {
                Color layerColor = liquidLayerData.GetLayerColor(currentLayer);
                layerColors[currentLayer] = layerColor;
            }
            layerLerps[currentLayer] = liquidLayerData.GetLayerLerpRange(currentLayer);
            
            // 计算当前层高度和下一层高度
            float currentHeight = (float)currentLayer / maxLayers;
            float nextHeight = (float)(currentLayer + 1) / maxLayers;
            
            // 执行异步动画
            // 高度动画
            UniTask heightTask = BartendingAnimation.AnimateTwoFloatAsync(
                currentHeight, 
                nextHeight, 
                liquidPourDuration, 
                (float value) =>
                {
                    liquidHeight01 = value;
                    shaderNeedUpdate = true;
                },
                heightCurve
            );
            
            // 波浪动画
            UniTask warpTask = BartendingAnimation.AnimateFloatAsync(
                liquidPourDuration,
                (float value) =>
                {
                    waveAmplitude = value;
                    shaderNeedUpdate = true;
                },
                warpCurve
            );

            // 渐变动画
            UniTask lerpTask = BartendingAnimation.AnimateTwoFloatAsync(
                0, 
                layerLerps[currentLayer], 
                liquidPourDuration, 
                (float value) =>
                {
                    layerLerps[currentLayer] = value;
                    Debug.Log(layerLerps[currentLayer]);
                    shaderNeedUpdate = true;
                },
                lerpCurve
            );
            
            // 等待所有异步动画完成
            await UniTask.WhenAll(heightTask, warpTask, lerpTask);

            // 增加当前层数
            currentLayer++;
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
                await BartendingAnimation.AnimateTwoFloatAsync(
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