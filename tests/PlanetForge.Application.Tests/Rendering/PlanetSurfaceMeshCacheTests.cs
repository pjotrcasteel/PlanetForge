using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Rendering;

[TestClass]
public sealed class PlanetSurfaceMeshCacheTests
{
    [TestMethod]
    public void GetOrBuild_SameTileSeedAndRadius_ReturnsCachedMeshInstance()
    {
        var cache = CreateCache();
        var id = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);

        var first = cache.GetOrBuild(id, 8, 42, 6_371_000.0);
        var second = cache.GetOrBuild(id, 8, 42, 6_371_000.0);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetOrBuild_SameTileSetTwice_ReturnsReferencesWithoutGeometryOnSecondRequest()
    {
        var cache = CreateCache();
        PlanetTileId[] ids =
        [
            new(CubeFace.PositiveZ, 1, 0, 0),
            new(CubeFace.PositiveZ, 1, 1, 0),
        ];

        var first = cache.GetOrBuild(ids, 8, 42, 6_371_000.0);
        var second = cache.GetOrBuild(ids, 8, 42, 6_371_000.0);

        Assert.IsTrue(first.All(mesh => mesh.IncludesGeometry));
        Assert.IsTrue(second.All(mesh => !mesh.IncludesGeometry));
        Assert.IsTrue(first.Select(mesh => mesh.Key).SequenceEqual(second.Select(mesh => mesh.Key)));
        Assert.IsTrue(first.Select(mesh => mesh.SurfaceVertexCount).SequenceEqual(second.Select(mesh => mesh.SurfaceVertexCount)));
    }

    [TestMethod]
    public void GetOrBuild_DifferentTileSet_ReturnsGeometryForNewRequest()
    {
        var cache = CreateCache();
        PlanetTileId[] firstIds = [new(CubeFace.PositiveZ, 1, 0, 0)];
        PlanetTileId[] secondIds = [new(CubeFace.PositiveZ, 1, 1, 0)];

        _ = cache.GetOrBuild(firstIds, 8, 42, 6_371_000.0);
        var second = cache.GetOrBuild(secondIds, 8, 42, 6_371_000.0);

        Assert.IsTrue(second.All(mesh => mesh.IncludesGeometry));
    }

    [TestMethod]
    public void GetOrBuild_AdjacentViews_OnlyTransmitsGeometryForNewTiles()
    {
        var cache = CreateCache();
        var stable = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 1);
        var previous = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);
        var next = new PlanetTileId(CubeFace.PositiveZ, 2, 2, 1);

        _ = cache.GetOrBuild([stable, previous], 8, 42, 6_371_000.0);
        var rotated = cache.GetOrBuild([stable, next], 8, 42, 6_371_000.0);

        Assert.IsFalse(rotated[0].IncludesGeometry, "The unchanged tile must be reused from the browser's GPU cache.");
        Assert.IsTrue(rotated[1].IncludesGeometry, "The newly visible tile must contain vertex data.");
        Assert.AreEqual(stable, rotated[0].Id);
        Assert.AreEqual(next, rotated[1].Id);
    }

    [TestMethod]
    public void GetOrBuild_ChangingTessellation_ResendsGeometry()
    {
        var cache = CreateCache();
        PlanetTileId[] tiles = [new(CubeFace.PositiveZ, 2, 1, 1)];

        _ = cache.GetOrBuild(tiles, 8, 42, 6_371_000.0);
        var differentCells = cache.GetOrBuild(tiles, 16, 42, 6_371_000.0);

        Assert.IsTrue(differentCells.Single().IncludesGeometry);
    }

    [TestMethod]
    public void GetOrBuild_SeedChange_InvalidatesCachedMeshes()
    {
        var cache = CreateCache();
        var id = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);

        var first = cache.GetOrBuild(id, 8, 42, 6_371_000.0);
        var second = cache.GetOrBuild(id, 8, 43, 6_371_000.0);

        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void GetOrBuild_RadiusChange_UsesDifferentMesh()
    {
        var cache = CreateCache();
        var id = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);

        var first = cache.GetOrBuild(id, 8, 42, 6_371_000.0);
        var second = cache.GetOrBuild(id, 8, 42, 3_185_500.0);

        Assert.AreNotSame(first, second);
    }

    private static PlanetSurfaceMeshCache CreateCache()
    {
        var elevationSource = new FlatElevationSource();
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        return new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(sampler, elevationSource));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}