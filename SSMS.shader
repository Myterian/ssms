Shader "Hidden/PostProcessing/SSMS"
{
	HLSLINCLUDE

	#include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
    #pragma multi_compile __ HIGH_QUALITY_UPSAMPLE

	TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
	TEXTURE2D_SAMPLER2D(_HighTex, sampler_HighTex);
    TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);

	float4 _MainTex_TexelSize;
    float4 _CameraDepthTexture_TexelSize;
	float _StartDistance;
    float _Density;
    float _Scatter;
	float _Blend;
	float _Intensity;
	half3 _Color;

    // Sample Depth with offet ( StartDistance ) and scale ( Density )
    half Depth( float2 uv )
    {
        half depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv));

        // "Scale and offset" basically
        depth = max( 0, depth - _StartDistance);
        depth = saturate( depth - _Density * depth );

        return depth;
    }

	// Prefilter: Apply depth as threshold to image.
	half4 FragPrefilter(VaryingsDefault i) : SV_Target
	{
        float2 uv = i.texcoord;
        // half depth = Depth(uv, false);

        // Small box blur. Helps to remove ambient occlusion like effect from the fog
        float4 d = _MainTex_TexelSize.xyxy * float4( -1, -1, 1, 1 ) * 10;
        half3 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xy).rgb * Depth(uv + d.xy); // -1, -1
        half3 c1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zy).rgb * Depth(uv + d.zy); //  1, -1
        half3 c2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xw).rgb * Depth(uv + d.xw); // -1,  1
        half3 c3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zw).rgb * Depth(uv + d.zw); //  1,  1
        half3 c4 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * Depth(uv);
        half3 c = max( max( max( c0, c1), max( c2, c3 ) ), c4 );

        // Greyscale
        c = max(max(c.r, c.g), c.b);

        return half4( c * _Color, 1 );
	}

	// Downsampler
	half4 FragDownsample(VaryingsDefault i) : SV_Target
	{
        float2 uv = i.texcoord;
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);

        // Simple box blur
        half3 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xy).rgb; // -1, -1
		half3 c1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zy).rgb; //  1, -1
		half3 c2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xw).rgb; // -1,  1
		half3 c3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zw).rgb; //  1,  1
        half3 c4 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;

        // Helps to remove ambient occlusion like effect from the fog
        half3 c = ( max( c0, c4) + max( c1, c4) + max( c2, c4) + max( c3, c4) ) / 4 ;

        return half4( c, 1 );
	}

	// Upsampler
	half4 FragUpsample(VaryingsDefault i) : SV_Target
	{
        float2 uv = i.texcoord;
        half3 highTex = SAMPLE_TEXTURE2D(_HighTex, sampler_HighTex, uv).rgb;

        #if HIGH_QUALITY_UPSAMPLE

            // High quality weighted blur
            half3 c4 = 0;
            float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);

            c4  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - d.xy).rgb;     //  1,  0
            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - d.wy).rgb * 2; // -1,  0
            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - d.zy).rgb;     // -1,  0

            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zw).rgb * 2; //  1,  0
            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv       ).rgb * 4;
            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xw).rgb * 2; // -1,  0

            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zy).rgb;     //  1, -1
            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.wy).rgb * 2; //  0, -1
            c4 += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xy).rgb;     // -1, -1

            half3 c = c4 / 16;
            return half4( highTex + c * ( 1 + _Scatter ), 1) / ( 1 + ( _Scatter * 0.735));

        #else

            // Box Blur
            float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
            half3 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xy).rgb; // -1, -1
            half3 c1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zy).rgb; //  1, -1
            half3 c2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.xw).rgb; // -1,  1
            half3 c3 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + d.zw).rgb; //  1,  1

            half3 c = ( c0 + c1 + c2 + c3 ) / 4;
            return half4( highTex + c * ( 1 + _Scatter ), 1) / ( 1 + ( _Scatter * 0.735));
        #endif
	}

	// Final composition
	half4 FragComposition(VaryingsDefault i) : SV_Target
	{
		half3 c0 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb;
		half3 c1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb;
		half3 c2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord).rgb;
		half3 c3 = SAMPLE_TEXTURE2D(_HighTex, sampler_HighTex, i.texcoord).rgb;
		half3 cf = (c0 + c1 + c2);

        return half4( lerp( c3, c0 * _Intensity, clamp( Depth(i.texcoord.xy), 0, _Blend) ), 1 );
	}

	ENDHLSL

	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertDefault
			#pragma fragment FragPrefilter
			ENDHLSL
		}
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertDefault
			#pragma fragment FragDownsample
			ENDHLSL
		}
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertDefault
			#pragma fragment FragUpsample
			ENDHLSL
		}
		Pass
		{
			HLSLPROGRAM
			#pragma vertex VertDefault
			#pragma fragment FragComposition
			ENDHLSL
		}
	}
}
