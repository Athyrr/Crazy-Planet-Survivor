// nuages des planetes WFC: coque transparente au-dessus du sol, bruit anime, eclairee par le soleil. s'efface quand la camera
// est proche (vue de jeu) pour ne jamais cacher l'action: visible en orbite et dans le lobby. compatible Entities Graphics
Shader "WFC/Planet Clouds"
{
    Properties
    {
        _CloudColor ("Cloud", Color) = (1, 1, 1, 1)
        _ShadeColor ("Cloud Shade", Color) = (0.45, 0.55, 0.75, 1)
        _Coverage ("Coverage", Range(0, 1)) = 0.52
        _Softness ("Softness", Range(0.01, 0.5)) = 0.14
        _Scale ("Scale", Float) = 3.2
        _Speed ("Drift Speed", Float) = 0.012
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _FadeStart ("Camera Fade Start (shell radii)", Float) = 1.35
        _FadeEnd ("Camera Fade End (shell radii)", Float) = 1.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-20" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _CloudColor, _ShadeColor; half _Coverage, _Softness; float _Scale, _Speed; half _Opacity; float _FadeStart, _FadeEnd;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WFCPlanetCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 dirOS : TEXCOORD2;
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
                output.dirOS = normalize(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float t = _Time.y * _Speed;
                // rotation lente autour de l'axe Y + deformation: les nuages derivent et changent de forme
                float s = sin(t), c = cos(t);
                float3 d = float3(input.dirOS.x * c - input.dirOS.z * s, input.dirOS.y, input.dirOS.x * s + input.dirOS.z * c);
                float3 p = d * _Scale;
                float3 warp = float3(WFCNoise(p + 3.1 + t * 4.0), WFCNoise(p + 7.7 - t * 3.0), WFCNoise(p + 1.3)) - 0.5;
                float density = WFCFbm(p + warp * 1.2, 5);
                half cloud = smoothstep(_Coverage, _Coverage + _Softness, density);

                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float shellRadius = length(TransformObjectToWorld(float3(1, 0, 0)) - centerWS);
                half fade = saturate((distance(_WorldSpaceCameraPos, centerWS) / shellRadius - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));

                Light mainLight = GetMainLight();
                half ndl = saturate(dot(n, mainLight.direction) * 0.6 + 0.4);
                // cœur des nuages plus sombre que leurs bords (epaisseur)
                half3 color = lerp(_ShadeColor.rgb, _CloudColor.rgb, saturate(ndl * (1.2 - (density - _Coverage) * 1.5))) * mainLight.color;
                color += SampleSH(n) * 0.3;
                half alpha = saturate(cloud * _Opacity * fade * saturate(ndl * 1.5 + 0.15));
                color = saturate(color);
                if (any(isnan(color)) || isnan(alpha)) return half4(0, 0, 0, 0);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
