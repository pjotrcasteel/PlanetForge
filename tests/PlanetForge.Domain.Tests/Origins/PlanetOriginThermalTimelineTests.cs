using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginThermalTimelineTests
{
    private static PlanetRockEnthalpyModel CreateRockModel() =>
        new(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));

    [TestMethod]
    public void AdvanceYears_GeologicalTimeAndStoredEnergyMoveTogetherDuringCooling()
    {
        var start = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(4_400.0),
            CreateRockModel());
        const long geologicalStepYears = 1_000_000;
        var secondsPerStep = geologicalStepYears * PlanetThermalEnergyBudget.JulianYearSeconds;

        var partlyMolten = start.AdvanceYears(geologicalStepYears,
            new PlanetThermalEnergyFlux(0.0, 0.0, 1_300.0 / secondsPerStep));
        var solid = partlyMolten.AdvanceYears(geologicalStepYears,
            new PlanetThermalEnergyFlux(0.0, 0.0, 1_100.0 / secondsPerStep));

        Assert.AreEqual(4_540_000_000L, start.Clock.YearsBeforePresent);
        Assert.AreEqual(4_539_000_000L, partlyMolten.Clock.YearsBeforePresent);
        Assert.AreEqual(4_538_000_000L, solid.Clock.YearsBeforePresent);
        Assert.AreEqual(1.0, start.Rock.MeltFraction, 0.0);
        Assert.AreEqual(0.5, partlyMolten.Rock.MeltFraction, 1e-9);
        Assert.AreEqual(0.0, solid.Rock.MeltFraction, 1e-9);
        Assert.AreEqual(100.0, solid.Rock.TemperatureKelvin, 1e-9);
        Assert.IsLessThan(1e-8, Math.Abs(solid.EnergyBudget.ConservationResidualJoules));
        Assert.AreEqual(4_400.0, start.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceYears_LateImpact_ReheatsPreviouslySolidRockWithoutReversingGeologicalTime()
    {
        var initial = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0),
            CreateRockModel());
        const long elapsedYears = 500_000;
        var durationSeconds = elapsedYears * PlanetThermalEnergyBudget.JulianYearSeconds;
        var impact = new PlanetThermalEnergyFlux(2_400.0 / durationSeconds, 0.0, 0.0);

        var heated = initial.AdvanceYears(elapsedYears, impact);

        Assert.AreEqual(0.0, initial.Rock.MeltFraction, 0.0);
        Assert.AreEqual(1.0, heated.Rock.MeltFraction, 0.0);
        Assert.AreEqual(4_539_500_000L, heated.Clock.YearsBeforePresent);
        Assert.AreEqual(2_400.0, heated.EnergyBudget.CumulativeImpactHeatJoules, 1e-9);
        Assert.AreEqual(0.0, heated.EnergyBudget.ConservationResidualJoules, 1e-9);
    }

    [TestMethod]
    public void AdvanceYears_ZeroYearsWithZeroFlux_IsIdempotent()
    {
        var timeline = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_500.0),
            CreateRockModel());

        var unchanged = timeline.AdvanceYears(0, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0));

        Assert.AreEqual(timeline.Clock, unchanged.Clock);
        Assert.AreEqual(timeline.EnergyBudget.StoredEnergyJoules, unchanged.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(timeline.Rock, unchanged.Rock);
    }

    [TestMethod]
    public void AdvanceYears_ExcessiveCooling_RejectsStepWithoutMutatingPreviousEpoch()
    {
        var timeline = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(1.0),
            CreateRockModel());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            timeline.AdvanceYears(1, new PlanetThermalEnergyFlux(0.0, 0.0, 1.0)));

        Assert.AreEqual(4_540_000_000L, timeline.Clock.YearsBeforePresent);
        Assert.AreEqual(1.0, timeline.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceYears_InvalidClockOrEnergy_RejectsWholeStep()
    {
        var timeline = new PlanetOriginThermalTimeline(
            new PlanetOriginClock(PlanetOriginClock.SolarSystemReferenceAgeYears - 1),
            new PlanetThermalEnergyBudget(500.0),
            CreateRockModel());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            timeline.AdvanceYears(2, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            timeline.AdvanceYears(-1, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0)));
        Assert.AreEqual(1L, timeline.Clock.YearsBeforePresent);
    }

    [TestMethod]
    public void Constructor_NullPhysicsDependencies_RejectsPartialWorldState()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference, null!, CreateRockModel()));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference, new PlanetThermalEnergyBudget(0.0), null!));
    }
}
