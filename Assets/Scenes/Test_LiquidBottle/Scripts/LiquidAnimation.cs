using System;
using UnityEngine;
using static LiquidBottleManager;

[Serializable]
public class LiquidAnimation 
{
    // 目标管理器
    private LiquidBottleManager dstManager;

    // 添加动画
    public float lifeTime_Add = 1;
    public AnimationCurve curve_Up = new AnimationCurve();
    public AnimationCurve curve_Warp_Add = new AnimationCurve();
    public AnimationCurve curve_LerpRange_Add = new AnimationCurve();
    
    // 混合动画
    public float lifeTime_Mix = 1;
    public AnimationCurve curve_RGBA_Mix = new AnimationCurve();
    public AnimationCurve curve_Warp_Mix = new AnimationCurve();
    public AnimationCurve curve_LerpRange_Mix = new AnimationCurve();

    public enum UpdateMode
    {
        None,
        Add,
        Mix,
    };
    private UpdateMode updateMode = UpdateMode.None;
    public UpdateMode GetUpdateMode() => updateMode;
    private float time0;
    
    // 设置目标管理器
    public void SetManager(LiquidBottleManager dstManager)
    {
        this.dstManager = dstManager;
    }

    // 添加液体动画
    private float[] rawLerpRange = new float[maxLiquidNumInShader];
    private float currentH01, nextH01;  // 添加前后的归一化液面高度
    private int lastID_Liquid;  // 每次添加记录最新添加的液体ID, 在Update时只修改这一项的LerpRange
    public void StartUpdate_Add()
    {
        // 缓存原始的混合范围, 作为Update中的插值上界
        int liquidNum = dstManager.GetCurrentLiquidNum();
        lastID_Liquid = liquidNum - 1;
        for (int i = 0; i < liquidNum; i++)
        {
            rawLerpRange[i] = dstManager.GetLerpRange(i);
        }

        // 高度动画插值上下界
        currentH01 = (float)lastID_Liquid / dstManager.maxLiquidNum;
        nextH01 = (float)liquidNum / dstManager.maxLiquidNum;
            
        // Update变量
        time0 = Time.timeSinceLevelLoad;
        updateMode = UpdateMode.Add;
    }

    // 混合动画
    private Color[] rawRGBA = new Color[maxLiquidNumInShader];
    private float[] rawBubbleInt = new float[maxLiquidNumInShader];
    private RenderTexture rawMask2DArr;
    
    private Color averageRGBA;
    private float averageBubbleInt;
    private RenderTexture averageMask;
    public void StartUpdate_Mix()
    {
        // 计算平均色 & 泡沫强度
        int liquidNum = dstManager.GetCurrentLiquidNum();
        averageRGBA = Color.clear;
        for (int i = 0; i < liquidNum; i++)
        {
            rawRGBA[i] = dstManager.GetRGBA(i);
            rawBubbleInt[i] = dstManager.GetBubbleInt(i);
            rawLerpRange[i] = dstManager.GetLerpRange(i);
            averageRGBA += rawRGBA[i];
            averageBubbleInt += rawBubbleInt[i];
        }
        averageRGBA /= liquidNum;
        averageBubbleInt /= liquidNum;
        
        // 平均mask
        if (rawMask2DArr)
        {
            rawMask2DArr.Release();
        }
        rawMask2DArr = dstManager.CopyMaskArrayBuffer();
        if (averageMask)
        {
            averageMask.Release();
        }
        averageMask = new RenderTexture(
            maskSize, maskSize, 0, 
            RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
        averageMask.enableRandomWrite = true;
        averageMask.Create();
        LiquidTextureRuntimeBaker_CS.AverageMask(averageMask, rawMask2DArr, liquidNum);
        
        // Update变量
        time0 = Time.timeSinceLevelLoad;
        updateMode = UpdateMode.Mix; 
    }
    
    // 刷新动画
    public void UpdateAnim()
    {
        switch (updateMode)
        {
            case UpdateMode.Add:
            {
                Anim_Add();
                break;
            }
            case UpdateMode.Mix:
            {
                Anim_Mix();
                break;
            }
        }
    }
    
    // 高度动画
    private void Anim_Add()
    {
        float lerp01 = Mathf.Clamp01((Time.timeSinceLevelLoad - time0) / lifeTime_Add);
        
        // 高度
        float h01 = Mathf.Lerp(currentH01, nextH01, curve_Up.Evaluate(lerp01));
        dstManager.SetHeight01(h01);
    
        // 液面扰动
        float warpInt = curve_Warp_Add.Evaluate(lerp01);
        dstManager.SetHeightWarpInt(warpInt);
    
        // 过渡范围
        float lerpRange = curve_LerpRange_Add.Evaluate(lerp01) * rawLerpRange[lastID_Liquid];
        dstManager.SetLerpRange(lastID_Liquid, lerpRange);

        // 动画结尾
        if (lerp01 >= 1)
        {
            updateMode = UpdateMode.None;
        }
    }
    // 混合动画
    private void Anim_Mix()
    {
        float lerp01 = Mathf.Clamp01((Time.timeSinceLevelLoad - time0) / lifeTime_Mix);
                
        // 颜色 & 泡沫强度 & 混合范围插值
        int liquidNum = dstManager.GetCurrentLiquidNum();
        Color rgba_i = Color.black;
        float bubbleInt_i = 0, lerpRange_i = 0;
        for (int i = 0; i < liquidNum; i++)
        {
            rgba_i = Color.Lerp(rawRGBA[i], averageRGBA, lerp01);
            bubbleInt_i = Mathf.Lerp(rawBubbleInt[i], averageBubbleInt, curve_RGBA_Mix.Evaluate(lerp01));
            lerpRange_i = Mathf.Lerp(rawLerpRange[i], 0.5f, curve_LerpRange_Mix.Evaluate(lerp01));
            dstManager.SetRGBA(i, rgba_i);
            dstManager.SetBubbleInt(i, bubbleInt_i);
            dstManager.SetLerpRange(i, lerpRange_i);
        }
        if (liquidNum < maxLiquidNumInShader)
        {
            dstManager.SetRGBA(liquidNum, rgba_i);
            dstManager.SetBubbleInt(liquidNum, bubbleInt_i);
            dstManager.SetLerpRange(liquidNum, 0);
        }
        
        // mask插值
        LiquidTextureRuntimeBaker_CS.LerpMask(
            dstManager.GetMaskArray(), rawMask2DArr, averageMask, lerp01);
        
        // 液面扰动
        float warpInt = curve_Warp_Mix.Evaluate(lerp01);
        dstManager.SetHeightWarpInt(warpInt);

        // 动画结尾
        if (lerp01 >= 1)
        {
            updateMode = UpdateMode.None;
        }
    }
}
