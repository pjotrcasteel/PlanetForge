namespace PlanetForge.Domain.Climate;

public sealed record ClimateFeedbackState(
    double ElapsedYears,
    double SurfaceTemperatureKelvin,
    double CryosphereFraction,
    double EffectiveBondAlbedo);