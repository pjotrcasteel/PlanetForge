namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Durable, keyed storage of versioned geological JSON archives. Implementations
/// must complete writes atomically or fail; a region is never partially visible.
/// Persistence is separate from explicit publication into gameplay terrain.
/// </summary>
public interface IPlanetGeologicalRegionDocumentStore
{
    Task<string?> LoadAsync(string key, CancellationToken cancellationToken);
    Task SaveAsync(string key, string json, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
