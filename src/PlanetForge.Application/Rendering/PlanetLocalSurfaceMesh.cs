using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed record PlanetLocalSurfaceMesh(
    string Key,
    PlanetSurfaceAddress AnchorAddress,
    PlanetVector AnchorDirection,
    float[] PositionsMeters,
    float[] Normals,
    float[] ElevationsMeters,
    int TriangleCount,
    double SizeMeters,
    double CameraAltitudeMeters)
{
    public int VertexCount => TriangleCount * 3;
}