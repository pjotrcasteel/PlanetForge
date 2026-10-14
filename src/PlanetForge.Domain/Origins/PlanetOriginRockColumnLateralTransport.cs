using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>Internal Fourier exchange only. No mass, momentum or external heat is introduced.</summary>
public sealed record PlanetOriginRockColumnLateralTrial(PlanetOriginRockColumnField Candidate, double TransportedJoules, double ConservationResidualJoules);

public sealed partial class PlanetOriginRockColumnField
{
    private readonly record struct LateralContact(int First, int FirstLayer, int Second, int SecondLayer, double ConductanceWattsPerKelvin);
    private static readonly PlanetGridDirection[] ColumnSides =
        [PlanetGridDirection.North, PlanetGridDirection.East, PlanetGridDirection.South, PlanetGridDirection.West];

    /// <summary>
    /// Equal/opposite fluxes across the actual overlapping vertical material intervals.
    /// A layer index is not a physical depth: contraction can place one layer beside two neighbors.
    /// The supplied extent selects a physical surface layer in each column, not an aggregate mixed reservoir.
    /// </summary>
    public PlanetOriginRockColumnLateralTrial ProposeLateralHeat(PlanetOriginDuration interval, double conductivityWattsPerMeterKelvin,
        double activeDepthMeters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateLateral(interval, conductivityWattsPerMeterKelvin, activeDepthMeters);
        if (conductivityWattsPerMeterKelvin == 0.0) return new(this, 0.0, 0.0);
        var count = Columns.Count;
        var deltas = Enumerable.Range(0, count).Select(_ => new double[Properties.LayerCount]).ToArray();
        var incidence = Enumerable.Range(0, count).Select(_ => new double[Properties.LayerCount]).ToArray();
        var temperatures = Columns.Select(column => column.Layers.Select(layer => layer.Resolve(Material).TemperatureKelvin).ToArray()).ToArray();
        var transported = 0.0;
        foreach (var contact in LateralContacts(conductivityWattsPerMeterKelvin, activeDepthMeters, cancellationToken))
        {
            var integrated = contact.ConductanceWattsPerKelvin * interval.TotalSeconds;
            var heat = integrated * (temperatures[contact.First][contact.FirstLayer] - temperatures[contact.Second][contact.SecondLayer]);
            if (!double.IsFinite(heat) || !double.IsFinite(integrated))
                throw new InvalidOperationException("Lateral column heat flux must remain finite.");
            deltas[contact.First][contact.FirstLayer] -= heat;
            deltas[contact.Second][contact.SecondLayer] += heat;
            incidence[contact.First][contact.FirstLayer] += integrated;
            incidence[contact.Second][contact.SecondLayer] += integrated;
            transported += Math.Abs(heat);
        }
        var result = new PlanetOriginRockColumn[count];
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[index];
            var layers = new PlanetOriginRockColumnLayer[Properties.LayerCount];
            for (var depth = 0; depth < layers.Length; depth++)
            {
                var layer = column.Layers[depth];
                VerifyCoupling(incidence[index][depth], layer.MassKilograms * Material.SpecificHeatCapacityJoulesPerKilogramKelvin);
                layers[depth] = layer.AddEnthalpy(deltas[index][depth]);
                if (!double.IsFinite(layers[depth].StoredEnthalpyJoules) || layers[depth].StoredEnthalpyJoules < 0.0)
                    throw new InvalidOperationException("Lateral column heat would overdraw or overflow an owned layer.");
            }
            result[index] = new(column.Id, column.AreaSquareMeters, column.ReferenceThicknessMeters, layers);
            if (result[index].ThicknessMeters(Material, Properties) / Aggregate.PlanetRadiusMeters > 0.001)
                throw new InvalidOperationException("Lateral columns exceeded the validated thin-shell geometry envelope.");
        }
        var aggregate = PlanetOriginSurfaceRockPhaseField.FromOwnedColumns(Aggregate, result);
        var residual = Aggregate.TotalStoredEnthalpyJoules - aggregate.TotalStoredEnthalpyJoules;
        if (!double.IsFinite(transported) || !double.IsFinite(residual) || Math.Abs(residual) > Math.Max(1e-6, Aggregate.TotalStoredEnthalpyJoules * 2e-12))
            throw new InvalidOperationException("Lateral columns did not close their internal enthalpy account.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(aggregate, Properties, result), transported, residual);
    }

    private double MaximumLateralStepSeconds(double conductivity, double activeDepth, CancellationToken cancellationToken)
    {
        if (conductivity == 0.0) return double.PositiveInfinity;
        var incidence = Columns.Select(column => new double[column.Layers.Count]).ToArray();
        foreach (var contact in LateralContacts(conductivity, activeDepth, cancellationToken))
        {
            incidence[contact.First][contact.FirstLayer] += contact.ConductanceWattsPerKelvin;
            incidence[contact.Second][contact.SecondLayer] += contact.ConductanceWattsPerKelvin;
        }
        var maximum = double.PositiveInfinity;
        for (var index = 0; index < Columns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var depth = 0; depth < Properties.LayerCount; depth++)
                maximum = Math.Min(maximum, StableSeconds(incidence[index][depth],
                    Columns[index].Layers[depth].MassKilograms * Material.SpecificHeatCapacityJoulesPerKilogramKelvin));
        }
        return maximum;
    }

    private IEnumerable<LateralContact> LateralContacts(double conductivity, double activeDepth, CancellationToken cancellationToken)
    {
        var bounds = Columns.Select(column => LayerBoundaries(column)).ToArray();
        var centers = Columns.Select(column => PlanetSurfaceGridGeometry.GetCenterDirection(column.Id)).ToArray();
        for (var index = 0; index < Columns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var side in ColumnSides)
            {
                var other = Aggregate.Layout.GetIndex(PlanetSurfaceGridTopology.GetNeighbor(Columns[index].Id, side).Cell);
                if (other <= index) continue;
                var arc = Math.Atan2(PlanetVector.Cross(centers[index], centers[other]).Length, PlanetVector.Dot(centers[index], centers[other]));
                var distance = Aggregate.PlanetRadiusMeters * arc;
                var edge = Aggregate.SharedSphericalEdgeLengthMeters(Columns[index].Id, side);
                if (!double.IsFinite(distance) || distance <= 0.0) throw new InvalidOperationException("Invalid lateral column geometry.");
                var first = 0;
                var second = 0;
                while (first < Properties.LayerCount && second < Properties.LayerCount)
                {
                    // Bounds descend from the surface to the fixed original basal datum.
                    var top = Math.Min(bounds[index][first], bounds[other][second]);
                    var bottom = Math.Max(Math.Max(bounds[index][first + 1], bounds[other][second + 1]),
                        Math.Max(bounds[index][0] - activeDepth, bounds[other][0] - activeDepth));
                    if (top > bottom)
                        yield return new(index, first, other, second, conductivity * edge * (top - bottom) / distance);
                    var firstBottom = bounds[index][first + 1];
                    var secondBottom = bounds[other][second + 1];
                    if (firstBottom >= secondBottom) first++;
                    if (secondBottom >= firstBottom) second++;
                }
            }
        }
    }

    private double[] LayerBoundaries(PlanetOriginRockColumn column)
    {
        var bounds = new double[column.Layers.Count + 1];
        bounds[^1] = -column.ReferenceThicknessMeters;
        for (var depth = column.Layers.Count - 1; depth >= 0; depth--)
            bounds[depth] = bounds[depth + 1] + column.Layers[depth].VolumeCubicMeters(Material, Properties) / column.AreaSquareMeters;
        return bounds;
    }

    private static void ValidateLateral(PlanetOriginDuration interval, double conductivity, double activeDepth)
    {
        if (interval.IsZero || !double.IsFinite(interval.TotalSeconds) || interval.TotalSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (!double.IsFinite(conductivity) || conductivity < 0.0)
            throw new ArgumentOutOfRangeException(nameof(conductivity));
        if (!double.IsFinite(activeDepth) || activeDepth <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(activeDepth));
    }
}
