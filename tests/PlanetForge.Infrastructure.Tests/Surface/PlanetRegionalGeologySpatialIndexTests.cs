using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetRegionalGeologySpatialIndexTests
{
    private const int Seed = 24061984;
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Candidates_GlobeDistributedRegions_ConservativelyMatchFullScanAtAllBoundaries()
    {
        var overlays = new List<PlanetRegionalGeologyOverlay>();
        for (var lat = -75; lat <= 75; lat += 30)
        {
            for (var lon = -165; lon <= 165; lon += 30)
            {
                var phi = lat * Math.PI / 180.0;
                var theta = lon * Math.PI / 180.0;
                var point = new PlanetVector(Math.Cos(phi) * Math.Cos(theta),
                    Math.Sin(phi), Math.Cos(phi) * Math.Sin(theta));
                overlays.Add(Region($"region-{lat}-{lon}", point, 1_000.0, 25f + (lon + 180) / 30f));
            }
        }

        var atlas = new PlanetRegionalGeologyAtlas(new FlatBedrock(), [.. overlays]);
        var index = new PlanetRegionalGeologySpatialIndex(overlays);
        var random = new Random(17);
        var locations = new List<PlanetVector>();
        locations.AddRange(overlays.Select(o => o.CenterDirection));
        locations.AddRange([PlanetVector.UnitX, PlanetVector.UnitY, PlanetVector.UnitZ,
            PlanetVector.UnitX * -1, PlanetVector.UnitY * -1, PlanetVector.UnitZ * -1]);

        foreach (var region in overlays)
        {
            var frame = PlanetLocalFrame.Create(region.CenterDirection, RadiusMeters, 0);
            for (var i = 0; i < 8; i++)
            {
                var dx = (random.NextDouble() - 0.5) * 18_000.0;
                var dy = (random.NextDouble() - 0.5) * 18_000.0;
                locations.Add(PlanetVector.Normalize(region.CenterDirection * RadiusMeters +
                    frame.East * dx + frame.North * dy));
            }
        }

        for (var i = 0; i < 300; i++)
        {
            locations.Add(PlanetVector.Normalize(new PlanetVector(
                random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1)));
        }

        foreach (var direction in locations)
        {
            var weighted = 0.0;
            var weight = 0.0;
            foreach (var region in overlays)
            {
                var sample = region.SampleWeightedDeltaMeters(direction, Seed);
                weighted += sample.DeltaMeters * sample.Weight;
                weight += sample.Weight;
                if (sample.Weight > 0)
                {
                    CollectionAssert.Contains(index.Candidates(direction, Seed).ToArray(), region,
                        "Spatial prefilter must never exclude a contributing overlay.");
                }
            }

            Assert.AreEqual(1_000.0 + weighted / Math.Max(1.0, weight), atlas.SampleElevationMeters(direction, Seed), 1e-9);
        }

        Assert.AreEqual(0, index.Candidates(PlanetVector.UnitZ, Seed + 1).Count);
        Assert.IsLessThan(overlays.Count / 2, index.Candidates(PlanetVector.UnitZ, Seed).Count,
            "Regions on distant continents must not be scanned for each world vertex.");
    }

    [TestMethod]
    public void Candidates_BroadRegionAndPolarBounds_DoNotLoseContributions()
    {
        var north = Region("polar", PlanetVector.UnitY, 150_000.0, 50f);
        var index = new PlanetRegionalGeologySpatialIndex([north]);
        var frame = PlanetLocalFrame.Create(PlanetVector.UnitY, RadiusMeters, 0);
        foreach (var east in new[] { -1_000_000.0, -450_000.0, 0.0, 450_000.0, 1_000_000.0 })
        {
            var direction = PlanetVector.Normalize(PlanetVector.UnitY * RadiusMeters + frame.East * east);
            var weight = north.SampleWeightedDeltaMeters(direction, Seed).Weight;
            if (weight > 0)
            {
                CollectionAssert.Contains(index.Candidates(direction, Seed).ToArray(), north);
            }
        }
    }

    private static PlanetRegionalGeologyOverlay Region(string key, PlanetVector center, double spacing, float cut)
    {
        const int width = 17;
        var before = Enumerable.Repeat(1_000f, width * width).ToArray();
        var original = PlanetRegionalGeologyEvolution.Initialize(Seed, key, width, width, spacing, before);
        var eroded = original with
        {
            Iteration = 1,
            ElevationMeters = Enumerable.Repeat(1_000f - cut, width * width).ToArray(),
            CumulativeErodedVolumeCubicMeters = cut * width * width * spacing * spacing,
            CumulativeExportedVolumeCubicMeters = cut * width * width * spacing * spacing
        };
        return PlanetRegionalGeologyOverlay.Create(original, eroded, center, RadiusMeters);
    }

    private sealed class FlatBedrock : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1_000.0;
    }
}
