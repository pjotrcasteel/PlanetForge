using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetHydrologyModelBuilder(IPlanetElevationSource elevationSource)
{
    private const int MaximumHydrologyLevel = 9;

    public PlanetHydrologySnapshot Build(int level, int seed, double planetRadiusMeters, double seaLevelMeters, CancellationToken cancellationToken)
    {
        if (level < 0 || level > MaximumHydrologyLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Hydrology level must be between 0 and {MaximumHydrologyLevel}.");
        }

        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        if (!double.IsFinite(seaLevelMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(seaLevelMeters), seaLevelMeters, "Sea level must be finite.");
        }

        var layout = new PlanetSurfaceGridLayout(level);
        var rawElevationMeters = new double[layout.CellCount];
        var filledElevationMeters = new double[layout.CellCount];
        var isOcean = new bool[layout.CellCount];
        SampleTerrain(layout, seed, seaLevelMeters, rawElevationMeters, filledElevationMeters, isOcean, cancellationToken);

        var drainageTargetIndices = new int[layout.CellCount];
        Array.Fill(drainageTargetIndices, -1);
        var visitOrder = FloodDepressions(layout, rawElevationMeters, filledElevationMeters, isOcean, drainageTargetIndices, cancellationToken);
        var contributingLandCellCount = AccumulateFlow(visitOrder, drainageTargetIndices, isOcean, cancellationToken);
        var cells = BuildCells(layout, rawElevationMeters, filledElevationMeters, drainageTargetIndices, contributingLandCellCount, isOcean);
        return new PlanetHydrologySnapshot(layout, cells);
    }

    private void SampleTerrain(
        PlanetSurfaceGridLayout layout,
        int seed,
        double seaLevelMeters,
        double[] rawElevationMeters,
        double[] filledElevationMeters,
        bool[] isOcean,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < layout.CellCount; index++)
        {
            CheckCancellation(index, cancellationToken);
            var cell = layout.GetCell(index);
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell);
            var elevationMeters = elevationSource.SampleElevationMeters(direction, seed);
            rawElevationMeters[index] = elevationMeters;
            filledElevationMeters[index] = elevationMeters;
            isOcean[index] = elevationMeters <= seaLevelMeters;
        }
    }

    private static int[] FloodDepressions(
        PlanetSurfaceGridLayout layout,
        double[] rawElevationMeters,
        double[] filledElevationMeters,
        bool[] isOcean,
        int[] drainageTargetIndices,
        CancellationToken cancellationToken)
    {
        var queue = new PriorityQueue<int, (double ElevationMeters, int Index)>();
        var visited = new bool[layout.CellCount];
        var visitOrder = new int[layout.CellCount];
        var seededOutletCount = SeedOutlets(rawElevationMeters, filledElevationMeters, isOcean, visited, queue);

        if (seededOutletCount == 0)
        {
            var minimumIndex = FindMinimumElevationIndex(rawElevationMeters);
            visited[minimumIndex] = true;
            queue.Enqueue(minimumIndex, (filledElevationMeters[minimumIndex], minimumIndex));
        }

        var visitCount = 0;
        while (queue.TryDequeue(out var currentIndex, out _))
        {
            CheckCancellation(visitCount, cancellationToken);
            visitOrder[visitCount++] = currentIndex;
            var currentCell = layout.GetCell(currentIndex);

            foreach (var direction in Enum.GetValues<PlanetGridDirection>())
            {
                var neighborCell = PlanetSurfaceGridTopology.GetNeighbor(currentCell, direction).Cell;
                var neighborIndex = layout.GetIndex(neighborCell);
                if (visited[neighborIndex])
                {
                    continue;
                }

                visited[neighborIndex] = true;
                filledElevationMeters[neighborIndex] = Math.Max(rawElevationMeters[neighborIndex], filledElevationMeters[currentIndex]);
                drainageTargetIndices[neighborIndex] = currentIndex;
                queue.Enqueue(neighborIndex, (filledElevationMeters[neighborIndex], neighborIndex));
            }
        }

        if (visitCount != layout.CellCount)
        {
            throw new InvalidOperationException($"Hydrology flood visited {visitCount} of {layout.CellCount} cells.");
        }

        return visitOrder;
    }

    private static int SeedOutlets(
        double[] rawElevationMeters,
        double[] filledElevationMeters,
        bool[] isOcean,
        bool[] visited,
        PriorityQueue<int, (double ElevationMeters, int Index)> queue)
    {
        var outletCount = 0;
        for (var index = 0; index < isOcean.Length; index++)
        {
            if (!isOcean[index])
            {
                continue;
            }

            visited[index] = true;
            queue.Enqueue(index, (filledElevationMeters[index], index));
            outletCount++;
        }

        return outletCount;
    }

    private static int FindMinimumElevationIndex(double[] rawElevationMeters)
    {
        var minimumIndex = 0;
        for (var index = 1; index < rawElevationMeters.Length; index++)
        {
            if (rawElevationMeters[index] < rawElevationMeters[minimumIndex])
            {
                minimumIndex = index;
            }
        }

        return minimumIndex;
    }

    private static long[] AccumulateFlow(int[] visitOrder, int[] drainageTargetIndices, bool[] isOcean, CancellationToken cancellationToken)
    {
        var contributingLandCellCount = new long[visitOrder.Length];
        for (var index = 0; index < contributingLandCellCount.Length; index++)
        {
            contributingLandCellCount[index] = isOcean[index] ? 0L : 1L;
        }

        for (var orderIndex = visitOrder.Length - 1; orderIndex >= 0; orderIndex--)
        {
            CheckCancellation(orderIndex, cancellationToken);
            var cellIndex = visitOrder[orderIndex];
            var targetIndex = drainageTargetIndices[cellIndex];
            if (targetIndex >= 0)
            {
                contributingLandCellCount[targetIndex] += contributingLandCellCount[cellIndex];
            }
        }

        return contributingLandCellCount;
    }

    private static IReadOnlyList<PlanetHydrologyCell> BuildCells(
        PlanetSurfaceGridLayout layout,
        double[] rawElevationMeters,
        double[] filledElevationMeters,
        int[] drainageTargetIndices,
        long[] contributingLandCellCount,
        bool[] isOcean)
    {
        var cells = new PlanetHydrologyCell[layout.CellCount];
        for (var index = 0; index < layout.CellCount; index++)
        {
            PlanetSurfaceGridCellId? drainageTarget = drainageTargetIndices[index] >= 0 ? layout.GetCell(drainageTargetIndices[index]) : null;
            cells[index] = new PlanetHydrologyCell(
                layout.GetCell(index),
                rawElevationMeters[index],
                filledElevationMeters[index],
                drainageTarget,
                contributingLandCellCount[index],
                isOcean[index],
                Math.Max(0.0, filledElevationMeters[index] - rawElevationMeters[index]));
        }

        return cells;
    }

    private static void CheckCancellation(int iteration, CancellationToken cancellationToken)
    {
        if ((iteration & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
