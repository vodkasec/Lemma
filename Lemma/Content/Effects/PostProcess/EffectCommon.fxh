#include "../Common.fxh"

// Transform matrices
float4x4 InverseViewProjectionMatrix;
float4x4 InverseProjectionMatrix;
float NearPlaneDistance;
float FarPlaneDistance;

float2 DestinationDimensions;

float2 SourceDimensions0;
texture2D SourceTexture0;
sampler2D SourceSampler0 = sampler_state
{
	Texture = <SourceTexture0>;
	MinFilter = point;
	MagFilter = point;
	MipFilter = point;
	MaxAnisotropy = 1;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

float2 SourceDimensions1;
texture2D SourceTexture1;
sampler2D SourceSampler1 = sampler_state
{
	Texture = <SourceTexture1>;
	MinFilter = point;
	MagFilter = point;
	MipFilter = point;
	MaxAnisotropy = 1;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

float2 SourceDimensions2;
texture2D SourceTexture2;
sampler2D SourceSampler2 = sampler_state
{
	Texture = <SourceTexture2>;
	MinFilter = point;
	MagFilter = point;
	MipFilter = point;
	MaxAnisotropy = 1;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

float2 SourceDimensions3;
texture2D SourceTexture3;
sampler2D SourceSampler3 = sampler_state
{
	Texture = <SourceTexture3>;
	MinFilter = point;
	MagFilter = point;
	MipFilter = point;
	MaxAnisotropy = 1;
	AddressU = CLAMP;
	AddressV = CLAMP;
};

struct PostProcessPSInput
{
	float2 texCoord : TEXCOORD0;
	float3 viewRay : TEXCOORD1;
	float3 viewSpacePosition : TEXCOORD2;
};

void PostProcessVS (	in float3 position : POSITION,
						in float3 texCoord : TEXCOORD0,
						out float4 outputPosition : POSITION,
						out PostProcessPSInput output)
{
	// Offset the position by half a pixel to correctly align texels to pixels
	outputPosition.x = position.x - (0.5f / DestinationDimensions.x);
	outputPosition.y = position.y + (0.5f / DestinationDimensions.y);
	outputPosition.z = position.z;
	outputPosition.w = 1.0f;
	
	// Pass along the texture coordinate and the world-space view ray
	output.texCoord = texCoord.xy;
	float4 v = mul(outputPosition, InverseViewProjectionMatrix);
	output.viewRay = (v.xyz / v.w) - CameraPosition;

	output.viewSpacePosition = mul(outputPosition, InverseProjectionMatrix);
}

// Reconstruct position from a linear depth
float3 PositionFromDepth(float depth, float3 viewRay)
{
	return CameraPosition + (viewRay * depth);
}

// Reconstruct position from a linear depth buffer
float3 PositionFromDepthSampler(sampler2D DepthSampler, float2 texCoord, float3 viewRay)
{
	return PositionFromDepth(tex2D(DepthSampler, texCoord).x, viewRay);
}