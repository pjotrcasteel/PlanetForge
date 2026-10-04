using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Application.Planets;

public sealed record PlanetExperienceState(
    int Seed,
    PlanetPhysicalParameters PhysicalParameters,
    AtmosphereParameters AtmosphereParameters,
    WaterParameters WaterParameters,
    ClimateFeedbackState ClimateState);