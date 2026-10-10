using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class ProceduralPlanetElevationSourceTests
{

    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void SampleTectonicRelief_SwappedPlateOrder_HasNoContactHeightDiscontinuity(int seed)
    {
        // A Voronoi contact reverses the nearest-plate order. Its physical
        // height must not depend on which plate appears first.
        var direction = PlanetVector.Normalize(new PlanetVector(0.42242678262485306, 0.4821837720791227, -0.7674988099435489));
        foreach (var firstIndex in Enumerable.Range(0, 18))
        {
            for (var secondIndex = firstIndex + 1; secondIndex < 18; secondIndex++)
            {
                foreach (var firstContinental in new[] { false, true })
                {
                    foreach (var secondContinental in new[] { false, true })
                    {
                        var atContact = new ProceduralPlanetElevationSource.NearestPlatePair(firstIndex, secondIndex, 0.91, 0.91);
                        var reversed = new ProceduralPlanetElevationSource.NearestPlatePair(secondIndex, firstIndex, 0.91, 0.91);
                        var left = ProceduralPlanetElevationSource.SampleTectonicRelief(
                            direction, seed, atContact, firstContinental, secondContinental, 0.8, 0.15);
                        var right = ProceduralPlanetElevationSource.SampleTectonicRelief(
                            direction, seed, reversed, secondContinental, firstContinental, 0.8, 0.15);
                        Assert.AreEqual(left, right, 1e-12,
                            $"Seed {seed}, plates {firstIndex}/{secondIndex}: the same contact must have the same height.");

                        const double epsilon = 1e-8;
                        var firstSide = atContact with { SecondaryDot = 0.91 - epsilon };
                        var oppositeSide = reversed with { SecondaryDot = 0.91 - epsilon };
                        var before = ProceduralPlanetElevationSource.SampleTectonicRelief(
                            direction, seed, firstSide, firstContinental, secondContinental, 0.8, 0.15);
                        var after = ProceduralPlanetElevationSource.SampleTectonicRelief(
                            direction, seed, oppositeSide, secondContinental, firstContinental, 0.8, 0.15);
                        Assert.IsLessThan(0.002, Math.Abs(after - before) * 8_400.0,
                            "Nearby samples crossing a contact must not acquire metre-scale rock steps.");
                    }
                }
            }
        }
    }

    [TestMethod]
    [DataRow(24061984)]
    [DataRow(346147916)]
    [DataRow(579460630)]
    public void SampleRegionalRockReliefMeters_NestedPhysicalWindows_ContainBoundedContinuousStructure(int seed)
    {
        var source = new ProceduralPlanetElevationSource();
        var anchor = PlanetVector.Normalize(new PlanetVector(0.42242678262485306, 0.4821837720791227, -0.7674988099435489));
        var frame = PlanetLocalFrame.Create(anchor, 6_371_000, 0);
        foreach (var span in new[] { 128_000.0, 32_000.0, 8_000.0 })
        {
            var curvature = 0.0;
            for (var y = -8; y <= 8; y++)
            {
                for (var x = -8; x <= 8; x++)
                {
                    var point = anchor * 6_371_000 + frame.East * (x * span / 16) + frame.North * (y * span / 16);
                    double Height(PlanetVector p) => source.SampleRegionalRockReliefMeters(PlanetVector.Normalize(p), seed);
                    var height = Height(point);
                    Assert.AreEqual(height, Height(point));
                    Assert.IsTrue(double.IsFinite(height) && Math.Abs(height) <= 620.001);
                    Assert.IsLessThan(2.0, Math.Abs(height - Height(point + frame.East)),
                        "A metre of travel must not cross an artificial bedrock seam.");
                    var offset = frame.East * (span / 16);
                    var secondDifference = Height(point - offset) - 2 * height + Height(point + offset);
                    curvature += secondDifference * secondDifference;
                }
            }

            Assert.IsGreaterThan(0.5, Math.Sqrt(curvature / 289),
                $"The {span / 1_000} km window must contain actual bedrock structure beyond a planar slope.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_SameDirectionAndSeed_IsDeterministic()
    {
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));

        var first = source.SampleElevationMeters(direction, 42);
        var second = source.SampleElevationMeters(direction, 42);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void SampleTerrainFields_GeneratedElevation_MatchesCanonicalElevationSource()
    {
        var source = new ProceduralPlanetElevationSource();
        foreach (var seed in new[] { PlanetSeedCatalog.ShowcaseSeed, 346147916, 579460630 })
        {
            foreach (var direction in FibonacciDirections(64))
            {
                var fields = source.SampleTerrainFields(direction, seed);
                Assert.AreEqual(fields.ElevationMeters, source.SampleElevationMeters(direction, seed));
                Assert.IsTrue(double.IsFinite(fields.ContinentalPotential));
                Assert.IsTrue(double.IsFinite(fields.TectonicUplift));
                Assert.IsTrue(double.IsFinite(fields.MountainBelt));
            }
        }
    }

    [TestMethod]
    public void SampleOrogenicFoldReliefMeters_SameWorldLocation_IsBoundedDeterministicAndGeographicallyContinuous()
    {
        var source = new ProceduralPlanetElevationSource();
        foreach (var seed in new[] { 24061984, 346147916, 579460630 })
        {
            var directions = FibonacciDirections(2_048).ToArray();
            var foldHeights = directions.Select(direction => source.SampleOrogenicFoldReliefMeters(direction, seed)).ToArray();
            Assert.IsGreaterThan(45.0, foldHeights.Max(Math.Abs),
                $"Seed {seed} should contain physically sized orogenic fold crests and troughs.");
            Assert.IsTrue(foldHeights.All(fold => double.IsFinite(fold) && Math.Abs(fold) <= 230.001));

            var index = Enumerable.Range(0, foldHeights.Length).MaxBy(index => Math.Abs(foldHeights[index]));
            var focus = directions[index];
            var replay = source.SampleOrogenicFoldReliefMeters(focus, seed);
            Assert.AreEqual(foldHeights[index], replay);

            // Twenty metres along a fixed planetary direction is a tiny
            // displacement compared with the physical 55–95 km folding band.
            var nearby = PlanetVector.Normalize(focus + (PlanetVector.UnitY * (20.0 / 6_371_000.0)));
            Assert.IsLessThan(4.0, Math.Abs(replay - source.SampleOrogenicFoldReliefMeters(nearby, seed)),
                "A continuous tectonic fold must not jump across nearby vertices or local LODs.");
        }
    }

    [TestMethod]
    public void SampleOrogenicRidgeReliefMeters_MultiscaleMountainsRemainContinuousAndSeedDeterministic()
    {
        var source = new ProceduralPlanetElevationSource();
        foreach (var seed in new[] { 24061984, 346147916, 579460630 })
        {
            var directions = FibonacciDirections(2_048).ToArray();
            var ridges = directions.Select(direction => source.SampleOrogenicRidgeReliefMeters(direction, seed)).ToArray();
            Assert.IsGreaterThan(30.0, ridges.Max(Math.Abs),
                $"Seed {seed} lacks the shorter 5–25 km bedrock ridges.");
            Assert.IsTrue(ridges.All(value => double.IsFinite(value) && value >= 0.0 && value <= 223.001),
                "Finite rock spurs add only bounded rock mass; troughs belong to tectonic folding and erosion.");

            var index = Enumerable.Range(0, ridges.Length).MaxBy(i => Math.Abs(ridges[i]));
            var focus = directions[index];
            Assert.AreEqual(ridges[index], source.SampleOrogenicRidgeReliefMeters(focus, seed));

            var nearby = PlanetVector.Normalize(focus + PlanetVector.UnitY * (20.0 / 6_371_000.0));
            Assert.IsLessThan(8.0, Math.Abs(source.SampleOrogenicRidgeReliefMeters(nearby, seed) - ridges[index]),
                "Twenty metres along the same mountain ridge cannot jump by several hundred metres.");

            // The old short-wavelength cosine was an endless, evenly spaced
            // stripe train. A genuine finite spur must vary substantially
            // over 30–60 km instead of repeating the same profile indefinitely.
            var transverse = PlanetVector.Normalize(PlanetVector.Cross(focus,
                Math.Abs(focus.Y) < 0.9 ? PlanetVector.UnitY : PlanetVector.UnitX));
            var nearbyHeights = new[] { -60_000.0, -30_000.0, 30_000.0, 60_000.0 }
                .Select(distance => source.SampleOrogenicRidgeReliefMeters(
                    PlanetVector.Normalize(focus + transverse * (distance / 6_371_000.0)), seed)).ToArray();
            Assert.IsTrue(nearbyHeights.Any(value => Math.Abs(value - ridges[index]) > 2.0),
                "A local tectonic spur cannot be uniform along its full surrounding landscape.");
        }
    }

    [TestMethod]
    public void SampleTerrainFields_OrientedFoldRelief_IsSharedByAlignedPhysicalHeroSamples()
    {
        var source = new ProceduralPlanetElevationSource();
        var anchor = PlanetVector.Normalize(new PlanetVector(0.2, 0.7, 0.6));
        var builder = new PlanetHeroRegionBuilder(source);
        const int seed = 24061984;
        var coarse = builder.Build(anchor, seed, 17, 16_000, 1);
        var fine = builder.Build(anchor, seed, 33, 16_000, 1);

        for (var y = 0; y < coarse.Width; y++)
        {
            for (var x = 0; x < coarse.Width; x++)
            {
                Assert.AreEqual(coarse.OriginalElevationMeters[y * coarse.Width + x],
                    fine.OriginalElevationMeters[(2 * y) * fine.Width + 2 * x],
                    "LOD refinement must preserve exact canonical fold and bedrock identity at aligned locations.");
            }
        }
    }

    [TestMethod]
    public void SampleElevationMeters_DifferentSeed_ProducesDifferentElevation()
    {
        var source = new ProceduralPlanetElevationSource();
        var direction = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));

        var first = source.SampleElevationMeters(direction, 42);
        var second = source.SampleElevationMeters(direction, 43);

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void SampleElevationMeters_IntegritySeedGallery_ProducesDistinctWorldFingerprints()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(512).ToArray();
        var fingerprints = PlanetSeedCatalog.IntegritySeeds
            .Select(seed => string.Join(",", directions.Select(direction => (int)Math.Round(source.SampleElevationMeters(direction, seed) / 25.0))))
            .ToArray();

        Assert.AreEqual(PlanetSeedCatalog.IntegritySeeds.Count, fingerprints.Distinct().Count());
    }

    [TestMethod]
    public void SampleElevationMeters_NearbyDirections_RemainContinuous()
    {
        var source = new ProceduralPlanetElevationSource();
        var firstDirection = PlanetVector.Normalize(new PlanetVector(0.31, -0.47, 0.83));
        var secondDirection = PlanetVector.Normalize(firstDirection + new PlanetVector(0.0000001, -0.0000001, 0.0000001));

        var first = source.SampleElevationMeters(firstDirection, 42);
        var second = source.SampleElevationMeters(secondDirection, 42);

        Assert.IsLessThan(10.0, Math.Abs(first - second));
    }

    [TestMethod]
    public void SampleElevationMeters_ReturnsEarthScaleReliefRange()
    {
        var source = new ProceduralPlanetElevationSource();
        PlanetVector[] directions =
        [
            PlanetVector.UnitX,
            PlanetVector.UnitY,
            PlanetVector.UnitZ,
            PlanetVector.Normalize(new PlanetVector(1.0, 1.0, 1.0)),
            PlanetVector.Normalize(new PlanetVector(-1.0, 0.25, 0.4)),
        ];

        foreach (var direction in directions)
        {
            var elevationMeters = source.SampleElevationMeters(direction, 42);
            Assert.IsGreaterThanOrEqualTo(-7_000.0, elevationMeters);
            Assert.IsLessThanOrEqualTo(9_000.0, elevationMeters);
        }
    }

    [TestMethod]
    public void SampleElevationMeters_GlobalSample_ContainsMountainsBasinsAndOceans()
    {
        var source = new ProceduralPlanetElevationSource();
        var elevations = FibonacciDirections(2_048).Select(direction => source.SampleElevationMeters(direction, 42)).ToArray();
        var landFraction = elevations.Count(elevation => elevation > 0.0) / (double)elevations.Length;

        Assert.IsGreaterThan(1_000.0, elevations.Max());
        Assert.IsLessThan(-2_000.0, elevations.Min());
        Assert.IsGreaterThan(0.15, landFraction);
        Assert.IsLessThan(0.55, landFraction);
    }

    [TestMethod]
    public void SampleElevationMeters_GlobalSample_ContainsVariedHighlandStructure()
    {
        var source = new ProceduralPlanetElevationSource();
        var highlands = FibonacciDirections(1_024)
            .Select(direction => source.SampleElevationMeters(direction, 42))
            .Where(elevation => elevation > 1_500.0)
            .ToArray();
        var elevationBands = highlands.Select(elevation => (int)Math.Floor(elevation / 500.0)).Distinct().Count();

        Assert.IsGreaterThan(8, highlands.Length);
        Assert.IsGreaterThanOrEqualTo(3, elevationBands);
        Assert.IsGreaterThan(700.0, highlands.Max() - highlands.Min());
    }


    [TestMethod]
    public void SampleElevationMeters_IntegritySeedGallery_AvoidsSingleFlatHemisphere()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(2_048).ToArray();

        foreach (var seed in PlanetSeedCatalog.IntegritySeeds)
        {
            var elevations = directions.Select(direction => source.SampleElevationMeters(direction, seed)).ToArray();
            var landFraction = elevations.Count(elevation => elevation > 0.0) / (double)elevations.Length;
            var roundedBands = elevations.Select(elevation => (int)Math.Round(elevation / 250.0)).Distinct().Count();

            Assert.IsGreaterThan(0.10, landFraction, $"Seed {seed} generated almost no land.");
            Assert.IsLessThan(0.65, landFraction, $"Seed {seed} generated almost no ocean.");
            Assert.IsGreaterThanOrEqualTo(12, roundedBands, $"Seed {seed} has insufficient terrain variation.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_GlobalSample_HasTectonicMountainRelief()
    {
        var source = new ProceduralPlanetElevationSource();
        var elevations = FibonacciDirections(4_096).Select(direction => source.SampleElevationMeters(direction, 42)).ToArray();
        var highMountains = elevations.Count(elevation => elevation > 2_500.0);

        Assert.IsGreaterThan(0, highMountains);
        Assert.IsGreaterThan(3_000.0, elevations.Max());
    }


    [TestMethod]
    public void SampleTerrainFields_OrogenicBelts_FormRegionalMountainSystems()
    {
        var source = new ProceduralPlanetElevationSource();
        var directions = FibonacciDirections(4_096).ToArray();

        foreach (var seed in new[] { PlanetSeedCatalog.ShowcaseSeed, 346147916, 579460630 })
        {
            var samples = directions.Select(direction => source.SampleTerrainFields(direction, seed)).ToArray();
            var mountainSamples = samples.Where(sample => sample.MountainBelt > 0.60 && sample.ElevationMeters > 0.0).ToArray();
            Assert.IsGreaterThan(10, mountainSamples.Length, $"Seed {seed} lacks sufficiently broad orogenic mountain systems.");
            Assert.IsGreaterThan(1_200.0, mountainSamples.Max(sample => sample.ElevationMeters) -
                mountainSamples.Min(sample => sample.ElevationMeters), $"Seed {seed} lacks regional mountain relief.");
        }
    }

    [TestMethod]
    public void SampleElevationMeters_ContinentalLand_HasRegionalReliefHierarchy()
    {
        var source = new ProceduralPlanetElevationSource();
        var elevations = FibonacciDirections(8_192).Select(direction => source.SampleElevationMeters(direction, PlanetSeedCatalog.ShowcaseSeed)).Where(elevation => elevation > 100.0).ToArray();
        var lowlands = elevations.Count(elevation => elevation < 1_000.0);
        var uplands = elevations.Count(elevation => elevation >= 1_000.0 && elevation < 2_500.0);
        var mountains = elevations.Count(elevation => elevation >= 2_500.0);

        Assert.IsGreaterThan(50, lowlands);
        Assert.IsGreaterThan(20, uplands);
        Assert.IsGreaterThan(0, mountains);
        Assert.IsGreaterThan(2_000.0, elevations.Max() - elevations.Min());
    }

    private static IEnumerable<PlanetVector> FibonacciDirections(int count)
    {
        var goldenRatio = (1.0 + Math.Sqrt(5.0)) / 2.0;

        for (var index = 0; index < count; index++)
        {
            var y = 1.0 - (2.0 * (index + 0.5) / count);
            var radius = Math.Sqrt(Math.Max(0.0, 1.0 - (y * y)));
            var angle = 2.0 * Math.PI * index / goldenRatio;
            yield return new PlanetVector(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
        }
    }
}