using Microsoft.Extensions.DependencyInjection;
using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Terrain;
using PlanetForge.Infrastructure.Terrain;

namespace PlanetForge.Bootstrap;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlanetForge(this IServiceCollection services)
    {
        services.AddSingleton<IPlanetTerrainNoise, SeededTerrainNoise>();
        services.AddSingleton<PlanetMeshBuilder>();
        services.AddSingleton<PlanetExperience>();
        return services;
    }
}
