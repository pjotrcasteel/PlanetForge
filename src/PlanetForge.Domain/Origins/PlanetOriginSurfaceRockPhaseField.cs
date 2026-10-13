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
    double ConservationResidualJoules);

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
public sealed class PlanetOriginSurfaceRockPhaseField
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
        var absorbedStellar = 0.0;
        var emittedInfrared = 0.0;
        for (var index = 0; index < cells.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = cells[index];
            var rate = rates(current, current.Resolve(Material));
            rate.Validate();
            var conductiveEnergy = rate.InteriorConductionWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            var stellarEnergy = rate.AbsorbedStellarWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            var infraredEnergy = rate.OutgoingInfraredWattsPerSquareMeter * current.AreaSquareMeters * seconds;
            var enthalpy = current.StoredEnthalpyJoules + (conductiveEnergy + stellarEnergy - infraredEnergy);
            if (!double.IsFinite(conductiveEnergy) || !double.IsFinite(stellarEnergy) ||
                !double.IsFinite(infraredEnergy) || !double.IsFinite(enthalpy) || enthalpy < 0.0)
            {
                throw new InvalidOperationException("Proposed rock phase transition overdraws or overflows a conserved material cell.");
            }

            next[index] = current with { StoredEnthalpyJoules = enthalpy };
            conduction += conductiveEnergy;
            absorbedStellar += stellarEnergy;
            emittedInfrared += infraredEnergy;
        }

        if (!double.IsFinite(conduction) || !double.IsFinite(absorbedStellar) || !double.IsFinite(emittedInfrared))
        {
            throw new InvalidOperationException("The proposed global rock energy exchanges must be finite.");
        }

        var candidate = new PlanetOriginSurfaceRockPhaseField(Material, Layout, PlanetRadiusMeters, next);
        var residual = TotalStoredEnthalpyJoules + conduction + absorbedStellar - emittedInfrared -
            candidate.TotalStoredEnthalpyJoules;
        return new PlanetOriginSurfaceRockPhaseTrial(candidate, conduction, absorbedStellar, emittedInfrared, residual);
    }
}
