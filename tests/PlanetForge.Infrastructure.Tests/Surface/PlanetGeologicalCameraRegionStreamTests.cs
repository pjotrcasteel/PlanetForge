using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetGeologicalCameraRegionStreamTests
{
    private const int Seed = 24061984;
    private const double RadiusMeters = 6_371_000.0;
    private static readonly PlanetGeologicalRegionId North = Region(CubeFace.PositiveZ);
    private static readonly PlanetGeologicalRegionId East = Region(CubeFace.PositiveX);

    [TestMethod]
    public void Select_NearCameraPicksCorrectSavedWorld_AndIgnoresFarOrbit()
    {
        Assert.AreEqual(North, PlanetGeologicalCameraRegionSelector.Select([East, North], Seed, View(PlanetVector.UnitZ)));
        Assert.IsNull(PlanetGeologicalCameraRegionSelector.Select([East], Seed, View(PlanetVector.UnitZ)));
        Assert.IsNull(PlanetGeologicalCameraRegionSelector.Select([North], Seed + 1, View(PlanetVector.UnitZ)));
        Assert.IsNull(PlanetGeologicalCameraRegionSelector.Select([North], Seed, View(PlanetVector.UnitZ, 3.0)));
        Assert.IsNull(PlanetGeologicalCameraRegionSelector.Select([North], Seed,
            View(PlanetVector.Normalize(new PlanetVector(0.15, 0.0, 1.0)))));
    }

    [TestMethod]
    public void TryParsePresentArchiveKey_CurrentWorldAndEpoch_RoundTrips_WithoutLoading()
    {
        var key = PlanetGeologicalRegionRepository.Key(North, PlanetGeologicalEpoch.Present);
        Assert.IsTrue(PlanetGeologicalCameraRegionSelector.TryParsePresentArchiveKey(key, out var parsed));
        Assert.AreEqual(North, parsed);
        Assert.IsFalse(PlanetGeologicalCameraRegionSelector.TryParsePresentArchiveKey(
            key.Replace("YBP:0", "YBP:50000000"), out _));
        Assert.IsFalse(PlanetGeologicalCameraRegionSelector.TryParsePresentArchiveKey(
            key.Replace("G18:", "G17:"), out _));
        Assert.IsFalse(PlanetGeologicalCameraRegionSelector.TryParsePresentArchiveKey(
            key.Replace(":L0:X0", ":L0:X5"), out _));
        Assert.IsFalse(PlanetGeologicalCameraRegionSelector.TryParsePresentArchiveKey(
            key.Replace(":F4:", ":F99:"), out _));
        Assert.IsFalse(PlanetGeologicalCameraRegionSelector.TryParsePresentArchiveKey("not-an-archive", out _));
    }

    [TestMethod]
    public async Task ObserveAsync_VisitLeaveAndRevisit_LoadsOnce_AndRestoresCanonicalOrbit()
    {
        var documents = new Documents();
        documents.Put(History(North, 30f));
        var (stream, provider) = CreateStream(documents);

        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]));
        Assert.AreEqual(970.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.AreEqual(North, stream.ActiveRegion);
        Assert.IsFalse(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]));
        Assert.AreEqual(1, documents.Reads);

        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ, 3.0), [North]));
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.IsNull(stream.ActiveRegion);
        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]));
        Assert.AreEqual(970.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.AreEqual(1, documents.Reads, "Revisiting should use the bounded in-memory history cache.");
    }

    [TestMethod]
    public async Task ObserveAsync_SwitchBetweenDistantRegions_DoesNotKeepOldGeology()
    {
        var documents = new Documents();
        documents.Put(History(North, 40f));
        documents.Put(History(East, 90f));
        var (stream, provider) = CreateStream(documents);

        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North, East]));
        Assert.AreEqual(960.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitX), [North, East]));
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.AreEqual(910.0, provider.SampleElevationMeters(PlanetVector.UnitX, Seed), 1e-4);
        Assert.AreEqual(East, stream.ActiveRegion);

        Assert.IsTrue(stream.Reset());
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitX, Seed), 1e-4);
        Assert.IsFalse(stream.Reset());
    }

    [TestMethod]
    public async Task ObserveAsync_OnlyGeographicTileNearby_RequiresActualPhysicalFootprint()
    {
        var documents = new Documents();
        documents.Put(History(North, 50f));
        var (stream, provider) = CreateStream(documents);
        var away = PlanetVector.Normalize(new PlanetVector(0.03, 0.0, 1.0));

        Assert.IsFalse(await stream.ObserveAsync(Seed, View(away), [North]));
        Assert.IsNull(stream.ActiveRegion);
        Assert.AreEqual(0L, provider.Revision);
        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]),
            "Moving toward the physical footprint must retry an earlier tile-level miss.");
        Assert.AreEqual(950.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.AreEqual(1, documents.Reads);
    }

    [TestMethod]
    public async Task ObserveAsync_SupersededBlockedRead_CannotPublishObsoleteRegion()
    {
        var documents = new Documents { BlockNextRead = true };
        documents.Put(History(North, 20f));
        var (stream, provider) = CreateStream(documents);
        var obsolete = stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]);
        await documents.Reading.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ, 3.0), [North]),
            "A camera retreat must invalidate an unfinished archive restoration.");
        documents.Release.TrySetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => obsolete);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
        Assert.IsNull(stream.ActiveRegion);
    }

    [TestMethod]
    public async Task ObserveAsync_NoSavedRegionsOrCorruptData_DoesNotInventGeology()
    {
        var documents = new Documents();
        var (stream, provider) = CreateStream(documents);
        Assert.IsFalse(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), []));
        Assert.IsFalse(await stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]));
        Assert.AreEqual(0L, provider.Revision);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);

        stream.Reset();
        documents.Values[PlanetGeologicalRegionRepository.Key(North, PlanetGeologicalEpoch.Present)] = "{broken-json";
        await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(
            () => stream.ObserveAsync(Seed, View(PlanetVector.UnitZ), [North]));
        Assert.IsNull(stream.ActiveRegion);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-4);
    }

    private static PlanetGeologicalRegionId Region(CubeFace face) =>
        PlanetGeologicalRegionId.CreateCurrent(Seed, new PlanetTileId(face, 0, 0, 0));

    private static PlanetSurfaceView View(PlanetVector direction, double distance = 1.15) =>
        new(direction, distance, 900, Math.PI / 4.2);

    private static PlanetGeologicalRegionHistory History(PlanetGeologicalRegionId region, float cut)
    {
        const int width = 17;
        const double spacing = 1_000.0;
        var initial = PlanetRegionalGeologyEvolution.Initialize(region.World.Seed, region.ToString(),
            width, width, spacing, Enumerable.Repeat(1_000f, width * width).ToArray());
        var evolved = initial with
        {
            Iteration = 1,
            ElevationMeters = Enumerable.Repeat(1_000f - cut, width * width).ToArray(),
            CumulativeErodedVolumeCubicMeters = cut * width * width * spacing * spacing,
            CumulativeExportedVolumeCubicMeters = cut * width * width * spacing * spacing
        };
        return PlanetGeologicalRegionHistory.Create(region, PlanetGeologicalEpoch.Present, RadiusMeters, initial, evolved);
    }

    private static (PlanetGeologicalCameraRegionStream Stream, PlanetTerrainRegionProvider Provider) CreateStream(Documents documents)
    {
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var repository = new PlanetGeologicalRegionRepository(documents, new PlanetGeologicalRegionCache(8));
        return (new PlanetGeologicalCameraRegionStream(repository, new PlanetGeologicalRegionSession(repository, provider)), provider);
    }

    private sealed class FlatSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }

    private sealed class Documents : IPlanetGeologicalRegionDocumentStore
    {
        public readonly Dictionary<string, string> Values = [];
        public readonly TaskCompletionSource Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }
        public bool BlockNextRead { get; set; }

        public void Put(PlanetGeologicalRegionHistory history) =>
            Values[PlanetGeologicalRegionRepository.Key(history.Region, history.Epoch)] = PlanetGeologicalRegionArchive.Serialize(history);

        public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken)
        {
            Reads++;
            if (BlockNextRead)
            {
                BlockNextRead = false;
                Reading.TrySetResult();
                await Release.Task; // Deliberately ignores cancellation to verify stale-read rejection.
            }
            return Values.GetValueOrDefault(key);
        }

        public Task SaveAsync(string key, string json, CancellationToken cancellationToken)
        {
            Values[key] = json;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
