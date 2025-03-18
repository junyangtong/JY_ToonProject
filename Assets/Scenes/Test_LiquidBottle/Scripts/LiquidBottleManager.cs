using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LiquidBottleManager : MonoBehaviour
{
    [Space()] [Header("__________________Bottle__________________")]
    public GameObject gObj_Bottle;

    [Space()] [Header("__________________Liquid__________________")]
    public Renderer rdr_Liquid;
    public int maxLiquidNum;

    [Space()] [Header("__________________Ice__________________")]
    public GameObject pre_Ice;
    public float iceBirthH = 4;
    public float iceCenterBias = 0;
    
    [Space()] [Header("__________________SRP__________________")]
    public bool bIceUseRenderFeature = false;
    public Material mat_RFMerge;
    
    [Space()] [Header("__________________Height__________________")]
    public float maxLiquidHeightOS = 1;
    
    [Space()] [Header("__________________Physic__________________")]
    public float dragScale = 2;
    
    [Space()] [Header("__________________Anime__________________")]
    public LiquidAnimation liquidAnimation = new LiquidAnimation();

    // shader参数ID
    public const int maxLiquidNumInShader = 5;
    
    private static readonly int id_liquidRGBA = Shader.PropertyToID("liquidRGBA");
    private static readonly int id_liquidBubbleInt = Shader.PropertyToID("liquidBubbleInt");
    private static readonly int id_liquidLerpRange = Shader.PropertyToID("liquidLerpRange");
    private static readonly int id_MaskTex2DArr = Shader.PropertyToID("_MaskTex2DArr");
    
    private static readonly int id_LiquidHeightWS_GLB = Shader.PropertyToID("_LiquidHeightWS_GLB");
    private static readonly int id_MaxLiquidHeightOS = Shader.PropertyToID("_MaxLiquidHeightOS");
    private static readonly int id_LiquidHeight01 = Shader.PropertyToID("_LiquidHeight01");
    private static readonly int id_HeightWarpInt = Shader.PropertyToID("_HeightWarpInt");

    private List<Liquid> liquids;
    private Material mat_Liquid;
    private float _LiquidHeightWS_GLB;  // 全局液面世界高度, 给冰块用
    
    private void Start()
    {
        mat_Liquid = rdr_Liquid.material;
        ResetLayerNum(maxLiquidNum);
        ClearLiquid();
        liquidAnimation.SetManager(this);
        SetRenderMode(bIceUseRenderFeature);
    }
    
    private void Update()
    {
        RefreshHeightProp();
        liquidAnimation.UpdateAnim();
    }

    private void FixedUpdate()
    {
        // 冰块浮力
        if (rigid_Ice != null)
        {
            foreach (Rigidbody rigid_Ice_i in rigid_Ice)
            {
                IceFakeBuoyancy(rigid_Ice_i);
            }
        }
    }

#region RenderFeature
    private RPass_LiquidBottle renderPass;
    
    // 切换渲染方式
    public void SetRenderMode(bool bUseRenderFeatureIN)
    {
        bIceUseRenderFeature = bUseRenderFeatureIN;
        
        // 液体
        rdr_Liquid.enabled = !bIceUseRenderFeature;
        rdr_Liquid.material.SetVector(RPass_LiquidBottle.id_RelativeOriginAndFlag, Vector4.zero);   // 使用shader中的包围盒变量计算水平液面
        // renderfeature时直接输出原RGBA值的全屏RT, 后手动混合, 所以需要 one zero
        rdr_Liquid.sharedMaterial.SetInt("_SrcBlend", (int)(bIceUseRenderFeature ? BlendMode.One : BlendMode.SrcAlpha));
        rdr_Liquid.sharedMaterial.SetInt("_DstBlend", (int)(bIceUseRenderFeature ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
        
        // 冰块
        if (rigid_Ice != null)
        {
            foreach (Rigidbody rigid_i in rigid_Ice)
            {
                Renderer rdr = rigid_i.GetComponent<Renderer>();
                rdr.enabled = !bIceUseRenderFeature;
            }
        }
    }
    
    private void OnEnable()
    {
        renderPass = new RPass_LiquidBottle(
            rdr_Liquid,
            pre_Ice.GetComponent<MeshFilter>().sharedMesh, 
            pre_Ice.GetComponent<Renderer>().sharedMaterial,
            mat_RFMerge
        );
        renderPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        renderPass.Dispose();
    }
    
    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (bIceUseRenderFeature && renderPass != null && 
            cam.cameraType == CameraType.Game)
        {
            renderPass.RefreshIceMatrix(rigid_Ice);  // 冰块矩阵每帧刷新
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(renderPass);
        }
    }
#endregion
#region GetSet参数
    /// <summary>
    /// 设置材质的缓存数组
    /// Liquids数组 -> Buffer -> 材质三段同步
    /// 数组参数设置, 首次必须设为可能的最大, 否则后续给shader设置更长的数组会被截断
    /// </summary>
    private Material dstLiquidMat;
    private RenderTexture buffer_Mask2DArr;
    public const int maskSize = 1024;
    private Color[] buffer_LiquidRGBA = new Color[maxLiquidNumInShader];
    private float[] buffer_LiquidBubbleInt = new float[maxLiquidNumInShader];
    private float[] buffer_LiquidLerpRange = new float[maxLiquidNumInShader];
    
    // 按Liquids数组 -> Buffer -> 材质的顺序同步三份数据
    private void UnifyArrayProps()
    {
        // 已有层的buffer
        int liquidNum = liquids.Count;
        for (int i = 0; i < liquidNum; i++)
        {
            Liquid liquid_i = liquids[i];
            buffer_LiquidRGBA[i] = liquid_i.RGBA;
            buffer_LiquidBubbleInt[i] = liquid_i.bubbleIntensity;
            buffer_LiquidLerpRange[i] = liquid_i.lerpRange;
        }

        // 尚未填充层的buffer
        // 为保证材质不取到无效的数组元素, 需对其余层按最后一有效层进行补齐
        Color lastRGBA = Color.clear;
        float lastBubbleInt = 0;
        if (liquidNum > 0)
        {
            Liquid lastLiquid = liquids.Last();
            lastRGBA = lastLiquid.RGBA;
            lastBubbleInt = lastLiquid.bubbleIntensity;
        }
        for (int i = liquidNum; i < maxLiquidNumInShader; i++)
        {
            buffer_LiquidRGBA[i] = lastRGBA;
            buffer_LiquidBubbleInt[i] = lastBubbleInt;
            buffer_LiquidLerpRange[i] = 0;
        }
        
        // 材质设置
        mat_Liquid.SetColorArray(id_liquidRGBA, buffer_LiquidRGBA);
        mat_Liquid.SetFloatArray(id_liquidBubbleInt, buffer_LiquidBubbleInt);
        mat_Liquid.SetFloatArray(id_liquidLerpRange, buffer_LiquidLerpRange);
    }
    
    // 颜色
    public void SetRGBA(int ID, Color RGBA)
    {
        if (ID < liquids.Count)
        {
            liquids[ID].RGBA = RGBA;
        }
        buffer_LiquidRGBA[ID] = RGBA;
        mat_Liquid.SetColorArray(id_liquidRGBA, buffer_LiquidRGBA);
    }
    public Color GetRGBA(int ID) => liquids[ID].RGBA;
    
    // Mask
    public RenderTexture CopyMaskArrayBuffer()
    {
        RenderTexture outArr = new RenderTexture(buffer_Mask2DArr);
        Graphics.CopyTexture(buffer_Mask2DArr, outArr);
        return outArr;
    }
    public RenderTexture GetMaskArray() => buffer_Mask2DArr;
    
    // 泡沫强度
    public void SetBubbleInt(int ID, float bubbleInt)
    {
        if (ID < liquids.Count)
        {
            liquids[ID].bubbleIntensity = bubbleInt;
        }
        buffer_LiquidBubbleInt[ID] = bubbleInt;
        mat_Liquid.SetFloatArray(id_liquidBubbleInt, buffer_LiquidBubbleInt);
    }
    public float GetBubbleInt(int ID) => liquids[ID].bubbleIntensity;

    // 混合范围
    public void SetLerpRange(int ID, float lerpRange)
    {
        if (ID < liquids.Count)
        {
            liquids[ID].lerpRange = lerpRange;
        }
        buffer_LiquidLerpRange[ID] = lerpRange;
        mat_Liquid.SetFloatArray(id_liquidLerpRange, buffer_LiquidLerpRange);
    }
    public float GetLerpRange(int ID) => liquids[ID].lerpRange;
    
    // 归一化高度
    public void SetHeight01(float h01)
    {
        mat_Liquid.SetFloat(id_LiquidHeight01, h01);
    }
    public float GetHeight01()
    {
        return mat_Liquid.GetFloat(id_LiquidHeight01);
    }
    
    // 液面扰动强度
    public void SetHeightWarpInt(float warpInt)
    {
        mat_Liquid.SetFloat(id_HeightWarpInt, warpInt);
    }
    
    // 当前液体层数
    public int GetCurrentLiquidNum() => liquids.Count;
    
    // 标准创建mask2DArr
    private static RenderTexture CreateMask2DArr()
    {
        RenderTexture outRT = new RenderTexture(
            maskSize, maskSize, 0, 
            RenderTextureFormat.R8, RenderTextureReadWrite.Linear
        );
        outRT.dimension = TextureDimension.Tex2DArray;
        outRT.wrapMode = TextureWrapMode.Repeat;
        outRT.useMipMap = false;
        outRT.enableRandomWrite = true;
        return outRT;
    }
    
    // 设置最大液体层数 (重新创建缓存mask的tex2DArray)
    public void ResetLayerNum(int layerNum)
    {
        if (layerNum > 0)
        {
            if (buffer_Mask2DArr && layerNum != buffer_Mask2DArr.volumeDepth)
            {
                buffer_Mask2DArr.Release();
            }
            buffer_Mask2DArr = CreateMask2DArr();
            buffer_Mask2DArr.volumeDepth = layerNum;
            mat_Liquid.SetTexture(id_MaskTex2DArr, buffer_Mask2DArr);
        }
    }
    
    // 更新液面高度参数
    private void RefreshHeightProp()
    {
        // 全局液面世界高度
        float minLiquidHeightWS = rdr_Liquid.bounds.min.y - 0.01f;    // 取包围盒下界作为【杯底】
        _LiquidHeightWS_GLB = Mathf.Lerp(
            minLiquidHeightWS, minLiquidHeightWS + maxLiquidHeightOS,
            GetHeight01()
        );
        Shader.SetGlobalFloat(id_LiquidHeightWS_GLB, _LiquidHeightWS_GLB);
        
        // 液面最大高度
        mat_Liquid.SetFloat(id_MaxLiquidHeightOS, maxLiquidHeightOS);
    }
#endregion
#region 液体
    // 添加液体
    public void AddLiquid(Liquid liquidIN)
    {
        int newLiquidID = liquids.Count;
        if (newLiquidID < maxLiquidNum)
        {
            // 因为动画动态调参, 且存在同类液体复用的情况, 所以都需要new
            Liquid newLiquid = new Liquid(liquidIN);
            liquids.Add(newLiquid);
            
            // 同步数组数据
            UnifyArrayProps();
            
            // 更新mask2DArr
            Texture2D newMask = newLiquid.maskTex;
            Graphics.Blit(newMask, buffer_Mask2DArr, 0, newLiquidID);
            if (newLiquidID + 1 < maxLiquidNum) // 使得液面能采到当前层的mask而不为空
            {
                Graphics.Blit(newMask, buffer_Mask2DArr, 0, newLiquidID + 1);
            }
            
            // 动画播放预备
            liquidAnimation.StartUpdate_Add();
        }
    }
    
    // 清除(初始化)液体
    public void ClearLiquid()
    {
        liquids = new List<Liquid>(maxLiquidNumInShader);
        mat_Liquid.SetFloat(id_LiquidHeight01, 0);
        UnifyArrayProps();
        for (int i = 0; i < buffer_Mask2DArr.volumeDepth; i++)
        {
            Graphics.Blit(Texture2D.blackTexture, buffer_Mask2DArr, 0, i);
        }
    }
    
    // 混合液体
    public void MixLiquid()
    {
        if (liquids.Count > 1)
        {
            liquidAnimation.StartUpdate_Mix();
        }
    }
#endregion
#region 冰块
    // 重置冰块位置和数量
    private List<Rigidbody> rigid_Ice;
    private float iceSize;
    public void ResetIce(int num)
    {
        // 删除已有的冰块
        if (rigid_Ice != null)
        {
            foreach (Rigidbody rigid_Ice_i in rigid_Ice)
            {
                if (rigid_Ice_i.gameObject)
                {
                    Destroy(rigid_Ice_i.gameObject);
                }
            }
        }
        rigid_Ice = null;
        
        // 在杯子上方生成冰块
        if (num > 0)
        {
            rigid_Ice = new List<Rigidbody>(num);
            Vector3 posWS_Bottle = gObj_Bottle.transform.position;
            iceSize = pre_Ice.transform.lossyScale.x;   // 冰块mesh的边长=单位1
            for (int i = 0; i < num; i++)
            {
                Vector3 pos_i = posWS_Bottle + Vector3.up * (iceBirthH + 2 * i * iceSize);
                GameObject ice = Instantiate(pre_Ice, pos_i, Quaternion.Euler(45, 45 * i, 45));
                Rigidbody rigid_i = ice.GetComponent<Rigidbody>();
                rigid_Ice.Add(rigid_i);
            }
        }
        
        // 设置冰块渲染是否用renderFeature
        SetRenderMode(bIceUseRenderFeature);
    }
    
    // 浮力模拟
    private void IceFakeBuoyancy(Rigidbody rigidbody)
    {
        float currentH = rigidbody.transform.position.y + iceCenterBias;
        float halfSize = 0.5f * iceSize;
        float bottomWS = currentH - halfSize;
        if (bottomWS < _LiquidHeightWS_GLB)
        {
            float V = iceSize * iceSize * Mathf.Min(iceSize, _LiquidHeightWS_GLB - bottomWS);
            Vector3 drag = -rigidbody.velocity * dragScale * V;
            rigidbody.AddForce(-Physics.gravity * V + drag);
        }
    }
#endregion

    // UI
#if UNITY_EDITOR
    public Liquid currentLiquid_UI;
    public int iceNum = 3;
    private Material mat_DrawMask2DArr;
    private RenderTexture rt_DrawMask2DArr;
    public void DrawUI()
    {
        EditorUtility.SetDirty(this);
        
        GUILayout.Space(10);
        GUILayout.Label("__________________杯子__________________");
        gObj_Bottle = EditorGUILayout.ObjectField("杯子", gObj_Bottle, typeof(GameObject), true) as GameObject;
        
        GUILayout.Space(10);
        GUILayout.Label("__________________液体__________________");
        rdr_Liquid = EditorGUILayout.ObjectField("液体Renderer", rdr_Liquid, typeof(Renderer), true) as Renderer;
        GUI.enabled = liquidAnimation.GetUpdateMode() == LiquidAnimation.UpdateMode.None;
        EditorGUI.BeginChangeCheck();
        maxLiquidNum = EditorGUILayout.IntSlider("最大层数", maxLiquidNum, 1, maxLiquidNumInShader);
        if (Application.isPlaying && EditorGUI.EndChangeCheck())
        {
            ClearLiquid();
            ResetLayerNum(maxLiquidNum);
        }
        
        // 目前添加的液体颜色
        GUI.enabled = false;
        if (liquids != null)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < liquids.Count; i++)
            {
                EditorGUILayout.ColorField(liquids[i].RGBA);
            }
            GUILayout.EndHorizontal();
        }
        GUI.enabled = true;
        
        GUILayout.BeginHorizontal();
        currentLiquid_UI = EditorGUILayout.ObjectField("待添加液体", currentLiquid_UI, typeof(Liquid), false) as Liquid;
        GUI.enabled = Application.isPlaying;
        if (currentLiquid_UI)
        {
            GUI.enabled = false;
            EditorGUILayout.ColorField(currentLiquid_UI.RGBA);
            GUI.enabled = 
                Application.isPlaying && 
                liquids != null && liquids.Count < maxLiquidNum && 
                liquidAnimation.GetUpdateMode() == LiquidAnimation.UpdateMode.None;
            if (GUILayout.Button("【添加】"))
            {
                AddLiquid(currentLiquid_UI);
            }
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUI.enabled = Application.isPlaying && liquidAnimation.GetUpdateMode() == LiquidAnimation.UpdateMode.None;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("【清空】"))
        {
            ClearLiquid();
        }
        if (GUILayout.Button("【混合】"))
        {
            MixLiquid();
        }
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        
        GUILayout.Space(10);
        GUILayout.Label("__________________冰块__________________");
        pre_Ice = EditorGUILayout.ObjectField("冰块预制体", pre_Ice, typeof(GameObject), false) as GameObject;
        iceBirthH = EditorGUILayout.FloatField("冰块出生高度", iceBirthH);
        iceCenterBias = EditorGUILayout.Slider("冰块中心偏移", iceCenterBias, -1, 1);
        iceNum = EditorGUILayout.IntField("冰块数量", iceNum);
        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("【重置】"))
        {
            ResetIce(iceNum);
        }
        GUI.enabled = false;
        GUILayout.Label("冰块尺寸=" + iceSize);
        GUI.enabled = true;
        
        GUILayout.Space(10);
        GUILayout.Label("__________________SRP__________________");
        EditorGUI.BeginChangeCheck();
        bIceUseRenderFeature = GUILayout.Toggle(bIceUseRenderFeature, "使用SRP");
        mat_RFMerge = EditorGUILayout.ObjectField("SRP混合材质", mat_RFMerge, typeof(Material), false) as Material;
        if (EditorGUI.EndChangeCheck() && Application.isPlaying)
        {
            SetRenderMode(bIceUseRenderFeature);
        }
        
        GUILayout.Space(10);
        GUILayout.Label("__________________高度__________________");
        maxLiquidHeightOS = EditorGUILayout.FloatField("最大相对液面高度", maxLiquidHeightOS);
        
        GUILayout.Space(10);
        GUILayout.Label("__________________物理__________________");
        dragScale = EditorGUILayout.Slider("阻力倍率", dragScale, 0, 10);
        
        GUILayout.Space(10);
        GUILayout.Label("__________________动画__________________");
        GUILayout.Label("【添加液体动画】");
        liquidAnimation.lifeTime_Add = EditorGUILayout.FloatField("动画时间", liquidAnimation.lifeTime_Add);
        liquidAnimation.curve_Up = EditorGUILayout.CurveField("高度变化", liquidAnimation.curve_Up);
        liquidAnimation.curve_Warp_Add = EditorGUILayout.CurveField("液面扰动强度", liquidAnimation.curve_Warp_Add);
        liquidAnimation.curve_LerpRange_Add = EditorGUILayout.CurveField("分层混合范围", liquidAnimation.curve_LerpRange_Add);
        GUILayout.Label("【混合液体动画】");
        liquidAnimation.lifeTime_Mix = EditorGUILayout.FloatField("动画时间", liquidAnimation.lifeTime_Mix);
        liquidAnimation.curve_RGBA_Mix = EditorGUILayout.CurveField("颜色变化", liquidAnimation.curve_RGBA_Mix);
        liquidAnimation.curve_Warp_Mix = EditorGUILayout.CurveField("液面扰动强度", liquidAnimation.curve_Warp_Mix);
        liquidAnimation.curve_LerpRange_Mix = EditorGUILayout.CurveField("分层混合范围", liquidAnimation.curve_LerpRange_Mix);
        
        GUILayout.Space(10);
        GUILayout.Label("__________________其他数据__________________");
        GUI.enabled = false;
        if (mat_Liquid)
        {
            GUILayout.Label("材质颜色");
            {
                GUILayout.BeginHorizontal();
                Color[] mCol = mat_Liquid.GetColorArray(id_liquidRGBA);
                if (mCol != null)
                {
                    foreach (var RGBA in mCol)
                    {
                        EditorGUILayout.ColorField(RGBA);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("泡沫强度");
            {
                GUILayout.BeginHorizontal();
                float[] mBubbleInt = mat_Liquid.GetFloatArray(id_liquidBubbleInt);
                if (mBubbleInt != null)
                {
                    foreach (var bubbleInt in mBubbleInt)
                    {
                        EditorGUILayout.FloatField(bubbleInt);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("材质混合范围");
            {
                GUILayout.BeginHorizontal();
                float[] mLerpRange = mat_Liquid.GetFloatArray(id_liquidLerpRange);
                if (mLerpRange != null)
                {
                    foreach (var lerpRange in mLerpRange)
                    {
                        EditorGUILayout.FloatField(lerpRange);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
        GUI.enabled = true;
    }

    const int debugTexsize = 128;
    private void OnGUI()
    {
        // mask纹理数组
        Texture mMask2DArr = mat_Liquid.GetTexture(id_MaskTex2DArr);
        if (mMask2DArr != null)
        {
            if (!mat_DrawMask2DArr)
            {
                mat_DrawMask2DArr = new Material(Shader.Find("Editor/LiquidBottle_ArrayUI"));
            }
            if (!rt_DrawMask2DArr)
            {
                rt_DrawMask2DArr = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32);
            }
            mat_DrawMask2DArr.SetTexture("_Tex2DArr", mMask2DArr);
            Graphics.Blit(null, rt_DrawMask2DArr, mat_DrawMask2DArr);
            RenderTexture.active = null;    // 没有这行, UI无法有效渲染
            
            GUI.DrawTexture(new Rect(0,0,debugTexsize,debugTexsize), rt_DrawMask2DArr);
        }
        
        // RenderFeature中间纹理
        // 场景
        GUI.DrawTexture(new Rect(0, debugTexsize, debugTexsize, debugTexsize), 
            renderPass.handle_SceneColor);
        GUI.DrawTexture(new Rect(debugTexsize, debugTexsize, debugTexsize, debugTexsize), 
            renderPass.handle_SceneDepth);
        // 液体
        GUI.DrawTexture(new Rect(0, 2*debugTexsize, debugTexsize, debugTexsize), 
            renderPass.handle_LiquidColor);
        GUI.DrawTexture(new Rect(debugTexsize, 2*debugTexsize, debugTexsize, debugTexsize), 
            renderPass.handle_LiquidDepth);
        // 冰块
        GUI.DrawTexture(new Rect(0, 3*debugTexsize, debugTexsize, debugTexsize), 
            renderPass.handle_IceColor);
        GUI.DrawTexture(new Rect(debugTexsize, 3*debugTexsize, debugTexsize, debugTexsize), 
            renderPass.handle_IceDepth);
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(LiquidBottleManager))]
class LiquidBottleManagerEditor : Editor
{
    private LiquidBottleManager dst;
    public override void OnInspectorGUI()
    {
        dst = (LiquidBottleManager) target;
        dst.DrawUI();
    }
}
#endif