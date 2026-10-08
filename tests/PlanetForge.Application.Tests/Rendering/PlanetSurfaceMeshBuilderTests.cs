using System.Numerics;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Rendering;

[TestClass]
public sealed class PlanetSurfaceMeshBuilderTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void BuildGlobal_LevelOne_CreatesExpectedTileAndTriangleCount()
    {
        var builder = CreateBuilder(new FlatElevationSource());

        var tiles = builder.BuildGlobal(1, 4, 42, EarthRadiusMeters);

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
        var builder = CreateBuilder(new FlatElevationSource());
        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42, EarthRadiusMeters);
        var surfaceFloatCount = tile.SurfaceTriangleCount * 9;

        for (var offset = 0; offset < surfaceFloatCount; offset += 9)
        {
            var position = new Vector3(tile.Positions[offset], tile.Positions[offset + 1], tile.Positions[offset + 2]);
            var normal = new Vector3(tile.Normals[offset], tile.Normals[offset + 1], tile.Normals[offset + 2]);
            Assert.IsGreaterThan(0f, Vector3.Dot(position, normal));
        }
    }

    [TestMethod]
    public void BuildTile_SurfaceNormals_AreSmoothPerVertex()
    {
        var builder = CreateBuilder(new ConstantElevationSource(1_000.0));
        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42, EarthRadiusMeters);
        var first = new Vector3(tile.Normals[0], tile.Normals[1], tile.Normals[2]);
        var second = new Vector3(tile.Normals[3], tile.Normals[4], tile.Normals[5]);

        Assert.IsGreaterThan(0.000001f, Vector3.Distance(first, second));
    }

    [TestMethod]
    public void BuildTile_TerrainSlope_ChangesNormalFromRadialDirection()
    {
        var builder = CreateBuilder(new SlopedElevationSource());
        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 8, 42, EarthRadiusMeters);
        var offset = (tile.SurfaceTriangleCount / 2) * 9;
        var position = Vector3.Normalize(new Vector3(tile.Positions[offset], tile.Positions[offset + 1], tile.Positions[offset + 2]));
        var normal = new Vector3(tile.Normals[offset], tile.Normals[offset + 1], tile.Normals[offset + 2]);

        Assert.IsGreaterThan(0.0001f, Vector3.Distance(position, normal));
    }

    [TestMethod]
    public void BuildTile_GlobalResolution_SamplesCanonicalFineScaleNormals()
    {
        var source = new CountingElevationSource();
        var builder = CreateBuilder(source);

        _ = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 1, 0, 0), 24, 42, EarthRadiusMeters);

        // One elevation sample plus two finite-difference probes per vertex. This provides
        // geological detail independent of coarse triangles without paying for four extra probes.
        Assert.AreEqual(3 * 25 * 25, source.SampleCount);
    }

    [TestMethod]
    public void BuildTile_AddsSkirtsBelowSurfaceRadius()
    {
        var builder = CreateBuilder(new FlatElevationSource());
        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42, EarthRadiusMeters);
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

    [TestMethod]
    public void BuildTile_MetreElevation_IsNormalizedOnlyAtRenderBoundary()
    {
        const double elevationMeters = 1_000.0;
        var builder = CreateBuilder(new ConstantElevationSource(elevationMeters));

        var tile = builder.BuildTile(new PlanetTileId(CubeFace.PositiveZ, 0, 0, 0), 4, 42, EarthRadiusMeters);
        var position = new Vector3(tile.Positions[0], tile.Positions[1], tile.Positions[2]);

        Assert.AreEqual(1.0 + (elevationMeters / EarthRadiusMeters), position.Length(), 0.000001);
    }

    private static PlanetSurfaceMeshBuilder CreateBuilder(IPlanetElevationSource elevationSource)
    {
        var sampler = new PlanetSurfaceTileSampler(elevationSource);
        return new PlanetSurfaceMeshBuilder(sampler, elevationSource);
    }

    private sealed class FlatElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => 0.0;
    }

    private sealed class ConstantElevationSource(double elevationMeters) : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => elevationMeters;
    }

    private sealed class SlopedElevationSource : IPlanetElevationSource
    {
        public double SampleElevationMeters(PlanetVector direction, int seed) => direction.X * 250_000.0;
    }

    private sealed class CountingElevationSource : IPlanetElevationSource
    {
        public int SampleCount { get; private set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            SampleCount++;
            return direction.X * 1_000.0;
        }
    }
}