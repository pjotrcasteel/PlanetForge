namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetTileId(CubeFace Face, int Level, int X, int Y)
{
    public int TilesPerAxis => 1 << Level;

    public PlanetTileId
    {
        if (Level < 0 || Level > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(Level), Level, "Level must be between 0 and 30.");
        }

        var tilesPerAxis = 1 << Level;
        if (X < 0 || X >= tilesPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(X), X, "X must be inside the tile grid for the selected level.");
        }

        if (Y < 0 || Y >= tilesPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(Y), Y, "Y must be inside the tile grid for the selected level.");
        }
    }

    public IReadOnlyList<PlanetTileId> Children()
    {
        var childLevel = Level + 1;
        var childX = X * 2;
        var childY = Y * 2;
        return
        [
            new PlanetTileId(Face, childLevel, childX, childY),
            new PlanetTileId(Face, childLevel, childX + 1, childY),
            new PlanetTileId(Face, childLevel, childX, childY + 1),
            new PlanetTileId(Face, childLevel, childX + 1, childY + 1),
        ];
    }
}
