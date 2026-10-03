namespace PlanetForge.Domain.Atmosphere;

public sealed record AtmosphereSnapshot(
    double SurfacePressurePascals,
    double EarthAtmosphereMasses,
    double CarbonDioxideRadiativeForcingWattsPerSquareMeter,
    double OtherGasFraction);
