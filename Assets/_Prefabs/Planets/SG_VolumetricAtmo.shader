Shader "Hyverno/Planets/VolumetricAtmo"
{
    Properties
    {
        [Header(Colors)]
        [HDR] _AtmoColor        ("Atmo Color (in-scatter)", Color) = (0.35, 0.6, 1.0, 1.0)
        [HDR] _AtmoTint         ("Atmo Tint",               Color) = (1.0, 1.0, 1.0, 1.0)
        [HDR] _SunColor         ("Sun Color",               Color) = (1.0, 0.9, 0.75, 1.0)

        [Header(Geometry)]
        _PlanetCenter           ("Planet Center",     Vector) = (0, 0, 0, 0)
        _SunDirection           ("Sun Direction",     Vector) = (0, 1, 0, 0)
        _PlanetRadius           ("Planet Radius",     Float)  = 1.0
        _AtmoRadius             ("Atmo Radius",       Float)  = 1.15

        [Header(Fog Density)]
        _Density                ("Density",           Range(0, 10)) = 2.0
        _Falloff                ("Height Falloff",    Range(0, 16)) = 4.0
        _EdgeFade               ("Edge Fade",         Range(0, 1))  = 0.35

        [Header(Fog Noise)]
        _NoiseScale             ("Noise Scale",       Float)       = 2.0
        _NoiseStrength          ("Noise Strength",    Range(0, 1)) = 0.65
        _NoiseAnim              ("Noise Animation",   Float)       = 0.05
        [IntRange] _NoiseOctaves ("Noise Octaves",    Range(1, 4)) = 3

        [Header(Scattering)]
        _Intensity              ("Intensity",         Range(0, 8))  = 1.5
        _ScatteringPower        ("Forward Scattering (Mie)", Range(1, 32)) = 8.0
        _AmbientBoost           ("Ambient Boost",     Range(0, 2)) = 0.25

        [Header(Distance Fade)]
        _DistanceFadeStart      ("Fade Start (world)", Float)   = 0.0
        _DistanceFadeEnd        ("Fade End (world)",   Float)   = 1.0

        [Header(Raymarch)]
        [IntRange] _ViewSteps   ("View Steps",  Range(4, 96)) = 24
        [IntRange] _LightSteps  ("Light Steps", Range(1, 16)) = 4
        _Jitter                 ("Temporal Jitter", Range(0, 1)) = 1.0

        [Toggle(_USE_DEPTH_OCCLUSION)] _UseDepthOcclusion ("Occlude By Scene Depth", Float) = 1
        [Toggle(_LIGHT_MARCH)]         _UseLightMarch     ("Light Ray March",        Float) = 1
        [Toggle(_FOG_NOISE)]           _UseFogNoise       ("Enable Fog Noise",       Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+100"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardVolumetric"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_DEPTH_OCCLUSION
            #pragma shader_feature_local _LIGHT_MARCH
            #pragma shader_feature_local _FOG_NOISE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmoColor;
                float4 _AtmoTint;
                float4 _SunColor;
                float4 _PlanetCenter;
                float4 _SunDirection;
                float  _PlanetRadius;
                float  _AtmoRadius;
                float  _Density;
                float  _Falloff;
                float  _EdgeFade;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _NoiseAnim;
                float  _NoiseOctaves;
                float  _Intensity;
                float  _ScatteringPower;
                float  _AmbientBoost;
                float  _DistanceFadeStart;
                float  _DistanceFadeEnd;
                float  _ViewSteps;
                float  _LightSteps;
                float  _Jitter;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs v = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = v.positionCS;
                OUT.positionWS = v.positionWS;
                OUT.screenPos  = ComputeScreenPos(v.positionCS);
                return OUT;
            }

            // ---------- Utils ----------

            bool RaySphere(float3 ro, float3 rd, float3 center, float radius, out float tNear, out float tFar)
            {
                float3 oc = ro - center;
                float  b  = dot(oc, rd);
                float  c  = dot(oc, oc) - radius * radius;
                float  h  = b * b - c;
                if (h < 0.0) { tNear = 0; tFar = 0; return false; }
                h    = sqrt(h);
                tNear = -b - h;
                tFar  = -b + h;
                return true;
            }

            float Hash12(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 78.233);
                return frac(p.x * p.y);
            }

            // 3D value noise with smooth hermite interp — cheap enough for the inner light march.
            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0, 0, 0));
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float FBM(float3 p, int octaves)
            {
                float sum = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                [loop]
                for (int i = 0; i < octaves; i++)
                {
                    sum  += ValueNoise3D(p * freq) * amp;
                    freq *= 2.02;
                    amp  *= 0.5;
                }
                return sum;
            }

            // Normalized altitude 0 (surface) .. 1 (top of atmo).
            float AtmoAltitude(float3 posWS, out float shellH)
            {
                shellH = max(1e-4, _AtmoRadius - _PlanetRadius);
                float h = (length(posWS - _PlanetCenter.xyz) - _PlanetRadius) / shellH;
                return saturate(h);
            }

            // Local fog density with height falloff + edge fade + optional noise modulation.
            float SampleDensity(float3 posWS)
            {
                float shellH;
                float h = AtmoAltitude(posWS, shellH);

                // Exponential height falloff (thicker near surface).
                float density = exp(-_Falloff * h);

                // Smooth fade at inner (surface) and outer (space) shell boundaries.
                float edgeW   = max(1e-3, _EdgeFade);
                float innerF  = smoothstep(0.0, edgeW,        h);
                float outerF  = smoothstep(1.0, 1.0 - edgeW,  h);
                density      *= innerF * outerF;

                #if defined(_FOG_NOISE)
                    int oct = (int)clamp(_NoiseOctaves, 1, 4);
                    float3 np = (posWS - _PlanetCenter.xyz) * (_NoiseScale / max(0.001, shellH));
                    np += float3(0, _TimeParameters.x * _NoiseAnim, 0);
                    float n = FBM(np, oct) * 2.0 - 1.0; // -1..1
                    density *= saturate(1.0 + n * _NoiseStrength);
                #endif

                return density * _Density;
            }

            float OpticalDepthToSun(float3 posWS, float3 sunDir, int steps)
            {
                float tN, tF;
                if (!RaySphere(posWS, sunDir, _PlanetCenter.xyz, _AtmoRadius, tN, tF))
                    return 0.0;

                float t = max(0.0, tF);
                float stepLen = t / max(1.0, (float)steps);
                float sum = 0.0;
                float3 p = posWS + sunDir * (stepLen * 0.5);
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    sum += SampleDensity(p) * stepLen;
                    p   += sunDir * stepLen;
                }
                return sum;
            }

            // Henyey-Greenstein phase function.
            float PhaseHG(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(denom, 1e-4), 1.5));
            }

            // ---------- Fragment ----------

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 camPos = GetCameraPositionWS();
                float3 rayDir = normalize(IN.positionWS - camPos);

                float atmoNear, atmoFar;
                if (!RaySphere(camPos, rayDir, _PlanetCenter.xyz, _AtmoRadius, atmoNear, atmoFar))
                    return half4(0,0,0,0);

                atmoNear = max(atmoNear, 0.0);

                // Scene-depth occlusion.
                #if defined(_USE_DEPTH_OCCLUSION)
                    float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                    float  rawDepth = SampleSceneDepth(screenUV);
                    float  sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                    float3 viewVS   = mul((float3x3)UNITY_MATRIX_V, rayDir);
                    float  eyePerM  = max(1e-4, -viewVS.z);
                    float  sceneT   = sceneEye / eyePerM;
                    atmoFar = min(atmoFar, sceneT);
                #endif

                // Clip against the opaque planet.
                float surfNear, surfFar;
                if (RaySphere(camPos, rayDir, _PlanetCenter.xyz, _PlanetRadius, surfNear, surfFar))
                    atmoFar = min(atmoFar, max(0.0, surfNear));

                float through = max(0.0, atmoFar - atmoNear);
                if (through <= 1e-4) return half4(0,0,0,0);

                float3 sunDir = normalize(_SunDirection.xyz);

                // Phase: strong forward peak + mild ambient.
                float g       = saturate(1.0 - 1.0 / max(1.0, _ScatteringPower));
                float cosSun  = dot(-rayDir, sunDir);
                float phase   = PhaseHG(cosSun, g) * 4.0 * 3.14159265;

                int   viewSteps  = (int)clamp(_ViewSteps,  4, 96);
                int   lightSteps = (int)clamp(_LightSteps, 1, 16);
                float stepLen    = through / (float)viewSteps;

                float jitter = Hash12(IN.positionCS.xy + _TimeParameters.x) * _Jitter;
                float t      = atmoNear + stepLen * jitter;

                float3 scattered    = 0.0;
                float  transmittance = 1.0;

                [loop]
                for (int i = 0; i < viewSteps; i++)
                {
                    float3 p       = camPos + rayDir * t;
                    float  density = SampleDensity(p);

                    if (density > 1e-5)
                    {
                        float segOD = density * stepLen;

                        #if defined(_LIGHT_MARCH)
                            float sunOD = OpticalDepthToSun(p, sunDir, lightSteps);
                        #else
                            float shellH;
                            float sunAlt = AtmoAltitude(p + sunDir * (_AtmoRadius - _PlanetRadius), shellH);
                            float sunOD  = _Density * (1.0 - sunAlt) * shellH;
                        #endif

                        float3 sunAttn = exp(-sunOD * _AtmoColor.rgb);

                        float3 upDir    = normalize(p - _PlanetCenter.xyz);
                        float  dayNight = saturate(dot(upDir, sunDir) * 0.5 + 0.5);

                        float3 inScatter = _SunColor.rgb * phase * sunAttn * dayNight;
                        inScatter += _AtmoColor.rgb * _AtmoTint.rgb * _AmbientBoost * dayNight;

                        scattered    += inScatter * segOD * transmittance;
                        transmittance *= exp(-segOD);

                        if (transmittance < 0.005) break;
                    }

                    t += stepLen;
                }

                float camDist = length(camPos - _PlanetCenter.xyz);
                float distFade = 1.0;
                if (_DistanceFadeEnd > _DistanceFadeStart)
                    distFade = 1.0 - smoothstep(_DistanceFadeStart, _DistanceFadeEnd, camDist);

                float alpha = saturate((1.0 - transmittance) * _Intensity * distFade);
                float3 col  = scattered * _Intensity * distFade;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
