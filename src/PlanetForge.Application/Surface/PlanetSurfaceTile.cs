using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed record PlanetSurfaceTile(PlanetTileId Id, int CellsPerAxis, IReadOnlyList<PlanetSurfacePoint> Points)
{
    public int PointsPerAxis => CellsPerAxis + 1;

    public PlanetSurfacePoint GetPoint(int x, int y)
    {
        if (x < 0 || x >= PointsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= PointsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return Points[(y * PointsPerAxis) + x];
    }
}
