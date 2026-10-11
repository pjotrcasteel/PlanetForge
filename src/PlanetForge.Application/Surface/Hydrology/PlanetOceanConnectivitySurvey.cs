using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

/// <summary>
/// Connected-component diagnostics for actual submerged terrain on the
/// spherical hydrology grid. Values count grid cells, not equal-area physical
/// square metres; cubed-sphere cells vary in area. This is not a renderer mask.
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
/// Measures the geographic connectivity of terrain at the actual chosen sea
/// level. Uses existing seam-aware cubed-sphere topology, not a screen-space
/// framebuffer. The largest basin can be compared with rendered ocean quality
/// to locate terrain-versus-graphics fragmentation.
/// </summary>
public static class PlanetOceanConnectivitySurvey
{
    public static PlanetOceanConnectivityReport Measure(PlanetHydrologySnapshot hydrology, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hydrology);
        var layout = hydrology.Layout;
        var visited = new bool[layout.CellCount];
        var queue = new int[layout.CellCount];
        var oceanCells = 0;
        var components = 0;
        var largest = 0;

        for (var index = 0; index < layout.CellCount; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!hydrology.Cells[index].IsOcean)
            {
                continue;
            }

            oceanCells++;
            if (visited[index]) continue;
            components++;
            visited[index] = true;
            var head = 0;
            var tail = 1;
            queue[0] = index;
            var size = 0;

            while (head < tail)
            {
                if ((head & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var currentIndex = queue[head++];
                size++;
                foreach (var neighbor in PlanetHydrologyNeighborhood.Enumerate(layout.GetCell(currentIndex)))
                {
                    var neighborIndex = layout.GetIndex(neighbor);
                    if (visited[neighborIndex] || !hydrology.Cells[neighborIndex].IsOcean) continue;
                    visited[neighborIndex] = true;
                    queue[tail++] = neighborIndex;
                }
            }

            largest = Math.Max(largest, size);
        }

        return new PlanetOceanConnectivityReport(layout.CellCount, oceanCells, components, largest);
    }
}
