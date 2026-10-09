using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetHillslopeMassWastingTests
{
    [TestMethod]
    public void Apply_UnstableRockSpire_TransfersMaterialDownhillAndConservesVolume()
    {
        const int width = 17;
        var bedrock = Enumerable.Repeat(1000f, width * width).ToArray();
        var center = 8 * width + 8;
        bedrock[center] = 1600f;
        var original = (float[])bedrock.Clone();
        var lithology = Enumerable.Repeat(1.1f, bedrock.Length).ToArray();

        var result = PlanetHillslopeMassWasting.Apply(bedrock, lithology, width, width, 62.5);

        Assert.IsGreaterThan(0, result.InitiallyUnstableEdges);
        Assert.IsTrue(result.ElevationMeters[center] < 1600f, "Unstable bedrock must collapse downhill.");
        Assert.IsTrue(result.ElevationMeters[center - 1] > 1000f, "Rockfall must accumulate as local talus.");
        Assert.IsTrue(result.ElevationMeters[center + width] > 1000f, "Routed material must reach other downhill neighbors.");
        Assert.IsTrue(result.AdditionalErodedVolumeCubicMeters > 0.0);
        Assert.AreEqual(result.AdditionalErodedVolumeCubicMeters, result.AdditionalDepositedVolumeCubicMeters, 100.0);
        CollectionAssert.AreEqual(original, bedrock, "Simulation must never mutate canonical bedrock.");
        var replay = PlanetHillslopeMassWasting.Apply(bedrock, lithology, width, width, 62.5);
        CollectionAssert.AreEqual(result.ElevationMeters, replay.ElevationMeters);
    }

    [TestMethod]
    public void Apply_StableHillslopeAndFlatBedrock_RemainUnchanged()
    {
        const int width = 17;
        var stableSlope = new float[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                stableSlope[y * width + x] = 1000f + x * 20f;
            }
        }

        var lithology = Enumerable.Repeat(1f, stableSlope.Length).ToArray();
        var result = PlanetHillslopeMassWasting.Apply(stableSlope, lithology, width, width, 62.5);
        CollectionAssert.AreEqual(stableSlope, result.ElevationMeters);
        Assert.AreEqual(0.0, result.AdditionalErodedVolumeCubicMeters);
        Assert.AreEqual(0.0, result.AdditionalDepositedVolumeCubicMeters);
        Assert.AreEqual(0, result.InitiallyUnstableEdges);
    }

    [TestMethod]
    public void Apply_SameGeologyAtDifferentCellSizes_RespectsPhysicalSlopeThreshold()
    {
        const int width = 17;
        var elevated = Enumerable.Repeat(1000f, width * width).ToArray();
        elevated[8 * width + 8] = 1120f;
        var lithology = Enumerable.Repeat(1f, elevated.Length).ToArray();

        var coarse = PlanetHillslopeMassWasting.Apply(elevated, lithology, width, width, 1000.0);
        var fine = PlanetHillslopeMassWasting.Apply(elevated, lithology, width, width, 62.5);

        CollectionAssert.AreEqual(elevated, coarse.ElevationMeters);
        Assert.IsTrue(fine.AdditionalDepositedVolumeCubicMeters > 0.0);
    }

    [TestMethod]
    public void Apply_InvalidFieldsAndCancellation_FailWithoutRunningTransport()
    {
        var bedrock = Enumerable.Repeat(1000f, 17 * 17).ToArray();
        var lithology = Enumerable.Repeat(1f, bedrock.Length).ToArray();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PlanetHillslopeMassWasting.Apply(bedrock, lithology, 17, 17, 0.0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            PlanetHillslopeMassWasting.Apply(bedrock, lithology[1..], 17, 17, 50.0));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetHillslopeMassWasting.Apply(bedrock, lithology, 17, 17, 50.0, cancellationToken: cancel.Token));
    }
}
