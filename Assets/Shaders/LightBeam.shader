Shader "DontDropIt/LightBeam"
{
    // Additive glow for the lighthouse beam: bright at the lamp (uv.y = 0), gone at the tip (uv.y = 1).
    Properties
    {
        _Color ("Color", Color) = (1.0, 0.9, 0.55, 0.5)
        _CameraFade ("Fade Near Camera (m)", Float) = 6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "LightBeam"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _CameraFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float eye : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.uv = input.uv;
                o.eye = -v.positionVS.z;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float along = 1.0 - i.uv.y;
                float a = _Color.a * along * along * saturate(i.eye / max(0.01, _CameraFade));
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
