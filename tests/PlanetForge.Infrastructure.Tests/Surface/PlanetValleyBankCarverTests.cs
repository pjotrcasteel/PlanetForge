using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetValleyBankCarverTests
{
    [TestMethod]
    public void Apply_RoutedIncision_CreatesActualValleyShouldersAndAccountsForAllExcavation()
    {
        const int width = 41;
        var original = Enumerable.Repeat(1800f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        var flow = new float[original.Length];
        for (var x = 8; x < width - 8; x++)
        {
            var channel = (width / 2) * width + x;
            flow[channel] = 100;
            eroded[channel] -= 95;
        }

        var result = PlanetValleyBankCarver.Apply(original, eroded, flow, width, width, 250);
        var shoulder = (width / 2 + 1) * width + width / 2;
        Assert.IsLessThan(original[shoulder], result.ElevationMeters[shoulder]);
        Assert.IsLessThanOrEqualTo(eroded[(width / 2) * width + width / 2], result.ElevationMeters[(width / 2) * width + width / 2]);
        Assert.IsGreaterThan(0.0, result.AdditionalExportedSedimentCubicMeters);

        var volume = 0.0;
        for (var i = 0; i < original.Length; i++)
        {
            Assert.IsLessThanOrEqualTo(eroded[i], result.ElevationMeters[i], "Bank erosion must never raise bedrock.");
            volume += ((double)eroded[i] - result.ElevationMeters[i]) * 250 * 250;
        }

        Assert.AreEqual(volume, result.AdditionalExportedSedimentCubicMeters, Math.Max(1.0, volume * 1e-8));
    }

    [TestMethod]
    public void Apply_EastwardRiver_CutsAcrossItsBanksMoreThanAlongItsFlow()
    {
        const int width = 41;
        const int centerX = 20;
        const int centerY = 20;
        var center = centerY * width + centerX;
        var original = Enumerable.Repeat(1800f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        eroded[center] -= 110f;
        var runoff = new float[original.Length];
        runoff[center] = 1000f;
        var receivers = Enumerable.Repeat(-1, original.Length).ToArray();
        receivers[center] = center + 1;

        var result = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 100.0, receivers);
        var acrossBank = (centerY + 1) * width + centerX;
        var alongBank = centerY * width + centerX + 1;
        var acrossCut = original[acrossBank] - result.ElevationMeters[acrossBank];
        var alongCut = original[alongBank] - result.ElevationMeters[alongBank];

        Assert.IsGreaterThan(0.0, acrossCut, "Real runoff should excavate streamside banks.");
        Assert.IsGreaterThan(alongCut, acrossCut, "Cross-stream retreat must exceed isolated upstream/downstream erosion.");
        Assert.IsLessThan(result.ElevationMeters[centerY * width + centerX - 1],
            result.ElevationMeters[centerY * width + centerX + 1],
            "A continuous downstream reach must excavate further along the receiver than behind its source.");
        Assert.IsGreaterThan(0.0, result.AdditionalExportedSedimentCubicMeters);
    }

    [TestMethod]
    public void Apply_DiagonalReceiver_RoutesPhysicalChannelAlongTheConnectedReach()
    {
        const int width = 41;
        const int center = 20 * width + 20;
        var original = Enumerable.Repeat(1_700f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        eroded[center] -= 90f;
        var runoff = new float[original.Length];
        runoff[center] = 160f; // 1 km² at 79.06 m spacing
        var receiver = center + width + 1;
        var receivers = Enumerable.Repeat(-1, original.Length).ToArray();
        receivers[center] = receiver;

        var result = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 100, receivers);
        var receiverCut = original[receiver] - result.ElevationMeters[receiver];
        var oppositeCut = original[center - width - 1] - result.ElevationMeters[center - width - 1];
        var leftBankCut = original[center + width] - result.ElevationMeters[center + width];
        var rightBankCut = original[center + 1] - result.ElevationMeters[center + 1];

        Assert.IsGreaterThan(oppositeCut, receiverCut,
            "The physical diagonal downstream receiver must be carved as part of a continuous reach.");
        Assert.AreEqual(leftBankCut, rightBankCut, 0.01,
            "Opposite shoulders of the diagonal reach should be symmetric.");
        Assert.IsGreaterThan(0.0, result.AdditionalExportedSedimentCubicMeters);
        var replay = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 100, receivers);
        CollectionAssert.AreEqual(result.ElevationMeters, replay.ElevationMeters);
    }

    [TestMethod]
    public void Apply_PhysicalCatchmentArea_ControlsWideningAtEveryGridResolution()
    {
        const int width = 41;
        var center = 20 * width + 20;
        var original = Enumerable.Repeat(1500f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        eroded[center] -= 120f;
        var narrowFlow = new float[original.Length];
        narrowFlow[center] = 100f;
        var broadFlow = new float[original.Length];
        broadFlow[center] = 1600f;

        var narrow = PlanetValleyBankCarver.Apply(original, eroded, narrowFlow, width, width, 50.0);
        var broad = PlanetValleyBankCarver.Apply(original, eroded, broadFlow, width, width, 50.0);
        var bank = 21 * width + 22;
        Assert.IsGreaterThan(original[bank] - narrow.ElevationMeters[bank],
            original[bank] - broad.ElevationMeters[bank], "A larger catchment must carve broader banks.");
        Assert.IsGreaterThan(narrow.AdditionalExportedSedimentCubicMeters,
            broad.AdditionalExportedSedimentCubicMeters);
    }

    [TestMethod]
    public void Apply_EquivalentPhysicalDrainageAreas_ActivateChannelsAtDifferentCellSizes()
    {
        const int width = 17;
        const int center = 8 * width + 8;
        var original = Enumerable.Repeat(1_500f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        eroded[center] = 1_400f;

        // Both cells represent 1 km² of contributing watershed despite a
        // 25-fold difference in contributing raster cell count.
        var fine = new float[original.Length];
        fine[center] = 100f; // 100 × 100 m × 100 m
        var coarse = new float[original.Length];
        coarse[center] = 4f; // 4 × 500 m × 500 m
        var narrow = PlanetValleyBankCarver.Apply(original, eroded, fine, width, width, 100.0);
        var broad = PlanetValleyBankCarver.Apply(original, eroded, coarse, width, width, 500.0);

        Assert.IsGreaterThan(0.0, narrow.AdditionalExportedSedimentCubicMeters);
        Assert.IsGreaterThan(0.0, broad.AdditionalExportedSedimentCubicMeters,
            "Physically identical drainage must not disappear only because a coarse grid has fewer cells.");
        Assert.IsLessThan(original[center + 1], narrow.ElevationMeters[center + 1]);
        Assert.IsLessThan(original[center + 1], broad.ElevationMeters[center + 1]);
    }

    [TestMethod]
    public void Apply_AlreadyCancelled_DoesNotBeginExpensiveBankExcavation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var bedrock = Enumerable.Repeat(1000f, 33 * 33).ToArray();
        var runoff = Enumerable.Repeat(100f, 33 * 33).ToArray();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetValleyBankCarver.Apply(bedrock, bedrock, runoff, 33, 33, 100.0,
                cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void Apply_ConnectedSteepRiver_DeepensItsPhysicalChannelWithoutReversingDownstreamHead()
    {
        const int width = 41;
        const int center = 20 * width + 20;
        var original = Enumerable.Repeat(1400f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        var runoff = new float[original.Length];
        var downstream = Enumerable.Repeat(-1, original.Length).ToArray();
        var receiver = center + 1;
        eroded[center] = 1300f;
        eroded[receiver] = 1050f;
        runoff[center] = 150f;
        downstream[center] = receiver;

        var result = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 100, downstream);

        Assert.IsLessThan(eroded[center], result.ElevationMeters[center],
            "A connected steep reach must deepen beyond the ordinary valley-bank shoulder.");
        Assert.IsGreaterThan(result.ElevationMeters[receiver], result.ElevationMeters[center],
            "Headward excavation must not cut a channel below its receiver.");
        Assert.IsGreaterThan(0.0, result.AdditionalExportedSedimentCubicMeters);

        var replay = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 100, downstream);
        CollectionAssert.AreEqual(result.ElevationMeters, replay.ElevationMeters);
        Assert.AreEqual(result.AdditionalExportedSedimentCubicMeters, replay.AdditionalExportedSedimentCubicMeters);
    }

    [TestMethod]
    public void Apply_SteepReach_HeadwardRetreatIsBoundedByExistingCutAndActualCellSize()
    {
        const int width = 41;
        const int center = 20 * width + 20;
        const int receiver = center + 1;
        var original = Enumerable.Repeat(1400f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        eroded[center] = 1300f;
        eroded[receiver] = 1000f;
        var runoff = new float[original.Length];
        // 31.25 m cells require ~820 contributing cells to exceed 0.8 km².
        runoff[center] = 1_000f;
        var downstream = Enumerable.Repeat(-1, original.Length).ToArray();
        downstream[center] = receiver;

        foreach (var spacing in new[] { 31.25, 100.0, 1_000.0 })
        {
            var result = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, spacing, downstream);
            var additional = eroded[center] - result.ElevationMeters[center];
            var reachLimit = Math.Min((original[center] - eroded[center]) * 0.28, spacing * 0.06);

            Assert.IsGreaterThan(0f, additional, "Steep connected reaches must still erode.");
            Assert.IsLessThanOrEqualTo((float)(reachLimit + 0.01), additional,
                "One coarse routing step must not excavate an implausible deep vertical terrace.");
            Assert.IsGreaterThan(result.ElevationMeters[receiver], result.ElevationMeters[center],
                "The updated headcut must remain above the receiving channel.");
        }
    }

    [TestMethod]
    public void Apply_ConnectedGentleRiver_DoesNotInventSteepHeadwardIncision()
    {
        const int width = 41;
        const int center = 20 * width + 20;
        var original = Enumerable.Repeat(1400f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        var runoff = new float[original.Length];
        var downstream = Enumerable.Repeat(-1, original.Length).ToArray();
        eroded[center] = 1350f;
        eroded[center + 1] = 1349f;
        runoff[center] = 150f;
        downstream[center] = center + 1;

        var result = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 100, downstream);

        Assert.AreEqual(eroded[center], result.ElevationMeters[center]);
    }

    [TestMethod]
    public void Apply_WithoutBothRunoffAndHydraulicCut_DoesNotInventRiverGeometry()
    {
        const int width = 33;
        var original = Enumerable.Repeat(1100f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        var runoff = Enumerable.Repeat(1000f, width * width).ToArray();
        var noErosion = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 250);
        CollectionAssert.AreEqual(eroded, noErosion.ElevationMeters);
        Assert.AreEqual(0.0, noErosion.AdditionalExportedSedimentCubicMeters);

        eroded[width * width / 2] -= 85;
        Array.Fill(runoff, 0f);
        var noRunoff = PlanetValleyBankCarver.Apply(original, eroded, runoff, width, width, 250);
        CollectionAssert.AreEqual(eroded, noRunoff.ElevationMeters);
        Assert.AreEqual(0.0, noRunoff.AdditionalExportedSedimentCubicMeters);
    }

    [TestMethod]
    public void Apply_DiagonalTributary_IsDeterministicAndDoesNotDrawCardinalOnlyChannels()
    {
        const int width = 41;
        var original = Enumerable.Repeat(1200f, width * width).ToArray();
        var eroded = (float[])original.Clone();
        var flow = new float[original.Length];
        for (var i = 5; i < 35; i++)
        {
            var index = i * width + i;
            eroded[index] -= 100;
            flow[index] = 125;
        }

        var first = PlanetValleyBankCarver.Apply(original, eroded, flow, width, width, 250);
        var repeated = PlanetValleyBankCarver.Apply(original, eroded, flow, width, width, 250);
        CollectionAssert.AreEqual(first.ElevationMeters, repeated.ElevationMeters);
        Assert.AreEqual(first.AdditionalExportedSedimentCubicMeters, repeated.AdditionalExportedSedimentCubicMeters);
        Assert.IsLessThan(1200f, first.ElevationMeters[20 * width + 21]);
        Assert.IsLessThan(1200f, first.ElevationMeters[21 * width + 20]);
        Assert.AreEqual(first.ElevationMeters[20 * width + 21], first.ElevationMeters[21 * width + 20]);
    }
}
