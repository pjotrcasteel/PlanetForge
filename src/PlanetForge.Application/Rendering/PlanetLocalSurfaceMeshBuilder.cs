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

        for (var y = 0; y < patch.CellsPerAxis; y++)
        {
            for (var x = 0; x < patch.CellsPerAxis; x++)
            {
                var a = patch.GetPoint(x, y);
                var b = patch.GetPoint(x + 1, y);
                var c = patch.GetPoint(x, y + 1);
                var d = patch.GetPoint(x + 1, y + 1);
                WriteTriangle(positions, normals, elevations, ref positionOffset, ref elevationOffset, a, b, c);
                WriteTriangle(positions, normals, elevations, ref positionOffset, ref elevationOffset, b, d, c);
            }
        }

        var key = FormattableString.Invariant($"{patch.Frame.Up.X:R}:{patch.Frame.Up.Y:R}:{patch.Frame.Up.Z:R}:{patch.SizeMeters:R}:{patch.CellsPerAxis}");
        return new PlanetLocalSurfaceMesh(key, positions, normals, elevations, triangleCount, patch.SizeMeters, cameraAltitudeMeters);
    }

    private static void WriteTriangle(
        float[] positions,
        float[] normals,
        float[] elevations,
        ref int positionOffset,
        ref int elevationOffset,
        PlanetLocalSurfacePoint first,
        PlanetLocalSurfacePoint second,
        PlanetLocalSurfacePoint third)
    {
        var a = ToRenderVector(first.LocalPosition);
        var b = ToRenderVector(second.LocalPosition);
        var c = ToRenderVector(third.LocalPosition);
        var normal = Vector3.Cross(b - a, c - a);

        if (normal.Y < 0f)
        {
            (b, c) = (c, b);
            (second, third) = (third, second);
            normal = -normal;
        }

        normal = Vector3.Normalize(normal);
        WriteVector(positions, positionOffset, a);
        WriteVector(positions, positionOffset + 3, b);
        WriteVector(positions, positionOffset + 6, c);
        WriteVector(normals, positionOffset, normal);
        WriteVector(normals, positionOffset + 3, normal);
        WriteVector(normals, positionOffset + 6, normal);
        elevations[elevationOffset] = (float)first.ElevationMeters;
        elevations[elevationOffset + 1] = (float)second.ElevationMeters;
        elevations[elevationOffset + 2] = (float)third.ElevationMeters;
        positionOffset += 9;
        elevationOffset += 3;
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
