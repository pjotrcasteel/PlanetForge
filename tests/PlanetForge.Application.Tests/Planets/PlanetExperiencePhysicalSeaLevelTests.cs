using PlanetForge.Application.Planets;
using PlanetForge.Application.Rendering;
using PlanetForge.Application.Surface;
using PlanetForge.Application.Surface.Hydrology;
using PlanetForge.Domain.Surface;

namespace PlanetForge.Application.Tests.Planets;

[TestClass]
public sealed class PlanetExperiencePhysicalSeaLevelTests
{
    private const double EarthRadiusMeters = 6_371_000.0;

    [TestMethod]
    public void CreateSnapshot_WithLiquidWater_FillsTheActualTerrainVolume()
    {
        var source = new CountingFlatElevationSource();
        var experience = CreateExperience(source);

        var snapshot = experience.CreateSnapshot();
        Assert.IsGreaterThan(0.0, snapshot.Water.LiquidMassKilograms);

        var expectedWaterLevel = snapshot.Water.LiquidMassKilograms /
            (1_000.0 * 4.0 * Math.PI * snapshot.PhysicalParameters.RadiusMeters * snapshot.PhysicalParameters.RadiusMeters);
        Assert.AreEqual(expectedWaterLevel, snapshot.SeaLevelMeters, 0.02,
            "At uniform bedrock elevation, conserved liquid water volume sets global sea level exactly.");
    }

    [TestMethod]
    public void CreateSnapshot_UnchangedTerrain_ReusesCachedSeaLevelProfile()
    {
        var source = new CountingFlatElevationSource();
        var experience = CreateExperience(source);

        var first = experience.CreateSnapshot();
        var samples = source.SampleCount;
        var next = experience.CreateSnapshot();

        Assert.AreEqual(first.SeaLevelMeters, next.SeaLevelMeters, 0.00001);
        Assert.AreEqual(samples, source.SampleCount, "A repeated climate/render snapshot must not resample the whole globe.");
    }

    [TestMethod]
    public void DecreaseWater_Repeatedly_DrainsOceanWithoutRegeneratingBedrock()
    {
        var source = new CountingFlatElevationSource();
        var experience = CreateExperience(source);
        var before = experience.CreateSnapshot();
        var samples = source.SampleCount;

        var partial = experience.DecreaseWater();
        var another = experience.DecreaseWater();
        var nearEmpty = experience.DecreaseWater();
        var dry = experience.DecreaseWater();

        Assert.IsLessThan(before.SeaLevelMeters, partial.SeaLevelMeters);
        Assert.IsLessThan(partial.SeaLevelMeters, another.SeaLevelMeters);
        Assert.IsLessThan(another.SeaLevelMeters, nearEmpty.SeaLevelMeters);
        Assert.AreEqual(0.0, dry.Water.LiquidMassKilograms, 0.0);
        Assert.AreEqual(-11_000.0, dry.SeaLevelMeters, 0.0);
        Assert.AreEqual(samples, source.SampleCount,
            "Changing water volume and reaching a dry world should reuse the rock distribution.");
    }

    private static PlanetExperience CreateExperience(CountingFlatElevationSource source)
    {
        var surfaceSampler = new PlanetSurfaceTileSampler(source);
        var meshBuilder = new PlanetSurfaceMeshBuilder(surfaceSampler, source);
        var cache = new PlanetSurfaceMeshCache(meshBuilder);
        var localSampler = new PlanetLocalSurfacePatchSampler(source);
        var localMeshBuilder = new PlanetLocalSurfaceMeshBuilder();
        var hypsometry = new PlanetHypsometryCache(new PlanetHypsometrySampler(source), source);
        return new PlanetExperience(cache, localSampler, localMeshBuilder, hypsometryCache: hypsometry);
    }

    private sealed class CountingFlatElevationSource : IPlanetElevationSource
    {
        public int SampleCount { get; private set; }

        public double SampleElevationMeters(PlanetVector direction, int seed)
        {
            SampleCount++;
            return 0.0;
        }
    }
}
