using PlanetForge.Domain.Physics;
using PlanetForge.Domain.Planets;

namespace PlanetForge.Domain.Tests.Physics;

[TestClass]
public sealed class PlanetPhysicsCalculatorTests
{
    [TestMethod]
    public void Calculate_EarthReference_ReturnsExpectedPhysicalValues()
    {
        var result = PlanetPhysicsCalculator.Calculate(EarthReference.CreateParameters());

        Assert.AreEqual(9.82, result.SurfaceGravityMetersPerSecondSquared, 0.02);
        Assert.AreEqual(1361.0, result.SolarFluxWattsPerSquareMeter, 2.0);
        Assert.AreEqual(255.0, result.EquilibriumTemperatureKelvin, 1.5);
        Assert.AreEqual(5513.0, result.MeanDensityKilogramsPerCubicMeter, 2.0);
        Assert.AreEqual(11_186.0, result.EscapeVelocityMetersPerSecond, 5.0);
    }

    [TestMethod]
    public void Calculate_DoubleOrbitalDistance_ReducesSolarFluxToQuarter()
    {
        var earth = EarthReference.CreateParameters();
        var baseline = PlanetPhysicsCalculator.Calculate(earth);
        var farther = PlanetPhysicsCalculator.Calculate(earth with { OrbitalDistanceMeters = earth.OrbitalDistanceMeters * 2.0 });

        Assert.AreEqual(baseline.SolarFluxWattsPerSquareMeter / 4.0, farther.SolarFluxWattsPerSquareMeter, 0.0001);
    }

    [TestMethod]
    public void Calculate_LowerAlbedo_IncreasesEquilibriumTemperature()
    {
        var earth = EarthReference.CreateParameters();
        var baseline = PlanetPhysicsCalculator.Calculate(earth);
        var darker = PlanetPhysicsCalculator.Calculate(earth with { BondAlbedo = 0.1 });

        Assert.IsGreaterThan(darker.EquilibriumTemperatureKelvin, baseline.EquilibriumTemperatureKelvin);
    }

    [TestMethod]
    public void Calculate_InvalidRadius_Throws()
    {
        var invalid = EarthReference.CreateParameters() with { RadiusMeters = 0.0 };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetPhysicsCalculator.Calculate(invalid));
    }
}
