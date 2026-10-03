using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;

namespace PlanetForge.Domain.Planets;

public sealed class PlanetState
{
    public PlanetState(
        int seed = 24061984,
        PlanetPhysicalParameters? physicalParameters = null,
        AtmosphereParameters? atmosphereParameters = null,
        WaterParameters? waterParameters = null)
    {
        Seed = seed;
        PhysicalParameters = physicalParameters ?? EarthReference.CreateParameters();
        AtmosphereParameters = atmosphereParameters ?? EarthAtmosphereReference.CreateParameters();
        WaterParameters = waterParameters ?? EarthWaterReference.CreateParameters();
    }

    public int Seed { get; private set; }

    public PlanetPhysicalParameters PhysicalParameters { get; private set; }

    public AtmosphereParameters AtmosphereParameters { get; private set; }

    public WaterParameters WaterParameters { get; private set; }

    public void SetPhysicalParameters(PlanetPhysicalParameters parameters) => PhysicalParameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public void SetAtmosphereParameters(AtmosphereParameters parameters) => AtmosphereParameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public void SetWaterParameters(WaterParameters parameters) => WaterParameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public void Reseed(int seed) => Seed = seed;

    public void ResetEarthReference()
    {
        PhysicalParameters = EarthReference.CreateParameters();
        AtmosphereParameters = EarthAtmosphereReference.CreateParameters();
        WaterParameters = EarthWaterReference.CreateParameters();
    }
}
