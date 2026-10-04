namespace PlanetForge.Domain.Climate;

public sealed record ClimateFeedbackSnapshot(
    double ElapsedYears,
    double TargetSurfaceTemperatureKelvin,
    double CryosphereFraction,
    double BaseBondAlbedo,
    double EffectiveBondAlbedo,
    double IceAlbedoContribution)
{
    public double SeaIceFraction { get; init; } = CryosphereFraction;

    public double LandIceFraction { get; init; } = CryosphereFraction;

    public double SnowCoverFraction { get; init; } = CryosphereFraction;
}