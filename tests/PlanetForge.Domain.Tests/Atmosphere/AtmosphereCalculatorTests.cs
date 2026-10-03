using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Tests.Atmosphere;

[TestClass]
public sealed class AtmosphereCalculatorTests
{
    [TestMethod]
    public void Calculate_EarthReference_ReturnsEarthLikeSurfacePressure()
    {
        var planet = EarthReference.CreateParameters();
        var physics = PlanetPhysicsCalculator.Calculate(planet);

        var result = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);

        Assert.AreEqual(99_100.0, result.SurfacePressurePascals, 500.0);
        Assert.AreEqual(1.0, result.EarthAtmosphereMasses, 0.0001);
    }

    [TestMethod]
    public void Calculate_DoubleCarbonDioxide_ReturnsLogarithmicForcing()
    {
        var planet = EarthReference.CreateParameters();
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = EarthAtmosphereReference.CreateParameters() with { CarbonDioxidePartsPerMillion = 560.0 };

        var result = AtmosphereCalculator.Calculate(atmosphere, planet, physics);

        Assert.AreEqual(3.71, result.CarbonDioxideRadiativeForcingWattsPerSquareMeter, 0.02);
    }

    [TestMethod]
    public void Calculate_ZeroAtmosphericMass_ReturnsZeroPressure()
    {
        var planet = EarthReference.CreateParameters();
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = EarthAtmosphereReference.CreateParameters() with { MassKilograms = 0.0 };

        var result = AtmosphereCalculator.Calculate(atmosphere, planet, physics);

        Assert.AreEqual(0.0, result.SurfacePressurePascals);
        Assert.AreEqual(0.0, result.CarbonDioxideRadiativeForcingWattsPerSquareMeter);
    }
}
