namespace PlanetForge.Domain.Atmosphere;

public static class EarthAtmosphereReference
{
    public const double TotalMassKilograms = 5.148e18;
    public const double NitrogenFraction = 0.7808;
    public const double OxygenFraction = 0.2095;
    public const double PreindustrialCarbonDioxidePartsPerMillion = 280.0;
    public const double CarbonDioxideForcingCoefficientWattsPerSquareMeter = 5.35;
    public const double ReferenceGreenhouseWarmingKelvin = 33.0;
    public const double ClimateResponseKelvinPerWattPerSquareMeter = 0.8;

    public static AtmosphereParameters CreateParameters() => new(
        TotalMassKilograms,
        NitrogenFraction,
        OxygenFraction,
        PreindustrialCarbonDioxidePartsPerMillion);
}
