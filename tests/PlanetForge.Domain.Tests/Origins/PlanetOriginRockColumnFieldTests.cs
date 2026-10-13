using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetOriginRockColumnFieldTests
{
    private const double Radius = 6_371_000.0;
    private static readonly PlanetOriginSurfaceRockPhaseMaterial Material = new(850.0, 400_000.0, 1_300.0, 1_900.0);
    private static PlanetOriginRegionalAirlessParameters Boundary => new(new PlanetVector(1.0, 0.0, 0.0), 0.0, 0.0, 0.9, 3.0, 25.0);

    [TestMethod]
    public void Partition_FundedMoltenSurface_PreservesMassEnergyAndUniformDepthTemperature()
    {
        var state = CreateState();
        foreach (var column in state.Columns!.Columns)
        {
            var aggregate = state.Field.Cells[state.Field.Layout.GetIndex(column.Id)];
            Assert.AreEqual(aggregate.ActiveRockMassKilograms, column.MassKilograms, aggregate.ActiveRockMassKilograms * 1e-12);
            Assert.AreEqual(aggregate.StoredEnthalpyJoules, column.StoredEnthalpyJoules, aggregate.StoredEnthalpyJoules * 1e-12);
            Assert.IsTrue(column.Layers.All(layer => Math.Abs(layer.Resolve(Material).TemperatureKelvin - 2_200.0) < 1e-8));
            Assert.AreEqual(0.0, column.SolidSurfaceThicknessMeters(Material, state.Columns.Properties), 0.0);
        }
        Assert.AreEqual(state.Field.TotalStoredEnthalpyJoules,
            state.Epoch.CoupledEnergy!.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), state.Field.TotalStoredEnthalpyJoules * 1e-12);
    }

    [TestMethod]
    public void Advance_RadiationOnly_CoolsOnlyOwnedSurfaceLayerWithoutHeatingMantle()
    {
        var state = CreateState(conductivity: 0.0);
        var next = state.Advance(PlanetOriginDuration.FromSeconds(1.0), Boundary with { ConductivityWattsPerMeterKelvin = 0.0 });
        var before = state.Columns!.Columns[0];
        var after = next.Columns!.Columns[0];
        Assert.IsLessThan(before.Layers[0].StoredEnthalpyJoules, after.Layers[0].StoredEnthalpyJoules);
        for (var depth = 1; depth < before.Layers.Count; depth++) Assert.AreEqual(before.Layers[depth], after.Layers[depth]);
        Assert.AreEqual(state.Epoch.Thermal.EnergyBudget.StoredEnergyJoules, next.Epoch.Thermal.EnergyBudget.StoredEnergyJoules, 0.0);
        Assert.IsGreaterThan(next.ResolveSurfacePhase(before.Id).TemperatureKelvin, next.Field.Cells[0].Resolve(Material).TemperatureKelvin);
        AssertConservation(next);
    }

    [TestMethod]
    public void Advance_InternalConduction_MovesHeatBetweenDepthLayersWithoutExternalEnergy()
    {
        var state = CreateState();
        var cooled = state.Advance(PlanetOriginDuration.FromSeconds(300.0), Boundary with { ConductivityWattsPerMeterKelvin = 0.0 });
        var next = cooled.Advance(PlanetOriginDuration.FromSeconds(300.0), Boundary with { InfraredEmissivity = 0.0, ConductivityWattsPerMeterKelvin = 0.0 });
        var before = cooled.Columns!.Columns[0];
        var after = next.Columns!.Columns[0];
        Assert.IsGreaterThan(before.Layers[0].StoredEnthalpyJoules, after.Layers[0].StoredEnthalpyJoules);
        Assert.IsLessThan(before.Layers[1].StoredEnthalpyJoules, after.Layers[1].StoredEnthalpyJoules);
        Assert.AreEqual(cooled.Field.TotalStoredEnthalpyJoules, next.Field.TotalStoredEnthalpyJoules, cooled.Field.TotalStoredEnthalpyJoules * 1e-12);
        AssertConservation(next);
    }

    [TestMethod]
    public void Advance_RadiativeCooling_GeneratesSolidSurfaceAboveMoltenDepthAndRealVolumeContraction()
    {
        var initial = CreateState();
        var state = AdvanceHours(initial, 4);
        var column = state.Columns!.Columns[0];
        Assert.AreEqual(0.0, column.Layers[0].Resolve(Material).MeltFraction, 0.0);
        Assert.IsGreaterThan(0.9, column.Layers[^1].Resolve(Material).MeltFraction);
        Assert.IsGreaterThan(0.0, column.SolidSurfaceThicknessMeters(Material, state.Columns.Properties));
        Assert.IsLessThan(column.ReferenceThicknessMeters, column.ThicknessMeters(Material, state.Columns.Properties));
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, state.Field.TotalActiveRockMassKilograms, initial.Field.TotalActiveRockMassKilograms * 1e-12);
        AssertConservation(state);
    }

    [TestMethod]
    public void Advance_ActualStellarHeat_RemeltsSolidSurfaceAndAccountsForIncomingEnergy()
    {
        var cold = AdvanceHours(CreateState(), 4);
        var subsolar = cold.Columns!.Columns.OrderByDescending(column => PlanetSurfaceGridGeometry.GetCenterDirection(column.Id).X).First();
        Assert.AreEqual(0.0, cold.ResolveSurfacePhase(subsolar.Id).MeltFraction, 0.0);
        var heated = cold.Advance(PlanetOriginDuration.FromSeconds(120.0), Boundary with { SolarIrradianceWattsPerSquareMeter = 1e8 });
        Assert.IsGreaterThan(0.0, heated.ResolveSurfacePhase(subsolar.Id).MeltFraction);
        var column = heated.Columns!.Columns[heated.Field.Layout.GetIndex(subsolar.Id)];
        Assert.AreEqual(0.0, column.SolidSurfaceThicknessMeters(Material, heated.Columns.Properties), 0.0);
        Assert.IsGreaterThan(cold.Epoch.CoupledEnergy!.CumulativeAbsorbedStellarEnergyJoules, heated.Epoch.CoupledEnergy!.CumulativeAbsorbedStellarEnergyJoules);
        AssertConservation(heated);
    }

    [TestMethod]
    public void Advance_InstabilityCancellationOrUnsupportedLateralFlux_LeavesAllDepthLayersUnpublished()
    {
        var state = CreateState();
        var original = state.Columns!.Columns[0].Layers.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Advance(PlanetOriginDuration.FromSeconds(3_600.0), Boundary));
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Advance(PlanetOriginDuration.FromSeconds(1.0),
            Boundary with { LateralConductivityWattsPerMeterKelvin = 3.0, LateralLayerThicknessMeters = 1.0 }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => state.Advance(PlanetOriginDuration.FromSeconds(1.0), Boundary, cancellation.Token));
        Assert.AreEqual(0L, state.Epoch.Revision);
        CollectionAssert.AreEqual(original, state.Columns.Columns[0].Layers.ToArray());
    }

    [TestMethod]
    public void Advance_FinerSubsteps_ConvergesOverSamePhysicalIntervalAndReplays()
    {
        var start = CreateState();
        PlanetOriginCanonicalRockCells Integrate(int count)
        {
            var state = start;
            for (var i = 0; i < count; i++) state = state.Advance(PlanetOriginDuration.FromSeconds(300.0 / count), Boundary);
            Assert.AreEqual(300_000_000L, state.Epoch.Clock.MicrosecondsIntoYear);
            AssertConservation(state);
            return state;
        }
        double Temperature(PlanetOriginCanonicalRockCells state) => state.ResolveSurfacePhase(state.Field.Cells[0].Id).TemperatureKelvin;
        var reference = Temperature(Integrate(1_000));
        var coarse = Temperature(Integrate(1));
        var medium = Temperature(Integrate(2));
        var fine = Integrate(4);
        var replay = Integrate(4);
        Assert.IsLessThan(Math.Abs(coarse - reference), Math.Abs(medium - reference));
        Assert.IsLessThan(Math.Abs(medium - reference), Math.Abs(Temperature(fine) - reference));
        CollectionAssert.AreEqual(fine.Columns!.Columns[0].Layers.ToArray(), replay.Columns!.Columns[0].Layers.ToArray());
    }

    private static PlanetOriginCanonicalRockCells AdvanceHours(PlanetOriginCanonicalRockCells state, int hours)
    {
        for (var i = 0; i < hours * 12; i++) state = state.Advance(PlanetOriginDuration.FromSeconds(300.0), Boundary);
        return state;
    }

    private static void AssertConservation(PlanetOriginCanonicalRockCells state)
    {
        Assert.AreEqual(0.0, state.Epoch.EnergyConservationResidualJoules, state.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(state.Field.TotalStoredEnthalpyJoules,
            state.Epoch.CoupledEnergy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock), state.Field.TotalStoredEnthalpyJoules * 2e-12);
        Assert.AreEqual(0.0, state.Epoch.WaterConservationResidualKilograms, 0.0);
    }

    [TestMethod]
    public void BoundedIntegration_ReevaluatesAdmission_ReachesExactClockAndReplaysWithoutPublishingInitial()
    {
        var initial = CreateState();
        var duration = PlanetOriginDuration.FromSeconds(3_600.123456);
        Assert.ThrowsExactly<InvalidOperationException>(() => initial.Advance(duration, Boundary));
        var next = PlanetOriginRockColumnIntegrator.Advance(initial, duration, Boundary, new(3_600.0, 10_000));
        var replay = PlanetOriginRockColumnIntegrator.Advance(initial, duration, Boundary, new(3_600.0, 10_000));
        Assert.AreEqual(initial.Epoch.Clock.MicrosecondsIntoYear + 3_600_123_456L, next.Epoch.Clock.MicrosecondsIntoYear);
        Assert.IsGreaterThan(1L, next.Epoch.Revision);
        Assert.AreEqual(0L, initial.Epoch.Revision);
        CollectionAssert.AreEqual(next.Columns!.Columns[0].Layers.ToArray(), replay.Columns!.Columns[0].Layers.ToArray());
        AssertConservation(next);
        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetOriginRockColumnIntegrator.Advance(initial, duration, Boundary, new(3_600.0, 1)));
        Assert.AreEqual(0L, initial.Epoch.Revision);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetOriginRockColumnIntegrator.Advance(initial, duration, Boundary, new(30.0, 10_000), cancellation.Token));
    }

    [TestMethod]
    public void BoundedIntegration_AccuracyCapRefinement_ConvergesSurfaceEnthalpyAtIdenticalTime()
    {
        var initial = CreateState();
        var interval = PlanetOriginDuration.FromSeconds(300.0);
        PlanetOriginCanonicalRockCells Integrate(double cap) => PlanetOriginRockColumnIntegrator.Advance(initial, interval, Boundary, new(cap, 10_000));
        var coarse = Integrate(30.0);
        var refined = Integrate(15.0);
        var reference = Integrate(1.0);
        double Error(PlanetOriginCanonicalRockCells state) => Math.Abs(state.Columns!.Columns[0].Layers[0].StoredEnthalpyJoules -
            reference.Columns!.Columns[0].Layers[0].StoredEnthalpyJoules);
        Assert.IsLessThan(Error(coarse), Error(refined));
        Assert.AreEqual(coarse.Epoch.Clock, refined.Epoch.Clock);
        Assert.AreEqual(refined.Epoch.Clock, reference.Epoch.Clock);
        AssertConservation(coarse);
        AssertConservation(refined);
    }

    [TestMethod]
    public void DepthRefinement_FourHourMatchedRun_ExposesUnresolvedThermalFront()
    {
        // Actual model integration, not recorded data. Reduce only the geographic
        // grid (to the six cube faces); depth physics, per-area rock mass,
        // mantle, radiation, elapsed time and substep cap remain identical.
        PlanetOriginRockColumnResolutionSample[] samples = new PlanetOriginRockColumnResolutionSample[4];
        var depths = new[] { 8, 16, 32, 64 };
        for (var i = 0; i < depths.Length; i++)
        {
            var initial = CreateState(layerCount: depths[i], gridLevel: 0);
            var cooled = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(14_400.0),
                Boundary with { SolarIrradianceWattsPerSquareMeter = 1_361.0 },
                new PlanetOriginRockColumnIntegrationOptions(5.0, 10_000));
            Assert.AreEqual(14_400_000_000L, cooled.Epoch.Clock.MicrosecondsIntoYear);
            Assert.AreEqual(0L, initial.Epoch.Revision);
            AssertConservation(cooled);
            Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, cooled.Field.TotalActiveRockMassKilograms,
                initial.Field.TotalActiveRockMassKilograms * 1e-12);
            samples[i] = PlanetOriginRockColumnResolutionSample.FromColumn(
                cooled.Columns!.Columns[0], cooled.Columns.Material, cooled.Columns.Properties);
            Assert.AreEqual(depths[i], samples[i].LayerCount);
            Console.WriteLine($"Depth={depths[i]}, fully solid={samples[i].FullySolidTopMeters:F6} m, " +
                $"unresolved cell={samples[i].UnresolvedCellWidthMeters:F6} m, " +
                $"surface={samples[i].SurfaceTemperatureKelvin:F2} K");
        }
        var screen = PlanetOriginRockColumnDepthRefinement.Screen(samples);
        Assert.IsFalse(screen.MeetsScreeningCriteria,
            "Current coarse thermal columns must not be represented as a converged quantitative primary-crust front.");
    }

    private static PlanetOriginCanonicalRockCells CreateState(double conductivity = 3.0, int layerCount = 8, int gridLevel = 2)
    {
        var mantle = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(4e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(mantle.CalculateStoredEnergyJoules(2_400.0)), mantle);
        const double mass = 1e19;
        var energy = new PlanetRockEnthalpyModel(Material.ToRockProperties(mass)).CalculateStoredEnergyJoules(2_200.0);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty, new PlanetEnvironmentalEnergyBudget(0.0, 0.0, energy));
        var capacity = new PlanetEnvironmentHeatCapacityParameters(0.0, 1_000.0, 2_000.0, 4_200.0, mass, 850.0) { SurfaceRockPhaseMaterial = Material };
        var ledger = new PlanetOriginEnergyLedger(interior, environment, capacity);
        var field = PlanetOriginSurfaceRockPhaseField.FromCanonicalPhaseSurface(ledger, new PlanetSurfaceGridLayout(gridLevel), Radius);
        var columns = PlanetOriginRockColumnField.Partition(field, new PlanetOriginRockColumnProperties(layerCount, 3_000.0, 2_800.0, conductivity));
        return new PlanetOriginCanonicalRockCells(new PlanetOriginEpochState(ledger), field, columns);
    }
}
