using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetLateralErosionRelaxationTests
{
    [TestMethod]
    public void Apply_IsolatedHydraulicIncision_WidensValleyWithoutChangingTotalRemovedMaterial()
    {
        const int size = 17;
        var original = Enumerable.Repeat(2500f, size * size).ToArray();
        var eroded = (float[])original.Clone();
        var center = size / 2 * size + size / 2;
        eroded[center] -= 200f;

        var relaxed = PlanetLateralErosionRelaxation.Apply(original, eroded, size, size, 8);
        var removedBefore = original.Zip(eroded, (a, b) => (double)a - b).Sum();
        var removedAfter = original.Zip(relaxed, (a, b) => (double)a - b).Sum();

        Assert.IsGreaterThan(eroded[center], relaxed[center], "The original narrow incision should become shallower.");
        Assert.IsLessThan(original[center + 1], relaxed[center + 1], "Adjacent rock must be affected by lateral bank retreat.");
        Assert.IsLessThan(0.02, Math.Abs(removedBefore - removedAfter), "No net sediment may appear or vanish.");
        Assert.AreEqual(2500f, original[center]);
        Assert.AreEqual(2300f, eroded[center]);
    }

    [TestMethod]
    public void Apply_RepeatingPasses_AreDeterministicAndPreserveSignedMass()
    {
        const int width = 24;
        const int height = 16;
        var original = Enumerable.Range(0, width * height).Select(i => (float)(1000 + Math.Sin(i * 0.2) * 120)).ToArray();
        var eroded = original.Select((value, i) => value - (i % width >= 8 && i % width <= 10 ? 60f : 0f) +
            (i / width >= 10 ? 15f : 0f)).ToArray();

        var first = PlanetLateralErosionRelaxation.Apply(original, eroded, width, height, 8);
        var repeated = PlanetLateralErosionRelaxation.Apply(original, eroded, width, height, 8);
        CollectionAssert.AreEqual(first, repeated);

        var totalBefore = original.Zip(eroded, (a, b) => (double)(a - b)).Sum();
        var totalAfter = original.Zip(first, (a, b) => (double)(a - b)).Sum();
        Assert.IsLessThan(0.1, Math.Abs(totalBefore - totalAfter));
    }

    [TestMethod]
    public void Apply_ProtectedChannelEdges_RetainsSteepBedrockBanksAndConservesExcavation()
    {
        const int size = 17;
        var original = Enumerable.Repeat(2500f, size * size).ToArray();
        var eroded = (float[])original.Clone();
        var channel = 8 * size + 8;
        eroded[channel] -= 200f;
        eroded[channel + size] -= 160f;

        var broad = PlanetLateralErosionRelaxation.Apply(original, eroded, size, size);
        var protectedBanks = PlanetLateralErosionRelaxation.Apply(original, eroded, size, size, preserveIncisionEdges: true);

        Assert.AreEqual(original[channel - 1], protectedBanks[channel - 1],
            "Uncut bank rock should not be blurred by diffusion.");
        Assert.IsLessThan(protectedBanks[channel - 1], broad[channel - 1],
            "The unprotected relaxation should diffuse cut beyond the channel.");
        Assert.IsGreaterThan(broad[channel], protectedBanks[channel],
            "The protected channel should remain more strongly incised.");
        Assert.AreEqual(360.0, original.Zip(protectedBanks, (a, b) => (double)a - b).Sum(), 0.02);
    }

    [TestMethod]
    public void Apply_ZeroPassesAndFlatGeology_PreserveExactElevations()
    {
        var original = Enumerable.Repeat(1200f, 9 * 9).ToArray();
        var eroded = (float[])original.Clone();
        eroded[40] -= 120f;
        CollectionAssert.AreEqual(eroded, PlanetLateralErosionRelaxation.Apply(original, eroded, 9, 9, 0));
        CollectionAssert.AreEqual(original, PlanetLateralErosionRelaxation.Apply(original, original, 9, 9, 12));
    }

    [TestMethod]
    public void Apply_InvalidDimensionsOrNonFiniteHeights_RejectsInput()
    {
        var heights = Enumerable.Repeat(1000f, 9 * 9).ToArray();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetLateralErosionRelaxation.Apply(heights, heights, 9, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PlanetLateralErosionRelaxation.Apply(heights, heights, 9, 9, 40));
        heights[6] = float.PositiveInfinity;
        Assert.ThrowsExactly<ArgumentException>(() => PlanetLateralErosionRelaxation.Apply(heights, heights, 9, 9));
    }
}
