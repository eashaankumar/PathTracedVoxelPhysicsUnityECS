#include "RayPayload.hlsl"
#include "Utils.hlsl"


void MainMissShader0(inout RayPayload payload, float3 WorldRayDirection, float3 g_SkyboxBottomColor, float3 g_SkyboxTopColor)
{
    float dotProd = dot(WorldRayDirection, float3(0, 1, 0));
    float dotProdNorm = dotProd * 0.5 + 0.5;
    payload.emission = lerp(g_SkyboxBottomColor, g_SkyboxTopColor, dotProdNorm);
    payload.bounceIndexOpaque = -1;
    payload.bounceRayOrigin = 3.402823466e+38F;
    payload.albedo = -1;

    payload.meta.normal = WorldRayDirection;
    payload.meta.albedo = 0;
    payload.meta.emission = payload.emission;
    payload.k = 0;
}

