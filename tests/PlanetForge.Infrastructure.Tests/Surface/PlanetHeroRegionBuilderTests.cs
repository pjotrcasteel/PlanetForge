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
            Assert.AreEqual(8, result.LateralRelaxationPasses);
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
            var totalHeightLossVolume = result.CumulativeCutMeters.Sum(delta => (double)delta) *
                result.CellSpacingMeters * result.CellSpacingMeters;
            Assert.IsLessThan(Math.Max(20_000.0, result.ExportedVolumeCubicMeters * 1e-4),
                Math.Abs(totalHeightLossVolume - result.ExportedVolumeCubicMeters));
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
    public void FindIncisedChannelFocus_PreservesGlobalIdentityForNestedLocalGeology()
    {
        var anchor = PlanetVector.Normalize(new PlanetVector(0.55, 0.3, 0.78));
        var builder = new PlanetHeroRegionBuilder(new ProceduralPlanetElevationSource());
        var regional = builder.Build(anchor, 24061984, 33, 32_000, 3);
        var focus = PlanetHeroRegionBuilder.FindIncisedChannelFocus(regional, anchor);
        var again = PlanetHeroRegionBuilder.FindIncisedChannelFocus(regional, anchor);

        Assert.AreEqual(1.0, focus.Length, 1e-12);
        Assert.IsGreaterThan(0.999, PlanetVector.Dot(anchor, focus));
        Assert.AreEqual(focus, again);

        var local = builder.Build(focus, 24061984, 17, 4_000, 3);
        Assert.AreEqual(250.0, local.CellSpacingMeters);
        Assert.IsGreaterThan(0.0, local.OriginalElevationMeters.Max() - local.OriginalElevationMeters.Min());
    }

    [TestMethod]
    public void CreateParentOverlay_NestedRegionInheritsPhysicalParentIncision()
    {
        const int width = 17;
        const int seed = 24061984;
        var bedrock = Enumerable.Repeat(1000f, width * width).ToArray();
        var evolved = (float[])bedrock.Clone();
        var centerIndex = width * (width / 2) + width / 2;
        evolved[centerIndex] = 920f;
        var cuts = new float[bedrock.Length];
        cuts[centerIndex] = 80f;
        var parent = new PlanetHeroRegion(seed, width, 1000.0, bedrock, evolved, cuts,
            new float[bedrock.Length], 1, 0, 80_000_000.0, 0.0, 80_000_000.0);

        var overlay = PlanetHeroRegionBuilder.CreateParentOverlay(parent, PlanetVector.UnitZ);
        var source = new PlanetRegionalEvolvedElevationSource(new FlatRockElevationSource(), overlay);
        // Parent data must be present in the child's *original* height field,
        // before that child begins its finer hydrological evolution.
        var local = new PlanetHeroRegionBuilder(source).Build(PlanetVector.UnitZ, seed, 9, 2_000, 1);

        Assert.AreEqual(920f, local.OriginalElevationMeters[4 * 9 + 4]);
        Assert.AreEqual(1000f, bedrock[centerIndex]);
        Assert.AreEqual(920f, evolved[centerIndex]);
        Assert.AreEqual(1000.0, source.SampleElevationMeters(PlanetVector.UnitZ, seed + 1), 0.0001);
        Assert.AreEqual(920.0, source.SampleElevationMeters(PlanetVector.UnitZ, seed), 0.0001);
        Assert.IsTrue(local.EvolvedElevationMeters.All(float.IsFinite));
    }

    [TestMethod]
    public void CreateParentOverlay_ThreeScaleSampling_PreservesCumulativeParentAndChildCuts()
    {
        const int seed = 24061984;
        static PlanetHeroRegion Region(int seed, int width, double spacing, float original, float cut)
        {
            var before = Enumerable.Repeat(original, width * width).ToArray();
            var after = (float[])before.Clone();
            var middle = width / 2 * width + width / 2;
            after[middle] -= cut;
            var cuts = new float[before.Length];
            cuts[middle] = cut;
            var sediment = cut * spacing * spacing;
            return new PlanetHeroRegion(seed, width, spacing, before, after, cuts,
                new float[before.Length], 1, 0, sediment, 0, sediment);
        }

        var parent = PlanetHeroRegionBuilder.CreateParentOverlay(
            Region(seed, 17, 1_000, 1_000, 80), PlanetVector.UnitZ);
        var nested = PlanetHeroRegionBuilder.CreateParentOverlay(
            Region(seed, 17, 250, 920, 30), PlanetVector.UnitZ);
        IPlanetElevationSource source = new FlatRockElevationSource();
        source = new PlanetRegionalEvolvedElevationSource(source, parent);
        source = new PlanetRegionalEvolvedElevationSource(source, nested);

        Assert.AreEqual(890.0, source.SampleElevationMeters(PlanetVector.UnitZ, seed), 0.001);
        Assert.AreEqual(1000.0, source.SampleElevationMeters(PlanetVector.UnitZ, seed + 1), 0.001);
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
