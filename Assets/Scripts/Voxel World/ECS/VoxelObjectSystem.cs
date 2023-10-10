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
using System;

namespace VoxelWorld.ECS.VoxelObject.Systems
{
    [BurstCompile]
    public partial struct VoxelObjectCreateSystem : ISystem
    {
        EntityQuery query_renderentities;
        RandomVoxelGenerator randomVoxGenerator;

        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {
            randomVoxGenerator = new RandomVoxelGenerator(24451245);
            query_renderentities = state.GetEntityQuery(ComponentType.ReadOnly<VoxelObjectComponent>(), ComponentType.ReadOnly<LocalToWorld>());
        }

        [BurstCompile]
        void ISystem.OnDestroy(ref SystemState state)
        {
            foreach (var vox in SystemAPI.Query<RefRW<VoxelObjectComponent>>())
            {
                vox.ValueRW.Dispose();
            }
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state) 
        {
            //if (ECSVoxelData.Instance == null) return;
            if (Input.GetMouseButtonDown(0))
            {
                Entity entity = state.EntityManager.CreateEntity();
                
                float voxelSize = 0.5f;

                #region Voxel Obj
                var voxObj = new VoxelObjectComponent(voxelSize);
                CreateRandomVoxelObject(ref voxObj);
                
                state.EntityManager.AddComponentData(entity, voxObj);
                state.EntityManager.AddComponentData(entity, new LocalTransform
                {
                    Position = randomVoxGenerator.RandPos(0, 10),
                    Rotation = quaternion.identity,
                    Scale = 1,
                });
                state.EntityManager.AddComponent(entity, typeof(LocalToWorld));

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
                #endregion

                #region physics
                BoxGeometry boxGeometry = new BoxGeometry
                {
                    Center = 0,
                    Size = voxelSize,
                    BevelRadius = 0.01f,
                    Orientation = quaternion.identity,
                };
                var collider = Unity.Physics.BoxCollider.Create(boxGeometry, CollisionFilter.Default);
                PhysicsCollider physicsCollider = new PhysicsCollider
                {
                    Value = collider
                };
                state.EntityManager.AddSharedComponentManaged(entity, new PhysicsWorldIndex { Value=0 });
                state.EntityManager.AddComponentData(entity, physicsCollider);
                state.EntityManager.AddComponentData(entity, new PhysicsVelocity
                {
                    Linear = 0,
                    Angular = 0,
                });

                state.EntityManager.AddComponentData(entity, new PhysicsGravityFactor
                {
                    Value = 0
                });

                
                PhysicsMass pm = PhysicsMass.CreateDynamic(physicsCollider.MassProperties, voxelSize * 1.2f);
                state.EntityManager.AddComponentData(entity, pm);

                state.EntityManager.AddComponentData(entity, new PhysicsDamping
                {
                    Linear = 0.01f
                });
                
                #endregion

                
                //ECSVoxelData.Instance.standardMap.Add(entity.Index, map);
            }

            int count = query_renderentities.CalculateEntityCount();
            #region Assembler
            RendererAssembler assembler = new RendererAssembler((int)count, (int)count, Allocator.TempJob);

            foreach (var (voxObj, trans) in SystemAPI.Query<RefRO<VoxelObjectComponent>, RefRO<LocalToWorld>>())
            {
                #region Standard voxels
                if (voxObj.ValueRO.standardVoxels.Count() > 0)
                {
                    
                    foreach (var voxKVP in voxObj.ValueRO.standardVoxels)
                    {
                        var t = voxObj.ValueRO.LocalGridToWorldPos(voxKVP.Key, trans.ValueRO.Value);
                        assembler.standardMaterialAssembly.Add(new StandardVoxelAssembledData
                        {
                            material = voxKVP.Value,
                            trs = float4x4.TRS(t, trans.ValueRO.Rotation, voxObj.ValueRO.voxelSize)
                        });
                    }
                }
                #endregion

                #region Glass voxels
                if (voxObj.ValueRO.glassVoxels.Count() > 0)
                {

                    foreach (var voxKVP in voxObj.ValueRO.glassVoxels)
                    {
                        var t = voxObj.ValueRO.LocalGridToWorldPos(voxKVP.Key, trans.ValueRO.Value);
                        assembler.glassMaterialAssembly.Add(new GlassVoxelAssembledData
                        {
                            material = voxKVP.Value,
                            trs = float4x4.TRS(t, trans.ValueRO.Rotation, voxObj.ValueRO.voxelSize)
                        });
                    }
                }
                #endregion
            }
            #endregion

            #region cache
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
            #endregion

            assembler.Dispose();
        }

        void CreateRandomVoxelObject(ref VoxelObjectComponent voxObj)
        {
            voxObj.standardVoxels.Add(0, new StandardMaterialData
            {
                albedo = randomVoxGenerator.RandColor(),
                specular = 0,
                emission = 0,
                smoothness = 0,
                metallic = 0,
                ior = 0
            });
            voxObj.glassVoxels.Add(new int3(1, 0, 0), new GlassMaterialData
            {
                albedo = randomVoxGenerator.RandColor(),
                emission = 0,
                roughness = 0.5f,
                extinctionCoeff = 1,
                ior = 2.0f
            });

        }

    }

    public struct RandomVoxelGenerator
    {
        public Unity.Mathematics.Random random;

        public RandomVoxelGenerator(uint seed)
        {
            random = new Unity.Mathematics.Random(seed);
        }
        public float3 RandColor()
        {
            int index = random.NextInt();
            return new float3(math.sin(index) * 0.5f + 0.5f, math.cos(index) * 0.5f + 0.5f, math.tan(index) * 0.5f + 0.5f);
        }

        public float3 RandSpecular()
        {
            return new float3(random.NextFloat(0f, 1f), random.NextFloat(0f, 1f), random.NextFloat(0f, 1f));
        }

        public float3 RandPos(float3 center, float radius)
        {
            return random.NextFloat3(0, 1) * radius + center;
        }
    }

    /*[BurstCompile]
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
            //if (ECSVoxelData.Instance == null) return;
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
    }*/

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