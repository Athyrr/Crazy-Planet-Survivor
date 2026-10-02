#ifndef WFC_PLANET_COMMON_INCLUDED
#define WFC_PLANET_COMMON_INCLUDED

// bruit procedural 3D (pas de texture): les planetes WFC n'ont pas d'UV utilisables, on echantillonne en espace objet

float WFCHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float WFCNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(WFCHash(i + float3(0, 0, 0)), WFCHash(i + float3(1, 0, 0)), f.x),
                     lerp(WFCHash(i + float3(0, 1, 0)), WFCHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(WFCHash(i + float3(0, 0, 1)), WFCHash(i + float3(1, 0, 1)), f.x),
                     lerp(WFCHash(i + float3(0, 1, 1)), WFCHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float WFCFbm(float3 p, int octaves)
{
    float sum = 0.0, amp = 0.5;
    for (int o = 0; o < octaves; o++)
    {
        sum += amp * WFCNoise(p);
        p = p * 2.03 + 17.1;
        amp *= 0.5;
    }
    return sum;
}

// cretes: 1 sur les lignes de passage a 0.5 du bruit (fissures, veines)
float WFCRidge(float3 p)
{
    return 1.0 - abs(WFCNoise(p) * 2.0 - 1.0);
}

half3 WFCSaturate(half3 c, half saturation, half contrast)
{
    half l = dot(c, half3(0.2126, 0.7152, 0.0722));
    c = lerp(l.xxx, c, saturation);
    return max(0, (c - 0.5) * contrast + 0.5);
}

// brume teintee par l'atmosphere: les terres lointaines (horizon de la petite planete) s'eclaircissent
half3 WFCHaze(half3 color, float3 positionWS, half4 hazeColor, float hazeStart, float hazeEnd)
{
    float d = distance(positionWS, _WorldSpaceCameraPos);
    half h = saturate((d - hazeStart) / max(hazeEnd - hazeStart, 0.001)) * hazeColor.a;
    return lerp(color, hazeColor.rgb, h * h);
}

// ombre recue sans acne: lecture decalee le long de la normale, et l'ombre portee s'efface la ou la face se detourne
// du soleil (la lumiere enveloppante y eclaire encore, l'ombre de la face elle-meme y donnait un tramage noir)
#if defined(UNIVERSAL_LIGHTING_INCLUDED)
Light WFCMainLight(float3 positionWS, float3 normalWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS + normalWS * 0.12);
    Light light = GetMainLight(shadowCoord);
    half ndl = dot(normalWS, light.direction);
    light.shadowAttenuation = lerp(1.0, light.shadowAttenuation, saturate(ndl * 6.0));
    return light;
}
#endif

#endif
