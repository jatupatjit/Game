Shader "Custom/CarryableOutline"
{
    Properties
    {
        [Header(Outline Settings)]
        _OutlineColor("Outline Color", Color) = (0.2, 1.0, 0.4, 1.0)
        _OutlineWidth("Outline Width", Range(0, 10)) = 3.5
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4 // LessEqual
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+100"
            "RenderPipeline"="UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormalOS : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // If smoothed normal was baked into TEXCOORD1, use it. Otherwise fallback to normalOS
                float3 norm = (dot(input.smoothNormalOS, input.smoothNormalOS) > 0.001) ? input.smoothNormalOS : input.normalOS;

                // Base clip-space position
                float4 posCS = TransformObjectToHClip(input.positionOS.xyz);

                // Normal transformed to clip space
                float3 normWS = TransformObjectToWorldNormal(norm);
                float3 normCS = TransformWorldToHClipDir(normWS);

                // Screen aspect ratio correction for uniform pixel thickness across all screen resolutions
                float aspect = (_ScreenParams.x > 0 && _ScreenParams.y > 0) ? (_ScreenParams.y / _ScreenParams.x) : 1.0;
                float2 screenDir = float2(normCS.x * aspect, normCS.y);
                float screenDirLen = length(screenDir);

                if (screenDirLen > 0.0001)
                {
                    float2 extDir = screenDir / screenDirLen;
                    // Extrude in 2D clip space (multiplied by posCS.w for perspective consistency)
                    posCS.xy += extDir * (_OutlineWidth * 0.0018 * posCS.w);
                }

                output.positionCS = posCS;
                output.color = _OutlineColor;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }

    // Fallback SubShader for standard/built-in rendering preview
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            Name "OutlineFallback"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float3 smoothNormal : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;

            v2f vert(appdata v)
            {
                v2f o;
                float3 norm = (dot(v.smoothNormal, v.smoothNormal) > 0.001) ? v.smoothNormal : v.normal;
                float4 clipPos = UnityObjectToClipPos(v.vertex);
                float3 normClip = UnityObjectToClipPos(float4(norm, 0)).xyz;
                float aspect = (_ScreenParams.x > 0 && _ScreenParams.y > 0) ? (_ScreenParams.y / _ScreenParams.x) : 1.0;
                float2 screenDir = float2(normClip.x * aspect, normClip.y);
                float screenDirLen = length(screenDir);

                if (screenDirLen > 0.0001)
                {
                    float2 extDir = screenDir / screenDirLen;
                    clipPos.xy += extDir * (_OutlineWidth * 0.0018 * clipPos.w);
                }

                o.pos = clipPos;
                o.color = _OutlineColor;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
