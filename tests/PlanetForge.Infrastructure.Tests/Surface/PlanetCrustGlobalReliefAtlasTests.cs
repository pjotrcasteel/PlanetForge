using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetCrustGlobalReliefAtlasTests
{
    private const int Seed = 24061984;
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void PublishGlobalCrustDeformation_ChangesRealElevationWithoutChangingReferenceWorld()
    {
        var timeline = Timeline(Seed);
        var canonical = new FlatBedrock();
        var provider = new PlanetTerrainRegionProvider(canonical);
        var previous = provider.CaptureSnapshot();
        var publication = timeline[^1];

        Assert.AreEqual(0L, provider.Revision);
        Assert.AreEqual(1L, provider.PublishGlobalCrustDeformation(publication));
        Assert.AreEqual(1L, provider.Revision);
        Assert.AreSame(canonical, provider.CanonicalElevation);

        var atlas = new PlanetCrustGlobalReliefAtlas(publication);
        Assert.IsGreaterThan(0.01, atlas.MaximumAbsoluteDisplacementMeters);
        var changed = 0;
        foreach (var material in publication.Columns.Where((_, index) => index % 11 == 0))
        {
            var point = material.GeographicDirection;
            Assert.AreEqual(1_000.0, previous.SampleElevationMeters(point, Seed));
            Assert.AreEqual(1_000.0, canonical.SampleElevationMeters(point, Seed));
            Assert.AreEqual(1_000.0, provider.SampleElevationMeters(point, Seed + 1));
            var expected = 1_000.0 + atlas.SampleDisplacementMeters(point);
            Assert.AreEqual(expected, provider.SampleElevationMeters(point, Seed), 1e-8);
            if (Math.Abs(expected - 1_000.0) > 0.01)
            {
                changed++;
            }
        }

        Assert.IsGreaterThan(0, changed, "The actual shared elevation source must change, not merely shader colours.");
        Assert.AreEqual(2L, provider.ClearGlobalCrustDeformation());
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitX, Seed));
    }

    [TestMethod]
    public void GlobalReliefAtlas_LongitudeSeamAndPoles_AreContinuous()
    {
        var atlas = new PlanetCrustGlobalReliefAtlas(Timeline(Seed)[^1]);
        foreach (var latitude in new[] { -1.35, -0.45, 0.0, 0.31, 1.41 })
        {
            var parallel = Math.Cos(latitude);
            var north = Math.Sin(latitude);
            var eitherSide = new PlanetVector(parallel * Math.Cos(1e-10), north, parallel * Math.Sin(1e-10));
            var otherSide = new PlanetVector(parallel * Math.Cos(-1e-10), north, parallel * Math.Sin(-1e-10));
            Assert.AreEqual(atlas.SampleDisplacementMeters(eitherSide),
                atlas.SampleDisplacementMeters(otherSide), 0.01);
        }

        foreach (var sign in new[] { -1.0, 1.0 })
        {
            var north = sign > 0 ? PlanetVector.UnitY : PlanetVector.UnitY * -1.0;
            var reference = atlas.SampleDisplacementMeters(north);
            for (var angle = 0.0; angle < Math.Tau; angle += 0.47)
            {
                var almostPole = PlanetVector.Normalize(new PlanetVector(
                    1e-8 * Math.Cos(angle), sign, 1e-8 * Math.Sin(angle)));
                Assert.AreEqual(reference, atlas.SampleDisplacementMeters(almostPole), 0.05);
            }
        }
    }

    [TestMethod]
    public void PublishGlobalCrustDeformation_OrbitAndGroundLOD0Through3_SharePhysicalVertexHeight()
    {
        var canonical = new FlatBedrock();
        var provider = new PlanetTerrainRegionProvider(canonical);
        var tileSampler = new PlanetSurfaceTileSampler(provider);
        var groundSampler = new PlanetLocalSurfacePatchSampler(provider);
        var cache = new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(tileSampler));
        var timeline = Timeline(Seed);

        var tileId = new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0);
        Assert.IsTrue(cache.GetOrBuild([tileId], 8, Seed, RadiusMeters).Single().IncludesGeometry);
        Assert.IsFalse(cache.GetOrBuild([tileId], 8, Seed, RadiusMeters).Single().IncludesGeometry);

        provider.PublishGlobalCrustDeformation(timeline[^1]);
        Assert.IsTrue(cache.GetOrBuild([tileId], 8, Seed, RadiusMeters).Single().IncludesGeometry,
            "A geology publication must invalidate existing orbital GPU mesh references.");

        const int cells = 8;
        var orbital = new double[4];
        for (var level = 0; level <= 3; level++)
        {
            var n = level == 0 ? 0 : (1 << (level - 1)) - 1;
            var id = new PlanetTileId(CubeFace.PositiveZ, level, n, n);
            var tile = tileSampler.Sample(id, cells, Seed);
            orbital[level] = tile.GetPoint(cells, cells).ElevationMeters;
        }

        var ground = groundSampler.Sample(PlanetVector.UnitZ, 2_000, cells,
            Seed, RadiusMeters, CancellationToken.None);
        foreach (var elevation in orbital)
        {
            Assert.AreEqual(orbital[0], elevation, 1e-7);
        }
        Assert.AreEqual(orbital[0], ground.GetPoint(cells / 2, cells / 2).ElevationMeters, 1e-6);

        var snapshot = provider.CaptureSnapshot();
        provider.ClearGlobalCrustDeformation();
        Assert.IsTrue(cache.GetOrBuild([tileId], cells, Seed, RadiusMeters).Single().IncludesGeometry);
        Assert.AreEqual(1_000.0, tileSampler.Sample(tileId, cells, Seed).GetPoint(4, 4).ElevationMeters);
        Assert.AreEqual(orbital[0], snapshot.SampleElevationMeters(PlanetVector.UnitZ, Seed), 1e-7,
            "Previously captured immutable geology must not change mid-frame.");
    }

    [TestMethod]
    public void PublishGlobalCrustDeformation_RejectsOldEpochWithoutAlteringRevision()
    {
        var provider = new PlanetTerrainRegionProvider(new FlatBedrock());
        var timeline = Timeline(Seed);
        Assert.ThrowsExactly<ArgumentException>(() => provider.PublishGlobalCrustDeformation(timeline[0]));
        Assert.AreEqual(0L, provider.Revision);
        Assert.AreEqual(1_000.0, provider.SampleElevationMeters(PlanetVector.UnitZ, Seed));
    }

    private static IReadOnlyList<PlanetCrustEvolvingSnapshot> Timeline(int seed)
    {
        var catalog = PlanetCrustParcelCatalog.Create(seed);
        return new PlanetCrustAccumulatedDeformation().Simulate(catalog, PlanetCrustOwnershipHistory.Create(catalog), 2);
    }

    private sealed class FlatBedrock : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }
}
