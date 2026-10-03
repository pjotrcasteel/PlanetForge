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
        Assert.IsTrue(tiles.All(tile => tile.TriangleCount == 32));
        Assert.IsTrue(tiles.All(tile => tile.Positions.Length == 32 * 9));
        Assert.IsTrue(tiles.All(tile => tile.Normals.Length == tile.Positions.Length));
    }

    [TestMethod]
    public void BuildTile_FlatSurface_NormalsPointAwayFromPlanet()
    {
        var builder = CreateBuilder();

        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42);

        for (var offset = 0; offset < tile.Positions.Length; offset += 9)
        {
            var position = new Vector3(tile.Positions[offset], tile.Positions[offset + 1], tile.Positions[offset + 2]);
            var normal = new Vector3(tile.Normals[offset], tile.Normals[offset + 1], tile.Normals[offset + 2]);
            Assert.IsGreaterThan(Vector3.Dot(position, normal), 0f);
        }
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
