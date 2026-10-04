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
        services.AddSingleton<IPlanetElevationSource, ProceduralPlanetElevationSource>();
        services.AddSingleton(PlanetSurfaceLodOptions.Default);
        services.AddSingleton<PlanetSurfaceTileSampler>();
        services.AddSingleton<PlanetSurfaceLodSelector>();
        services.AddSingleton<PlanetLocalSurfacePatchSampler>();
        services.AddSingleton<PlanetSurfaceCellAnalyzer>();
        services.AddSingleton<PlanetHydrologyModelBuilder>();
        services.AddSingleton<PlanetHydrologyFeatureExtractor>();
        services.AddSingleton<PlanetSurfaceMeshBuilder>();
        services.AddSingleton<PlanetSurfaceMeshCache>();
        services.AddSingleton<PlanetLocalSurfaceMeshBuilder>();
        services.AddSingleton<PlanetExperience>();
        services.AddSingleton<FrozenWorldMission>();
        services.AddSingleton<PlanetRun>();
        return services;
    }
}