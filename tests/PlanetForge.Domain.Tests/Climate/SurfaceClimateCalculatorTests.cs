using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Physics;

namespace PlanetForge.Domain.Tests.Climate;

[TestClass]
public sealed class SurfaceClimateCalculatorTests
{
    [TestMethod]
    public void Calculate_EarthReference_ReturnsEarthLikeSurfaceTemperature()
    {
        var planet = EarthReference.CreateParameters();
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);

        var result = SurfaceClimateCalculator.Calculate(physics, atmosphere);

        Assert.AreEqual(288.1, result.SurfaceTemperatureKelvin, 1.5);
    }

    [TestMethod]
    public void Calculate_DoubleCarbonDioxide_WarmsSurfaceByAboutThreeKelvin()
    {
        var planet = EarthReference.CreateParameters();
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var baselineAtmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);
        var doubledAtmosphere = AtmosphereCalculator.Calculate(
            EarthAtmosphereReference.CreateParameters() with { CarbonDioxidePartsPerMillion = 560.0 },
            planet,
            physics);
        var baseline = SurfaceClimateCalculator.Calculate(physics, baselineAtmosphere);

        var doubled = SurfaceClimateCalculator.Calculate(physics, doubledAtmosphere);

        Assert.AreEqual(3.0, doubled.SurfaceTemperatureKelvin - baseline.SurfaceTemperatureKelvin, 0.1);
    }
}
