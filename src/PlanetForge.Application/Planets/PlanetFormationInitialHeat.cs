using PlanetForge.Domain.Origins;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Planets;

/// <summary>
/// A declared, seed-specific initial heat distribution, not simulated impacts or convection.
/// All owned rock starts molten; bounded regional temperatures redistribute the existing
/// 2200 K mean shell enthalpy. The same ordinary column solver subsequently cools every region.
/// </summary>
public static class PlanetFormationInitialHeat
{
    public const double MinimumInitialKelvin = 1950.0;
    public const double MaximumInitialKelvin = 3400.0;

    public static PlanetOriginCanonicalRockCells Create(double radiusMeters, int seed)
    {
        var uniform = PlanetMagmaOceanReference.CreateInitial(radiusMeters, 0.0, 4);
        var field = uniform.Field;
        var lobes = CreateLobes(seed);
        var weights = field.Cells.Select(cell =>
        {
            var direction = PlanetSurfaceGridGeometry.GetCenterDirection(cell.Id);
            var broad = lobes.Sum(lobe => lobe.Amplitude * Math.Exp((PlanetVector.Dot(direction, lobe.Direction) - 1.0) / lobe.WidthSquared));
            // A declared, spatially resolved initial thermal spectrum, not shader noise.
            // Longer modes localize the heat; shorter modes break rotationally symmetric
            // patches. These temperatures own real enthalpy and subsequently cool normally.
            var spectrum = lobes.Take(8).Sum(lobe => lobe.Amplitude *
                Math.Sin((12.0 + 100.0 * lobe.WidthSquared) * PlanetVector.Dot(direction, lobe.Direction) + lobe.Amplitude * 8.0)) /
                lobes.Take(8).Sum(lobe => lobe.Amplitude);
            return (0.025 + broad) * Math.Exp(5.0 * spectrum);
        }).ToArray();
        var meanExcess = PlanetMagmaOceanReference.InitialSurfaceTemperatureKelvin - MinimumInitialKelvin;
        var maximumExcess = MaximumInitialKelvin - MinimumInitialKelvin;
        double WeightedExcess(double scale) => field.Cells.Select((cell, index) =>
            cell.ActiveRockMassKilograms * Math.Min(maximumExcess, scale * weights[index])).Sum() / field.TotalActiveRockMassKilograms;
        var lower = 0.0;
        var upper = 1.0;
        while (WeightedExcess(upper) < meanExcess) upper *= 2.0;
        for (var iteration = 0; iteration < 64; iteration++)
        {
            var middle = (lower + upper) * 0.5;
            if (WeightedExcess(middle) < meanExcess) lower = middle;
            else upper = middle;
        }
        var scale = (lower + upper) * 0.5;
        var redistributed = field.TotalActiveRockMassKilograms * field.Material.SpecificHeatCapacityJoulesPerKilogramKelvin * meanExcess;
        var regional = field.RedistributeInitialEnergy(cell => cell.ActiveRockMassKilograms *
            Math.Min(maximumExcess, scale * weights[field.Layout.GetIndex(cell.Id)]), redistributed);
        var temperatures = regional.Cells.Select(cell => cell.Resolve(regional.Material)).ToArray();
        if (temperatures.Any(state => state.MeltFraction != 1.0 || state.TemperatureKelvin < MinimumInitialKelvin - 1e-6 ||
            state.TemperatureKelvin > MaximumInitialKelvin + 1e-6))
            throw new InvalidOperationException("Initial heat redistribution must preserve the declared molten temperature bounds.");
        return new(uniform.Epoch, regional);
    }

    private static (PlanetVector Direction, double WidthSquared, double Amplitude)[] CreateLobes(int seed)
    {
        var lobes = new (PlanetVector, double, double)[14];
        var state = unchecked((uint)seed) ^ 0xA511E9B3u;
        double Next()
        {
            state = unchecked(state + 0x9E3779B9u);
            var value = state;
            value = unchecked((value ^ (value >> 16)) * 0x21F0AAADu);
            value = unchecked((value ^ (value >> 15)) * 0x735A2D97u);
            return (value ^ (value >> 15)) / 4294967296.0;
        }
        for (var index = 0; index < lobes.Length; index++)
        {
            var y = Next() * 2.0 - 1.0;
            var angle = Next() * 2.0 * Math.PI;
            var horizontal = Math.Sqrt(1.0 - y * y);
            var width = 0.10 + Next() * 0.22;
            lobes[index] = (new(horizontal * Math.Cos(angle), y, horizontal * Math.Sin(angle)), width * width, 0.4 + Next() * 0.6);
        }
        return lobes;
    }
}
