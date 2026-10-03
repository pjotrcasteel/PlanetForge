using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Rendering;

public sealed record PlanetRenderSnapshot(
    IReadOnlyList<PlanetSurfaceTileMesh> SurfaceTiles,
    double SeaLevel,
    double AtmosphereDensity,
    int Seed,
    PlanetPhysicalParameters PhysicalParameters,
    PlanetPhysicsSnapshot Physics,
    AtmosphereParameters AtmosphereParameters,
    AtmosphereSnapshot Atmosphere,
    SurfaceClimateSnapshot Climate,
    WaterParameters WaterParameters,
    WaterPhaseSnapshot Water);
