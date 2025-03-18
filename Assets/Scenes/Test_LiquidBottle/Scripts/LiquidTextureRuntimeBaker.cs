using UnityEngine;

public static class LiquidTextureRuntimeBaker_CS
{
    private static readonly ComputeShader cs_Static = Resources.Load<ComputeShader>("CS_LiquidBottle");
    private static readonly int kID_AverageMask = cs_Static.FindKernel("AverageMask");
    private static readonly int kID_LerpMask = cs_Static.FindKernel("LerpMask");
    
    private static readonly int id_SrcMaskTex2DArr = Shader.PropertyToID("_SrcMaskTex2DArr");
    private static readonly int id_DstMaskTex2D = Shader.PropertyToID("_DstMaskTex2D");
    private static readonly int id_OutMaskTex2DArr = Shader.PropertyToID("_OutMaskTex2DArr");
    private static readonly int id_OutTex2D = Shader.PropertyToID("_OutTex2D"); 
    private static readonly int id_LayerNum = Shader.PropertyToID("_LayerNum");
    private static readonly int id_Lerp01 = Shader.PropertyToID("_Lerp01");

    // 求平均
    public static void AverageMask(RenderTexture out2D, RenderTexture src2DArr, int layerNum)
    {
        ComputeShader cs = ComputeShader.Instantiate(cs_Static);
        
        uint tSize_X, tSize_Y, tSize_Z;
        cs.GetKernelThreadGroupSizes(kID_AverageMask, out tSize_X, out tSize_Y, out tSize_Z);
        Vector3Int gSize = new Vector3Int(
            Mathf.CeilToInt(out2D.width / (float) tSize_X),
            Mathf.CeilToInt(out2D.height / (float) tSize_Y),
            1
        );
        
        cs.SetInt(id_LayerNum, layerNum);
        cs.SetTexture(kID_AverageMask, id_OutTex2D, out2D);
        cs.SetTexture(kID_AverageMask, id_SrcMaskTex2DArr, src2DArr);
        cs.Dispatch(kID_AverageMask, gSize.x, gSize.y, gSize.z);
        
        ComputeShader.Destroy(cs);
    }
    
    // 插值
    public static void LerpMask(RenderTexture out2DArr, RenderTexture src2DArr, Texture dst2D, float lerp01)
    {
        ComputeShader cs = ComputeShader.Instantiate(cs_Static);
        
        uint tSize_X, tSize_Y, tSize_Z;
        cs_Static.GetKernelThreadGroupSizes(kID_LerpMask, out tSize_X, out tSize_Y, out tSize_Z);
        Vector3Int gSize = new Vector3Int(
            Mathf.CeilToInt(out2DArr.width / (float) tSize_X),
            Mathf.CeilToInt(out2DArr.height / (float) tSize_Y),
            Mathf.CeilToInt(out2DArr.volumeDepth / (float) tSize_Z)
        );
        
        cs.SetTexture(kID_LerpMask, id_OutMaskTex2DArr, out2DArr);
        cs.SetTexture(kID_LerpMask, id_SrcMaskTex2DArr, src2DArr);
        cs.SetTexture(kID_LerpMask, id_DstMaskTex2D, dst2D);
        cs.SetFloat(id_Lerp01, lerp01);
        cs.Dispatch(kID_LerpMask, gSize.x, gSize.y, gSize.z);
        
        ComputeShader.Destroy(cs);
    }
}