using PlanetForge.Application.Rendering;
using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

/// <summary>
/// Dry/airless thin-column formation reference, with declared constant-pressure
/// material. It funds the existing molten shell, not a new primordial continent map.
/// No mechanical crust, convection, plate motion or calibrated Earth age is implied.
/// </summary>
public static class PlanetRockColumnReference
{
    public const int AdvanceSeconds = 3_600;
    public static PlanetOriginCanonicalRockCells CreateInitial(double radiusMeters) => CreateInitial(radiusMeters, 8);

    /// <summary>
    /// Research-only depth resolution control. The shipped reference stays at eight layers;
    /// comparisons must share radius, initial energy, boundary and elapsed time.
    /// </summary>
    public static PlanetOriginCanonicalRockCells CreateInitial(double radiusMeters, int layerCount)
    {
        var funded = PlanetMagmaOceanReference.CreateInitial(radiusMeters);
        var columns = PlanetOriginRockColumnField.Partition(funded.Field, new(layerCount, 3_000.0, 2_800.0, 3.0));
        return new(funded.Epoch, columns.Aggregate, columns);
    }

    /// <summary>Opt-in surface-focused numerical reference; not the released eight-layer reference or calibrated Earth cooling.</summary>
    public static PlanetOriginCanonicalRockCells CreateResolvedInitial(double radiusMeters, int layerCount = 64, double solarIrradianceWattsPerSquareMeter = 1361.0, double contrastFraction = 0.08)
    {
        var funded = PlanetMagmaOceanReference.CreateInitial(radiusMeters, contrastFraction);
        var columns = PlanetOriginRockColumnField.Partition(funded.Field, new(layerCount, 3000.0, 2800.0, 3.0)
        {
            DepthGradingExponent = 2.0,
            UseRadiatingInterface = true,
            UseImplicitDepthTransport = true
        });
        var boundary = new PlanetOriginRegionalAirlessParameters(PlanetVector.UnitX, solarIrradianceWattsPerSquareMeter, 0.15, 0.9, 3.0, 25.0);
        boundary.Validate();
        return new(funded.Epoch, columns.Aggregate, columns) { SurfaceBoundary = boundary };
    }

    public static PlanetOriginCanonicalRockCells Advance(PlanetOriginCanonicalRockCells current,
        PlanetRenderSnapshot world, CancellationToken cancellationToken = default) =>
        PlanetOriginRockColumnIntegrator.Advance(current, PlanetOriginDuration.FromSeconds(AdvanceSeconds),
            new(PlanetVector.UnitX, world.Physics.SolarFluxWattsPerSquareMeter, 0.15, 0.9, 3.0, 25.0),
            new(30.0, 10_000), cancellationToken);
}
