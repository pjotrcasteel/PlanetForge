using PlanetForge.Domain.Surface;

namespace PlanetForge.Domain.Origins;

/// <summary>
/// Composition-dependent values are supplied explicitly, not inferred from
/// colour, geography, mantle bulk melt fraction, or a fixed Hadean chronology.
/// These are effective one-component reference properties; they do not yet
/// represent crystallization differentiation or permanent primordial crust.
/// </summary>
public readonly record struct PlanetOriginSurfaceRockPhaseMaterial(
    double SpecificHeatCapacityJoulesPerKilogramKelvin,
    double LatentHeatOfFusionJoulesPerKilogram,
    double SolidusTemperatureKelvin,
    double LiquidusTemperatureKelvin)
{
    public void Validate()
    {
        _ = new PlanetRockEnthalpyModel(ToRockProperties(1.0));
    }

    public PlanetRockThermalProperties ToRockProperties(double massKilograms) =>
        new(massKilograms, SpecificHeatCapacityJoulesPerKilogramKelvin, LatentHeatOfFusionJoulesPerKilogram,
            SolidusTemperatureKelvin, LiquidusTemperatureKelvin);
}

/// <summary>
/// An active physical cell's rock enthalpy (sensible + fusion latent energy).
/// Temperature and melt fraction are inverted from the ONE conserved energy
/// quantity, never interpolated independently by the WebGL shader.
/// </summary>
public readonly record struct PlanetOriginSurfaceRockPhaseCell(
    PlanetSurfaceGridCellId Id,
    double AreaSquareMeters,
    double ActiveRockMassKilograms,
    double StoredEnthalpyJoules)
{
    public PlanetRockThermalState Resolve(PlanetOriginSurfaceRockPhaseMaterial material) =>
        new PlanetRockEnthalpyModel(material.ToRockProperties(ActiveRockMassKilograms)).Resolve(StoredEnthalpyJoules);
}

/// <summary>
/// A candidate spatial enthalpy distribution and its physical energy boundary
/// terms. It remains uncommitted until the canonical environment ledger
/// supports latent energy for the same named SurfaceRock reservoir.
/// </summary>
public sealed record PlanetOriginSurfaceRockPhaseTrial(
    PlanetOriginSurfaceRockPhaseField Candidate,
    double InteriorConductionJoules,
    double AbsorbedStellarJoules,
    double OutgoingInfraredJoules,
    double ConservationResidualJoules)
{
    public double InteriorReturnJoules { get; init; }
}

/// <summary>
/// Experimental phase-aware state for the EXISTING spherical surface cells.
/// Initial creation is deliberately permitted only from fully SOLID canonical
/// sensible-rock energy. A molten reference cannot silently reinterpret the
/// legacy sensible-only reservoir as latent-inclusive enthalpy.
///
/// This read-only trial API never publishes an epoch or changes the shared
/// energy ledger. A future atomic ledger migration/commit is required before
/// these cells may drive the main globe.
/// </summary>
public sealed partial class PlanetOriginSurfaceRockPhaseField
{
    private readonly IReadOnlyList<PlanetOriginSurfaceRockPhaseCell> cells;

    private PlanetOriginSurfaceRockPhaseField(
        PlanetOriginSurfaceRockPhaseMaterial material,
        PlanetSurfaceGridLayout layout,
        double radiusMeters,
        PlanetOriginSurfaceRockPhaseCell[] values)
    {
        Material = material;
        Layout = layout;
        PlanetRadiusMeters = radiusMeters;
        cells = Array.AsReadOnly(values);
        TotalActiveRockMassKilograms = values.Sum(x => x.ActiveRockMassKilograms);
        TotalStoredEnthalpyJoules = values.Sum(x => x.StoredEnthalpyJoules);
    }

    public PlanetOriginSurfaceRockPhaseMaterial Material { get; }
    public PlanetSurfaceGridLayout Layout { get; }
    public double PlanetRadiusMeters { get; }
    public IReadOnlyList<PlanetOriginSurfaceRockPhaseCell> Cells => cells;
    public double TotalActiveRockMassKilograms { get; }
    public double TotalStoredEnthalpyJoules { get; }

    public static PlanetOriginSurfaceRockPhaseField FromSolidCanonicalSurface(
        PlanetOriginEnergyLedger canonicalEnergy,
        PlanetSurfaceGridLayout layout,
        double planetRadiusMeters,
        PlanetOriginSurfaceRockPhaseMaterial material)
    {
        material.Validate();
        ArgumentNullException.ThrowIfNull(canonicalEnergy);
        if (material.SpecificHeatCapacityJoulesPerKilogramKelvin !=
            canonicalEnergy.HeatCapacities.SurfaceRockSpecificHeatJoulesPerKilogramKelvin)
        {
            throw new InvalidOperationException(
                "Canonical SurfaceRock heat capacity must match the phase material; changing material properties requires a conserved migration.");
        }

        var sensible = PlanetOriginSurfaceThermalField.Partition(canonicalEnergy, layout, planetRadiusMeters);
        var values = new PlanetOriginSurfaceRockPhaseCell[sensible.Cells.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var cell = sensible.Cells[i];
            if (cell.TemperatureKelvin > material.SolidusTemperatureKelvin)
            {
                throw new InvalidOperationException(
                    "Molten surface rock needs an explicit canonical latent-enthalpy migration; do not double-count fusion energy.");
            }

            // Below solidus the existing sensible-only energy and the phase
            // model's enthalpy have the same zero-K reference, exactly.
            var cellMaterial = material.ToRockProperties(cell.ActiveRockMassKilograms);
            var model = new PlanetRockEnthalpyModel(cellMaterial);
            var recovered = model.Resolve(cell.SensibleEnergyJoules);
            if (recovered.MeltFraction != 0.0)
            {
                throw new InvalidOperationException("A sensible-only store cannot seed molten phase cells.");
            }

            values[i] = new PlanetOriginSurfaceRockPhaseCell(
                cell.Id, cell.AreaSquareMeters, cell.ActiveRockMassKilograms, cell.SensibleEnergyJoules);
        }

        return new PlanetOriginSurfaceRockPhaseField(material, layout, planetRadiusMeters, values);
    }

    /// <summary>
    /// Creates spatial cells from an EXISTING phase-aware canonical budget.
    /// The joules are already funded fusion enthalpy; this method merely
    /// partitions them, without creating or consuming an additional joule.
    /// </summary>
    public static PlanetOriginSurfaceRockPhaseField FromCanonicalPhaseSurface(
        PlanetOriginEnergyLedger canonicalEnergy,
        PlanetSurfaceGridLayout layout,
        double planetRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(canonicalEnergy);
        var material = canonicalEnergy.HeatCapacities.SurfaceRockPhaseMaterial ??
            throw new InvalidOperationException("Canonical surface energy has not been declared phase-aware.");
        material.Validate();
        var sensiblePartition = PlanetOriginSurfaceThermalField.Partition(canonicalEnergy, layout, planetRadiusMeters);
        var values = sensiblePartition.Cells.Select(cell => new PlanetOriginSurfaceRockPhaseCell(
            cell.Id, cell.AreaSquareMeters, cell.ActiveRockMassKilograms, cell.SensibleEnergyJoules)).ToArray();
        var field = new PlanetOriginSurfaceRockPhaseField(material, layout, planetRadiusMeters, values);
        foreach (var cell in values)
        {
            _ = cell.Resolve(material);
        }

        return field;
    }

    /// <summary>
    /// Conservatively redistributes EXISTING enthalpy between cells to encode
    /// an explicitly supplied heterogeneous initial condition. Weights have
    /// no historical interpretation; there is no unrecorded impact heating.
    /// </summary>
    public PlanetOriginSurfaceRockPhaseField RedistributeInitialEnergy(
        Func<PlanetOriginSurfaceRockPhaseCell, double> relativeWeight,
        double redistributedJoules)
    {
        ArgumentNullException.ThrowIfNull(relativeWeight);
        if (!double.IsFinite(redistributedJoules) || redistributedJoules < 0.0)
            throw new ArgumentOutOfRangeException(nameof(redistributedJoules));

        var weights = cells.Select(relativeWeight).ToArray();
        if (weights.Any(v => !double.IsFinite(v) || v < 0.0) || weights.All(v => v == 0.0))
            throw new ArgumentOutOfRangeException(nameof(relativeWeight), "Weights must be finite, nonnegative and contain at least one positive cell.");
        var weightedTotal = weights.Sum();
        var sourceEnergy = TotalStoredEnthalpyJoules;
        if (!double.IsFinite(weightedTotal) || weightedTotal <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(relativeWeight));

        // Distribution is an explicit initial condition, not an unbalanced
        // forcing law: the reference uniform heat is reassigned by mass area.
        var remaining = sourceEnergy;
        var values = new PlanetOriginSurfaceRockPhaseCell[cells.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var current = cells[i];
            var uniformShare = sourceEnergy * (current.ActiveRockMassKilograms / TotalActiveRockMassKilograms);
            var selectedShare = sourceEnergy * weights[i] / weightedTotal;
            var redistributed = redistributedJoules / sourceEnergy;
            var candidate = uniformShare * (1.0 - redistributed) + selectedShare * redistributed;
            if (i == values.Length - 1) candidate = remaining;
            if (!double.IsFinite(candidate) || candidate < 0.0)
                throw new InvalidOperationException("Initial spatial redistribution cannot overdraw rock enthalpy.");
            values[i] = current with { StoredEnthalpyJoules = candidate };
            remaining -= candidate;
        }

        return new PlanetOriginSurfaceRockPhaseField(Material, Layout, PlanetRadiusMeters, values);
    }

    public PlanetOriginSurfaceRockPhaseTrial Propose(
        PlanetOriginDuration duration,
        Func<PlanetOriginSurfaceRockPhaseCell, PlanetRockThermalState, PlanetOriginSurfaceThermalRates> rates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (duration.IsZero) throw new ArgumentOutOfRangeException(nameof(duration));
        var seconds = duration.TotalSeconds;
        if (!double.IsFinite(seconds) || seconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var next = new PlanetOriginSurfaceRockPhaseCell[cells.Count];
        var conduction = 0.0;
        var returned = 0.0;
        var absorbedStellar = 0.0;
        var emittedInfrared = 0.0;
        for (var index = 0; index < cells.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = cells[index];
            var rate = rates(current, current.Resolve(Material));
            rate.Validate();
            var conductiveEnergy = rate.InteriorConductionWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            var returnEnergy = rate.InteriorReturnWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            if (!double.IsFinite(returnEnergy) || returnEnergy > current.StoredEnthalpyJoules)
                throw new InvalidOperationException("Reverse conduction exceeds the unchanged surface donor enthalpy.");
            var stellarEnergy = rate.AbsorbedStellarWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            var infraredEnergy = rate.OutgoingInfraredWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            var enthalpy = current.StoredEnthalpyJoules + (conductiveEnergy + stellarEnergy - infraredEnergy - returnEnergy);
            if (!double.IsFinite(conductiveEnergy) || !double.IsFinite(stellarEnergy) ||
                !double.IsFinite(infraredEnergy) || !double.IsFinite(enthalpy) || enthalpy < 0.0)
            {
                throw new InvalidOperationException("Proposed rock phase transition overdraws or overflows a conserved material cell.");
            }

            next[index] = current with { StoredEnthalpyJoules = enthalpy };
            conduction += conductiveEnergy;
            returned += returnEnergy;
            absorbedStellar += stellarEnergy;
            emittedInfrared += infraredEnergy;
        }

        if (!double.IsFinite(conduction) || !double.IsFinite(returned) || !double.IsFinite(absorbedStellar) || !double.IsFinite(emittedInfrared))
        {
            throw new InvalidOperationException("The proposed global rock energy exchanges must be finite.");
        }

        var candidate = new PlanetOriginSurfaceRockPhaseField(Material, Layout, PlanetRadiusMeters, next);
        var residual = TotalStoredEnthalpyJoules + conduction - returned + absorbedStellar - emittedInfrared -
            candidate.TotalStoredEnthalpyJoules;
        return new PlanetOriginSurfaceRockPhaseTrial(candidate, conduction, absorbedStellar, emittedInfrared, residual)
        {
            InteriorReturnJoules = returned
        };
    }
}
