Shader "PathTracing/StandardGlassInstanced"
{
    Properties
    {

    }    
    
    SubShader
    {
        Pass
        {
            Name "PathTracing"
            Tags{ "LightMode" = "RayTracing" }

            HLSLPROGRAM
   
            #include "UnityRaytracingMeshUtils.cginc"
            #include "RayPayload.hlsl"
            #include "Utils.hlsl"
            #include "GlobalResources.hlsl"

            #pragma raytracing test
            
            #pragma shader_feature _FLAT_SHADING
 
            #pragma multi_compile _ INSTANCING_ON

            #if INSTANCING_ON
                // Unity built-in shader property and represents the index of the fist ray tracing Mesh instance in the TLAS.
                uint unity_BaseInstanceID;

            // How many ray tracing instances were added using RayTracingAccelerationStructure.AddInstances is used. Not used here.
            uint unity_InstanceCount;
            #endif

             struct Data
            {
                float3 albedo;
                float3 emission;
                float ior; // 1.0, 2.8
                float roughness; // 0, 0.5
                float extinctionCoeff; // 0, 20
                float flatShading; // bool
            };


            StructuredBuffer<Data> g_Data;

            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float3 position;
                float3 normal;
                float2 uv;
            };

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.uv = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(position);
                INTERPOLATE_ATTRIBUTE(normal);
                INTERPOLATE_ATTRIBUTE(uv);
                return v;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                if (payload.bounceIndexTransparent == g_BounceCountTransparent)
                {
                    payload.bounceIndexTransparent = -1;
                    return;
                }

                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

#if INSTANCING_ON
                uint instanceID = InstanceIndex() - unity_BaseInstanceID;

                Data instanceData = g_Data[instanceID];
#else
                Data instanceData = g_Data[InstanceID()];
#endif

                bool isFrontFace = HitKind() == HIT_KIND_TRIANGLE_FRONT_FACE;

                float3 roughness = instanceData.roughness * RandomUnitVector(payload.rngState);

                float3 localNormal = 0;
                if (instanceData.flatShading)
                {
                    float3 e0 = v1.position - v0.position;
                    float3 e1 = v2.position - v0.position;

                    localNormal = normalize(cross(e0, e1));
                }
                else {
                    localNormal = v.normal;
                }

                float normalSign = isFrontFace ? 1 : -1;

                localNormal *= normalSign;

                float3 worldNormal = normalize(mul(localNormal, (float3x3)WorldToObject()) + roughness);

                float3 reflectionRayDir = reflect(WorldRayDirection(), worldNormal);
                
                float indexOfRefraction = isFrontFace ? 1 / instanceData.ior : instanceData.ior;

                float3 refractionRayDir = refract(WorldRayDirection(), worldNormal, indexOfRefraction);
                
                float fresnelFactor = FresnelReflectAmountTransparent(isFrontFace ? 1 : instanceData.ior, isFrontFace ? instanceData.ior : 1, WorldRayDirection(), worldNormal);

                float doRefraction = (RandomFloat01(payload.rngState) > fresnelFactor) ? 1 : 0;

                float3 bounceRayDir = lerp(reflectionRayDir, refractionRayDir, doRefraction);

                float3 worldPosition = mul(ObjectToWorld(), float4(v.position, 1)).xyz;

                float pushOff = doRefraction ? -K_RAY_ORIGIN_PUSH_OFF : K_RAY_ORIGIN_PUSH_OFF;

                float3 albedo = !isFrontFace ? exp(-(1 - instanceData.albedo) * RayTCurrent() * instanceData.extinctionCoeff) : float3(1, 1, 1);

                payload.k                       = (doRefraction == 1) ? 1 - fresnelFactor : fresnelFactor;
                payload.albedo                  = albedo;
                payload.emission                = instanceData.emission;
                payload.bounceIndexTransparent  = payload.bounceIndexTransparent + 1;
                payload.bounceRayOrigin         = worldPosition + pushOff * worldNormal;
                payload.bounceRayDirection      = bounceRayDir;

                payload.meta.normal = worldNormal;
                payload.meta.albedo = albedo;
                payload.meta.emission = instanceData.emission;
                payload.meta.shape = float3(0, 1, 0);
                payload.meta.specular = float3(-1, -1, -1);
                payload.meta.extCoMetal = instanceData.extinctionCoeff;
                payload.meta.roughSmooth = instanceData.roughness;
                payload.meta.ior = instanceData.ior;
            }

            ENDHLSL
        }
    
    }

    CustomEditor "PathTracingSimpleGlassShaderGUI"
}