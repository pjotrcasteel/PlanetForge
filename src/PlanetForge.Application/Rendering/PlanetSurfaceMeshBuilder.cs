using System.Numerics;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetSurfaceMeshBuilder(PlanetSurfaceTileSampler tileSampler)
{
    private const double MinimumSkirtDepthMeters = 5.0;
    private const double MaximumSkirtDepthMeters = 500.0;

    public PlanetSurfaceTileMesh BuildTile(PlanetTileId id, int cellsPerAxis, int seed, double planetRadiusMeters)
    {
        ValidatePlanetRadius(planetRadiusMeters);
        var tile = tileSampler.Sample(id, cellsPerAxis, seed);
        var surfaceTriangleCount = cellsPerAxis * cellsPerAxis * 2;
        var skirtTriangleCount = cellsPerAxis * 8;
        var totalTriangleCount = surfaceTriangleCount + skirtTriangleCount;
        var positions = new float[totalTriangleCount * 9];
        var normals = new float[totalTriangleCount * 9];
        var offset = 0;

        for (var y = 0; y < cellsPerAxis; y++)
        {
            for (var x = 0; x < cellsPerAxis; x++)
            {
                var a = ToRenderVector(tile.GetPoint(x, y), planetRadiusMeters);
                var b = ToRenderVector(tile.GetPoint(x + 1, y), planetRadiusMeters);
                var c = ToRenderVector(tile.GetPoint(x, y + 1), planetRadiusMeters);
                var d = ToRenderVector(tile.GetPoint(x + 1, y + 1), planetRadiusMeters);
                WriteSurfaceTriangle(positions, normals, ref offset, a, c, b);
                WriteSurfaceTriangle(positions, normals, ref offset, b, c, d);
            }
        }

        WriteSkirts(tile, cellsPerAxis, planetRadiusMeters, positions, normals, ref offset);
        return new PlanetSurfaceTileMesh(id, positions, normals, surfaceTriangleCount, skirtTriangleCount);
    }

    public IReadOnlyList<PlanetSurfaceTileMesh> BuildGlobal(int level, int cellsPerAxis, int seed, double planetRadiusMeters)
    {
        if (level < 0 || level > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Global render level must be between 0 and 8.");
        }

        ValidatePlanetRadius(planetRadiusMeters);
        var tilesPerAxis = 1 << level;
        var result = new List<PlanetSurfaceTileMesh>(6 * tilesPerAxis * tilesPerAxis);

        foreach (var face in Enum.GetValues<CubeFace>())
        {
            for (var y = 0; y < tilesPerAxis; y++)
            {
                for (var x = 0; x < tilesPerAxis; x++)
                {
                    result.Add(BuildTile(new PlanetTileId(face, level, x, y), cellsPerAxis, seed, planetRadiusMeters));
                }
            }
        }

        return result;
    }

    private static void WriteSkirts(
        PlanetSurfaceTile tile,
        int cellsPerAxis,
        double planetRadiusMeters,
        float[] positions,
        float[] normals,
        ref int offset)
    {
        var bounds = PlanetTileGeometry.CalculateBounds(tile.Id);
        var tileArcLengthMeters = planetRadiusMeters * bounds.AngularRadiusRadians * 2.0;
        var approximateCellSizeMeters = tileArcLengthMeters / cellsPerAxis;
        var skirtDepthMeters = Math.Clamp(approximateCellSizeMeters * 0.05, MinimumSkirtDepthMeters, MaximumSkirtDepthMeters);

        for (var index = 0; index < cellsPerAxis; index++)
        {
            WriteSkirtSegment(tile.GetPoint(index, 0), tile.GetPoint(index + 1, 0), planetRadiusMeters, skirtDepthMeters, positions, normals, ref offset);
            WriteSkirtSegment(tile.GetPoint(cellsPerAxis, index), tile.GetPoint(cellsPerAxis, index + 1), planetRadiusMeters, skirtDepthMeters, positions, normals, ref offset);
            WriteSkirtSegment(tile.GetPoint(index + 1, cellsPerAxis), tile.GetPoint(index, cellsPerAxis), planetRadiusMeters, skirtDepthMeters, positions, normals, ref offset);
            WriteSkirtSegment(tile.GetPoint(0, index + 1), tile.GetPoint(0, index), planetRadiusMeters, skirtDepthMeters, positions, normals, ref offset);
        }
    }

    private static void WriteSkirtSegment(
        PlanetSurfacePoint first,
        PlanetSurfacePoint second,
        double planetRadiusMeters,
        double skirtDepthMeters,
        float[] positions,
        float[] normals,
        ref int offset)
    {
        var firstSurface = first.WorldPositionMeters(planetRadiusMeters);
        var secondSurface = second.WorldPositionMeters(planetRadiusMeters);
        var firstInner = LowerRadially(firstSurface, skirtDepthMeters);
        var secondInner = LowerRadially(secondSurface, skirtDepthMeters);
        var a = ToRenderVector(firstSurface, planetRadiusMeters);
        var b = ToRenderVector(secondSurface, planetRadiusMeters);
        var c = ToRenderVector(firstInner, planetRadiusMeters);
        var d = ToRenderVector(secondInner, planetRadiusMeters);
        WriteFlatTriangle(positions, normals, ref offset, a, c, b);
        WriteFlatTriangle(positions, normals, ref offset, b, c, d);
    }

    private static PlanetVector LowerRadially(PlanetVector positionMeters, double skirtDepthMeters)
    {
        var length = positionMeters.Length;
        var targetLength = Math.Max(1.0, length - skirtDepthMeters);
        return positionMeters * (targetLength / length);
    }

    private static Vector3 ToRenderVector(PlanetSurfacePoint point, double planetRadiusMeters) => ToRenderVector(point.WorldPositionMeters(planetRadiusMeters), planetRadiusMeters);

    private static Vector3 ToRenderVector(PlanetVector worldPositionMeters, double planetRadiusMeters)
    {
        var normalized = worldPositionMeters / planetRadiusMeters;
        return new Vector3((float)normalized.X, (float)normalized.Y, (float)normalized.Z);
    }

    private static void WriteSurfaceTriangle(float[] positions, float[] normals, ref int offset, Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var center = (a + b + c) / 3f;

        if (Vector3.Dot(normal, center) < 0f)
        {
            (b, c) = (c, b);
            normal = -normal;
        }

        normal = Vector3.Normalize(normal);
        WriteVector(positions, offset, a);
        WriteVector(positions, offset + 3, b);
        WriteVector(positions, offset + 6, c);
        WriteVector(normals, offset, normal);
        WriteVector(normals, offset + 3, normal);
        WriteVector(normals, offset + 6, normal);
        offset += 9;
    }

    private static void WriteFlatTriangle(float[] positions, float[] normals, ref int offset, Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        var center = (a + b + c) / 3f;

        if (Vector3.Dot(normal, center) < 0f)
        {
            (b, c) = (c, b);
            normal = -normal;
        }

        normal = Vector3.Normalize(normal);
        WriteVector(positions, offset, a);
        WriteVector(positions, offset + 3, b);
        WriteVector(positions, offset + 6, c);
        WriteVector(normals, offset, normal);
        WriteVector(normals, offset + 3, normal);
        WriteVector(normals, offset + 6, normal);
        offset += 9;
    }

    private static void WriteVector(float[] target, int offset, Vector3 value)
    {
        target[offset] = value.X;
        target[offset + 1] = value.Y;
        target[offset + 2] = value.Z;
    }

    private static void ValidatePlanetRadius(double planetRadiusMeters)
    {
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters), planetRadiusMeters, "Planet radius must be finite and greater than zero.");
        }
    }
}
