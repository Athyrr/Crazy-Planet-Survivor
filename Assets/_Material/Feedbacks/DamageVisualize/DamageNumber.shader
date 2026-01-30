Shader "Custom/DamageNumbers"
{
    Properties {
        _MainTex ("Digit Atlas (0-9)", 2D) = "white" {}
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct DamageData {
                float3 position;
                float value;
                float startTime;
                int digitCount;
            };

            StructuredBuffer<DamageData> _DamageBuffer;
            sampler2D _MainTex;
            float _CurrentTime;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
            };

            v2f vert (uint id : SV_VertexID, uint inst : SV_InstanceID) {
                v2f o;
                DamageData data = _DamageBuffer[inst];
                
                // Calcul de la durée de vie (0 à 1)
                float age = _CurrentTime - data.startTime;
                float lifePct = saturate(age / 2.0); // Durée 2s
                
                // Animation : monte vers le haut
                float3 worldPos = data.position + float3(0, lifePct * 2.0, 0);
                
                // Logique pour séparer les chiffres (Simplifiée pour l'exemple)
                // En pratique, on dessinerait plusieurs instances par nombre ou on utiliserait un géométrie shader
                // Ici, on affiche le premier chiffre pour l'exemple de structure
                o.pos = UnityWorldToClipPos(float4(worldPos, 1.0));
                
                // Billboard simple (toujours face caméra)
                float2 quadUV = float2(id % 2, (id / 2) % 2);
                o.pos.xy += (quadUV - 0.5) * 0.2; 

                // o.uv = quadUV;
                // o.alpha = 1.0 - lifePct;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                // return tex2D(_MainTex, i.uv) * float4(1,1,1, i.alpha);
                return float4(1,1,1,1);
            }
            ENDCG
        }
    }
}