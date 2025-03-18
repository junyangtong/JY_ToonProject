using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RPass_LiquidBottle : ScriptableRenderPass
{
    private Renderer rdr_Liquid;
    private Mesh mesh_Ice;
    private Material mat_Ice;
    private Matrix4x4[] matrix_Ice;

    public static readonly int id_RelativeOriginAndFlag = Shader.PropertyToID("_RelativeOriginAndFlag");
    public RTHandle handle_SceneColor, handle_SceneDepth;
    public RTHandle handle_LiquidColor, handle_LiquidDepth;
    public RTHandle handle_IceColor, handle_IceDepth;
    
    private Material mat_Merge;
    private static Mesh s_TriangleMesh;
    public static readonly int id_SceneColorBuffer = Shader.PropertyToID("_SceneColorBuffer");
    public static readonly int id_SceneDepthBuffer = Shader.PropertyToID("_SceneDepthBuffer");
    public static readonly int id_LiquidColorBuffer = Shader.PropertyToID("_LiquidColorBuffer");
    public static readonly int id_LiquidDepthBuffer = Shader.PropertyToID("_LiquidDepthBuffer");
    public static readonly int id_IceColorBuffer = Shader.PropertyToID("_IceColorBuffer");
    public static readonly int id_IceDepthBuffer = Shader.PropertyToID("_IceDepthBuffer");
    public static readonly int _BlitMipLevel = Shader.PropertyToID("_BlitMipLevel");
    public static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
    
    public RPass_LiquidBottle(Renderer rdr_Liquid, Mesh mesh_Ice, Material mat_Ice, Material mat_RFMerge)
    {
        // 液体
        this.rdr_Liquid = rdr_Liquid;
        this.rdr_Liquid.sharedMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
        this.rdr_Liquid.sharedMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
        
        // 冰块
        this.mesh_Ice = mesh_Ice;
        this.mat_Ice = new Material(mat_Ice);
        this.mat_Ice.enableInstancing = true;

        // 混合
        this.mat_Merge = mat_RFMerge;
        InitFullScreenMesh();
    }

    // 初始化全屏三角面
    private static void InitFullScreenMesh()
    {
        float nearClipZ = -1;
        if (SystemInfo.usesReversedZBuffer)
            nearClipZ = 1;
        
        if (!s_TriangleMesh)
        {
            s_TriangleMesh = new Mesh();
            s_TriangleMesh.vertices = GetFullScreenTriangleVertexPosition(nearClipZ);
            s_TriangleMesh.uv = GetFullScreenTriangleTexCoord();
            s_TriangleMesh.triangles = new int[3] { 0, 1, 2 };
        }
        
        // Should match Common.hlsl
        static Vector3[] GetFullScreenTriangleVertexPosition(float z /*= UNITY_NEAR_CLIP_VALUE*/)
        {
            var r = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                Vector2 uv = new Vector2((i << 1) & 2, i & 2);
                r[i] = new Vector3(uv.x * 2.0f - 1.0f, uv.y * 2.0f - 1.0f, z);
            }
            return r;
        }

        // Should match Common.hlsl
        static Vector2[] GetFullScreenTriangleTexCoord()
        {
            var r = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                if (SystemInfo.graphicsUVStartsAtTop)
                    r[i] = new Vector2((i << 1) & 2, 1.0f - (i & 2));
                else
                    r[i] = new Vector2((i << 1) & 2, i & 2);
            }
            return r;
        }
    }
    
    // 刷新冰块变换矩阵
    public void RefreshIceMatrix(List<Rigidbody> rigid_Ice)
    {
        if (rigid_Ice != null)
        {
            int iceNum = rigid_Ice.Count;
            if (matrix_Ice == null || matrix_Ice.Length != iceNum)
            {
                matrix_Ice = new Matrix4x4[iceNum];
            }
            for (int i = 0; i < iceNum; i++)
            {
                matrix_Ice[i] = rigid_Ice[i].transform.localToWorldMatrix;
            }
        }
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // 缓存纹理格式
        ScriptableRenderer sRdr = renderingData.cameraData.renderer;
        RenderTextureDescriptor colorDesc = sRdr.cameraColorTargetHandle.rt.descriptor;
        RenderTextureDescriptor depthDesc = sRdr.cameraDepthTargetHandle.rt.descriptor;

        // 场景
        RenderingUtils.ReAllocateIfNeeded(ref handle_SceneColor, colorDesc);
        RenderingUtils.ReAllocateIfNeeded(ref handle_SceneDepth, depthDesc);
        
        // 液体
        colorDesc.colorFormat = RenderTextureFormat.ARGBHalf;   // 液体&冰块包含透明度信息, 且HDR(不能用带norm的格式)
        RenderingUtils.ReAllocateIfNeeded(ref handle_LiquidColor, colorDesc);
        RenderingUtils.ReAllocateIfNeeded(ref handle_LiquidDepth, depthDesc);
        
        // 冰块
        colorDesc.colorFormat = RenderTextureFormat.RGBAUShort;  // 每通道16位无符号整型纹理, 配合pack
        RenderingUtils.ReAllocateIfNeeded(ref handle_IceColor, colorDesc);
        RenderingUtils.ReAllocateIfNeeded(ref handle_IceDepth, depthDesc);
    }

    private static readonly ProfilingSampler profilingSampler_Scene = new("LiquidBottleRPass_Scene");
    private static readonly ProfilingSampler profilingSampler_Liquid = new("LiquidBottleRPass_Liquid");
    private static readonly ProfilingSampler profilingSampler_Ice = new("LiquidBottleRPass_Ice");
    private static readonly ProfilingSampler profilingSampler_Merge = new("LiquidBottleRPass_Merge");
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // 缓存
        CommandBuffer cmd = CommandBufferPool.Get();
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
            Blitter.BlitColorAndDepth(cmd, 
                sRdr_Camera.cameraColorTargetHandle, sRdr_Camera.cameraDepthTargetHandle, 
                viewportScale, 0, true);
        }
        mat_Merge.SetTexture(id_SceneColorBuffer, handle_SceneColor);
        mat_Merge.SetTexture(id_SceneDepthBuffer, handle_SceneDepth);
        
        // 液体
        if (rdr_Liquid)
        {
            using (new ProfilingScope(cmd, profilingSampler_Liquid))
            {
                // cmd.DrawRenderer绘制时, shader取到的包围盒坐标不正确, 所以从外部传
                Vector3 posWS_Origin = (rdr_Liquid.bounds.min + rdr_Liquid.bounds.max) * 0.5f;
                posWS_Origin.y = rdr_Liquid.bounds.min.y;
                rdr_Liquid.sharedMaterial.SetVector(id_RelativeOriginAndFlag,
                    new Vector4(posWS_Origin.x, posWS_Origin.y, posWS_Origin.z, 1));
                
                // 绘制
                CoreUtils.SetRenderTarget(cmd, handle_LiquidColor, handle_LiquidDepth, ClearFlag.All, Color.clear);
                cmd.DrawRenderer(rdr_Liquid, rdr_Liquid.sharedMaterial);
            }
            mat_Merge.SetTexture(id_LiquidColorBuffer, handle_LiquidColor);
            mat_Merge.SetTexture(id_LiquidDepthBuffer, handle_LiquidDepth);
        }
        
        // instance绘制冰块
        if (mesh_Ice && mat_Ice && matrix_Ice != null && matrix_Ice.Length > 0)
        {
            using (new ProfilingScope(cmd, profilingSampler_Ice))
            {
                CoreUtils.SetRenderTarget(cmd, handle_IceColor, handle_IceDepth, ClearFlag.All, Color.clear);
                cmd.DrawMeshInstanced(mesh_Ice, 0, mat_Ice, 1/*用第二个pass*/, matrix_Ice);
            }
            mat_Merge.SetTexture(id_IceColorBuffer, handle_IceColor);
            mat_Merge.SetTexture(id_IceDepthBuffer, handle_IceDepth);
        }
        
        // 混合
        using (new ProfilingScope(cmd, profilingSampler_Merge))
        {
            CoreUtils.SetRenderTarget(cmd, sRdr_Camera.cameraColorTargetHandle, sRdr_Camera.cameraDepthTargetHandle, ClearFlag.All, Color.clear);
            
            // 设置BlitColorAndDepth.hlsl的内置材质参数
            mat_Merge.SetFloat(_BlitMipLevel, 0);
            mat_Merge.SetVector(_BlitScaleBias, viewportScale);
        
            // 绘制全屏三角形
            if (SystemInfo.graphicsShaderLevel < 30)
                cmd.DrawMesh(s_TriangleMesh, Matrix4x4.identity, mat_Merge, 0, 0);
            else
                cmd.DrawProcedural(Matrix4x4.identity, mat_Merge, 0, MeshTopology.Triangles, 3, 1);
        }
        
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }
    
    public void Dispose()
    {
        this.rdr_Liquid.sharedMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        this.rdr_Liquid.sharedMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        
        handle_SceneColor?.Release();
        handle_SceneDepth?.Release();
        
        handle_LiquidColor?.Release();
        handle_LiquidDepth?.Release();
        
        handle_IceColor?.Release();
        handle_IceDepth?.Release();
        
        CoreUtils.Destroy(mat_Ice);
        mat_Ice = null;
        
        CoreUtils.Destroy(s_TriangleMesh);
        s_TriangleMesh = null;
    }
}