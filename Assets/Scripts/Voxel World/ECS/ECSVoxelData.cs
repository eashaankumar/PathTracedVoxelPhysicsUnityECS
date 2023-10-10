using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VoxelWorld.Rendering.Structs;

namespace VoxelWorld.ECS.VoxelObject.MonoBehaviors
{
    public class ECSVoxelData : MonoBehaviour
    {
        /*public NativeParallelHashMap<int, NativeParallelHashMap<int3, StandardMaterialData>> standardMap;
        public NativeParallelHashMap<int, NativeParallelHashMap<int3, GlassMaterialData>> glassMap;

        public static ECSVoxelData Instance;

        // Start is called before the first frame update
        void Awake()
        {
            Instance = this;
            standardMap = new NativeParallelHashMap<int, NativeParallelHashMap<int3, StandardMaterialData>>(10000, Allocator.Persistent);
            glassMap = new NativeParallelHashMap<int, NativeParallelHashMap<int3, GlassMaterialData>>(10000, Allocator.Persistent);
        }

        // Update is called once per frame
        void OnDestroy()
        {
            UnloadStandardVoxelData();
            UnloadGlassVoxelData();
        }

        public void UnloadStandardVoxelData()
        {
            if (standardMap.IsCreated)
            {
                standardMap.Dispose();
                foreach (var kvp in standardMap)
                {
                    kvp.Value.Dispose();
                }
            }
        }

        public void UnloadGlassVoxelData()
        {
            if (glassMap.IsCreated)
            {
                glassMap.Dispose();
                foreach (var kvp in glassMap)
                {
                    kvp.Value.Dispose();
                }
            }
        }*/
    }
}
