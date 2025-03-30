Shader "ShadowMap/ShadowCaster" {

    SubShader {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #include "UnityCG.cginc"
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ DRAW_TRANSPARENT_SHADOWS

            sampler2D _MainTex;
            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 Normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                //TODO: bias
                    
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // TODO: Understand why depth is reversed
                // 原本：值越大越远
                // 反转：值越大表示越近
                float depth = 1 - i.vertex.z;
                // 存储 depth^2 方便方差阴影映射 VSM
                // return float4(depth, pow(depth, 2), 0, 0);
                return float4(depth, pow(depth, 2), depth*depth*depth, depth*depth*depth*depth);
            }
            ENDCG
        }
    }
}