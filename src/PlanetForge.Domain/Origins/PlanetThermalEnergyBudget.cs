namespace PlanetForge.Domain.Origins;

/// <summary>
/// Net power entering and leaving a planetary internal-energy reservoir.
/// Rates are explicit model outputs; this ledger does not invent accretion,
/// radioactive-decay, surface-temperature or melt-fraction physics.
/// </summary>
public readonly record struct PlanetThermalEnergyFlux(
    double ImpactHeatingWatts,
    double RadiogenicHeatingWatts,
    double SurfaceHeatLossWatts)
{
    /// <summary>Heat returned from an explicitly debited environmental store; not external heating.</summary>
    public double SurfaceHeatGainWatts { get; init; }

    public void Validate()
    {
        ValidatePower(ImpactHeatingWatts, nameof(ImpactHeatingWatts));
        ValidatePower(RadiogenicHeatingWatts, nameof(RadiogenicHeatingWatts));
        ValidatePower(SurfaceHeatLossWatts, nameof(SurfaceHeatLossWatts));
        ValidatePower(SurfaceHeatGainWatts, nameof(SurfaceHeatGainWatts));
    }

    private static void ValidatePower(double watts, string parameterName)
    {
        if (!double.IsFinite(watts) || watts < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Heat flow must be finite and nonnegative.");
        }
    }
}

/// <summary>
/// An immutable, conserved energy ledger for early-Earth thermal evolution.
/// Latent heat, mantle differentiation and radioactive-decay rates must be
/// supplied by future calibrated models; no temperature can be inferred
/// directly from this aggregate energy without material heat capacities.
/// </summary>
public sealed class PlanetThermalEnergyBudget
{
    public const double JulianYearSeconds = 31_557_600.0;

    public PlanetThermalEnergyBudget(double initialStoredEnergyJoules)
    {
        ValidateEnergy(initialStoredEnergyJoules, nameof(initialStoredEnergyJoules));
        InitialStoredEnergyJoules = initialStoredEnergyJoules;
        StoredEnergyJoules = initialStoredEnergyJoules;
    }

    private PlanetThermalEnergyBudget(
        double initialStoredEnergyJoules,
        double storedEnergyJoules,
        double cumulativeImpactHeatJoules,
        double cumulativeRadiogenicHeatJoules,
        double cumulativeSurfaceHeatLossJoules,
        double cumulativeMaterialInputJoules,
        double cumulativeMaterialExportJoules)
    {
        InitialStoredEnergyJoules = initialStoredEnergyJoules;
        StoredEnergyJoules = storedEnergyJoules;
        CumulativeImpactHeatJoules = cumulativeImpactHeatJoules;
        CumulativeRadiogenicHeatJoules = cumulativeRadiogenicHeatJoules;
        CumulativeSurfaceHeatLossJoules = cumulativeSurfaceHeatLossJoules;
        CumulativeMaterialInputJoules = cumulativeMaterialInputJoules;
        CumulativeMaterialExportJoules = cumulativeMaterialExportJoules;
    }

    private double StoredRoundingCorrection { get; init; }

    public double InitialStoredEnergyJoules { get; }

    public double StoredEnergyJoules { get; }

    public double CumulativeImpactHeatJoules { get; }

    public double CumulativeRadiogenicHeatJoules { get; }

    public double CumulativeSurfaceHeatLossJoules { get; }

    /// <summary>Internal heat returned by the environment; distinct from material delivery and impacts.</summary>
    public double CumulativeSurfaceHeatGainJoules { get; private init; }

    /// <summary>External enthalpy carried into interior-bound volatile material, distinct from impact heating.</summary>
    public double CumulativeMaterialInputJoules { get; }

    /// <summary>Material enthalpy transferred from the interior to the environmental atmosphere.</summary>
    public double CumulativeMaterialExportJoules { get; }

    public double ConservationResidualJoules => InitialStoredEnergyJoules + CumulativeImpactHeatJoules +
        CumulativeRadiogenicHeatJoules + CumulativeMaterialInputJoules + CumulativeSurfaceHeatGainJoules -
        CumulativeSurfaceHeatLossJoules - CumulativeMaterialExportJoules - StoredEnergyJoules;

    public PlanetThermalEnergyBudget Advance(double elapsedYears, PlanetThermalEnergyFlux flux)
    {
        if (!double.IsFinite(elapsedYears) || elapsedYears < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedYears), "Elapsed years must be finite and nonnegative.");
        }

        return AdvanceSeconds(elapsedYears * JulianYearSeconds, flux);
    }

    /// <summary>Integrates validated thermal power over an explicit physical second interval.</summary>
    public PlanetThermalEnergyBudget AdvanceSeconds(double elapsedSeconds, PlanetThermalEnergyFlux flux)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed physical seconds must be finite and nonnegative.");
        }

        flux.Validate();
        var durationSeconds = elapsedSeconds;
        var impactJoules = flux.ImpactHeatingWatts * durationSeconds;
        var radiogenicJoules = flux.RadiogenicHeatingWatts * durationSeconds;
        var coolingJoules = flux.SurfaceHeatLossWatts * durationSeconds;
        var returnedJoules = flux.SurfaceHeatGainWatts * durationSeconds;
        ValidateEnergy(returnedJoules, nameof(flux.SurfaceHeatGainWatts));
        ValidateEnergy(impactJoules, nameof(flux.ImpactHeatingWatts));
        ValidateEnergy(radiogenicJoules, nameof(flux.RadiogenicHeatingWatts));
        ValidateEnergy(coolingJoules, nameof(flux.SurfaceHeatLossWatts));

        var energyAvailable = StoredEnergyJoules + impactJoules + radiogenicJoules + returnedJoules;
        if (!double.IsFinite(energyAvailable) || coolingJoules > energyAvailable)
        {
            throw new InvalidOperationException("The thermal step is inconsistent: heat loss exceeds available stored and deposited energy.");
        }

        var correctedChange = (impactJoules + radiogenicJoules + returnedJoules - coolingJoules) - StoredRoundingCorrection;
        var storedEnergy = StoredEnergyJoules + correctedChange;
        var correction = (storedEnergy - StoredEnergyJoules) - correctedChange;
        var cumulativeImpacts = CumulativeImpactHeatJoules + impactJoules;
        var cumulativeRadiogenic = CumulativeRadiogenicHeatJoules + radiogenicJoules;
        var cumulativeCooling = CumulativeSurfaceHeatLossJoules + coolingJoules;
        var cumulativeReturned = CumulativeSurfaceHeatGainJoules + returnedJoules;
        ValidateEnergy(cumulativeReturned, nameof(cumulativeReturned));
        ValidateEnergy(storedEnergy, nameof(storedEnergy));
        ValidateEnergy(cumulativeImpacts, nameof(cumulativeImpacts));
        ValidateEnergy(cumulativeRadiogenic, nameof(cumulativeRadiogenic));
        ValidateEnergy(cumulativeCooling, nameof(cumulativeCooling));

        return new PlanetThermalEnergyBudget(InitialStoredEnergyJoules,
            storedEnergy, cumulativeImpacts, cumulativeRadiogenic, cumulativeCooling,
            CumulativeMaterialInputJoules, CumulativeMaterialExportJoules)
        {
            CumulativeSurfaceHeatGainJoules = cumulativeReturned,
            StoredRoundingCorrection = correction
        };
    }

    /// <summary>
    /// Atomic material enthalpy change. Incoming energy must accompany documented
    /// externally delivered material; exported energy must be credited to another
    /// planetary reservoir by the caller. No new heat is generated by degassing.
    /// </summary>
    internal PlanetThermalEnergyBudget ExchangeMaterialEnthalpy(double incomingJoules, double exportedJoules)
    {
        ValidateEnergy(incomingJoules, nameof(incomingJoules));
        ValidateEnergy(exportedJoules, nameof(exportedJoules));
        var available = StoredEnergyJoules + incomingJoules;
        if (!double.IsFinite(available) || exportedJoules > available)
        {
            throw new InvalidOperationException("Volatile material enthalpy exceeds available interior heat.");
        }

        var correctedChange = (incomingJoules - exportedJoules) - StoredRoundingCorrection;
        var stored = StoredEnergyJoules + correctedChange;
        var correction = (stored - StoredEnergyJoules) - correctedChange;
        var delivered = CumulativeMaterialInputJoules + incomingJoules;
        var exported = CumulativeMaterialExportJoules + exportedJoules;
        ValidateEnergy(stored, nameof(stored));
        ValidateEnergy(delivered, nameof(delivered));
        ValidateEnergy(exported, nameof(exported));
        return new PlanetThermalEnergyBudget(InitialStoredEnergyJoules, stored, CumulativeImpactHeatJoules,
            CumulativeRadiogenicHeatJoules, CumulativeSurfaceHeatLossJoules, delivered, exported)
        {
            CumulativeSurfaceHeatGainJoules = CumulativeSurfaceHeatGainJoules,
            StoredRoundingCorrection = correction
        };
    }

    private static void ValidateEnergy(double joules, string parameterName)
    {
        if (!double.IsFinite(joules) || joules < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Energy must be finite and nonnegative.");
        }
    }
}
