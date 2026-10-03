namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetLocalSurfacePoint(
    PlanetVector Direction,
    double ElevationMeters,
    PlanetLocalPosition LocalPosition);
