using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginCoupledEpochStateTests
{
    private const double Year = PlanetThermalEnergyBudget.JulianYearSeconds;

    private static PlanetEnvironmentHeatCapacityParameters Parameters => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 10.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void AdvanceCoupled_HeatRadiationAndCondensation_PublishesOneConservedEpoch()
    {
        var original = CreateWorld();
        var step = Step(1, impactJoules: 100.0, radiogenicJoules: 50.0, interiorExportJoules: 300.0,
            stellarJoules: 1.0e6, infraredJoules: 2.0e6,
            phaseChanges:
            [
                new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 1.0, 2.0e6),
                    PlanetEnvironmentHeatReservoir.Atmosphere)
            ]);

        var next = original.AdvanceCoupled(step);

        Assert.AreEqual(1L, next.Revision);
        Assert.AreEqual(original.Clock.YearsBeforePresent - 1L, next.Clock.YearsBeforePresent);
        Assert.AreEqual(1_850.0, next.Thermal.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(4.0, next.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(21.0, next.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(25.0, next.Water.PlanetaryWaterMassKilograms, 0.0);
        Assert.AreEqual(-2.0e6, next.Environment!.RelativeLatentEnergyJoules, 1e-6);
        Assert.AreEqual(300.0, next.CoupledEnergy!.CumulativeInteriorExportJoules, 1e-6);
        Assert.AreEqual(0.0, next.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, next.WaterConservationResidualKilograms, 0.0);
        Assert.IsNotNull(next.Temperatures);
        Assert.AreSame(next.CoupledEnergy.Interior, next.Thermal);
        Assert.AreSame(next.CoupledEnergy.Environment.Water, next.Water);

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(5.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_RecomputedSubsteps_CommitOnceAndMatchSequentialPhysics()
    {
        var original = CreateWorld();
        var firstStep = Step(1, interiorExportJoules: 200.0, stellarJoules: 1.0e5, infraredJoules: 2.0e5);
        var secondStep = Step(2, impactJoules: 100.0, interiorExportJoules: 300.0,
            stellarJoules: 1.5e5, infraredJoules: 1.0e5);

        var singlePublication = original.AdvanceCoupled([firstStep, secondStep]);
        var sequential = original.AdvanceCoupled(firstStep).AdvanceCoupled(secondStep);

        Assert.AreEqual(1L, singlePublication.Revision);
        Assert.AreEqual(2L, sequential.Revision);
        Assert.AreEqual(sequential.Clock, singlePublication.Clock);
        Assert.AreEqual(sequential.CoupledEnergy!.StoredEnergyJoules, singlePublication.CoupledEnergy!.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(sequential.Thermal.EnergyBudget.StoredEnergyJoules,
            singlePublication.Thermal.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, singlePublication.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, singlePublication.WaterConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_InvalidSecondSubstep_DoesNotPublishFirstSubstep()
    {
        var original = CreateWorld();
        var valid = Step(1, interiorExportJoules: 100.0);
        var invalid = Step(1, infraredJoules: 10.0e6);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.AdvanceCoupled([valid, invalid]));
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(6.0e6, original.Environment!.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 0.0);
        Assert.AreEqual(0.0, original.CoupledEnergy!.CumulativeInteriorExportJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_OverdrawsSteamWithCondensation_RejectsWholeEpoch()
    {
        var original = CreateWorld();
        var impossible = Step(1, phaseChanges:
        [
            new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, 6.0, 2.0e6),
                PlanetEnvironmentHeatReservoir.Atmosphere)
        ]);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.AdvanceCoupled(impossible));
        Assert.AreEqual(5.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0L, original.Revision);
    }

    [TestMethod]
    public void AdvanceCoupled_Cancelled_DoesNotPublishAnySubsteps()
    {
        var original = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => original.AdvanceCoupled(
            [Step(1, stellarJoules: 500.0), Step(1, infraredJoules: 500.0)], cancellation.Token));
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0.0, original.CoupledEnergy!.CumulativeAbsorbedStellarEnergyJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_EmptyOrZeroDuration_RejectsPublication()
    {
        var original = CreateWorld();

        Assert.ThrowsExactly<ArgumentException>(() => original.AdvanceCoupled(Array.Empty<PlanetOriginEnergyStep>()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => original.AdvanceCoupled(Step(0)));
        Assert.AreEqual(0L, original.Revision);
    }

    [TestMethod]
    public void Advance_SeparateLegacyAndCoupledModes_PreventUnaccountedHeatOrWater()
    {
        var coupled = CreateWorld();
        var legacy = new PlanetOriginEpochState(coupled.Thermal, coupled.Water);
        var legacyStep = new PlanetOriginEpochStep(1, new PlanetThermalEnergyFlux(0.0, 0.0, 0.0),
            [PlanetOriginWaterChange.Transfer(PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, 1.0)]);

        Assert.ThrowsExactly<InvalidOperationException>(() => coupled.Advance(legacyStep));
        Assert.ThrowsExactly<InvalidOperationException>(() => legacy.AdvanceCoupled(Step(1)));
        Assert.AreEqual(0L, coupled.Revision);
        Assert.AreEqual(0L, legacy.Revision);
        Assert.IsNull(legacy.CoupledEnergy);
    }

    private static PlanetOriginEpochState CreateWorld()
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));
        var thermal = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0), rock);
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        var environmental = new PlanetWaterEnvironmentalState(water, new PlanetEnvironmentalEnergyBudget(6.0e6, 24.0e6, 8.0e6));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(thermal, environmental, Parameters));
    }

    private static PlanetOriginEnergyStep Step(
        long elapsedYears,
        double impactJoules = 0.0,
        double radiogenicJoules = 0.0,
        double interiorExportJoules = 0.0,
        double stellarJoules = 0.0,
        double infraredJoules = 0.0,
        IReadOnlyList<PlanetWaterPhaseSensibleHeatStep>? phaseChanges = null)
    {
        var seconds = elapsedYears * Year;
        return new PlanetOriginEnergyStep(elapsedYears, impactJoules / seconds, radiogenicJoules / seconds,
            new PlanetInteriorHeatExport(interiorExportJoules / seconds, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, stellarJoules / seconds, infraredJoules / seconds),
                PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.Atmosphere),
            phaseChanges ?? []);
    }
}
