#ifndef WFC_PLANET_PASSES_INCLUDED
#define WFC_PLANET_PASSES_INCLUDED

// passes communes des shaders de planete WFC: ombres, profondeur, normales (SSAO). le CBUFFER UnityPerMaterial est declare
// par chaque shader avant d'inclure ce fichier (meme layout dans toutes les passes: SRP Batcher et Entities Graphics)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct WFCAuxAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct WFCAuxVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

WFCAuxVaryings WFCShadowVert(WFCAuxAttributes input)
{
    WFCAuxVaryings output = (WFCAuxVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    output.positionCS = positionCS;
    return output;
}

half4 WFCShadowFrag(WFCAuxVaryings input) : SV_TARGET
{
    return 0;
}

WFCAuxVaryings WFCDepthVert(WFCAuxAttributes input)
{
    WFCAuxVaryings output = (WFCAuxVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

half WFCDepthFrag(WFCAuxVaryings input) : SV_TARGET
{
    return input.positionCS.z;
}

half4 WFCDepthNormalsFrag(WFCAuxVaryings input) : SV_TARGET
{
    return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
}

#endif
