using System.Collections;
using System.Collections.Generic;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class ShadowMappingEX : MonoBehaviour
{
    public enum CustomShadowResolution{
        Low = 256,Medium = 512,
        High = 1024,Ultra = 2048,
    }
    public enum ShadowsType{
        NONE,HARD,PCF,PCSS,VSM,VSSM,MOMENT
    }

    [Header("默认设置")]
    [SerializeField]    //可见并编辑
    Shader _depthShader;
    [SerializeField]   
    ComputeShader _blur;

    public Light dirLight;

    [Header("阴影设置")]
    [SerializeField]
    CustomShadowResolution _resolution = CustomShadowResolution.High;
    public FilterMode _filterMode = FilterMode.Bilinear;
    public ShadowsType _shadowsType = ShadowsType.HARD;
    [Range(0,1)]
    public float shadowStrength = 0.6f;
    [Range(0,2)]
    public float shadowBias = 0.05f;
    public bool drawTransparent =true;

    [Header("VSM参数")]

    [Range(0,1)]
    public float varianceShadowExpansion = 0.3f;
    [Range(0,100)]
    public int varianceBlurIterations = 1;

    [Header("PCSS参数")]
    // 搜索 blocker 的半径
    public float PCSSFilterScale = 4.0f;
    // 模糊范围缩放
    public float PCSSSearchRadius = 20.0f;


    // 渲染目标
    Camera _shadowCam;
    RenderTexture _backTarget;  //辅助渲染纹理
    RenderTexture _target;      //主纹理渲染

    void Update(){

        _depthShader =  _depthShader ? _depthShader : Shader.Find("ShadowMapping/ShadowCaster");
        SetUpShadowCam();
        UpdateRenderTexture();
        UpdateShadowCameraPosition();
        
        _shadowCam.targetTexture = _target;
        // 摄像机渲染时，_depthShader就会执行
        _shadowCam.RenderWithShader(_depthShader, "");

        //VSM和VSSM的高斯模糊处理
        // if (_shadowsType == ShadowsType.VSM || _shadowsType == ShadowsType.VSSM)
            for(int i = 0; i < varianceBlurIterations; i++){
                _blur.SetTexture(0, "Read", _target);
                _blur.SetTexture(0, "Result", _backTarget);
                // Dispatch() 调用 computeShader
                _blur.Dispatch(0, _target.width / 8, _target.height / 8, 1);
                Swap(ref _backTarget, ref _target);
            }

        UpdateShaderArgs();
    }

    void OnDestroy()
    {
        OnDisable();
    }

    #region 函数

    // 设置阴影相机
    void SetUpShadowCam(){
        if(_shadowCam)  return;
        GameObject go = new GameObject("Shadow Camera");
        go.hideFlags = HideFlags.DontSave;

        _shadowCam = go.AddComponent<Camera>();
        _shadowCam.orthographic = true;
        _shadowCam.nearClipPlane = 0;
        _shadowCam.enabled = false;
        _shadowCam.backgroundColor = new Color(0, 0, 0, 0);
        _shadowCam.clearFlags = CameraClearFlags.SolidColor;
    }

    // 更新渲染纹理
    void UpdateRenderTexture(){
        if(_target != null && (_target.width != (int)_resolution || _target.filterMode != _filterMode)){
            DestroyImmediate(_target);
            _target = null;
        }

        if(_target == null){
            _target = CreateTarget();
            _backTarget = CreateTarget();
        }
    }

    // 创建渲染目标
    RenderTexture CreateTarget(){
        RenderTexture target = new RenderTexture((int)_resolution,(int)_resolution,24, RenderTextureFormat.RGFloat);
        target.filterMode = _filterMode;
        target.wrapMode = TextureWrapMode.Clamp;
        target.enableRandomWrite = true;
        target.Create();
        return target;
    }

    // 更新阴影相机位置 
    void UpdateShadowCameraPosition(){
        Camera cam = _shadowCam;
        if(dirLight == null){
            dirLight = FindObjectOfType<Light>();
        }

        cam.transform.position = dirLight.transform.position;
        cam.transform.rotation = dirLight.transform.rotation;
        cam.transform.LookAt(cam.transform.position + cam.transform.forward, cam.transform.up);

        // 计算场景包围盒
        Vector3 center, extents;
        List<Renderer> renderers = new List<Renderer>();
        renderers.AddRange(FindObjectsOfType<Renderer>());  // 返回所有激活的Renderer组件

        GetRenderersExtents(renderers, cam.transform, out center, out extents);

        // 确保相机视锥体完全包含所有物体
        center.z -= extents.z / 2;
        cam.transform.position = cam.transform.TransformPoint(center);
        cam.nearClipPlane = 0;
        cam.farClipPlane = extents.z;

        // 设置正交视角参数
        cam.aspect = extents.x / extents.y;
        cam.orthographicSize = extents.y / 2;
    }

    // - 计算场景包围盒
    void GetRenderersExtents(List<Renderer> renderers, Transform frame, out Vector3 center, out Vector3 extents)
    {
        Vector3[] arr = new Vector3[8];

        Vector3 min = Vector3.one * Mathf.Infinity;
        Vector3 max = Vector3.one * Mathf.NegativeInfinity;
        foreach (var r in renderers)
        {
            GetBoundsPoints(r.bounds, arr, frame.worldToLocalMatrix);

            foreach (var p in arr)
            {
                for (int i = 0; i < 3; i++)
                {
                    min[i] = Mathf.Min(p[i], min[i]);
                    max[i] = Mathf.Max(p[i], max[i]);
                }
            }
        }

        extents = max - min;
        center = (max + min) / 2;
    }

    void GetBoundsPoints(Bounds b, Vector3[] points, Matrix4x4? mat = null)
    {
        Matrix4x4 trans = mat ?? Matrix4x4.identity;

        int count = 0;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 v = b.extents;
                    v.x *= x;
                    v.y *= y;
                    v.z *= z;
                    v += b.center;
                    v = trans.MultiplyPoint(v);

                    points[count++] = v;
                }
    }

    // 更新阴影参数
    void UpdateShaderArgs(){
        // 指定关键字
        ForAllKeywords(s => Shader.DisableKeyword(ToKeyword(s)));
        Shader.EnableKeyword(ToKeyword(_shadowsType));

        // 传递全局参数
        Shader.SetGlobalTexture("_ShadowTex", _target);
        Shader.SetGlobalMatrix("_LightMatrix", _shadowCam.transform.worldToLocalMatrix);
        Shader.SetGlobalFloat("_MaxShadowIntensity", shadowStrength);
        Shader.SetGlobalFloat("_VarianceShadowExpansion", varianceShadowExpansion);
        Shader.SetGlobalFloat("_ShadowBias", shadowBias);
        Shader.SetGlobalFloat("_PCSSFilterScale",PCSSFilterScale);
        Shader.SetGlobalFloat("_PCSSSearchRadius",PCSSSearchRadius);

        // 透明物体处理
        if (drawTransparent) Shader.EnableKeyword("DRAW_TRANSPARENT_SHAaDOWS");
        else Shader.DisableKeyword("DRAW_TRANSPARENT_SHADOWS");

        // 传递阴影贴图尺寸
        Vector4 size = Vector4.zero;
        size.y = _shadowCam.orthographicSize * 2;
        size.x = _shadowCam.aspect * size.y;
        size.z = _shadowCam.farClipPlane;
        size.w = 1.0f /(int) _resolution;
        Shader.SetGlobalVector("_ShadowTexScale", size);

        // ComputeShader参数
        _blur.SetInts("_TextureSize", _target.width, _target.height);
        
    }

    // - 阴影类型
    void ForAllKeywords(System.Action<ShadowsType> func)
    {
        // 遍历所有阴影类型​（HARD, PCF, VARIANCE, MOMENT），并对每种类型执行传入的 func 操作。
        // 用于批量启用/禁用关键字
        func(ShadowsType.HARD);
        func(ShadowsType.PCF);
        func(ShadowsType.PCSS);
        func(ShadowsType.VSM);
        func(ShadowsType.VSSM);
        func(ShadowsType.MOMENT);
    }

    // - 将阴影类型转换为关键字
    string ToKeyword(ShadowsType shadowType)
    {
        if (shadowType == ShadowsType.HARD) return "HARD_SHADOWS";
        if (shadowType == ShadowsType.PCF) return "PCF_SHADOWS";
        if (shadowType == ShadowsType.PCSS) return "PCSS_SHADOWS";
        if (shadowType == ShadowsType.VSM) return "VSM_SHADOWS";
        if (shadowType == ShadowsType.VSSM) return "VSSM_SHADOWS";
        if (shadowType == ShadowsType.MOMENT) return "MOMENT_SHADOWS";
        return "";
    }

    // 释放内存
    void OnDisable()
    {
        if (_shadowCam)
        {
            DestroyImmediate(_shadowCam.gameObject);
            _shadowCam = null;
        }

        if (_target)
        {
            DestroyImmediate(_target);
            _target = null;
        }

        if (_backTarget)
        {
            DestroyImmediate(_backTarget);
            _backTarget = null;
        }

        ForAllKeywords(s => Shader.DisableKeyword(ToKeyword(s)));
    }

    // 交换
    void Swap<T>(ref T a, ref T b){
        T temp = a;
        a = b;
        b = temp;
    }

    #endregion
}
