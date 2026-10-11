using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetGeologicalRegionSessionTests
{
    // The pinned MSTest 3.10 package does not expose TestContext.CancellationToken.
    private CancellationTokenSource timeout = null!;
    private CancellationToken TestCancellation => timeout.Token;

    [TestInitialize]
    public void Initialize() => timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    [TestCleanup]
    public void Cleanup() => timeout.Dispose();
    private static readonly PlanetGeologicalEpoch Present = PlanetGeologicalEpoch.Present;
    private static readonly PlanetGeologicalRegionId A = Region(CubeFace.PositiveZ);
    private static readonly PlanetGeologicalRegionId B = Region(CubeFace.PositiveX);

    [TestMethod]
    public async Task RestoreAsync_LeaveAndReturnAfterCacheRecreation_RecoversIdenticalTerrain()
    {
        var documents = new Documents();
        documents.Put(History(A, 20));
        documents.Put(History(B, 60));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var first = Session(documents, provider);
        await first.RestoreAsync([A], Present, TestCancellation);
        var captured = provider.CaptureSnapshot();
        var expected = Height(provider, A);
        Assert.AreEqual(980.0, expected, 1e-6);
        await first.RestoreAsync([B], Present, TestCancellation);
        Assert.AreEqual(1_000.0, Height(provider, A));
        Assert.AreEqual(940.0, Height(provider, B), 1e-6);

        var restarted = Session(documents, provider);
        await restarted.RestoreAsync([A], Present, TestCancellation);
        Assert.AreEqual(expected, Height(provider, A));
        Assert.AreEqual(expected, Height(captured, A), "Captured geometry must keep its original immutable source.");
        Assert.AreEqual(3L, provider.Revision);
        Assert.AreEqual(3, documents.Reads);
    }

    [TestMethod]
    public async Task RestoreAsync_MissingOrCorruptArchive_PreservesEntirePublishedSelection()
    {
        var documents = new Documents();
        documents.Put(History(A, 20));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        await session.RestoreAsync([A], Present, TestCancellation);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => session.RestoreAsync([A, B], Present, TestCancellation));
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreEqual(980.0, Height(provider, A), 1e-6);
        documents.Values[PlanetGeologicalRegionRepository.Key(B, Present)] = "corrupt";
        await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(() => session.RestoreAsync([A, B], Present, TestCancellation));
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreEqual(980.0, Height(provider, A), 1e-6);
    }

    [TestMethod]
    public async Task RestoreAsync_NewerSelectionWhileStorageIsBlocked_OnlyNewestRequestPublishes()
    {
        var documents = new Documents { BlockNextRead = true };
        documents.Put(History(A, 20));
        documents.Put(History(B, 60));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        var obsolete = session.RestoreAsync([A], Present, TestCancellation);
        await documents.Entered.Task.WaitAsync(TestCancellation);
        var latest = session.RestoreAsync([B], Present, TestCancellation);
        Assert.AreEqual(1, documents.Reads, "Only one request may load archives at a time.");
        documents.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => obsolete);
        await latest;
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreEqual(1_000.0, Height(provider, A));
        Assert.AreEqual(940.0, Height(provider, B), 1e-6);
    }

    [TestMethod]
    public async Task Clear_PendingStorageIgnoresCancellation_CannotResurrectOldTerrain()
    {
        var documents = new Documents { BlockNextRead = true };
        documents.Put(History(A, 20));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        var pending = session.RestoreAsync([A], Present, TestCancellation);
        await documents.Entered.Task.WaitAsync(TestCancellation);
        session.Clear();
        documents.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreEqual(1_000.0, Height(provider, A));
    }

    [TestMethod]
    public async Task RestoreAsync_CancellationAfterRead_PreservesTerrainAndAllowsRetry()
    {
        var documents = new Documents { BlockNextRead = true };
        documents.Put(History(A, 20));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestCancellation);
        var pending = session.RestoreAsync([A], Present, cancellation.Token);
        await documents.Entered.Task.WaitAsync(TestCancellation);
        cancellation.Cancel();
        documents.Release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(0L, provider.Revision);
        await session.RestoreAsync([A], Present, TestCancellation);
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreEqual(980.0, Height(provider, A), 1e-6);
    }

    [TestMethod]
    public async Task RestoreAsync_InvalidSelection_RejectsBeforeReadingStorage()
    {
        var documents = new Documents();
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => session.RestoreAsync([], Present, TestCancellation));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => session.RestoreAsync(Enumerable.Repeat(A, 9).ToArray(), Present, TestCancellation));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => session.RestoreAsync([A, A], Present, TestCancellation));
        var otherWorld = PlanetGeologicalRegionId.CreateCurrent(99, B.Tile);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => session.RestoreAsync([A, otherWorld], Present, TestCancellation));
        var obsolete = new PlanetGeologicalRegionId(new PlanetWorldIdentity(24061984, new PlanetGenerationVersion(1)), A.Tile);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => session.RestoreAsync([obsolete], Present, TestCancellation));
        Assert.AreEqual(0, documents.Reads);
        Assert.AreEqual(0L, provider.Revision);
    }

    [TestMethod]
    public async Task RestoreAsync_GridBudgetExceeded_DoesNotPublishPartialAtlas()
    {
        var documents = new Documents();
        var c = Region(CubeFace.PositiveY);
        foreach (var region in new[] { A, B, c }) documents.Put(History(region, 20, width: 512));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => session.RestoreAsync([A, B, c], Present, TestCancellation));
        Assert.AreEqual(0L, provider.Revision);
        Assert.AreEqual(1_000.0, Height(provider, A));
    }

    [TestMethod]
    public async Task RestoreAsync_DifferentEpoch_UsesOnlyRequestedSavedEpoch()
    {
        var documents = new Documents();
        var past = new PlanetGeologicalEpoch(1_000_000);
        documents.Put(History(A, 20));
        documents.Put(History(A, 70, past));
        var provider = new PlanetTerrainRegionProvider(new FlatSource());
        var session = Session(documents, provider);
        await session.RestoreAsync([A], past, TestCancellation);
        Assert.AreEqual(930.0, Height(provider, A), 1e-6);
        await session.RestoreAsync([A], Present, TestCancellation);
        Assert.AreEqual(980.0, Height(provider, A), 1e-6);
    }

    private static PlanetGeologicalRegionSession Session(Documents documents, PlanetTerrainRegionProvider provider) =>
        new(new PlanetGeologicalRegionRepository(documents, new PlanetGeologicalRegionCache(1)), provider);

    private static PlanetGeologicalRegionId Region(CubeFace face) =>
        PlanetGeologicalRegionId.CreateCurrent(24061984, new PlanetTileId(face, 0, 0, 0));

    private static double Height(IPlanetElevationSource source, PlanetGeologicalRegionId region) =>
        source.SampleElevationMeters(region.CenterDirection, region.World.Seed);

    private static PlanetGeologicalRegionHistory History(PlanetGeologicalRegionId region, float cut,
        PlanetGeologicalEpoch epoch = default, int width = 16)
    {
        var original = PlanetRegionalGeologyEvolution.Initialize(region.World.Seed, region.ToString(),
            width, width, 1_000, Enumerable.Repeat(1_000f, width * width).ToArray());
        var evolved = original with
        {
            Iteration = 1,
            ElevationMeters = Enumerable.Repeat(1_000f - cut, width * width).ToArray(),
            CumulativeErodedVolumeCubicMeters = cut * width * width * 1_000_000.0,
            CumulativeExportedVolumeCubicMeters = cut * width * width * 1_000_000.0
        };
        return PlanetGeologicalRegionHistory.Create(region, epoch, 6_371_000, original, evolved);
    }

    private sealed class FlatSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000;
    }

    private sealed class Documents : IPlanetGeologicalRegionDocumentStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockNextRead { get; set; }
        public int Reads { get; private set; }

        public void Put(PlanetGeologicalRegionHistory history) =>
            Values[PlanetGeologicalRegionRepository.Key(history.Region, history.Epoch)] = PlanetGeologicalRegionArchive.Serialize(history);

        public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken)
        {
            Reads++;
            if (BlockNextRead)
            {
                BlockNextRead = false;
                Entered.SetResult();
                // Deliberately ignore cancellation to model an already-started JS storage read.
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
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
