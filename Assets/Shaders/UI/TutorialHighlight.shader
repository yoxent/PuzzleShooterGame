Shader "UI/TutorialHighlight"
{
    Properties
    {
        _DimAlpha ("Dim Alpha", Range(0,1)) = 0.7
        _HighlightMask ("Highlight Mask", 2D) = "white" {}
        _HighlightScale ("Highlight Scale (X,Y)", Vector) = (1,1,0,0)
        _HighlightOffset ("Highlight Offset (X,Y)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _DimAlpha;
            sampler2D _HighlightMask;
            float4 _HighlightMask_ST;
            float2 _HighlightScale;
            float2 _HighlightOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the highlight mask texture using transformed UVs and additional scale.
                float2 maskUV = TRANSFORM_TEX(i.uv, _HighlightMask);

                // Scale around the center (0.5,0.5) so changing _HighlightScale
                // makes the mask appear larger/smaller without shifting its center.
                maskUV = (maskUV - 0.5) * _HighlightScale + 0.5;

                // Offset the mask in UV space to move the highlighted region.
                maskUV += _HighlightOffset;

                float mask = tex2D(_HighlightMask, maskUV).a;

                // Assume the mask alpha is 1 where we want the highlighted region (transparent),
                // and 0 where we want the dim. Invert to get dim amount.
                float dimAmount = 1.0 - mask;

                float alpha = _DimAlpha * saturate(dimAmount);

                return float4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}