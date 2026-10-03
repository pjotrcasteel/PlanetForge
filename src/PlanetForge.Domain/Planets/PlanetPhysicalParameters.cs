namespace PlanetForge.Domain.Planets;

public sealed record PlanetPhysicalParameters(
    double RadiusMeters,
    double MassKilograms,
    double RotationPeriodSeconds,
    double OrbitalDistanceMeters,
    double StellarLuminosityWatts,
    double BondAlbedo);
