Shader "Hidden/TUPshaders/SSAO"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _AOTex ("AO", 2D) = "white" {}
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
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D_float _CameraDepthTexture;
            float4 _MainTex_TexelSize;
            half _Intensity, _Radius;
            int _Samples;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

            half hash(half2 p) { return frac(sin(dot(p, half2(41.2h, 289.1h))) * 43758.5h); }

            half4 frag(v2f i) : SV_Target
            {
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
                half occ = 0;
                int s = clamp(_Samples, 4, 16);
                for (int k = 0; k < 16; k++)
                {
                    half active = k < s ? 1.h : 0.h;
                    half ang = (k + 0.5h) * 2.399963h; // golden angle
                    half2 dir = half2(cos(ang), sin(ang));
                    half2 uv = i.uv + dir * _Radius * _MainTex_TexelSize.xy * (1.h + hash(i.uv + k));
                    float sd = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                    half diff = saturate((depth - sd) * 2.h);
                    occ += diff * active;
                }
                occ = 1.h - saturate(occ / max((half)s, 1.h) * _Intensity);
                return half4(occ, occ, occ, 1);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _AOTex;
            half _Intensity;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 col = tex2D(_MainTex, i.uv).rgb;
                half ao = tex2D(_AOTex, i.uv).r;
                return half4(col * lerp(1.h, ao, _Intensity), 1);
            }
            ENDCG
        }
    }
}
