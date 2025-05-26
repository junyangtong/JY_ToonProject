# JY_ToonProject
## 目录
- [简介](#简介)
- [星穹铁道调酒效果复刻实现](#星穹铁道调酒效果复刻实现)
- [DynamicBone多线程优化](#DynamicBone多线程优化)

# 简介
将2025年开始制作的一些卡通渲染相关的个人作品会放在这个工程里


# 星穹铁道调酒效果复刻实现
知乎文章地址：https://zhuanlan.zhihu.com/p/1898865414240446162

本文的实现方式基本思路参考了知乎 @伊底1D 的关于星穹铁道调酒效果(分层液体瓶)的复刻尝试：https://zhuanlan.zhihu.com/p/694949392

## 渲染顺序
![image](https://github.com/user-attachments/assets/45ddf8e1-525f-4924-9f6e-948a0cccd743)![image](https://github.com/user-attachments/assets/52bcbda3-384e-4edc-bef7-94ad42adbfa1)


直接看drawcall比较简单粗暴：先画液体正面再画液体背面，使用stencil遮罩假平面的深度，画完液体把color传入冰块做折射用（冰块不用透明度，深度可以放在颜色a通道可以少采一张图）。

然后，在mergeshader对 `_IceColorBuffer` `_LiquidColorBuffer` `_LiquidDepthBuffer` `SceneColor`做混合，输出正确排序的颜色和深度。

最后，再copy一张sceneColor给玻璃杯做折射用。

```hlsl
// 采样
half4 iceColor = SAMPLE_TEXTURE2D(_IceColorBuffer, sampler_IceColorBuffer, input.texcoord);
half iceDepth = iceColor.a;
half4 liquidColor = SAMPLE_TEXTURE2D(_LiquidColorBuffer, sampler_LiquidColorBuffer, input.texcoord);
half liquidDepth = SAMPLE_TEXTURE2D(_LiquidDepthBuffer, sampler_LiquidDepthBuffer, input.texcoord).r;
// 混合颜色
half4 sceneColor = half4(SampleSceneColor(input.texcoord), 1.0);
// 冰块
half4 iceAndBackGround = lerp(sceneColor, iceColor, step(0.001, iceDepth));
                
// 冰块和液面交界高亮
half liquidDepth01 = Linear01Depth(liquidDepth, _ZBufferParams);
half iceDepth01 = Linear01Depth(iceDepth, _ZBufferParams);
half contactMask = smoothstep(liquidDepth01, liquidDepth01 + 0.00007, iceDepth01);
liquidColor.rgb = lerp(liquidColor.rgb * 1.5, liquidColor.rgb, contactMask);
half4 finalColor = lerp(iceAndBackGround * (1 - liquidColor.a) + liquidColor * liquidColor.a, iceAndBackGround, step(liquidDepth, iceDepth));
                
// 混合深度
depthOUT = lerp(liquidDepth, iceDepth, step(liquidDepth, iceDepth));
return finalColor;
```

## 数据结构

每一层酒的数据结构：
![image](https://github.com/user-attachments/assets/0c0ce3b1-6e30-4f83-b651-6cb89f989e92)

- Color: RGB颜色 A透明度
- MaskTex： 每层酒的细节纹理，不赋值为黑色没有纹理
- LerpRange：和下层酒之间的融合范围
- BubbleInt：气泡强度
- Lerp WarpInt：融合部分的扰动程度，越大融合部分噪声越强烈
- LerpWarpSize：控制融合部分的噪声密集程度

manager脚本中维护这些数组，记录当前已有的数据，传递到shader：

```csharp
[SerializeField] private Color[] layerColors;
[SerializeField] private float[] layerLerps;
[SerializeField] private float[] bubbleInt;
[SerializeField] private float[] lerpWarpInt;
[SerializeField] private float[] lerpWarpSize;
//...
private RenderTexture layerMaskTexArray;
```

## 液体

液体的模型就是杯子内壁的模型复制一份，分两个pass渲染，一次画正面一次画背面。

```hlsl
Pass
{
    Name "Draw Front"
    Tags {"LightMode" = "SRPDefaultUnlit"}
    Stencil
    {
        Ref 1
        Comp Always
        Pass Replace
        ZFail Replace // 确保在被其他物体遮挡时也能写入模板值
    }
    Cull Back
    Blend One Zero
    ZWrite On
}

Pass
{
    Name "Draw Back"
    Tags {"LightMode" = "UniversalForward"}
    Stencil
    {
        Ref 1
        Comp NotEqual
        Pass Replace
    }
    Cull Front
    Blend One Zero
    ZWrite On
}
```

## 冰块

GPUInstance绘制冰块，将之前绘制的液体颜色传入冰块shader，在冰块shader中采样liquidColor和sceneColor排序后做折射。

```csharp
iceMat.SetTexture(id_LiquidColorBuffer, handle_LiquidColor);
// Pass2 冰块 gpuinstance
using (new ProfilingScope(cmd, profilingSampler_Ice))
{
    if (iceMatrix != null && iceMatrix.Length > 0)
    {
        CoreUtils.SetRenderTarget(cmd, handle_IceColor, handle_IceDepth, ClearFlag.All);
        cmd.DrawMeshInstanced(iceMesh, 0, iceMat, -1, iceMatrix, iceMatrix.Length);
    }
}
```

## 液面

### 虚拟平面

通过平面射线相交来模拟液体表面，根据：
- 平面方程：n * (intersectPos - planeCenter) = 0
- 射线方程：intersectPos = input.positionWS + t * input.viewDirWS

解 intersectPosWS：

```hlsl
//虚拟液面
half3 planeCenter = float3(0.0, originPosWS.y + liquidHeightOS, 0.0);
half3 n = waveInfo.normal;
float3 intersectPosWS = input.positionWS + input.viewDirWS * dot(n, planeCenter - input.positionWS) / dot(n, input.viewDirWS);
```

### 液面模拟水面张力
![image](https://github.com/user-attachments/assets/4a76194e-9bed-46a4-bb11-4d456b1ee83a)

1. **法线**

水和杯壁接触的部分会产生张力，张力有浸润和不浸润两种情况，但是玻璃材质的杯子一般是浸润的。
计算液面边缘区域的遮罩，将遮罩范围内的液面法线和杯壁的法线做lerp就还原了张力影响的水面法线。但是，这里没有拿到真正的杯壁法线，而是计算了一个指向液面中心的虚拟杯壁法线。
![image](https://github.com/user-attachments/assets/543458ac-89c4-4b95-97f6-445d389bf8d1)
![image](https://github.com/user-attachments/assets/d72feb9d-ae1d-4ca8-a6d2-da02b88f3cd0)

```hlsl
float3 CalculateCylindricalNormal(float3 positionOS)
{      
   float2 dirFromCenter = normalize(positionOS.xz);
   return normalize(float3(dirFromCenter.x, 0, dirFromCenter.y));
}
```

2. **吃水线**
![image](https://github.com/user-attachments/assets/5d176c99-0c73-40c9-a58a-eefe8c8d5216)

因为张力会让水面抬升一定的高度，那么从侧面看过去，光线被这段抬升的高度折射，就形成了吃水线：

```hlsl
// 吃水线
half waterlineMask = smoothstep(_WaterLineWidth, 0.0, clipPos) * saturate(dot(input.normalWS, normal));
float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
float2 refractionUV = screenUV + waterlineMask * 0.3;
half3 waterlineCol = SampleSceneColor(refractionUV) * waterlineMask; 
finalColor = lerp(finalColor, finalColor * 0.5, waterlineMask) + waterlineCol * (1.0 - alpha);
```

这里直接给屏幕uv加上遮罩做了一些扭曲，一点也不物理，但是效果好像还行。

## 浮力模拟

初中浮力物理公式，直接放代码：

```csharp
// 计算波浪
waveAmplitude = bartendingManager.WaveAmplitude;
WaveInfo waveInfo = CalculateWave(relativePosition);

// 当前液面相对高度
liquidHeight01 = bartendingManager.LiquidHeight01;
liquidHeight = liquidHeight01 * maxLiquidHeight + waveInfo.height;

// F浮=液体的密度×体积×重力加速度
float bottomDepth = liquidHeight - relativePosition.y + centerToBottomOffset;
Vector3 buoyancy = buoyancyForceStrength * bottomDepth * bottomDepth * bottomDepth * -Physics.gravity.normalized;
buoyancy = Vector3.ClampMagnitude(buoyancy, maxBuoyancyForce);
rigidBody.AddForce(buoyancy, ForceMode.Acceleration);
            
// 添加阻力
rigidBody.AddTorque(-angularDrag * rigidBody.angularVelocity);
var forcePosition = rigidBody.worldCenterOfMass + 1f * Vector3.up;
rigidBody.AddForceAtPosition(drag.x * Vector3.Dot(transform.right, -rigidBody.velocity) * transform.right, forcePosition, ForceMode.Acceleration);
rigidBody.AddForceAtPosition(drag.y * Vector3.Dot(Vector3.up, -rigidBody.velocity) * Vector3.up, forcePosition, ForceMode.Acceleration);
rigidBody.AddForceAtPosition(drag.z * Vector3.Dot(transform.forward, -rigidBody.velocity) * transform.forward, forcePosition, ForceMode.Acceleration);
```

拟合了两种波形，一种用来做倒酒时候的波浪，一种做搅拌的波浪：

```hlsl
WaveInfo CalculateWave(float3 position)
{
    WaveInfo waveInfo;
    float time = _Time.y * _WaveSpeed;

    float waveHeight = 0.0;
    if (_WaveType > 0.5)
    {
        waveHeight = _WaveAmplitude * 0.05 * sin(position.z * _WaveFrequency + time)
                     + _WaveAmplitude * 0.05 * sin(position.x * _WaveFrequency + time);
    }
    else
    {
        position.xz = Polar(position.xz);// 极坐标
        waveHeight = _WaveAmplitude * 0.05 * sin(position.x * PI * 3.0  + time);
    }                

    float3 T = float3
    (
        1.0,
        _WaveAmplitude * 0.05 * _WaveFrequency * cos(position.x * _WaveFrequency + time) 
        * _WaveAmplitude * 0.05 * sin(position.z * _WaveFrequency + time),
        0.0
    );
    float3 B = float3
    (
        0.0,
        _WaveAmplitude * 0.05 * _WaveFrequency * sin(position.x * _WaveFrequency + time) 
        * _WaveAmplitude * 0.05 * cos(position.z * _WaveFrequency + time),
        1.0
    );

    float3 N = cross(B, T);
    float3 normal = normalize(N);
    
    waveInfo.normal = normal;
    waveInfo.height = waveHeight;

    return waveInfo;
}
```

## 高度裁剪

首先，设置当前的最大高度`_MaxLiquidHeight`，和最大层数`_MaxLayers`。

然后，每次添加液体会传入一个`_LiquidHeight01`参数到shader，`_LiquidHeight01`是通过 当前层ID/MaxLayers 得到的，所以`_LiquidHeight01 * _MaxLiquidHeight`就是当前液面的高度。用这个高度截断相对坐标的y轴，再加上波形产生的高度就得到了需要裁剪的范围。

```hlsl
// 计算相对坐标
float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
float3 relativePos = input.positionWS.xyz - originPosWS;
// 高度裁剪
float liquidHeightOS = _LiquidHeight01 * _MaxLiquidHeight + _LiquidHeightOffset;
float clipPos = liquidHeightOS - relativePos.y + waveInfo.height;
clip(clipPos);
```

## 层ID

这里要同时拿到currentID和nextID，为之后做过渡做准备：

currentID：
![image](https://github.com/user-attachments/assets/65f8f40a-ba30-4ed3-a7e5-b934c0af822f)

nextID：
![image](https://github.com/user-attachments/assets/3017eb01-d611-4f7d-a3c9-ce77a2994f79)


```hlsl
float liquidHeight0Max = relativePos.y / _MaxLiquidHeight * _MaxLayers;
uint currentID = floor(liquidHeight0Max - 0.5);
int nextID = min(_MaxLayers - 1, currentID + 1);
```

用currentID和nextID采样数组中的颜色：

```hlsl
half4 currentColor = _LiquidLayerColor[currentID];
half4 nextColor = _LiquidLayerColor[nextID];
```

currentColor:
![image](https://github.com/user-attachments/assets/9a4210ff-40b8-47dc-b3a5-af3152da59e7)

nextColor:
![image](https://github.com/user-attachments/assets/fa586af7-12fd-45ea-8c8e-8f6b8847e429)


## 层过渡

这里用nextID计算出了位于每层中间位置的一个渐变遮罩：

```hlsl
half lerpRange = _LiquidLayerLerpRange[nextID];
half lerp01 = smoothstep(nextID - lerpRange, nextID + lerpRange, liquidHeight0Max);
half lerpWarpInt = _LerpWarpInt[nextID];
half lerpWarpSize = _LerpWarpSize[nextID];
half layerWarpMask = 1.0 - abs(lerp01 - 0.5) * 2.0;
half lerpNoise = SAMPLE_TEXTURE2D(_LerpNoise, sampler_LerpNoise, input.uv * lerpWarpSize * _LayerWarpSize).r;
lerp01 = lerp01 + (lerpNoise - 0.5) * lerpWarpInt * layerWarpMask;
```
![image](https://github.com/user-attachments/assets/8c655268-5011-48ee-8ee6-c63cf2c89659)

用这个遮罩lerp currentColor和nextColor就得到了混合后的颜色。

可以看到这里每层的混合方式好像不太一样，最上层紫色混合处有很多扰动，这是因为每层过渡都有单独的Warp参数，由每层液体的配置文件决定。

![image](https://github.com/user-attachments/assets/9b7bcff6-9cd8-4ace-b563-712fb4860c8f)


这里只写了颜色，其他的遮罩、气泡等都是同理。

![image](https://github.com/user-attachments/assets/3577dadb-7cb4-49ff-a55a-b6aa6004c466)


## 异步动画

以搅拌动画为例，计算动画前先计算好各个动画参数的平均值作为目标值：

```csharp
// 计算平均值
int blendCount = currentLayer - 1;// 需要混合的层数
Color averageColor = Color.clear;
float averageBubbleInt = 0;
for (int i = 0; i <= blendCount; i++)
{
    averageColor += layerColors[i];
    averageBubbleInt += bubbleInt[i];
}
averageColor /= currentLayer;
averageBubbleInt /= currentLayer;
int count = blendCount == 4 ? blendCount : currentLayer; // 要改变上面两层颜色
RenderTexture averageMask = BartendingAnimation.AverageMask(layerMaskTexArray, count);
float averagelayerLerp = 0.6f;
// 切换波浪动画
waveType = 0;
```

使用UniTask实现异步，混合一些参数时用返回值time根据各个部分的动画曲线更新相应的参数，混合mask则是直接在异步方法中使用cs计算了混合后的mask：

```csharp
// 混合渐变动画
UniTask blendTask =  BartendingAnimation.AnimationTimerAsync(
    liquidBlendDuration,
    (float time) => 
    {
        for (int i = 0; i <= count; i++)
        {
            // 混合颜色
            layerColors[i] = Color.Lerp(layerColorTarget[i], averageColor, blendColorCurve.Evaluate(time));
            // 混合泡沫强度
            bubbleInt[i] = Mathf.Lerp(bubbleIntTarget[i], averageBubbleInt, blendBubbleCurve.Evaluate(time));
            // 增加lerp范围
            layerLerps[i] = Mathf.Lerp(layerLerpsTarget[i], averagelayerLerp, blendlerpCurve.Evaluate(time));
            shaderNeedUpdate = true;
        }
        // 波浪动画
        waveAmplitude = blendWarpCurve.Evaluate(time);
        // UV动画 （在上次的uv偏移基础上累加）
        uvOffest.x = preUvOffest.x + blendUVCurve.Evaluate(time);
        uvOffest.y = preUvOffest.y + blendUVCurve.Evaluate(time);
    }
);
// 混合mask
UniTask blendMaskTask = BartendingAnimation.MaskAnimationAsync(
    liquidBlendDuration, 
    layerMaskTexArray, 
    (RenderTexture mask) => {
        layerMaskTexArray = mask;
        shaderNeedUpdate = true;        
    }
    ,blendMaskCurve
);
```

![image](https://github.com/user-attachments/assets/5f3bf6d5-413f-4e30-9ecb-5585e5289bd8)


## 未解决问题

平面方程不能很好的模拟液体波动比较大时的水面，更精致的模拟可以用raymarching？

## 最后

本项目仅作为个人学习使用
如有错误，还请指正

# DynamicBone多线程优化
