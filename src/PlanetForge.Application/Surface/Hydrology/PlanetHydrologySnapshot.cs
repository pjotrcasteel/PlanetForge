using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface.Hydrology;

public sealed class PlanetHydrologySnapshot
{
    private readonly IReadOnlyList<PlanetHydrologyCell> cells;

    public PlanetHydrologySnapshot(PlanetSurfaceGridLayout layout, IReadOnlyList<PlanetHydrologyCell> cells)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count != layout.CellCount)
        {
            throw new ArgumentException("Hydrology cell count must match the grid layout.", nameof(cells));
        }

        Layout = layout;
        this.cells = cells;
    }

    public PlanetSurfaceGridLayout Layout { get; }

    public IReadOnlyList<PlanetHydrologyCell> Cells => cells;

    public PlanetHydrologyCell GetCell(PlanetSurfaceGridCellId cell) => cells[Layout.GetIndex(cell)];
}
