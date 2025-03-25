using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace JY.Toon.Bartending
{
    public class LiquidPass : ScriptableRenderPass
    {
        public RTHandle handle_SceneColor, handle_SceneDepth;

        private Material mergeMat;

        private static readonly ProfilingSampler profilingSampler_Scene = new("LiquidBottleRPass_Scene");
        private static readonly ProfilingSampler profilingSampler_Liquid = new("LiquidBottleRPass_Liquid");

        public static readonly int id_SceneColorBuffer = Shader.PropertyToID("_SceneColorBuffer");
        public static readonly int id_SceneDepthBuffer = Shader.PropertyToID("_SceneDepthBuffer");
        public LiquidPass()
        {
            // 混合
            mergeMat = new Material(Shader.Find("JY/Toon/LiquidMerge"));
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 缓存纹理格式
            ScriptableRenderer sRdr = renderingData.cameraData.renderer;
            
            // 使用RenderingUtils获取描述符，避免直接访问rt
            RenderTextureDescriptor colorDesc = renderingData.cameraData.cameraTargetDescriptor;
            colorDesc.depthBufferBits = 0; // 确保颜色RT没有深度缓冲
            
            RenderTextureDescriptor depthDesc = renderingData.cameraData.cameraTargetDescriptor;
            depthDesc.colorFormat = RenderTextureFormat.Depth;
            depthDesc.depthBufferBits = 32; // 根据需要调整深度缓冲位数
            
            // 重新分配场景RT
            RenderingUtils.ReAllocateIfNeeded(ref handle_SceneColor, colorDesc);
            RenderingUtils.ReAllocateIfNeeded(ref handle_SceneDepth, depthDesc);
        }
    /* 
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            m_DestinationColor = null;
            m_DestinationDepth = null;
        } */

    /*     public void Setup(RTHandle destinationColor, RTHandle destinationDepth)
        {
            m_DestinationColor = destinationColor;
            m_DestinationDepth = destinationDepth;
        }
    */
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 缓存
            CommandBuffer cmd = CommandBufferPool.Get("LiquidPass"); // 添加名称参数
            ScriptableRenderer sRdr_Camera = renderingData.cameraData.renderer;
            RTHandle tempCamColorHandle = sRdr_Camera.cameraColorTargetHandle;
            Vector2 viewportScale = tempCamColorHandle.useScaling ?
                new Vector2(
                    tempCamColorHandle.rtHandleProperties.rtHandleScale.x, 
                    tempCamColorHandle.rtHandleProperties.rtHandleScale.y
                ) : Vector2.one;

            // 场景
            using (new ProfilingScope(cmd, profilingSampler_Scene))
            {
                CoreUtils.SetRenderTarget(cmd, handle_SceneColor, handle_SceneDepth, ClearFlag.All, Color.clear);
                Blitter.BlitColorAndDepth(cmd, sRdr_Camera.cameraColorTargetHandle, sRdr_Camera.cameraDepthTargetHandle, 
                    viewportScale, 0, true);
            }
            mergeMat.SetTexture(id_SceneColorBuffer, handle_SceneColor);
            mergeMat.SetTexture(id_SceneDepthBuffer, handle_SceneDepth);

            cmd.DrawProcedural(Matrix4x4.identity, mergeMat, 0, MeshTopology.Triangles, 3, 1);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // 此方法在每个相机渲染完成后调用
            // 可以在这里释放临时资源，但不要释放在多帧之间重用的RTHandle
        }

        // 在RendererFeature的Dispose中调用此方法
        public void Cleanup()
        {
            handle_SceneColor?.Release();
            handle_SceneDepth?.Release();
            CoreUtils.Destroy(mergeMat);
        }
    }
}