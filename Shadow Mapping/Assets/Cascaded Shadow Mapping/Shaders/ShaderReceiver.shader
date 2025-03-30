Shader "CSM/ShadowReceiver"{
    SubShader{
        Tags{"RenderType"="Opaque"}

        Pass{
            Tags{"LightMode"="ForwardBase"}
            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag

            struct v2f{
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 shadowCoord : TEXCOORD1;
                float eyeZ : TEXCOORD2;
                float4 worldPos : TEXCOORD3;
            };

			uniform float4 _gLightSplitsNear;
			uniform float4 _gLightSplitsFar;
			uniform float4x4 _gWorld2Shadow[4];
			
			uniform sampler2D _gShadowMapTexture0;
			uniform sampler2D _gShadowMapTexture1;
			uniform sampler2D _gShadowMapTexture2;
			uniform sampler2D _gShadowMapTexture3;

			uniform float _gShadowStrength;
            // PCF采样
            uniform float _gShadowMapTexture_TexelSize0;
            uniform float _gShadowMapTexture_TexelSize1;
            uniform float _gShadowMapTexture_TexelSize2;
            uniform float _gShadowMapTexture_TexelSize3;


            v2f vert(appdata_full v){
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.eyeZ = o.pos.w;
                return o;
            }

            // 判断属于哪个级联
            fixed4 getCascadeWeights(float z){
                // 会挨个对比，赢了就写 1
                // - zNear = (12 >= [0.3, 10, 25, 50]) → float4(1, 1, 0, 0)
                // - zFar  = (12 <  [10, 25, 50, 100]) → float4(0, 1, 1, 1)
                // 乘法相当于AND：zNear * zFar = float4(0, 1, 0, 0)
                fixed4 zNear = float4(z >= _gLightSplitsNear);
                fixed4 zFar = float4(z < _gLightSplitsFar);
                fixed4 weights = zNear * zFar;
                return weights;
            }

            fixed4 getCascadeWeights_Blend(float z)
            {
                float blendRange = 5.0; // 混合过渡范围（单位：eye space 深度）

                fixed4 weights = fixed4(0, 0, 0, 0);

                for (int i = 0; i < 4; i++)
                {
                    float zNear = _gLightSplitsNear[i];
                    float zFar = _gLightSplitsFar[i];

                    if (z >= zNear && z < zFar)
                    {
                        // 混合因子（距离远平面的距离）
                        float t = saturate((zFar - z) / blendRange);
                        float blend = smoothstep(0.0, 1.0, t);

                        if (i > 0)
                            weights[i - 1] = 1.0 - blend; // 混入前一个 cascade 的一部分
                        weights[i] = blend;              // 当前 cascade 的主要权重

                        break; // 只匹配一个区域
                    }
                }

                return weights;
            }


            float4 SampleShadowTexture_Hard(float4 wPos, fixed4 cascadedWeights){
                float4 shadowCoord0 = mul(_gWorld2Shadow[0], wPos);
                float4 shadowCoord1 = mul(_gWorld2Shadow[1], wPos);
                float4 shadowCoord2 = mul(_gWorld2Shadow[2], wPos);
                float4 shadowCoord3 = mul(_gWorld2Shadow[3], wPos);

                shadowCoord0.xy /= shadowCoord0.w;
				shadowCoord1.xy /= shadowCoord1.w;
				shadowCoord2.xy /= shadowCoord2.w;
				shadowCoord3.xy /= shadowCoord3.w;

                shadowCoord0.xy = shadowCoord0.xy * 0.5 + 0.5;
				shadowCoord1.xy = shadowCoord1.xy * 0.5 + 0.5;
				shadowCoord2.xy = shadowCoord2.xy * 0.5 + 0.5;
				shadowCoord3.xy = shadowCoord3.xy * 0.5 + 0.5;

                float4 sampleDepth0 = tex2D(_gShadowMapTexture0, shadowCoord0.xy);
				float4 sampleDepth1 = tex2D(_gShadowMapTexture1, shadowCoord1.xy);
				float4 sampleDepth2 = tex2D(_gShadowMapTexture2, shadowCoord2.xy);
				float4 sampleDepth3 = tex2D(_gShadowMapTexture3, shadowCoord3.xy);

                float depth0 = shadowCoord0.z / shadowCoord0.w;
				float depth1 = shadowCoord1.z / shadowCoord1.w;
				float depth2 = shadowCoord2.z / shadowCoord2.w;
				float depth3 = shadowCoord3.z / shadowCoord3.w;

                #if defined (SHADER_TARGET_GLSL)
					depth0 = depth0*0.5 + 0.5; //(-1, 1)-->(0, 1)
					depth1 = depth1*0.5 + 0.5;
					depth2 = depth2*0.5 + 0.5;
					depth3 = depth3*0.5 + 0.5;
				#elif defined (UNITY_REVERSED_Z)
					depth0 = 1 - depth0;       //(1, 0)-->(0, 1)
					depth1 = 1 - depth1;
					depth2 = 1 - depth2;
					depth3 = 1 - depth3;
				#endif

                float shadow0 = sampleDepth0 < depth0 ? _gShadowStrength : 1;
				float shadow1 = sampleDepth1 < depth1 ? _gShadowStrength : 1;
				float shadow2 = sampleDepth2 < depth2 ? _gShadowStrength : 1;
				float shadow3 = sampleDepth3 < depth3 ? _gShadowStrength : 1;

                // - 实际上只会命中一个cascade
                float shadow = shadow0 * cascadedWeights[0] + shadow1 * cascadedWeights[1] + shadow2 * cascadedWeights[2] + shadow3 * cascadedWeights[3];
				return shadow;
           }

            // float SampleShadowTexture_Hard(float4 wPos, fixed4 cascadeWeights)
            // {
            //     float shadow = 0;
            //     for (int i = 0; i < 4; i++)
            //     {
            //         if (cascadeWeights[i] == 0) continue;

            //         float4 shadowCoord = mul(_gWorld2Shadow[i], wPos);
            //         shadowCoord.xy /= shadowCoord.w;
            //         shadowCoord.xy = shadowCoord.xy * 0.5 + 0.5;
            //         float depth = shadowCoord.z / shadowCoord.w;

            //         #if defined (SHADER_TARGET_GLSL)
            //             depth = depth * 0.5 + 0.5;
            //         #elif defined (UNITY_REVERSED_Z)
            //             depth = 1.0 - depth;
            //         #endif

            //         float sampleDepth = 1.0;
            //         if (i == 0)
            //             sampleDepth = tex2D(_gShadowMapTexture0, shadowCoord.xy).r;
            //         else if (i == 1)
            //             sampleDepth = tex2D(_gShadowMapTexture1, shadowCoord.xy).r;
            //         else if (i == 2)
            //             sampleDepth = tex2D(_gShadowMapTexture2, shadowCoord.xy).r;
            //         else
            //             sampleDepth = tex2D(_gShadowMapTexture3, shadowCoord.xy).r;

            //         float s = sampleDepth < depth ? _gShadowStrength : 1.0;
            //         shadow += s * cascadeWeights[i];
            //     }
            //     return shadow;
            // }

           // PCF采样
           float SamplePCF(sampler2D shadowMap, float2 uv, float compareDepth, float texelSize) {
                float shadow = 0.0;
                int range = 2;
                for (int x = -range; x <= range; ++x) {
                    for (int y = -range; y <= range; ++y) {
                        float2 offset = float2(x, y) * texelSize;
                        float sampleDepth = tex2D(shadowMap, uv + offset).r;
                        shadow += sampleDepth < compareDepth ? _gShadowStrength : 1.0;
                    }
                }
                return shadow / ((2 * range + 1) * (2 * range + 1));
            }

            float4 SampleShadowTexture_PCF(float4 wPos, fixed4 weights) {
                float4 shadowCoords[4];
                shadowCoords[0] = mul(_gWorld2Shadow[0], wPos);
                shadowCoords[1] = mul(_gWorld2Shadow[1], wPos);
                shadowCoords[2] = mul(_gWorld2Shadow[2], wPos);
                shadowCoords[3] = mul(_gWorld2Shadow[3], wPos);

                float shadow = 1.0;

                [unroll]
                for (int i = 0; i < 4; i++) {
                    if (weights[i] > 0.5) {
                        float4 coord = shadowCoords[i];
                        coord.xy /= coord.w;
                        coord.xy = coord.xy * 0.5 + 0.5;
                        float depth = coord.z / coord.w;

                        #if defined (SHADER_TARGET_GLSL)
                            depth = depth * 0.5 + 0.5;
                        #elif defined (UNITY_REVERSED_Z)
                            depth = 1 - depth;
                        #endif

                        if (i == 0)
                            shadow = SamplePCF(_gShadowMapTexture0, coord.xy, depth, _gShadowMapTexture_TexelSize0);
                        else if (i == 1)
                            shadow = SamplePCF(_gShadowMapTexture1, coord.xy, depth, _gShadowMapTexture_TexelSize1);
                        else if (i == 2)
                            shadow = SamplePCF(_gShadowMapTexture2, coord.xy, depth, _gShadowMapTexture_TexelSize2);
                        else
                            shadow = SamplePCF(_gShadowMapTexture3, coord.xy, depth, _gShadowMapTexture_TexelSize3);

                    }
                }

                return shadow;
            }




            fixed4 frag(v2f i):SV_Target{
                // 判断属于哪个级联
                fixed4 weights = getCascadeWeights(i.eyeZ);
                // fixed4 weights = getCascadeWeights_Blend(i.eyeZ);

                float4 col = SampleShadowTexture_Hard(i.worldPos, weights);
                // float4 col = SampleShadowTexture_PCF(i.worldPos, weights);

                return col * weights;
            }

        
            ENDCG
        }
    }
}