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
