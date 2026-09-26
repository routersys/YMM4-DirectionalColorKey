using System.Windows;

namespace DirectionalColorKey.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new DirectionalColorKeyEffect();

        Assert.Equal(Texts.DirectionalColorKeyEffectName, effect.Label);
    }
}
