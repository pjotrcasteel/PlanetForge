using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Rendering;

[TestClass]
public sealed class PlanetLocalSurfaceMeshBuilderTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void Build_FlatPatch_CreatesUpwardFacingTriangles()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new FlatElevationSource());
        var patch = sampler.Sample(PlanetVector.UnitZ, 1_000.0, 4, 42, EarthRadiusMeters, CancellationToken.None);
        var mesh = new PlanetLocalSurfaceMeshBuilder().Build(patch, 500.0);

        Assert.AreEqual(32, mesh.TriangleCount);
        Assert.AreEqual(mesh.VertexCount * 3, mesh.PositionsMeters.Length);
        Assert.AreEqual(mesh.VertexCount * 3, mesh.Normals.Length);
        Assert.AreEqual(mesh.VertexCount, mesh.ElevationsMeters.Length);

        for (var offset = 1; offset < mesh.Normals.Length; offset += 3)
        {
            Assert.IsTrue(mesh.Normals[offset] > 0f);
        }
    }

    [TestMethod]
    public void Build_SamePatchAtDifferentAltitude_PreservesGeometryKey()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new FlatElevationSource());
        var patch = sampler.Sample(new PlanetVector(0.2, 0.4, 0.8), 2_000.0, 4, 42, EarthRadiusMeters, CancellationToken.None);
        var builder = new PlanetLocalSurfaceMeshBuilder();

        var low = builder.Build(patch, 250.0);
        var high = builder.Build(patch, 2_500.0);

        Assert.AreEqual(low.Key, high.Key);
        Assert.AreEqual(250.0, low.CameraAltitudeMeters);
        Assert.AreEqual(2_500.0, high.CameraAltitudeMeters);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
