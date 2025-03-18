using UnityEngine;

[CreateAssetMenu(menuName = "自定义资源/Liquid")]
public class Liquid : ScriptableObject
{
    public Color RGBA;                          // 颜色 & 不透明度
    public float bubbleIntensity;               // 泡沫强度
    public Texture2D maskTex;                   // 静态细节遮罩纹理
    [Range(0, 0.5f)] public float lerpRange;    // 向下层的过渡宽度

    public Liquid(Liquid liquidIN)
    {
        RGBA = liquidIN.RGBA;
        maskTex = liquidIN.maskTex;
        bubbleIntensity = liquidIN.bubbleIntensity;
        lerpRange = liquidIN.lerpRange;
    }
}
