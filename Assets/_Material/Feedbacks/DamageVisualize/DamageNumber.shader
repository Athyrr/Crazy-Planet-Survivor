Shader "Custom/DamageNumbers"
{
    Properties
    {
        _MainTex ("Digit Atlas (0-9)", 2D) = "white" {}
        _LifeTime ("Life Time", Float) = 1.0
        _FloatSpeed ("Float Speed", Float) = 1.0
        _EmissiveDuration ("Emissive Duration", Float) = 1.6
        _Color ("Color", Color) = (1,0,1,1)
        _Scale ("Scale", Float) = 1.0
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
                float3 color;
                float value;
            };
            
            struct TimeData
            {
                float startTime;
            };

            StructuredBuffer<DamageData>    _DamageBuffer;
            StructuredBuffer<TimeData>      _TimeBuffer;

            sampler2D _MainTex;
            float _CurrentTime;
            float _LifeTime;
            float _FloatSpeed;
            float _EmissiveDuration;
            float4 _Color;
            float _Scale;

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

                DamageData damageData = _DamageBuffer[inst];
                TimeData timeData = _TimeBuffer[inst];
                
                int digitCount = floor(log10(abs(damageData.value))) + 1.0;
                
                float age = _CurrentTime - timeData.startTime;
                float life01 = saturate(1.0 - age / _LifeTime);
                
                float width = digitCount * _Scale;

                float2 quad[4] =
                {
                    float2(-width, -_Scale),
                    float2(-width,  _Scale),
                    float2( width, -_Scale),
                    float2( width,  _Scale)
                };

                float2 uvs[4] =
                {
                    float2(0,0),
                    float2(0,1),
                    float2(1,0),
                    float2(1,1)
                };

                int v = id & 3;

                float3 worldPos = damageData.position;
                worldPos.y += age * _FloatSpeed;

                float3 camRight = normalize(UNITY_MATRIX_V._m00_m01_m02);
                float3 camUp    = normalize(UNITY_MATRIX_V._m10_m11_m12);
                
                float3 offset =
                    camRight * quad[v].x +
                    camUp    * quad[v].y;

                float4 clipPos = mul(UNITY_MATRIX_VP, float4(worldPos + offset, 1.0));

                o.pos = clipPos;
                o.uv = uvs[v];
                o.alpha = life01;
                o.inst = inst;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                DamageData damageData = _DamageBuffer[i.inst];
                TimeData timeData = _TimeBuffer[i.inst];
                int digitCount = floor(log10(abs(damageData.value))) + 1.0;

                float number = damageData.value -1;
                float2 uv = i.uv;

                fixed4 col = fixed4(0,0,0,0);

                if (digitCount == 1)
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
                        if (d >= digitCount) continue;

                        int digit = (int)floor(fmod(number / pow(10.0, d), 10.0));

                        float left  = step(at / (float)digitCount, uv.x);
                        float right = step(uv.x, (at + 1) / (float)digitCount);

                        float2 tc;
                        tc.x = uv.x * digitCount / 10.0 + digit / 10.0;
                        tc.y = uv.y;

                        col += left * right * tex2D(_MainTex, tc);
                        at++;
                    }
                }

                float age = _CurrentTime - timeData.startTime;
                float emissive = saturate(1.0 - age / _EmissiveDuration);
                
                if (age > _LifeTime || age < 0) discard;

                col.a *= min(col.r, i.alpha);
                col.rgb *= damageData.color * emissive;

                return col;
            }
            ENDCG
        }
    }
}
