using System.Numerics;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetSurfaceMeshBuilder(PlanetSurfaceTileSampler tileSampler)
{
    private const double ElevationScale = 0.055;
    private const double SkirtDepth = 0.004;

    public PlanetSurfaceTileMesh BuildTile(PlanetTileId id, int cellsPerAxis, int seed)
    {
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
                var a = ToRenderVector(tile.GetPoint(x, y).Position(ElevationScale));
                var b = ToRenderVector(tile.GetPoint(x + 1, y).Position(ElevationScale));
                var c = ToRenderVector(tile.GetPoint(x, y + 1).Position(ElevationScale));
                var d = ToRenderVector(tile.GetPoint(x + 1, y + 1).Position(ElevationScale));
                WriteTriangle(positions, normals, ref offset, a, c, b);
                WriteTriangle(positions, normals, ref offset, b, c, d);
            }
        }

        WriteSkirts(tile, cellsPerAxis, positions, normals, ref offset);
        return new PlanetSurfaceTileMesh(id, positions, normals, surfaceTriangleCount, skirtTriangleCount);
    }

    public IReadOnlyList<PlanetSurfaceTileMesh> BuildGlobal(int level, int cellsPerAxis, int seed)
    {
        if (level < 0 || level > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Global render level must be between 0 and 8.");
        }

        var tilesPerAxis = 1 << level;
        var result = new List<PlanetSurfaceTileMesh>(6 * tilesPerAxis * tilesPerAxis);

        foreach (var face in Enum.GetValues<CubeFace>())
        {
            for (var y = 0; y < tilesPerAxis; y++)
            {
                for (var x = 0; x < tilesPerAxis; x++)
                {
                    result.Add(BuildTile(new PlanetTileId(face, level, x, y), cellsPerAxis, seed));
                }
            }
        }

        return result;
    }

    private static void WriteSkirts(PlanetSurfaceTile tile, int cellsPerAxis, float[] positions, float[] normals, ref int offset)
    {
        for (var index = 0; index < cellsPerAxis; index++)
        {
            WriteSkirtSegment(tile.GetPoint(index, 0), tile.GetPoint(index + 1, 0), positions, normals, ref offset);
            WriteSkirtSegment(tile.GetPoint(cellsPerAxis, index), tile.GetPoint(cellsPerAxis, index + 1), positions, normals, ref offset);
            WriteSkirtSegment(tile.GetPoint(index + 1, cellsPerAxis), tile.GetPoint(index, cellsPerAxis), positions, normals, ref offset);
            WriteSkirtSegment(tile.GetPoint(0, index + 1), tile.GetPoint(0, index), positions, normals, ref offset);
        }
    }

    private static void WriteSkirtSegment(PlanetSurfacePoint first, PlanetSurfacePoint second, float[] positions, float[] normals, ref int offset)
    {
        var firstSurface = first.Position(ElevationScale);
        var secondSurface = second.Position(ElevationScale);
        var firstInner = LowerRadially(firstSurface);
        var secondInner = LowerRadially(secondSurface);
        var a = ToRenderVector(firstSurface);
        var b = ToRenderVector(secondSurface);
        var c = ToRenderVector(firstInner);
        var d = ToRenderVector(secondInner);
        WriteTriangle(positions, normals, ref offset, a, c, b);
        WriteTriangle(positions, normals, ref offset, b, c, d);
    }

    private static PlanetVector LowerRadially(PlanetVector position)
    {
        var length = position.Length;
        var targetLength = Math.Max(0.001, length - SkirtDepth);
        return position * (targetLength / length);
    }

    private static Vector3 ToRenderVector(PlanetVector value) => new((float)value.X, (float)value.Y, (float)value.Z);

    private static void WriteTriangle(float[] positions, float[] normals, ref int offset, Vector3 a, Vector3 b, Vector3 c)
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
}
