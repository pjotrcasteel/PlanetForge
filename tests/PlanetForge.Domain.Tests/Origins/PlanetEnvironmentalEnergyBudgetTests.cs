using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetEnvironmentalEnergyBudgetTests
{
    [TestMethod]
    public void TransferHeat_RepeatedSubUlpDebits_PreservesSmallFluxesAndImmutableSource()
    {
        const double initialEnergy = 1e20;
        var initial = new PlanetEnvironmentalEnergyBudget(initialEnergy, 0.0, 0.0);
        var candidate = initial;
        for (var i = 0; i < 10_000; i++)
            candidate = candidate.TransferHeat(PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.SurfaceOcean, 1.0);
        Assert.AreEqual(initialEnergy - 10_000.0, candidate.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(10_000.0, candidate.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(initialEnergy, initial.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
    }

    [TestMethod]
    public void Apply_CondensationFreezingAndMelting_ConservesMassAndCombinedEnvironmentalLatentEnergy()
    {
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 1_000.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 200.0)
            .Deliver(PlanetWaterReservoir.SurfaceIce, 100.0);
        var initial = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(2.0e9, 1.0e9, 5.0e8));

        var evolved = initial.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 100.0, 2.4e6),
                PlanetEnvironmentHeatReservoir.Atmosphere),
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 20.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean),
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 10.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceRock)
        ]);

        Assert.AreEqual(900.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(290.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(110.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(1_300.0, evolved.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0.0, evolved.Water.ConservationResidualKilograms, 0.0);
        Assert.AreEqual(2.24e9, evolved.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 1e-6);
        Assert.AreEqual(1.00668e9, evolved.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 1e-6);
        Assert.AreEqual(496.66e6, evolved.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 1e-6);
        Assert.AreEqual(-243.34e6, evolved.RelativeLatentEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, evolved.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, initial.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(2.0e9, initial.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(0.0, initial.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce) - 100.0, 0.0);
    }

    [TestMethod]
    public void Apply_InsufficientOceanEnergy_RejectsEvaporationWithoutPublishingAnyChanges()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceLiquid, 300.0);
        var original = new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(1.0e9, 1.0e7, 1.0e9));

        Assert.ThrowsExactly<InvalidOperationException>(() => original.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 20.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean),
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Evaporate, 100.0, 2.4e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]));

        Assert.AreEqual(300.0, original.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, original.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(1.0e7, original.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(0.0, original.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Apply_FreezingFollowedByMelting_UsesReleasedHeatWithoutCreatingEnergy()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceLiquid, 100.0);
        var original = new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(0.0, 0.0, 0.0));

        var result = original.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 50.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean),
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 50.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]);

        Assert.AreEqual(100.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, result.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(0.0, result.Environment.TotalEnergyJoules, 0.0);
        Assert.AreEqual(0.0, result.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void TransferHeat_AtmosphereToOcean_ConservesEnergyBetweenEnvironmentalStores()
    {
        var original = new PlanetEnvironmentalEnergyBudget(100.0, 10.0, 0.0);
        var moved = original.TransferHeat(PlanetEnvironmentHeatReservoir.Atmosphere,
            PlanetEnvironmentHeatReservoir.SurfaceOcean, 25.0);

        Assert.AreEqual(75.0, moved.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(35.0, moved.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(110.0, moved.TotalEnergyJoules, 0.0);
        Assert.AreEqual(100.0, original.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.ThrowsExactly<InvalidOperationException>(() => moved.TransferHeat(
            PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.SurfaceOcean, 76.0));
    }

    [TestMethod]
    public void Apply_DryWorld_RemainsDryAndDoesNotSpontaneouslyProduceRain()
    {
        var original = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(1.0e20, 1.0e20, 1.0e20));

        var unchanged = original.Apply([]);

        Assert.AreEqual(0.0, unchanged.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0.0, unchanged.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, unchanged.EnergyConservationResidualJoules, 0.0);
        Assert.ThrowsExactly<InvalidOperationException>(() => original.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 1.0, 2.4e6),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ]));
    }

    [TestMethod]
    public void Apply_InvalidOrCancelledStep_RejectsPartialWaterAndThermalPublication()
    {
        var original = new PlanetWaterEnvironmentalState(
            PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 100.0),
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, 0.0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => original.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 50.0, 2.4e6),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ], cancellation.Token));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => original.Apply(
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 50.0, 2.4e6),
                (PlanetEnvironmentHeatReservoir)99)
        ]));
        Assert.AreEqual(100.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0.0, original.Environment.TotalEnergyJoules, 0.0);
    }

    [TestMethod]
    public void EnvironmentalBudget_NonfiniteAndNegativeValues_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetEnvironmentalEnergyBudget(-1.0, 0.0, 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetEnvironmentalEnergyBudget(0.0, double.NaN, 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetEnvironmentalEnergyBudget(double.MaxValue, double.MaxValue, 0.0));
        var budget = new PlanetEnvironmentalEnergyBudget(100.0, 200.0, 300.0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => budget.GetEnergyJoules((PlanetEnvironmentHeatReservoir)10));
        Assert.ThrowsExactly<ArgumentException>(() =>
            budget.TransferHeat(PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere, 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            budget.TransferHeat(PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.SurfaceOcean, double.PositiveInfinity));
    }
}
