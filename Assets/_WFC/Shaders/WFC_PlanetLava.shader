// lave des planetes WFC: coulees animees (bruit deforme), veines HDR pour le bloom, croute refroidie et fissuree pres des
// rives, pulsation. uv1.x = distance au rivage (monde, cuite par WFCCrazyPlanetBaker). compatible Entities Graphics
Shader "WFC/Planet Lava"
{
    Properties
    {
        [Header(Haze)]
        _HazeColor ("Haze (alpha = max)", Color) = (0.45, 0.65, 0.95, 0.45)
        _HazeStart ("Haze Start", Float) = 110
        _HazeEnd ("Haze End", Float) = 380
        [Header(Colors)]
        [HDR] _HotColor ("Hot (veins)", Color) = (7, 2.4, 0.4, 1)
        [HDR] _LavaColor ("Lava", Color) = (1.8, 0.28, 0.03, 1)
        [HDR] _CoolColor ("Cooling Lava", Color) = (0.4, 0.035, 0.01, 1)
        _CrustColor ("Crust", Color) = (0.07, 0.045, 0.04, 1)

        [Header(Flow)]
        _FlowScale ("Flow Scale (per world unit)", Float) = 0.07
        _FlowSpeed ("Flow Speed", Float) = 0.12
        _VeinScale ("Vein Scale (per world unit)", Float) = 0.11
        _CrustAmount ("Crust Amount", Range(0, 1)) = 0.58

        [Header(Shore)]
        _ShoreCrust ("Shore Crust Width", Float) = 5
        _ShoreGlow ("Shore Glow Width", Float) = 1.6

        [Header(Pulse)]
        _PulseSpeed ("Pulse Speed", Float) = 1.3
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _HotColor, _LavaColor, _CoolColor, _CrustColor;
            float _FlowScale, _FlowSpeed, _VeinScale; half _CrustAmount;
            float _ShoreCrust, _ShoreGlow;
            half _PulseSpeed, _PulseAmount;
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
                output.shore = input.shore;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float t = _Time.y * _FlowSpeed;
                float3 p = input.positionOS * _FlowScale;

                // coulee: bruit deforme par un second bruit qui derive (la lave "coule" sans direction fixe)
                float3 warp = float3(WFCFbm(p + float3(t, 0, -t), 3), WFCFbm(p + float3(-t, t * 0.7, 3.1), 3), WFCFbm(p + float3(5.2, -t, t), 3));
                float flow = WFCFbm(p * 1.6 + warp * 2.2, 4);

                // plaques de croute qui flottent, veines brulantes entre elles
                float dist = input.shore.x;
                half shoreCrust = 1.0 - saturate(dist / max(_ShoreCrust, 0.001));
                half crustMask = smoothstep(1.0 - _CrustAmount - 0.05, 1.0 - _CrustAmount + 0.12, flow + shoreCrust * 0.45);
                float3 vp = input.positionOS * _VeinScale + warp * 1.5;
                half veins = pow(saturate(WFCRidge(vp + t * 0.5)), 10.0);

                half pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed + flow * 9.0);
                half3 molten = lerp(_CoolColor.rgb, _LavaColor.rgb, saturate(flow * 1.6 - 0.2));
                molten = lerp(molten, _HotColor.rgb, saturate(veins * 1.2 + pow(saturate(1.0 - flow * 1.4), 3.0) * 0.6)) * pulse;

                Light mainLight = WFCMainLight(input.positionWS, n);
                half lit = saturate(dot(n, mainLight.direction) * 0.5 + 0.5) * lerp(0.4, 1.0, mainLight.shadowAttenuation);
                half3 crust = _CrustColor.rgb * (mainLight.color * lit + SampleSH(n));
                // la croute garde des fissures incandescentes
                crust += _LavaColor.rgb * veins * 0.8 * pulse;

                half3 color = lerp(molten, crust, crustMask);
                // liseré brulant contre les rochers: la lave lèche la falaise
                half glow = 1.0 - smoothstep(0.0, _ShoreGlow, dist);
                color += _HotColor.rgb * glow * 0.5 * pulse;

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
