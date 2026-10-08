namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Research-stage lateral bank relaxation for already routed hydraulic incision.
/// Redistributes existing cut and fill to neighboring geological cells without creating
/// or destroying net eroded volume. This is a numerical valley-width approximation,
/// not a calibrated hydraulic/thermal erosion law.
/// </summary>
public static class PlanetLateralErosionRelaxation
{
    public const int DefaultPasses = 8;
    private const double LateralTransfer = 0.13;

    public static float[] Apply(
        IReadOnlyList<float> originalElevation, IReadOnlyList<float> hydraulicallyErodedElevation,
        int width, int height, int passes = DefaultPasses)
    {
        ArgumentNullException.ThrowIfNull(originalElevation);
        ArgumentNullException.ThrowIfNull(hydraulicallyErodedElevation);

        if (width < 3 || height < 3 || (long)width * height != originalElevation.Count ||
            originalElevation.Count != hydraulicallyErodedElevation.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Both geological fields must use the same rectangular grid.");
        }

        if (passes is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(passes));
        }

        var count = width * height;
        var delta = new double[count];
        var updates = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (!float.IsFinite(originalElevation[i]) || !float.IsFinite(hydraulicallyErodedElevation[i]))
            {
                throw new ArgumentException("Regional geological elevations must be finite.");
            }

            delta[i] = hydraulicallyErodedElevation[i] - originalElevation[i];
        }

        for (var pass = 0; pass < passes; pass++)
        {
            Array.Clear(updates);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    if (x + 1 < width)
                    {
                        Exchange(delta, updates, i, i + 1);
                    }

                    if (y + 1 < height)
                    {
                        Exchange(delta, updates, i, i + width);
                    }
                }
            }

            for (var i = 0; i < count; i++)
            {
                delta[i] += updates[i];
            }
        }

        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = (float)(originalElevation[i] + delta[i]);
        }

        return result;
    }

    private static void Exchange(double[] elevations, double[] changes, int first, int second)
    {
        // Every transfer is symmetric: one cell's cut/fill is exactly balanced
        // by the adjacent cell's inverse change (up to final float quantization).
        var transfer = (elevations[second] - elevations[first]) * LateralTransfer;
        changes[first] += transfer;
        changes[second] -= transfer;
    }
}
