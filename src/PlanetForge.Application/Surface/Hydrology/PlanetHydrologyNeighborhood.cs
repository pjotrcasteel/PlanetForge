using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

internal static class PlanetHydrologyNeighborhood
{
    public static IEnumerable<PlanetSurfaceGridCellId> Enumerate(PlanetSurfaceGridCellId cell)
    {
        foreach (var direction in Enum.GetValues<PlanetGridDirection>())
        {
            yield return PlanetSurfaceGridTopology.GetNeighbor(cell, direction).Cell;
        }

        var last = cell.CellsPerAxis - 1;
        if (cell.X == 0 || cell.X == last || cell.Y == 0 || cell.Y == last)
        {
            yield break;
        }

        yield return new PlanetSurfaceGridCellId(cell.Face, cell.Level, cell.X - 1, cell.Y - 1);
        yield return new PlanetSurfaceGridCellId(cell.Face, cell.Level, cell.X - 1, cell.Y + 1);
        yield return new PlanetSurfaceGridCellId(cell.Face, cell.Level, cell.X + 1, cell.Y - 1);
        yield return new PlanetSurfaceGridCellId(cell.Face, cell.Level, cell.X + 1, cell.Y + 1);
    }
}
