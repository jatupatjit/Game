Shader "Custom/HighlightXRay"
{
    // Flat see-through colour for gameplay highlights (GameplayHighlights). With ZTest Greater it draws only the
    // parts of a mesh that something else hides (team-mates and the crate behind walls); with LessEqual it is a
    // plain unlit marker (wall grab points). Skinned meshes work: it is an ordinary vertex transform.
    Properties
    {
        _Color("Color", Color) = (0.35, 0.75, 1, 0.55)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 5 // Greater
        // Moves each vertex this far toward the camera before the depth test, so a body's own limbs and a crate's
        // own planks never count as "behind something": only an obstacle more than this far in front does.
        _DepthPull("Depth Pull", Float) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+90" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "XRay"
            Cull Back
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float rim : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ZTest;
                float _DepthPull;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 viewWS = normalize(GetWorldSpaceViewDir(positionWS));
                output.positionCS = TransformWorldToHClip(positionWS + viewWS * _DepthPull);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                // Brighter towards the silhouette so the shape reads as a body, not a flat blob.
                output.rim = 1.0 - saturate(dot(normalWS, viewWS));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = _Color;
                color.a *= lerp(0.55, 1.0, input.rim);
                return color;
            }
            ENDHLSL
        }
    }
}
