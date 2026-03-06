Shader "Pano2Stereo/MonoPanorama"
{
    Properties
    {
        _MainTex ("Mono Panorama", 2D) = "black" {}
        _Gamma ("Gamma", Float) = 1.0
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Gamma;
            float _FlipX;
            float _FlipY;

            Varyings vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                if (_FlipX > 0.5)
                {
                    uv.x = 1.0 - uv.x;
                }
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
