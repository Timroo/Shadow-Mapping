Shader "SSSM/ShadowReceiver" {

	Properties {
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base", 2D) = "white" {}
	}

	SubShader {
		Tags { "RenderType"="Opaque" "LIGHTMODE"="ForwardBase" }

		Pass {

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct v2f
			{
				half4 pos : SV_POSITION;
				half2 uv : TEXCOORD0;
				half4 screenPos : TEXCOORD4;	
			};


			half4 _Color;
			sampler2D _MainTex;
			float4 _MainTex_ST;

			uniform sampler2D _ScreenSpceShadowTexture;
					
			v2f vert (appdata_full v) 
			{
				v2f o;
				o.pos = UnityObjectToClipPos (v.vertex);
				o.uv.xy = TRANSFORM_TEX(v.texcoord,_MainTex);
				o.screenPos = o.pos;
				return o; 
			}
				
			fixed4 frag (v2f i) : SV_TARGET
			{						
				i.screenPos.xy = i.screenPos.xy / i.screenPos.w;
				float2 screenUV = i.screenPos.xy * 0.5 + 0.5;
				// 兼容 OpenGL 平台的 UV 翻转
				#if UNITY_UV_STARTS_AT_TOP
					screenUV.y = _ProjectionParams.x < 0 ? 1 - screenUV.y : screenUV.y;
				#endif

				half shadow = tex2D(_ScreenSpceShadowTexture, screenUV).r;
				fixed4 c = tex2D(_MainTex, i.uv.xy) * _Color * shadow;

				return c;
			}	
			ENDCG
		}
	}
}
