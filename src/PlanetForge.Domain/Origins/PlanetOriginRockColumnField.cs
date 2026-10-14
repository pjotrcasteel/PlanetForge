using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>Explicit thin-column reference properties; not a calibrated full-depth convecting mantle.</summary>
public readonly record struct PlanetOriginRockColumnProperties(
    int LayerCount,
    double SolidDensityKilogramsPerCubicMeter,
    double LiquidDensityKilogramsPerCubicMeter,
    double ConductivityWattsPerMeterKelvin)
{
    /// <summary>Equal mass at one; larger exponents concentrate depth resolution at the cooling boundary.</summary>
    public double DepthGradingExponent { get; init; } = 1.0;

    /// <summary>Opt-in massless conductive/radiative face; legacy references retain cell-centre radiation.</summary>
    public bool UseRadiatingInterface { get; init; }
    public bool UseImplicitDepthTransport { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(DepthGradingExponent) || DepthGradingExponent < 1.0 || DepthGradingExponent > 2.0 ||
            LayerCount is < 2 or > 128 || !double.IsFinite(SolidDensityKilogramsPerCubicMeter) || SolidDensityKilogramsPerCubicMeter <= 0.0 ||
            !double.IsFinite(LiquidDensityKilogramsPerCubicMeter) || LiquidDensityKilogramsPerCubicMeter <= 0.0 ||
            !double.IsFinite(ConductivityWattsPerMeterKelvin) || ConductivityWattsPerMeterKelvin < 0.0)
            throw new ArgumentOutOfRangeException(nameof(LayerCount), "Rock columns need 2–128 layers, positive finite densities and finite nonnegative conductivity.");
    }
}

/// <summary>An Eulerian control-volume layer, not a moving plate or a differentiated crust parcel.</summary>
public readonly record struct PlanetOriginRockColumnLayer(double MassKilograms, double StoredEnthalpyJoules)
{
    internal double EnthalpyRoundingCorrection { get; init; }

    internal PlanetOriginRockColumnLayer AddEnthalpy(double delta)
    {
        var corrected = delta - EnthalpyRoundingCorrection;
        var next = StoredEnthalpyJoules + corrected;
        return this with { StoredEnthalpyJoules = next, EnthalpyRoundingCorrection = (next - StoredEnthalpyJoules) - corrected };
    }

    public PlanetRockThermalState Resolve(PlanetOriginSurfaceRockPhaseMaterial material) =>
        new PlanetRockEnthalpyModel(material.ToRockProperties(MassKilograms)).Resolve(StoredEnthalpyJoules);

    public double VolumeCubicMeters(PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties)
    {
        var melt = Resolve(material).MeltFraction;
        return MassKilograms * ((1.0 - melt) / properties.SolidDensityKilogramsPerCubicMeter + melt / properties.LiquidDensityKilogramsPerCubicMeter);
    }
}

/// <summary>Immutable owned material from the radiating surface (index 0) down to the interior boundary.</summary>
public sealed class PlanetOriginRockColumn
{
    internal PlanetOriginRockColumn(PlanetSurfaceGridCellId id, double area, double referenceThickness, PlanetOriginRockColumnLayer[] layers)
    {
        Id = id;
        AreaSquareMeters = area;
        ReferenceThicknessMeters = referenceThickness;
        Layers = Array.AsReadOnly(layers);
    }

    public PlanetSurfaceGridCellId Id { get; }
    public double AreaSquareMeters { get; }
    public double ReferenceThicknessMeters { get; }
    public IReadOnlyList<PlanetOriginRockColumnLayer> Layers { get; }
    public double MassKilograms => Layers.Sum(layer => layer.MassKilograms);
    public double StoredEnthalpyJoules => Layers.Sum(layer => layer.StoredEnthalpyJoules);

    public double ThicknessMeters(PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties) =>
        Layers.Sum(layer => layer.VolumeCubicMeters(material, properties)) / AreaSquareMeters;

    /// <summary>
    /// Linear reconstruction of the first solidus isotherm between depth-cell centres.
    /// A thermal diagnostic with mesh uncertainty, not a sharp material interface or mechanical crust.
    /// </summary>
    public double ReconstructSolidusDepthMeters(PlanetOriginSurfaceRockPhaseMaterial material,
        PlanetOriginRockColumnProperties properties, double radiatingFaceKelvin)
    {
        if (!double.IsFinite(radiatingFaceKelvin) || radiatingFaceKelvin < 0.0)
            throw new ArgumentOutOfRangeException(nameof(radiatingFaceKelvin));
        if (radiatingFaceKelvin >= material.SolidusTemperatureKelvin) return 0.0;
        var previousTemperature = radiatingFaceKelvin;
        var previousCentre = 0.0;
        var top = 0.0;
        foreach (var layer in Layers)
        {
            var width = layer.VolumeCubicMeters(material, properties) / AreaSquareMeters;
            var centre = top + width * 0.5;
            var temperature = layer.Resolve(material).TemperatureKelvin;
            if (temperature >= material.SolidusTemperatureKelvin)
                return previousCentre + (centre - previousCentre) *
                    (material.SolidusTemperatureKelvin - previousTemperature) / (temperature - previousTemperature);
            previousTemperature = temperature;
            previousCentre = centre;
            top += width;
        }
        return top;
    }

    /// <summary>Contiguous fully solid surface material; no mechanical strength or chemical differentiation is inferred.</summary>
    public double SolidSurfaceThicknessMeters(PlanetOriginSurfaceRockPhaseMaterial material, PlanetOriginRockColumnProperties properties)
    {
        var thickness = 0.0;
        foreach (var layer in Layers)
        {
            if (layer.Resolve(material).MeltFraction != 0.0) break;
            thickness += layer.MassKilograms / properties.SolidDensityKilogramsPerCubicMeter / AreaSquareMeters;
        }
        return thickness;
    }
}

public sealed record PlanetOriginRockColumnTrial(
    PlanetOriginRockColumnField Candidate,
    double InteriorExportJoules,
    double InteriorImportJoules,
    double AbsorbedStellarJoules,
    double OutgoingInfraredJoules,
    double ConservationResidualJoules);

/// <summary>
/// Depth resolution of the SAME canonical SurfaceRock mass and sensible/fusion enthalpy.
/// Radiation acts only on the top layer; each internal Fourier flux has equal opposite debits/credits.
/// Constant-pressure phase material and planar thin-column geometry are declared reference approximations.
/// </summary>
public sealed partial class PlanetOriginRockColumnField
{
    private PlanetOriginRockColumnField(PlanetOriginSurfaceRockPhaseField aggregate, PlanetOriginRockColumnProperties properties, PlanetOriginRockColumn[] columns)
    {
        Aggregate = aggregate;
        Properties = properties;
        Columns = Array.AsReadOnly(columns);
    }

    public PlanetOriginSurfaceRockPhaseField Aggregate { get; }
    public PlanetOriginRockColumnProperties Properties { get; }
    public IReadOnlyList<PlanetOriginRockColumn> Columns { get; }
    public PlanetOriginSurfaceRockPhaseMaterial Material => Aggregate.Material;

    public static PlanetOriginRockColumnField Partition(PlanetOriginSurfaceRockPhaseField fundedField, PlanetOriginRockColumnProperties properties)
    {
        ArgumentNullException.ThrowIfNull(fundedField);
        properties.Validate();
        var columns = new PlanetOriginRockColumn[fundedField.Cells.Count];
        for (var i = 0; i < columns.Length; i++)
        {
            var cell = fundedField.Cells[i];
            var layers = new PlanetOriginRockColumnLayer[properties.LayerCount];
            var remainingMass = cell.ActiveRockMassKilograms;
            var remainingEnergy = cell.StoredEnthalpyJoules;
            for (var depth = 0; depth < layers.Length; depth++)
            {
                var last = depth == layers.Length - 1;
                var fraction = properties.DepthGradingExponent == 1.0 ? 1.0 / layers.Length : Math.Pow((depth + 1.0) / layers.Length, properties.DepthGradingExponent) -
                    Math.Pow((double)depth / layers.Length, properties.DepthGradingExponent);
                var mass = last ? remainingMass : cell.ActiveRockMassKilograms * fraction;
                var energy = last ? remainingEnergy : cell.StoredEnthalpyJoules * fraction;
                layers[depth] = new PlanetOriginRockColumnLayer(mass, energy);
                remainingMass -= mass;
                remainingEnergy -= energy;
            }
            var thickness = layers.Sum(layer => layer.VolumeCubicMeters(fundedField.Material, properties)) / cell.AreaSquareMeters;
            if (!double.IsFinite(thickness) || thickness <= 0.0 || thickness / fundedField.PlanetRadiusMeters > 0.001)
                throw new InvalidOperationException("Planar rock columns require a positive thin shell (depth/radius <= 0.001).");
            columns[i] = new PlanetOriginRockColumn(cell.Id, cell.AreaSquareMeters, thickness, layers);
        }
        return new PlanetOriginRockColumnField(fundedField, properties, columns);
    }

    public PlanetRockThermalState ResolveSurface(PlanetSurfaceGridCellId id) =>
        Columns[Aggregate.Layout.GetIndex(id)].Layers[0].Resolve(Material);

    /// <summary>Current explicit admission bound; re-evaluate after every accepted immutable substep.</summary>
    public double MaximumStableStepSeconds(PlanetOriginRegionalAirlessParameters boundary,
        PlanetOriginThermalTimeline interior, CancellationToken cancellationToken = default)
    {
        boundary.Validate();
        ArgumentNullException.ThrowIfNull(interior);
        boundary = PlanetOriginMagmaConvection.ResolveBoundary(boundary, this, interior);
        var maximum = MaximumLateralStepSeconds(boundary.LateralConductivityWattsPerMeterKelvin, boundary.LateralLayerThicknessMeters, cancellationToken);
        var mantleConductance = 0.0;
        foreach (var column in Columns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Properties.UseImplicitDepthTransport)
            {
                var bottomWidth = column.Layers[^1].VolumeCubicMeters(Material, Properties) / column.AreaSquareMeters;
                mantleConductance += boundary.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters /
                    (boundary.ConductiveBoundaryThicknessMeters + bottomWidth * 0.5);
                continue;
            }
            var thickness = column.Layers.Select(layer => layer.VolumeCubicMeters(Material, Properties) / column.AreaSquareMeters).ToArray();
            var incidence = new double[thickness.Length];
            for (var depth = 0; depth < thickness.Length - 1; depth++)
            {
                var conductance = Properties.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters /
                    ((thickness[depth] + thickness[depth + 1]) * 0.5);
                incidence[depth] += conductance;
                incidence[depth + 1] += conductance;
            }
            var bottom = boundary.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters /
                (boundary.ConductiveBoundaryThicknessMeters + thickness[^1] * 0.5);
            incidence[^1] += bottom;
            mantleConductance += bottom;
            var surface = column.Layers[0].Resolve(Material).TemperatureKelvin;
            incidence[0] += (Properties.UseRadiatingInterface
                ? Properties.ConductivityWattsPerMeterKelvin / (thickness[0] * 0.5)
                : 4.0 * boundary.InfraredEmissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
                    Math.Pow(surface, 3)) * column.AreaSquareMeters;
            for (var depth = 0; depth < incidence.Length; depth++)
                maximum = Math.Min(maximum, StableSeconds(incidence[depth],
                    column.Layers[depth].MassKilograms * Material.SpecificHeatCapacityJoulesPerKilogramKelvin));
        }
        return Math.Min(maximum, StableSeconds(mantleConductance, interior.MinimumRockHeatCapacityJoulesPerKelvin));
    }

    private static double StableSeconds(double conductance, double capacity)
    {
        if (!double.IsFinite(conductance) || conductance < 0.0 || !double.IsFinite(capacity) || capacity <= 0.0)
            throw new InvalidOperationException("Column capacity and thermal incidence must remain finite and physical.");
        return conductance == 0.0 ? double.PositiveInfinity : 0.5 * capacity / conductance;
    }

    public PlanetOriginRockColumnTrial Propose(
        PlanetOriginDuration interval, PlanetOriginRegionalAirlessParameters boundary,
        PlanetOriginThermalTimeline interior, CancellationToken cancellationToken = default)
    {
        boundary.Validate();
        ArgumentNullException.ThrowIfNull(interior);
        if (interval.IsZero) throw new ArgumentOutOfRangeException(nameof(interval));
        boundary = PlanetOriginMagmaConvection.ResolveBoundary(boundary, this, interior);
        var verticalBoundary = boundary with { LateralConductivityWattsPerMeterKelvin = 0.0, LateralLayerThicknessMeters = 0.0 };
        var vertical = ProposeVertical(interval, verticalBoundary, interior, cancellationToken);
        if (boundary.LateralConductivityWattsPerMeterKelvin == 0.0) return vertical;
        var lateral = vertical.Candidate.ProposeLateralHeat(interval, boundary.LateralConductivityWattsPerMeterKelvin,
            boundary.LateralLayerThicknessMeters, cancellationToken);
        return vertical with { Candidate = lateral.Candidate, ConservationResidualJoules = vertical.ConservationResidualJoules + lateral.ConservationResidualJoules };
    }

    private PlanetOriginRockColumnTrial ProposeVertical(PlanetOriginDuration interval, PlanetOriginRegionalAirlessParameters boundary,
        PlanetOriginThermalTimeline interior, CancellationToken cancellationToken)
    {
        if (Properties.UseImplicitDepthTransport) return ProposeImplicit(interval, boundary, interior, cancellationToken);
        var seconds = interval.TotalSeconds;
        var result = new PlanetOriginRockColumn[Columns.Count];
        var exported = 0.0;
        var imported = 0.0;
        var absorbed = 0.0;
        var emitted = 0.0;
        var interiorCoupling = 0.0;
        for (var i = 0; i < result.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var column = Columns[i];
            var phase = column.Layers.Select(layer => layer.Resolve(Material)).ToArray();
            var thickness = column.Layers.Select(layer => layer.VolumeCubicMeters(Material, Properties) / column.AreaSquareMeters).ToArray();
            var delta = new double[phase.Length];
            var coupling = new double[phase.Length];
            for (var depth = 0; depth < phase.Length - 1; depth++)
            {
                var conductanceSeconds = Properties.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters * seconds /
                    ((thickness[depth] + thickness[depth + 1]) * 0.5);
                var transferred = conductanceSeconds * (phase[depth].TemperatureKelvin - phase[depth + 1].TemperatureKelvin);
                if (!double.IsFinite(transferred)) throw new InvalidOperationException("Column conduction must be finite.");
                delta[depth] -= transferred;
                delta[depth + 1] += transferred;
                coupling[depth] += conductanceSeconds;
                coupling[depth + 1] += conductanceSeconds;
            }

            var bottom = phase.Length - 1;
            var bottomConductance = boundary.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters * seconds /
                (boundary.ConductiveBoundaryThicknessMeters + thickness[bottom] * 0.5);
            var interiorHeat = bottomConductance * (interior.Rock.TemperatureKelvin - phase[bottom].TemperatureKelvin);
            if (!double.IsFinite(interiorHeat) || -interiorHeat > column.Layers[bottom].StoredEnthalpyJoules)
                throw new InvalidOperationException("Column boundary overdraws or overflows its donor enthalpy.");
            delta[bottom] += interiorHeat;
            coupling[bottom] += bottomConductance;
            interiorCoupling += bottomConductance;
            exported += Math.Max(0.0, interiorHeat);
            imported += Math.Max(0.0, -interiorHeat);

            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(column.Id);
            var solar = boundary.SolarIrradianceWattsPerSquareMeter * (1.0 - boundary.BondAlbedo) *
                Math.Max(0.0, PlanetVector.Dot(direction, boundary.StellarDirection)) * column.AreaSquareMeters * seconds;
            var surfaceKelvin = Properties.UseRadiatingInterface
                ? PlanetOriginRockRadiatingBoundary.ResolveTemperature(phase[0].TemperatureKelvin,
                    Properties.ConductivityWattsPerMeterKelvin, thickness[0] * 0.5, solar / column.AreaSquareMeters / seconds, boundary.InfraredEmissivity)
                : phase[0].TemperatureKelvin;
            var infrared = boundary.InfraredEmissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
                Math.Pow(surfaceKelvin, 4) * column.AreaSquareMeters * seconds;
            delta[0] += solar - infrared;
            absorbed += solar;
            emitted += infrared;
            // Bound the explicit radiative Jacobian as well as conductive incidence.
            var radiativeDerivative = 4.0 * boundary.InfraredEmissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
                Math.Pow(surfaceKelvin, 3);
            coupling[0] += (Properties.UseRadiatingInterface
                ? Math.Min(radiativeDerivative, Properties.ConductivityWattsPerMeterKelvin / (thickness[0] * 0.5))
                : radiativeDerivative) * column.AreaSquareMeters * seconds;
            var nextLayers = new PlanetOriginRockColumnLayer[phase.Length];
            for (var depth = 0; depth < nextLayers.Length; depth++)
            {
                VerifyCoupling(coupling[depth], column.Layers[depth].MassKilograms * Material.SpecificHeatCapacityJoulesPerKilogramKelvin);
                var nextLayer = column.Layers[depth].AddEnthalpy(delta[depth]);
                var energy = nextLayer.StoredEnthalpyJoules;
                if (!double.IsFinite(energy) || energy < 0.0)
                    throw new InvalidOperationException("Column step overdraws or overflows a physical rock layer; subdivide the duration.");
                nextLayers[depth] = nextLayer;
            }
            result[i] = new PlanetOriginRockColumn(column.Id, column.AreaSquareMeters, column.ReferenceThicknessMeters, nextLayers);
            if (result[i].ThicknessMeters(Material, Properties) / Aggregate.PlanetRadiusMeters > 0.001)
                throw new InvalidOperationException("Column evolution exceeded the validated thin-shell geometry envelope.");
        }
        VerifyCoupling(interiorCoupling, interior.MinimumRockHeatCapacityJoulesPerKelvin);
        var aggregate = PlanetOriginSurfaceRockPhaseField.FromOwnedColumns(Aggregate, result);
        var candidate = new PlanetOriginRockColumnField(aggregate, Properties, result);
        var residual = Aggregate.TotalStoredEnthalpyJoules + exported - imported + absorbed - emitted - aggregate.TotalStoredEnthalpyJoules;
        if (!double.IsFinite(residual) || !double.IsFinite(exported) || !double.IsFinite(imported) || !double.IsFinite(absorbed) || !double.IsFinite(emitted) ||
            Math.Abs(residual) > Math.Max(1e-6, Aggregate.TotalStoredEnthalpyJoules * 2e-12))
            throw new InvalidOperationException("Depth-resolved rock energy exchanges did not close the canonical surface account.");
        return new PlanetOriginRockColumnTrial(candidate, exported, imported, absorbed, emitted, residual);
    }

    private static void VerifyCoupling(double integratedConductance, double capacity)
    {
        var coupling = integratedConductance / capacity;
        if (!double.IsFinite(capacity) || capacity <= 0.0 || !double.IsFinite(coupling) || coupling > 0.5)
            throw new InvalidOperationException("Rock-column step exceeds its explicit stability bound; subdivide the physical duration.");
    }
}
