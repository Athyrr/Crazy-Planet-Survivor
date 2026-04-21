Shader "UI/Blur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _SourceTex ("Source (Main Camera RT)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BlurSize ("Blur Size (px)", Range(0, 20)) = 4
        _Vibrancy ("Vibrancy", Range(0, 2)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        // utiliser pour exclude des layer
        //[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIBlur"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                float4 mask        : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_SourceTex);
            SAMPLER(sampler_SourceTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _SourceTex_ST;
                float4 _SourceTex_TexelSize;
                float4 _Color;
                float4 _TextureSampleAdd;
                float4 _ClipRect;
                float  _BlurSize;
                float  _Vibrancy;
                float  _UIMaskSoftnessX;
                float  _UIMaskSoftnessY;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.screenPos   = ComputeScreenPos(OUT.positionHCS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color * _Color;

                float2 pixelSize = OUT.positionHCS.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float2 maskSoftness = float2(_UIMaskSoftnessX, _UIMaskSoftnessY);
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.mask = float4(
                    IN.positionOS.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 + maskSoftness * pixelSize));
                return OUT;
            }

            half4 SampleBlur(float2 uv, float2 texel)
            {
                // 9-tap Gaussian
                half4 c = 0;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2(-1,-1)) * 0.0625;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2( 0,-1)) * 0.125;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2( 1,-1)) * 0.0625;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2(-1, 0)) * 0.125;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2( 0, 0)) * 0.25;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2( 1, 0)) * 0.125;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2(-1, 1)) * 0.0625;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2( 0, 1)) * 0.125;
                c += SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, uv + texel * float2( 1, 1)) * 0.0625;
                return c;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 texel    = _BlurSize / _ScreenParams.xy;

                half4 blurColor   = SampleBlur(screenUV, texel);
                half4 spriteColor = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) + _TextureSampleAdd) * IN.color;

                half3 rgb = blurColor.rgb * spriteColor.rgb;
                half  luma = dot(rgb, half3(0.299, 0.587, 0.114));
                rgb = lerp(luma.xxx, rgb, _Vibrancy);

                half4 color = half4(rgb, spriteColor.a);

                #ifdef UNITY_UI_CLIP_RECT
                    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                    color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }

    Fallback "UI/Default"
}
