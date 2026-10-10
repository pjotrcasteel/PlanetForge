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

    public long ClearLayers() => ReplaceLayers();

    private sealed record PublishedTerrain(IPlanetElevationSource Elevation, long Revision);
}
