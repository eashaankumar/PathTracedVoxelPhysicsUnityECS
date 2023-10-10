using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;
using VoxelWorld.ECS.VoxelObject.MonoBehaviors;
using Unity.Transforms;
using VoxelWorld.Rendering.Structs;
using Unity.Jobs;

namespace VoxelWorld.ECS.VoxelObject.Systems
{
    [BurstCompile]
    public partial struct VoxelObjectCreateSystem : ISystem
    {
        
        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {
            

        }

        [BurstCompile]
        void ISystem.OnDestroy(ref SystemState state)
        {
            
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state) 
        {
            if (ECSVoxelData.Instance == null) return;
            if (Input.GetMouseButton(0))
            {
                Entity entity = state.EntityManager.CreateEntity();
                
                float voxelSize = 0.5f;
                state.EntityManager.AddComponentData(entity, new VoxelObjectComponent { voxelSize= voxelSize });
                state.EntityManager.AddComponentData(entity, new LocalToWorld { Value = float4x4.TRS(0, quaternion.identity, voxelSize) });

                #region physics
                BoxGeometry boxGeometry = new BoxGeometry
                {
                    Center = 0,
                    Size = voxelSize,
                    BevelRadius = 0.01f,
                    Orientation = quaternion.identity,
                };
                var collider = Unity.Physics.BoxCollider.Create(boxGeometry, CollisionFilter.Default);
                state.EntityManager.AddComponentData(entity, new PhysicsCollider
                {
                    Value = collider
                });
                state.EntityManager.AddComponentData(entity, new PhysicsVelocity
                {
                    Linear = 0,
                    Angular = 0,
                });

                state.EntityManager.AddComponentData(entity, new PhysicsGravityFactor
                {
                    Value = 1
                });
                #endregion

                var map = new NativeParallelHashMap<int3, StandardMaterialData>(100000, Allocator.Persistent);
                map.Add(0, new StandardMaterialData
                {
                    albedo = new float3(0.5f, 0, 0),
                    specular = 0,
                    emission = 0,
                    smoothness = 0,
                    metallic = 0,
                    ior = 0,
                });
                ECSVoxelData.Instance.standardMap.Add(entity.Index, map);
            }

            int count = 0;
            foreach(RefRO<VoxelObjectComponent> r in SystemAPI.Query<RefRO<VoxelObjectComponent>>())
            {
                count++;
            }
        }
    }

    [BurstCompile]
    public partial struct VoxelObjectRendererSystem : ISystem
    {
        EntityQuery query_renderentities;

        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {

            query_renderentities = state.GetEntityQuery(ComponentType.ReadOnly<VoxelObjectComponent>(), ComponentType.ReadOnly<LocalToWorld>());
        }

        [BurstCompile]
        void OnUpdate(ref SystemState state)
        {
            if (ECSVoxelData.Instance == null) return;
            // renderer
            RendererAssembler assembler = new RendererAssembler((int)100000, (int)100000, Allocator.TempJob);
            new AssemblerJob
            {
                standardMaterialAssembly = assembler.standardMaterialAssembly.AsParallelWriter(),
                glassMaterialAssembly = assembler.glassMaterialAssembly.AsParallelWriter(),
                standardMap = ECSVoxelData.Instance.standardMap,
                glassMap = ECSVoxelData.Instance.glassMap,
            }.ScheduleParallel(query_renderentities);

            if (ECSVoxelWorldRendererProvider.Instance.renderer.IsCreated)
            {
                ECSVoxelWorldRendererProvider.Instance.renderer.Dispose();
            }
            ECSVoxelWorldRendererProvider.Instance.renderer = new VoxelWorldInstancedRenderer(assembler.standardMaterialAssembly.Length, assembler.glassMaterialAssembly.Length, Allocator.Persistent);
            PopulateRendererCacheJob job = new PopulateRendererCacheJob
            {
                assembler = assembler,
                rendererCache = ECSVoxelWorldRendererProvider.Instance.renderer,
            };
            job.Schedule(assembler.standardMaterialAssembly.Length + assembler.glassMaterialAssembly.Length, 64).Complete();

            Debug.Log("Rendering: " + (ECSVoxelWorldRendererProvider.Instance.renderer.standardMaterialData.Length + ECSVoxelWorldRendererProvider.Instance.renderer.glassMaterialData.Length));

            assembler.Dispose();

            int count = query_renderentities.CalculateEntityCount();
            Debug.Log(count);
        }
    }

    [BurstCompile]
    struct PopulateRendererCacheJob : IJobParallelFor
    {
        [ReadOnly] public RendererAssembler assembler;
        [NativeDisableParallelForRestriction] public VoxelWorldInstancedRenderer rendererCache;
        public void Execute(int index)
        {
            if (index < assembler.standardMaterialAssembly.Length)
            {
                StandardVoxelAssembledData assembly = assembler.standardMaterialAssembly[index];
                rendererCache.standardMatrices[index] = assembly.trs;
                rendererCache.standardMaterialData[index] = assembly.material;
            }
            else if (index < assembler.standardMaterialAssembly.Length + assembler.glassMaterialAssembly.Length)
            {
                int idx = index - assembler.standardMaterialAssembly.Length;
                if (idx < 0 || idx >= assembler.glassMaterialAssembly.Length) return;
                GlassVoxelAssembledData assembly = assembler.glassMaterialAssembly[idx];
                rendererCache.glassMatrices[idx] = assembly.trs;
                rendererCache.glassMaterialData[idx] = assembly.material;
            }
        }
    }

    [BurstCompile]
    partial struct AssemblerJob : IJobEntity
    {
        [NativeDisableParallelForRestriction]
        public NativeList<StandardVoxelAssembledData>.ParallelWriter standardMaterialAssembly;
        [NativeDisableParallelForRestriction]
        public NativeList<GlassVoxelAssembledData>.ParallelWriter glassMaterialAssembly;
        [ReadOnly] public NativeParallelHashMap<int, NativeParallelHashMap<int3, StandardMaterialData>> standardMap;
        [ReadOnly] public NativeParallelHashMap<int, NativeParallelHashMap<int3, GlassMaterialData>> glassMap;

        void Execute([EntityIndexInQuery] int entityIndexInQuery, ref VoxelObjectComponent voc, in LocalToWorld trans)
        {
            if (standardMap.ContainsKey(entityIndexInQuery))
            {
                NativeParallelHashMap<int3, StandardMaterialData> map = standardMap[entityIndexInQuery];
                foreach(var kv in map)
                {
                    var mat = kv.Value;
                    standardMaterialAssembly.AddNoResize(new StandardVoxelAssembledData
                    {
                        material = mat,
                        trs = trans.Value
                    });
                }
            }
            else if (glassMap.ContainsKey(entityIndexInQuery))
            {
                NativeParallelHashMap<int3, GlassMaterialData> map = glassMap[entityIndexInQuery];
                foreach (var kv in map)
                {
                    var mat = kv.Value;
                    glassMaterialAssembly.AddNoResize(new GlassVoxelAssembledData
                    {
                        material = mat,
                        trs = trans.Value
                    });
                }
            }
        }
    }

    struct RendererAssembler : System.IDisposable
    {
        public NativeList<StandardVoxelAssembledData> standardMaterialAssembly;
        public NativeList<GlassVoxelAssembledData> glassMaterialAssembly;

        public RendererAssembler(int standardVoxels, int glassVoxels, Allocator alloc)
        {
            standardMaterialAssembly = new NativeList<StandardVoxelAssembledData>(standardVoxels, alloc);
            glassMaterialAssembly = new NativeList<GlassVoxelAssembledData>(glassVoxels, alloc);
        }

        public void Dispose()
        {
            if (standardMaterialAssembly.IsCreated) standardMaterialAssembly.Dispose();
            if (glassMaterialAssembly.IsCreated) glassMaterialAssembly.Dispose();
        }
    }

    public struct StandardVoxelAssembledData
    {
        public StandardMaterialData material;
        public Matrix4x4 trs;
    }

    public struct GlassVoxelAssembledData
    {
        public GlassMaterialData material;
        public Matrix4x4 trs;
    }
}