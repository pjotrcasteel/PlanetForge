using PlanetForge.Domain.Surface;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Immutable, opt-in physical geology layer for render-LOD-independent sampling.
/// Neighboring regional solutions at the same geological scale are blended in
/// world space. Nested scales are separate additive layers: an 8 km excavation
/// must not replace the geological history inherited from 128 km and 32 km.
/// </summary>
/// <remarks>
/// Cross-region height continuity is not a claim of hydraulic or sediment-flux
/// conservation across simulation boundaries. Keep this experimental until
/// geographically indexed regional solves and shared sediment flux are ready.
/// </remarks>
public sealed class PlanetRegionalGeologyAtlas : IPlanetElevationSource
{
    private readonly IPlanetElevationSource canonicalElevation;
    private readonly PlanetRegionalGeologyOverlay[][] layers;

    public PlanetRegionalGeologyAtlas(IPlanetElevationSource canonicalElevation, params PlanetRegionalGeologyOverlay[][] layers)
    {
        ArgumentNullException.ThrowIfNull(canonicalElevation);
        ArgumentNullException.ThrowIfNull(layers);

        this.canonicalElevation = canonicalElevation;
        this.layers = new PlanetRegionalGeologyOverlay[layers.Length][];
        for (var index = 0; index < layers.Length; index++)
        {
            var regions = layers[index] ?? throw new ArgumentException("Geological layers cannot be null.", nameof(layers));
            if (regions.Length == 0 || regions.Any(region => region is null))
            {
                throw new ArgumentException("Geological layers must contain at least one non-null region.", nameof(layers));
            }

            var identities = new HashSet<(int Seed, string RegionKey)>();
            var reference = regions[0];
            foreach (var region in regions)
            {
                if (!identities.Add((region.Seed, region.RegionKey)))
                {
                    throw new ArgumentException("A geological region may occur only once within the same scale.", nameof(layers));
                }

                if (region.Width != reference.Width || region.Height != reference.Height ||
                    region.CellSpacingMeters != reference.CellSpacingMeters)
                {
                    throw new ArgumentException("Overlapping regions in one layer must have the same physical grid scale.", nameof(layers));
                }
            }

            this.layers[index] = (PlanetRegionalGeologyOverlay[])regions.Clone();
        }
    }

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var elevation = canonicalElevation.SampleElevationMeters(direction, seed);
        foreach (var layer in layers)
        {
            var weightedDelta = 0.0;
            var combinedWeight = 0.0;
            foreach (var region in layer)
            {
                var sample = region.SampleWeightedDeltaMeters(direction, seed);
                weightedDelta += sample.DeltaMeters * sample.Weight;
                combinedWeight += sample.Weight;
            }

            // With a single region, retain the original smooth fade to canonical
            // bedrock. In overlaps, normalize the weights instead of summing two
            // competing height fields and accidentally doubling the erosion.
            elevation += weightedDelta / Math.Max(1.0, combinedWeight);
        }

        return elevation;
    }
}
