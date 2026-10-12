namespace PlanetForge.Domain.Origins;

/// <summary>
/// One phase proposal. Its latent heat is assigned to the named environmental
/// store, while advected sensible energy moves from the water's origin store
/// (atmosphere for vapor, ocean for liquid, ice store for ice) to its
/// destination store. No rate or eligibility is inferred.
/// </summary>
public readonly record struct PlanetWaterPhaseSensibleHeatStep(
    PlanetWaterPhaseTransition Transition,
    PlanetEnvironmentHeatReservoir LatentHeatReservoir);

/// <summary>
/// Bounded, reversible-in-accounting coupling for water mass and sensible heat
/// across vapor, liquid and ice. Uses donor bulk temperature and a well-mixed
/// effective specific heat; the transferred sensible joules are credited
/// unchanged to the receiving reservoir. Subsequent temperatures reflect mixing
/// and the destination's different heat capacity. An equilibrium/phase enthalpy
/// solver must eventually account for variable cp and pressure-dependent
/// transitions.
/// </summary>
public static class PlanetWaterPhaseSensibleTransport
{
    public static PlanetWaterEnvironmentalState Apply(
        PlanetWaterEnvironmentalState initial,
        PlanetEnvironmentHeatCapacityParameters properties,
        IReadOnlyList<PlanetWaterPhaseSensibleHeatStep> steps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(steps);
        properties.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var current = initial;
        PlanetEnvironmentTemperatureModel.Resolve(current, properties);

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (source, destination, specificHeat) = Identify(step.Transition.Kind, properties);
            // Validate phase proposal and available source mass before moving
            // environmental energy; even an invalid step is never published.
            step.Transition.Apply(current.Water);

            if (step.Transition.MassKilograms > 0.0)
            {
                var temperatures = PlanetEnvironmentTemperatureModel.Resolve(current, properties);
                var sourceCapacity = temperatures.GetHeatCapacityJoulesPerKelvin(source);
                var donorEnergy = current.Environment.GetEnergyJoules(source);
                if (sourceCapacity <= 0.0)
                {
                    throw new InvalidOperationException("The donating phase must have a nonzero effective heat capacity.");
                }

                // Express heat as a fraction of the existing donor's energy,
                // avoiding overflow from multiplying Earth-scale mass by
                // specific heat and temperature separately.
                var movedHeatCapacity = step.Transition.MassKilograms * specificHeat;
                var sourceFraction = movedHeatCapacity / sourceCapacity;
                if (!double.IsFinite(sourceFraction) || sourceFraction < 0.0 || sourceFraction > 1.0 + 1e-12)
                {
                    throw new InvalidOperationException("The transferred water cannot exceed the donor's heat capacity.");
                }

                var energyToAdvect = donorEnergy * Math.Min(1.0, sourceFraction);
                if (!double.IsFinite(energyToAdvect))
                {
                    throw new InvalidOperationException("Advected sensible heat must remain finite.");
                }

                current = current.TransferSensibleHeat(source, destination, energyToAdvect);
            }

            current = current.Apply(
                [new PlanetWaterPhaseHeatStep(step.Transition, step.LatentHeatReservoir)], cancellationToken);
            // No ghost heat in an empty material reservoir after the step.
            PlanetEnvironmentTemperatureModel.Resolve(current, properties);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return current;
    }

    private static (PlanetEnvironmentHeatReservoir Source, PlanetEnvironmentHeatReservoir Destination, double SpecificHeat)
        Identify(PlanetWaterPhaseTransitionKind kind, PlanetEnvironmentHeatCapacityParameters properties) => kind switch
        {
            PlanetWaterPhaseTransitionKind.Condense =>
                (PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.SurfaceOcean,
                    properties.VaporSpecificHeatJoulesPerKilogramKelvin),
            PlanetWaterPhaseTransitionKind.Evaporate =>
                (PlanetEnvironmentHeatReservoir.SurfaceOcean, PlanetEnvironmentHeatReservoir.Atmosphere,
                    properties.LiquidWaterSpecificHeatJoulesPerKilogramKelvin),
            PlanetWaterPhaseTransitionKind.Freeze =>
                (PlanetEnvironmentHeatReservoir.SurfaceOcean, PlanetEnvironmentHeatReservoir.SurfaceIce,
                    properties.LiquidWaterSpecificHeatJoulesPerKilogramKelvin),
            PlanetWaterPhaseTransitionKind.Melt =>
                (PlanetEnvironmentHeatReservoir.SurfaceIce, PlanetEnvironmentHeatReservoir.SurfaceOcean,
                    properties.IceSpecificHeatJoulesPerKilogramKelvin),
            PlanetWaterPhaseTransitionKind.Sublimate =>
                (PlanetEnvironmentHeatReservoir.SurfaceIce, PlanetEnvironmentHeatReservoir.Atmosphere,
                    properties.IceSpecificHeatJoulesPerKilogramKelvin),
            PlanetWaterPhaseTransitionKind.Deposit =>
                (PlanetEnvironmentHeatReservoir.Atmosphere, PlanetEnvironmentHeatReservoir.SurfaceIce,
                    properties.VaporSpecificHeatJoulesPerKilogramKelvin),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported water phase transition.")
        };
}
