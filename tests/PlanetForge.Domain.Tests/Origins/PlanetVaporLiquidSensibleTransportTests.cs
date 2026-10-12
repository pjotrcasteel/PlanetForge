using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetVaporLiquidSensibleTransportTests
{
    private static PlanetEnvironmentHeatCapacityParameters Parameters => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 10.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void Apply_Condensation_AdvectsVaporSensibleHeatAndDepositsLatentHeatIntoOcean()
    {
        var before = CreateWorld();
        var after = PlanetVaporLiquidSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 2.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]);
        var temperatures = PlanetEnvironmentTemperatureModel.Resolve(after, Parameters);

        Assert.AreEqual(3.0, after.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(22.0, after.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(25.0, after.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(4.8e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 1e-8);
        Assert.AreEqual(29.2e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 1e-8);
        Assert.AreEqual(300.0, temperatures.AtmosphereTemperatureKelvin!.Value, 1e-10,
            "Removing a well-mixed vapor fraction must also remove its sensible heat.");
        Assert.AreEqual(29.2e6 / 88_000.0, temperatures.OceanTemperatureKelvin!.Value, 1e-10);
        Assert.AreEqual(-4.0e6, after.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, after.EnergyConservationResidualJoules, 1e-8);
        Assert.AreEqual(0.0, after.Water.ConservationResidualKilograms, 0.0);
        Assert.AreEqual(6.0e6, before.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(5.0, before.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
    }

    [TestMethod]
    public void Apply_Evaporation_TransfersOceanSensibleHeatInsteadOfCreatingWarmVapor()
    {
        var before = CreateWorld();
        var after = PlanetVaporLiquidSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Evaporate, 2.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]);

        Assert.AreEqual(7.0, after.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(18.0, after.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(8.4e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 1e-8);
        Assert.AreEqual(17.6e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 1e-8);
        Assert.AreEqual(4.0e6, after.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, after.EnergyConservationResidualJoules, 1e-8);
        Assert.AreEqual(0.0, after.Water.ConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void Apply_EntireSteamInventoryIntoFirstOcean_LeavesNoGhostAtmosphericEnergy()
    {
        var parameters = Parameters with { NoncondensableAtmosphereMassKilograms = 0.0 };
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0);
        var before = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(3.0e6, 0.0, 0.0));

        var after = PlanetVaporLiquidSensibleTransport.Apply(before, parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 5.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]);
        var temperatures = PlanetEnvironmentTemperatureModel.Resolve(after, parameters);

        Assert.IsNull(temperatures.AtmosphereTemperatureKelvin);
        Assert.AreEqual(0.0, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(13.0e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(-10.0e6, after.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, after.EnergyConservationResidualJoules, 0.0);
        Assert.AreEqual(5.0, after.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
    }

    [TestMethod]
    public void Apply_EvaporatingLastOceanWater_TransfersEverySensibleJouleToSteam()
    {
        var parameters = Parameters with { NoncondensableAtmosphereMassKilograms = 0.0 };
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceLiquid, 5.0);
        var before = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(0.0, 6.0e6, 30.0e6));

        var after = PlanetVaporLiquidSensibleTransport.Apply(before, parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Evaporate, 5.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceRock)
        ]);
        var temperatures = PlanetEnvironmentTemperatureModel.Resolve(after, parameters);

        Assert.IsNull(temperatures.OceanTemperatureKelvin);
        Assert.AreEqual(0.0, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
        Assert.AreEqual(6.0e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(20.0e6, after.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), 0.0);
        Assert.AreEqual(10.0e6, after.RelativeLatentEnergyJoules, 0.0);
        Assert.AreEqual(0.0, after.EnergyConservationResidualJoules, 0.0);
        Assert.AreEqual(5.0, after.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
    }

    [TestMethod]
    public void Apply_InsufficientEnergyForEvaporation_RollsBackWaterAndHeatTogether()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.SurfaceLiquid, 100.0);
        var before = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(0.0, 120.0e6, 0.0));
        var parameters = Parameters with
        {
            NoncondensableAtmosphereMassKilograms = 0.0,
            SurfaceRockMassKilograms = 0.0
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetVaporLiquidSensibleTransport.Apply(before, parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Evaporate, 100.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]));

        Assert.AreEqual(100.0, before.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, before.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(120.0e6, before.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
    }

    [TestMethod]
    public void Apply_LaterInvalidPhase_DoesNotPublishPriorSuccessfulTransport()
    {
        var before = CreateWorld();

        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetVaporLiquidSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 2.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean),
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 10.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]));

        Assert.AreEqual(5.0, before.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(20.0, before.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, before.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Apply_IceTransitionWithoutIceThermalStore_IsRejected()
    {
        var before = CreateWorld();

        Assert.ThrowsExactly<NotSupportedException>(() => PlanetVaporLiquidSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 1.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]));
        Assert.AreEqual(0.0, before.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
    }

    [TestMethod]
    public void Apply_CancelledTransaction_DoesNotPublishAnyEnergyOrMass()
    {
        var before = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetVaporLiquidSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 2.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ], cancellation.Token));
        Assert.AreEqual(5.0, before.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(6.0e6, before.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
    }

    private static PlanetWaterEnvironmentalState CreateWorld()
    {
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        return new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(6.0e6, 24.0e6, 8.0e6));
    }
}
