using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace DirectionalColorKey.Tests;

public sealed class DirectionalColorKeyInteropTests
{
    private const int Width = 96;
    private const int Height = 64;

    private static int[] CreateImage()
    {
        int[] pixels = new int[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool foreground = x >= Width / 4 && x < Width * 3 / 4 && y >= Height / 4 && y < Height * 3 / 4;

                pixels[(y * Width) + x] = foreground
                    ? unchecked((int)0xFFC83C28)
                    : unchecked((int)0xFF00FF00);
            }
        }

        return pixels;
    }

    private static ID2D1Bitmap1 CreateBitmap(IGraphicsDevicesAndContext graphicsContext, BitmapOptions options)
        => graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(Width, Height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                options));

    [Fact]
    public void SharedTextureRoundTripPreservesEveryPixel()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = DirectionalColorKeyInteropProvider.TryCreate(graphicsContext, scheduler, out var interopDevice);
        if (provider is null || interopDevice is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var domain = interopDevice.RegisterExternalDomain(provider);
        using var resourceSet = DirectionalColorKeyResourceSet.Create(interopDevice, domain);
        using var interopHost = DirectionalColorKeyInteropHost.Create(interopDevice, 2);
        using var analyzer = DirectionalColorKeyAnalyzer.TryCreate(interopDevice);

        Assert.NotNull(analyzer);
        Assert.True(resourceSet.TryEnsureSource(Width, Height, out _));
        Assert.True(resourceSet.TryEnsureForeground(Width, Height, out _));

        int[] pixels = CreateImage();

        using var inputBitmap = CreateBitmap(graphicsContext, BitmapOptions.None);
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            inputBitmap.CopyFromMemory(pinned.AddrOfPinnedObject(), Width * sizeof(int));
        }
        finally
        {
            pinned.Free();
        }

        var renderContext = provider.RenderContext;
        using (var borrow = resourceSet.BeginSourceExternalOperation())
        {
            var previousTarget = renderContext.Target;

            using var sourceBitmap = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());

            renderContext.Target = sourceBitmap;
            renderContext.BeginDraw();
            renderContext.Clear(null);
            renderContext.DrawImage(
                inputBitmap,
                new Vector2(0f, 0f),
                null,
                InterpolationMode.NearestNeighbor,
                CompositeMode.SourceCopy);
            renderContext.EndDraw();
            renderContext.Target = previousTarget;
        }

        var captured = analyzer!.PrepareSource(Width, Height);

        interopHost.CaptureSource(resourceSet.GetSourceComputeBinding(), captured, Width, Height).Wait();

        int[] capturedPixels = new int[Width * Height];
        captured.CopyTo(capturedPixels);

        Assert.Equal(pixels, capturedPixels);

        using var field = interopDevice.AllocateReadWriteBuffer(pixels);

        interopHost.WriteForegroundField(
            resourceSet.GetForegroundComputeBinding(), field.AsReadOnly(), Width, Height).Wait();

        using var lease = resourceSet.AcquireForegroundExternalViewLease();
        using var foregroundBitmap = new ID2D1Bitmap1(lease.DangerousGetView().AddRefBitmap());
        using var staging = CreateBitmap(graphicsContext, BitmapOptions.CpuRead | BitmapOptions.CannotDraw);

        staging.CopyFromBitmap(foregroundBitmap);

        var mapped = staging.Map(MapOptions.Read);
        try
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int actual = Marshal.ReadInt32(mapped.Bits + (nint)((y * mapped.Pitch) + (x * sizeof(int))));

                    Assert.Equal(pixels[(y * Width) + x], actual);
                }
            }
        }
        finally
        {
            staging.Unmap();
        }
    }
}
