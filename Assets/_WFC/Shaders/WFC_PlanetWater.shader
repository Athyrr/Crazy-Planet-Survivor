// mer des planetes WFC: vagues animees, eau peu profonde pres des cotes, ecume qui roule vers le rivage, reflet du soleil.
// uv1.x = distance au rivage (monde, cuite par WFCCrazyPlanetBaker), uv1.y = 1 sous les terres. compatible Entities Graphics
Shader "WFC/Planet Water"
{
    Properties
    {
        [Header(Haze)]
        _HazeColor ("Haze (alpha = max)", Color) = (0.45, 0.65, 0.95, 0.45)
        _HazeStart ("Haze Start", Float) = 110
        _HazeEnd ("Haze End", Float) = 380
        [Header(Colors)]
        _DeepColor ("Deep", Color) = (0.02, 0.16, 0.36, 1)
        _ShallowColor ("Shallow", Color) = (0.06, 0.62, 0.68, 1)
        _ShallowDistance ("Shallow Distance", Float) = 10
        [HDR] _SkyColor ("Sky Reflection", Color) = (0.55, 0.8, 1.2, 1)
        [HDR] _FoamColor ("Foam", Color) = (1.2, 1.25, 1.25, 1)

        [Header(Foam)]
        _FoamDistance ("Foam Distance", Float) = 3.5
        _FoamBands ("Foam Band Frequency", Float) = 1.1
        _FoamSpeed ("Foam Band Speed", Float) = 1.4

        [Header(Waves)]
        _WaveScale ("Wave Scale (per world unit)", Float) = 0.18
        _WaveSpeed ("Wave Speed", Float) = 0.35
        _WaveNormal ("Wave Normal Strength", Range(0, 2)) = 0.3
        _Gloss ("Sun Glint Sharpness", Range(16, 1024)) = 160
        _GlintStrength ("Sun Glint Strength", Range(0, 8)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _ShallowColor; float _ShallowDistance; half4 _SkyColor, _FoamColor;
            float _FoamDistance, _FoamBands, _FoamSpeed;
            float _WaveScale, _WaveSpeed; half _WaveNormal; half _Gloss, _GlintStrength;
            half4 _HazeColor; float _HazeStart, _HazeEnd;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WFCPlanetCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 shore : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 shore : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WaveField(float3 p)
            {
                float t = _Time.y * _WaveSpeed;
                return WFCNoise(p + float3(t, t * 0.6, -t * 0.8)) * 0.6 + WFCNoise(p * 2.3 - float3(t * 1.3, -t, t * 0.5)) * 0.4;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // pas de deplacement des sommets: les passes de profondeur (depth priming) doivent tomber au meme endroit
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.shore = input.shore;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n0 = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // normale des vagues: gradient du champ de vagues (differences finies en espace objet)
                float3 p = input.positionOS * _WaveScale;
                float e = 0.35;
                float w = WaveField(p);
                float3 grad = float3(WaveField(p + float3(e, 0, 0)), WaveField(p + float3(0, e, 0)), WaveField(p + float3(0, 0, e))) - w;
                float3 gradWS = TransformObjectToWorldDir(grad / e, false);
                float3 n = normalize(n0 - (gradWS - n0 * dot(gradWS, n0)) * _WaveNormal);

                float dist = input.shore.x;
                half shallow = 1.0 - saturate(dist / max(_ShallowDistance, 0.001));
                half3 water = lerp(_DeepColor.rgb, _ShallowColor.rgb, shallow * shallow);

                Light mainLight = WFCMainLight(input.positionWS, n0);
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half ndl = saturate(dot(n, mainLight.direction) * 0.5 + 0.5);
                half3 color = water * (mainLight.color * ndl * lerp(0.55, 1.0, shadow) + SampleSH(n0));

                half fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                color = lerp(color, _SkyColor.rgb, fres * 0.6);

                float3 hv = normalize(mainLight.direction + v);
                half glint = pow(saturate(dot(n, hv)), _Gloss) * _GlintStrength * shadow;
                color += mainLight.color * glint;

                // ecume: liseré contre la cote + bandes qui avancent vers le rivage, cassees par du bruit
                half breakup = WFCNoise(input.positionOS * 0.35 + _Time.y * 0.2);
                half edge = 1.0 - smoothstep(0.0, _FoamDistance * 0.35, dist);
                // la phase des bandes est deformee par du bruit: pas de lignes paralleles au maillage
                half wobble = WFCNoise(input.positionOS * 0.22) * 2.5;
                half bands = saturate(sin((dist + wobble) * _FoamBands * 6.2831 + _Time.y * _FoamSpeed) * 0.5 + 0.5);
                bands = smoothstep(0.8, 0.97, bands) * (1.0 - smoothstep(0.0, _FoamDistance, dist)) * smoothstep(0.3, 0.6, breakup);
                half edgeNoise = WFCNoise(input.positionOS * 0.9 + _Time.y * 0.3);
                half foam = saturate(edge * (0.6 + 0.6 * edgeNoise) + bands * 0.7);
                color = lerp(color, _FoamColor.rgb * (0.6 + 0.4 * shadow), foam * 0.8);

                color = WFCHaze(color, input.positionWS, _HazeColor, _HazeStart, _HazeEnd);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WFCDepthVert
            #pragma fragment WFCDepthFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "WFCPlanetPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WFCDepthVert
            #pragma fragment WFCDepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "WFCPlanetPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
