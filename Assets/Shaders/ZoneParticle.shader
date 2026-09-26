Shader "DontDropIt/ZoneParticle"
{
    Properties
    {
        _Color ("Tint Color", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "ParticleForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Procedural radial soft glowing particle dot
                float2 p = input.uv - 0.5;
                float dist = length(p) * 2.0;

                if (dist > 1.0)
                {
                    discard;
                }

                // Smooth falloff from center to edge with bright core
                float core = saturate(1.0 - dist * 1.8);
                float glow = saturate(1.0 - dist);
                glow = glow * glow;

                float intensity = core * 0.5 + glow * 0.8;
                half4 finalColor = input.color * _Color * intensity;
                return finalColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ParticleSRPUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - 0.5;
                float dist = length(p) * 2.0;
                if (dist > 1.0) discard;

                float core = saturate(1.0 - dist * 1.8);
                float glow = saturate(1.0 - dist);
                glow = glow * glow;

                float intensity = core * 0.5 + glow * 0.8;
                return input.color * _Color * intensity;
            }
            ENDHLSL
        }
    }
}
