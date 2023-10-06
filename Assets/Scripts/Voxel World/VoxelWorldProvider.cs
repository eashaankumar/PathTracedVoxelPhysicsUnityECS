using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelWorldTemp.Rendering.AbstractClasses;
using VoxelWorldTemp.Rendering.Interfaces;
using VoxelWorldTemp.Rendering.Structs;

namespace VoxelWorldTemp.Rendering
{
    public class VoxelWorldProvider : AbstractVoxelWorldInstancedRendererProviderMonoBehaviour
    {
        [SerializeField] 
        uint seed;
        [SerializeField]
        uint numVoxels;
        [SerializeField]
        float2 spawnRadiusMinMax;
        [SerializeField]
        float2 spawnSizeMinMax;

        VoxelWorldInstancedRenderer vwIRenderer;
        bool isReady;

        public override VoxelWorldInstancedRenderer GetRenderer()
        {
            return vwIRenderer;
        }

        public override bool IsReady()
        {
            return isReady;
        }

        IEnumerator Start()
        {
            isReady = false;
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed);
            #region Assembler
            VerletPhysicsRendererAssembler assembler = new VerletPhysicsRendererAssembler((int)numVoxels, (int)numVoxels, Allocator.TempJob);
            GenerateWorldJob generateWorldJob = new GenerateWorldJob
            {
                standardMaterialAssembly = assembler.standardMaterialAssembly.AsParallelWriter(),
                glassMaterialAssembly = assembler.glassMaterialAssembly.AsParallelWriter(),
                random = random,
                spawnRadius = spawnRadiusMinMax,
                spawnSize = spawnSizeMinMax
            };
            JobHandle genHandle = generateWorldJob.Schedule((int)numVoxels, 64);
            yield return new WaitUntil(() => genHandle.IsCompleted);
            genHandle.Complete();
            #endregion

            if (vwIRenderer.IsCreated) vwIRenderer.Dispose();

            #region cache
            vwIRenderer = new VoxelWorldInstancedRenderer(assembler.standardMaterialAssembly.Length, assembler.glassMaterialAssembly.Length, Allocator.Persistent);
            Debug.Log(assembler.standardMaterialAssembly.Length + " " + assembler.glassMaterialAssembly.Length);
            PopulateRendererCacheJob rendererCacheJob = new PopulateRendererCacheJob
            {
                assembler = assembler,
                rendererCache = vwIRenderer
            };
            JobHandle cacheHandle = rendererCacheJob.Schedule(assembler.standardMaterialAssembly.Length + assembler.glassMaterialAssembly.Length, 64);
            yield return new WaitUntil(() => cacheHandle.IsCompleted);
            cacheHandle.Complete();
            #endregion

            assembler.Dispose();
            isReady = true;
        }

        void OnDestroy()
        {
            if (vwIRenderer.IsCreated)
                vwIRenderer.Dispose();
        }

        [BurstCompile]
        struct GenerateWorldJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction]
            public NativeList<StandardVoxelAssembledData>.ParallelWriter standardMaterialAssembly;
            [NativeDisableParallelForRestriction]
            public NativeList<GlassVoxelAssembledData>.ParallelWriter glassMaterialAssembly;
            public Unity.Mathematics.Random random;
            [ReadOnly]
            public float2 spawnRadius;
            [ReadOnly]
            public float2 spawnSize;

            public void Execute(int index)
            {
                float3 pos = (random.NextFloat3() * 2 - 1) * spawnRadius.y;//(posNoise) * random.NextFloat(spawnRadius.x, spawnRadius.y);
                quaternion rot = random.NextQuaternionRotation();
                float size = random.NextFloat(spawnSize.x, spawnSize.y);
                Matrix4x4 trs = Matrix4x4.TRS(pos, rot, new float3(1, 1, 1) * size);

                VoxelMaterialType type = (VoxelMaterialType)random.NextInt(0, 2);

                if (type == VoxelMaterialType.STANDARD)
                {
                    standardMaterialAssembly.AddNoResize(
                        new StandardVoxelAssembledData
                        {
                            material = new StandardMaterialData
                            {
                                albedo = RandColor(index),
                                specular = RandSpecular(),
                                emission = RandColor(index) * random.NextFloat(0f, 1f),
                                smoothness = random.NextFloat(0f, 1f),
                                metallic = random.NextFloat(0f, 1f),
                                ior = random.NextFloat(0f, 1f)
                            },
                            trs = trs
                        }
                    );
                }
                else
                {
                    glassMaterialAssembly.AddNoResize(
                        new GlassVoxelAssembledData
                        {
                            material = new GlassMaterialData
                            {
                                albedo = RandColor(index),
                                emission = RandColor(index) * random.NextFloat(0f, 1.1f),
                                ior = random.NextFloat(1.0f, 2.8f),
                                roughness = random.NextFloat(0f, 0.5f),
                                extinctionCoeff = random.NextFloat(0f, 10f),
                                flatShading = random.NextBool() ? 1 : 0,
                            },
                            trs = trs
                        }
                    );
                }
            }

            float3 RandColor(int index)
            {
                return new float3(math.sin(index) * 0.5f + 0.5f, math.cos(index) * 0.5f + 0.5f, math.tan(index) * 0.5f + 0.5f);
            }

            float3 RandSpecular()
            {
                return new float3(random.NextFloat(0f, 1f), random.NextFloat(0f, 1f), random.NextFloat(0f, 1f));
            }
        }

        [BurstCompile]
        struct PopulateRendererCacheJob : IJobParallelFor
        {
            [ReadOnly] public VerletPhysicsRendererAssembler assembler;
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

        #region structs
        public enum VoxelMaterialType
        {
            STANDARD, GLASS
        }

        public struct Voxel
        {
            public VoxelMaterialType type;
            public uint id;
            public float size;
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
        public struct VerletPhysicsRendererAssembler : System.IDisposable
        {
            public NativeList<StandardVoxelAssembledData> standardMaterialAssembly;
            public NativeList<GlassVoxelAssembledData> glassMaterialAssembly;

            public VerletPhysicsRendererAssembler(int standardVoxels, int glassVoxels, Allocator alloc)
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
        #endregion
    }
}