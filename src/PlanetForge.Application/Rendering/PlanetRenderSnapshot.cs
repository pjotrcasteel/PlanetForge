using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Rendering;

public sealed record PlanetRenderSnapshot(
    PlanetMesh Mesh,
    double SeaLevel,
    double AtmosphereDensity,
    int Seed,
    PlanetPhysicalParameters PhysicalParameters,
    PlanetPhysicsSnapshot Physics,
    AtmosphereParameters AtmosphereParameters,
    AtmosphereSnapshot Atmosphere,
    ClimateSnapshot Climate,
    WaterParameters WaterParameters,
    WaterPhaseSnapshot Water);
