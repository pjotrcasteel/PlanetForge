using System.Text.Json;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetRegionalGeologyEvolutionTests
{
    [TestMethod]
    public void Initialize_OriginalBedrockIsCopiedAndUnaffectedByFutureEdits()
    {
        var heights = MountainGrid(24, 24);
        var state = PlanetRegionalGeologyEvolution.Initialize(24061984, "tile/4/9", 24, 24, 2000, heights);
        var original = state.ElevationMeters[85];

        heights[85] = 100_000f;
        Assert.AreEqual(original, state.ElevationMeters[85]);
        Assert.AreEqual(0, state.Iteration);
        Assert.AreEqual(4, state.SchemaVersion);
    }

    [TestMethod]
    public void Advance_FivePasses_EquivalentToResumingAfterTwoAndThree()
    {
        var original = PlanetRegionalGeologyEvolution.Initialize(24061984, "tile/4/9", 40, 40, 2200, MountainGrid(40, 40));
        var all = PlanetRegionalGeologyEvolution.Advance(original, 5);
        var intermediate = PlanetRegionalGeologyEvolution.Advance(original, 2);
        var resumed = PlanetRegionalGeologyEvolution.Advance(intermediate, 3);

        CollectionAssert.AreEqual(all.ElevationMeters, resumed.ElevationMeters);
        Assert.AreEqual(all.CumulativeErodedVolumeCubicMeters, resumed.CumulativeErodedVolumeCubicMeters);
        Assert.AreEqual(all.CumulativeDepositedVolumeCubicMeters, resumed.CumulativeDepositedVolumeCubicMeters);
        Assert.AreEqual(all.CumulativeExportedVolumeCubicMeters, resumed.CumulativeExportedVolumeCubicMeters);
        Assert.AreEqual(0, original.Iteration);
        Assert.AreEqual(5, resumed.Iteration);
    }

    [TestMethod]
    public void Restore_JsonRoundTrip_ResumesWithoutLosingGeologicalIdentity()
    {
        var initial = PlanetRegionalGeologyEvolution.Initialize(346147916, "faceZ/region12", 32, 32, 1500, MountainGrid(32, 32));
        var before = PlanetRegionalGeologyEvolution.Advance(initial, 2);
        var json = JsonSerializer.Serialize(before);
        var persisted = JsonSerializer.Deserialize<PlanetRegionalGeologySnapshot>(json)!;
        var restored = PlanetRegionalGeologyEvolution.Restore(persisted);
        var expected = PlanetRegionalGeologyEvolution.Advance(initial, 3);
        var actual = PlanetRegionalGeologyEvolution.Advance(restored, 1);

        Assert.AreEqual("faceZ/region12", actual.RegionKey);
        Assert.AreEqual(346147916, actual.Seed);
        Assert.AreEqual(3, actual.Iteration);
        CollectionAssert.AreEqual(expected.ElevationMeters, actual.ElevationMeters);
        Assert.AreEqual(expected.CumulativeExportedVolumeCubicMeters, actual.CumulativeExportedVolumeCubicMeters);
        Assert.AreNotSame(persisted.ElevationMeters, restored.ElevationMeters);
    }

    [TestMethod]
    public void Advance_ConstantClimateAndTerrain_ConservesSedimentAcrossManyIterations()
    {
        var initial = PlanetRegionalGeologyEvolution.Initialize(579460630, "region/2", 40, 40, 2500, MountainGrid(40, 40));
        var state = PlanetRegionalGeologyEvolution.Advance(initial, 12);
        var balance = state.CumulativeErodedVolumeCubicMeters - state.CumulativeDepositedVolumeCubicMeters -
            state.CumulativeExportedVolumeCubicMeters;

        Assert.IsGreaterThan(0.0, state.CumulativeErodedVolumeCubicMeters);
        Assert.IsLessThan(Math.Max(1.0, state.CumulativeErodedVolumeCubicMeters * 1e-5), Math.Abs(balance));
        Assert.AreEqual(12, state.Iteration);
        Assert.IsTrue(state.ElevationMeters.All(float.IsFinite));
    }

    [TestMethod]
    public void Advance_NoRain_LeavesBedrockUnchanged()
    {
        const int size = 24;
        var initial = PlanetRegionalGeologyEvolution.Initialize(24061984, "dry", size, size, 1800, MountainGrid(size, size));
        var dry = PlanetRegionalGeologyEvolution.Advance(initial, 8, new float[size * size]);

        CollectionAssert.AreEqual(initial.ElevationMeters, dry.ElevationMeters);
        Assert.AreEqual(0.0, dry.CumulativeExportedVolumeCubicMeters);
        Assert.AreEqual(0.0, dry.CumulativeErodedVolumeCubicMeters);
    }

    [TestMethod]
    public void Restore_IncompatibleOrAlteredSnapshot_FailsSafely()
    {
        var original = PlanetRegionalGeologyEvolution.Initialize(1, "stable", 16, 16, 5000, MountainGrid(16, 16));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyEvolution.Restore(original with { SchemaVersion = 1 }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyEvolution.Restore(original with { SchemaVersion = 2 }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyEvolution.Restore(original with { SchemaVersion = 3 }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyEvolution.Restore(original with { SchemaVersion = 99 }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyEvolution.Restore(original with { CumulativeErodedVolumeCubicMeters = 9.0 }));
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalGeologyEvolution.Restore(original with { ElevationMeters = [float.NaN, .. original.ElevationMeters[1..]] }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetRegionalGeologyEvolution.Advance(original, 65));
    }

    [TestMethod]
    public void Advance_CancelledOperation_DoesNotMutateExistingSnapshot()
    {
        var initial = PlanetRegionalGeologyEvolution.Initialize(1, "cancel", 32, 32, 2000, MountainGrid(32, 32));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            PlanetRegionalGeologyEvolution.Advance(initial, 4, cancellationToken: source.Token));
        Assert.AreEqual(0, initial.Iteration);
    }

    private static float[] MountainGrid(int width, int height)
    {
        var heights = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var mountain = 2_800 * Math.Exp(-Math.Pow((x - width * 0.51) / (width * 0.23), 2));
                var basin = 180 * Math.Sin((x * 0.47) + (y * 0.33));
                heights[y * width + x] = (float)(mountain + basin - y * 13 - 260);
            }
        }

        return heights;
    }
}
