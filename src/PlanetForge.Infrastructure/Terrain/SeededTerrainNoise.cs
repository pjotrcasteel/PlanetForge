using PlanetForge.Application.Terrain;

namespace PlanetForge.Infrastructure.Terrain;

public sealed class SeededTerrainNoise : IPlanetTerrainNoise
{
    public double Sample(double x, double y, double z, int seed)
    {
        var continental = Wave(x * 1.7, y * 1.4, z * 1.9, seed);
        var regional = Wave(x * 4.3, y * 4.7, z * 4.1, seed ^ 0x51ED270B);
        var detail = Wave(x * 10.1, y * 9.7, z * 10.7, seed ^ 0x2C1B3C6D);
        return Math.Clamp((continental * 0.62) + (regional * 0.28) + (detail * 0.10), -1.0, 1.0);
    }

    private static double Wave(double x, double y, double z, int seed)
    {
        var seedPhase = seed * 0.0000137;
        return (Math.Sin((x + seedPhase) * 3.1) + Math.Sin((y - seedPhase) * 4.7) + Math.Cos((z + seedPhase) * 5.3)) / 3.0;
    }
}
