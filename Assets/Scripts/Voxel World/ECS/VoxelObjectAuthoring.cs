using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoxelWorld.Rendering.Structs;

namespace VoxelWorld.ECS.VoxelObject
{
    public struct VoxelObjectComponent : IComponentData, IDisposable
    {
        public float voxelSize;
        public NativeParallelHashMap<int3, StandardMaterialData> standardVoxels;
        public NativeParallelHashMap<int3, GlassMaterialData> glassVoxels;

        public VoxelObjectComponent(float vs)
        {
            voxelSize = vs;
            standardVoxels = new NativeParallelHashMap<int3, StandardMaterialData>(1, Allocator.Persistent);
            glassVoxels = new NativeParallelHashMap<int3, GlassMaterialData>(1, Allocator.Persistent);
            
        }

        public float3 LocalGridToLocalWorld(int3 localGrid)
        {
            return (float3)localGrid * voxelSize;
        }

        public float3 LocalGridToWorldPos(int3 localGrid, float4x4 localToWorld)
        {
            return math.mul(localToWorld, new float4(LocalGridToLocalWorld(localGrid), 1)).xyz;
        }
    

        public void Dispose()
        {
            if (standardVoxels.IsCreated)
                standardVoxels.Dispose();
            
            if (glassVoxels.IsCreated)
                glassVoxels.Dispose();
        }
    }

    [InternalBufferCapacity(16)]
    public struct VoxelData : IBufferElementData
    {
        public int Value;
    }

    /*public class VoxelObjectAuthoring : MonoBehaviour
    {
        public float VoxelSize;
    }

    class VoxelObjectBaker : Baker<VoxelObjectAuthoring>
    {
        public override void Bake(VoxelObjectAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new VoxelObject
            {
                voxelSize = authoring.VoxelSize
            });
        }
    }*/
}