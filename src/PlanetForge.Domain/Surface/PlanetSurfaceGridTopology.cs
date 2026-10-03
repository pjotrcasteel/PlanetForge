namespace PlanetForge.Domain.Surface;

public static class PlanetSurfaceGridTopology
{
    public static PlanetSurfaceGridNeighbor GetNeighbor(PlanetSurfaceGridCellId cell, PlanetGridDirection direction)
    {
        var last = cell.CellsPerAxis - 1;
        return direction switch
        {
            PlanetGridDirection.North when cell.Y < last => Create(cell.Face, cell.Level, cell.X, cell.Y + 1, PlanetGridDirection.South),
            PlanetGridDirection.East when cell.X < last => Create(cell.Face, cell.Level, cell.X + 1, cell.Y, PlanetGridDirection.West),
            PlanetGridDirection.South when cell.Y > 0 => Create(cell.Face, cell.Level, cell.X, cell.Y - 1, PlanetGridDirection.North),
            PlanetGridDirection.West when cell.X > 0 => Create(cell.Face, cell.Level, cell.X - 1, cell.Y, PlanetGridDirection.East),
            _ => CrossFace(cell, direction, last),
        };
    }

    private static PlanetSurfaceGridNeighbor CrossFace(PlanetSurfaceGridCellId cell, PlanetGridDirection direction, int last)
    {
        var reversedX = last - cell.X;
        var reversedY = last - cell.Y;

        return (cell.Face, direction) switch
        {
            (CubeFace.PositiveZ, PlanetGridDirection.East) => Create(CubeFace.PositiveX, cell.Level, 0, cell.Y, PlanetGridDirection.West),
            (CubeFace.PositiveZ, PlanetGridDirection.West) => Create(CubeFace.NegativeX, cell.Level, last, cell.Y, PlanetGridDirection.East),
            (CubeFace.PositiveZ, PlanetGridDirection.North) => Create(CubeFace.PositiveY, cell.Level, cell.X, 0, PlanetGridDirection.South),
            (CubeFace.PositiveZ, PlanetGridDirection.South) => Create(CubeFace.NegativeY, cell.Level, cell.X, last, PlanetGridDirection.North),

            (CubeFace.NegativeZ, PlanetGridDirection.East) => Create(CubeFace.NegativeX, cell.Level, 0, cell.Y, PlanetGridDirection.West),
            (CubeFace.NegativeZ, PlanetGridDirection.West) => Create(CubeFace.PositiveX, cell.Level, last, cell.Y, PlanetGridDirection.East),
            (CubeFace.NegativeZ, PlanetGridDirection.North) => Create(CubeFace.PositiveY, cell.Level, reversedX, last, PlanetGridDirection.North),
            (CubeFace.NegativeZ, PlanetGridDirection.South) => Create(CubeFace.NegativeY, cell.Level, reversedX, 0, PlanetGridDirection.South),

            (CubeFace.PositiveX, PlanetGridDirection.East) => Create(CubeFace.NegativeZ, cell.Level, 0, cell.Y, PlanetGridDirection.West),
            (CubeFace.PositiveX, PlanetGridDirection.West) => Create(CubeFace.PositiveZ, cell.Level, last, cell.Y, PlanetGridDirection.East),
            (CubeFace.PositiveX, PlanetGridDirection.North) => Create(CubeFace.PositiveY, cell.Level, last, cell.X, PlanetGridDirection.East),
            (CubeFace.PositiveX, PlanetGridDirection.South) => Create(CubeFace.NegativeY, cell.Level, last, reversedX, PlanetGridDirection.East),

            (CubeFace.NegativeX, PlanetGridDirection.East) => Create(CubeFace.PositiveZ, cell.Level, 0, cell.Y, PlanetGridDirection.West),
            (CubeFace.NegativeX, PlanetGridDirection.West) => Create(CubeFace.NegativeZ, cell.Level, last, cell.Y, PlanetGridDirection.East),
            (CubeFace.NegativeX, PlanetGridDirection.North) => Create(CubeFace.PositiveY, cell.Level, 0, reversedX, PlanetGridDirection.West),
            (CubeFace.NegativeX, PlanetGridDirection.South) => Create(CubeFace.NegativeY, cell.Level, 0, cell.X, PlanetGridDirection.West),

            (CubeFace.PositiveY, PlanetGridDirection.East) => Create(CubeFace.PositiveX, cell.Level, cell.Y, last, PlanetGridDirection.North),
            (CubeFace.PositiveY, PlanetGridDirection.West) => Create(CubeFace.NegativeX, cell.Level, reversedY, last, PlanetGridDirection.North),
            (CubeFace.PositiveY, PlanetGridDirection.North) => Create(CubeFace.NegativeZ, cell.Level, reversedX, last, PlanetGridDirection.North),
            (CubeFace.PositiveY, PlanetGridDirection.South) => Create(CubeFace.PositiveZ, cell.Level, cell.X, last, PlanetGridDirection.North),

            (CubeFace.NegativeY, PlanetGridDirection.East) => Create(CubeFace.PositiveX, cell.Level, reversedY, 0, PlanetGridDirection.South),
            (CubeFace.NegativeY, PlanetGridDirection.West) => Create(CubeFace.NegativeX, cell.Level, cell.Y, 0, PlanetGridDirection.South),
            (CubeFace.NegativeY, PlanetGridDirection.North) => Create(CubeFace.PositiveZ, cell.Level, cell.X, 0, PlanetGridDirection.South),
            (CubeFace.NegativeY, PlanetGridDirection.South) => Create(CubeFace.NegativeZ, cell.Level, reversedX, 0, PlanetGridDirection.South),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported surface-grid transition."),
        };
    }

    private static PlanetSurfaceGridNeighbor Create(CubeFace face, int level, int x, int y, PlanetGridDirection returnDirection) =>
        new(new PlanetSurfaceGridCellId(face, level, x, y), returnDirection);
}
