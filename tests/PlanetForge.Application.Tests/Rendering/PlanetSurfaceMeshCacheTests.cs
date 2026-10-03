using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Rendering;

[TestClass]
public sealed class PlanetSurfaceMeshCacheTests
{
    [TestMethod]
    public void GetOrBuild_SameTileAndSeed_ReturnsCachedMeshInstance()
    {
        var cache = CreateCache();
        var id = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);

        var first = cache.GetOrBuild(id, 8, 42);
        var second = cache.GetOrBuild(id, 8, 42);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void GetOrBuild_SeedChange_InvalidatesCachedMeshes()
    {
        var cache = CreateCache();
        var id = new PlanetTileId(CubeFace.PositiveZ, 2, 1, 2);

        var first = cache.GetOrBuild(id, 8, 42);
        var second = cache.GetOrBuild(id, 8, 43);

        Assert.AreNotSame(first, second);
    }

    private static PlanetSurfaceMeshCache CreateCache()
    {
        var sampler = new PlanetSurfaceTileSampler(new FlatElevationSource());
        return new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(sampler));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double Sample(PlanetVector direction, int seed) => 0.0;
    }
}
