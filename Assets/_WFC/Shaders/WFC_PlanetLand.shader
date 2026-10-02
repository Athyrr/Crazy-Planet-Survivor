// sol des planetes WFC: couleur de sommet (stamps), lumiere enveloppante, contour (atmosphere), occlusion par le relief,
// detail de bruit et emission (alpha du vertex color = 1 - emission, comme SG_VertexColor). compatible Entities Graphics
Shader "WFC/Planet Land"
{
    Properties
    {
        [Header(Haze)]
        _HazeColor ("Haze (alpha = max)", Color) = (0.45, 0.65, 0.95, 0.45)
        _HazeStart ("Haze Start", Float) = 110
        _HazeEnd ("Haze End", Float) = 380
        [Header(Color)]
        _Saturation ("Saturation", Range(0, 2)) = 1.6
        _Contrast ("Contrast", Range(0.5, 1.5)) = 1.05
        _Brightness ("Brightness", Range(0, 2)) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12

        [Header(Detail Noise)]
        _NoiseScale ("Noise Scale (per world unit)", Float) = 0.12
        _NoiseStrength ("Noise Strength", Range(0, 0.5)) = 0.14

        [Header(Relief)]
        _PlanetRadius ("Planet Radius", Float) = 100
        _HeightRange ("Height Range", Float) = 8
        _LowTint ("Low Tint", Color) = (0.55, 0.6, 0.75, 1)
        _HighTint ("High Tint", Color) = (1.12, 1.08, 1.0, 1)
        _AOStrength ("Relief Occlusion", Range(0, 1)) = 0.45

        [Header(Lighting)]
        _Wrap ("Wrap Lighting", Range(0, 1)) = 0.2
        _ShadowTint ("Shadow Tint", Color) = (0.35, 0.4, 0.6, 1)
        [HDR] _RimColor ("Rim Color", Color) = (0.45, 0.75, 1.4, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 6
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.25

        [Header(Emission)]
        _EmissionStrength ("Emission Strength", Float) = 3
        _EmissionPulse ("Emission Pulse", Range(0, 1)) = 0.35
        _PulseSpeed ("Pulse Speed", Float) = 1.5
        _PulseScale ("Pulse Scale", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _Saturation, _Contrast, _Brightness, _Smoothness;
            float _NoiseScale; half _NoiseStrength;
            float _PlanetRadius, _HeightRange;
            half4 _LowTint, _HighTint; half _AOStrength;
            half _Wrap; half4 _ShadowTint; half4 _RimColor; half _RimPower, _RimStrength;
            half _EmissionStrength, _EmissionPulse, _PulseSpeed, _PulseScale;
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
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // couleur du stamp, plus vive, puis detail de bruit et teinte selon l'altitude
                half3 albedo = WFCSaturate(input.color.rgb, _Saturation, _Contrast) * _Brightness;
                float3 p = input.positionOS * _NoiseScale;
                half detail = WFCFbm(p, 3) * 0.7 + WFCNoise(p * 7.3) * 0.3;
                albedo *= 1.0 + (detail - 0.5) * 2.0 * _NoiseStrength;
                float height = (length(input.positionOS) - _PlanetRadius) / max(_HeightRange, 0.001);
                half h01 = saturate(height * 0.5 + 0.5);
                albedo *= lerp(_LowTint.rgb, _HighTint.rgb, h01);
                half ao = lerp(1.0 - _AOStrength, 1.0, h01);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = dot(n, mainLight.direction);
                half wrap = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half lit = wrap * shadow;
                // ombre teintee (bleutee) plutot que noire: plus lisible vue de haut
                half3 lightTerm = mainLight.color * lit + _ShadowTint.rgb * (1.0 - lit) * 0.35;
                half3 ambient = SampleSH(n) * ao;
                half3 color = albedo * (lightTerm + ambient);

                float3 hv = normalize(mainLight.direction + v);
                half spec = pow(saturate(dot(n, hv)), lerp(8.0, 128.0, _Smoothness)) * _Smoothness * shadow;
                color += mainLight.color * spec;

                // contour sur la normale de la sphere (pas celle de la pente): lueur au bord de la planete et a l'horizon
                float3 radial = normalize(input.positionWS - TransformObjectToWorld(float3(0, 0, 0)));
                half fres = pow(1.0 - saturate(dot(radial, v)), _RimPower);
                color += _RimColor.rgb * fres * _RimStrength * (0.35 + 0.65 * saturate(ndl + 0.4));

                half emission = 1.0 - input.color.a;
                half pulse = 1.0 + _EmissionPulse * sin(_Time.y * _PulseSpeed + dot(input.positionOS, float3(1, 1.3, 0.7)) * _PulseScale);
                color += input.color.rgb * emission * _EmissionStrength * pulse;

                color = WFCHaze(color, input.positionWS, _HazeColor, _HazeStart, _HazeEnd);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WFCShadowVert
            #pragma fragment WFCShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "WFCPlanetPasses.hlsl"
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
