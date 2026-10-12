using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginCondensationSafetyGateTests
{
    private static readonly PlanetOriginDuration Minute = PlanetOriginDuration.FromSeconds(60.0);

    private static PlanetEnvironmentHeatCapacityParameters HeatCapacities => new(
        NoncondensableAtmosphereMassKilograms: 500.0,
        NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin: 1_000.0,
        VaporSpecificHeatJoulesPerKilogramKelvin: 2_000.0,
        LiquidWaterSpecificHeatJoulesPerKilogramKelvin: 4_000.0,
        SurfaceRockMassKilograms: 2_000.0,
        SurfaceRockSpecificHeatJoulesPerKilogramKelvin: 800.0);

    private static PlanetOriginCondensationSafetyConditions SafeConditions => new(
        new PlanetMixedAtmosphereParameters(
            PlanetRadiusMeters: 1.0,
            SurfaceGravityMetersPerSecondSquared: 9.81,
            DryGasMassKilograms: 500.0,
            DryGasMolarMassKilogramsPerMole: 0.02897),
        MaximumValidatedSurfacePressurePascals: 8_000.0,
        MaximumPhysicalSubstep: PlanetOriginDuration.FromSeconds(600.0));

    [TestMethod]
    public void AdvanceCoupled_SupersaturatedProposedCondensation_ConservesWaterAndEnergy()
    {
        var initial = World();
        var step = Step(100.0);

        var final = initial.AdvanceCoupled(step);

        Assert.AreEqual(1L, final.Revision);
        Assert.AreEqual(60_000_000L, final.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(4_900.0, final.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(1_100.0, final.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, final.EnergyConservationResidualJoules, 1e-4);
        Assert.AreEqual(0.0, final.WaterConservationResidualKilograms, 0.0);
        Assert.AreEqual(0L, initial.Revision);
        Assert.AreEqual(5_000.0, initial.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
    }

    [TestMethod]
    public void AdvanceCoupled_OverCondensationWithoutSteamOverdraw_RejectsWholeEpoch()
    {
        var initial = World();
        // The atmosphere owns enough vapour (5,000 kg), but at 300 K
        // approximately 650 kg at most is eligible in the idealized column.
        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupled(Step(1_000.0)));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_CondensationThatHeatsAtmospherePastSaturation_IsRejected()
    {
        var initial = World();
        var step = Step(100.0, PlanetEnvironmentHeatReservoir.Atmosphere);

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupled(step));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_UncertifiedTotalPressure_RejectsCondensation()
    {
        var initial = World();
        var conditions = SafeConditions with { MaximumValidatedSurfacePressurePascals = 1_000.0 };

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupled(Step(100.0, guard: conditions)));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_OutsideIapwsLiquidVaporTemperatures_RejectsRainProposals()
    {
        var cold = World(250.0);
        var hot = World(700.0);

        Assert.ThrowsExactly<InvalidOperationException>(() => cold.AdvanceCoupled(Step(100.0)));
        Assert.ThrowsExactly<InvalidOperationException>(() => hot.AdvanceCoupled(Step(100.0)));
        AssertUnchanged(cold);
        AssertUnchanged(hot);
    }

    [TestMethod]
    public void AdvanceCoupled_DifferentDryGasMassForThermalAndHydrostaticModels_Rejects()
    {
        var initial = World();
        var conditions = SafeConditions with
        {
            Atmosphere = SafeConditions.Atmosphere with { DryGasMassKilograms = 700.0 }
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupled(Step(100.0, guard: conditions)));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_UnsupportedCombinedPhaseProposals_RejectRatherThanInventCoupling()
    {
        var initial = World();
        var step = Step(100.0) with
        {
            PhaseChanges =
            [
                Phase(100.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
                new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Freeze, 1.0, 1_000_000.0),
                    PlanetEnvironmentHeatReservoir.SurfaceRock)
            ]
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => initial.AdvanceCoupled(step));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_OverlongStep_IsRejectedBeforeMaterialTransfers()
    {
        var initial = World();
        var duration = PlanetOriginDuration.FromSeconds(900.0);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            initial.AdvanceCoupled(Step(100.0, duration: duration)));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_UnvalidatedGuardParameters_RejectWithoutPublication()
    {
        var initial = World();
        var badPressure = SafeConditions with { MaximumValidatedSurfacePressurePascals = double.NaN };
        var badDuration = SafeConditions with { MaximumPhysicalSubstep = new PlanetOriginDuration(0) };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            initial.AdvanceCoupled(Step(100.0, guard: badPressure)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            initial.AdvanceCoupled(Step(100.0, guard: badDuration)));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_SecondRejectedSubstep_DoesNotPublishFirstSuccessfulCondensation()
    {
        var initial = World();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            initial.AdvanceCoupled([Step(100.0), Step(1_000.0)]));
        AssertUnchanged(initial);
    }

    [TestMethod]
    public void AdvanceCoupled_LegacyProposalWithoutOptIn_RemainsBackwardCompatible()
    {
        var initial = World();
        var unguarded = Step(1_000.0) with { CondensationSafety = null };

        var result = initial.AdvanceCoupled(unguarded);

        Assert.AreEqual(1L, result.Revision);
        Assert.AreEqual(4_000.0, result.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(0.0, result.EnergyConservationResidualJoules, 1e-4);
        Assert.AreEqual(0.0, result.WaterConservationResidualKilograms, 0.0);
    }

    private static PlanetOriginEpochState World(double atmosphereTemperatureKelvin = 300.0)
    {
        var props = HeatCapacities;
        var water = PlanetWaterInventory.Empty
            .Deliver(PlanetWaterReservoir.AtmosphericVapor, 5_000.0)
            .Deliver(PlanetWaterReservoir.SurfaceLiquid, 1_000.0);
        var atmosphereCapacity = 500.0 * props.NoncondensableAtmosphereSpecificHeatJoulesPerKilogramKelvin +
            5_000.0 * props.VaporSpecificHeatJoulesPerKilogramKelvin;
        var environment = new PlanetWaterEnvironmentalState(water,
            new PlanetEnvironmentalEnergyBudget(
                atmosphereCapacity * atmosphereTemperatureKelvin,
                1_000.0 * props.LiquidWaterSpecificHeatJoulesPerKilogramKelvin * 300.0,
                2_000.0 * props.SurfaceRockSpecificHeatJoulesPerKilogramKelvin * 300.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(2_000.0),
            new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0)));
        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(interior, environment, props));
    }

    private static PlanetOriginEnergyStep Step(
        double condensedKilograms,
        PlanetEnvironmentHeatReservoir latentDestination = PlanetEnvironmentHeatReservoir.SurfaceRock,
        PlanetOriginCondensationSafetyConditions? guard = null,
        PlanetOriginDuration? duration = null) =>
        new(duration ?? Minute, 0.0, 0.0,
            new PlanetInteriorHeatExport(0.0, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(
                new PlanetRadiativeBalance(1.0, 0.0, 0.0, 0.0, 0.0),
                PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.Atmosphere),
            [Phase(condensedKilograms, latentDestination)])
        {
            CondensationSafety = guard ?? SafeConditions
        };

    private static PlanetWaterPhaseSensibleHeatStep Phase(
        double mass, PlanetEnvironmentHeatReservoir latentDestination) =>
        new(new PlanetWaterPhaseTransition(PlanetWaterPhaseTransitionKind.Condense, mass, 2_000_000.0),
            latentDestination);

    private static void AssertUnchanged(PlanetOriginEpochState original)
    {
        Assert.AreEqual(0L, original.Revision);
        Assert.AreEqual(0L, original.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(5_000.0, original.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 0.0);
        Assert.AreEqual(1_000.0, original.Water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid), 0.0);
        Assert.AreEqual(0.0, original.EnergyConservationResidualJoules, 1e-4);
    }
}
