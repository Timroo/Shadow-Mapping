Shader "ShadowMapping/ShadowReceiver" {
    Properties{
        _Color ("Color", Color) = (1,1,1,1)
    }

    SubShader {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ HARD_SHADOWS PCF_SHADOWS PCSS_SHADOWS VSM_SHADOWS VSSM_SHADOWS MOMENT_SHADOWS 

            float4 _Color;

            // 阴影贴图信息
            sampler2D _ShadowTex;
            float4x4 _LightMatrix;
            float4 _ShadowTexScale;

            // 阴影参数
            float _MaxShadowIntensity;
            int _DrawTransparentGeometry;
            float _ShadowBias;
            // VSM
            float _VarianceShadowExpansion;
            float _PCSSFilterScale;
            float _PCSSSearchRadius;
        
            

            struct v2f
            {
                float4 pos : SV_POSITION;
				float4 wPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float depth : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wPos = mul(unity_ObjectToWorld, v.vertex);
                o.normal = v.normal;

                COMPUTE_EYEDEPTH(o.depth);
                return o; 
            }

            float Hard(float depth,float2 uv){
                float4 samp = tex2D(_ShadowTex, uv);
                float sDepth = samp.r;
                // 这里的偏移是补偿阴影贴图生成的
                // depth > sDepth ? 1 : 0
                // 1 : 表示在阴影里
                return step(sDepth, depth);
            }

            float PCF(float depth, float2 uv){
                // 在深度比较时引入区域滤波
                float shadow=0.0;
                int samples = 0;
                for(int x=-2; x<=2; ++x)
                {
                    for(int y=-2; y<=2; ++y)
                    {
                        float4 col=tex2D( _ShadowTex, uv + float2(x,y)*_ShadowTexScale.w);
                        float sampleDepth=col.r;
                        shadow += step(sampleDepth, depth);
                        samples++;
                    }
                }
                return shadow / samples ;
            }

            float PCSS(float depth, float2 uv){
                // === 设置 ===
                // - 遮挡者采样数
                // - PCF模糊的采样数
                // - 搜索 blocker 的半径
                //  - 模糊范围缩放
                const int blockerSamples = 16;
                const int pcfSamples = 16;
                float searchRegion = _PCSSSearchRadius; // 20
                float filterScale = _PCSSFilterScale; // 4

                // === Step 1: Blocker Search ===
                // 寻找遮挡者
                float avgBlockerDepth = 0;
                int blockerCount = 0;

                for (int x = -2; x <= 2; ++x)
                {
                    for (int y = -2; y <= 2; ++y)
                    {
                        float2 offset = float2(x, y) * _ShadowTexScale.w * searchRegion;
                        float sampleDepth = tex2D(_ShadowTex, uv + offset).r;

                        // 说明被挡住了，说明是blocker
                        if (sampleDepth < depth)
                        {
                            avgBlockerDepth += sampleDepth;
                            blockerCount++;
                        }
                    }
                }

                if (blockerCount == 0)
                    return 0; // 全亮无阴影

                avgBlockerDepth /= blockerCount;

                // === Step 2: Penumbra Size Estimation ===
                // 当前深度和blocker平均深度的差值，估算软阴影区域
                float penumbra = (depth - avgBlockerDepth) * filterScale;

                // === Step 3: PCF with variable radius ===
                // 用PCF制作软阴影
                float shadow = 0.0;
                int samples = 0;

                for (int x = -2; x <= 2; ++x)
                {
                    for (int y = -2; y <= 2; ++y)
                    {
                        // 在uv附近以估算的模糊半径进行采样
                        float2 offset = float2(x, y) * _ShadowTexScale.w * penumbra;
                        float sDepth = tex2D(_ShadowTex, uv + offset).r;
                        shadow += step(sDepth, depth);
                        samples++;
                    }
                }

                return shadow / samples;
            }

            float VSM(float depth, float2 uv){
                // 一个像素点局部区域内的深度均值（E[x]）和平方均值（E[x²]）
                // - 通过 Chebyshev 不等式 估算它被遮挡（在阴影中）的最大可能性（上界）。
            
                float4 samp = tex2D(_ShadowTex, uv);
                // x:深度均值
                // x2：深度平方均值
                float2 s = samp.rg;
                float x = s.r;              
                float x2 = s.g;
                float var = x2 - x * x; // 方差
                // 估算当前深度depth 落在阴影中的概率  Chebyshev 不等式
                float delta = depth -x;
                float p_max = var / (var + delta * delta);
                // soft - edge控制参数：控制阴影过度范围
                float amount = _VarianceShadowExpansion;
                p_max = clamp ((p_max - amount) /(1 - amount), 0, 1);
                // 硬阴影区
                float p = depth <= x;
                return 1 - max (p, p_max);
            }

            float VSSM(float depth, float2 uv){
                // 参数：采样核大小
                const int kernelSize = 3;
                float shadow = 0.0;
                float totalWeight = 0.0;

                // 【PCF思想】
                for (int x = -kernelSize; x <= kernelSize; ++x)
                {
                    for (int y = -kernelSize; y <= kernelSize; ++y)
                    {
                        float2 offset = float2(x, y) * _ShadowTexScale.w;

                        // 采样当前邻域像素的平均深度和深度平方
                        float4 samp = tex2D(_ShadowTex, uv + offset);
                        float mean = samp.r;
                        float mean2 = samp.g;
                        float variance = mean2 - mean * mean;
                        float delta = depth - mean;

                        // 【VSM思想】
                        // Chebyshev 上界估计
                        float p_max = variance / (variance + delta * delta + 0.0001); // 防止除0

                        // 保证硬边界仍生效
                        float p = depth <= mean;

                        // 使用 soft-amount 参数控制过渡
                        float amount = _VarianceShadowExpansion;
                        p_max = clamp((p_max - amount) / (1.0 - amount), 0.0, 1.0);

                        // 当前点阴影强度：1 - max(p, p_max)
                        float visibility = 1.0 - max(p, p_max);

                        // 权重：可使用距离作为权重因子
                        float weight = 1.0 - length(float2(x, y)) / (kernelSize + 1);
                        shadow += visibility * weight;
                        totalWeight += weight;
                    }
                }
                return shadow / totalWeight;
            }


            float MSM(float depth, float2 uv) {
                // from 《GPU Gem3》
                float4 b = tex2D(_ShadowTex, uv);
                // 构造三阶矩阵分解因子（类似协方差矩阵）
                float L32D22 = mad(-b[0], b[1], b[2]);
                float D22 = mad(-b[0], b[0], b[1]);
                float SquaredDepthVariance = mad(-b[1], b[1], b[3]);
                // 构建拟合三次分布的线性系统参数
                float D33D22 = dot(float2(SquaredDepthVariance, -L32D22),float2(D22, L32D22));
                // 解线性系统，构造约化系数 c
                float InvD22 = 1.0f / D22;
                float L32 = L32D22*InvD22;
                float3 z;
                z[0] = depth;
                float3 c = float3(1.0f, z[0], z[0] * z[0]);
                // - 减去均值影响
                c[1] -= b.x;
                c[2] -= b.y + L32*c[1];
                c[1] *= InvD22;
                c[2] *= D22 / D33D22;
                c[1] -= L32*c[2];
                // c[0] 是最终的分布偏移量
                c[0] -= dot(c.yz, b.xy);
                // 解三次方程，获得两个实根
                float InvC2 = 1.0f / c[2];
                float p = c[1] * InvC2;
                float q = c[0] * InvC2;
                float r = sqrt((p*p*0.25f) - q);
                // “两个阈值”解，代表当前 depth 与分布中“遮挡范围”的交界。
                z[1] = -p*0.5f - r;
                z[2] = -p*0.5f + r;
                // 根据当前深度在哪个区间，选择不同的阴影模式：
                // [z1, z2] ：“渐变阴影”
                //   < z1 ：完全遮挡
                //   > z2 ：完全光照
                float4 Switch =
                    (z[2]<z[0]) ? float4(z[1], z[0], 1.0f, 1.0f) : (
                    (z[1]<z[0]) ? float4(z[0], z[1], 0.0f, 1.0f) :
                        float4(0.0f, 0.0f, 0.0f, 0.0f));
                // 最终遮挡概率 Quotient（当前点是否在阴影中）
                float Quotient = (Switch[0] * z[2] - b[0] * (Switch[0] + z[2]) + b[1])
                    / ((z[2] - Switch[1])*(z[0] - z[1]));
                    
                return saturate(Switch[2] + Switch[3] * Quotient);
            }
            
            
            
            fixed4 frag(v2f i):SV_TARGET
            {
                float4 color = _Color;

                // 当前像素点 在光源相机裁剪空间的深度值
                float4 lightSpacePos = mul(_LightMatrix, i.wPos);   // 转换到阴影相机空间
                float3 lightSpaceNorm = normalize(mul(_LightMatrix,UnityObjectToWorldNormal(i.normal)));
                // 为什么不是 float depth = lightSpacePos.z / lightSpacePos.w;？
                // - 像素虽然在透视相机渲染流程下,但是要转换到正交相机的裁剪空间，所以要符合正交相机的ndc空间转换。
                float depth = lightSpacePos.z / _ShadowTexScale.z;
                depth -= _ShadowBias;

                float2 uv = lightSpacePos.xy;
                uv += _ShadowTexScale.xy / 2;
                uv /= _ShadowTexScale.xy;

                float shadowIntensity = 0;
                
                #ifdef HARD_SHADOWS 
                    shadowIntensity = Hard(depth, uv);
                #endif

                #ifdef PCF_SHADOWS
                    shadowIntensity = PCF(depth, uv);  
                #endif 

                #ifdef PCSS_SHADOWS
                    shadowIntensity = PCSS(depth, uv);  
                #endif 

                #ifdef VSM_SHADOWS
                    shadowIntensity = VSM(depth, uv);  
                #endif 

                #ifdef VSSM_SHADOWS
                    shadowIntensity = VSSM(depth, uv);  
                #endif 

                #ifdef MOMENT_SHADOWS
                    shadowIntensity = MSM(depth, uv);
                #endif

                color.xyz *= 1 - shadowIntensity * _MaxShadowIntensity;
                color.xyz += UNITY_LIGHTMODEL_AMBIENT.xyz;
                return color;
            }

            


            ENDCG
        }
    }
}