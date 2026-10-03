namespace PlanetForge.Domain.Hydrology;

public static class EarthWaterReference
{
    public const double TotalHydrosphereMassKilograms = 1.4e21;

    public static WaterParameters CreateParameters() => new(TotalHydrosphereMassKilograms);
}
