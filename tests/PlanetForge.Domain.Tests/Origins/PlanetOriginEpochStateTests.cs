using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginEpochStateTests
{
    [TestMethod]
    public void Advance_ImpactDegassingCondensation_AdvancesOneConsistentPhysicalEpoch()
    {
        var original = CreateWorld();
        var durationSeconds = 1_000_000.0 * PlanetThermalEnergyBudget.JulianYearSeconds;
        var step = new PlanetOriginEpochStep(
            1_000_000,
            new PlanetThermalEnergyFlux(100.0 / durationSeconds, 0.0, 50.0 / durationSeconds),
            [
                PlanetOriginWaterChange.Deliver(PlanetWaterReservoir.HydratedMantle, 100.0),
                PlanetOriginWaterChange.Transfer(PlanetWaterReservoir.HydratedMantle, PlanetWaterReservoir.DissolvedInMagma, 400.0),
                PlanetOriginWaterChange.Transfer(PlanetWaterReservoir.DissolvedInMagma, PlanetWaterReservoir.AtmosphericVapor, 300.0),
                PlanetOriginWaterChange.Transfer(PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, 200.0)
            ]);

        var evolved = original.Advance(step);

        Assert.AreEqual(1L, evolved.Revision);
        Assert.AreEqual(4_539_000_000L, evolved.Clock.YearsBeforePresent);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(2_050.0, evolved.Thermal.EnergyBudget.StoredEnergyJoules, 1e-9);
        Assert.AreEqual(2_100.0, evolved.Water.PlanetaryWaterMassKilograms, 1e-9);
        Assert.AreEqual(1_700.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.HydratedMantle), 1e-9);
        Assert.AreEqual(100.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.DissolvedInMagma), 1e-9);
        Assert.AreEqual(100.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 1e-9);
        Assert.AreEqual(200.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 1e-9);
        Assert.AreEqual(0.0, evolved.WaterConservationResidualKilograms, 1e-9);
        Assert.AreEqual(0.0, evolved.EnergyConservationResidualJoules, 1e-9);
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(2_000.0, original.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0.0, original.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
    }

    [TestMethod]
    public void Advance_InvalidCondensation_RejectsWholeEpochWithoutMutatingPreviousState()
    {
        var original = CreateWorld();
        var step = new PlanetOriginEpochStep(1, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0),
        [
            PlanetOriginWaterChange.Transfer(PlanetWaterReservoir.HydratedMantle, PlanetWaterReservoir.AtmosphericVapor, 100.0),
            PlanetOriginWaterChange.Transfer(PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, 500.0)
        ]);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.Advance(step));
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(2_000.0, original.Water.GetMassKilograms(PlanetWaterReservoir.HydratedMantle), 0.0);
        Assert.AreEqual(0.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0L, original.Revision);
    }

    [TestMethod]
    public void Advance_ExcessiveCooling_DoesNotApplyValidWaterDelivery()
    {
        var original = CreateWorld();
        var step = new PlanetOriginEpochStep(1, new PlanetThermalEnergyFlux(0.0, 0.0, 1_000.0),
        [
            PlanetOriginWaterChange.Deliver(PlanetWaterReservoir.HydratedMantle, 1_000.0)
        ]);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.Advance(step));
        Assert.AreEqual(2_000.0, original.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_CancelledEpoch_CannotPublishEnergyOrWaterChanges()
    {
        var original = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => original.Advance(
            new PlanetOriginEpochStep(1, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0),
            [PlanetOriginWaterChange.Deliver(PlanetWaterReservoir.HydratedMantle, 5.0)]),
            cancellation.Token));
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(2_000.0, original.Water.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Advance_UnknownReservoirMovement_RejectsWholeEpoch()
    {
        var original = CreateWorld();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => original.Advance(
            new PlanetOriginEpochStep(1, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0),
            [new PlanetOriginWaterChange((PlanetOriginWaterChangeKind)99, default, default, 10.0)])));
        Assert.AreEqual(0L, original.Revision);
    }

    [TestMethod]
    public void Advance_TwoValidEpochs_UseMonotonePublicationRevision()
    {
        var original = CreateWorld();
        var step = new PlanetOriginEpochStep(100, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0),
            Array.Empty<PlanetOriginWaterChange>());

        var first = original.Advance(step);
        var second = first.Advance(step);

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(1L, first.Revision);
        Assert.AreEqual(2L, second.Revision);
        Assert.AreEqual(4_539_999_800L, second.Clock.YearsBeforePresent);
        Assert.AreEqual(2_000.0, second.Water.PlanetaryWaterMassKilograms, 0.0);
    }

    private static PlanetOriginEpochState CreateWorld()
    {
        var material = new PlanetRockEnthalpyModel(
            new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));
        var thermal = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0),
            material);
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.HydratedMantle, 2_000.0);
        return new PlanetOriginEpochState(thermal, water);
    }
}
