// FoliageIndirect.shader
Shader "Foliage/Indirect"
{
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            StructuredBuffer<float4> _Dummy; // keep header neat if needed

            struct FoliageInstance
            {
                float3 position;
                float3 normal;
                float scale;
                float3 rotation;
            };
            StructuredBuffer<FoliageInstance> _Instances;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            // rotate a vector around axis by angle (radians)
            float3 rotateAroundAxis(float3 v, float3 axis, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return v * c + cross(axis, v) * s + axis * (dot(axis, v) * (1 - c));
            }

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                FoliageInstance inst = _Instances[instanceID];

                // build tangent basis so that mesh's up(0,1,0) maps to inst.normal
                float3 up = float3(0,1,0);
                float3 n = normalize(inst.normal);
                float3 tangent = normalize(cross(up, n));
                if (all(tangent == 0)) tangent = normalize(cross(float3(1,0,0), n));
                float3 bitangent = cross(n, tangent);

                // rotate vertex around normal by inst.rotation (degrees -> radians)
                float3 localVertex = v.vertex.xyz * inst.scale;

                // convert local vertex to tangent space (x->tangent, y->normal, z->bitangent)
                float3 v_t = localVertex.x * tangent + localVertex.y * n + localVertex.z * bitangent;

                // apply rotation around normal
                float3 v_rot = rotateAroundAxis(v_t, n, inst.rotation);

                float3 worldPos = inst.position + v_rot;
                o.pos = UnityObjectToClipPos(float4(worldPos,1));
                o.uv = v.uv;
                o.worldNormal = normalize(mul((float3x3)unity_WorldToObject, v.normal)); // best effort
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
