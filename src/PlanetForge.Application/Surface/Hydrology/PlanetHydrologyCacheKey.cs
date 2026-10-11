namespace PlanetForge.Application.Surface.Hydrology;

/// <summary>
/// Identifies the physical terrain and sea level used to calculate a drainage network.
/// Climate-driven runoff can change without invalidating the underlying topology.
/// This is runtime cache identity, not part of the save schema.
/// </summary>
public readonly record struct PlanetHydrologyCacheKey(
    int GenerationVersion,
    int Seed,
    double PlanetRadiusMeters,
    double SeaLevelMeters,
    long GeologicalRevision,
    int TerrainDeformationRevision);
