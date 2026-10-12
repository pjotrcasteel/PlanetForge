using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginEpochStateTests
{
    private static readonly double Year = PlanetThermalEnergyBudget.JulianYearSeconds;

    [TestMethod]
    public void Advance_InteriorRadiationAndPhaseChange_PublishesOneClosedEpoch()
    {
        var original = CreateWorld();
        var step = new PlanetOriginEnergyStep(
            1,
            100.0 / Year,
            50.0 / Year,
            new PlanetInteriorHeatExport(300.0 / Year, PlanetEnvironmentHeatReservoir.Atmosphere),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, 100_000.0 / Year, 50_000.0 / Year),
                PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.Atmosphere),
            [
                new(
                    new PlanetWaterPhaseTransition(
                        PlanetWaterPhaseTransitionKind.Condense, 1.0, 2_000_000.0),
                    PlanetEnvironmentHeatReservoir.Atmosphere)
            ]);

        var evolved = original.Advance(step);

        Assert.AreEqual(1L, evolved.Revision);
        Assert.AreEqual(4_539_999_999L, evolved.Clock.YearsBeforePresent);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(1_850.0, evolved.Thermal.EnergyBudget.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(9.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(21.0, evolved.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(original.EnergyLedger.StoredEnergyJoules + 50_150.0,
            evolved.EnergyLedger.StoredEnergyJoules, 1e-5);
        Assert.AreEqual(0.0, evolved.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, evolved.Environment.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, evolved.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(10.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_InvalidRadiativeExchange_DoesNotPublishAnyPartOfEpoch()
    {
        var original = CreateWorld();
        var step = new PlanetOriginEnergyStep(
            1,
            0.0,
            0.0,
            new PlanetInteriorHeatExport(100.0 / Year, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 3_000_000.0 / Year),
                PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.Atmosphere),
            []);

        Assert.ThrowsExactly<InvalidOperationException>(() => original.Advance(step));

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(10.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0.0, original.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void Advance_CancelledStep_DoesNotPublishEpoch()
    {
        var original = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => original.Advance(
            CreateNoOpStep(), cancellation.Token));

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(30.0, original.Water.PlanetaryWaterMassKilograms, 0.0);
    }

    [TestMethod]
    public void Advance_SequentialSteps_KeepRevisionAndEpochAligned()
    {
        var original = CreateWorld();

        var first = original.Advance(CreateNoOpStep());
        var second = first.Advance(CreateNoOpStep());

        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(1L, first.Revision);
        Assert.AreEqual(2L, second.Revision);
        Assert.AreEqual(4_539_999_998L, second.Clock.YearsBeforePresent);
        Assert.AreNotSame(original.EnergyLedger, first.EnergyLedger);
        Assert.AreEqual(0.0, second.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceSubsteps_ReevaluatedSteps_PublishAsOneRevision()
    {
        var original = CreateWorld();
        var evaluations = 0;

        var substepped = original.AdvanceSubsteps([1, 1], (_, _, _) =>
        {
            var step = evaluations++ == 0
                ? CreateNoOpStep() with { ImpactHeatingWatts = 10.0 / Year }
                : CreateNoOpStep() with { RadiogenicHeatingWatts = 5.0 / Year };
            return step;
        });
        var sequential = original
            .Advance(CreateNoOpStep() with { ImpactHeatingWatts = 10.0 / Year })
            .Advance(CreateNoOpStep() with { RadiogenicHeatingWatts = 5.0 / Year });

        Assert.AreEqual(2, evaluations);
        Assert.AreEqual(1L, substepped.Revision);
        Assert.AreEqual(2L, sequential.Revision);
        Assert.AreEqual(sequential.Clock, substepped.Clock);
        Assert.AreEqual(sequential.EnergyLedger.StoredEnergyJoules, substepped.EnergyLedger.StoredEnergyJoules, 1e-6);
        Assert.AreEqual(0.0, substepped.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0L, original.Revision);
    }

    [TestMethod]
    public void AdvanceSubsteps_FailedLaterSubstep_DoesNotPublishEarlierWork()
    {
        var original = CreateWorld();
        var invalid = new PlanetOriginEnergyStep(
            1,
            0.0,
            0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.Atmosphere),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 3_000_000.0 / Year),
                PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.Atmosphere),
            []);
        var evaluations = 0;

        Assert.ThrowsExactly<InvalidOperationException>(() => original.AdvanceSubsteps([1, 1], (_, _, _) =>
            evaluations++ == 0
                ? CreateNoOpStep() with { ImpactHeatingWatts = 10.0 / Year }
                : invalid));

        Assert.AreEqual(2, evaluations);
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.AreEqual(0.0, original.EnergyConservationResidualJoules, 0.0);
    }

    [TestMethod]
    public void AdvanceSubsteps_EmptyList_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateWorld().AdvanceSubsteps([], (_, _, _) => CreateNoOpStep()));
    }

    [TestMethod]
    public void AdvanceSubsteps_CancelledDuringEvaluation_DoesNotPublishEarlierSubsteps()
    {
        var original = CreateWorld();
        using var cancellation = new CancellationTokenSource();
        var evaluations = 0;

        Assert.ThrowsExactly<OperationCanceledException>(() => original.AdvanceSubsteps([1, 1],
            (_, _, token) =>
            {
                Assert.AreEqual(cancellation.Token, token);
                if (evaluations++ == 1)
                {
                    cancellation.Cancel();
                }

                return CreateNoOpStep();
            },
            cancellation.Token));

        Assert.AreEqual(2, evaluations);
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(4_540_000_000L, original.Clock.YearsBeforePresent);
        Assert.AreEqual(2_000.0, original.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Advance_ZeroDurationStep_PreservesStateAndAdvancesPublicationRevision()
    {
        var original = CreateWorld();

        var next = original.Advance(CreateNoOpStep() with { ElapsedYears = 0 });

        Assert.AreEqual(1L, next.Revision);
        Assert.AreEqual(original.Clock, next.Clock);
        Assert.AreEqual(original.EnergyLedger.StoredEnergyJoules, next.EnergyLedger.StoredEnergyJoules, 0.0);
        Assert.AreEqual(original.Water.PlanetaryWaterMassKilograms, next.Water.PlanetaryWaterMassKilograms, 0.0);
    }

    private static PlanetOriginEnergyStep CreateNoOpStep() => new(
        1,
        0.0,
        0.0,
        new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.Atmosphere),
        new PlanetRadiativeExchange(
            new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
            PlanetEnvironmentHeatReservoir.Atmosphere,
            PlanetEnvironmentHeatReservoir.Atmosphere),
        []);

    private static PlanetOriginEpochState CreateWorld()
    {
        var material = new PlanetRockEnthalpyModel(
            new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));
        var thermal = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0),
            material);
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 10.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 20.0);
        var environment = new PlanetWaterEnvironmentalState(
            water,
            new PlanetEnvironmentalEnergyBudget(2_000_000.0, 4_000_000.0, 8_000_000.0));
        var heatCapacities = new PlanetEnvironmentHeatCapacityParameters(
            NoncondensableAtmosphereMassKilograms: 0.0,
            NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
            VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
            LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
            SurfaceRockMassKilograms: 10.0,
            SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);
        var ledger = new PlanetOriginEnergyLedger(thermal, environment, heatCapacities);
        return new PlanetOriginEpochState(ledger);
    }
}
