using PlanetForge.Domain.Surface;
using PlanetForge.Domain.WorldGeneration;

namespace PlanetForge.Infrastructure.Surface;

/// <summary>
/// Immutable, world-addressed diagnostic field for column THICKNESS change,
/// not topographic elevation. Uses normalized compact spherical kernels plus
/// a zero-anomaly baseline, so it fades continuously outside material support.
/// Samples of the same geographic direction agree across regions and mesh LODs.
/// </summary>
public sealed class PlanetCrustColumnThicknessField
{
    public const double SupportRadiusRadians = 0.24;
    private const double BackgroundWeight = 0.35;
    private static readonly double MinimumDot = Math.Cos(SupportRadiusRadians);

    private readonly PlanetVector[] directions;
    private readonly double[] thicknessAnomalies;

    public PlanetCrustColumnThicknessField(PlanetCrustDeformationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        World = snapshot.World;
        Epoch = snapshot.Epoch;
        directions = new PlanetVector[snapshot.Columns.Count];
        thicknessAnomalies = new double[snapshot.Columns.Count];

        for (var i = 0; i < directions.Length; i++)
        {
            var column = snapshot.Columns[i];
            if (column.State.Material.World != World || column.State.Epoch != Epoch ||
                !double.IsFinite(column.ThicknessMeters) || column.ThicknessMeters <= 0.0)
            {
                throw new ArgumentException("Column field requires consistent, finite material snapshots.", nameof(snapshot));
            }

            directions[i] = PlanetVector.Normalize(column.State.GeographicDirection);
            thicknessAnomalies[i] = column.ThicknessMeters - column.State.Material.ReferenceThicknessMeters;
        }
    }

    public PlanetWorldIdentity World { get; }
    public PlanetGeologicalEpoch Epoch { get; }

    /// <summary>
    /// Continuous research sample: physical crust thickness anomaly in metres.
    /// Do not publish as a surface-height offset without an isostasy, mantle
    /// and crust-material volume/coverage model.
    /// </summary>
    public double SampleThicknessAnomalyMeters(PlanetVector geographicDirection)
    {
        var position = PlanetVector.Normalize(geographicDirection);
        double sum = 0.0;
        double weight = BackgroundWeight;
        for (var i = 0; i < directions.Length; i++)
        {
            var dot = PlanetVector.Dot(position, directions[i]);
            if (dot <= MinimumDot)
            {
                continue;
            }

            var t = Math.Clamp((dot - MinimumDot) / (1.0 - MinimumDot), 0.0, 1.0);
            var influence = t * t * (3.0 - 2.0 * t);
            sum += influence * thicknessAnomalies[i];
            weight += influence;
        }

        return sum / weight;
    }
}
