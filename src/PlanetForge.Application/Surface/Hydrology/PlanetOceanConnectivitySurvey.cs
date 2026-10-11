using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

/// <summary>
/// Cell-count topology on the actual spherical hydrology grid. Cell counts
/// are useful for algorithms but are not a physical surface-area measurement.
/// </summary>
public readonly record struct PlanetOceanConnectivityReport(
    int TotalCells,
    int OceanCells,
    int OceanComponents,
    int LargestOceanComponentCells)
{
    public int LandCells => TotalCells - OceanCells;

    public double OceanCellFraction => TotalCells > 0 ? OceanCells / (double)TotalCells : 0.0;

    public double LargestConnectedOceanShare => OceanCells > 0 ? LargestOceanComponentCells / (double)OceanCells : 0.0;
}

/// <summary>
/// Physical spherical surface area measured with the same cell geometry as
/// the sea-level hypsometry model. Does not claim a screenshot pixel ratio.
/// </summary>
public readonly record struct PlanetOceanAreaConnectivityReport(
    PlanetOceanConnectivityReport Topology,
    double TotalAreaSquareMeters,
    double OceanAreaSquareMeters,
    double LargestConnectedOceanAreaSquareMeters)
{
    public double OceanAreaFraction => TotalAreaSquareMeters > 0.0 ? OceanAreaSquareMeters / TotalAreaSquareMeters : 0.0;

    public double LargestConnectedOceanAreaShare =>
        OceanAreaSquareMeters > 0.0 ? LargestConnectedOceanAreaSquareMeters / OceanAreaSquareMeters : 0.0;
}

/// <summary>
/// Measures connected physical submerged terrain through the existing
/// seam-aware cubed-sphere hydrology neighbors, never an image colour mask.
/// </summary>
public static class PlanetOceanConnectivitySurvey
{
    public static PlanetOceanConnectivityReport Measure(PlanetHydrologySnapshot hydrology, CancellationToken cancellationToken) =>
        MeasureCore(hydrology, null, cancellationToken).Topology;

    public static PlanetOceanAreaConnectivityReport MeasureAreaWeighted(
        PlanetHydrologySnapshot hydrology, double radiusMeters, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Planet radius must be finite and positive.");
        }

        return MeasureCore(hydrology, radiusMeters, cancellationToken);
    }

    private static PlanetOceanAreaConnectivityReport MeasureCore(
        PlanetHydrologySnapshot hydrology, double? radiusMeters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hydrology);
        cancellationToken.ThrowIfCancellationRequested();
        var layout = hydrology.Layout;
        var visited = new bool[layout.CellCount];
        var queue = new int[layout.CellCount];
        var areaByCell = radiusMeters.HasValue ? new double[layout.CellCount] : null;
        var totalArea = 0.0;

        if (areaByCell is not null)
        {
            for (var index = 0; index < areaByCell.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                areaByCell[index] = PlanetSurfaceCellArea.AreaSquareMeters(layout.GetCell(index), radiusMeters!.Value);
                totalArea += areaByCell[index];
            }
        }

        var oceanCells = 0;
        var components = 0;
        var largestCells = 0;
        var oceanArea = 0.0;
        var largestArea = 0.0;

        for (var index = 0; index < layout.CellCount; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!hydrology.Cells[index].IsOcean) continue;
            oceanCells++;
            if (areaByCell is not null) oceanArea += areaByCell[index];
            if (visited[index]) continue;

            components++;
            visited[index] = true;
            var head = 0;
            var tail = 1;
            queue[0] = index;
            var size = 0;
            var componentArea = 0.0;

            while (head < tail)
            {
                if ((head & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var currentIndex = queue[head++];
                size++;
                if (areaByCell is not null) componentArea += areaByCell[currentIndex];
                foreach (var neighbor in PlanetHydrologyNeighborhood.Enumerate(layout.GetCell(currentIndex)))
                {
                    var neighborIndex = layout.GetIndex(neighbor);
                    if (visited[neighborIndex] || !hydrology.Cells[neighborIndex].IsOcean) continue;
                    visited[neighborIndex] = true;
                    queue[tail++] = neighborIndex;
                }
            }

            largestCells = Math.Max(largestCells, size);
            largestArea = Math.Max(largestArea, componentArea);
        }

        return new PlanetOceanAreaConnectivityReport(
            new PlanetOceanConnectivityReport(layout.CellCount, oceanCells, components, largestCells),
            totalArea, oceanArea, largestArea);
    }
}
