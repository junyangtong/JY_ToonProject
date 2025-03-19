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
        
        /// <summary>
        /// 高度平滑过渡
        /// <returns></returns>
        public static async UniTask AnimateFloatAsync(float startHeight, float targetHeight, float duration, Action<float> callback)
        {
            isAnimating = true;
            float elapsedTime = 0f;
            float currentValue = startHeight;
            
            while (elapsedTime < duration)
            {
                float t = elapsedTime / duration;
                // TODO: 使用动画曲线
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                currentValue = Mathf.Lerp(startHeight, targetHeight, smoothT);
                
                // 回调更新高度
                callback.Invoke(currentValue);
                
                // 等待下一帧
                await UniTask.Yield();
                elapsedTime += Time.deltaTime;
            }
            
            callback.Invoke(targetHeight);
            isAnimating = false;
        }
    }
}