using System.Numerics;
using PlanetForge.Application.Surface;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetSurfaceMeshBuilder(PlanetSurfaceTileSampler tileSampler)
{
    private const double ElevationScale = 0.055;

    public PlanetSurfaceTileMesh BuildTile(PlanetTileId id, int cellsPerAxis, int seed)
    {
        var tile = tileSampler.Sample(id, cellsPerAxis, seed);
        var triangleCount = cellsPerAxis * cellsPerAxis * 2;
        var positions = new float[triangleCount * 9];
        var normals = new float[triangleCount * 9];
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

        return new PlanetSurfaceTileMesh(id, positions, normals, triangleCount);
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
