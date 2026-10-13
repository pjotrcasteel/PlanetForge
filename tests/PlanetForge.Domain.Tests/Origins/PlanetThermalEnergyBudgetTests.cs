using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetThermalEnergyBudgetTests
{
    [TestMethod]
    public void AdvanceSeconds_RepeatedSubUlpHeatLoss_RetainsDebitsInFiniteMantle()
    {
        const double stored = 1e20;
        var initial = new PlanetThermalEnergyBudget(stored);
        var candidate = initial;
        for (var i = 0; i < 10_000; i++) candidate = candidate.AdvanceSeconds(1.0, new(0.0, 0.0, 1.0));
        Assert.AreEqual(stored - 10_000.0, candidate.StoredEnergyJoules, 0.0);
        Assert.AreEqual(10_000.0, candidate.CumulativeSurfaceHeatLossJoules, 0.0);
        Assert.AreEqual(stored, initial.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_ImpactRadiogenicHeatingAndSurfaceLoss_ConservesJoules()
    {
        var initial = new PlanetThermalEnergyBudget(1_000.0);
        var oneSecond = 1.0 / PlanetThermalEnergyBudget.JulianYearSeconds;

        var evolved = initial.Advance(oneSecond, new PlanetThermalEnergyFlux(100.0, 40.0, 30.0));

        Assert.AreEqual(1_110.0, evolved.StoredEnergyJoules, 1e-9);
        Assert.AreEqual(100.0, evolved.CumulativeImpactHeatJoules, 1e-9);
        Assert.AreEqual(40.0, evolved.CumulativeRadiogenicHeatJoules, 1e-9);
        Assert.AreEqual(30.0, evolved.CumulativeSurfaceHeatLossJoules, 1e-9);
        Assert.AreEqual(0.0, evolved.ConservationResidualJoules, 1e-9);
        Assert.AreEqual(1_000.0, initial.StoredEnergyJoules, 0.0,
            "A thermal advancement must not mutate the previous geological snapshot.");
    }

    [TestMethod]
    public void Advance_NoHeatSources_CoolsWithoutCreatingNewInternalEnergy()
    {
        var initial = new PlanetThermalEnergyBudget(1.0e20);
        var cooling = new PlanetThermalEnergyFlux(0.0, 0.0, 1.0e10);

        var evolved = initial.Advance(100.0, cooling);

        var loss = 100.0 * PlanetThermalEnergyBudget.JulianYearSeconds * cooling.SurfaceHeatLossWatts;
        Assert.AreEqual(1.0e20 - loss, evolved.StoredEnergyJoules, 1e6);
        Assert.AreEqual(0.0, evolved.CumulativeImpactHeatJoules, 0.0);
        Assert.AreEqual(0.0, evolved.CumulativeRadiogenicHeatJoules, 0.0);
        Assert.AreEqual(loss, evolved.CumulativeSurfaceHeatLossJoules, 1e6);
        Assert.IsLessThan(1.0e20 * 1e-12, Math.Abs(evolved.ConservationResidualJoules));
    }

    [TestMethod]
    public void Advance_TwoHalfSteps_MatchesOneWholeStep()
    {
        var starting = new PlanetThermalEnergyBudget(1_000_000.0);
        var flux = new PlanetThermalEnergyFlux(100.0, 20.0, 60.0);
        var halfSecond = 0.5 / PlanetThermalEnergyBudget.JulianYearSeconds;

        var halfSteps = starting.Advance(halfSecond, flux).Advance(halfSecond, flux);
        var fullStep = starting.Advance(halfSecond * 2.0, flux);

        Assert.AreEqual(fullStep.StoredEnergyJoules, halfSteps.StoredEnergyJoules, 1e-8);
        Assert.AreEqual(fullStep.CumulativeImpactHeatJoules, halfSteps.CumulativeImpactHeatJoules, 1e-8);
        Assert.AreEqual(fullStep.CumulativeSurfaceHeatLossJoules, halfSteps.CumulativeSurfaceHeatLossJoules, 1e-8);
    }

    [TestMethod]
    public void Advance_ExcessiveRadiativeLoss_IsRejectedInsteadOfCreatingNegativeEnergy()
    {
        var budget = new PlanetThermalEnergyBudget(10.0);
        var oneSecond = 1.0 / PlanetThermalEnergyBudget.JulianYearSeconds;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            budget.Advance(oneSecond, new PlanetThermalEnergyFlux(0.0, 0.0, 20.0)));
        Assert.AreEqual(10.0, budget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_ZeroDuration_LeavesAllEnergyAccountsUnchanged()
    {
        var initial = new PlanetThermalEnergyBudget(200.0);

        var evolved = initial.Advance(0.0, new PlanetThermalEnergyFlux(500.0, 400.0, 900.0));

        Assert.AreEqual(200.0, evolved.StoredEnergyJoules, 0.0);
        Assert.AreEqual(0.0, evolved.CumulativeImpactHeatJoules, 0.0);
        Assert.AreEqual(0.0, evolved.CumulativeSurfaceHeatLossJoules, 0.0);
    }

    [TestMethod]
    public void Advance_InvalidDurationOrPower_IsRejected()
    {
        var budget = new PlanetThermalEnergyBudget(500.0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetThermalEnergyBudget(-1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetThermalEnergyBudget(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.Advance(-1.0, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.Advance(1.0, new PlanetThermalEnergyFlux(-1.0, 0.0, 0.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.Advance(1.0, new PlanetThermalEnergyFlux(0.0, double.NaN, 0.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.Advance(double.PositiveInfinity, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0)));
    }

    [TestMethod]
    public void Advance_OverlargePhysicalInputs_AreRejectedInsteadOfProducingInfinity()
    {
        var budget = new PlanetThermalEnergyBudget(1.0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.Advance(1.0e250, new PlanetThermalEnergyFlux(1.0e250, 0.0, 0.0)));
    }
}
