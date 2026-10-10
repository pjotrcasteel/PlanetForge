using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetGeologicalRegionRepositoryTests
{
    private const int Seed = 24061984;
    private const double Radius = 6_371_000.0;

    [TestMethod]
    public async Task LoadAsync_AfterNewCacheAndRepository_ReplaysExactEpochAndHeight()
    {
        var documents = new MemoryDocuments();
        var region = PlanetGeologicalRegionId.CreateCurrent(Seed, new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0));
        var past = new PlanetGeologicalEpoch(50_000_000);
        var first = new PlanetGeologicalRegionRepository(documents, new PlanetGeologicalRegionCache(1));
        await first.SaveAsync(History(region, past, 78f));
        await first.SaveAsync(History(region, PlanetGeologicalEpoch.Present, 22f));

        var afterRestart = new PlanetGeologicalRegionRepository(documents, new PlanetGeologicalRegionCache(1));
        var recoveredPast = await afterRestart.LoadAsync(region, past);
        var recoveredPresent = await afterRestart.LoadAsync(region, PlanetGeologicalEpoch.Present);

        Assert.IsNotNull(recoveredPast);
        Assert.IsNotNull(recoveredPresent);
        Assert.AreEqual(-78.0, recoveredPast.CreateOverlay().SampleDeltaMeters(region.CenterDirection, Seed), 1e-6);
        Assert.AreEqual(-22.0, recoveredPresent.CreateOverlay().SampleDeltaMeters(region.CenterDirection, Seed), 1e-6);
        Assert.AreEqual(2, documents.Count);
        Assert.AreNotEqual(PlanetGeologicalRegionRepository.Key(region, past),
            PlanetGeologicalRegionRepository.Key(region, PlanetGeologicalEpoch.Present));
    }

    [TestMethod]
    public async Task DeleteAsync_InExistingCacheAndDurableStore_RemovesOnlySelectedEpoch()
    {
        var documents = new MemoryDocuments();
        var repo = new PlanetGeologicalRegionRepository(documents, new PlanetGeologicalRegionCache(2));
        var region = PlanetGeologicalRegionId.CreateCurrent(Seed, new PlanetTileId(CubeFace.PositiveY, 0, 0, 0));
        var past = new PlanetGeologicalEpoch(1_000_000);
        await repo.SaveAsync(History(region, past, 50f));
        await repo.SaveAsync(History(region, PlanetGeologicalEpoch.Present, 20f));

        await repo.DeleteAsync(region, past);
        Assert.IsNull(await repo.LoadAsync(region, past));
        Assert.IsNotNull(await repo.LoadAsync(region, PlanetGeologicalEpoch.Present));
        Assert.AreEqual(1, documents.Count);
    }

    [TestMethod]
    public async Task LoadAsync_CorruptArchiveOrWrongWorld_NeverEntersCache()
    {
        var documents = new MemoryDocuments();
        var cache = new PlanetGeologicalRegionCache(2);
        var repo = new PlanetGeologicalRegionRepository(documents, cache);
        var a = PlanetGeologicalRegionId.CreateCurrent(Seed, new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0));
        var b = PlanetGeologicalRegionId.CreateCurrent(Seed + 1, new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0));
        await documents.SaveAsync(PlanetGeologicalRegionRepository.Key(a, PlanetGeologicalEpoch.Present),
            PlanetGeologicalRegionArchive.Serialize(History(b, PlanetGeologicalEpoch.Present, 60f)), default);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await repo.LoadAsync(a, PlanetGeologicalEpoch.Present));
        Assert.AreEqual(0, cache.Count);

        await documents.SaveAsync(PlanetGeologicalRegionRepository.Key(a, PlanetGeologicalEpoch.Present),
            "invalid geological archive", default);
        await Assert.ThrowsExactlyAsync<System.Text.Json.JsonException>(async () =>
            await repo.LoadAsync(a, PlanetGeologicalEpoch.Present));
        Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    public async Task SaveAsync_StoreFailure_DoesNotReportCachedSuccess()
    {
        var documents = new MemoryDocuments { FailWrites = true };
        var cache = new PlanetGeologicalRegionCache(1);
        var repo = new PlanetGeologicalRegionRepository(documents, cache);
        var region = PlanetGeologicalRegionId.CreateCurrent(Seed, new PlanetTileId(CubeFace.NegativeX, 0, 0, 0));

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await repo.SaveAsync(History(region, PlanetGeologicalEpoch.Present, 10f)));
        Assert.AreEqual(0, cache.Count);
    }

    private static PlanetGeologicalRegionHistory History(PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch, float cut)
    {
        const int width = 16;
        const double spacing = 1_000.0;
        var initial = PlanetRegionalGeologyEvolution.Initialize(region.World.Seed, region.ToString(),
            width, width, spacing, Enumerable.Repeat(1_000f, width * width).ToArray());
        var eroded = initial with
        {
            Iteration = 1,
            ElevationMeters = Enumerable.Repeat(1_000f - cut, width * width).ToArray(),
            CumulativeErodedVolumeCubicMeters = cut * width * width * spacing * spacing,
            CumulativeExportedVolumeCubicMeters = cut * width * width * spacing * spacing
        };
        return PlanetGeologicalRegionHistory.Create(region, epoch, Radius, initial, eroded);
    }

    private sealed class MemoryDocuments : IPlanetGeologicalRegionDocumentStore
    {
        private readonly Dictionary<string, string> values = [];
        public bool FailWrites { get; set; }
        public int Count => values.Count;

        public Task<string?> LoadAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(values.GetValueOrDefault(key));
        }

        public Task SaveAsync(string key, string json, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailWrites) throw new IOException("Simulated quota error");
            values[key] = json;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
