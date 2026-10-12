using PlanetForge.Domain.Origins;

namespace PlanetForge.Application.Rendering;

/// <summary>
/// A read-only view of one conserved Earth-origin epoch on the main planet.
/// Bulk interior melt is not a spatial crust or lava coverage fraction.
/// </summary>
public sealed record PlanetOriginSurfaceSnapshot(
    long Revision,
    long YearsBeforePresent,
    double BulkRockTemperatureKelvin,
    double BulkRockMeltFraction,
    double? NearSurfaceRockTemperatureKelvin,
    double? AtmosphereTemperatureKelvin,
    double AtmosphericSteamMassKilograms,
    double SurfaceLiquidMassKilograms,
    double SurfaceIceMassKilograms,
    double DryGasMassKilograms,
    double SurfacePressurePascals,
    double SurfaceWaterLiquidFraction,
    double SurfaceWaterVaporFraction,
    double SurfaceWaterIceFraction);

/// <summary>
/// Pure projection of one atomic geological epoch. This creates no water,
/// transfers no energy and cannot propose any phase transition.
/// </summary>
public static class PlanetOriginSurfaceProjection
{
    public static PlanetOriginSurfaceSnapshot Create(
        PlanetOriginEpochState epoch, double radiusMeters, double surfaceGravityMetersPerSecondSquared)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (epoch.CoupledEnergy is null)
        {
            throw new InvalidOperationException("The main planet requires an energy-coupled Earth-origin epoch.");
        }

        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0.0 ||
            !double.IsFinite(surfaceGravityMetersPerSecondSquared) || surfaceGravityMetersPerSecondSquared <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), "Planet geometry and gravity must be finite and positive.");
        }

        var water = epoch.Water;
        var steam = water.GetMassKilograms(PlanetWaterReservoir.AtmosphericVapor);
        var liquid = water.GetMassKilograms(PlanetWaterReservoir.SurfaceLiquid);
        var ice = water.GetMassKilograms(PlanetWaterReservoir.SurfaceIce);
        var dryGas = epoch.CoupledEnergy.HeatCapacities.NoncondensableAtmosphereMassKilograms;
        var exposedWater = steam + liquid + ice;
        var atmosphereMass = dryGas + steam;
        var area = 4.0 * Math.PI * radiusMeters * radiusMeters;
        var pressure = surfaceGravityMetersPerSecondSquared * atmosphereMass / area;
        if (!double.IsFinite(exposedWater) || !double.IsFinite(atmosphereMass) || !double.IsFinite(area) ||
            !double.IsFinite(pressure) || area <= 0.0)
        {
            throw new InvalidOperationException("The canonical surface projection exceeds the finite physical range.");
        }

        var temperatures = epoch.Temperatures!.Value;
        return new PlanetOriginSurfaceSnapshot(
            epoch.Revision, epoch.Clock.YearsBeforePresent, epoch.Rock.TemperatureKelvin, epoch.Rock.MeltFraction,
            temperatures.SurfaceRockTemperatureKelvin, temperatures.AtmosphereTemperatureKelvin,
            steam, liquid, ice, dryGas, pressure,
            exposedWater == 0.0 ? 0.0 : liquid / exposedWater,
            exposedWater == 0.0 ? 0.0 : steam / exposedWater,
            exposedWater == 0.0 ? 0.0 : ice / exposedWater);
    }
}
