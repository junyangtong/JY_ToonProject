using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
//using Cysharp.Threading.Tasks;

namespace JY.Toon.Bartending
{
    /// <summary>
    /// 调酒相关动画
    /// </summary>
    public static class BartendingAnimation
    {
        private static bool isAnimating = false; // 是否正在执行动画

        public static bool IsAnimating => isAnimating;
        
        /// <summary>
        /// 高度平滑过渡
        /// <returns></returns>
        public static async UniTask AnimateFloatAsync(float startHeight, float targetHeight, float duration, Action<float> onUpdate)
        {
            if (isAnimating)
            {
                Debug.LogWarning("动画正在执行");
                return;
            }
            
            isAnimating = true;
            float elapsedTime = 0f;
            float currentValue = startHeight;
            
            while (elapsedTime < duration)
            {
                float t = elapsedTime / duration;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                currentValue = Mathf.Lerp(startHeight, targetHeight, smoothT);
                
                // 调用更新回调
                Invoke(currentValue);
                
                // 等待下一帧
                await UniTask.Yield();
                elapsedTime += Time.deltaTime;
            }
            
            // 确保最终值精确
            Invoke(targetHeight);
            isAnimating = false;
        }
    }
}