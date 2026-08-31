using System.Numerics;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;

namespace DirectionalColorKey.Tests;

public sealed class DirectionalColorKeyEffectTests
{
    private static double ValueAt(Animation animation) => animation.GetValue(0, 1, 30);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new DirectionalColorKeyEffect();

        Assert.Equal(Color.FromRgb(0, 255, 0), effect.BackgroundColor);
        Assert.Equal(DirectionalColorKeyScaleMode.Physical, effect.ScaleMode);
        Assert.Equal(Color.FromRgb(255, 255, 255), effect.ForegroundColor);
        Assert.True(effect.OutputForeground);

        Assert.Equal(1d, ValueAt(effect.ClusterCount), 6);
        Assert.Equal(99d, ValueAt(effect.OpaquePercentile), 6);
        Assert.Equal(0.02d, ValueAt(effect.NoiseThreshold), 6);
        Assert.Equal(0.1d, ValueAt(effect.SigmaColor), 6);
        Assert.Equal(0d, ValueAt(effect.EdgeSoftness), 6);
        Assert.Equal(50d, ValueAt(effect.SpillStrength), 6);
        Assert.Equal(0d, ValueAt(effect.DespillBias), 6);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new DirectionalColorKeyEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Fact]
    public void ScaleModeValuesAreDistinctFlags()
    {
        Assert.Equal(1, (int)DirectionalColorKeyScaleMode.Physical);
        Assert.Equal(2, (int)DirectionalColorKeyScaleMode.Opaque);
        Assert.Equal(4, (int)DirectionalColorKeyScaleMode.Foreground);
    }
}

public sealed class DirectionalColorKeyAnalyzerTests
{
    private static readonly Vector3 BackgroundLab = new(0.51975185f, -0.14032371f, 0.10767135f);
    private static readonly Vector3 BackgroundSrgb = new(0f, 1f, 0f);

    private static Vector3 WhiteDirection()
    {
        Vector3 direction = new Vector3(1f, 0f, 0f) - BackgroundLab;

        return direction / direction.Length();
    }

    private const int ForegroundPixel = unchecked((int)0xFFFF0000);
    private const int BackgroundPixel = unchecked((int)0xFF00FF00);

    private static int[] CreateImage(int width, int height)
    {
        int[] pixels = new int[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool foreground = x >= width / 4 && x < width * 3 / 4 && y >= height / 4 && y < height * 3 / 4;

                pixels[(y * width) + x] = foreground ? ForegroundPixel : BackgroundPixel;
            }
        }

        return pixels;
    }

    private static void Analyze(DirectionalColorKeyAnalyzer analyzer, int[] image, int width, int height, int clusters, DirectionalColorKeyScaleMode mode)
    {
        analyzer.Analyze(
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
            0.5f,
            static (_, floorValue) => MathF.Max(floorValue, 0.5f),
            true);
    }

    [Fact]
    public void ClusterCountIsClampedToRequestedRange()
    {
        using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
        if (analyzer is null) { Assert.Skip("Direct3D 12 is unavailable."); return; }

        const int Width = 128;
        const int Height = 96;

        int[] image = CreateImage(Width, Height);

        Analyze(analyzer, image, Width, Height, 1, DirectionalColorKeyScaleMode.Physical);
        Assert.Equal(1, analyzer.ClusterCount);

        Analyze(analyzer, image, Width, Height, 4, DirectionalColorKeyScaleMode.Physical);
        Assert.InRange(analyzer.ClusterCount, 1, 4);

        Analyze(analyzer, image, Width, Height, 99, DirectionalColorKeyScaleMode.Physical);
        Assert.InRange(analyzer.ClusterCount, 1, 4);
    }

    [Fact]
    public void LambdaIsPositiveForEveryCluster()
    {
        using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
        if (analyzer is null) { Assert.Skip("Direct3D 12 is unavailable."); return; }

        const int Width = 128;
        const int Height = 96;

        Analyze(analyzer, CreateImage(Width, Height), Width, Height, 4, DirectionalColorKeyScaleMode.Physical);

        for (int cluster = 0; cluster < analyzer.ClusterCount; cluster++)
        {
            Assert.True(analyzer.GetLambda(cluster) > 0f);
        }
    }

    [Fact]
    public void ForegroundFieldRecoversTheForegroundColor()
    {
        using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
        if (analyzer is null) { Assert.Skip("Direct3D 12 is unavailable."); return; }

        const int Width = 128;
        const int Height = 96;

        int[] image = CreateImage(Width, Height);

        Analyze(analyzer, image, Width, Height, 1, DirectionalColorKeyScaleMode.Foreground);

        ReadOnlySpan<int> field = analyzer.BuildForegroundField(Width, Height, BackgroundLab, BackgroundSrgb);

        Assert.Equal(Width * Height, field.Length);

        int centerIndex = ((Height / 2) * Width) + (Width / 2);

        Assert.Equal(ForegroundPixel, field[centerIndex]);
    }

    [Fact]
    public void RepeatedAnalyzeOnIdenticalInputIsStable()
    {
        using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
        if (analyzer is null) { Assert.Skip("Direct3D 12 is unavailable."); return; }

        const int Width = 128;
        const int Height = 96;

        int[] image = CreateImage(Width, Height);

        Analyze(analyzer, image, Width, Height, 1, DirectionalColorKeyScaleMode.Physical);

        Vector3 first = analyzer.GetCenter(0);

        Analyze(analyzer, image, Width, Height, 1, DirectionalColorKeyScaleMode.Physical);

        Vector3 second = analyzer.GetCenter(0);

        Assert.Equal(first.X, second.X, 4);
        Assert.Equal(first.Y, second.Y, 4);
        Assert.Equal(first.Z, second.Z, 4);
    }

    [Fact]
    public void AnalyzeAcceptsSizeChangesWithoutFailing()
    {
        using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
        if (analyzer is null) { Assert.Skip("Direct3D 12 is unavailable."); return; }

        foreach ((int width, int height) in new[] { (64, 48), (256, 144), (128, 96) })
        {
            Analyze(analyzer, CreateImage(width, height), width, height, 2, DirectionalColorKeyScaleMode.Physical);

            Assert.InRange(analyzer.ClusterCount, 1, 4);
        }
    }
}
