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
        var sampler = new PlanetSurfaceTileSampler(new FlatElevationSource());
        return new PlanetSurfaceMeshCache(new PlanetSurfaceMeshBuilder(sampler));
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
