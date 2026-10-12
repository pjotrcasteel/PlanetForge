using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginSubyearEnergyTests
{
    private static PlanetEnvironmentHeatCapacityParameters Parameters => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 10.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void AdvanceCoupled_OneHourOfThermalRadiationAndCondensation_ConservesWaterAndGlobalEnergy()
    {
        var original = CreateWorld();
        var step = Step(PlanetOriginDuration.FromSeconds(3_600), impactJoules: 100.0, radiogenicJoules: 50.0,
            interiorExportJoules: 300.0, absorbedJoules: 1.0e6, emittedJoules: 2.0e6,
            phases:
            [
                new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 1.0, 2.0e6),
                    PlanetEnvironmentHeatReservoir.Atmosphere)
            ]);

        var next = original.AdvanceCoupled(step);

        Assert.AreEqual(1L, next.Revision);
        Assert.AreEqual(3_600_000_000L, next.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(original.Clock.YearsBeforePresent, next.Clock.YearsBeforePresent);
        Assert.AreEqual(1_850.0, next.Thermal.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(4.0, next.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(21.0, next.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, next.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, next.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0L, original.Clock.MicrosecondsIntoYear);
    }

    [TestMethod]
    public void AdvanceCoupled_OneDayAndTwoHalfDays_PublishEquivalentEnergyAndAbsoluteAge()
    {
        var original = CreateWorld();
        var whole = original.AdvanceCoupled(Step(PlanetOriginDuration.FromSeconds(86_400),
            impactJoules: 200.0, interiorExportJoules: 100.0, absorbedJoules: 400.0, emittedJoules: 200.0));
        var split = original.AdvanceCoupled(
        [
            Step(PlanetOriginDuration.FromSeconds(43_200), impactJoules: 100.0,
                interiorExportJoules: 50.0, absorbedJoules: 200.0, emittedJoules: 100.0),
            Step(PlanetOriginDuration.FromSeconds(43_200), impactJoules: 100.0,
                interiorExportJoules: 50.0, absorbedJoules: 200.0, emittedJoules: 100.0)
        ]);

        Assert.AreEqual(whole.Clock, split.Clock);
        Assert.AreEqual(1L, whole.Revision);
        Assert.AreEqual(1L, split.Revision);
        Assert.AreEqual(whole.CoupledEnergy!.StoredEnergyJoules, split.CoupledEnergy!.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(whole.Thermal.EnergyBudget.StoredEnergyJoules, split.Thermal.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, whole.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, split.EnergyConservationResidualJoules, 1e-6);
    }

    [TestMethod]
    public void AdvanceCoupled_SecondSubdayStepOverdrawsRadiation_PublishesNothing()
    {
        var original = CreateWorld();
        var good = Step(PlanetOriginDuration.FromSeconds(60), interiorExportJoules: 50.0);
        var impossible = Step(PlanetOriginDuration.FromSeconds(60), emittedJoules: 10.0e6);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.AdvanceCoupled([good, impossible]));
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0L, original.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(0.0, original.CoupledEnergy!.CumulativeInteriorExportJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_ShortDurationAndLegacyWholeYear_AdvanceSameLedgerWithoutRounding()
    {
        var original = CreateWorld();
        var oneSecond = original.AdvanceCoupled(Step(PlanetOriginDuration.FromSeconds(1)));
        var later = oneSecond.AdvanceCoupled(new PlanetOriginEnergyStep(1, 0.0, 0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
                PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere), []));

        Assert.AreEqual(original.Clock.YearsSinceSolarSystemFormation + 1, later.Clock.YearsSinceSolarSystemFormation);
        Assert.AreEqual(1_000_000L, later.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(2L, later.Revision);
        Assert.AreEqual(0.0, later.EnergyConservationResidualJoules, 1e-6);
    }

    private static PlanetOriginEpochState CreateWorld()
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));
        var thermal = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0), rock);
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        var environment = new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(6.0e6, 24.0e6, 8.0e6));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(thermal, environment, Parameters));
    }

    private static PlanetOriginEnergyStep Step(
        PlanetOriginDuration duration,
        double impactJoules = 0.0,
        double radiogenicJoules = 0.0,
        double interiorExportJoules = 0.0,
        double absorbedJoules = 0.0,
        double emittedJoules = 0.0,
        IReadOnlyList<PlanetWaterPhaseSensibleHeatStep>? phases = null)
    {
        var seconds = duration.TotalSeconds;
        return new PlanetOriginEnergyStep(duration, impactJoules / seconds, radiogenicJoules / seconds,
            new PlanetInteriorHeatExport(interiorExportJoules / seconds, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(new PlanetRadiativeBalance(1.0, 0.0, 0.0,
                absorbedJoules / seconds, emittedJoules / seconds), PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.Atmosphere), phases ?? []);
    }
}
