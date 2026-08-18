Shader "Hidden/TUPshaders/Bloom"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
        _DirtTex ("Dirt", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: bright pass + soft knee
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            half _Threshold, _SoftKnee;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 c = tex2D(_MainTex, i.uv).rgb;
                half br = max(c.r, max(c.g, c.b));
                half knee = _SoftKnee * 0.5h;
                half soft = saturate((br - _Threshold + knee) / max(_SoftKnee, 1e-4h));
                soft = soft * soft * knee;
                half contrib = max(br - _Threshold, soft);
                half w = contrib / max(br, 1e-4h);
                return half4(c * w, 1);
            }
            ENDCG
        }

        // 1: Kawase blur step
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half _Offset;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half2 o = _MainTex_TexelSize.xy * _Offset;
                half3 s = 0;
                s += tex2D(_MainTex, i.uv + half2( o.x,  o.y)).rgb;
                s += tex2D(_MainTex, i.uv + half2( o.x, -o.y)).rgb;
                s += tex2D(_MainTex, i.uv + half2(-o.x,  o.y)).rgb;
                s += tex2D(_MainTex, i.uv + half2(-o.x, -o.y)).rgb;
                return half4(s * 0.25h, 1);
            }
            ENDCG
        }

        // 2: composite
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _BloomTex, _DirtTex;
            half _Intensity, _DirtIntensity;
            struct v2f { float4 pos : SV_POSITION; half2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            half4 frag(v2f i) : SV_Target
            {
                half3 baseCol = tex2D(_MainTex, i.uv).rgb;
                half3 bloom = tex2D(_BloomTex, i.uv).rgb;
                half3 dirt = tex2D(_DirtTex, i.uv).rgb;
                bloom += bloom * dirt * _DirtIntensity;
                return half4(baseCol + bloom * _Intensity, 1);
            }
            ENDCG
        }
    }
}
