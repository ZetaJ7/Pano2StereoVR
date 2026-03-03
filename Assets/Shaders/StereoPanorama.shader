Shader "Pano2Stereo/StereoPanorama"
{
    Properties
    {
        _MainTex ("SBS Texture", 2D) = "black" {}
        _Gamma ("Gamma", Float) = 1.0
        _Mode ("Mode", Float) = 3
        _PreviewEye ("Preview Eye", Float) = 0
        _SwapEyes ("Swap Eyes", Float) = 0
        _FlipX ("Flip X", Float) = 1
        _FlipY ("Flip Y", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Front
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Gamma;
            float _PreviewEye;
            float _SwapEyes;
            float _FlipX;
            float _FlipY;

            Varyings vert(AppData input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                float eye = _PreviewEye;
                #if defined(UNITY_SINGLE_PASS_STEREO) || defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                eye = unity_StereoEyeIndex;
                #endif
                if (_SwapEyes > 0.5)
                {
                    eye = 1.0 - eye;
                }
                float2 uv = input.uv;
                float x = uv.x;
                if (_FlipX > 0.5)
                {
                    x = 1.0 - x;
                }
                uv.x = x * 0.5 + eye * 0.5;
                if (_FlipY > 0.5)
                {
                    uv.y = 1.0 - uv.y;
                }
                fixed4 color = tex2D(_MainTex, uv);
                float gamma = max(_Gamma, 1e-6);
                color.rgb = pow(max(color.rgb, 1e-6), 1.0 / gamma);
                return color;
            }
            ENDHLSL
        }
    }
}
