// Subjective study: top-bottom stereo equirectangular panorama (left eye in the top half).
// The ERP lookup is computed from the object-space direction of the sphere surface, so the mesh UVs do
// not matter: ERP column centre (u = 0.5) is +Z (content forward), longitude increases to the right (+X),
// latitude increases upward (+Y). Each eye reads its own half via unity_StereoEyeIndex; without XR the
// _PreviewEye property selects the half shown on the desktop.
Shader "Pano2Stereo/StudyStereoTB"
{
    Properties
    {
        _MainTex ("Top-bottom stereo ERP", 2D) = "gray" {}
        _Layout ("Layout (0 = top-bottom, 1 = mono)", Float) = 0
        _PreviewEye ("Preview eye without XR (0 = left, 1 = right)", Float) = 0
        _UseGray ("Show neutral gray", Float) = 1
        _Gray ("Gray level", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        Cull Off
        ZWrite Off
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Layout;
            float _PreviewEye;
            float _UseGray;
            float _Gray;

            Varyings vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.dir = input.vertex.xyz;
                return output;
            }

            fixed4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_UseGray > 0.5)
                {
                    return fixed4(_Gray, _Gray, _Gray, 1.0);
                }
                float eye = _PreviewEye;
                #if defined(USING_STEREO_MATRICES)
                eye = unity_StereoEyeIndex;
                #endif
                float3 d = normalize(input.dir);
                float lon = atan2(d.x, d.z);
                float lat = asin(clamp(d.y, -1.0, 1.0));
                float u = lon / (2.0 * UNITY_PI) + 0.5;
                float v = lat / UNITY_PI + 0.5;
                if (_Layout < 0.5)
                {
                    float halfTexel = 0.5 * _MainTex_TexelSize.y;
                    v = clamp(v * 0.5, halfTexel, 0.5 - halfTexel);
                    v += (eye < 0.5) ? 0.5 : 0.0;
                }
                return tex2Dlod(_MainTex, float4(u, v, 0.0, 0.0));
            }
            ENDHLSL
        }
    }
}
