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
        Assert.AreEqual(result.ElevationMeters[centerY * width + centerX - 1],
            result.ElevationMeters[centerY * width + centerX + 1], 0.00001);
        Assert.IsGreaterThan(0.0, result.AdditionalExportedSedimentCubicMeters);
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
