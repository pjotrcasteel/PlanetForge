using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetHeroRegionBuilderTests
{
    [TestMethod]
    public void Build_CanonicalGeology_ProducesPhysicalErodedGeometryAndConservesSediment()
    {
        var builder = new PlanetHeroRegionBuilder(new ProceduralPlanetElevationSource());

        foreach (var seed in new[] { 24061984, 346147916, 579460630 })
        {
            var result = builder.Build(PlanetVector.UnitZ, seed, 33, 32_000, 3);
            Assert.AreEqual(seed, result.Seed);
            Assert.AreEqual(33, result.Width);
            Assert.AreEqual(1_000.0, result.CellSpacingMeters);
            Assert.AreEqual(33 * 33, result.OriginalElevationMeters.Length);
            Assert.AreEqual(33 * 33, result.EvolvedElevationMeters.Length);
            Assert.AreEqual(3, result.ErosionIterations);
            Assert.IsGreaterThan(0.0, result.OriginalElevationMeters.Max() - result.OriginalElevationMeters.Min());
            Assert.IsTrue(result.EvolvedElevationMeters.All(float.IsFinite));
            Assert.AreEqual(result.OriginalElevationMeters.Length, result.AccumulatedRunoffCells.Length);

            for (var i = 0; i < result.CumulativeCutMeters.Length; i++)
            {
                Assert.AreEqual(result.OriginalElevationMeters[i] - result.EvolvedElevationMeters[i],
                    result.CumulativeCutMeters[i]);
            }

            var sedimentBalance = result.ErodedVolumeCubicMeters -
                result.DepositedVolumeCubicMeters - result.ExportedVolumeCubicMeters;
            Assert.IsLessThan(Math.Max(1.0, result.ErodedVolumeCubicMeters * 1e-5), Math.Abs(sedimentBalance));
        }
    }

    [TestMethod]
    public void Build_SameSeedAndAnchor_ReplaysExactlyWithoutVerticalExaggeration()
    {
        var builder = new PlanetHeroRegionBuilder(new ProceduralPlanetElevationSource());
        var first = builder.Build(PlanetVector.Normalize(new PlanetVector(0.2, 0.7, 0.6)), 24061984, 17, 16_000, 4);
        var replay = builder.Build(PlanetVector.Normalize(new PlanetVector(0.2, 0.7, 0.6)), 24061984, 17, 16_000, 4);

        CollectionAssert.AreEqual(first.OriginalElevationMeters, replay.OriginalElevationMeters);
        CollectionAssert.AreEqual(first.EvolvedElevationMeters, replay.EvolvedElevationMeters);
        CollectionAssert.AreEqual(first.CumulativeCutMeters, replay.CumulativeCutMeters);
        Assert.AreEqual(first.ExportedVolumeCubicMeters, replay.ExportedVolumeCubicMeters);
    }

    [TestMethod]
    public void Build_DryClimateOrInvalidGrid_DoesNotProduceHiddenNoise()
    {
        var builder = new PlanetHeroRegionBuilder(new FlatRockElevationSource());

        var result = builder.Build(PlanetVector.UnitY, 99, 9, 8_000, 3);
        Assert.IsTrue(result.OriginalElevationMeters.All(height => height == 1000f));
        Assert.IsTrue(result.CumulativeCutMeters.All(cut => cut == 0f));
        Assert.AreEqual(0.0, result.ErodedVolumeCubicMeters);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.Build(PlanetVector.UnitY, 99, 10, 8_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.Build(PlanetVector.UnitY, 99, 257, 800_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => builder.Build(PlanetVector.UnitY, 99, 9, 8_000, 0));
    }

    [TestMethod]
    public void Build_CancelledBeforeSampling_DoesNotStartGeologicalWork()
    {
        var builder = new PlanetHeroRegionBuilder(new FlatRockElevationSource());
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            builder.Build(PlanetVector.UnitZ, 99, 33, 32_000, 2, source.Token));
    }

    private sealed class FlatRockElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1000.0;
    }
}
