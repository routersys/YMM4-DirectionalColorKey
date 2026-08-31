using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace DirectionalColorKey
{
    internal sealed class ActionDisposer(Action action) : IDisposable
    {
        private readonly Action action = action;

        public void Dispose() => action();
    }

    internal sealed class DirectionalColorKeyExternalView : IDisposable
    {
        private readonly ID3D11Texture2D texture;
        private readonly ID2D1Bitmap1 bitmap;

        public DirectionalColorKeyExternalView(ID3D11Texture2D texture, ID2D1Bitmap1 bitmap)
        {
            this.texture = texture;
            this.bitmap = bitmap;
        }

        public ID2D1Bitmap1 Bitmap => bitmap;

        public void Dispose()
        {
            bitmap.Dispose();
            texture.Dispose();
        }
    }

    internal sealed class DirectionalColorKeyInteropProvider : IComputeExternalInteropProvider<DirectionalColorKeyExternalView>
    {
        private readonly ID3D11Device1 device;
        private readonly ID3D11Device5 device5;
        private readonly ID3D11DeviceContext4 context;
        private readonly ID2D1DeviceContext6 renderContext;
        private readonly ComputeExternalQueueScheduler scheduler;
        private readonly long adapterLuid;

        private ID3D11Fence? fence;

        private DirectionalColorKeyInteropProvider(
            ID3D11Device1 device,
            ID3D11Device5 device5,
            ID3D11DeviceContext4 context,
            ID2D1DeviceContext6 renderContext,
            ComputeExternalQueueScheduler scheduler,
            long adapterLuid)
        {
            this.device = device;
            this.device5 = device5;
            this.context = context;
            this.renderContext = renderContext;
            this.scheduler = scheduler;
            this.adapterLuid = adapterLuid;
        }

        public ExternalAdapterIdentity AdapterIdentity => new(adapterLuid);

        public ComputeExternalQueueScheduler Scheduler => scheduler;

        public ExternalInteropCapabilities Capabilities =>
            ExternalInteropCapabilities.SharedFence |
            ExternalInteropCapabilities.SharedTexture2D |
            ExternalInteropCapabilities.SingleImmediateContextOrdering |
            ExternalInteropCapabilities.PersistentExternalViewOrdering;

        public ID2D1DeviceContext6 RenderContext => renderContext;

        public static DirectionalColorKeyInteropProvider? TryCreate(
            IGraphicsDevicesAndContext devices,
            ComputeExternalQueueScheduler scheduler,
            out GraphicsDevice? graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(scheduler);

            graphicsDevice = null;

            ID3D11Device1? device = null;
            ID3D11Device5? device5 = null;
            ID3D11DeviceContext4? context = null;
            ID2D1DeviceContext6? renderContext = null;

            try
            {
                long adapterLuid = devices.DXGI.Adapter.Description.Luid;

                if (!GraphicsDevice.TryGetDevice(new ExternalAdapterIdentity(adapterLuid), out graphicsDevice))
                    return null;

                device = devices.D3D.Device.QueryInterface<ID3D11Device1>();
                device5 = devices.D3D.Device.QueryInterface<ID3D11Device5>();
                context = devices.D3D.DeviceContext.QueryInterface<ID3D11DeviceContext4>();
                renderContext = devices.D2D.Device.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations)
                    .QueryInterface<ID2D1DeviceContext6>();
                return new DirectionalColorKeyInteropProvider(device, device5, context, renderContext, scheduler, adapterLuid);
            }
            catch
            {
                renderContext?.Dispose();
                context?.Dispose();
                device5?.Dispose();
                device?.Dispose();
                graphicsDevice = null;
                return null;
            }
        }

        public void Initialize(in ExternalTimelineInitialization initialization)
        {
            fence = device5.OpenSharedFence<ID3D11Fence>(initialization.SharedFenceHandle.DangerousGetHandle());
        }

        public void EnqueueSignal(ulong value)
        {
            context.Signal(fence!, value);
        }

        public void FlushAfterSignal()
        {
            context.Flush();
        }

        public void EnqueueWait(ulong value)
        {
            context.Wait(fence!, value);
        }

        public DirectionalColorKeyExternalView OpenSharedTexture(BorrowedSharedHandle resourceHandle, in ExternalTextureDescriptor descriptor)
        {
            ID3D11Texture2D? texture = null;
            ID2D1Bitmap1? bitmap = null;

            try
            {
                texture = device.OpenSharedResource1<ID3D11Texture2D>(resourceHandle.DangerousGetHandle());

                using var surface = texture.QueryInterface<IDXGISurface>();

                var pixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
                var options = descriptor.ExternalUsage is ExternalTextureUsage.RenderTarget
                    ? BitmapOptions.Target
                    : BitmapOptions.None;

                bitmap = renderContext.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(pixelFormat, 96f, 96f, options));

                var view = new DirectionalColorKeyExternalView(texture, bitmap);

                texture = null;
                bitmap = null;

                return view;
            }
            finally
            {
                bitmap?.Dispose();
                texture?.Dispose();
            }
        }

        public void OnDeviceTerminal(Exception reason)
        {
        }

        public void Dispose()
        {
            fence?.Dispose();
            renderContext.Dispose();
            context.Dispose();
            device5.Dispose();
            device.Dispose();
        }
    }

    [ComputePipelineHost("device", 2)]
    internal sealed partial class DirectionalColorKeyInteropHost
    {
        private readonly GraphicsDevice device;

        [ComputePipeline]
        [ComputeInterop]
        private void CaptureSource(
            in ComputeContext context,
            [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
            [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> bgra,
            int width,
            int height)
        {
            _ = device;

            context.For(width, height, new SharedTextureToBufferShader(source, bgra, width, height));
        }

        [ComputePipeline]
        [ComputeInterop]
        private void WriteForegroundField(
            in ComputeContext context,
            [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> destination,
            [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> foreground,
            int width,
            int height)
        {
            _ = device;

            context.For(width, height, new BufferToSharedTextureShader(foreground, destination, width, height));
        }
    }

    [ComputeInteropResourceSet]
    internal sealed partial class DirectionalColorKeyResourceSet
    {
        [ComputeSharedTexture(
            ComputeResourceResizePolicy.Exact,
            ComputeResourceAccess.ReadWrite,
            ExternalResourceAccess.Write,
            ExternalTextureUsage.RenderTarget,
            ComputeAlphaMode.Premultiplied,
            ComputeSharedTextureInitialOwner.External,
            ComputeResourceRecovery.RecreateFromHost)]
        private readonly SharedTextureSlot<Bgra32, Float4, DirectionalColorKeyExternalView> source;

        [ComputeSharedTexture(
            ComputeResourceResizePolicy.Exact,
            ComputeResourceAccess.ReadWrite,
            ExternalResourceAccess.Read,
            ExternalTextureUsage.Sampled,
            ComputeAlphaMode.Premultiplied,
            ComputeSharedTextureInitialOwner.Compute,
            ComputeResourceRecovery.Recompute)]
        private readonly SharedTextureSlot<Bgra32, Float4, DirectionalColorKeyExternalView> foreground;
    }
}
