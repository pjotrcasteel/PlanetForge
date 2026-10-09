namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// A bounded, conservative hillslope-failure step on real elevation data.
/// Where adjacent bedrock exceeds its lithology-controlled critical slope,
/// material is transferred to the lower cell as local talus. No shader
/// displacement, world-space randomness, or unaccounted export is introduced.
/// </summary>
public static class PlanetHillslopeMassWasting
{
    public const int DefaultPasses = 6;

    public sealed record Result(
        float[] ElevationMeters,
        double AdditionalErodedVolumeCubicMeters,
        double AdditionalDepositedVolumeCubicMeters,
        int InitiallyUnstableEdges = 0);

    public static Result Apply(
        IReadOnlyList<float> elevations, IReadOnlyList<float> erodibility,
        int width, int height, double cellSpacingMeters,
        int passes = DefaultPasses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elevations);
        ArgumentNullException.ThrowIfNull(erodibility);
        if (width is < 9 or > 512 || height is < 9 or > 512 ||
            (long)width * height != elevations.Count || elevations.Count != erodibility.Count)
        {
            throw new ArgumentException("Hillslope evolution requires matching physical elevation and lithology grids.");
        }

        if (!double.IsFinite(cellSpacingMeters) || cellSpacingMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSpacingMeters));
        }

        if (passes is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(passes));
        }

        if (elevations.Any(value => !float.IsFinite(value)) ||
            erodibility.Any(value => !float.IsFinite(value) || value < 0.0f || value > 3.0f))
        {
            throw new ArgumentException("Hillslope elevations and erodibility must be finite and physically bounded.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var original = elevations.ToArray();
        var working = (float[])original.Clone();
        var change = new double[working.Length];
        var initiallyUnstableEdges = 0;
        var trackInitialPass = false;

        for (var pass = 0; pass < passes; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            trackInitialPass = pass == 0;
            Array.Clear(change);
            for (var y = 1; y < height - 1; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 1; x < width - 1; x++)
                {
                    var index = y * width + x;
                    // Visit each undirected edge once. Cardinal and diagonal
                    // exchange preserve area-integrated mass by construction.
                    Transfer(index, index + 1, cellSpacingMeters, 0.12);
                    Transfer(index, index + width, cellSpacingMeters, 0.12);
                    Transfer(index, index + width + 1, cellSpacingMeters * Math.Sqrt(2.0), 0.05);
                    Transfer(index, index + width - 1, cellSpacingMeters * Math.Sqrt(2.0), 0.05);
                }
            }

            for (var index = 0; index < working.Length; index++)
            {
                working[index] = (float)(working[index] + change[index]);
            }
        }

        // Report actual float-quantized volume moved in each direction. Rockfall
        // removes material uphill AND deposits the same material downslope.
        // Both amounts are added to gross erosion/deposition; neither is export.
        var cut = 0.0;
        var fill = 0.0;
        var cellArea = cellSpacingMeters * cellSpacingMeters;
        for (var index = 0; index < working.Length; index++)
        {
            var difference = original[index] - (double)working[index];
            cut += Math.Max(0.0, difference);
            fill += Math.Max(0.0, -difference);
        }

        return new Result(working, cut * cellArea, fill * cellArea, initiallyUnstableEdges);

        void Transfer(int first, int second, double distanceMeters, double relaxationRate)
        {
            var difference = working[first] - (double)working[second];
            var uphill = difference >= 0.0 ? first : second;
            var downhill = difference >= 0.0 ? second : first;
            var meanErodibility = (erodibility[first] + erodibility[second]) * 0.5;
            // Resistant rock supports steeper cliffs; weaker rock falls at a
            // lower angle. These thresholds are numerical stability limits,
            // not a claim of experimentally calibrated material parameters.
            // These are tan(angle of repose) style slope bounds:
            // weaker lithology ~23-31°, resistant rock up to ~40°.
            // The earlier >45° threshold overlooked the visibly unstable
            // 8 km Hero faces entirely.
            var criticalSlope = Math.Clamp(0.90 - 0.25 * meanErodibility, 0.42, 0.86);
            var excessHeight = Math.Abs(difference) - criticalSlope * distanceMeters;
            if (excessHeight <= 0.0)
            {
                return;
            }

            var movedHeight = excessHeight * relaxationRate;
            // The first pass measures meaningful *physical* slope failures.
            // Subsequent passes may still transport talus, but must not
            // redefine whether the initial geological surface was unstable.
            if (trackInitialPass && movedHeight >= 0.01)
            {
                initiallyUnstableEdges++;
            }

            change[uphill] -= movedHeight;
            change[downhill] += movedHeight;
        }
    }
}
