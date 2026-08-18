Shader "Hidden/TUPshaders/GodRays"
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
            half _Intensity;
            int _Samples;
            float4 _LightPos;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 col = tex2D(_MainTex, i.uv).rgb;
                half2 delta = (_LightPos.xy - i.uv) / (half)max(_Samples, 1);
                half2 uv = i.uv;
                half3 rays = 0;
                half decay = 1;
                int s = clamp(_Samples, 4, 24);
                for (int k = 0; k < 24; k++)
                {
                    half active = k < s ? 1.h : 0.h;
                    uv += delta;
                    half3 sCol = tex2D(_MainTex, uv).rgb;
                    half bright = saturate(dot(sCol, half3(0.3h, 0.5h, 0.2h)) - 0.75h);
                    rays += sCol * bright * decay * active;
                    decay *= 0.92h;
                }
                return half4(col + rays * _Intensity * 0.35h, 1);
            }
            ENDCG
        }
    }
}
