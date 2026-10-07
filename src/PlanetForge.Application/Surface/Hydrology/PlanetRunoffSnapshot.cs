using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetRunoffSnapshot
{
    private readonly IReadOnlyList<PlanetRunoffCell> cells;

    public PlanetRunoffSnapshot(PlanetHydrologySnapshot hydrology, double cellAreaSquareMeters, IReadOnlyList<PlanetRunoffCell> cells)
    {
        ArgumentNullException.ThrowIfNull(hydrology);
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count != hydrology.Layout.CellCount)
        {
            throw new ArgumentException("Runoff cell count must match the hydrology grid.", nameof(cells));
        }

        Hydrology = hydrology;
        CellAreaSquareMeters = cellAreaSquareMeters;
        this.cells = cells;
    }

    public PlanetHydrologySnapshot Hydrology { get; }

    public double CellAreaSquareMeters { get; }

    public IReadOnlyList<PlanetRunoffCell> Cells => cells;

    public PlanetRunoffCell GetCell(PlanetSurfaceGridCellId cell) => cells[Hydrology.Layout.GetIndex(cell)];
}
