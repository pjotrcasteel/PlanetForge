using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

/// <summary>
/// Science-only visual acceptance of regional sensible + fusion enthalpy
/// on the SAME canonical planet. The thermal contrast is a declared,
/// hypothetical initial condition with exactly conserved total rock heat.
/// It is not an early-Earth reconstruction, simulated ancient impact or
/// a primordial crust or mantle-plume geography model.
/// </summary>
public static class PlanetRegionalCoolingReference
{
    public const int AdvanceSeconds = 3_600;
    public const int SubstepSeconds = 300;
    public const double InitialUniformTemperatureKelvin = 1_600.0;
    private const double SurfaceRockMassKilograms = 1.0e19;
    private static readonly PlanetOriginSurfaceRockPhaseMaterial Material =
        new(850.0, 400_000.0, 1_300.0, 1_900.0);

    public static PlanetOriginCanonicalRockCells CreateInitial(double planetRadiusMeters)
    {
        var mantle = new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(
            4.0e24, 1_200.0, 400_000.0, 1_300.0, 1_900.0));
        var interior = new PlanetOriginThermalTimeline(PlanetOriginClock.EarthAccretionReference,
            new PlanetThermalEnergyBudget(mantle.CalculateStoredEnergyJoules(1_750.0)), mantle);
        var rockEnergy = new PlanetRockEnthalpyModel(Material.ToRockProperties(SurfaceRockMassKilograms))
            .CalculateStoredEnergyJoules(InitialUniformTemperatureKelvin);
        var environment = new PlanetWaterEnvironmentalState(PlanetWaterInventory.Empty,
            new PlanetEnvironmentalEnergyBudget(0.0, 0.0, rockEnergy));
        var capacities = new PlanetEnvironmentHeatCapacityParameters(
            0.0, 1_000.0, 2_000.0, 4_200.0, SurfaceRockMassKilograms,
            Material.SpecificHeatCapacityJoulesPerKilogramKelvin) { SurfaceRockPhaseMaterial = Material };
        var ledger = new PlanetOriginEnergyLedger(interior, environment, capacities);
        var baseField = PlanetOriginSurfaceRockPhaseField.FromCanonicalPhaseSurface(
            ledger, new PlanetSurfaceGridLayout(3), planetRadiusMeters);
        var hotspot = new PlanetVector(0.1, 0.2, 0.9746794344808963);
        var field = baseField.RedistributeInitialEnergy(cell =>
        {
            var center = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id);
            return Math.Pow(Math.Max(0.0, PlanetVector.Dot(center, hotspot)), 16.0) *
                cell.ActiveRockMassKilograms;
        }, rockEnergy * 0.012);

        return new PlanetOriginCanonicalRockCells(new PlanetOriginEpochState(ledger), field);
    }

    public static PlanetOriginCanonicalRockCells Advance(
        PlanetOriginCanonicalRockCells current, PlanetRenderSnapshot world,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(world);
        var parameters = new PlanetOriginRegionalAirlessParameters(
            new PlanetVector(1.0, 0.0, 0.0),
            world.Physics.SolarFluxWattsPerSquareMeter,
            0.15, 0.90, 0.0, 25.0);
        var candidate = current;
        for (var seconds = 0; seconds < AdvanceSeconds; seconds += SubstepSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidate = candidate.Advance(PlanetOriginDuration.FromSeconds(SubstepSeconds),
                parameters, cancellationToken);
        }

        return candidate;
    }
}
