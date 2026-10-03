using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceCellAnalyzerTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Analyze_FlatSurface_HasZeroSlopeAndNoDrainageTarget()
    {
        var analyzer = new PlanetSurfaceCellAnalyzer(new FlatElevationSource());
        var cell = new PlanetSurfaceGridCellId(CubeFace.PositiveZ, 4, 7, 7);

        var result = analyzer.Analyze(cell, 42, EarthRadiusMeters);

        Assert.AreEqual(0.0, result.SlopeRadians, 0.0000000001);
        Assert.IsNull(result.DrainageTarget);
        Assert.AreEqual(0.0, result.SteepestDownhillGradient, 0.0000000001);
    }

    [TestMethod]
    public void Analyze_SurfaceDescendingWest_DrainsToWestNeighbor()
    {
        var analyzer = new PlanetSurfaceCellAnalyzer(new EastwardRiseElevationSource());
        var cell = new PlanetSurfaceGridCellId(CubeFace.PositiveZ, 5, 17, 16);
        var expected = PlanetSurfaceGridTopology.GetNeighbor(cell, PlanetGridDirection.West).Cell;

        var result = analyzer.Analyze(cell, 42, EarthRadiusMeters);

        Assert.AreEqual(expected, result.DrainageTarget);
        Assert.IsLessThan(0.0, result.SteepestDownhillGradient);
        Assert.IsGreaterThan(0.0, result.SlopeRadians);
    }

    [TestMethod]
    public void Analyze_InvalidPlanetRadius_Throws()
    {
        var analyzer = new PlanetSurfaceCellAnalyzer(new FlatElevationSource());
        var cell = new PlanetSurfaceGridCellId(CubeFace.PositiveZ, 4, 7, 7);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => analyzer.Analyze(cell, 42, 0.0));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }

    private sealed class EastwardRiseElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => direction.X * 10_000.0;
    }
}
