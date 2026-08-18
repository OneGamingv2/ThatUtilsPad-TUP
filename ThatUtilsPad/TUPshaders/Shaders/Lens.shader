Shader "Hidden/TUPshaders/Lens"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half _Intensity, _Vignette, _Grain, _Dirt, _Flare, _CA, _TimeSeed;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

            half hash(half2 p) { return frac(sin(dot(p, half2(12.9898h, 78.233h))) * 43758.5453h); }

            half4 frag(v2f i) : SV_Target
            {
                half2 uv = i.uv;
                half2 centered = uv - 0.5h;

                // Optional CA
                half2 caOff = centered * _CA * 0.01h;
                half r = tex2D(_MainTex, uv + caOff).r;
                half g = tex2D(_MainTex, uv).g;
                half b = tex2D(_MainTex, uv - caOff).b;
                half3 col = half3(r, g, b);

                // Vignette
                half vig = 1.h - dot(centered, centered) * (_Vignette * 2.5h);
                col *= lerp(1.h, saturate(vig), _Intensity);

                // Grain
                half n = hash(uv * half2(1920, 1080) + _TimeSeed);
                col += (n - 0.5h) * _Grain * _Intensity;

                // Soft dirt / hotspot approximation
                half dirt = hash(floor(uv * 24.h)) * hash(floor(uv * 7.h + 3.h));
                col += dirt * dirt * _Dirt * 0.15h * _Intensity;

                // Cheap anamorphic-ish flare streak
                half flare = exp(-abs(centered.y) * 18.h) * exp(-abs(centered.x) * 2.h);
                half bright = saturate(dot(col, half3(0.3h, 0.5h, 0.2h)) - 0.7h);
                col += flare * bright * _Flare * _Intensity * half3(1.0h, 0.85h, 0.6h);

                return half4(saturate(col), 1);
            }
            ENDCG
        }
    }
}
