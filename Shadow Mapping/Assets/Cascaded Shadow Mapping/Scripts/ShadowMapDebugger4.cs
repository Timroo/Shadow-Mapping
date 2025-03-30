using UnityEngine;
using UnityEditor;


[ExecuteAlways]
public class ShadowMapDebugger4 : MonoBehaviour
{
    [Header("显示设置")]
    [Range(0f, 1f)] public float size = 0.25f;  // 每张贴图占屏幕比例
    public Vector2 anchor = new Vector2(1f, 1f); // 右上角为起点
    public string[] globalTextureNames = new string[]
    {
        "_gShadowMapTexture0",
        "_gShadowMapTexture1",
        "_gShadowMapTexture2",
        "_gShadowMapTexture3"
    };

    private void OnGUI()
    {
        float texWidth = Screen.width * size;
        float texHeight = Screen.height * size;

        int columns = 2;
        int rows = 2;

        for (int i = 0; i < globalTextureNames.Length; i++)
        {
            string texName = globalTextureNames[i];
            var tex = Shader.GetGlobalTexture(texName);
            if (tex == null) continue;

            int col = i % columns;
            int row = i / columns;

            // 排列方式：右上角开始，从左到右，从上到下
            float x = anchor.x * Screen.width + (col - columns) * texWidth;
            float y = anchor.y * Screen.height + row * texHeight - (rows * texHeight);

            GUI.DrawTexture(new Rect(x, y, texWidth, texHeight), tex, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(x, y, texWidth, 20), texName, EditorGUIUtility.isProSkin ? EditorStyles.whiteLabel : EditorStyles.boldLabel);
        }
    }
}
