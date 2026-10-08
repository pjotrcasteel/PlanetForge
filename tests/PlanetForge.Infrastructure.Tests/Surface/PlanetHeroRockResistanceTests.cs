using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetHeroRockResistanceTests
{
    [TestMethod]
    public void SampleErodibility_SameSphericalPointAndSeed_IsStableAtAllRenderingScales()
    {
        var point = PlanetVector.Normalize(new PlanetVector(0.4, 0.5, 0.73));
        var a = PlanetHeroRockResistance.SampleErodibility(point, 24061984, 1600);
        var b = PlanetHeroRockResistance.SampleErodibility(point * 7.0, 24061984, 1600);
        Assert.AreEqual(a, b);
        Assert.IsGreaterThanOrEqualTo(0.35f, a);
        Assert.IsLessThanOrEqualTo(1.9f, a);
    }

    [TestMethod]
    public void SampleErodibility_IndependentWorldPoints_ChangeHardnessWithoutChangingHeight()
    {
        const double radius = 6_371_000.0;
        var center = PlanetVector.UnitZ;
        var east = PlanetLocalFrame.Create(center, radius, 0).East;
        var grades = new List<float>();
        for (var i = -8; i <= 8; i++)
        {
            var direction = PlanetVector.Normalize(center * radius + east * (i * 1_200));
            grades.Add(PlanetHeroRockResistance.SampleErodibility(direction, 346147916, 1500));
        }

        Assert.IsGreaterThan(0.05f, grades.Max() - grades.Min());
        var directionA = PlanetVector.Normalize(center * radius + east * 10_000.0);
        var directionB = PlanetVector.Normalize(center * radius + east * 10_001.0);
        Assert.IsLessThan(0.01f, Math.Abs(
            PlanetHeroRockResistance.SampleErodibility(directionA, 346147916, 1500) -
            PlanetHeroRockResistance.SampleErodibility(directionB, 346147916, 1500)));
    }

    [TestMethod]
    public void Build_HardAndSoftRock_ChangeRealIncisionWhileConservingSediment()
    {
        const int width = 32;
        var heights = new float[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                heights[y * width + x] = 5_000 - 16 * x - 22 * y;
            }
        }

        var resistant = PlanetRegionalWatershed.Build(width, width, 200,
            heights, erodibilityCellWeights: Enumerable.Repeat(0.35f, heights.Length).ToArray());
        var weak = PlanetRegionalWatershed.Build(width, width, 200,
            heights, erodibilityCellWeights: Enumerable.Repeat(1.9f, heights.Length).ToArray());

        Assert.IsGreaterThan(0.0, resistant.ErodedVolumeCubicMeters);
        Assert.IsLessThan(weak.ErodedVolumeCubicMeters, resistant.ErodedVolumeCubicMeters);
        Assert.AreEqual(heights.Length, weak.EvolvedElevationMeters.Length);

        foreach (var result in new[] { resistant, weak })
        {
            var budget = result.ErodedVolumeCubicMeters - result.DepositedVolumeCubicMeters -
                result.ExportedVolumeCubicMeters;
            Assert.IsLessThan(Math.Max(1.0, result.ErodedVolumeCubicMeters * 1e-6), Math.Abs(budget));
        }
    }

    [TestMethod]
    public void Build_InvalidRockResistance_RejectsInputBeforeRouting()
    {
        var heights = Enumerable.Repeat(1000f, 10 * 10).ToArray();
        var resistance = Enumerable.Repeat(1f, heights.Length).ToArray();
        resistance[10] = float.NaN;
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalWatershed.Build(10, 10, 1000,
            heights, erodibilityCellWeights: resistance));
        resistance[10] = 3.1f;
        Assert.ThrowsExactly<ArgumentException>(() => PlanetRegionalWatershed.Build(10, 10, 1000,
            heights, erodibilityCellWeights: resistance));
    }
}
