Shader "Foliage/Indirect_Rotation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle] _UseRotationMatrix ("Use Rotation Matrix", Float) = 1
    }

    // Primary SubShader for SM 4.5+ with full features
    SubShader
    {
        Tags { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 400

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct FoliageInstance
            {
                float3 position;
                float3 normal;
                float scale;
                float3 rotation;
            };

            StructuredBuffer<FoliageInstance> _Instances;
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fogCoord : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                FoliageInstance inst = _Instances[instanceID];

                float3x3 rotationMatrix = CreateCombinedRotation(inst.rotation, inst.normal);
                float3 rotatedVertex = mul(rotationMatrix, input.positionOS * inst.scale);
                float3 worldPos = rotatedVertex + inst.position;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS = normalize(mul(rotationMatrix, input.normalOS));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);

                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Sample texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                texColor *= _Color;

                // Alpha clipping
                #if defined(_ALPHATEST_ON)
                    clip(texColor.a - _Cutoff);
                #endif

                // Basic lighting
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lightColor = mainLight.color * mainLight.shadowAttenuation;
                
                half NdotL = saturate(dot(input.normalWS, mainLight.direction));
                half3 lighting = lightColor * NdotL + _GlossyEnvironmentColor.rgb;
                
                half3 color = texColor.rgb * lighting;
                color = MixFog(color, input.fogCoord);

                return half4(color, texColor.a);
            }
            ENDHLSL
        }

        // Shadow pass
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            struct FoliageInstance
            {
                float3 position;
                float3 normal;
                float scale;
                float3 rotation;
            };

            StructuredBuffer<FoliageInstance> _Instances;

            float3x3 EulerToMatrix(float3 euler)
            {
                float3 rad = radians(euler);
                float cx = cos(rad.x); float sx = sin(rad.x);
                float cy = cos(rad.y); float sy = sin(rad.y);
                float cz = cos(rad.z); float sz = sin(rad.z);

                float3x3 mx = float3x3(1, 0, 0, 0, cx, -sx, 0, sx, cx);
                float3x3 my = float3x3(cy, 0, sy, 0, 1, 0, -sy, 0, cy);
                float3x3 mz = float3x3(cz, -sz, 0, sz, cz, 0, 0, 0, 1);

                return mul(mz, mul(my, mx));
            }

            float3x3 CreateRotationFromNormal(float3 normal)
            {
                float3 up = normalize(normal);
                float3 forward = float3(0, 0, 1);
                
                if (abs(up.y) > 0.999)
                    forward = float3(0, 0, 1);
                else
                    forward = normalize(cross(up, float3(0, 1, 0)));
                
                float3 right = normalize(cross(forward, up));
                forward = normalize(cross(up, right));
                
                return float3x3(right, up, forward);
            }

            float3x3 CreateCombinedRotation(float3 eulerRotation, float3 normal)
            {
                float3x3 instanceRot = EulerToMatrix(eulerRotation);
                float3x3 normalRot = CreateRotationFromNormal(normal);
                return mul(normalRot, instanceRot);
            }

            Attributes ShadowPassVertex(Attributes input, uint instanceID : SV_InstanceID)
            {
                FoliageInstance inst = _Instances[instanceID];
                
                float3x3 rotationMatrix = CreateCombinedRotation(inst.rotation, inst.normal);
                float3 rotatedVertex = mul(rotationMatrix, input.positionOS * inst.scale);
                float3 worldPos = rotatedVertex + inst.position;
                
                input.positionOS = float4(TransformWorldToObject(worldPos), 0);
                return input;
            }
            ENDHLSL
        }
    }

    // Fallback SubShader for SM 3.0 (simplified)
    SubShader
    {
        Tags { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 3.0

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct FoliageInstance
            {
                float3 position;
                float3 normal;
                float scale;
                float3 rotation;
            };

            StructuredBuffer<FoliageInstance> _Instances;
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogCoord : TEXCOORD2;
            };

            // Simplified rotation for SM 3.0
            float3x3 CreateSimpleRotation(float3 normal, float yRotation)
            {
                float3 up = normalize(normal);
                float3 forward = float3(0, 0, 1);
                
                if (abs(up.y) > 0.999)
                    forward = float3(0, 0, 1);
                else
                    forward = normalize(cross(up, float3(0, 1, 0)));
                
                float3 right = normalize(cross(forward, up));
                
                // Apply rotation around normal (Y axis)
                float cosY = cos(yRotation);
                float sinY = sin(yRotation);
                
                float3 newRight = right * cosY + forward * sinY;
                float3 newForward = cross(up, newRight);
                
                return float3x3(newRight, up, newForward);
            }

            Varyings vert(Attributes input, uint instanceID : SV_InstanceID)
            {
                Varyings output;

                FoliageInstance inst = _Instances[instanceID];

                // Use only Y rotation for simplicity
                float3x3 rotationMatrix = CreateSimpleRotation(inst.normal, inst.rotation.y);
                float3 rotatedVertex = mul(rotationMatrix, input.positionOS * inst.scale);
                float3 worldPos = rotatedVertex + inst.position;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.normalWS = normalize(mul(rotationMatrix, input.normalOS));
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);

                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                texColor *= _Color;

                #if defined(_ALPHATEST_ON)
                    clip(texColor.a - _Cutoff);
                #endif

                half3 color = MixFog(texColor.rgb, input.fogCoord);
                return half4(color, texColor.a);
            }
            ENDHLSL
        }
    }

    // Fallback for very old hardware or when StructuredBuffer is not available
    SubShader
    {
        Tags { 
            "RenderType" = "Opaque" 
            "Queue" = "Geometry"
        }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDCG
        }
    }

    FallBack "Universal Render Pipeline/Lit"
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.LitShader"
}