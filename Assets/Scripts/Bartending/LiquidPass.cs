using UnityEngine;
using UnityEngine.Rendering;

public class LiquidPass : ScriptableRenderPass
{
    public RTHandle handle_SceneColor, handle_SceneDepth;

    private static readonly ProfilingSampler profilingSampler_Scene = new("LiquidBottleRPass_Scene");
    private static readonly ProfilingSampler profilingSampler_Liquid = new("LiquidBottleRPass_Liquid");

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {

        // 缓存纹理格式
        ScriptableRenderer sRdr = renderingData.cameraData.renderer;
        RenderTextureDescriptor colorDesc = sRdr.cameraColorTargetHandle.rt.descriptor;
        RenderTextureDescriptor depthDesc = sRdr.cameraDepthTargetHandle.rt.descriptor;
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        // Then using RTHandles, the color and the depth properties must be separate
        desc.depthBufferBits = 0;
        RenderingUtils.ReAllocateIfNeeded(ref handle_SceneColor, colorDesc);
        RenderingUtils.ReAllocateIfNeeded(ref handle_SceneDepth, depthDesc);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        m_DestinationColor = null;
        m_DestinationDepth = null;
    }

    public void Setup(RTHandle destinationColor, RTHandle destinationDepth)
    {
        m_DestinationColor = destinationColor;
        m_DestinationDepth = destinationDepth;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();

        using (new ProfilingScope(cmd, profilingSampler_Scene))
        {
            CoreUtils.SetRenderTarget(cmd, handle_SceneColor, handle_SceneDepth, ClearFlag.All, Color.clear);
            Blitter.BlitColorAndDepth(cmd, sRdr_Camera.cameraColorTargetHandle, sRdr_Camera.cameraDepthTargetHandle, 
                viewportScale, 0, true);
        }
        mat_Merge.SetTexture(id_SceneColorBuffer, handle_SceneColor);
        mat_Merge.SetTexture(id_SceneDepthBuffer, handle_SceneDepth);
        
        using (new ProfilingScope(cmd, profilingSampler_Liquid))
        {
            
        }
        
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    void Dispose()
    {
        handle_SceneColor?.Release();
        handle_SceneDepth?.Release();
    }
}
