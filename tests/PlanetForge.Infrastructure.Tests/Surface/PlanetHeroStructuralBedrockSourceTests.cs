using PlanetForge.Domain.Surface;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetHeroStructuralBedrockSourceTests
{
    private const double RadiusMeters = 6_371_000.0;

    [TestMethod]
    public void SampleElevationMeters_SameSphericalPoint_IsDeterministicAtEveryViewScale()
    {
        var anchor = PlanetVector.Normalize(new PlanetVector(0.51, 0.71, 0.46));
        var source = new PlanetHeroStructuralBedrockSource(new FlatBedrockSource(), anchor, 24061984);
        var direction = Offset(anchor, 1_400, -2_700);
        var value = source.SampleElevationMeters(direction, 24061984);

        Assert.AreEqual(value, source.SampleElevationMeters(direction * 3.0, 24061984), 1e-8);
        Assert.AreEqual(value, source.SampleElevationMeters(direction, 24061984), 1e-8);
        Assert.AreEqual(1000.0, source.SampleElevationMeters(direction, 579460630), 1e-8);
    }

    [TestMethod]
    public void SampleElevationMeters_RockFabricHasPhysicalDetailWithoutGridDiscontinuities()
    {
        var anchor = PlanetVector.UnitZ;
        var source = new PlanetHeroStructuralBedrockSource(new FlatBedrockSource(), anchor, 346147916);
        var heights = new List<double>();
        for (var y = -8; y <= 8; y++)
        {
            for (var x = -8; x <= 8; x++)
            {
                heights.Add(source.SampleElevationMeters(Offset(anchor, x * 1_000, y * 1_000), 346147916));
            }
        }

        Assert.IsGreaterThan(25.0, heights.Max() - heights.Min(), "Erodible rock fabric has no physical 16 km relief.");
        var left = source.SampleElevationMeters(Offset(anchor, 1_000, -3_000), 346147916);
        var right = source.SampleElevationMeters(Offset(anchor, 1_001, -3_000), 346147916);
        Assert.IsLessThan(3.0, Math.Abs(left - right), "Bedrock suddenly jumps between neighboring world directions.");
        Assert.IsGreaterThan(0.05, Math.Abs(
            source.SampleElevationMeters(Offset(anchor, 3_000, 0), 346147916) -
            source.SampleElevationMeters(Offset(anchor, 0, 3_000), 346147916)),
            "Bedrock has an artificial concentric radial profile.");
    }

    private static PlanetVector Offset(PlanetVector center, double eastMeters, double northMeters)
    {
        var frame = PlanetLocalFrame.Create(center, RadiusMeters, 0);
        return PlanetVector.Normalize(center * RadiusMeters +
            frame.East * eastMeters + frame.North * northMeters);
    }

    private sealed class FlatBedrockSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 1000;
    }
}
