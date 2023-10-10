using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


namespace VoxelWorld.ECS.VoxelObject
{
    public struct VoxelObjectComponent : IComponentData
    {
        public float voxelSize;
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