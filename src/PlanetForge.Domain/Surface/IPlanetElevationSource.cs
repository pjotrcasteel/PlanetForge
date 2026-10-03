namespace PlanetForge.Domain.Surface;

public interface IPlanetElevationSource
{
    double Sample(PlanetVector direction, int seed);
}
