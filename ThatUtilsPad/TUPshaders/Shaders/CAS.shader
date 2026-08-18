Shader "Hidden/TUPshaders/CAS"
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
            half _Sharpness, _Adaptive;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

            half luma(half3 c) { return dot(c, half3(0.2126h, 0.7152h, 0.0722h)); }

            half4 frag(v2f i) : SV_Target
            {
                half2 texel = _MainTex_TexelSize.xy;
                half3 c = tex2D(_MainTex, i.uv).rgb;
                half3 n = tex2D(_MainTex, i.uv + half2(0,  texel.y)).rgb;
                half3 s = tex2D(_MainTex, i.uv + half2(0, -texel.y)).rgb;
                half3 e = tex2D(_MainTex, i.uv + half2( texel.x, 0)).rgb;
                half3 w = tex2D(_MainTex, i.uv + half2(-texel.x, 0)).rgb;

                half lc = luma(c), ln = luma(n), ls = luma(s), le = luma(e), lw = luma(w);
                half mn = min(lc, min(min(ln, ls), min(le, lw)));
                half mx = max(lc, max(max(ln, ls), max(le, lw)));
                half amp = saturate(min(mn, 1.h - mx) / max(mx, 1e-4h));
                amp = sqrt(amp);
                half wgt = -_Sharpness * lerp(1.h, amp, _Adaptive) * 0.25h;

                half3 sharp = (n + s + e + w) * wgt + c * (1.h - 4.h * wgt);
                return half4(saturate(sharp), 1);
            }
            ENDCG
        }
    }
}
