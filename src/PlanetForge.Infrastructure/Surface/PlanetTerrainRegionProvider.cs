using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Shared planet elevation gateway for orbital tiles and metre-scale patches.
/// Starts with the unchanged canonical planet. A fully constructed geological
/// atlas can be published atomically; readers never sample a half-written atlas.
/// Regional erosion remains opt-in until inter-region water/sediment exchange
/// and geographic indexing have been validated.
/// </summary>
public sealed class PlanetTerrainRegionProvider(IPlanetElevationSource canonicalElevation) : IPlanetElevationRevisionSource, IPlanetOriginSurfacePublication
{
    private readonly object publicationGate = new();
    private PublishedTerrain published = new(canonicalElevation ?? throw new ArgumentNullException(nameof(canonicalElevation)), 0);

    private PublishedTerrain? originBase;
    private long allocatedRevision;

    public long Revision { get { lock (publicationGate) return published.Revision; } }
    public IPlanetElevationSource CanonicalElevation => canonicalElevation;

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        lock (publicationGate) return published.Elevation.SampleElevationMeters(direction, seed);
    }

    /// <summary>
    /// Captures one immutable elevation source for a complete sampling operation.
    /// An in-progress publication cannot mix two different geology revisions.
    /// </summary>
    public IPlanetElevationSource CaptureSnapshot()
    {
        lock (publicationGate) return published.Elevation;
    }

    public T ReadConsistent<T>(Func<T> readWorld)
    {
        ArgumentNullException.ThrowIfNull(readWorld);
        lock (publicationGate) return readWorld();
    }

    public T PublishOriginSurface<T>(PlanetOriginRockColumnField? columns, Func<T> publishWorld)
    {
        ArgumentNullException.ThrowIfNull(publishWorld);
        var elevation = columns is null ? null : new PlanetOriginRockColumnElevationSource(columns);
        lock (publicationGate)
        {
            var previous = published;
            var previousBase = originBase;
            try
            {
                if (elevation is not null)
                {
                    originBase ??= published;
                    published = new PublishedTerrain(elevation, NextRevision());
                }
                else if (originBase is not null)
                {
                    published = new PublishedTerrain(originBase.Elevation, NextRevision());
                    originBase = null;
                }
                return publishWorld();
            }
            catch
            {
                published = previous;
                originBase = previousBase;
                // Never reuse a failed geometry revision: caches may have sampled it.
                throw;
            }
        }
    }

    private long NextRevision() => allocatedRevision = checked(allocatedRevision + 1);

    private void RequireGeologyOwnership()
    {
        if (originBase is not null)
            throw new InvalidOperationException("Clear the canonical origin columns before replacing terrain with independent geology.");
    }

    /// <summary>
    /// Replaces all published, geographically anchored geology in one transaction.
    /// Each peer layer uses a common physical resolution; nested scales add their
    /// deltas to their parent. No erosion simulation runs during sampling.
    /// </summary>
    public long ReplaceLayers(params PlanetRegionalGeologyOverlay[][] layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var elevation = layers.Length == 0 ? canonicalElevation : new PlanetRegionalGeologyAtlas(canonicalElevation, layers);
        lock (publicationGate)
        {
            RequireGeologyOwnership();
            var next = new PublishedTerrain(elevation, NextRevision());
            published = next;
            return next.Revision;
        }
    }

    /// <summary>
    /// Publishes an explicitly selected, previously evolved geological epoch
    /// using stable geographical addresses. No simulation occurs here.
    /// Publication is all-or-nothing if any history has an incompatible
    /// world, duplicate region, epoch or physical snapshot.
    /// </summary>
    public long PublishHistories(PlanetGeologicalEpoch epoch, IReadOnlyList<PlanetGeologicalRegionHistory> histories) =>
        ReplaceLayers(PlanetGeologicalRegionLayerBuilder.Build(epoch, histories));

    /// <summary>
    /// Atomically publish a whole-planet crust deformation research snapshot
    /// into the SAME canonical elevation path used by orbital tiles and local
    /// ground patches. The complete geographic index is constructed before
    /// the publication lock; no renderer can observe partially filled cells.
    /// This research profile is world-seed isolated and does not update saves.
    /// </summary>
    public long PublishGlobalCrustDeformation(PlanetCrustEvolvingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Epoch.YearsBeforePresent != 0)
        {
            throw new ArgumentException("Only an explicitly completed present-day research snapshot may be published.", nameof(snapshot));
        }

        return PublishGlobalCrustResearchEpoch(snapshot);
    }

    /// <summary>
    /// Explicit, research-only comparison of validated *geological terrain*
    /// snapshots from a single forward crust-history reconstruction. Earlier
    /// snapshots are not complete planetary climate/water epochs: callers
    /// must label this a crust-deformation comparison, not time travel in the
    /// playable world. The same source is published to orbit and local LOD.
    /// </summary>
    public long PublishGlobalCrustResearchEpoch(PlanetCrustEvolvingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var atlas = new PlanetCrustGlobalReliefAtlas(snapshot);
        var elevation = new PlanetCrustGlobalReliefElevationSource(canonicalElevation, atlas);
        lock (publicationGate)
        {
            RequireGeologyOwnership();
            var next = new PublishedTerrain(elevation, NextRevision());
            published = next;
            return next.Revision;
        }
    }

    /// <summary>
    /// Restores the unchanged canonical terrain, invalidating both orbital
    /// and local mesh revisions for a reproducible A/B comparison.
    /// </summary>
    public long ClearGlobalCrustDeformation() => ClearLayers();

    public long ClearLayers() => ReplaceLayers();

    private sealed record PublishedTerrain(IPlanetElevationSource Elevation, long Revision);
}
