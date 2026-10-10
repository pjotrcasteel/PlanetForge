using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetTerrainRegionProviderTests
{
    private const int Seed = 24061984;
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void ReplaceLayers_OverlappingRegions_ChangeWholePlanetSourceWithoutChangingCanonicalWorld()
    {
        var canonical = new FlatBedrock();
        var provider = new PlanetTerrainRegionProvider(canonical);
        var western = CreateOverlay("western", PlanetVector.UnitZ, 1_000.0, 100f);
        var eastern = CreateOverlay("eastern", Offset(PlanetVector.UnitZ, 12_000.0), 1_000.0, 200f);
        var center = PlanetVector.UnitZ;
        var shared = Offset(center, 6_000.0);
        var oldSnapshot = provider.CaptureSnapshot();

        Assert.AreEqual(0L, provider.Revision);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(center, Seed));
        Assert.AreEqual(1L, provider.ReplaceLayers([western, eastern]));
        Assert.AreEqual(900.0, provider.SampleElevationMeters(center, Seed), 0.01);
        Assert.AreEqual(850.0, provider.SampleElevationMeters(shared, Seed), 0.1);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(shared, Seed + 1), 0.01);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitX, Seed), 0.01);
        Assert.AreEqual(1_000.0, oldSnapshot.SampleElevationMeters(center, Seed),
            "An in-progress tile sampler should be allowed to finish its captured geology revision.");

        Assert.AreEqual(2L, provider.ClearLayers());
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(center, Seed), 0.01);
        Assert.AreSame(canonical, provider.CanonicalElevation);
    }

    [TestMethod]
    public void ReplaceLayers_InvalidAtlas_DoesNotAlterPublishedPlanetOrRevision()
    {
        var provider = new PlanetTerrainRegionProvider(new FlatBedrock());
        var region = CreateOverlay("same", PlanetVector.UnitZ, 1_000.0, 100f);
        provider.ReplaceLayers([region]);

        Assert.ThrowsExactly<ArgumentException>(() => provider.ReplaceLayers([region, region]));
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreEqual(900.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed), 0.01);
    }

    [TestMethod]
    public void ReplaceLayers_OrbitalTileGroundPatchAndMeshCache_ObserveSamePublishedHistory()
    {
        var provider = new PlanetTerrainRegionProvider(new FlatBedrock());
        var tileSampler = new PlanetSurfaceTileSampler(provider);
        var groundSampler = new PlanetLocalSurfacePatchSampler(provider);
        var cache = new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(tileSampler));
        var id = new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0);
        var first = cache.GetOrBuild([id], 8, Seed, RadiusMeters);
        var cached = cache.GetOrBuild([id], 8, Seed, RadiusMeters);

        Assert.IsTrue(first.Single().IncludesGeometry);
        Assert.IsFalse(cached.Single().IncludesGeometry);
        Assert.AreEqual(1L, provider.ReplaceLayers([CreateOverlay("test", PlanetVector.UnitZ, 1_000.0, 100f)]));

        var orbital = tileSampler.Sample(id, 8, Seed);
        var ground = groundSampler.Sample(PlanetVector.UnitZ, 2_000.0, 8, Seed, RadiusMeters, CancellationToken.None);
        var rebuilt = cache.GetOrBuild([id], 8, Seed, RadiusMeters);
        var retained = cache.GetOrBuild([id], 8, Seed, RadiusMeters);

        Assert.AreEqual(900.0, orbital.GetPoint(4, 4).ElevationMeters, 0.01);
        Assert.AreEqual(orbital.GetPoint(4, 4).ElevationMeters, ground.GetPoint(4, 4).ElevationMeters, 1e-6);
        Assert.IsTrue(rebuilt.Single().IncludesGeometry, "Publishing geology must resend updated orbital GPU geometry.");
        Assert.IsFalse(retained.Single().IncludesGeometry, "Unchanged revisions should still use GPU references.");

        provider.ClearLayers();
        Assert.IsTrue(cache.GetOrBuild([id], 8, Seed, RadiusMeters).Single().IncludesGeometry);
        Assert.AreEqual(1_000.0, groundSampler.Sample(PlanetVector.UnitZ, 2_000.0, 8, Seed, RadiusMeters, CancellationToken.None)
            .GetPoint(4, 4).ElevationMeters, 0.01);
    }

    private static PlanetRegionalGeologyOverlay CreateOverlay(string key, PlanetVector center, double spacing, float cut)
    {
        const int width = 17;
        var original = Enumerable.Repeat(1_000f, width * width).ToArray();
        var final = Enumerable.Repeat(1_000f - cut, width * width).ToArray();
        var baseline = PlanetRegionalGeologyEvolution.Initialize(Seed, key, width, width, spacing, original);
        var volume = cut * width * width * spacing * spacing;
        var evolved = baseline with
        {
            Iteration = 1,
            ElevationMeters = final,
            CumulativeErodedVolumeCubicMeters = volume,
            CumulativeExportedVolumeCubicMeters = volume
        };
        return PlanetRegionalGeologyOverlay.Create(baseline, evolved, center, RadiusMeters);
    }

    private static PlanetVector Offset(PlanetVector center, double eastMeters)
    {
        var frame = PlanetLocalFrame.Create(center, RadiusMeters, 0);
        return PlanetVector.Normalize(center * RadiusMeters + frame.East * eastMeters);
    }

    private sealed class FlatBedrock : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }
}
