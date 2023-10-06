using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Unity.Mathematics;
using Unity.Collections;

// https://github.com/INedelcu/RayTracingMeshInstancingSimple
namespace BarelyFunctional.Renderer
{
    [ExecuteInEditMode]
    public class RaytraceInstancing : MonoBehaviour
    {
        public RayTracingShader rayTracingShader = null;

        //public Cubemap envTexture = null;

        [Range(1, 100)]
        public uint bounceCountOpaque = 5;

        [Range(1, 100)]
        public uint bounceCountTransparent = 8;

        public Mesh mesh;
        public Material material;

        public Transform target;

        private uint cameraWidth = 0;
        private uint cameraHeight = 0;

        private int convergenceStep = 0;

        private Matrix4x4 prevCameraMatrix;
        private uint prevBounceCountOpaque = 0;
        private uint prevBounceCountTransparent = 0;

        private RenderTexture rayTracingOutput = null;

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

            if (rayTracingOutput != null)
            {
                rayTracingOutput.Release();
                rayTracingOutput = null;
            }

            cameraWidth = 0;
            cameraHeight = 0;
        }

        private void CreateResources()
        {
            CreateRayTracingAccelerationStructure();

            if (cameraWidth != Camera.main.pixelWidth || cameraHeight != Camera.main.pixelHeight)
            {
                if (rayTracingOutput)
                    rayTracingOutput.Release();

                RenderTextureDescriptor rtDesc = new RenderTextureDescriptor()
                {
                    dimension = TextureDimension.Tex2D,
                    width = Camera.main.pixelWidth,
                    height = Camera.main.pixelHeight,
                    depthBufferBits = 0,
                    volumeDepth = 1,
                    msaaSamples = 1,
                    vrUsage = VRTextureUsage.OneEye,
                    graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat,
                    enableRandomWrite = true,
                };

                rayTracingOutput = new RenderTexture(rtDesc);
                rayTracingOutput.Create();

                cameraWidth = (uint)Camera.main.pixelWidth;
                cameraHeight = (uint)Camera.main.pixelHeight;

                convergenceStep = 0;
            }
        }

        void OnDestroy()
        {
            ReleaseResources();
        }

        void OnDisable()
        {
            ReleaseResources();
        }

        private void OnEnable()
        {
            prevCameraMatrix = Camera.main.cameraToWorldMatrix;
            prevBounceCountOpaque = bounceCountOpaque;
            prevBounceCountTransparent = bounceCountTransparent;

        }

        private void Update()
        {
            CreateResources();

            if (Input.GetKeyDown("space"))
                convergenceStep = 0;
        }

        struct Data
        {
            public float3 color;
            public float emission;
        };

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (!SystemInfo.supportsRayTracing || !rayTracingShader)
            {
                Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");
                Graphics.Blit(src, dest);
                return;
            }

            if (rayTracingAccelerationStructure == null)
                return;

            if (prevCameraMatrix != Camera.main.cameraToWorldMatrix)
                convergenceStep = 0;

            if (prevBounceCountOpaque != bounceCountOpaque)
                convergenceStep = 0;

            if (prevBounceCountTransparent != bounceCountTransparent)
                convergenceStep = 0;

            convergenceStep = 0;

            rayTracingAccelerationStructure.ClearInstances();

            #region Instancing
            GraphicsBuffer data = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 4 * sizeof(float));
            data.SetData(new Data[] { new Data { color=new float3(1, 0, 0), emission=Mathf.Sin(Time.time) * 0.5f + 0.5f }, 
                                      new Data { color=new float3(1, 1, 0), emission=Mathf.Cos(Time.time) * 0.5f + 0.5f} });

            RayTracingMeshInstanceConfig config = new RayTracingMeshInstanceConfig(mesh, 0, material);

            config.materialProperties = new MaterialPropertyBlock();
            config.materialProperties.SetBuffer("g_Data", data);
            config.material.enableInstancing = true;

            /*float4x4 matrix1 = float4x4.TRS(target.position, target.rotation, target.localScale);
            rayTracingAccelerationStructure.AddInstance(config, matrix1, null, (uint)0);
            float4x4 matrix2 = float4x4.TRS(target.position + target.forward * 2, target.rotation, target.localScale);
            rayTracingAccelerationStructure.AddInstance(config, matrix2, null, (uint)1);*/

            NativeArray<Matrix4x4> matrices = new NativeArray<Matrix4x4>(2, Allocator.Temp);
            matrices[0] = Matrix4x4.TRS(target.position, target.rotation, target.localScale);
            matrices[1] = Matrix4x4.TRS(target.position + target.forward * 2, target.rotation, target.localScale);

            rayTracingAccelerationStructure.AddInstances(config, matrices);
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
            //rayTracingShader.SetTexture(Shader.PropertyToID("g_EnvTex"), envTexture);

            // Output
            rayTracingShader.SetTexture(Shader.PropertyToID("g_Radiance"), rayTracingOutput);

            rayTracingShader.Dispatch("MainRayGenShader", (int)cameraWidth, (int)cameraHeight, 1, Camera.main);

            Graphics.Blit(rayTracingOutput, dest);

            convergenceStep++;

            prevCameraMatrix = Camera.main.cameraToWorldMatrix;
            prevBounceCountOpaque = bounceCountOpaque;
            prevBounceCountTransparent = bounceCountTransparent;

            data.Release();
            matrices.Dispose();
        }
    }
}
