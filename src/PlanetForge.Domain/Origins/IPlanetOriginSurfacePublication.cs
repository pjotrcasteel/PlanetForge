namespace PlanetForge.Domain.Origins;

/// <summary>
/// Serializes canonical epoch/terrain publication and consistent snapshot reads.
/// The callback must complete synchronously and restore its own state on failure;
/// the gateway restores the previous elevation and revision if it throws.
/// </summary>
public interface IPlanetOriginSurfacePublication
{
    T PublishOriginSurface<T>(PlanetOriginRockColumnField? columns, Func<T> publishWorld);
    T ReadConsistent<T>(Func<T> readWorld);
}
