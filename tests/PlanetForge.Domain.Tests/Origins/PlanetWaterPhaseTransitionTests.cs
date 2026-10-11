using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetWaterPhaseTransitionTests
{
    [TestMethod]
    public void Apply_SteamCondensationAndIceFormation_ConservesWaterAndAccountsForReleasedLatentHeat()
    {
        var primordialSteam = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 1.0e18);
        var changes = new[]
        {
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 4.0e17, 2.4e6),
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 1.0e17, 3.34e5),
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 5.0e16, 3.34e5)
        };

        var result = PlanetWaterPhaseTransitionSequence.Apply(primordialSteam, changes);

        Assert.AreEqual(6.0e17, result.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 128.0);
        Assert.AreEqual(3.5e17, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 128.0);
        Assert.AreEqual(5.0e16, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 128.0);
        Assert.AreEqual(1.0e18, result.Water.PlanetaryWaterMassKilograms, 128.0);
        Assert.AreEqual(0.0, result.Water.ConservationResidualKilograms, 128.0);
        Assert.AreEqual(4.0e17 * 2.4e6 + 1.0e17 * 3.34e5, result.HeatReleasedToEnvironmentJoules, 1e9);
        Assert.AreEqual(5.0e16 * 3.34e5, result.HeatAbsorbedFromEnvironmentJoules, 1e9);
        Assert.IsTrue(result.NetHeatDemandJoules < 0.0);
        Assert.AreEqual(0.0, primordialSteam.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0,
            "Prior epochs must not acquire oceans when a proposed transition is evaluated.");
    }

    [TestMethod]
    public void Apply_FreezingThenMelting_RequiresEqualAndOppositeEnvironmentalHeat()
    {
        var initial = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceLiquid, 1_000.0);
        const double latentHeat = 334_000.0;

        var result = PlanetWaterPhaseTransitionSequence.Apply(initial,
        [
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 400.0, latentHeat),
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 400.0, latentHeat)
        ]);

        Assert.AreEqual(1_000.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(0.0, result.NetHeatDemandJoules, 0.0);
        Assert.AreEqual(400.0 * latentHeat, result.HeatReleasedToEnvironmentJoules, 0.0);
        Assert.AreEqual(400.0 * latentHeat, result.HeatAbsorbedFromEnvironmentJoules, 0.0);
    }

    [TestMethod]
    public void Apply_EvaporationAndSublimation_AbsorbHeatFromEnvironment()
    {
        var initial = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 100.0)
            .Deliver(PlanetWaterReservoir.SurfaceIce, 50.0);

        var result = PlanetWaterPhaseTransitionSequence.Apply(initial,
        [
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Evaporate, 20.0, 2.25e6),
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Sublimate, 10.0, 2.8e6)
        ]);

        Assert.AreEqual(30.0, result.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 1e-9);
        Assert.AreEqual(80.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 1e-9);
        Assert.AreEqual(40.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 1e-9);
        Assert.AreEqual(20.0 * 2.25e6 + 10.0 * 2.8e6, result.NetHeatDemandJoules, 1e-8);
        Assert.AreEqual(0.0, result.HeatReleasedToEnvironmentJoules, 0.0);
    }

    [TestMethod]
    public void Apply_Deposition_ReleasesHeatWithoutInventingNewIceMass()
    {
        var vapor = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 60.0);
        var result = new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Deposit, 10.0, 2.8e6).Apply(vapor);

        Assert.AreEqual(50.0, result.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(10.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(-28.0e6, result.NetHeatDemandJoules, 0.0);
        Assert.AreEqual(0.0, vapor.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
    }

    [TestMethod]
    public void Apply_InsufficientSourceMass_RejectsWholeSequenceWithoutPartialPublication()
    {
        var dry = PlanetWaterInventory.Empty;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetWaterPhaseTransitionSequence.Apply(dry,
            [new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 1.0, 334_000.0)]));

        var water = dry.Deliver(PlanetWaterReservoir.AtmosphericVapor, 100.0);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanetWaterPhaseTransitionSequence.Apply(water,
            [
                new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 50.0, 2.4e6),
                new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 80.0, 334_000.0)
            ]));
        Assert.AreEqual(100.0, water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0.0, water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
    }

    [TestMethod]
    public void Apply_PreCancelledSequence_DoesNotProduceAnIntermediateState()
    {
        var input = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceIce, 10.0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetWaterPhaseTransitionSequence.Apply(input,
                [new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 5.0, 334_000.0)],
                cancellation.Token));
        Assert.AreEqual(10.0, input.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
    }

    [TestMethod]
    public void Apply_UnsupportedThermalParameters_AreRejected()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceLiquid, 5.0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 1.0, double.NaN).Apply(water));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, -1.0, 334_000.0).Apply(water));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 1.0, 0.0).Apply(water));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetWaterPhaseTransition((PlanetWaterPhaseTransitionKind)123, 1.0, 334_000.0).Apply(water));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, double.MaxValue, double.MaxValue).Apply(water));
        Assert.AreEqual(5.0, water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
    }
}
