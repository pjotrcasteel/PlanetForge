using PlanetForge.Domain.Atmosphere;
using PlanetForge.Domain.Climate;
using PlanetForge.Domain.Hydrology;
using PlanetForge.Domain.Physics;

namespace PlanetForge.Domain.Tests.Climate;

[TestClass]
public sealed class ClimateFeedbackSimulatorTests
{
    [TestMethod]
    public void Advance_ColdForcing_GrowsCryosphereAndRaisesEffectiveAlbedo()
    {
        var planet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 1.35 };
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);
        var water = EarthWaterReference.CreateParameters();
        var state = ClimateFeedbackSimulator.Initialize(EarthReference.CreateParameters(), atmosphere, water);

        var advanced = ClimateFeedbackSimulator.Advance(planet, atmosphere, water, state, 100.0);

        Assert.IsTrue(advanced.CryosphereFraction > state.CryosphereFraction);
        Assert.IsTrue(advanced.EffectiveBondAlbedo > planet.BondAlbedo);
        Assert.IsTrue(advanced.SurfaceTemperatureKelvin < state.SurfaceTemperatureKelvin);
    }

    [TestMethod]
    public void Advance_WarmForcing_MeltsExistingCryosphereAndLowersEffectiveAlbedo()
    {
        var coldPlanet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 1.35 };
        var coldPhysics = PlanetPhysicsCalculator.Calculate(coldPlanet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), coldPlanet, coldPhysics);
        var water = EarthWaterReference.CreateParameters();
        var initial = ClimateFeedbackSimulator.Initialize(EarthReference.CreateParameters(), atmosphere, water);
        var frozen = ClimateFeedbackSimulator.Advance(coldPlanet, atmosphere, water, initial, 100.0);
        var warmPlanet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 0.75 };

        var warmed = ClimateFeedbackSimulator.Advance(warmPlanet, atmosphere, water, frozen, 100.0);

        Assert.IsTrue(warmed.CryosphereFraction < frozen.CryosphereFraction);
        Assert.IsTrue(warmed.EffectiveBondAlbedo < frozen.EffectiveBondAlbedo);
        Assert.IsTrue(warmed.SurfaceTemperatureKelvin > frozen.SurfaceTemperatureKelvin);
    }

    [TestMethod]
    public void Advance_DryPlanet_DoesNotCreateCryosphereFeedback()
    {
        var planet = EarthReference.CreateParameters() with { OrbitalDistanceMeters = PhysicalConstants.AstronomicalUnitMeters * 1.5 };
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);
        var water = new WaterParameters(0.0);
        var initial = ClimateFeedbackSimulator.Initialize(planet, atmosphere, water);

        var advanced = ClimateFeedbackSimulator.Advance(planet, atmosphere, water, initial, 100.0);

        Assert.AreEqual(0.0, advanced.CryosphereFraction, 0.000001);
        Assert.AreEqual(planet.BondAlbedo, advanced.EffectiveBondAlbedo, 0.000001);
    }

    [TestMethod]
    public void Advance_MoreThanFiveHundredYears_Throws()
    {
        var planet = EarthReference.CreateParameters();
        var physics = PlanetPhysicsCalculator.Calculate(planet);
        var atmosphere = AtmosphereCalculator.Calculate(EarthAtmosphereReference.CreateParameters(), planet, physics);
        var water = EarthWaterReference.CreateParameters();
        var state = ClimateFeedbackSimulator.Initialize(planet, atmosphere, water);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ClimateFeedbackSimulator.Advance(planet, atmosphere, water, state, 501.0));
    }
}