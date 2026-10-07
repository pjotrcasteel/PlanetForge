namespace PlanetForge.Application.Runs;

public sealed record PlanetWaterPathSegment(
    double FromX,
    double FromY,
    double FromZ,
    double ToX,
    double ToY,
    double ToZ,
    double RelativeDischarge,
    int StreamOrder,
    double MeanDischargeCubicMetersPerSecond = 0.0);
