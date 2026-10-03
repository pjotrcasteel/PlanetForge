using PlanetForge.Application.Rendering;
using PlanetForge.Application.Terrain;

namespace PlanetForge.Application.Tests.Rendering;

[TestClass]
public sealed class PlanetMeshBuilderTests
{
    [TestMethod]
    public void Build_CreatesExpectedLowPolyTriangleCount()
    {
        var builder = new PlanetMeshBuilder(new FlatTerrainNoise());

        var mesh = builder.Build(42);

        Assert.AreEqual(1280, mesh.TriangleCount);
        Assert.AreEqual(1280 * 9, mesh.Positions.Length);
        Assert.AreEqual(mesh.Positions.Length, mesh.Normals.Length);
    }

    private sealed class FlatTerrainNoise : IPlanetTerrainNoise
    {
        public double Sample(double x, double y, double z, int seed) => 0;
    }
}
