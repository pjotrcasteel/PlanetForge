using Microsoft.Extensions.DependencyInjection;
using PlanetForge.Application.Missions;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Runs;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Bootstrap;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlanetForge(this IServiceCollection services)
    {
        services.AddSingleton<IPlanetTerrainDeformationStore, PlanetTerrainDeformationStore>();
        services.AddSingleton<ProceduralPlanetElevationSource>();
        // Both orbital cube-sphere tiles and the ground sampler resolve the
        // same geological planet. Research regions are opt-in publications;
        // until one is published this forwards the original canonical source.
        services.AddSingleton<PlanetTerrainRegionProvider>(provider =>
            new PlanetTerrainRegionProvider(provider.GetRequiredService<ProceduralPlanetElevationSource>()));
        services.AddSingleton<IPlanetElevationSource>(provider => provider.GetRequiredService<PlanetTerrainRegionProvider>());
        services.AddSingleton(PlanetSurfaceLodOptions.Default);
        services.AddSingleton<PlanetSurfaceTileSampler>();
        services.AddSingleton<PlanetSurfaceLodSelector>();
        services.AddSingleton<PlanetLocalSurfacePatchSampler>();
        services.AddSingleton<PlanetSurfaceCellAnalyzer>();
        services.AddSingleton<PlanetHydrologyModelBuilder>();
        services.AddSingleton<PlanetRunoffModel>();
        services.AddSingleton<PlanetCryosphereRunoffModel>();
        services.AddSingleton<PlanetHydrologyFeatureExtractor>();
        services.AddSingleton<PlanetRiverGeomorphologyModel>();
        services.AddSingleton<PlanetRiverSedimentModel>();
        services.AddSingleton<PlanetSurfaceMeshBuilder>();
        services.AddSingleton<PlanetSurfaceMeshCache>();
        services.AddSingleton<PlanetLocalSurfaceMeshBuilder>();
        services.AddSingleton<PlanetExperience>();
        services.AddSingleton<FrozenWorldMission>();
        services.AddSingleton<PlanetRun>();
        return services;
    }
}