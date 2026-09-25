Shader "DontDropIt/PortalSwirl"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1, 0.35, 0.02, 0.95)
        _FlowColor ("Flow Color", Color) = (1, 0.75, 0.05, 0.95)
        _SparkColor ("Spark Color", Color) = (1, 0.98, 0.58, 1)
        _FlowSpeed ("Flow Speed", Float) = 1.7
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "PortalSwirl"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _FlowColor;
                half4 _SparkColor;
                float _FlowSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float x = uv.x * 2.0 - 1.0;
                float y = uv.y;

                // Rectangular lower opening with a rounded arch cap.
                float rectMask = step(abs(x), 0.96) * step(y, 0.58);
                float2 capPoint = float2(x / 0.96, (y - 0.58) / 0.42);
                float capDistance = length(capPoint) - 1.0;
                float capMask = step(0.58, y) * (1.0 - smoothstep(-0.015, 0.015, capDistance));
                float mask = saturate(max(rectMask, capMask));

                float2 flowPoint = float2(x, (y - 0.48) * 0.82);
                float radius = length(flowPoint);
                float angle = atan2(flowPoint.y, flowPoint.x);
                float time = _Time.y * _FlowSpeed;
                float ribbonA = sin(angle * 7.0 + radius * 24.0 - time * 1.7);
                float ribbonB = sin(angle * 3.0 - radius * 17.0 + time * 1.15);
                float ribbonC = sin(y * 31.0 + x * 8.0 + time * 1.4);
                float flow = saturate(0.5 + ribbonA * 0.25 + ribbonB * 0.18 + ribbonC * 0.14);
                float spark = smoothstep(0.90, 0.99, flow);

                half3 color = lerp(_CoreColor.rgb, _FlowColor.rgb, flow);
                color = lerp(color, _SparkColor.rgb, spark * 0.85);
                half alpha = mask * max(_CoreColor.a, 0.9h);
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
