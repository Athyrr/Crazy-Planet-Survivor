
Shader "Custom/VAT/ExampleShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VATTexture ("VAT Texture", 2D) = "white" {}
        _VATMinBounds ("Min Bounds", Vector) = (0,0,0,0)
        _VATMaxBounds ("Max Bounds", Vector) = (1,1,1,0)
        _VATFrame ("Current Frame", Float) = 0
        _VATSpeed ("Animation Speed", Float) = 1
        _MainTex_ST ("Main Tex Scale/Offset", Vector) = (1,1,0,0)
        _VATTexture_ST ("VAT Tex Scale/Offset", Vector) = (1,1,0,0)
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1; // Vertex ID as UV
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _VATTexture;
            float4 _VATTexture_ST;
            float3 _VATMinBounds;
            float3 _VATMaxBounds;
            float _VATFrame;
            float _VATSpeed;
            
            v2f vert (appdata v)
            {
                v2f o;
                
                // Calculate normalized time (0 to 1)
                float time = frac(_Time.y * _VATSpeed);
                
                // Get vertex offset from VAT texture
                // x = frame/time, y = vertex ID
                float2 vatUV = float2(time, v.uv2.y);
                float3 offsetColor = tex2Dlod(_VATTexture, float4(vatUV, 0, 0)).rgb;
                
                // Convert from normalized color back to offset
                float3 offset = lerp(_VATMinBounds, _VATMaxBounds, offsetColor);
                
                // Apply offset to vertex position
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                worldPos.xyz += offset;
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
