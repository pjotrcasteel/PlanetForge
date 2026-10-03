using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Rendering;

public sealed class PlanetSurfaceMeshCache(PlanetSurfaceMeshBuilder meshBuilder)
{
    private readonly Dictionary<CacheKey, PlanetSurfaceTileMesh> cache = [];
    private IReadOnlyList<PlanetTileId>? lastRequestedIds;
    private int? activeSeed;
    private int lastCellsPerAxis;
    private double lastPlanetRadiusMeters;

    public PlanetSurfaceTileMesh GetOrBuild(PlanetTileId id, int cellsPerAxis, int seed, double planetRadiusMeters)
    {
        EnsureSeed(seed);
        var key = new CacheKey(id, cellsPerAxis, planetRadiusMeters);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var mesh = meshBuilder.BuildTile(id, cellsPerAxis, seed, planetRadiusMeters);
        cache.Add(key, mesh);
        return mesh;
    }

    public IReadOnlyList<PlanetSurfaceTileMesh> GetOrBuild(IReadOnlyList<PlanetTileId> ids, int cellsPerAxis, int seed, double planetRadiusMeters)
    {
        EnsureSeed(seed);
        if (IsSameRequest(ids, cellsPerAxis, planetRadiusMeters))
        {
            return CreateReferences(ids, cellsPerAxis, planetRadiusMeters);
        }

        var result = new PlanetSurfaceTileMesh[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            result[index] = GetOrBuild(ids[index], cellsPerAxis, seed, planetRadiusMeters);
        }

        RememberRequest(ids, cellsPerAxis, planetRadiusMeters);
        return result;
    }

    public IReadOnlyList<PlanetSurfaceTileMesh> GetOrBuildGlobal(int level, int cellsPerAxis, int seed, double planetRadiusMeters)
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

        return GetOrBuild(ids, cellsPerAxis, seed, planetRadiusMeters);
    }

    public void Clear()
    {
        cache.Clear();
        activeSeed = null;
        lastRequestedIds = null;
        lastCellsPerAxis = 0;
        lastPlanetRadiusMeters = 0.0;
    }

    private IReadOnlyList<PlanetSurfaceTileMesh> CreateReferences(IReadOnlyList<PlanetTileId> ids, int cellsPerAxis, double planetRadiusMeters)
    {
        var result = new PlanetSurfaceTileMesh[ids.Count];
        for (var index = 0; index < ids.Count; index++)
        {
            var key = new CacheKey(ids[index], cellsPerAxis, planetRadiusMeters);
            result[index] = cache[key].AsReference();
        }

        return result;
    }

    private bool IsSameRequest(IReadOnlyList<PlanetTileId> ids, int cellsPerAxis, double planetRadiusMeters)
    {
        if (lastRequestedIds is null || lastCellsPerAxis != cellsPerAxis || lastPlanetRadiusMeters != planetRadiusMeters || lastRequestedIds.Count != ids.Count)
        {
            return false;
        }

        for (var index = 0; index < ids.Count; index++)
        {
            if (lastRequestedIds[index] != ids[index])
            {
                return false;
            }
        }

        return true;
    }

    private void RememberRequest(IReadOnlyList<PlanetTileId> ids, int cellsPerAxis, double planetRadiusMeters)
    {
        lastRequestedIds = ids.ToArray();
        lastCellsPerAxis = cellsPerAxis;
        lastPlanetRadiusMeters = planetRadiusMeters;
    }

    private void EnsureSeed(int seed)
    {
        if (activeSeed == seed)
        {
            return;
        }

        cache.Clear();
        activeSeed = seed;
        lastRequestedIds = null;
        lastCellsPerAxis = 0;
        lastPlanetRadiusMeters = 0.0;
    }

    private readonly record struct CacheKey(PlanetTileId Id, int CellsPerAxis, double PlanetRadiusMeters);
}
