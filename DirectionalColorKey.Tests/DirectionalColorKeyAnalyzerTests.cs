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
}
