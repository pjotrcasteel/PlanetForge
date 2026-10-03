namespace PlanetForge.Domain.Surface;

public readonly record struct PlanetSurfacePoint(PlanetVector Direction, double ElevationMeters)
{
    public PlanetVector WorldPositionMeters(double planetRadiusMeters)
    {
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }

        var distanceFromCenter = planetRadiusMeters + ElevationMeters;
        if (!double.IsFinite(distanceFromCenter) || distanceFromCenter <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(ElevationMeters), ElevationMeters, "Elevation places the surface at or below the planet center.");
        }

        return Direction * distanceFromCenter;
    }
}
