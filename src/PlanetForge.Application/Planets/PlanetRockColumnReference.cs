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
    public static PlanetOriginCanonicalRockCells CreateInitial(double radiusMeters)
    {
        var funded = PlanetMagmaOceanReference.CreateInitial(radiusMeters);
        var columns = PlanetOriginRockColumnField.Partition(funded.Field, new(8, 3_000.0, 2_800.0, 3.0));
        return new(funded.Epoch, columns.Aggregate, columns);
    }

    public static PlanetOriginCanonicalRockCells Advance(PlanetOriginCanonicalRockCells current,
        PlanetRenderSnapshot world, CancellationToken cancellationToken = default) =>
        PlanetOriginRockColumnIntegrator.Advance(current, PlanetOriginDuration.FromSeconds(AdvanceSeconds),
            new(PlanetVector.UnitX, world.Physics.SolarFluxWattsPerSquareMeter, 0.15, 0.9, 3.0, 25.0),
            new(30.0, 10_000), cancellationToken);
}
