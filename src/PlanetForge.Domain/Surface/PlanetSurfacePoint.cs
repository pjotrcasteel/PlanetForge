namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetSurfacePoint(PlanetVector Direction, double ElevationNormalized)
{
    public PlanetVector Position(double elevationScale) => Direction * (1.0 + (ElevationNormalized * elevationScale));
}
