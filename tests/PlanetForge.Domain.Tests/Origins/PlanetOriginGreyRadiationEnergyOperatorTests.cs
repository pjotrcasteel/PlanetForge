using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginGreyRadiationEnergyOperatorTests
{
    [TestMethod]
    public void Propose_TransparentAtmosphere_DebitsRockAndOnlyCreditsSpace()
    {
        var start = CreateEnergy();
        var trial = Propose(start, 0.0);

        Assert.AreEqual(trial.Flux.SurfaceEmittedPowerWatts, trial.OutgoingInfraredEnergyJoules, 1e-6);
        Assert.AreEqual(3.0e6, trial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(80.0e6 - trial.OutgoingInfraredEnergyJoules,
            trial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 1e-6);
        AssertConserved(start, trial);
    }

    [TestMethod]
    public void Propose_AbsorbingAtmosphere_CreditsAbsorbedRockInfraredAndConservesEnergy()
    {
        var start = CreateEnergy();
        var trial = Propose(start, 1.0);

        Assert.IsGreaterThan(0.0, trial.Flux.AtmosphereAbsorbedSurfacePowerWatts);
        Assert.IsGreaterThan(3.0e6, trial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere));
        Assert.IsLessThan(80.0e6, trial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock));
        Assert.IsLessThan(trial.Flux.SurfaceEmittedPowerWatts, trial.Flux.TotalOutgoingToSpaceWatts);
        AssertConserved(start, trial);
    }

    [TestMethod]
    public void Propose_HotGas_WarmsSurfaceWhilePlanetStillLosesEnergyToSpace()
    {
        var start = CreateEnergy(surfaceKelvin: 300.0, atmosphereKelvin: 800.0);
        var trial = Propose(start, 10.0);

        Assert.IsGreaterThan(0.0, trial.Flux.TotalOutgoingToSpaceWatts);
        Assert.IsLessThan(0.0, trial.Flux.SurfaceNetCoolingWatts);
        Assert.IsGreaterThan(30.0e6,
            trial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock));
        Assert.IsLessThan(8.0e6,
            trial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere));
        AssertConserved(start, trial);
    }

    [TestMethod]
    public void Propose_RadiativeOverdraw_RejectsWithoutChangingConservedWorld()
    {
        var start = CreateEnergy();
        var initialRockEnergy = start.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock);
        var initialAtmosphereEnergy = start.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetOriginGreyRadiationEnergyOperator.Propose(
                start, PlanetOriginDuration.FromSeconds(500.0), 1.0, 0.0));

        Assert.AreEqual(initialRockEnergy,
            start.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 0.0);
        Assert.AreEqual(initialAtmosphereEnergy,
            start.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(0.0, start.ConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Propose_EmptyAtmosphereOrRock_RejectsGhostEnergyStore()
    {
        var emptyAtmosphere = CreateEnergy(dryGasKilograms: 0.0);
        var emptyRock = CreateEnergy(surfaceRockMassKilograms: 0.0);

        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(emptyAtmosphere, 1.0));
        Assert.ThrowsExactly<InvalidOperationException>(() => Propose(emptyRock, 1.0));
    }

    [TestMethod]
    public void Propose_InvalidOpticalDepthOrDuration_RejectsUnphysicalTrial()
    {
        var start = CreateEnergy();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiationEnergyOperator.Propose(start, new PlanetOriginDuration(0), 1.0, 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiationEnergyOperator.Propose(start, PlanetOriginDuration.FromSeconds(1), 1.0, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetOriginGreyRadiationEnergyOperator.Propose(start, PlanetOriginDuration.FromSeconds(1), -1.0, 1.0));
    }

    private static PlanetOriginGreyRadiationTrial Propose(PlanetOriginEnergyLedger energy, double opticalDepth) =>
        PlanetOriginGreyRadiationEnergyOperator.Propose(
            energy, PlanetOriginDuration.FromSeconds(1.0), 1.0, opticalDepth);

    private static void AssertConserved(PlanetOriginEnergyLedger original, PlanetOriginGreyRadiationTrial trial)
    {
        var originalBudget = original.Environment.Environment;
        var afterBudget = trial.Environment;
        var expected = originalBudget.TotalEnergyJoules - trial.OutgoingInfraredEnergyJoules;

        Assert.AreEqual(expected, afterBudget.TotalEnergyJoules, 1e-6,
            "Internal surface-atmosphere exchange must not create or destroy planetary heat.");
        Assert.AreEqual(originalBudget.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean),
            afterBudget.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(originalBudget.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce),
            afterBudget.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(0.0, original.Environment.Water.ConservationResidualKilograms, 0.0);
        Assert.AreEqual(0.0, original.ConservationResidualJoules, 0.0);
        Assert.AreEqual(0L, original.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(0.0, original.Environment.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.IsFalse(ReferenceEquals(original.Environment.Environment, trial.Environment),
            "A radiation trial must create a new immutable heat budget, not mutate the published one.");
    }

    private static PlanetOriginEnergyLedger CreateEnergy(
        double surfaceKelvin = 800.0, double atmosphereKelvin = 300.0,
        double dryGasKilograms = 10.0, double surfaceRockMassKilograms = 100.0)
    {
        var rockModel = new PlanetRockEnthalpyModel(
            new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0), rockModel);
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            dryGasKilograms, 1_000.0, 2_000.0, 4_000.0, surfaceRockMassKilograms, 1_000.0);
        var environmental = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(
                atmosphereKelvin * dryGasKilograms * 1_000.0,
                0.0,
                surfaceKelvin * surfaceRockMassKilograms * 1_000.0));
        return new PlanetOriginEnergyLedger(interior, environmental, capacities);
    }
}
