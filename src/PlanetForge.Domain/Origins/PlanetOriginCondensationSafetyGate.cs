namespace PlanetForge.Domain.Origins;

/// <summary>
/// Explicit assumptions for an opt-in liquid-vapour proposal safety check.
/// Only use when a single well-mixed bulk atmospheric temperature is an
/// accepted proxy for the effective condensation layer. The caller must
/// supply its own justified ideal-mixture pressure and timestep envelopes.
/// This is NOT a precipitation kinetics or Hadean greenhouse model.
/// </summary>
public sealed record PlanetOriginCondensationSafetyConditions(
    PlanetMixedAtmosphereParameters Atmosphere,
    double MaximumValidatedSurfacePressurePascals,
    PlanetOriginDuration MaximumPhysicalSubstep)
{
    public void Validate(PlanetEnvironmentHeatCapacityParameters thermalProperties)
    {
        Atmosphere.Validate();
        thermalProperties.Validate();
        if (!double.IsFinite(MaximumValidatedSurfacePressurePascals) || MaximumValidatedSurfacePressurePascals <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumValidatedSurfacePressurePascals),
                "An evidence-backed positive pressure limit is required for ideal-mixture phase checks.");
        }

        if (MaximumPhysicalSubstep.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPhysicalSubstep),
                "Condensation checks require a nonzero, explicitly bounded physical timestep.");
        }

        // Keep the hydrostatic column and the effective heat capacity on the
        // exact same gas inventory. A different dry-gas mass would silently
        // evaluate pressure and temperature for two different atmospheres.
        if (Atmosphere.DryGasMassKilograms != thermalProperties.NoncondensableAtmosphereMassKilograms)
        {
            throw new InvalidOperationException(
                "The atmospheric gas mass must match the coupled environmental heat-capacity parameters.");
        }
    }
}

/// <summary>
/// Rejects an explicitly proposed condensation that lies outside the simple
/// mixed-atmosphere, well-mixed-temperature ideal-gas assumptions. An initial
/// supersaturation is necessary but not sufficient for precipitation. The
/// release is checked again *after* the full latent/sensible heat change,
/// because condensation can warm the atmosphere and erase supersaturation.
/// No condensation is ever invented or automatically applied here.
/// </summary>
public static class PlanetOriginCondensationSafetyGate
{
    public static double VerifyProposals(
        PlanetWaterEnvironmentalState prePhase,
        PlanetEnvironmentHeatCapacityParameters thermalProperties,
        PlanetOriginDuration duration,
        IReadOnlyList<PlanetWaterPhaseSensibleHeatStep> proposedPhases,
        PlanetOriginCondensationSafetyConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(prePhase);
        ArgumentNullException.ThrowIfNull(proposedPhases);
        ArgumentNullException.ThrowIfNull(conditions);
        conditions.Validate(thermalProperties);

        if (Compare(duration, conditions.MaximumPhysicalSubstep) > 0)
        {
            throw new InvalidOperationException("Condensation substep exceeds its explicitly validated physical timestep.");
        }

        var proposedMass = 0.0;
        foreach (var proposal in proposedPhases)
        {
            if (proposal.Transition.Kind != PlanetWaterPhaseTransitionKind.Condense)
            {
                throw new InvalidOperationException(
                    "The idealized condensation safety check cannot validate simultaneous different phase reactions.");
            }

            var mass = proposal.Transition.MassKilograms;
            if (!double.IsFinite(mass) || mass < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(proposedPhases), "Condensation mass must be finite and nonnegative.");
            }

            proposedMass += mass;
            if (!double.IsFinite(proposedMass))
            {
                throw new ArgumentOutOfRangeException(nameof(proposedPhases), "Combined condensation mass must remain finite.");
            }
        }

        if (proposedMass == 0.0) return 0.0;

        var before = EvaluateBounded(prePhase, thermalProperties, conditions);
        var available = before.PotentialCondensateMassKilograms!.Value;
        var tolerance = Math.Max(1e-12 * prePhase.Water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor), 1e-12);
        if (proposedMass > available + tolerance)
        {
            throw new InvalidOperationException(
                "Proposed condensation exceeds the supersaturated water mass in the validated atmospheric column.");
        }

        return available;
    }

    public static void VerifyResult(
        PlanetWaterEnvironmentalState postPhase,
        PlanetEnvironmentHeatCapacityParameters thermalProperties,
        PlanetOriginCondensationSafetyConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(postPhase);
        ArgumentNullException.ThrowIfNull(conditions);

        var after = EvaluateBounded(postPhase, thermalProperties, conditions);
        var saturation = after.WaterSaturationPressurePascals!.Value;
        // An excess-condensation proposal can heat the recipient environment
        // and make the remaining atmosphere undersaturated. Such a proposal
        // requires a coupled thermodynamic re-evaluation, not publication.
        if (after.WaterVaporPartialPressurePascals + saturation * 1e-10 < saturation)
        {
            throw new InvalidOperationException(
                "Condensation would leave the effective atmospheric layer undersaturated after sensible/latent heat redistribution.");
        }
    }

    private static PlanetMixedAtmosphereSaturationSnapshot EvaluateBounded(
        PlanetWaterEnvironmentalState state,
        PlanetEnvironmentHeatCapacityParameters thermalProperties,
        PlanetOriginCondensationSafetyConditions conditions)
    {
        conditions.Validate(thermalProperties);
        var temperature = PlanetEnvironmentTemperatureModel.Resolve(state, thermalProperties).AtmosphereTemperatureKelvin;
        if (temperature is null)
        {
            throw new InvalidOperationException("Condensation requires a populated, temperature-defined atmosphere.");
        }

        var diagnostic = PlanetMixedAtmosphereSaturation.Evaluate(state.Water, conditions.Atmosphere, temperature.Value);
        if (diagnostic.TotalSurfacePressurePascals > conditions.MaximumValidatedSurfacePressurePascals)
        {
            throw new InvalidOperationException(
                "Atmospheric pressure exceeds the caller-validated ideal-mixture thermodynamic envelope.");
        }

        if (diagnostic.Regime != PlanetLiquidVaporSaturationRegime.LiquidVaporIapws ||
            diagnostic.WaterSaturationPressurePascals is null || diagnostic.PotentialCondensateMassKilograms is null)
        {
            throw new InvalidOperationException(
                "This atmospheric state is outside the validated liquid-vapour saturation-temperature regime.");
        }

        return diagnostic;
    }

    private static int Compare(PlanetOriginDuration left, PlanetOriginDuration right)
    {
        var years = left.WholeYears.CompareTo(right.WholeYears);
        return years == 0 ? left.RemainingMicroseconds.CompareTo(right.RemainingMicroseconds) : years;
    }
}
