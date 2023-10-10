using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelWorld.Rendering.AbstractClasses;
using VoxelWorld.Rendering.Structs;

public class ECSVoxelWorldRendererProvider : AbstractVoxelWorldInstancedRendererProviderMonoBehaviour
{

    public static ECSVoxelWorldRendererProvider Instance;
    public new VoxelWorldInstancedRenderer renderer;

    public override VoxelWorldInstancedRenderer GetRenderer()
    {
        return renderer;
    }

    public override bool IsReady()
    {
        return renderer.IsCreated;
    }


    private void OnDestroy()
    {
        if (renderer.IsCreated)
            renderer.Dispose();
    }
    private void Awake()
    {
        Instance = this;
        renderer = new VoxelWorldInstancedRenderer(0, 0, Unity.Collections.Allocator.Persistent);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
    }
}
