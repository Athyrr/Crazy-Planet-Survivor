Shader "Custom/DamageNumbers"
{
    Properties
    {
        _MainTex ("Digit Atlas (0-9)", 2D) = "white" {}
        _LifeTime ("Life Time", Float) = 1.0
        _FloatSpeed ("Float Speed", Float) = 1.0
        _EmissiveDuration ("Emissive Duration", Float) = 1.6
        _Color ("Color", Color) = (1,0,1,1)
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
            float4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                uint inst : TEXCOORD2;
            };

            v2f vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                v2f o;

                DamageData data = _DamageBuffer[inst];

                float age = _CurrentTime - data.startTime;
                float life01 = saturate(1.0 - age / _LifeTime);

                float baseSize = 0.3;
                float width = data.digitCount * baseSize;

                float2 quad[4] =
                {
                    float2(-width, -baseSize),
                    float2(-width,  baseSize),
                    float2( width, -baseSize),
                    float2( width,  baseSize)
                };

                float2 uvs[4] =
                {
                    float2(0,0),
                    float2(0,1),
                    float2(1,0),
                    float2(1,1)
                };

                int v = id & 3;

                float3 worldPos = data.position;
                worldPos.y += age * _FloatSpeed;

                o.pos = UnityObjectToClipPos(float4(worldPos + float3(quad[v], 0), 1));
                o.uv = uvs[v];
                o.alpha = life01;
                o.inst = inst;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                DamageData data = _DamageBuffer[i.inst];

                float number = data.value;
                float2 uv = i.uv;

                fixed4 col = fixed4(0,0,0,0);

                if (data.digitCount == 1)
                {
                    int digit = (int)floor(number);
                    float2 tc = float2(uv.x / 10.0 + digit / 10.0, uv.y);
                    col = tex2D(_MainTex, tc);
                }
                else
                {
                    int at = 0;

                    [unroll]
                    for (int d = MAX_DIGITS - 1; d >= 0; d--)
                    {
                        if (d >= data.digitCount) continue;

                        int digit = (int)floor(fmod(number / pow(10.0, d), 10.0));

                        float left  = step(at / (float)data.digitCount, uv.x);
                        float right = step(uv.x, (at + 1) / (float)data.digitCount);

                        float2 tc;
                        tc.x = uv.x * data.digitCount / 10.0 + digit / 10.0;
                        tc.y = uv.y;

                        col += left * right * tex2D(_MainTex, tc);
                        at++;
                    }
                }

                float age = _CurrentTime - data.startTime;
                float emissive = saturate(1.0 - age / _EmissiveDuration);

                col.a *= min(col.r, i.alpha);
                col.rgb *= _Color.rgb * emissive;

                return col;
            }
            ENDCG
        }
    }
}
