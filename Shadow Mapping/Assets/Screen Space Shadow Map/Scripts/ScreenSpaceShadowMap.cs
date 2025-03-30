using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Camera))]
public class ScreenSpaceShadowMap : MonoBehaviour {
    // 【控制Shader】
    public Material shadowCasterMat = null;     // 渲染深度图
    public Material shadowCollectorMat = null;  // 后处理中合成阴影图（Bilt）

    // 【光源相机】
    public GameObject _light;
    public static Camera _lightCamera;          // 光源相机
    RenderTexture lightDepthTexture = null;     // 光源相机的深度图
    public float orthographicsSize = 6f;
    public float nearClipPlane = 0.3f;
    public float farClipPlane = 20f;

    // 【主相机】
    public static Camera  _depthCamera;         // 主相机
    RenderTexture depthTexture = null;          // 主相机视角深度图

    // 【屏幕空间阴影图】
    RenderTexture screenSpaceShadowTexture = null; // 屏幕空间阴影图

    public int quality = 2;     // 控制贴图分辨率

    [Range(0,1)]
    public float shadowBias = 0.05f;
    [Range(0,1)]
    public float shadowStrength = 0.9f;

    // 资源清理
    void OnDestroy()
    {
        _depthCamera = null;
        _lightCamera = null;
        if (depthTexture) DestroyImmediate(lightDepthTexture);
        if (lightDepthTexture) DestroyImmediate(lightDepthTexture);
        if (screenSpaceShadowTexture)DestroyImmediate(screenSpaceShadowTexture);
    }
    
    public void Update()
    {
        if (shadowCasterMat == null || shadowCollectorMat == null) return;

        // 【分别从主视角和光源视角生成深度图】
        if (!_depthCamera)  _depthCamera = CreateDepthCamera();
        _depthCamera.RenderWithShader(shadowCasterMat.shader, "");

        if (!_lightCamera)   _lightCamera = CreateLightCamera();
        _lightCamera.RenderWithShader(shadowCasterMat.shader, "");

        // 后处理阴影贴图
        if (screenSpaceShadowTexture == null)
        {   // （这里第3个参数是0，指 不申请深度缓冲区，仅创建颜色图）
            screenSpaceShadowTexture = new RenderTexture(Screen.width * quality, Screen.height * quality, 0, RenderTextureFormat.Default);
            screenSpaceShadowTexture.hideFlags = HideFlags.DontSave;
        }

        // 获取主摄的逆矩阵，用于从屏幕空间重建世界坐标
        Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false);
        Shader.SetGlobalMatrix("_inverseVP", Matrix4x4.Inverse(projectionMatrix * Camera.main.worldToCameraMatrix));

        // 【Blit合成屏幕空间阴影】
        shadowCollectorMat.SetTexture("_CameraDepthTex", depthTexture);
        shadowCollectorMat.SetTexture("_LightDepthTex", lightDepthTexture);
        shadowCollectorMat.SetFloat("_shadowBias", shadowBias);
        shadowCollectorMat.SetFloat("_shadowStrength", 1 - shadowStrength);
        Graphics.Blit(depthTexture, screenSpaceShadowTexture, shadowCollectorMat);

        // 【传递结果】供主Shader使用
        Shader.SetGlobalTexture("_CameraDepthTex", depthTexture);
        Shader.SetGlobalTexture("_LightDepthTex", lightDepthTexture);
        Shader.SetGlobalTexture("_ScreenSpceShadowTexture", screenSpaceShadowTexture);
        projectionMatrix = GL.GetGPUProjectionMatrix(_lightCamera.projectionMatrix, false);
        Shader.SetGlobalMatrix("_WorldToShadow", projectionMatrix * _lightCamera.worldToCameraMatrix);
    }

#region 函数

    // 主摄角度
    public Camera CreateDepthCamera()
    {
        GameObject goDepthCamera = new GameObject("Depth Camera");
        Camera depthCamera = goDepthCamera.AddComponent<Camera>();

        depthCamera.CopyFrom(Camera.main);
        depthCamera.backgroundColor = Color.white;
        depthCamera.clearFlags = CameraClearFlags.SolidColor;
        depthCamera.enabled = false;

        if (!depthCamera.targetTexture)
            depthCamera.targetTexture = depthTexture = CreateTextureFor();

        Shader.SetGlobalTexture("_DepthTexture", depthTexture);
        depthCamera.transform.parent = Camera.main.transform;
        depthCamera.transform.localPosition = Vector3.zero;
        depthCamera.transform.localRotation = Quaternion.identity;

        return depthCamera;
    }

    // 光源角度
    public Camera CreateLightCamera()
    {
        GameObject goLightCamera = new GameObject("Shadow Camera");
        Camera LightCamera = goLightCamera.AddComponent<Camera>();

        LightCamera.backgroundColor = Color.white;
        LightCamera.clearFlags = CameraClearFlags.SolidColor;
        LightCamera.orthographic = true;
        LightCamera.orthographicSize = orthographicsSize;
        LightCamera.nearClipPlane = nearClipPlane;
        LightCamera.farClipPlane = farClipPlane;
        LightCamera.enabled = false;

        if (!LightCamera.targetTexture)
            LightCamera.targetTexture = lightDepthTexture = CreateTextureFor();
        
        LightCamera.transform.parent = _light.transform;
        LightCamera.transform.localPosition = Vector3.zero;
        LightCamera.transform.localRotation = Quaternion.identity;

        return LightCamera;
    }

    // 创建渲染材质RT
    private RenderTexture CreateTextureFor()
    {
        RenderTexture rt = new RenderTexture(Screen.width * quality, Screen.height * quality, 24, RenderTextureFormat.Default);
        rt.hideFlags = HideFlags.DontSave;        

        return rt;
    }

#endregion
}
