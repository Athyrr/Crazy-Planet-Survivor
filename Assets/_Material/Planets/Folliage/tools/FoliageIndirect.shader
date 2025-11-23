Shader "Foliage/Indirect_Rotation"
{
    Properties
    {
        _ColorA ("Color A", Color) = (1,0,0,1)
        _ColorB ("Color B", Color) = (1,1,1,1)
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
                float3 rotation;
            };

            StructuredBuffer<FoliageInstance> _Instances;
            float4 _ColorA;
            float4 _ColorB;

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

            float3x3 EulerToMatrix(float3 euler)
            {
                float3 rad = radians(euler);
                float cx = cos(rad.x); float sx = sin(rad.x);
                float cy = cos(rad.y); float sy = sin(rad.y);
                float cz = cos(rad.z); float sz = sin(rad.z);

                float3x3 mx = float3x3(
                    1, 0, 0,
                    0, cx, -sx,
                    0, sx, cx
                );
                float3x3 my = float3x3(
                    cy, 0, sy,
                    0, 1, 0,
                    -sy, 0, cy
                );
                float3x3 mz = float3x3(
                    cz, -sz, 0,
                    sz, cz, 0,
                    0, 0, 1
                );

                return mul(mz, mul(my, mx));
            }

            float3x3 CreateRotationFromNormal(float3 normal)
            {
                float3 up = normalize(normal);
                float3 forward = float3(0, 0, 1);
                
                if (abs(up.y) > 0.999)
                {
                    forward = float3(0, 0, 1);
                }
                else
                {
                    forward = normalize(cross(up, float3(0, 1, 0)));
                }
                
                float3 right = normalize(cross(forward, up));
                forward = normalize(cross(up, right));
                
                return float3x3(
                    right.x, up.x, forward.x,
                    right.y, up.y, forward.y,
                    right.z, up.z, forward.z
                );
            }

            float3x3 CreateCombinedRotation(float3 eulerRotation, float3 normal)
            {
                float3x3 instanceRot = EulerToMatrix(eulerRotation);
                
                float3x3 normalRot = CreateRotationFromNormal(normal);
                
                return mul(normalRot, instanceRot);
            }

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                FoliageInstance inst = _Instances[id];
                v2f o;

                float3x3 rotationMatrix = CreateCombinedRotation(inst.rotation, inst.normal);
                
                float3 rotatedVertex = mul(rotationMatrix, v.vertex * inst.scale);
                
                float3 worldPos = rotatedVertex + inst.position;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                o.nrm = normalize(mul(rotationMatrix, v.normal));
                o.uv = v.uv;

                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                float blendFactor = pow(saturate(uv.y), 2.0); // Quadratic curve
                fixed4 color = lerp(_ColorA, _ColorB, blendFactor);
                
                return color;
            }
            ENDCG
        }
    }
}