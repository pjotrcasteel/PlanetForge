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

            Assert.AreEqual(result.ExportedVolumeCubicMeters,
                result.HydraulicExportedVolumeCubicMeters + result.BankExportedVolumeCubicMeters,
                Math.Max(1.0, result.ExportedVolumeCubicMeters * 1e-10));
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
        Assert.IsGreaterThanOrEqualTo(0, regional.FocusCellIndex);
        Assert.IsLessThan(regional.Width * regional.Width, regional.FocusCellIndex);
        Assert.AreEqual(regional.FocusCellIndex, PlanetHeroRegionBuilder.FindIncisedChannelCell(regional));
        Assert.IsGreaterThan(0f, regional.AccumulatedRunoffCells[regional.FocusCellIndex]);
        Assert.AreEqual(regional.FocusCellIndex, builder.Build(anchor, 24061984, 33, 32_000, 3).FocusCellIndex);

        var local = builder.Build(focus, 24061984, 17, 4_000, 3);
        Assert.AreEqual(250.0, local.CellSpacingMeters);
        Assert.IsGreaterThan(0.0, local.OriginalElevationMeters.Max() - local.OriginalElevationMeters.Min());
    }

    [TestMethod]
    public void FindIncisedChannelCell_ChoosesStructuredBranchingTerrainInsteadOfDeepSinglePixel()
    {
        const int width = 65;
        const int seed = 24061984;
        var before = Enumerable.Repeat(1_500f, width * width).ToArray();
        var after = (float[])before.Clone();
        var cuts = new float[before.Length];
        var runoff = new float[before.Length];

        var isolated = 16 * width + 16;
        after[isolated] = 1_340f;
        cuts[isolated] = 160f;
        runoff[isolated] = 300f;

        for (var y = 31; y < 54; y++)
        {
            for (var x = 31; x < 54; x++)
            {
                var index = y * width + x;
                var fold = 100f * (float)(Math.Sin((x - 31) * 0.42) * Math.Cos((y - 31) * 0.46));
                before[index] += fold;
                after[index] = before[index] - 8f;
                cuts[index] = 8f;
                runoff[index] = 15f;
            }
        }

        var region = new PlanetHeroRegion(seed, width, 500, before, after, cuts, runoff,
            6, 8, 0, 0, 0);
        var selected = PlanetHeroRegionBuilder.FindIncisedChannelCell(region);
        Assert.IsGreaterThan(30, selected % width,
            "A 32 km window with connected ridge variation should win over an isolated deep erosion scar.");
        Assert.IsGreaterThan(30, selected / width);
        Assert.AreEqual(selected, PlanetHeroRegionBuilder.FindIncisedChannelCell(region));
    }

    [TestMethod]
    public void FindIncisedChannelCell_RuggedCraterAndConnectedStream_PrefersLocalCatchment()
    {
        const int width = 65;
        const double spacing = 125.0;
        const int seed = 24061984;
        var original = Enumerable.Repeat(1_500f, width * width).ToArray();
        var after = (float[])original.Clone();
        var runoff = new float[original.Length];

        // The dry impact rim has substantially greater nonplanar relief and
        // one deep routed headcut. That must not dominate a whole 8 km
        // nested camera target when a branching catchment is also present.
        for (var y = 10; y <= 28; y++)
        {
            for (var x = 10; x <= 28; x++)
            {
                var distance = Math.Sqrt((x - 19) * (x - 19) + (y - 19) * (y - 19));
                var rim = 340.0 * Math.Exp(-Math.Pow((distance - 5.0) / 1.3, 2.0));
                original[y * width + x] += (float)rim;
                after[y * width + x] = original[y * width + x];
            }
        }

        after[19 * width + 19] -= 180f;
        runoff[19 * width + 19] = 900f;

        // Two intersecting, physically routed reaches occupy the other side
        // of the same geological region. No screen-space detail is involved.
        for (var y = 30; y <= 55; y++)
        {
            var center = 44 + (int)Math.Round(2.0 * Math.Sin(y * 0.19));
            for (var dx = -3; dx <= 3; dx++)
            {
                var i = y * width + center + dx;
                original[i] += 55f * (1.0f - (float)Math.Exp(-dx * dx / 3.0));
                after[i] = original[i];
            }

            var channel = y * width + center;
            after[channel] -= 14f;
            runoff[channel] = 85f;
        }

        for (var t = 0; t < 17; t++)
        {
            var x = 34 + t / 2;
            var y = 36 + t;
            var channel = y * width + x;
            after[channel] = original[channel] - 10f;
            runoff[channel] = 55f;
        }

        var cuts = original.Zip(after, (first, second) => first - second).ToArray();
        var region = new PlanetHeroRegion(seed, width, spacing, original, after, cuts, runoff, 6, 8, 0, 0, 0);
        var selected = PlanetHeroRegionBuilder.FindIncisedChannelCell(region);

        Assert.IsGreaterThan(32, selected % width, "Select the branching valley, not the isolated crater rim.");
        Assert.IsGreaterThan(29, selected / width, "The selected 8 km region must include the connected channels.");
        Assert.AreEqual(selected, PlanetHeroRegionBuilder.FindIncisedChannelCell(region));
    }

    [TestMethod]
    public void FindIncisedChannelCell_NumericalErosionScar_DoesNotOutrankStructuredBedrockCatchment()
    {
        const int width = 65;
        const double spacing = 125.0;
        var original = Enumerable.Repeat(1_500f, width * width).ToArray();
        var evolved = (float[])original.Clone();
        var runoff = new float[original.Length];

        // The synthetic numerical cliff has very high evolved relief and
        // abundant incised pixels, but its pre-erosion rock was planar.
        for (var y = 15; y <= 29; y++)
        {
            for (var x = 20; x <= 29; x++)
            {
                if (x < 23)
                {
                    continue;
                }

                var index = y * width + x;
                evolved[index] -= 280f;
                runoff[index] = 80f;
            }
        }

        // A separate real bedrock catchment has moderate curved ridge relief
        // and one continuous routed tributary through its interior.
        for (var y = 33; y <= 55; y++)
        {
            for (var x = 37; x <= 55; x++)
            {
                var index = y * width + x;
                original[index] += (float)(85.0 * Math.Sin((x - 36) * 0.33) * Math.Cos((y - 32) * 0.31));
                evolved[index] = original[index];
            }

            var center = 46 + (int)Math.Round(2.0 * Math.Sin(y * 0.25));
            var stream = y * width + center;
            evolved[stream] -= 16f;
            runoff[stream] = 85f;
        }

        var cuts = original.Zip(evolved, (before, after) => before - after).ToArray();
        var region = new PlanetHeroRegion(24061984, width, spacing, original, evolved, cuts, runoff, 6, 8, 0, 0, 0);
        var selected = PlanetHeroRegionBuilder.FindIncisedChannelCell(region);

        Assert.IsGreaterThan(35, selected % width, "Numerically steep erosion is not itself complex source bedrock.");
        Assert.IsGreaterThan(30, selected / width, "Inspect the physically structured valley, not the simulated cliff.");
    }

    [TestMethod]
    public void FindIncisedChannelCell_NoIncisedStreams_UsesStableCentralFallback()
    {
        const int width = 65;
        var values = Enumerable.Repeat(1_500f, width * width).ToArray();
        var region = new PlanetHeroRegion(24061984, width, 125.0, values, (float[])values.Clone(),
            new float[values.Length], new float[values.Length], 6, 8, 0, 0, 0);

        Assert.AreEqual(32 * width + 32, PlanetHeroRegionBuilder.FindIncisedChannelCell(region));
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
