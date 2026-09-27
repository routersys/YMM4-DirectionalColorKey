namespace DirectionalColorKey;

[ComputeResourceGroup]
internal sealed partial class DirectionalColorKeyFrameResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> PreviousBgra { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> ColorLab { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> DirectionsA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> DirectionsB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> PreviousResult { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> SeedMask { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> DilateScratch { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> AdoptMask { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<int> ComputeMask { get; }
}

[ComputeResourceGroup]
internal sealed partial class DirectionalColorKeyTableResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> SrgbToLinear { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> PremultipliedLinear { get; }
}

[ComputePipelineHost("device", 1)]
internal sealed partial class DirectionalColorKeyPipelineHost
{
    private readonly GraphicsDevice device;

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<DirectionalColorKeyFrameResources> frame = new();

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<DirectionalColorKeyTableResources> tables = new();

    [ComputePipeline]
    private void RecordTables(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(tables))] DirectionalColorKeyTableResources tables)
    {
        _ = device;

        context.For(tables.SrgbToLinear.Length, new SrgbToLinearTableShader(tables.SrgbToLinear));
        context.Barrier(tables.SrgbToLinear);

        context.For(tables.PremultipliedLinear.Length, new PremultipliedLinearTableShader(tables.PremultipliedLinear));
        context.Barrier(tables.PremultipliedLinear);
    }

    [ComputePipeline]
    private void RecordDisplacementField(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> bgra,
        float backgroundLabX,
        float backgroundLabY,
        float backgroundLabZ,
        float noiseThreshold,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new DisplacementFieldShader(
            bgra, frame.ColorLab, frame.DirectionsA,
            backgroundLabX, backgroundLabY, backgroundLabZ,
            noiseThreshold, width, height));
        context.Barrier(frame.ColorLab);
        context.Barrier(frame.DirectionsA);
    }

    [ComputePipeline]
    private void RecordChangeCount(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> bgra,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> count,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new ChangeSeedShader(bgra, frame.PreviousBgra, frame.SeedMask, width, height));
        context.Barrier(frame.SeedMask);

        context.For(ThreadGroupAlignment.AlignX<MaskCountShader>(width), ThreadGroupAlignment.AlignY<MaskCountShader>(height), new MaskCountShader(frame.SeedMask, count, width, height));
        context.Barrier(count);
    }

    [ComputePipeline]
    private void RecordDirectionSmooth(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        float sigmaColorSquared,
        int iterations,
        int width,
        int height)
    {
        _ = device;

        var source = frame.DirectionsA;
        var target = frame.DirectionsB;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            context.For(ThreadGroupAlignment.AlignX<DirectionSmoothShader>(width), ThreadGroupAlignment.AlignY<DirectionSmoothShader>(height), new DirectionSmoothShader(source, frame.ColorLab, target, sigmaColorSquared, width, height));
            context.Barrier(target);

            (source, target) = (target, source);
        }
    }

    [ComputePipeline]
    private void RecordRegionSmooth(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        float sigmaColorSquared,
        int adoptReach,
        int guardReach,
        int iterations,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new DilateHorizontalShader(frame.SeedMask, frame.DilateScratch, adoptReach, width, height));
        context.Barrier(frame.DilateScratch);
        context.For(width, height, new DilateVerticalShader(frame.DilateScratch, frame.AdoptMask, adoptReach, width, height));
        context.Barrier(frame.AdoptMask);

        context.For(width, height, new DilateHorizontalShader(frame.AdoptMask, frame.DilateScratch, guardReach, width, height));
        context.Barrier(frame.DilateScratch);
        context.For(width, height, new DilateVerticalShader(frame.DilateScratch, frame.ComputeMask, guardReach, width, height));
        context.Barrier(frame.ComputeMask);

        var source = frame.DirectionsA;
        var target = frame.DirectionsB;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            context.For(ThreadGroupAlignment.AlignX<RegionDirectionSmoothShader>(width), ThreadGroupAlignment.AlignY<RegionDirectionSmoothShader>(height), new RegionDirectionSmoothShader(
                source, frame.ColorLab, target, frame.ComputeMask, sigmaColorSquared, width, height));
            context.Barrier(target);

            (source, target) = (target, source);
        }

        context.For(width, height, new AdoptRegionShader(source, frame.PreviousResult, frame.AdoptMask, width, height));
        context.Barrier(source);
    }

    [ComputePipeline]
    private void RecordPreviousSnapshot(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> bgra,
        int smoothIterations,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new CopyDirectionsShader(SmoothedDirections(frame, smoothIterations), frame.PreviousResult, width, height));
        context.Barrier(frame.PreviousResult);

        context.For(width, height, new CopyPackedShader(bgra, frame.PreviousBgra, width, height));
        context.Barrier(frame.PreviousBgra);
    }

    [ComputePipeline]
    private void RecordClusterAssign(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<float> centers,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> accumulators,
        int smoothIterations,
        int clusterCount,
        float fixedPointScale,
        int width,
        int height)
    {
        _ = device;

        context.For(ThreadGroupAlignment.AlignX<ClusterAssignAccumulateShader>(width), ThreadGroupAlignment.AlignY<ClusterAssignAccumulateShader>(height), new ClusterAssignAccumulateShader(
            SmoothedDirections(frame, smoothIterations), centers, accumulators, clusterCount, fixedPointScale, width, height));
        context.Barrier(accumulators);
    }

    [ComputePipeline]
    private void RecordProjectionHistogram(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        [ComputeResource(ComputeResourceAccess.Read)] ReadOnlyBuffer<float> centers,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> histogram,
        int smoothIterations,
        float backgroundLabX,
        float backgroundLabY,
        float backgroundLabZ,
        int clusterCount,
        int binsPerCluster,
        float projectionScale,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new ProjectionHistogramShader(
            frame.ColorLab, SmoothedDirections(frame, smoothIterations), centers, histogram,
            backgroundLabX, backgroundLabY, backgroundLabZ,
            clusterCount, binsPerCluster, projectionScale, width, height));
        context.Barrier(histogram);
    }

    [ComputePipeline]
    private void RecordForegroundField(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(frame))] DirectionalColorKeyFrameResources frame,
        [ComputeOwnedResource(nameof(tables))] DirectionalColorKeyTableResources tables,
        [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> bgra,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> foregroundA,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> foregroundB,
        float backgroundLabX,
        float backgroundLabY,
        float backgroundLabZ,
        float referencePerp,
        float backgroundSrgbR,
        float backgroundSrgbG,
        float backgroundSrgbB,
        float sigmaLineSquared,
        int iterations,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new ForegroundSeedShader(
            bgra, frame.ColorLab, foregroundA,
            backgroundLabX, backgroundLabY, backgroundLabZ,
            referencePerp, width, height));
        context.Barrier(foregroundA);

        var source = foregroundA;
        var target = foregroundB;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            context.For(ThreadGroupAlignment.AlignX<ForegroundPropagateShader>(width), ThreadGroupAlignment.AlignY<ForegroundPropagateShader>(height), new ForegroundPropagateShader(
                source, bgra, tables.SrgbToLinear.AsReadOnly(), tables.PremultipliedLinear.AsReadOnly(), target,
                backgroundSrgbR, backgroundSrgbG, backgroundSrgbB,
                sigmaLineSquared, width, height));
            context.Barrier(target);

            (source, target) = (target, source);
        }
    }

    [ComputePipeline]
    [ComputeInterop]
    private void CaptureSource(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> bgra,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new SharedTextureToBufferShader(source, bgra, width, height));
    }

    [ComputePipeline]
    [ComputeInterop]
    private void WriteForegroundField(
        in ComputeContext context,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> destination,
        [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> foreground,
        int width,
        int height)
    {
        _ = device;

        context.For(width, height, new BufferToSharedTextureShader(foreground, destination, width, height));
    }

    private static ReadWriteBuffer<float> SmoothedDirections(DirectionalColorKeyFrameResources frame, int iterations)
        => (iterations & 1) == 0 ? frame.DirectionsA : frame.DirectionsB;
}
