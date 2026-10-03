using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Planets;

public sealed record PlanetScenarioParameters
{
    public required PlanetPhysicalParameters PhysicalParameters { get; init; }

    public required AtmosphereParameters AtmosphereParameters { get; init; }

    public required WaterParameters WaterParameters { get; init; }

    public double ClimateSpinUpYears { get; init; }
}