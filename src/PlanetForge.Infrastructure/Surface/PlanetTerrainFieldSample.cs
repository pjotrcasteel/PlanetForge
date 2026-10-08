namespace PlanetForge.Infrastructure.Surface;

/// <summary>Canonical spherical generator layers exposed for visual inspection and deterministic tests.</summary>
public readonly record struct PlanetTerrainFieldSample(
    double ContinentalPotential,
    double TectonicUplift,
    double MountainBelt,
    double ElevationMeters);
