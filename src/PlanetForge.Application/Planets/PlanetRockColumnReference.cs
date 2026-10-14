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
        return CreateResolvedInitial(funded, layerCount, solarIrradianceWattsPerSquareMeter);
    }

    public static PlanetOriginCanonicalRockCells CreateResolvedInitial(PlanetOriginCanonicalRockCells funded, int layerCount = 64,
        double solarIrradianceWattsPerSquareMeter = 1361.0, double depthGradingExponent = 2.0)
    {
        ArgumentNullException.ThrowIfNull(funded);
        if (funded.Columns is not null) throw new ArgumentException("Initial columns must partition an unpartitioned funded shell.", nameof(funded));
        var columns = PlanetOriginRockColumnField.Partition(funded.Field, new(layerCount, 3000.0, 2800.0, 3.0)
        {
            DepthGradingExponent = depthGradingExponent,
            UseRadiatingInterface = true,
            UseImplicitDepthTransport = true
        });
        var boundary = new PlanetOriginRegionalAirlessParameters(PlanetVector.UnitX, solarIrradianceWattsPerSquareMeter, 0.15, 0.9, 3.0, 25.0);
        boundary.Validate();
        return new(funded.Epoch, columns.Aggregate, columns) { SurfaceBoundary = boundary };
    }

    /// <summary>Coupled liquid heat transport on an explicitly funded upper shell; not a resolved magma velocity field.</summary>
    public static PlanetOriginCanonicalRockCells CreateCoupledUpperMagmaInitial(PlanetOriginCanonicalRockCells funded,
        int layerCount = 64, double solarIrradianceWattsPerSquareMeter = 1361.0)
    {
        var resolved = CreateResolvedInitial(funded, layerCount, solarIrradianceWattsPerSquareMeter, 4.0);
        return new(resolved.Epoch, resolved.Field, resolved.Columns)
        {
            SurfaceBoundary = resolved.SurfaceBoundary!.Value with { LiquidHeatTransport = PlanetOriginLiquidHeatTransportParameters.Reference }
        };
    }

    /// <summary>
    /// Research-only funded upper magma reference. Quartic depth grading resolves
    /// the sub-millimetre initial radiating boundary without making the entire
    /// kilometre-depth inventory one surface control volume. No convection is implied.
    /// </summary>
    public static PlanetOriginCanonicalRockCells CreateUpperMagmaInitial(double radiusMeters,
        double liquidDepthMeters = 1_000.0, int layerCount = 64, int gridLevel = 3)
    {
        var funded = PlanetMagmaOceanReference.CreateUpperMagmaInitial(radiusMeters, liquidDepthMeters, 0.0, gridLevel);
        var columns = PlanetOriginRockColumnField.Partition(funded.Field, new(layerCount, 3000.0, 2800.0, 3.0)
        {
            DepthGradingExponent = 4.0,
            UseRadiatingInterface = true,
            UseImplicitDepthTransport = true
        });
        var boundary = new PlanetOriginRegionalAirlessParameters(PlanetVector.UnitX, 1361.0, 0.15, 0.9, 3.0, 25.0);
        return new(funded.Epoch, columns.Aggregate, columns) { SurfaceBoundary = boundary };
    }

    public static PlanetOriginCanonicalRockCells Advance(PlanetOriginCanonicalRockCells current,
        PlanetRenderSnapshot world, CancellationToken cancellationToken = default) =>
        PlanetOriginRockColumnIntegrator.Advance(current, PlanetOriginDuration.FromSeconds(AdvanceSeconds),
            new(PlanetVector.UnitX, world.Physics.SolarFluxWattsPerSquareMeter, 0.15, 0.9, 3.0, 25.0),
            new(30.0, 10_000), cancellationToken);
}
