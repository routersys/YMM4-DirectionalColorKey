using System.Globalization;
using System.Numerics;
using System.Windows.Media;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;

namespace DirectionalColorKey.Tests;

[Collection("Direct2D")]
public sealed class DirectionalColorKeyEffectProcessorTests
{
    const int Width = 96;
    const int Height = 64;
    const int Length = 30;

    static readonly bool Direct3D12IsAvailable = GraphicsDevice.EnumerateDevices().Any();
    static readonly Bgra Magenta = Bgra.Opaque(180, 40, 200);
    static readonly Bgra Yellow = Bgra.Opaque(40, 220, 240);
    static readonly Bgra Blue = Bgra.Opaque(220, 60, 40);
    static readonly Bgra Gray = Bgra.Opaque(128, 128, 128);

    static void RequireDirect3D12()
    {
        if (!Direct3D12IsAvailable)
            Assert.Skip("Direct3D 12 is unavailable.");
    }

    static Bgra Blend(Bgra under, Bgra over, float coverage)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * coverage);
        return Bgra.Opaque(Channel(under.Blue, over.Blue), Channel(under.Green, over.Green), Channel(under.Red, over.Red));
    }

    static Bgra Scene(int x, int y)
    {
        var background = Bgra.Opaque((byte)(3 + 3 * MathF.Sin(x * 0.19f + y * 0.41f)), (byte)(248 + 6 * MathF.Cos(x * 0.23f - y * 0.29f)), (byte)(3 + 3 * MathF.Sin(x * 0.37f + y * 0.11f)));
        var disc = Math.Clamp(14f - MathF.Sqrt((x + 0.5f - 28f) * (x + 0.5f - 28f) + (y + 0.5f - 30f) * (y + 0.5f - 30f)) + 0.5f, 0f, 1f);
        var ball = Math.Clamp(9f - MathF.Sqrt((x + 0.5f - 70f) * (x + 0.5f - 70f) + (y + 0.5f - 18f) * (y + 0.5f - 18f)) + 0.5f, 0f, 1f);
        var color = Blend(Blend(background, Magenta, disc), Blue, ball);
        if (x is >= 56 and < 88 && y is >= 36 and < 58)
            color = x % 8 < 1 || y % 8 < 1 ? Bgra.Opaque(40, 40, 220) : Yellow;
        if (x is >= 4 and < 10 && y is >= 40 and < 60)
            color = Gray;
        return color;
    }

    static Bgra GreenWithSquare(int x, int y) => x is >= 32 and < 64 && y is >= 16 and < 48 ? Magenta : Bgra.Opaque(0, 255, 0);

    static Animation Linear(double from, double to)
        => Json.LoadFromText<Animation>(string.Create(CultureInfo.InvariantCulture, $$"""{"AnimationType":"直線移動","Values":[{"Value":{{from}}},{"Value":{{to}}}]}"""))!;

    static Rendering RenderFrame(IGraphicsDevicesAndContext devices, IVideoEffectProcessor processor, int frame)
    {
        processor.Update(EffectDescriptions.At(frame, Length));
        return Rendering.Capture(devices, processor.Output);
    }

    static Rendering RenderFresh(IGraphicsDevicesAndContext devices, DirectionalColorKeyEffect effect, ID2D1Image image)
    {
        using var processor = effect.CreateVideoEffect(devices);
        processor.SetInput(image);
        return RenderFrame(devices, processor, 0);
    }

    [Fact]
    public void TheProcessorHandsTheDrawDescriptionBackUnchanged()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Scene);
        using var processor = new DirectionalColorKeyEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var description = EffectDescriptions.At(0, Length);

        var draw = processor.Update(description);

        Assert.Same(description.DrawDescription, draw);
    }

    [Fact]
    public void TheBackgroundIsRemovedAndTheForegroundKept()
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, GreenWithSquare);

        var rendering = RenderFresh(context, new DirectionalColorKeyEffect(), source.Bitmap);

        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point =>
        {
            var pixel = rendering[point.X, point.Y];
            if (source[point.X, point.Y] == Magenta)
                Assert.True(Math.Abs(pixel.Blue - Magenta.Blue) <= 1 && Math.Abs(pixel.Green - Magenta.Green) <= 1 && Math.Abs(pixel.Red - Magenta.Red) <= 1 && pixel.Alpha == byte.MaxValue, $"({point.X}, {point.Y}) {pixel}");
            else
                Assert.Equal(Bgra.Transparent, pixel);
        });
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(-37, 21)]
    public void TheKeyTravelsWithTheImage(int dx, int dy)
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Scene);
        using var moved = new AffineTransform2D(context.DeviceContext)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor,
            BorderMode = BorderMode.Hard,
            TransformMatrix = Matrix3x2.CreateTranslation(dx, dy),
        };
        moved.SetInput(0, source.Bitmap, true);
        using var movedOutput = moved.Output;
        var inPlace = RenderFresh(context, new DirectionalColorKeyEffect(), source.Bitmap);

        var travelled = RenderFresh(context, new DirectionalColorKeyEffect(), movedOutput);

        Assert.Equal((inPlace.Left + dx, inPlace.Top + dy, inPlace.Width, inPlace.Height), (travelled.Left, travelled.Top, travelled.Width, travelled.Height));
        Assert.All(inPlace.Coordinates(), point => Assert.True(inPlace[point.X, point.Y] == travelled[point.X + dx, point.Y + dy], $"({point.X}, {point.Y})"));
    }

    [Fact]
    public void ReturningToAFrameReproducesItExactly()
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Scene);
        using var processor = new DirectionalColorKeyEffect().CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var first = RenderFrame(context, processor, 4);
        RenderFrame(context, processor, 9);
        var again = RenderFrame(context, processor, 4);

        Assert.True(first.SamePixelsAs(again));
    }

    [Fact]
    public void ANewProcessorDrawsTheSameKey()
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Scene);
        var effect = new DirectionalColorKeyEffect();
        effect.ClusterCount.Values[0].Value = 4;
        var first = RenderFresh(context, effect, source.Bitmap);

        var second = RenderFresh(context, effect, source.Bitmap);

        Assert.True(first.SamePixelsAs(second));
    }

    [Fact]
    public void AnimatedEdgeSoftnessIsReadAtEachFrame()
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Scene);
        var effect = new DirectionalColorKeyEffect();
        effect.EdgeSoftness.CopyFrom(Linear(0d, 90d));
        var still = new DirectionalColorKeyEffect();
        still.EdgeSoftness.Values[0].Value = effect.EdgeSoftness.GetValue(Length - 1, Length, EffectDescriptions.Fps);
        var expectedEnd = RenderFresh(context, still, source.Bitmap);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);

        var start = RenderFrame(context, processor, 0);
        var end = RenderFrame(context, processor, Length - 1);

        Assert.True(start.SamePixelsAs(RenderFresh(context, new DirectionalColorKeyEffect(), source.Bitmap)));
        Assert.True(end.SamePixelsAs(expectedEnd));
    }

    public static readonly TheoryData<string, Action<DirectionalColorKeyEffect>, Action<DirectionalColorKeyEffect>> LaterChanges = new()
    {
        { nameof(DirectionalColorKeyEffect.BackgroundColor), _ => { }, effect => effect.BackgroundColor = Color.FromRgb(0, 200, 0) },
        { nameof(DirectionalColorKeyEffect.ClusterCount), _ => { }, effect => effect.ClusterCount.Values[0].Value = 4 },
        { nameof(DirectionalColorKeyEffect.ScaleMode), _ => { }, effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground },
        { nameof(DirectionalColorKeyEffect.ForegroundColor), effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Foreground, effect => effect.ForegroundColor = Color.FromRgb(40, 200, 60) },
        { nameof(DirectionalColorKeyEffect.OpaquePercentile), effect => effect.ScaleMode = DirectionalColorKeyScaleMode.Opaque, effect => effect.OpaquePercentile.Values[0].Value = 50 },
        { nameof(DirectionalColorKeyEffect.NoiseThreshold), effect => effect.ClusterCount.Values[0].Value = 4, effect => effect.NoiseThreshold.Values[0].Value = 0.2 },
        { nameof(DirectionalColorKeyEffect.SigmaColor), effect => effect.ClusterCount.Values[0].Value = 4, effect => effect.SigmaColor.Values[0].Value = 0.001 },
        { nameof(DirectionalColorKeyEffect.EdgeSoftness), _ => { }, effect => effect.EdgeSoftness.Values[0].Value = 50 },
        { nameof(DirectionalColorKeyEffect.SpillStrength), _ => { }, effect => effect.SpillStrength.Values[0].Value = 100 },
        { nameof(DirectionalColorKeyEffect.DespillBias), _ => { }, effect => effect.DespillBias.Values[0].Value = 0.05 },
        { nameof(DirectionalColorKeyEffect.OutputForeground), _ => { }, effect => effect.OutputForeground = false },
    };

    [Theory]
    [MemberData(nameof(LaterChanges))]
    public void EverySettingChangedAfterTheFirstFrameDrawsLikeAFreshProcessor(string setting, Action<DirectionalColorKeyEffect> prepare, Action<DirectionalColorKeyEffect> change)
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Scene);
        var effect = new DirectionalColorKeyEffect();
        prepare(effect);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(source.Bitmap);
        var before = RenderFrame(context, processor, 0);
        change(effect);
        var expected = RenderFresh(context, effect, source.Bitmap);

        var after = RenderFrame(context, processor, 0);

        Assert.False(before.SamePixelsAs(after), setting);
        Assert.True(after.SamePixelsAs(expected), setting);
    }

    [Theory]
    [InlineData(32, 128)]
    [InlineData(128, 32)]
    public void AResizedSourceIsKeyedLikeAFreshOne(int firstSize, int secondSize)
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var first = new SourceImage(context, firstSize, firstSize / 2, Scene);
        using var second = new SourceImage(context, secondSize, secondSize / 2, Scene);
        var effect = new DirectionalColorKeyEffect { ScaleMode = DirectionalColorKeyScaleMode.Opaque };
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(first.Bitmap);
        RenderFrame(context, processor, 0);
        var expected = RenderFresh(context, effect, second.Bitmap);

        processor.SetInput(second.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.True(after.SamePixelsAs(expected));
    }

    [Fact]
    public void ANewImageAtTheSameFrameIsKeyedLikeOneAtTheNextFrame()
    {
        RequireDirect3D12();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var empty = SourceImage.Solid(context, Width, Height, Bgra.Transparent);
        using var source = new SourceImage(context, Width, Height, Scene);
        var effect = new DirectionalColorKeyEffect();
        using var nextFrame = effect.CreateVideoEffect(context);
        nextFrame.SetInput(empty.Bitmap);
        RenderFrame(context, nextFrame, 0);
        nextFrame.SetInput(source.Bitmap);
        var expected = RenderFrame(context, nextFrame, 1);
        using var processor = effect.CreateVideoEffect(context);
        processor.SetInput(empty.Bitmap);
        RenderFrame(context, processor, 0);

        processor.SetInput(source.Bitmap);
        var after = RenderFrame(context, processor, 0);

        Assert.True(after.SamePixelsAs(expected));
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = new DirectionalColorKeyEffect().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }
}
