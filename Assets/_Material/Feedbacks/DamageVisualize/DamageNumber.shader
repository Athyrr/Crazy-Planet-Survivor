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
                float value;
                float startTime;
                int digitCount;
                float critIntensity;
            };

            StructuredBuffer<DamageData> _DamageBuffer;

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

                DamageData data = _DamageBuffer[inst];

                float age = _CurrentTime - data.startTime;
                float life01 = saturate(1.0 - age / _LifeTime);
                
                // Crit Logic for Scale
                // if critIntensity <= 0, scale mult is 1.0
                // if critIntensity > 0, scale mult increases.
                // e.g. 1.0 intensity -> 1.5 scale. 2.0 intensity -> 2.0 scale.
                float critScaleMult = 1.0 + max(0, data.critIntensity * 0.5); 
                
                float currentScale = _Scale * critScaleMult;
                float width = data.digitCount * currentScale;

                float2 quad[4] =
                {
                    float2(-width, -currentScale),
                    float2(-width,  currentScale),
                    float2( width, -currentScale),
                    float2( width,  currentScale)
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
                DamageData data = _DamageBuffer[i.inst];

                float number = data.value -1;
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

                // Color Logic
                float4 finalColor = _Color;
                if (data.critIntensity > 0)
                {
                    // Yellow (Weak) -> Orange (Base) -> Red (Strong)
                    float4 colWeak   = float4(1.0, 1.0, 0.0, 1.0); // Yellow
                    float4 colBase   = float4(1.0, 0.6, 0.0, 1.0); // Orange
                    float4 colStrong = float4(1.0, 0.0, 0.0, 1.0); // Red

                    float t = data.critIntensity;
                    
                    // Remap t so 1.0 is the "center"
                    // 0.5 -> Weak
                    // 1.0 -> Base
                    // 1.5 -> Strong

                    if (t <= 1.0)
                    {
                        // Lerp Yellow -> Orange
                        // t goes 0.5 to 1.0
                        // (t - 0.5) / 0.5 => 0..1
                        // Clamp t to min 0.5 just in case
                        float lerpVal = saturate((t - 0.5) * 2.0);
                        finalColor = lerp(colWeak, colBase, lerpVal);
                    }
                    else
                    {
                        // Lerp Orange -> Red
                        // t goes 1.0 to 1.5
                        // (t - 1.0) / 0.5 => 0..1
                        float lerpVal = saturate((t - 1.0) * 2.0);
                        finalColor = lerp(colBase, colStrong, lerpVal);
                    }
                }

                col.a *= min(col.r, i.alpha);
                col.rgb *= finalColor.rgb * emissive;

                return col;
            }
            ENDCG
        }
    }
}
