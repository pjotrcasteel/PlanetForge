using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed record PlanetSurfaceTileMesh(PlanetTileId Id, float[] Positions, float[] Normals, int TriangleCount)
{
    public string Key => $"{Id.Face}:{Id.Level}:{Id.X}:{Id.Y}";
}
