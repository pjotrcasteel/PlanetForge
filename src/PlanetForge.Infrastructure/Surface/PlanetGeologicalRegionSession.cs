using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Explicit, bounded restoration of one world's saved regional epoch. Loading
/// never replaces the visible terrain until every requested archive is valid.
/// This is an opt-in session owner, not a camera-driven simulation or streamer.
/// </summary>
public sealed class PlanetGeologicalRegionSession(PlanetGeologicalRegionRepository repository, PlanetTerrainRegionProvider provider)
{
    public const int MaximumRegions = 8;
    public const int MaximumGridSamples = 524_288;
    private readonly object publicationGate = new();
    private readonly SemaphoreSlim loadingGate = new(1, 1);
    private long requestGeneration;

    /// <summary>
    /// Restores all requested regions or leaves the previous terrain unchanged.
    /// A newer valid request supersedes an older one, even if storage ignores
    /// cancellation. Superseded requests throw OperationCanceledException.
    /// A missing archive is an error, never a silently omitted terrain layer.
    /// </summary>
    public async Task<long> RestoreAsync(IReadOnlyList<PlanetGeologicalRegionId> regions,
        PlanetGeologicalEpoch epoch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = ValidateAndCopy(regions);
        long request;
        lock (publicationGate)
        {
            request = checked(++requestGeneration);
        }

        // Only one request materializes archives at a time. Waiting obsolete
        // requests are discarded before any storage I/O or atlas construction.
        await loadingGate.WaitAsync(cancellationToken);
        try
        {
            var histories = new List<PlanetGeologicalRegionHistory>(requested.Length);
            var samples = 0L;
            foreach (var region in requested)
            {
                EnsureCurrent(request, cancellationToken);
                var history = await repository.LoadAsync(region, epoch, cancellationToken);
                EnsureCurrent(request, cancellationToken);
                if (history is null)
                {
                    throw new InvalidDataException($"No saved geological region exists for {region} at epoch {epoch.YearsBeforePresent}.");
                }

                var grid = history.Original;
                samples += (long)grid.Width * grid.Height;
                if (samples > MaximumGridSamples)
                {
                    throw new InvalidDataException("Requested geological selection exceeds the active grid sample budget.");
                }

                histories.Add(history);
            }

            var layers = PlanetGeologicalRegionLayerBuilder.Build(epoch, histories);
            lock (publicationGate)
            {
                EnsureCurrent(request, cancellationToken);
                return provider.ReplaceLayers(layers);
            }
        }
        finally
        {
            loadingGate.Release();
        }
    }

    /// <summary>Returns to canonical terrain and invalidates every pending restoration.</summary>
    public long Clear()
    {
        lock (publicationGate)
        {
            requestGeneration = checked(requestGeneration + 1);
            return provider.ClearLayers();
        }
    }

    private void EnsureCurrent(long request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (publicationGate)
        {
            if (request != requestGeneration)
            {
                throw new OperationCanceledException("Geological restoration was superseded by a newer selection.");
            }
        }
    }

    private static PlanetGeologicalRegionId[] ValidateAndCopy(IReadOnlyList<PlanetGeologicalRegionId> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count is < 1 or > MaximumRegions)
        {
            throw new ArgumentOutOfRangeException(nameof(regions), "Select one to eight regions; use Clear to return to canonical terrain.");
        }

        var copy = regions.ToArray();
        var world = copy[0].World;
        if (world.GenerationVersion != PlanetGenerationVersion.Current ||
            copy.Any(region => region.World != world || !Enum.IsDefined(region.Tile.Face)) ||
            copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("Regions must be unique addresses in one current generated world.", nameof(regions));
        }

        return copy;
    }
}
