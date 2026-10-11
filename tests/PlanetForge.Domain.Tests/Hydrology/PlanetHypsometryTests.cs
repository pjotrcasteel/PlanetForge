using PlanetForge.Domain.Hydrology;

namespace PlanetForge.Domain.Tests.Hydrology;

[TestClass]
public sealed class PlanetHypsometryTests
{
    [TestMethod]
    public void SolveSeaLevelMeters_KnownWeightedBasins_ConservesWaterVolume()
    {
        var model = CreateWeightedTerrain();

        Assert.AreEqual(2.0, model.SolveSeaLevelMeters(10.0), 1e-9);
        Assert.AreEqual(10.0, model.GetSubmergedVolumeCubicMeters(2.0), 1e-9);
        Assert.AreEqual(24.0, model.GetSubmergedVolumeCubicMeters(5.0), 1e-9);
        Assert.AreEqual(4.0 / 6.0, model.GetFloodedAreaFraction(2.0), 1e-9);
        Assert.AreEqual(6.0, model.TotalAreaSquareMeters, 1e-9);
    }

    [TestMethod]
    public void SolveSeaLevelMeters_IncreasingLiquidVolume_NeverLowersSeaLevelOrFloodedArea()
    {
        var model = CreateWeightedTerrain();
        var volumes = new[] { 0.0, 0.01, 1.0, 5.0, 10.0, 20.0, 50.0 };
        var levels = volumes.Select(model.SolveSeaLevelMeters).ToArray();

        for (var index = 1; index < levels.Length; index++)
        {
            Assert.IsGreaterThan(levels[index - 1], levels[index]);
            Assert.IsGreaterThanOrEqualTo(model.GetFloodedAreaFraction(levels[index - 1]), model.GetFloodedAreaFraction(levels[index]));
            Assert.AreEqual(volumes[index], model.GetSubmergedVolumeCubicMeters(levels[index]), 1e-8);
        }
    }

    [TestMethod]
    public void SolveSeaLevelMeters_ZeroLiquidVolume_LeavesAllSamplesDry()
    {
        var model = CreateWeightedTerrain();

        var seaLevel = model.SolveSeaLevelMeters(0.0);

        Assert.IsLessThan(model.MinimumElevationMeters, seaLevel);
        Assert.AreEqual(0.0, model.GetSubmergedVolumeCubicMeters(seaLevel), 0.0);
        Assert.AreEqual(0.0, model.GetFloodedAreaFraction(seaLevel), 0.0);
    }

    [TestMethod]
    public void SolveSeaLevelMeters_InputOrdering_DoesNotChangePhysicalResult()
    {
        var ordered = CreateWeightedTerrain();
        var shuffled = new PlanetHypsometry(
        [
            new(4.0, 2.0),
            new(-2.0, 1.0),
            new(0.0, 3.0),
        ]);

        foreach (var volume in new[] { 0.1, 1.0, 10.0, 100.0 })
        {
            Assert.AreEqual(ordered.SolveSeaLevelMeters(volume), shuffled.SolveSeaLevelMeters(volume), 1e-10);
        }
    }

    [TestMethod]
    public void SolveSeaLevelMeters_EarthScaleOceanVolume_RetainsVolumePrecision()
    {
        var area = 4.0 * Math.PI * 6_371_000.0 * 6_371_000.0;
        var model = new PlanetHypsometry(
        [
            new(-4_000.0, area * 0.70),
            new(800.0, area * 0.30)
        ]);
        const double oneEarthHydrosphereCubicMeters = 1.386e18;

        var seaLevel = model.SolveSeaLevelMeters(oneEarthHydrosphereCubicMeters);

        Assert.IsTrue(double.IsFinite(seaLevel));
        Assert.IsGreaterThan(-4_000.0, seaLevel);
        Assert.AreEqual(oneEarthHydrosphereCubicMeters, model.GetSubmergedVolumeCubicMeters(seaLevel), oneEarthHydrosphereCubicMeters * 1e-12);
    }

    [TestMethod]
    public void Constructor_InvalidAreaOrElevation_RejectsWorld()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PlanetHypsometry([]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetHypsometry([new(0.0, 0.0)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetHypsometry([new(double.NaN, 1.0)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PlanetHypsometry([new(0.0, double.PositiveInfinity)]));
    }

    [TestMethod]
    public void SolveSeaLevelMeters_InvalidWaterVolume_RejectsCalculation()
    {
        var model = CreateWeightedTerrain();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.SolveSeaLevelMeters(-1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.SolveSeaLevelMeters(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.GetSubmergedVolumeCubicMeters(double.NegativeInfinity));
    }

    private static PlanetHypsometry CreateWeightedTerrain() =>
        new(
        [
            new(-2.0, 1.0),
            new(0.0, 3.0),
            new(4.0, 2.0)
        ]);
}
