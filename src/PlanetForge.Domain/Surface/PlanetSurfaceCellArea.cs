namespace PlanetForge.Domain.Surface;

/// <summary>
/// Exact spherical polygon surface angle for a spherified cubed-sphere cell.
/// Unlike grid-cell counts, this preserves true physical area between cube
/// face interiors, face edges and corners. Calculations use unit sphere.
/// </summary>
public static class PlanetSurfaceCellArea
{
    public static double SolidAngleSteradians(PlanetSurfaceGridCellId cell)
    {
        var count = cell.CellsPerAxis;
        var u0 = -1.0 + 2.0 * cell.X / count;
        var v0 = -1.0 + 2.0 * cell.Y / count;
        var u1 = -1.0 + 2.0 * (cell.X + 1) / count;
        var v1 = -1.0 + 2.0 * (cell.Y + 1) / count;
        var a = CubedSphereProjection.ToUnitSphere(cell.Face, u0, v0);
        var b = CubedSphereProjection.ToUnitSphere(cell.Face, u1, v0);
        var c = CubedSphereProjection.ToUnitSphere(cell.Face, u1, v1);
        var d = CubedSphereProjection.ToUnitSphere(cell.Face, u0, v1);
        return SphericalTriangleArea(a, b, c) + SphericalTriangleArea(a, c, d);
    }

    public static double AreaSquareMeters(PlanetSurfaceGridCellId cell, double radiusMeters)
    {
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Planetary radius must be finite and positive.");
        }

        var area = SolidAngleSteradians(cell) * radiusMeters * radiusMeters;
        if (!double.IsFinite(area) || area <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Cell area must remain finite and positive.");
        }

        return area;
    }

    private static double SphericalTriangleArea(PlanetVector a, PlanetVector b, PlanetVector c)
    {
        var triple = Math.Abs(PlanetVector.Dot(a, PlanetVector.Cross(b, c)));
        var denominator = 1.0 + PlanetVector.Dot(a, b) + PlanetVector.Dot(b, c) + PlanetVector.Dot(c, a);
        return 2.0 * Math.Atan2(triple, denominator);
    }
}
