using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginVolatileTransportTests
{
    private static PlanetEnvironmentHeatCapacityParameters Parameters => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 10.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void AdvanceCoupled_DeliveryOutgassingEscapeAndCondensation_ConservesPlanetaryWaterAndEnergy()
    {
        var original = CreateWorld();
        var step = Step(
        [
            PlanetOriginVolatileExchange.DeliverToMantle(10.0, 2_000.0),
            PlanetOriginVolatileExchange.DissolveMantleWater(8.0),
            PlanetOriginVolatileExchange.OutgasSteam(3.0, 1_000.0),
            PlanetOriginVolatileExchange.EscapeSteam(1.0)
        ],
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 1.0, 2_000_000.0),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ]);

        var evolved = original.AdvanceCoupled(step);

        Assert.AreEqual(1L, evolved.Revision);
        Assert.AreEqual(1_000_000L, evolved.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(14.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.HydratedMantle), 0.0);
        Assert.AreEqual(5.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.DissolvedInMagma), 0.0);
        Assert.AreEqual(1.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(21.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(41.0, evolved.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(10.0, evolved.Water.CumulativeDeliveredMassKilograms - original.Water.CumulativeDeliveredMassKilograms, 0.0);
        Assert.AreEqual(1.0, evolved.Water.CumulativeEscapedMassKilograms, 0.0);
        Assert.AreEqual(6_000.0, evolved.Thermal.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(1_000.0, evolved.Thermal.EnergyBudget.CumulativeMaterialExportJoules, 1e-6);
        Assert.AreEqual(2_000.0, evolved.CoupledEnergy!.CumulativeDeliveredMaterialEnergyJoules, 1e-6);
        Assert.AreEqual(750_125.0, evolved.CoupledEnergy.CumulativeEscapedMaterialEnergyJoules, 1e-6);
        Assert.AreEqual(-2_000_000.0, evolved.Environment!.RelativeLatentEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, evolved.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, evolved.Thermal.EnergyBudget.ConservationResidualJoules, 1e-9);
        Assert.AreEqual(0.0, evolved.Environment.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, evolved.WaterConservationResidualKilograms, 0.0);

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(5_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_DirectSteamDelivery_FormsAtmosphereBeforeRadiationWithoutInventingWater()
    {
        var original = CreateWorld();
        var step = Step([PlanetOriginVolatileExchange.DeliverSteam(2.0, 1_000_000.0)],
            stellarJoules: 200.0, infraredJoules: 50.0);
        var next = original.AdvanceCoupled(step);

        Assert.AreEqual(2.0, next.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(1_000_000.0, next.CoupledEnergy!.CumulativeDeliveredMaterialEnergyJoules, 0.0);
        Assert.AreEqual(150.0, next.CoupledEnergy.CumulativeAbsorbedStellarEnergyJoules -
            next.CoupledEnergy.CumulativeEmittedInfraredEnergyJoules, 1e-9);
        Assert.AreEqual(0.0, next.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, next.WaterConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_EscapingAllVaporFromNoDryAtmosphere_RemovesAllVaporSensibleHeat()
    {
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 2.0);
        var properties = Parameters with { NoncondensableAtmosphereMassKilograms = 0.0 };
        var thermal = CreateThermal();
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(4_000_000.0, 0.0, 8_000_000.0));
        var original = new PlanetOriginEpochState(new PlanetOriginEnergyLedger(thermal, environment, properties));

        var evolved = original.AdvanceCoupled(Step([PlanetOriginVolatileExchange.EscapeSteam(2.0)]));

        Assert.AreEqual(0.0, evolved.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(0.0, evolved.Environment!.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(4_000_000.0, evolved.CoupledEnergy!.CumulativeEscapedMaterialEnergyJoules, 1e-6);
        Assert.IsNull(evolved.Temperatures!.Value.AtmosphereTemperatureKelvin);
        Assert.AreEqual(0.0, evolved.EnergyConservationResidualJoules, 1e-6);
    }

    [TestMethod]
    public void AdvanceCoupled_MagmaOutgassingExceedsInteriorEnergy_RejectsWholeEpoch()
    {
        var original = CreateWorld();
        Assert.ThrowsExactly<InvalidOperationException>(() => original.AdvanceCoupled(Step(
        [
            PlanetOriginVolatileExchange.DissolveMantleWater(2.0),
            PlanetOriginVolatileExchange.OutgasSteam(2.0, 10_000.0)
        ])));

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(12.0, original.Water.GetMassKilograms(PlanetWaterReservoir.HydratedMantle), 0.0);
        Assert.AreEqual(5_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_SecondMaterialEventOverdrawsAtmosphere_RollsBackAllTransactions()
    {
        var original = CreateWorld();
        var valid = Step([PlanetOriginVolatileExchange.DeliverSteam(3.0, 1_000.0)]);
        var invalid = Step([PlanetOriginVolatileExchange.EscapeSteam(4.0)]);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.AdvanceCoupled([valid, invalid]));
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0.0, original.CoupledEnergy!.CumulativeDeliveredMaterialEnergyJoules, 0.0);
        Assert.AreEqual(0L, original.Clock.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void AdvanceCoupled_InvalidRouteGhostHeatOrNegativeMass_IsRejected()
    {
        var original = CreateWorld();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => original.AdvanceCoupled(Step(
            [new PlanetOriginVolatileExchange((PlanetOriginVolatileExchangeKind)99, 1.0, 0.0)])));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => original.AdvanceCoupled(Step(
            [PlanetOriginVolatileExchange.DeliverSteam(-1.0, 10.0)])));
        Assert.ThrowsExactly<ArgumentException>(() => original.AdvanceCoupled(Step(
            [new PlanetOriginVolatileExchange(PlanetOriginVolatileExchangeKind.EscapeAtmosphericVapor, 1.0, 50.0)])));
        Assert.ThrowsExactly<ArgumentException>(() => original.AdvanceCoupled(Step(
            [PlanetOriginVolatileExchange.OutgasSteam(0.0, 100.0)])));
        Assert.ThrowsExactly<ArgumentException>(() => original.AdvanceCoupled(Step(
            [PlanetOriginVolatileExchange.DeliverSteam(1.0, 0.0)])));
        Assert.AreEqual(0L, original.Revision);
    }

    [TestMethod]
    public void AdvanceCoupled_PreCancelledVolatileSequence_DoesNotPublish()
    {
        var original = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => original.AdvanceCoupled(
            Step([PlanetOriginVolatileExchange.DeliverToMantle(3.0, 100.0)]), cancellation.Token));
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0.0, original.CoupledEnergy!.CumulativeDeliveredMaterialEnergyJoules, 0.0);
    }

    private static PlanetOriginEpochState CreateWorld()
    {
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.HydratedMantle, 12.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(6_000_000.0, 24_000_000.0, 8_000_000.0));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(CreateThermal(), environment, Parameters));
    }

    private static PlanetOriginThermalTimeline CreateThermal() => new(
        PlanetOriginClock.EarthAccretionReference,
        new PlanetThermalEnergyBudget(5_000.0),
        new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0)));

    private static PlanetOriginEnergyStep Step(
        IReadOnlyList<PlanetOriginVolatileExchange> exchanges,
        IReadOnlyList<PlanetWaterPhaseSensibleHeatStep>? phases = null,
        double stellarJoules = 0.0,
        double infraredJoules = 0.0)
    {
        var duration = PlanetOriginDuration.FromSeconds(1.0);
        return new PlanetOriginEnergyStep(duration, 0.0, 0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, stellarJoules, infraredJoules),
                PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.Atmosphere),
            phases ?? [])
        {
            VolatileChanges = exchanges
        };
    }
}
