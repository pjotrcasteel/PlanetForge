using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Surface;

public sealed class PlanetSurfaceTileSampler(IPlanetElevationSource elevationSource)
{
    public PlanetSurfaceTile Sample(PlanetTileId id, int cellsPerAxis, int seed)
    {
        if (cellsPerAxis < 1 || cellsPerAxis > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(cellsPerAxis), cellsPerAxis, "Cells per axis must be between 1 and 256.");
        }

        var pointsPerAxis = cellsPerAxis + 1;
        var points = new PlanetSurfacePoint[pointsPerAxis * pointsPerAxis];
        var globalCellsPerAxis = checked(id.TilesPerAxis * cellsPerAxis);

        for (var y = 0; y < pointsPerAxis; y++)
        {
            var globalY = checked((id.Y * cellsPerAxis) + y);
            var v = ToFaceCoordinate(globalY, globalCellsPerAxis);

            for (var x = 0; x < pointsPerAxis; x++)
            {
                var globalX = checked((id.X * cellsPerAxis) + x);
                var u = ToFaceCoordinate(globalX, globalCellsPerAxis);
                var direction = CubedSphereProjection.ToUnitSphere(id.Face, u, v);
                var elevation = elevationSource.Sample(direction, seed);
                points[(y * pointsPerAxis) + x] = new PlanetSurfacePoint(direction, elevation);
            }
        }

        return new PlanetSurfaceTile(id, cellsPerAxis, points);
    }

    private static double ToFaceCoordinate(int gridCoordinate, int cellsPerAxis) => -1.0 + (2.0 * gridCoordinate / cellsPerAxis);
}
