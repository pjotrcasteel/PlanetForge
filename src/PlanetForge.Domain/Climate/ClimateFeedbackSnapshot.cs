namespace PlanetForge.Domain.Climate;

public sealed record ClimateFeedbackSnapshot(
    double ElapsedYears,
    double TargetSurfaceTemperatureKelvin,
    double CryosphereFraction,
    double BaseBondAlbedo,
    double EffectiveBondAlbedo,
    double IceAlbedoContribution);