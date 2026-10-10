namespace PlanetForge.Domain.Surface;

/// <summary>
/// A planet-wide elevation source whose published geological field can change
/// without changing the seed. Consumers must invalidate cached geometry when
/// Revision changes; it is not a persistent gameplay save or generation version.
/// </summary>
public interface IPlanetElevationRevisionSource : IPlanetElevationSource
{
    long Revision { get; }
}
