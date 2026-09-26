namespace DirectionalColorKey.Harness;

internal static class SyntheticImage
{
    const int GridPitch = 24;
    const int GridLine = 2;
    const float EdgeWidth = 6f;
    const float TranslucentAlpha = 0.5f;

    static readonly (float Red, float Green, float Blue) MagentaColor = (200f / 255f, 40f / 255f, 180f / 255f);
    static readonly (float Red, float Green, float Blue) BlueColor = (40f / 255f, 60f / 255f, 220f / 255f);
    static readonly (float Red, float Green, float Blue) YellowColor = (240f / 255f, 220f / 255f, 40f / 255f);
    static readonly (float Red, float Green, float Blue) RedColor = (220f / 255f, 40f / 255f, 40f / 255f);
    static readonly (float Red, float Green, float Blue) GrayColor = (128f / 255f, 128f / 255f, 128f / 255f);

    public static byte[] Create(int width, int height)
    {
        var pixels = new byte[width * height * HarnessImage.BytesPerPixel];
        var unit = MathF.Min(width, height);
        var discCenterX = width * 0.30f;
        var discCenterY = height * 0.42f;
        var discRadius = unit * 0.26f;
        var ballCenterX = width * 0.74f;
        var ballCenterY = height * 0.30f;
        var ballRadius = unit * 0.14f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var positionX = x + 0.5f;
                var positionY = y + 0.5f;
                var u = positionX / width;
                var v = positionY / height;
                if (u is >= 0.02f and < 0.10f && v is >= 0.04f and < 0.16f)
                    continue;

                var color = (
                    Red: (3f + 3f * MathF.Sin(x * 0.37f + y * 0.11f)) / 255f,
                    Green: (248f + 6f * MathF.Cos(x * 0.23f - y * 0.29f)) / 255f,
                    Blue: (3f + 3f * MathF.Sin(x * 0.19f + y * 0.41f)) / 255f);
                var alpha = 1f;

                color = Blend(color, MagentaColor, Disc(positionX, positionY, discCenterX, discCenterY, discRadius));
                color = Blend(color, BlueColor, Disc(positionX, positionY, ballCenterX, ballCenterY, ballRadius));

                var plateCoverage = Rectangle(positionX, positionY, width * 0.54f, width * 0.92f, height * 0.56f, height * 0.90f);
                var onGrid = x % GridPitch < GridLine || y % GridPitch < GridLine;
                color = Blend(color, onGrid ? RedColor : YellowColor, plateCoverage);

                color = Blend(color, GrayColor, Rectangle(positionX, positionY, width * 0.04f, width * 0.10f, height * 0.56f, height * 0.94f));

                if (u is >= 0.20f and < 0.44f && v is >= 0.76f and < 0.94f)
                {
                    color = MagentaColor;
                    alpha = TranslucentAlpha;
                }

                var offset = (y * width + x) * HarnessImage.BytesPerPixel;
                pixels[offset] = ToByte(color.Blue * alpha);
                pixels[offset + 1] = ToByte(color.Green * alpha);
                pixels[offset + 2] = ToByte(color.Red * alpha);
                pixels[offset + 3] = ToByte(alpha);
            }
        }

        return pixels;
    }

    static float Disc(float x, float y, float centerX, float centerY, float radius)
    {
        var distance = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
        return Math.Clamp((radius - distance) / EdgeWidth + 0.5f, 0f, 1f);
    }

    static float Rectangle(float x, float y, float left, float right, float top, float bottom)
    {
        var inset = MathF.Min(MathF.Min(x - left, right - x), MathF.Min(y - top, bottom - y));
        return Math.Clamp(inset + 0.5f, 0f, 1f);
    }

    static (float Red, float Green, float Blue) Blend((float Red, float Green, float Blue) under, (float Red, float Green, float Blue) over, float coverage)
        => (
            under.Red + (over.Red - under.Red) * coverage,
            under.Green + (over.Green - under.Green) * coverage,
            under.Blue + (over.Blue - under.Blue) * coverage);

    static byte ToByte(float premultiplied) => (byte)MathF.Round(Math.Clamp(premultiplied, 0f, 1f) * byte.MaxValue);
}
