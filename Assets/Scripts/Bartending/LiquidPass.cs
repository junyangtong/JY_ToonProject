using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
namespace JY.Toon.Bartending
{
    public class LiquidPass : ScriptableRenderPass
    {
        public RTHandle handle_SceneColor, handle_SceneDepth;

        private Material mergeMat;
        private Renderer liquidRenderer;
        private Mesh iceMesh;
        private Material liquidMat;
        private Material iceMat;
        private Matrix4x4[] iceMatrix;
        public static readonly int id_SceneColorBuffer = Shader.PropertyToID("_SceneColorBuffer");
        public static readonly int id_SceneDepthBuffer = Shader.PropertyToID("_SceneDepthBuffer");

        private static readonly ProfilingSampler profilingSampler_Scene = new("LiquidPass_Scene");
        private static readonly ProfilingSampler profilingSampler_Liquid = new("LiquidPass_Liquid");
        private static readonly ProfilingSampler profilingSampler_Ice = new("LiquidPass_Ice");
        private static readonly ProfilingSampler profilingSampler_Merge = new("LiquidPass_Merge");

        public LiquidPass(Renderer liquidRenderer, GameObject iceObj)
        {
            this.liquidRenderer = liquidRenderer;
            this.iceMesh = iceObj.GetComponent<MeshFilter>().sharedMesh;
            liquidMat = liquidRenderer.sharedMaterial;
            iceMat = iceObj.GetComponent<Renderer>().sharedMaterial;
            mergeMat = new Material(Shader.Find("JY/Toon/LiquidMerge"));
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public void UpdateIceMatrix(List<Rigidbody> iceRigid)
        {
            if (iceRigid != null)
            {
                iceMatrix = new Matrix4x4[iceRigid.Count];
                for (int i = 0; i < iceRigid.Count; i++)
                {
                    iceMatrix[i] = iceRigid[i].transform.localToWorldMatrix;
                }
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 缓存纹理格式
            ScriptableRenderer sRdr = renderingData.cameraData.renderer;
            RenderTextureDescriptor colorDesc = sRdr.cameraColorTargetHandle.rt.descriptor;
            RenderTextureDescriptor depthDesc = sRdr.cameraDepthTargetHandle.rt.descriptor;
            
            // 场景RT
            RenderingUtils.ReAllocateIfNeeded(ref handle_SceneColor, colorDesc);
            RenderingUtils.ReAllocateIfNeeded(ref handle_SceneDepth, depthDesc);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 缓存
            CommandBuffer cmd = CommandBufferPool.Get("LiquidPass");
            ScriptableRenderer sRdr_Camera = renderingData.cameraData.renderer;
            RTHandle tempCamColorHandle = sRdr_Camera.cameraColorTargetHandle;
            Vector2 viewportScale = tempCamColorHandle.useScaling ?
                new Vector2(
                    tempCamColorHandle.rtHandleProperties.rtHandleScale.x, 
                    tempCamColorHandle.rtHandleProperties.rtHandleScale.y
                ) : Vector2.one;

            // Pass1 场景
            using (new ProfilingScope(cmd, profilingSampler_Scene))
            {
                CoreUtils.SetRenderTarget(cmd, handle_SceneColor, handle_SceneDepth, ClearFlag.None);
                Blitter.BlitColorAndDepth(cmd, sRdr_Camera.cameraColorTargetHandle, sRdr_Camera.cameraDepthTargetHandle, 
                    viewportScale, 0, true);
            }
            mergeMat.SetTexture(id_SceneColorBuffer, handle_SceneColor);
            mergeMat.SetTexture(id_SceneDepthBuffer, handle_SceneDepth);

            // Pass2 液体
            using (new ProfilingScope(cmd, profilingSampler_Liquid))
            {
                CoreUtils.SetRenderTarget(cmd, handle_SceneColor, handle_SceneDepth, ClearFlag.Stencil);
                cmd.DrawRenderer(liquidRenderer, liquidMat, 0, -1);
            }
            // Pass3 冰块 gpuinstance
            using (new ProfilingScope(cmd, profilingSampler_Ice))
            {
                if (iceMatrix != null && iceMatrix.Length > 0)
                {
                    CoreUtils.SetRenderTarget(cmd, handle_SceneColor, handle_SceneDepth, ClearFlag.None);
                    cmd.DrawMeshInstanced(iceMesh, 0, iceMat, -1, iceMatrix, iceMatrix.Length);
                }
            }
            // Pass4 混合
            using (new ProfilingScope(cmd, profilingSampler_Merge))
            {
                CoreUtils.SetRenderTarget(cmd, sRdr_Camera.cameraColorTargetHandle, sRdr_Camera.cameraDepthTargetHandle, ClearFlag.None);
                cmd.DrawProcedural(Matrix4x4.identity, mergeMat, 0, MeshTopology.Triangles, 3, 1);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            handle_SceneColor?.Release();
            handle_SceneDepth?.Release();
            CoreUtils.Destroy(mergeMat);
        }
    }
}