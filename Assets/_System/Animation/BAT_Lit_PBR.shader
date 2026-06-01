Shader "Custom/URP/BAT_Lit_PBR"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)

        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(-5,5)) = 1.0

        _ORMMap("ORM (R=Occlusion G=Roughness B=Metallic)", 2D) = "white" {}

        _EmissionMap("Emission", 2D) = "black" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,0)

        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        [Toggle(_BAT)] _Bat_OnOff("Bone animation texture", Float) = 0

        _BATMap("BAT Map", 2D) = "black" {}
        
        _BAT_FPS("BAT Fps", Float) = 30
        _BATFrame("BAT Frame", Float) = 0
        _BATFrames("BAT Frames", Float) = 1
        _BATFrameRows("BAT Frame Rows", Float) = 1
        _BATPlaneRows("BAT Plane Rows", Float) = 1
        _BATColumns("BAT Columns", Float) = 1
        
        _BATPlaneWidth("BAT Plane Width", Float) = 1
        _BATPlaneHeight("BAT Plane Height", Float) = 1
        _BATPlaneGridX("BAT Plane GridX", Float) = 1
        _BATPlaneGridY("BAT Plane Grid Y", Float) = 1
        
        _BATBoneCount("BAT Bone Count", Float) = 1

        [Space(10)]
        _BATMap2("BAT Map 2 (crossfade target)", 2D) = "black" {}
        _BATFrames2("BAT Frames 2", Float) = 1
        _BATFrameRows2("BAT Frame Rows 2", Float) = 1
        _BATPlaneHeight2("BAT Plane Height 2", Float) = 1
        _BATPlaneWidth2("BAT Plane Width 2", Float) = 1
        _BATColumns2("BAT Columns", Float) = 1
//        _BATBlend("BAT Blend Amount", Range(0,1)) = 0
        _BATBlendFrame("BAT Blend Frame Offset", Float) = 0
        _BAT_FPS_Blend("BAT FPS Blend", Float) = 30
        [HideInInspector] _BATManualTime("BAT Manual Time", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }
        HLSLINCLUDE
        
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        
        TEXTURE2D(_BATMap);
        SAMPLER(sampler_BATMap);
        TEXTURE2D(_BATMap2);
        SAMPLER(sampler_BATMap2);
        
        CBUFFER_START(UnityPerMaterial)
            float4 _BATMap_TexelSize;
            float4 _BATMap2_TexelSize;
        
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _EmissionColor;
            float _NormalScale;
            float _Cutoff;

            half _BAT_FPS;
            uint _BATFrame;
            uint _BATFrames;
            uint _BATFrameRows;
            uint _BATPlaneRows;
            uint _BATColumns;
            uint _BATPlaneWidth;
            uint _BATPlaneHeight;
            uint _BATPlaneGridX;
            uint _BATPlaneGridY;
            
            float _BATBoneCount;
            float _BATManualTime;

            uint _BATFrames2;
            uint _BATColumns2;
            uint _BATFrameRows2;
            uint _BATPlaneHeight2;
            uint _BATPlaneWidth2;
            uint _BATBlendFrame;
            half _BAT_FPS_Blend;
        CBUFFER_END
        
        float _BATBlend;
        
        struct BoneRows
        {
            float4 r0;
            float4 r1;
            float4 r2;
        };
        
        float Frame()
        {
            if (_BATManualTime > 0.5)
                return fmod(_BATFrame, (float)_BATFrames);
            return fmod(_BATFrame + _Time.y * _BAT_FPS, (float)_BATFrames);
        }

        float2 GetBATUV(uint boneIndex, float frameIdx, uint planeIndex)
        {
            uint columns     = _BATColumns;
            uint frameRows   = _BATFrameRows;
            uint planeWidth  = _BATPlaneWidth;
            uint planeHeight = _BATPlaneHeight;
            uint planeGridX  = _BATPlaneGridX;

            uint rowBlock = boneIndex / columns;
            uint col      = boneIndex - rowBlock * columns;

            uint planeX = planeIndex % planeGridX;
            uint planeY = planeIndex / planeGridX;

            uint x = planeX * planeWidth + col;
            float y = (float)(planeY * planeHeight + rowBlock * frameRows) + frameIdx;

            float texWidth  = _BATMap_TexelSize.z;
            float texHeight = _BATMap_TexelSize.w;

            return (float2((float)x, y) + 0.5) / float2(texWidth, texHeight);
        }

        BoneRows SampleBoneRows(uint boneIndex, float frameIdx)
        {
            BoneRows o;

            float2 uv0 = GetBATUV(boneIndex, frameIdx, 0);
            float2 uv1 = GetBATUV(boneIndex, frameIdx, 1);
            float2 uv2 = GetBATUV(boneIndex, frameIdx, 2);

            o.r0 = SAMPLE_TEXTURE2D_LOD(_BATMap, sampler_BATMap, uv0, 0);
            o.r1 = SAMPLE_TEXTURE2D_LOD(_BATMap, sampler_BATMap, uv1, 0);
            o.r2 = SAMPLE_TEXTURE2D_LOD(_BATMap, sampler_BATMap, uv2, 0);

            return o;
        }

        float FrameBlend()
        {
            if (_BATManualTime > 0.5)
                return fmod(_BATBlendFrame, (float)_BATFrames2);
            return fmod(_BATBlendFrame + _Time.y * _BAT_FPS_Blend, (float)_BATFrames2);
        }

        float2 GetBATUV2(uint boneIndex, float frameIdx, uint planeIndex)
        {
            uint columns     = _BATColumns2;
            uint frameRows   = _BATFrameRows2;
            uint planeHeight = _BATPlaneHeight2;
            uint planeWidth = _BATPlaneWidth2;
            uint planeGridX  = _BATPlaneGridX;

            uint rowBlock = boneIndex / columns;
            uint col      = boneIndex - rowBlock * columns;

            uint planeX = planeIndex % planeGridX;
            uint planeY = planeIndex / planeGridX;

            uint x = planeX * planeWidth + col;
            float y = (float)(planeY * planeHeight + rowBlock * frameRows) + frameIdx;

            float texWidth  = _BATMap2_TexelSize.z;
            float texHeight = _BATMap2_TexelSize.w;

            return (float2((float)x, y) + 0.5) / float2(texWidth, texHeight);
        }

        BoneRows SampleBoneRows2(uint boneIndex, float frameIdx)
        {
            BoneRows o;

            float2 uv0 = GetBATUV2(boneIndex, frameIdx, 0);
            float2 uv1 = GetBATUV2(boneIndex, frameIdx, 1);
            float2 uv2 = GetBATUV2(boneIndex, frameIdx, 2);

            o.r0 = SAMPLE_TEXTURE2D_LOD(_BATMap2, sampler_BATMap2, uv0, 0);
            o.r1 = SAMPLE_TEXTURE2D_LOD(_BATMap2, sampler_BATMap2, uv1, 0);
            o.r2 = SAMPLE_TEXTURE2D_LOD(_BATMap2, sampler_BATMap2, uv2, 0);

            return o;
        }

        float3 MulPointBAT(BoneRows m, float3 p)
        {
            float4 v = float4(p, 1.0);
            return float3(
                dot(m.r0, v),
                dot(m.r1, v),
                dot(m.r2, v)
            );
        }

        float3 MulVectorBAT(BoneRows m, float3 v)
        {
            return float3(
                dot(m.r0.xyz, v),
                dot(m.r1.xyz, v),
                dot(m.r2.xyz, v)
            );
        }
        
        BoneRows BlendBoneRows(BoneRows a, BoneRows b, float t)
        {
            BoneRows o;
            o.r0 = lerp(a.r0, b.r0, t);
            o.r1 = lerp(a.r1, b.r1, t);
            o.r2 = lerp(a.r2, b.r2, t);
            return o;
        }

        void Bat(float3 positionOS, float3 normalOS, float3 tangentOS, uint4 boneIndices, float4 boneWeights,
            float frameIdx, out float3 position, out float3 normal, out float3 tangent)
        {
            tangent = 0;
            normal = 0;
            position = 0;

            float blend = _BATBlend;
            float frameIdx2 = FrameBlend();

            [unroll] for (int i = 0; i < 4; i++)
            {
                float w = boneWeights[i];
                if (w <= 0.0001) continue;

                BoneRows m = SampleBoneRows(boneIndices[i], frameIdx);

                if (blend > 0.001)
                {
                    BoneRows m2 = SampleBoneRows2(boneIndices[i], frameIdx2);
                    m = BlendBoneRows(m, m2, blend);
                }

                position += MulPointBAT(m, positionOS) * w;
                normal += MulVectorBAT(m, normalOS) * w;
                tangent += MulVectorBAT(m, tangentOS) * w;
            }
            
            normal = normalize(normal);
            tangent = normalize(tangent);
        }
        
        void BatShadow(float3 positionOS, float3 normalOS, uint4 boneIndices, float4 boneWeights,
            float frameIdx, out float3 position, out float3 normal)
        {
            normal = 0;
            position = 0;

            float blend = _BATBlend;
            float frameIdx2 = FrameBlend();

            [unroll] for (int i = 0; i < 4; i++)
            {
                float w = boneWeights[i];
                if (w <= 0.0001) continue;

                BoneRows m = SampleBoneRows(boneIndices[i], frameIdx);

                if (blend > 0.001)
                {
                    BoneRows m2 = SampleBoneRows2(boneIndices[i], frameIdx2);
                    m = BlendBoneRows(m, m2, blend);
                }

                position += MulPointBAT(m, positionOS) * w;
                normal += MulVectorBAT(m, normalOS) * w;
            }
            normal = normalize(normal);
        }
        
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #pragma shader_feature _ _SHADOWS_SOFT
            #pragma shader_feature_fragment _ LIGHTMAP_ON
            #pragma multi_compile_fog
            
            #pragma shader_feature _ _ADDITIONAL_LIGHTS
            #pragma shader_feature _ _CLUSTER_LIGHT_LOOP
            #pragma shader_feature _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma shader_feature _ _MAIN_LIGHT_SHADOWS
            #pragma shader_feature _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma shader_feature _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma shader_feature_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            
            #pragma shader_feature_vertex _ _BAT
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_ORMMap);
            SAMPLER(sampler_ORMMap);

            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            
            struct Attributes
            {
                float4 position_os : POSITION;
                float3 normal_os   : NORMAL;
                float4 tangent_os  : TANGENT;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;

                // ===== BAT =====
                uint4 boneIndices : BLENDINDICES;
                float4 boneWeights : BLENDWEIGHTS;
            };

            struct Varyings
            {
                float4 position_cs : SV_POSITION;
                float3 position_ws : TEXCOORD0;
                float3 normal_ws   : TEXCOORD1;
                float4 tangent_ws  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float2 lightmap_uv : TEXCOORD4;
                half3 sh          : TEXCOORD5;
                float fog_coord    : TEXCOORD6;
            };

            float3 get_normal_ws(float2 uv, float3 normal_ws, float4 tangent_ws)
            {
                float3 n_ts = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalScale);

                float3 t = normalize(tangent_ws.xyz);
                float3 n = normalize(normal_ws);
                float3 b = normalize(cross(n, t) * tangent_ws.w);

                float3x3 tbn = float3x3(t, b, n);
                return normalize(mul(n_ts, tbn));
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float3 position_os = IN.position_os.xyz;
                float3 normal_os = IN.normal_os;
                float4 tangent_os = IN.tangent_os;

                #if defined (_BAT)
                float frameIdx = Frame();
                
                float3 outTxyz = 0;
                
                Bat(position_os, normal_os, tangent_os.xyz, IN.boneIndices, IN.boneWeights, frameIdx,
                    position_os, normal_os, outTxyz);
                
                tangent_os.xyz = outTxyz;
                
                #endif

                VertexPositionInputs pos_inputs = GetVertexPositionInputs(position_os);
                VertexNormalInputs nrm_inputs = GetVertexNormalInputs(normal_os, tangent_os);

                OUT.position_cs = pos_inputs.positionCS;
                OUT.position_ws = pos_inputs.positionWS;
                OUT.normal_ws = normalize(nrm_inputs.normalWS);
                OUT.tangent_ws = float4(normalize(nrm_inputs.tangentWS), tangent_os.w);
                OUT.uv = TRANSFORM_TEX(IN.uv0, _BaseMap);
                OUT.lightmap_uv = IN.uv1;
                OUT.sh = SampleSHVertex(OUT.normal_ws);
                OUT.fog_coord = ComputeFogFactor(pos_inputs.positionCS.z);

                return OUT;
            }
            
            //from BasicLight.hlsl
            float3 urp_complete_pbr(float3 world_pos,float3 world_normal,float3 view_dir_ws,half3 baked_gi, float2 lightmap_uv,
                half3 albedo,
                half metallic,half smoothness,half occlusion,half3 emission,half alpha)
            {
                float3 n = normalize(world_normal);
                float3 v = SafeNormalize(view_dir_ws);

                float4 position_cs = TransformWorldToHClip(world_pos);

                InputData input_data = (InputData)0;
                input_data.positionWS = world_pos;
                input_data.normalWS = n;
                input_data.viewDirectionWS = v;
                input_data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(position_cs);
                input_data.shadowCoord = TransformWorldToShadowCoord(world_pos);
                input_data.vertexLighting = half3(0,0,0);
                input_data.fogCoord = 0;

                // Shadowmask / GI, en suivant le pattern URP
                
                input_data.shadowMask = half4(1,1,1,1);

                #if defined(LIGHTMAP_ON)
                input_data.shadowMask = SAMPLE_SHADOWMASK(lightmap_uv);
                input_data.bakedGI = baked_gi;
                #elif defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                input_data.bakedGI = SampleProbeVolumePixel(
                    baked_gi,                           // vertex/default fallback
                    GetAbsolutePositionWS(world_pos),
                    n,
                    v,
                    position_cs.xy                      // important : comme URP Lit
                );
                #else
                input_data.bakedGI = baked_gi;
                #endif

                SurfaceData surface_data;
                surface_data.albedo = albedo;
                surface_data.metallic = metallic;
                surface_data.specular = half3(0,0,0);
                surface_data.smoothness = smoothness;
                surface_data.normalTS = half3(0,0,1);
                surface_data.occlusion = occlusion;
                surface_data.emission = emission;
                surface_data.alpha = alpha;
                surface_data.clearCoatMask = 0;
                surface_data.clearCoatSmoothness = 1;
                
                return UniversalFragmentPBR(input_data, surface_data).rgb;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 albedo_sample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 base_color = albedo_sample * _BaseColor;

                #if defined(_ALPHATEST_ON)
                    clip(base_color.a - _Cutoff);
                #endif

                half3 orm = SAMPLE_TEXTURE2D(_ORMMap, sampler_ORMMap, IN.uv).rgb;
                half occlusion = orm.r;
                half roughness = orm.g;
                half metallic = orm.b;
                half smoothness = 1.0 - roughness;

                float3 normal_ws = get_normal_ws(IN.uv, IN.normal_ws, IN.tangent_ws);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;

                half3 baked_gi = SampleSHPixel(IN.sh, normal_ws);
                
                 
                half3 color = urp_complete_pbr(
                    IN.position_ws,normal_ws,GetWorldSpaceViewDir(IN.position_ws),baked_gi, IN.lightmap_uv,base_color.rgb,
                    metallic, smoothness,occlusion,emission,base_color.a);

                color = MixFog(color, IN.fog_coord);
                
                return half4(color, base_color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            
            #pragma shader_feature_vertex _ _BAT
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float3 position_os : POSITION;
                float3 normal_os   : NORMAL;
                float2 uv0        : TEXCOORD0;

                // ===== BAT =====
                uint4 boneIndices : BLENDINDICES;
                float4 boneWeights : BLENDWEIGHTS;
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float4 position_cs : SV_POSITION;
            };

            float4 GetShadowPositionHClipCustom(Attributes input)
            {
                float3 positionOS = input.position_os.xyz;
                float3 normalOS = input.normal_os;

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition.xyz - positionWS);
                #else
                    float3 lightDirectionWS = _MainLightPosition.xyz;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings OUT;
                #if defined (_BAT)
                float frameIdx = Frame();
                BatShadow(input.position_os, input.normal_os, input.boneIndices, input.boneWeights, frameIdx, 
                    input.position_os,input.normal_os);
                #endif
                OUT.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
                OUT.position_cs = GetShadowPositionHClipCustom(input);
                return OUT;
            }

            half4 ShadowPassFragment(Varyings IN) : SV_TARGET
            {
                #if defined(_ALPHATEST_ON)
                    float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                    clip(baseSample.a - _Cutoff);
                #endif

                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}