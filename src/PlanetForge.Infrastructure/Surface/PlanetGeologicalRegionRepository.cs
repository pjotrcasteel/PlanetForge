using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Cache-aside geological checkpoint repository. Canonical world and geological
/// epoch form the storage key; restored archives must agree with that key.
/// Writes reach durable storage before entering the disposable LRU cache.
/// This never activates an overlay on the gameplay planet.
/// </summary>
public sealed class PlanetGeologicalRegionRepository(
    IPlanetGeologicalRegionDocumentStore documents, PlanetGeologicalRegionCache cache)
{
    public async Task SaveAsync(PlanetGeologicalRegionHistory history, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        var key = Key(history.Region, history.Epoch);
        var json = PlanetGeologicalRegionArchive.Serialize(history);
        await documents.SaveAsync(key, json, cancellationToken);
        cache.Put(history);
    }

    public async Task<PlanetGeologicalRegionHistory?> LoadAsync(
        PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch, CancellationToken cancellationToken = default)
    {
        if (cache.TryGet(region, epoch, out var cached))
        {
            return cached;
        }

        var json = await documents.LoadAsync(Key(region, epoch), cancellationToken);
        if (json is null)
        {
            return null;
        }

        var restored = PlanetGeologicalRegionArchive.Deserialize(json);
        if (restored.Region != region || restored.Epoch != epoch)
        {
            throw new InvalidDataException("Stored geology does not match the requested world, region and epoch.");
        }

        cache.Put(restored);
        return restored.Copy();
    }

    public async Task DeleteAsync(
        PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch, CancellationToken cancellationToken = default)
    {
        await documents.DeleteAsync(Key(region, epoch), cancellationToken);
        cache.Remove(region, epoch);
    }

    public static string Key(PlanetGeologicalRegionId region, PlanetGeologicalEpoch epoch) =>
        FormattableString.Invariant($"planetforge/geology/v1/{region}/YBP:{epoch.YearsBeforePresent}");
}
