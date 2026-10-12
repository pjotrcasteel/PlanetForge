using System.Threading;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Shared planet elevation gateway for orbital tiles and metre-scale patches.
/// Starts with the unchanged canonical planet. A fully constructed geological
/// atlas can be published atomically; readers never sample a half-written atlas.
/// Regional erosion remains opt-in until inter-region water/sediment exchange
/// and geographic indexing have been validated.
/// </summary>
public sealed class PlanetTerrainRegionProvider(IPlanetElevationSource canonicalElevation) : IPlanetElevationRevisionSource
{
    private readonly object publicationGate = new();
    private PublishedTerrain published = new(canonicalElevation ?? throw new ArgumentNullException(nameof(canonicalElevation)), 0);

    public long Revision => Volatile.Read(ref published).Revision;
    public IPlanetElevationSource CanonicalElevation => canonicalElevation;

    public double SampleElevationMeters(PlanetVector direction, int seed) =>
        Volatile.Read(ref published).Elevation.SampleElevationMeters(direction, seed);

    /// <summary>
    /// Captures one immutable elevation source for a complete sampling operation.
    /// An in-progress publication cannot mix two different geology revisions.
    /// </summary>
    public IPlanetElevationSource CaptureSnapshot() => Volatile.Read(ref published).Elevation;

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
            var next = new PublishedTerrain(elevation, checked(published.Revision + 1));
            Volatile.Write(ref published, next);
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
            var next = new PublishedTerrain(elevation, checked(published.Revision + 1));
            Volatile.Write(ref published, next);
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
