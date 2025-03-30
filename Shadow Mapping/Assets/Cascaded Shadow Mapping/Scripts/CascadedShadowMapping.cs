using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class CascadedShadowMapping : MonoBehaviour
{
    public Light dirLight;
    Camera dirLightCamera;

    [Header("Cascade 分布比例（相对于 Far）")]
    [Tooltip("值必须递增，范围 [0,1]")]
    public float[] cascadeSplits = new float[3] { 0.067f, 0.2f, 0.467f };
    public Shader shadowCaster = null;

    // w2s矩阵 从世界空间到各个级联阴影贴图空间的变换矩阵
    List<Matrix4x4> world2ShadowMats = new List<Matrix4x4>(4);
    // 各个级联的阴影相机
    GameObject[] dirLightCameraSplits = new GameObject[4];
    RenderTexture[] depthTextures = new RenderTexture[4];
    public int RT_Resolution = 1024;

    [Range(0,1)]
    public float ShadowBias = 0.01f;

    [Range(0,1)]
    public float ShadowStrength = 1f;


 void OnDestroy(){   // 清理资源
        dirLightCamera = null;
        for(int i = 0; i < 4; i++){
            if(depthTextures[i]){
                DestroyImmediate(depthTextures[i]);
            }
        }
    }
    void Awake(){
        // 创建视锥体角点数组
        InitFrustumCorners();
    }

    
    private void Update()
    {
        // 计算主摄视锥体划分
        CalcMainCameraSplitsFrustumCorners();
        // 计算光源相机的视锥体划分
        CalcLightCameraSplitsFrustum();

        // 更新光源相机和渲染纹理
        if (dirLight){
            if(!dirLightCamera){
                dirLightCamera = CreateDirLightCamera();
                CreateRenderTexture();
            }
        }

        Shader.SetGlobalFloat("_gShadowBias", ShadowBias);
        Shader.SetGlobalFloat("_gShadowStrength", 1 - ShadowStrength);

        world2ShadowMats.Clear();

        // 渲染阴影贴图
        for (int i = 0; i < 4; i++){
            // 设置当前级联的光源相机
            ConstructLightCameraSplits(i);

            dirLightCamera.targetTexture = depthTextures[i];
            dirLightCamera.RenderWithShader(shadowCaster, "");

            // 计算 世界到shadow map空间 的 变换矩阵
            // - 获取适配 Unity 的平台的投影矩阵
            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(dirLightCamera.projectionMatrix, false);
            // - 世界空间 - 相机空间 - 投影空间
            world2ShadowMats.Add(projectionMatrix * dirLightCamera.worldToCameraMatrix);
        }

        Shader.SetGlobalMatrixArray("_gWorld2Shadow", world2ShadowMats);
    }
#region 函数
    
    // 视锥体角点变量
    float[] _LightSplitsNear;
    float[] _LightSplitsFar;
    struct FrustumCorners{
        public Vector3[] nearCorners;
        public Vector3[] farCorners;
    }
    FrustumCorners[] mainCamera_Splits_fcs;
    FrustumCorners[] lightCamera_Splits_fcs;
    void InitFrustumCorners(){ // 创建视锥体角点数组
        mainCamera_Splits_fcs = new FrustumCorners[4];
        lightCamera_Splits_fcs = new FrustumCorners[4];
        for(int i = 0; i < 4; i++){
            mainCamera_Splits_fcs[i].nearCorners = new Vector3[4];
            mainCamera_Splits_fcs[i].farCorners = new Vector3[4];

            lightCamera_Splits_fcs[i].nearCorners = new Vector3[4];
            lightCamera_Splits_fcs[i].farCorners = new Vector3[4];
        }
    }

    void CalcMainCameraSplitsFrustumCorners(){// 计算主摄视锥体划分
        float near = Camera.main.nearClipPlane;
        float far = Camera.main.farClipPlane;

        // 第一级联: 主摄像机的近裁剪面。
        // 第二级联：远裁剪面的 6.7% 加上近裁剪面。
        // 第三级联：远裁剪面的 13.3% 加上前面级联的部分再加上近裁剪面。
        // 第四级联：远裁剪面的 26.7% 加上前两个级联的部分再加上近裁剪面。
        float[] nears = {   near, 
                            far * 0.067f + near, 
                            far * 0.133f + far * 0.067f + near, 
                            far * 0.267f + far * 0.133f + far * 0.067f + near };

        float[] fars = {    far * 0.067f + near, 
                            far * 0.133f + far * 0.067f + near, 
                            far * 0.267f + far * 0.133f + far * 0.067f + near, 
                            far };

        // 这些变量用于在shader中判断当前像素属于哪个级联区域
        _LightSplitsNear = nears;
        _LightSplitsFar = fars;
        Shader.SetGlobalVector("_gLightSplitsNear", new Vector4(_LightSplitsNear[0], _LightSplitsNear[1], _LightSplitsNear[2], _LightSplitsNear[3]));
        Shader.SetGlobalVector("_gLightSplitsFar", new Vector4(_LightSplitsFar[0], _LightSplitsFar[1], _LightSplitsFar[2], _LightSplitsFar[3]));

        // 计算主摄视锥体每个级联的近裁剪面和远裁剪面的四个角点
        // - 并将这些角点从摄像机的局部空间转换到世界空间。
        for(int k = 0; k < 4; k++){
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), _LightSplitsNear[k], Camera.MonoOrStereoscopicEye.Mono, mainCamera_Splits_fcs[k].nearCorners);
            for(int i = 0; i < 4; i++){
                // 从相机空间转换到世界空间
                mainCamera_Splits_fcs[k].nearCorners[i] = Camera.main.transform.TransformPoint(mainCamera_Splits_fcs[k].nearCorners[i]);
            }
            Camera.main.CalculateFrustumCorners(new Rect(0, 0, 1, 1), _LightSplitsFar[k], Camera.MonoOrStereoscopicEye.Mono, mainCamera_Splits_fcs[k].farCorners);
            for(int i = 0; i < 4; i++){
                mainCamera_Splits_fcs[k].farCorners[i] = Camera.main.transform.TransformPoint(mainCamera_Splits_fcs[k].farCorners[i]);
            }
        }
    }

    // crossDis缓存 对角线距离
    
    void CalcLightCameraSplitsFrustum(){// 计算光源相机的视锥体划分
        if (dirLightCamera == null) return;

        for(int k = 0; k < 4; k++){
            // 将主摄像机角点转换到光源空间(从世界空间转换到光源相机空间)
            for(int i = 0; i < 4; i++){
                lightCamera_Splits_fcs[k].nearCorners[i] = dirLightCameraSplits[k].transform.InverseTransformPoint(mainCamera_Splits_fcs[k].nearCorners[i]);
                lightCamera_Splits_fcs[k].farCorners[i] = dirLightCameraSplits[k].transform.InverseTransformPoint(mainCamera_Splits_fcs[k].farCorners[i]);
            }
            
            // 计算光源相机的包围盒范围
            float[] xs = { lightCamera_Splits_fcs[k].nearCorners[0].x,
                            lightCamera_Splits_fcs[k].nearCorners[1].x,
                            lightCamera_Splits_fcs[k].nearCorners[2].x,
                            lightCamera_Splits_fcs[k].nearCorners[3].x,
                            lightCamera_Splits_fcs[k].farCorners[0].x, 
                            lightCamera_Splits_fcs[k].farCorners[1].x, 
                            lightCamera_Splits_fcs[k].farCorners[2].x, 
                            lightCamera_Splits_fcs[k].farCorners[3].x };
            
            float[] ys = { lightCamera_Splits_fcs[k].nearCorners[0].y, lightCamera_Splits_fcs[k].nearCorners[1].y, lightCamera_Splits_fcs[k].nearCorners[2].y, lightCamera_Splits_fcs[k].nearCorners[3].y,
                       lightCamera_Splits_fcs[k].farCorners[0].y, lightCamera_Splits_fcs[k].farCorners[1].y, lightCamera_Splits_fcs[k].farCorners[2].y, lightCamera_Splits_fcs[k].farCorners[3].y };

            float[] zs = { lightCamera_Splits_fcs[k].nearCorners[0].z, lightCamera_Splits_fcs[k].nearCorners[1].z, lightCamera_Splits_fcs[k].nearCorners[2].z, lightCamera_Splits_fcs[k].nearCorners[3].z,
                       lightCamera_Splits_fcs[k].farCorners[0].z, lightCamera_Splits_fcs[k].farCorners[1].z, lightCamera_Splits_fcs[k].farCorners[2].z, lightCamera_Splits_fcs[k].farCorners[3].z };

            float minX = Mathf.Min(xs);
            float maxX = Mathf.Max(xs);

            float minY = Mathf.Min(ys);
            float maxY = Mathf.Max(ys);

            float minZ = Mathf.Min(zs);
            float maxZ = Mathf.Max(zs);

            // 更新光源相机的角点
            // - 近平面Z始终是最小的
            lightCamera_Splits_fcs[k].nearCorners[0] = new Vector3(minX, minY, minZ);
            lightCamera_Splits_fcs[k].nearCorners[1] = new Vector3(maxX, minY, minZ);
            lightCamera_Splits_fcs[k].nearCorners[2] = new Vector3(maxX, maxY, minZ);
            lightCamera_Splits_fcs[k].nearCorners[3] = new Vector3(minX, maxY, minZ);
            // - 远平面Z始终是最大的
            lightCamera_Splits_fcs[k].farCorners[0] = new Vector3(minX, minY, maxZ);
            lightCamera_Splits_fcs[k].farCorners[1] = new Vector3(maxX, minY, maxZ);
            lightCamera_Splits_fcs[k].farCorners[2] = new Vector3(maxX, maxY, maxZ);
            lightCamera_Splits_fcs[k].farCorners[3] = new Vector3(minX, maxY, maxZ);

            // （3）技巧： Texel-Snapping 纹素对齐处理 
            // 计算最AABB最长的一段
            // Vector3.Magnitude() 计算模长
            float crossDis = Vector3.Magnitude(lightCamera_Splits_fcs[k].farCorners[2] - lightCamera_Splits_fcs[k].nearCorners[0]);
            // - 锁定光源相机位置，使阴影纹理与世界空间对齐，实现阴影稳定性
            float unitPerpixel = crossDis / depthTextures[k].width;   // 计算1像素对应的世界空间单位
            // 取near平面的中心点
            Vector3 pos = lightCamera_Splits_fcs[k].nearCorners[0] + (lightCamera_Splits_fcs[k].nearCorners[2] - lightCamera_Splits_fcs[k].nearCorners[0]) * 0.5f;
            // 像素对齐:将光源相机的位置对齐到 unitPerPixel 的整数倍。 
            pos.x = Mathf.Floor(pos.x / unitPerpixel) * unitPerpixel;
            pos.y = Mathf.Floor(pos.y / unitPerpixel) * unitPerpixel;
            
            // 设置光源相机的位置和方向
            dirLightCameraSplits[k].transform.position = dirLightCameraSplits[k].transform.TransformPoint(pos);
            dirLightCameraSplits[k].transform.rotation = dirLight.transform.rotation;  
        }
    }

    public Camera CreateDirLightCamera(){ //创建光源相机
        GameObject goLightCamera = new GameObject("Directional light Camera");
        Camera LightCamera = goLightCamera.AddComponent<Camera>();

        LightCamera.backgroundColor = Color.white;
        LightCamera.clearFlags = CameraClearFlags.SolidColor;
        LightCamera.orthographic = true;
        LightCamera.enabled = false; // 关闭相机

        for(int i = 0; i < 4; i++){
            dirLightCameraSplits[i] = new GameObject("dirLightCameraSplits" + i);
        }

        return LightCamera;
    }

    public void CreateRenderTexture(){ // 创建渲染纹理（为每个级联创建一个RenderTexture）
        RenderTextureFormat rtFormat = RenderTextureFormat.Default;

        for(int i = 0; i < 4; i++){
            depthTextures[i] = new RenderTexture(RT_Resolution, RT_Resolution, 24, rtFormat);
            Shader.SetGlobalTexture("_gShadowMapTexture" + i, depthTextures[i]);
            Shader.SetGlobalFloat("_gShadowMapTexture_TexelSize" + i, 1/RT_Resolution);
        }
    }

    private Dictionary<int, float> crossDisCached = new Dictionary<int, float>();
    void ConstructLightCameraSplits(int k){ // 构建光源相机
        dirLightCamera.transform.position = dirLightCameraSplits[k].transform.position;
        dirLightCamera.transform.rotation = dirLightCameraSplits[k].transform.rotation;

        dirLightCamera.nearClipPlane = lightCamera_Splits_fcs[k].nearCorners[0].z;
        dirLightCamera.farClipPlane = lightCamera_Splits_fcs[k].farCorners[0].z;
        
        //dirLightCamera.aspect = Vector3.Magnitude(lightCamera_Splits_fcs[k].nearCorners[0] - lightCamera_Splits_fcs[k].nearCorners[1]) / Vector3.Magnitude(lightCamera_Splits_fcs[k].nearCorners[1] - lightCamera_Splits_fcs[k].nearCorners[2]);
        dirLightCamera.aspect = 1;  //固定为正方形（更容易使贴图稳定）

        // 缓存每个 cascade 的最大投影尺寸，并且让它只增不减，防止阴影抖动（shadow shimmering）现象。
        // - 本质是一个“防闪抖（stabilize）策略”。
        float crossDis = Vector3.Magnitude(lightCamera_Splits_fcs[k].farCorners[2] - lightCamera_Splits_fcs[k].nearCorners[0]);
        float maxDis = -1;
        
        if(!crossDisCached.TryGetValue(k, out maxDis)){
            if(crossDis != 0){
                crossDisCached.Add(k, crossDis);
                maxDis = crossDis;
            }
        }
        else{
            if(crossDis > maxDis){
                crossDisCached[k] = crossDis;
            }
        }

        dirLightCamera.orthographicSize = crossDis * 0.5f;
    }

    void OnDrawGizmos(){
        if (dirLightCamera == null)
            return;

        FrustumCorners[] fcs = new FrustumCorners[4];
        for (int k = 0; k < 4; k++)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(mainCamera_Splits_fcs[k].nearCorners[1], mainCamera_Splits_fcs[k].nearCorners[2]);

            fcs[k].nearCorners = new Vector3[4];
            fcs[k].farCorners = new Vector3[4];

            for (int i = 0; i < 4; i++)
            {
                fcs[k].nearCorners[i] = dirLightCameraSplits[k].transform.TransformPoint(lightCamera_Splits_fcs[k].nearCorners[i]);
                fcs[k].farCorners[i] = dirLightCameraSplits[k].transform.TransformPoint(lightCamera_Splits_fcs[k].farCorners[i]);
            }

            Gizmos.color = Color.red;
            Gizmos.DrawLine(fcs[k].nearCorners[0], fcs[k].nearCorners[1]);
            Gizmos.DrawLine(fcs[k].nearCorners[1], fcs[k].nearCorners[2]);
            Gizmos.DrawLine(fcs[k].nearCorners[2], fcs[k].nearCorners[3]);
            Gizmos.DrawLine(fcs[k].nearCorners[3], fcs[k].nearCorners[0]);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(fcs[k].farCorners[0], fcs[k].farCorners[1]);
            Gizmos.DrawLine(fcs[k].farCorners[1], fcs[k].farCorners[2]);
            Gizmos.DrawLine(fcs[k].farCorners[2], fcs[k].farCorners[3]);
            Gizmos.DrawLine(fcs[k].farCorners[3], fcs[k].farCorners[0]);

            Gizmos.DrawLine(fcs[k].nearCorners[0], fcs[k].farCorners[0]);
            Gizmos.DrawLine(fcs[k].nearCorners[1], fcs[k].farCorners[1]);
            Gizmos.DrawLine(fcs[k].nearCorners[2], fcs[k].farCorners[2]);
            Gizmos.DrawLine(fcs[k].nearCorners[3], fcs[k].farCorners[3]);
        }
    }

#endregion
}
