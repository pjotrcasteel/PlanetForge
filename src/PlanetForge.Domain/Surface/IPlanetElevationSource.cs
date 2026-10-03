namespace PlanetForge.Domain.Surface;

public interface IPlanetElevationSource
{
    double SampleElevationMeters(PlanetVector direction, int seed);
}
