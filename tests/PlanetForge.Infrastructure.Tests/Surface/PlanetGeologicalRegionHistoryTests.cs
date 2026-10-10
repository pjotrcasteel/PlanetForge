using System.Text.Json;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetGeologicalRegionHistoryTests
{
    private const int Seed = 24061984;
    private const double Radius = 6_371_000.0;

    [TestMethod]
    public void Archive_SaveReloadAfterRestart_ReconstructsIdenticalGeologyAtSamePlanetaryAddress()
    {
        var id = Region(CubeFace.PositiveZ);
        var epoch = new PlanetGeologicalEpoch(80_000_000);
        var history = History(id, epoch, 74f);
        var expected = history.CreateOverlay().SampleDeltaMeters(id.CenterDirection, Seed);
        var json = PlanetGeologicalRegionArchive.Serialize(history);
        var restored = PlanetGeologicalRegionArchive.Deserialize(json);
        var cache = new PlanetGeologicalRegionCache(2);
        cache.Put(restored);

        Assert.IsTrue(cache.TryGet(id, epoch, out var loaded));
        Assert.IsNotNull(loaded);
        Assert.AreEqual(id, loaded.Region);
        Assert.AreEqual(epoch, loaded.Epoch);
        Assert.AreEqual(expected, loaded.CreateOverlay().SampleDeltaMeters(id.CenterDirection, Seed), 1e-6);
        Assert.AreEqual(0.0, loaded.CreateOverlay().SampleDeltaMeters(id.CenterDirection, Seed + 1), 1e-9);
        Assert.AreEqual(json, PlanetGeologicalRegionArchive.Serialize(loaded),
            "Exact region reload must not change the persisted physical heights or addressing.");
    }

    [TestMethod]
    public void Cache_RecentlyUsedRegionSurvivesEviction_AndRetrievedElevationsAreDefensive()
    {
        var cache = new PlanetGeologicalRegionCache(2);
        var epoch = PlanetGeologicalEpoch.Present;
        var a = Region(CubeFace.PositiveX);
        var b = Region(CubeFace.PositiveY);
        var c = Region(CubeFace.PositiveZ);
        cache.Put(History(a, epoch, 20f));
        cache.Put(History(b, epoch, 35f));
        Assert.IsTrue(cache.TryGet(a, epoch, out var loaded));
        Assert.IsNotNull(loaded);
        loaded.Original.ElevationMeters[0] = -999f;
        loaded.Evolved.ElevationMeters[0] = -999f;
        cache.Put(History(c, epoch, 50f));

        Assert.AreEqual(2, cache.Count);
        Assert.IsFalse(cache.TryGet(b, epoch, out _));
        Assert.IsTrue(cache.TryGet(a, epoch, out var reloaded));
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(1_000f, reloaded.Original.ElevationMeters[0]);
        Assert.AreEqual(980f, reloaded.Evolved.ElevationMeters[0]);
        Assert.IsTrue(cache.TryGet(c, epoch, out _));
    }

    [TestMethod]
    public void PublishHistories_ChangingEpoch_RestoresExactRockAndInvalidatesPlanetCache()
    {
        var location = Region(CubeFace.PositiveZ);
        var historyPast = History(location, new PlanetGeologicalEpoch(50_000_000), 80f);
        var historyPresent = History(location, PlanetGeologicalEpoch.Present, 25f);
        var provider = new PlanetTerrainRegionProvider(new FlatBedrock());
        var before = provider.CaptureSnapshot();

        Assert.AreEqual(1L, provider.PublishHistories(historyPast.Epoch, [historyPast]));
        Assert.AreEqual(920.0, provider.SampleElevationMeters(location.CenterDirection, Seed), 1e-5);
        Assert.AreEqual(2L, provider.PublishHistories(historyPresent.Epoch, [historyPresent]));
        Assert.AreEqual(975.0, provider.SampleElevationMeters(location.CenterDirection, Seed), 1e-5);
        Assert.AreEqual(1_000.0, before.SampleElevationMeters(location.CenterDirection, Seed), 1e-5);
        Assert.AreEqual(920.0, historyPast.CreateOverlay().SampleDeltaMeters(location.CenterDirection, Seed) + 1_000.0, 1e-5);
        Assert.AreEqual(3L, provider.ClearLayers());
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(location.CenterDirection, Seed), 1e-5);
    }

    [TestMethod]
    public void PublishHistories_ReorderedRegions_IsIndependentOfStoreIterationOrder()
    {
        var provider = new PlanetTerrainRegionProvider(new FlatBedrock());
        var epoch = PlanetGeologicalEpoch.Present;
        var a = History(Region(CubeFace.PositiveX), epoch, 50f);
        var b = History(Region(CubeFace.PositiveZ), epoch, 75f);
        provider.PublishHistories(epoch, [a, b]);
        var x = provider.SampleElevationMeters(a.Region.CenterDirection, Seed);
        var z = provider.SampleElevationMeters(b.Region.CenterDirection, Seed);

        provider.PublishHistories(epoch, [b, a]);
        Assert.AreEqual(x, provider.SampleElevationMeters(a.Region.CenterDirection, Seed), 1e-6);
        Assert.AreEqual(z, provider.SampleElevationMeters(b.Region.CenterDirection, Seed), 1e-6);
    }

    [TestMethod]
    public void PublishHistories_InvalidEpochWorldOrDuplicates_LeavesExistingGeologyUntouched()
    {
        var provider = new PlanetTerrainRegionProvider(new FlatBedrock());
        var current = History(Region(CubeFace.PositiveZ), PlanetGeologicalEpoch.Present, 20f);
        provider.PublishHistories(current.Epoch, [current]);
        var revision = provider.Revision;
        var height = provider.SampleElevationMeters(current.Region.CenterDirection, Seed);

        Assert.ThrowsExactly<ArgumentException>(() =>
            provider.PublishHistories(new PlanetGeologicalEpoch(1), [current]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            provider.PublishHistories(current.Epoch, [current, current]));
        var otherWorld = History(Region(CubeFace.PositiveX, Seed + 1), current.Epoch, 10f);
        Assert.ThrowsExactly<ArgumentException>(() =>
            provider.PublishHistories(current.Epoch, [current, otherWorld]));
        var oldWorld = new PlanetGeologicalRegionId(
            new PlanetWorldIdentity(Seed, new PlanetGenerationVersion(PlanetGenerationVersion.Current.Value - 1)),
            new PlanetTileId(CubeFace.NegativeZ, 0, 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            provider.PublishHistories(current.Epoch, [History(oldWorld, current.Epoch, 10f)]));
        Assert.AreEqual(revision, provider.Revision);
        Assert.AreEqual(height, provider.SampleElevationMeters(current.Region.CenterDirection, Seed));
    }

    [TestMethod]
    public void Archive_DifferentSchemaOrTamperedGeology_IsRejected()
    {
        var archive = PlanetGeologicalRegionArchive.Serialize(History(Region(CubeFace.PositiveZ),
            PlanetGeologicalEpoch.Present, 30f));
        var schema = archive.Replace("\"FormatVersion\":1", "\"FormatVersion\":999", StringComparison.Ordinal);
        var wrongAddress = archive.Replace("\"RegionKey\":\"G18:S24061984:F4:L0:X0:Y0\"",
            "\"RegionKey\":\"fabricated\"", StringComparison.Ordinal);
        var negativeTime = archive.Replace("\"YearsBeforePresent\":0", "\"YearsBeforePresent\":-1", StringComparison.Ordinal);

        Assert.ThrowsExactly<ArgumentException>(() => PlanetGeologicalRegionArchive.Deserialize(schema));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetGeologicalRegionArchive.Deserialize(wrongAddress));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetGeologicalRegionArchive.Deserialize(negativeTime));
        Assert.ThrowsExactly<JsonException>(() => PlanetGeologicalRegionArchive.Deserialize("{invalid-json"));
    }

    private static PlanetGeologicalRegionId Region(CubeFace face, int seed = Seed) =>
        PlanetGeologicalRegionId.CreateCurrent(seed, new PlanetTileId(face, 0, 0, 0));

    private static PlanetGeologicalRegionHistory History(PlanetGeologicalRegionId id,
        PlanetGeologicalEpoch epoch, float incisionMeters)
    {
        const int width = 17;
        const double spacing = 1_000.0;
        var original = Enumerable.Repeat(1_000f, width * width).ToArray();
        var initial = PlanetRegionalGeologyEvolution.Initialize(id.World.Seed, id.ToString(),
            width, width, spacing, original);
        var changed = Enumerable.Repeat(1_000f - incisionMeters, width * width).ToArray();
        var volume = incisionMeters * width * width * spacing * spacing;
        var evolved = initial with
        {
            Iteration = 1,
            ElevationMeters = changed,
            CumulativeErodedVolumeCubicMeters = volume,
            CumulativeExportedVolumeCubicMeters = volume
        };
        return PlanetGeologicalRegionHistory.Create(id, epoch, Radius, initial, evolved);
    }

    private sealed class FlatBedrock : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }
}
