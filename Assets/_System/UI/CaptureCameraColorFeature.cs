using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CaptureCameraColorFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        public RenderTexture targetTexture;
        public bool gameViewOnly = true;
        public bool baseCamerasOnly = true;
        public string cameraTag = "MainCamera";
    }

    public Settings settings = new Settings();

    private CopyPass pass;
    private RTHandle destHandle;

    public override void Create()
    {
        pass = new CopyPass();
    }

    protected override void Dispose(bool disposing)
    {
        destHandle?.Release();
        destHandle = null;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.targetTexture == null) return;

        var camData = renderingData.cameraData;
        if (settings.gameViewOnly && camData.cameraType != CameraType.Game) return;
        if (settings.baseCamerasOnly && camData.renderType != CameraRenderType.Base) return;
        if (!string.IsNullOrEmpty(settings.cameraTag) && !camData.camera.CompareTag(settings.cameraTag)) return;

        if (settings.targetTexture.depth != 0)
        {
            Debug.LogError($"[CaptureCameraColorFeature] '{settings.targetTexture.name}' has a depth buffer. " +
                           "Render Graph requires imported RTs to be color-only. Set 'Depth Buffer = No depth buffer' on the RenderTexture asset.", settings.targetTexture);
            return;
        }

        if (destHandle == null || destHandle.rt != settings.targetTexture)
        {
            destHandle?.Release();
            destHandle = RTHandles.Alloc(settings.targetTexture);
        }

        pass.Setup(destHandle, settings.renderPassEvent);
        renderer.EnqueuePass(pass);
    }

    private class CopyPass : ScriptableRenderPass
    {
        private RTHandle _dest;

        public void Setup(RTHandle target, RenderPassEvent evt)
        {
            _dest = target;
            renderPassEvent = evt;
        }

        private class PassData
        {
            public TextureHandle Source;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var src = resourceData.activeColorTexture;
            var dst = renderGraph.ImportTexture(_dest);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Capture Camera Color", out var data))
            {
                data.Source = src;
                builder.UseTexture(src);
                builder.SetRenderAttachment(dst, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
}
