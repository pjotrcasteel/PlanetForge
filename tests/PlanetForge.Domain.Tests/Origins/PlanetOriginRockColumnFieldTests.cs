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
    public void Advance_InstabilityCancellationOrExcessiveLateralFlux_LeavesAllDepthLayersUnpublished()
    {
        var state = CreateState();
        var original = state.Columns!.Columns[0].Layers.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Advance(PlanetOriginDuration.FromSeconds(3_600.0), Boundary));
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Advance(PlanetOriginDuration.FromSeconds(1.0),
            Boundary with { LateralConductivityWattsPerMeterKelvin = 1e30, LateralLayerThicknessMeters = 1.0 }));
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DepthRefinement_GradedRadiatingBoundary_MeasuresFourHourCooling(bool implicitTransport)
    {
        double previousFront = 0.0;
        double previousFace = 0.0;
        foreach (var count in implicitTransport ? new[] { 8, 16, 32, 64, 128 } : new[] { 8, 16, 32, 64 })
        {
            var initial = CreateState(layerCount: count, gridLevel: 0, grading: 2.0, interfaceBoundary: true, implicitTransport: implicitTransport);
            var cooled = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(14_400.0),
                Boundary with { SolarIrradianceWattsPerSquareMeter = 1_361.0 }, new(5.0, 1_000_000));
            AssertConservation(cooled);
            var columns = cooled.Columns!;
            var column = columns.Columns[0];
            var sample = PlanetOriginRockColumnResolutionSample.FromColumn(column, columns.Material, columns.Properties);
            var phase = column.Layers[0].Resolve(Material);
            var face = PlanetOriginRockRadiatingBoundary.ResolveTemperature(phase.TemperatureKelvin, 3.0,
                column.Layers[0].VolumeCubicMeters(Material, columns.Properties) / column.AreaSquareMeters * 0.5, 1_361.0, 0.9);
            var front = column.ReconstructSolidusDepthMeters(Material, columns.Properties, face);
            Assert.AreEqual(14_400_000_000L, cooled.Epoch.Clock.MicrosecondsIntoYear);
            Assert.AreEqual(0L, initial.Epoch.Revision);
            Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, cooled.Field.TotalActiveRockMassKilograms, initial.Field.TotalActiveRockMassKilograms * 1e-12);
            if (count >= 64)
            {
                Assert.IsTrue(Math.Abs(front - previousFront) < 0.001, "Fine-grid reconstructed solidus must agree within 1 mm.");
                Assert.IsTrue(Math.Abs(face - previousFace) < 1.0, "Fine-grid radiating face must agree within 1 K.");
            }
            if (count == 128)
            {
                Assert.IsTrue(sample.UnresolvedCellWidthMeters < 0.012);
                Assert.IsTrue(sample.FullySolidTopLayers >= 3);
            }
            previousFront = front;
            previousFace = face;
            Console.WriteLine($"GRADED implicit={implicitTransport} {count}: solid={sample.FullySolidTopMeters:F8}; front={front:F8}; width={sample.UnresolvedCellWidthMeters:F8}; centre={phase.TemperatureKelvin:F5}; face={face:F5}");
        }
    }

    [TestMethod]
    public void ImplicitTransport_TimeRefinement_ApproachesIndependentExplicitSolutionAndRejectsUnpublishedTrials()
    {
        var implicitInitial = CreateState(layerCount: 32, gridLevel: 0, grading: 2.0, interfaceBoundary: true, implicitTransport: true);
        var explicitInitial = CreateState(layerCount: 32, gridLevel: 0, grading: 2.0, interfaceBoundary: true);
        var interval = PlanetOriginDuration.FromSeconds(3600.0);
        var reference = PlanetOriginRockColumnIntegrator.Advance(explicitInitial, interval, Boundary, new(0.25, 100_000));
        double Error(double cap)
        {
            var result = PlanetOriginRockColumnIntegrator.Advance(implicitInitial, interval, Boundary, new(cap, 100_000));
            AssertConservation(result);
            return Math.Abs(result.ResolveSurfacePhase(result.Field.Cells[0].Id).TemperatureKelvin -
                reference.ResolveSurfacePhase(reference.Field.Cells[0].Id).TemperatureKelvin);
        }
        var coarse = Error(120.0);
        var medium = Error(30.0);
        var fine = Error(5.0);
        Console.WriteLine($"Implicit time errors vs 0.25 s explicit: {coarse:R}, {medium:R}, {fine:R} K");
        Assert.IsTrue(fine < medium && medium < coarse);
        Assert.IsTrue(fine < 0.5);
        Assert.ThrowsExactly<InvalidOperationException>(() => PlanetOriginRockColumnIntegrator.Advance(implicitInitial, interval, Boundary, new(30.0, 1)));
        Assert.AreEqual(0L, implicitInitial.Epoch.Revision);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetOriginRockColumnIntegrator.Advance(implicitInitial, interval, Boundary, new(30.0, 1000), cancellation.Token));
    }

    [TestMethod]
    public void Partition_GradedDepth_PreservesInitialThermodynamicsAndUsesDeclaredMesh()
    {
        var initial = CreateState(layerCount: 64, gridLevel: 0, grading: 2.0, interfaceBoundary: true);
        var column = initial.Columns!.Columns[0];
        Assert.AreEqual(column.MassKilograms / 4096.0, column.Layers[0].MassKilograms, column.MassKilograms * 1e-12);
        Assert.IsTrue(column.Layers.All(layer => Math.Abs(layer.Resolve(Material).TemperatureKelvin - 2200.0) < 1e-8));
        AssertConservation(initial);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateState(grading: double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateState(grading: 3.0));
    }

    [TestMethod]
    public void LateralHeat_UniformColumns_HasNoThermalSourceAndRetainsEveryLayerMass()
    {
        var state = CreateState(conductivity: 0.0, gridLevel: 0);
        var trial = state.Columns!.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(1.0), 3.0, 25.0);
        Assert.IsTrue(trial.TransportedJoules < state.Field.TotalStoredEnthalpyJoules * 1e-15);
        Assert.AreEqual(state.Field.TotalStoredEnthalpyJoules, trial.Candidate.Aggregate.TotalStoredEnthalpyJoules, state.Field.TotalStoredEnthalpyJoules * 1e-12);
        for (var index = 0; index < state.Columns.Columns.Count; index++)
            CollectionAssert.AreEqual(state.Columns.Columns[index].Layers.Select(layer => layer.MassKilograms).ToArray(),
                trial.Candidate.Columns[index].Layers.Select(layer => layer.MassKilograms).ToArray());
    }

    [TestMethod]
    public void LateralHeat_PhysicalSurfaceExtent_ExchangesHeatAcrossCubeSeamsWithoutMixingDeepLayers()
    {
        var state = CreateLateralContrast();
        var boundary = Boundary with { ConductivityWattsPerMeterKelvin = 0.0, InfraredEmissivity = 0.0,
            LateralConductivityWattsPerMeterKelvin = 1e16, LateralLayerThicknessMeters = 0.1 };
        var next = state.Advance(PlanetOriginDuration.FromSeconds(1.0), boundary);
        var before = state.Columns!.Columns;
        var after = next.Columns!.Columns;
        Assert.IsTrue(after.Any(column => column.Layers[0].StoredEnthalpyJoules != before[state.Field.Layout.GetIndex(column.Id)].Layers[0].StoredEnthalpyJoules));
        for (var index = 0; index < before.Count; index++)
        {
            for (var depth = 1; depth < before[index].Layers.Count; depth++)
                Assert.AreEqual(before[index].Layers[depth], after[index].Layers[depth]);
            Assert.AreEqual(before[index].MassKilograms, after[index].MassKilograms, 0.0);
        }
        var minimum = before.Min(column => column.Layers[0].Resolve(Material).TemperatureKelvin);
        var maximum = before.Max(column => column.Layers[0].Resolve(Material).TemperatureKelvin);
        Assert.IsTrue(after.All(column => column.Layers[0].Resolve(Material).TemperatureKelvin >= minimum - 1e-8 &&
            column.Layers[0].Resolve(Material).TemperatureKelvin <= maximum + 1e-8));
        Assert.IsTrue(after.Max(column => column.Layers[0].Resolve(Material).TemperatureKelvin) < maximum);
        AssertConservation(next);
    }

    [TestMethod]
    public void LateralHeat_StableSubsteps_ReplaysAndConvergesAtTheSameCanonicalTime()
    {
        var state = CreateLateralContrast();
        var boundary = Boundary with { ConductivityWattsPerMeterKelvin = 0.0, InfraredEmissivity = 0.0,
            LateralConductivityWattsPerMeterKelvin = 1e16, LateralLayerThicknessMeters = 25.0 };
        PlanetOriginCanonicalRockCells Integrate(double cap) => PlanetOriginRockColumnIntegrator.Advance(state,
            PlanetOriginDuration.FromSeconds(300.0), boundary, new(cap, 10000), CancellationToken.None);
        var coarse = Integrate(20.0);
        var fine = Integrate(10.0);
        var reference = Integrate(1.0);
        var replay = Integrate(10.0);
        double Error(PlanetOriginCanonicalRockCells candidate) => candidate.Columns!.Columns.Select((column, index) =>
            Math.Abs(column.Layers[0].Resolve(Material).TemperatureKelvin - reference.Columns!.Columns[index].Layers[0].Resolve(Material).TemperatureKelvin)).Max();
        Assert.IsTrue(Error(fine) < Error(coarse));
        Assert.AreEqual(coarse.Epoch.Clock, fine.Epoch.Clock);
        CollectionAssert.AreEqual(fine.Field.Cells.ToArray(), replay.Field.Cells.ToArray());
        AssertConservation(fine);
        AssertConservation(reference);
    }

    [TestMethod]
    public void LateralHeat_InstabilityOrCancellation_RejectsWithoutMutatingAnyOwnedLayer()
    {
        var state = CreateLateralContrast();
        var before = state.Columns!.Columns.SelectMany(column => column.Layers).ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Columns.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(1.0), 1e30, 25.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => state.Columns.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(1.0), -1.0, 25.0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => state.Columns.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(1.0), 3.0, 25.0, cancellation.Token));
        CollectionAssert.AreEqual(before, state.Columns.Columns.SelectMany(column => column.Layers).ToArray());
        Assert.AreEqual(0L, state.Epoch.Revision);
    }

    [TestMethod]
    public void MeltTransfers_OwnedLiquid_MovesActualMassEnthalpyAndReliefWithoutChangingDonorTemperature()
    {
        var state = CreateState(conductivity: 0.0, gridLevel: 1);
        var donor = state.Columns!.Columns[0];
        var recipientId = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.West).Cell;
        var recipientIndex = state.Field.Layout.GetIndex(recipientId);
        var recipient = state.Columns.Columns[recipientIndex];
        var mass = donor.Layers[0].MassKilograms * 0.01;
        var boundary = Boundary with { ConductivityWattsPerMeterKelvin = 0.0, InfraredEmissivity = 0.0 };
        var next = state.AdvanceWithMeltTransfers(PlanetOriginDuration.FromSeconds(1.0), boundary, [new(donor.Id, 0, recipientId, 0, mass)]);
        var afterDonor = next.Columns!.Columns[0];
        var afterRecipient = next.Columns.Columns[recipientIndex];
        Assert.AreEqual(donor.Layers[0].MassKilograms - mass, afterDonor.Layers[0].MassKilograms, donor.Layers[0].MassKilograms * 1e-12);
        Assert.AreEqual(recipient.Layers[0].MassKilograms + mass, afterRecipient.Layers[0].MassKilograms, recipient.Layers[0].MassKilograms * 1e-12);
        Assert.AreEqual(donor.Layers[0].Resolve(Material).TemperatureKelvin, afterDonor.Layers[0].Resolve(Material).TemperatureKelvin, 1e-8);
        Assert.IsTrue(afterDonor.ThicknessMeters(Material, state.Columns.Properties) < donor.ThicknessMeters(Material, state.Columns.Properties));
        Assert.IsTrue(afterRecipient.ThicknessMeters(Material, state.Columns.Properties) > recipient.ThicknessMeters(Material, state.Columns.Properties));
        Assert.AreEqual(1_000_000L, next.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(state.Field.TotalActiveRockMassKilograms, next.Field.TotalActiveRockMassKilograms, state.Field.TotalActiveRockMassKilograms * 1e-12);
        AssertConservation(next);
        for (var depth = 1; depth < donor.Layers.Count; depth++) Assert.AreEqual(donor.Layers[depth], afterDonor.Layers[depth]);
    }

    [TestMethod]
    public void MeltTransfers_IncomingMaterial_DoesNotHideOriginalDonorOverdraw()
    {
        var state = CreateState(conductivity: 0.0, gridLevel: 1);
        var donor = state.Columns!.Columns[0];
        var eastId = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.East).Cell;
        var westId = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.West).Cell;
        var mass = donor.Layers[0].MassKilograms;
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Columns.ProposeMeltTransfers(
            [new(donor.Id, 0, eastId, 0, mass * 0.20), new(eastId, 0, donor.Id, 0, mass * 0.20), new(donor.Id, 0, westId, 0, mass * 0.10)]));
        Assert.AreEqual(mass, state.Columns.Columns[0].Layers[0].MassKilograms, 0.0);
        Assert.AreEqual(0L, state.Epoch.Revision);
    }

    [TestMethod]
    public void MeltTransfers_SolidNonAdjacentOrNonOverlappingMaterial_IsRejectedWithoutPublishing()
    {
        var cold = AdvanceHours(CreateState(gridLevel: 0), 4);
        var donor = cold.Columns!.Columns[0];
        var recipientId = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.East).Cell;
        var mass = donor.Layers[0].MassKilograms * 0.01;
        Assert.AreEqual(0.0, donor.Layers[0].Resolve(Material).MeltFraction, 0.0);
        Assert.ThrowsExactly<InvalidOperationException>(() => cold.Columns.ProposeMeltTransfers([new(donor.Id, 0, recipientId, 0, mass)]));
        var hot = CreateState(conductivity: 0.0, gridLevel: 0);
        Assert.ThrowsExactly<ArgumentException>(() => hot.Columns!.ProposeMeltTransfers([new(donor.Id, 0, donor.Id, 0, mass)]));
        Assert.ThrowsExactly<InvalidOperationException>(() => hot.Columns!.ProposeMeltTransfers([new(donor.Id, 0, recipientId, 7, mass)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => hot.Columns!.ProposeMeltTransfers([new(donor.Id, 0, recipientId, 0, double.NaN)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => hot.Columns!.ProposeMeltTransfers([new(donor.Id, -1, recipientId, 0, mass)]));
        Assert.AreEqual(0L, hot.Epoch.Revision);
    }

    [TestMethod]
    public void MeltTransfers_DifferentTemperatures_UsesDonorSpecificEnthalpyAndReplays()
    {
        var state = CreateLateralContrast();
        var donor = state.Columns!.Columns.OrderByDescending(column => column.Layers[0].Resolve(Material).TemperatureKelvin).First();
        var recipientId = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.East).Cell;
        var recipient = state.Columns.Columns[state.Field.Layout.GetIndex(recipientId)];
        var mass = donor.Layers[0].MassKilograms * 0.01;
        PlanetOriginMeltFaceTransfer[] transfers = [new(donor.Id, 0, recipientId, 0, mass)];
        var trial = state.Columns.ProposeMeltTransfers(transfers);
        var replay = state.Columns.ProposeMeltTransfers(transfers);
        var expectedHeat = mass * donor.Layers[0].StoredEnthalpyJoules / donor.Layers[0].MassKilograms;
        Assert.AreEqual(expectedHeat, trial.TransportedEnthalpyJoules, expectedHeat * 1e-12);
        var after = trial.Candidate.Columns[state.Field.Layout.GetIndex(recipientId)];
        Assert.AreEqual(recipient.Layers[0].StoredEnthalpyJoules + expectedHeat, after.Layers[0].StoredEnthalpyJoules, expectedHeat * 1e-12);
        CollectionAssert.AreEqual(trial.Candidate.Aggregate.Cells.ToArray(), replay.Candidate.Aggregate.Cells.ToArray());
        Assert.AreEqual(0.0, trial.MassResidualKilograms, state.Field.TotalActiveRockMassKilograms * 2e-12);
        Assert.AreEqual(0.0, trial.EnthalpyResidualJoules, state.Field.TotalStoredEnthalpyJoules * 2e-12);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => state.Columns.ProposeMeltTransfers(transfers, cancellation.Token));
    }

    [TestMethod]
    public void LateralHeat_UnequalPhysicalLayerWidths_ExchangesAcrossDifferentDepthIndices()
    {
        var state = CreateMixedPhaseContrast();
        var cold = state.Columns!.Columns.OrderBy(column => column.Layers[0].Resolve(Material).TemperatureKelvin).First();
        var hotId = PlanetSurfaceGridTopology.GetNeighbor(cold.Id, PlanetGridDirection.East).Cell;
        var hot = state.Columns.Columns[state.Field.Layout.GetIndex(hotId)];
        var coldWidth = cold.Layers[0].VolumeCubicMeters(Material, state.Columns.Properties) / cold.AreaSquareMeters;
        var hotWidth = hot.Layers[0].VolumeCubicMeters(Material, state.Columns.Properties) / hot.AreaSquareMeters;
        Assert.IsTrue(coldWidth < hotWidth);
        var extent = (coldWidth + hotWidth) * 0.5;
        var trial = state.Columns.ProposeLateralHeat(PlanetOriginDuration.FromSeconds(1.0), 1e16, extent);
        var afterCold = trial.Candidate.Columns[state.Field.Layout.GetIndex(cold.Id)];
        var afterHot = trial.Candidate.Columns[state.Field.Layout.GetIndex(hot.Id)];
        Assert.IsTrue(afterCold.Layers[1].StoredEnthalpyJoules > cold.Layers[1].StoredEnthalpyJoules);
        Assert.AreEqual(hot.Layers[1], afterHot.Layers[1]);
        Assert.AreEqual(cold.Layers[2], afterCold.Layers[2]);
        Assert.AreEqual(0.0, trial.ConservationResidualJoules, state.Field.TotalStoredEnthalpyJoules * 2e-12);
    }

    [TestMethod]
    public void MeltTransfers_SolidRecipient_RequiresAnInterfaceModelRatherThanInjectingLiquidIntoRock()
    {
        var state = CreateMixedPhaseContrast();
        var cold = state.Columns!.Columns.OrderBy(column => column.Layers[0].Resolve(Material).TemperatureKelvin).First();
        var donorId = PlanetSurfaceGridTopology.GetNeighbor(cold.Id, PlanetGridDirection.East).Cell;
        var donor = state.Columns.Columns[state.Field.Layout.GetIndex(donorId)];
        Assert.AreEqual(0.0, cold.Layers[0].Resolve(Material).MeltFraction);
        Assert.AreEqual(1.0, donor.Layers[0].Resolve(Material).MeltFraction);
        Assert.ThrowsExactly<InvalidOperationException>(() => state.Columns.ProposeMeltTransfers(
            [new(donor.Id, 0, cold.Id, 0, donor.Layers[0].MassKilograms * 0.01)]));
    }

    private static PlanetOriginCanonicalRockCells CreateMixedPhaseContrast()
    {
        var state = CreateState(conductivity: 0.0, gridLevel: 0);
        var field = state.Field.RedistributeInitialEnergy(cell => cell.ActiveRockMassKilograms *
            (1.0 + PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id).X), state.Field.TotalStoredEnthalpyJoules * 0.60);
        var columns = PlanetOriginRockColumnField.Partition(field, state.Columns!.Properties);
        return new(state.Epoch, field, columns);
    }

    private static PlanetOriginCanonicalRockCells CreateLateralContrast()
    {
        var state = CreateState(conductivity: 0.0, gridLevel: 1);
        var field = state.Field.RedistributeInitialEnergy(cell => cell.ActiveRockMassKilograms *
            (1.0 + PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id).X), state.Field.TotalStoredEnthalpyJoules * 0.01);
        var columns = PlanetOriginRockColumnField.Partition(field, state.Columns!.Properties);
        return new(state.Epoch, field, columns);
    }

    private static PlanetOriginCanonicalRockCells CreateState(double conductivity = 3.0, int layerCount = 8, int gridLevel = 2, double grading = 1.0, bool interfaceBoundary = false, bool implicitTransport = false)
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
        var columns = PlanetOriginRockColumnField.Partition(field, new PlanetOriginRockColumnProperties(layerCount, 3_000.0, 2_800.0, conductivity)
        { DepthGradingExponent = grading, UseRadiatingInterface = interfaceBoundary, UseImplicitDepthTransport = implicitTransport });
        return new PlanetOriginCanonicalRockCells(new PlanetOriginEpochState(ledger), field, columns);
    }
}
