// Subjective study: top-bottom stereo image on a quad (left eye in the top half), used for the
// random-dot stereogram screening. Each eye reads its own half via unity_StereoEyeIndex.
Shader "Pano2Stereo/StudyStereoQuadTB"
{
    Properties
    {
        _MainTex ("Top-bottom stereo image", 2D) = "gray" {}
        _PreviewEye ("Preview eye without XR (0 = left, 1 = right)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
            float _PreviewEye;

            Varyings vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float eye = _PreviewEye;
                #if defined(USING_STEREO_MATRICES)
                eye = unity_StereoEyeIndex;
                #endif
                float2 uv = input.uv;
                uv.y = uv.y * 0.5 + ((eye < 0.5) ? 0.5 : 0.0);
                return tex2D(_MainTex, uv);
            }
            ENDHLSL
        }
    }
}
