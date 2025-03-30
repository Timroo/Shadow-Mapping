Shader "SSSM/SSShadowCompute" 
{
	Subshader 
	{
		Pass 
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			uniform sampler2D _CameraDepthTex;
			uniform sampler2D _LightDepthTex;

			uniform float4x4 _inverseVP;
			uniform float4x4 _WorldToShadow;

			float _shadowBias;
			float _shadowStrength;

			struct a2v
			{
				float4 texcoord : TEXCOORD0;
				float4 vertex : POSITION;
			};
			
			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
			};
			
			v2f vert (a2v i)
			{
				v2f o;
				o.pos = UnityObjectToClipPos (i.vertex);
				o.uv = i.texcoord;
				
				return o;
			}

			fixed4 frag(v2f i) : SV_TARGET
			{
				// 获取主摄深度
				half rawDepth = tex2D(_CameraDepthTex, i.uv).r;
				#if defined (SHADER_TARGET_GLSL) 
					rawDepth = rawDepth * 2 - 1;	 // (0, 1)-->(-1, 1)
				#elif defined (UNITY_REVERSED_Z)
					rawDepth = 1 - rawDepth;       // (0, 1)-->(1, 0)
				#endif

				// 主摄裁切空间坐标
				float4 clipPos;
				clipPos.xy = i.uv * 2 - 1;
				clipPos.z = rawDepth;
				clipPos.w = 1;

				// 世界空间坐标
				float4 posWorld = mul(_inverseVP, clipPos);
				posWorld /= posWorld.w;

				// 将世界坐标转为光源相机空间坐标
				half4 shadowCoord = mul(_WorldToShadow, posWorld);
				half2 uv = shadowCoord.xy;
				uv = uv * 0.5 + 0.5; //(-1, 1)-->(0, 1)

				// 光源相机空间 -> ndc空间
				half depth = shadowCoord.z / shadowCoord.w;
				#if defined(UNITY_REVERSED_Z)
					depth = 1.0 - depth;
				#else
					depth = depth * 0.5 + 0.5;
				#endif
				
				// 获取光源深度图深度
				half sDepth = tex2D(_LightDepthTex, uv).r;
				half shadow = (sDepth < depth - _shadowBias) ? _shadowStrength : 1;

				return shadow;
			}
			ENDCG
		}
	}
}