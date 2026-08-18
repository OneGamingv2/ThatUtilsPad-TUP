Shader "Hidden/TUPshaders/Ambient"
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
            half _Intensity, _ShadowBoost;
            half4 _AmbientTint;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 c = tex2D(_MainTex, i.uv).rgb;
                half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                half shadow = saturate(1.h - l);
                half3 lifted = c + _AmbientTint.rgb * shadow * _ShadowBoost;
                half3 ambient = lerp(c, lifted * lerp(1.h, 1.08h, _Intensity), _Intensity);
                return half4(ambient, 1);
            }
            ENDCG
        }
    }
}
