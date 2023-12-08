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
using static Unity.Physics.CompoundCollider;

namespace VoxelWorld.ECS.VoxelObject.Systems
{
    [BurstCompile]
    public partial struct VoxelObjectCreateSystem : ISystem
    {
        //EntityQuery query_renderentities;
        RandomVoxelGenerator randomVoxGenerator;
        float lastTime;

        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {
            randomVoxGenerator = new RandomVoxelGenerator(24451245);
            //query_renderentities = state.GetEntityQuery(ComponentType.ReadOnly<VoxelObjectComponent>(), ComponentType.ReadOnly<LocalToWorld>());
            lastTime = Time.time;
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
            if (Input.GetMouseButton(0) && lastTime + 0.01f < Time.time)
            {
                lastTime = Time.time;
                CreateRandomVoxelObject(ref state); 
            }   

        }

        void CreateRandomVoxelObject(ref SystemState state)
        {
            Entity entity = state.EntityManager.CreateEntity();

            float voxelSize = 0.5f;

            #region Voxel Obj

            state.EntityManager.AddComponentData(entity, new LocalTransform
            {
                Position = randomVoxGenerator.RandPos(0, 10),
                Rotation = quaternion.identity,
                Scale = 1,
            });
            state.EntityManager.AddComponent(entity, typeof(LocalToWorld));

            var voxObj = new VoxelObjectComponent(voxelSize);
            FillRandomVoxelObject(ref state, entity, ref voxObj, state.EntityManager.GetComponentData<LocalToWorld>(entity));

            state.EntityManager.AddComponentData(entity, voxObj);

            #endregion

            #region physics

            state.EntityManager.AddSharedComponentManaged(entity, new PhysicsWorldIndex { Value = 0 });
            state.EntityManager.AddComponentData(entity, new PhysicsVelocity
            {
                Linear = 0,
                Angular = 0,
            });

            state.EntityManager.AddComponentData(entity, new PhysicsGravityFactor
            {
                Value = 0
            });


            state.EntityManager.AddComponentData(entity, new PhysicsDamping
            {
                Linear = 0.01f
            });

            #endregion
        }

        void FillRandomVoxelObject(ref SystemState state, Entity entity, ref VoxelObjectComponent voxObj, LocalToWorld parentL2W)
        {
            NativeList<ColliderBlobInstance> colliders = new NativeList<ColliderBlobInstance>(Allocator.Temp);
            int3 dim = randomVoxGenerator.random.NextInt3(new int3(-3, -2, -4), new int3(1, 3, 2));
            if (dim.x == 0) dim.x++;
            if (dim.y == 0) dim.y++;
            if (dim.z == 0) dim.z++;
            for(int x = 0; x < math.abs(dim.x); x++)
            {
                for (int y = 0; y < math.abs(dim.y); y++)
                {
                    for (int z = 0; z < math.abs(dim.z); z++)
                    {
                        int3 offset = new int3(x, y, z);
                        AddCollider(offset, ref state, entity, voxObj, parentL2W, ref colliders);
                    }
                }
            }
            var tempCollArray = colliders.ToArray(Allocator.Temp);
            var blobAssetRef = CompoundCollider.Create(tempCollArray);

            PhysicsCollider physicsCollider = new PhysicsCollider
            {
                Value = blobAssetRef
            };

            PhysicsMass pm = PhysicsMass.CreateDynamic(physicsCollider.MassProperties, voxObj.voxelSize * 2);

            state.EntityManager.AddComponentData(entity, physicsCollider);
            state.EntityManager.AddComponentData(entity, pm);
            
            tempCollArray.Dispose();
            colliders.Dispose();
        }

        void AddCollider(int3 grid, ref SystemState state, Entity parent, VoxelObjectComponent parentVoxObj, LocalToWorld parentL2W, ref NativeList<ColliderBlobInstance> colliders)
        {
            #region Voxel Engine Logic Stuff
            Entity entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent(entity, typeof(VoxelObjectColliderComponent));
            // parent it
            state.EntityManager.AddComponentData(entity, new Parent { Value = parent });
            LocalTransform ltrans = new LocalTransform
            {
                Position = parentVoxObj.LocalGridToLocalWorld(grid),
                Rotation = quaternion.identity,
                Scale = 0.5f,
            };
            state.EntityManager.AddComponentData(entity, ltrans);
            state.EntityManager.AddComponent(entity, typeof(LocalToWorld));
            #endregion

            #region Unity #CS Physics Stuff
            BoxGeometry boxGeometry = new BoxGeometry
            {
                Center = parentVoxObj.LocalGridToWorldPos(grid, parentL2W.Value),
                Size = parentVoxObj.voxelSize,
                BevelRadius = 0.01f,
                Orientation = parentL2W.Rotation,
            };
            var collider = Unity.Physics.BoxCollider.Create(boxGeometry, CollisionFilter.Default);
            ColliderBlobInstance colliderBlob = new ColliderBlobInstance()
            {
                Collider = collider,
                Entity = parent,
                CompoundFromChild = new RigidTransform
                {
                    rot = ltrans.Rotation,
                    pos = ltrans.Position
                }
            };
            colliders.Add(colliderBlob);
            state.EntityManager.AddSharedComponentManaged(entity, new PhysicsWorldIndex { Value = 0 });
            #endregion

            #region Material
            if (randomVoxGenerator.random.NextBool())
            {
                state.EntityManager.AddComponentData(entity, new StandardMaterialData
                {
                    albedo = randomVoxGenerator.RandColor(),
                    specular = 0,
                    emission = 0,
                    smoothness = 0,
                    metallic = 0,
                    ior = 0
                });
            }
            else
            {
                state.EntityManager.AddComponentData(entity, new GlassMaterialData
                {
                    albedo = randomVoxGenerator.RandColor(),
                    emission = 0,
                    roughness = 0.5f,
                    extinctionCoeff = 1,
                    ior = 2.0f
                });
            }
            #endregion
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
}