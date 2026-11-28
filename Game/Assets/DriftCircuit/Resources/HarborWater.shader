Shader "Drift Circuit/Harbor Water"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(float3 p : POSITION)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float wave = sin(dot(p, float2(0.45, 1.65)) + _Time.y * 0.88 +
                    sin(p.x * 0.37 + p.y * 0.13 - _Time.y * 0.56));
                float swell = sin(dot(p, float2(0.13, -0.66)) - _Time.y * 0.44);
                float waterDistance = distance(_WorldSpaceCameraPos, i.positionWS);
                float fade = 1 - smoothstep(36, 175, waterDistance);
                half ripple = smoothstep(0.7, 0.98, wave) * (0.5 + 0.5 * swell) * fade;
                float shore = length(max(abs(p - float2(2, 0)) - float2(99, 76), 0));
                half shallows = 1 - smoothstep(1, 15, shore);
                half3 color = lerp(half3(0.045, 0.159, 0.222), half3(0.065, 0.228, 0.255), shallows);
                color += ripple * half3(0.055, 0.075, 0.082);
                return half4(lerp(unity_FogColor.rgb, color, exp2(-pow(waterDistance * unity_FogParams.x, 2))), 1);
            }
            ENDHLSL
        }
    }
}
