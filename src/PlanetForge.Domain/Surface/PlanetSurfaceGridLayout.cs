namespace PlanetForge.Domain.Surface;

public sealed class PlanetSurfaceGridLayout
{
    public PlanetSurfaceGridLayout(int level)
    {
        if (level < 0 || level > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be between 0 and 12.");
        }

        Level = level;
        CellsPerAxis = 1 << level;
        CellsPerFace = checked(CellsPerAxis * CellsPerAxis);
        CellCount = checked(6 * CellsPerFace);
    }

    public int Level { get; }

    public int CellsPerAxis { get; }

    public int CellsPerFace { get; }

    public int CellCount { get; }

    public int GetIndex(PlanetSurfaceGridCellId cell)
    {
        if (cell.Level != Level)
        {
            throw new ArgumentException("Cell level must match the grid layout level.", nameof(cell));
        }

        return checked(((int)cell.Face * CellsPerFace) + (cell.Y * CellsPerAxis) + cell.X);
    }

    public PlanetSurfaceGridCellId GetCell(int index)
    {
        if (index < 0 || index >= CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be inside the grid layout.");
        }

        var face = (CubeFace)(index / CellsPerFace);
        var faceIndex = index % CellsPerFace;
        var y = faceIndex / CellsPerAxis;
        var x = faceIndex % CellsPerAxis;
        return new PlanetSurfaceGridCellId(face, Level, x, y);
    }
}
