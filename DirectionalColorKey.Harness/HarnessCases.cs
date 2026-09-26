using System.Windows.Media;

namespace DirectionalColorKey.Harness;

internal static class HarnessCases
{
    static readonly Color Blue = Color.FromRgb(0, 0, 255);
    static readonly Color Magenta = Color.FromRgb(200, 40, 180);

    public static IEnumerable<(string Name, DirectionalColorKeyEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("default-frames-0-8", Create(), Enumerable.Range(0, 9).ToArray());
        yield return ("background-color-blue", Create(effect => effect.BackgroundColor = Blue), [0]);
        yield return ("cluster-count-4", Create(effect => effect.ClusterCount.Values[0].Value = 4), [0]);
        yield return ("cluster-count-4-scale-mode-opaque", Create(effect =>
        {
            effect.ClusterCount.Values[0].Value = 4;
            effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque;
        }), [0]);
        yield return ("opaque-percentile-50", Create(effect =>
        {
            effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque;
            effect.OpaquePercentile.Values[0].Value = 50;
        }), [0]);
        yield return ("scale-mode-foreground", Create(effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground), [0]);
        yield return ("foreground-color-magenta", Create(effect =>
        {
            effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground;
            effect.ForegroundColor = Magenta;
        }), [0]);
        yield return ("noise-threshold-0", Create(effect => effect.NoiseThreshold.Values[0].Value = 0), [0]);
        yield return ("noise-threshold-0.2", Create(effect => effect.NoiseThreshold.Values[0].Value = 0.2), [0]);
        yield return ("cluster-count-4-sigma-color-0.001", Create(effect =>
        {
            effect.ClusterCount.Values[0].Value = 4;
            effect.SigmaColor.Values[0].Value = 0.001;
        }), [0]);
        yield return ("cluster-count-4-sigma-color-0.5", Create(effect =>
        {
            effect.ClusterCount.Values[0].Value = 4;
            effect.SigmaColor.Values[0].Value = 0.5;
        }), [0]);
        yield return ("edge-softness-50", Create(effect => effect.EdgeSoftness.Values[0].Value = 50), [0]);
        yield return ("spill-strength-0", Create(effect => effect.SpillStrength.Values[0].Value = 0), [0]);
        yield return ("spill-strength-100", Create(effect => effect.SpillStrength.Values[0].Value = 100), [0]);
        yield return ("despill-bias-0.05", Create(effect => effect.DespillBias.Values[0].Value = 0.05), [0]);
        yield return ("output-foreground-off", Create(effect => effect.OutputForeground = false), [0]);
    }

    public static IEnumerable<(string Name, Func<DirectionalColorKeyEffect> Create, Action<DirectionalColorKeyEffect> Change, int Frame)> Transitions()
    {
        yield return ("background-color-green-to-blue", () => Create(), effect => effect.BackgroundColor = Blue, 0);
        yield return ("cluster-count-1-to-4", () => Create(), effect => effect.ClusterCount.Values[0].Value = 4, 0);
        yield return ("cluster-count-4-scale-mode-physical-to-opaque", () => Create(effect => effect.ClusterCount.Values[0].Value = 4), effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque, 0);
        yield return ("opaque-percentile-99-to-50", () => Create(effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque), effect => effect.OpaquePercentile.Values[0].Value = 50, 0);
        yield return ("scale-mode-physical-to-foreground", () => Create(), effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground, 0);
        yield return ("foreground-color-white-to-magenta", () => Create(effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground), effect => effect.ForegroundColor = Magenta, 0);
        yield return ("noise-threshold-0.02-to-0.2", () => Create(), effect => effect.NoiseThreshold.Values[0].Value = 0.2, 0);
        yield return ("cluster-count-4-sigma-color-0.1-to-0.5", () => Create(effect => effect.ClusterCount.Values[0].Value = 4), effect => effect.SigmaColor.Values[0].Value = 0.5, 0);
        yield return ("edge-softness-0-to-50", () => Create(), effect => effect.EdgeSoftness.Values[0].Value = 50, 0);
        yield return ("spill-strength-50-to-100", () => Create(), effect => effect.SpillStrength.Values[0].Value = 100, 0);
        yield return ("despill-bias-0-to-0.05", () => Create(), effect => effect.DespillBias.Values[0].Value = 0.05, 0);
        yield return ("output-foreground-on-to-off", () => Create(), effect => effect.OutputForeground = false, 0);
    }

    public static IEnumerable<(string Name, DirectionalColorKeyEffect Effect)> Benchmarks()
    {
        yield return ("default", Create());
        yield return ("cluster-count-4", Create(effect => effect.ClusterCount.Values[0].Value = 4));
        yield return ("opaque-percentile-50", Create(effect =>
        {
            effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque;
            effect.OpaquePercentile.Values[0].Value = 50;
        }));
        yield return ("scale-mode-foreground", Create(effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground));
    }

    public static DirectionalColorKeyEffect Create(Action<DirectionalColorKeyEffect>? configure = null)
    {
        var effect = new DirectionalColorKeyEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
