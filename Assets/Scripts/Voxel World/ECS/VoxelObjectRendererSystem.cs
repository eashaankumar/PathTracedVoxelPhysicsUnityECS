using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VoxelWorld.Rendering.Structs;

namespace VoxelWorld.ECS.VoxelObject.Systems
{
    [BurstCompile]
    public partial struct VoxelObjectRendererSystem : ISystem
    {
        EntityQuery query_standardVoxels, query_glassVoxels;
        RendererAssembler assembler;

        [BurstCompile]
        void ISystem.OnCreate(ref SystemState state)
        {
            query_standardVoxels = state.GetEntityQuery(ComponentType.ReadOnly<StandardMaterialData>(), ComponentType.ReadOnly<LocalToWorld>());
            query_glassVoxels = state.GetEntityQuery(ComponentType.ReadOnly<GlassMaterialData>(), ComponentType.ReadOnly<LocalToWorld>());

        }

        [BurstCompile]
        void ISystem.OnDestroy(ref SystemState state)
        {
            assembler.Dispose();
        }

        [BurstCompile]
        void ISystem.OnUpdate(ref SystemState state) 
        {
            var standardVoxels = query_standardVoxels.ToComponentDataListAsync<StandardMaterialData>(Allocator.TempJob, out JobHandle stdVoxJH);
            var standardVoxelsTrans = query_standardVoxels.ToComponentDataListAsync<LocalToWorld>(Allocator.TempJob, out JobHandle stdVoxTransJH);

            var glassVoxels = query_glassVoxels.ToComponentDataListAsync<GlassMaterialData>(Allocator.TempJob, out JobHandle glassVoxJH);
            var glassVoxelsTrans = query_glassVoxels.ToComponentDataListAsync<LocalToWorld>(Allocator.TempJob, out JobHandle glassVoxTransJH);

            NativeArray<JobHandle> handles = new NativeArray<JobHandle>(
                new JobHandle[] { stdVoxJH, stdVoxTransJH, glassVoxJH, glassVoxTransJH }, Allocator.TempJob);
            var voxelQueriesHandles = JobHandle.CombineDependencies(handles);
            voxelQueriesHandles.Complete();

            // populate renderer
            if (ECSVoxelWorldRendererProvider.Instance.renderer.IsCreated)
            {
                ECSVoxelWorldRendererProvider.Instance.renderer.Dispose();
            }
            ECSVoxelWorldRendererProvider.Instance.renderer = new VoxelWorldInstancedRenderer(standardVoxels.Length, glassVoxels.Length, Allocator.TempJob);

            PopulateRendererCacheJobV2 job = new PopulateRendererCacheJobV2
            {
                stdVoxels = standardVoxels,
                glassVoxels = glassVoxels,
                stdVoxelsTrans = standardVoxelsTrans,
                glassVoxelsTrans = glassVoxelsTrans,
                rendererCache = ECSVoxelWorldRendererProvider.Instance.renderer,
            };

            job.Schedule(standardVoxelsTrans.Length + glassVoxelsTrans.Length, 64).Complete();

            standardVoxels.Dispose();
            standardVoxelsTrans.Dispose();
            glassVoxels.Dispose();
            glassVoxelsTrans.Dispose();
            handles.Dispose();

            /*//int count = query_renderentities.CalculateEntityCount();
            #region Assembler
            var vobjs = query_renderentities.ToComponentDataListAsync<VoxelObjectComponent>(Allocator.TempJob, out JobHandle vobjh);
            var ltws = query_renderentities.ToComponentDataListAsync<LocalToWorld>(Allocator.TempJob, out JobHandle ltwjh);
            var queryJobHandle = JobHandle.CombineDependencies(vobjh, ltwjh);

            queryJobHandle.Complete();

            NativeSum stdCounter = new NativeSum(Allocator.Temp);
            NativeSum glassCounter = new NativeSum(Allocator.Temp);

            CountMaterialsJob countMatJob = new CountMaterialsJob
            {
                vobjs = vobjs,
                standardMatCounter = stdCounter,
                glassMatCounter = glassCounter,
            };

            var jobHandle = countMatJob.Schedule(vobjs.Length, 64);
            jobHandle.Complete();

            if (!assembler.IsCreated)
            {
                assembler = new RendererAssembler(stdCounter.Total, glassCounter.Total, Allocator.Persistent);
            }
            else
            {
                assembler.Resize(stdCounter.Total, glassCounter.Total);
                assembler.Clear();
            }

            stdCounter.Dispose();
            glassCounter.Dispose();

            FillAssemblerJob faj = new FillAssemblerJob
            {
                vobjs = vobjs,
                ltws = ltws,
                standardMaterialAssembly = assembler.standardMaterialAssembly,
                glassMaterialAssembly = assembler.glassMaterialAssembly,
            };

            jobHandle = faj.Schedule(vobjs.Length, 64);
            jobHandle.Complete();

            vobjs.Dispose();
            ltws.Dispose();
            #endregion


            #region cache
            if (ECSVoxelWorldRendererProvider.Instance.renderer.IsCreated)
            {
                ECSVoxelWorldRendererProvider.Instance.renderer.Dispose();
            } 
            ECSVoxelWorldRendererProvider.Instance.renderer = new VoxelWorldInstancedRenderer(assembler.standardMaterialAssembly.Length, assembler.glassMaterialAssembly.Length, Allocator.TempJob);

            PopulateRendererCacheJob job = new PopulateRendererCacheJob
            {
                assembler = assembler,
                rendererCache = ECSVoxelWorldRendererProvider.Instance.renderer,
            };
            job.Schedule(assembler.standardMaterialAssembly.Length + assembler.glassMaterialAssembly.Length, 64).Complete();
            #endregion    
            */
        }
    }
    [BurstCompile]
    struct CountMaterialsJob : IJobParallelFor
    {
        [ReadOnly] public NativeList<VoxelObjectComponent> vobjs;
        public NativeSum.ParallelWriter standardMatCounter;
        public NativeSum.ParallelWriter glassMatCounter;

        public void Execute(int index)
        {
            var voxObj = vobjs[index];
            standardMatCounter.Add(voxObj.standardVoxels.Count);
            glassMatCounter.Add(voxObj.glassVoxels.Count);
        }
    }

    [BurstCompile]
    public partial struct FillAssemblerJob : IJobParallelFor
    {
        [ReadOnly] public NativeList<VoxelObjectComponent> vobjs;
        [ReadOnly] public NativeList<LocalToWorld> ltws;
        [NativeDisableParallelForRestriction] public NativeList<StandardVoxelAssembledData> standardMaterialAssembly;
        [NativeDisableParallelForRestriction] public NativeList<GlassVoxelAssembledData> glassMaterialAssembly;

        public void Execute(int index)
        {
            {
                var voxObj = vobjs[index];
                var trans = ltws[index];
                foreach (var voxKVP in voxObj.standardVoxels)
                {
                    var t = voxObj.LocalGridToWorldPos(voxKVP.Key, trans.Value);
                    standardMaterialAssembly.Add(new StandardVoxelAssembledData
                    {
                        material = voxKVP.Value,
                        trs = float4x4.TRS(t, trans.Rotation, voxObj.voxelSize)
                    });
                }

                foreach (var voxKVP in voxObj.glassVoxels)
                {
                    var t = voxObj.LocalGridToWorldPos(voxKVP.Key, trans.Value);
                    glassMaterialAssembly.Add(new GlassVoxelAssembledData
                    {
                        material = voxKVP.Value,
                        trs = float4x4.TRS(t, trans.Rotation, voxObj.voxelSize)
                    });
                }
            }
        }
    }

    [BurstCompile]
    struct PopulateRendererCacheJobV2 : IJobParallelFor
    {
        [ReadOnly] public NativeList<StandardMaterialData> stdVoxels;
        [ReadOnly] public NativeList<GlassMaterialData> glassVoxels;
        [ReadOnly] public NativeList<LocalToWorld> stdVoxelsTrans;
        [ReadOnly] public NativeList<LocalToWorld> glassVoxelsTrans;

        [NativeDisableParallelForRestriction] public VoxelWorldInstancedRenderer rendererCache;
        public void Execute(int index)
        {
            if (index < stdVoxelsTrans.Length)
            {
                rendererCache.standardMatrices[index] = stdVoxelsTrans[index].Value;
                rendererCache.standardMaterialData[index] = stdVoxels[index];
            }
            else if (index < stdVoxelsTrans.Length + glassVoxelsTrans.Length)
            {
                int idx = index - stdVoxelsTrans.Length;
                if (idx < 0 || idx >= glassVoxelsTrans.Length) return;
                rendererCache.glassMatrices[idx] = glassVoxelsTrans[idx].Value;
                rendererCache.glassMaterialData[idx] = glassVoxels[idx];
            }
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
                foreach (var kv in map)
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
        public bool IsCreated;

        public RendererAssembler(int standardVoxels, int glassVoxels, Allocator alloc)
        {
            standardMaterialAssembly = new NativeList<StandardVoxelAssembledData>(standardVoxels, alloc);
            glassMaterialAssembly = new NativeList<GlassVoxelAssembledData>(glassVoxels, alloc);
            IsCreated = false;
        }

        public void Dispose()
        {
            if (standardMaterialAssembly.IsCreated) standardMaterialAssembly.Dispose();
            if (glassMaterialAssembly.IsCreated) glassMaterialAssembly.Dispose();
            IsCreated = false;
        }

        public void Resize(int std, int glass)
        {
            if (standardMaterialAssembly.IsCreated) standardMaterialAssembly.SetCapacity(std);
            if (glassMaterialAssembly.IsCreated) glassMaterialAssembly.SetCapacity(glass);
        }

        public void Clear()
        {
            if (standardMaterialAssembly.IsCreated) standardMaterialAssembly.Clear();
            if (glassMaterialAssembly.IsCreated) glassMaterialAssembly.Clear();
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