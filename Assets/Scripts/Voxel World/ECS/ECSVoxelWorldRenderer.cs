using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VoxelWorld.Rendering.Enums;
using VoxelWorld.Rendering.GlobalClasses;
using VoxelWorld.Rendering.Structs;

namespace VoxelWorld.Rendering
{
    public class ECSVoxelWorldRenderer : MonoBehaviour
    {
        [Header("Ray Tracing")]
        [SerializeField]
        RayTracingShader rayTracingShader = null;
        [SerializeField]
        RayTracingShader rayTracingMetaShader = null;

        [Header("Skybox")]
        [SerializeField]
        UnityEngine.Color topColor;
        [SerializeField]
        UnityEngine.Color bottomColor;

        [Header("Path tracing settings")]
        [SerializeField]
        PathTracingResolution ptRes = PathTracingResolution._240p;
        [SerializeField, Range(1, 100)]
        uint bounceCountOpaque = 5;
        [SerializeField, Range(1, 100)]
        uint bounceCountTransparent = 8;
        [SerializeField]
        Mesh mesh;
        [SerializeField]
        Material standardMaterial;
        [SerializeField]
        Material glassMaterial;

        private uint cameraWidth = 0;
        private uint cameraHeight = 0;

        private int convergenceStep = 0;

        private RenderTexture noisyRadianceRT = null, convergedRT = null;
        private RenderTexture normalRT = null, depthRT = null, albedoRT = null, emissionRT = null,
            kRT = null, shapeRT = null, specularRT = null, roughSmoothRT = null, extcoMetalRT = null, iorRT = null;

        private RayTracingAccelerationStructure rayTracingAccelerationStructure = null;

        private void CreateRayTracingAccelerationStructure()
        {
            if (rayTracingAccelerationStructure == null)
            {
                RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings();
                settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
                settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
                settings.layerMask = 255;

                rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);
            }
        }

        private void ReleaseResources()
        {
            if (rayTracingAccelerationStructure != null)
            {
                rayTracingAccelerationStructure.Release();
                rayTracingAccelerationStructure = null;
            }

            if (noisyRadianceRT != null)
            {
                ReleaseRT(ref noisyRadianceRT);
                ReleaseRT(ref convergedRT);
                ReleaseRT(ref normalRT);
                ReleaseRT(ref depthRT);
                ReleaseRT(ref albedoRT);
                ReleaseRT(ref emissionRT);
                ReleaseRT(ref kRT);
                ReleaseRT(ref shapeRT);

                ReleaseRT(ref specularRT);
                ReleaseRT(ref roughSmoothRT);
                ReleaseRT(ref extcoMetalRT);
                ReleaseRT(ref iorRT);
            }

            cameraWidth = 0;
            cameraHeight = 0;
        }

        void ReleaseRT(ref RenderTexture tex)
        {
            tex.Release();
            tex = null;
        }

        public Vector2Int PixelDim
        {
            get
            {
                return ((int)ptRes + 1) * PathTracingResolutionHandler._240PRes;
            }
        }

        private void CreateResources()
        {
            CreateRayTracingAccelerationStructure();
            Vector2Int Dim = PixelDim;
            if (cameraWidth != Dim.x || cameraHeight != Dim.y)
            {
                if (noisyRadianceRT)
                {
                    ReleaseRT(ref noisyRadianceRT);
                    ReleaseRT(ref convergedRT);
                    ReleaseRT(ref normalRT);
                    ReleaseRT(ref depthRT);
                    ReleaseRT(ref albedoRT);
                    ReleaseRT(ref emissionRT);
                    ReleaseRT(ref kRT);
                    ReleaseRT(ref shapeRT);

                    ReleaseRT(ref specularRT);
                    ReleaseRT(ref roughSmoothRT);
                    ReleaseRT(ref extcoMetalRT);
                    ReleaseRT(ref iorRT);
                }

                RenderTextureDescriptor rtDesc4Channel = new RenderTextureDescriptor()
                {
                    dimension = TextureDimension.Tex2D,
                    width = Dim.x,
                    height = Dim.y,
                    depthBufferBits = 0,
                    volumeDepth = 1,
                    msaaSamples = 1,
                    vrUsage = VRTextureUsage.OneEye,
                    graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                    enableRandomWrite = true,
                };

                CreateRenderTexture(ref noisyRadianceRT, rtDesc4Channel);

                CreateRenderTexture(ref convergedRT, rtDesc4Channel);

                CreateRenderTexture(ref normalRT, rtDesc4Channel);

                CreateRenderTexture(ref albedoRT, rtDesc4Channel);

                CreateRenderTexture(ref depthRT, rtDesc4Channel);

                CreateRenderTexture(ref emissionRT, rtDesc4Channel);

                CreateRenderTexture(ref kRT, rtDesc4Channel);

                CreateRenderTexture(ref shapeRT, rtDesc4Channel);

                CreateRenderTexture(ref specularRT, rtDesc4Channel);
                CreateRenderTexture(ref roughSmoothRT, rtDesc4Channel);
                CreateRenderTexture(ref extcoMetalRT, rtDesc4Channel);
                CreateRenderTexture(ref iorRT, rtDesc4Channel);

                cameraWidth = (uint)Dim.x;
                cameraHeight = (uint)Dim.y;
            }
        }

        void CreateRenderTexture(ref RenderTexture tex, RenderTextureDescriptor desc)
        {
            tex = new RenderTexture(desc);
            tex.Create();
        }

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => ECSVoxelWorldRendererProvider.Instance.IsReady());
        }


        void OnDestroy()
        {
            ReleaseResources();
        }


        void OnDisable()
        {
            ReleaseResources();
        }

        private void Update()
        {
            CreateResources();
            if (Camera.main.transform.hasChanged)
            {
                //convergenceStep = 0;
                Camera.main.transform.hasChanged = false;
            }
        }


        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (!ECSVoxelWorldRendererProvider.Instance.IsReady()) return;
            if (!SystemInfo.supportsRayTracing || !rayTracingShader)
            {
                Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
                Graphics.Blit(src, dest);
                return;
            }

            if (rayTracingAccelerationStructure == null)
                return;

            rayTracingAccelerationStructure.ClearInstances();
            GraphicsBuffer stadardMaterialdata = null, glassMaterialData = null;
            #region Instancing

            VoxelWorldInstancedRenderer vRenderer = ECSVoxelWorldRendererProvider.Instance.GetRenderer();

            if (vRenderer.standardMaterialData.Length > 0)
            {
                stadardMaterialdata = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vRenderer.standardMaterialData.Length, StandardMaterialData.Size);
                stadardMaterialdata.SetData(vRenderer.standardMaterialData);

                RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(mesh, 0, standardMaterial);
                config.materialProperties = new MaterialPropertyBlock();
                config.materialProperties.SetBuffer("g_Data", stadardMaterialdata);
                config.material.enableInstancing = true;

                rayTracingAccelerationStructure.AddInstances(config, vRenderer.standardMatrices);

            }

            if (vRenderer.glassMaterialData.Length > 0)
            {
                glassMaterialData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vRenderer.glassMaterialData.Length, GlassMaterialData.Size);
                glassMaterialData.SetData(vRenderer.glassMaterialData);

                RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(mesh, 0, glassMaterial);
                config.materialProperties = new MaterialPropertyBlock();
                config.materialProperties.SetBuffer("g_Data", glassMaterialData);
                config.material.enableInstancing = true;

                rayTracingAccelerationStructure.AddInstances(config, vRenderer.glassMatrices);
            }

            #endregion

            // Not really needed per frame if the scene is static.
            rayTracingAccelerationStructure.Build();

            rayTracingShader.SetShaderPass("PathTracing");

            Shader.SetGlobalInt(Shader.PropertyToID("g_BounceCountOpaque"), (int)bounceCountOpaque);
            Shader.SetGlobalInt(Shader.PropertyToID("g_BounceCountTransparent"), (int)bounceCountTransparent);

            // Input
            rayTracingShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            rayTracingShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
            rayTracingShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), cameraWidth / (float)cameraHeight);
            rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceStep);
            rayTracingShader.SetInt(Shader.PropertyToID("g_FrameIndex"), Time.frameCount);
            rayTracingShader.SetVector(Shader.PropertyToID("g_SkyboxBottomColor"), new Vector3(bottomColor.r, bottomColor.g, bottomColor.b));
            rayTracingShader.SetVector(Shader.PropertyToID("g_SkyboxTopColor"), new Vector3(topColor.r, topColor.g, topColor.b));


            // Output
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), noisyRadianceRT);
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Normal"), normalRT);
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Albedo"), albedoRT);
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Depth"), depthRT);
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Emission"), emissionRT);
            rayTracingShader.SetTexture(Shader.PropertyToID("g_K"), kRT);
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Shape"), shapeRT);

            // noisy buffer
            for (int i = 0; i < 1; i++)
            {
                rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), noisyRadianceRT);
                rayTracingShader.SetInt(Shader.PropertyToID("g_ConvergenceStep"), convergenceStep);
                rayTracingShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);

                convergenceStep++;
                convergenceStep %= 1;
            }

            MetaShader();

            Graphics.Blit(noisyRadianceRT, dest);

            if (glassMaterialData != null) glassMaterialData.Release();
            if (stadardMaterialdata != null) stadardMaterialdata.Release();
        }

        void MetaShader()
        {
            rayTracingMetaShader.SetShaderPass("PathTracing");
            rayTracingMetaShader.SetAccelerationStructure(Shader.PropertyToID("g_AccelStruct"), rayTracingAccelerationStructure);
            rayTracingMetaShader.SetFloat(Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * Camera.main.fieldOfView * 0.5f));
            rayTracingMetaShader.SetFloat(Shader.PropertyToID("g_AspectRatio"), cameraWidth / (float)cameraHeight);
            rayTracingMetaShader.SetVector(Shader.PropertyToID("g_SkyboxBottomColor"), new Vector3(bottomColor.r, bottomColor.g, bottomColor.b));
            rayTracingMetaShader.SetVector(Shader.PropertyToID("g_SkyboxTopColor"), new Vector3(topColor.r, topColor.g, topColor.b));
            rayTracingMetaShader.SetTexture(Shader.PropertyToID("g_Specular"), specularRT);
            rayTracingMetaShader.SetTexture(Shader.PropertyToID("g_ExtCoMetal"), extcoMetalRT);
            rayTracingMetaShader.SetTexture(Shader.PropertyToID("g_RoughSmooth"), roughSmoothRT);
            rayTracingMetaShader.SetTexture(Shader.PropertyToID("g_IOR"), iorRT);
            rayTracingMetaShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);
        }
    }
}