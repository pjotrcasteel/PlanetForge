using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;

namespace PlanetForge.Domain.Tests.Climate;

[TestClass]
public sealed class PlanetCryosphereMassBudgetTests
{
    [TestMethod]
    public void Evaluate_EnoughFrozenWater_ForSpecifiedSeaAndLandIce_ReturnsSupportedMassBound()
    {
        var water = CreateWater(180_000.0);
        var seaIce = new PlanetIceCoverageRequirement(100.0, 0.5, 2.0, 900.0);
        var landIce = new PlanetIceCoverageRequirement(25.0, 1.0, 4.0, 800.0);

        var result = PlanetCryosphereMassBudget.Evaluate(water, seaIce, landIce);

        Assert.AreEqual(170_000.0, result.MinimumRequiredMassKilograms, 0.0);
        Assert.AreEqual(180_000.0, result.AvailableIceMassKilograms, 0.0);
        Assert.IsTrue(result.HasEnoughIceMass);
        Assert.AreEqual(0.0, result.MissingIceMassKilograms, 0.0);
        Assert.AreEqual(180_000.0, water.IceMassKilograms, 0.0);
    }

    [TestMethod]
    public void Evaluate_FullCoverageWithNoActualIce_ReportsMassShortfall()
    {
        var water = CreateWater(0.0, liquidMassKilograms: 1_000_000.0);
        var seaIce = new PlanetIceCoverageRequirement(100.0, 1.0, 2.0, 900.0);
        var landIce = new PlanetIceCoverageRequirement(25.0, 1.0, 4.0, 800.0);

        var result = PlanetCryosphereMassBudget.Evaluate(water, seaIce, landIce);

        Assert.IsFalse(result.HasEnoughIceMass,
            "Snowball coverage cannot be justified solely by a feedback fraction if all actual H2O remains liquid.");
        Assert.AreEqual(260_000.0, result.MissingIceMassKilograms, 0.0);
        Assert.AreEqual(1_000_000.0, water.LiquidMassKilograms, 0.0);
    }

    [TestMethod]
    public void Evaluate_PartialCoverage_UsesPhysicalAreaRatherThanGridCellCount()
    {
        var water = CreateWater(46_000.0);
        var seaIce = new PlanetIceCoverageRequirement(100.0, 0.25, 2.0, 920.0);
        var noLandIce = new PlanetIceCoverageRequirement(50.0, 0.0, 0.0, 900.0);

        var result = PlanetCryosphereMassBudget.Evaluate(water, seaIce, noLandIce);

        Assert.IsTrue(result.HasEnoughIceMass);
        Assert.AreEqual(46_000.0, result.MinimumRequiredMassKilograms, 1e-8);
    }

    [TestMethod]
    public void Evaluate_DryWorldWithoutCoverage_NeedsNoIce()
    {
        var water = CreateWater(0.0);
        var seaIce = new PlanetIceCoverageRequirement(0.0, 0.0, 0.0, 917.0);
        var landIce = new PlanetIceCoverageRequirement(100.0, 0.0, 0.0, 917.0);

        var result = PlanetCryosphereMassBudget.Evaluate(water, seaIce, landIce);

        Assert.IsTrue(result.HasEnoughIceMass);
        Assert.AreEqual(0.0, result.MinimumRequiredMassKilograms, 0.0);
    }

    [TestMethod]
    public void Evaluate_InsufficientIceMass_MustNotBorrowFromLiquidOrVapor()
    {
        var water = CreateWater(10.0, liquidMassKilograms: 100_000.0, vaporMassKilograms: 100_000.0);
        var seaIce = new PlanetIceCoverageRequirement(100.0, 1.0, 1.0, 900.0);
        var landIce = new PlanetIceCoverageRequirement(0.0, 0.0, 0.0, 900.0);

        var result = PlanetCryosphereMassBudget.Evaluate(water, seaIce, landIce);

        Assert.IsFalse(result.HasEnoughIceMass);
        Assert.AreEqual(89_990.0, result.MissingIceMassKilograms, 0.0);
        Assert.AreEqual(10.0, water.IceMassKilograms, 0.0);
    }

    [TestMethod]
    public void Evaluate_UnsupportedCoverageParameters_RejectsFalseIceClaims()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetIceCoverageRequirement(100.0, 1.01, 1.0, 917.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetIceCoverageRequirement(100.0, double.NaN, 1.0, 917.0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PlanetIceCoverageRequirement(100.0, 1.0, 0.0, 917.0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new PlanetIceCoverageRequirement(0.0, 1.0, 2.0, 917.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetIceCoverageRequirement(100.0, 0.5, 2.0, -1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetIceCoverageRequirement(double.MaxValue, 1.0, 1e200, 917.0));
    }

    private static WaterPhaseSnapshot CreateWater(double iceMassKilograms, double liquidMassKilograms = 0.0, double vaporMassKilograms = 0.0)
    {
        var total = iceMassKilograms + liquidMassKilograms + vaporMassKilograms;
        return new WaterPhaseSnapshot(iceMassKilograms, liquidMassKilograms, vaporMassKilograms,
            total > 0.0 ? iceMassKilograms / total : 0.0,
            total > 0.0 ? liquidMassKilograms / total : 0.0,
            total > 0.0 ? vaporMassKilograms / total : 0.0, 373.15, liquidMassKilograms > 0.0);
    }
}
