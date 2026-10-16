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
        Assert.IsTrue(current.Field.Cells.All(c => current.ResolveSurfacePhase(c.Id).MeltFraction < 1.0));
        Assert.IsTrue(current.Columns!.Columns.All(c => c.Layers[^1].Resolve(current.Field.Material).MeltFraction == 1.0));
        var projection = PlanetOriginSpatialSurfaceProjection.Create(current);
        Assert.IsTrue(projection.TemperatureKelvin.Max() < initialProjection.TemperatureKelvin.Min());
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
    public void ResolvedFormation_EarlyCoolingLargerStepCap_StaysWithinThermalErrorBudget()
    {
        var experience = CreateExperience();
        var snapshot = PlanetFormationSimulation.Start(experience, 24061984);
        var initial = experience.PublishedRegionalRock!;
        var boundary = new PlanetOriginRegionalAirlessParameters(
            PlanetVector.UnitX, snapshot.Physics.SolarFluxWattsPerSquareMeter, 0.15, 0.9, 3.0, 25.0);
        var duration = PlanetOriginDuration.FromSeconds(30.0);
        var options = new PlanetOriginRockColumnIntegrationOptions(0.25, 20_000);
        var candidateOptions = new PlanetOriginRockColumnIntegrationOptions(2.5, 20_000);
        _ = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), boundary, options);
        _ = PlanetOriginRockColumnIntegrator.Advance(initial, PlanetOriginDuration.FromSeconds(1.0), boundary, candidateOptions);

        var reference = PlanetOriginRockColumnIntegrator.Advance(initial, duration, boundary, options);
        var candidate = PlanetOriginRockColumnIntegrator.Advance(initial, duration, boundary, candidateOptions);
        var maximumFaceTemperatureDifference = initial.Field.Cells.Max(cell =>
            Math.Abs(reference.ResolveRadiatingSurfaceTemperature(cell.Id) - candidate.ResolveRadiatingSurfaceTemperature(cell.Id)));
        var maximumTopMeltDifference = initial.Field.Cells.Max(cell =>
            Math.Abs(reference.ResolveSurfacePhase(cell.Id).MeltFraction - candidate.ResolveSurfacePhase(cell.Id).MeltFraction));

        Assert.AreEqual(reference.Epoch.Clock, candidate.Epoch.Clock);
        var referenceSubsteps = reference.Epoch.Revision - initial.Epoch.Revision;
        var candidateSubsteps = candidate.Epoch.Revision - initial.Epoch.Revision;
        Assert.IsTrue(candidateSubsteps * 5 <= referenceSubsteps,
            $"The 2.5 s cap used {candidateSubsteps} substeps; the 0.25 s reference used {referenceSubsteps}. " +
            $"Maximum face-temperature error {maximumFaceTemperatureDifference:R} K, top melt error {maximumTopMeltDifference:R}.");
        Assert.AreEqual(0.0, candidate.Epoch.EnergyConservationResidualJoules,
            candidate.Epoch.CoupledEnergy!.InitialStoredEnergyJoules * 1e-12);
        Assert.IsLessThan(8.0, maximumFaceTemperatureDifference);
        Assert.IsLessThan(0.02, maximumTopMeltDifference);
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
