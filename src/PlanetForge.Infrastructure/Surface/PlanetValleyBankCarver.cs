namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Experimental stream-adjacent bank excavation driven by the watershed's real
/// flow accumulation and existing hydraulic incision. This modifies the physical
/// elevation grid, never a shader or material mask. Material cut from the banks
/// is explicitly exported by this isolated research region pending downstream
/// depositional coupling.
/// </summary>
public static class PlanetValleyBankCarver
{
    public sealed record Result(float[] ElevationMeters, double AdditionalExportedSedimentCubicMeters);

    public static Result Apply(
        IReadOnlyList<float> original, IReadOnlyList<float> eroded, IReadOnlyList<float> accumulatedRunoff,
        int width, int height, double spacingMeters)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(eroded);
        ArgumentNullException.ThrowIfNull(accumulatedRunoff);
        if (width is < 9 or > 512 || height is < 9 or > 512 ||
            (long)width * height != original.Count || original.Count != eroded.Count ||
            original.Count != accumulatedRunoff.Count)
        {
            throw new ArgumentException("Bank carving requires three matching geological raster fields.");
        }

        if (!double.IsFinite(spacingMeters) || spacingMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spacingMeters));
        }

        var length = original.Count;
        var deepestCut = new double[length];
        for (var i = 0; i < length; i++)
        {
            if (!float.IsFinite(original[i]) || !float.IsFinite(eroded[i]) ||
                !float.IsFinite(accumulatedRunoff[i]) || accumulatedRunoff[i] < 0)
            {
                throw new ArgumentException("Geological raster fields must be finite; runoff must be nonnegative.");
            }

            deepestCut[i] = Math.Max(0, original[i] - eroded[i]);
        }

        // A tributary must have real drainage accumulation AND pre-existing
        // hydraulic incision. We never invent a stream through un-eroded rock.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var center = y * width + x;
                var runoff = accumulatedRunoff[center];
                var existingCut = original[center] - eroded[center];
                if (runoff < 14 || existingCut < 4.0)
                {
                    continue;
                }

                var halfWidth = Math.Clamp(230.0 + 85.0 * Math.Sqrt(runoff), 350.0, 1_400.0);
                var radius = Math.Min(12, (int)Math.Ceiling(halfWidth * 1.8 / spacingMeters));
                var channelDepth = Math.Min(260.0, existingCut);
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= width)
                        {
                            continue;
                        }

                        var distance = Math.Sqrt(dx * dx + dy * dy) * spacingMeters;
                        if (distance > halfWidth * 1.8)
                        {
                            continue;
                        }

                        var fraction = distance / halfWidth;
                        var shoulderCut = channelDepth * Math.Exp(-1.8 * fraction * fraction);
                        var neighbor = ny * width + nx;
                        // A stream can excavate neighboring bank material but cannot
                        // fill a pre-existing channel or create a higher streambed.
                        deepestCut[neighbor] = Math.Max(deepestCut[neighbor], shoulderCut);
                    }
                }
            }
        }

        var output = new float[length];
        var extraRemoved = 0.0;
        for (var i = 0; i < length; i++)
        {
            // Preserve any pre-existing deposition when the widening effect is
            // absent, and never raise the existing hydraulic riverbed.
            output[i] = (float)Math.Min(eroded[i], original[i] - deepestCut[i]);
            extraRemoved += Math.Max(0.0, eroded[i] - (double)output[i]);
        }

        return new Result(output, extraRemoved * spacingMeters * spacingMeters);
    }
}
