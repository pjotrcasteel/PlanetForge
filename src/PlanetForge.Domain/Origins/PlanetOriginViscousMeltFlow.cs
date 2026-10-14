using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>Newtonian creeping-flow hypothesis. Rejection limits are numerical/model admission, not a viscosity calibration for Earth.</summary>
public readonly record struct PlanetOriginViscousMeltParameters(double GravityMetersPerSecondSquared, double DynamicViscosityPascalSeconds,
    double MaximumReynoldsNumber = 0.1)
{
    public void Validate()
    {
        if (!double.IsFinite(GravityMetersPerSecondSquared) || GravityMetersPerSecondSquared <= 0.0 ||
            !double.IsFinite(DynamicViscosityPascalSeconds) || DynamicViscosityPascalSeconds <= 0.0 ||
            !double.IsFinite(MaximumReynoldsNumber) || MaximumReynoldsNumber <= 0.0 || MaximumReynoldsNumber > 1.0)
            throw new ArgumentOutOfRangeException(nameof(GravityMetersPerSecondSquared));
    }
}

/// <summary>One immutable gravity/density reference; mechanical energy is excess potential above the original resting geoid.</summary>
public sealed record PlanetOriginViscousMeltReference(PlanetOriginViscousMeltParameters Parameters, double DensityKilogramsPerCubicMeter)
{
    public void Validate(PlanetOriginRockColumnField columns)
    {
        Parameters.Validate();
        if (!double.IsFinite(DensityKilogramsPerCubicMeter) || DensityKilogramsPerCubicMeter <= 0.0 ||
            columns.Properties.LiquidDensityKilogramsPerCubicMeter != DensityKilogramsPerCubicMeter ||
            columns.Properties.SolidDensityKilogramsPerCubicMeter != DensityKilogramsPerCubicMeter ||
            columns.Columns.Any(column => !double.IsFinite(column.ReferenceMassKilograms) || column.ReferenceMassKilograms <= 0.0) ||
            Math.Abs(columns.Aggregate.TotalActiveRockMassKilograms - columns.Columns.Sum(column => column.ReferenceMassKilograms)) >
                columns.Aggregate.TotalActiveRockMassKilograms * 2e-12)
            throw new InvalidOperationException("This reference requires one fixed incompressible density and conserved mass relative to its original resting geoid.");
    }

    public double PotentialEnergyJoules(PlanetOriginRockColumnField columns)
    {
        Validate(columns);
        // The immovable resting-film potential is a constant offset, not a source
        // to be purchased from heat. Only actual surface displacement carries available work.
        var potential = columns.Columns.Sum(column =>
        {
            var displacementMass = column.MassKilograms - column.ReferenceMassKilograms;
            return Parameters.GravityMetersPerSecondSquared * displacementMass * displacementMass /
                (2.0 * DensityKilogramsPerCubicMeter * column.AreaSquareMeters);
        });
        if (!double.IsFinite(potential) || potential < 0.0) throw new InvalidOperationException("Gravitational potential must remain finite and funded.");
        return potential;
    }
}

public readonly record struct PlanetOriginViscousMeltFaceFlux(PlanetSurfaceGridCellId Donor, PlanetSurfaceGridCellId Recipient,
    double MassRateKilogramsPerSecond, double MeanVelocityMetersPerSecond, double ReynoldsNumber);

public sealed record PlanetOriginViscousMeltTrial(PlanetOriginRockColumnField Candidate, double DissipatedWorkJoules,
    double TransportedMassKilograms, IReadOnlyList<PlanetOriginViscousMeltFaceFlux> Faces)
{
    /// <summary>Excluded phase regimes are not a prediction of coherent crust strength or impermeability.</summary>
    public int ExcludedColumnCount { get; init; }
}

public sealed partial class PlanetOriginRockColumnField
{
    private readonly record struct ViscousLayerRate(int Donor, int DonorLayer, int Recipient, int RecipientLayer,
        double MassRateKilogramsPerSecond, double HeatWeight);
    private sealed record ViscousPlan(List<ViscousLayerRate> Layers, List<PlanetOriginViscousMeltFaceFlux> Faces, double MaximumStepSeconds, int ExcludedColumns);

    public double MaximumViscousMeltStepSeconds(PlanetOriginViscousMeltReference reference, CancellationToken cancellationToken = default) =>
        CreateViscousPlan(reference, cancellationToken).MaximumStepSeconds;

    public PlanetOriginViscousMeltTrial ProposeViscousMeltFlow(PlanetOriginDuration interval, PlanetOriginViscousMeltReference reference,
        CancellationToken cancellationToken = default)
    {
        if (interval.IsZero || !double.IsFinite(interval.TotalSeconds) || interval.TotalSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(interval));
        var plan = CreateViscousPlan(reference, cancellationToken);
        if (interval.TotalSeconds > plan.MaximumStepSeconds)
            throw new InvalidOperationException("Viscous flow exceeds its head-relaxation or original-donor mass bound; subdivide the physical interval.");
        if (plan.Layers.Count == 0) return new(this, 0.0, 0.0, plan.Faces.AsReadOnly()) { ExcludedColumnCount = plan.ExcludedColumns };
        var transfers = plan.Layers.Select(rate => new PlanetOriginMeltFaceTransfer(Columns[rate.Donor].Id, rate.DonorLayer,
            Columns[rate.Recipient].Id, rate.RecipientLayer, rate.MassRateKilogramsPerSecond * interval.TotalSeconds)).ToArray();
        var material = ProposeMeltTransfers(transfers, cancellationToken);
        // Use the accepted material masses, including their finite-precision representation.
        // The stable quadratic difference avoids subtracting two large potential totals.
        var massDelta = Columns.Select((column, index) => material.Candidate.Columns[index].MassKilograms - column.MassKilograms).ToArray();
        var work = 0.0;
        for (var index = 0; index < Columns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[index];
            var head = (column.MassKilograms - column.ReferenceMassKilograms) / (reference.DensityKilogramsPerCubicMeter * column.AreaSquareMeters);
            work -= reference.Parameters.GravityMetersPerSecondSquared * (head * massDelta[index] +
                massDelta[index] * massDelta[index] / (2.0 * reference.DensityKilogramsPerCubicMeter * column.AreaSquareMeters));
        }
        if (!double.IsFinite(work) || work < 0.0) throw new InvalidOperationException("An admitted creeping-flow step must dissipate potential, never create it.");
        if (work == 0.0) return new(material.Candidate, 0.0, material.TransportedMassKilograms, plan.Faces.AsReadOnly()) { ExcludedColumnCount = plan.ExcludedColumns };
        var heat = Columns.Select(column => new double[column.Layers.Count]).ToArray();
        var totalWeight = plan.Layers.Sum(rate => rate.HeatWeight);
        if (!double.IsFinite(totalWeight) || totalWeight <= 0.0) throw new InvalidOperationException("Invalid viscous dissipation quadrature.");
        // Close the rounding remainder in the largest physical share. Closing in
        // a vanishing sliver could make its heat negative after many positive allocations.
        var closing = Enumerable.Range(0, plan.Layers.Count).MaxBy(index => plan.Layers[index].HeatWeight);
        var remaining = work;
        for (var index = 0; index < plan.Layers.Count; index++)
        {
            if (index == closing) continue;
            var rate = plan.Layers[index];
            var allocated = work * rate.HeatWeight / totalWeight;
            if (!double.IsFinite(allocated) || allocated < 0.0) throw new InvalidOperationException("Invalid viscous heat allocation.");
            remaining -= allocated;
            heat[rate.Donor][rate.DonorLayer] += allocated * 0.5;
            heat[rate.Recipient][rate.RecipientLayer] += allocated * 0.5;
        }
        if (!double.IsFinite(remaining) || remaining < 0.0) throw new InvalidOperationException("Viscous heat allocations do not close.");
        var closingRate = plan.Layers[closing];
        heat[closingRate.Donor][closingRate.DonorLayer] += remaining * 0.5;
        heat[closingRate.Recipient][closingRate.RecipientLayer] += remaining * 0.5;
        var candidate = material.Candidate.AddOwnedLayerHeat(heat, cancellationToken);
        return new(candidate, work, material.TransportedMassKilograms, plan.Faces.AsReadOnly()) { ExcludedColumnCount = plan.ExcludedColumns };
    }

    internal PlanetOriginRockColumnField AddOwnedLayerHeat(double[][] heat, CancellationToken cancellationToken)
    {
        var result = new PlanetOriginRockColumn[Columns.Count];
        for (var index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[index];
            var layers = column.Layers.Select((layer, depth) => layer.AddEnthalpy(heat[index][depth])).ToArray();
            foreach (var layer in layers) _ = layer.Resolve(Material);
            result[index] = new(column.Id, column.AreaSquareMeters, column.ReferenceThicknessMeters, layers, column.ReferenceMassKilograms);
            if (result[index].ThicknessMeters(Material, Properties) / Aggregate.PlanetRadiusMeters > 0.001)
                throw new InvalidOperationException("Mechanical heating exceeded the thin-shell geometry envelope.");
        }
        var aggregate = PlanetOriginSurfaceRockPhaseField.FromOwnedColumns(Aggregate, result);
        return new(aggregate, Properties, result);
    }

    private ViscousPlan CreateViscousPlan(PlanetOriginViscousMeltReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate(this);
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = Columns.Select(LayerBoundaries).ToArray();
        // The original mass datum makes resting columns exactly well balanced.
        // Subtracting separately summed layer volumes manufactured roundoff-driven currents.
        var heads = Columns.Select(column => (column.MassKilograms - column.ReferenceMassKilograms) /
            (reference.DensityKilogramsPerCubicMeter * column.AreaSquareMeters)).ToArray();
        var centers = Columns.Select(column => PlanetSurfaceGridGeometry.GetCenterDirection(column.Id)).ToArray();
        var liquid = Columns.Select(column => column.Layers.All(layer => layer.Resolve(Material).MeltFraction == 1.0)).ToArray();
        var incidence = new double[Columns.Count];
        var withdrawals = Columns.Select(column => new double[column.Layers.Count]).ToArray();
        var layers = new List<ViscousLayerRate>();
        var faces = new List<PlanetOriginViscousMeltFaceFlux>();
        var density = reference.DensityKilogramsPerCubicMeter;
        var gravity = reference.Parameters.GravityMetersPerSecondSquared;
        var viscosity = reference.Parameters.DynamicViscosityPascalSeconds;
        for (var index = 0; index < Columns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!liquid[index]) continue; // No liquid is routed through a solid or unresolved mush/lid.
            foreach (var side in ColumnSides)
            {
                var other = Aggregate.Layout.GetIndex(PlanetSurfaceGridTopology.GetNeighbor(Columns[index].Id, side).Cell);
                if (other <= index || !liquid[other]) continue;
                var difference = heads[index] - heads[other];
                var bottom = Math.Max(bounds[index][^1], bounds[other][^1]);
                var top = Math.Min(bounds[index][0], bounds[other][0]);
                var depth = top - bottom;
                if (depth <= 0.0) continue;
                var distance = Aggregate.PlanetRadiusMeters * Math.Atan2(PlanetVector.Cross(centers[index], centers[other]).Length,
                    PlanetVector.Dot(centers[index], centers[other]));
                var edge = Aggregate.SharedSphericalEdgeLengthMeters(Columns[index].Id, side);
                if (depth / distance > 0.01 || Math.Abs(difference) / distance > 0.05 || Math.Abs(difference) / depth > 0.25)
                    throw new InvalidOperationException("Viscous face exceeds the declared long-wave/small-face-contrast reference.");
                var mobility = density * density * gravity * edge * depth * depth * depth / (3.0 * viscosity * distance);
                if (!double.IsFinite(mobility) || mobility <= 0.0) throw new InvalidOperationException("Invalid viscous face mobility.");
                incidence[index] += mobility;
                incidence[other] += mobility;
                if (difference == 0.0) continue;
                var massRate = mobility * Math.Abs(difference);
                var velocity = massRate / (density * edge * depth);
                var reynolds = density * velocity * depth / viscosity;
                if (!double.IsFinite(reynolds) || reynolds > reference.Parameters.MaximumReynoldsNumber)
                    throw new InvalidOperationException("Flow inertia exceeds the creeping-flow reference; a momentum solver is required, not a clipped velocity.");
                var donor = difference > 0.0 ? index : other;
                var recipient = difference > 0.0 ? other : index;
                faces.Add(new(Columns[donor].Id, Columns[recipient].Id, massRate, velocity, reynolds));
                AddViscousLayerRates(donor, recipient, bottom, top, massRate, gravity * massRate * Math.Abs(difference), bounds, layers, withdrawals);
            }
        }
        var maximum = double.PositiveInfinity;
        for (var index = 0; index < Columns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (incidence[index] > 0.0) maximum = Math.Min(maximum, 0.25 * density * Columns[index].AreaSquareMeters / incidence[index]);
            for (var depth = 0; depth < Properties.LayerCount; depth++)
                if (withdrawals[index][depth] > 0.0)
                    maximum = Math.Min(maximum, Columns[index].Layers[depth].MassKilograms * 0.25 / withdrawals[index][depth]);
        }
        return new(layers, faces, maximum, liquid.Count(value => !value));
    }

    private static void AddViscousLayerRates(int donor, int recipient, double bottom, double top, double massRate, double workRate,
        double[][] bounds, List<ViscousLayerRate> rates, double[][] withdrawals)
    {
        var depth = top - bottom;
        var normalization = depth * depth * depth / 3.0;
        double Integral(double z) => depth * z * z * 0.5 - z * z * z / 6.0;
        var first = 0;
        var second = 0;
        while (first < bounds[donor].Length - 1 && second < bounds[recipient].Length - 1)
        {
            var high = Math.Min(top, Math.Min(bounds[donor][first], bounds[recipient][second]));
            var low = Math.Max(bottom, Math.Max(bounds[donor][first + 1], bounds[recipient][second + 1]));
            if (high > low)
            {
                var highZ = high - bottom;
                var lowZ = low - bottom;
                var rate = massRate * (Integral(highZ) - Integral(lowZ)) / normalization;
                var shearWeight = workRate * (Math.Pow(depth - lowZ, 3) - Math.Pow(depth - highZ, 3)) / (3.0 * normalization);
                if (!double.IsFinite(rate) || rate < 0.0 || !double.IsFinite(shearWeight) || shearWeight < 0.0)
                    throw new InvalidOperationException("Invalid viscous depth quadrature.");
                if (rate > 0.0)
                {
                    rates.Add(new(donor, first, recipient, second, rate, shearWeight));
                    withdrawals[donor][first] += rate;
                }
            }
            var firstBottom = bounds[donor][first + 1];
            var secondBottom = bounds[recipient][second + 1];
            if (firstBottom >= secondBottom) first++;
            if (secondBottom >= firstBottom) second++;
        }
    }
}
