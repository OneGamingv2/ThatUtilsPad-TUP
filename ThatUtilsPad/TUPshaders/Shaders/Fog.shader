Shader "Hidden/TUPshaders/Fog"
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
            half _Intensity, _DistanceDensity, _Height, _HeightDensity;
            half4 _FogColor;
            float4x4 unity_CameraToWorld;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 col = tex2D(_MainTex, i.uv).rgb;
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
                half distFog = 1.h - exp(-_DistanceDensity * depth);
                // Approximate height fog without full world reconstruct (cheap)
                half heightFog = saturate(exp(-abs(_Height) * _HeightDensity) * (depth * 0.02h));
                half fog = saturate(max(distFog, heightFog) * _Intensity);
                return half4(lerp(col, _FogColor.rgb, fog), 1);
            }
            ENDCG
        }
    }
}
