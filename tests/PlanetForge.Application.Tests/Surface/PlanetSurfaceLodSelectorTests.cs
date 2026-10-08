using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Surface;

[TestClass]
public sealed class PlanetSurfaceLodSelectorTests
{
    [TestMethod]
    public void Select_NearSurface_ReachesHigherDetailThanFarOrbit()
    {
        var selector = new PlanetSurfaceLodSelector(new PlanetSurfaceLodOptions(7, 220.0, 0.02));
        var direction = new PlanetVector(0.0, 0.0, 1.0);
        var far = selector.Select(new PlanetSurfaceView(direction, 5.0, 1080, Math.PI / 4.0));
        var near = selector.Select(new PlanetSurfaceView(direction, 1.2, 1080, Math.PI / 4.0));

        Assert.IsGreaterThan(far.Max(tile => tile.Level), near.Max(tile => tile.Level));
    }

    [TestMethod]
    public void Select_FarOrbit_UsesSameCompleteLevelOneCoverageForEveryDirection()
    {
        var selector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        var first = selector.Select(new PlanetSurfaceView(PlanetVector.UnitZ, 3.15, 1080, Math.PI / 4.0));
        var second = selector.Select(new PlanetSurfaceView(PlanetVector.UnitX, 3.15, 1080, Math.PI / 4.0));

        Assert.AreEqual(24, first.Count);
        Assert.IsTrue(first.All(tile => tile.Level == 1));
        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
    }

    [TestMethod]
    public void Select_AdaptiveView_OppositeRootFaceKeepsCoarseFallbackCoverage()
    {
        var selector = new PlanetSurfaceLodSelector(new PlanetSurfaceLodOptions(3, 220.0, 0.0));

        var tiles = selector.Select(new PlanetSurfaceView(new PlanetVector(0.0, 0.0, 1.0), 1.3, 1080, Math.PI / 4.0));

        Assert.IsTrue(tiles.Any(tile => tile.Face == CubeFace.NegativeZ && tile.Level == 0));
        Assert.IsTrue(tiles.Any(tile => tile.Face == CubeFace.PositiveZ && tile.Level > 0));
    }

    [TestMethod]
    public void Select_NeverExceedsConfiguredMaximumLevel()
    {
        const int maxLevel = 4;
        var selector = new PlanetSurfaceLodSelector(new PlanetSurfaceLodOptions(maxLevel, 1.0, 0.02));

        var tiles = selector.Select(new PlanetSurfaceView(new PlanetVector(0.0, 0.0, 1.0), 1.05, 2160, Math.PI / 4.0));

        Assert.IsTrue(tiles.Count > 0);
        Assert.IsTrue(tiles.All(tile => tile.Level <= maxLevel));
    }

    [TestMethod]
    public void Select_ApproachingPlanet_IncreasesMaximumLevelWithoutExceedingMobileBudget()
    {
        var selector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        var middle = selector.Select(new PlanetSurfaceView(PlanetVector.UnitZ, 1.22, 844, Math.PI / 4.2));
        var close = selector.Select(new PlanetSurfaceView(PlanetVector.UnitZ, 1.03, 844, Math.PI / 4.2));
        var veryClose = selector.Select(new PlanetSurfaceView(PlanetVector.UnitZ, 1.004, 844, Math.PI / 4.2));

        Assert.IsGreaterThan(1, middle.Max(tile => tile.Level));
        Assert.IsGreaterThan(middle.Max(tile => tile.Level), close.Max(tile => tile.Level));
        Assert.IsGreaterThanOrEqualTo(close.Max(tile => tile.Level), veryClose.Max(tile => tile.Level));
        Assert.IsTrue(new[] { middle, close, veryClose }.All(tiles => tiles.Count <= 56));
    }

    [TestMethod]
    public void Select_CloseOrbitalDescent_RefinesBeyondLevelSixWithinMobileTileBudget()
    {
        var selector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        foreach (var direction in new[] { PlanetVector.UnitX, PlanetVector.UnitY, PlanetVector.UnitZ })
        {
            var view = new PlanetSurfaceView(direction, 1.0039, 844, Math.PI / 4.2);
            var tiles = selector.Select(view);

            Assert.IsLessThanOrEqualTo(56, tiles.Count);
            Assert.IsGreaterThanOrEqualTo(7, tiles.Max(tile => tile.Level));
            Assert.IsTrue(tiles.All(tile => tile.Level <= 8));
        }
    }

    [TestMethod]
    public void Select_CameraInsidePlanet_Throws()
    {
        var selector = new PlanetSurfaceLodSelector(PlanetSurfaceLodOptions.Default);
        var view = new PlanetSurfaceView(new PlanetVector(0.0, 0.0, 1.0), 1.0, 1080, Math.PI / 4.0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => selector.Select(view));
    }
}
