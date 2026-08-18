Shader "Hidden/TUPshaders/Sky"
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
            half _Intensity, _SunBoost;
            half4 _SkyTint, _SunTint;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 col = tex2D(_MainTex, i.uv).rgb;
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                half skyMask = raw < 1e-5h ? 1.h : 0.h; // far plane / clear
                // also treat very bright upper pixels as skyish
                half l = dot(col, half3(0.2126h, 0.7152h, 0.0722h));
                half upper = saturate((i.uv.y - 0.45h) * 3.h);
                half mask = saturate(skyMask + upper * saturate(l * 1.5h - 0.4h));
                half3 sky = lerp(col, col * _SkyTint.rgb * 1.1h, mask * _Intensity);
                half sun = saturate(l - 0.85h) * upper;
                sky = lerp(sky, sky * _SunTint.rgb * (1.h + _SunBoost), sun * _Intensity);
                return half4(sky, 1);
            }
            ENDCG
        }
    }
}
