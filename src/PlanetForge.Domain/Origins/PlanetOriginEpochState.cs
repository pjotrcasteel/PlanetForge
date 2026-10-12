namespace PlanetForge.Domain.Origins;

/// <summary>
/// One immutable publication candidate for Earth-origin state. A revision
/// advances only when its complete interior, environment, water and radiation
/// ledger advances successfully.
/// </summary>
public sealed class PlanetOriginEpochState
{
    public PlanetOriginEpochState(PlanetOriginEnergyLedger energyLedger)
        : this(energyLedger, 0)
    {
    }

    private PlanetOriginEpochState(PlanetOriginEnergyLedger energyLedger, long revision)
    {
        EnergyLedger = energyLedger ?? throw new ArgumentNullException(nameof(energyLedger));
        Revision = revision;
    }

    public PlanetOriginEnergyLedger EnergyLedger { get; }

    public PlanetOriginThermalTimeline Thermal => EnergyLedger.Interior;

    public PlanetWaterEnvironmentalState Environment => EnergyLedger.Environment;

    public PlanetWaterInventory Water => Environment.Water;

    public long Revision { get; }

    public PlanetOriginClock Clock => EnergyLedger.Clock;

    public PlanetRockThermalState Rock => Thermal.Rock;

    public double EnergyConservationResidualJoules => EnergyLedger.ConservationResidualJoules;

    public double WaterConservationResidualKilograms => Water.ConservationResidualKilograms;

    public PlanetOriginEpochState Advance(
        PlanetOriginEnergyStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        return AdvanceSubsteps([step.ElapsedYears], (_, _, _) => step, cancellationToken);
    }

    /// <summary>
    /// Evaluates and integrates solver-supplied substeps as one publication
    /// revision. Each evaluator sees the previous candidate ledger and must
    /// recompute its process inputs. This method does not infer saturation
    /// thresholds or choose step sizes.
    /// </summary>
    public PlanetOriginEpochState AdvanceSubsteps(
        IReadOnlyList<long> elapsedYearsBySubstep,
        Func<PlanetOriginEnergyLedger, long, CancellationToken, PlanetOriginEnergyStep> evaluateStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elapsedYearsBySubstep);
        ArgumentNullException.ThrowIfNull(evaluateStep);
        if (elapsedYearsBySubstep.Count == 0)
        {
            throw new ArgumentException("An epoch advance requires at least one physical step.", nameof(elapsedYearsBySubstep));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var nextEnergyLedger = EnergyLedger;
        foreach (var elapsedYears in elapsedYearsBySubstep)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(elapsedYears);
            var step = evaluateStep(nextEnergyLedger, elapsedYears, cancellationToken);
            ArgumentNullException.ThrowIfNull(step);
            if (step.ElapsedYears != elapsedYears)
            {
                throw new ArgumentException("The evaluated step duration must match its requested substep.", nameof(evaluateStep));
            }

            nextEnergyLedger = nextEnergyLedger.Advance(step, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginEpochState(nextEnergyLedger, checked(Revision + 1));
    }
}
