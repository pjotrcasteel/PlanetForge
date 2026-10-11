using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Immutable, globally addressed RESEARCH response of local crust-mantle
/// compensation. These normalized compact kernels give identical samples for
/// identical positions regardless of region or mesh LOD. Their overlap is
/// smooth, but the parcels are not yet a watertight crust discretization.
/// </summary>
public sealed class PlanetCrustIsostaticReliefField
{
    public const double ResearchSupportRadiusRadians = 0.16;
    private const double BackgroundWeight = 0.35;
    private static readonly double MinimumDot = Math.Cos(ResearchSupportRadiusRadians);

    private readonly PlanetVector[] positions;
    private readonly double[] displacements;

    public PlanetCrustIsostaticReliefField(PlanetCrustIsostasySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        World = snapshot.World;
        Epoch = snapshot.Epoch;
        positions = new PlanetVector[snapshot.Columns.Count];
        displacements = new double[snapshot.Columns.Count];

        for (var index = 0; index < positions.Length; index++)
        {
            var column = snapshot.Columns[index];
            if (!double.IsFinite(column.SurfaceDisplacementMeters))
            {
                throw new ArgumentException("Research relief must contain finite displacement columns.", nameof(snapshot));
            }

            positions[index] = PlanetVector.Normalize(column.GeographicDirection);
            displacements[index] = column.SurfaceDisplacementMeters;
        }
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }

    public double SampleUnpublishedDisplacementMeters(PlanetVector geographicDirection)
    {
        var direction = PlanetVector.Normalize(geographicDirection);
        var sum = 0.0;
        var weight = BackgroundWeight;
        for (var index = 0; index < positions.Length; index++)
        {
            var dot = PlanetVector.Dot(direction, positions[index]);
            if (dot <= MinimumDot)
            {
                continue;
            }

            var t = Math.Clamp((dot - MinimumDot) / (1.0 - MinimumDot), 0.0, 1.0);
            var influence = t * t * (3.0 - 2.0 * t);
            sum += influence * displacements[index];
            weight += influence;
        }

        return sum / weight;
    }
}

/// <summary>
/// Explicit diagnostic adapter for future global/LOD acceptance tests.
/// NOT registered in application DI, and NOT published by the normal shared
/// planet terrain provider. It is O(number of reference parcels) per sample;
/// broad terrain rendering must await a bounded spatial lookup/atlas.
/// </summary>
public sealed class PlanetCrustIsostaticResearchElevationSource(
    IPlanetElevationSource canonical,
    PlanetCrustIsostaticReliefField field) : IPlanetElevationSource
{
    private readonly IPlanetElevationSource canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    private readonly PlanetCrustIsostaticReliefField field = field ?? throw new ArgumentNullException(nameof(field));

    public double SampleElevationMeters(PlanetVector direction, int seed)
    {
        var baseline = canonical.SampleElevationMeters(direction, seed);
        return PlanetWorldIdentity.CreateCurrent(seed) == field.World
            ? baseline + field.SampleUnpublishedDisplacementMeters(direction)
            : baseline;
    }
}
