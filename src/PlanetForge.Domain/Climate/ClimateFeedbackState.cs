namespace PlanetForge.Domain.Climate;

public sealed record ClimateFeedbackState(
    double ElapsedYears,
    double SurfaceTemperatureKelvin,
    double CryosphereFraction,
    double EffectiveBondAlbedo)
{
    public double SeaIceFraction { get; init; } = CryosphereFraction;

    public double LandIceFraction { get; init; } = CryosphereFraction;

    public double SnowCoverFraction { get; init; } = CryosphereFraction;
}