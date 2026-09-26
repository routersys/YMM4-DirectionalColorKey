using System.Windows;
using Telemetry;

namespace DirectionalColorKey.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        DirectionalColorKeyTelemetry.EnsureStartedOnce();
        DirectionalColorKeyTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new DirectionalColorKeyEffect();

        Assert.Equal(Texts.DirectionalColorKeyEffectName, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
