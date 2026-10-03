using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceTileSamplerTests
{
    [TestMethod]
    public void Sample_AdjacentTilesShareIdenticalBoundaryPoints()
    {
        var sampler = new PlanetSurfaceTileSampler(new DirectionElevationSource());
        var left = sampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 1, 0, 0), 8, 42);
        var right = sampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 1, 1, 0), 8, 42);

        for (var y = 0; y < left.PointsPerAxis; y++)
        {
            AssertPointEqual(left.GetPoint(left.CellsPerAxis, y), right.GetPoint(0, y));
        }
    }

    [TestMethod]
    public void Sample_ParentAndChildAlignedPointShareSameWorldSample()
    {
        var sampler = new PlanetSurfaceTileSampler(new DirectionElevationSource());
        var parent = sampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 8, 42);
        var child = sampler.Sample(new PlanetTileId(CubeFace.PositiveZ, 1, 0, 0), 8, 42);

        AssertPointEqual(parent.GetPoint(4, 4), child.GetPoint(8, 8));
    }

    private static void AssertPointEqual(PlanetSurfacePoint expected, PlanetSurfacePoint actual)
    {
        Assert.AreEqual(expected.Direction.X, actual.Direction.X, 0.0000000001);
        Assert.AreEqual(expected.Direction.Y, actual.Direction.Y, 0.0000000001);
        Assert.AreEqual(expected.Direction.Z, actual.Direction.Z, 0.0000000001);
        Assert.AreEqual(expected.ElevationMeters, actual.ElevationMeters, 0.0000000001);
    }

    private sealed class DirectionElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) =>
            ((direction.X * 0.3) + (direction.Y * 0.2) + (direction.Z * 0.1) + (seed * 0.000001)) * 1_000.0;
    }
}
