using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelWorld.Rendering.Enums;
using VoxelWorld.Rendering.Interfaces;
using VoxelWorld.Rendering.Structs;

namespace VoxelWorld.Rendering.Enums
{
    [System.Serializable]
    public enum PathTracingResolution
    {
        _240p=0, _480p=1, _720p=2, _960p=3
    }
}

namespace VoxelWorld.Rendering.GlobalClasses
{
    public static class PathTracingResolutionHandler
    {
        public static readonly Vector2Int _240PRes = new Vector2Int(426, 240);
    }
}

namespace VoxelWorld.Rendering.Structs
{
    public struct VoxelWorldInstancedRenderer : System.IDisposable
    {
        public NativeArray<StandardMaterialData> standardMaterialData;
        public NativeArray<GlassMaterialData> glassMaterialData;
        public NativeArray<Matrix4x4> standardMatrices;
        public NativeArray<Matrix4x4> glassMatrices;
        private bool isCreated;

        public VoxelWorldInstancedRenderer(int standardVoxles, int glassVoxels, Allocator alloc)
        {
            standardMaterialData = new NativeArray<StandardMaterialData>(standardVoxles, alloc);
            glassMaterialData = new NativeArray<GlassMaterialData>(glassVoxels, alloc);
            standardMatrices = new NativeArray<Matrix4x4>(standardVoxles, alloc);
            glassMatrices = new NativeArray<Matrix4x4>(glassVoxels, alloc);
            isCreated = true;
        }

        public void Dispose()
        {
            if (standardMaterialData.IsCreated) standardMaterialData.Dispose();
            if (glassMaterialData.IsCreated) glassMaterialData.Dispose();
            if (standardMatrices.IsCreated) standardMatrices.Dispose();
            if (glassMatrices.IsCreated) glassMatrices.Dispose();
            isCreated = false;
        }

        public bool IsCreated
        {
            get { return isCreated; }
        }
    }

    public struct StandardMaterialData
    {
        public float3 albedo;
        public float3 specular;
        public float3 emission;
        public float smoothness;
        public float metallic;
        public float ior;

        public static int Size
        {
            get
            {
                return (3 + 3 + 3 + 1 + 1 + 1) * sizeof(float);
            }
        }
    };

    public struct GlassMaterialData
    {
        public float3 albedo;
        public float3 emission;
        public float ior; // 1.0, 2.8
        public float roughness; // 0, 0.5
        public float extinctionCoeff; // 0, 1 (technically, 0, 20 but that explodes the colors)
        public float flatShading; // bool

        public static int Size
        {
            get
            {
                return (3 + 3 + 1 + 1 + 1 + 1) * sizeof(float);
            }
        }
    }
}

namespace VoxelWorld.Rendering.Interfaces
{
    public interface IVoxelWorldInstancedRendererProvider
    {
        public VoxelWorldInstancedRenderer GetRenderer();
        public bool IsReady();
    }
}

namespace VoxelWorld.Rendering.AbstractClasses
{
    public abstract class AbstractVoxelWorldInstancedRendererProviderMonoBehaviour : MonoBehaviour, IVoxelWorldInstancedRendererProvider
    {
        public abstract VoxelWorldInstancedRenderer GetRenderer();
        public abstract bool IsReady();
    }
}