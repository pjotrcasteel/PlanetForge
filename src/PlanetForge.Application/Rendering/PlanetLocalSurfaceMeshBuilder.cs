using System.Numerics;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetLocalSurfaceMeshBuilder
{
    public PlanetLocalSurfaceMesh Build(PlanetLocalSurfacePatch patch, double cameraAltitudeMeters)
    {
        if (!double.IsFinite(cameraAltitudeMeters) || cameraAltitudeMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraAltitudeMeters), cameraAltitudeMeters, "Camera altitude must be finite and greater than zero.");
        }

        var triangleCount = patch.CellsPerAxis * patch.CellsPerAxis * 2;
        var positions = new float[triangleCount * 9];
        var normals = new float[triangleCount * 9];
        var elevations = new float[triangleCount * 3];
        var vertexNormals = BuildVertexNormals(patch);
        var positionOffset = 0;
        var elevationOffset = 0;

        for (var y = 0; y < patch.CellsPerAxis; y++)
        {
            for (var x = 0; x < patch.CellsPerAxis; x++)
            {
                var a = patch.GetPoint(x, y);
                var b = patch.GetPoint(x + 1, y);
                var c = patch.GetPoint(x, y + 1);
                var d = patch.GetPoint(x + 1, y + 1);
                var normalA = vertexNormals[ToPointIndex(x, y, patch.CellsPerAxis)];
                var normalB = vertexNormals[ToPointIndex(x + 1, y, patch.CellsPerAxis)];
                var normalC = vertexNormals[ToPointIndex(x, y + 1, patch.CellsPerAxis)];
                var normalD = vertexNormals[ToPointIndex(x + 1, y + 1, patch.CellsPerAxis)];
                WriteTriangle(positions, normals, elevations, ref positionOffset, ref elevationOffset, a, normalA, b, normalB, c, normalC);
                WriteTriangle(positions, normals, elevations, ref positionOffset, ref elevationOffset, b, normalB, d, normalD, c, normalC);
            }
        }

        var anchorAddress = PlanetSurfaceAddressing.Encode(patch.Frame.Up);
        var key = FormattableString.Invariant($"{anchorAddress}:{patch.SizeMeters:R}:{patch.CellsPerAxis}");
        return new PlanetLocalSurfaceMesh(
            key,
            anchorAddress,
            patch.Frame.Up,
            positions,
            normals,
            elevations,
            triangleCount,
            patch.SizeMeters,
            cameraAltitudeMeters);
    }

    private static Vector3[] BuildVertexNormals(PlanetLocalSurfacePatch patch)
    {
        var result = new Vector3[(patch.CellsPerAxis + 1) * (patch.CellsPerAxis + 1)];

        for (var y = 0; y <= patch.CellsPerAxis; y++)
        {
            for (var x = 0; x <= patch.CellsPerAxis; x++)
            {
                var west = ToRenderVector(patch.GetPoint(Math.Max(0, x - 1), y).LocalPosition);
                var east = ToRenderVector(patch.GetPoint(Math.Min(patch.CellsPerAxis, x + 1), y).LocalPosition);
                var south = ToRenderVector(patch.GetPoint(x, Math.Max(0, y - 1)).LocalPosition);
                var north = ToRenderVector(patch.GetPoint(x, Math.Min(patch.CellsPerAxis, y + 1)).LocalPosition);
                var normal = Vector3.Cross(east - west, north - south);
                if (normal.LengthSquared() <= float.Epsilon)
                {
                    normal = Vector3.UnitY;
                }
                else
                {
                    normal = Vector3.Normalize(normal);
                }

                if (normal.Y < 0f)
                {
                    normal = -normal;
                }

                result[ToPointIndex(x, y, patch.CellsPerAxis)] = normal;
            }
        }

        return result;
    }

    private static void WriteTriangle(
        float[] positions,
        float[] normals,
        float[] elevations,
        ref int positionOffset,
        ref int elevationOffset,
        PlanetLocalSurfacePoint first,
        Vector3 firstNormal,
        PlanetLocalSurfacePoint second,
        Vector3 secondNormal,
        PlanetLocalSurfacePoint third,
        Vector3 thirdNormal)
    {
        var a = ToRenderVector(first.LocalPosition);
        var b = ToRenderVector(second.LocalPosition);
        var c = ToRenderVector(third.LocalPosition);
        var faceNormal = Vector3.Cross(b - a, c - a);

        if (faceNormal.Y < 0f)
        {
            (b, c) = (c, b);
            (second, third) = (third, second);
            (secondNormal, thirdNormal) = (thirdNormal, secondNormal);
        }

        WriteVector(positions, positionOffset, a);
        WriteVector(positions, positionOffset + 3, b);
        WriteVector(positions, positionOffset + 6, c);
        WriteVector(normals, positionOffset, firstNormal);
        WriteVector(normals, positionOffset + 3, secondNormal);
        WriteVector(normals, positionOffset + 6, thirdNormal);
        elevations[elevationOffset] = (float)first.ElevationMeters;
        elevations[elevationOffset + 1] = (float)second.ElevationMeters;
        elevations[elevationOffset + 2] = (float)third.ElevationMeters;
        positionOffset += 9;
        elevationOffset += 3;
    }

    private static int ToPointIndex(int x, int y, int cellsPerAxis) => (y * (cellsPerAxis + 1)) + x;

    private static Vector3 ToRenderVector(PlanetLocalPosition position) =>
        new((float)position.EastMeters, (float)position.UpMeters, (float)-position.NorthMeters);

    private static void WriteVector(float[] target, int offset, Vector3 value)
    {
        target[offset] = value.X;
        target[offset + 1] = value.Y;
        target[offset + 2] = value.Z;
    }
}
