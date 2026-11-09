Shader "DontDropIt/HeightFog"
{
    // Soft vertical fog. Rings (mode 0): thick near the bottom of the mesh (uv.y = 0), gone at the top (uv.y = 1).
    // Discs (mode 1): thick in the middle, gone at the rim. Used for fog round the islands and the cloud sea below.
    Properties
    {
        _Color ("Color (A = density)", Color) = (0.9, 0.95, 1.0, 0.6)
        [Enum(Ring,0,Disc,1)] _Mode ("Shape", Float) = 0
        _NoiseScale ("Noise Scale", Float) = 0.04
        _NoiseSpeed ("Noise Speed", Float) = 0.6
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.5
        _SoftDistance ("Soft Edge On Geometry (m)", Float) = 4
        _CameraFade ("Fade Near Camera (m)", Float) = 8
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HeightFog"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Mode;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseStrength;
                float _SoftDistance;
                float _CameraFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float fogCoord : TEXCOORD3;
                float3 normalWS : TEXCOORD4;
            };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                o.uv = input.uv;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.screenPos = ComputeScreenPos(v.positionCS);
                o.fogCoord = ComputeFogFactor(v.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float shape;
                if (_Mode < 0.5)
                    shape = smoothstep(0.0, 0.2, i.uv.y) * (1.0 - smoothstep(0.25, 1.0, i.uv.y));
                else
                    shape = 1.0 - smoothstep(0.35, 1.0, length(i.uv - 0.5) * 2.0);

                float t = _Time.y * _NoiseSpeed;
                float2 p = (i.positionWS.xz + i.positionWS.y * 0.7) * _NoiseScale;
                float n = Noise(p + float2(t, t * 0.6)) * 0.6 + Noise(p * 2.3 - float2(t * 0.8, t * 0.3)) * 0.4;
                float noise = lerp(1.0, saturate(n * 1.6 - 0.2), _NoiseStrength);

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragEye = i.screenPos.w;
                float soft = saturate((sceneEye - fragEye) / max(0.01, _SoftDistance));
                float nearFade = saturate((fragEye - 1.0) / max(0.01, _CameraFade));

                // Fade where the surface turns edge-on, so a ring's sides never show a hard vertical edge.
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                float edge = _Mode < 0.5 ? smoothstep(0.15, 0.9, abs(dot(normalize(i.normalWS), viewDir))) : 1.0;
                half a = _Color.a * shape * noise * soft * nearFade * edge;
                half3 col = MixFog(_Color.rgb, i.fogCoord);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
