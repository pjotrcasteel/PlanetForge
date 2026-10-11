using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Application.Surface.Hydrology;

/// <summary>
/// Cache identity belongs to physical world state, not a renderer or climate
/// timestep. Changes to plate publications, erosion or planet dimensions
/// trigger a fresh terrain volume distribution.
/// </summary>
public readonly record struct PlanetHypsometryCacheKey(
    int GenerationVersion,
    int GridLevel,
    int Seed,
    double RadiusMeters,
    long GeologicalRevision,
    int TerrainDeformationRevision);

/// <summary>
/// Lazily builds the distribution of terrain area by elevation and reuses it
/// when water quantity changes. Never publishes an incomplete or stale build.
/// </summary>
public sealed class PlanetHypsometryCache(
    PlanetHypsometrySampler sampler,
    IPlanetElevationSource elevationSource,
    IPlanetTerrainDeformationStore? terrainDeformationStore = null)
{
    private readonly object sync = new();
    private CacheEntry? current;

    public PlanetHypsometry GetOrBuild(int gridLevel, int seed, double radiusMeters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = CaptureKey(gridLevel, seed, radiusMeters);
        lock (sync)
        {
            if (current is { } cached && cached.Key == key)
            {
                return cached.Model;
            }
        }

        var model = sampler.Build(gridLevel, seed, radiusMeters, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (CaptureKey(gridLevel, seed, radiusMeters) != key)
        {
            throw new InvalidOperationException("Terrain changed while building the water-volume model. Retry using the latest geological revision.");
        }

        lock (sync)
        {
            // A second revision check closes the window between the completed
            // sampling pass and cache publication if another build intervened.
            if (CaptureKey(gridLevel, seed, radiusMeters) != key)
            {
                throw new InvalidOperationException("Terrain changed before the water-volume model could be published.");
            }

            if (current is { } cached && cached.Key == key)
            {
                return cached.Model;
            }

            current = new CacheEntry(key, model);
            return model;
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            current = null;
        }
    }

    private PlanetHypsometryCacheKey CaptureKey(int gridLevel, int seed, double radiusMeters)
        => new(
            PlanetGenerationVersion.Current.Value,
            gridLevel,
            seed,
            radiusMeters,
            (elevationSource as IPlanetElevationRevisionSource)?.Revision ?? 0,
            terrainDeformationStore?.GetRevision(seed) ?? 0);

    private sealed record CacheEntry(PlanetHypsometryCacheKey Key, PlanetHypsometry Model);
}
