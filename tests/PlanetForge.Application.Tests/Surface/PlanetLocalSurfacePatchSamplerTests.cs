using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface;

[TestClass]
public sealed class PlanetLocalSurfacePatchSamplerTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Sample_CenterPoint_EqualsLocalFrameOrigin()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new ConstantElevationSource(250.0));
        var patch = sampler.Sample(new PlanetVector(0.3, 0.4, 0.5), 10_000.0, 4, 42, EarthRadiusMeters, CancellationToken.None);

        var center = patch.GetPoint(2, 2);

        Assert.AreEqual(0.0, center.LocalPosition.EastMeters, 0.000001);
        Assert.AreEqual(0.0, center.LocalPosition.NorthMeters, 0.000001);
        Assert.AreEqual(0.0, center.LocalPosition.UpMeters, 0.000001);
    }

    [TestMethod]
    public void Sample_RefinedPatch_PreservesAlignedWorldSamples()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new DirectionElevationSource());
        var anchor = new PlanetVector(0.31, 0.72, -0.22);
        var coarse = sampler.Sample(anchor, 20_000.0, 4, 42, EarthRadiusMeters, CancellationToken.None);
        var fine = sampler.Sample(anchor, 20_000.0, 8, 42, EarthRadiusMeters, CancellationToken.None);

        for (var y = 0; y <= coarse.CellsPerAxis; y++)
        {
            for (var x = 0; x <= coarse.CellsPerAxis; x++)
            {
                AssertPointEqual(coarse.GetPoint(x, y), fine.GetPoint(x * 2, y * 2));
            }
        }
    }

    [TestMethod]
    public void Sample_FlatPlanet_CornersCurveBelowLocalTangentPlane()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new ConstantElevationSource(0.0));
        var patch = sampler.Sample(PlanetVector.UnitZ, 100_000.0, 4, 42, EarthRadiusMeters, CancellationToken.None);

        Assert.IsTrue(patch.GetPoint(0, 0).LocalPosition.UpMeters < 0.0);
        Assert.IsTrue(patch.GetPoint(4, 4).LocalPosition.UpMeters < 0.0);
    }

    [TestMethod]
    public void Sample_PreCancelledToken_Throws()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new ConstantElevationSource(0.0));
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            sampler.Sample(PlanetVector.UnitZ, 10_000.0, 4, 42, EarthRadiusMeters, cancellationTokenSource.Token));
    }

    private static void AssertPointEqual(PlanetLocalSurfacePoint expected, PlanetLocalSurfacePoint actual)
    {
        Assert.AreEqual(expected.Direction.X, actual.Direction.X, 0.000000000001);
        Assert.AreEqual(expected.Direction.Y, actual.Direction.Y, 0.000000000001);
        Assert.AreEqual(expected.Direction.Z, actual.Direction.Z, 0.000000000001);
        Assert.AreEqual(expected.ElevationMeters, actual.ElevationMeters, 0.000000001);
        Assert.AreEqual(expected.LocalPosition.EastMeters, actual.LocalPosition.EastMeters, 0.000001);
        Assert.AreEqual(expected.LocalPosition.NorthMeters, actual.LocalPosition.NorthMeters, 0.000001);
        Assert.AreEqual(expected.LocalPosition.UpMeters, actual.LocalPosition.UpMeters, 0.000001);
    }

    private sealed class ConstantElevationSource(double elevationMeters) : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => elevationMeters;
    }

    private sealed class DirectionElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) =>
            (direction.X * 1_000.0) + (direction.Y * 500.0) - (direction.Z * 250.0) + seed;
    }
}
