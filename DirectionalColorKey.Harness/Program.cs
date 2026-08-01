using System.Diagnostics;
using System.Numerics;
using DirectionalColorKey;

using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
if (analyzer is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

string library = typeof(GraphicsDevice).Assembly.GetName().Name ?? "unknown";
string version = typeof(GraphicsDevice).Assembly.GetName().Version?.ToString() ?? "unknown";

Console.WriteLine($"library: {library} {version}");
Console.WriteLine($"device: {GraphicsDevice.GetDefault()}");
Console.WriteLine();

Vector3 backgroundSrgb = new(0f, 177f / 255f, 64f / 255f);
Vector3 backgroundLab = ToOklab(ToLinear(backgroundSrgb));
Vector3 whiteDirection = ComputeWhiteDirection(backgroundLab);

(int Width, int Height)[] sizes = [(1280, 720), (1920, 1080), (3840, 2160)];

Console.WriteLine("size          clusters  mode        analyze(ms)  foreground(ms)  total(ms)");
Console.WriteLine("------------  --------  ----------  -----------  --------------  ---------");

foreach ((int width, int height) in sizes)
{
    int[] image = CreateTestImage(width, height);

    foreach (int clusters in new[] { 1, 4 })
    {
        foreach (DirectionalColorKeyScaleMode mode in new[] { DirectionalColorKeyScaleMode.Physical, DirectionalColorKeyScaleMode.Foreground })
        {
            Measure(analyzer, image, width, height, backgroundLab, backgroundSrgb, whiteDirection, clusters, mode, out double analyze, out double foreground);

            Console.WriteLine($"{width,5}x{height,-6}  {clusters,8}  {mode,-10}  {analyze,11:F2}  {foreground,14:F2}  {analyze + foreground,9:F2}");
        }
    }
}

return 0;

static void Measure(
    DirectionalColorKeyAnalyzer analyzer,
    int[] image,
    int width,
    int height,
    Vector3 backgroundLab,
    Vector3 backgroundSrgb,
    Vector3 whiteDirection,
    int clusters,
    DirectionalColorKeyScaleMode mode,
    out double analyzeMilliseconds,
    out double foregroundMilliseconds)
{
    const int WarmupFrames = 3;
    const int MeasuredFrames = 11;

    for (int frame = 0; frame < WarmupFrames; frame++)
    {
        RunAnalyze(analyzer, image, width, height, backgroundLab, whiteDirection, clusters, mode, frame == 0);
        _ = analyzer.BuildForegroundField(width, height, backgroundLab, backgroundSrgb);
    }

    double[] analyzeSamples = new double[MeasuredFrames];
    double[] foregroundSamples = new double[MeasuredFrames];

    for (int frame = 0; frame < MeasuredFrames; frame++)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        RunAnalyze(analyzer, image, width, height, backgroundLab, whiteDirection, clusters, mode, false);

        stopwatch.Stop();
        analyzeSamples[frame] = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();

        _ = analyzer.BuildForegroundField(width, height, backgroundLab, backgroundSrgb);

        stopwatch.Stop();
        foregroundSamples[frame] = stopwatch.Elapsed.TotalMilliseconds;
    }

    Array.Sort(analyzeSamples);
    Array.Sort(foregroundSamples);

    analyzeMilliseconds = analyzeSamples[MeasuredFrames / 2];
    foregroundMilliseconds = foregroundSamples[MeasuredFrames / 2];
}

static void RunAnalyze(
    DirectionalColorKeyAnalyzer analyzer,
    int[] image,
    int width,
    int height,
    Vector3 backgroundLab,
    Vector3 whiteDirection,
    int clusters,
    DirectionalColorKeyScaleMode mode,
    bool resetLambdaSmoothing)
{
    analyzer.Analyze(
        image.AsSpan(0, width * height),
        width,
        height,
        backgroundLab,
        whiteDirection,
        clusters,
        0.02f,
        0.08f,
        mode,
        0.98f,
        0.5f,
        static (keyDirection, floorValue) => MathF.Max(floorValue, 0.5f),
        resetLambdaSmoothing);
}

static int[] CreateTestImage(int width, int height)
{
    int[] pixels = new int[width * height];

    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / width;
            float v = (float)y / height;

            float dx = u - 0.5f;
            float dy = v - 0.5f;
            float radius = MathF.Sqrt((dx * dx) + (dy * dy));

            int r;
            int g;
            int b;

            if (radius < 0.22f)
            {
                r = (int)(220f * (1f - (radius * 2f)));
                g = (int)(120f + (80f * v));
                b = (int)(60f + (150f * u));
            }
            else if (radius < 0.28f)
            {
                float blend = (radius - 0.22f) / 0.06f;
                r = (int)((1f - blend) * 180f);
                g = (int)(((1f - blend) * 140f) + (blend * 177f));
                b = (int)(((1f - blend) * 90f) + (blend * 64f));
            }
            else
            {
                r = (int)(6f * MathF.Sin((u + v) * 24f));
                g = 177 + (int)(4f * MathF.Cos(u * 31f));
                b = 64 + (int)(4f * MathF.Sin(v * 27f));
            }

            r = Math.Clamp(r, 0, 255);
            g = Math.Clamp(g, 0, 255);
            b = Math.Clamp(b, 0, 255);

            pixels[(y * width) + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
    }

    return pixels;
}

static Vector3 ComputeWhiteDirection(Vector3 backgroundLab)
{
    Vector3 whiteLab = new(1f, 0f, 0f);
    Vector3 direction = whiteLab - backgroundLab;
    float length = direction.Length();

    return length > 1e-6f ? direction / length : whiteLab;
}

static Vector3 ToLinear(Vector3 srgb)
{
    return new Vector3(SrgbToLinear(srgb.X), SrgbToLinear(srgb.Y), SrgbToLinear(srgb.Z));
}

static float SrgbToLinear(float c)
    => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

static Vector3 ToOklab(Vector3 c)
{
    float l = (0.4122214708f * c.X) + (0.5363325363f * c.Y) + (0.0514459929f * c.Z);
    float m = (0.2119034982f * c.X) + (0.6806995451f * c.Y) + (0.1073969566f * c.Z);
    float s = (0.0883024619f * c.X) + (0.2817188376f * c.Y) + (0.6299787005f * c.Z);

    float lRoot = MathF.Cbrt(l);
    float mRoot = MathF.Cbrt(m);
    float sRoot = MathF.Cbrt(s);

    return new Vector3(
        (0.2104542553f * lRoot) + (0.7936177850f * mRoot) - (0.0040720468f * sRoot),
        (1.9779984951f * lRoot) - (2.4285922050f * mRoot) + (0.4505937099f * sRoot),
        (0.0259040371f * lRoot) + (0.7827717662f * mRoot) - (0.8086757660f * sRoot));
}
