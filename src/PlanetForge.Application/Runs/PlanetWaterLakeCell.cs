namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterLakeCell(
    double X,
    double Y,
    double Z,
    double AngularRadiusRadians)
{
    public double SurfaceElevationMeters { get; init; }
}
