using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace DirectionalColorKey.Tests;

public sealed class DirectionalColorKeyEffectTests
{
    static readonly Color DefaultBackgroundColor = Color.FromRgb(0, 255, 0);
    static readonly Color DefaultForegroundColor = Color.FromRgb(255, 255, 255);

    static PropertyInfo Property(string name) => typeof(DirectionalColorKeyEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(DirectionalColorKeyEffect effect)
        => [effect.ClusterCount, effect.OpaquePercentile, effect.NoiseThreshold, effect.SigmaColor, effect.EdgeSoftness, effect.SpillStrength, effect.DespillBias];

    [Theory]
    [InlineData(nameof(DirectionalColorKeyEffect.ClusterCount), 1d, 1d, 4d)]
    [InlineData(nameof(DirectionalColorKeyEffect.OpaquePercentile), 99d, 0d, 100d)]
    [InlineData(nameof(DirectionalColorKeyEffect.NoiseThreshold), 0.02d, 0d, 1d)]
    [InlineData(nameof(DirectionalColorKeyEffect.SigmaColor), 0.1d, 0.001d, 1d)]
    [InlineData(nameof(DirectionalColorKeyEffect.EdgeSoftness), 0d, 0d, 100d)]
    [InlineData(nameof(DirectionalColorKeyEffect.SpillStrength), 50d, 0d, 100d)]
    [InlineData(nameof(DirectionalColorKeyEffect.DespillBias), 0d, 0d, 1d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new DirectionalColorKeyEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void ColorsScaleModeAndOutputStartFromTheirDefaults()
    {
        var effect = new DirectionalColorKeyEffect();

        Assert.Equal(DefaultBackgroundColor, effect.BackgroundColor);
        Assert.Equal(DirectionalColorKeyScaleMode.Physical, effect.ScaleMode);
        Assert.Equal(DefaultForegroundColor, effect.ForegroundColor);
        Assert.True(effect.OutputForeground);
    }

    [Fact]
    public void ChangingColorsScaleModeOrOutputNotifiesTheEditor()
    {
        var effect = new DirectionalColorKeyEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.BackgroundColor = Colors.Blue;
        effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque;
        effect.ForegroundColor = Colors.Crimson;
        effect.OutputForeground = false;

        Assert.Equal(
            [nameof(DirectionalColorKeyEffect.BackgroundColor), nameof(DirectionalColorKeyEffect.ScaleMode), nameof(DirectionalColorKeyEffect.ForegroundColor), nameof(DirectionalColorKeyEffect.OutputForeground)],
            changed);
    }

    [Fact]
    public void AssigningAnUnchangedValueDoesNotNotify()
    {
        var effect = new DirectionalColorKeyEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.BackgroundColor = DefaultBackgroundColor;
        effect.ScaleMode = DirectionalColorKeyScaleMode.Physical;
        effect.ForegroundColor = DefaultForegroundColor;
        effect.OutputForeground = true;

        Assert.Empty(changed);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        var effect = new DirectionalColorKeyEffect();

        Assert.Equal(Texts.DirectionalColorKeyEffectName, effect.Label);
    }

    [Fact]
    public void TheSevenNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new DirectionalColorKeyEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new DirectionalColorKeyEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheEffectIsRegisteredForCompositionWithoutAviUtlSupport()
    {
        var attribute = typeof(DirectionalColorKeyEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.DirectionalColorKeyEffectName), attribute.Name);
        Assert.Equal([VideoEffectCategories.Composition], attribute.Categories);
        Assert.Equal(["directional color key", "dcsk", "chroma key", "方向クロマキー", "色分離キー", "変位方向キー"], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.DirectionalColorKeyEffectName, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(DirectionalColorKeyEffect.BackgroundColor), nameof(Texts.DirectionalColorKeyBackgroundColorName), nameof(Texts.DirectionalColorKeyBackgroundColorDesc), 100)]
    [InlineData(nameof(DirectionalColorKeyEffect.ClusterCount), nameof(Texts.DirectionalColorKeyClusterCountName), nameof(Texts.DirectionalColorKeyClusterCountDesc), 110)]
    [InlineData(nameof(DirectionalColorKeyEffect.ScaleMode), nameof(Texts.DirectionalColorKeyScaleModeName), nameof(Texts.DirectionalColorKeyScaleModeDesc), 120)]
    [InlineData(nameof(DirectionalColorKeyEffect.ForegroundColor), nameof(Texts.DirectionalColorKeyForegroundColorName), nameof(Texts.DirectionalColorKeyForegroundColorDesc), 130)]
    [InlineData(nameof(DirectionalColorKeyEffect.OpaquePercentile), nameof(Texts.DirectionalColorKeyOpaquePercentileName), nameof(Texts.DirectionalColorKeyOpaquePercentileDesc), 140)]
    [InlineData(nameof(DirectionalColorKeyEffect.NoiseThreshold), nameof(Texts.DirectionalColorKeyNoiseThresholdName), nameof(Texts.DirectionalColorKeyNoiseThresholdDesc), 150)]
    [InlineData(nameof(DirectionalColorKeyEffect.SigmaColor), nameof(Texts.DirectionalColorKeySigmaColorName), nameof(Texts.DirectionalColorKeySigmaColorDesc), 160)]
    [InlineData(nameof(DirectionalColorKeyEffect.EdgeSoftness), nameof(Texts.DirectionalColorKeyEdgeSoftnessName), nameof(Texts.DirectionalColorKeyEdgeSoftnessDesc), 170)]
    [InlineData(nameof(DirectionalColorKeyEffect.SpillStrength), nameof(Texts.DirectionalColorKeySpillStrengthName), nameof(Texts.DirectionalColorKeySpillStrengthDesc), 180)]
    [InlineData(nameof(DirectionalColorKeyEffect.DespillBias), nameof(Texts.DirectionalColorKeyDespillBiasName), nameof(Texts.DirectionalColorKeyDespillBiasDesc), 190)]
    [InlineData(nameof(DirectionalColorKeyEffect.OutputForeground), nameof(Texts.DirectionalColorKeyOutputForegroundName), nameof(Texts.DirectionalColorKeyOutputForegroundDesc), 200)]
    public void EveryParameterIsDisplayedInTheEffectGroupInOrder(string property, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(nameof(Texts.DirectionalColorKeyGroupName), display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(DirectionalColorKeyEffect.ClusterCount), "F0", "", 1d, 4d)]
    [InlineData(nameof(DirectionalColorKeyEffect.OpaquePercentile), "F1", "%", 0d, 100d)]
    [InlineData(nameof(DirectionalColorKeyEffect.NoiseThreshold), "F3", "", 0d, 0.2d)]
    [InlineData(nameof(DirectionalColorKeyEffect.SigmaColor), "F3", "", 0.001d, 0.5d)]
    [InlineData(nameof(DirectionalColorKeyEffect.EdgeSoftness), "F1", "%", 0d, 100d)]
    [InlineData(nameof(DirectionalColorKeyEffect.SpillStrength), "F1", "%", 0d, 100d)]
    [InlineData(nameof(DirectionalColorKeyEffect.DespillBias), "F3", "", 0d, 0.5d)]
    public void AnimatedParametersAreEditedWithAnimationSliders(string property, string format, string unit, double minimum, double maximum)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal(format, slider.StringFormat);
        Assert.Equal(unit, slider.UnitText);
        Assert.Equal(minimum, slider.DefaultMin);
        Assert.Equal(maximum, slider.DefaultMax);
    }

    [Theory]
    [InlineData(nameof(DirectionalColorKeyEffect.BackgroundColor))]
    [InlineData(nameof(DirectionalColorKeyEffect.ForegroundColor))]
    public void ColorsArePickedWithAColorPicker(string property)
    {
        Assert.NotNull(Attribute<ColorPickerAttribute>(property));
    }

    [Fact]
    public void TheScaleModeIsChosenFromACombo()
    {
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(DirectionalColorKeyEffect.ScaleMode)));
        Assert.Equal([DirectionalColorKeyScaleMode.Physical, DirectionalColorKeyScaleMode.Opaque, DirectionalColorKeyScaleMode.Foreground], Enum.GetValues<DirectionalColorKeyScaleMode>());
    }

    [Theory]
    [InlineData(DirectionalColorKeyScaleMode.Physical, 1)]
    [InlineData(DirectionalColorKeyScaleMode.Opaque, 2)]
    [InlineData(DirectionalColorKeyScaleMode.Foreground, 4)]
    public void TheScaleModesKeepTheirStoredValues(DirectionalColorKeyScaleMode mode, int value)
    {
        Assert.Equal(value, (int)mode);
    }

    [Theory]
    [InlineData(DirectionalColorKeyScaleMode.Physical, nameof(Texts.DirectionalColorKeyScaleModePhysicalName), nameof(Texts.DirectionalColorKeyScaleModePhysicalDesc))]
    [InlineData(DirectionalColorKeyScaleMode.Opaque, nameof(Texts.DirectionalColorKeyScaleModeOpaqueName), nameof(Texts.DirectionalColorKeyScaleModeOpaqueDesc))]
    [InlineData(DirectionalColorKeyScaleMode.Foreground, nameof(Texts.DirectionalColorKeyScaleModeForegroundName), nameof(Texts.DirectionalColorKeyScaleModeForegroundDesc))]
    public void EveryScaleModeIsDisplayedWithItsLocalizedName(DirectionalColorKeyScaleMode mode, string name, string description)
    {
        var display = typeof(DirectionalColorKeyScaleMode).GetField(mode.ToString())!.GetCustomAttribute<DisplayAttribute>()!;

        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(DirectionalColorKeyEffect.ForegroundColor), DirectionalColorKeyScaleMode.Foreground)]
    [InlineData(nameof(DirectionalColorKeyEffect.OpaquePercentile), DirectionalColorKeyScaleMode.Opaque)]
    public void ModeSpecificParametersAreShownOnlyForTheirScaleMode(string property, DirectionalColorKeyScaleMode mode)
    {
        var condition = Attribute<ShowPropertyEditorWhenAttribute>(property);

        Assert.Equal(nameof(DirectionalColorKeyEffect.ScaleMode), condition.PropertyName);
        Assert.Equal(mode, condition.Value);
    }

    [Fact]
    public void TheForegroundOutputIsSwitchedWithAToggle()
    {
        Assert.NotNull(Attribute<ToggleSliderAttribute>(nameof(DirectionalColorKeyEffect.OutputForeground)));
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new DirectionalColorKeyEffect
        {
            BackgroundColor = Colors.Blue,
            ScaleMode = DirectionalColorKeyScaleMode.Foreground,
            ForegroundColor = Colors.Crimson,
            OutputForeground = false,
        };
        var values = new[] { 3d, 75d, 0.15d, 0.25d, 40d, 90d, 0.3d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(Colors.Blue, clone.BackgroundColor);
        Assert.Equal(DirectionalColorKeyScaleMode.Foreground, clone.ScaleMode);
        Assert.Equal(Colors.Crimson, clone.ForegroundColor);
        Assert.False(clone.OutputForeground);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
