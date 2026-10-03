namespace PlanetForge.Domain.Hydrology;

public sealed record WaterPhaseSnapshot(
    double IceMassKilograms,
    double LiquidMassKilograms,
    double VaporMassKilograms,
    double IceFraction,
    double LiquidFraction,
    double VaporFraction,
    double BoilingPointKelvin,
    bool LiquidWaterStable);
