namespace PlanetForge.Domain.Origins;

/// <summary>
/// Heat leaving the interior and arriving in a named environmental store
/// (conduction through crust, convection at a magma-ocean surface, hydrothermal
/// exchange). This is an <b>internal</b> transfer: the interior is debited and
/// the environment credited by exactly the same joules. Loss to space only
/// happens through the radiative boundary of the environment.
/// </summary>
public readonly record struct PlanetInteriorHeatExport(
    double Watts,
    PlanetEnvironmentHeatReservoir Destination);

/// <summary>
/// External radiation for one interval: absorbed starlight enters one store and
/// infrared emission leaves one store. Powers must come from a radiation model
/// evaluated for this interval (see <see cref="PlanetRadiationBalanceModel"/>).
/// </summary>
public readonly record struct PlanetRadiativeExchange(
    PlanetRadiativeBalance Balance,
    PlanetEnvironmentHeatReservoir AbsorbingReservoir,
    PlanetEnvironmentHeatReservoir EmittingReservoir);

/// <summary>
/// Inputs for one coupled energy interval. Rates are supplied by process
/// models; this ledger only integrates and checks them. Interior heat that
/// leaves the planet must first pass through the environment.
/// </summary>
public sealed record PlanetOriginEnergyStep(
    long ElapsedYears,
    double ImpactHeatingWatts,
    double RadiogenicHeatingWatts,
    PlanetInteriorHeatExport InteriorExport,
    PlanetRadiativeExchange Radiation,
    IReadOnlyList<PlanetWaterPhaseSensibleHeatStep> PhaseChanges)
{
    /// <summary>
    /// Explicit high-resolution interval constructor. Legacy whole-year
    /// steps keep their existing constructor and semantics.
    /// </summary>
    public PlanetOriginEnergyStep(
        PlanetOriginDuration duration,
        double impactHeatingWatts,
        double radiogenicHeatingWatts,
        PlanetInteriorHeatExport interiorExport,
        PlanetRadiativeExchange radiation,
        IReadOnlyList<PlanetWaterPhaseSensibleHeatStep> phaseChanges)
        : this(0, impactHeatingWatts, radiogenicHeatingWatts, interiorExport, radiation, phaseChanges)
    {
        ExplicitDuration = duration;
    }

    public PlanetOriginDuration? ExplicitDuration { get; init; }

    public PlanetOriginDuration Duration
    {
        get
        {
            if (ExplicitDuration.HasValue && ElapsedYears != 0)
            {
                throw new InvalidOperationException("An energy step cannot specify both elapsed whole years and an explicit duration.");
            }

            return ExplicitDuration ?? PlanetOriginDuration.FromYears(ElapsedYears);
        }
    }
}

/// <summary>
/// One closed energy account for the whole planet: interior, environmental
/// sensible stores and the relative latent enthalpy of water, with the
/// absolute geological clock. The only external terms are impact and
/// radiogenic heating, absorbed starlight and emitted infrared:
/// <code>
/// stored(now) = stored(initial) + impact + radiogenic + stellar - infrared
/// </code>
/// Within one step the operators are applied in a fixed order (interior
/// sources and export, radiation, then phase changes). That is first-order
/// operator splitting: callers must keep steps short enough that the state
/// does not cross a phase or saturation threshold inside one step.
/// Any failure or cancellation leaves the previous ledger unchanged.
/// </summary>
public sealed class PlanetOriginEnergyLedger
{
    public PlanetOriginEnergyLedger(
        PlanetOriginThermalTimeline interior,
        PlanetWaterEnvironmentalState environment,
        PlanetEnvironmentHeatCapacityParameters heatCapacities)
    {
        Interior = interior ?? throw new ArgumentNullException(nameof(interior));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        heatCapacities.Validate();
        HeatCapacities = heatCapacities;
        // Reject an initial state with heat in an empty material store.
        PlanetEnvironmentTemperatureModel.Resolve(environment, heatCapacities);
        InitialStoredEnergyJoules = StoredEnergyJoules;
    }

    private PlanetOriginEnergyLedger(
        PlanetOriginThermalTimeline interior,
        PlanetWaterEnvironmentalState environment,
        PlanetEnvironmentHeatCapacityParameters heatCapacities,
        double initialStoredEnergyJoules,
        double absorbedStellarJoules,
        double emittedInfraredJoules,
        double exportedInteriorJoules)
    {
        Interior = interior;
        Environment = environment;
        HeatCapacities = heatCapacities;
        InitialStoredEnergyJoules = initialStoredEnergyJoules;
        CumulativeAbsorbedStellarEnergyJoules = absorbedStellarJoules;
        CumulativeEmittedInfraredEnergyJoules = emittedInfraredJoules;
        CumulativeInteriorExportJoules = exportedInteriorJoules;
    }

    public PlanetOriginThermalTimeline Interior { get; }

    public PlanetWaterEnvironmentalState Environment { get; }

    public PlanetEnvironmentHeatCapacityParameters HeatCapacities { get; }

    public PlanetOriginClock Clock => Interior.Clock;

    public double InitialStoredEnergyJoules { get; }

    public double CumulativeAbsorbedStellarEnergyJoules { get; }

    public double CumulativeEmittedInfraredEnergyJoules { get; }

    /// <summary>Internal interior→environment transfer; not an external term.</summary>
    public double CumulativeInteriorExportJoules { get; }

    public double StoredEnergyJoules => Interior.EnergyBudget.StoredEnergyJoules +
        Environment.Environment.TotalEnergyJoules + Environment.RelativeLatentEnergyJoules;

    public double ConservationResidualJoules => InitialStoredEnergyJoules +
        Interior.EnergyBudget.CumulativeImpactHeatJoules + Interior.EnergyBudget.CumulativeRadiogenicHeatJoules +
        CumulativeAbsorbedStellarEnergyJoules - CumulativeEmittedInfraredEnergyJoules - StoredEnergyJoules;

    public PlanetEnvironmentTemperatureSnapshot Temperatures =>
        PlanetEnvironmentTemperatureModel.Resolve(Environment, HeatCapacities);

    public PlanetOriginEnergyLedger Advance(PlanetOriginEnergyStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(step.PhaseChanges);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Interior: sources in, exported heat out (the interior ledger
        //    records the export as its surface loss).
        var duration = step.Duration;
        var nextInterior = Interior.Advance(duration, new PlanetThermalEnergyFlux(
            step.ImpactHeatingWatts, step.RadiogenicHeatingWatts, step.InteriorExport.Watts));
        var exportedJoules = nextInterior.EnergyBudget.CumulativeSurfaceHeatLossJoules -
            Interior.EnergyBudget.CumulativeSurfaceHeatLossJoules;

        // 2. The same joules arrive in the environment.
        var environmentBudget = Environment.Environment.Deposit(step.InteriorExport.Destination, exportedJoules);

        // 3. External radiation over the same physical interval.
        var seconds = duration.TotalSeconds;
        var absorbed = RequireEnergy(step.Radiation.Balance.AbsorbedStellarPowerWatts * seconds, "absorbed starlight");
        var emitted = RequireEnergy(step.Radiation.Balance.EmittedThermalPowerWatts * seconds, "emitted infrared");
        environmentBudget = environmentBudget.ApplyExternalRadiation(
            step.Radiation.AbsorbingReservoir, absorbed, step.Radiation.EmittingReservoir, emitted);

        var boundaryNet = exportedJoules + absorbed - emitted;
        var nextEnvironment = Environment.WithBoundaryExchange(environmentBudget, boundaryNet);
        // Heat must not be deposited in a store that holds no matter.
        PlanetEnvironmentTemperatureModel.Resolve(nextEnvironment, HeatCapacities);

        // 4. Phase changes move mass, sensible and latent heat internally.
        nextEnvironment = PlanetWaterPhaseSensibleTransport.Apply(
            nextEnvironment, HeatCapacities, step.PhaseChanges, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetOriginEnergyLedger(nextInterior, nextEnvironment, HeatCapacities, InitialStoredEnergyJoules,
            RequireEnergy(CumulativeAbsorbedStellarEnergyJoules + absorbed, "cumulative starlight"),
            RequireEnergy(CumulativeEmittedInfraredEnergyJoules + emitted, "cumulative infrared"),
            RequireEnergy(CumulativeInteriorExportJoules + exportedJoules, "cumulative interior export"));
    }

    private static double RequireEnergy(double joules, string name)
    {
        if (!double.IsFinite(joules) || joules < 0.0)
        {
            throw new ArgumentOutOfRangeException(name, "Integrated energy must be finite and nonnegative.");
        }

        return joules;
    }
}
