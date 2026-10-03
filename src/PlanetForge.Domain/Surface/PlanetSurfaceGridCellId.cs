namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetSurfaceGridCellId
{
    public PlanetSurfaceGridCellId(CubeFace face, int level, int x, int y)
    {
        if (level < 0 || level > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be between 0 and 30.");
        }

        var cellsPerAxis = 1 << level;
        if (x < 0 || x >= cellsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "X must be inside the grid for the selected level.");
        }

        if (y < 0 || y >= cellsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Y must be inside the grid for the selected level.");
        }

        Face = face;
        Level = level;
        X = x;
        Y = y;
    }

    public CubeFace Face { get; }

    public int Level { get; }

    public int X { get; }

    public int Y { get; }

    public int CellsPerAxis => 1 << Level;
}
