Shader "Foliage/Indirect_Rotation_Wind"
{
    Properties
    {
        // Added Culling Property (Default to 0 = Off for double-sided foliage)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 0
        
        // Distance Culling
        _CullDistance ("Cull Distance", Float) = 100.0
        _CullFade ("Cull Fade Range", Float) = 10.0

        _ColorA ("Color A", Color) = (1,0,0,1)
        _ColorB ("Color B", Color) = (1,1,1,1)
        _ZoneColor ("Zone Color", Color) = (0,1,0,1) // Couleur dans les zones d'influence
        
        // Wind properties
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.5
        _WindFrequency ("Wind Frequency", Range(0, 5)) = 1.0
        _WindTurbulence ("Wind Turbulence", Range(0, 3)) = 1.0
        _WindWaveScale ("Wind Wave Scale", Range(0.1, 10)) = 2.0
        _WindDirection ("Wind Direction", Vector) = (1,0,0,0)
        
        // Stem properties
        _StemStiffness ("Stem Stiffness", Range(0, 1)) = 0.7
        _LeafFlutter ("Leaf Flutter", Range(0, 2)) = 0.5
        
        // Zone properties
        _ZoneBlend ("Zone Blend Smoothness", Range(0.1, 10)) = 2.0
        _ZoneIntensity ("Zone Color Intensity", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            // Apply the Culling mode defined in Properties
            Cull [_Cull]

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct FoliageInstance
            {
                float3 position;
                float3 normal;
                float  scale;
                float3 rotation;
            };

            StructuredBuffer<FoliageInstance> _Instances;
            float4 _ColorA;
            float4 _ColorB;
            float4 _ZoneColor;
            
            // Wind properties
            float _WindStrength;
            float _WindFrequency;
            float _WindTurbulence;
            float _WindWaveScale;
            float4 _WindDirection;
            float _StemStiffness;
            float _LeafFlutter;

            // Culling properties
            float _CullDistance;
            float _CullFade;

            // Zone properties
            float _ZoneBlend;
            float _ZoneIntensity;

            // Liste des zones d'influence (position + radius)
            #define MAX_ZONES 32
            int _ZoneCount;
            float4 _ZonePositions[MAX_ZONES]; // xyz = position, w = radius
            float _ZoneRadii[MAX_ZONES];

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float3 nrm : TEXCOORD1;
                float3 worldPos : TEXCOORD2; // Position mondiale pour les calculs de zone
            };

            // Simple noise function for wind variation
            float noise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Smooth noise for more natural wind
            float smoothNoise(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                
                float a = noise(i);
                float b = noise(i + float2(1.0, 0.0));
                float c = noise(i + float2(0.0, 1.0));
                float d = noise(i + float2(1.0, 1.0));
                
                float2 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(a, b, u.x) + 
                      (c - a) * u.y * (1.0 - u.x) + 
                      (d - b) * u.x * u.y;
            }

            // Multi-octave noise for more interesting wind patterns
            float fractalNoise(float2 uv, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * smoothNoise(uv * frequency);
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                
                return value;
            }

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

            // Calculate wind offset for vertex
            float3 CalculateWindOffset(float3 worldPos, float3 vertex, float2 uv, float instanceScale)
            {
                // Base wind movement
                float time = _Time.y * _WindFrequency;
                
                // Global wind wave
                float2 windUV = worldPos.xz / _WindWaveScale + _WindDirection.xz * time;
                float globalWind = fractalNoise(windUV, 3) * 2.0 - 1.0;
                
                // Local turbulence based on vertex position
                float2 localUV = (worldPos.xz + vertex.xz) / (_WindWaveScale * 0.5) + _WindDirection.xz * time * 1.3;
                float localTurbulence = fractalNoise(localUV, 2) * 2.0 - 1.0;
                
                // Combine wind effects
                float windPower = globalWind * _WindStrength + localTurbulence * _WindTurbulence * 0.3;
                
                // Stem stiffness - less movement at the bottom
                float stemInfluence = pow(uv.y, _StemStiffness * 4.0);
                
                // Leaf flutter - high frequency movement for leaves
                float leafFlutter = sin(time * 8.0 + worldPos.x * 2.0 + worldPos.z * 2.0) * _LeafFlutter;
                
                // Calculate final wind offset
                float3 windOffset = _WindDirection.xyz * windPower * stemInfluence;
                windOffset += float3(leafFlutter * 0.1, 0, leafFlutter * 0.1) * uv.y;
                
                // Scale by instance size
                windOffset *= instanceScale;
                
                return windOffset;
            }

            // Calcule l'influence des zones sur la position actuelle
            float CalculateZoneInfluence(float3 worldPos)
            {
                float totalInfluence = 0.0;
                
                for (int i = 0; i < _ZoneCount && i < MAX_ZONES; i++)
                {
                    float4 zone = _ZonePositions[i];
                    float radius = _ZoneRadii[i];
                    
                    // Distance au centre de la zone
                    float distanceToZone = distance(worldPos, zone.xyz);
                    
                    // Calcul de l'influence (1 au centre, 0 au bord et au-delà)
                    float influence = 1.0 - smoothstep(0.0, radius, distanceToZone);
                    
                    // Appliquer un lissage supplémentaire si souhaité
                    influence = pow(influence, _ZoneBlend);
                    
                    totalInfluence = max(totalInfluence, influence);
                }
                
                return saturate(totalInfluence);
            }

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                FoliageInstance inst = _Instances[id];
                v2f o;

                // --- OPTIMIZATION CULLING ---
                float dist = distance(_WorldSpaceCameraPos, inst.position);
                
                // Culling complet au-delà de la distance
                if (dist >= _CullDistance)
                {
                    // Place le vertex hors de l'écran pour qu'il soit clipé
                    o.pos = float4(0, 0, 10000, 1); // Position hors écran
                    o.uv = float2(0,0);
                    o.nrm = float3(0,0,0);
                    o.worldPos = float3(0,0,0);
                    return o; 
                }

                // Fade progressif avant la disparition complète
                float cullScale = 1.0;
                if (dist > _CullDistance - _CullFade)
                {
                    cullScale = 1.0 - smoothstep(_CullDistance - _CullFade, _CullDistance, dist);
                    
                    // Si l'échelle est quasi nulle, on cull aussi
                    if (cullScale < 0.01)
                    {
                        o.pos = float4(0, 0, 10000, 1);
                        o.uv = float2(0,0);
                        o.nrm = float3(0,0,0);
                        o.worldPos = float3(0,0,0);
                        return o;
                    }
                }

                // Application de l'échelle de culling
                float finalScale = inst.scale * cullScale;

                // Create base rotation
                float3x3 rotationMatrix = CreateCombinedRotation(inst.rotation, inst.normal);
                
                // Transform vertex without wind
                float3 baseVertex = mul(rotationMatrix, v.vertex * finalScale);
                float3 worldPos = baseVertex + inst.position;
                
                // Calculate wind offset
                float3 windOffset = CalculateWindOffset(worldPos, v.vertex, v.uv, finalScale);
                
                // Apply wind to world position
                worldPos += windOffset;
                
                // Transform to clip space
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                // Simple normal adjustment
                o.nrm = normalize(mul(rotationMatrix, v.normal));
                o.uv = v.uv;
                o.worldPos = worldPos; // Stocke la position mondiale pour le fragment shader

                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Calcul de l'influence des zones
                float zoneInfluence = CalculateZoneInfluence(i.worldPos);
                
                // Blend factor original basé sur la hauteur UV
                float blendFactor = pow(saturate(uv.y), 2.0); // Quadratic curve
                
                // Couleur de base
                fixed4 baseColor = lerp(_ColorA, _ColorB, blendFactor);
                
                // Mélange avec la couleur de zone
                fixed4 finalColor = lerp(baseColor, _ZoneColor * _ZoneIntensity, zoneInfluence);
                
                return finalColor;
            }
            ENDCG
        }
    }
}