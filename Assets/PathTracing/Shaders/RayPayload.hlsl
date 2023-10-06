struct MetaPayload
{
    float3 normal;
    float3 albedo;
    float3 emission;
    float3 shape;

    float3 specular;
    float extCoMetal;
    float roughSmooth;
    float ior;
};

struct RayPayload
{
    float k;                // Energy conservation constraint
    float3 albedo;
    float3 emission;
    uint bounceIndexOpaque;
    uint bounceIndexTransparent;
    float3 bounceRayOrigin;
    float3 bounceRayDirection;
    uint rngState;          // Random number generator state.
    MetaPayload meta;
};