namespace DirectionalColorKey
{
    [ComputePipelineHost("device", 1)]
    internal sealed partial class DirectionalColorKeyPipelineHost
    {
        private readonly GraphicsDevice device;

        [ComputePipeline]
        private void RecordForegroundField(
            in ComputeContext context,
            [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<int> bgra,
            [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<float> colorLab,
            [ComputeResource(ComputeResourceAccess.Read)] IReadOnlyBuffer<float> srgbToLinear,
            [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> foregroundA,
            [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> foregroundB,
            float backgroundLabX,
            float backgroundLabY,
            float backgroundLabZ,
            float referencePerp,
            float backgroundSrgbR,
            float backgroundSrgbG,
            float backgroundSrgbB,
            int reach,
            float sigmaLineSquared,
            int iterations,
            int width,
            int height)
        {
            _ = device;

            context.For(width, height, new ForegroundSeedShader(
                bgra, colorLab, foregroundA,
                backgroundLabX, backgroundLabY, backgroundLabZ,
                referencePerp, width, height));
            context.Barrier(foregroundA);

            var source = foregroundA;
            var target = foregroundB;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                context.For(width, height, new ForegroundPropagateShader(
                    source, bgra, srgbToLinear, target,
                    backgroundSrgbR, backgroundSrgbG, backgroundSrgbB,
                    reach, sigmaLineSquared, width, height));
                context.Barrier(target);

                (source, target) = (target, source);
            }
        }
    }
}
