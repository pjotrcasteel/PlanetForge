using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Infrastructure.Surface;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetFormationSimulationTests
{
    [TestMethod]
    public async Task AdvanceAsync_YieldingBatches_PreservesExactSynchronousEnthalpiesAndCancelsAtomically()
    {
        var synchronous = CreateExperience();
        var asynchronous = CreateExperience();
        PlanetFormationSimulation.Start(synchronous, 24061984);
        PlanetFormationSimulation.Start(asynchronous, 24061984);
        PlanetFormationSimulation.Advance(synchronous, 30);
        await PlanetFormationSimulation.AdvanceAsync(asynchronous, 30);
        CollectionAssert.AreEqual(synchronous.PublishedRegionalRock!.Columns!.Columns.SelectMany(column => column.Layers).ToArray(),
            asynchronous.PublishedRegionalRock!.Columns!.Columns.SelectMany(column => column.Layers).ToArray());
        Assert.AreEqual(synchronous.PublishedOriginEpoch!.Clock, asynchronous.PublishedOriginEpoch!.Clock);
        var original = asynchronous.PublishedRegionalRock;
        using var cancellation = new CancellationTokenSource();
        var pending = PlanetFormationSimulation.AdvanceAsync(asynchronous, 30, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pending);
        Assert.AreSame(original, asynchronous.PublishedRegionalRock);
    }

    [TestMethod]
    public void Advance_MantleConvection_ChangesBasalFluxWithoutInventingEnergy()
    {
        var experience = CreateExperience();
        PlanetFormationSimulation.Start(experience, 24061984);
        var initial = experience.PublishedRegionalRock!;
        var boundary = initial.SurfaceBoundary!.Value;
        var physics = PlanetOriginMagmaConvection.Diagnose(boundary, initial.Columns!, initial.Epoch.Thermal);
        Assert.IsTrue(physics.RayleighNumber > 1708);
        Assert.IsTrue(physics.NusseltNumber > 1.0);
        Assert.IsTrue(physics.EffectiveBoundaryThicknessMeters < 25);
        Assert.IsTrue(physics.MeanUpwardHeatFluxWattsPerSquareMeter > 0);
        var viscous = boundary with
        {
            MagmaConvection = PlanetOriginMagmaConvectionParameters.Reference with { DynamicViscosityPascalSeconds = 1.0 }
        };
        Assert.IsTrue(PlanetOriginMagmaConvection.Diagnose(viscous, initial.Columns!, initial.Epoch.Thermal).NusseltNumber < physics.NusseltNumber);
        var invalid = boundary with { MagmaConvection = PlanetOriginMagmaConvectionParameters.Reference with { DepthMeters = 0 } };
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => invalid.Validate());
        var interval = PlanetOriginDuration.FromSeconds(1);
        var settings = new PlanetOriginRockColumnIntegrationOptions(0.25, 1000);
        var convecting = PlanetOriginRockColumnIntegrator.Advance(initial, interval, boundary, settings);
        var reference = PlanetOriginRockColumnIntegrator.Advance(initial, interval, boundary with { MagmaConvection = null }, settings);
        var last = initial.Columns!.Columns[0].Layers.Count - 1;
        Assert.AreNotEqual(reference.Columns!.Columns[0].Layers[last].StoredEnthalpyJoules,
            convecting.Columns!.Columns[0].Layers[last].StoredEnthalpyJoules);
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, convecting.Field.TotalActiveRockMassKilograms, 0.0);
        Assert.AreEqual(0.0, convecting.Epoch.EnergyConservationResidualJoules,
            convecting.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetOriginRockColumnIntegrator.Advance(initial, interval, boundary, settings, cancellation.Token));
        Assert.AreSame(initial, experience.PublishedRegionalRock);
    }

    [TestMethod]
    public void Start_NewWorld_IsMoltenDryAndCanonicalBeforeFirstRender()
    {
        var experience = CreateExperience();
        var snapshot = PlanetFormationSimulation.Start(experience, 24061984);
        var world = experience.PublishedRegionalRock!;
        Assert.IsNotNull(snapshot.Origin);
        Assert.IsNotNull(snapshot.SpatialRock);
        Assert.AreEqual(0L, world.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(0.0, snapshot.Origin.SurfaceLiquidMassKilograms, 0.0);
        Assert.AreEqual(0.0, snapshot.Origin.AtmosphericSteamMassKilograms, 0.0);
        Assert.AreEqual(0.0, snapshot.AtmosphereDensity, 0.0);
        Assert.IsTrue(world.Field.Cells.All(c => world.ResolveSurfacePhase(c.Id).MeltFraction == 1.0));
        Assert.IsTrue(world.Field.Cells.Max(c => world.ResolveRadiatingSurfaceTemperature(c.Id)) -
            world.Field.Cells.Min(c => world.ResolveRadiatingSurfaceTemperature(c.Id)) > 200.0);
        Assert.AreEqual(24061984, snapshot.Seed);
    }

    [TestMethod]
    public void Advance_ShortCoolingSequence_ChangesOwnedPhaseAndAtlasWithoutSwitchingWorldOrLosingEnergy()
    {
        var experience = CreateExperience();
        PlanetFormationSimulation.Start(experience, 24061984);
        var initial = experience.PublishedRegionalRock!;
        var initialProjection = PlanetOriginSpatialSurfaceProjection.Create(initial);
        for (var i = 0; i < 6; i++) PlanetFormationSimulation.Advance(experience, 5, CancellationToken.None);
        var current = experience.PublishedRegionalRock!;
        Assert.AreEqual(30_000_000L, current.Epoch.Clock.MicrosecondsIntoYear);
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, current.Field.TotalActiveRockMassKilograms, initial.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(0.0, current.Epoch.EnergyConservationResidualJoules, current.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.IsTrue(current.Field.Cells.Any(c => current.ResolveSurfacePhase(c.Id).MeltFraction < 1.0));
        Assert.IsTrue(current.Columns!.Columns.All(c => c.Layers[^1].Resolve(current.Field.Material).MeltFraction == 1.0));
        var projection = PlanetOriginSpatialSurfaceProjection.Create(current);
        Assert.IsTrue(projection.TemperatureKelvin.Zip(initialProjection.TemperatureKelvin).All(pair => pair.First < pair.Second));
        Assert.IsTrue(projection.MeltFraction.Any(f => f < 1.0f));
        Assert.AreEqual(0L, initial.Epoch.Revision);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => PlanetFormationSimulation.Advance(experience, 1, cancellation.Token));
        Assert.AreSame(current, experience.PublishedRegionalRock);
        var replay = CreateExperience();
        PlanetFormationSimulation.Start(replay, 24061984);
        for (var i = 0; i < 6; i++) PlanetFormationSimulation.Advance(replay, 5, CancellationToken.None);
        CollectionAssert.AreEqual(current.Columns.Columns[0].Layers.ToArray(), replay.PublishedRegionalRock!.Columns!.Columns[0].Layers.ToArray());
    }

    [TestMethod]
    public void Start_AfterEvolution_ReplaysInitialStateAndClearsTime()
    {
        var experience = CreateExperience();
        var initial = PlanetFormationSimulation.Start(experience, 24061984);
        PlanetFormationSimulation.Advance(experience, 5, CancellationToken.None);
        var restarted = PlanetFormationSimulation.Start(experience, 24061984);
        Assert.AreEqual(0L, experience.PublishedOriginEpoch!.Clock.MicrosecondsIntoYear);
        CollectionAssert.AreEqual(initial.SpatialRock!.TemperatureKelvin, restarted.SpatialRock!.TemperatureKelvin);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetFormationSimulation.Advance(experience, 0));
    }

    [TestMethod]
    public void Publish_MaterialFaceTransaction_UpdatesTheSameCanonicalGlobeAndPhysicalRelief()
    {
        var source = new PlanetTerrainRegionProvider(new FlatElevation());
        var experience = new PlanetExperience(new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(new PlanetSurfaceTileSampler(source), source)),
            new PlanetLocalSurfacePatchSampler(source), new PlanetLocalSurfaceMeshBuilder(),
            hypsometryCache: new PlanetHypsometryCache(new PlanetHypsometrySampler(source), source));
        var initialRender = PlanetFormationSimulation.Start(experience, 24061984);
        var initial = experience.PublishedRegionalRock!;
        var donor = initial.Columns!.Columns[0];
        var recipientId = PlanetSurfaceGridTopology.GetNeighbor(donor.Id, PlanetGridDirection.East).Cell;
        var recipientIndex = initial.Field.Layout.GetIndex(recipientId);
        var depth = donor.Layers.Count - 1;
        var mass = donor.Layers[depth].MassKilograms * 0.01;
        // A supplied material transaction verifies publication; it is not a fabricated default flow field.
        var candidate = initial.AdvanceWithMeltTransfers(PlanetOriginDuration.FromSeconds(0.01), initial.SurfaceBoundary!.Value,
            [new(donor.Id, depth, recipientId, depth, mass)]);
        Assert.AreSame(initial, experience.PublishedRegionalRock);
        var rendered = experience.PublishRegionalRock(candidate);
        Assert.AreSame(candidate, experience.PublishedRegionalRock);
        Assert.AreEqual(candidate.Epoch.Revision, rendered.SpatialRock!.Revision);
        Assert.AreNotEqual(initialRender.TerrainRevision, rendered.TerrainRevision);
        var expected = new PlanetOriginRockColumnElevationSource(candidate.Columns!);
        var donorDirection = PlanetSurfaceGridGeometry.GetCenterDirection(donor.Id);
        var recipientDirection = PlanetSurfaceGridGeometry.GetCenterDirection(recipientId);
        Assert.AreEqual(expected.SampleElevationMeters(donorDirection, 24061984), source.SampleElevationMeters(donorDirection, 24061984), 1e-9);
        Assert.AreEqual(expected.SampleElevationMeters(recipientDirection, 24061984), source.SampleElevationMeters(recipientDirection, 24061984), 1e-9);
        Assert.IsTrue(expected.SampleElevationMeters(donorDirection, 24061984) < 0.0);
        Assert.IsTrue(expected.SampleElevationMeters(recipientDirection, 24061984) > 0.0);
        Assert.AreEqual(initial.Field.TotalActiveRockMassKilograms, candidate.Field.TotalActiveRockMassKilograms, initial.Field.TotalActiveRockMassKilograms * 1e-12);
        Assert.AreEqual(0.0, candidate.Epoch.EnergyConservationResidualJoules, candidate.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.AreEqual(10_000L, candidate.Epoch.Clock.MicrosecondsIntoYear);
    }

    private static PlanetExperience CreateExperience()
    {
        var source = new PlanetTerrainRegionProvider(new FlatElevation());
        return new(new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(new PlanetSurfaceTileSampler(source), source)),
            new PlanetLocalSurfacePatchSampler(source), new PlanetLocalSurfaceMeshBuilder(),
            hypsometryCache: new PlanetHypsometryCache(new PlanetHypsometrySampler(source), source));
    }

    private sealed class FlatElevation : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
