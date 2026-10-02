// halo d'atmosphere des planetes WFC: coque un peu plus grande que la planete, additive, plus dense au bord et cote soleil
// (diffusion vers l'avant au terminateur). compatible Entities Graphics
Shader "WFC/Planet Atmosphere"
{
    Properties
    {
        [HDR] _AtmoColor ("Atmosphere", Color) = (0.35, 0.65, 1.6, 1)
        [HDR] _SunsetColor ("Terminator / Sunset", Color) = (1.6, 0.6, 0.25, 1)
        _RimPower ("Rim Power", Range(0.5, 12)) = 7
        _Intensity ("Intensity", Range(0, 4)) = 0.9
        _NightSide ("Night Side Light", Range(0, 1)) = 0.08
        _Limb ("Planet Limb (N.V where the planet edge is)", Range(0.05, 0.95)) = 0.45
        _FadeStart ("Camera Fade Start (shell radii)", Float) = 1.3
        _FadeEnd ("Camera Fade End (shell radii)", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _AtmoColor, _SunsetColor; half _RimPower, _Intensity, _NightSide, _Limb; float _FadeStart, _FadeEnd;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // additif borne par l'alpha (couleur LDR x alpha <= 1): jamais de surexposition, avec ou sans tonemapping
            Blend SrcAlpha One
            ZWrite Off
            // faces arriere seulement: la planete (opaque, devant) les cache, le halo n'existe qu'autour de sa silhouette,
            // jamais en voile sur le disque
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight();
                half ndl = dot(n, mainLight.direction); // cote eclaire de la coque (normale exterieure)
                // halo: monte depuis le bord de la coque jusqu'au limbe de la planete, puis s'efface vers le centre du disque
                // face arriere: la normale s'eloigne de la camera. 0 au bord de la coque, limb au bord de la planete (au-dela: cache)
                half x = saturate(-dot(n, v));
                half limb = clamp(_Limb, 0.05, 0.95);
                half rim = pow(smoothstep(0.0, limb, x), max(_RimPower * 0.3, 1.0));
                // vue de jeu (camera pres du sol): la coque serait vue en rasant, elle disparait
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float shellRadius = length(TransformObjectToWorld(float3(1, 0, 0)) - centerWS);
                half fade = saturate((distance(_WorldSpaceCameraPos, centerWS) / shellRadius - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));
                half day = saturate(ndl * 0.8 + 0.35);
                // bande chaude au terminateur, surtout quand on regarde vers le soleil
                half terminator = (1.0 - abs(ndl)) * saturate(dot(-v, mainLight.direction) * 0.5 + 0.5);
                half3 color = _AtmoColor.rgb * (day + _NightSide) + _SunsetColor.rgb * terminator * terminator * 0.8;
                // teinte de la lumiere seulement (pas son intensite): la Scene view sans eclairage ou un soleil tres fort ne brulent pas le halo
                half3 lightTint = mainLight.color / max(1.0, max(mainLight.color.r, max(mainLight.color.g, mainLight.color.b)));
                color = saturate(color / max(1.0, max(color.r, max(color.g, color.b))) * lightTint);
                half alpha = saturate(rim * fade * _Intensity);
                if (any(isnan(color)) || isnan(alpha)) return half4(0, 0, 0, 0);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
