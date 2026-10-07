using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetRunoffModel
{
    private const double SecondsPerYear = 365.25 * 24.0 * 60.0 * 60.0;

    public PlanetRunoffSnapshot Build(
        PlanetHydrologySnapshot hydrology,
        double planetRadiusMeters,
        double seaLevelMeters,
        double annualRunoffMillimeters,
        CancellationToken cancellationToken)
        => Build(hydrology, planetRadiusMeters, seaLevelMeters, annualRunoffMillimeters, null, cancellationToken);

    public PlanetRunoffSnapshot Build(
        PlanetHydrologySnapshot hydrology,
        double planetRadiusMeters,
        double seaLevelMeters,
        double annualRunoffMillimeters,
        IReadOnlyList<double>? additionalRunoffMillimeters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hydrology);
        ValidateInputs(hydrology, planetRadiusMeters, seaLevelMeters, annualRunoffMillimeters, additionalRunoffMillimeters);

        var cellAreaSquareMeters = 4.0 * Math.PI * planetRadiusMeters * planetRadiusMeters / hydrology.Layout.CellCount;
        var precipitationRunoffMillimeters = BuildLocalRunoff(hydrology, seaLevelMeters, annualRunoffMillimeters, cancellationToken);
        var localRunoffMillimeters = new double[hydrology.Layout.CellCount];
        var accumulatedVolumeCubicMeters = new double[hydrology.Layout.CellCount];

        for (var index = 0; index < hydrology.Layout.CellCount; index++)
        {
            CheckCancellation(index, cancellationToken);
            if (hydrology.Cells[index].IsOcean)
            {
                continue;
            }

            var meltwaterRunoffMillimeters = additionalRunoffMillimeters?[index] ?? 0.0;
            localRunoffMillimeters[index] = precipitationRunoffMillimeters[index] + meltwaterRunoffMillimeters;
            accumulatedVolumeCubicMeters[index] = localRunoffMillimeters[index] / 1_000.0 * cellAreaSquareMeters;
        }

        AccumulateDownstream(hydrology, accumulatedVolumeCubicMeters, cancellationToken);

        var cells = new PlanetRunoffCell[hydrology.Layout.CellCount];
        for (var index = 0; index < cells.Length; index++)
        {
            CheckCancellation(index, cancellationToken);
            var hydrologyCell = hydrology.Cells[index];
            cells[index] = new PlanetRunoffCell(
                hydrologyCell.Cell,
                localRunoffMillimeters[index],
                hydrologyCell.ContributingLandCellCount * cellAreaSquareMeters,
                accumulatedVolumeCubicMeters[index],
                accumulatedVolumeCubicMeters[index] / SecondsPerYear)
            {
                LocalAnnualPrecipitationRunoffMillimeters = precipitationRunoffMillimeters[index],
                LocalAnnualMeltwaterRunoffMillimeters = additionalRunoffMillimeters?[index] ?? 0.0,
            };
        }

        return new PlanetRunoffSnapshot(hydrology, cellAreaSquareMeters, cells);
    }

    private static void ValidateInputs(
        PlanetHydrologySnapshot hydrology,
        double planetRadiusMeters,
        double seaLevelMeters,
        double annualRunoffMillimeters,
        IReadOnlyList<double>? additionalRunoffMillimeters)
    {
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        if (!double.IsFinite(seaLevelMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(seaLevelMeters), seaLevelMeters, "Sea level must be finite.");
        }

        if (!double.IsFinite(annualRunoffMillimeters) || annualRunoffMillimeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(annualRunoffMillimeters), annualRunoffMillimeters, "Annual runoff must be finite and non-negative.");
        }

        if (additionalRunoffMillimeters is not null && additionalRunoffMillimeters.Count != hydrology.Layout.CellCount)
        {
            throw new ArgumentException("Additional runoff cell count must match the hydrology grid.", nameof(additionalRunoffMillimeters));
        }

        if (additionalRunoffMillimeters is null)
        {
            return;
        }

        for (var index = 0; index < additionalRunoffMillimeters.Count; index++)
        {
            var value = additionalRunoffMillimeters[index];
            if (!double.IsFinite(value) || value < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(additionalRunoffMillimeters), value, "Additional runoff values must be finite and non-negative.");
            }
        }
    }

    private static double[] BuildLocalRunoff(
        PlanetHydrologySnapshot hydrology,
        double seaLevelMeters,
        double annualRunoffMillimeters,
        CancellationToken cancellationToken)
    {
        var weights = new double[hydrology.Layout.CellCount];
        var weightSum = 0.0;
        var landCellCount = 0;

        for (var index = 0; index < weights.Length; index++)
        {
            CheckCancellation(index, cancellationToken);
            var cell = hydrology.Cells[index];
            if (cell.IsOcean)
            {
                continue;
            }

            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Cell);
            var latitudeMoisture = 0.72 + (0.56 * Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(direction.Y)), 0.65));
            var elevationAboveSeaLevel = Math.Max(0.0, cell.RawElevationMeters - seaLevelMeters);
            var orographicResponse = Math.Clamp(0.88 + (elevationAboveSeaLevel / 7_000.0 * 0.28), 0.88, 1.16);
            var weight = latitudeMoisture * orographicResponse;
            weights[index] = weight;
            weightSum += weight;
            landCellCount++;
        }

        if (landCellCount == 0 || annualRunoffMillimeters <= 0.0)
        {
            return weights;
        }

        var meanWeight = weightSum / landCellCount;
        for (var index = 0; index < weights.Length; index++)
        {
            if (!hydrology.Cells[index].IsOcean)
            {
                weights[index] = annualRunoffMillimeters * weights[index] / meanWeight;
            }
        }

        return weights;
    }

    private static void AccumulateDownstream(PlanetHydrologySnapshot hydrology, double[] accumulatedVolumeCubicMeters, CancellationToken cancellationToken)
    {
        var orderedLandIndices = hydrology.Cells
            .Select((cell, index) => (cell, index))
            .Where(item => !item.cell.IsOcean)
            .OrderBy(item => item.cell.ContributingLandCellCount)
            .ThenBy(item => item.index)
            .Select(item => item.index)
            .ToArray();

        for (var orderIndex = 0; orderIndex < orderedLandIndices.Length; orderIndex++)
        {
            CheckCancellation(orderIndex, cancellationToken);
            var cellIndex = orderedLandIndices[orderIndex];
            var target = hydrology.Cells[cellIndex].DrainageTarget;
            if (target is null)
            {
                continue;
            }

            accumulatedVolumeCubicMeters[hydrology.Layout.GetIndex(target.Value)] += accumulatedVolumeCubicMeters[cellIndex];
        }
    }

    private static void CheckCancellation(int iteration, CancellationToken cancellationToken)
    {
        if ((iteration & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
