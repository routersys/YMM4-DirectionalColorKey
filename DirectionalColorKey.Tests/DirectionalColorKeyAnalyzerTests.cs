using System.Numerics;

namespace DirectionalColorKey.Tests;

[Collection("Direct3D12")]
public sealed class DirectionalColorKeyAnalyzerTests
{
    const int ForegroundPixel = unchecked((int)0xFFFF0000);
    const int BackgroundPixel = unchecked((int)0xFF00FF00);

    static readonly Vector3 BackgroundLab = new(0.51975185f, -0.14032371f, 0.10767135f);
    static readonly Vector3 BackgroundSrgb = new(0f, 1f, 0f);

    static readonly bool Direct3D12IsAvailable = GraphicsDevice.EnumerateDevices().Any();

    static DirectionalColorKeyAnalyzer CreateAnalyzer()
    {
        if (!Direct3D12IsAvailable)
            Assert.Skip("Direct3D 12 is unavailable.");
        var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
        Assert.NotNull(analyzer);
        return analyzer;
    }

    static float Linear(int channel)
    {
        var c = channel / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    static Vector3 Lab(int pixel)
    {
        var r = Linear((pixel >> 16) & 0xFF);
        var g = Linear((pixel >> 8) & 0xFF);
        var b = Linear(pixel & 0xFF);
        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return new(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    static Vector3 WhiteDirection()
    {
        var direction = new Vector3(1f, 0f, 0f) - BackgroundLab;
        return direction / direction.Length();
    }

    static int[] CreateImage(int width, int height)
    {
        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var foreground = x >= width / 4 && x < width * 3 / 4 && y >= height / 4 && y < height * 3 / 4;
                pixels[y * width + x] = foreground ? ForegroundPixel : BackgroundPixel;
            }
        }

        return pixels;
    }

    static void Analyze(DirectionalColorKeyAnalyzer analyzer, int[] image, int width, int height, int clusters, DirectionalColorKeyScaleMode mode, float foregroundLambda = 0.5f, bool resetLambdaSmoothing = true)
        => analyzer.Analyze(
            image.AsSpan(0, width * height),
            width,
            height,
            BackgroundLab,
            WhiteDirection(),
            clusters,
            0.02f,
            0.1f,
            mode,
            0.99f,
            foregroundLambda,
            static (_, floorValue) => MathF.Max(floorValue, 0.5f),
            resetLambdaSmoothing);

    [Fact]
    public void ClusterCountIsClampedToRequestedRange()
    {
        using var analyzer = CreateAnalyzer();
        var image = CreateImage(128, 96);

        Analyze(analyzer, image, 128, 96, 1, DirectionalColorKeyScaleMode.Physical);
        Assert.Equal(1, analyzer.ClusterCount);

        Analyze(analyzer, image, 128, 96, 4, DirectionalColorKeyScaleMode.Physical);
        Assert.InRange(analyzer.ClusterCount, 1, 4);

        Analyze(analyzer, image, 128, 96, 99, DirectionalColorKeyScaleMode.Physical);
        Assert.InRange(analyzer.ClusterCount, 1, 4);
    }

    [Fact]
    public void LambdaIsPositiveForEveryCluster()
    {
        using var analyzer = CreateAnalyzer();

        Analyze(analyzer, CreateImage(128, 96), 128, 96, 4, DirectionalColorKeyScaleMode.Physical);

        for (var cluster = 0; cluster < analyzer.ClusterCount; cluster++)
            Assert.True(analyzer.GetLambda(cluster) > 0f);
    }

    [Fact]
    public void ForegroundFieldRecoversTheForegroundColor()
    {
        using var analyzer = CreateAnalyzer();
        Analyze(analyzer, CreateImage(128, 96), 128, 96, 1, DirectionalColorKeyScaleMode.Foreground);

        var field = analyzer.BuildForegroundField(128, 96, BackgroundLab, BackgroundSrgb);

        Assert.Equal(128 * 96, field.Length);
        Assert.Equal(ForegroundPixel, field[48 * 128 + 64]);
    }

    [Fact]
    public void RepeatedAnalyzeOnIdenticalInputIsStable()
    {
        using var analyzer = CreateAnalyzer();
        var image = CreateImage(128, 96);
        Analyze(analyzer, image, 128, 96, 1, DirectionalColorKeyScaleMode.Physical);
        var first = analyzer.GetCenter(0);

        Analyze(analyzer, image, 128, 96, 1, DirectionalColorKeyScaleMode.Physical);
        var second = analyzer.GetCenter(0);

        Assert.Equal(first.X, second.X, 4);
        Assert.Equal(first.Y, second.Y, 4);
        Assert.Equal(first.Z, second.Z, 4);
    }

    [Fact]
    public void AnalyzeAcceptsSizeChangesWithoutFailing()
    {
        using var analyzer = CreateAnalyzer();

        foreach (var (width, height) in new[] { (64, 48), (256, 144), (128, 96) })
        {
            Analyze(analyzer, CreateImage(width, height), width, height, 2, DirectionalColorKeyScaleMode.Physical);

            Assert.InRange(analyzer.ClusterCount, 1, 4);
        }
    }

    [Fact]
    public void TheLambdaMovesAQuarterOfTheWayUnlessItsSmoothingIsReset()
    {
        using var analyzer = CreateAnalyzer();
        var image = CreateImage(128, 96);
        Analyze(analyzer, image, 128, 96, 1, DirectionalColorKeyScaleMode.Foreground, foregroundLambda: 0.5f);

        Analyze(analyzer, image, 128, 96, 1, DirectionalColorKeyScaleMode.Foreground, foregroundLambda: 0.9f, resetLambdaSmoothing: false);
        var smoothed = analyzer.GetLambda(0);
        Analyze(analyzer, image, 128, 96, 1, DirectionalColorKeyScaleMode.Foreground, foregroundLambda: 0.9f);
        var reset = analyzer.GetLambda(0);

        Assert.Equal(0.6f, smoothed, 5);
        Assert.Equal(0.9f, reset, 5);
    }

    [Fact]
    public void AnAnalyzerReusedAfterASmallChangeFindsTheSameCenterAsAFreshOne()
    {
        const int BluePixel = unchecked((int)0xFF0000FF);
        using var reused = CreateAnalyzer();
        using var fresh = CreateAnalyzer();
        var before = CreateImage(128, 96);
        var after = (int[])before.Clone();
        for (var y = 8; y < 16; y++)
        {
            for (var x = 8; x < 16; x++)
                after[y * 128 + x] = BluePixel;
        }

        Analyze(reused, before, 128, 96, 1, DirectionalColorKeyScaleMode.Physical);
        var first = reused.GetCenter(0);
        Analyze(reused, after, 128, 96, 1, DirectionalColorKeyScaleMode.Physical);
        Analyze(fresh, after, 128, 96, 1, DirectionalColorKeyScaleMode.Physical);

        Assert.NotEqual(first, fresh.GetCenter(0));
        Assert.Equal(fresh.GetCenter(0), reused.GetCenter(0));
    }

    [Fact]
    public void AnOpaqueScaleSitsInTheMiddleOfTheBinHoldingTheProjection()
    {
        var background = Lab(BackgroundPixel);
        using var analyzer = CreateAnalyzer();

        analyzer.Analyze(
            CreateImage(128, 96),
            128,
            96,
            background,
            Vector3.Normalize(new Vector3(1f, 0f, 0f) - background),
            1,
            0.02f,
            0.1f,
            DirectionalColorKeyScaleMode.Opaque,
            0.99f,
            0.5f,
            static (_, floorValue) => MathF.Max(floorValue, 0.5f),
            true);
        var projection = Vector3.Dot(Lab(ForegroundPixel) - background, analyzer.GetCenter(0));

        Assert.Equal((MathF.Floor(projection * 256) + 0.5f) / 256, analyzer.GetLambda(0), 6);
    }
}
