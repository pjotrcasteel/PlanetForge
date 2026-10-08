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
    public void Build_SharedTriangleVertices_UseIdenticalSmoothNormals()
    {
        var sampler = new PlanetLocalSurfacePatchSampler(new TiltedElevationSource());
        var patch = sampler.Sample(PlanetVector.UnitZ, 1_000.0, 16, 42, EarthRadiusMeters, CancellationToken.None);
        var mesh = new PlanetLocalSurfaceMeshBuilder().Build(patch, 250.0);

        // Each square is a pair of triangles (a,b,c) and (b,d,c).
        // Shared vertices b and c must agree exactly rather than receive separate facet normals.
        for (var cell = 0; cell < 16 * 16; cell++)
        {
            var vertexOffset = cell * 18;
            for (var axis = 0; axis < 3; axis++)
            {
                Assert.AreEqual(mesh.Normals[vertexOffset + 3 + axis], mesh.Normals[vertexOffset + 9 + axis]);
                Assert.AreEqual(mesh.Normals[vertexOffset + 6 + axis], mesh.Normals[vertexOffset + 15 + axis]);
            }
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

    private sealed class TiltedElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) =>
            (direction.X * 20_000.0) + (direction.Y * direction.Y * 900_000.0);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }
}
