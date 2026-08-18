Shader "Hidden/TUPshaders/SSR"
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
            sampler2D_float _CameraDepthTexture;
            float4 _MainTex_TexelSize;
            half _Intensity;
            int _Steps;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 col = tex2D(_MainTex, i.uv).rgb;
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
                // Screen-space reflection approximation: march toward mirror UV
                half2 dir = normalize(half2(0.5h - i.uv.x, 0.15h));
                half3 refl = col;
                half hit = 0;
                int steps = clamp(_Steps, 4, 24);
                for (int k = 1; k <= 24; k++)
                {
                    half active = k <= steps ? 1.h : 0.h;
                    half2 uv = i.uv + dir * (k * 0.012h);
                    float sd = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                    half ok = (sd < depth - 0.05h) * active;
                    half3 sampleCol = tex2D(_MainTex, uv).rgb;
                    refl = lerp(refl, sampleCol, ok * (1.h - hit));
                    hit = max(hit, ok);
                }
                half gloss = saturate(1.h - depth * 0.01h);
                return half4(lerp(col, refl, hit * gloss * _Intensity), 1);
            }
            ENDCG
        }
    }
}
