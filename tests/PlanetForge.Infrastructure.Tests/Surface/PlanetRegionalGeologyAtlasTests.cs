using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetRegionalGeologyAtlasTests
{
    private const double RadiusMeters = 6_371_000.0;
    private const int Seed = 24061984;

    [TestMethod]
    public void SampleElevationMeters_OverlappingPeerRegions_BlendsWithoutDoublingIncisionOrVisibleSeams()
    {
        var center = PlanetVector.UnitZ;
        var neighbor = Offset(center, 12_000.0);
        var western = Region("western", center, 1000.0, 100f);
        var eastern = Region("eastern", neighbor, 1000.0, 200f);
        var atlas = new PlanetRegionalGeologyAtlas(new FlatBedrock(), [western, eastern]);
        var reverse = new PlanetRegionalGeologyAtlas(new FlatBedrock(), [eastern, western]);
        var shared = Offset(center, 6_000.0);
        var elevation = atlas.SampleElevationMeters(shared, Seed);

        Assert.AreEqual(850.0, elevation, 0.1);
        Assert.AreEqual(elevation, reverse.SampleElevationMeters(shared, Seed), 1e-9);
        Assert.AreEqual(900.0, atlas.SampleElevationMeters(center, Seed), 0.01);
        Assert.AreEqual(800.0, atlas.SampleElevationMeters(neighbor, Seed), 0.01);
        Assert.AreEqual(1000.0, atlas.SampleElevationMeters(shared, Seed + 1), 0.01);
        Assert.AreEqual(1000.0, atlas.SampleElevationMeters(PlanetVector.UnitX, Seed), 0.01);

        for (var x = 2_000.0; x < 10_000.0; x += 125.0)
        {
            var before = atlas.SampleElevationMeters(Offset(center, x), Seed);
            var after = atlas.SampleElevationMeters(Offset(center, x + 1.0), Seed);
            Assert.IsLessThan(1.0, Math.Abs(after - before), $"Height seam at {x} metres.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_NestedGeologicalScales_KeepBothPhysicalErosionHistories()
    {
        var regional = Region("regional", PlanetVector.UnitZ, 1_000.0, 100f);
        var nested = Region("nested", PlanetVector.UnitZ, 250.0, 40f);
        var atlas = new PlanetRegionalGeologyAtlas(new FlatBedrock(), [regional], [nested]);

        Assert.AreEqual(860.0, atlas.SampleElevationMeters(PlanetVector.UnitZ, Seed), 0.01);
        Assert.AreEqual(900.0, atlas.SampleElevationMeters(Offset(PlanetVector.UnitZ, 5_000.0), Seed), 0.01);
        Assert.AreEqual(1000.0, atlas.SampleElevationMeters(PlanetVector.UnitZ, Seed + 1), 0.01);
    }

    [TestMethod]
    public void SampleElevationMeters_CubeSphereTilesAndLocalPatch_ShareTheSameEvolvedCanonicalHeight()
    {
        var atlas = new PlanetRegionalGeologyAtlas(new FlatBedrock(),
            [Region("proof", PlanetVector.UnitZ, 1000.0, 100f)]);
        var orbitalSampler = new PlanetSurfaceTileSampler(atlas);
        var localSampler = new PlanetLocalSurfacePatchSampler(atlas);
        var global = orbitalSampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 16, Seed);
        var left = orbitalSampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 1, 0, 0), 16, Seed);
        var right = orbitalSampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 1, 1, 0), 16, Seed);
        var local = localSampler.Sample(PlanetVector.UnitZ, 2_000, 8, Seed, RadiusMeters, CancellationToken.None);
        var center = global.GetPoint(8, 8).ElevationMeters;
        var leftEdge = left.GetPoint(16, 16).ElevationMeters;
        var rightEdge = right.GetPoint(0, 16).ElevationMeters;
        var nearGround = local.GetPoint(4, 4).ElevationMeters;

        Assert.AreEqual(900.0, center, 0.01);
        Assert.AreEqual(center, nearGround, 1e-6);
        Assert.AreEqual(leftEdge, rightEdge, 1e-6);
        Assert.AreEqual(center, rightEdge, 1e-6);
    }

    [TestMethod]
    public void Constructor_DuplicateOrDifferentScaleWithinPeerLayer_RejectsAmbiguousCompositing()
    {
        var regional = Region("same", PlanetVector.UnitZ, 1_000.0, 100f);
        var duplicate = Region("same", PlanetVector.UnitY, 1_000.0, 100f);
        var different = Region("different", PlanetVector.UnitZ, 250.0, 40f);

        Assert.ThrowsExactly<ArgumentException>(() => new PlanetRegionalGeologyAtlas(new FlatBedrock(), [regional, duplicate]));
        Assert.ThrowsExactly<ArgumentException>(() => new PlanetRegionalGeologyAtlas(new FlatBedrock(), [regional, different]));
        Assert.ThrowsExactly<ArgumentException>(() => new PlanetRegionalGeologyAtlas(new FlatBedrock(), []));
    }

    private static PlanetRegionalGeologyOverlay Region(string key, PlanetVector anchor, double spacing, float cut)
    {
        const int width = 17;
        var before = Enumerable.Repeat(1000f, width * width).ToArray();
        var after = Enumerable.Repeat(1000f - cut, width * width).ToArray();
        var original = PlanetRegionalGeologyEvolution.Initialize(Seed, key, width, width, spacing, before);
        var excavated = width * width * spacing * spacing * cut;
        var evolved = original with
        {
            Iteration = 1,
            ElevationMeters = after,
            CumulativeErodedVolumeCubicMeters = excavated,
            CumulativeExportedVolumeCubicMeters = excavated
        };
        return PlanetRegionalGeologyOverlay.Create(original, evolved, anchor, RadiusMeters);
    }

    private static PlanetVector Offset(PlanetVector center, double eastMeters)
    {
        var frame = PlanetLocalFrame.Create(center, RadiusMeters, 0);
        return PlanetVector.Normalize(center * RadiusMeters + frame.East * eastMeters);
    }

    private sealed class FlatBedrock : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1000.0;
    }
}
