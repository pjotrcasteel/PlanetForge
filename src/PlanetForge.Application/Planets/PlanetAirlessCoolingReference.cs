using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Origins;

namespace PlanetForge.Application.Planets;

/// <summary>
/// Explicit, dry and atmosphere-free thermal reference for checking the ONE
/// canonical planet's cooling, rendering, clock and mobile interaction.
/// This is not a reconstructed Hadean atmosphere, magma-ocean crust model
/// or a default playable Earth-origin scenario.
/// </summary>
public static class PlanetAirlessCoolingReference
{
    public const int AdvanceSeconds = 3_600;
    public const double InitialInteriorTemperatureKelvin = 1_750.0;
    public const double InitialSurfaceTemperatureKelvin = 1_600.0;

    private const double ActiveMantleMassKilograms = 4.0e24;
    private const double ActiveSurfaceRockMassKilograms = 1.0e19;
    private const double SurfaceRockHeatCapacity = 850.0;
    private const double SurfaceInfraredEmissivity = 0.90;
    private const double AssumedBondAlbedo = 0.15;

    public static PlanetOriginEpochState CreateInitial()
    {
        var rock = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            ActiveMantleMassKilograms, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var timeline = new PlanetOriginThermalTimeline(
            PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(rock.CalculateStoredEnergyJoules(InitialInteriorTemperatureKelvin)),
            rock);
        var environment = new PlanetWaterEnvironmentalState(
            PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(
                0.0, 0.0, ActiveSurfaceRockMassKilograms * SurfaceRockHeatCapacity * InitialSurfaceTemperatureKelvin));
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, ActiveSurfaceRockMassKilograms, SurfaceRockHeatCapacity);

        return new PlanetOriginEpochState(new PlanetOriginEnergyLedger(timeline, environment, capacities));
    }

    public static PlanetRenderSnapshot Advance(PlanetExperience planet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planet);
        if (planet.PublishedOriginEpoch?.CoupledEnergy is not { } energy ||
            energy.HeatCapacities.NoncondensableAtmosphereMassKilograms != 0.0 ||
            energy.Environment.Water.PlanetaryWaterMassKilograms != 0.0)
        {
            throw new InvalidOperationException("The airless cooling reference requires its dry, atmosphere-free coupled world.");
        }

        var world = planet.CreateSnapshot();
        var parameters = new PlanetOriginAirlessThermalParameters(
            new PlanetOriginAirlessRadiationParameters(
                world.PhysicalParameters.RadiusMeters,
                world.Physics.SolarFluxWattsPerSquareMeter,
                AssumedBondAlbedo,
                SurfaceInfraredEmissivity),
            new PlanetOriginRockConductionParameters(3.0, 25.0));
        var options = new PlanetOriginAdaptiveIntegrationOptions(
            PlanetOriginDuration.FromSeconds(60.0),
            PlanetOriginDuration.FromSeconds(1.0),
            MaximumAcceptedSubsteps: 512);

        return planet.AdvanceOriginEpochAdaptive(
            PlanetOriginDuration.FromSeconds(AdvanceSeconds), options,
            (candidate, duration) => PlanetOriginAirlessThermalProcess.Propose(candidate, duration, parameters),
            cancellationToken);
    }
}
