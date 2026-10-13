using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// Bounded dry-rock heat exchange. The selected sun direction is a known
/// boundary condition, not a calibrated Hadean rotation or orbital history.
/// The same coefficients must be used for every regional cell.
/// </summary>
public readonly record struct PlanetOriginRegionalAirlessParameters(
    PlanetVector StellarDirection,
    double SolarIrradianceWattsPerSquareMeter,
    double BondAlbedo,
    double InfraredEmissivity,
    double ConductivityWattsPerMeterKelvin,
    double ConductiveBoundaryThicknessMeters)
{
    /// <summary>Optional shell conduction across adjacent spherical cells. Zero preserves the airless reference.</summary>
    public double LateralConductivityWattsPerMeterKelvin { get; init; }
    public double LateralLayerThicknessMeters { get; init; }

    public void Validate()
    {
        if (!double.IsFinite(SolarIrradianceWattsPerSquareMeter) || SolarIrradianceWattsPerSquareMeter < 0.0 ||
            !double.IsFinite(BondAlbedo) || BondAlbedo is < 0.0 or > 1.0 ||
            !double.IsFinite(InfraredEmissivity) || InfraredEmissivity is < 0.0 or > 1.0 ||
            !double.IsFinite(ConductivityWattsPerMeterKelvin) || ConductivityWattsPerMeterKelvin < 0.0 ||
            !double.IsFinite(ConductiveBoundaryThicknessMeters) || ConductiveBoundaryThicknessMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(SolarIrradianceWattsPerSquareMeter));
        }

        if (!double.IsFinite(StellarDirection.X) || !double.IsFinite(StellarDirection.Y) ||
            !double.IsFinite(StellarDirection.Z) ||
            Math.Abs(PlanetVector.Dot(StellarDirection, StellarDirection) - 1.0) > 1e-8)
        {
            throw new ArgumentOutOfRangeException(nameof(StellarDirection), "Sun direction must be a unit vector.");
        }

        if (!double.IsFinite(LateralConductivityWattsPerMeterKelvin) || LateralConductivityWattsPerMeterKelvin < 0.0 ||
            !double.IsFinite(LateralLayerThicknessMeters) || LateralLayerThicknessMeters < 0.0 ||
            (LateralConductivityWattsPerMeterKelvin > 0.0 && LateralLayerThicknessMeters == 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(LateralConductivityWattsPerMeterKelvin),
                "Lateral heat transfer requires finite nonnegative conductivity and a positive finite active-layer thickness.");
        }
    }
}

/// <summary>
/// One canonical epoch and its owned regional phase-enthalpy interpretation.
/// The phase field's total joules are exactly the existing SurfaceRock
/// reservoir; it cannot introduce a second rock energy ledger.
/// </summary>
public sealed class PlanetOriginCanonicalRockCells
{
    public PlanetOriginCanonicalRockCells(PlanetOriginEpochState epoch, PlanetOriginSurfaceRockPhaseField field, PlanetOriginRockColumnField? columns = null)
    {
        Epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Columns = columns;
        if (columns is not null && !ReferenceEquals(columns.Aggregate, field))
            throw new InvalidOperationException("Column aggregate must be the exact phase field owned by this canonical world.");
        CheckInvariant(Epoch, Field);
    }

    public PlanetOriginEpochState Epoch { get; }
    public PlanetOriginSurfaceRockPhaseField Field { get; }
    public PlanetOriginRockColumnField? Columns { get; }

    /// <summary>Accepted forcing for optical face reconstruction; never an additional energy reservoir.</summary>
    public PlanetOriginRegionalAirlessParameters? SurfaceBoundary { get; init; }

    public double ResolveRadiatingSurfaceTemperature(PlanetSurfaceGridCellId id)
    {
        if (Columns is not { Properties.UseRadiatingInterface: true } || SurfaceBoundary is not { } boundary)
            return ResolveSurfacePhase(id).TemperatureKelvin;
        var column = Columns.Columns[Field.Layout.GetIndex(id)];
        var layer = column.Layers[0];
        var absorbed = boundary.SolarIrradianceWattsPerSquareMeter * (1.0 - boundary.BondAlbedo) *
            Math.Max(0.0, PlanetVector.Dot(PlanetSurfaceGridGeometry.GetCenterDirection(id), boundary.StellarDirection));
        return PlanetOriginRockRadiatingBoundary.ResolveTemperature(layer.Resolve(Field.Material).TemperatureKelvin,
            Columns.Properties.ConductivityWattsPerMeterKelvin, layer.VolumeCubicMeters(Field.Material, Columns.Properties) / column.AreaSquareMeters * 0.5,
            absorbed, boundary.InfraredEmissivity);
    }

    public PlanetRockThermalState ResolveSurfacePhase(PlanetSurfaceGridCellId id) =>
        Columns?.ResolveSurface(id) ?? Field.Cells[Field.Layout.GetIndex(id)].Resolve(Field.Material);

    public PlanetOriginCanonicalRockCells Advance(
        PlanetOriginDuration interval, PlanetOriginRegionalAirlessParameters parameters,
        CancellationToken cancellationToken = default)
    {
        parameters.Validate();
        if (interval.IsZero) throw new ArgumentOutOfRangeException(nameof(interval));
        var ledger = Epoch.CoupledEnergy!;
        var water = ledger.Environment.Water;
        if (ledger.HeatCapacities.NoncondensableAtmosphereMassKilograms != 0.0 ||
            water.PlanetaryWaterMassKilograms != 0.0)
        {
            throw new InvalidOperationException("Regional airless radiation cannot represent atmospheric or wet epochs.");
        }

        if (Columns is not null) return AdvanceColumns(interval, parameters, cancellationToken);
        VerifyInteriorConductionStability(interval, parameters, ledger.Interior, cancellationToken);
        var seconds = interval.TotalSeconds;
        var area = 4.0 * Math.PI * Field.PlanetRadiusMeters * Field.PlanetRadiusMeters;
        var interiorTemp = ledger.Interior.Rock.TemperatureKelvin;
        var trial = Field.Propose(interval, (cell, phase) =>
        {
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id);
            var cosine = Math.Max(0.0, PlanetVector.Dot(direction, parameters.StellarDirection));
            var absorbed = parameters.SolarIrradianceWattsPerSquareMeter * (1.0 - parameters.BondAlbedo) * cosine;
            var conductive = parameters.ConductivityWattsPerMeterKelvin *
                (interiorTemp - phase.TemperatureKelvin) / parameters.ConductiveBoundaryThicknessMeters;
            var kelvinSquared = phase.TemperatureKelvin * phase.TemperatureKelvin;
            var emitted = parameters.InfraredEmissivity *
                PlanetRadiationBalanceModel.StefanBoltzmannWattsPerSquareMeterKelvinFourth * kelvinSquared * kelvinSquared;
            return new PlanetOriginSurfaceThermalRates(Math.Max(0.0, conductive), absorbed, emitted)
            {
                InteriorReturnWattsPerSquareMeter = Math.Max(0.0, -conductive)
            };
        }, cancellationToken);

        // Operator splitting: compute boundary heat exchanges from the current
        // canonical state, then move heat *within* that same SurfaceRock account.
        // Lateral flux has no source or sink in the global energy ledger.
        var lateralField = parameters.LateralConductivityWattsPerMeterKelvin == 0.0
            ? trial.Candidate
            : trial.Candidate.ProposeLateralHeat(interval, parameters.LateralConductivityWattsPerMeterKelvin,
                parameters.LateralLayerThicknessMeters, cancellationToken).Candidate;

        var radiation = new PlanetRadiativeBalance(
            area, trial.AbsorbedStellarJoules / seconds / area, trial.OutgoingInfraredJoules / seconds / area,
            trial.AbsorbedStellarJoules / seconds, trial.OutgoingInfraredJoules / seconds);
        var step = new PlanetOriginEnergyStep(
            interval, 0.0, 0.0,
            new PlanetInteriorHeatExport(trial.InteriorConductionJoules / seconds, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(radiation, PlanetEnvironmentHeatReservoir.SurfaceRock,
                PlanetEnvironmentHeatReservoir.SurfaceRock),
            [])
        {
            InteriorImport = new PlanetInteriorHeatImport(trial.InteriorReturnJoules / seconds, PlanetEnvironmentHeatReservoir.SurfaceRock)
        };
        var nextEpoch = Epoch.AdvanceCoupled(step, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginCanonicalRockCells(nextEpoch, lateralField);
    }

    private PlanetOriginCanonicalRockCells AdvanceColumns(
        PlanetOriginDuration interval, PlanetOriginRegionalAirlessParameters parameters, CancellationToken cancellationToken)
    {
        var trial = Columns!.Propose(interval, parameters, Epoch.Thermal, cancellationToken);
        var seconds = interval.TotalSeconds;
        var area = 4.0 * Math.PI * Field.PlanetRadiusMeters * Field.PlanetRadiusMeters;
        var radiation = new PlanetRadiativeBalance(area, trial.AbsorbedStellarJoules / seconds / area, trial.OutgoingInfraredJoules / seconds / area,
            trial.AbsorbedStellarJoules / seconds, trial.OutgoingInfraredJoules / seconds);
        var step = new PlanetOriginEnergyStep(interval, 0.0, 0.0,
            new PlanetInteriorHeatExport(trial.InteriorExportJoules / seconds, PlanetEnvironmentHeatReservoir.SurfaceRock),
            new PlanetRadiativeExchange(radiation, PlanetEnvironmentHeatReservoir.SurfaceRock, PlanetEnvironmentHeatReservoir.SurfaceRock), [])
        {
            InteriorImport = new PlanetInteriorHeatImport(trial.InteriorImportJoules / seconds, PlanetEnvironmentHeatReservoir.SurfaceRock)
        };
        var nextEpoch = Epoch.AdvanceCoupled(step, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginCanonicalRockCells(nextEpoch, trial.Candidate.Aggregate, trial.Candidate) { SurfaceBoundary = parameters };
    }

    private void VerifyInteriorConductionStability(
        PlanetOriginDuration interval, PlanetOriginRegionalAirlessParameters parameters,
        PlanetOriginThermalTimeline interior, CancellationToken cancellationToken)
    {
        if (parameters.ConductivityWattsPerMeterKelvin == 0.0) return;
        var conductancePerArea = parameters.ConductivityWattsPerMeterKelvin / parameters.ConductiveBoundaryThicknessMeters;
        var integratedConductance = 0.0;
        foreach (var cell in Field.Cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conductanceSeconds = conductancePerArea * cell.AreaSquareMeters * interval.TotalSeconds;
            var capacity = cell.ActiveRockMassKilograms * Field.Material.SpecificHeatCapacityJoulesPerKilogramKelvin;
            VerifyThermalCoupling(conductanceSeconds, capacity);
            integratedConductance += conductanceSeconds;
        }

        // The common interior is another finite-capacity node, coupled to ALL cells.
        // Sensible cp is a conservative lower bound even across the fusion interval.
        VerifyThermalCoupling(integratedConductance, interior.MinimumRockHeatCapacityJoulesPerKelvin);
    }

    private static void VerifyThermalCoupling(double conductanceSeconds, double minimumCapacity)
    {
        var coupling = conductanceSeconds / minimumCapacity;
        if (!double.IsFinite(minimumCapacity) || minimumCapacity <= 0.0 || !double.IsFinite(coupling) || coupling > 0.5)
            throw new InvalidOperationException("Interior conduction exceeds its explicit thermal stability bound; subdivide the physical duration.");
    }

    private static void CheckInvariant(PlanetOriginEpochState epoch, PlanetOriginSurfaceRockPhaseField field)
    {
        var ledger = epoch.CoupledEnergy ??
            throw new InvalidOperationException("Spatial rock requires an energy-coupled geological epoch.");
        if (ledger.HeatCapacities.SurfaceRockPhaseMaterial != field.Material)
            throw new InvalidOperationException("Local phase material must match the canonical ledger.");
        var energy = ledger.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock);
        var mass = ledger.HeatCapacities.SurfaceRockMassKilograms;
        var maxEnergyTolerance = Math.Max(Math.Abs(energy) * 2e-12, 1e-6);
        var maxMassTolerance = Math.Max(Math.Abs(mass) * 2e-12, 1e-6);
        if (Math.Abs(energy - field.TotalStoredEnthalpyJoules) > maxEnergyTolerance ||
            Math.Abs(mass - field.TotalActiveRockMassKilograms) > maxMassTolerance)
        {
            throw new InvalidOperationException($"Surface cells differ from the canonical world: relative energy {(field.TotalStoredEnthalpyJoules - energy) / energy:R}, relative mass {(field.TotalActiveRockMassKilograms - mass) / mass:R}.");
        }
    }
}
