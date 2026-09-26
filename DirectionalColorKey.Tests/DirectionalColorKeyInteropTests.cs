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

    private static readonly Vector3 BackgroundLab = new(0.8664f, -0.2339f, 0.1795f);
    private static readonly Vector3 BackgroundSrgb = new(0f, 1f, 0f);

    private static int[] CreateImage()
    {
        int[] pixels = new int[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool foreground = x >= Width / 4 && x < Width * 3 / 4 && y >= Height / 4 && y < Height * 3 / 4;

                pixels[(y * Width) + x] = foreground
                    ? unchecked((int)0xFFC828B4)
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

    private static void Analyze(DirectionalColorKeyAnalyzer analyzer, ReadOnlySpan<int> pixels)
        => analyzer.Analyze(
            pixels,
            Width,
            Height,
            BackgroundLab,
            Vector3.Normalize(new Vector3(1f, 0f, 0f) - BackgroundLab),
            1,
            0.02f,
            0.1f,
            DirectionalColorKeyScaleMode.Physical,
            0.99f,
            0.5f,
            static (_, floorValue) => MathF.Max(floorValue, 0.5f),
            true);

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
        using var shared = DirectionalColorKeyAnalyzer.TryCreate(interopDevice);
        using var direct = DirectionalColorKeyAnalyzer.TryCreate(interopDevice);

        Assert.NotNull(shared);
        Assert.NotNull(direct);
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

        shared!.CaptureSource(resourceSet.GetSourceComputeBinding(), Width, Height);
        Analyze(shared, default);
        Analyze(direct!, pixels);

        int[] sharedField = shared.BuildForegroundField(Width, Height, BackgroundLab, BackgroundSrgb).ToArray();
        int[] directField = direct.BuildForegroundField(Width, Height, BackgroundLab, BackgroundSrgb).ToArray();

        Assert.Contains(directField, value => value != 0);
        Assert.Equal(directField, sharedField);
        Assert.Equal(direct.GetCenter(0), shared.GetCenter(0));
        Assert.Equal(direct.GetLambda(0), shared.GetLambda(0));

        shared.WriteForegroundField(resourceSet.GetForegroundComputeBinding(), Width, Height, BackgroundLab, BackgroundSrgb);

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

                    Assert.Equal(directField[(y * Width) + x], actual);
                }
            }
        }
        finally
        {
            staging.Unmap();
        }
    }
}
