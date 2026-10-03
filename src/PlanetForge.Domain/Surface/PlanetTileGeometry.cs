namespace PlanetForge.Domain.Surface;

public static class PlanetTileGeometry
{
    public static PlanetTileBounds CalculateBounds(PlanetTileId id)
    {
        var tilesPerAxis = id.TilesPerAxis;
        var minU = ToFaceCoordinate(id.X, tilesPerAxis);
        var maxU = ToFaceCoordinate(id.X + 1, tilesPerAxis);
        var minV = ToFaceCoordinate(id.Y, tilesPerAxis);
        var maxV = ToFaceCoordinate(id.Y + 1, tilesPerAxis);
        var center = CubedSphereProjection.ToUnitSphere(id.Face, (minU + maxU) * 0.5, (minV + maxV) * 0.5);
        PlanetVector[] corners =
        [
            CubedSphereProjection.ToUnitSphere(id.Face, minU, minV),
            CubedSphereProjection.ToUnitSphere(id.Face, maxU, minV),
            CubedSphereProjection.ToUnitSphere(id.Face, minU, maxV),
            CubedSphereProjection.ToUnitSphere(id.Face, maxU, maxV),
        ];

        var angularRadius = corners.Max(corner => Math.Acos(Math.Clamp(PlanetVector.Dot(center, corner), -1.0, 1.0)));
        return new PlanetTileBounds(center, angularRadius);
    }

    private static double ToFaceCoordinate(int tileCoordinate, int tilesPerAxis) => -1.0 + (2.0 * tileCoordinate / tilesPerAxis);
}
