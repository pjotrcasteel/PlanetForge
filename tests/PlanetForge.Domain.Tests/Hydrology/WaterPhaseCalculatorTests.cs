using PlanetForge.Domain.Hydrology;

namespace PlanetForge.Domain.Tests.Hydrology;

[TestClass]
public sealed class WaterPhaseCalculatorTests
{
    [TestMethod]
    public void Calculate_EarthLikeConditions_ReturnsMostlyLiquidWater()
    {
        var result = WaterPhaseCalculator.Calculate(EarthWaterReference.CreateParameters(), 288.15, 101_325.0);

        Assert.IsGreaterThan(0.99, result.LiquidFraction);
        Assert.IsTrue(result.LiquidWaterStable);
    }

    [TestMethod]
    public void Calculate_BelowFreezing_ReturnsIce()
    {
        var result = WaterPhaseCalculator.Calculate(EarthWaterReference.CreateParameters(), 250.0, 101_325.0);

        Assert.AreEqual(1.0, result.IceFraction, 0.0001);
        Assert.AreEqual(0.0, result.LiquidFraction, 0.0001);
    }

    [TestMethod]
    public void Calculate_BelowTriplePointPressure_HasNoStableLiquidWater()
    {
        var result = WaterPhaseCalculator.Calculate(EarthWaterReference.CreateParameters(), 280.0, 500.0);

        Assert.AreEqual(0.0, result.LiquidFraction, 0.0001);
        Assert.IsFalse(result.LiquidWaterStable);
    }

    [TestMethod]
    public void Calculate_AboveBoiling_ReturnsVapor()
    {
        var result = WaterPhaseCalculator.Calculate(EarthWaterReference.CreateParameters(), 400.0, 101_325.0);

        Assert.AreEqual(1.0, result.VaporFraction, 0.0001);
    }
}
