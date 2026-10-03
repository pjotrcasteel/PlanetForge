namespace PlanetForge.Application.Rendering;

public sealed record PlanetLocalSurfaceMesh(
    float[] PositionsMeters,
    float[] Normals,
    float[] ElevationsMeters,
    int TriangleCount,
    double SizeMeters,
    double CameraAltitudeMeters)
{
    public int VertexCount => TriangleCount * 3;
}
