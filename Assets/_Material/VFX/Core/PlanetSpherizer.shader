Shader "Custom/PlanetSpherizer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PlanetRadius ("Planet Radius", Float) = 50.0
        _PlanetCenter ("Planet Center", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float _PlanetRadius;
            float3 _PlanetCenter;

            Varyings vert(Attributes input)
            {
                Varyings output;

                // 1. Position du vertex en World Space (comme s'il était sur un sol plat)
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);

                // 2. On calcule la direction depuis le centre de la planète
                // On utilise X et Z pour la position "latérale" sur la sphère
                float3 dir = normalize(worldPos - _PlanetCenter);

                // 3. On définit la hauteur (Y) par rapport à la surface
                // On prend la coordonnée Y locale du mesh pour savoir de combien il "dépasse" du sol
                float height = input.positionOS.y; 

                // 4. La nouvelle position : Centre + Direction * (Rayon + Hauteur)
                float3 sphericalPos = _PlanetCenter + dir * (_PlanetRadius + height);

                // 5. Retour en Clip Space pour l'affichage
                output.positionCS = TransformWorldToHClip(sphericalPos);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return tex2D(_MainTex, input.uv);
            }
            ENDHLSL
        }
    }
}