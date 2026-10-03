namespace PlanetForge.Domain.Planets;

public sealed record PlanetPhysicsSnapshot(
    double SurfaceGravityMetersPerSecondSquared,
    double SolarFluxWattsPerSquareMeter,
    double EquilibriumTemperatureKelvin,
    double MeanDensityKilogramsPerCubicMeter,
    double EscapeVelocityMetersPerSecond);
