using System.Numerics;
using PlanetForge.Application.Terrain;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetMeshBuilder(IPlanetTerrainNoise terrainNoise)
{
    private const int SubdivisionLevel = 3;
    private const float ElevationScale = 0.055f;

    private static readonly Vector3[] IcosahedronVertices = CreateIcosahedronVertices();

    private static readonly int[] IcosahedronFaces =
    [
        0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
        1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
        4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
    ];

    public PlanetMesh Build(int seed)
    {
        var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>(1280);

        for (var index = 0; index < IcosahedronFaces.Length; index += 3)
        {
            Subdivide(
                IcosahedronVertices[IcosahedronFaces[index]],
                IcosahedronVertices[IcosahedronFaces[index + 1]],
                IcosahedronVertices[IcosahedronFaces[index + 2]],
                SubdivisionLevel,
                triangles);
        }

        var positions = new float[triangles.Count * 9];
        var normals = new float[triangles.Count * 9];
        var offset = 0;

        foreach (var triangle in triangles)
        {
            var a = Deform(triangle.A, seed);
            var b = Deform(triangle.B, seed);
            var c = Deform(triangle.C, seed);
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));

            WriteVector(positions, offset, a);
            WriteVector(positions, offset + 3, b);
            WriteVector(positions, offset + 6, c);
            WriteVector(normals, offset, normal);
            WriteVector(normals, offset + 3, normal);
            WriteVector(normals, offset + 6, normal);
            offset += 9;
        }

        return new PlanetMesh(positions, normals, triangles.Count);
    }

    private Vector3 Deform(Vector3 point, int seed)
    {
        var elevation = terrainNoise.Sample(point.X, point.Y, point.Z, seed);
        var radius = 1f + (float)elevation * ElevationScale;
        return point * radius;
    }

    private static void Subdivide(Vector3 a, Vector3 b, Vector3 c, int remaining, List<(Vector3 A, Vector3 B, Vector3 C)> triangles)
    {
        if (remaining == 0)
        {
            triangles.Add((a, b, c));
            return;
        }

        var ab = Vector3.Normalize((a + b) * 0.5f);
        var bc = Vector3.Normalize((b + c) * 0.5f);
        var ca = Vector3.Normalize((c + a) * 0.5f);

        Subdivide(a, ab, ca, remaining - 1, triangles);
        Subdivide(b, bc, ab, remaining - 1, triangles);
        Subdivide(c, ca, bc, remaining - 1, triangles);
        Subdivide(ab, bc, ca, remaining - 1, triangles);
    }

    private static Vector3[] CreateIcosahedronVertices()
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        Vector3[] vertices =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        ];

        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = Vector3.Normalize(vertices[index]);
        }

        return vertices;
    }

    private static void WriteVector(float[] target, int offset, Vector3 value)
    {
        target[offset] = value.X;
        target[offset + 1] = value.Y;
        target[offset + 2] = value.Z;
    }
}
