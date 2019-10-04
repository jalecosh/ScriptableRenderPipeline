using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.HighDefinition
{
    public class HDRenderPipelineRayTracingResources : BaseRenderResources
    {
        public override int Id => 1;

#if ENABLE_RAYTRACING
        // Reflection
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Reflections/RaytracingReflections.raytrace")]
        public RayTracingShader reflectionRaytracingRT;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Reflections/RaytracingReflections.compute")]
        public ComputeShader reflectionRaytracingCS;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/RaytracingReflectionFilter.compute")]
        public ComputeShader reflectionBilateralFilterCS;

        // Shadows
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Shadows/RaytracingShadow.raytrace")]
        public RayTracingShader shadowRaytracingRT;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Shadows/RaytracingShadow.compute")]
        public ComputeShader shadowRaytracingCS;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Shadows/RaytracingShadowFilter.compute")]
        public ComputeShader shadowFilterCS;

        // Recursive tracing
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/RaytracingRenderer.raytrace")]
        public RayTracingShader forwardRaytracing;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/RaytracingFlagMask.shader")]
        public Shader raytracingFlagMask;

        // Light cluster
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/RaytracingLightCluster.compute")]
        public ComputeShader lightClusterBuildCS;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/DebugLightCluster.shader")]
        public Shader lightClusterDebugS;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/DebugLightCluster.compute")]
        public ComputeShader lightClusterDebugCS;
        
        // Indirect Diffuse
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/IndirectDiffuse/RaytracingIndirectDiffuse.raytrace")]
        public RayTracingShader indirectDiffuseRaytracingRT;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/IndirectDiffuse/RaytracingIndirectDiffuse.compute")]
        public ComputeShader indirectDiffuseRaytracingCS;

        // Ambient Occlusion
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/RaytracingAmbientOcclusion.raytrace")]
        public RayTracingShader aoRaytracing;

        // Denoising
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Denoising/TemporalFilter.compute")]
        public ComputeShader temporalFilterCS;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Denoising/SimpleDenoiser.compute")]
        public ComputeShader simpleDenoiserCS;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Denoising/DiffuseDenoiser.compute")]
        public ComputeShader diffuseDenoiserCS;

        // Deferred Lighting
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Deferred/RaytracingGBuffer.raytrace")]
        public RayTracingShader gBufferRaytracingRT;
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Deferred/RaytracingDeferred.compute")]
        public ComputeShader deferredRaytracingCS;

        // Path Tracing
        [Reload("Runtime/RenderPipeline/PathTracing/Shaders/PathTracingMain.raytrace")]
        public RayTracingShader pathTracing;

        // Ray Binning
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/Common/RayBinning.compute")]
        public ComputeShader rayBinningCS;

        // Ray count
        [Reload("Runtime/RenderPipeline/Raytracing/Shaders/CountTracedRays.compute")]
        public ComputeShader countTracedRays;
#endif
    }
}
