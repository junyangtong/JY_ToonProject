using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace JY.Toon.Bartending
{
    /// <summary>
    /// 调酒相关动画
    /// </summary>
    public static class BartendingAnimation
    {
        private static bool isAnimating = false;

        public static bool IsAnimating => isAnimating;

        // mask
        private static ComputeShader maskBlendCS;
        private static int k_AverageMask;
        private static int k_LerpMask;
        private static RenderTexture averageMask;
        private static int maskSize = BartendingManager.Instance.MaskSize;

        /// <summary>
        /// 初始化
        /// </summary>
        public static void Initialize(ComputeShader cs)
        {
            maskBlendCS = cs;
            k_AverageMask = maskBlendCS.FindKernel("AverageMask");
            k_LerpMask = maskBlendCS.FindKernel("LerpMask");
        }

        /// <summary>
        /// 动画计时器
        /// </summary>
        public static async UniTask AnimationTimerAsync(float duration, Action<float> callback)
        {
            isAnimating = true;
            float elapsedTime = 0f;
            while (elapsedTime < duration)
            {
                callback.Invoke(elapsedTime / duration);
                await UniTask.Yield();
                elapsedTime += Time.deltaTime;
            }
            callback.Invoke(1.0f);
            isAnimating = false;
        }

        /// <summary>
        /// 计算 mask texarray 中图像的平均颜色
        /// </summary>
        public static RenderTexture AverageMask(RenderTexture layerMaskTexArray, int count)
        {
            if (averageMask != null)
            {
                averageMask.Release();
            }
            averageMask = new RenderTexture(
                maskSize, maskSize, 0, 
                RenderTextureFormat.R8, RenderTextureReadWrite.Linear
            );
            averageMask.enableRandomWrite = true;
            averageMask.Create();
            uint tSize_X, tSize_Y, tSize_Z;
            maskBlendCS.GetKernelThreadGroupSizes(k_AverageMask, out tSize_X, out tSize_Y, out tSize_Z);
            Vector3Int gSize = new Vector3Int(
                Mathf.CeilToInt(averageMask.width / (float) tSize_X),
                Mathf.CeilToInt(averageMask.height / (float) tSize_Y),
                1
            );
            
            maskBlendCS.SetInt("_LayerNum", count);
            maskBlendCS.SetTexture(k_AverageMask, "_OutTex2D", averageMask);
            maskBlendCS.SetTexture(k_AverageMask, "_SrcMaskTex2DArr", layerMaskTexArray);
            maskBlendCS.Dispatch(k_AverageMask, gSize.x, gSize.y, gSize.z);

            return averageMask;
        }

        /// <summary>
        /// mask texarray 过渡到 averageMask
        /// </summary>
        public static async UniTask MaskAnimationAsync(float duration, RenderTexture layerMaskTexArray, Action<RenderTexture> callback)
        {
            isAnimating = true;
            float elapsedTime = 0f;
            while (elapsedTime < duration)
            {
                float time = elapsedTime / duration;
                uint tSize_X, tSize_Y, tSize_Z;
                maskBlendCS.GetKernelThreadGroupSizes(k_LerpMask, out tSize_X, out tSize_Y, out tSize_Z);
                Vector3Int gSize = new Vector3Int(
                    Mathf.CeilToInt(averageMask.width / (float) tSize_X),
                    Mathf.CeilToInt(averageMask.height / (float) tSize_Y),
                    1
                );
                
                maskBlendCS.SetTexture(k_LerpMask, "_OutMaskTex2DArr", layerMaskTexArray);
                maskBlendCS.SetTexture(k_LerpMask, "_SrcMaskTex2DArr", layerMaskTexArray);
                maskBlendCS.SetTexture(k_LerpMask, "_DstMaskTex2D", averageMask);
                maskBlendCS.SetFloat("_Lerp01", time);
                maskBlendCS.Dispatch(k_LerpMask, gSize.x, gSize.y, gSize.z);
                callback.Invoke(layerMaskTexArray);
                await UniTask.Yield();
                elapsedTime += Time.deltaTime;
            }
            
            isAnimating = false;
        }
    }
}