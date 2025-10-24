Shader "Unlit/SurfaceWrapShader"
{
    Properties
    {
        _MainTex("Main texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (0, 0.5, 1, 0.5)
        _ScrollSpeedU("Scroll Speed U", float) = 0.1
        _ScrollSpeedV("Scroll Speed V", float) = 0.1
        _RotationSpeed("Rotation speed", float) = 0.1
        _Emissive("Emissive", float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off // Render front face only

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            //@todo enable material instance

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
            };

            float4 _PlanetCenter;
            float _PlanetRadius;
            
            sampler2D _MainTex;
            float4 _MainTex_ST; // Fix compile error
            half4 _BaseColor;
            float _Emissive;
            float _ScrollSpeedU;
            float _ScrollSpeedV;
            float _RotationSpeed;


            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // World space vertex position
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // Direction from planet center position to vertex position
                float3 direction = normalize(positionWS - _PlanetCenter.xyz);

                // Project vertex on planet surface based on its radius
                float3 newPositionWS = _PlanetCenter.xyz + direction * _PlanetRadius;

                // Set output clip pos
                OUT.positionCS = TransformWorldToHClip(newPositionWS);
                
                // Tiling and offset logic
                float2 uv = IN.uv;
                uv.x += _Time.y * _ScrollSpeedU;
                uv.y += _Time.y * _ScrollSpeedV;

                // @todo Rotation

                // Set output uv
                OUT.uv = TRANSFORM_TEX(uv, _MainTex);

                // Recalulate normal (planet center to vertex direction)
                OUT.normalWS = direction;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Sample texture
                half4 texColor = tex2D(_MainTex, IN.uv);
                
                //Set color on texture
                half4 finalColor = texColor * _BaseColor;
                
                // Emissive
                finalColor.rgb *= _Emissive;

                return finalColor;
            }
            ENDHLSL
        }
    }
}