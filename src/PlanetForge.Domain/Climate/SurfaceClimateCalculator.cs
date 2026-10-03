using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Climate;

public static class SurfaceClimateCalculator
{
    private const double MinimumTemperatureKelvin = 2.7;

    public static ClimateSnapshot Calculate(PlanetPhysicsSnapshot physics, AtmosphereSnapshot atmosphere)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(atmosphere);

        var atmosphereScale = Math.Sqrt(Math.Clamp(atmosphere.EarthAtmosphereMasses, 0.0, 16.0));
        var backgroundGreenhouse = EarthAtmosphereReference.ReferenceGreenhouseWarmingKelvin * atmosphereScale;
        var carbonDioxideWarming = EarthAtmosphereReference.ClimateResponseKelvinPerWattPerSquareMeter
            * atmosphere.CarbonDioxideRadiativeForcingWattsPerSquareMeter;
        var surfaceTemperature = Math.Max(
            MinimumTemperatureKelvin,
            physics.EquilibriumTemperatureKelvin + backgroundGreenhouse + carbonDioxideWarming);

        return new ClimateSnapshot(surfaceTemperature, backgroundGreenhouse, carbonDioxideWarming);
    }
}
