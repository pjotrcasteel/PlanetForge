using PlanetForge.Domain.Origins;

namespace PlanetForge.Domain.Tests.Origins;

[TestClass]
public sealed class PlanetRockEnthalpyModelTests
{
    private static PlanetRockEnthalpyModel CreateModel() => new(
        new PlanetRockThermalProperties(2.0, 10.0, 100.0, 100.0, 200.0));

    [TestMethod]
    public void Resolve_RockBelowSolidus_IsFullySolid()
    {
        var state = CreateModel().Resolve(1_000.0);

        Assert.AreEqual(50.0, state.TemperatureKelvin, 1e-10);
        Assert.AreEqual(0.0, state.MeltFraction, 0.0);
        Assert.AreEqual(1_000.0, state.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void Resolve_RockInMushyInterval_StoresLatentHeatAndPartialMelting()
    {
        var model = CreateModel();
        var state = model.Resolve(3_100.0);

        Assert.AreEqual(150.0, state.TemperatureKelvin, 1e-10);
        Assert.AreEqual(0.5, state.MeltFraction, 1e-10);
        Assert.AreEqual(3_100.0, model.CalculateStoredEnergyJoules(150.0), 1e-10);
    }

    [TestMethod]
    public void Resolve_AboveLiquidus_IsEntirelyMolten()
    {
        var state = CreateModel().Resolve(4_400.0);

        Assert.AreEqual(210.0, state.TemperatureKelvin, 1e-10);
        Assert.AreEqual(1.0, state.MeltFraction, 0.0);
    }

    [TestMethod]
    public void Resolve_CoolingAcrossPhaseBoundaries_ConsumesLatentHeatAndSolidifiesContinuously()
    {
        var model = CreateModel();
        var high = new PlanetThermalEnergyBudget(4_400.0);
        var oneSecond = 1.0 / PlanetThermalEnergyBudget.JulianYearSeconds;
        var halfMolten = high.Advance(oneSecond, new PlanetThermalEnergyFlux(0.0, 0.0, 1_300.0));
        var rock = halfMolten.Advance(oneSecond, new PlanetThermalEnergyFlux(0.0, 0.0, 1_100.0));

        Assert.AreEqual(1.0, model.Resolve(high).MeltFraction, 0.0);
        Assert.AreEqual(0.5, model.Resolve(halfMolten).MeltFraction, 1e-10);
        Assert.AreEqual(0.0, model.Resolve(rock).MeltFraction, 0.0);
        Assert.AreEqual(100.0, model.Resolve(rock).TemperatureKelvin, 1e-10);
        Assert.AreEqual(0.0, rock.ConservationResidualJoules, 1e-9);
    }

    [TestMethod]
    public void CalculateStoredEnergyJoules_AcrossSolidusAndLiquidus_RoundTripsMonotonically()
    {
        var model = CreateModel();
        var previous = -1.0;
        foreach (var temperature in new[] { 0.0, 25.0, 99.99, 100.0, 120.0, 150.0, 199.99, 200.0, 210.0, 500.0 })
        {
            var joules = model.CalculateStoredEnergyJoules(temperature);
            var recovered = model.Resolve(joules);
            Assert.IsGreaterThan(previous, joules);
            Assert.AreEqual(temperature, recovered.TemperatureKelvin, 1e-9);
            previous = joules;
        }
    }

    [TestMethod]
    public void Resolve_EarthMantleScaleNumbers_RemainFiniteAndConserveEnergy()
    {
        var properties = new PlanetRockThermalProperties(4.0e24, 1_200.0, 4.0e5, 1_400.0, 2_000.0);
        var model = new PlanetRockEnthalpyModel(properties);
        var expectedTemperature = 1_800.0;
        var joules = model.CalculateStoredEnergyJoules(expectedTemperature);

        var resolved = model.Resolve(new PlanetThermalEnergyBudget(joules));

        Assert.IsTrue(double.IsFinite(joules));
        Assert.AreEqual(expectedTemperature, resolved.TemperatureKelvin, 1e-9);
        Assert.AreEqual(2.0 / 3.0, resolved.MeltFraction, 1e-10);
        Assert.AreEqual(joules, resolved.StoredEnergyJoules, 0.0);
    }

    [TestMethod]
    public void InvalidMaterialAndEnergy_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(0.0, 10.0, 100.0, 100.0, 200.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(1.0, 10.0, 100.0, 200.0, 100.0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new PlanetRockEnthalpyModel(new PlanetRockThermalProperties(1.0, 10.0, double.NaN, 100.0, 200.0)));
        var model = CreateModel();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.Resolve(-1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.Resolve(double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => model.CalculateStoredEnergyJoules(double.NaN));
    }
}
