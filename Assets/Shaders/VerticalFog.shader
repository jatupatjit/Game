Shader "DontDropIt/VerticalFog"
{
    // Full-screen height fog. Thicker the lower you go: density = Density * exp(-Falloff * (y - FogHeight)).
    // The fog is integrated along each view ray up to the depth buffer, so looking down into the void fills with fog
    // while the islands above stay clear. Drawn by VerticalFog.cs on a mesh whose vertices are mapped straight to the screen.
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.72, 0.82, 0.95, 1)
        _SunColor ("Sun Glow Color", Color) = (1.0, 0.93, 0.8, 1)
        _SunGlow ("Sun Glow Strength", Range(0, 1)) = 0.35
        _FogHeight ("Fog Height (world Y)", Float) = 0
        _Density ("Density At Fog Height", Float) = 0.02
        _Falloff ("Height Falloff", Float) = 0.08
        _StartDistance ("Clear Distance Near Camera", Float) = 6
        _MaxOpacity ("Max Opacity", Range(0, 1)) = 1
        _SkyDistance ("Distance Used For Sky", Float) = 1500
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VerticalFog"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                half4 _SunColor;
                float _SunGlow;
                float _FogHeight;
                float _Density;
                float _Falloff;
                float _StartDistance;
                float _MaxOpacity;
                float _SkyDistance;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                // Ignore the transform: a 0..1 quad becomes the whole screen.
                float4 pos = float4(input.uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                if (_ProjectionParams.x < 0.0) pos.y = -pos.y;
                o.positionCS = pos;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float rawDepth = SampleSceneDepth(uv);
                float3 camPos = GetCameraPositionWS();

                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.000001;
                #else
                    bool isSky = rawDepth >= 0.999999;
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 toPoint = worldPos - camPos;
                float dist = length(toPoint);
                float3 dir = toPoint / max(dist, 1e-4);
                if (isSky) dist = _SkyDistance;

                // Skip the first few metres so the player and crate stay sharp.
                float len = max(0.0, dist - _StartDistance);
                float3 origin = camPos + dir * min(_StartDistance, dist);

                // Analytic integral of exponential height fog along the ray.
                float k = max(_Falloff, 1e-4);
                float startTerm = min(-k * (origin.y - _FogHeight), 60.0);
                float slope = -k * dir.y * len;
                float along = abs(slope) > 1e-3 ? (exp(min(slope, 60.0)) - 1.0) / slope : 1.0;
                float optical = _Density * exp(startTerm) * len * along;
                float fog = saturate(1.0 - exp(-optical)) * _MaxOpacity;

                Light sun = GetMainLight();
                float glow = pow(saturate(dot(dir, sun.direction)), 8.0) * _SunGlow;
                half3 col = lerp(_FogColor.rgb, _SunColor.rgb, glow);
                return half4(col, fog * _FogColor.a);
            }
            ENDHLSL
        }
    }
}
