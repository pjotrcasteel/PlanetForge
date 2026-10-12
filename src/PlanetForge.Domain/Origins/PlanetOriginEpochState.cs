namespace PlanetForge.Domain.Origins;

public enum PlanetOriginWaterChangeKind
{
    ExternalDelivery = 0,
    InternalTransfer = 1,
    AtmosphericEscape = 2
}

/// <summary>
/// A model-proposed transfer of existing H2O, explicit impact/accretion
/// delivery, or vapour escaping to space. A proposal is not an automatic
/// condensation or outgassing law: future calibrated physics determines it.
/// </summary>
public readonly record struct PlanetOriginWaterChange(
    PlanetOriginWaterChangeKind Kind,
    PlanetWaterReservoir Source,
    PlanetWaterReservoir Destination,
    double MassKilograms)
{
    public static PlanetOriginWaterChange Deliver(PlanetWaterReservoir destination, double massKilograms) =>
        new(PlanetOriginWaterChangeKind.ExternalDelivery, default, destination, massKilograms);

    public static PlanetOriginWaterChange Transfer(PlanetWaterReservoir source, PlanetWaterReservoir destination, double massKilograms) =>
        new(PlanetOriginWaterChangeKind.InternalTransfer, source, destination, massKilograms);

    public static PlanetOriginWaterChange EscapeAtmosphere(double massKilograms) =>
        new(PlanetOriginWaterChangeKind.AtmosphericEscape, PlanetWaterReservoir.AtmosphericVapor, default, massKilograms);

    internal PlanetWaterInventory Apply(PlanetWaterInventory inventory) => Kind switch
    {
        PlanetOriginWaterChangeKind.ExternalDelivery => inventory.Deliver(Destination, MassKilograms),
        PlanetOriginWaterChangeKind.InternalTransfer => inventory.Transfer(Source, Destination, MassKilograms),
        PlanetOriginWaterChangeKind.AtmosphericEscape => inventory.EscapeAtmosphericVapor(MassKilograms),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown Earth-origin water change.")
    };
}

/// <summary>
/// Inputs for one proposed physical epoch transition. The energy flux and
/// water exchanges are inputs from separate calibrated process solvers;
/// numerical iteration counts are never silently converted into years.
/// </summary>
public sealed record PlanetOriginEpochStep(
    long ElapsedYears,
    PlanetThermalEnergyFlux HeatFlux,
    IReadOnlyList<PlanetOriginWaterChange> WaterChanges);

/// <summary>
/// One immutable publication candidate for Earth-origin thermal and volatile
/// state. Atomically advances absolute geological age, internal energy and
/// the existing mass-conserving H2O ledger. It changes neither Hero terrain
/// nor the existing frozen-climate experiment until publication is integrated.
/// </summary>
public sealed class PlanetOriginEpochState
{
    public PlanetOriginEpochState(PlanetOriginThermalTimeline thermal, PlanetWaterInventory water)
        : this(thermal, water, 0)
    {
    }

    /// <summary>
    /// Creates an atomically published, energy-coupled Earth-origin world.
    /// Interior energy, atmospheric/ocean/ice sensible heat, latent energy,
    /// radiation and H2O all have a single owning ledger.
    /// </summary>
    public PlanetOriginEpochState(PlanetOriginEnergyLedger coupledEnergy)
        : this(coupledEnergy ?? throw new ArgumentNullException(nameof(coupledEnergy)), 0)
    {
    }

    private PlanetOriginEpochState(PlanetOriginEnergyLedger coupledEnergy, long revision)
        : this(coupledEnergy.Interior, coupledEnergy.Environment.Water, revision)
    {
        CoupledEnergy = coupledEnergy;
    }

    private PlanetOriginEpochState(PlanetOriginThermalTimeline thermal, PlanetWaterInventory water, long revision)
    {
        Thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
        Water = water ?? throw new ArgumentNullException(nameof(water));
        Revision = revision;
    }

    public PlanetOriginThermalTimeline Thermal { get; }

    public PlanetWaterInventory Water { get; }

    /// <summary>
    /// The canonical energy account for coupled epochs. Null only for legacy
    /// thermal/water snapshots, which remain readable for compatibility.
    /// Legacy epoch steps cannot advance a coupled world.
    /// </summary>
    public PlanetOriginEnergyLedger? CoupledEnergy { get; }

    public PlanetWaterEnvironmentalState? Environment => CoupledEnergy?.Environment;

    public PlanetEnvironmentTemperatureSnapshot? Temperatures => CoupledEnergy?.Temperatures;

    public long Revision { get; }

    public PlanetOriginClock Clock => Thermal.Clock;

    public PlanetRockThermalState Rock => Thermal.Rock;

    public double EnergyConservationResidualJoules =>
        CoupledEnergy?.ConservationResidualJoules ?? Thermal.EnergyBudget.ConservationResidualJoules;

    public double WaterConservationResidualKilograms => Water.ConservationResidualKilograms;

    public PlanetOriginEpochState Advance(PlanetOriginEpochStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(step.WaterChanges);
        cancellationToken.ThrowIfCancellationRequested();
        if (CoupledEnergy is not null)
        {
            throw new InvalidOperationException("Coupled epochs must advance through their full energy ledger.");
        }

        // No partial publication: any failed flux, source inventory, epoch
        // boundary or cancellation keeps the previous snapshot unchanged.
        var nextThermal = Thermal.AdvanceYears(step.ElapsedYears, step.HeatFlux);
        var nextWater = Water;

        foreach (var change in step.WaterChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nextWater = change.Apply(nextWater);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginEpochState(nextThermal, nextWater, checked(Revision + 1));
    }

    /// <summary>
    /// Advances a single explicitly modelled physical interval. The same
    /// energy-ledger candidate owns the new geological clock and water mass.
    /// </summary>
    public PlanetOriginEpochState AdvanceCoupled(
        PlanetOriginEnergyStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        return AdvanceCoupled([step], cancellationToken);
    }

    /// <summary>
    /// Integrates caller-supplied physical substeps, then publishes exactly
    /// one immutable epoch revision. Radiation, phase proposals and energy
    /// transport must be recalculated by the caller for each substep; this
    /// method never treats a long constant-flux step as automatically safe
    /// across saturation thresholds. If any substep fails or is cancelled,
    /// not even the preceding successful substeps are published.
    /// </summary>
    /// <summary>
    /// Atomically integrates re-evaluated, bounded physical substeps. The
    /// process callback receives the latest accepted candidate state and
    /// must supply *all* physical rates and material/phase proposals. A
    /// rejected trial is reduced and re-proposed; no intermediate candidate
    /// becomes a public geological revision, even if a later trial fails.
    /// </summary>
    public PlanetOriginEpochState AdvanceCoupledAdaptive(
        PlanetOriginDuration duration,
        PlanetOriginAdaptiveIntegrationOptions options,
        Func<PlanetOriginEnergyLedger, PlanetOriginDuration, PlanetOriginEnergyStep> propose,
        CancellationToken cancellationToken = default)
    {
        var initial = CoupledEnergy ?? throw new InvalidOperationException(
            "Adaptive physical steps require an energy-coupled Earth-origin epoch.");
        var candidate = PlanetOriginAdaptiveIntegrator.Integrate(initial, duration, options, propose, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginEpochState(candidate, checked(Revision + 1));
    }

    public PlanetOriginEpochState AdvanceCoupled(
        IReadOnlyList<PlanetOriginEnergyStep> substeps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(substeps);
        cancellationToken.ThrowIfCancellationRequested();

        var energy = CoupledEnergy ?? throw new InvalidOperationException(
            "A legacy thermal/water snapshot has no environmental energy ledger. Construct a coupled epoch explicitly.");
        if (substeps.Count == 0)
        {
            throw new ArgumentException("A coupled epoch requires at least one physical substep.", nameof(substeps));
        }

        var next = energy;
        foreach (var substep in substeps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(substep);
            if (substep.Duration.IsZero)
            {
                throw new ArgumentOutOfRangeException(nameof(substeps), "Each physical substep must advance geological time.");
            }

            next = next.Advance(substep, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginEpochState(next, checked(Revision + 1));
    }

}
