Shader "Drift Circuit/Circuit Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.24, 0.49, 0.72, 1)
        _Horizon ("Horizon", Color) = (0.8, 0.88, 0.88, 1)
        _SunColor ("Sun", Color) = (1, 0.84, 0.57, 1)
        _SunDirection ("Sun direction", Vector) = (0, 1, 0, 0)
        _SunStrength ("Sun strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _SunColor;
                float4 _SunDirection;
                half _SunStrength;
            CBUFFER_END

            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(float3 p : POSITION)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(p);
                o.direction = p;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 direction = normalize(i.direction);
                half height = smoothstep(-0.065, 0.34, direction.y);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, height);
                half sun = max(0, dot(direction, normalize(_SunDirection.xyz)));
                sky += _SunStrength * _SunColor.rgb *
                    (0.25 * pow(sun, 28) + 0.7 * smoothstep(0.9991, 0.99955, sun));
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
