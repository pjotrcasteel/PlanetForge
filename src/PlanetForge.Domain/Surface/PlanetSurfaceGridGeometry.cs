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
}
