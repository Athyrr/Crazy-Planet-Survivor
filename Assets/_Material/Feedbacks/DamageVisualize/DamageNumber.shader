Shader "Custom/DamageNumbers"
{
    Properties
    {
        _MainTex ("Digit Atlas (0-9)", 2D) = "white" {}
        _LifeTime ("Life Time", Float) = 1.0
        _FloatSpeed ("Float Speed", Float) = 1.0
        _EmissiveDuration ("Emissive Duration", Float) = 1.6
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"
            
            #define MAX_DIGITS 6

            struct DamageData
            {
                float3 position;
                float value;
                float startTime;
                int digitCount;
            };

            StructuredBuffer<DamageData> _DamageBuffer;

            sampler2D _MainTex;
            float _CurrentTime;
            float _LifeTime;
            float _FloatSpeed;
            float _EmissiveDuration;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float alpha : TEXCOORD1;
                uint inst : TEXCOORD2;
            };

            v2f vert (uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                v2f o;

                DamageData data = _DamageBuffer[inst];

                float age = _CurrentTime - data.startTime;
                float life01 = saturate(1.0 - age / _LifeTime);
                float base = 0.3f;
                float size = data.digitCount * base;

                float2 quad[4] = {
                    float2(-size, -base),
                    float2(-size,  base),
                    float2( size, -base),
                    float2( size,  base)
                };

                float2 uvs[4] = {
                    float2(0, 0),
                    float2(0, 1),
                    float2(1, 0),
                    float2(1, 1)
                };

                int vId = id & 3;

                float3 worldPos = data.position;
                worldPos.y += age * _FloatSpeed;

                o.pos = UnityObjectToClipPos(float4(worldPos + float3(quad[vId], 0), 1.0));
                o.uv = uvs[vId];
                o.alpha = life01;
                o.inst = inst;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                DamageData data = _DamageBuffer[i.inst];

                float number = data.value;

                float2 uv = i.uv;
                fixed4 finalColor = fixed4(0,0,0,0);

                if (int(data.digitCount) == 1)
                {
                    float digit = floor(number);
                    float2 texCoord = float2(uv.x / 10.0 + digit / 10.0, uv.y);
                    finalColor = tex2D(_MainTex, texCoord);
                }
                else
                {
                    int at = 1;
                    
                    [unroll]
                    for (int d = MAX_DIGITS; d >= 0; d--)
                    {
                        if (d > data.digitCount - 1) continue;
                        int digit = (int)floor(fmod(number / pow(10.0, d), 10.0));

                        float2 texCoord;
                        texCoord.x = uv.x * (data.digitCount + 1.0) / 10.0
                                   + (digit - at + 1.0) / 10.0;
                        texCoord.y = uv.y;

                        float left  = step((at - 1.0) / (data.digitCount + 1.0), uv.x);
                        float right = step(uv.x, at / (data.digitCount + 1.0));

                        finalColor += left * right * tex2D(_MainTex, texCoord);
                        at++;
                    }
                }

                int emissivePow = max(pow(i.alpha * _EmissiveDuration, 10), 1);
                
                finalColor.a *= min(finalColor.r, i.alpha);
                finalColor.r *= emissivePow;
                finalColor.g *= emissivePow;
                finalColor.b *= emissivePow;
                return finalColor;
            }
            ENDCG
        }
    }
}
