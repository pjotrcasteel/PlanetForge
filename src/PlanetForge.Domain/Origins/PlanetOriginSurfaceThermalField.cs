using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// One spherical cell's share of the existing canonical SurfaceRock sensible
/// energy reservoir. No additional matter or joules are introduced by
/// partitioning. Local Kelvin is derived only from cell energy and capacity.
/// No phase/melt fraction or primordial crust thickness is inferred.
/// </summary>
public readonly record struct PlanetOriginSurfaceThermalCell(
    PlanetSurfaceGridCellId Id,
    double AreaSquareMeters,
    double ActiveRockMassKilograms,
    double SpecificHeatCapacityJoulesPerKilogramKelvin,
    double SensibleEnergyJoules)
{
    public double HeatCapacityJoulesPerKelvin => ActiveRockMassKilograms * SpecificHeatCapacityJoulesPerKilogramKelvin;
    public double TemperatureKelvin => SensibleEnergyJoules / HeatCapacityJoulesPerKelvin;
}

/// <summary>
/// Rates per physical square metre, all explicitly nonnegative.
/// AbsorbedStellar and OutgoingInfrared are external planetary energy
/// exchanges; InteriorConduction is an internal transfer sourced from the
/// separately conserved mantle account.
/// </summary>
public readonly record struct PlanetOriginSurfaceThermalRates(
    double InteriorConductionWattsPerSquareMeter,
    double AbsorbedStellarWattsPerSquareMeter,
    double OutgoingInfraredWattsPerSquareMeter)
{
    /// <summary>Nonnegative reverse conduction, explicitly debited here and credited to the interior.</summary>
    public double InteriorReturnWattsPerSquareMeter { get; init; }

    public void Validate()
    {
        ValidateRate(InteriorConductionWattsPerSquareMeter, nameof(InteriorConductionWattsPerSquareMeter));
        ValidateRate(InteriorReturnWattsPerSquareMeter, nameof(InteriorReturnWattsPerSquareMeter));
        ValidateRate(AbsorbedStellarWattsPerSquareMeter, nameof(AbsorbedStellarWattsPerSquareMeter));
        ValidateRate(OutgoingInfraredWattsPerSquareMeter, nameof(OutgoingInfraredWattsPerSquareMeter));
    }

    private static void ValidateRate(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "A physical surface energy rate must be finite and nonnegative.");
        }
    }
}

/// <summary>
/// An immutable energy-trial result, not an accepted epoch. The three total
/// joule exchanges must be applied once by the canonical ledger before
/// publishing any field to the main planet.
/// </summary>
public sealed record PlanetOriginSurfaceThermalTrial(
    PlanetOriginSurfaceThermalField Candidate,
    double InteriorConductionJoules,
    double AbsorbedStellarJoules,
    double OutgoingInfraredJoules,
    double ConservationResidualJoules);

/// <summary>
/// Physically area-weighted, read-only spatial partition of the existing
/// SurfaceRock heat reservoir. No separate terrain, crust, mantle or thermal
/// timekeeper. Advance is deliberately a pure TRIAL; it cannot publish or
/// change a canonical PlanetOriginEnergyLedger.
/// </summary>
public sealed class PlanetOriginSurfaceThermalField
{
    private readonly IReadOnlyList<PlanetOriginSurfaceThermalCell> cells;

    private PlanetOriginSurfaceThermalField(
        PlanetSurfaceGridLayout layout, double planetRadiusMeters,
        PlanetOriginSurfaceThermalCell[] values)
    {
        Layout = layout;
        PlanetRadiusMeters = planetRadiusMeters;
        cells = Array.AsReadOnly(values);
        TotalActiveRockMassKilograms = values.Sum(cell => cell.ActiveRockMassKilograms);
        TotalSensibleEnergyJoules = values.Sum(cell => cell.SensibleEnergyJoules);
    }

    public PlanetSurfaceGridLayout Layout { get; }
    public double PlanetRadiusMeters { get; }
    public IReadOnlyList<PlanetOriginSurfaceThermalCell> Cells => cells;
    public double TotalActiveRockMassKilograms { get; }
    public double TotalSensibleEnergyJoules { get; }

    public static PlanetOriginSurfaceThermalField Partition(
        PlanetOriginEnergyLedger canonicalEnergy, PlanetSurfaceGridLayout layout, double radiusMeters)
    {
        ArgumentNullException.ThrowIfNull(canonicalEnergy);
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        }

        var parameters = canonicalEnergy.HeatCapacities;
        var mass = parameters.SurfaceRockMassKilograms;
        var totalJoules = canonicalEnergy.Environment.Environment.GetEnergyJoules(PlanetEnvironmentHeatReservoir.SurfaceRock);
        if (mass <= 0.0)
        {
            throw new InvalidOperationException("Cannot spatially partition an unoccupied surface rock reservoir.");
        }

        // Surface grid resolution stays bounded while spatial thermodynamics
        // are experimental. Refining the GPU LOD must never silently create
        // new physical mass or modify the thermal grid.
        if (layout.Level > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(layout), "Surface thermal physics grid is limited to level 6.");
        }

        var areas = new double[layout.CellCount];
        var totalArea = 0.0;
        for (var index = 0; index < areas.Length; index++)
        {
            areas[index] = PlanetSurfaceCellArea.AreaSquareMeters(layout.GetCell(index), radiusMeters);
            totalArea += areas[index];
        }

        var values = new PlanetOriginSurfaceThermalCell[layout.CellCount];
        var remainingMass = mass;
        var remainingEnergy = totalJoules;
        for (var index = 0; index < values.Length; index++)
        {
            var last = index == values.Length - 1;
            var fraction = areas[index] / totalArea;
            var cellMass = last ? remainingMass : mass * fraction;
            var cellEnergy = last ? remainingEnergy : totalJoules * fraction;
            values[index] = new PlanetOriginSurfaceThermalCell(
                layout.GetCell(index), areas[index], cellMass,
                parameters.SurfaceRockSpecificHeatJoulesPerKilogramKelvin, cellEnergy);
            remainingMass -= cellMass;
            remainingEnergy -= cellEnergy;
        }

        return new PlanetOriginSurfaceThermalField(layout, radiusMeters, values);
    }

    /// <summary>
    /// Proposes one local energy rate for each cell against the unchanged
    /// source field. Rates may vary spatially only when justified by a
    /// physical boundary condition or a modeled material property.
    /// </summary>
    public PlanetOriginSurfaceThermalTrial Propose(
        PlanetOriginDuration duration, Func<PlanetOriginSurfaceThermalCell, PlanetOriginSurfaceThermalRates> rates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (duration.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var seconds = duration.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var next = new PlanetOriginSurfaceThermalCell[cells.Count];
        var conduction = 0.0;
        var starlight = 0.0;
        var infrared = 0.0;
        for (var index = 0; index < cells.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cell = cells[index];
            var rate = rates(cell);
            rate.Validate();
            var conducted = rate.InteriorConductionWattsPerSquareMeter * cell.AreaSquareMeters * seconds;
            var absorbed = rate.AbsorbedStellarWattsPerSquareMeter * cell.AreaSquareMeters * seconds;
            var emitted = rate.OutgoingInfraredWattsPerSquareMeter * cell.AreaSquareMeters * seconds;
            if (rate.InteriorReturnWattsPerSquareMeter != 0.0)
                throw new InvalidOperationException("Sensible-only surface trials do not account for reverse conduction; use phase-aware canonical cells.");
            var newEnergy = cell.SensibleEnergyJoules + (conducted + absorbed - emitted);
            if (!double.IsFinite(conducted) || !double.IsFinite(absorbed) || !double.IsFinite(emitted) ||
                !double.IsFinite(newEnergy) || newEnergy < 0.0)
            {
                throw new InvalidOperationException("Local thermal step overdrew or overflowed physical rock energy.");
            }

            next[index] = cell with { SensibleEnergyJoules = newEnergy };
            conduction += conducted;
            starlight += absorbed;
            infrared += emitted;
        }

        if (!double.IsFinite(conduction) || !double.IsFinite(starlight) || !double.IsFinite(infrared))
        {
            throw new InvalidOperationException("Integrated surface energy flow must remain finite.");
        }

        var candidate = new PlanetOriginSurfaceThermalField(Layout, PlanetRadiusMeters, next);
        var residual = TotalSensibleEnergyJoules + conduction + starlight - infrared - candidate.TotalSensibleEnergyJoules;
        return new PlanetOriginSurfaceThermalTrial(candidate, conduction, starlight, infrared, residual);
    }
}
