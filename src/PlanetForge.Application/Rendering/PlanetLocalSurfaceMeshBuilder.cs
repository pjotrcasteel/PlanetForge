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
        var positionOffset = 0;
        var elevationOffset = 0;
        var vertexNormals = CalculateVertexNormals(patch);

        for (var y = 0; y < patch.CellsPerAxis; y++)
        {
            for (var x = 0; x < patch.CellsPerAxis; x++)
            {
                var a = patch.GetPoint(x, y);
                var b = patch.GetPoint(x + 1, y);
                var c = patch.GetPoint(x, y + 1);
                var d = patch.GetPoint(x + 1, y + 1);
                var row = y * (patch.CellsPerAxis + 1);
                var nextRow = row + patch.CellsPerAxis + 1;
                WriteTriangle(positions, normals, elevations, ref positionOffset, ref elevationOffset, a, b, c,
                    vertexNormals[row + x], vertexNormals[row + x + 1], vertexNormals[nextRow + x]);
                WriteTriangle(positions, normals, elevations, ref positionOffset, ref elevationOffset, b, d, c,
                    vertexNormals[row + x + 1], vertexNormals[nextRow + x + 1], vertexNormals[nextRow + x]);
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

    private static void WriteTriangle(
        float[] positions,
        float[] normals,
        float[] elevations,
        ref int positionOffset,
        ref int elevationOffset,
        PlanetLocalSurfacePoint first,
        PlanetLocalSurfacePoint second,
        PlanetLocalSurfacePoint third,
        Vector3 firstNormal,
        Vector3 secondNormal,
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

    private static Vector3[] CalculateVertexNormals(PlanetLocalSurfacePatch patch)
    {
        var cells = patch.CellsPerAxis;
        var width = cells + 1;
        var normals = new Vector3[width * width];

        for (var y = 0; y <= cells; y++)
        {
            for (var x = 0; x <= cells; x++)
            {
                // Derive shared vertex normals from the same canonical mesh geometry.
                // This avoids a visibly faceted normal discontinuity along each cell diagonal.
                var left = ToRenderVector(patch.GetPoint(Math.Max(0, x - 1), y).LocalPosition);
                var right = ToRenderVector(patch.GetPoint(Math.Min(cells, x + 1), y).LocalPosition);
                var south = ToRenderVector(patch.GetPoint(x, Math.Max(0, y - 1)).LocalPosition);
                var north = ToRenderVector(patch.GetPoint(x, Math.Min(cells, y + 1)).LocalPosition);
                var normal = Vector3.Normalize(Vector3.Cross(right - left, north - south));
                normals[(y * width) + x] = normal.Y >= 0f ? normal : -normal;
            }
        }

        return normals;
    }

    private static Vector3 ToRenderVector(PlanetLocalPosition position) =>
        new((float)position.EastMeters, (float)position.UpMeters, (float)-position.NorthMeters);

    private static void WriteVector(float[] target, int offset, Vector3 value)
    {
        target[offset] = value.X;
        target[offset + 1] = value.Y;
        target[offset + 2] = value.Z;
    }
}