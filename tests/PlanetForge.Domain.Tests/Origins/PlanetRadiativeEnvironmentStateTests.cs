using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetRadiativeEnvironmentStateTests
{
    [TestMethod]
    public void Advance_AbsorbedStarlightAndInfraredLoss_ConserveExternalEnergyLedger()
    {
        var initial = new PlanetRadiativeEnvironmentState(
            new PlanetEnvironmentalEnergyBudget(1_000.0, 200.0, 500.0));
        var boundary = ConstantBoundary(absorbedWatts: 40.0, emittedWatts: 30.0);

        var evolved = initial.Advance(boundary, 10.0,
            PlanetEnvironmentHeatReservoir.SurfaceRock,
            PlanetEnvironmentHeatReservoir.Atmosphere);

        Assert.AreEqual(700.0, evolved.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(200.0, evolved.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(900.0, evolved.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 0.0);
        Assert.AreEqual(400.0, evolved.CumulativeAbsorbedStellarEnergyJoules, 0.0);
        Assert.AreEqual(300.0, evolved.CumulativeEmittedThermalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, evolved.ConservationResidualJoules, 0.0);
        Assert.AreEqual(1_000.0, initial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(0.0, initial.CumulativeEmittedThermalEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_IceStoreEnergy_IsPreservedOrRadiatedButNeverDropped()
    {
        var initial = new PlanetRadiativeEnvironmentState(
            new PlanetEnvironmentalEnergyBudget(1_000.0, 200.0, 500.0, 300.0));
        var boundary = ConstantBoundary(absorbedWatts: 10.0, emittedWatts: 20.0);

        var unrelated = initial.Advance(boundary, 5.0,
            PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere);
        var iceEmits = initial.Advance(boundary, 5.0,
            PlanetEnvironmentHeatReservoir.SurfaceIce, PlanetEnvironmentHeatReservoir.SurfaceIce);

        Assert.AreEqual(300.0, unrelated.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(0.0, unrelated.ConservationResidualJoules, 0.0);
        Assert.AreEqual(250.0, iceEmits.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(0.0, iceEmits.ConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Advance_OneIntervalAndTwoHalfIntervals_AgreeForConstantBoundary()
    {
        var boundary = ConstantBoundary(absorbedWatts: 15.0, emittedWatts: 10.0);
        var initial = new PlanetRadiativeEnvironmentState(new PlanetEnvironmentalEnergyBudget(300.0, 0.0, 500.0));

        var whole = initial.Advance(boundary, 4.0,
            PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere);
        var halves = initial.Advance(boundary, 2.0,
            PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere)
            .Advance(boundary, 2.0,
                PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere);

        Assert.AreEqual(whole.Environment.TotalEnergyJoules, halves.Environment.TotalEnergyJoules, 0.0);
        Assert.AreEqual(whole.CumulativeAbsorbedStellarEnergyJoules, halves.CumulativeAbsorbedStellarEnergyJoules, 0.0);
        Assert.AreEqual(whole.CumulativeEmittedThermalEnergyJoules, halves.CumulativeEmittedThermalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, halves.ConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Advance_SameEmittingAndAbsorbingStore_AppliesNetExternalRadiationExactlyOnce()
    {
        var initial = new PlanetRadiativeEnvironmentState(new PlanetEnvironmentalEnergyBudget(50.0, 0.0, 0.0));

        var result = initial.Advance(ConstantBoundary(100.0, 80.0), 1.0,
            PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere);

        Assert.AreEqual(70.0, result.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(0.0, result.ConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Advance_RadiativeLossExceedingEmittingEnergy_RejectsRatherThanMakingHeatNegative()
    {
        var initial = new PlanetRadiativeEnvironmentState(new PlanetEnvironmentalEnergyBudget(100.0, 0.0, 0.0));

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.Advance(ConstantBoundary(0.0, 20.0),
            10.0, PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere));

        Assert.AreEqual(100.0, initial.Environment.TotalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, initial.CumulativeEmittedThermalEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_ZeroDuration_NeverCreatesExternalEnergy()
    {
        var initial = new PlanetRadiativeEnvironmentState(new PlanetEnvironmentalEnergyBudget(100.0, 200.0, 300.0));

        var result = initial.Advance(ConstantBoundary(1e10, 1e10), 0.0,
            PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere);

        Assert.AreEqual(600.0, result.Environment.TotalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, result.CumulativeAbsorbedStellarEnergyJoules, 0.0);
        Assert.AreEqual(0.0, result.CumulativeEmittedThermalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, result.ConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Advance_OverflowOrInvalidPower_RejectsUnphysicalBoundary()
    {
        var initial = new PlanetRadiativeEnvironmentState(new PlanetEnvironmentalEnergyBudget(100.0, 0.0, 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => initial.Advance(ConstantBoundary(double.NaN, 0.0),
            1.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => initial.Advance(ConstantBoundary(0.0, -1.0),
            1.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => initial.Advance(ConstantBoundary(1e200, 0.0),
            1e200, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => initial.Advance(ConstantBoundary(0.0, 0.0),
            -1.0, PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere));
    }

    [TestMethod]
    public void Advance_InvalidEmissionReservoirOrCancellation_KeepsOriginalEpoch()
    {
        var initial = new PlanetRadiativeEnvironmentState(new PlanetEnvironmentalEnergyBudget(100.0, 0.0, 0.0));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            initial.Advance(ConstantBoundary(20.0, 10.0), 1.0,
                PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere, cts.Token));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            initial.Advance(ConstantBoundary(20.0, 10.0), 1.0,
                PlanetEnvironmentHeatReservoir.Atmosphere, (PlanetEnvironmentHeatReservoir)999));
        Assert.AreEqual(100.0, initial.Environment.TotalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, initial.CumulativeAbsorbedStellarEnergyJoules, 0.0);
    }

    private static PlanetRadiativeBalance ConstantBoundary(double absorbedWatts, double emittedWatts) =>
        new(1.0, absorbedWatts, emittedWatts, absorbedWatts, emittedWatts);
}
