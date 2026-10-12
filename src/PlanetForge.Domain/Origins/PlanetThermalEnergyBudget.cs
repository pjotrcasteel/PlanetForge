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
    public void Validate()
    {
        ValidatePower(ImpactHeatingWatts, nameof(ImpactHeatingWatts));
        ValidatePower(RadiogenicHeatingWatts, nameof(RadiogenicHeatingWatts));
        ValidatePower(SurfaceHeatLossWatts, nameof(SurfaceHeatLossWatts));
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
        double cumulativeSurfaceHeatLossJoules)
    {
        InitialStoredEnergyJoules = initialStoredEnergyJoules;
        StoredEnergyJoules = storedEnergyJoules;
        CumulativeImpactHeatJoules = cumulativeImpactHeatJoules;
        CumulativeRadiogenicHeatJoules = cumulativeRadiogenicHeatJoules;
        CumulativeSurfaceHeatLossJoules = cumulativeSurfaceHeatLossJoules;
    }

    public double InitialStoredEnergyJoules { get; }

    public double StoredEnergyJoules { get; }

    public double CumulativeImpactHeatJoules { get; }

    public double CumulativeRadiogenicHeatJoules { get; }

    public double CumulativeSurfaceHeatLossJoules { get; }

    public double ConservationResidualJoules => InitialStoredEnergyJoules + CumulativeImpactHeatJoules +
        CumulativeRadiogenicHeatJoules - CumulativeSurfaceHeatLossJoules - StoredEnergyJoules;

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
        ValidateEnergy(impactJoules, nameof(flux.ImpactHeatingWatts));
        ValidateEnergy(radiogenicJoules, nameof(flux.RadiogenicHeatingWatts));
        ValidateEnergy(coolingJoules, nameof(flux.SurfaceHeatLossWatts));

        var energyAvailable = StoredEnergyJoules + impactJoules + radiogenicJoules;
        if (!double.IsFinite(energyAvailable) || coolingJoules > energyAvailable)
        {
            throw new InvalidOperationException("The thermal step is inconsistent: heat loss exceeds available stored and deposited energy.");
        }

        var storedEnergy = energyAvailable - coolingJoules;
        var cumulativeImpacts = CumulativeImpactHeatJoules + impactJoules;
        var cumulativeRadiogenic = CumulativeRadiogenicHeatJoules + radiogenicJoules;
        var cumulativeCooling = CumulativeSurfaceHeatLossJoules + coolingJoules;
        ValidateEnergy(storedEnergy, nameof(storedEnergy));
        ValidateEnergy(cumulativeImpacts, nameof(cumulativeImpacts));
        ValidateEnergy(cumulativeRadiogenic, nameof(cumulativeRadiogenic));
        ValidateEnergy(cumulativeCooling, nameof(cumulativeCooling));

        return new PlanetThermalEnergyBudget(InitialStoredEnergyJoules,
            storedEnergy, cumulativeImpacts, cumulativeRadiogenic, cumulativeCooling);
    }

    private static void ValidateEnergy(double joules, string parameterName)
    {
        if (!double.IsFinite(joules) || joules < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Energy must be finite and nonnegative.");
        }
    }
}
