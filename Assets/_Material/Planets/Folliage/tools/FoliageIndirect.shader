Shader "Foliage/Indirect_Rotation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct FoliageInstance
            {
                float3 position;
                float3 normal;
                float  scale;
                float4 rotation;   // quaternion
            };

            StructuredBuffer<FoliageInstance> _Instances;
            sampler2D _MainTex;

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float3 nrm : TEXCOORD1;
            };

            // Rotation quaternion → float3
            float3 qmul(float4 q, float3 v)
            {
                return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
            }

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                FoliageInstance inst = _Instances[id];
                v2f o;

                float3 local = v.vertex * inst.scale;

                float3 rotated = qmul(inst.rotation, local);

                float3 worldPos = inst.position + rotated;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                o.nrm = normalize(qmul(inst.rotation, v.normal));
                o.uv = v.uv;

                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
