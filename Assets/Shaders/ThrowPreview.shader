Shader "Custom/ThrowPreview"
{
    // Flat unlit colour (vertex colour x _Color) for the throw path, its arrows and the landing ring (ThrowPreview).
    // Transparent, both sides; ZTest is set per material (the path draws over the carried crate, see ThrowPreview).
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _DepthPull("Depth Pull", Float) = 0.02
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4 // LessEqual
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+80" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ThrowPreview"
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _DepthPull;
                float _ZTest;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 viewWS = normalize(GetWorldSpaceViewDir(positionWS));
                output.positionCS = TransformWorldToHClip(positionWS + viewWS * _DepthPull);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
