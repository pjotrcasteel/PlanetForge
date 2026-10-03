using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetHydrologyFeatureExtractor
{
    private const double LakeElevationToleranceMeters = 0.001;
    private const double MinimumLakeDepthMeters = 0.001;

    public PlanetHydrologyFeatures Extract(
        PlanetHydrologySnapshot hydrology,
        long minimumRiverContributingLandCells,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hydrology);

        if (minimumRiverContributingLandCells < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumRiverContributingLandCells),
                minimumRiverContributingLandCells,
                "Minimum river contribution must be at least one land cell.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var watersheds = ExtractWatersheds(hydrology, cancellationToken);
        var lakes = ExtractLakes(hydrology, cancellationToken);
        var riverSegments = ExtractRiverSegments(hydrology, minimumRiverContributingLandCells, cancellationToken);
        return new PlanetHydrologyFeatures(watersheds, lakes, riverSegments);
    }

    private static IReadOnlyList<PlanetWatershed> ExtractWatersheds(PlanetHydrologySnapshot hydrology, CancellationToken cancellationToken)
    {
        var layout = hydrology.Layout;
        var terminalIndices = new int[layout.CellCount];
        Array.Fill(terminalIndices, -1);

        for (var index = 0; index < layout.CellCount; index++)
        {
            CheckCancellation(index, cancellationToken);
            ResolveTerminalIndex(hydrology, index, terminalIndices);
        }

        var landCellCountByOutlet = new Dictionary<int, long>();
        for (var index = 0; index < layout.CellCount; index++)
        {
            CheckCancellation(index, cancellationToken);
            if (hydrology.Cells[index].IsOcean)
            {
                continue;
            }

            var outletIndex = terminalIndices[index];
            landCellCountByOutlet.TryGetValue(outletIndex, out var count);
            landCellCountByOutlet[outletIndex] = count + 1;
        }

        return landCellCountByOutlet
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var outlet = hydrology.Cells[pair.Key];
                return new PlanetWatershed(
                    outlet.Cell,
                    outlet.IsOcean,
                    pair.Value,
                    outlet.ContributingLandCellCount);
            })
            .ToArray();
    }

    private static int ResolveTerminalIndex(PlanetHydrologySnapshot hydrology, int startIndex, int[] terminalIndices)
    {
        if (terminalIndices[startIndex] >= 0)
        {
            return terminalIndices[startIndex];
        }

        var layout = hydrology.Layout;
        var path = new List<int>();
        var currentIndex = startIndex;

        while (terminalIndices[currentIndex] < 0)
        {
            path.Add(currentIndex);
            var current = hydrology.Cells[currentIndex];
            if (current.DrainageTarget is null)
            {
                terminalIndices[currentIndex] = currentIndex;
                break;
            }

            currentIndex = layout.GetIndex(current.DrainageTarget.Value);
        }

        var terminalIndex = terminalIndices[currentIndex];
        for (var index = path.Count - 1; index >= 0; index--)
        {
            terminalIndices[path[index]] = terminalIndex;
        }

        return terminalIndex;
    }

    private static IReadOnlyList<PlanetLake> ExtractLakes(PlanetHydrologySnapshot hydrology, CancellationToken cancellationToken)
    {
        var layout = hydrology.Layout;
        var visited = new bool[layout.CellCount];
        var lakes = new List<PlanetLake>();

        for (var index = 0; index < layout.CellCount; index++)
        {
            CheckCancellation(index, cancellationToken);
            var cell = hydrology.Cells[index];
            if (visited[index] || !IsLakeCell(cell))
            {
                continue;
            }

            lakes.Add(ExtractLake(hydrology, index, visited, cancellationToken));
        }

        return lakes;
    }

    private static PlanetLake ExtractLake(PlanetHydrologySnapshot hydrology, int startIndex, bool[] visited, CancellationToken cancellationToken)
    {
        var layout = hydrology.Layout;
        var queue = new Queue<int>();
        var cells = new List<PlanetSurfaceGridCellId>();
        var surfaceElevationMeters = hydrology.Cells[startIndex].FilledElevationMeters;
        var maximumDepthMeters = 0.0;
        var minimumIndex = startIndex;
        queue.Enqueue(startIndex);
        visited[startIndex] = true;

        while (queue.TryDequeue(out var currentIndex))
        {
            CheckCancellation(cells.Count, cancellationToken);
            var current = hydrology.Cells[currentIndex];
            cells.Add(current.Cell);
            maximumDepthMeters = Math.Max(maximumDepthMeters, current.DepressionFillDepthMeters);
            minimumIndex = Math.Min(minimumIndex, currentIndex);

            foreach (var direction in Enum.GetValues<PlanetGridDirection>())
            {
                var neighbor = PlanetSurfaceGridTopology.GetNeighbor(current.Cell, direction).Cell;
                var neighborIndex = layout.GetIndex(neighbor);
                if (visited[neighborIndex])
                {
                    continue;
                }

                var neighborCell = hydrology.Cells[neighborIndex];
                if (!IsLakeCell(neighborCell) || Math.Abs(neighborCell.FilledElevationMeters - surfaceElevationMeters) > LakeElevationToleranceMeters)
                {
                    continue;
                }

                visited[neighborIndex] = true;
                queue.Enqueue(neighborIndex);
            }
        }

        cells.Sort((first, second) => layout.GetIndex(first).CompareTo(layout.GetIndex(second)));
        return new PlanetLake(minimumIndex, surfaceElevationMeters, maximumDepthMeters, cells);
    }

    private static IReadOnlyList<PlanetRiverSegment> ExtractRiverSegments(
        PlanetHydrologySnapshot hydrology,
        long minimumRiverContributingLandCells,
        CancellationToken cancellationToken)
    {
        var layout = hydrology.Layout;
        var riverIndices = hydrology.Cells
            .Select((cell, index) => (cell, index))
            .Where(item => !item.cell.IsOcean && item.cell.DrainageTarget is not null && item.cell.ContributingLandCellCount >= minimumRiverContributingLandCells)
            .Select(item => item.index)
            .OrderBy(index => hydrology.Cells[index].ContributingLandCellCount)
            .ThenBy(index => index)
            .ToArray();
        var riverSet = riverIndices.ToHashSet();
        var upstreamByIndex = new Dictionary<int, List<int>>();

        foreach (var riverIndex in riverIndices)
        {
            var target = hydrology.Cells[riverIndex].DrainageTarget!.Value;
            var targetIndex = layout.GetIndex(target);
            if (!riverSet.Contains(targetIndex))
            {
                continue;
            }

            if (!upstreamByIndex.TryGetValue(targetIndex, out var upstream))
            {
                upstream = [];
                upstreamByIndex.Add(targetIndex, upstream);
            }

            upstream.Add(riverIndex);
        }

        var streamOrderByIndex = new Dictionary<int, int>();
        var segments = new PlanetRiverSegment[riverIndices.Length];
        for (var resultIndex = 0; resultIndex < riverIndices.Length; resultIndex++)
        {
            CheckCancellation(resultIndex, cancellationToken);
            var riverIndex = riverIndices[resultIndex];
            var cell = hydrology.Cells[riverIndex];
            var order = CalculateStrahlerOrder(riverIndex, upstreamByIndex, streamOrderByIndex);
            streamOrderByIndex[riverIndex] = order;
            var target = cell.DrainageTarget!.Value;
            var targetCell = hydrology.GetCell(target);
            segments[resultIndex] = new PlanetRiverSegment(
                cell.Cell,
                target,
                PlanetSurfaceGridGeometry.GetCenterDirection(cell.Cell),
                PlanetSurfaceGridGeometry.GetCenterDirection(target),
                cell.FilledElevationMeters,
                targetCell.FilledElevationMeters,
                cell.ContributingLandCellCount,
                order);
        }

        return segments;
    }

    private static int CalculateStrahlerOrder(int riverIndex, IReadOnlyDictionary<int, List<int>> upstreamByIndex, IReadOnlyDictionary<int, int> streamOrderByIndex)
    {
        if (!upstreamByIndex.TryGetValue(riverIndex, out var upstream) || upstream.Count == 0)
        {
            return 1;
        }

        var maximumOrder = 0;
        var maximumOrderCount = 0;
        foreach (var upstreamIndex in upstream)
        {
            var order = streamOrderByIndex[upstreamIndex];
            if (order > maximumOrder)
            {
                maximumOrder = order;
                maximumOrderCount = 1;
            }
            else if (order == maximumOrder)
            {
                maximumOrderCount++;
            }
        }

        return maximumOrderCount >= 2 ? maximumOrder + 1 : maximumOrder;
    }

    private static bool IsLakeCell(PlanetHydrologyCell cell) => !cell.IsOcean && cell.DepressionFillDepthMeters > MinimumLakeDepthMeters;

    private static void CheckCancellation(int iteration, CancellationToken cancellationToken)
    {
        if ((iteration & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
