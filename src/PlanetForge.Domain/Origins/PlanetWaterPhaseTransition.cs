namespace PlanetForge.Domain.Origins;

/// <summary>
/// Material phase transitions that exchange latent heat with their surrounding
/// thermal reservoir. Rates and temperature/pressure eligibility come from
/// future validated physics, never from this mass/energy accounting component.
/// </summary>
public enum PlanetWaterPhaseTransitionKind
{
    Melt = 0,
    Freeze = 1,
    Evaporate = 2,
    Condense = 3,
    Sublimate = 4,
    Deposit = 5
}

/// <summary>
/// Separately records heat drawn from or released into the environment.
/// Latent heat is not assigned to the mantle by default: atmospheric, oceanic
/// and surface energy reservoirs must be coupled explicitly later.
/// </summary>
public sealed record PlanetWaterPhaseExchange(
    PlanetWaterInventory Water,
    double HeatAbsorbedFromEnvironmentJoules,
    double HeatReleasedToEnvironmentJoules)
{
    public double NetHeatDemandJoules => HeatAbsorbedFromEnvironmentJoules - HeatReleasedToEnvironmentJoules;
}

/// <summary>
/// A proposed transfer of an existing H2O mass between ice, liquid and vapour.
/// An explicit effective specific latent heat is supplied by the process model:
/// it is not assumed to be constant for all temperatures and pressures.
/// </summary>
public readonly record struct PlanetWaterPhaseTransition(
    PlanetWaterPhaseTransitionKind Kind,
    double MassKilograms,
    double SpecificLatentHeatJoulesPerKilogram)
{
    public PlanetWaterPhaseExchange Apply(PlanetWaterInventory water)
    {
        ArgumentNullException.ThrowIfNull(water);
        if (!double.IsFinite(MassKilograms) || MassKilograms < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MassKilograms), "Transferred water mass must be finite and nonnegative.");
        }

        if (!double.IsFinite(SpecificLatentHeatJoulesPerKilogram) || SpecificLatentHeatJoulesPerKilogram <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpecificLatentHeatJoulesPerKilogram),
                "Effective specific latent heat must be finite and positive.");
        }

        var (source, destination, absorbsHeat) = Kind switch
        {
            PlanetWaterPhaseTransitionKind.Melt =>
                (PlanetWaterReservoir.SurfaceIce, PlanetWaterReservoir.SurfaceLiquid, true),
            PlanetWaterPhaseTransitionKind.Freeze =>
                (PlanetWaterReservoir.SurfaceLiquid, PlanetWaterReservoir.SurfaceIce, false),
            PlanetWaterPhaseTransitionKind.Evaporate =>
                (PlanetWaterReservoir.SurfaceLiquid, PlanetWaterReservoir.AtmosphericVapor, true),
            PlanetWaterPhaseTransitionKind.Condense =>
                (PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceLiquid, false),
            PlanetWaterPhaseTransitionKind.Sublimate =>
                (PlanetWaterReservoir.SurfaceIce, PlanetWaterReservoir.AtmosphericVapor, true),
            PlanetWaterPhaseTransitionKind.Deposit =>
                (PlanetWaterReservoir.AtmosphericVapor, PlanetWaterReservoir.SurfaceIce, false),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported water phase transition.")
        };

        var latentHeatJoules = MassKilograms * SpecificLatentHeatJoulesPerKilogram;
        if (!double.IsFinite(latentHeatJoules))
        {
            throw new ArgumentOutOfRangeException(nameof(MassKilograms), "The required latent energy must remain finite.");
        }

        var nextWater = water.Transfer(source, destination, MassKilograms);
        return new PlanetWaterPhaseExchange(nextWater, absorbsHeat ? latentHeatJoules : 0.0,
            absorbsHeat ? 0.0 : latentHeatJoules);
    }
}

/// <summary>
/// Processes proposed phase transfers on immutable snapshots. All transfers
/// must succeed before the caller may publish the resulting water and energy
/// accounts together; cancellation never publishes a partial sequence.
/// </summary>
public static class PlanetWaterPhaseTransitionSequence
{
    public static PlanetWaterPhaseExchange Apply(
        PlanetWaterInventory water,
        IReadOnlyList<PlanetWaterPhaseTransition> transitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(transitions);
        cancellationToken.ThrowIfCancellationRequested();
        var currentWater = water;
        var totalAbsorbed = 0.0;
        var totalReleased = 0.0;

        foreach (var transition in transitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exchange = transition.Apply(currentWater);
            var nextAbsorbed = totalAbsorbed + exchange.HeatAbsorbedFromEnvironmentJoules;
            var nextReleased = totalReleased + exchange.HeatReleasedToEnvironmentJoules;
            if (!double.IsFinite(nextAbsorbed) || !double.IsFinite(nextReleased))
            {
                throw new ArgumentOutOfRangeException(nameof(transitions), "Cumulative latent heat must remain finite.");
            }

            currentWater = exchange.Water;
            totalAbsorbed = nextAbsorbed;
            totalReleased = nextReleased;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlanetWaterPhaseExchange(currentWater, totalAbsorbed, totalReleased);
    }
}
