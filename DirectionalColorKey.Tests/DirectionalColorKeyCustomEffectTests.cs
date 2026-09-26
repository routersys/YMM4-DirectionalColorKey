using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;

namespace DirectionalColorKey.Tests;

[Collection("Direct2D")]
public sealed class DirectionalColorKeyCustomEffectTests
{
    const int Width = 40;
    const int Height = 24;

    static readonly Bgra Green = Bgra.Opaque(0, 255, 0);
    static readonly Bgra Magenta = Bgra.Opaque(180, 40, 200);
    static readonly Bgra Yellow = Bgra.Opaque(40, 220, 240);
    static readonly Vector3 GreenLab = Oklab(Green);

    static float SrgbToLinear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    static Vector3 Linear(Bgra color) => new(SrgbToLinear(color.Red / 255f), SrgbToLinear(color.Green / 255f), SrgbToLinear(color.Blue / 255f));

    static Vector3 Oklab(Vector3 linear)
    {
        var l = MathF.Cbrt(0.4122214708f * linear.X + 0.5363325363f * linear.Y + 0.0514459929f * linear.Z);
        var m = MathF.Cbrt(0.2119034982f * linear.X + 0.6806995451f * linear.Y + 0.1073969566f * linear.Z);
        var s = MathF.Cbrt(0.0883024619f * linear.X + 0.2817188376f * linear.Y + 0.6299787005f * linear.Z);
        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    static Vector3 Oklab(Bgra color) => Oklab(Linear(color));

    static float GreenSpill(Bgra color)
    {
        var lab = Oklab(color);
        var direction = Vector2.Normalize(new Vector2(GreenLab.Y, GreenLab.Z));
        return Vector2.Dot(new Vector2(lab.Y, lab.Z), direction);
    }

    static DirectionalColorKeyCustomEffect CreateKey(IGraphicsDevicesAndContext devices)
        => new(devices)
        {
            BackgroundLab = GreenLab,
            BackgroundChromaDir = new Vector3(0f, GreenLab.Y, GreenLab.Z),
            NoiseThreshold = 0.02f,
            SpillStrength = 0f,
            OutputForeground = 1f,
        };

    static Rendering Render(IGraphicsDevicesAndContext devices, DirectionalColorKeyCustomEffect effect, ID2D1Image source, ID2D1Image foreground)
    {
        effect.SetInput(0, source, true);
        effect.SetInput(1, foreground, true);
        using var output = effect.Output;
        return Rendering.Capture(devices, output);
    }

    static bool Within(int tolerance, Bgra expected, Bgra actual)
        => Math.Abs(expected.Blue - actual.Blue) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance && Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance;

    [Fact]
    public void TheEffectIsEnabledOnceCreated()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new DirectionalColorKeyCustomEffect(context);

        Assert.True(effect.IsEnabled);
    }

    [Fact]
    public void EveryPropertyStartsFromZero()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new DirectionalColorKeyCustomEffect(context);

        Assert.Equal(
            (Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector3.Zero, 0f, 0f, 0f, 0f, 0f, Vector3.Zero, 0),
            (effect.Cluster0, effect.Cluster1, effect.Cluster2, effect.Cluster3, effect.BackgroundLab, effect.NoiseThreshold, effect.SpillStrength, effect.EdgeSoftness, effect.DespillBias, effect.OutputForeground, effect.BackgroundChromaDir, effect.ClusterCount));
    }

    [Fact]
    public void EveryPropertyReadsBackWhatWasWritten()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var effect = new DirectionalColorKeyCustomEffect(context)
        {
            Cluster0 = new Vector4(1f, 2f, 3f, 4f),
            Cluster1 = new Vector4(5f, 6f, 7f, 8f),
            Cluster2 = new Vector4(9f, 10f, 11f, 12f),
            Cluster3 = new Vector4(13f, 14f, 15f, 16f),
            BackgroundLab = new Vector3(17f, 18f, 19f),
            NoiseThreshold = 20f,
            SpillStrength = 21f,
            EdgeSoftness = 22f,
            DespillBias = 23f,
            OutputForeground = 24f,
            BackgroundChromaDir = new Vector3(25f, 26f, 27f),
            ClusterCount = 28,
        };

        Assert.Equal(
            (new Vector4(1f, 2f, 3f, 4f), new Vector4(5f, 6f, 7f, 8f), new Vector4(9f, 10f, 11f, 12f), new Vector4(13f, 14f, 15f, 16f), new Vector3(17f, 18f, 19f), 20f, 21f, 22f, 23f, 24f, new Vector3(25f, 26f, 27f), 28),
            (effect.Cluster0, effect.Cluster1, effect.Cluster2, effect.Cluster3, effect.BackgroundLab, effect.NoiseThreshold, effect.SpillStrength, effect.EdgeSoftness, effect.DespillBias, effect.OutputForeground, effect.BackgroundChromaDir, effect.ClusterCount));
    }

    [Fact]
    public void TheOutputBoundsFollowTheSourceAlone()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Magenta);
        using var foreground = SourceImage.Solid(context, 8, 8, Magenta);
        using var moved = new AffineTransform2D(context.DeviceContext)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
            BorderMode = BorderMode.Hard,
            TransformMatrix = Matrix3x2.CreateTranslation(50f, -6f),
        };
        moved.SetInput(0, foreground.Bitmap, true);
        using var movedOutput = moved.Output;
        using var effect = CreateKey(context);
        effect.SetInput(0, source.Bitmap, true);
        effect.SetInput(1, movedOutput, true);
        using var output = effect.Output;

        var bounds = context.DeviceContext.GetImageLocalBounds(output);

        Assert.Equal((0f, 0f, (float)Width, (float)Height), (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
    }

    [Fact]
    public void ATransparentSourceStaysTransparent()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Bgra.Transparent);
        using var foreground = SourceImage.Solid(context, Width, Height, Magenta);
        using var effect = CreateKey(context);

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.Equal(Bgra.Transparent, rendering[point.X, point.Y]));
    }

    [Fact]
    public void TheKeyColorIsRemoved()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Green);
        using var foreground = SourceImage.Solid(context, Width, Height, Magenta);
        using var effect = CreateKey(context);

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.Equal(Bgra.Transparent, rendering[point.X, point.Y]));
    }

    [Fact]
    public void AColorResolvedFromTheForegroundFieldKeepsItsColor()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Magenta);
        using var foreground = SourceImage.Solid(context, Width, Height, Magenta);
        using var effect = CreateKey(context);

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.True(Within(1, Magenta, rendering[point.X, point.Y]), $"({point.X}, {point.Y}) {rendering[point.X, point.Y]}"));
    }

    [Theory]
    [InlineData(255)]
    [InlineData(128)]
    public void TheMaskShowsTheAlphaInGray(byte sourceAlpha)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Magenta with { Alpha = sourceAlpha });
        using var foreground = SourceImage.Solid(context, Width, Height, Magenta);
        using var effect = CreateKey(context);
        effect.OutputForeground = 0f;

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.True(Within(1, new Bgra(sourceAlpha, sourceAlpha, sourceAlpha, sourceAlpha), rendering[point.X, point.Y]), $"({point.X}, {point.Y}) {rendering[point.X, point.Y]}"));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    public void EdgeSoftnessCutsTheLowerAlpha(float softness)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var green = Linear(Green);
        var mixed = green + (Linear(Magenta) - green) * 0.6f;
        var linearToSrgb = static (float value) => (byte)Math.Round(Math.Clamp(value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f, 0f, 1f) * 255f);
        var pixel = Bgra.Opaque(linearToSrgb(mixed.Z), linearToSrgb(mixed.Y), linearToSrgb(mixed.X));
        var seedDirection = Linear(Magenta) - green;
        var alpha = Math.Clamp(Vector3.Dot(Linear(pixel) - green, seedDirection) / Vector3.Dot(seedDirection, seedDirection), 0f, 1f);
        var expected = Math.Clamp((alpha - softness) / (1f - softness), 0f, 1f) * 255f;
        using var source = SourceImage.Solid(context, Width, Height, pixel);
        using var foreground = SourceImage.Solid(context, Width, Height, Magenta);
        using var effect = CreateKey(context);
        effect.EdgeSoftness = softness;

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.InRange(rendering[point.X, point.Y].Alpha, expected - 1.5f, expected + 1.5f));
    }

    [Fact]
    public void WithoutClustersAnUnresolvedColorIsRemoved()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Magenta);
        using var foreground = SourceImage.Solid(context, Width, Height, Bgra.Transparent);
        using var effect = CreateKey(context);

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.Equal(Bgra.Transparent, rendering[point.X, point.Y]));
    }

    [Fact]
    public void AClusterPointingAtAColorKeepsIt()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Magenta);
        using var foreground = SourceImage.Solid(context, Width, Height, Bgra.Transparent);
        using var effect = CreateKey(context);
        var displacement = Oklab(Magenta) - GreenLab;
        var direction = Vector3.Normalize(displacement);
        effect.Cluster0 = new Vector4(direction, displacement.Length());
        effect.ClusterCount = 1;

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.True(Within(1, Magenta, rendering[point.X, point.Y]), $"({point.X}, {point.Y}) {rendering[point.X, point.Y]}"));
    }

    [Fact]
    public void FullSpillRemovalTakesTheKeyTintOutOfTheForeground()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Yellow);
        using var foreground = SourceImage.Solid(context, Width, Height, Yellow);
        using var effect = CreateKey(context);
        effect.SpillStrength = 1f;

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.True(GreenSpill(Yellow) > 0.1f);
        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            Assert.Equal(byte.MaxValue, pixel.Alpha);
            Assert.InRange(GreenSpill(pixel), -0.03f, 0.03f);
        });
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 0.5f)]
    public void WithoutSpillRemovalTheForegroundKeepsItsColor(float strength, float bias)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = SourceImage.Solid(context, Width, Height, Yellow);
        using var foreground = SourceImage.Solid(context, Width, Height, Yellow);
        using var effect = CreateKey(context);
        effect.SpillStrength = strength;
        effect.DespillBias = bias;

        var rendering = Render(context, effect, source.Bitmap, foreground.Bitmap);

        Assert.All(rendering.Coordinates(), point => Assert.True(Within(1, Yellow, rendering[point.X, point.Y]), $"({point.X}, {point.Y}) {rendering[point.X, point.Y]}"));
    }
}
