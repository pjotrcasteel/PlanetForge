using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Rendering;

public sealed record PlanetRenderSnapshot(
    IReadOnlyList<PlanetSurfaceTileMesh> SurfaceTiles,
    double SeaLevelMeters,
    double AtmosphereDensity,
    int Seed,
    PlanetPhysicalParameters PhysicalParameters,
    PlanetPhysicsSnapshot Physics,
    AtmosphereParameters AtmosphereParameters,
    AtmosphereSnapshot Atmosphere,
    ClimateSnapshot Climate,
    ClimateFeedbackSnapshot ClimateFeedback,
    WaterParameters WaterParameters,
    WaterPhaseSnapshot Water,
    PlanetLocalSurfaceMesh? LocalSurface = null,
    int TerrainRevision = 0,
    PlanetOriginRenderState? Origin = null);

/// <summary>A render-only view of the single published Earth-origin epoch; not a second simulation store.</summary>
public sealed record PlanetOriginRenderState(
    long Revision,
    long YearsBeforePresent,
    double RockTemperatureKelvin,
    double MeltFraction,
    double? AtmosphereTemperatureKelvin,
    double? SurfaceRockTemperatureKelvin,
    double AtmosphericVaporKilograms,
    double SurfaceLiquidKilograms,
    double SurfaceIceKilograms);