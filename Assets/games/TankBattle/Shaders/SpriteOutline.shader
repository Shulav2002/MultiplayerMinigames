Shader "Custom/SpriteOutline"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Fill Color", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1
    }
    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "IgnoreProjector"="True" 
            "RenderType"="Transparent" 
            "PreviewType"="Plane"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
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
                float4 color : COLOR;
            };
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                half4 fillColor = _Color * i.color;
                fillColor.rgb *= fillColor.a;
                c.rgb = fillColor.rgb * c.a;
                c.a *= fillColor.a;
                float stepX = _MainTex_TexelSize.x * _OutlineWidth;
                float stepY = _MainTex_TexelSize.y * _OutlineWidth;
                fixed alpha_up = tex2D(_MainTex, i.uv + fixed2(0, stepY)).a;
                fixed alpha_down = tex2D(_MainTex, i.uv - fixed2(0, stepY)).a;
                fixed alpha_right = tex2D(_MainTex, i.uv + fixed2(stepX, 0)).a;
                fixed alpha_left = tex2D(_MainTex, i.uv - fixed2(stepX, 0)).a;
                half4 outlineC = _OutlineColor * i.color;
                outlineC.rgb *= outlineC.a;
                bool isOutline = (c.a == 0) && (alpha_up + alpha_down + alpha_right + alpha_left > 0);
                return lerp(c, outlineC, isOutline ? 1.0 : 0.0);
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
