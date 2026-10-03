using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Physics;

public static class EarthReference
{
    public const double MeanRadiusMeters = 6_371_000.0;
    public const double MassKilograms = 5.9722e24;
    public const double RotationPeriodSeconds = 86_164.1;
    public const double BondAlbedo = 0.294;

    public static PlanetPhysicalParameters CreateParameters() => new(
        MeanRadiusMeters,
        MassKilograms,
        RotationPeriodSeconds,
        PhysicalConstants.AstronomicalUnitMeters,
        PhysicalConstants.NominalSolarLuminosityWatts,
        BondAlbedo);
}
