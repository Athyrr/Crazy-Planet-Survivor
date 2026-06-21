//
//  OutlineInvisibleBase.shader
//
//  Base material for QuickOutline "outline proxy" GameObjects used to highlight pure-ECS lobby
//  buildings. QuickOutline appends its mask + fill passes onto whatever material a renderer already
//  has. We don't want the proxy to re-draw the building (the ECS entity already renders it), so the
//  base writes nothing visible (ColorMask 0) — only the appended outline passes show.
//
Shader "Hidden/QuickOutline/InvisibleBase"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "InvisibleBase"
            ColorMask 0
            ZWrite Off
            Cull Back

            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
