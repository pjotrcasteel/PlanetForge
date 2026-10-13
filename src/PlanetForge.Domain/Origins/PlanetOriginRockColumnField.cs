using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>Explicit thin-column reference properties; not a calibrated full-depth convecting mantle.</summary>
public readonly record struct PlanetOriginRockColumnProperties(
    int LayerCount,
    double SolidDensityKilogramsPerCubicMeter,
    double LiquidDensityKilogramsPerCubicMeter,
    double ConductivityWattsPerMeterKelvin)
{
    public void Validate()
    {
        if (LayerCount is < 2 or > 64 || !double.IsFinite(SolidDensityKilogramsPerCubicMeter) || SolidDensityKilogramsPerCubicMeter <= 0.0 ||
            !double.IsFinite(LiquidDensityKilogramsPerCubicMeter) || LiquidDensityKilogramsPerCubicMeter <= 0.0 ||
            !double.IsFinite(ConductivityWattsPerMeterKelvin) || ConductivityWattsPerMeterKelvin < 0.0)
            throw new ArgumentOutOfRangeException(nameof(LayerCount), "Rock columns need 2–64 layers, positive finite densities and finite nonnegative conductivity.");
    }
}

/// <summary>An Eulerian control-volume layer, not a moving plate or a differentiated crust parcel.</summary>
public readonly record struct PlanetOriginRockColumnLayer(double MassKilograms, double StoredEnthalpyJoules)
{
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
public sealed class PlanetOriginRockColumnField
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
                var mass = last ? remainingMass : cell.ActiveRockMassKilograms / layers.Length;
                var energy = last ? remainingEnergy : cell.StoredEnthalpyJoules / layers.Length;
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
        if (boundary.LateralConductivityWattsPerMeterKelvin != 0.0)
            throw new InvalidOperationException("Column integration requires layer-consistent lateral transport.");
        var maximum = double.PositiveInfinity;
        var mantleConductance = 0.0;
        foreach (var column in Columns)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            // The emitting surface is a half-cell above the first cell center.
            // Its effective conductance limits how strongly radiation can
            // perturb the cell as the depth mesh is refined.
            var radiativeConductance = 4.0 * boundary.InfraredEmissivity *
                PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
                Math.Pow(surface, 3) * column.AreaSquareMeters;
            var surfaceConductance = Properties.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters / (0.5 * thickness[0]);
            incidence[0] += surfaceConductance == 0.0 ? 0.0 :
                radiativeConductance * surfaceConductance / (radiativeConductance + surfaceConductance);
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
        if (boundary.LateralConductivityWattsPerMeterKelvin != 0.0)
            throw new InvalidOperationException("Depth-resolved lateral exchange needs layer-consistent fluxes; aggregate lateral conduction cannot silently mix column material.");

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
            var surfaceKelvin = phase[0].TemperatureKelvin;
            var absorbedFlux = solar / (column.AreaSquareMeters * seconds);
            var emittingSurfaceKelvin = ResolveRadiatingSurfaceKelvin(surfaceKelvin, thickness[0],
                absorbedFlux, boundary.InfraredEmissivity, Properties.ConductivityWattsPerMeterKelvin);
            var infrared = boundary.InfraredEmissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
                Math.Pow(emittingSurfaceKelvin, 4) * column.AreaSquareMeters * seconds;
            delta[0] += solar - infrared;
            absorbed += solar;
            emitted += infrared;
            var radiativeConductance = 4.0 * boundary.InfraredEmissivity *
                PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth *
                Math.Pow(emittingSurfaceKelvin, 3) * column.AreaSquareMeters;
            var surfaceConductance = Properties.ConductivityWattsPerMeterKelvin * column.AreaSquareMeters / (0.5 * thickness[0]);
            coupling[0] += surfaceConductance == 0.0 ? 0.0 :
                radiativeConductance * surfaceConductance / (radiativeConductance + surfaceConductance) * seconds;
            var nextLayers = new PlanetOriginRockColumnLayer[phase.Length];
            for (var depth = 0; depth < nextLayers.Length; depth++)
            {
                VerifyCoupling(coupling[depth], column.Layers[depth].MassKilograms * Material.SpecificHeatCapacityJoulesPerKilogramKelvin);
                var energy = column.Layers[depth].StoredEnthalpyJoules + delta[depth];
                if (!double.IsFinite(energy) || energy < 0.0)
                    throw new InvalidOperationException("Column step overdraws or overflows a physical rock layer; subdivide the duration.");
                nextLayers[depth] = column.Layers[depth] with { StoredEnthalpyJoules = energy };
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

    /// <summary>
    /// Solve the steady sub-cell conductive/radiative boundary. Radiation is
    /// emitted at the physical surface, not at the center of the first cell.
    /// The root is unique for nonnegative temperatures and conductivities.
    /// </summary>
    private static double ResolveRadiatingSurfaceKelvin(
        double centerKelvin, double halfCellThicknessMeters, double absorbedFluxWattsPerSquareMeter,
        double emissivity, double conductivityWattsPerMeterKelvin)
    {
        if (conductivityWattsPerMeterKelvin == 0.0) return centerKelvin;
        var conductance = conductivityWattsPerMeterKelvin / (0.5 * halfCellThicknessMeters);
        var radiativeCoefficient = emissivity * PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth;
        if (radiativeCoefficient == 0.0) return centerKelvin + absorbedFluxWattsPerSquareMeter / conductance;
        var lower = 0.0;
        var upper = Math.Max(centerKelvin + absorbedFluxWattsPerSquareMeter / conductance, 1.0);
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var middle = (lower + upper) * 0.5;
            var balance = radiativeCoefficient * Math.Pow(middle, 4) +
                conductance * (middle - centerKelvin) - absorbedFluxWattsPerSquareMeter;
            if (balance > 0.0) upper = middle;
            else lower = middle;
        }
        return (lower + upper) * 0.5;
    }

    private static void VerifyCoupling(double integratedConductance, double capacity)
    {
        var coupling = integratedConductance / capacity;
        if (!double.IsFinite(capacity) || capacity <= 0.0 || !double.IsFinite(coupling) || coupling > 0.5)
            throw new InvalidOperationException("Rock-column step exceeds its explicit stability bound; subdivide the physical duration.");
    }
}
