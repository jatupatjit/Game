Shader "DontDropIt/DeliveryZoneRing"
{
    Properties
    {
        [HDR] _Color ("Zone Color", Color) = (1.0, 0.15, 0.15, 1.0)
        [HDR] _BaseColor ("Base Color", Color) = (1.0, 0.15, 0.15, 1.0)
        _InnerAlpha ("Inner Glow Alpha", Range(0, 1)) = 0.18
        _RingThickness ("Ring Thickness", Range(0.01, 0.5)) = 0.08
        _EdgeGlow ("Edge Glow Multiplier", Range(1, 10)) = 2.5
        _PulseSpeed ("Pulse Speed", Float) = 2.5
        _RotationSpeed ("Rotation Speed", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
        }

        Pass
        {
            Name "DeliveryZonePass"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(half4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _InnerAlpha)
                UNITY_DEFINE_INSTANCED_PROP(float, _RingThickness)
                UNITY_DEFINE_INSTANCED_PROP(float, _EdgeGlow)
                UNITY_DEFINE_INSTANCED_PROP(float, _PulseSpeed)
                UNITY_DEFINE_INSTANCED_PROP(float, _RotationSpeed)
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 zoneColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Color);
                float innerAlpha = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _InnerAlpha);
                float ringThickness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _RingThickness);
                float edgeGlow = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _EdgeGlow);
                float pulseSpeed = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PulseSpeed);
                float rotationSpeed = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _RotationSpeed);

                // UV from 0..1 to -1..1 centered at (0, 0)
                float2 p = input.uv * 2.0 - 1.0;
                float dist = length(p);

                // Discard pixels outside the unit circle with soft antialiased edge
                if (dist > 1.02)
                {
                    discard;
                }

                float outerEdge = 1.0;
                float innerEdge = outerEdge - ringThickness;

                // Circular outer ring
                float ring = smoothstep(innerEdge - 0.02, innerEdge + 0.01, dist) * smoothstep(outerEdge + 0.02, outerEdge - 0.01, dist);

                // Subtle dynamic energy pulse and rotation along the perimeter
                float angle = atan2(p.y, p.x);
                float pulse = 0.88 + 0.12 * sin(_Time.y * pulseSpeed);
                float energyWaves = 0.8 + 0.2 * sin(angle * 6.0 - _Time.y * rotationSpeed * 3.0);
                float dashes = 0.9 + 0.1 * cos(angle * 16.0 + _Time.y * rotationSpeed);

                // Inner soft floor glow
                float innerFill = smoothstep(innerEdge, 0.0, dist) * innerAlpha;

                // Center subtle radar ripple wave expanding outward
                float ripple = sin((dist - frac(_Time.y * 0.4)) * 12.566);
                float rippleMask = smoothstep(0.7, 1.0, ripple) * smoothstep(0.9, 0.1, dist) * 0.12;

                // Combine intensity
                float ringIntensity = ring * edgeGlow * energyWaves * dashes * pulse;
                float totalIntensity = ringIntensity + innerFill + rippleMask;

                half3 rgb = zoneColor.rgb * (ring * edgeGlow * energyWaves + 1.0);
                half alpha = saturate(ring * pulse * 0.95 + innerFill + rippleMask) * zoneColor.a;

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
