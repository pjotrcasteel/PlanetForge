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

    private PlanetOriginEpochState(PlanetOriginThermalTimeline thermal, PlanetWaterInventory water, long revision)
    {
        Thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
        Water = water ?? throw new ArgumentNullException(nameof(water));
        Revision = revision;
    }

    public PlanetOriginThermalTimeline Thermal { get; }

    public PlanetWaterInventory Water { get; }

    public long Revision { get; }

    public PlanetOriginClock Clock => Thermal.Clock;

    public PlanetRockThermalState Rock => Thermal.Rock;

    public double EnergyConservationResidualJoules => Thermal.EnergyBudget.ConservationResidualJoules;

    public double WaterConservationResidualKilograms => Water.ConservationResidualKilograms;

    public PlanetOriginEpochState Advance(PlanetOriginEpochStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(step.WaterChanges);
        cancellationToken.ThrowIfCancellationRequested();

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
}
