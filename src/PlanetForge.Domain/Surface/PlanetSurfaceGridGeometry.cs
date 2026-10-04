namespace PlanetForge.Domain.Surface;

public static class PlanetSurfaceGridGeometry
{
    public static PlanetVector GetCenterDirection(PlanetSurfaceGridCellId id)
    {
        var cellsPerAxis = id.CellsPerAxis;
        var u = -1.0 + (2.0 * (id.X + 0.5) / cellsPerAxis);
        var v = -1.0 + (2.0 * (id.Y + 0.5) / cellsPerAxis);
        return CubedSphereProjection.ToUnitSphere(id.Face, u, v);
    }

    public static IReadOnlyList<PlanetVector> GetCornerDirections(PlanetSurfaceGridCellId id)
    {
        var cellsPerAxis = id.CellsPerAxis;
        var u0 = -1.0 + (2.0 * id.X / cellsPerAxis);
        var u1 = -1.0 + (2.0 * (id.X + 1.0) / cellsPerAxis);
        var v0 = -1.0 + (2.0 * id.Y / cellsPerAxis);
        var v1 = -1.0 + (2.0 * (id.Y + 1.0) / cellsPerAxis);
        return
        [
            CubedSphereProjection.ToUnitSphere(id.Face, u0, v0),
            CubedSphereProjection.ToUnitSphere(id.Face, u1, v0),
            CubedSphereProjection.ToUnitSphere(id.Face, u1, v1),
            CubedSphereProjection.ToUnitSphere(id.Face, u0, v1),
        ];
    }
}
