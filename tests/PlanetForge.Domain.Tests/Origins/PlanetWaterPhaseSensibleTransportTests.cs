using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetWaterPhaseSensibleTransportTests
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
        var after = PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
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
        var after = PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
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

        var after = PlanetWaterPhaseSensibleTransport.Apply(before, parameters,
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

        var after = PlanetWaterPhaseSensibleTransport.Apply(before, parameters,
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

        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetWaterPhaseSensibleTransport.Apply(before, parameters,
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

        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
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
    public void Apply_FreezeThenMeltSameMass_RestoresMassAndEnergyExactly()
    {
        var before = CreateWorld();

        var frozen = PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 5.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]);
        var temperatures = PlanetEnvironmentTemperatureModel.Resolve(frozen, Parameters);

        // Ocean 24 MJ over 80 kJ/K = 300 K. 5 kg of liquid carries 5*4000*300 = 6 MJ into the ice store.
        Assert.AreEqual(5.0, frozen.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(6.0e6, frozen.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 1e-6);
        Assert.AreEqual(24.0e6 - 6.0e6 + 1.67e6,
            frozen.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 1e-6);
        Assert.AreEqual(6.0e6 / (5.0 * 2_100.0), temperatures.IceTemperatureKelvin!.Value, 1e-9,
            "Moved joules are preserved; the different ice cp changes the diagnostic temperature.");
        Assert.AreEqual(0.0, frozen.EnergyConservationResidualJoules, 1e-6);

        var thawed = PlanetWaterPhaseSensibleTransport.Apply(frozen, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 5.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]);

        Assert.AreEqual(0.0, thawed.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(0.0, thawed.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 0.0,
            "Melting all ice must leave no ghost heat in the empty ice store.");
        Assert.AreEqual(before.Environment.TotalEnergyJoules, thawed.Environment.TotalEnergyJoules, 1e-6);
        Assert.AreEqual(before.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean),
            thawed.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 1e-6);
        Assert.AreEqual(0.0, thawed.RelativeLatentEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, thawed.Water.ConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void Apply_DepositionAndSublimation_CarrySensibleHeatBetweenAtmosphereAndIce()
    {
        var before = CreateWorld();

        var deposited = PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Deposit, 2.0, 2.83e6),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ]);

        // Atmosphere capacity 10*1000 + 5*2000 = 20 kJ/K; 2 kg vapor carries 4 kJ/K / 20 kJ/K of 6 MJ.
        Assert.AreEqual(1.2e6, deposited.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 1e-6);
        Assert.AreEqual(6.0e6 - 1.2e6 + 5.66e6,
            deposited.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 1e-6);
        Assert.AreEqual(-5.66e6, deposited.RelativeLatentEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, deposited.EnergyConservationResidualJoules, 1e-6);

        var sublimated = PlanetWaterPhaseSensibleTransport.Apply(deposited, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Sublimate, 2.0, 2.83e6),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ]);

        Assert.AreEqual(0.0, sublimated.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(5.0, sublimated.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(before.Environment.TotalEnergyJoules, sublimated.Environment.TotalEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, sublimated.RelativeLatentEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, sublimated.EnergyConservationResidualJoules, 1e-6);
    }

    [TestMethod]
    public void Apply_MeltMoreIceThanExists_RejectsWithoutMovingHeat()
    {
        var before = CreateWorld();

        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Melt, 1.0, 334_000.0),
                PlanetEnvironmentHeatReservoir.SurfaceOcean)
        ]));
        Assert.AreEqual(0.0, before.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce), 0.0);
        Assert.AreEqual(24.0e6, before.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceOcean), 0.0);
    }

    [TestMethod]
    public void Apply_CancelledTransaction_DoesNotPublishAnyEnergyOrMass()
    {
        var before = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetWaterPhaseSensibleTransport.Apply(before, Parameters,
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
