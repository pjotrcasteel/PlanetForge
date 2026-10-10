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
    double ExportedVolumeCubicMeters,
    int FocusCellIndex = -1,
    double HillslopeTransportedVolumeCubicMeters = 0,
    int HillslopeInitiallyUnstableEdges = 0);

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
        var selected = region.FocusCellIndex >= 0 && region.FocusCellIndex < width * width
            ? region.FocusCellIndex
            : FindIncisedChannelCell(region);
        var frame = PlanetLocalFrame.Create(center, ReferencePlanetRadiusMeters, 0.0);
        var offsetX = ((selected % width) - (width - 1) * 0.5) * region.CellSpacingMeters;
        var offsetY = ((selected / width) - (width - 1) * 0.5) * region.CellSpacingMeters;
        return PlanetVector.Normalize((center * ReferencePlanetRadiusMeters) +
            (frame.East * offsetX) + (frame.North * offsetY));
    }

    /// <summary>
    /// Selects a physical nested catchment rather than the deepest isolated
    /// eroded pixel. Each potential child center is scored against a 5×5
    /// sampling of the surrounding quarter-scale landscape. Subtracting the
    /// best-fit local plane distinguishes actual branching bedrock morphology
    /// from a uniformly sloping escarpment or a single raster-step scar.
    /// </summary>
    public static int FindIncisedChannelCell(PlanetHeroRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var width = region.Width;
        var margin = Math.Max(2, width / 5);
        var radius = Math.Max(2, (width - 1) / 8);
        var step = Math.Max(1, radius / 2);
        var selected = (width / 2) * width + (width / 2);
        var best = double.NegativeInfinity;
        var areaPerCellKm2 = region.CellSpacingMeters * region.CellSpacingMeters / 1_000_000.0;

        for (var y = margin; y < width - margin; y++)
        {
            for (var x = margin; x < width - margin; x++)
            {
                var index = y * width + x;
                var cut = Math.Max(0.0, region.CumulativeCutMeters[index]);
                var contributingKm2 = Math.Max(0.0, region.AccumulatedRunoffCells[index]) * areaPerCellKm2;
                // Prefer a real drain with water and excavation. A fallback is
                // still available for a perfectly dry or uneroded research tile.
                if (cut <= 0.01 || contributingKm2 <= 0.01)
                {
                    continue;
                }

                var min = double.PositiveInfinity;
                var max = double.NegativeInfinity;
                var mean = 0.0;
                var horizontalMoment = 0.0;
                var verticalMoment = 0.0;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var sampleX = Math.Clamp(x + dx * step, 0, width - 1);
                        var sampleY = Math.Clamp(y + dy * step, 0, width - 1);
                        var height = region.EvolvedElevationMeters[sampleY * width + sampleX];
                        min = Math.Min(min, height);
                        max = Math.Max(max, height);
                        mean += height;
                        horizontalMoment += dx * height;
                        verticalMoment += dy * height;
                    }
                }

                mean /= 25.0;
                var horizontalSlope = horizontalMoment / 50.0;
                var verticalSlope = verticalMoment / 50.0;
                var residualSum = 0.0;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var sampleX = Math.Clamp(x + dx * step, 0, width - 1);
                        var sampleY = Math.Clamp(y + dy * step, 0, width - 1);
                        var height = region.EvolvedElevationMeters[sampleY * width + sampleX];
                        var residual = height - (mean + horizontalSlope * dx + verticalSlope * dy);
                        residualSum += residual * residual;
                    }
                }

                var nonPlanarRelief = Math.Sqrt(residualSum / 25.0);
                var score = 1.8 * nonPlanarRelief + 0.25 * (max - min) +
                    Math.Min(cut, 200.0) * 0.45 + 12.0 * Math.Log2(1.0 + contributingKm2);
                if (score <= best)
                {
                    continue;
                }

                best = score;
                selected = index;
            }
        }

        return selected;
    }

    /// <summary>
    /// Provides the nested geological solve with the parent's already-evolved terrain.
    /// The child first samples this shared physical height, then develops finer drainage;
    /// it must not independently regenerate a different geological world.
    /// </summary>
    public static PlanetRegionalGeologyOverlay CreateParentOverlay(PlanetHeroRegion parent, PlanetVector centerDirection)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var center = PlanetVector.Normalize(centerDirection);
        var key = $"hero-parent/{parent.Seed}/{center.X:R}/{center.Y:R}/{center.Z:R}/{parent.Width}/{parent.CellSpacingMeters:R}";
        var baseline = PlanetRegionalGeologyEvolution.Initialize(parent.Seed, key, parent.Width, parent.Width,
            parent.CellSpacingMeters, parent.OriginalElevationMeters);
        var evolved = baseline with
        {
            Iteration = parent.ErosionIterations,
            ElevationMeters = (float[])parent.EvolvedElevationMeters.Clone(),
            CumulativeErodedVolumeCubicMeters = parent.ErodedVolumeCubicMeters,
            CumulativeDepositedVolumeCubicMeters = parent.DepositedVolumeCubicMeters,
            CumulativeExportedVolumeCubicMeters = parent.ExportedVolumeCubicMeters
        };

        return PlanetRegionalGeologyOverlay.Create(baseline, evolved, center, ReferencePlanetRadiusMeters);
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
        var erodibility = new float[gridWidth * gridWidth];
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
                var index = y * gridWidth + x;
                original[index] = (float)elevationSource.SampleElevationMeters(direction, seed);
                erodibility[index] = PlanetHeroRockResistance.SampleErodibility(direction, seed, original[index]);
            }
        }

        var key = $"hero/{seed}/{center.X:R}/{center.Y:R}/{center.Z:R}/{gridWidth}/{regionSpanMeters:R}";
        var initial = PlanetRegionalGeologyEvolution.Initialize(seed, key, gridWidth, gridWidth, spacing, original);
        var evolved = PlanetRegionalGeologyEvolution.Advance(initial, erosionIterations,
            cancellationToken: cancellationToken, erodibilityCellWeights: erodibility);
        // Hydraulic flow is discrete at the research grid resolution. Redistributing
        // its already-computed cut/fill field conservatively approximates lateral
        // bank retreat without applying fake shader displacement to the preview.
        var relaxedElevation = PlanetLateralErosionRelaxation.Apply(original, evolved.ElevationMeters, gridWidth, gridWidth,
            preserveIncisionEdges: true);
        var finalWatershed = PlanetRegionalWatershed.Build(gridWidth, gridWidth, spacing, relaxedElevation);
        // Hydraulically routed tributaries excavate lateral bank shoulders into the
        // actual mesh. Excavated material is currently exported at this research
        // region's boundary; downstream inter-region deposition is future work.
        var valleys = PlanetValleyBankCarver.Apply(
            original, relaxedElevation, finalWatershed.AccumulatedRunoffCells, gridWidth, gridWidth, spacing,
            finalWatershed.DownstreamIndices, cancellationToken);
        // Resolve unstable bedrock faces with local, conservative hillslope
        // transport. This stage reworks material into downhill talus, not
        // another export from an isolated region.
        var hillslopes = PlanetHillslopeMassWasting.Apply(
            valleys.ElevationMeters, erodibility, gridWidth, gridWidth, spacing,
            cancellationToken: cancellationToken);
        var cut = new float[original.Length];
        for (var index = 0; index < cut.Length; index++)
        {
            cut[index] = original[index] - hillslopes.ElevationMeters[index];
        }

        var region = new PlanetHeroRegion(seed, gridWidth, spacing, original, hillslopes.ElevationMeters, cut,
            finalWatershed.AccumulatedRunoffCells, erosionIterations, PlanetLateralErosionRelaxation.DefaultPasses,
            evolved.CumulativeErodedVolumeCubicMeters + valleys.AdditionalExportedSedimentCubicMeters +
                hillslopes.AdditionalErodedVolumeCubicMeters,
            evolved.CumulativeDepositedVolumeCubicMeters + hillslopes.AdditionalDepositedVolumeCubicMeters,
            evolved.CumulativeExportedVolumeCubicMeters + valleys.AdditionalExportedSedimentCubicMeters,
            HillslopeTransportedVolumeCubicMeters: hillslopes.AdditionalDepositedVolumeCubicMeters,
            HillslopeInitiallyUnstableEdges: hillslopes.InitiallyUnstableEdges);
        return region with { FocusCellIndex = FindIncisedChannelCell(region) };
    }
}
