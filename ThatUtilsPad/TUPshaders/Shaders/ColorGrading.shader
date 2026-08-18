Shader "Hidden/TUPshaders/ColorGrading"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LutTex ("LUT", 3D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile __ TUP_STEREO
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half _Intensity, _Vibrance, _Saturation, _Contrast, _Gamma;
            half _Temperature, _Tint, _Lift, _Gain, _LutStrength;
            int _Tonemap;

            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

            // Branchless ACES approximation
            half3 Aces(half3 x)
            {
                const half a = 2.51h, b = 0.03h, c = 2.43h, d = 0.59h, e = 0.14h;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            half3 WhiteBalance(half3 c, half temp, half tint)
            {
                c.r += temp * 0.15h;
                c.b -= temp * 0.15h;
                c.g += tint * 0.1h;
                return c;
            }

            half3 VibranceSat(half3 c, half vib, half sat)
            {
                half luma = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                half maxc = max(c.r, max(c.g, c.b));
                half minc = min(c.r, min(c.g, c.b));
                half satMask = 1.h - saturate(maxc - minc);
                half3 vibC = lerp(luma.xxx, c, 1.h + vib * satMask);
                return lerp(luma.xxx, vibC, sat);
            }

            half4 frag(v2f i) : SV_Target
            {
                half3 col = tex2D(_MainTex, i.uv).rgb;
                half3 graded = col;

                graded = WhiteBalance(graded, _Temperature, _Tint);
                graded = graded * _Gain + _Lift;
                graded = VibranceSat(graded, _Vibrance, _Saturation);
                graded = (graded - 0.5h) * _Contrast + 0.5h;
                graded = pow(max(graded, 1e-4h), 1.h / max(_Gamma, 0.01h));

                // tonemap modes: 0 none, 1 ACES, 2 Reinhard
                half3 tmAces = Aces(graded);
                half3 tmRein = graded / (1.h + graded);
                half useAces = saturate(1.h - abs((half)_Tonemap - 1.h));
                half useRein = saturate(1.h - abs((half)_Tonemap - 2.h));
                half useNone = 1.h - max(useAces, useRein);
                graded = graded * useNone + tmAces * useAces + tmRein * useRein;

                col = lerp(col, graded, saturate(_Intensity));
                return half4(col, 1);
            }
            ENDCG
        }
    }
}
