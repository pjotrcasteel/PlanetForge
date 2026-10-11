using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Infrastructure.Surface;

namespace PlanetForge.Infrastructure.Tests.Surface;

[TestClass]
public sealed class PlanetOceanBasinShowcaseQualityTests
{
    [TestMethod]
    public void ShowcaseSeed_EarthHydrosphere_ProducesAConnectedPhysicalOceanBasin()
    {
        const int seed = 24_061_984;
        const double radiusMeters = 6_371_000.0;
        const double liquidWaterDensityKilogramsPerCubicMeter = 1_000.0;

        var elevation = new ProceduralPlanetElevationSource();
        var terrainVolume = new PlanetHypsometrySampler(elevation)
            .Build(3, seed, radiusMeters, CancellationToken.None);
        var seaLevel = terrainVolume.SolveSeaLevelMeters(
            EarthWaterReference.TotalHydrosphereMassKilograms / liquidWaterDensityKilogramsPerCubicMeter);
        var hydrology = new PlanetHydrologyModelBuilder(elevation)
            .Build(5, seed, radiusMeters, seaLevel, CancellationToken.None);
        var connectivity = PlanetOceanConnectivitySurvey.Measure(hydrology, CancellationToken.None);

        TestContext.WriteLine($"Showcase seed {seed}: physical sea level {seaLevel:0.0}m; " +
            $"ocean-cell fraction {connectivity.OceanCellFraction:P2}; " +
            $"components {connectivity.OceanComponents}; " +
            $"largest basin share {connectivity.LargestConnectedOceanShare:P2}.");

        Assert.IsTrue(connectivity.OceanCells > 0 && connectivity.LandCells > 0,
            "The Earth-reference inventory should not produce either an entirely dry or entirely flooded showcase.");
        Assert.IsTrue(connectivity.LargestConnectedOceanShare > 0.20,
            $"Showcase ocean is physically fragmented, before the renderer: {connectivity.OceanComponents} disconnected " +
            $"regions; largest contains {connectivity.LargestConnectedOceanShare:P2} of submerged grid cells. " +
            "Investigate real geological basin structure instead of painting ocean pixels.");
    }

    public TestContext TestContext { get; set; } = null!;
}
