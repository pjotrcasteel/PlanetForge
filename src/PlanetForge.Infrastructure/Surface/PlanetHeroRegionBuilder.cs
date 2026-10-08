using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Small, deliberately opt-in geological showcase generated from actual canonical elevations
/// and persisted regional hydrology. Height, incision and flow are physical fields rather
/// than a renderer texture. The expensive calculation runs once per requested scene.
/// </summary>
public sealed record PlanetHeroRegion(
    int Seed,
    int Width,
    double CellSpacingMeters,
    float[] OriginalElevationMeters,
    float[] EvolvedElevationMeters,
    float[] CumulativeCutMeters,
    float[] AccumulatedRunoffCells,
    int ErosionIterations,
    int LateralRelaxationPasses,
    double ErodedVolumeCubicMeters,
    double DepositedVolumeCubicMeters,
    double ExportedVolumeCubicMeters);

public sealed class PlanetHeroRegionBuilder(IPlanetElevationSource elevationSource)
{
    public const int DefaultGridWidth = 129;
    public const double DefaultRegionSpanMeters = 128_000.0;
    public const int DefaultErosionIterations = 6;
    public const double ReferencePlanetRadiusMeters = 6_371_000.0;

    /// <summary>
    /// Chooses a fixed geographic focus for a finer nested erosion grid from real
    /// interior channel incision. The calculation is independent of camera and LOD.
    /// </summary>
    public static PlanetVector FindIncisedChannelFocus(PlanetHeroRegion region, PlanetVector regionCenter)
    {
        ArgumentNullException.ThrowIfNull(region);
        var center = PlanetVector.Normalize(regionCenter);
        var width = region.Width;
        var margin = Math.Max(1, width / 5);
        var selected = (width / 2) * width + (width / 2);
        var best = double.NegativeInfinity;

        for (var y = margin; y < width - margin; y++)
        {
            for (var x = margin; x < width - margin; x++)
            {
                var index = y * width + x;
                var cut = region.CumulativeCutMeters[index];
                if (cut <= best)
                {
                    continue;
                }

                best = cut;
                selected = index;
            }
        }

        var frame = PlanetLocalFrame.Create(center, ReferencePlanetRadiusMeters, 0.0);
        var offsetX = ((selected % width) - (width - 1) * 0.5) * region.CellSpacingMeters;
        var offsetY = ((selected / width) - (width - 1) * 0.5) * region.CellSpacingMeters;
        return PlanetVector.Normalize((center * ReferencePlanetRadiusMeters) +
            (frame.East * offsetX) + (frame.North * offsetY));
    }

    public PlanetHeroRegion Build(
        PlanetVector centerDirection, int seed, int gridWidth = DefaultGridWidth,
        double regionSpanMeters = DefaultRegionSpanMeters, int erosionIterations = DefaultErosionIterations,
        CancellationToken cancellationToken = default)
    {
        if (gridWidth is < 9 or > 257 || (gridWidth - 1 & gridWidth - 2) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridWidth), "The terrain grid must contain 2^n + 1 vertices, from 9 to 257.");
        }

        if (!double.IsFinite(regionSpanMeters) || regionSpanMeters <= 0.0 ||
            regionSpanMeters >= ReferencePlanetRadiusMeters * 0.1)
        {
            throw new ArgumentOutOfRangeException(nameof(regionSpanMeters));
        }

        if (erosionIterations is < 1 or > PlanetRegionalGeologyEvolution.MaximumIterationsPerCall)
        {
            throw new ArgumentOutOfRangeException(nameof(erosionIterations));
        }

        var center = PlanetVector.Normalize(centerDirection);
        var frame = PlanetLocalFrame.Create(center, ReferencePlanetRadiusMeters, 0);
        var spacing = regionSpanMeters / (gridWidth - 1);
        var original = new float[gridWidth * gridWidth];
        var half = (gridWidth - 1) * 0.5;

        for (var y = 0; y < gridWidth; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var north = (y - half) * spacing;
            for (var x = 0; x < gridWidth; x++)
            {
                var east = (x - half) * spacing;
                var direction = PlanetVector.Normalize((center * ReferencePlanetRadiusMeters) +
                    (frame.East * east) + (frame.North * north));
                original[y * gridWidth + x] = (float)elevationSource.SampleElevationMeters(direction, seed);
            }
        }

        var key = $"hero/{seed}/{center.X:R}/{center.Y:R}/{center.Z:R}/{gridWidth}/{regionSpanMeters:R}";
        var initial = PlanetRegionalGeologyEvolution.Initialize(seed, key, gridWidth, gridWidth, spacing, original);
        var evolved = PlanetRegionalGeologyEvolution.Advance(initial, erosionIterations, cancellationToken: cancellationToken);
        // Hydraulic flow is discrete at the research grid resolution. Redistributing
        // its already-computed cut/fill field conservatively approximates lateral
        // bank retreat without applying fake shader displacement to the preview.
        var relaxedElevation = PlanetLateralErosionRelaxation.Apply(original, evolved.ElevationMeters, gridWidth, gridWidth);
        var finalWatershed = PlanetRegionalWatershed.Build(gridWidth, gridWidth, spacing, relaxedElevation);
        var cut = new float[original.Length];

        for (var index = 0; index < cut.Length; index++)
        {
            cut[index] = original[index] - relaxedElevation[index];
        }

        return new PlanetHeroRegion(seed, gridWidth, spacing, original, relaxedElevation, cut,
            finalWatershed.AccumulatedRunoffCells, erosionIterations, PlanetLateralErosionRelaxation.DefaultPasses,
            evolved.CumulativeErodedVolumeCubicMeters,
            evolved.CumulativeDepositedVolumeCubicMeters, evolved.CumulativeExportedVolumeCubicMeters);
    }
}
