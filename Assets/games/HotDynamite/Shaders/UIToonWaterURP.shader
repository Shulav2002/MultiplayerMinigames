Shader "UI/ToonWaterURP"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _BaseColor ("BaseColor", Color) = (0.08, 0.47, 0.78, 1)
        _WaveColor ("WaveColor", Color) = (0.20, 0.72, 0.95, 1)
        _HighlightColor ("HighlightColor", Color) = (0.78, 0.95, 1.00, 0.85)

        _WaveSpeed1 ("WaveSpeed1", Float) = 0.09
        _WaveSpeed2 ("WaveSpeed2", Float) = 0.05
        _NoiseScale1 ("NoiseScale1", Float) = 7.5
        _NoiseScale2 ("NoiseScale2", Float) = 14.0
        _WaveIntensity ("WaveIntensity", Float) = 1.15
        _HighlightIntensity ("HighlightIntensity", Float) = 0.20
        _RippleScale ("RippleScale", Float) = 9.0
        _RippleDensity ("RippleDensity", Float) = 2.6
        _RippleSpeed ("RippleSpeed", Float) = 0.55
        _RippleThickness ("RippleThickness", Range(0.01,0.35)) = 0.09
        _RippleWarp ("RippleWarp", Float) = 0.7
        _Animate ("Animate", Float) = 1
        _Opacity ("Opacity", Range(0,1)) = 0.90

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
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
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIToonWater"

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 positionXY : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _ClipRect;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _WaveColor;
                float4 _HighlightColor;
                float _WaveSpeed1;
                float _WaveSpeed2;
                float _NoiseScale1;
                float _NoiseScale2;
                float _WaveIntensity;
                float _HighlightIntensity;
                float _RippleScale;
                float _RippleDensity;
                float _RippleSpeed;
                float _RippleThickness;
                float _RippleWarp;
                float _Animate;
                float _Opacity;
            CBUFFER_END

            inline float2 WrapPeriod(float2 p, float period)
            {
                return p - floor(p / period) * period;
            }

            inline float Hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float NoiseTiled(float2 uv, float period)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);

                float a = Hash21(WrapPeriod(i + float2(0,0), period));
                float b = Hash21(WrapPeriod(i + float2(1,0), period));
                float c = Hash21(WrapPeriod(i + float2(0,1), period));
                float d = Hash21(WrapPeriod(i + float2(1,1), period));

                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            inline float UnityGet2DClipping(float2 position, float4 clipRect)
            {
                float2 inside = step(clipRect.xy, position) * step(position, clipRect.zw);
                return inside.x * inside.y;
            }

            inline float ToonBands(float v)
            {
                float band1 = smoothstep(0.28, 0.46, v);
                float band2 = smoothstep(0.58, 0.78, v);
                return saturate(band1 * 0.55 + band2 * 0.80);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionXY = v.positionOS.xy;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Animate;
                float2 uv = i.uv;
                const float TILE_PERIOD = 64.0;

                float2 flow1 = float2(0.28, 0.17) * (_WaveSpeed1 * t);
                float2 flow2 = float2(-0.21, 0.31) * (_WaveSpeed2 * t);

                float2 p = uv * _RippleScale;
                float warpA = NoiseTiled(p * 0.62 + flow1, TILE_PERIOD);
                float warpB = NoiseTiled(p * 1.08 + flow2, TILE_PERIOD);
                p += (float2(warpA, warpB) - 0.5) * _RippleWarp;

                float n1 = NoiseTiled(uv * _NoiseScale1 + flow1 * 0.65, TILE_PERIOD);
                float n2 = NoiseTiled(uv * _NoiseScale2 + flow2 * 0.85, TILE_PERIOD);
                float waveField = saturate((n1 * 0.56 + n2 * 0.44) * _WaveIntensity);
                float baseBands = ToonBands(waveField);

                float2 c1 = float2(1.8, 2.3) + flow1 * 0.7;
                float2 c2 = float2(5.4, 3.7) + flow2 * 0.8;
                float2 c3 = float2(3.6, 6.1) + (flow1 - flow2) * 0.55;

                float d1 = length(p - c1);
                float d2 = length(p - c2);
                float d3 = length(p - c3);

                float ring1 = abs(frac(d1 * _RippleDensity - t * _RippleSpeed) - 0.5);
                float ring2 = abs(frac(d2 * (_RippleDensity * 0.92) + t * (_RippleSpeed * 0.86)) - 0.5);
                float ring3 = abs(frac(d3 * (_RippleDensity * 1.04) - t * (_RippleSpeed * 1.12)) - 0.5);

                float line1 = smoothstep(_RippleThickness, 0.0, ring1);
                float line2 = smoothstep(_RippleThickness * 1.05, 0.0, ring2);
                float line3 = smoothstep(_RippleThickness * 0.95, 0.0, ring3);
                float rippleLines = saturate(max(line1, max(line2 * 0.92, line3 * 0.88)));

                float lineBreakup = NoiseTiled(p * 0.38 + float2(t * 0.13, -t * 0.11), TILE_PERIOD);
                rippleLines *= smoothstep(0.14, 0.96, lineBreakup);

                float shimmerNoise = NoiseTiled(uv * (_NoiseScale1 + _NoiseScale2) * 1.55 + float2(t * 0.48, -t * 0.43), TILE_PERIOD);
                float shimmer = smoothstep(0.90, 0.985, shimmerNoise) * (0.55 + 0.45 * sin(t * 3.6 + uv.x * 22.0 + uv.y * 18.0));

                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                half4 col = lerp(_BaseColor, _WaveColor, saturate(baseBands * 0.85 + 0.08));
                float whiteMask = saturate(rippleLines * (0.75 + 0.25 * baseBands));
                whiteMask = saturate(whiteMask + shimmer * 0.15);
                col.rgb = lerp(col.rgb, _HighlightColor.rgb, whiteMask * _HighlightIntensity);

                col *= sprite;
                col *= i.color;
                col.a = saturate(col.a * _Opacity + whiteMask * _HighlightColor.a * 0.18);

                #ifdef UNITY_UI_CLIP_RECT
                    col.a *= UnityGet2DClipping(i.positionXY, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
