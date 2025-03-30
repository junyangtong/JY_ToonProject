using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JY.Toon.Bartending
{
    public class BartendingManager : MonoBehaviour
    {
        public static BartendingManager Instance { get; private set; }

        [Header("LiquidLayerData")]
        [SerializeField] private LiquidLayerData liquidLayerData;
        
        [Header("Liquid Setting")]
        [SerializeField] private Renderer liquidRenderer;
        [SerializeField] private float maxLiquidHeight = 1.0f;
        [SerializeField] private Color defaultLiquidColor = new Color(0.5f, 0.3f, 0.1f, 0.8f);
        [SerializeField] private float waveAmplitude = 0.3f;
        [SerializeField] private float waveFrequency = 3.0f;
        [SerializeField] private float waveSpeed = 1.0f;
        [SerializeField] private Color[] layerColors;

        [Header("Animation")]
        [SerializeField] private float liquidPourDuration = 1.0f;
        [SerializeField] private float liquidBlendDuration = 1.0f;
        [SerializeField] private AnimationCurve heightCurve;
        [SerializeField] private AnimationCurve warpCurve;
        [SerializeField] private AnimationCurve lerpCurve;
        [SerializeField] private AnimationCurve blendCurve;
        [Header("Ice")]
        [SerializeField] private IceCount iceCount = IceCount.less;
        [SerializeField] private GameObject iceObj;
        [SerializeField] private float iceInitialHeight = 2.0f;
        
        [Header("UI")]
        [SerializeField] private Button pourButton; 
        [SerializeField] private Button resetButton; 
        [SerializeField] private Button addIceButton; 
        [SerializeField] private Button blendButton; 
        
        private int maxLayers = 0;
        private Material liquidMaterial;
        private float[] layerLerps;
        private float liquidHeight01 = 0f;
        private int currentLayer = 0;
        private bool shaderNeedUpdate = false;
        private RenderTexture layerMaskTexArray;
        private const int maskSize = 256;
        private LiquidPass liquidPass;
        private List<Rigidbody> iceRigid;
        private List<GameObject> iceObjPool;
        private int iceCountMax = 8;
        private float[] bubbleInt;

        public float LiquidHeight01 => liquidHeight01;
        public float MaxLiquidHeight => maxLiquidHeight;
        public float WaveAmplitude => waveAmplitude;
        public float WaveFrequency => waveFrequency;
        public float WaveSpeed => waveSpeed;
        public Renderer LiquidRenderer => liquidRenderer;

        enum IceCount
        {
            None = 0,
            less = 3,
            medium = 5,
            more = 8
        }
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
            bubbleInt = new float[maxLayers];

            ResetMaskTexArray(maxLayers);

            liquidMaterial = liquidRenderer.sharedMaterial;
            
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

#region Ice
        public void AddIce()
        {
            // 每次添加都初始化
            iceRigid = new List<Rigidbody>();// 重置list
            // 重置冰块对象池
            foreach (GameObject ice in iceObjPool)
            {
                ice.SetActive(false);
            }

            if ((int)iceCount == 0)
            {
                return;
            }

            if (iceObj == null)
            {
                Debug.LogError("<BartendingManager> iceObj未指定");
                return;
            }
            
            // 冰块生成位置
            Vector3 initalPosition = liquidRenderer.transform.position + Vector3.up * iceInitialHeight;
            for (int i = 0; i < (int)iceCount; i++)
            {
                iceObjPool[i].SetActive(true);
                iceObjPool[i].transform.position = initalPosition;
                iceObjPool[i].GetComponent<Renderer>().enabled = false;
                iceRigid.Add(iceObjPool[i].GetComponent<Rigidbody>());
            }
        }
#endregion

#region RenderFeature
        // 自定义渲染顺序
        private void OnEnable()
        {
            //初始化冰块对象池 对象池在每帧执行的情况下比destroy快了一倍但是应该没人手速这么快吧..
            if (iceObjPool == null)
            {
                iceObjPool = new List<GameObject>(iceCountMax);
                for (int i = 0; i < iceCountMax; i++)
                {
                    GameObject ice = Instantiate(iceObj, Vector3.zero, Quaternion.identity);
                    ice.SetActive(false);
                    iceObjPool.Add(ice);
                }
            }
            // 初始化liquidPass
            if (liquidRenderer != null && iceObj != null)
            {
                liquidPass = new LiquidPass(liquidRenderer, iceObj);
                liquidPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            }
            else
            {
                Debug.LogError("<BartendingManager> liquidRenderer或iceObj未指定");
            }
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            liquidPass.Dispose();
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (liquidPass != null && cam.cameraType == CameraType.Game)
            {
                liquidPass.UpdateIceMatrix(iceRigid);
                cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(liquidPass);
            }
        }
#endregion
#region MaskTexArray
        /// <summary>
        /// 创建Texture2DArray
        /// </summary>
        private void CreateMaskTexArray()
        {
            layerMaskTexArray = new RenderTexture(
                maskSize, maskSize, 0, 
                RenderTextureFormat.R8, RenderTextureReadWrite.Linear
            );
            layerMaskTexArray.dimension = TextureDimension.Tex2DArray;
            layerMaskTexArray.wrapMode = TextureWrapMode.Repeat;
            layerMaskTexArray.useMipMap = false;
            layerMaskTexArray.enableRandomWrite = true;
        }
        
        /// <summary>
        /// 初始化Mask
        /// </summary>
        public void ResetMaskTexArray(int layerNum)
        {
            if (layerNum > 0)
            {
                if (layerMaskTexArray && layerNum != layerMaskTexArray.volumeDepth)
                {
                    layerMaskTexArray.Release();
                }
                CreateMaskTexArray();
                layerMaskTexArray.volumeDepth = layerNum;
                // 初始化Mask
                for (int i = 0; i < layerMaskTexArray.volumeDepth; i++)
                {
                    Graphics.Blit(Texture2D.blackTexture, layerMaskTexArray, 0, i);
                }
            }
        }
#endregion
        /// <summary>
        /// 初始化时更新shader参数
        /// </summary>
        private void InitializeShaderProperties()
        {
            if (liquidMaterial != null)
            {
                liquidMaterial.SetInt("_MaxLayers", maxLayers);
                liquidMaterial.SetFloat("_LiquidHeight01", liquidHeight01);
                liquidMaterial.SetFloat("_MaxLiquidHeight", maxLiquidHeight);
                liquidMaterial.SetColorArray("_LiquidLayerColor", layerColors);
                liquidMaterial.SetFloatArray("_LiquidLayerLerpRange", layerLerps);
                liquidMaterial.SetFloatArray("_BubbleInt", bubbleInt);
                liquidMaterial.SetFloat("_WaveAmplitude", 0f);
                liquidMaterial.SetFloat("_WaveFrequency", 1f);
                liquidMaterial.SetFloat("_WaveSpeed", 1f);
                liquidMaterial.SetTexture("_LiquidLayerMaskTex", layerMaskTexArray);
            }
        }
        /// <summary>
        /// 每次更新Shader参数
        /// </summary>
        private void UpdateShaderProperties()
        {
            if (liquidMaterial != null)
            {
                liquidMaterial.SetFloat("_LiquidHeight01", liquidHeight01);
                liquidMaterial.SetColorArray("_LiquidLayerColor", layerColors);
                liquidMaterial.SetFloatArray("_LiquidLayerLerpRange", layerLerps);
                liquidMaterial.SetFloatArray("_BubbleInt", bubbleInt);
                liquidMaterial.SetTexture("_LiquidLayerMaskTex", layerMaskTexArray);
                liquidMaterial.SetFloat("_WaveAmplitude", waveAmplitude);
                liquidMaterial.SetFloat("_WaveFrequency", waveFrequency);
                liquidMaterial.SetFloat("_WaveSpeed", waveSpeed);
            }
        }
#region Action
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
                Debug.Log("正在播放动画，无法添加！");
                return;
            }

            //更新数组
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

            // 更新mask2DArr
            Texture2D newMask = liquidLayerData.GetLayerMaskTex(currentLayer);
            Graphics.Blit(newMask, layerMaskTexArray, 0, currentLayer);
            if (currentLayer < maxLayers - 1) // 和颜色一样 每次填充上面两层
            {
                Graphics.Blit(newMask, layerMaskTexArray, 0, currentLayer + 1);
            }
            
            layerLerps[currentLayer] = liquidLayerData.GetLayerLerpRange(currentLayer);
            bubbleInt[currentLayer] = liquidLayerData.GetLayerBubbleInt(currentLayer);

            // 计算当前层高度和下一层高度
            float currentHeight = (float)currentLayer / maxLayers;
            float nextHeight = (float)(currentLayer + 1) / maxLayers;
            
            // 执行异步动画
            // 高度动画
            UniTask heightTask = BartendingAnimation.AnimateTwoValueAsync(
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
            UniTask lerpTask = BartendingAnimation.AnimateTwoValueAsync(
                0, 
                layerLerps[currentLayer], 
                liquidPourDuration, 
                (float value) =>
                {
                    layerLerps[currentLayer] = value;
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
        /// 搅拌
        /// </summary>
        public async void Blend()
        {
            if (currentLayer <= 1)
            {
                Debug.Log("少于两层液体无法搅拌！");
                return;
            }
             if (BartendingAnimation.IsAnimating)
            {
                Debug.Log("正在播放动画，无法搅拌！");
                return;
            }
            //混合颜色
            /* 搅拌时计算当前已有的所有颜色的混合颜色值作为目标颜色
            当前已有的颜色值全部向目标颜色过渡 */
            int blendCount = currentLayer - 1;// 需要混合的层数
            Color averageColor = Color.clear;
            for (int i = 0; i <= blendCount; i++)
            {
                averageColor += layerColors[i];
            }
            averageColor /= blendCount + 1;
            
            // 颜色渐变动画
            int count = blendCount == 4 ? blendCount : blendCount+1; // 要改变上面两层颜色
            UniTask blendColTask = BartendingAnimation.AnimateTwoValueAsync(
                layerColors, 
                averageColor, 
                liquidBlendDuration,
                count,
                (Color[] value) =>
                {
                    layerColors = value;
                    shaderNeedUpdate = true;
                },
                blendCurve
            );

            //混合Mask

            
            // 等待所有异步动画完成
            await UniTask.WhenAll(blendColTask);
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
                await BartendingAnimation.AnimateTwoValueAsync(
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
#endregion
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
            if (addIceButton != null)
            {
                addIceButton.onClick.AddListener(AddIce);
            }
            if (blendButton != null)
            {
                blendButton.onClick.AddListener(Blend);
            }
        }

        private void OnGUI()
        {
            
        }
    }
}