namespace PlanetForge.Application.Terrain;

public interface IPlanetTerrainNoise
{
    double Sample(double x, double y, double z, int seed);
}
