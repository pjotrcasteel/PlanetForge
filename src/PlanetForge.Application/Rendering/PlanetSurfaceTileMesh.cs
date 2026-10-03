using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed record PlanetSurfaceTileMesh(
    PlanetTileId Id,
    float[] Positions,
    float[] Normals,
    int SurfaceTriangleCount,
    int SkirtTriangleCount)
{
    public string Key => $"{Id.Face}:{Id.Level}:{Id.X}:{Id.Y}";

    public int TriangleCount => SurfaceTriangleCount + SkirtTriangleCount;

    public int SurfaceVertexCount => SurfaceTriangleCount * 3;

    public int SkirtVertexCount => SkirtTriangleCount * 3;
}
