using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginAdaptiveIntegratorTests
{
    private static PlanetEnvironmentHeatCapacityParameters ThermalProperties => new(
        NoncondensableAtmosphereMassKilograms: 10.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 2_000.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    [TestMethod]
    public void AdvanceCoupledAdaptive_OverdrawRefinesAndReevaluatesRatesOnLatestCandidate()
    {
        var initial = World();
        var observed = new List<double>();
        var options = Options(maximumSeconds: 10, minimumSeconds: 5);

        var result = initial.AdvanceCoupledAdaptive(PlanetOriginDuration.FromSeconds(10), options, (energy, duration) =>
        {
            var stored = energy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere);
            observed.Add(stored);
            return CoolingStep(duration, stored * 0.11);
        });

        Assert.AreEqual(3, observed.Count, "Initial ten-second overdraw must retry two separate five-second proposals.");
        Assert.AreEqual(3_000_000.0, observed[0], 0.0);
        Assert.AreEqual(3_000_000.0, observed[1], 0.0);
        Assert.AreEqual(1_350_000.0, observed[2], 1e-6);
        Assert.AreEqual(607_500.0, result.Environment!.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere), 1e-6);
        Assert.AreEqual(2_392_500.0, result.CoupledEnergy!.CumulativeEmittedInfraredEnergyJoules, 1e-6);
        Assert.AreEqual(10_000_000L, result.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-6);
        Assert.AreEqual(0.0, result.WaterConservationResidualKilograms, 0.0);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_ExhaustsMinimumTimestep_LeavesPublishedEpochUntouched()
    {
        var initial = World();
        var attempts = 0;
        var options = Options(maximumSeconds: 10, minimumSeconds: 5);

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(10), options, (energy, duration) =>
            {
                attempts++;
                var stored = energy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere);
                return CoolingStep(duration, stored * 0.25);
            }));

        Assert.AreEqual(2, attempts);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_ExceedsAcceptedStepBudget_RejectsEarlierGoodCandidates()
    {
        var initial = World();
        var options = Options(maximumSeconds: 5, minimumSeconds: 1) with { MaximumAcceptedSubsteps = 1 };

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(10), options, (energy, duration) => CoolingStep(duration, 1.0)));

        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_CancelledAfterFirstAcceptedSubstep_DoesNotPublish()
    {
        var initial = World();
        var options = Options(maximumSeconds: 5, minimumSeconds: 1);
        using var cts = new CancellationTokenSource();
        var calls = 0;

        Assert.ThrowsExactly<OperationCanceledException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(10), options, (energy, duration) =>
            {
                if (++calls == 2) cts.Cancel();
                return CoolingStep(duration, 1.0);
            }, cts.Token));

        Assert.AreEqual(2, calls);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_ProcessReturningMismatchedDuration_IsRejectedRatherThanAdjusted()
    {
        var initial = World();

        Assert.ThrowsExactly<ArgumentException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(10), Options(5, 1),
            (energy, duration) => CoolingStep(PlanetOriginDuration.FromSeconds(3), 0.0)));

        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_NonfinitePhysicalRate_IsNeverAcceptedByRetry()
    {
        var initial = World();
        var calls = 0;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(10), Options(10, 1), (energy, duration) =>
            {
                calls++;
                return new PlanetOriginEnergyStep(duration, double.NaN, 0.0,
                    new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
                    new PlanetRadiativeExchange(
                        new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
                        PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere), []);
            }));

        Assert.AreEqual(1, calls);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_MillionYearHorizon_PreservesGeologicalClockWithoutMicrosecondOverflow()
    {
        var initial = World();
        var horizon = new PlanetOriginDuration(1_000_000, 123_456L);
        var options = new PlanetOriginAdaptiveIntegrationOptions(horizon, PlanetOriginDuration.FromSeconds(1.0),
            MaximumAcceptedSubsteps: 1);
        var calls = 0;

        var advanced = initial.AdvanceCoupledAdaptive(horizon, options, (candidate, duration) =>
        {
            calls++;
            Assert.AreEqual(horizon, duration);
            return CoolingStep(duration, 0.0);
        });

        Assert.AreEqual(1, calls);
        Assert.AreEqual(initial.Clock.YearsSinceSolarSystemFormation + 1_000_000L,
            advanced.Clock.YearsSinceSolarSystemFormation);
        Assert.AreEqual(123_456L, advanced.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, advanced.Revision);
        Assert.AreEqual(0.0, advanced.EnergyConservationResidualJoules, 1e-5);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_ThousandYearInterval_PreservesExactSubyearTail()
    {
        var initial = World();
        var horizon = new PlanetOriginDuration(1_000L, 1_000_001L);
        var options = new PlanetOriginAdaptiveIntegrationOptions(
            PlanetOriginDuration.FromYears(400), PlanetOriginDuration.FromYears(1));
        var accepted = new List<PlanetOriginDuration>();

        var result = initial.AdvanceCoupledAdaptive(horizon, options, (candidate, duration) =>
        {
            accepted.Add(duration);
            return CoolingStep(duration, 0.0);
        });

        CollectionAssert.AreEqual(new[]
        {
            PlanetOriginDuration.FromYears(400),
            PlanetOriginDuration.FromYears(400),
            new PlanetOriginDuration(200, 1_000_001)
        }, accepted);
        Assert.AreEqual(initial.Clock.YearsSinceSolarSystemFormation + 1_000L,
            result.Clock.YearsSinceSolarSystemFormation);
        Assert.AreEqual(1_000_001L, result.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-5);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_LongIntervalOverdraw_RefinesAcrossYearBoundaryAndReevaluatesRates()
    {
        var initial = World();
        var horizon = PlanetOriginDuration.FromYears(1_000);
        var options = new PlanetOriginAdaptiveIntegrationOptions(horizon, PlanetOriginDuration.FromYears(250));
        var proposed = new List<PlanetOriginDuration>();

        var result = initial.AdvanceCoupledAdaptive(horizon, options, (candidate, duration) =>
        {
            proposed.Add(duration);
            var stored = candidate.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere);
            return CoolingStep(duration, stored / (750.0 * PlanetThermalEnergyBudget.JulianYearSeconds));
        });

        CollectionAssert.AreEqual(new[]
        {
            PlanetOriginDuration.FromYears(1_000),
            PlanetOriginDuration.FromYears(500),
            PlanetOriginDuration.FromYears(500)
        }, proposed);
        Assert.AreEqual(initial.Clock.YearsSinceSolarSystemFormation + 1_000L,
            result.Clock.YearsSinceSolarSystemFormation);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-5);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_IntervalPastPresent_IsRejectedWithoutPublishing()
    {
        var initial = World();
        var beyondPresent = PlanetOriginDuration.FromYears(
            PlanetOriginClock.EarthAccretionReferenceAgeYearsBeforePresent + 1);
        var options = new PlanetOriginAdaptiveIntegrationOptions(beyondPresent, PlanetOriginDuration.FromYears(1));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => initial.AdvanceCoupledAdaptive(
            beyondPresent, options, (energy, duration) => CoolingStep(duration, 0.0)));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_SaturationCrossingWithoutPhaseKinetics_RejectsWholeEpoch()
    {
        var initial = SteamWorld(330.0);
        var conditions = SteamConditions();
        var options = Options(10, 1) with { SaturationSafety = conditions };
        var calls = 0;

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(10), options, (energy, duration) =>
            {
                calls++;
                var stored = energy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere);
                return CoolingStep(duration, stored * 0.02);
            }));

        Assert.IsTrue(calls > 1, "Crossing requires physical substep refinement before reaching the minimum.");
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_ValidAtmosphericCooling_ReevaluatesAndPreservesEnergy()
    {
        var initial = SteamWorld(330.0);
        var conditions = SteamConditions();
        var options = Options(5, 1) with { SaturationSafety = conditions };
        var observed = new List<double>();

        var result = initial.AdvanceCoupledAdaptive(PlanetOriginDuration.FromSeconds(10), options, (energy, duration) =>
        {
            var stored = energy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.Atmosphere);
            observed.Add(stored);
            return CoolingStep(duration, stored * 0.0001);
        });

        Assert.AreEqual(2, observed.Count);
        Assert.IsTrue(observed[1] < observed[0]);
        Assert.IsTrue(result.Temperatures!.Value.AtmosphereTemperatureKelvin > 320.0);
        Assert.AreEqual(10_000_000L, result.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, result.WaterConservationResidualKilograms, 0.0);
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_RejectsUnsupportedHotSteamRegimeWithoutPublishing()
    {
        var initial = SteamWorld(700.0);
        var options = Options(5, 1) with { SaturationSafety = SteamConditions() };

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupledAdaptive(
            PlanetOriginDuration.FromSeconds(5), options, (energy, duration) => CoolingStep(duration, 1.0)));

        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupledAdaptive_ReservesRepresentableTailInsteadOfCreatingTooShortLastStep()
    {
        var initial = World();
        var durations = new List<double>();

        var result = initial.AdvanceCoupledAdaptive(PlanetOriginDuration.FromSeconds(11), Options(10, 3),
            (energy, duration) =>
            {
                durations.Add(duration.TotalSeconds);
                return CoolingStep(duration, 1.0);
            });

        CollectionAssert.AreEqual(new[] { 8.0, 3.0 }, durations);
        Assert.AreEqual(11_000_000L, result.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-6);
        AssertUnchanged(initial);
    }

    private static PlanetOriginAdaptiveIntegrationOptions Options(double maximumSeconds, double minimumSeconds) =>
        new(PlanetOriginDuration.FromSeconds(maximumSeconds), PlanetOriginDuration.FromSeconds(minimumSeconds));

    private static PlanetOriginCondensationSafetyConditions SteamConditions() => new(
        new PlanetMixedAtmosphereParameters(1.0, 9.81, 500.0, 0.02897),
        MaximumValidatedSurfacePressurePascals: 8_000.0,
        MaximumPhysicalSubstep: PlanetOriginDuration.FromSeconds(10.0));

    private static PlanetOriginEpochState SteamWorld(double temperatureKelvin)
    {
        var thermal = ThermalProperties with { NoncondensableAtmosphereMassKilograms = 500.0 };
        var water = PlanetWaterInventory.Empty.Deliver(PlanetWaterReservoir.AtmosphericVapor, 5_000.0);
        var heatCapacity = 500.0 * thermal.NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin +
            5_000.0 * thermal.VaporSpecificHeatJoulesPerKilogramKelvin;
        return CreateWorld(water, thermal, heatCapacity * temperatureKelvin);
    }

    private static PlanetOriginEpochState World() => CreateWorld(
        PlanetWaterInventory.Empty, ThermalProperties, 3_000_000.0);

    private static PlanetOriginEpochState CreateWorld(
        PlanetWaterInventory water, PlanetEnvironmentHeatCapacityParameters properties, double atmosphereJoules)
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0), rock);
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(atmosphereJoules, 0.0, 10_000_000.0));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(interior, environment, properties));
    }

    private static PlanetOriginEnergyStep CoolingStep(PlanetOriginDuration duration, double emittedWatts) =>
        new(duration, 0.0, 0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, emittedWatts),
                PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere), []);

    private static void AssertUnchanged(PlanetOriginEpochState initial)
    {
        Assert.AreEqual(0L, initial.Revision);
        Assert.AreEqual(0L, initial.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(0.0, initial.EnergyConservationResidualJoules, 1e-5);
        Assert.AreEqual(0.0, initial.WaterConservationResidualKilograms, 0.0);
    }
}
