using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetOriginSurfacePublicationTests
{
    private const double Radius = 6_371_000.0;

    [TestMethod]
    public void ColumnElevation_InitialDatumAndContraction_ArePhysicalAndIndependentOfLegacyTerrain()
    {
        var initial = CreateWorld();
        var initialSource = new PlanetOriginRockColumnElevationSource(initial.Columns!);
        Assert.AreEqual(0.0, initialSource.SampleElevationMeters(PlanetVector.UnitZ, 1), 0.0);
        var cooled = Cool(initial);
        var source = new PlanetOriginRockColumnElevationSource(cooled.Columns!);
        foreach (var column in cooled.Columns!.Columns)
        {
            var expected = column.ThicknessMeters(cooled.Columns.Material, cooled.Columns.Properties) - column.ReferenceThicknessMeters;
            Assert.AreEqual(expected, source.SampleElevationMeters(PlanetSurfaceGridGeometry.GetCenterDirection(column.Id), 1), 1e-9);
        }
        var first = source.SampleElevationMeters(new(1.0, 0.0, 1.0 - 1e-8), 1);
        var second = source.SampleElevationMeters(new(1.0 - 1e-8, 0.0, 1.0), 2);
        Assert.AreEqual(first, second, 1e-7, "Reconstruction must stay continuous across cube faces and independent of a procedural seed.");
        Assert.IsTrue(first < 0.0 && first > -1.0, "Density contraction is not a licence to invent mountains.");
    }

    [TestMethod]
    public void PublishOriginSurface_Failure_RestoresExactSourceAndDoesNotReuseFailedRevision()
    {
        var provider = new PlanetTerrainRegionProvider(new LegacyElevation());
        var old = provider.CaptureSnapshot();
        long failedRevision = -1;
        Assert.ThrowsExactly<InvalidOperationException>(() => provider.PublishOriginSurface<int>(CreateWorld().Columns, () =>
        {
            failedRevision = provider.Revision;
            Assert.AreEqual(0.0, provider.SampleElevationMeters(PlanetVector.UnitZ, 1), 0.0);
            throw new InvalidOperationException("Projection failure after sampling candidate geometry.");
        }));
        Assert.AreSame(old, provider.CaptureSnapshot());
        Assert.AreEqual(0L, provider.Revision);
        provider.PublishOriginSurface(CreateWorld().Columns, () => true);
        Assert.IsGreaterThan(failedRevision, provider.Revision);
        Assert.ThrowsExactly<InvalidOperationException>(() => provider.ClearLayers());
        provider.PublishOriginSurface<bool>(null, () => true);
        Assert.AreSame(old, provider.CaptureSnapshot());
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, 1), 0.0);
    }

    [TestMethod]
    public async Task PublishOriginSurface_ConcurrentReader_OnlySeesCommittedWorldAndTerrain()
    {
        var provider = new PlanetTerrainRegionProvider(new LegacyElevation());
        using var candidateReady = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var readerStarted = new ManualResetEventSlim();
        var revision = 0L;
        var writer = Task.Run(() => provider.PublishOriginSurface(CreateWorld().Columns, () =>
        {
            candidateReady.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            revision = 1L;
            return true;
        }));
        Assert.IsTrue(candidateReady.Wait(TimeSpan.FromSeconds(5)));
        var reader = Task.Run(() =>
        {
            readerStarted.Set();
            return provider.ReadConsistent(() => (revision, provider.SampleElevationMeters(PlanetVector.UnitZ, 1)));
        });
        try
        {
            Assert.IsTrue(readerStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(reader.IsCompleted, "Readers cannot combine a proposed terrain with the previous physical world.");
        }
        finally { finish.Set(); }
        await writer;
        var result = await reader;
        Assert.AreEqual(1L, result.revision);
        Assert.AreEqual(0.0, result.Item2, 0.0);
    }

    [TestMethod]
    public void Experience_ColumnPublish_UpdatesOrbitGroundAndTopPhaseTogetherAndRollsBackRejectedClock()
    {
        var provider = new PlanetTerrainRegionProvider(new LegacyElevation());
        var experience = CreateExperience(provider);
        var initial = CreateWorld();
        var snapshot = experience.PublishRegionalRock(initial);
        Assert.AreSame(initial, experience.PublishedRegionalRock);
        Assert.AreEqual(initial.Epoch.Revision, snapshot.SpatialRock!.Revision);
        var tile = new PlanetSurfaceTileSampler(provider).Sample(new(CubeFace.PositiveZ, 0, 0, 0), 8, 1);
        var patch = new PlanetLocalSurfacePatchSampler(provider).Sample(PlanetVector.UnitZ, 2_000.0, 8, 1, Radius, CancellationToken.None);
        Assert.AreEqual(0.0, tile.GetPoint(4, 4).ElevationMeters, 0.0);
        Assert.AreEqual(tile.GetPoint(4, 4).ElevationMeters, patch.GetPoint(4, 4).ElevationMeters, 1e-9);
        var source = provider.CaptureSnapshot();
        var revision = provider.Revision;
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishRegionalRock(initial));
        Assert.AreSame(initial, experience.PublishedRegionalRock);
        Assert.AreSame(source, provider.CaptureSnapshot());
        Assert.AreEqual(revision, provider.Revision);
        var next = Cool(initial);
        var after = experience.PublishRegionalRock(next);
        Assert.AreEqual(next.Epoch.Revision, after.SpatialRock!.Revision);
        Assert.AreSame(next.Epoch, experience.PublishedOriginEpoch);
        Assert.IsTrue(provider.SampleElevationMeters(PlanetVector.UnitZ, 1) < 0.0);
        experience.ClearOriginEpoch();
        Assert.IsNull(experience.PublishedOriginEpoch);
        Assert.IsNull(experience.PublishedRegionalRock);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, 1), 0.0);
    }

    [TestMethod]
    public void Experience_UnsupportedDisplayTemperature_RestoresAcceptedEpochTerrainAndGeometryGeneration()
    {
        var provider = new PlanetTerrainRegionProvider(new LegacyElevation());
        var experience = CreateExperience(provider);
        var initial = CreateWorld();
        experience.PublishRegionalRock(initial);
        var elevation = provider.CaptureSnapshot();
        var revision = provider.Revision;
        var heated = initial.Advance(PlanetOriginDuration.FromSeconds(1.0), new(PlanetVector.UnitX, 1e12, 0.0, 0.0, 0.0, 25.0));
        Assert.ThrowsExactly<InvalidOperationException>(() => experience.PublishRegionalRock(heated));
        Assert.AreSame(initial, experience.PublishedRegionalRock);
        Assert.AreSame(initial.Epoch, experience.PublishedOriginEpoch);
        Assert.AreSame(elevation, provider.CaptureSnapshot());
        Assert.AreEqual(revision, provider.Revision);
        var snapshot = experience.CreateSnapshot();
        Assert.AreEqual(initial.Epoch.Revision, snapshot.SpatialRock!.Revision);
    }

    private static PlanetOriginCanonicalRockCells CreateWorld()
    {
        var initial = PlanetMagmaOceanReference.CreateInitial(Radius);
        var columns = PlanetOriginRockColumnField.Partition(initial.Field, new(8, 3_000.0, 2_800.0, 3.0));
        return new(initial.Epoch, columns.Aggregate, columns);
    }

    private static PlanetOriginCanonicalRockCells Cool(PlanetOriginCanonicalRockCells initial)
    {
        var state = initial;
        for (var i = 0; i < 48; i++) state = state.Advance(PlanetOriginDuration.FromSeconds(300.0),
            new(PlanetVector.UnitX, 0.0, 0.0, 0.9, 3.0, 25.0));
        return state;
    }

    private static PlanetExperience CreateExperience(PlanetTerrainRegionProvider source) => new(
        new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(new PlanetSurfaceTileSampler(source), source)),
        new PlanetLocalSurfacePatchSampler(source), new PlanetLocalSurfaceMeshBuilder(),
        hypsometryCache: new PlanetHypsometryCache(new PlanetHypsometrySampler(source), source));

    private sealed class LegacyElevation : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }
}
