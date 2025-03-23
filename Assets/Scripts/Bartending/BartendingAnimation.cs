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
        /// 高度过渡动画
        /// <summary>
        public static async UniTask AnimateTwoFloatAsync(float startValue, float targetValue, float duration, Action<float> callback, AnimationCurve curve = null)
        {
            isAnimating = true;
            float elapsedTime = 0f;
            float currentValue = startValue;
            
            while (elapsedTime < duration)
            {
                float t = elapsedTime / duration;

                float smoothT;
                
                if (curve != null)
                {
                    smoothT = curve.Evaluate(t); // 使用动画曲线 
                }
                else
                {
                    smoothT = Mathf.SmoothStep(0f, 1f, t);
                }
                
                currentValue = Mathf.Lerp(startValue, targetValue, smoothT);
                
                // 回调更新
                callback.Invoke(currentValue);
                
                // 等待下一帧
                await UniTask.Yield();
                elapsedTime += Time.deltaTime;
            }
            
            callback.Invoke(targetValue);
            isAnimating = false;
        }

        /// <summary>
        /// 波浪过渡动画
        /// <summary>
        public static async UniTask AnimateFloatAsync(float duration, Action<float> callback, AnimationCurve curve = null)
        {
            isAnimating = true;
            float elapsedTime = 0f;
            float currentValue = 0f;
            
            while (elapsedTime < duration)
            {
                float t = elapsedTime / duration;

                float smoothT;
                
                if (curve != null)
                {
                    smoothT = curve.Evaluate(t); // 使用动画曲线 
                }
                else
                {
                    smoothT = 0f;
                }
                
                currentValue = smoothT;
                
                // 回调更新
                callback.Invoke(currentValue);
                
                // 等待下一帧
                await UniTask.Yield();
                elapsedTime += Time.deltaTime;
            }
            isAnimating = false;
        }
    }
}