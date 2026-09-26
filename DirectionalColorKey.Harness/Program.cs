using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DirectionalColorKey;
using DirectionalColorKey.Harness;
using SharpGen.Runtime;

const int CanvasWidth = 1280;
const int CanvasHeight = 720;
const int ImageWidth = 640;
const int ImageHeight = 480;
const int FullHdWidth = 1920;
const int FullHdHeight = 1080;
const int UltraHdWidth = 3840;
const int UltraHdHeight = 2160;

_ = PackUriHelper.UriSchemePack;

HarnessArguments arguments;
try
{
    arguments = HarnessArguments.Parse(args);
}
catch (HarnessException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine(HarnessArguments.Usage);
    return 2;
}

try
{
    if (arguments.Mode == HarnessMode.Compare)
        return Compare(arguments.Before!, arguments.After!);

    if (arguments.Mode == HarnessMode.Analysis)
    {
        HarnessImage[] analysisImages = arguments.Input is { } analysisInput
            ? [HarnessImage.Load(analysisInput)]
            : [HarnessImage.Synthetic(CanvasWidth, CanvasHeight), HarnessImage.Synthetic(FullHdWidth, FullHdHeight), HarnessImage.Synthetic(UltraHdWidth, UltraHdHeight)];
        return Analysis(analysisImages);
    }

    var outputDirectory = arguments.OutputDirectory ?? Path.Combine(AppContext.BaseDirectory, "harness-output");
    if (arguments.Mode == HarnessMode.Benchmark)
    {
        if (arguments.Input is { } benchmarkInput)
        {
            Benchmark(HarnessImage.Load(benchmarkInput));
        }
        else
        {
            Benchmark(HarnessImage.Synthetic(CanvasWidth, CanvasHeight));
            Benchmark(HarnessImage.Synthetic(FullHdWidth, FullHdHeight));
        }

        return 0;
    }

    var image = arguments.Input is { } input ? HarnessImage.Load(input) : HarnessImage.Synthetic(ImageWidth, ImageHeight);
    using var renderer = new HarnessRenderer(CanvasWidth, CanvasHeight, image);
    return arguments.Mode switch
    {
        HarnessMode.Golden => WriteGolden(renderer, image),
        HarnessMode.Verify => Verify(renderer, image),
        HarnessMode.Transition => Transition(renderer, outputDirectory),
        _ => Render(renderer, outputDirectory),
    };
}
catch (Exception exception) when (exception is HarnessException or SharpGenException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static int Render(HarnessRenderer renderer, string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    var failures = 0;
    var cases = Evaluate(renderer, (golden, files, frames) =>
    {
        Console.WriteLine($"{golden.Name}: {golden.Hash}");
        for (var index = 0; index < files.Length; index++)
        {
            HarnessImage.Save(Path.Combine(outputDirectory, files[index]), frames[index], renderer.CanvasWidth, renderer.CanvasHeight);
            var opaque = CountOpaque(frames[index]);
            Console.WriteLine($"  {files[index]} opaque={opaque}");
            if (opaque == 0)
            {
                Console.Error.WriteLine($"{files[index]}: 出力が完全に透明です。");
                failures++;
            }
        }
    });
    Console.WriteLine($"-> {outputDirectory}");
    return failures + CountDuplicates(cases) == 0 ? 0 : 1;
}

static int WriteGolden(HarnessRenderer renderer, HarnessImage image)
{
    var cases = Evaluate(renderer, null);
    if (CountDuplicates(cases) != 0)
        return 1;
    if (cases.Count == 0)
        Console.Error.WriteLine("ケースがありません。HarnessCases に書き足してください。");

    var golden = new Golden(renderer.Adapter, renderer.Driver, image.Identity, $"{renderer.CanvasWidth}x{renderer.CanvasHeight}", cases);
    var path = Golden.PathFor(image);
    golden.Save(path);
    Console.WriteLine($"adapter: {golden.Adapter} (driver {golden.Driver})");
    Console.WriteLine($"input: {golden.Input}");
    foreach (var (name, _, hash) in cases)
        Console.WriteLine($"{name}: {hash}");
    Console.WriteLine($"-> {path}");
    return 0;
}

static int Verify(HarnessRenderer renderer, HarnessImage image)
{
    var golden = Golden.Load(Golden.PathFor(image));
    var canvas = $"{renderer.CanvasWidth}x{renderer.CanvasHeight}";
    if (golden.Input != image.Identity || golden.Canvas != canvas)
    {
        Console.Error.WriteLine($"基準値は入力 {golden.Input} とキャンバス {golden.Canvas} で書かれています。今回は入力 {image.Identity} とキャンバス {canvas} です。");
        return 1;
    }

    if (golden.Adapter != renderer.Adapter || golden.Driver != renderer.Driver)
        Console.Error.WriteLine($"基準値は {golden.Adapter} (driver {golden.Driver}) で書かれています。今回は {renderer.Adapter} (driver {renderer.Driver}) です。画素の差はこの違いから来ることがあります。");

    var failures = 0;
    var current = Evaluate(renderer, null).ToDictionary(item => item.Name, StringComparer.Ordinal);
    if (golden.Cases.Count == 0 && current.Count == 0)
        Console.Error.WriteLine("ケースがありません。HarnessCases に書き足してください。");
    foreach (var (name, frames, hash) in golden.Cases)
    {
        if (!current.Remove(name, out var actual))
        {
            Console.Error.WriteLine($"{name}: ケースがありません。基準値を書き直してください。");
            failures++;
        }
        else if (!actual.Frames.SequenceEqual(frames))
        {
            Console.Error.WriteLine($"{name}: フレームが違います。基準値 [{string.Join(", ", frames)}]、今回 [{string.Join(", ", actual.Frames)}]。基準値を書き直してください。");
            failures++;
        }
        else if (actual.Hash != hash)
        {
            Console.Error.WriteLine($"{name}: 一致しません。基準値 {hash}、今回 {actual.Hash}");
            failures++;
        }
        else
        {
            Console.WriteLine($"{name}: 一致");
        }
    }

    foreach (var name in current.Keys)
    {
        Console.Error.WriteLine($"{name}: 基準値にありません。基準値を書き直してください。");
        failures++;
    }

    return failures == 0 ? 0 : 1;
}

static int Transition(HarnessRenderer renderer, string outputDirectory)
{
    var failures = 0;
    foreach (var (name, create, change, frame) in HarnessCases.Transitions())
    {
        var (direct, transitioned) = renderer.RenderTransition(create, change, frame);
        var difference = ImageComparison.Of(direct, transitioned, renderer.CanvasWidth, renderer.CanvasHeight);
        if (difference.IsEmpty)
        {
            Console.WriteLine($"{name}: 一致");
            continue;
        }

        failures++;
        Directory.CreateDirectory(outputDirectory);
        HarnessImage.Save(Path.Combine(outputDirectory, name + "-direct.png"), direct, renderer.CanvasWidth, renderer.CanvasHeight);
        HarnessImage.Save(Path.Combine(outputDirectory, name + "-transitioned.png"), transitioned, renderer.CanvasWidth, renderer.CanvasHeight);
        Console.Error.WriteLine($"{name}: {difference}。設定を変えた後の描画が作り直した場合と違います。-> {outputDirectory}");
    }

    if (!HarnessCases.Transitions().Any() && !HarnessCases.All().Any(item => item.Frames.Count > 1))
        Console.Error.WriteLine("ケースがありません。HarnessCases に書き足してください。");

    foreach (var (name, effect, frames) in HarnessCases.All())
    {
        if (frames.Count < 2)
            continue;

        var sequential = renderer.Render(effect, frames);
        for (var index = 0; index < frames.Count; index++)
        {
            var fresh = renderer.Render(effect, [frames[index]])[0];
            var difference = ImageComparison.Of(fresh, sequential[index], renderer.CanvasWidth, renderer.CanvasHeight);
            var label = $"{name}-f{frames[index]:D3}";
            if (difference.IsEmpty)
            {
                Console.WriteLine($"{label}: 一致");
                continue;
            }

            failures++;
            Directory.CreateDirectory(outputDirectory);
            HarnessImage.Save(Path.Combine(outputDirectory, label + "-direct.png"), fresh, renderer.CanvasWidth, renderer.CanvasHeight);
            HarnessImage.Save(Path.Combine(outputDirectory, label + "-transitioned.png"), sequential[index], renderer.CanvasWidth, renderer.CanvasHeight);
            Console.Error.WriteLine($"{label}: {difference}。前のフレームから進めた描画が作り直した場合と違います。-> {outputDirectory}");
        }
    }

    return failures == 0 ? 0 : 1;
}

static int Analysis(IReadOnlyList<HarnessImage> images)
{
    const int Rounds = 12;
    const int Seed = 17;
    const float NoiseThreshold = 0.02f;
    const float SigmaColor = 0.1f;
    const float OpaquePercentile = 0.99f;
    const float ForegroundLambda = 0.5f;

    using var analyzer = DirectionalColorKeyAnalyzer.TryCreate();
    if (analyzer is null)
        throw new HarnessException("Direct3D 12を利用できません。");

    var backgroundSrgb = new Vector3(0f, 1f, 0f);
    var backgroundLab = ToOklab(ToLinear(backgroundSrgb));
    var whiteDirection = ComputeWhiteDirection(backgroundLab);
    var physicalLambda = static (Vector3 _, float floor) => MathF.Max(floor, ForegroundLambda);
    var variants = new (int Clusters, DirectionalColorKeyScaleMode Mode)[]
    {
        (1, DirectionalColorKeyScaleMode.Physical),
        (4, DirectionalColorKeyScaleMode.Physical),
        (1, DirectionalColorKeyScaleMode.Foreground),
        (4, DirectionalColorKeyScaleMode.Foreground),
    };
    var stopwatch = new Stopwatch();

    Console.WriteLine($"analysis over {Rounds} interleaved rounds (ms)");
    foreach (var image in images)
    {
        var pixels = MemoryMarshal.Cast<byte, int>(image.Pixels).ToArray();
        var analyzeSamples = new List<double>[variants.Length];
        var foregroundSamples = new List<double>[variants.Length];
        for (var index = 0; index < variants.Length; index++)
        {
            analyzeSamples[index] = new List<double>(Rounds);
            foregroundSamples[index] = new List<double>(Rounds);
        }

        foreach (var (clusters, mode) in variants)
        {
            analyzer.Analyze(pixels, image.Width, image.Height, backgroundLab, whiteDirection, clusters, NoiseThreshold, SigmaColor, mode, OpaquePercentile, ForegroundLambda, physicalLambda, true);
            _ = analyzer.BuildForegroundField(image.Width, image.Height, backgroundLab, backgroundSrgb);
        }

        var order = Enumerable.Range(0, variants.Length).ToArray();
        var random = new Random(Seed);
        for (var round = 0; round < Rounds; round++)
        {
            for (var index = order.Length - 1; index > 0; index--)
            {
                var swap = random.Next(index + 1);
                (order[index], order[swap]) = (order[swap], order[index]);
            }

            foreach (var index in order)
            {
                var (clusters, mode) = variants[index];
                analyzer.Analyze(pixels, image.Width, image.Height, backgroundLab, whiteDirection, clusters, NoiseThreshold, SigmaColor, mode, OpaquePercentile, ForegroundLambda, physicalLambda, true);
                _ = analyzer.BuildForegroundField(image.Width, image.Height, backgroundLab, backgroundSrgb);

                stopwatch.Restart();
                analyzer.Analyze(pixels, image.Width, image.Height, backgroundLab, whiteDirection, clusters, NoiseThreshold, SigmaColor, mode, OpaquePercentile, ForegroundLambda, physicalLambda, false);
                stopwatch.Stop();
                analyzeSamples[index].Add(stopwatch.Elapsed.TotalMilliseconds);

                stopwatch.Restart();
                _ = analyzer.BuildForegroundField(image.Width, image.Height, backgroundLab, backgroundSrgb);
                stopwatch.Stop();
                foregroundSamples[index].Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        for (var index = 0; index < variants.Length; index++)
        {
            var analyze = analyzeSamples[index].OrderBy(static value => value).ToArray();
            var foreground = foregroundSamples[index].OrderBy(static value => value).ToArray();
            Console.WriteLine(
                $"  {image.Width}x{image.Height} clusters={variants[index].Clusters} {variants[index].Mode,-10} " +
                $"analyze min={analyze[0],8:F2} median={analyze[analyze.Length / 2],8:F2}  " +
                $"foreground min={foreground[0],8:F2} median={foreground[foreground.Length / 2],8:F2}");
        }
    }

    return 0;
}

static Vector3 ComputeWhiteDirection(Vector3 backgroundLab)
{
    var whiteLab = new Vector3(1f, 0f, 0f);
    var direction = whiteLab - backgroundLab;
    var length = direction.Length();
    return length > 1e-6f ? direction / length : whiteLab;
}

static Vector3 ToLinear(Vector3 srgb) => new(SrgbToLinear(srgb.X), SrgbToLinear(srgb.Y), SrgbToLinear(srgb.Z));

static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

static Vector3 ToOklab(Vector3 c)
{
    var l = MathF.Cbrt(0.4122214708f * c.X + 0.5363325363f * c.Y + 0.0514459929f * c.Z);
    var m = MathF.Cbrt(0.2119034982f * c.X + 0.6806995451f * c.Y + 0.1073969566f * c.Z);
    var s = MathF.Cbrt(0.0883024619f * c.X + 0.2817188376f * c.Y + 0.6299787005f * c.Z);
    return new Vector3(
        0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
        1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
        0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
}

static int Compare(string beforeDirectory, string afterDirectory)
{
    if (!Directory.Exists(beforeDirectory) || !Directory.Exists(afterDirectory))
        throw new HarnessException("比べる出力先がありません。");

    var failures = 0;
    var names = Directory.EnumerateFiles(beforeDirectory, "*.png")
        .Concat(Directory.EnumerateFiles(afterDirectory, "*.png"))
        .Select(path => Path.GetFileName(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (names.Length == 0)
        throw new HarnessException("比べる PNG がありません。");

    foreach (var name in names)
    {
        var before = Path.Combine(beforeDirectory, name);
        var after = Path.Combine(afterDirectory, name);
        if (!File.Exists(before) || !File.Exists(after))
        {
            Console.Error.WriteLine($"{name}: {(File.Exists(before) ? "後" : "前")}にありません。");
            failures++;
            continue;
        }

        var (beforeWidth, beforeHeight, beforePixels) = HarnessImage.LoadPremultiplied(before);
        var (afterWidth, afterHeight, afterPixels) = HarnessImage.LoadPremultiplied(after);
        if (beforeWidth != afterWidth || beforeHeight != afterHeight)
        {
            Console.Error.WriteLine($"{name}: 寸法が違います。前 {beforeWidth}x{beforeHeight}、後 {afterWidth}x{afterHeight}");
            failures++;
            continue;
        }

        var difference = ImageComparison.Of(beforePixels, afterPixels, beforeWidth, beforeHeight);
        Console.WriteLine($"{name}: {difference}");
        if (!difference.IsEmpty)
            failures++;
    }

    return failures == 0 ? 0 : 1;
}

static void Benchmark(HarnessImage image)
{
    const int Frames = 60;

    using var renderer = new HarnessRenderer(image.Width, image.Height, image);
    Console.WriteLine($"adapter: {renderer.Adapter} (driver {renderer.Driver})");
    foreach (var (name, effect) in HarnessCases.Benchmarks())
    {
        Report(image, name, "still", renderer.Measure(effect, Frames, moving: false));
        Report(image, name, "moving", renderer.Measure(effect, Frames, moving: true));
    }
}

static void Report(HarnessImage image, string name, string motion, HarnessRenderer.Measurement measurement)
    => Console.WriteLine(
        $"{image.Width}x{image.Height} {name,-15} {motion,-6} " +
        $"gpu={(measurement.Gpu is { } gpu ? gpu.TotalMilliseconds.ToString("F3") : "n/a"),7} " +
        $"cpu={measurement.Cpu.TotalMilliseconds,7:F3} " +
        $"update={measurement.Update.TotalMilliseconds,6:F3} " +
        $"alloc={measurement.Allocated,6} gen0={measurement.Gen0}");

static List<GoldenCase> Evaluate(HarnessRenderer renderer, Action<GoldenCase, string[], byte[][]>? report)
{
    var cases = new List<GoldenCase>();
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var (name, effect, frames) in HarnessCases.All())
    {
        if (!names.Add(name))
            throw new HarnessException($"ケース名が重複しています。{name}");

        var rendered = renderer.Render(effect, frames);
        var files = new string[frames.Count];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var index = 0; index < frames.Count; index++)
        {
            hash.AppendData(rendered[index]);
            files[index] = frames.Count == 1 ? name + ".png" : $"{name}-f{frames[index]:D3}.png";
        }

        var golden = new GoldenCase(name, frames, Convert.ToHexString(hash.GetHashAndReset()));
        cases.Add(golden);
        report?.Invoke(golden, files, rendered);
    }

    return cases;
}

static int CountDuplicates(IReadOnlyList<GoldenCase> cases)
{
    var duplicates = 0;
    var seen = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (name, _, hash) in cases)
    {
        if (seen.TryGetValue(hash, out var first))
        {
            Console.Error.WriteLine($"{name}: {first} と出力が一致します。設定がシェーダーへ届いていません。");
            duplicates++;
        }
        else
        {
            seen.Add(hash, name);
        }
    }

    return duplicates;
}

static int CountOpaque(byte[] pixels)
{
    var count = 0;
    for (var offset = HarnessImage.BytesPerPixel - 1; offset < pixels.Length; offset += HarnessImage.BytesPerPixel)
    {
        if (pixels[offset] != 0)
            count++;
    }

    return count;
}
