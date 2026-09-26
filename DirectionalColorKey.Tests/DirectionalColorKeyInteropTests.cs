using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;

namespace DirectionalColorKey.Tests;

[Collection("Direct2D")]
public sealed class DirectionalColorKeyInteropTests
{
    static readonly Bgra Green = Bgra.Opaque(0, 255, 0);
    static readonly Bgra Magenta = Bgra.Opaque(180, 40, 200);
    static readonly Vector3 BackgroundLab = new(0.8664f, -0.2339f, 0.1795f);
    static readonly Vector3 BackgroundSrgb = new(0f, 1f, 0f);

    static Func<int, int, Bgra> CenteredRectangle(int width, int height)
        => (x, y) => x >= width / 4 && x < width * 3 / 4 && y >= height / 4 && y < height * 3 / 4 ? Magenta : Green;

    sealed class Interop : IDisposable
    {
        readonly ComputeExternalQueueScheduler scheduler;
        readonly DirectionalColorKeyInteropProvider provider;
        readonly ComputeInteropDomain domain;

        public GraphicsDevice Device { get; }

        public DirectionalColorKeyResourceSet Resources { get; }

        public DirectionalColorKeyAnalyzer Analyzer { get; }

        Interop(ComputeExternalQueueScheduler scheduler, DirectionalColorKeyInteropProvider provider, GraphicsDevice device)
        {
            this.scheduler = scheduler;
            this.provider = provider;
            Device = device;
            domain = device.RegisterExternalDomain(provider);
            Resources = DirectionalColorKeyResourceSet.Create(device, domain);
            Analyzer = DirectionalColorKeyAnalyzer.TryCreate(device)!;
        }

        public static Interop Create(IGraphicsDevicesAndContext devices)
        {
            var scheduler = ComputeExternalQueueScheduler.Create();
            var provider = DirectionalColorKeyInteropProvider.TryCreate(devices, scheduler, out var device);
            if (provider is null || device is null)
            {
                scheduler.Dispose();
                Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            }

            return new Interop(scheduler, provider, device);
        }

        public void Draw(ID2D1Image image)
        {
            var context = provider.RenderContext;
            using var borrow = Resources.BeginSourceExternalOperation();
            var previousTarget = context.Target;
            using var target = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
            context.Target = target;
            context.BeginDraw();
            context.Clear(null);
            context.DrawImage(image, Vector2.Zero, null, InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
            context.EndDraw();
            context.Target = previousTarget;
        }

        public Rendering CaptureForeground(IGraphicsDevicesAndContext devices)
        {
            using var lease = Resources.AcquireForegroundExternalViewLease();
            using var bitmap = new ID2D1Bitmap1(lease.DangerousGetView().AddRefBitmap());
            return Rendering.Capture(devices, bitmap);
        }

        public void Dispose()
        {
            Analyzer.Dispose();
            Resources.Dispose();
            Resources.WaitForDisposal();
            domain.Dispose();
            domain.WaitForDisposal();
            provider.Dispose();
            scheduler.Dispose();
        }
    }

    static void Analyze(DirectionalColorKeyAnalyzer analyzer, ReadOnlySpan<int> pixels, int width, int height)
        => analyzer.Analyze(
            pixels,
            width,
            height,
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

    static int[] Pixels(SourceImage source)
    {
        var pixels = new int[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var (blue, green, red, alpha) = source[x, y];
                pixels[y * source.Width + x] = alpha << 24 | red << 16 | green << 8 | blue;
            }
        }

        return pixels;
    }

    static Bgra Unpack(int value) => new((byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24));

    [Fact]
    public void TheSharedSourceIsAnalyzedLikeTheSamePixelsSentFromTheCpu()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var interop = Interop.Create(context);
        using var source = new SourceImage(context, 96, 64, CenteredRectangle(96, 64));
        using var direct = DirectionalColorKeyAnalyzer.TryCreate(interop.Device)!;
        Assert.True(interop.Resources.TryEnsureSource(96, 64, out _));
        interop.Draw(source.Bitmap);

        interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), 96, 64);
        Analyze(interop.Analyzer, default, 96, 64);
        Analyze(direct, Pixels(source), 96, 64);

        var expected = direct.BuildForegroundField(96, 64, BackgroundLab, BackgroundSrgb).ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(expected, interop.Analyzer.BuildForegroundField(96, 64, BackgroundLab, BackgroundSrgb).ToArray());
        Assert.Equal(direct.GetCenter(0), interop.Analyzer.GetCenter(0));
        Assert.Equal(direct.GetLambda(0), interop.Analyzer.GetLambda(0));
    }

    [Fact]
    public void OnlyAChangedSourceIsReportedAsChanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var interop = Interop.Create(context);
        using var first = new SourceImage(context, 96, 64, CenteredRectangle(96, 64));
        using var second = new SourceImage(context, 96, 64, (x, y) => x == 10 && y == 10 ? Magenta : CenteredRectangle(96, 64)(x, y));
        Assert.True(interop.Resources.TryEnsureSource(96, 64, out _));
        interop.Draw(first.Bitmap);
        interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), 96, 64);
        Analyze(interop.Analyzer, default, 96, 64);

        interop.Draw(first.Bitmap);
        interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), 96, 64);
        var unchanged = interop.Analyzer.DetectSourceChange();
        interop.Draw(second.Bitmap);
        interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), 96, 64);
        var changed = interop.Analyzer.DetectSourceChange();
        Analyze(interop.Analyzer, default, 96, 64);
        interop.Draw(second.Bitmap);
        interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), 96, 64);
        var settled = interop.Analyzer.DetectSourceChange();

        Assert.False(unchanged);
        Assert.True(changed);
        Assert.False(settled);
    }

    [Fact]
    public void TheForegroundFieldReachesTheSharedTextureUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var interop = Interop.Create(context);
        using var source = new SourceImage(context, 96, 64, CenteredRectangle(96, 64));
        Assert.True(interop.Resources.TryEnsureSource(96, 64, out _));
        Assert.True(interop.Resources.TryEnsureForeground(96, 64, out _));
        interop.Draw(source.Bitmap);
        interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), 96, 64);
        Analyze(interop.Analyzer, default, 96, 64);
        var field = interop.Analyzer.BuildForegroundField(96, 64, BackgroundLab, BackgroundSrgb).ToArray();

        interop.Analyzer.WriteForegroundField(interop.Resources.GetForegroundComputeBinding(), 96, 64, BackgroundLab, BackgroundSrgb);
        var rendering = interop.CaptureForeground(context);

        Assert.Contains(field, value => value != 0);
        Assert.Equal((0, 0, 96, 64), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(Unpack(field[point.Y * 96 + point.X]), rendering[point.X, point.Y]));
    }

    [Fact]
    public void TheForegroundIsWrittenAfterTheSourceIsReplacedByALargerOne()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var interop = Interop.Create(context);

        foreach (var (width, height) in new[] { (96, 64), (160, 64) })
        {
            using var source = new SourceImage(context, width, height, CenteredRectangle(width, height));
            Assert.True(interop.Resources.TryEnsureSource(width, height, out var sourceChanged));
            Assert.True(interop.Resources.TryEnsureForeground(width, height, out var foregroundChanged));
            interop.Draw(source.Bitmap);
            interop.Analyzer.CaptureSource(interop.Resources.GetSourceComputeBinding(), width, height);
            Analyze(interop.Analyzer, default, width, height);
            var field = interop.Analyzer.BuildForegroundField(width, height, BackgroundLab, BackgroundSrgb).ToArray();

            interop.Analyzer.WriteForegroundField(interop.Resources.GetForegroundComputeBinding(), width, height, BackgroundLab, BackgroundSrgb);
            var rendering = interop.CaptureForeground(context);

            Assert.True(sourceChanged);
            Assert.True(foregroundChanged);
            Assert.Contains(field, value => value != 0);
            Assert.Equal((0, 0, width, height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
            Assert.All(rendering.Coordinates(), point => Assert.Equal(Unpack(field[point.Y * width + point.X]), rendering[point.X, point.Y]));
        }
    }
}
