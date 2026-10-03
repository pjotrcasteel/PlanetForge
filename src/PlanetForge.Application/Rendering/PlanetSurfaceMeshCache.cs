using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetSurfaceMeshCache(PlanetSurfaceMeshBuilder meshBuilder)
{
    private readonly Dictionary<CacheKey, PlanetSurfaceTileMesh> cache = [];
    private int? activeSeed;

    public PlanetSurfaceTileMesh GetOrBuild(PlanetTileId id, int cellsPerAxis, int seed)
    {
        EnsureSeed(seed);
        var key = new CacheKey(id, cellsPerAxis);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var mesh = meshBuilder.BuildTile(id, cellsPerAxis, seed);
        cache.Add(key, mesh);
        return mesh;
    }

    public IReadOnlyList<PlanetSurfaceTileMesh> GetOrBuild(IReadOnlyList<PlanetTileId> ids, int cellsPerAxis, int seed)
    {
        var result = new PlanetSurfaceTileMesh[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            result[index] = GetOrBuild(ids[index], cellsPerAxis, seed);
        }

        return result;
    }

    public IReadOnlyList<PlanetSurfaceTileMesh> GetOrBuildGlobal(int level, int cellsPerAxis, int seed)
    {
        if (level < 0 || level > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Global render level must be between 0 and 8.");
        }

        var tilesPerAxis = 1 << level;
        var ids = new List<PlanetTileId>(6 * tilesPerAxis * tilesPerAxis);
        foreach (var face in Enum.GetValues<CubeFace>())
        {
            for (var y = 0; y < tilesPerAxis; y++)
            {
                for (var x = 0; x < tilesPerAxis; x++)
                {
                    ids.Add(new PlanetTileId(face, level, x, y));
                }
            }
        }

        return GetOrBuild(ids, cellsPerAxis, seed);
    }

    public void Clear()
    {
        cache.Clear();
        activeSeed = null;
    }

    private void EnsureSeed(int seed)
    {
        if (activeSeed == seed)
        {
            return;
        }

        cache.Clear();
        activeSeed = seed;
    }

    private readonly record struct CacheKey(PlanetTileId Id, int CellsPerAxis);
}
