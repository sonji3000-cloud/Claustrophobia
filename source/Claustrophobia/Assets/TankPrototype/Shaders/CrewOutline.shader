Shader "Tank/Crew Outline"
{
	Properties { _Width ("Width in pixels", Range(0, 6)) = 1.5 }
	SubShader
	{
		Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
		Pass
		{
			Name "Outline"
			Tags { "LightMode"="SRPDefaultUnlit" }
			Cull Front
			ZWrite Off
			ZTest LEqual
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			CBUFFER_START(UnityPerMaterial)
			float _Width;
			CBUFFER_END
			struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
			struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
				float4 posCS = TransformWorldToHClip(posWS);
				float2 dir = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy * _ScaledScreenParams.xy;
				dir /= max(length(dir), .0001);
				posCS.xy += dir * (2.0 * _Width / _ScaledScreenParams.xy) * posCS.w;
				output.positionCS = posCS;
				return output;
			}
			half4 Frag(Varyings input) : SV_Target { return half4(1, 1, 1, 1); }
			ENDHLSL
		}
	}
}
