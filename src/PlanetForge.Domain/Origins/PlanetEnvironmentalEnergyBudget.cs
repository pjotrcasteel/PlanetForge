namespace PlanetForge.Domain.Origins;

/// <summary>
/// Separate *environmental* sensible-energy accounts. SurfaceRock denotes
/// near-surface material, not the already modeled mantle/magma energy budget.
/// SurfaceIce is the sensible energy of the frozen-water mass itself, so ice
/// can carry heat when it melts, sublimates or forms from liquid or vapor.
/// Initial energies and future heat fluxes require calibrated models.
/// </summary>
public enum PlanetEnvironmentHeatReservoir
{
    Atmosphere = 0,
    SurfaceOcean = 1,
    SurfaceRock = 2,
    SurfaceIce = 3
}

/// <summary>
/// An immutable, non-negative environmental heat budget. No atmospheric or
/// oceanic temperature is inferred here without their own mass/heat capacity.
/// </summary>
public sealed class PlanetEnvironmentalEnergyBudget
{
    private const int ReservoirCount = 4;
    private readonly double[] energies;

    /// <summary>Creates a budget whose ice store holds no sensible energy.</summary>
    public PlanetEnvironmentalEnergyBudget(double atmosphereJoules, double oceanJoules, double surfaceRockJoules)
        : this([atmosphereJoules, oceanJoules, surfaceRockJoules, 0.0])
    {
    }

    public PlanetEnvironmentalEnergyBudget(
        double atmosphereJoules, double oceanJoules, double surfaceRockJoules, double surfaceIceJoules)
        : this([atmosphereJoules, oceanJoules, surfaceRockJoules, surfaceIceJoules])
    {
    }

    private PlanetEnvironmentalEnergyBudget(double[] energies)
    {
        foreach (var energy in energies) ValidateEnergy(energy);
        if (!double.IsFinite(energies.Sum()))
        {
            throw new ArgumentOutOfRangeException(nameof(energies), "Total environmental heat must remain finite.");
        }

        this.energies = energies;
    }

    public double TotalEnergyJoules => energies.Sum();

    public double GetEnergyJoules(PlanetEnvironmentHeatReservoir reservoir) => energies[ValidateReservoir(reservoir)];

    /// <summary>Moves sensible heat between existing environmental stores, without creating heat.</summary>
    public PlanetEnvironmentalEnergyBudget TransferHeat(
        PlanetEnvironmentHeatReservoir source, PlanetEnvironmentHeatReservoir destination, double joules)
    {
        var sourceIndex = ValidateReservoir(source);
        var destinationIndex = ValidateReservoir(destination);
        ValidateEnergy(joules);
        if (sourceIndex == destinationIndex)
        {
            throw new ArgumentException("Heat source and destination must differ.", nameof(destination));
        }

        if (joules == 0.0) return this;
        if (joules > energies[sourceIndex])
        {
            throw new InvalidOperationException("Environmental heat transfer exceeds the source energy.");
        }

        var copy = (double[])energies.Clone();
        copy[sourceIndex] -= joules;
        copy[destinationIndex] = AddFinite(copy[destinationIndex], joules);
        return new PlanetEnvironmentalEnergyBudget(copy);
    }

    internal PlanetEnvironmentalEnergyBudget ExchangeLatentHeat(
        PlanetEnvironmentHeatReservoir reservoir, double absorbedFromEnvironmentJoules, double releasedToEnvironmentJoules)
    {
        var index = ValidateReservoir(reservoir);
        ValidateEnergy(absorbedFromEnvironmentJoules);
        ValidateEnergy(releasedToEnvironmentJoules);
        if (absorbedFromEnvironmentJoules > energies[index])
        {
            throw new InvalidOperationException("The phase change demands more heat than the selected reservoir contains.");
        }

        if (absorbedFromEnvironmentJoules == 0.0 && releasedToEnvironmentJoules == 0.0) return this;
        var copy = (double[])energies.Clone();
        copy[index] = AddFinite(copy[index] - absorbedFromEnvironmentJoules, releasedToEnvironmentJoules);
        return new PlanetEnvironmentalEnergyBudget(copy);
    }

    /// <summary>
    /// Credits heat that crossed into the environment from a separately
    /// debited account (for example interior conduction). The caller owns the
    /// matching debit; this method never creates energy by itself.
    /// </summary>
    internal PlanetEnvironmentalEnergyBudget Deposit(PlanetEnvironmentHeatReservoir reservoir, double joules)
    {
        var index = ValidateReservoir(reservoir);
        ValidateEnergy(joules);
        if (joules == 0.0) return this;
        var copy = (double[])energies.Clone();
        copy[index] = AddFinite(copy[index], joules);
        return new PlanetEnvironmentalEnergyBudget(copy);
    }

    /// <summary>Withdraws heat carried away by matter leaving the modelled planet.</summary>
    internal PlanetEnvironmentalEnergyBudget Withdraw(PlanetEnvironmentHeatReservoir reservoir, double joules)
    {
        var index = ValidateReservoir(reservoir);
        ValidateEnergy(joules);
        if (joules > energies[index])
        {
            throw new InvalidOperationException("Material escape exceeds the heat available in its donor reservoir.");
        }

        if (joules == 0.0) return this;
        var copy = (double[])energies.Clone();
        copy[index] -= joules;
        return new PlanetEnvironmentalEnergyBudget(copy);
    }

    /// <summary>
    /// Applies one interval of external radiation: stellar energy enters the
    /// absorbing store and infrared energy leaves the emitting store. When both
    /// are the same store only the net change is applied. Either store becoming
    /// negative rejects the whole interval.
    /// </summary>
    internal PlanetEnvironmentalEnergyBudget ApplyExternalRadiation(
        PlanetEnvironmentHeatReservoir absorbingReservoir, double absorbedJoules,
        PlanetEnvironmentHeatReservoir emittingReservoir, double emittedJoules)
    {
        var absorbingIndex = ValidateReservoir(absorbingReservoir);
        var emittingIndex = ValidateReservoir(emittingReservoir);
        ValidateEnergy(absorbedJoules);
        ValidateEnergy(emittedJoules);

        var copy = (double[])energies.Clone();
        copy[absorbingIndex] = AddFinite(copy[absorbingIndex], absorbedJoules);
        copy[emittingIndex] -= emittedJoules;
        if (!double.IsFinite(copy[emittingIndex]) || copy[emittingIndex] < 0.0)
        {
            throw new InvalidOperationException("Radiative heat loss exceeds the energy of the explicitly selected emitting reservoir.");
        }

        return new PlanetEnvironmentalEnergyBudget(copy);
    }

    private static int ValidateReservoir(PlanetEnvironmentHeatReservoir reservoir)
    {
        if (reservoir is < PlanetEnvironmentHeatReservoir.Atmosphere or > PlanetEnvironmentHeatReservoir.SurfaceIce)
        {
            throw new ArgumentOutOfRangeException(nameof(reservoir), reservoir, "Unknown environmental heat reservoir.");
        }

        return (int)reservoir;
    }

    private static void ValidateEnergy(double joules)
    {
        if (!double.IsFinite(joules) || joules < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(joules), "Environmental heat must be finite and nonnegative.");
        }
    }

    private static double AddFinite(double first, double second)
    {
        var sum = first + second;
        if (!double.IsFinite(sum))
        {
            throw new ArgumentOutOfRangeException(nameof(second), "Accumulated environmental energy must remain finite.");
        }

        return sum;
    }
}

/// <summary>
/// Identifies where latent heat is deposited or withdrawn for one proposed
/// phase change. The physical process model must select a credible reservoir;
/// this contract never guesses it from the water phase name.
/// </summary>
public readonly record struct PlanetWaterPhaseHeatStep(
    PlanetWaterPhaseTransition Transition,
    PlanetEnvironmentHeatReservoir EnvironmentReservoir);

/// <summary>
/// A publication candidate for conserved water mass and its corresponding
/// environmental/latent energy change. The phase enthalpy is relative to the
/// initial water inventory; it may be negative for condensation of preexisting
/// atmospheric steam. This is not an absolute zero-K thermodynamic reference.
/// </summary>
public sealed class PlanetWaterEnvironmentalState
{
    public PlanetWaterEnvironmentalState(PlanetWaterInventory water, PlanetEnvironmentalEnergyBudget environment)
        : this(water, environment, 0.0, environment?.TotalEnergyJoules ?? throw new ArgumentNullException(nameof(environment)))
    {
    }

    private PlanetWaterEnvironmentalState(
        PlanetWaterInventory water, PlanetEnvironmentalEnergyBudget environment,
        double relativeLatentEnergyJoules, double initialCombinedEnergyJoules)
    {
        Water = water ?? throw new ArgumentNullException(nameof(water));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        RelativeLatentEnergyJoules = relativeLatentEnergyJoules;
        InitialCombinedEnergyJoules = initialCombinedEnergyJoules;
    }

    public PlanetWaterInventory Water { get; }

    public PlanetEnvironmentalEnergyBudget Environment { get; }

    public double RelativeLatentEnergyJoules { get; }

    public double InitialCombinedEnergyJoules { get; }

    public double EnergyConservationResidualJoules =>
        Environment.TotalEnergyJoules + RelativeLatentEnergyJoules - InitialCombinedEnergyJoules;

    /// <summary>
    /// Relocates existing sensible energy between two environmental stores.
    /// Used only as part of a coupled mass-transfer candidate. The source
    /// inventory is unchanged until the corresponding phase change succeeds.
    /// </summary>
    internal PlanetWaterEnvironmentalState TransferSensibleHeat(
        PlanetEnvironmentHeatReservoir source, PlanetEnvironmentHeatReservoir destination, double joules)
    {
        var relocated = Environment.TransferHeat(source, destination, joules);
        return new PlanetWaterEnvironmentalState(Water, relocated, RelativeLatentEnergyJoules, InitialCombinedEnergyJoules);
    }

    /// <summary>
    /// Replaces the environmental energy after an exchange across the
    /// environment's boundary (interior conduction, stellar input, infrared
    /// loss). <paramref name="netBoundaryInputJoules"/> is the signed energy
    /// that crossed the boundary, so this state's own phase-change residual
    /// stays closed; the owning ledger records where the energy came from.
    /// </summary>
    internal PlanetWaterEnvironmentalState WithBoundaryExchange(
        PlanetEnvironmentalEnergyBudget environment, double netBoundaryInputJoules)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var initial = InitialCombinedEnergyJoules + netBoundaryInputJoules;
        if (!double.IsFinite(initial))
        {
            throw new ArgumentOutOfRangeException(nameof(netBoundaryInputJoules), "Boundary energy exchange must remain finite.");
        }

        return new PlanetWaterEnvironmentalState(Water, environment, RelativeLatentEnergyJoules, initial);
    }

    /// <summary>
    /// Atomically changes water inventory and sensible energy for one matter
    /// crossing, without pretending the transfer is an evaporation/freezing
    /// reaction. Net energy is signed: arriving material credits the state,
    /// escaped material debits it, and interior outgassing credits this state
    /// only after the caller has debited the interior by the same amount.
    /// </summary>
    internal PlanetWaterEnvironmentalState WithMaterialExchange(
        PlanetWaterInventory water, PlanetEnvironmentalEnergyBudget environment, double netEnergyJoules)
    {
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(environment);
        if (!double.IsFinite(netEnergyJoules) || !double.IsFinite(InitialCombinedEnergyJoules + netEnergyJoules))
        {
            throw new ArgumentOutOfRangeException(nameof(netEnergyJoules), "Material enthalpy must remain finite.");
        }

        return new PlanetWaterEnvironmentalState(water, environment, RelativeLatentEnergyJoules,
            InitialCombinedEnergyJoules + netEnergyJoules);
    }

    public PlanetWaterEnvironmentalState Apply(
        IReadOnlyList<PlanetWaterPhaseHeatStep> steps, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        cancellationToken.ThrowIfCancellationRequested();
        var water = Water;
        var environment = Environment;
        var latent = RelativeLatentEnergyJoules;

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = step.Transition.Apply(water);
            var nextEnvironment = environment.ExchangeLatentHeat(
                step.EnvironmentReservoir, result.HeatAbsorbedFromEnvironmentJoules, result.HeatReleasedToEnvironmentJoules);
            var nextLatent = latent + result.NetHeatDemandJoules;
            if (!double.IsFinite(nextLatent))
            {
                throw new ArgumentOutOfRangeException(nameof(steps), "Cumulative relative latent energy must remain finite.");
            }

            water = result.Water;
            environment = nextEnvironment;
            latent = nextLatent;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetWaterEnvironmentalState(water, environment, latent, InitialCombinedEnergyJoules);
    }
}
