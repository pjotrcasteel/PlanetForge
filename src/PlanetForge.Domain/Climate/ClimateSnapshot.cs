namespace PlanetForge.Domain.Climate;

public sealed record ClimateSnapshot(
    double SurfaceTemperatureKelvin,
    double BackgroundGreenhouseWarmingKelvin,
    double CarbonDioxideWarmingKelvin);
