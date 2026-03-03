Shader "Custom/SpriteOutline"
{
    Properties
    {
        _MainTex  ("Base (RGB)", 2D) = "white" {}
        _Color    ("Outline Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4    _MainTex_TexelSize;
            fixed4    _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                half2  uv  : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);
                c.rgb *= c.a;   // pre-multiply alpha

                // Sample neighbours (one texel step in each direction)
                fixed n_up    = tex2D(_MainTex, i.uv + fixed2(0,  _MainTex_TexelSize.y)).a;
                fixed n_down  = tex2D(_MainTex, i.uv - fixed2(0,  _MainTex_TexelSize.y)).a;
                fixed n_right = tex2D(_MainTex, i.uv + fixed2(_MainTex_TexelSize.x, 0)).a;
                fixed n_left  = tex2D(_MainTex, i.uv - fixed2(_MainTex_TexelSize.x, 0)).a;

                // Outline pixel: current is transparent but any neighbour is opaque
                bool isOutline = (c.a == 0) && (n_up + n_down + n_right + n_left > 0);

                half4 outlineC = _Color;
                outlineC.rgb  *= outlineC.a;

                return lerp(c, outlineC, isOutline ? 1.0 : 0.0);
            }

            ENDCG
        }
    }

    FallBack "Sprites/Default"
}
