using System.Numerics;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Rendering;

[TestClass]
public sealed class PlanetSurfaceMeshBuilderTests
{
    [TestMethod]
    public void BuildGlobal_LevelOne_CreatesExpectedTileAndTriangleCount()
    {
        var builder = CreateBuilder();

        var tiles = builder.BuildGlobal(1, 4, 42);

        Assert.AreEqual(24, tiles.Count);
        Assert.IsTrue(tiles.All(tile => tile.SurfaceTriangleCount == 32));
        Assert.IsTrue(tiles.All(tile => tile.SkirtTriangleCount == 32));
        Assert.IsTrue(tiles.All(tile => tile.TriangleCount == 64));
        Assert.IsTrue(tiles.All(tile => tile.Positions.Length == 64 * 9));
        Assert.IsTrue(tiles.All(tile => tile.Normals.Length == tile.Positions.Length));
    }

    [TestMethod]
    public void BuildTile_FlatSurface_NormalsPointAwayFromPlanet()
    {
        var builder = CreateBuilder();
        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42);
        var surfaceFloatCount = tile.SurfaceTriangleCount * 9;

        for (var offset = 0; offset < surfaceFloatCount; offset += 9)
        {
            var position = new Vector3(tile.Positions[offset], tile.Positions[offset + 1], tile.Positions[offset + 2]);
            var normal = new Vector3(tile.Normals[offset], tile.Normals[offset + 1], tile.Normals[offset + 2]);
            Assert.IsGreaterThan(0f, Vector3.Dot(position, normal));
        }
    }

    [TestMethod]
    public void BuildTile_AddsSkirtsBelowSurfaceRadius()
    {
        var builder = CreateBuilder();
        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42);
        var skirtStart = tile.SurfaceTriangleCount * 9;
        var minimumSkirtRadius = double.MaxValue;

        for (var offset = skirtStart; offset < tile.Positions.Length; offset += 3)
        {
            var radius = Math.Sqrt(
                (tile.Positions[offset] * tile.Positions[offset]) +
                (tile.Positions[offset + 1] * tile.Positions[offset + 1]) +
                (tile.Positions[offset + 2] * tile.Positions[offset + 2]));
            minimumSkirtRadius = Math.Min(minimumSkirtRadius, radius);
        }

        Assert.IsLessThan(1.0, minimumSkirtRadius);
    }

    private static PlanetSurfaceMeshBuilder CreateBuilder()
    {
        var sampler = new PlanetSurfaceTileSampler(new FlatElevationSource());
        return new PlanetSurfaceMeshBuilder(sampler);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double Sample(PlanetVector direction, int seed) => 0.0;
    }
}
